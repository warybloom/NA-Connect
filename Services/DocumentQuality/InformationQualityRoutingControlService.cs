using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public static class RoutingControlOperations
{
    public const string EditDePara = "EDIT_DE_PARA";
    public const string ResetDePara = "RESET_DE_PARA";
    public const string ResetRedistribution = "RESET_REDISTRIBUICAO";
    public static bool IsValid(string? value) => value is EditDePara or ResetDePara or ResetRedistribution;
}

public sealed record RoutingControlRequest(string ProjectId, string SourceDocumentCode, string Operation,
    string? DestinationDocumentCode, string Reason, string Version);
public sealed record RoutingDestination(string DocumentCode, string? Title, decimal Hours, decimal Cost);
public sealed record RoutingState(decimal SourceHours, decimal SourceCost, IReadOnlyList<RoutingDestination> Destinations);
public sealed record RoutingPreview(bool CanConfirm, string? BlockingReason, RoutingState Before, RoutingState After,
    decimal TotalHoursDelta, decimal TotalCostDelta, string Version);
public sealed record RoutingApplyResult(bool Success, string Message, string Version);
public sealed record RoutingHistory(string SourceDocumentCode, string Method, string Details, string By, DateTime AtUtc, string Reason);
public sealed record RoutingItem(string SourceDocumentCode, string? SourceTitle, string? Discipline, decimal SourceHours,
    decimal SourceCost, IReadOnlyList<string> Professionals, string Method, bool Active, string? DecisionId,
    string? DestinationDocumentCode, IReadOnlyList<RoutingDestination> Destinations, string? DecisionBy,
    DateTime? DecisionAtUtc, string? Reason, bool Conflict, string? Warning, string Version);
public sealed record RoutingControlResult(string ProjectId, DateTime GeneratedAtUtc, IReadOnlyList<RoutingItem> Items,
    IReadOnlyList<RoutingHistory> History);

public sealed class RoutingControlSchemaException(string message) : InvalidOperationException(message) { }
public sealed class RoutingControlConflictException(string message) : InvalidOperationException(message) { }

/// <summary>Central governada sobre o snapshot original. Nunca altera Timesheet, LDP, HH ou custo.</summary>
public sealed class InformationQualityRoutingControlService(IConfiguration configuration)
{
    private const string Migration = "Migrations/Qualidade/07_central_direcionamento_hh.sql";
    private static readonly HashSet<string> ProtectedInactiveIds = new(StringComparer.OrdinalIgnoreCase)
        { "d0cb6c09", "fba8e837", "2e9cf4fb", "744f9b35" };

    public async Task<RoutingControlResult> ReadAsync(string projectId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);
        var sources = await ReadSourcesAsync(connection, null, projectId, ct);
        var decisions = await ReadDecisionsAsync(connection, null, projectId, sources, ct);
        return new(projectId, DateTime.UtcNow, decisions, await ReadHistoryAsync(connection, projectId, ct));
    }

    public async Task<RoutingPreview> PreviewAsync(RoutingControlRequest request, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);
        return await BuildPreviewAsync(connection, null, Normalize(request), false, ct);
    }

    public async Task<RoutingApplyResult> ApplyAsync(RoutingControlRequest request, string actor, CancellationToken ct)
    {
        request = Normalize(request);
        // LDP/snapshot preflight intentionally precedes the local transaction: no linked/view read inside it.
        await using var connection = await OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);
        var preflight = await BuildPreviewAsync(connection, null, request, false, ct);
        if (!preflight.CanConfirm) throw new RoutingControlConflictException(preflight.BlockingReason ?? "Operação bloqueada.");

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var locked = await BuildPreviewAsync(connection, transaction, request, true, ct);
            if (!locked.CanConfirm || !FixedEquals(request.Version, locked.Version))
                throw new RoutingControlConflictException("Os dados foram alterados após a pré-visualização. Atualize e tente novamente.");

            var beforeJson = JsonSerializer.Serialize(locked.Before);
            if (request.Operation == RoutingControlOperations.EditDePara)
                await UpdateDeParaAsync(connection, transaction, request, ct);
            else if (request.Operation == RoutingControlOperations.ResetDePara)
                await ResetDeParaAsync(connection, transaction, request, ct);
            else
                await ResetRedistributionAsync(connection, transaction, request, ct);

            var after = request.Operation == RoutingControlOperations.EditDePara
                ? locked.After : new RoutingState(locked.Before.SourceHours, locked.Before.SourceCost, []);
            await InsertHistoryAsync(connection, transaction, request, actor, beforeJson, JsonSerializer.Serialize(after), ct);
            await transaction.CommitAsync(ct);
            var version = VersionOf(request.ProjectId, request.SourceDocumentCode, request.Operation, actor, DateTime.UtcNow.Ticks.ToString());
            return new(true, "Direcionamento atualizado com histórico permanente.", version);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<RoutingPreview> BuildPreviewAsync(SqlConnection connection, SqlTransaction? transaction,
        RoutingControlRequest request, bool locked, CancellationToken ct)
    {
        var sources = await ReadSourcesAsync(connection, transaction, request.ProjectId, ct, request.SourceDocumentCode, locked);
        var source = sources.GetValueOrDefault(request.SourceDocumentCode);
        var empty = new RoutingState(source?.Hours ?? 0, source?.Cost ?? 0, []);
        if (source is null) return Block("A origem não existe no Timesheet atual.", empty, request.Version);

        var decisions = await ReadRawDecisionsAsync(connection, transaction, request.ProjectId, request.SourceDocumentCode, locked, ct);
        var dePara = decisions.DePara;
        var redist = decisions.Redistribution;
        var currentVersion = VersionOf(request.ProjectId, request.SourceDocumentCode, dePara.VersionPart, redist.VersionPart,
            source.Hours.ToString("G29"), source.Cost.ToString("G29"));
        if (!FixedEquals(request.Version, currentVersion)) return Block("Os dados mudaram desde a consulta. Atualize a tela.", empty, currentVersion);
        // Quando ambos estao ativos, permitir APENAS reset explicito de um dos metodos:
        // e justamente a forma segura de resolver a duplicidade com historico.
        // Editar De/Para continua proibido ate a sobreposicao ser resolvida.
        if (dePara.Active && redist.Active && request.Operation == RoutingControlOperations.EditDePara)
            return Block("Há conflito: De/Para e redistribuição estão ativos. Resete explicitamente o método indevido antes de editar.", empty, currentVersion);

        if (request.Operation is RoutingControlOperations.EditDePara or RoutingControlOperations.ResetDePara)
        {
            if (!dePara.Exists || !dePara.Active) return Block("Não existe De/Para ativo para esta origem.", empty, currentVersion);
            var before = new RoutingState(source.Hours, source.Cost,
                [new(dePara.Destination!, dePara.DestinationTitle, source.Hours, source.Cost)]);
            if (request.Operation == RoutingControlOperations.ResetDePara)
                return Ok(before, new(source.Hours, source.Cost, []), currentVersion);

            var target = await ReadTargetAsync(connection, transaction, request.ProjectId, request.DestinationDocumentCode!, ct);
            if (target is null) return Block("Destino inexistente na LDP atual.", before, currentVersion);
            if (target.Cancelled) return Block("Destino cancelado/retirado na LDP.", before, currentVersion);
            if (!SameDiscipline(source.Discipline, target.Discipline)) return Block("Destino pertence a outra disciplina.", before, currentVersion);
            return Ok(before, new(source.Hours, source.Cost,
                [new(target.Code, target.Title, source.Hours, source.Cost)]), currentVersion);
        }

        if (!redist.Exists || !redist.Active) return Block("Não existe redistribuição ativa para esta origem.", empty, currentVersion);
        if (ProtectedInactiveIds.Any(id => redist.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)) && !redist.Active)
            return Block("Auditoria histórica protegida não pode ser reativada.", empty, currentVersion);
        var redistBefore = new RoutingState(source.Hours, source.Cost, redist.Destinations);
        return Ok(redistBefore, new(source.Hours, source.Cost, []), currentVersion);
    }

    private static RoutingPreview Ok(RoutingState before, RoutingState after, string version) =>
        new(true, null, before, after, after.Destinations.Sum(x => x.Hours) - before.Destinations.Sum(x => x.Hours),
            after.Destinations.Sum(x => x.Cost) - before.Destinations.Sum(x => x.Cost), version);
    private static RoutingPreview Block(string reason, RoutingState state, string version) => new(false, reason, state, state, 0, 0, version);

    private sealed record Source(string Code, string? Title, string? Discipline, decimal Hours, decimal Cost, string[] Professionals);
    private async Task<Dictionary<string, Source>> ReadSourcesAsync(SqlConnection c, SqlTransaction? tx, string project,
        CancellationToken ct, string? only = null, bool locked = false)
    {
        var hint = locked ? " WITH (UPDLOCK,HOLDLOCK)" : "";
        var sql = $"""
            SELECT UPPER(LTRIM(RTRIM(t.DocumentoCodigo))), MAX(NULLIF(LTRIM(RTRIM(t.Documento)),'')),
                   MAX(NULLIF(LTRIM(RTRIM(nm.Disciplina)),'')),
                   CAST(SUM(ISNULL(t.HorasTrabalhadas,0)) AS decimal(18,6)),
                   CAST(SUM(ISNULL(t.CustoCalculado,0)) AS decimal(18,6)),
                   STRING_AGG(CONVERT(nvarchar(max),NULLIF(LTRIM(RTRIM(t.NomeProfissional)),'')),N'|')
            FROM gestao.TimesheetSnapshotAtual t{hint}
            OUTER APPLY (SELECT TOP(1) pn.NormaId FROM coordenacao.ProjetoNorma pn WHERE pn.Project_ID_ACC=t.Project_ID_ACC ORDER BY pn.Data_Atualizacao DESC) pn
            LEFT JOIN coordenacao.NormaMapeamento nm ON nm.NormaId=pn.NormaId AND UPPER(LTRIM(RTRIM(nm.Sigla)))=UPPER(LTRIM(RTRIM(t.Doc3)))
            WHERE t.Project_ID_ACC=@p AND NULLIF(LTRIM(RTRIM(t.DocumentoCodigo)),'') IS NOT NULL
              AND (@only IS NULL OR UPPER(LTRIM(RTRIM(t.DocumentoCodigo)))=@only)
            GROUP BY UPPER(LTRIM(RTRIM(t.DocumentoCodigo)));
            """;
        var result = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = Command(c, tx, sql, project); cmd.Parameters.Add("@only", SqlDbType.NVarChar, 300).Value = (object?)only ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) { var code=r.GetString(0); result[code]=new(code,r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetDecimal(3),r.GetDecimal(4),(r.IsDBNull(5)?"":r.GetString(5)).Split('|',StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()); }
        return result;
    }

    private sealed record DeParaState(bool Exists,bool Active,string? Destination,string? DestinationTitle,string VersionPart);
    private sealed record RedistState(bool Exists,bool Active,string Id,IReadOnlyList<RoutingDestination> Destinations,string VersionPart);
    private async Task<(DeParaState DePara, RedistState Redistribution)> ReadRawDecisionsAsync(SqlConnection c, SqlTransaction? tx,
        string project,string source,bool locked,CancellationToken ct)
    {
        var hint=locked?" WITH (UPDLOCK,HOLDLOCK)":"";
        var dp=new DeParaState(false,false,null,null,"-");
        await using(var cmd=Command(c,tx,$"SELECT TOP(1) Documento_Destino,Ativo,CONVERT(varchar(34),RowVersion,1) FROM gestao.TimesheetDocumentoDePara_Espelho{hint} WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@s ORDER BY Ativo DESC",project)){cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=source;await using var r=await cmd.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct))dp=new(true,r.GetBoolean(1),r.IsDBNull(0)?null:r.GetString(0),null,r.GetString(2));}
        Guid rid=Guid.Empty;bool active=false;string rv="-";
        await using(var cmd=Command(c,tx,$"SELECT TOP(1) Id,Ativo,CONVERT(varchar(34),RowVersion,1) FROM gestao.AuditoriaRedistribuicaoDocumento{hint} WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@s ORDER BY Ativo DESC,ConfirmadoEm DESC",project)){cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=source;await using var r=await cmd.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){rid=r.GetGuid(0);active=r.GetBoolean(1);rv=r.GetString(2);}}
        var dest=new List<RoutingDestination>();if(rid!=Guid.Empty){await using var cmd=new SqlCommand("SELECT Documento_Destino,Titulo_Destino,HH_Redistribuida,Custo_Redistribuido FROM gestao.AuditoriaRedistribuicaoItem WHERE Redistribuicao_Id=@id ORDER BY Documento_Destino",c,tx);cmd.Parameters.Add("@id",SqlDbType.UniqueIdentifier).Value=rid;await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))dest.Add(new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.GetDecimal(2),r.GetDecimal(3)));}
        return(dp,new(rid!=Guid.Empty,active,rid.ToString(),dest,rv));
    }

    private sealed record Target(string Code,string? Title,string? Discipline,bool Cancelled);
    private async Task<Target?> ReadTargetAsync(SqlConnection c,SqlTransaction? tx,string project,string code,CancellationToken ct)
    {
        const string sql="""SELECT TOP(1) UPPER(LTRIM(RTRIM(l.NA_Document_Number))),NULLIF(LTRIM(RTRIM(l.Document_Title)),''),NULLIF(LTRIM(RTRIM(nm.Disciplina)),''),CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(l.Document_Status,'')))) IN ('DOCUMENTO RETIRADO DA LDP','CANCELADO','CANCELLED') THEN 1 ELSE 0 END FROM dbo.Ldp_Unificada l OUTER APPLY(SELECT TOP(1) pn.NormaId FROM coordenacao.ProjetoNorma pn WHERE pn.Project_ID_ACC=l.Project_ID_ACC ORDER BY pn.Data_Atualizacao DESC)pn LEFT JOIN coordenacao.NormaMapeamento nm ON nm.NormaId=pn.NormaId AND UPPER(LTRIM(RTRIM(nm.Sigla)))=UPPER(LTRIM(RTRIM(l.Discipline_Activity))) WHERE l.Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(l.NA_Document_Number)))=@code AND l.Data_Sincronizacao=(SELECT MAX(x.Data_Sincronizacao) FROM dbo.Ldp_Unificada x WHERE x.Project_ID_ACC=@p);""";
        await using var cmd=Command(c,tx,sql,project);cmd.Parameters.Add("@code",SqlDbType.NVarChar,300).Value=code;await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetInt32(3)==1):null;
    }

    private async Task<IReadOnlyList<RoutingItem>> ReadDecisionsAsync(SqlConnection c,SqlTransaction? tx,string project,Dictionary<string,Source> sources,CancellationToken ct)
    {
        var items=new List<RoutingItem>();
        foreach(var s in sources.Values){var d=await ReadRawDecisionsAsync(c,tx,project,s.Code,false,ct);var conflict=d.DePara.Active&&d.Redistribution.Active;var v=VersionOf(project,s.Code,d.DePara.VersionPart,d.Redistribution.VersionPart,s.Hours.ToString("G29"),s.Cost.ToString("G29"));if(d.DePara.Exists)items.Add(new(s.Code,s.Title,s.Discipline,s.Hours,s.Cost,s.Professionals,"DE_PARA",d.DePara.Active,null,d.DePara.Destination,d.DePara.Destination is null?[]:[new(d.DePara.Destination,d.DePara.DestinationTitle,s.Hours,s.Cost)],null,null,null,conflict,conflict?"De/Para e redistribuição ativos":null,v));if(d.Redistribution.Exists)items.Add(new(s.Code,s.Title,s.Discipline,s.Hours,s.Cost,s.Professionals,"REDISTRIBUICAO",d.Redistribution.Active,d.Redistribution.Id,null,d.Redistribution.Destinations,null,null,null,conflict,conflict?"De/Para e redistribuição ativos":null,v));if(!d.DePara.Exists&&!d.Redistribution.Exists)items.Add(new(s.Code,s.Title,s.Discipline,s.Hours,s.Cost,s.Professionals,s.Code.Contains("-AT-",StringComparison.OrdinalIgnoreCase)?"AT":"SEM_DIRECIONAMENTO",false,null,null,[],null,null,null,false,null,v));}
        return items;
    }

    private async Task<IReadOnlyList<RoutingHistory>> ReadHistoryAsync(SqlConnection c,string project,CancellationToken ct){var x=new List<RoutingHistory>();await using var cmd=Command(c,null,"SELECT Documento_Origem,Metodo,CONCAT(Operacao,N': ',AntesJson,N' -> ',DepoisJson),AlteradoPor,AlteradoEm,Motivo FROM qualidade.DirecionamentoHHAuditoria WHERE Project_ID_ACC=@p ORDER BY AlteradoEm DESC",project);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))x.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTime.SpecifyKind(r.GetDateTime(4),DateTimeKind.Utc),r.GetString(5)));return x;}
    private static async Task UpdateDeParaAsync(SqlConnection c,SqlTransaction tx,RoutingControlRequest q,CancellationToken ct){await using var cmd=Command(c,tx,"UPDATE gestao.TimesheetDocumentoDePara_Espelho SET Documento_Destino=@d,Observacao=@m WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@s AND Ativo=1; IF @@ROWCOUNT<>1 THROW 54101,N'Conflito ao editar De/Para.',1;",q.ProjectId);cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=q.SourceDocumentCode;cmd.Parameters.Add("@d",SqlDbType.NVarChar,300).Value=q.DestinationDocumentCode!;cmd.Parameters.Add("@m",SqlDbType.NVarChar,500).Value=q.Reason;await cmd.ExecuteNonQueryAsync(ct);}
    private static async Task ResetDeParaAsync(SqlConnection c,SqlTransaction tx,RoutingControlRequest q,CancellationToken ct){await using var cmd=Command(c,tx,"UPDATE gestao.TimesheetDocumentoDePara_Espelho SET Ativo=0,Observacao=@m WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@s AND Ativo=1; IF @@ROWCOUNT<>1 THROW 54102,N'Conflito ao resetar De/Para.',1;",q.ProjectId);cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=q.SourceDocumentCode;cmd.Parameters.Add("@m",SqlDbType.NVarChar,500).Value=q.Reason;await cmd.ExecuteNonQueryAsync(ct);}
    private static async Task ResetRedistributionAsync(SqlConnection c,SqlTransaction tx,RoutingControlRequest q,CancellationToken ct){await using var cmd=Command(c,tx,"UPDATE gestao.AuditoriaRedistribuicaoDocumento SET Ativo=0,Motivo=@m WHERE Project_ID_ACC=@p AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@s AND Ativo=1 AND Status=N'CONFIRMADO'; IF @@ROWCOUNT<>1 THROW 54103,N'Conflito ao resetar redistribuição.',1;",q.ProjectId);cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=q.SourceDocumentCode;cmd.Parameters.Add("@m",SqlDbType.NVarChar,500).Value=q.Reason;await cmd.ExecuteNonQueryAsync(ct);}
    private static async Task InsertHistoryAsync(SqlConnection c,SqlTransaction tx,RoutingControlRequest q,string actor,string before,string after,CancellationToken ct){await using var cmd=Command(c,tx,"INSERT INTO qualidade.DirecionamentoHHAuditoria(Project_ID_ACC,Documento_Origem,Metodo,Operacao,AntesJson,DepoisJson,Motivo,AlteradoPor) VALUES(@p,@s,@method,@op,@before,@after,@reason,@actor)",q.ProjectId);cmd.Parameters.Add("@s",SqlDbType.NVarChar,300).Value=q.SourceDocumentCode;cmd.Parameters.Add("@method",SqlDbType.NVarChar,30).Value=q.Operation.Contains("DE_PARA",StringComparison.Ordinal)?"DE_PARA":"REDISTRIBUICAO";cmd.Parameters.Add("@op",SqlDbType.NVarChar,40).Value=q.Operation;cmd.Parameters.Add("@before",SqlDbType.NVarChar,-1).Value=before;cmd.Parameters.Add("@after",SqlDbType.NVarChar,-1).Value=after;cmd.Parameters.Add("@reason",SqlDbType.NVarChar,1000).Value=q.Reason;cmd.Parameters.Add("@actor",SqlDbType.NVarChar,255).Value=actor;await cmd.ExecuteNonQueryAsync(ct);}

    private static async Task EnsureSchemaAsync(SqlConnection c,CancellationToken ct){const string sql="SELECT CASE WHEN OBJECT_ID(N'qualidade.DirecionamentoHHAuditoria',N'U') IS NOT NULL AND COL_LENGTH(N'gestao.TimesheetDocumentoDePara_Espelho',N'RowVersion') IS NOT NULL AND COL_LENGTH(N'gestao.AuditoriaRedistribuicaoDocumento',N'RowVersion') IS NOT NULL THEN 1 ELSE 0 END";await using var cmd=new SqlCommand(sql,c);if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))!=1)throw new RoutingControlSchemaException($"Central de direcionamento indisponível: migration aditiva pendente ({Migration}).");}
    private async Task<SqlConnection> OpenAsync(CancellationToken ct){var cs=configuration.GetConnectionString("LocalConnection")??throw new RoutingControlSchemaException("Conexão LocalConnection indisponível.");var c=new SqlConnection(cs);await c.OpenAsync(ct);return c;}
    private static SqlCommand Command(SqlConnection c,SqlTransaction? tx,string sql,string project){var cmd=new SqlCommand(sql,c,tx){CommandTimeout=60};cmd.Parameters.Add("@p",SqlDbType.NVarChar,100).Value=project;return cmd;}
    private static RoutingControlRequest Normalize(RoutingControlRequest q)=>q with{SourceDocumentCode=q.SourceDocumentCode.Trim().ToUpperInvariant(),DestinationDocumentCode=string.IsNullOrWhiteSpace(q.DestinationDocumentCode)?null:q.DestinationDocumentCode.Trim().ToUpperInvariant(),Reason=q.Reason.Trim()};
    internal static string VersionOf(params string[] parts)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f',parts))));
    internal static bool SameDiscipline(string? a,string? b)=>!string.IsNullOrWhiteSpace(a)&&string.Equals(a.Trim(),b?.Trim(),StringComparison.OrdinalIgnoreCase);
    private static bool FixedEquals(string a,string b){try{return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a),Convert.FromHexString(b));}catch(FormatException){return false;}}
}
