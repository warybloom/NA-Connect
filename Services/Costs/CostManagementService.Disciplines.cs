using System.Data;
using System.Globalization;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Services.Costs;

public sealed record PackageModelSupport(bool DisciplineModelInstalled, string? Message);
public sealed record LdpDisciplineSummary(string Disciplina, int Documentos, int Ativos, int Cancelados, decimal HHLdpAtivos, decimal CustoLdpAtivos,
    IReadOnlyList<LdpPackageDocument> Itens);
public sealed record LdpDisciplineCatalog(IReadOnlyList<LdpDisciplineSummary> Disciplinas, int DocumentosSemDisciplina, string? Diagnostico);
public sealed record StoredPackageDiscipline(int Id, int PackageId, string Disciplina, string Abrangencia, decimal HHPrevisto, decimal CustoPrevisto,
    decimal? HHPrevistoLdp, decimal? CustoPrevistoLdp, int? DocumentosLdp, string? Justificativa, IReadOnlyList<string> Documentos, DateTime AtualizadoEm);
/// <summary>Apropriação de um documento do rateio: bruto (Timesheet) × apropriado (até o previsto) × excedente; custo pelas regras existentes.</summary>
public sealed record DisciplineAppropriationLine(int PackageId, string Disciplina, string DocumentoCodigo, string? DocumentStatus, decimal? StatusPercentual,
    decimal HHPrevisto, decimal HHDireto, decimal HHRateado, decimal HHBruto, decimal HHApropriado, decimal HHExcedente, decimal HHSaldo,
    decimal CustoPrevisto, decimal CustoBruto, decimal CustoApropriado, decimal CustoExcedente, decimal CustoLiberado);
public sealed record DisciplinePendingHours(string Tipo, string? Disciplina, string? DocumentoCodigo, decimal Horas, int Lancamentos, string Motivo);
public sealed record DisciplineExcessAlert(string ProjectIdAcc, int PackageId, string Pacote, string? Profissional, string Disciplina, string? DocumentoCodigo,
    decimal HHPrevisto, decimal HHBruto, decimal HHApropriado, decimal HHExcedente, string Situacao);
public sealed record DisciplineTotals(decimal HHPrevisto, decimal HHBruto, decimal HHApropriado, decimal HHExcedente, decimal HHSaldo,
    decimal CustoPrevisto, decimal CustoBruto, decimal CustoApropriado, decimal CustoExcedente, decimal CustoLiberado, decimal HHPendente);
public sealed record DisciplinePackageDetail(CostPackage Pacote, IReadOnlyList<StoredPackageDiscipline> Cadastro, DisciplinePackagePreview RateioAtual,
    IReadOnlyList<DisciplineAppropriationLine> Apropriacao, IReadOnlyList<DisciplinePendingHours> Pendencias, IReadOnlyList<DisciplineExcessAlert> Excedentes,
    DisciplineTotals Totais)
{
    public PackageHistoricalFacts? HistoricalMeasurement { get; init; }
    public decimal HhGerencial => HistoricalMeasurement is { Disciplines.Count: >0 } h ? h.Disciplines.Sum(d=>d.Balance.RecognizedHours+d.Balance.ComplementHours) : Totais.HHApropriado;
    public decimal CustoGerencial => HistoricalMeasurement is { Disciplines.Count: >0 } h ? h.Disciplines.Sum(d=>d.Balance.RecognizedCost+d.Balance.ComplementCost) : Totais.CustoApropriado;
}
public sealed record DisciplineImportRow(int Row, string Pacote, string? Profissional, string? Empresa, string Disciplina, decimal? HHPrevisto,
    decimal? CustoPrevisto, string Abrangencia, IReadOnlyList<string> Documentos, string? Justificativa, string? Erro);
public sealed record DisciplineImportPackage(string Pacote, string? Profissional, string? Empresa, DisciplinePackagePreview Previa, IReadOnlyList<string> Erros);
public sealed record DisciplineImportPreview(IReadOnlyList<DisciplineImportRow> Rows, IReadOnlyList<DisciplineImportPackage> Pacotes, bool Valido, int Invalidas);

/// <summary>Modelo de cadastro Por Disciplina (Pacotes). Separado de ICostManagementService para não quebrar implementações existentes.</summary>
public interface IPackageDisciplineService
{
    Task<PackageModelSupport> PackageModelSupportAsync(CancellationToken ct);
    Task<LdpDisciplineCatalog> LdpDisciplinesAsync(string projectId, CancellationToken ct);
    Task<DisciplinePackagePreview> PreviewDisciplinePackageAsync(string projectId, DisciplinePackageInput input, CancellationToken ct);
    Task<int> CreateDisciplinePackageAsync(string projectId, DisciplinePackageInput input, string? author, CancellationToken ct);
    Task<bool> UpdateDisciplinePackageAsync(string projectId, int packageId, DisciplinePackageInput input, string? author, CancellationToken ct);
    Task<DisciplinePackageDetail> DisciplinePackageDetailAsync(string projectId, int packageId, CancellationToken ct);
    Task<byte[]> DisciplineImportTemplateAsync(CancellationToken ct);
    Task<DisciplineImportPreview> PreviewDisciplineExcelAsync(string projectId, Stream file, CancellationToken ct);
    Task<CostImportCommitResult> CommitDisciplineExcelAsync(string projectId, Stream file, string? author, CancellationToken ct);
}

public sealed partial class CostManagementService : IPackageDisciplineService
{
    private const string SchemaMissing = "Modelo Por Disciplina indisponível: aplique o script Scripts/09D_PackageDisciplineModel.sql em DB_NA_CONNECT.";
    private bool? disciplineSchema;

    private async Task<bool> DisciplineSchemaAsync(CancellationToken ct)
    {
        if (disciplineSchema is bool known) return known;
        await using var c = await Open(ct);
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN COL_LENGTH(N'costs.Package',N'ModeloCadastro') IS NOT NULL
                         AND OBJECT_ID(N'costs.PackageDiscipline',N'U') IS NOT NULL
                         AND OBJECT_ID(N'costs.PackageDisciplineDocument',N'U') IS NOT NULL
                         AND OBJECT_ID(N'costs.PackageDisciplineAllocationHistory',N'U') IS NOT NULL THEN 1 ELSE 0 END
            """, c);
        disciplineSchema = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
        return disciplineSchema.Value;
    }

    private async Task RequireDisciplineSchemaAsync(CancellationToken ct)
    {
        if (!await DisciplineSchemaAsync(ct)) throw new InvalidOperationException(SchemaMissing);
    }

    /// <summary>DOCUMENTO quando a 09D não foi aplicada. Usado para impedir edição de documentos manuais em pacote Por Disciplina.</summary>
    private static async Task<string> PackageModelAsync(SqlConnection c, SqlTransaction? tx, int packageId, CancellationToken ct)
    {
        await using (var exists = new SqlCommand("SELECT CASE WHEN COL_LENGTH(N'costs.Package',N'ModeloCadastro') IS NULL THEN 0 ELSE 1 END", c, tx))
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0) return PackageModel.Documento;
        await using var cmd = new SqlCommand("SELECT ModeloCadastro FROM costs.Package WHERE Id=@id", c, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = packageId;
        return await cmd.ExecuteScalarAsync(ct) as string ?? PackageModel.Documento;
    }

    private static async Task EnsureDocumentModelAsync(SqlConnection c, SqlTransaction tx, int packageId, CancellationToken ct)
    {
        if (PackageModel.IsDiscipline(await PackageModelAsync(c, tx, packageId, ct)))
            throw new InvalidOperationException("Pacote Por Disciplina: os documentos vêm da LDP Unificada. Altere as disciplinas do pacote em vez de documentos manuais.");
    }

    public async Task<PackageModelSupport> PackageModelSupportAsync(CancellationToken ct) =>
        await DisciplineSchemaAsync(ct) ? new(true, null) : new(false, SchemaMissing);

    // ------------------------------------------------------------------------------------------------ LDP ----
    /// <summary>Documentos da LDP Unificada (somente leitura) com a disciplina canônica da Norma de Coordenação e o status atual.</summary>
    private async Task<IReadOnlyList<LdpPackageDocument>> LoadLdpDocumentsAsync(string projectId, PackageContext context, CancellationToken ct)
    {
        await using var c = await Open(ct);
        var sql = "SELECT NA_Document_Number,Document_Title,Planned_HH,Planned_Cost FROM dbo.VW_Ldp_Unificada V WHERE V.Project_ID_ACC=@p AND " + LdpItemRules.SqlIsDocument("V");
        var rows = new List<(string Code, string? Title, decimal? Hours, decimal? Cost)>();
        await using (var cmd = Cmd(sql, c, projectId))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (!r.IsDBNull(0) && Normalize(r.GetString(0)) is { Length: > 0 } code)
                    rows.Add((code, Txt(r, 1), r.IsDBNull(2) ? null : Dec(r, 2), r.IsDBNull(3) ? null : Dec(r, 3)));
        }
        return rows.GroupBy(x => x.Code, StringComparer.Ordinal).Select(g =>
        {
            // Linhas repetidas do mesmo número: conferência usa o maior Planned_HH/Planned_Cost informado (sem somar duplicidade).
            var (doc3, discipline, _) = DisciplineOf(g.Key, context);
            context.Statuses.TryGetValue(g.Key, out var status);
            decimal? percent = context.Percentages.TryGetValue(DocumentStatusCanonical.Key(status), out var p) ? p : null;
            return new LdpPackageDocument(g.Key, discipline, g.Select(x => x.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)), status,
                PackageAllocationRules.IsCancelled(status), g.Max(x => x.Hours), g.Max(x => x.Cost), percent, doc3);
        }).OrderBy(x => x.Codigo, StringComparer.Ordinal).ToList();
    }

    public async Task<LdpDisciplineCatalog> LdpDisciplinesAsync(string projectId, CancellationToken ct)
    {
        var context = await LoadPackageContextAsync(projectId, ct);
        var docs = await LoadLdpDocumentsAsync(projectId, context, ct);
        var label = CoordinationDisciplineResolver.SemDisciplinaLabel;
        var groups = docs.Where(d => d.Disciplina != label).GroupBy(d => d.Disciplina).OrderBy(g => g.Key)
            .Select(g => new LdpDisciplineSummary(g.Key, g.Count(), g.Count(d => !d.Cancelado), g.Count(d => d.Cancelado),
                g.Where(d => !d.Cancelado).Sum(d => d.PlannedHH ?? 0m), g.Where(d => !d.Cancelado).Sum(d => d.PlannedCost ?? 0m), g.ToList()))
            .ToList();
        var semDisciplina = docs.Count(d => d.Disciplina == label);
        return new(groups, semDisciplina, context.NormaId is null ? "Projeto sem Norma de Coordenação ativa: disciplinas não podem ser identificadas." :
            semDisciplina > 0 ? $"{semDisciplina} documento(s) da LDP sem disciplina pela Norma de Coordenação: não entram em pacotes por disciplina." : null);
    }

    public async Task<DisciplinePackagePreview> PreviewDisciplinePackageAsync(string projectId, DisciplinePackageInput input, CancellationToken ct)
    {
        var context = await LoadPackageContextAsync(projectId, ct);
        return PackageDisciplineRules.Preview(input, await LoadLdpDocumentsAsync(projectId, context, ct), CoordinationDisciplineResolver.SemDisciplinaLabel);
    }

    // -------------------------------------------------------------------------------------------- gravação ----
    private async Task<DisciplinePackagePreview> ValidPreviewAsync(string projectId, DisciplinePackageInput input, CancellationToken ct)
    {
        await RequireDisciplineSchemaAsync(ct);
        var preview = await PreviewDisciplinePackageAsync(projectId, input, ct);
        if (!preview.Valido)
            throw new InvalidOperationException("Rateio inconsistente: " + string.Join(" ", preview.Erros.Concat(preview.Disciplinas.SelectMany(d => d.Erros.Select(e => $"[{d.Disciplina}] {e}")))));
        return preview;
    }

    public async Task<int> CreateDisciplinePackageAsync(string projectId, DisciplinePackageInput input, string? author, CancellationToken ct)
    {
        var preview = await ValidPreviewAsync(projectId, input, ct);
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var id = await InsertDisciplinePackageAsync(c, tx, projectId, input, preview, author, ct);
            await tx.CommitAsync(ct);
            return id;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    private static async Task<int> InsertDisciplinePackageAsync(SqlConnection c, SqlTransaction tx, string projectId, DisciplinePackageInput input,
        DisciplinePackagePreview preview, string? author, CancellationToken ct)
    {
        await EnsureUniquePackageNameAsync(c, tx, projectId, input.NomeReferencia, null, ct);
        await using var cmd = new SqlCommand("""
            INSERT INTO costs.Package(Project_ID_ACC,Empresa,NomeReferencia,AliasTimesheet,Descricao,HHBase,CustoBase,ModeloCadastro)
            VALUES(@p,@e,@n,@a,@d,@h,@c,'DISCIPLINA');
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, c, tx);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        cmd.Parameters.Add("@e", SqlDbType.NVarChar, 200).Value = (object?)NullIfBlank(input.Empresa) ?? DBNull.Value;
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = input.NomeReferencia.Trim();
        cmd.Parameters.Add("@a", SqlDbType.NVarChar, 200).Value = (object?)NullIfBlank(input.AliasTimesheet) ?? DBNull.Value;
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, -1).Value = (object?)NullIfBlank(input.Descricao) ?? DBNull.Value;
        Decimal(cmd, "@h", preview.HHTotal);
        Decimal(cmd, "@c", preview.CustoTotal);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await InsertDisciplinesAsync(c, tx, id, input, preview, author, "CADASTRO", ct);
        return id;
    }

    private static async Task InsertDisciplinesAsync(SqlConnection c, SqlTransaction tx, int packageId, DisciplinePackageInput input,
        DisciplinePackagePreview preview, string? author, string reason, CancellationToken ct)
    {
        foreach (var (plan, source) in preview.Disciplinas.Zip(input.Disciplinas))
        {
            int disciplineId;
            await using (var cmd = new SqlCommand("""
                INSERT INTO costs.PackageDiscipline(PackageId,Disciplina,Abrangencia,HHPrevisto,CustoPrevisto,HHPrevistoLdp,CustoPrevistoLdp,DocumentosLdp,Justificativa,CriadoPor)
                VALUES(@pk,@disc,@scope,@h,@c,@hl,@cl,@n,@j,@by);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, c, tx))
            {
                cmd.Parameters.Add("@pk", SqlDbType.Int).Value = packageId;
                cmd.Parameters.Add("@disc", SqlDbType.NVarChar, 150).Value = plan.Disciplina;
                cmd.Parameters.Add("@scope", SqlDbType.VarChar, 12).Value = plan.Abrangencia;
                Decimal(cmd, "@h", plan.HHPrevisto); Decimal(cmd, "@c", plan.CustoPrevisto);
                Decimal(cmd, "@hl", plan.HHLdp); Decimal(cmd, "@cl", plan.CustoLdp);
                cmd.Parameters.Add("@n", SqlDbType.Int).Value = plan.DocumentosElegiveis;
                cmd.Parameters.Add("@j", SqlDbType.NVarChar, 1000).Value = (object?)plan.Justificativa ?? DBNull.Value;
                cmd.Parameters.Add("@by", SqlDbType.NVarChar, 320).Value = (object?)author ?? DBNull.Value;
                disciplineId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            }
            if (plan.Abrangencia == PackageModel.Selecionados)
            {
                // Guarda a escolha do usuário (inclusive cancelados) para preservar o vínculo; o rateio ativo ignora cancelados.
                var chosen = (source.Documentos ?? Array.Empty<string>()).Select(Normalize).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal);
                foreach (var code in chosen)
                {
                    await using var doc = new SqlCommand("INSERT INTO costs.PackageDisciplineDocument(PackageDisciplineId,DocumentoCodigo) VALUES(@id,@doc)", c, tx);
                    doc.Parameters.Add("@id", SqlDbType.Int).Value = disciplineId;
                    doc.Parameters.Add("@doc", SqlDbType.NVarChar, 255).Value = code;
                    await doc.ExecuteNonQueryAsync(ct);
                }
            }
            foreach (var share in plan.Documentos)
            {
                await using var history = new SqlCommand("""
                    INSERT INTO costs.PackageDisciplineAllocationHistory(PackageDisciplineId,DocumentoCodigo,HHPrevisto,CustoPrevisto,DocumentosElegiveis,Motivo,RegistradoPor)
                    VALUES(@id,@doc,@h,@c,@n,@m,@by);
                    """, c, tx);
                history.Parameters.Add("@id", SqlDbType.Int).Value = disciplineId;
                history.Parameters.Add("@doc", SqlDbType.NVarChar, 255).Value = share.DocumentoCodigo;
                Decimal(history, "@h", share.HHPrevisto); Decimal(history, "@c", share.CustoPrevisto);
                history.Parameters.Add("@n", SqlDbType.Int).Value = plan.DocumentosElegiveis;
                history.Parameters.Add("@m", SqlDbType.VarChar, 30).Value = reason;
                history.Parameters.Add("@by", SqlDbType.NVarChar, 320).Value = (object?)author ?? DBNull.Value;
                await history.ExecuteNonQueryAsync(ct);
            }
        }
    }

    public async Task<bool> UpdateDisciplinePackageAsync(string projectId, int packageId, DisciplinePackageInput input, string? author, CancellationToken ct)
    {
        var preview = await ValidPreviewAsync(projectId, input, ct);
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await EnsurePackageAsync(c, tx, projectId, packageId, ct);
            if (!PackageModel.IsDiscipline(await PackageModelAsync(c, tx, packageId, ct)))
                throw new InvalidOperationException("Este pacote é Por Documento. Pacotes existentes não são convertidos de modelo.");
            await EnsureUniquePackageNameAsync(c, tx, projectId, input.NomeReferencia, packageId, ct);
            await using (var cmd = new SqlCommand("""
                UPDATE costs.Package SET Empresa=@e,NomeReferencia=@n,AliasTimesheet=@a,Descricao=@d,HHBase=@h,CustoBase=@c,AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@id AND Project_ID_ACC=@p;
                UPDATE x SET Ativo=0,DesativadoEm=SYSUTCDATETIME() FROM costs.PackageDisciplineDocument x JOIN costs.PackageDiscipline d ON d.Id=x.PackageDisciplineId
                WHERE d.PackageId=@id AND d.Ativo=1 AND x.Ativo=1;
                UPDATE costs.PackageDiscipline SET Ativo=0,AtualizadoEm=SYSUTCDATETIME() WHERE PackageId=@id AND Ativo=1;
                """, c, tx))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = packageId;
                cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                cmd.Parameters.Add("@e", SqlDbType.NVarChar, 200).Value = (object?)NullIfBlank(input.Empresa) ?? DBNull.Value;
                cmd.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = input.NomeReferencia.Trim();
                cmd.Parameters.Add("@a", SqlDbType.NVarChar, 200).Value = (object?)NullIfBlank(input.AliasTimesheet) ?? DBNull.Value;
                cmd.Parameters.Add("@d", SqlDbType.NVarChar, -1).Value = (object?)NullIfBlank(input.Descricao) ?? DBNull.Value;
                Decimal(cmd, "@h", preview.HHTotal); Decimal(cmd, "@c", preview.CustoTotal);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            // Versões anteriores ficam inativas (com o histórico de rateio) — nada é apagado.
            await InsertDisciplinesAsync(c, tx, packageId, input, preview, author, "ALTERACAO", ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    private async Task<IReadOnlyList<StoredPackageDiscipline>> StoredDisciplinesAsync(string projectId, int? packageId, CancellationToken ct)
    {
        if (!await DisciplineSchemaAsync(ct)) return Array.Empty<StoredPackageDiscipline>();
        await using var c = await Open(ct);
        var docs = new Dictionary<int, List<string>>();
        await using (var cmd = Cmd("""
            SELECT x.PackageDisciplineId,x.DocumentoCodigo FROM costs.PackageDisciplineDocument x
            JOIN costs.PackageDiscipline d ON d.Id=x.PackageDisciplineId JOIN costs.Package p ON p.Id=d.PackageId
            WHERE p.Project_ID_ACC=@p AND d.Ativo=1 AND x.Ativo=1 AND (@pk IS NULL OR p.Id=@pk)
            """, c, projectId))
        {
            cmd.Parameters.Add("@pk", SqlDbType.Int).Value = (object?)packageId ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) { var id = r.GetInt32(0); if (!docs.TryGetValue(id, out var list)) docs[id] = list = new(); list.Add(r.GetString(1)); }
        }
        var output = new List<StoredPackageDiscipline>();
        await using (var cmd = Cmd("""
            SELECT d.Id,d.PackageId,d.Disciplina,d.Abrangencia,d.HHPrevisto,d.CustoPrevisto,d.HHPrevistoLdp,d.CustoPrevistoLdp,d.DocumentosLdp,d.Justificativa,d.AtualizadoEm
            FROM costs.PackageDiscipline d JOIN costs.Package p ON p.Id=d.PackageId
            WHERE p.Project_ID_ACC=@p AND d.Ativo=1 AND (@pk IS NULL OR p.Id=@pk) ORDER BY d.PackageId,d.Disciplina
            """, c, projectId))
        {
            cmd.Parameters.Add("@pk", SqlDbType.Int).Value = (object?)packageId ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                output.Add(new(id, r.GetInt32(1), r.GetString(2), r.GetString(3), Dec(r, 4), Dec(r, 5), r.IsDBNull(6) ? null : Dec(r, 6),
                    r.IsDBNull(7) ? null : Dec(r, 7), r.IsDBNull(8) ? null : r.GetInt32(8), Txt(r, 9),
                    docs.TryGetValue(id, out var list) ? list : Array.Empty<string>(), r.GetDateTime(10)));
            }
        }
        return output;
    }

    private static DisciplinePackageInput InputFrom(CostPackage package, IEnumerable<StoredPackageDiscipline> stored) =>
        new(package.Empresa, package.NomeReferencia, package.AliasTimesheet, package.Descricao,
            stored.Select(s => new PackageDisciplineInput(s.Disciplina, s.HHPrevisto, s.CustoPrevisto, s.Abrangencia, s.Documentos, s.Justificativa)).ToList());

    // ----------------------------------------------------------------------------------------- apropriação ----
    private sealed record DisciplineEvaluation(IReadOnlyDictionary<int, DisciplinePackagePreview> Plans, IReadOnlyList<DisciplineRowAssignment> Assignments,
        IReadOnlyList<DisciplineAppropriationLine> Lines, IReadOnlyDictionary<int, List<DisciplinePendingHours>> Pending,
        IReadOnlyDictionary<int, IReadOnlyList<StoredPackageDiscipline>> Stored);

    private static readonly DisciplineEvaluation EmptyDisciplineEvaluation = new(new Dictionary<int, DisciplinePackagePreview>(),
        Array.Empty<DisciplineRowAssignment>(), Array.Empty<DisciplineAppropriationLine>(), new Dictionary<int, List<DisciplinePendingHours>>(),
        new Dictionary<int, IReadOnlyList<StoredPackageDiscipline>>());

    /// <summary>
    /// Rateio vigente (LDP atual) + apropriação das linhas do Timesheet NÃO atribuídas a pacotes Por Documento. O Timesheet nunca é alterado:
    /// bruto = soma original; apropriado = até o previsto de cada documento; excedente = diferença, sempre exposta.
    /// </summary>
    private async Task<DisciplineEvaluation> EvaluateDisciplinesAsync(string projectId, IReadOnlyList<CostPackage> packages, IReadOnlyList<TimesheetWork> rows,
        IEnumerable<int> candidateRows, PackageContext context, CancellationToken ct)
    {
        var disciplinePackages = packages.Where(p => p.Ativo && PackageModel.IsDiscipline(p.ModeloCadastro)).ToList();
        if (disciplinePackages.Count == 0 || !await DisciplineSchemaAsync(ct)) return EmptyDisciplineEvaluation;
        var storedAll = (await StoredDisciplinesAsync(projectId, null, ct)).GroupBy(s => s.PackageId).ToDictionary(g => g.Key, g => (IReadOnlyList<StoredPackageDiscipline>)g.ToList());
        var ldp = await LoadLdpDocumentsAsync(projectId, context, ct);
        var label = CoordinationDisciplineResolver.SemDisciplinaLabel;
        var plans = disciplinePackages.ToDictionary(p => p.Id, p => PackageDisciplineRules.Preview(
            InputFrom(p, storedAll.GetValueOrDefault(p.Id) ?? Array.Empty<StoredPackageDiscipline>()), ldp, label));
        var aliases = await PackageAliasesAsync(projectId, ct);
        var matchers = disciplinePackages.Select(p => new DisciplinePackageMatcher(p.Id, PackageFallback(p),
            aliases.TryGetValue(p.Id, out var a) ? a : new List<(string, string)>(),
            plans[p.Id].Disciplinas.GroupBy(d => d.Disciplina).ToDictionary(g => g.Key,
                g => (IReadOnlySet<string>)g.First().Documentos.Select(x => Normalize(x.DocumentoCodigo)).ToHashSet(StringComparer.Ordinal)))).ToList();
        var ldpCodes = ldp.Select(d => d.Codigo).ToHashSet(StringComparer.Ordinal);
        string? Hint(TimesheetWork row)
        {
            if (!string.IsNullOrWhiteSpace(row.Doc3))
            {
                var (_, discipline, diagnostic) = DisciplineOfDoc3(CoordinationDisciplineResolver.Normalize(row.Doc3), context);
                if (diagnostic is null) return discipline;
            }
            return string.IsNullOrWhiteSpace(row.Disciplina) ? null : row.Disciplina;
        }
        var assignments = PackageDisciplineRules.AssignRows(rows, candidateRows, matchers, ldpCodes, Hint);
        var statusByCode = ldp.ToDictionary(d => d.Codigo, StringComparer.Ordinal);
        var lines = new List<DisciplineAppropriationLine>();
        var pending = disciplinePackages.ToDictionary(p => p.Id, _ => new List<DisciplinePendingHours>());
        foreach (var package in disciplinePackages)
        {
            var mine = assignments.Where(x => x.PackageId == package.Id).ToList();
            foreach (var plan in plans[package.Id].Disciplinas)
            {
                var direct = mine.Where(x => x.Kind == DisciplineRowKind.Direto && x.Disciplina == plan.Disciplina).ToList();
                var undocumented = mine.Where(x => x.Kind == DisciplineRowKind.SemDocumento && x.Disciplina == plan.Disciplina).ToList();
                if (!plan.Valido || plan.Documentos.Count == 0)
                {
                    var stuck = direct.Concat(undocumented).ToList();
                    if (stuck.Count > 0) pending[package.Id].Add(new("RATEIO_INVALIDO", plan.Disciplina, null, stuck.Sum(x => Math.Max(0m, rows[x.RowIndex].Hours)), stuck.Count,
                        "Disciplina sem rateio válido na LDP atual (" + string.Join(" ", plan.Erros) + "). Horas preservadas, não apropriadas."));
                    continue;
                }
                var directHours = direct.GroupBy(x => x.DocumentoCodigo!).ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, rows[x.RowIndex].Hours)), StringComparer.Ordinal);
                var appropriation = PackageDisciplineRules.Appropriate(plan.Documentos.Select(d => (d.DocumentoCodigo, d.HHPrevisto)).ToList(), directHours,
                    undocumented.Sum(x => Math.Max(0m, rows[x.RowIndex].Hours)));
                foreach (var (doc, share) in appropriation.Zip(plan.Documentos))
                {
                    statusByCode.TryGetValue(doc.DocumentoCodigo, out var info);
                    // Regras financeiras EXISTENTES: custo proporcional às HH (CostAllocationRules.Recognize) e custo liberado pelo peso do status.
                    var disciplineMoney = CostAllocationRules.Recognize(appropriation.Sum(d=>d.HHBruto), plan.HHPrevisto, plan.CustoPrevisto, null);
                    var grossTotal=appropriation.Sum(d=>d.HHBruto);
                    var fraction=grossTotal>0?doc.HHBruto/grossTotal:0m;
                    var money=(TheoreticalCost:disciplineMoney.TheoreticalCost*fraction,RecognizedCost:disciplineMoney.RecognizedCost*fraction,ExcessCost:disciplineMoney.ExcessCost*fraction);
                    var released = PackageAllocationRules.Recognize(0, doc.HHPrevisto, share.CustoPrevisto, info?.StatusPercentual, false).Cost;
                    lines.Add(new(package.Id, plan.Disciplina, doc.DocumentoCodigo, info?.Status, info?.StatusPercentual, doc.HHPrevisto, doc.HHDireto, doc.HHRateado,
                        doc.HHBruto, doc.HHApropriado, doc.HHExcedente, doc.HHSaldo, share.CustoPrevisto, money.TheoreticalCost, money.RecognizedCost, money.ExcessCost, released));
                }
            }
            foreach (var group in mine.Where(x => x.Kind == DisciplineRowKind.ForaDaAbrangencia).GroupBy(x => x.DocumentoCodigo))
                pending[package.Id].Add(new("FORA_DA_ABRANGENCIA", statusByCode.TryGetValue(group.Key!, out var d) ? d.Disciplina : null, group.Key,
                    group.Sum(x => Math.Max(0m, rows[x.RowIndex].Hours)), group.Count(), "Documento da LDP fora das disciplinas/abrangência do pacote: não apropriado."));
            var undetermined = mine.Where(x => x.Kind == DisciplineRowKind.DisciplinaIndefinida).ToList();
            if (undetermined.Count > 0)
                pending[package.Id].Add(new("DISCIPLINA_INDEFINIDA", null, null, undetermined.Sum(x => Math.Max(0m, rows[x.RowIndex].Hours)), undetermined.Count,
                    "Lançamento sem documento e sem disciplina identificável num pacote com várias disciplinas: tratar individualmente."));
        }
        return new(plans, assignments, lines, pending, storedAll);
    }

    private static CostRecognitionLine RecognitionLine(DisciplineAppropriationLine x) =>
        new(x.PackageId, null, x.DocumentoCodigo, x.Disciplina, x.HHBruto, x.HHPrevisto, x.HHApropriado, x.HHExcedente, x.CustoPrevisto, x.CustoLiberado,
            x.CustoExcedente, false, null, x.DocumentStatus, x.StatusPercentual, x.HHPrevisto, x.CustoPrevisto, x.CustoPrevisto, false,
            x.HHExcedente > 0 ? "EXCEDENTE_DISCIPLINAR: parcela acima do teto da disciplina." : x.HHBruto>x.HHPrevisto ? "ALERTA_DOCUMENTAL: concentração acima do rateio, reconhecida dentro do teto disciplinar." : null);

    private async Task<DisciplinePackageDetail> DetailFromAsync(string projectId, CostPackage package, CancellationToken ct)
    {
        var context = await LoadPackageContextAsync(projectId, ct);
        var packages = await PackagesAsync(projectId, ct);
        var rows = await TimesheetRowsAsync(projectId, ct);
        var documentCandidates = await DocumentPackageCandidatesAsync(projectId, packages, ct);
        var assigned = CostTimesheetMatchingRules.AssignPackages(rows, documentCandidates);
        var free = assigned.Where(a => a.PackageId is null && !a.Ambiguous && !a.PackageNameOnly).Select(a => a.RowIndex);
        var evaluation = await EvaluateDisciplinesAsync(projectId, packages, rows, free, context, ct);
        var stored = evaluation.Stored.GetValueOrDefault(package.Id) ?? await StoredDisciplinesAsync(projectId, package.Id, ct);
        var plan = evaluation.Plans.TryGetValue(package.Id, out var current) ? current
            : PackageDisciplineRules.Preview(InputFrom(package, stored), await LoadLdpDocumentsAsync(projectId, context, ct), CoordinationDisciplineResolver.SemDisciplinaLabel);
        var lines = evaluation.Lines.Where(x => x.PackageId == package.Id).ToList();
        var pending = evaluation.Pending.GetValueOrDefault(package.Id) ?? new List<DisciplinePendingHours>();
        var alerts = lines.Where(x => x.HHExcedente > 0 || x.HHBruto > x.HHPrevisto).Select(x => new DisciplineExcessAlert(projectId, package.Id, package.NomeReferencia, package.AliasTimesheet,
            x.Disciplina, x.DocumentoCodigo, x.HHPrevisto, x.HHBruto, x.HHApropriado, x.HHExcedente, x.HHExcedente>0 ? "EXCEDENTE_DISCIPLINAR_NAO_APROPRIADO" : "ALERTA_DOCUMENTAL_APROPRIADO")).ToList();
        var totals = new DisciplineTotals(lines.Sum(x => x.HHPrevisto), lines.Sum(x => x.HHBruto), lines.Sum(x => x.HHApropriado), lines.Sum(x => x.HHExcedente),
            Math.Max(0,lines.Sum(x=>x.HHPrevisto)-lines.Sum(x=>x.HHApropriado)), lines.Sum(x => x.CustoPrevisto), lines.Sum(x => x.CustoBruto), lines.Sum(x => x.CustoApropriado), lines.Sum(x => x.CustoExcedente),
            lines.Sum(x => x.CustoLiberado), pending.Sum(x => x.Horas));
        var actual=await new PmoActualFactsProvider(configuration,new CoordinationStandardRepository(configuration,Microsoft.Extensions.Logging.Abstractions.NullLogger<CoordinationStandardRepository>.Instance)).ReadAsync(projectId,ct);
        var historical=actual.HistoricalMeasurement;
        var packageHistorical=historical is null?null:historical with { Disciplines=historical.Disciplines.Where(d=>d.PackageId==package.Id).ToArray(),Documents=historical.Documents.Where(d=>d.PackageId==package.Id).ToArray() };
        return new(package, stored, plan, lines, pending, alerts, totals) { HistoricalMeasurement=packageHistorical };
    }

    public async Task<DisciplinePackageDetail> DisciplinePackageDetailAsync(string projectId, int packageId, CancellationToken ct)
    {
        var package = (await PackagesAsync(projectId, ct)).SingleOrDefault(x => x.Id == packageId) ?? throw new ArgumentException("Pacote não pertence ao projeto atual.");
        if (!PackageModel.IsDiscipline(package.ModeloCadastro)) throw new InvalidOperationException("Este pacote é Por Documento.");
        return await DetailFromAsync(projectId, package, ct);
    }

    private async Task<CostPackageAllocation> DisciplineAllocationAsync(string projectId, CostPackage package, CancellationToken ct)
    {
        var context = await LoadPackageContextAsync(projectId, ct);
        var stored = await StoredDisciplinesAsync(projectId, package.Id, ct);
        var ldp = await LoadLdpDocumentsAsync(projectId, context, ct);
        var plan = PackageDisciplineRules.Preview(InputFrom(package, stored), ldp, CoordinationDisciplineResolver.SemDisciplinaLabel);
        var byCode = ldp.ToDictionary(d => d.Codigo, StringComparer.Ordinal);
        var docs = plan.Disciplinas.SelectMany(d => d.Documentos.Select(s =>
        {
            byCode.TryGetValue(s.DocumentoCodigo, out var info);
            var released = PackageAllocationRules.Recognize(0, s.HHPrevisto, s.CustoPrevisto, info?.StatusPercentual, false).Cost;
            return new CostDocumentAllocation(0, s.DocumentoCodigo, d.Disciplina, s.HHPrevisto, "RATEIO", s.CustoPrevisto, "RATEIO", info?.Doc3, info?.Status,
                info?.StatusPercentual, s.HHPrevisto, s.CustoPrevisto, 0, 0, 0, 0, released, false, null);
        })).ToList();
        var error = plan.Valido ? null : string.Join(" ", plan.Erros.Concat(plan.Disciplinas.SelectMany(d => d.Erros)));
        return new(package.Id, plan.HHTotal, plan.CustoTotal, docs, error);
    }

    private async Task<IReadOnlyList<PackageDocumentMatch>> DocumentPackageCandidatesAsync(string projectId, IReadOnlyList<CostPackage> packages, CancellationToken ct)
    {
        var aliases = await PackageAliasesAsync(projectId, ct);
        var output = new List<PackageDocumentMatch>();
        foreach (var package in packages.Where(p => p.Ativo && !PackageModel.IsDiscipline(p.ModeloCadastro)))
            foreach (var d in (await RawDocumentsAsync(projectId, package.Id, ct)).Where(d => d.Ativo))
                output.Add(new(package.Id, d.DocumentoCodigo, PackageFallback(package), aliases.TryGetValue(package.Id, out var a) ? a : Array.Empty<(string, string)>()));
        return output;
    }

    /// <summary>Aliases dos pacotes Por Disciplina ativos — usado pelo custo real por documento para não contar a mesma hora como custo interno.</summary>
    internal static async Task<IReadOnlyList<(string? Fallback, IReadOnlyList<(string Text, string Type)> Aliases)>> DisciplinePackageAliasesAsync(SqlConnection c, string projectId, CancellationToken ct)
    {
        await using (var exists = new SqlCommand("SELECT CASE WHEN COL_LENGTH(N'costs.Package',N'ModeloCadastro') IS NULL THEN 0 ELSE 1 END", c))
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0) return Array.Empty<(string?, IReadOnlyList<(string, string)>)>();
        var rows = new Dictionary<int, (string? Fallback, List<(string, string)> Aliases)>();
        await using var cmd = new SqlCommand("""
            SELECT p.Id,COALESCE(NULLIF(LTRIM(RTRIM(p.AliasTimesheet)),''),p.NomeReferencia),a.TextoMatch,a.TipoMatch
            FROM costs.Package p LEFT JOIN costs.TimesheetMatchAlias a ON a.PackageId=p.Id AND a.Ativo=1
            WHERE p.Project_ID_ACC=@p AND p.Ativo=1 AND p.ModeloCadastro='DISCIPLINA'
            """, c);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            if (!rows.TryGetValue(id, out var value)) rows[id] = value = (r.IsDBNull(1) ? null : r.GetString(1), new());
            if (!r.IsDBNull(2)) value.Aliases.Add((r.GetString(2), r.GetString(3)));
        }
        return rows.Values.Select(v => (v.Fallback, (IReadOnlyList<(string, string)>)v.Aliases)).ToList();
    }

    // ------------------------------------------------------------------------------------------------ Excel ----
    private static readonly string[] DisciplineHeaders =
        { "Pacote", "Profissional (alias no Timesheet)", "Empresa", "Disciplina", "HH previsto total", "Custo previsto total", "Abrangência", "Documentos selecionados", "Observação / justificativa" };

    public Task<byte[]> DisciplineImportTemplateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var workbook = new OfficeOpenXml.ExcelPackage();
        var ws = workbook.Workbook.Worksheets.Add("Pacotes por disciplina");
        for (var i = 0; i < DisciplineHeaders.Length; i++) ws.Cells[1, i + 1].Value = DisciplineHeaders[i];
        var header = ws.Cells[1, 1, 1, DisciplineHeaders.Length];
        header.Style.Font.Bold = true;
        header.Style.Font.Color.SetColor(System.Drawing.Color.White);
        header.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(24, 35, 56));
        header.AutoFilter = true;
        ws.View.FreezePanes(2, 1);
        ws.Row(1).Height = 26;
        var widths = new[] { 30, 30, 24, 26, 18, 20, 16, 50, 46 };
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.Cells[2, 5, 501, 6].Style.Numberformat.Format = "#,##0.00";
        var scope = ws.DataValidations.AddListValidation("G2:G501");
        scope.Formula.Values.Add(PackageModel.Todos); scope.Formula.Values.Add(PackageModel.Selecionados);

        var help = workbook.Workbook.Worksheets.Add("Instruções");
        string[] lines =
        {
            "N&A CONNECT | MODELO DE IMPORTAÇÃO DE PACOTES — POR DISCIPLINA",
            "",
            "1. Projeto/OS = projeto selecionado no N&A Connect (mesmo fluxo da importação Por Documento).",
            "2. Uma linha por disciplina do pacote. Repita Pacote/Profissional/Empresa em todas as linhas do mesmo pacote.",
            "3. Disciplina = nome da Norma de Coordenação (ex.: Processo). Documentos SEM DISCIPLINA não podem ser usados.",
            "4. HH previsto total e Custo previsto total são da DISCIPLINA inteira (números >= 0). O sistema rateia igualmente entre os documentos elegíveis.",
            "5. Abrangência TODOS = todos os documentos ativos da disciplina na LDP (novos entram, cancelados saem). SELECIONADOS = só os listados.",
            "6. Documentos selecionados: números separados por ponto e vírgula, apenas quando Abrangência = SELECIONADOS.",
            "7. NÃO informe HH ou custo realizados: eles vêm do Timesheet e das regras financeiras oficiais.",
            "8. A divergência entre o HH do pacote e o Planned_HH da LDP é exibida na prévia; ela não bloqueia e a LDP não é alterada. Justifique na última coluna.",
            "9. Importe, confira a prévia e só depois confirme. A gravação é transacional (tudo ou nada).",
            "",
            "EXEMPLO ILUSTRATIVO — NÃO É IMPORTADO",
        };
        for (var i = 0; i < lines.Length; i++) help.Cells[i + 1, 1].Value = lines[i];
        help.Cells["A1"].Style.Font.Bold = true; help.Cells["A1"].Style.Font.Size = 14;
        var exampleRow = lines.Length + 1;
        for (var i = 0; i < DisciplineHeaders.Length; i++) help.Cells[exampleRow, i + 1].Value = DisciplineHeaders[i];
        help.Cells[exampleRow, 1, exampleRow, DisciplineHeaders.Length].Style.Font.Bold = true;
        object[][] examples =
        {
            new object[] { "PACOTE EXEMPLO", "NOME NO TIMESHEET", "EMPRESA EXEMPLO", "Processo", 1000, 150000, PackageModel.Todos, "", "Total conforme proposta." },
            new object[] { "PACOTE EXEMPLO", "NOME NO TIMESHEET", "EMPRESA EXEMPLO", "Mecânica", 120, 18000, PackageModel.Selecionados, "EXEMPLO-000-ME0-AT-001; EXEMPLO-000-ME0-AT-002", "" },
        };
        for (var r = 0; r < examples.Length; r++) for (var col = 0; col < examples[r].Length; col++) help.Cells[exampleRow + 1 + r, col + 1].Value = examples[r][col];
        help.Column(1).Width = 40;
        for (var i = 2; i <= DisciplineHeaders.Length; i++) help.Column(i).Width = 24;
        return Task.FromResult(workbook.GetAsByteArray());
    }

    private static async Task<IReadOnlyList<DisciplineImportRow>> ReadDisciplineExcelAsync(Stream file, CancellationToken ct)
    {
        if (!file.CanRead || file.Length <= 0 || file.Length > 10 * 1024 * 1024) throw new InvalidOperationException("Arquivo Excel inválido ou excede 10 MB.");
        using var ms = new MemoryStream(); await file.CopyToAsync(ms, ct);
        using var workbook = new OfficeOpenXml.ExcelPackage(ms);
        var ws = workbook.Workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("Planilha vazia.");
        if (ws.Dimension is null) throw new InvalidOperationException("Planilha vazia.");
        var headers = new Dictionary<string, int>();
        for (var col = 1; col <= ws.Dimension.End.Column; col++) { var h = Header(ws.Cells[1, col].Text); if (h.Length > 0) headers[h] = col; }
        int Col(params string[] names) => names.Select(x => headers.TryGetValue(x, out var col) ? col : 0).FirstOrDefault(x => x > 0);
        var packageCol = Col("PACOTE", "NOMEPACOTE", "NOMEDOPACOTE");
        var disciplineCol = Col("DISCIPLINA");
        var hoursCol = Col("HHPREVISTOTOTAL", "HHPREVISTO", "HHTOTAL", "HHPREVISTODADISCIPLINA");
        var costCol = Col("CUSTOPREVISTOTOTAL", "CUSTOPREVISTO", "CUSTOTOTAL", "CUSTOPREVISTODADISCIPLINA");
        if (packageCol == 0 || disciplineCol == 0 || hoursCol == 0)
            throw new InvalidOperationException("Cabeçalhos obrigatórios: Pacote, Disciplina e HH previsto total. Use o modelo Por Disciplina.");
        if (Col("NUMERODODOCUMENTO", "DOCUMENTOCODIGO", "NUMERODOCUMENTO") > 0 && Col("ABRANGENCIA") == 0)
            throw new InvalidOperationException("Esta planilha é do modelo Por Documento. Selecione \"Por Documento\" para importá-la.");
        var professionalCol = Col("PROFISSIONALALIASNOTIMESHEET", "PROFISSIONAL", "ALIASTIMESHEET", "PROFISSIONALPACOTE");
        var companyCol = Col("EMPRESA");
        var scopeCol = Col("ABRANGENCIA", "ABRANGENCIADOSDOCUMENTOS");
        var docsCol = Col("DOCUMENTOSSELECIONADOS", "DOCUMENTOS");
        var noteCol = Col("OBSERVACAOJUSTIFICATIVA", "JUSTIFICATIVA", "OBSERVACAO");
        if (Col("HHREALIZADO", "HHREALIZADA", "CUSTOREALIZADO") > 0)
            throw new InvalidOperationException("HH/custo realizados não são importados: eles vêm do Timesheet. Remova essas colunas.");
        var rows = new List<DisciplineImportRow>();
        for (var row = 2; row <= ws.Dimension.End.Row; row++)
        {
            var package = ws.Cells[row, packageCol].Text.Trim(); var discipline = ws.Cells[row, disciplineCol].Text.Trim();
            if (package.Length == 0 && discipline.Length == 0) continue;
            string? error = null; decimal? hours = null, cost = null;
            try { hours = Number(ws, row, hoursCol); cost = Number(ws, row, costCol); }
            catch (FormatException) { error = "Valor numérico inválido ou negativo."; }
            var rawScope = Text(ws, row, scopeCol);
            var scope = PackageDisciplineRules.NormalizeScope(rawScope);
            if (!string.IsNullOrWhiteSpace(rawScope) && !new[] { PackageModel.Todos, PackageModel.Selecionados }.Contains(rawScope.Trim(), StringComparer.OrdinalIgnoreCase))
                error ??= "Abrangência deve ser TODOS ou SELECIONADOS.";
            var docs = (Text(ws, row, docsCol) ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (scope == PackageModel.Todos && docs.Count > 0) error ??= "Documentos selecionados exigem Abrangência = SELECIONADOS.";
            if (package.Length == 0 || discipline.Length == 0) error ??= "Pacote e Disciplina são obrigatórios.";
            if (hours is null) error ??= "HH previsto total é obrigatório.";
            rows.Add(new(row, package, NullIfBlank(Text(ws, row, professionalCol)), NullIfBlank(Text(ws, row, companyCol)), discipline, hours, cost ?? 0m, scope, docs,
                NullIfBlank(Text(ws, row, noteCol)), error));
        }
        if (rows.Count == 0) throw new InvalidOperationException("Nenhuma linha preenchida na planilha.");
        return rows;
    }

    private static IEnumerable<(string Name, List<DisciplineImportRow> Rows, DisciplinePackageInput Input, List<string> Errors)> GroupImport(IReadOnlyList<DisciplineImportRow> rows)
    {
        foreach (var g in rows.Where(x => x.Pacote.Length > 0).GroupBy(x => x.Pacote.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var list = g.ToList(); var errors = new List<string>();
            if (list.Select(x => x.Profissional ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) errors.Add("Profissional diferente entre linhas do mesmo pacote.");
            if (list.Select(x => x.Empresa ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) errors.Add("Empresa diferente entre linhas do mesmo pacote.");
            var input = new DisciplinePackageInput(list[0].Empresa, g.Key, list[0].Profissional, null,
                list.Select(x => new PackageDisciplineInput(x.Disciplina, x.HHPrevisto ?? 0m, x.CustoPrevisto ?? 0m, x.Abrangencia, x.Documentos, x.Justificativa)).ToList());
            yield return (g.Key, list, input, errors);
        }
    }

    public async Task<DisciplineImportPreview> PreviewDisciplineExcelAsync(string projectId, Stream file, CancellationToken ct)
    {
        var rows = await ReadDisciplineExcelAsync(file, ct);
        var context = await LoadPackageContextAsync(projectId, ct);
        var ldp = await LoadLdpDocumentsAsync(projectId, context, ct);
        var existing = (await PackagesAsync(projectId, ct)).Where(p => p.Ativo).Select(p => p.NomeReferencia.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packages = GroupImport(rows).Select(x =>
        {
            if (existing.Contains(x.Name)) x.Errors.Add("Já existe um pacote ativo com esse nome neste projeto (importação não altera pacotes existentes).");
            var preview = PackageDisciplineRules.Preview(x.Input, ldp, CoordinationDisciplineResolver.SemDisciplinaLabel);
            return new DisciplineImportPackage(x.Name, x.Input.AliasTimesheet, x.Input.Empresa, preview, x.Errors);
        }).ToList();
        var invalid = rows.Count(x => x.Erro is not null);
        return new(rows, packages, invalid == 0 && packages.Count > 0 && packages.All(p => p.Erros.Count == 0 && p.Previa.Valido), invalid);
    }

    public async Task<CostImportCommitResult> CommitDisciplineExcelAsync(string projectId, Stream file, string? author, CancellationToken ct)
    {
        await RequireDisciplineSchemaAsync(ct);
        using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); buffer.Position = 0;
        var preview = await PreviewDisciplineExcelAsync(projectId, buffer, ct);
        if (!preview.Valido) throw new InvalidOperationException("A importação possui linhas ou rateios inválidos. Corrija a planilha e gere a prévia novamente.");
        buffer.Position = 0;
        var groups = GroupImport(await ReadDisciplineExcelAsync(buffer, ct)).ToList();
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var disciplines = 0;
            foreach (var (group, package) in groups.Zip(preview.Pacotes))
            {
                await InsertDisciplinePackageAsync(c, tx, projectId, group.Input, package.Previa, author, ct);
                disciplines += package.Previa.Disciplinas.Count;
            }
            await tx.CommitAsync(ct);
            return new(groups.Count, disciplines);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }
}
