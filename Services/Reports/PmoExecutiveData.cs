using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Documents;
using APS.WebApp.Services.DocumentQuality;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Services.Reports;

public sealed record PmoDocumento(string RowId, string ProjectId, string? Codigo, string? Titulo, string? Disciplina,
    decimal? HHPrevisto, decimal? CustoPrevisto, DateTime? InicioSemana, DateTime? DataBaseline,
    DateTime? DataReprogramada, DateTime? DataEmissao, string? StatusInterno, string? StatusCliente,
    string? SituacaoEmissao, decimal? HHRealLdp, decimal? CustoRealLdp)
{
    public decimal? AvancoFisico { get; init; }
    public string ClasseFisica { get; init; } = "SEM_DADO";
    public string CorrelacaoCronograma { get; init; } = "SEM_MATCH";
    public bool Cancelado { get; init; }
    public bool Emitido => DocumentEmissionRules.IsIssued(StatusCliente, DataEmissao);
    public DateTime? DataAlvo { get; init; }
    public string FonteDataAlvo { get; init; } = "SEM_DADO";
    public string StatusPrazo { get; init; } = "SEM_DADO";
    public decimal? PercentFisicoLdp { get; init; }
    /// <summary>Peso/avanço físico configurado pelo planejador para o Document_Status deste projeto.</summary>
    public decimal? PercentFisicoConfigurado { get; init; }
    public string FonteAvancoFisico { get; init; } = "SEM_DADO";
    // Partição mutuamente exclusiva (ver PmoExecutiveRules.Completar): CANCELADO | EMITIDO | NO_PRAZO | ATRASADO | SEM_PRAZO.
    public string StatusDocumento { get; init; } = "SEM_PRAZO";
    // HH REALIZADO OFICIAL do documento = dbo.VW_Ldp_Unificada.Actual_HH_Total_Corrigido, lido PRONTO (regra no banco).
    // Nunca reconstruído a partir de outras colunas; se divergir de qualquer cálculo interno, a coluna ganha.
    public decimal? HHRealizado { get; init; }
    // CUSTO REALIZADO do documento = dbo.VW_Ldp_Unificada.Actual_Cost_Corrigido (Timesheet corrigido casado ao documento).
    // O total do projeto é o Timesheet completo e é calculado separadamente em ProjectActualFacts.
    public decimal? CustoRealizado { get; init; }
    public decimal? HHTimesheetOriginal { get; init; }
    public decimal? CustoTimesheetOriginal { get; init; }
    public decimal HHComplementoHistorico { get; init; }
    public decimal CustoComplementoHistorico { get; init; }
    /// <summary>Classificação documental da linha. AT continua válido para HH/custo, mas não é entregável documental.</summary>
    public string? DocTypeCode { get; init; }
    public string? Doc4 { get; init; }
    public string? DisciplinaPtPadrao { get; init; }
    public string? DisciplinaEnPadrao { get; init; }
    public string? DisciplinaCanonicaCadastrada { get; init; }
    /// <summary>Nome EXATO do cadastro da Norma de Coordenação vinculada ao projeto, resolvido do DOC3.</summary>
    public string? DisciplinaNorma { get; init; }
    public string? SiglaDoc3 { get; init; }
    public string? FonteDisciplinaNorma { get; init; }
    public string? DiagnosticoDisciplinaNorma { get; init; }
    public int? NormaCoordenacaoId { get; init; }
    // A partir da Padronização de Disciplinas, o cadastro global é a autoridade.
    // Nome ainda não mapeado permanece bruto e aparece na auditoria até ser corrigido.
    // 15/10/2026: a disciplina oficial vem dos 2 primeiros caracteres do DOC3 (Norma do projeto -> canônico global),
    // a MESMA rota do realizado. O texto Discipline da LDP só é usado quando o DOC3 não resolve.
    public string? DisciplinaCanonicaDoc3 { get; init; }
    public string? DisciplinaCanonica => !string.IsNullOrWhiteSpace(DisciplinaCanonicaDoc3)
        ? DisciplinaCanonicaDoc3
        : !string.IsNullOrWhiteSpace(DisciplinaCanonicaCadastrada)
        ? DisciplinaCanonicaCadastrada
        : Disciplina;
    public bool IsActivity => LdpItemRules.IsActivity(DocTypeCode, Doc4);
    public bool IsDocument => !IsActivity;
    /// <summary>Gates 30/60/90 do documento (LDP Tipo 2; vazio no Tipo 1). Percentuais já em pontos (0–100).</summary>
    public IReadOnlyList<PmoGate> Gates { get; init; } = Array.Empty<PmoGate>();
}

/// <summary>Gate de um documento LDP Tipo 2 (colunas Gate_XX_* de dbo.VW_Ldp_Unificada). A view guarda os percentuais em fração (1 = 100%).</summary>
public sealed record PmoGate(int Percentual, bool Ativo, DateTime? Baseline, DateTime? Reprogramada, decimal? PlanejadoFisico, decimal? FisicoConcluido)
{
    /// <summary>Gate cancelado: mesma regra de cancelamento da LDP (PmoExecutiveRules.Cancelado sobre Document_Status/Situation do gate).</summary>
    public bool Cancelado { get; init; }
    public string? DocumentStatus { get; init; }
    public string? Situation { get; init; }
    /// <summary>Gate aprovado pelo status oficial; emissão por fase não herda a data do documento geral.</summary>
    public bool Emitido => !Cancelado && APS.WebApp.Services.Documents.DocumentEmissionRules.IsApproved(DocumentStatus);
    /// <summary>
    /// ESFORÇO da fase = Work planejado (horas) da(s) tarefa(s) do Cronograma Tipo 2 deste documento neste gate
    /// (Campos_Customizados_JSON.__NA_CONNECT_WORK_V1.ItemHoras — mesma fonte do WBS da LDP Tipo 2). Null = sem esforço válido.
    /// </summary>
    public decimal? EsforcoHoras { get; init; }
}
// CustoRealizado = gestao.TimesheetSnapshotAtual.CustoTimesheetInformado (custo real informado no
// lançamento). Nunca CustoCalculado — ver PmoActualFactsProvider para a mesma regra na camada canônica.
public sealed record PmoLancamento(string RowId, string ProjectId, string? DocumentoCodigo, string? Documento,
    string? Disciplina, DateTime? DataLancamento, decimal? HHRealizado, decimal? CustoRealizado)
{
    // Custo real operacional atual do lançamento (gestao.TimesheetSnapshotAtual.CustoCalculado) — mesma fonte
    // canônica de PmoActualFactsProvider. CustoRealizado acima continua nulo (CustoTimesheetInformado não existe).
    public decimal? CustoCalculado { get; init; }
}
public sealed record PmoWeekly(string RowId, string ProjectId, string? NumeroAtividade, string? Disciplina,
    string? NomeTarefa, DateTime? DataBaseline, DateTime? DataReprogramada, string? StatusProgresso);
public sealed record PmoTarefa(string RowId, string ProjectId, string? DocumentoCodigo, string? Disciplina,
    decimal? Realizado, DateTime? DataFimBaseline, DateTime? DataFimAtual, int? Gate, string? Fase,
    bool ElegivelDocumento, bool Resumo, decimal? Trabalho)
{
    /// <summary>Work planejado (horas) do snapshot do Cronograma Tipo 2 (Campos_Customizados_JSON); null no Tipo 1.</summary>
    public decimal? EsforcoHoras { get; init; }
    public int? DesvioDias => DataFimAtual.HasValue && DataFimBaseline.HasValue
        ? (DataFimAtual.Value.Date - DataFimBaseline.Value.Date).Days : null;
}
public sealed record PmoExecutiveReport(string ProjectId, string NomeProjeto, int TipoLdp, DateTime DataReferencia,
    IReadOnlyList<PmoDocumento> Documentos, IReadOnlyList<PmoLancamento> Lancamentos,
    IReadOnlyList<PmoWeekly> WeeklyAtividades, IReadOnlyList<PmoTarefa> Cronograma)
{
    /// <summary>true = a view do ambiente expõe Actual_Cost_Total_Corrigido e o custo realizado oficial foi lido dela.</summary>
    public bool CustoRealizadoOficial { get; init; }
}

/// <summary>Regras de leitura PMO. Nenhum cálculo/escrita no cronograma operacional.</summary>
public static class PmoExecutiveRules
{
    public static string Chave(string? codigo) => (codigo ?? "").Trim().ToUpperInvariant();
    // Valores reais auditados na LDP: Situation e Document_Status = CANCELED/CANCELADO; Status_In_Flow = "Cancelado"
    // (36 documentos só marcados neste campo, nenhum emitido). HOLD/VOID não existem nos dados — não são cancelamento.
    public static bool Cancelado(string? status, string? situacao, string? statusInterno = null) =>
        DocumentEmissionRules.IsCancelled(status, situacao, statusInterno);
    public static string Classe(decimal? valor) => valor switch
    {
        null => "SEM_DADO", < 0 or > 100 => "INVALIDO", 100 => "FISICO_100", 0 => "FISICO_ZERO", _ => "FISICO_PARCIAL"
    };
    public static DateTime? DataConcordante(IEnumerable<DateTime?> values)
    {
        var dates = values.Where(x => x.HasValue).Select(x => x!.Value.Date).Distinct().ToArray();
        return dates.Length == 1 ? dates[0] : null;
    }
    public static PmoDocumento Completar(PmoDocumento d, IReadOnlyList<PmoTarefa> tarefas, int tipo, DateTime referencia)
    {
        // Tipo1: concordância de valores válidos, como a consolidação existente no sincronizador.
        // Tipo2: gates/fases distintos não representam um único percentual documental.
        var candidatos = tarefas.Where(t => t.ProjectId == d.ProjectId && t.ElegivelDocumento && !t.Resumo).ToArray();
        bool gatesAmbiguos = tipo == 2 && candidatos.Select(t => (t.Gate, t.Fase)).Distinct().Count() > 1;
        var validos = candidatos.Select(t => t.Realizado).Where(x => x is >= 0 and <= 100).Distinct().ToArray();
        var invalidos = candidatos.Select(t => t.Realizado).Where(x => x is < 0 or > 100).ToArray();
        decimal? fisico = !gatesAmbiguos && validos.Length == 1 ? validos[0] : null;
        string fonteFisico = fisico.HasValue ? "CRONOGRAMA_PERCENT_PHYSICAL_COMPLETED" : "SEM_DADO";
        // Tipo 1: a AUTORIDADE do avanço físico gerencial é a configuração do planejador por Document_Status,
        // em qualidade.DocumentStatusAvancoConfig. Percent_Physical_Completed da LDP/Cronograma fica apenas como
        // dado de origem/diagnóstico e NÃO prevalece sobre a regra configurada.
        if (tipo == 1)
        {
            fisico = d.PercentFisicoConfigurado is >= 0 and <= 100 ? d.PercentFisicoConfigurado : null;
            fonteFisico = fisico.HasValue ? "DOCUMENT_STATUS_CONFIGURADO" : "DOCUMENT_STATUS_SEM_CONFIGURACAO";
        }
        var classe = Classe(fisico);
        if (fisico is null && !gatesAmbiguos && validos.Length == 0 && invalidos.Length > 0) classe = "INVALIDO";
        string match = candidatos.Length == 0 ? "SEM_MATCH" : gatesAmbiguos || validos.Length > 1
            ? "AMBIGUO" : candidatos.Length == 1 ? "DOCUMENTO_EXATO" : "TAREFAS_CONCORDANTES";
        var atual = gatesAmbiguos ? null : DataConcordante(candidatos.Select(t => t.DataFimAtual));
        bool dataAmbigua = gatesAmbiguos || candidatos.Where(t => t.DataFimAtual.HasValue)
            .Select(t => t.DataFimAtual!.Value.Date).Distinct().Count() > 1;
        var baseline = gatesAmbiguos ? null : DataConcordante(candidatos.Select(t => t.DataFimBaseline));
        // AT = atividade de esforço/custo, não entregável documental. Preserva HH/custo e eventual avanço físico,
        // mas nunca é classificado como emitido/a emitir/atrasado/cancelado/sem prazo.
        if (d.IsActivity)
            return d with { AvancoFisico = fisico, ClasseFisica = classe, FonteAvancoFisico = fonteFisico,
                CorrelacaoCronograma = match, DataAlvo = null, FonteDataAlvo = "NAO_APLICAVEL_ATIVIDADE",
                StatusPrazo = "ATIVIDADE", StatusDocumento = "ATIVIDADE", Cancelado = false };
        // Prioridade EXATA pedida: 1) LDP Replanned_Date, 2) término atual do Cronograma, 3) LDP
        // Planned_Date_Baseline, 4) baseline do Cronograma. A LDP Planned_Date_Baseline (passo 3) é um
        // campo da LDP, sem nenhuma relação com a ambiguidade do Cronograma — nunca pode ser suprimida
        // por "dataAmbigua" (isso já fica corrigido só no passo 4, via a variável "baseline", que já
        // vem null quando ambígua). Um bug anterior aqui aplicava dataAmbigua também ao passo 3.
        // Data default/sentinela (MinValue, 1900-01-01...) nunca é prazo real: vira ausência e cai em SEM_PRAZO.
        static DateTime? Valida(DateTime? v) => v.HasValue && v.Value.Year >= 2000 ? v.Value.Date : null;
        var ldpReprogramada = Valida(d.DataReprogramada); var ldpBaseline = Valida(d.DataBaseline);
        atual = Valida(atual); baseline = Valida(baseline);
        DateTime? alvo; string fonte;
        if (ldpReprogramada.HasValue) { alvo = ldpReprogramada; fonte = "LDP_REPROGRAMADA"; }
        else if (atual.HasValue) { alvo = atual; fonte = "CRONOGRAMA_TERMINO_ATUAL"; }
        else if (ldpBaseline.HasValue) { alvo = ldpBaseline; fonte = "LDP_BASELINE_SEM_REPROGRAMACAO"; }
        else if (baseline.HasValue) { alvo = baseline; fonte = "CRONOGRAMA_BASELINE_SEM_REPROGRAMACAO"; }
        else { alvo = null; fonte = dataAmbigua ? "CRONOGRAMA_AMBIGUO" : "SEM_DADO"; }
        // Regra canônica: cancelado prevalece; emitido = APPROVED/APROVADO ou Emission_Date.
        var emissao = DocumentEmissionRules.Classify(d.StatusCliente, d.SituacaoEmissao, d.StatusInterno, d.DataEmissao);
        bool cancelado = emissao == DocumentEmissionRules.Cancelado;
        bool emitido = emissao == DocumentEmissionRules.Emitido;
        // APPROVED sem Emission_Date: sabemos que está emitido, mas não inventamos a data real da emissão.
        var prazo = cancelado ? "CANCELADO" : !alvo.HasValue || (emitido && !d.DataEmissao.HasValue) ? "SEM_DADO"
            : (d.DataEmissao?.Date ?? referencia.Date) > alvo.Value ? "ATRASADO" : "NO_PRAZO";
        // Partição exclusiva: CANCELADO -> EMITIDO -> somente o restante avalia prazo.
        var statusDocumento = cancelado ? "CANCELADO" : emitido ? "EMITIDO" : !alvo.HasValue ? "SEM_PRAZO"
            : alvo.Value >= referencia.Date ? "NO_PRAZO" : "ATRASADO";
        return d with { AvancoFisico = fisico, ClasseFisica = classe, Cancelado = cancelado, FonteAvancoFisico = fonteFisico,
            CorrelacaoCronograma = match, DataAlvo = alvo, FonteDataAlvo = fonte, StatusPrazo = prazo, StatusDocumento = statusDocumento };
    }
}

/// <summary>Um batch parametrizado por projeto, após autorização; sem N+1, cache ou escrita.</summary>
public static class PmoExecutiveData
{
    public static readonly string Query = $"""
        SELECT Nome_Projeto, Tipo_LDP FROM dbo.Projetos WHERE Project_ID_ACC=@projectId AND Is_Ativo=1;
        SELECT DISTINCT LdpAtual.ID_Registro, LdpAtual.NA_Document_Number, LdpAtual.Document_Title, LdpAtual.Discipline, LdpAtual.Planned_HH, LdpAtual.Planned_Cost,
            LdpAtual.Inicio_da_Semana, LdpAtual.Planned_Date_Baseline, LdpAtual.Replanned_Date, LdpAtual.Emission_Date, LdpAtual.Status_In_Flow, LdpAtual.Document_Status,
            LdpAtual.Situation, LdpAtual.Doc_Type_Code, LdpAtual.doc4, LdpAtual.Actual_HH, LdpAtual.Actual_Cost,
            LdpAtual.Percent_Physical_Completed,
            C.Disciplina_PT_Padrao, C.Disciplina_EN_Padrao, DP.Nome_Padrao AS Disciplina_Canonica_Cadastro,
            ISNULL(G.HH_Gerencial, 0) AS Actual_HH_Total_Corrigido,
            ISNULL(G.Custo_Gerencial, 0) AS Actual_Cost_Total_Corrigido,
            C.Gate_30_Ativo, C.Gate_30_Planned_Baseline, C.Gate_30_Replanned_Date, C.Gate_30_Planned_Physical, C.Gate_30_Physical_Completed, C.Gate_30_Document_Status, C.Gate_30_Situation,
            C.Gate_60_Ativo, C.Gate_60_Planned_Baseline, C.Gate_60_Replanned_Date, C.Gate_60_Planned_Physical, C.Gate_60_Physical_Completed, C.Gate_60_Document_Status, C.Gate_60_Situation,
            C.Gate_90_Ativo, C.Gate_90_Planned_Baseline, C.Gate_90_Replanned_Date, C.Gate_90_Planned_Physical, C.Gate_90_Physical_Completed, C.Gate_90_Document_Status, C.Gate_90_Situation
        FROM {APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela}
        -- 30/09/2026: HH realizado (Actual_HH_Total_Corrigido) e gates vêm da view homologada (mesmo ID_Registro da carga atual). Só colunas locais —
        -- o servidor vinculado da view não é consultado (≈0,2 s medido em CHS001).
        LEFT JOIN (SELECT ID_Registro, Disciplina_PT_Padrao, Disciplina_EN_Padrao, Actual_HH_Total_Corrigido, Actual_Cost_Corrigido,
                Gate_30_Ativo, Gate_30_Planned_Baseline, Gate_30_Replanned_Date, Gate_30_Planned_Physical, Gate_30_Physical_Completed, Gate_30_Document_Status, Gate_30_Situation,
                Gate_60_Ativo, Gate_60_Planned_Baseline, Gate_60_Replanned_Date, Gate_60_Planned_Physical, Gate_60_Physical_Completed, Gate_60_Document_Status, Gate_60_Situation,
                Gate_90_Ativo, Gate_90_Planned_Baseline, Gate_90_Replanned_Date, Gate_90_Planned_Physical, Gate_90_Physical_Completed, Gate_90_Document_Status, Gate_90_Situation
            FROM dbo.VW_Ldp_Unificada WHERE Project_ID_ACC=@projectId) C ON C.ID_Registro = LdpAtual.ID_Registro
        LEFT JOIN gestao.VW_TimesheetDocumentoGerencial G
          ON G.Project_ID_ACC = LdpAtual.Project_ID_ACC
         AND G.DocumentoCodigo = UPPER(LTRIM(RTRIM(LdpAtual.NA_Document_Number)))
        LEFT JOIN coordenacao.DisciplinaAlias DA
          ON UPPER(LTRIM(RTRIM(DA.Alias))) COLLATE Latin1_General_100_CI_AI =
             UPPER(LTRIM(RTRIM(LdpAtual.Discipline))) COLLATE Latin1_General_100_CI_AI
        LEFT JOIN coordenacao.DisciplinaPadrao DP
          ON DP.DisciplinaPadraoId=DA.DisciplinaPadraoId AND DP.Ativa=1
        WHERE LdpAtual.Project_ID_ACC=@projectId {APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano};
        -- Correção 17/09/2026 (Etapa 03): fonte trocada de gestao.TimesheetLancamento (ledger histórico) para o snapshot
        -- atual. 30/09/2026: lido pela view CORRIGIDA gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial (= TimesheetSnapshotAtual
        -- + De/Para ativo), com o DOCUMENTO CORRIGIDO (lançamentos por data; o HH realizado gerencial vem da coluna da view).
        -- CustoTimesheetInformado continua indisponível (não existe fisicamente em nenhuma das tabelas).
        SELECT Id, DocumentoCodigo_Corrigido AS DocumentoCodigo, Documento, Disciplina, DataLancamento, HorasTrabalhadas,
            CAST(NULL AS DECIMAL(18,6)) AS CustoTimesheetInformado, CustoCalculado
        FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial WHERE Project_ID_ACC=@projectId AND @details=1;
        SELECT Id_Registro, Numero_NA_Atividade, Disciplina, Nome_Tarefa, Weekly_Baseline_Date, Weekly_Replanned_Date,
            Weekly_Progress_Status FROM dbo.tb_WeeklyEngineeringSchedule
        WHERE Project_ID_ACC=@projectId AND Tipo_Registro='DADO' AND @details=1;
        IF EXISTS(SELECT 1 FROM dbo.Projetos WHERE Project_ID_ACC=@projectId AND Is_Ativo=1 AND Tipo_LDP=2)
            SELECT ID_Cronograma, NA_DOCUMENT_NUMBER, Disciplina_Cronograma AS Disciplina, Percent_Realizado AS Realizado,
                Termino_Linha_Base AS DataFimBaseline, Termino AS DataFimAtual, Gate_Percentual AS Gate, Fase,
                CASE WHEN Status_Validacao_Documento_Gate='VALIDO' THEN 1 ELSE 0 END ElegivelDocumento,
                CAST(ISNULL(Eh_Tarefa_Resumo,0) AS bit) Resumo, Trabalho, Campos_Customizados_JSON
            FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao WHERE Project_ID_ACC=@projectId AND Tipo_LDP=2;
        ELSE
            -- Shared table: Tipo 1 now persists summary, discipline and native Work.
            -- Documentary correlation below always excludes summary tasks.
            SELECT ID_Cronograma, NA_DOCUMENT_NUMBER, Disciplina_Cronograma AS Disciplina,
                PERCENT_PHYSICAL_COMPLETED AS Realizado, PLANNED_DATA_BASELINE AS DataFimBaseline,
                REPLANNED_DATE AS DataFimAtual, Gate_Percentual AS Gate, Fase,
                1 AS ElegivelDocumento, CAST(ISNULL(Eh_Tarefa_Resumo,0) AS bit) Resumo, Trabalho, CAST(NULL AS NVARCHAR(MAX)) AS Campos_Customizados_JSON
            FROM dbo.Cronograma_Projetos WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1;

        IF OBJECT_ID(N'qualidade.DocumentStatusAvancoConfig', N'U') IS NOT NULL
            SELECT Document_Status_Normalizado, Percentual
            FROM qualidade.DocumentStatusAvancoConfig
            WHERE Project_ID_ACC=@projectId AND Ativo=1;
        ELSE
            SELECT CAST(NULL AS nvarchar(255)) AS Document_Status_Normalizado,
                   CAST(NULL AS decimal(9,4)) AS Percentual
            WHERE 1=0;
        """;

    /// <summary>Coluna oficial de custo realizado (regra homologada no banco; lida pronta, nunca reconstruída).</summary>
    public const string ColunaCustoOficial = "Actual_Cost_Corrigido";

    /// <summary>
    /// Mesma consulta para um ambiente cuja view AINDA não tem Actual_Cost_Total_Corrigido: o custo oficial vem nulo e o custo
    /// realizado segue a fonte atual até a coluna existir (sem 503 e sem regra inventada).
    /// </summary>
    public static readonly string QuerySemCustoOficial = Query
        .Replace("C.Actual_Cost_Corrigido AS Actual_Cost_Total_Corrigido,", "CAST(NULL AS DECIMAL(18,4)) AS Actual_Cost_Total_Corrigido,")
        .Replace("Actual_HH_Total_Corrigido, Actual_Cost_Corrigido,", "Actual_HH_Total_Corrigido,");

    public static async Task<PmoExecutiveReport> ReadAsync(IConfiguration config, string projectId, bool details, CancellationToken ct, bool currentPlanningSnapshot = false)
    {
        using var connection = new SqlConnection(config.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(ct);
        // Apenas leitura: Norma ativa vinculada ao projeto, sem listas hardcoded ou associações por texto.
        // Reutiliza CoordinationDisciplineResolver como autoridade da sigla DOC3 -> nome da Norma.
        int? normaId = null;
        var mapaNorma = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var normaCmd = new SqlCommand("""
            SELECT n.NormaId, m.Sigla, m.Disciplina
              FROM coordenacao.ProjetoNorma pn
              JOIN coordenacao.Norma n ON n.NormaId=pn.NormaId AND n.Ativa=1
              LEFT JOIN coordenacao.NormaMapeamento m ON m.NormaId=n.NormaId
             WHERE pn.Project_ID_ACC=@projectId
            """, connection))
        {
            normaCmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
            await using var nr = await normaCmd.ExecuteReaderAsync(ct);
            while(await nr.ReadAsync(ct))
            {
                normaId = nr.GetInt32(0);
                if(!nr.IsDBNull(1))
                    mapaNorma[CoordinationDisciplineResolver.Normalize(nr.GetString(1))] = nr.GetString(2);
            }
        }
        // Nenhum cadastro global tem autoridade sobre a Norma vinculada ao projeto.
        var doc3Disciplinas = new NA_Connect.Services.Coordination.ProjectDoc3Disciplines(
            normaId, mapaNorma, new Dictionary<string, string>(StringComparer.Ordinal));
        bool custoOficial;
        using (var verifica = new SqlCommand("SELECT COL_LENGTH('dbo.VW_Ldp_Unificada', @coluna)", connection))
        {
            verifica.Parameters.Add("@coluna", SqlDbType.NVarChar, 128).Value = ColunaCustoOficial;
            custoOficial = await verifica.ExecuteScalarAsync(ct) is not (null or DBNull);
        }
        var baseQuery = custoOficial ? Query : QuerySemCustoOficial;
        var query = currentPlanningSnapshot ? baseQuery.Replace("FROM dbo.Cronograma_Projetos WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1;",
            "FROM dbo.Cronograma_Projetos WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1 " + APS.WebApp.Services.Planning.PmoPlanningFactsProvider.CurrentTipo1Predicate + ";") : baseQuery;
        using var command = new SqlCommand(query, connection) { CommandTimeout = 45 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        command.Parameters.Add("@details", SqlDbType.Bit).Value = details;
        using var reader = await command.ExecuteReaderAsync(ct);
        string? Text(string name) => reader[name] is DBNull ? null : Convert.ToString(reader[name], CultureInfo.InvariantCulture);
        decimal? Number(string name) => reader[name] is DBNull ? null : Convert.ToDecimal(reader[name], CultureInfo.InvariantCulture);
        DateTime? Date(string name) => reader[name] is DBNull ? null : Convert.ToDateTime(reader[name]).Date;
        string Id(string name, string domain) => $"{projectId}:{domain}:{Text(name)}";
        // Gate_XX_*_Physical na view = fração (1 = 100%) → pontos percentuais.
        static decimal? Pontos(decimal? fracao) => fracao.HasValue ? Math.Round(fracao.Value * 100m, 4) : null;
        string nome = projectId;
        int tipo = 0;
        if (await reader.ReadAsync(ct)) { nome = Text("Nome_Projeto") ?? projectId; tipo = (int)(Number("Tipo_LDP") ?? 0); }
        await reader.NextResultAsync(ct);
        var docsBrutos = new List<PmoDocumento>();
        while (await reader.ReadAsync(ct))
        {
            var codigoDoc = Text("NA_Document_Number");
            var segmentos = codigoDoc?.Split('-');
            // N&A Document Number = Doc1-Doc2-Doc3-Doc4-Doc5. Sem 3 segmentos, não inferir disciplina.
            var doc3 = segmentos is { Length: >= 3 } ? segmentos[2].Trim() : null;
            var resolve = CoordinationDisciplineResolver.Resolve(doc3, normaId, mapaNorma);
            var canonicaDoc3 = doc3Disciplinas.Canonical(codigoDoc);
            docsBrutos.Add(new(Id("ID_Registro", "ldp"), projectId, codigoDoc,
            Text("Document_Title"), Text("Discipline"), Number("Planned_HH"), Number("Planned_Cost"), Date("Inicio_da_Semana"),
            Date("Planned_Date_Baseline"), Date("Replanned_Date"), Date("Emission_Date"), Text("Status_In_Flow"),
            Text("Document_Status"), Text("Situation"), Number("Actual_HH"), Number("Actual_Cost"))
            {
                DocTypeCode = Text("Doc_Type_Code"),
                Doc4 = Text("doc4"),
                DisciplinaPtPadrao = Text("Disciplina_PT_Padrao"),
                DisciplinaEnPadrao = Text("Disciplina_EN_Padrao"),
                DisciplinaCanonicaCadastrada = Text("Disciplina_Canonica_Cadastro"),
                DisciplinaNorma = resolve.Matched ? resolve.Discipline : null,
                DisciplinaCanonicaDoc3 = canonicaDoc3,
                SiglaDoc3 = doc3,
                FonteDisciplinaNorma = resolve.Source,
                DiagnosticoDisciplinaNorma = resolve.Diagnostic,
                NormaCoordenacaoId = normaId,
                PercentFisicoLdp = Number("Percent_Physical_Completed"),
                HHRealizado = Number("Actual_HH_Total_Corrigido"),
                CustoRealizado = Number("Actual_Cost_Total_Corrigido"),
                Gates = tipo == 2 ? new[] { 30, 60, 90 }.Select(g => new PmoGate(g, reader[$"Gate_{g}_Ativo"] is bool a && a,
                    Date($"Gate_{g}_Planned_Baseline"), Date($"Gate_{g}_Replanned_Date"),
                    Pontos(Number($"Gate_{g}_Planned_Physical")), Pontos(Number($"Gate_{g}_Physical_Completed")))
                    { Cancelado = PmoExecutiveRules.Cancelado(Text($"Gate_{g}_Document_Status"), Text($"Gate_{g}_Situation")),
                      DocumentStatus = Text($"Gate_{g}_Document_Status"), Situation = Text($"Gate_{g}_Situation") }).ToArray() : Array.Empty<PmoGate>(),
            });
        }
        // Documento duplicado na LDP (mesmo Project_ID_ACC+NA_Document_Number em mais de uma linha) nunca
        // pode ser somado como orçamento único (dobraria HH/Custo Previsto) — mas também nunca pode
        // derrubar o relatório inteiro. Mesma decisão já tomada em gestao.vw_GestaoVista (WHERE
        // MultiplicidadeDocumento=1): exclui os documentos ambíguos deste conjunto, nunca lança exceção.
        var codigosAmbiguos = new HashSet<string>(docsBrutos.Where(d => !string.IsNullOrWhiteSpace(d.Codigo))
            .GroupBy(d => PmoExecutiveRules.Chave(d.Codigo)).Where(g => g.Count() > 1).Select(g => g.Key));
        var docs = currentPlanningSnapshot ? docsBrutos : docsBrutos.Where(d => string.IsNullOrWhiteSpace(d.Codigo) || !codigosAmbiguos.Contains(PmoExecutiveRules.Chave(d.Codigo))).ToList();
        await reader.NextResultAsync(ct);
        var lancamentos = new List<PmoLancamento>();
        // CustoRealizado é sempre CustoTimesheetInformado (custo real do lançamento) — nunca CustoCalculado
        // (reconciliação/auditoria). Ver AUDITORIA_FORGESYNC_NA/PROGRESSO.md, ajuste de 16/09/2026.
        while (await reader.ReadAsync(ct)) lancamentos.Add(new(Id("Id", "ts"), projectId, Text("DocumentoCodigo"), Text("Documento"),
            Text("Disciplina"), Date("DataLancamento"), Number("HorasTrabalhadas"), Number("CustoTimesheetInformado"))
            { CustoCalculado = Number("CustoCalculado") });
        await reader.NextResultAsync(ct);
        var weekly = new List<PmoWeekly>();
        while (await reader.ReadAsync(ct)) weekly.Add(new(Id("Id_Registro", "weekly"), projectId, Text("Numero_NA_Atividade"),
            Text("Disciplina"), Text("Nome_Tarefa"), Date("Weekly_Baseline_Date"), Date("Weekly_Replanned_Date"), Text("Weekly_Progress_Status")));
        await reader.NextResultAsync(ct);
        var tarefas = new List<PmoTarefa>();
        while (await reader.ReadAsync(ct)) tarefas.Add(new(Id("ID_Cronograma", "cronograma"), projectId, Text("NA_DOCUMENT_NUMBER"),
            Text("Disciplina"), Number("Realizado"), Date("DataFimBaseline"), Date("DataFimAtual"), (int?)Number("Gate"), Text("Fase"),
            Convert.ToInt32(reader["ElegivelDocumento"]) == 1, Convert.ToBoolean(reader["Resumo"]), Number("Trabalho"))
            { EsforcoHoras = PmoAvancoRules.LerEsforcoHoras(Text("Campos_Customizados_JSON")) });

        await reader.NextResultAsync(ct);
        var pesosPorStatus = new Dictionary<string, HashSet<decimal>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var status = DocumentStatusCanonical.Key(Text("Document_Status_Normalizado"));
            var percentual = Number("Percentual");
            if (status.Length == 0 || !percentual.HasValue) continue;
            if (!pesosPorStatus.TryGetValue(status, out var valores))
            {
                valores = new HashSet<decimal>();
                pesosPorStatus[status] = valores;
            }
            valores.Add(percentual.Value);
        }

        decimal? PesoConfigurado(string? documentStatus)
        {
            var key = DocumentStatusCanonical.Key(documentStatus);
            return pesosPorStatus.TryGetValue(key, out var valores) && valores.Count == 1
                ? valores.First()
                : null;
        }

        docs = docs.Select(d => d with { PercentFisicoConfigurado = PesoConfigurado(d.StatusCliente) }).ToList();

        var lookup = tarefas.Where(t => !string.IsNullOrWhiteSpace(t.DocumentoCodigo)).ToLookup(t => PmoExecutiveRules.Chave(t.DocumentoCodigo));
        var referencia = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "E. South America Standard Time").Date;
        return new(projectId, nome, tipo, referencia,
            docs.Select(d => PmoExecutiveRules.Completar(d, lookup[PmoExecutiveRules.Chave(d.Codigo)].ToArray(), tipo, referencia))
                .Select(d => tipo == 2 ? PmoAvancoRules.ComEsforcoDosGates(d, lookup[PmoExecutiveRules.Chave(d.Codigo)]) : d).ToArray(),
            lancamentos, weekly, tarefas) { CustoRealizadoOficial = custoOficial };
    }
}
