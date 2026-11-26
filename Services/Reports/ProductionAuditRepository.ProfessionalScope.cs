using System.Data;
using APS.WebApp.Services.Costs;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record EngineeringProfessionalRow(
    string Name, decimal Hours, decimal MissingCostHours, decimal KnownCost,
    int Entries, string CostStatus, bool Excluded);
public sealed record EngineeringProfessionalOverview(
    string ProjectCode, IReadOnlyList<EngineeringProfessionalRow> Items,
    decimal Hours, decimal MissingCostHours, decimal ExcludedHours, decimal ExcludedKnownCost);
public sealed record EngineeringProfessionalScopeInput(string Name, bool ExcludeEngineering);

public sealed partial class ProductionAuditRepository
{
    private sealed record EngineeringScopeSetting(string Name, bool Excluded);
    private sealed class EngineeringScopeAggregate(string name)
    {
        public string Name { get; } = name;
        public decimal Hours { get; set; }
        public decimal Missing { get; set; }
        public decimal Known { get; set; }
        public int Entries { get; set; }
        public string Status { get; set; } = "VIGENTE_NA_DATA";
    }

    // Configuração independente por projeto. A chave normalizada evita diferenças de caixa, acentos ou espaços.
    private async Task<Dictionary<string, EngineeringScopeSetting>> LoadEngineeringScopeAsync(string projectId, CancellationToken ct)
    {
        var result = new Dictionary<string, EngineeringScopeSetting>(StringComparer.Ordinal);
        await using var db = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT NomeNormalizado, NomeExibicao, ExcluirEngenharia
            FROM qualidade.ProfissionalEscopoEngenharia WHERE Project_ID_ACC=@p
            """, db);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(r.GetString(0), new(r.GetString(1), r.GetBoolean(2)));
        return result;
    }

    // Mesmo BI e mesma regra de roteamento de AFE/documento do comparativo; não inventa horas.
    private async Task<(ProjectIdentity Identity, List<RawBiTimesheetRow> Rows)> ReadEngineeringRowsAsync(
        string projectId, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        var routing = await ReadRoutingStateAsync(projectId, identity, ct);
        if (!routing.IsConfigured && !string.Equals(identity.ProjectCode, identity.BiProjectCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure a AFE/CR-OS para identificar as horas deste projeto.");
        var target = TargetDocumentRegex(identity.ProjectCode);
        var any = AnyDocumentRegex(identity.BiProjectCode);
        const string sql = """
            SELECT [data],[Nome],[SemanaDesk],[Entrada],[Saida],[Intervalo],
                   ISNULL([Trabalhadas],0),[AFE],[Descricao],[Projeto],[Funcao],[Disciplina]
            FROM dbo.tbTimeSheet
            WHERE UPPER(LTRIM(RTRIM([Projeto])))=@project AND ISNULL([Trabalhadas],0)>0;
            """;
        var rows = new List<RawBiTimesheetRow>();
        await using var bi = await OpenBiAsync(ct);
        await using var cmd = new SqlCommand(sql, bi) { CommandTimeout = 45 };
        cmd.Parameters.Add("@project", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var row = new RawBiTimesheetRow(Date(r, 0), Text(r, 1), Text(r, 2), Date(r, 3),
                Date(r, 4), Num(r, 5), Num(r, 6) ?? 0m, Text(r, 7), Text(r, 8),
                Text(r, 9) ?? identity.BiProjectCode, Text(r, 10), Text(r, 11));
            var description = row.Description ?? string.Empty;
            if (target.IsMatch(description) ||
                (!any.IsMatch(description) && MatchesRouting(row, routing)))
                rows.Add(row);
        }
        return (identity, rows);
    }

    public async Task<EngineeringProfessionalOverview> ReadEngineeringProfessionalScopeAsync(string projectId, CancellationToken ct)
    {
        var (identity, rows) = await ReadEngineeringRowsAsync(projectId, ct);
        var settings = await LoadEngineeringScopeAsync(projectId, ct);
        await using var local = await OpenAsync(ct);
        var catalog = await ProfessionalHourlyCostCatalog.LoadAsync(local, projectId, ct);
        var grouped = new Dictionary<string, EngineeringScopeAggregate>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = CostTimesheetMatchingRules.NormalizeName(row.Name);
            if (key.Length == 0) continue;
            if (!grouped.TryGetValue(key, out var g))
                grouped[key] = g = new EngineeringScopeAggregate(row.Name?.Trim() ?? key);
            var resolution = ResolveHourlyCost(catalog, row);
            g.Hours += row.Hours;
            g.Entries++;
            var cost = resolution.CostFor(row.Hours);
            if (cost.HasValue) g.Known += cost.Value;
            else
            {
                g.Missing += row.Hours;
                g.Status = resolution.Status;
            }
        }

        // Nomes adicionados manualmente continuam aparecendo mesmo antes de terem lançamentos.
        foreach (var (key, setting) in settings)
            if (!grouped.ContainsKey(key)) grouped[key] = new EngineeringScopeAggregate(setting.Name)
                { Status = "SEM_LANCAMENTOS" };
        var all = grouped.Select(x =>
        {
            var g = x.Value;
            var excluded = settings.TryGetValue(x.Key, out var setting) && setting.Excluded;
            return new EngineeringProfessionalRow(g.Name, g.Hours, g.Missing, g.Known,
                g.Entries, g.Status, excluded);
        }).OrderByDescending(x => x.MissingCostHours > 0)
          .ThenByDescending(x => x.Excluded)
          .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(identity.ProjectCode, all, all.Sum(x => x.Hours),
            all.Sum(x => x.MissingCostHours), all.Where(x => x.Excluded).Sum(x => x.Hours),
            all.Where(x => x.Excluded).Sum(x => x.KnownCost));
    }

    public async Task<bool> SaveEngineeringProfessionalScopeAsync(string projectId,
        EngineeringProfessionalScopeInput input, string? alteredBy, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var normalized = CostTimesheetMatchingRules.NormalizeName(name);
        if (name.Length == 0 || name.Length > 250 || normalized.Length == 0 || normalized.Length > 250)
            throw new ArgumentException("Informe o nome válido do profissional (até 250 caracteres).");
        await using var db = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE qualidade.ProfissionalEscopoEngenharia WITH (UPDLOCK, SERIALIZABLE)
            SET NomeExibicao=@name, ExcluirEngenharia=@excluded,
                AtualizadoPor=@actor, AtualizadoEm=SYSUTCDATETIME()
            WHERE Project_ID_ACC=@project AND NomeNormalizado=@normalized;
            IF @@ROWCOUNT=0
                INSERT INTO qualidade.ProfissionalEscopoEngenharia
                  (Project_ID_ACC,NomeNormalizado,NomeExibicao,ExcluirEngenharia,AtualizadoPor)
                VALUES (@project,@normalized,@name,@excluded,@actor);
            COMMIT TRANSACTION;
            """, db) { CommandTimeout = 20 };
        cmd.Parameters.Add("@project", SqlDbType.NVarChar, 255).Value = projectId;
        cmd.Parameters.Add("@normalized", SqlDbType.NVarChar, 250).Value = normalized;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 250).Value = name;
        cmd.Parameters.Add("@excluded", SqlDbType.Bit).Value = input.ExcludeEngineering;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 300).Value = (object?)alteredBy ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }
}
