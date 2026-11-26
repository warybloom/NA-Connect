using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Costs;

/// <summary>Estado da resolução do custo/hora de um lançamento. Só <see cref="Vigente"/> produz custo.</summary>
public static class ProfessionalHourlyCostStatus
{
    public const string Vigente = "VIGENTE_NA_DATA";
    public const string SemNome = "SEM_NOME";
    public const string SemCadastro = "SEM_CADASTRO";
    public const string Ambiguo = "PROFISSIONAL_AMBIGUO";
    public const string SemData = "SEM_DATA";
    public const string SemVigencia = "SEM_VIGENCIA";
    public const string VigenciaSobreposta = "VIGENCIA_SOBREPOSTA";
    public const string CustoInvalido = "CUSTO_INVALIDO";
}

public sealed record ProfessionalHourlyCostEntry(int Id, string? AliasTimesheet, bool Active, DateTime? InactiveAt,
    IReadOnlyList<(string Text, string Type)> Aliases);

public sealed record ProfessionalHourlyCostPeriod(ProfessionalCostPeriodRule Rule, IReadOnlyList<ProfessionalCostComponentRule> Items);

/// <summary>Rate=null sempre que Status != VIGENTE_NA_DATA — nunca zero silencioso.</summary>
public sealed record ProfessionalHourlyCostResolution(decimal? Rate, int? ProfessionalId, string Status)
{
    public decimal? CostFor(decimal hours) => Rate.HasValue ? decimal.Round(hours * Rate.Value, 6) : null;
}

/// <summary>
/// Regra ÚNICA de custo/hora de um lançamento de Timesheet: cadastro de profissionais do projeto
/// (costs.ProjectProfessional + AliasTimesheet/TimesheetMatchAlias) e o período vigente na data do lançamento
/// (costs.ProjectProfessionalCostPeriod). CustoHoraManual tem prioridade; sem ele, composição mensal ÷ HH base
/// (<see cref="ProjectCostHistoryRules.HourlyRate"/>). Usada pelo custo por documento, pela tela de HH sem documento (AT)
/// e pelo Resumo LAB — o mesmo lançamento gera o mesmo custo em todas as telas.
/// </summary>
public sealed class ProfessionalHourlyCostCatalog(
    IReadOnlyList<ProfessionalHourlyCostEntry> professionals,
    IReadOnlyDictionary<int, IReadOnlyList<ProfessionalHourlyCostPeriod>> periods)
{
    public ProfessionalHourlyCostResolution Resolve(string? name, DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(name)) return new(null, null, ProfessionalHourlyCostStatus.SemNome);

        var matches = professionals
            .Where(p => (p.Active || (p.InactiveAt is not null && date.HasValue && date.Value <= DateOnly.FromDateTime(p.InactiveAt.Value)))
                        && Matches(p, name))
            .Select(p => p.Id)
            .Distinct()
            .ToArray();
        if (matches.Length == 0) return new(null, null, ProfessionalHourlyCostStatus.SemCadastro);
        if (matches.Length > 1) return new(null, null, ProfessionalHourlyCostStatus.Ambiguo);

        var id = matches[0];
        if (!date.HasValue) return new(null, id, ProfessionalHourlyCostStatus.SemData);

        var valid = periods.TryGetValue(id, out var list)
            ? list.Where(x => ProjectCostHistoryRules.Covers(x.Rule, date.Value)).ToArray()
            : Array.Empty<ProfessionalHourlyCostPeriod>();
        if (valid.Length == 0) return new(null, id, ProfessionalHourlyCostStatus.SemVigencia);
        if (valid.Length > 1) return new(null, id, ProfessionalHourlyCostStatus.VigenciaSobreposta);

        try
        {
            return new(ProjectCostHistoryRules.HourlyRate(valid[0].Rule, valid[0].Items), id, ProfessionalHourlyCostStatus.Vigente);
        }
        catch (ArgumentException)
        {
            return new(null, id, ProfessionalHourlyCostStatus.CustoInvalido);
        }
    }

    private static bool Matches(ProfessionalHourlyCostEntry p, string name)
    {
        var candidates = p.Aliases.ToList();
        if (!string.IsNullOrWhiteSpace(p.AliasTimesheet)) candidates.Add((p.AliasTimesheet, "CONTEM"));
        return CostTimesheetMatchingRules.Matches(null, candidates, name);
    }

    public static async Task<ProfessionalHourlyCostCatalog> LoadAsync(SqlConnection connection, string projectId, CancellationToken ct)
    {
        var rows = new List<(int Id, string? Alias, bool Active, DateTime? InactiveAt)>();
        await using (var cmd = Command(connection, "SELECT Id,AliasTimesheet,Ativo,DesativadoEm FROM costs.ProjectProfessional WHERE Project_ID_ACC=@p", projectId))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetBoolean(2), r.IsDBNull(3) ? null : r.GetDateTime(3)));
        }

        var aliases = new Dictionary<int, List<(string Text, string Type)>>();
        const string aliasSql = "SELECT a.ProfessionalId,a.TextoMatch,a.TipoMatch FROM costs.TimesheetMatchAlias a JOIN costs.ProjectProfessional p ON p.Id=a.ProfessionalId WHERE p.Project_ID_ACC=@p AND a.Ativo=1";
        await using (var cmd = Command(connection, aliasSql, projectId))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (!aliases.TryGetValue(id, out var list)) aliases[id] = list = new();
                list.Add((r.GetString(1), r.GetString(2)));
            }
        }

        var grouped = new Dictionary<int, (int Owner, ProfessionalCostPeriodRule Rule, List<ProfessionalCostComponentRule> Items)>();
        const string periodSql = """
            SELECT h.Id,h.ProfessionalId,h.VigenciaInicio,h.VigenciaFim,h.HHBaseMensal,h.RemuneracaoBase,h.CustoHoraManual,h.Ativo,
                   i.TipoCalculo,i.Valor,i.Percentual,i.BaseCalculo,i.Ativo
            FROM costs.ProjectProfessionalCostPeriod h
            JOIN costs.ProjectProfessional p ON p.Id=h.ProfessionalId
            LEFT JOIN costs.ProfessionalCostItem i ON i.PeriodId=h.Id
            WHERE p.Project_ID_ACC=@p
            """;
        await using (var cmd = Command(connection, periodSql, projectId))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (!grouped.TryGetValue(id, out var item))
                    item = (r.GetInt32(1), new(id, DateOnly.FromDateTime(r.GetDateTime(2)), r.IsDBNull(3) ? null : DateOnly.FromDateTime(r.GetDateTime(3)),
                        Dec(r, 4), Dec(r, 5), r.IsDBNull(6) ? null : Dec(r, 6), r.GetBoolean(7)), new());
                if (!r.IsDBNull(8) && r.GetBoolean(12))
                    item.Items.Add(new(r.GetString(8), Dec(r, 9), r.IsDBNull(10) ? null : Dec(r, 10), r.GetString(11)));
                grouped[id] = item;
            }
        }

        var periods = grouped.Values
            .GroupBy(x => x.Owner)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProfessionalHourlyCostPeriod>)g.Select(x => new ProfessionalHourlyCostPeriod(x.Rule, x.Items)).ToList());
        var professionals = rows
            .Select(x => new ProfessionalHourlyCostEntry(x.Id, x.Alias, x.Active, x.InactiveAt,
                aliases.TryGetValue(x.Id, out var a) ? a : (IReadOnlyList<(string, string)>)Array.Empty<(string, string)>()))
            .ToList();
        return new(professionals, periods);
    }

    private static SqlCommand Command(SqlConnection connection, string sql, string projectId)
    {
        var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        return cmd;
    }

    private static decimal Dec(SqlDataReader r, int i) => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
}
