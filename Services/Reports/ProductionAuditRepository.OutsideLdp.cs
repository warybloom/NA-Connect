using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record OutsideLdpDocument(
    string OriginalDocument, int Entries, decimal Hours, decimal Cost,
    string Treatment, string? AssignedDiscipline, string? AssignedDestination);
public sealed record OutsideLdpOverview(
    string ProjectCode, IReadOnlyList<OutsideLdpDocument> Items,
    IReadOnlyList<string> DocumentDestinations, IReadOnlyList<string> Disciplines,
    decimal TotalHours, decimal TotalCost, decimal PendingHours, decimal ConfirmedAtHours);
public sealed record OutsideLdpDirectionInput(
    string OriginalDocument, string Mode, string? DestinationDocument, string? Discipline);
public sealed record OutsideLdpDirectionResult(
    string OriginalDocument, string Mode, string? DestinationDocument, decimal Hours, decimal Cost);

public sealed partial class ProductionAuditRepository
{
    public async Task<OutsideLdpOverview> ReadOutsideLdpAsync(string projectId, CancellationToken ct)
    {
        var identity = await ReadProjectIdentityAsync(projectId, ct);
        await using var db = await OpenAsync(ct);
        var documents = new List<OutsideLdpDocument>();
        var known = new List<string>();
        var disciplines = new List<string>();

        // A origem da conciliação documental é o espelho ANTES da aplicação de AT manual.
        // Portanto, os documentos AT permanecem visíveis e identificados como tratados.
        const string sql = """
            WITH LdpCodes AS
            (
                SELECT DISTINCT UPPER(LTRIM(RTRIM(NA_Document_Number))) Code
                FROM dbo.VW_Ldp_Unificada
                WHERE Project_ID_ACC=@pid AND NULLIF(LTRIM(RTRIM(NA_Document_Number)),N'') IS NOT NULL
            ),
            ByDocument AS
            (
                SELECT UPPER(LTRIM(RTRIM(s.DocumentoCodigo_Corrigido))) AS Code,
                    COUNT_BIG(*) AS Entries,
                    SUM(CAST(ISNULL(s.HorasTrabalhadas,0) AS decimal(28,6))) AS Hours,
                    SUM(CAST(ISNULL(s.CustoCalculado,0) AS decimal(28,6))) AS Cost
                FROM gestao.VW_TimeSheet_Corrigido_Espelho s
                WHERE s.Project_ID_ACC=@pid
                    AND NULLIF(LTRIM(RTRIM(s.DocumentoCodigo_Corrigido)),N'') IS NOT NULL
                GROUP BY UPPER(LTRIM(RTRIM(s.DocumentoCodigo_Corrigido)))
            )
            SELECT d.Code, d.Entries, d.Hours, d.Cost,
                   CASE WHEN r.Id IS NOT NULL THEN r.Status_Documento ELSE N'PENDENTE' END,
                   r.Disciplina
            FROM ByDocument d
            LEFT JOIN LdpCodes l ON l.Code=d.Code
            OUTER APPLY
            (
                SELECT TOP (1) a.Id,a.Status_Documento,a.Disciplina
                FROM gestao.AuditoriaRedistribuicaoDocumento a
                WHERE a.Project_ID_ACC=@pid AND a.Ativo=1 AND a.Status=N'CONFIRMADO'
                    AND UPPER(LTRIM(RTRIM(a.Documento_Origem)))=d.Code
                ORDER BY a.ConfirmadoEm DESC
            ) r
            WHERE l.Code IS NULL
            ORDER BY d.Hours DESC,d.Code;
            """;
        await using (var cmd = new SqlCommand(sql, db) { CommandTimeout = 90 })
        {
            cmd.Parameters.Add("@pid", SqlDbType.NVarChar, 100).Value = projectId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var code = Text(reader, 0) ?? "";
                documents.Add(new OutsideLdpDocument(
                    code, Convert.ToInt32(reader.GetValue(1)), Num(reader,2) ?? 0m,
                    Num(reader,3) ?? 0m, Text(reader,4) ?? "PENDENTE",
                    Text(reader,5), null));
            }
        }
        await using (var cmd = new SqlCommand("""
            SELECT DISTINCT UPPER(LTRIM(RTRIM(NA_Document_Number)))
            FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@pid
               AND NULLIF(LTRIM(RTRIM(NA_Document_Number)),N'') IS NOT NULL
            ORDER BY 1;
            """, db) { CommandTimeout=90 })
        {
            cmd.Parameters.Add("@pid",SqlDbType.NVarChar,100).Value=projectId;
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
                if (Text(reader,0) is { } code) known.Add(code);
        }
        // Disciplinas já associadas ao projeto, evitando inventar siglas.
        await using (var cmd = new SqlCommand("""
            SELECT DISTINCT NULLIF(LTRIM(RTRIM(Disciplina)),N'')
            FROM gestao.VW_TimeSheet_Corrigido_Espelho
            WHERE Project_ID_ACC=@pid AND NULLIF(LTRIM(RTRIM(Disciplina)),N'') IS NOT NULL
            ORDER BY 1
            """,db) { CommandTimeout=90 })
        {
            cmd.Parameters.Add("@pid",SqlDbType.NVarChar,100).Value=projectId;
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
                if (Text(reader,0) is { } d) disciplines.Add(d);
        }

        return new OutsideLdpOverview(identity.ProjectCode,documents,known,disciplines,
            documents.Sum(x=>x.Hours),documents.Sum(x=>x.Cost),
            documents.Where(x=>x.Treatment=="PENDENTE").Sum(x=>x.Hours),
            documents.Where(x=>x.Treatment=="AT").Sum(x=>x.Hours));
    }

    public async Task<OutsideLdpDirectionResult> SaveOutsideLdpDirectionAsync(
        string projectId, OutsideLdpDirectionInput input, string? actor, CancellationToken ct)
    {
        var source = input.OriginalDocument?.Trim().ToUpperInvariant() ?? "";
        var mode = input.Mode?.Trim().ToUpperInvariant() ?? "";
        var dest = input.DestinationDocument?.Trim().ToUpperInvariant();
        var discipline = input.Discipline?.Trim();
        if (source.Length is < 10 or > 255 || !(mode is "DOCUMENTO" or "AT"))
            throw new ArgumentException("Número do Documento original ou tipo de direcionamento inválido.");
        if (mode=="DOCUMENTO" && (string.IsNullOrWhiteSpace(dest) || dest==source))
            throw new ArgumentException("Selecione outro Número do Documento existente na LDP.");
        if (mode=="AT" && (string.IsNullOrWhiteSpace(discipline) || discipline.Length>255))
            throw new ArgumentException("Selecione a disciplina de destino para AT.");

        await using var db = await OpenAsync(ct);
        // A VW_Ldp_Unificada consulta servidor vinculado: nunca consultá-la DENTRO de
        // uma transação SQL local, pois isso requer MSDTC no SRV_NA_CONSULT.
        // Validação de leitura primeiro; transação local somente para persistência.
        // Não permite redirecionar lançamentos já conciliados nem sem HH.
            const string validate = """
                DECLARE @docFound bit=0;
                IF EXISTS(SELECT 1 FROM dbo.VW_Ldp_Unificada
                  WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(NA_Document_Number)))=@original)
                  THROW 54001,N'O Número do Documento já consta na LDP. Atualize a auditoria.',1;
                IF @mode=N'DOCUMENTO' AND NOT EXISTS
                   (SELECT 1 FROM dbo.VW_Ldp_Unificada
                    WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(NA_Document_Number)))=@dest)
                  THROW 54002,N'O Número do Documento de destino não existe na LDP do projeto.',1;
                IF NOT EXISTS(SELECT 1 FROM gestao.VW_TimeSheet_Corrigido_Espelho
                      WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido)))=@original)
                  THROW 54003,N'Nenhum lançamento permanece nesse Número do Documento.',1;
                SELECT COUNT(*),SUM(CAST(ISNULL(HorasTrabalhadas,0) AS decimal(28,6))),
                  SUM(CAST(ISNULL(CustoCalculado,0) AS decimal(28,6)))
                FROM gestao.VW_TimeSheet_Corrigido_Espelho
                WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido)))=@original;
                """;
            int entryCount;decimal hours,cost;
            await using(var cmd=new SqlCommand(validate,db) { CommandTimeout=90 })
            {
                cmd.Parameters.Add("@pid",SqlDbType.NVarChar,100).Value=projectId;
                cmd.Parameters.Add("@original",SqlDbType.NVarChar,255).Value=source;
                cmd.Parameters.Add("@mode",SqlDbType.NVarChar,30).Value=mode;
                cmd.Parameters.Add("@dest",SqlDbType.NVarChar,255).Value=(object?)dest??DBNull.Value;
                await using var r=await cmd.ExecuteReaderAsync(ct);
                if(!await r.ReadAsync(ct))throw new InvalidOperationException("Sem lançamentos para direcionar.");
                entryCount=Convert.ToInt32(r.GetValue(0));
                hours=Num(r,1)??0m;cost=Num(r,2)??0m;
            }
            if(entryCount==0||hours<=0)throw new InvalidOperationException("HH inexistentes para esse documento.");

            await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try
            {
            // Transação única e LOCAL: tira o AT antigo, se necessário, ANTES de ativar o De/Para.
            if(mode=="DOCUMENTO")
            {
                const string sql = """
                    UPDATE gestao.AuditoriaRedistribuicaoDocumento
                    SET Ativo=0,Motivo=CONCAT(LEFT(ISNULL(Motivo,N''),300),N' | Redirecionado à LDP em ',CONVERT(nvarchar(30),SYSUTCDATETIME(),126))
                    WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@original
                      AND Ativo=1 AND Status=N'CONFIRMADO' AND Status_Documento=N'AT';

                    UPDATE gestao.TimesheetDocumentoDePara_Espelho
                    SET Documento_Destino=@dest,Observacao=@note,Ativo=1
                    WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@original;
                    IF @@ROWCOUNT=0
                        INSERT INTO gestao.TimesheetDocumentoDePara_Espelho
                         (Project_ID_ACC,Documento_Origem,Documento_Destino,Observacao,Ativo)
                        VALUES(@pid,@original,@dest,@note,1);
                    """;
                await using var cmd=new SqlCommand(sql,db,tx) {CommandTimeout=60};
                cmd.Parameters.Add("@pid",SqlDbType.NVarChar,100).Value=projectId;
                cmd.Parameters.Add("@original",SqlDbType.NVarChar,255).Value=source;
                cmd.Parameters.Add("@dest",SqlDbType.NVarChar,300).Value=dest!;
                cmd.Parameters.Add("@note",SqlDbType.NVarChar,500).Value=
                    $"HH fora da LDP; redirecionamento manual por {actor??"usuário"} em {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                const string sql = """
                    IF EXISTS (SELECT 1 FROM gestao.TimesheetDocumentoDePara_Espelho
                        WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@original AND Ativo=1)
                      THROW 54004,N'Já existe De/Para ativo. Atualize a auditoria.',1;
                    UPDATE gestao.AuditoriaRedistribuicaoDocumento
                    SET Ativo=0
                    WHERE Project_ID_ACC=@pid AND UPPER(LTRIM(RTRIM(Documento_Origem)))=@original
                      AND Ativo=1 AND Status=N'CONFIRMADO';

                    INSERT INTO gestao.AuditoriaRedistribuicaoDocumento
                    (Project_ID_ACC,Documento_Origem,Disciplina,HH_Origem,Custo_Origem,Status,
                     ConfirmadoPor,Ativo,Motivo,Status_Documento)
                    VALUES (@pid,@original,@discipline,@hours,@cost,N'CONFIRMADO',
                            @actor,1,N'AT classificada manualmente na tela HH sem documento; Número do Documento original preservado.',N'AT');
                    """;
                await using var cmd=new SqlCommand(sql,db,tx) {CommandTimeout=60};
                cmd.Parameters.Add("@pid",SqlDbType.NVarChar,100).Value=projectId;
                cmd.Parameters.Add("@original",SqlDbType.NVarChar,255).Value=source;
                cmd.Parameters.Add("@discipline",SqlDbType.NVarChar,255).Value=discipline!;
                cmd.Parameters.Add("@actor",SqlDbType.NVarChar,255).Value=string.IsNullOrWhiteSpace(actor)?"usuario-autenticado":actor[..Math.Min(actor.Length,255)];
                var h=cmd.Parameters.Add("@hours",SqlDbType.Decimal);h.Precision=18;h.Scale=6;h.Value=hours;
                var c=cmd.Parameters.Add("@cost",SqlDbType.Decimal);c.Precision=18;c.Scale=6;c.Value=cost;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            }
            catch { await tx.RollbackAsync(ct); throw; }

            return new OutsideLdpDirectionResult(source,mode,mode=="DOCUMENTO"?dest:null,hours,cost);
    }
}
