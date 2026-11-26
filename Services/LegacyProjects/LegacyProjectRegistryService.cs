using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.LegacyProjects;

// O cadastro pertence a dbo.Projetos, fonte da Home e dos módulos N&A.
// Sem outras tabelas de cadastro ou de lançamentos; o histórico é consultado nas fontes existentes.
public sealed class LegacyProjectRegistryService(IConfiguration config)
{
    public sealed record LegacyProject(string ProjectIdAcc, string HubIdAcc, string ProjectName,
        string TimesheetProject, string CrOs, string Afe, string DocumentValidation, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
    public sealed record LegacyInput(string? ProjectIdAcc, string? HubIdAcc, string? ProjectName,
        string? TimesheetProject, string? CrOs, string? Afe);
    public sealed record RawRow(DateTime? Date, string? Professional, string? Function,
        string? Discipline, string? Description, string? Project, string? Afe, decimal Hours);
    public sealed record DocumentRow(DateTime? Date, string? Professional, string? Function,
        string? Discipline, string? Description, string? Project, string? AfeOrDoc1,
        string? DocumentCode, string? Document, string? Doc2, string? Doc3, string? Doc4,
        string? Doc5, decimal Hours);
    public sealed record DocSummary(string DocumentCode, string? Doc2, string? Doc3,
        string? Doc4, string? Doc5, int Entries, decimal Hours);
    public sealed record SourceSummary(int Entries, decimal Hours, DateTime? First, DateTime? Last);
    public sealed record LegacyPreview(string TimesheetProject, string CrOs, string Afe,
        SourceSummary ByAfe, SourceSummary ByDocument, IReadOnlyList<DocSummary> Documents,
        IReadOnlyList<RawRow> AfeRows, IReadOnlyList<DocumentRow> DocumentRows,
        bool AfeTruncated, bool DocumentTruncated, string Notice);

    private SqlConnection Local() => new(config.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("LocalConnection indisponível."));
    private SqlConnection BI() => new(config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection indisponível."));

    public static (string ProjectId,string Hub,string Name,string Project,string CrOs,string Afe) Validate(LegacyInput input)
    {
        var id=input.ProjectIdAcc?.Trim()??"";
        var hub=input.HubIdAcc?.Trim()??"";
        var name=input.ProjectName?.Trim()??"";
        var project=input.TimesheetProject?.Trim()??"";
        var cr=input.CrOs?.Trim()??"";
        var afe=input.Afe?.Trim()??"";
        if(!id.StartsWith("b.",StringComparison.OrdinalIgnoreCase) ||
           !Guid.TryParseExact(id[2..],"D",out _))
            throw new ArgumentException("Selecione um projeto ACC válido.");
        if(hub.Length is < 3 or > 100 || name.Length is < 2 or > 200 ||
           project.Length is < 1 or > 100 || cr.Length>100 || afe.Length>100 ||
           (cr.Length==0 && afe.Length==0))
            throw new ArgumentException("Informe o projeto, CR-OS e/ou AFE, respeitando os limites.");
        return (id,hub,name,project,cr,afe);
    }

    private const string SelectProjects = """
        SELECT p.Project_ID_ACC,COALESCE(h.Account_ID_ACC,''),COALESCE(p.Nome_Projeto,''),
            COALESCE(p.Timesheet_Projeto,''),COALESCE(p.CR_OS,''),COALESCE(p.AFE_Historico,''),
            'PROVISORIO',COALESCE(p.Sem_LDP_CriadoEm,SYSUTCDATETIME()),
            COALESCE(p.Sem_LDP_AtualizadoEm,SYSUTCDATETIME())
        FROM dbo.Projetos p
        LEFT JOIN dbo.Hubs_Conta h ON h.HubContaID=p.HubContaID
        WHERE p.Sem_LDP=1
        """;

    public async Task<IReadOnlyList<LegacyProject>> ListAsync(CancellationToken ct)
    {
        await using var cn=Local();await cn.OpenAsync(ct);
        await using var q=new SqlCommand(SelectProjects+" ORDER BY p.Nome_Projeto",cn);
        var list=new List<LegacyProject>();await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))list.Add(Read(r));
        return list;
    }
    private static LegacyProject Read(SqlDataReader r)=>new(
        r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
        r.GetString(4),r.GetString(5),r.GetString(6),r.GetDateTime(7),r.GetDateTime(8));

    public async Task<LegacyProject> SaveAsync(LegacyInput input,string actor,int? userId,CancellationToken ct)
    {
        var x=Validate(input);
        if(userId is null or <=0)throw new InvalidOperationException("Usuário interno sem identificação para acesso ao projeto.");
        await using var cn=Local();await cn.OpenAsync(ct);
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using var q=new SqlCommand("""
                DECLARE @hubPk int=(SELECT TOP 1 HubContaID FROM dbo.Hubs_Conta WHERE Account_ID_ACC=@hub);
                IF @hubPk IS NULL THROW 51011,'Conta ACC não reconhecida no cadastro de hubs.',1;
                DECLARE @projectPk int=(SELECT TOP 1 ProjetoID FROM dbo.Projetos WITH(UPDLOCK,HOLDLOCK)
                    WHERE Project_ID_ACC=@id);
                IF @projectPk IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM dbo.Projetos WHERE ProjetoID=@projectPk
                        AND File_ID_LDP_ACC IS NOT NULL AND NULLIF(LTRIM(RTRIM(File_ID_LDP_ACC)),'') IS NOT NULL)
                        THROW 51012,'Projeto já possui uma LDP; cadastro sem LDP bloqueado.',1;
                    UPDATE dbo.Projetos SET HubContaID=@hubPk,Nome_Projeto=@name,
                        CR_OS=NULLIF(@cr,''),Timesheet_Projeto=@project,
                        AFE_Historico=NULLIF(@afe,''),Sem_LDP=1,Is_Ativo=1,
                        Sem_LDP_CriadoEm=COALESCE(Sem_LDP_CriadoEm,SYSUTCDATETIME()),
                        Sem_LDP_AtualizadoEm=SYSUTCDATETIME(),
                        Sem_LDP_CriadoPor=COALESCE(Sem_LDP_CriadoPor,@actor)
                    WHERE ProjetoID=@projectPk;
                END
                ELSE
                BEGIN
                    INSERT dbo.Projetos
                        (HubContaID,Nome_Projeto,Project_ID_ACC,Is_Ativo,Tipo_LDP,CR_OS,
                        Timesheet_Projeto,AFE_Historico,Sem_LDP,Sem_LDP_CriadoEm,
                        Sem_LDP_AtualizadoEm,Sem_LDP_CriadoPor)
                    VALUES (@hubPk,@name,@id,1,1,NULLIF(@cr,''),@project,NULLIF(@afe,''),1,
                        SYSUTCDATETIME(),SYSUTCDATETIME(),@actor);
                    SET @projectPk=CONVERT(int,SCOPE_IDENTITY());
                END;
                IF NOT EXISTS (SELECT 1 FROM perm.Users WHERE UserId=@userId AND Ativo=1)
                    THROW 51013,'Usuário sem cadastro ativo para acessar o projeto.',1;
                IF EXISTS(SELECT 1 FROM perm.UserProjects WITH(UPDLOCK,HOLDLOCK)
                  WHERE UserId=@userId AND ProjetoId=@projectPk)
                    UPDATE perm.UserProjects SET Ativo=1,AtualizadoEm=SYSUTCDATETIME()
                    WHERE UserId=@userId AND ProjetoId=@projectPk;
                ELSE
                    INSERT perm.UserProjects(UserId,ProjetoId,Ativo) VALUES(@userId,@projectPk,1);
                """,cn,tx){CommandTimeout=45};
            q.Parameters.Add("@id",SqlDbType.NVarChar,255).Value=x.ProjectId;
            q.Parameters.Add("@hub",SqlDbType.NVarChar,100).Value=x.Hub;
            q.Parameters.Add("@name",SqlDbType.NVarChar,200).Value=x.Name;
            q.Parameters.Add("@project",SqlDbType.NVarChar,100).Value=x.Project;
            q.Parameters.Add("@cr",SqlDbType.NVarChar,100).Value=x.CrOs;
            q.Parameters.Add("@afe",SqlDbType.NVarChar,100).Value=x.Afe;
            q.Parameters.Add("@actor",SqlDbType.NVarChar,255).Value=actor;
            q.Parameters.Add("@userId",SqlDbType.Int).Value=userId.Value;
            await q.ExecuteNonQueryAsync(ct);
            await using var read=new SqlCommand(SelectProjects+" AND p.Project_ID_ACC=@id",cn,tx);
            read.Parameters.Add("@id",SqlDbType.NVarChar,255).Value=x.ProjectId;
            await using var r=await read.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new InvalidOperationException("Cadastro não encontrado após inserção.");
            var item=Read(r);await r.CloseAsync();
            await tx.CommitAsync(ct);
            return item;
        }
        catch {await tx.RollbackAsync(ct);throw;}
    }

    public async Task<LegacyPreview> PreviewAsync(string timesheetProject,string crOs,string afe,
        int afeOffset,int documentOffset,CancellationToken ct)
    {
        timesheetProject=(timesheetProject??"").Trim();
        crOs=(crOs??"").Trim();afe=(afe??"").Trim();
        if(timesheetProject.Length is < 1 or > 100 || crOs.Length>100 || afe.Length>100 ||
           (string.IsNullOrEmpty(crOs)&&string.IsNullOrEmpty(afe)) ||
           afeOffset<0||documentOffset<0||afeOffset>1_000_000||documentOffset>1_000_000)
            throw new ArgumentException("Projeto Timesheet e CR-OS/AFE são necessários; offsets inválidos.");
        // Documento: fonte corrigida LOCAL, a mesma utilizada nos relatórios e custos N&A.
        // AFE sem documento: fonte bruta BI, pois não existe uma visão corrigida equivalente
        // com a coluna AFE. A consulta AFE é auxiliar e nunca deve ser somada à documental.
        await using var cn=BI();await cn.OpenAsync(ct);
        await using var corrected=Local();await corrected.OpenAsync(ct);
        const int page=100;
        static string? Text(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToString(r.GetValue(i));
        static DateTime? Date(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDateTime(r.GetValue(i));
        static decimal Dec(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToDecimal(r.GetValue(i));
        async Task<SourceSummary> Summary(SqlConnection connection,string table,string column,string filter)
        {
            if(filter.Length==0)return new(0,0,null,null);
            await using var q=new SqlCommand($"""
                SELECT COUNT_BIG(*),COALESCE(SUM(CAST(Trabalhadas AS decimal(28,6))),0),
                    MIN([data]),MAX([data])
                FROM dbo.{table}
                WHERE UPPER(LTRIM(RTRIM(Projeto)))=UPPER(@project) AND UPPER(LTRIM(RTRIM({column})))=UPPER(@filter)
                """,connection){CommandTimeout=45};
            q.Parameters.Add("@project",SqlDbType.VarChar,100).Value=timesheetProject;
            q.Parameters.Add("@filter",SqlDbType.VarChar,100).Value=filter;
            await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
            var n=r.GetInt64(0);
            if(n>int.MaxValue)throw new InvalidOperationException("Quantidade de lançamentos excedeu limite.");
            return new((int)n,Dec(r,1),Date(r,2),Date(r,3));
        }
        var rawSummary=await Summary(cn,"tbTimeSheet","AFE",afe);
        var docSummary=await Summary(corrected,"VW_TimeSheet_NA_Corrigida_Projeto","doc1",crOs);
        var docs=new List<DocSummary>();
        if(crOs.Length>0){
            await using var q=new SqlCommand("""
                SELECT TOP (501)
                    COALESCE(NULLIF(LTRIM(RTRIM(DocumentoCodigo)),''),'[SEM CÓDIGO]'),
                    NULLIF(LTRIM(RTRIM(doc2)),''),NULLIF(LTRIM(RTRIM(doc3)),''),
                    NULLIF(LTRIM(RTRIM(doc4)),''),NULLIF(LTRIM(RTRIM(doc5)),''),
                    COUNT(*),SUM(CAST(Trabalhadas AS decimal(28,6)))
                FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
                WHERE UPPER(LTRIM(RTRIM(Projeto)))=UPPER(@project) AND UPPER(LTRIM(RTRIM(doc1)))=UPPER(@filter)
                GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(DocumentoCodigo)),''),'[SEM CÓDIGO]'),
                    NULLIF(LTRIM(RTRIM(doc2)),''),NULLIF(LTRIM(RTRIM(doc3)),''),
                    NULLIF(LTRIM(RTRIM(doc4)),''),NULLIF(LTRIM(RTRIM(doc5)),'')
                ORDER BY 1,2,3,4,5
                """,corrected){CommandTimeout=45};
            q.Parameters.Add("@project",SqlDbType.VarChar,100).Value=timesheetProject;
            q.Parameters.Add("@filter",SqlDbType.VarChar,100).Value=crOs;
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))docs.Add(new(r.GetString(0),Text(r,1),Text(r,2),Text(r,3),
                Text(r,4),r.GetInt32(5),Dec(r,6)));
        }
        var raws=new List<RawRow>();
        if(afe.Length>0){
            await using var q=new SqlCommand("""
                SELECT [data],Nome,Funcao,Disciplina,Descricao,Projeto,AFE,Trabalhadas
                FROM dbo.tbTimeSheet
                WHERE UPPER(LTRIM(RTRIM(Projeto)))=UPPER(@project) AND UPPER(LTRIM(RTRIM(AFE)))=UPPER(@filter)
                ORDER BY [data] DESC,Nome
                OFFSET @offset ROWS FETCH NEXT 100 ROWS ONLY
                """,cn){CommandTimeout=45};
            q.Parameters.Add("@project",SqlDbType.VarChar,100).Value=timesheetProject;
            q.Parameters.Add("@filter",SqlDbType.VarChar,100).Value=afe;
            q.Parameters.Add("@offset",SqlDbType.Int).Value=afeOffset;
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))raws.Add(new(Date(r,0),Text(r,1),Text(r,2),Text(r,3),
                Text(r,4),Text(r,5),Text(r,6),Dec(r,7)));
        }
        var rows=new List<DocumentRow>();
        if(crOs.Length>0){
            await using var q=new SqlCommand("""
                SELECT [data],Nome,Funcao,Disciplina,Descricao,Projeto,doc1,
                    DocumentoCodigo,Documento,doc2,doc3,doc4,doc5,Trabalhadas
                FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
                WHERE UPPER(LTRIM(RTRIM(Projeto)))=UPPER(@project) AND UPPER(LTRIM(RTRIM(doc1)))=UPPER(@filter)
                ORDER BY [data] DESC,Nome,DocumentoCodigo
                OFFSET @offset ROWS FETCH NEXT 100 ROWS ONLY
                """,corrected){CommandTimeout=45};
            q.Parameters.Add("@project",SqlDbType.VarChar,100).Value=timesheetProject;
            q.Parameters.Add("@filter",SqlDbType.VarChar,100).Value=crOs;
            q.Parameters.Add("@offset",SqlDbType.Int).Value=documentOffset;
            await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))rows.Add(new(Date(r,0),Text(r,1),Text(r,2),Text(r,3),
                Text(r,4),Text(r,5),Text(r,6),Text(r,7),Text(r,8),Text(r,9),
                Text(r,10),Text(r,11),Text(r,12),Dec(r,13)));
        }
        var notice="HH documentais pela visão CORRIGIDA N&A (dbo.VW_TimeSheet_NA_Corrigida_Projeto). " +
            "AFE auxiliar pela tbTimeSheet bruta, porque não há visão corrigida equivalente com AFE. " +
            "As fontes podem se sobrepor: NÃO somar os dois totais sem reconciliação. " +
            "Custo histórico não calculado; documentos aceitos apenas provisoriamente.";
        if(afe.Length>0 && rawSummary.Entries==0)
        {
            await using var status=new SqlCommand(
                "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.tbTimeSheet) THEN 1 ELSE 0 END",cn){CommandTimeout=20};
            if(Convert.ToInt32(await status.ExecuteScalarAsync(ct))==0)
                notice="ATENÇÃO: fonte AFE bruta tbTimeSheet inteira vazia no BI (possível recarga). "+notice;
        }
        if(crOs.Length>0 && docSummary.Entries==0)
        {
            await using var status=new SqlCommand(
                "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto) THEN 1 ELSE 0 END",
                corrected){CommandTimeout=20};
            if(Convert.ToInt32(await status.ExecuteScalarAsync(ct))==0)
                notice="ATENÇÃO: visão Timesheet corrigida inteira vazia no banco local (possível recarga). "+notice;
        }
        return new(timesheetProject,crOs,afe,rawSummary,docSummary,docs.Take(500).ToArray(),
            raws,rows,afeOffset+page<rawSummary.Entries,documentOffset+page<docSummary.Entries,
            notice);
    }
}
