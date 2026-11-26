using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Documents;
using static APS.WebApp.Services.Reports.Dashboards.DashboardEngine;
using N = APS.WebApp.Services.Reports.Dashboards.ReportStatusNormalizer;

namespace APS.WebApp.Services.Reports.Dashboards;

/// <summary>
/// Definição das 12 dashboards do Relatório de Produção (ids = ids do registry do frontend). Cada uma: UM
/// SELECT por Project_ID_ACC = @projectId, dimensões (cross-filter), KPIs, gráficos e tabela RESUMIDA.
/// Referência visual: páginas do PBIX BML011.022 quando existem (ver RELATORIO_PRODUCAO_PROGRESSO.md);
/// hardcodes/erros do BI NÃO são reproduzidos. Nenhuma tela exibe custo.
/// </summary>
public static class ProductionDashboards
{
    private static DashboardDimension Dim(string key, string label, string coluna, Func<object?, (string, string)>? normalizar = null) =>
        new(key, label, r => (normalizar ?? N.Texto)(r[coluna]));

    private static DashboardDimension Presenca(string key, string label, string coluna, string com, string sem) =>
        new(key, label, r => r[coluna] is null or DBNull || (r[coluna] is string s && string.IsNullOrWhiteSpace(s)) ? ("SEM", sem) : ("COM", com));

    private static DashboardColumn Col(string key, string label, string coluna, string tipo = DashboardFormats.Texto) =>
        new(key, label, tipo, r => r[coluna]);

    private static DashboardColumn ColNorm(string key, string label, DashboardDimension d) =>
        new(key, label, DashboardFormats.Texto, r => d.Value(r) is var (k, l) && k == N.NullKey ? null : l);

    // ================================================================================ LDP ====
    // Tipo_LDP do projeto vem junto (mesma regra do PmoLdpSnapshot: só 2 é Tipo 2; resto é Tipo 1).
    private const string LdpColunas = "ID_Registro, Item, Discipline, NA_Document_Number, Document_Title, Format, Document_Status, Doc_Type_Code, doc4, " +
        "Status_In_Flow, Situation, Date_Comments_Received, Replanned_Date, Emission_Date, Transmittal_Number, Issued_Purpose, Revision, Data_Sincronizacao, " +
        "CASE WHEN EXISTS (SELECT 1 FROM dbo.Projetos P WHERE P.Project_ID_ACC = @projectId AND P.Tipo_LDP = 2) THEN 2 ELSE 1 END AS Tipo_LDP";
    // Dashboard LDP/Controle Documental = universo DOCUMENTAL. AT continua na LDP canônica para HH/custo, mas fica fora daqui.
    private static readonly string LdpSql = $"""
        SELECT {LdpColunas}, DP.Nome_Padrao AS Discipline_Canonical
        FROM {PmoLdpSnapshot.Tabela}
        LEFT JOIN coordenacao.DisciplinaAlias DA
          ON UPPER(LTRIM(RTRIM(DA.Alias))) COLLATE Latin1_General_100_CI_AI =
             UPPER(LTRIM(RTRIM(LdpAtual.Discipline))) COLLATE Latin1_General_100_CI_AI
        LEFT JOIN coordenacao.DisciplinaPadrao DP
          ON DP.DisciplinaPadraoId=DA.DisciplinaPadraoId AND DP.Ativa=1
        WHERE {LdpItemRules.SqlIsDocument("LdpAtual")}
        ORDER BY ID_Registro {PmoLdpSnapshot.OpcaoPlano};
        """;

    /// <summary>Gates Tipo 2 (mesma view de LdpDetailsReportRepository / CdeDocumentListRepository).</summary>
    public const string GatesTipo2Sql = """
        SELECT Ldp_Registro_Id, Gate_Percentual, Gate_Ativo, Document_Status, Situation
        FROM dbo.VW_Ldp_Documento_Gates_Ultima_Versao WHERE Project_ID_ACC = @projectId;
        """;

    /// <summary>
    /// Campos derivados da LDP (reuso de regras já validadas, nenhuma regra nova):
    ///  • Classe_Gestao = partição da Gestão à Vista / mapa de Produção pela regra canônica:
    ///    CANCELADO prevalece; EMITIDO = Document_Status APPROVED/APROVADO ou Emission_Date;
    ///    senão A_EMITIR (= No prazo + Atrasado + Sem prazo da Gestão).
    ///  • Status_Atual / Situacao_Atual: Tipo 1 = colunas da própria linha; Tipo 2 = gate atual escolhido pela MESMA
    ///    regra do Espelho Tipo 2 (LdpTipo2EspelhoService.EscolherGate) — no Tipo 2 esses campos da linha principal
    ///    ficam vazios (a informação vive nos gates 30/60/90).
    /// </summary>
    public static void MesclarLdp(IReadOnlyList<Dictionary<string, object?>> linhas, IReadOnlyList<Dictionary<string, object?>> gates)
    {
        var porDocumento = gates.Where(g => g["Ldp_Registro_Id"] is not null)
            .GroupBy(g => Convert.ToInt64(g["Ldp_Registro_Id"]))
            .ToDictionary(g => g.Key, g => g.Select(x => new APS.WebApp.Models.LdpTipo2Gate
            {
                GatePercentual = Convert.ToByte(x["Gate_Percentual"]), GateAtivo = x["Gate_Ativo"] is true,
                DocumentStatus = x["Document_Status"] as string ?? null!, Situation = x["Situation"] as string ?? null!,
            }).ToList());
        foreach (var l in linhas)
        {
            object? V(string coluna) => l.TryGetValue(coluna, out var v) ? v : null;
            bool tipo2 = V("Tipo_LDP") is { } t && Convert.ToInt32(t) == 2;
            var gate = tipo2 && V("ID_Registro") is { } id && porDocumento.TryGetValue(Convert.ToInt64(id), out var gs)
                ? APS.WebApp.Services.LdpTipo2.LdpTipo2EspelhoService.EscolherGate(gs) : null;
            // O nome oficial cadastrado é a autoridade para cards/gráficos/tabelas de disciplina.
            // Se ainda não foi padronizado, preserva o nome bruto para ficar visível até a correção.
            l["Discipline"] = V("Discipline_Canonical") ?? V("Discipline");
            l["Status_Atual"] = tipo2 ? gate?.DocumentStatus : V("Document_Status");
            l["Situacao_Atual"] = tipo2 ? gate?.Situation : V("Situation");
            l["Gate_Atual"] = gate is null ? null : $"{gate.GatePercentual}%";
            // Tipo 2: preserva os TRÊS gates do documento (1 linha = 1 documento; nada é multiplicado).
            // Tipo 1: campos presentes e inativos — nenhuma leitura de gate existe para ele.
            var doDocumento = tipo2 && V("ID_Registro") is { } idDoc && porDocumento.TryGetValue(Convert.ToInt64(idDoc), out var todos) ? todos : null;
            foreach (var p in GatesLdp)
            {
                var g = doDocumento?.FirstOrDefault(x => x.GatePercentual == p);
                l[$"Gate{p}_Ativo"] = g?.GateAtivo == true;
                l[$"Gate{p}_Status"] = g?.DocumentStatus;
                l[$"Gate{p}_Situacao"] = g?.Situation;
                l[$"Gate{p}_Classe"] = ClasseGate(g?.GateAtivo == true, g?.DocumentStatus, g?.Situation).Key;
            }
            l["Classe_Gestao"] = DocumentEmissionRules.Classify(
                V("Document_Status") as string,
                V("Situation") as string,
                V("Status_In_Flow") as string,
                V("Emission_Date") is DateTime emissionDate ? emissionDate : null) switch
            {
                DocumentEmissionRules.Cancelado => "CANCELADO",
                DocumentEmissionRules.Emitido => "EMITIDO",
                _ => "A_EMITIR",
            };
        }
    }

    // ================================================================ LDP Tipo 2: gates 30/60/90 ====
    public static readonly int[] GatesLdp = { 30, 60, 90 };
    public const string GateInativo = "NA";

    /// <summary>
    /// Classe de EMISSÃO de um gate (dimensão gate30/60/90) — valores reais auditados em 29/09/2026 na
    /// VW_Ldp_Documento_Gates_Ultima_Versao; reutiliza ReportStatusNormalizer (sem tabela paralela):
    ///  • inativo → NA ("N/A"): fora de todas as contagens do gate;
    ///  • CANCELADO se Document_Status OU Situation normalizados = CANCELADO (CANCELED/CANCELLED/CANCELADO) — mesma
    ///    precedência comprovada da regra geral (cancelamento prevalece sobre emissão; PmoExecutiveRules);
    ///  • senão a Situation normalizada: EMITIDO (ISSUED), A_EMITIR (TO BE ISSUED) ou o próprio texto
    ///    (ex.: NOT TO BE ISSUED) / "Não informado". Emission_Date da linha principal NÃO participa.
    /// </summary>
    public static (string Key, string Label) ClasseGate(bool ativo, string? status, string? situacao)
    {
        if (!ativo) return (GateInativo, "N/A");
        if (N.StatusDoc(status).Key == "CANCELADO" || N.Situation(situacao).Key == "CANCELADO") return ("CANCELADO", "Cancelado");
        return N.Situation(situacao);
    }

    /// <summary>Status documental normalizado de um gate (dimensão gate30Status/…); inativo → NA.</summary>
    public static (string Key, string Label) StatusGate(bool ativo, string? status) => ativo ? N.StatusDoc(status) : (GateInativo, "N/A");

    private static bool GateAtivo(DashboardRow r, int p) => r[$"Gate{p}_Ativo"] is true;

    private static DashboardDimension DimGate(int p) =>
        new($"gate{p}", $"Gate {p}%", r => ClasseGate(GateAtivo(r, p), r[$"Gate{p}_Status"] as string, r[$"Gate{p}_Situacao"] as string));

    private static DashboardDimension DimGateStatus(int p) =>
        new($"gate{p}Status", $"Status {p}%", r => StatusGate(GateAtivo(r, p), r[$"Gate{p}_Status"] as string));

    /// <summary>Célula da tabela Tipo 2: rótulo curto + Document Status/Situation do gate para o detalhe (tooltip).</summary>
    public sealed record GateCelula(string Rotulo, bool Ativo, string Classe, string? DocumentStatus, string? Situation);

    private static GateCelula CelulaGate(DashboardRow r, int p)
    {
        bool ativo = GateAtivo(r, p);
        string? status = r[$"Gate{p}_Status"] as string, situacao = r[$"Gate{p}_Situacao"] as string;
        var (classe, rotuloClasse) = ClasseGate(ativo, status, situacao);
        if (!ativo) return new GateCelula("N/A", false, classe, status, situacao);
        // Sem precedência inventada: mostra a classe de emissão e, quando acrescenta informação, o status documental.
        var (statusKey, statusLabel) = N.StatusDoc(status);
        bool acrescenta = statusKey != N.NullKey && statusKey != classe;
        return new GateCelula(acrescenta ? $"{rotuloClasse} · {statusLabel}" : rotuloClasse, true, classe, status?.Trim(), situacao?.Trim());
    }

    private static readonly Dictionary<string, string> RotulosGestao = new()
    {
        ["CANCELADO"] = "Cancelado", ["EMITIDO"] = "Emitido", ["A_EMITIR"] = "A emitir",
    };

    private static DashboardDimension Gestao() =>
        new("gestao", "Situação (Gestão)", r => r["Classe_Gestao"] is string k ? (k, RotulosGestao[k]) : (N.NullKey, N.NullLabel));

    private static DashboardDimension Documento() => Dim("documento", "Documento", "NA_Document_Number");

    /// <summary>Tipo_LDP do dataset (mesma regra de LdpColunas: 2 = Tipo 2; resto = Tipo 1).</summary>
    public static int TipoLdp(IReadOnlyList<DashboardRow> linhas) =>
        linhas.Any(r => r["Tipo_LDP"] is { } t && t is not DBNull && Convert.ToInt32(t) == 2) ? 2 : 1;

    /// <summary>
    /// LDP: a MESMA base documental para Gerencial e Operacional. A definição efetiva depende do Tipo_LDP do projeto:
    ///  • Tipo 1 — exatamente a tela padrão (nenhum KPI/coluna/matriz/dimensão de gate existe na resposta);
    ///  • Tipo 2 — Total + Emitidos Geral (regra documental da Gestão à Vista) + matriz STATUS POR FASE 30/60/90
    ///    (cada gate do documento conta no máximo uma vez por métrica do gate; o total continua 1 por documento).
    /// </summary>
    private static DashboardDefinition Ldp()
    {
        var disciplina = Dim("disciplina", "Disciplina", "Discipline");
        var gestao = Gestao();
        var status = Dim("status", "Status atual", "Status_Atual", N.StatusDoc);
        var situacao = Dim("situacao", "Situação atual", "Situacao_Atual", N.Situation);
        var formato = Dim("formato", "Formato", "Format");
        var documento = Documento();
        var dimensoesPadrao = new[] { disciplina, gestao, status, situacao, formato, documento };
        var dimensoesGate = GatesLdp.SelectMany(p => new[] { DimGate(p), DimGateStatus(p) }).ToArray();
        const string source = "LDP atual do projeto (PmoLdpSnapshot / Ldp_Unificada; Tipo 2: gates)";

        var tipo1 = new DashboardDefinition
        {
            Id = "ldp", Title = "LDP", Source = source, Sql = LdpSql,
            ComplementSql = GatesTipo2Sql, Merge = MesclarLdp, TableRowFilter = "documento",
            Dimensions = dimensoesPadrao,
            Kpis = new DashboardKpi[]
            {
                new("total", "Total", "Documentos na LDP atual", Contagem()),
                new("emitidos", "Emitidos", "Aprovados (Document Status = APPROVED/APROVADO) ou com data de emissão, excluindo cancelados", ContagemOnde(gestao, "EMITIDO"), FilterDimension: "gestao", FilterValue: "EMITIDO"),
                new("cancelados", "Cancelados", "Cancelado no status, situação ou Status in the Flow (regra da Gestão à Vista)", ContagemOnde(gestao, "CANCELADO"), FilterDimension: "gestao", FilterValue: "CANCELADO"),
                new("revisaoCliente", "Revisão do cliente", "Status atual = Em análise do cliente / Client Review", ContagemOnde(status, "EM_ANALISE_CLIENTE"), FilterDimension: "status", FilterValue: "EM_ANALISE_CLIENTE"),
                new("aEmitir", "A emitir", "Não cancelado, não aprovado e sem evidência de emissão (No prazo + Atrasado + Sem prazo da Gestão)", ContagemOnde(gestao, "A_EMITIR"), FilterDimension: "gestao", FilterValue: "A_EMITIR"),
            },
            Charts = new DashboardChart[]
            {
                new("porGestao", "Situação de emissão (Gestão)", DashboardKinds.Rosca, "gestao"),
                new("porStatus", "Documentos por status atual", DashboardKinds.Coluna, "status"),
                new("porSituacao", "Documentos por situação atual", DashboardKinds.Coluna, "situacao"),
                new("porDisciplina", "Documentos por disciplina", DashboardKinds.Barra, "disciplina"),
            },
            Table = new[]
            {
                Col("item", "Item", "Item"), Col("disciplina", "Disciplina", "Discipline"), Col("na", "N&A Document Number", "NA_Document_Number"),
                Col("titulo", "Document Title", "Document_Title"), Col("formato", "Format", "Format"), Col("status", "Document Status (atual)", "Status_Atual"),
                Col("statusInFlow", "Status in the Flow", "Status_In_Flow"),
                Col("comentarios", "Date Comments Were Received", "Date_Comments_Received"),
                Col("reprogramada", "Replanned Date", "Replanned_Date", DashboardFormats.Data), Col("emissao", "Emission Date", "Emission_Date", DashboardFormats.Data),
            },
            Notes = new[]
            {
                "Emitidos/Cancelados/A emitir usam a mesma regra da Gestão à Vista e do mapa de Produção (PmoExecutiveRules).",
                "Clique numa linha da tabela para filtrar a tela pelo documento. Status/situação agrupam PT/EN equivalentes.",
            },
        };

        var fases = GatesLdp.Select(p => new DashboardOption($"{p}", $"{p}%")).ToArray();
        var linhasMatriz = new[]
        {
            new DashboardOption("emitidos", "Emitidos"), new DashboardOption("cancelados", "Cancelados"),
            new DashboardOption("analiseCliente", "Em análise do cliente"), new DashboardOption("aEmitir", "A emitir"),
        };
        var celulas = GatesLdp.SelectMany(p => new[]
        {
            new DashboardMatrixCell("emitidos", $"{p}", $"gate{p}", "EMITIDO"),
            new DashboardMatrixCell("cancelados", $"{p}", $"gate{p}", "CANCELADO"),
            new DashboardMatrixCell("analiseCliente", $"{p}", $"gate{p}Status", "EM_ANALISE_CLIENTE"),
            new DashboardMatrixCell("aEmitir", $"{p}", $"gate{p}", "A_EMITIR"),
        }).ToArray();

        var tipo2 = new DashboardDefinition
        {
            Id = "ldp", Title = "LDP", Source = source, Sql = LdpSql,
            ComplementSql = GatesTipo2Sql, Merge = MesclarLdp, TableRowFilter = "documento",
            Dimensions = dimensoesPadrao.Concat(dimensoesGate).ToArray(),
            Kpis = new DashboardKpi[]
            {
                new("total", "Total Documentos", "Documentos na LDP atual (1 por documento, independentemente dos gates)", Contagem()),
                new("emitidosGeral", "Emitidos Geral", "Leitura documental: APPROVED/APROVADO ou com data de emissão, excluindo cancelados — não é a soma dos gates",
                    ContagemOnde(gestao, "EMITIDO"), FilterDimension: "gestao", FilterValue: "EMITIDO"),
                new("revisaoAtual", "Revisão Atual", "Gate atual em análise do cliente (não consolida os três gates)",
                    ContagemOnde(status, "EM_ANALISE_CLIENTE"), FilterDimension: "status", FilterValue: "EM_ANALISE_CLIENTE"),
            },
            Matrices = new[] { new DashboardMatrix("statusPorFase", "Status por fase", linhasMatriz, fases, celulas) },
            Charts = new DashboardChart[]
            {
                new("porStatus", "Documentos por status (gate atual)", DashboardKinds.Coluna, "status"),
                new("porSituacao", "Documentos por situação (gate atual)", DashboardKinds.Coluna, "situacao"),
                new("porDisciplina", "Documentos por disciplina", DashboardKinds.Barra, "disciplina"),
            },
            Table = new[]
            {
                Col("item", "Item", "Item"), Col("disciplina", "Disciplina", "Discipline"), Col("na", "N&A Document Number", "NA_Document_Number"),
                Col("titulo", "Document Title", "Document_Title"),
                new DashboardColumn("gate30", "30%", DashboardFormats.Gate, r => CelulaGate(r, 30)),
                new DashboardColumn("gate60", "60%", DashboardFormats.Gate, r => CelulaGate(r, 60)),
                new DashboardColumn("gate90", "90%", DashboardFormats.Gate, r => CelulaGate(r, 90)),
                Col("gate", "Gate atual", "Gate_Atual"), Col("status", "Status (gate atual)", "Status_Atual"),
                Col("reprogramada", "Replanned Date", "Replanned_Date", DashboardFormats.Data), Col("emissao", "Emission Date", "Emission_Date", DashboardFormats.Data),
            },
            Notes = new[]
            {
                "Projeto Tipo 2 (gates 30/60/90): um mesmo documento pode ser emitido em mais de uma fase — cada gate conta uma vez na sua coluna; o Total continua 1 por documento.",
                "Emitidos Geral usa a regra documental da Gestão à Vista (não é a soma dos gates). Cancelamento é lido por fase na matriz.",
                "Gate inativo (N/A) não entra em nenhuma contagem daquele gate. Clique numa célula da matriz para filtrar os documentos.",
            },
        };

        // Base = Tipo 1 + dimensões de gate (só para validar filtros); a definição efetiva vem do dataset.
        return new DashboardDefinition
        {
            Id = tipo1.Id, Title = tipo1.Title, Source = tipo1.Source, Sql = tipo1.Sql, ComplementSql = tipo1.ComplementSql, Merge = tipo1.Merge,
            TableRowFilter = tipo1.TableRowFilter, Dimensions = tipo2.Dimensions, Kpis = tipo1.Kpis, Charts = tipo1.Charts, Table = tipo1.Table, Notes = tipo1.Notes,
            Especializar = linhas => TipoLdp(linhas) == 2 ? tipo2 : tipo1,
            Metadata = linhas => new Dictionary<string, object?> { ["tipoLdp"] = TipoLdp(linhas) },
        };
    }

    // ================================================================= CONTROLE DOCUMENTAL ====
    private static DashboardDefinition ControleDocumental()
    {
        var disciplina = Dim("disciplina", "Disciplina", "Discipline");
        var fluxo = Dim("statusInFlow", "Status in the Flow", "Status_In_Flow");
        var status = Dim("status", "Status atual", "Status_Atual", N.StatusDoc);
        var proposito = Dim("proposito", "Propósito de emissão", "Issued_Purpose");
        var revisao = Dim("revisao", "Revisão", "Revision");
        var emissao = Presenca("emissao", "Emissão", "Emission_Date", "Com data de emissão", "Sem data de emissão");
        var transmittal = Presenca("transmittal", "Transmittal", "Transmittal_Number", "Com transmittal", "Sem transmittal");
        return new()
        {
            Id = "controle-documental", Title = "Controle Documental", Source = "LDP atual do projeto (PmoLdpSnapshot / Ldp_Unificada; Tipo 2: gates)", Sql = LdpSql,
            ComplementSql = GatesTipo2Sql, Merge = MesclarLdp, TableRowFilter = "documento",
            Dimensions = new[] { disciplina, fluxo, status, proposito, revisao, emissao, transmittal, Documento() },
            Kpis = new DashboardKpi[]
            {
                new("total", "Documentos", "Na LDP atual", Contagem()),
                new("emitidos", "Com emissão", "Data de emissão preenchida", ContagemOnde(emissao, "COM"), FilterDimension: "emissao", FilterValue: "COM"),
                new("semEmissao", "Sem emissão", "Data de emissão vazia", ContagemOnde(emissao, "SEM"), FilterDimension: "emissao", FilterValue: "SEM"),
                new("comTransmittal", "Com transmittal", "Transmittal Number preenchido (GRD)", ContagemOnde(transmittal, "COM"), FilterDimension: "transmittal", FilterValue: "COM"),
                new("semTransmittal", "Sem transmittal", "Transmittal Number vazio", ContagemOnde(transmittal, "SEM"), FilterDimension: "transmittal", FilterValue: "SEM"),
            },
            Charts = new DashboardChart[]
            {
                new("porFluxo", "Status in the Flow", DashboardKinds.Barra, "statusInFlow"),
                new("porStatus", "Status atual do documento", DashboardKinds.Barra, "status"),
                new("porEmissao", "Emissão", DashboardKinds.Rosca, "emissao"),
                new("porProposito", "Propósito de emissão", DashboardKinds.Barra, "proposito"),
                new("porRevisao", "Revisão", DashboardKinds.Coluna, "revisao"),
                new("porDisciplina", "Documentos por disciplina", DashboardKinds.Barra, "disciplina"),
            },
            Table = new[]
            {
                Col("na", "N&A Document Number", "NA_Document_Number"), Col("titulo", "Título", "Document_Title"), Col("disciplina", "Disciplina", "Discipline"),
                Col("revisao", "Revisão", "Revision"), Col("emissao", "Emissão", "Emission_Date", DashboardFormats.Data),
                Col("transmittal", "Transmittal", "Transmittal_Number"), Col("proposito", "Propósito", "Issued_Purpose"), Col("statusInFlow", "Status in the Flow", "Status_In_Flow"),
            },
            Notes = new[] { "\"Em fluxo\" não é KPI: o conjunto de valores de Status in the Flow que significa \"em fluxo\" não está definido (REGRA_BI_A_CONFIRMAR) — ver o gráfico de Status in the Flow." },
        };
    }

    // ============================================================= PROGRAMAÇÃO (Weekly) ====
    private const string WeeklySql = """
        SELECT Id_Registro, Numero_NA_Atividade, Nome_Tarefa, Disciplina, Recurso, Weekly_Phase, Weekly_Progress_Status,
               Weekly_HH_Previsto, Weekly_Baseline_Date, Weekly_Replanned_Date, Data_Sincronizacao
        FROM dbo.tb_WeeklyEngineeringSchedule
        WHERE Project_ID_ACC = @projectId AND Tipo_Registro = N'DADO'
        ORDER BY Id_Registro;
        """;

    // Semana da atividade — REGRA OFICIAL já usada por PlanningSourceReader (programação semanal):
    // data programada = COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date); atividade sem data fica fora de qualquer
    // semana (aqui: grupo "Sem data (contínuas)" — linhas de coordenação/gestão contínua, sem entrega na semana).
    // Semana de domingo a sábado = convenção de PmoLdpSnapshot.Inicio_da_Semana e VW_N_A_Connect_Weekly_Control.
    public static DateTime? DataProgramada(DashboardRow r) => r.Date("Weekly_Replanned_Date") ?? r.Date("Weekly_Baseline_Date");
    public static DateTime InicioSemana(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);
    public const string SemData = "SEM_DATA";

    private static DashboardDimension Semana() => new("semana", "Semana", r =>
        DataProgramada(r) is DateTime d && InicioSemana(d) is var ini
            ? (ini.ToString("yyyy-MM-dd"), $"{ini:dd/MM}–{ini.AddDays(6):dd/MM/yy}")
            : (SemData, "Sem data (contínuas)"));

    /// <summary>Semana padrão do Nivelamento: a semana atual; se não houver atividade nela, a semana mais próxima com atividade.</summary>
    public static IReadOnlyDictionary<string, string[]> SemanaPadrao(IReadOnlyList<DashboardRow> linhas, DateTime hoje)
    {
        var semanas = linhas.Select(DataProgramada).Where(d => d.HasValue).Select(d => InicioSemana(d!.Value)).Distinct().ToList();
        if (semanas.Count == 0) return new Dictionary<string, string[]>();
        var atual = InicioSemana(hoje);
        var escolhida = semanas.Contains(atual) ? atual : semanas.OrderBy(x => Math.Abs((x - atual).TotalDays)).ThenBy(x => x).First();
        return new Dictionary<string, string[]> { ["semana"] = new[] { escolhida.ToString("yyyy-MM-dd") } };
    }

    private static DashboardDefinition Programacao()
    {
        var disciplina = Dim("disciplina", "Disciplina", "Disciplina");
        var status = Dim("status", "Status", "Weekly_Progress_Status", N.StatusDoc);
        return new()
        {
            Id = "programacao", Title = "Programação", Source = "Programação semanal (tb_WeeklyEngineeringSchedule)", Sql = WeeklySql,
            Dimensions = new[] { disciplina, status },
            Kpis = new DashboardKpi[]
            {
                new("total", "Qtd. atividades", "Atividades programadas", Contagem()),
                new("naoIniciado", "Não iniciado", "Not started / Não iniciado", ContagemOnde(status, "NAO_INICIADO"), FilterDimension: "status", FilterValue: "NAO_INICIADO"),
                new("elaboracao", "Em elaboração", "Elaboration / Em elaboração / Elaborando", ContagemOnde(status, "EM_ELABORACAO"), FilterDimension: "status", FilterValue: "EM_ELABORACAO"),
                new("comentarios", "Atendimento a comentários", "Addressing comments / Atendimento a comentários", ContagemOnde(status, "ATENDIMENTO_COMENTARIOS"), FilterDimension: "status", FilterValue: "ATENDIMENTO_COMENTARIOS"),
            },
            Charts = new DashboardChart[]
            {
                new("porDisciplina", "Documentos/atividades por disciplina", DashboardKinds.Coluna, "disciplina"),
                new("porStatus", "Atividades por status", DashboardKinds.Rosca, "status"),
            },
            Table = new[]
            {
                Col("disciplina", "Disciplina", "Disciplina"), Col("atividade", "N&A Document Number", "Numero_NA_Atividade"),
                Col("tarefa", "Document Title", "Nome_Tarefa"), ColNorm("status", "Progress Status", status),
                // Colunas 9/10 do BI, identificadas pelo cabeçalho no cache do PBIX: BASELINE DATE e REPLANNED DATE.
                Col("baseline", "Baseline Date", "Weekly_Baseline_Date", DashboardFormats.Data),
                Col("reprogramada", "Replanned Date", "Weekly_Replanned_Date", DashboardFormats.Data),
            },
            Notes = new[] { "O filtro fixo do BI (exclui CANCELED/CLIENT REVIEW) não é aplicado — use o gráfico de status para filtrar." },
        };
    }

    private static DashboardDefinition NivelamentoProgramacao()
    {
        var recurso = Dim("recurso", "Recurso", "Recurso");
        var disciplina = Dim("disciplina", "Disciplina", "Disciplina");
        var fase = Dim("fase", "Fase", "Weekly_Phase");
        var hh = Presenca("hh", "HH planejado", "Weekly_HH_Previsto", "Com HH", "Sem HH");
        var semana = Semana();
        return new()
        {
            Id = "nivelamento-programacao", Title = "Nivelamento Programação", Source = "Programação semanal (tb_WeeklyEngineeringSchedule)", Sql = WeeklySql,
            Dimensions = new[] { semana, recurso, disciplina, fase, hh },
            DefaultFilters = SemanaPadrao,
            Kpis = new DashboardKpi[]
            {
                new("total", "Atividades", "Atividades no filtro (semana selecionada)", Contagem()),
                new("hhPlanejado", "HH planejado", "Soma do HH previsto das atividades no filtro — por padrão, a semana atual", SomaConhecida("Weekly_HH_Previsto"), DashboardFormats.Horas),
                new("comHh", "Com HH", "HH previsto informado", ContagemOnde(hh, "COM"), FilterDimension: "hh", FilterValue: "COM"),
                new("semHh", "Sem HH", "HH previsto vazio (não conta como zero)", ContagemOnde(hh, "SEM"), FilterDimension: "hh", FilterValue: "SEM"),
                new("recursos", "Recursos", "Recursos distintos com atividade", DistintosNaoVazios(recurso)),
                new("semData", "Atividades sem data", "Contínuas (coordenação/gestão): fora do HH semanal", ContagemOnde(semana, SemData), FilterDimension: "semana", FilterValue: SemData),
            },
            Charts = new DashboardChart[]
            {
                new("hhPorSemana", "HH planejado por semana", DashboardKinds.Coluna, "semana", "HH", SomaMedida("Weekly_HH_Previsto"), DashboardFormats.Horas, OrdenarPorChave: true),
                new("hhPorRecurso", "HH planejado por recurso", DashboardKinds.Barra, "recurso", "HH", SomaMedida("Weekly_HH_Previsto"), DashboardFormats.Horas),
                new("hhPorDisciplina", "HH planejado por disciplina", DashboardKinds.Barra, "disciplina", "HH", SomaMedida("Weekly_HH_Previsto"), DashboardFormats.Horas),
                new("atividadesPorRecurso", "Atividades por recurso", DashboardKinds.Barra, "recurso"),
                new("atividadesPorDisciplina", "Atividades por disciplina", DashboardKinds.Coluna, "disciplina"),
                new("porHh", "Atividades com/sem HH", DashboardKinds.Rosca, "hh"),
            },
            Table = new[]
            {
                Col("atividade", "Nº N&A", "Numero_NA_Atividade"), Col("tarefa", "Tarefa", "Nome_Tarefa"), Col("disciplina", "Disciplina", "Disciplina"),
                Col("recurso", "Recurso", "Recurso"), Col("hh", "HH previsto", "Weekly_HH_Previsto", DashboardFormats.Horas),
                Col("baseline", "Baseline", "Weekly_Baseline_Date", DashboardFormats.Data),
            },
            Notes = new[]
            {
                "HH = Weekly_HH_Previsto da programação, somado para as atividades da semana selecionada (data reprogramada, senão baseline; semana de domingo a sábado — mesma regra do planejamento semanal). A tela abre na semana atual; troque clicando em \"HH planejado por semana\".",
                "Atividades sem data (linhas contínuas de coordenação/gestão) não entram em nenhuma semana — antes elas inflavam o total (ex.: 1.906 h de 3.420 h no CHS001).",
                "Capacidade / HH disponível por recurso não existe em nenhuma fonte sincronizada: a tela mostra a CARGA planejada, sem comparação com capacidade (REGRA_BI_A_CONFIRMAR).",
            },
        };
    }

    private static DashboardDefinition ControleProgramacao()
    {
        var status = Dim("status", "Status", "Weekly_Progress_Status", N.StatusDoc);
        var disciplina = Dim("disciplina", "Disciplina", "Disciplina");
        var recurso = Dim("recurso", "Recurso", "Recurso");
        var fase = Dim("fase", "Fase", "Weekly_Phase");
        var reprogramacao = new DashboardDimension("reprogramacao", "Reprogramação", r =>
            r.Date("Weekly_Baseline_Date") is not DateTime b ? ("SEM_BASELINE", "Sem baseline")
            : r.Date("Weekly_Replanned_Date") is not DateTime rp ? ("SEM_REPROGRAMACAO", "Sem data reprogramada")
            : rp > b ? ("APOS_BASELINE", "Reprogramada após a baseline") : ("ATE_BASELINE", "Reprogramada até a baseline"));
        var semana = Semana();
        return new()
        {
            Id = "controle-programacao", Title = "Controle Programação", Source = "Programação semanal (tb_WeeklyEngineeringSchedule)", Sql = WeeklySql,
            Dimensions = new[] { status, disciplina, recurso, fase, reprogramacao, semana },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total programado", "Atividades programadas", Contagem()),
                new("aposBaseline", "Reprogramadas após baseline", "Data reprogramada posterior à baseline", ContagemOnde(reprogramacao, "APOS_BASELINE"), FilterDimension: "reprogramacao", FilterValue: "APOS_BASELINE"),
                new("semBaseline", "Sem baseline", "Baseline não informada", ContagemOnde(reprogramacao, "SEM_BASELINE"), FilterDimension: "reprogramacao", FilterValue: "SEM_BASELINE"),
                new("hhPlanejado", "HH planejado (filtro atual)", "Soma do HH previsto no filtro; sem filtro de semana inclui toda a programação e as atividades contínuas", SomaConhecida("Weekly_HH_Previsto"), DashboardFormats.Horas),
            },
            Charts = new DashboardChart[]
            {
                new("porSemana", "Atividades por semana", DashboardKinds.Coluna, "semana", OrdenarPorChave: true),
                new("porStatus", "Atividades por status", DashboardKinds.Rosca, "status"),
                new("porDisciplina", "Atividades por disciplina", DashboardKinds.Barra, "disciplina"),
                new("porRecurso", "Atividades por recurso", DashboardKinds.Barra, "recurso"),
                new("porReprogramacao", "Baseline × reprogramação", DashboardKinds.Rosca, "reprogramacao"),
                new("porFase", "Atividades por fase", DashboardKinds.Coluna, "fase"),
            },
            Table = new[]
            {
                Col("atividade", "Nº N&A", "Numero_NA_Atividade"), Col("tarefa", "Tarefa", "Nome_Tarefa"), Col("disciplina", "Disciplina", "Disciplina"),
                Col("recurso", "Recurso", "Recurso"), ColNorm("status", "Status", status),
                Col("baseline", "Baseline", "Weekly_Baseline_Date", DashboardFormats.Data), Col("reprogramada", "Reprogramada", "Weekly_Replanned_Date", DashboardFormats.Data),
            },
            Notes = new[] { "% previsto × % realizado da semana não é exibido: os campos são texto na fonte e a semântica não está garantida (REGRA_BI_A_CONFIRMAR)." },
        };
    }

    // ============================================================== DESVIO DE QUALIDADE ====
    private static DashboardDefinition DesvioQualidade()
    {
        var responsavel = Dim("responsavel", "Responsável", "Responsavel");
        var disciplina = Dim("disciplina", "Disciplina", "Disciplina");
        var tipo = Dim("tipo", "Tipo de inconsistência", "TipoInconsistencia");
        var conclusao = Presenca("conclusao", "Conclusão", "DataConclusao", "Com data de conclusão", "Sem data de conclusão");
        return new()
        {
            Id = "desvio-qualidade", Title = "Desvio de Qualidade", Source = "[dbo].[DESVIO DE QUALIDADE] (Etapa 17)",
            Sql = """
                SELECT Id, Disciplina, NumeroDocumento, QuantidadeDocumentos, DescricaoInconsistencia, TipoInconsistencia, Responsavel, DataConclusao, Data_Sincronizacao
                FROM dbo.[DESVIO DE QUALIDADE] WHERE Project_ID_ACC = @projectId ORDER BY Id;
                """,
            Dimensions = new[] { responsavel, disciplina, tipo, conclusao },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total de desvios", "Registros de desvio de qualidade", Contagem()),
                new("semConclusao", "Sem conclusão", "Data de conclusão vazia", ContagemOnde(conclusao, "SEM"), FilterDimension: "conclusao", FilterValue: "SEM"),
                new("comConclusao", "Com conclusão", "Data de conclusão preenchida", ContagemOnde(conclusao, "COM"), FilterDimension: "conclusao", FilterValue: "COM"),
                new("documentos", "Documentos envolvidos", "Soma de QuantidadeDocumentos informados", SomaConhecida("QuantidadeDocumentos")),
            },
            Charts = new DashboardChart[]
            {
                new("porResponsavel", "Desvios por responsável", DashboardKinds.Barra, "responsavel"),
                new("porDisciplina", "Desvios por disciplina", DashboardKinds.Rosca, "disciplina"),
                new("porTipo", "Tipo de desvio", DashboardKinds.Coluna, "tipo"),
            },
            Table = new[]
            {
                Col("disciplina", "Disciplina", "Disciplina"), Col("documento", "Número do documento", "NumeroDocumento"),
                Col("descricao", "Descrição da inconsistência", "DescricaoInconsistencia"), Col("responsavel", "Responsável", "Responsavel"),
                Col("conclusao", "Data da conclusão", "DataConclusao", DashboardFormats.Data),
            },
            Notes = new[] { "Os cards do BI nesta página contavam a tabela RNC (erro do BI) — aqui todos os números vêm de DESVIO DE QUALIDADE." },
        };
    }

    // ================================================================== CAP INTERNO/EXTERNO ====
    private static DashboardDefinition Cap(string id, string titulo, string view)
    {
        var status = Dim("status", "Status", "Status");
        var afe = Dim("afe", "AFE", "AFE");
        var empresa = Dim("empresa", "Empresa", "Company");
        var responsavel = Dim("responsavel", "Responsável", "Name2");
        var frente = Dim("frente", "Frente de serviço", "FRONT_OF_SERVICES");
        var critico = Dim("critico", "Crítico", "Critical");
        return new()
        {
            Id = id, Title = titulo, Source = $"dbo.{view}",
            // Linha-modelo (só o ID pré-numerado, todos os demais campos vazios) não é ação — mesmo princípio do
            // PmoLdpSnapshot para linhas-modelo da LDP.
            Sql = $"""
                SELECT Id_Registro, ID, Name, Description, Status, Company, AFE, FRONT_OF_SERVICES, Name2, Critical, Schedule, Reschedule, Data_Sincronizacao
                FROM dbo.{view}
                WHERE Project_ID_ACC = @projectId
                  AND NOT (Date IS NULL AND Name IS NULL AND Company IS NULL AND Description IS NULL AND Name2 IS NULL
                           AND Schedule IS NULL AND Reschedule IS NULL AND Concl IS NULL AND Critical IS NULL AND Status IS NULL AND COMMENTS IS NULL)
                ORDER BY Id_Registro;
                """,
            Dimensions = new[] { status, afe, empresa, responsavel, frente, critico },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total de ações", "Ações registradas", Contagem()),
                new("concluidas", "Concluídas", "Status = Concluded", ContagemOnde(status, "CONCLUDED"), FilterDimension: "status", FilterValue: "CONCLUDED"),
                new("atrasadas", "Atrasadas", "Status = Overdue", ContagemOnde(status, "OVERDUE"), FilterDimension: "status", FilterValue: "OVERDUE"),
                new("noPrazo", "No prazo", "Status = On time", ContagemOnde(status, "ON TIME"), FilterDimension: "status", FilterValue: "ON TIME"),
                new("criticas", "Críticas", "Critical = Yes", ContagemOnde(critico, "YES"), FilterDimension: "critico", FilterValue: "YES"),
            },
            Charts = new DashboardChart[]
            {
                new("porStatus", "Status", DashboardKinds.Rosca, "status"),
                new("porAfe", "AFE (Front of services no BI)", DashboardKinds.Barra, "afe"),
                new("porEmpresa", "Empresa", DashboardKinds.Coluna, "empresa"),
                new("porResponsavel", "Responsável", DashboardKinds.Barra, "responsavel"),
            },
            Table = new[]
            {
                Col("id", "ID", "ID"), Col("nome", "Nome", "Name"), Col("descricao", "Descrição", "Description"), Col("status", "Status", "Status"),
                Col("empresa", "Empresa", "Company"), Col("programado", "Programado", "Schedule"), Col("reprogramado", "Reprogramado", "Reschedule"),
                Col("critico", "Crítico", "Critical"),
            },
            Notes = new[] { "Linhas-modelo (só ID, sem nenhum outro campo) são ignoradas. O filtro nominal do BI (exclusão de uma pessoa) não é reproduzido." },
        };
    }

    // ==================================================================== DESVIO DE ESCOPO ====
    private const string EscopoSql = """
        SELECT Id_Registro, ITEM, DISCIPLINA, N_DO_DESVIO, DESCRICAO, DATA, QUANT_HH, STATUS, DESVIO_INTERNO, CHANGE_ORDER, Data_Sincronizacao
        FROM dbo.vw_Latest_ScopeDeviation WHERE Project_ID_ACC = @projectId ORDER BY Id_Registro;
        """;

    private static DashboardDefinition DesvioEscopo()
    {
        var disciplina = Dim("disciplina", "Disciplina", "DISCIPLINA");
        var status = Dim("status", "Status", "STATUS");
        var co = Presenca("changeOrder", "Change Order", "CHANGE_ORDER", "Com Change Order", "Sem Change Order");
        var interno = Presenca("desvioInterno", "Desvio interno", "DESVIO_INTERNO", "Com desvio interno", "Sem desvio interno");
        return new()
        {
            Id = "desvio-escopo", Title = "Desvio de Escopo", Source = "dbo.vw_Latest_ScopeDeviation", Sql = EscopoSql,
            Dimensions = new[] { disciplina, status, co, interno },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total de desvios", "Desvios de escopo registrados", Contagem()),
                new("changeOrders", "Change Orders", "CHANGE ORDER preenchido (regra do card do BI)", ContagemOnde(co, "COM"), FilterDimension: "changeOrder", FilterValue: "COM"),
                new("desviosInternos", "Desvios internos", "DESVIO INTERNO preenchido (regra do card do BI)", ContagemOnde(interno, "COM"), FilterDimension: "desvioInterno", FilterValue: "COM"),
            },
            Charts = new DashboardChart[]
            {
                new("porDisciplina", "Desvios por disciplina", DashboardKinds.Barra, "disciplina"),
                new("porStatus", "Status", DashboardKinds.Rosca, "status"),
                new("porChangeOrder", "Com/sem Change Order", DashboardKinds.Rosca, "changeOrder"),
            },
            Table = new[]
            {
                Col("item", "Item", "ITEM"), Col("disciplina", "Disciplina", "DISCIPLINA"), Col("desvio", "Nº do desvio", "N_DO_DESVIO"),
                Col("descricao", "Descrição", "DESCRICAO"), Col("data", "Data (texto da fonte)", "DATA"), Col("hh", "Quant. HH (texto da fonte)", "QUANT_HH"),
            },
            Notes = new[] { "O gráfico \"HH por disciplina\" do BI contava itens — aqui o título correto é \"Desvios por disciplina\". QUANT_HH é texto na fonte e não é somado." },
        };
    }

    private static DashboardDefinition ChangeOrder()
    {
        var co = Dim("changeOrder", "Change Order", "CHANGE_ORDER");
        var disciplina = Dim("disciplina", "Disciplina", "DISCIPLINA");
        var status = Dim("status", "Status do desvio", "STATUS");
        return new()
        {
            Id = "change-order", Title = "Change Order", Source = "dbo.vw_Latest_ScopeDeviation (CHANGE_ORDER preenchido)",
            Sql = """
                SELECT Id_Registro, ITEM, DISCIPLINA, N_DO_DESVIO, DESCRICAO, DATA, QUANT_HH, STATUS, CHANGE_ORDER, Data_Sincronizacao
                FROM dbo.vw_Latest_ScopeDeviation
                WHERE Project_ID_ACC = @projectId AND LTRIM(RTRIM(ISNULL(CHANGE_ORDER, N''))) <> N''
                ORDER BY Id_Registro;
                """,
            Dimensions = new[] { co, disciplina, status },
            Kpis = new DashboardKpi[]
            {
                new("total", "Desvios com Change Order", "CHANGE ORDER preenchido", Contagem()),
                new("distintos", "Change Orders distintos", "Valores distintos de CHANGE ORDER", DistintosNaoVazios(co)),
            },
            Charts = new DashboardChart[]
            {
                new("porDisciplina", "Por disciplina", DashboardKinds.Barra, "disciplina"),
                new("porStatus", "Por status do desvio", DashboardKinds.Rosca, "status"),
                new("porChangeOrder", "Por Change Order", DashboardKinds.Barra, "changeOrder"),
            },
            Table = new[]
            {
                Col("changeOrder", "Change Order", "CHANGE_ORDER"), Col("desvio", "Nº do desvio", "N_DO_DESVIO"), Col("descricao", "Descrição", "DESCRICAO"),
                Col("disciplina", "Disciplina", "DISCIPLINA"), Col("data", "Data (texto da fonte)", "DATA"), Col("hh", "Quant. HH (texto da fonte)", "QUANT_HH"),
                Col("status", "Status", "STATUS"),
            },
            Notes = new[] { "Sem valor financeiro, aprovação ou impacto: esses campos não existem na fonte. Sem evolução temporal: DATA é texto na fonte." },
        };
    }

    // ================================================================================ HOLD ====
    private static DashboardDefinition Hold()
    {
        var disciplina = Dim("disciplina", "Disciplina", "DISCIPLINA");
        var status = Dim("status", "Status", "STATUS");
        var responsavel = Dim("responsavel", "Responsável", "RESPONSAVEL");
        var documento = Dim("documento", "Documento", "N_NA_DOCUMENTO");
        return new()
        {
            Id = "hold", Title = "HOLD", Source = "dbo.vw_Latest_HoldControl",
            Sql = """
                SELECT Id_Registro, ITEM, N_HOLD, N_NA_DOCUMENTO, DISCIPLINA, TITULO_DOCUMENTO, REV, MOTIVO_HOLD, STATUS, RESPONSAVEL, Data_Sincronizacao
                FROM dbo.vw_Latest_HoldControl WHERE Project_ID_ACC = @projectId ORDER BY Id_Registro;
                """,
            Dimensions = new[] { disciplina, status, responsavel, documento },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total de HOLDs", "HOLDs registrados", Contagem()),
                new("documentos", "Documentos em HOLD", "Documentos N&A distintos", DistintosNaoVazios(documento)),
                new("responsaveis", "Responsáveis", "Responsáveis distintos", DistintosNaoVazios(responsavel)),
            },
            Charts = new DashboardChart[]
            {
                new("porDisciplina", "HOLDs por disciplina", DashboardKinds.Barra, "disciplina"),
                new("porStatus", "Status", DashboardKinds.Rosca, "status"),
                new("porResponsavel", "HOLDs por responsável", DashboardKinds.Barra, "responsavel"),
            },
            Table = new[]
            {
                Col("item", "Item", "ITEM"), Col("disciplina", "Disciplina", "DISCIPLINA"), Col("documento", "Nº N&A do documento", "N_NA_DOCUMENTO"),
                Col("titulo", "Título do documento", "TITULO_DOCUMENTO"), Col("motivo", "Motivo do HOLD", "MOTIVO_HOLD"), Col("hold", "Nº HOLD", "N_HOLD"),
                Col("responsavel", "Responsável", "RESPONSAVEL"), Col("status", "Status", "STATUS"),
            },
            Notes = new[] { "Sem aging: a fonte não tem data de abertura/prazo. O card Min(ITEM) do BI não é reproduzido." },
        };
    }

    // ================================================================================= RNC ====
    private static DashboardDefinition Rnc()
    {
        var status = Dim("status", "Status", "Status");
        var disciplina = Dim("disciplina", "Disciplina", "Disciplina");
        var responsavel = Dim("responsavel", "Responsável", "Responsavel");
        var tipoAcao = Dim("tipoAcao", "Tipo de ação", "TipoAcao");
        var tipoRnc = Dim("tipoRnc", "Tipo da RNC", "TipoRNC");
        return new()
        {
            Id = "rnc", Title = "RNC", Source = "[dbo].[NCR NONCONFORMITY REPORT] (Etapa 17)",
            Sql = """
                SELECT Id, DataRegistro, CodigoFormulario, Disciplina, Responsavel, ImpactoProjeto, TipoRNC, TipoAcao, Status, DataConclusao, Data_Sincronizacao
                FROM dbo.[NCR NONCONFORMITY REPORT] WHERE Project_ID_ACC = @projectId ORDER BY Id;
                """,
            Dimensions = new[] { status, disciplina, responsavel, tipoAcao, tipoRnc },
            Kpis = new DashboardKpi[]
            {
                new("total", "Total de RNC", "Linhas do snapshot atual", Contagem()),
                new("abertas", "Abertas", "Status = ABERTO", ContagemOnde(status, "ABERTO"), FilterDimension: "status", FilterValue: "ABERTO"),
                new("encerradas", "Encerradas", "Status = FECHADO", ContagemOnde(status, "FECHADO"), FilterDimension: "status", FilterValue: "FECHADO"),
                new("pctAbertas", "% abertas", "Abertas ÷ total", PercentualOnde(status, "ABERTO"), DashboardFormats.Percentual),
                new("pctEncerradas", "% encerradas", "Encerradas ÷ total", PercentualOnde(status, "FECHADO"), DashboardFormats.Percentual),
                new("semStatus", "Sem status", "Nem aberta nem encerrada — não entra em nenhum dos percentuais", ContagemOnde(status, N.NullKey), FilterDimension: "status", FilterValue: N.NullKey),
            },
            Charts = new DashboardChart[]
            {
                new("porTipoRnc", "Causa raiz (tipo da RNC)", DashboardKinds.Coluna, "tipoRnc"),
                new("porDisciplina", "Disciplina", DashboardKinds.Rosca, "disciplina"),
                new("porResponsavel", "Recurso (responsável)", DashboardKinds.Barra, "responsavel"),
                new("porTipoAcao", "Tipo de ação", DashboardKinds.Coluna, "tipoAcao"),
            },
            Table = new[]
            {
                Col("disciplina", "Disciplina", "Disciplina"), Col("formulario", "Código do formulário", "CodigoFormulario"),
                Col("impacto", "Impacto no projeto", "ImpactoProjeto"), Col("conclusao", "Data de conclusão", "DataConclusao", DashboardFormats.Data),
                Col("responsavel", "Responsável", "Responsavel"), Col("status", "Status", "Status"),
            },
            Notes = new[] { "Status reais: ABERTO, FECHADO e vazio. Vazio não é tratado como encerrada. Os % do BI usavam Min(STATUS) (erro) e não são reproduzidos." },
        };
    }

    // ==================================================================== registro único ====
    public static readonly IReadOnlyDictionary<string, DashboardDefinition> Todos = new[]
    {
        Ldp(), NivelamentoProgramacao(), ControleProgramacao(), DesvioQualidade(),
        Cap("cap-interno", "CAP Interno", "vw_Latest_InternalAction"), Cap("cap-externo", "CAP Externo", "vw_Latest_ExternalAction"),
        ChangeOrder(), ControleDocumental(), Hold(), DesvioEscopo(), Rnc(), Programacao(),
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);
}
