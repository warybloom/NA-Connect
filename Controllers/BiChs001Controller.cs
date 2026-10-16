using System.Data;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Planning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

/// <summary>
/// BI — CHS001 (Original): linhas BRUTAS (somente leitura) para a reprodução do relatório Power BI CHS001-000-PM0-RT-002-01
/// no Controle de Produção. O frontend (ClientApp/src/features/production/biChs001) aplica os MESMOS filtros de página/visual e
/// as MESMAS medidas DAX do PBIX sobre estas linhas — por isso aqui não há agregação nem normalização de rótulos.
///   GET api/bi-chs001/ldp          → LDP atual (PmoLdpSnapshot, TODAS as linhas — o BI filtra FORMAT por página) + gates 30/60/90
///   GET api/bi-chs001/programacao  → programação semanal importada (tb_WeeklyEngineeringSchedule, valores como na planilha)
///   GET api/bi-chs001/cronograma   → cronograma oficial (VW_Cronograma_Tipo2_Ultima_Versao, última leitura do .mpp), NA ORDEM das
///                                    tarefas: o frontend deriva a hierarquia (WBX[Level n]) da ordem — o banco não guarda o pai.
///   GET api/bi-chs001/cronograma-diario → trabalho por tarefa × dia (curvas S / AssignmentByDay do PBIX). Enquanto a tabela
///                                    proposta (_bi_chs001_replica/proposta_migracao) não existir, responde linhas vazias + "indisponivel".
/// Nenhuma escrita, nenhum DDL. Mesmo gate de acesso/permissão dos relatórios SQL (SqlReportAuthorization).
/// </summary>
[ApiController]
[Route("api/bi-chs001")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BiChs001Controller(IProjectAccessService projectAccess, IPermissionService permissions, IConfiguration configuration,
    ILogger<BiChs001Controller> logger) : ControllerBase
{
    /// <summary>Key da página do BI (reports.gestao.view) OU as keys já existentes da LDP.</summary>
    public static readonly string[] KeysLdp = ["reports.gestao.view", "reports.ldp.view", "production.managerial.ldp.view", "production.operational.ldp.view"];

    /// <summary>Key da página do BI OU as keys já existentes da Programação Semanal.</summary>
    public static readonly string[] KeysProgramacao = ["reports.gestao.view", "production.managerial.programacao.view", "production.operational.programacao.view"];

    /// <summary>Key da página do BI OU a key já existente do relatório de cronograma.</summary>
    public static readonly string[] KeysCronograma = ["reports.gestao.view", "reports.cronograma.view"];

    // Gates pivotados por documento (mesma view usada por LdpDetailsReportRepository / dashboards de produção).
    public static readonly string LdpSql = $"""
        SELECT LdpAtual.ID_Registro, LdpAtual.Item, LdpAtual.Discipline, LdpAtual.NA_Document_Number, LdpAtual.Client_Document_Number,
               LdpAtual.Document_Title, LdpAtual.Format, LdpAtual.Revision, LdpAtual.Status_In_Flow, LdpAtual.Document_Status, LdpAtual.Situation,
               LdpAtual.Doc_Type_Code, LdpAtual.Data_Sincronizacao,
               G30.Fase AS Fase_30, G30.Gate_Ativo AS Ativo_30, G30.Document_Status AS Document_Status_30, G30.Situation AS Situation_30,
               G60.Fase AS Fase_60, G60.Gate_Ativo AS Ativo_60, G60.Document_Status AS Document_Status_60, G60.Situation AS Situation_60,
               G90.Fase AS Fase_90, G90.Gate_Ativo AS Ativo_90, G90.Document_Status AS Document_Status_90, G90.Situation AS Situation_90
        FROM {PmoLdpSnapshot.Tabela}
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G30
          ON G30.Ldp_Registro_Id = LdpAtual.ID_Registro AND G30.Project_ID_ACC = @projectId AND G30.Gate_Percentual = 30
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G60
          ON G60.Ldp_Registro_Id = LdpAtual.ID_Registro AND G60.Project_ID_ACC = @projectId AND G60.Gate_Percentual = 60
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G90
          ON G90.Ldp_Registro_Id = LdpAtual.ID_Registro AND G90.Project_ID_ACC = @projectId AND G90.Gate_Percentual = 90
        ORDER BY LdpAtual.ID_Registro
        {PmoLdpSnapshot.OpcaoPlano};
        """;

    // Programação: o snapshot atual é a última importação do projeto (mesmo Data_Sincronizacao para o lote inteiro).
    public const string ProgramacaoSql = """
        SELECT Id_Registro, Weekly_Item, Weekly_Phase, Disciplina, Numero_NA_Atividade, Nome_Tarefa, Weekly_Progress_Status,
               Weekly_Baseline_Date, Weekly_Replanned_Date, Arquivo_Origem, Data_Sincronizacao
        FROM dbo.tb_WeeklyEngineeringSchedule
        WHERE Project_ID_ACC = @projectId
          AND Data_Sincronizacao = (SELECT MAX(T.Data_Sincronizacao) FROM dbo.tb_WeeklyEngineeringSchedule T WHERE T.Project_ID_ACC = @projectId)
          AND ISNULL(Tipo_Registro, N'DADO') = N'DADO'
        ORDER BY Id_Registro;
        """;

    // Cronograma: TODAS as tarefas (resumo + folha) na ordem do arquivo. Campos customizados do .mpp lidos do JSON gravado pela
    // leitura do cronograma (texto exato, sem normalização). Início/Início da LB/Trabalho não existem na view (nulos) — não são
    // devolvidos para não parecer que existem.
    public const string CronogramaSqlModelo = """
        SELECT v.ID_Cronograma, v.UID_Tarefa, v.EDT_WBS, v.TASK_NAME, v.Eh_Tarefa_Resumo, v.Termino_Linha_Base, v.Termino,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Status1') END AS Status1,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Disciplina') END AS Disciplina,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Responsabilidade') END AS Responsabilidade,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Pacote') END AS Pacote,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$."NA DOCUMENT NUMBER"') END AS NA_Document_Number_Campo,
               -- Work da tarefa em horas, gravado pela leitura do .mpp (CronogramaTipo2Work.LerHoras) no snapshot __NA_CONNECT_WORK_V1.
               -- Conferido em 15/10/2026: igual a WBX[Trabalho] do PBIX em 693/711 folhas casadas (as demais: marcos de 1 min no PBIX, 0 aqui).
               TRY_CONVERT(decimal(18, 6), CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1
                   THEN JSON_VALUE(JSON_VALUE(v.Campos_Customizados_JSON, '$.__NA_CONNECT_WORK_V1'), '$.ItemHoras') END) AS Trabalho_Horas,
               {0}v.Data_Leitura_Cronograma
        FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao v
        WHERE v.Project_ID_ACC = @projectId
        ORDER BY v.ID_Cronograma;
        """;

    /// <summary>
    /// Colunas da proposta de migração (ainda NÃO aplicada): entram no SELECT só se já existirem na view — antes disso o frontend
    /// mostra PENDENTE com o campo faltante; depois, as curvas acendem sem nova publicação.
    /// </summary>
    public static readonly string[] ColunasCronogramaOpcionais =
        ["Inicio", "Inicio_Linha_Base", "Trabalho_Linha_Base_Horas", "Trabalho_Real_Horas", "UID_Tarefa_Pai", "Nivel_Estrutura"];

    public const string ViewCronogramaDiario = "dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao";

    public const string CronogramaDiarioSql = $"""
        SELECT d.UID_Tarefa, d.Data, d.Trabalho_Real_Horas, d.Trabalho_Linha_Base_Horas, d.Trabalho_Horas, d.Data_Sincronizacao
        FROM {ViewCronogramaDiario} d
        WHERE d.Project_ID_ACC = @projectId
        ORDER BY d.UID_Tarefa, d.Data;
        """;

    [HttpGet("ldp")]
    public Task<IActionResult> Ldp([FromQuery] string projectId, CancellationToken cancellationToken) =>
        Ler(projectId, KeysLdp, LdpSql, "ldp", cancellationToken);

    [HttpGet("programacao")]
    public Task<IActionResult> Programacao([FromQuery] string projectId, CancellationToken cancellationToken) =>
        Ler(projectId, KeysProgramacao, ProgramacaoSql, "programacao", cancellationToken);

    [HttpGet("cronograma")]
    public Task<IActionResult> Cronograma([FromQuery] string projectId, CancellationToken cancellationToken) =>
        Ler(projectId, KeysCronograma, async (connection, ct) =>
        {
            var existentes = await ColunasExistentesAsync(connection, "dbo.VW_Cronograma_Tipo2_Ultima_Versao", ColunasCronogramaOpcionais, ct);
            return (MontarCronogramaSql(existentes), null);
        }, "cronograma", cancellationToken);

    [HttpGet("cronograma-diario")]
    public Task<IActionResult> CronogramaDiario([FromQuery] string projectId, CancellationToken cancellationToken) =>
        Ler(projectId, KeysCronograma, async (connection, ct) =>
        {
            using var existe = new SqlCommand("SELECT OBJECT_ID(@nome, 'V')", connection) { CommandTimeout = 30 };
            existe.Parameters.Add("@nome", SqlDbType.NVarChar, 256).Value = ViewCronogramaDiario;
            return await existe.ExecuteScalarAsync(ct) is DBNull or null
                ? (null, $"{ViewCronogramaDiario} ainda não existe: trabalho por tarefa × dia do .mpp não é persistido (proposta de migração aguardando aprovação).")
                : (CronogramaDiarioSql, null);
        }, "cronograma-diario", cancellationToken);

    /// <summary>SELECT do cronograma com as colunas opcionais já existentes (nomes da lista fixa — nunca texto do usuário).</summary>
    public static string MontarCronogramaSql(IReadOnlyCollection<string> existentes) =>
        string.Format(CronogramaSqlModelo, string.Concat(ColunasCronogramaOpcionais.Where(existentes.Contains).Select(c => $"v.{c}, ")));

    private static async Task<HashSet<string>> ColunasExistentesAsync(SqlConnection connection, string objeto, string[] candidatas, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@objeto)", connection) { CommandTimeout = 30 };
        command.Parameters.Add("@objeto", SqlDbType.NVarChar, 256).Value = objeto;
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { var nome = reader.GetString(0); if (candidatas.Contains(nome, StringComparer.OrdinalIgnoreCase)) existentes.Add(nome); }
        return existentes;
    }

    private Task<IActionResult> Ler(string projectId, string[] keys, string sql, string fonte, CancellationToken cancellationToken) =>
        Ler(projectId, keys, (_, _) => Task.FromResult<(string?, string?)>((sql, null)), fonte, cancellationToken);

    /// <param name="montar">SQL a executar, ou (null, motivo) quando a fonte ainda não existe no banco (resposta vazia + "indisponivel").</param>
    private async Task<IActionResult> Ler(string projectId, string[] keys, Func<SqlConnection, CancellationToken, Task<(string? Sql, string? Indisponivel)>> montar,
        string fonte, CancellationToken cancellationToken)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        try
        {
            var authorization = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, keys, cancellationToken);
            if (authorization is not null) return authorization;
            await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
                ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
            await connection.OpenAsync(cancellationToken);
            var (sql, indisponivel) = await montar(connection, cancellationToken);
            if (sql is null) return Ok(new { fonte, totalLinhas = 0, linhas = Array.Empty<object>(), indisponivel });
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var colunas = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var linhas = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var linha = new Dictionary<string, object?>(colunas.Length, StringComparer.Ordinal);
                for (int i = 0; i < colunas.Length; i++) linha[colunas[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                linhas.Add(linha);
            }
            return Ok(new { fonte, totalLinhas = linhas.Count, linhas });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (SqlException ex)
        {
            logger.LogWarning("Falha na consulta do BI CHS001 ({Fonte}). Number={SqlErrorNumber} Message={SqlErrorMessage}", fonte, ex.Number, ex.Message);
            return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
        }
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
