using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public sealed record ProgressSettingItem(string DocumentStatus, decimal Percentual, bool Ativo);
public sealed record ProgressSettingsRequest(string ProjectId, IReadOnlyList<ProgressSettingItem> Items);
public sealed record ProgressSettingResult(string DocumentStatus, int DocumentCount, long? ConfigId, decimal? Percentual, bool Ativo, bool IsConfigured);
public sealed record ProgressPreview(string ProjectId, string DocumentCode, string? DocumentStatus, decimal? OriginalPercent, decimal? ConfiguredPercent, long? ConfigId, bool CanConfirm, string? BlockingReason);
public sealed record ProgressDecision(Guid Id, string ProjectId, string DocumentCode, string? DocumentStatus, long? ConfigId, decimal? OriginalPercent, decimal ValidatedPercent, string Status, string ConfirmedBy, DateTime ConfirmedAtUtc);

/// <summary>Configuração salva do projeto (Document_Status_Normalizado pode vir de versões antigas: sempre recanonicalizada na leitura).</summary>
public sealed record ProgressConfig(long Id, string StoredStatus, string DisplayStatus, decimal Percentual, bool Ativo);

/// <summary>Resultado da resolução de configuração de um Document_Status (null = sem configuração ativa).</summary>
public sealed record ProgressConfigMatch(ProgressConfig? Config, bool Conflict);

/// <summary>Camada gerencial opt-in: lê somente a LDP local atual e configurações persistidas.</summary>
public sealed class InformationQualityProgressService(IConfiguration configuration)
{
    public const string MigrationName = "Migrations/Qualidade/04_auditoria_avanco_status.sql";
    private const string MigrationPendingMessage = "Avanço físico por Document Status indisponível: a migração " + MigrationName + " ainda não foi aplicada neste banco.";

    public async Task<IReadOnlyList<ProgressSettingResult>> SettingsAsync(string projectId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);
        var sql = $"""
            SELECT MAX(NULLIF(LTRIM(RTRIM(Document_Status)), '')) AS Exibicao, COUNT_BIG(*) AS Quantidade
            FROM {PmoLdpSnapshot.Tabela}
            WHERE NULLIF(LTRIM(RTRIM(NA_Document_Number)), '') IS NOT NULL
            GROUP BY UPPER(LTRIM(RTRIM(Document_Status)))
            {PmoLdpSnapshot.OpcaoPlano};
            """;
        var observed = new List<(string Status, int Count)>();
        await using (var command = Command(connection, null, sql, projectId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                observed.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), checked((int)reader.GetInt64(1))));
        var configs = await LoadConfigsAsync(connection, null, projectId, ct) ?? [];
        return BuildSettings(observed, configs);
    }

    /// <summary>Status observados na LDP + configurações salvas, casados pela chave canônica. Configuração sem documento observado também aparece.</summary>
    internal static IReadOnlyList<ProgressSettingResult> BuildSettings(IReadOnlyList<(string Status, int Count)> observed, IReadOnlyList<ProgressConfig> configs)
    {
        var result = new List<ProgressSettingResult>();
        var usados = new HashSet<long>();
        foreach (var (status, count) in observed)
        {
            var match = Resolve(status, configs, includeInactive: true);
            if (match.Config is { } c) usados.Add(c.Id);
            result.Add(new(status, count, match.Config?.Id, match.Config?.Percentual, match.Config?.Ativo ?? false, match.Config is not null));
        }
        foreach (var c in configs.Where(c => !usados.Contains(c.Id)))
            result.Add(new(c.DisplayStatus, 0, c.Id, c.Percentual, c.Ativo, true));
        return result.OrderBy(x => x.DocumentStatus, StringComparer.CurrentCulture).ToList();
    }

    public async Task SaveSettingsAsync(ProgressSettingsRequest request, string actor, CancellationToken ct)
    {
        if (request.Items.Any(x => x.Percentual is < 0 or > 100)) throw new InvalidOperationException("Percentual deve estar entre 0 e 100.");
        var repetido = request.Items.GroupBy(x => NormalizeStatus(x.DocumentStatus)).FirstOrDefault(g => g.Key.Length > 0 && g.Count() > 1);
        if (repetido is not null)
            throw new InvalidOperationException($"Document Status equivalentes no mesmo salvamento: {string.Join(", ", repetido.Select(x => x.DocumentStatus.Trim()))}.");
        await using var connection = new SqlConnection(ConnectionString()); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var existentes = await LoadConfigsAsync(connection, tx, request.ProjectId, ct) ?? throw new InvalidOperationException(MigrationPendingMessage);
            const string update = """
                UPDATE qualidade.DocumentStatusAvancoConfig SET Document_Status_Normalizado=@normalized, Document_Status_Exibicao=@display, Percentual=@percentual, Ativo=@ativo, AtualizadoPor=@user, AtualizadoEm=SYSUTCDATETIME()
                WHERE Id=@id AND Project_ID_ACC=@projectId;
                """;
            const string insert = """
                INSERT INTO qualidade.DocumentStatusAvancoConfig(Project_ID_ACC,Document_Status_Normalizado,Document_Status_Exibicao,Percentual,Ativo,AtualizadoPor,AtualizadoEm)
                VALUES(@projectId,@normalized,@display,@percentual,@ativo,@user,SYSUTCDATETIME());
                """;
            foreach (var item in request.Items)
            {
                var normalized = NormalizeStatus(item.DocumentStatus);
                if (normalized.Length == 0) throw new InvalidOperationException("Document status é obrigatório.");
                // Mesmo significado canônico já salvo (inclusive grafia antiga) → atualiza ESSA linha; nunca cria configuração paralela.
                var atual = existentes.Where(c => NormalizeStatus(c.StoredStatus) == normalized).OrderBy(c => c.Id).ToList();
                if (atual.Count > 1) throw new InvalidOperationException($"Há mais de uma configuração salva equivalente a \"{item.DocumentStatus.Trim()}\". Revise a configuração do projeto.");
                await using var command = Command(connection, tx, atual.Count == 1 ? update : insert, request.ProjectId);
                if (atual.Count == 1) command.Parameters.Add("@id", SqlDbType.BigInt).Value = atual[0].Id;
                command.Parameters.Add("@normalized", SqlDbType.NVarChar, 255).Value = normalized;
                command.Parameters.Add("@display", SqlDbType.NVarChar, 255).Value = item.DocumentStatus.Trim();
                command.Parameters.Add("@percentual", SqlDbType.Decimal).ConfigureDecimal(9, 4).Value = item.Percentual;
                command.Parameters.Add("@ativo", SqlDbType.Bit).Value = item.Ativo;
                command.Parameters.Add("@user", SqlDbType.NVarChar, 255).Value = actor;
                await command.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<ProgressPreview> PreviewAsync(string projectId, string documentCode, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString()); await connection.OpenAsync(ct);
        return await BuildPreviewAsync(connection, null, projectId, NormalizeDocument(documentCode), ct);
    }

    public async Task<ProgressDecision> ConfirmAsync(string projectId, string documentCode, string actor, CancellationToken ct)
    {
        var code = NormalizeDocument(documentCode);
        await using var connection = new SqlConnection(ConnectionString()); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var existing = await ActiveDecisionAsync(connection, tx, projectId, code, ct);
            if (existing is not null) { await tx.CommitAsync(ct); return existing; }
            var preview = await BuildPreviewAsync(connection, tx, projectId, code, ct);
            if (!preview.CanConfirm) throw new InvalidOperationException(preview.BlockingReason ?? "Avanço não pode ser confirmado.");
            var id = Guid.NewGuid();
            const string insert = """
                INSERT INTO qualidade.AuditoriaAvancoDocumento(Id,Project_ID_ACC,Documento,Document_Status_Origem,Config_Id,Percentual_Original,Percentual_Validado,Status_Decisao,ConfirmadoPor,ConfirmadoEm,Ativo,Motivo)
                VALUES(@id,@projectId,@documento,@status,@configId,@original,@validated,N'CONFIRMADO',@user,SYSUTCDATETIME(),1,N'Validação explícita de avanço físico gerencial por status.');
                """;
            await using var command = Command(connection, tx, insert, projectId);
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value=id;
            command.Parameters.Add("@documento", SqlDbType.NVarChar,255).Value=code;
            command.Parameters.Add("@status", SqlDbType.NVarChar,255).Value=(object?)preview.DocumentStatus ?? DBNull.Value;
            command.Parameters.Add("@configId", SqlDbType.BigInt).Value=preview.ConfigId!.Value;
            command.Parameters.Add("@original", SqlDbType.Decimal).ConfigureDecimal(9,4).Value=(object?)preview.OriginalPercent ?? DBNull.Value;
            command.Parameters.Add("@validated", SqlDbType.Decimal).ConfigureDecimal(9,4).Value=preview.ConfiguredPercent!.Value;
            command.Parameters.Add("@user", SqlDbType.NVarChar,255).Value=actor.Trim();
            await command.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
            return new(id,projectId,code,preview.DocumentStatus,preview.ConfigId,preview.OriginalPercent,preview.ConfiguredPercent.Value,"CONFIRMADO",actor,DateTime.UtcNow);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<IReadOnlyList<ProgressDecision>> DecisionsAsync(string projectId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString()); await connection.OpenAsync(ct);
        if (!await TablesAvailableAsync(connection, ct)) return [];
        return await ReadDecisionsAsync(connection, projectId, ct);
    }

    private static async Task<List<ProgressDecision>> ReadDecisionsAsync(SqlConnection connection, string projectId, CancellationToken ct)
    {
        const string sql="SELECT Id,Project_ID_ACC,Documento,Document_Status_Origem,Config_Id,Percentual_Original,Percentual_Validado,Status_Decisao,ConfirmadoPor,ConfirmadoEm FROM qualidade.AuditoriaAvancoDocumento WHERE Project_ID_ACC=@projectId AND Ativo=1 ORDER BY ConfirmadoEm DESC;";
        await using var cmd=Command(connection,null,sql,projectId); var list=new List<ProgressDecision>(); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetInt64(4),r.IsDBNull(5)?null:r.GetDecimal(5),r.GetDecimal(6),r.GetString(7),r.GetString(8),DateTime.SpecifyKind(r.GetDateTime(9),DateTimeKind.Utc)));
        return list;
    }

    internal async Task ApplyAuditFindingsAsync(string projectId, IReadOnlyList<InformationQualityLdpRow> rows, List<InformationQualityFinding> findings, CancellationToken ct)
    {
        await using var c=new SqlConnection(ConnectionString()); await c.OpenAsync(ct);
        // Sem as tabelas da migração a auditoria continua funcionando; o motivo fica explícito (nada é escondido nem inventado).
        if (!await TablesAvailableAsync(c, ct))
        {
            findings.Add(new("AVANCO_CONFIGURACAO_INDISPONIVEL", "INFORMATIVO", "AVANCO", "AVANCO_CONFIGURACAO_INDISPONIVEL", "Avanço físico por status indisponível",
                MigrationPendingMessage, null, null, null, null, false, [new("Migração", MigrationName)]));
            return;
        }
        var configs = await LoadConfigsAsync(c, null, projectId, ct) ?? [];
        var decisions = await ReadDecisionsAsync(c, projectId, ct);
        var redistributed = new HashSet<string>(StringComparer.Ordinal);
        const string redistSql = "SELECT UPPER(LTRIM(RTRIM(Documento_Origem))) FROM gestao.AuditoriaRedistribuicaoDocumento WHERE Project_ID_ACC=@projectId AND Ativo=1 AND Status=N'CONFIRMADO';";
        await using (var cmd = Command(c, null, redistSql, projectId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) if (!r.IsDBNull(0)) redistributed.Add(r.GetString(0));
        findings.AddRange(BuildProgressFindings(rows, configs, decisions, redistributed));
    }

    /// <summary>
    /// Regra da auditoria (pura): documento da LDP atual, não cancelado/retirado, não redistribuído, sem avanço real (vazio ou 0):
    /// já validado → AVANCO_STATUS_CONFIRMADO; sem configuração ativa para o Document_Status (chave canônica) → AVANCO_STATUS_SEM_CONFIGURACAO;
    /// configuração &gt; 0% → AVANCO_FISICO_AUSENTE_STATUS (ação "Simular avanço físico"); configuração 0% → nada a confirmar.
    /// </summary>
    internal static List<InformationQualityFinding> BuildProgressFindings(IReadOnlyList<InformationQualityLdpRow> rows, IReadOnlyList<ProgressConfig> configs,
        IEnumerable<ProgressDecision> decisions, IReadOnlySet<string> redistributedCodes)
    {
        var findings = new List<InformationQualityFinding>();
        var decisoes = new Dictionary<string, ProgressDecision>(StringComparer.Ordinal);
        foreach (var d in decisions) decisoes[NormalizeDocument(d.DocumentCode)] = d;
        foreach (var row in rows.GroupBy(x => NormalizeDocument(x.Code), StringComparer.Ordinal).Select(x => x.First()))
        {
            var code = NormalizeDocument(row.Code);
            if (IsCancelled(row.Situation) || IsCancelled(row.DocumentStatus) || redistributedCodes.Contains(code)) continue;
            if ((row.ActualPhysical ?? 0) > 0) continue;
            if (NormalizeStatus(row.DocumentStatus).Length == 0) continue;
            if (decisoes.TryGetValue(code, out var decision))
            {
                findings.Add(Finding(row, "AVANCO_STATUS_CONFIRMADO", "INFORMATIVO", false,
                    $"Avanço de {decision.ValidatedPercent:0.####}% validado por {decision.ConfirmedBy} em {decision.ConfirmedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}.",
                    new InformationQualityDetail("Percentual validado", $"{decision.ValidatedPercent:0.####}%")));
                continue;
            }
            var match = Resolve(row.DocumentStatus, configs);
            if (match.Config is null)
            {
                findings.Add(Finding(row, "AVANCO_STATUS_SEM_CONFIGURACAO", "INFORMATIVO", false,
                    match.Conflict
                        ? "Há configurações salvas conflitantes (percentuais diferentes) para este mesmo Document Status. Revise em Qualidade -> Configuração de Avanço Físico."
                        : "Configure o status em Qualidade -> Configuração de Avanço Físico.",
                    new InformationQualityDetail("Document_Status", row.DocumentStatus ?? "—")));
                continue;
            }
            if (RequiresPendingFinding(row.ActualPhysical, match.Config.Percentual))
                findings.Add(Finding(row, "AVANCO_FISICO_AUSENTE_STATUS", "ATENCAO", true,
                    "O documento não possui avanço físico e há percentual configurado para seu Document_Status.",
                    new("Document_Status", row.DocumentStatus ?? "—"), new("Físico atual", $"{row.ActualPhysical ?? 0:0.####}%"),
                    new("Percentual configurado", $"{match.Config.Percentual:0.####}%"), new("Configuração", match.Config.DisplayStatus)));
        }
        return findings;
    }

    /// <summary>
    /// Configuração do Document_Status pela chave CANÔNICA (DocumentStatusCanonical). Só ativas, salvo includeInactive.
    /// Mais de uma configuração equivalente com percentuais diferentes = conflito (nenhuma é escolhida).
    /// </summary>
    internal static ProgressConfigMatch Resolve(string? documentStatus, IReadOnlyList<ProgressConfig> configs, bool includeInactive = false)
    {
        var key = NormalizeStatus(documentStatus);
        if (key.Length == 0) return new(null, false);
        var candidatos = configs.Where(c => (includeInactive || c.Ativo) && NormalizeStatus(c.StoredStatus) == key).OrderBy(c => c.Id).ToList();
        if (candidatos.Count == 0) return new(null, false);
        if (candidatos.Where(c => c.Ativo).Select(c => c.Percentual).Distinct().Count() > 1) return new(null, true);
        return new(candidatos.FirstOrDefault(c => c.Ativo) ?? candidatos[0], false);
    }

    private async Task<ProgressPreview> BuildPreviewAsync(SqlConnection c, SqlTransaction? tx,string project,string code,CancellationToken ct)
    {
        var sql=$"""
            SELECT TOP(1) NULLIF(LTRIM(RTRIM(Document_Status)),''), Percent_Physical_Completed, NULLIF(LTRIM(RTRIM(Situation)), '')
            FROM {PmoLdpSnapshot.Tabela} WHERE UPPER(LTRIM(RTRIM(NA_Document_Number)))=@documentCode {PmoLdpSnapshot.OpcaoPlano};
            """;
        string? status=null; decimal? original=null; string? situation=null;
        await using(var cmd=Command(c,tx,sql,project)) { cmd.Parameters.Add("@documentCode",SqlDbType.NVarChar,255).Value=code; await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) return new(project,code,null,null,null,null,false,"Documento não pertence ao snapshot atual da LDP."); status=r.IsDBNull(0)?null:r.GetString(0); original=r.IsDBNull(1)?null:r.GetDecimal(1); situation=r.IsDBNull(2)?null:r.GetString(2); }
        if(IsCancelled(status)||IsCancelled(situation)) return new(project,code,status,original,null,null,false,"Documento cancelado ou retirado não participa de avanço físico.");
        if(await HasRedistributionAsync(c,project,code,ct,tx)) return new(project,code,status,original,null,null,false,"Documento possui redistribuição ativa.");
        if((original??0)>0) return new(project,code,status,original,null,null,false,"Documento já possui avanço físico real.");
        var configs = await LoadConfigsAsync(c, tx, project, ct);
        if (configs is null) return new(project,code,status,original,null,null,false,MigrationPendingMessage);
        var match = Resolve(status, configs);
        if (match.Conflict) return new(project,code,status,original,null,null,false,"Há configurações conflitantes para este Document Status; revise a configuração do projeto.");
        if (match.Config is null) return new(project,code,status,original,null,null,false,"Document_Status sem configuração ativa para o projeto.");
        var percent = match.Config.Percentual;
        return percent<=0?new(project,code,status,original,percent,match.Config.Id,false,"Configuração resulta em 0%; não há avanço a validar."):new(project,code,status,original,percent,match.Config.Id,true,null);
    }

    /// <summary>Configurações do projeto (ativas e inativas). null = tabela da migração ausente.</summary>
    private static async Task<List<ProgressConfig>?> LoadConfigsAsync(SqlConnection c, SqlTransaction? tx, string project, CancellationToken ct)
    {
        await using (var check = new SqlCommand("SELECT OBJECT_ID(N'qualidade.DocumentStatusAvancoConfig', N'U');", c, tx))
            if (await check.ExecuteScalarAsync(ct) is null or DBNull) return null;
        const string sql = "SELECT Id,Document_Status_Normalizado,Document_Status_Exibicao,Percentual,Ativo FROM qualidade.DocumentStatusAvancoConfig WHERE Project_ID_ACC=@projectId;";
        var list = new List<ProgressConfig>();
        await using var cmd = Command(c, tx, sql, project);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? r.GetString(1) : r.GetString(2), r.GetDecimal(3), r.GetBoolean(4)));
        return list;
    }

    private static async Task<bool> TablesAvailableAsync(SqlConnection c, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'qualidade.DocumentStatusAvancoConfig', N'U') IS NOT NULL AND OBJECT_ID(N'qualidade.AuditoriaAvancoDocumento', N'U') IS NOT NULL THEN 1 ELSE 0 END;", c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    private async Task<ProgressDecision?> ActiveDecisionAsync(SqlConnection c,SqlTransaction tx,string project,string code,CancellationToken ct) { const string sql="SELECT TOP(1) Id,Project_ID_ACC,Documento,Document_Status_Origem,Config_Id,Percentual_Original,Percentual_Validado,Status_Decisao,ConfirmadoPor,ConfirmadoEm FROM qualidade.AuditoriaAvancoDocumento WITH (UPDLOCK,HOLDLOCK) WHERE Project_ID_ACC=@projectId AND Documento=@documento AND Ativo=1;"; await using var cmd=Command(c,tx,sql,project);cmd.Parameters.Add("@documento",SqlDbType.NVarChar,255).Value=code;await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetInt64(4),r.IsDBNull(5)?null:r.GetDecimal(5),r.GetDecimal(6),r.GetString(7),r.GetString(8),DateTime.SpecifyKind(r.GetDateTime(9),DateTimeKind.Utc)):null; }
    private static async Task<bool> HasRedistributionAsync(SqlConnection c,string project,string code,CancellationToken ct,SqlTransaction? tx=null) { const string sql="SELECT TOP(1) 1 FROM gestao.AuditoriaRedistribuicaoDocumento WHERE Project_ID_ACC=@projectId AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@documento AND Ativo=1 AND Status=N'CONFIRMADO';";await using var cmd=Command(c,tx,sql,project);cmd.Parameters.Add("@documento",SqlDbType.NVarChar,255).Value=code;return (await cmd.ExecuteScalarAsync(ct)) is not null; }
    private static InformationQualityFinding Finding(InformationQualityLdpRow r,string rule,string severity,bool required,string text,params InformationQualityDetail[] details)=>new($"{rule}:{r.Code}",severity,"AVANCO",rule,rule.Replace('_',' '),text,r.Code,r.Discipline,r.ActualHours,null,required,details);
    internal static decimal? EffectivePercent(decimal? original, decimal? confirmedOverride) => (original ?? 0) > 0 ? original : confirmedOverride ?? original;
    internal static bool RequiresPendingFinding(decimal? original, decimal configured) => (original ?? 0) <= 0 && configured > 0;
    /// <summary>Chave usada para persistir e procurar a configuração: canônica (DocumentStatusCanonical), nunca só Trim/Upper.</summary>
    internal static string NormalizeStatus(string? v)=>DocumentStatusCanonical.Key(v);
    internal static bool IsCancelled(string? v)=>!string.IsNullOrWhiteSpace(v) && (v.Contains("CANCEL",StringComparison.OrdinalIgnoreCase)||v.Contains("RETIR",StringComparison.OrdinalIgnoreCase));
    private static string NormalizeDocument(string v)=>(v ?? "").Trim().ToUpperInvariant();
    private string ConnectionString()=>configuration.GetConnectionString("LocalConnection")??throw new InvalidOperationException("Conexão LocalConnection indisponível.");
    private static SqlCommand Command(SqlConnection c,SqlTransaction? tx,string sql,string project) { var x=new SqlCommand(sql,c,tx){CommandTimeout=45};x.Parameters.Add("@projectId",SqlDbType.NVarChar,100).Value=project;return x; }
}
