using System.Data;
using APS.WebApp.Services.Costs;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

// Valores distintos por fonte: NUNCA substituir a LDP ou espelho pelo bruto sem declarar.
public sealed record AuditDifferenceDocument(
    string Document, decimal LdpHh, decimal LdpCost,
    decimal? MirrorHh, decimal? MirrorCost,
    decimal TimesheetAllHh, decimal TimesheetAllCost,
    decimal TimesheetEngHh, decimal TimesheetEngCost,
    decimal ManagementHh, decimal ManagementCost,
    decimal MissingCostHh, decimal DifferenceHh, decimal DifferenceCost,
    int Entries, string Reason);
public sealed record AuditDifferencePerson(
    string Name, decimal Hours, decimal KnownCost, decimal MissingCostHh,
    decimal? MirrorHh, decimal? MirrorCost, bool Management, int Entries, int Documents);
public sealed record AuditDifferenceEntry(
    string Document, DateTime? Date, string Person, decimal Hh,
    decimal? Cost, string CostStatus, bool Management, string Description);
public sealed record AuditDifferenceDay(
    DateTime? Date, string Person, decimal RawDayHh, decimal SplitDocumentHh,
    decimal DifferenceHh, bool Management);
public sealed record AuditDifferenceResult(
    string ProjectCode, decimal LdpHh, decimal LdpCost,
    decimal? MirrorHh, decimal? MirrorCost,
    decimal TimesheetAllHh, decimal TimesheetAllKnownCost,
    decimal TimesheetEngineeringHh, decimal TimesheetEngineeringKnownCost,
    decimal ManagementHh, decimal ManagementKnownCost, decimal MissingCostHh,
    decimal DifferenceHh, decimal DifferenceCost,
    string? MirrorError, IReadOnlyList<AuditDifferenceDocument> Documents,
    IReadOnlyList<AuditDifferencePerson> People, IReadOnlyList<AuditDifferenceEntry> Entries,
    decimal RawDayHh = 0m, decimal RawDayEngineeringHh = 0m,
    decimal RawDayManagementHh = 0m, decimal UnreconciledEngineeringHh = 0m,
    IReadOnlyList<AuditDifferenceDay>? DailyReconciliation = null, int SplitEntries = 0);

public sealed partial class ProductionAuditRepository
{
    private const string WithoutDocument = "(SEM DOCUMENTO)";
    private sealed class AuditBucket
    {
        public decimal LdpHh, LdpCost, MirrorHh, MirrorCost;
        public decimal RawHh, RawCost, EngHh, EngCost, ManagementHh, ManagementCost, MissingCostHh;
        public int Entries;
    }
    private sealed class AuditDayBucket(DateTime? date, string name, bool management)
    {
        public DateTime? Date { get; } = date;
        public string Name { get; } = name;
        public bool Management { get; } = management;
        public decimal RawHh;
        public decimal SplitHh;
    }
    private sealed record SplitDocumentRow(
        DateTime? Date, string? Name, decimal Hours,
        string? DocumentCode, string? Description, string? Function, string? Discipline);
    private sealed class AuditPersonBucket(string name)
    {
        public string Name { get; } = name;
        public decimal Hh, Cost, MissingCostHh, MirrorHh, MirrorCost;
        public int Entries;
        public readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fonte oficial da quebra por Número do Documento (N&A BI).
    /// É a mesma origem de dbo.VW_TimeSheet_NA, mas lida diretamente para evitar
    /// aplicar novamente joins/renormalizações e para manter cada HH distribuída.
    /// </summary>
    private async Task<List<SplitDocumentRow>> ReadDocumentAllocatedRowsAsync(ProjectIdentity identity, CancellationToken ct)
    {
        var output = new List<SplitDocumentRow>();
        await using var bi = await OpenBiAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT [data],[Nome],ISNULL([Trabalhadas],0),[DocumentoCodigo],
                   [Descricao],[Funcao],[Disciplina]
            FROM dbo.tbTimeSheetRealizadoDocumento
            WHERE [Projeto]=@project AND [doc1]=@cr
              AND ISNULL([Trabalhadas],0)<>0;
            """, bi) { CommandTimeout = 90 };
        cmd.Parameters.Add("@project", SqlDbType.NVarChar, 100).Value = identity.BiProjectCode;
        cmd.Parameters.Add("@cr", SqlDbType.NVarChar, 100).Value = identity.ProjectCode;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            output.Add(new SplitDocumentRow(
                Date(r, 0), Text(r, 1), Num(r, 2) ?? 0m,
                Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6)));
        return output;
    }

    public async Task<AuditDifferenceResult> ReadAuditDifferencesAsync(string projectId, CancellationToken ct)
    {
        // Duas fontes com finalidades distintas: dia bruto valida HH total; documento distribuído valida cada Número do Documento.
        var (identity, raw) = await ReadEngineeringRowsAsync(projectId, ct);
        var allocated = await ReadDocumentAllocatedRowsAsync(identity, ct);
        var classifications = await LoadEngineeringScopeAsync(projectId, ct);
        var days = new Dictionary<string, AuditDayBucket>(StringComparer.Ordinal);
        AuditDayBucket GetDay(DateTime? date, string? name)
        {
            var normalized = CostTimesheetMatchingRules.NormalizeName(name);
            var key = $"{date:yyyyMMdd}|{normalized}";
            if (!days.TryGetValue(key, out var day))
                days[key] = day = new AuditDayBucket(date, name?.Trim() ?? "(SEM NOME)",
                    classifications.TryGetValue(normalized, out var management) && management.Excluded);
            return day;
        }
        foreach (var row in raw) GetDay(row.Date, row.Name).RawHh += row.Hours;

        var docs = new Dictionary<string, AuditBucket>(StringComparer.OrdinalIgnoreCase);
        var people = new Dictionary<string, AuditPersonBucket>(StringComparer.Ordinal);
        var entries = new List<AuditDifferenceEntry>(raw.Count);
        static string Doc(string? d) => string.IsNullOrWhiteSpace(d) ? WithoutDocument : d.Trim().ToUpperInvariant();
        AuditBucket GetDoc(string d)
        {
            if (!docs.TryGetValue(d, out var bucket)) docs[d] = bucket = new AuditBucket();
            return bucket;
        }

        // LDP atual por Número do documento. A coluna Total Corrigido corresponde à LDP mostrada na auditoria.
        await using (var db = await OpenAsync(ct))
        await using (var cmd = new SqlCommand("""
            SELECT NA_Document_Number,
                   SUM(CAST(ISNULL(Actual_HH_Total_Corrigido,0) AS DECIMAL(28,6))),
                   SUM(CAST(ISNULL(Actual_Cost_Corrigido,0) AS DECIMAL(28,6)))
            FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@project
            GROUP BY NA_Document_Number
            """, db) { CommandTimeout = 90 })
        {
            cmd.Parameters.Add("@project", SqlDbType.NVarChar, 255).Value = projectId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var bucket = GetDoc(Doc(Text(reader,0)));
                bucket.LdpHh += Num(reader,1) ?? 0m;
                bucket.LdpCost += Num(reader,2) ?? 0m;
            }
        }

        // Espelho gerencial usado no "Resumo atual" (fonte diferente do custo/hora calculado do Timesheet bruto).
        string? mirrorError = null;
        var mirrorAvailable = false;
        try
        {
            await using var db = await OpenAsync(ct);
            await using var cmd = new SqlCommand("""
                SELECT DocumentoCodigo_Corrigido, NomeProfissional,
                       SUM(CAST(ISNULL(HorasTrabalhadas,0) AS DECIMAL(28,6))) AS HH,
                       SUM(CAST(ISNULL(CustoCalculado,0) AS DECIMAL(28,6))) AS Custo
                FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
                WHERE Project_ID_ACC=@project
                GROUP BY DocumentoCodigo_Corrigido,NomeProfissional
                """, db) { CommandTimeout = 90 };
            cmd.Parameters.Add("@project", SqlDbType.NVarChar, 255).Value = projectId;
            // Materializar antes de agregar evita apresentar metade dos dados em caso de timeout.
            var mirrorRows = new List<(string Document,string Person,decimal Hh,decimal Cost)>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    mirrorRows.Add((Doc(Text(reader,0)),Text(reader,1) ?? "", Num(reader,2) ?? 0m, Num(reader,3) ?? 0m));
            foreach (var row in mirrorRows)
            {
                var d = GetDoc(row.Document);
                d.MirrorHh += row.Hh; d.MirrorCost += row.Cost;
                var key = CostTimesheetMatchingRules.NormalizeName(row.Person);
                if (key.Length == 0) continue;
                if (!people.TryGetValue(key, out var p)) people[key] = p = new AuditPersonBucket(row.Person);
                p.MirrorHh += row.Hh; p.MirrorCost += row.Cost;
            }
            mirrorAvailable = true;
        }
        catch (SqlException e) when (!ct.IsCancellationRequested)
        {
            mirrorError = $"Espelho gerencial indisponível ({e.Number}). Totais do espelho não calculados.";
        }

        await using var local = await OpenAsync(ct);
        var catalog = await ProfessionalHourlyCostCatalog.LoadAsync(local, projectId, ct);

        // Cada linha do Timesheet Realizado por Documento já tem HH próprias (ex.: 00:15h => 0,25 HH).
        // Proibido inferir o documento pelo primeiro regex da Descrição do Timesheet diário.
        foreach (var row in allocated)
        {
            var document = Doc(row.DocumentCode);
            var b = GetDoc(document);
            var name = row.Name?.Trim() ?? "(SEM NOME)";
            var key = CostTimesheetMatchingRules.NormalizeName(row.Name);
            if (!people.TryGetValue(key, out var p)) people[key] = p = new AuditPersonBucket(name);
            var managed = classifications.TryGetValue(key, out var c) && c.Excluded;
            var costResolution = catalog.Resolve(row.Name, row.Date.HasValue ? DateOnly.FromDateTime(row.Date.Value) : null);
            var cost = costResolution.CostFor(row.Hours);
            b.RawHh += row.Hours;
            b.RawCost += cost ?? 0m;
            b.Entries++;
            p.Hh += row.Hours; p.Cost += cost ?? 0m; p.Entries++; p.Documents.Add(document);
            GetDay(row.Date, row.Name).SplitHh += row.Hours;
            if (managed)
            {
                b.ManagementHh += row.Hours; b.ManagementCost += cost ?? 0m;
            }
            else
            {
                b.EngHh += row.Hours; b.EngCost += cost ?? 0m;
                if (!cost.HasValue) { b.MissingCostHh += row.Hours; p.MissingCostHh += row.Hours; }
            }
            var description = row.Description ?? "";
            entries.Add(new AuditDifferenceEntry(document, row.Date, name, row.Hours, cost,
                costResolution.Status, managed,
                description.Length > 500 ? description[..500] : description));
        }

        var detail = docs.Select(kv =>
        {
            var (code, b) = (kv.Key, kv.Value);
            var diffHh = b.EngHh - b.LdpHh;
            var diffCost = b.EngCost - b.LdpCost;
            var issues = new List<string>();
            if (code == WithoutDocument) issues.Add("Lançamentos sem código de documento");
            if (b.LdpHh == 0m && b.EngHh != 0m) issues.Add("HH só no Timesheet");
            if (b.EngHh == 0m && b.LdpHh != 0m) issues.Add("HH só na LDP / Gestão excluída");
            if (b.ManagementHh != 0m) issues.Add("Gestão excluída dos indicadores");
            if (b.MissingCostHh != 0m) issues.Add("HH sem custo cadastrado");
            if (Math.Abs(diffHh) > 0.005m) issues.Add("HH divergente por documento");
            if (Math.Abs(diffCost) > 0.01m) issues.Add("Custo divergente; conferir taxa e composição");
            if (mirrorAvailable && Math.Abs(b.MirrorHh - b.RawHh) > 0.005m)
                issues.Add("Espelho gerencial e Timesheet distribuído por documento diferentes");
            if (issues.Count == 0) issues.Add("Conciliado");
            return new AuditDifferenceDocument(code,b.LdpHh,b.LdpCost,
                mirrorAvailable ? b.MirrorHh : null, mirrorAvailable ? b.MirrorCost : null,
                b.RawHh,b.RawCost,b.EngHh,b.EngCost,b.ManagementHh,b.ManagementCost,
                b.MissingCostHh,diffHh,diffCost,b.Entries,string.Join("; ",issues));
        }).OrderByDescending(x => Math.Max(Math.Abs(x.DifferenceCost) / 100m, Math.Abs(x.DifferenceHh)))
          .ThenBy(x => x.Document,StringComparer.OrdinalIgnoreCase).ToArray();

        var personRows = people.Select(kv =>
        {
            var (key,p)=(kv.Key,kv.Value);
            return new AuditDifferencePerson(p.Name,p.Hh,p.Cost,p.MissingCostHh,
                mirrorAvailable ? p.MirrorHh : null, mirrorAvailable ? p.MirrorCost : null,
                classifications.TryGetValue(key,out var c) && c.Excluded,p.Entries,p.Documents.Count);
        }).OrderByDescending(x => x.MissingCostHh)
          .ThenByDescending(x => x.Hours).ToArray();

        // Conciliação independente por dia/profissional: não completar o documento com o total bruto.
        // O usuário vê explicitamente as HH sem rateio (ou excedentes no rateio) e investiga a origem.
        var daily = days.Values.Select(day => new AuditDifferenceDay(
            day.Date, day.Name, day.RawHh, day.SplitHh, day.RawHh - day.SplitHh, day.Management))
            .Where(day => Math.Abs(day.DifferenceHh) > 0.005m)
            .OrderByDescending(day => Math.Abs(day.DifferenceHh))
            .ThenBy(day => day.Date).ToArray();
        var rawDayHh = days.Values.Sum(x => x.RawHh);
        var rawDayEngHh = days.Values.Where(x => !x.Management).Sum(x => x.RawHh);
        var rawDayManagementHh = days.Values.Where(x => x.Management).Sum(x => x.RawHh);
        var splitEngHh = days.Values.Where(x => !x.Management).Sum(x => x.SplitHh);

        return new AuditDifferenceResult(identity.ProjectCode,
            detail.Sum(x => x.LdpHh), detail.Sum(x => x.LdpCost),
            mirrorAvailable ? detail.Sum(x => x.MirrorHh ?? 0m) : null,
            mirrorAvailable ? detail.Sum(x => x.MirrorCost ?? 0m) : null,
            detail.Sum(x => x.TimesheetAllHh),detail.Sum(x => x.TimesheetAllCost),
            detail.Sum(x => x.TimesheetEngHh),detail.Sum(x => x.TimesheetEngCost),
            detail.Sum(x => x.ManagementHh),detail.Sum(x => x.ManagementCost),
            detail.Sum(x => x.MissingCostHh),
            detail.Sum(x => x.DifferenceHh),detail.Sum(x => x.DifferenceCost),
            mirrorError, detail, personRows, entries,
            rawDayHh, rawDayEngHh, rawDayManagementHh,
            rawDayEngHh - splitEngHh, daily, allocated.Count);
    }
}
