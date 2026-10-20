using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using System.Globalization;
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Importações do MPXJ (Prevenção de Ambiguidade entre tipos de Task)
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

using NA_Connect.Services;
using NA_Connect.Models;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    public class AccScheduleSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly DatabaseService _dbService;
        private readonly ILogger<AccScheduleSyncService> _logger;
        private readonly IConfiguration _config;
        private readonly ProjectAutoDiscoveryService _discoveryService;

        public AccScheduleSyncService(
            AutodeskApiService autodeskApi,
            DatabaseService dbService,
            ILogger<AccScheduleSyncService> logger,
            IConfiguration config,
            ProjectAutoDiscoveryService discoveryService)
        {
            _autodeskApi = autodeskApi;
            _dbService = dbService;
            _logger = logger;
            _config = config;
            _discoveryService = discoveryService;
        }

        // 🎯 MAPEAMENTO INTELIGENTE LDP (Padrão Forge Sync). %PHYSICAL COMPLETED (REALIZADO) NÃO está
        // mais aqui: tem resolução dedicada por igualdade normalizada EXATA (ResolverColunaLdpPhysicalCompleted,
        // seção 7 do pedido) — nunca mais Contains, e "REALIZADO" nunca mais é aceito como cabeçalho da
        // LDP (é o nome da ORIGEM no cronograma, não do destino).
        private readonly List<(string[] LdpKeywords, string SchKey)> _columnMappings = new()
        {
            (new[] { "PLANNED PHYSICAL", "PREVISTO" }, "%PLANNED PHYSICAL"),
            (new[] { "REPLANNED DATE", "REPROGRAMADA", "REPLANEJADA", "TENDENCIA", "TENDÊNCIA" }, "REPLANNED DATE"),
            (new[] { "BASE LINE", "BASELINE", "LINHA DE BASE" }, "PLANNED DATA (BASE LINE)"),
            (new[] { "WBS" }, "WBS_PERCENT")
        };

        /// <summary>Aliases oficiais do cabeçalho de destino na LDP (seção 7) — igualdade normalizada exata, nunca Contains.</summary>
        private static readonly string[] AliasesLdpPhysicalCompleted = { "%PHYSICAL COMPLETED", "% PHYSICAL COMPLETED", "PHYSICAL COMPLETED" };

        // -----------------------------------------------------------------------------------------
        // ALIASES CONTROLADOS (seção 3 da manutenção técnica): correspondência SOMENTE por nome
        // normalizado COMPLETO (nunca por palavra isolada tipo "PLAN"/"BASE"/"LB"/"REAL"/"PERCENT").
        // Isso é a correção do incidente BML011.013/017/021/022/023 (REALIZADO não encontrado, caindo
        // silenciosamente em TEXT2 — o mesmo campo que PREVISTO usava) e BML011.015/016 (uma palavra
        // genérica batendo por acaso num campo vazio, zerando 100%/90%/81,25% já publicados).
        // -----------------------------------------------------------------------------------------
        private static readonly string[] AliasesControladosRealizado =
        {
            "% REALIZADO", "REALIZADO", "PERCENTUAL REALIZADO",
            "PHYSICAL COMPLETED", "% PHYSICAL COMPLETED", "PERCENT PHYSICAL COMPLETED"
        };

        private static readonly string[] AliasesControladosPrevisto =
        {
            "% PREVISTO", "PREVISTO", "PERCENTUAL PREVISTO",
            "PLANNED PHYSICAL", "% PLANNED PHYSICAL", "PERCENT PREDICTED PHYSICAL"
        };

        private readonly string[] _kwsTendencia = { "TENDÊNCIA", "TENDENCIA", "TREND", "REPROGRAMADA", "REPLANEJADA", "TÉRMINO", "TERMINO" };

        /// <summary>Tamanho da amostra de valores reais exigida para aceitar um campo NATIVO comprovado (seção 4).</summary>
        private const int TamanhoAmostraCampoNativo = 5;

        /// <summary>Proteção de lote contra zeramento em massa (seção 6).</summary>
        private const int LimiteAbsolutoAlteracoesParaZero = 5;
        private const double LimitePercentualAlteracoesParaZero = 5.0;

        /// <summary>
        /// Linha de dados do MPP já pronta para comparar/gravar na LDP: os dois campos percentuais
        /// (REALIZADO/PREVISTO) já passaram pelo normalizador central (NormalizadorPercentualCronograma) —
        /// nunca reconvertidos aqui. Os demais campos (datas, WBS) continuam como texto, como sempre.
        /// </summary>
        internal sealed class MppScheduleRowData
        {
            public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public PercentualCronogramaNormalizado Realizado { get; set; }
            public PercentualCronogramaNormalizado Previsto { get; set; }
            public string TaskUid { get; set; }
            /// <summary>Código como veio do cronograma (antes de NormalizeDoc) — só para log.</summary>
            public string DocumentoOriginal { get; set; }
            /// <summary>True quando a chave veio do NA DOCUMENT NUMBER (TEXT1); false quando veio do fallback de nome da tarefa.</summary>
            public bool OrigemNaDocumentNumber { get; set; }
        }

        public async Task<string> SyncScheduleToLdpAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation($"📅 [CRONOGRAMA] INICIANDO SINCRONIZAÇÃO COMPLETA PARA {projectId}...");

            string configKey = projectId.StartsWith("b.") ? projectId : "b." + projectId;
            string mppPath = Path.Combine(Path.GetTempPath(), $"SCH_{Guid.NewGuid()}.mpp");
            string ldpPath = Path.Combine(Path.GetTempPath(), $"LDP_MASTER_{Guid.NewGuid()}.xlsx");

            try
            {
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Detalhes da LDP não encontrados.");

                string ldpVersionUrn = itemDetails.TipVersionUrn;
                string ldpParentFolderId = itemDetails.ParentFolderId;
                string ldpRealName = itemDetails.DisplayName;

                var folderSchId = await FindFolderByPaths(hubId, projectId, new[] {
                    new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
                    new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
                }, accessToken);

                if (string.IsNullOrEmpty(folderSchId)) return "Falha: Pasta de Cronograma não encontrada no ACC.";

                var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
                if (string.IsNullOrEmpty(schRes.FileId)) return "Falha: Arquivo .mpp não localizado na pasta de planejamento.";

                var mppDetails = await _autodeskApi.GetItemDetailsAsync(projectId, schRes.FileId, accessToken);
                string mppVersionUrn = mppDetails?.TipVersionUrn ?? schRes.FileId;
                string mppFileName = mppDetails?.DisplayName ?? schRes.FileName;

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, mppVersionUrn, accessToken))
                using (var fs = new FileStream(mppPath, FileMode.Create)) await dl.CopyToAsync(fs);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken))
                using (var fs = new FileStream(ldpPath, FileMode.Create)) await dl.CopyToAsync(fs);

                var projectFile = LoadMppFile(mppPath);

                // =================================================================================
                // FASE A.1 — RESOLUÇÃO DE ALIASES (seção 3/4): nunca cai em TEXT2/TEXT3 silenciosamente.
                // =================================================================================
                var resolucaoPrevisto = ResolverConceitoPorAliasControlado(
                    projectFile, "PREVISTO", AliasesControladosPrevisto, "CRONOGRAMA_ALIAS_PREVISTO_NAO_ENCONTRADO", out FieldType? tipoPrevisto);

                var resolucaoRealizado = ResolverRealizadoComFallbackNativo(
                    projectFile, tipoPrevisto, out FieldType? tipoRealizado);

                var resolucaoTendencia = ResolverTendencia(projectFile, out FieldType tipoTendencia);

                LogResumoAlias(projectId, mppFileName, projectFile, resolucaoRealizado, resolucaoPrevisto, resolucaoTendencia);

                if (!resolucaoRealizado.Encontrado)
                {
                    return $"ERRO: {resolucaoRealizado.CodigoErro} - {resolucaoRealizado.Mensagem} " +
                           "Nenhuma persistência SQL nem atualização de LDP foi realizada.";
                }

                if (!resolucaoPrevisto.Encontrado)
                {
                    return $"ERRO: {resolucaoPrevisto.CodigoErro} - {resolucaoPrevisto.Mensagem} " +
                           "Nenhuma persistência SQL nem atualização de LDP foi realizada.";
                }

                FieldType realField = tipoRealizado;
                FieldType prevField = tipoPrevisto;

                if (realField == prevField)
                {
                    _logger.LogError(
                        "[CRONOGRAMA][ALIAS][ERRO] Project_ID_ACC={ProjectIdAcc} | ArquivoMPP={ArquivoMpp} | CampoRealizado={CampoRealizado} | AliasRealizado={AliasRealizado} | " +
                        "CampoPrevisto={CampoPrevisto} | AliasPrevisto={AliasPrevisto} | Motivo={Motivo}",
                        projectId, mppFileName, realField, resolucaoRealizado.AliasOriginal, prevField, resolucaoPrevisto.AliasOriginal,
                        "REALIZADO e PREVISTO resolveram para o mesmo FieldType do MPP — persistência e atualização de LDP bloqueadas.");

                    return "ERRO: CONFIGURACAO_CRONOGRAMA_AMBIGUA - REALIZADO e PREVISTO apontam para o mesmo campo do MPP. " +
                           "Nenhuma persistência SQL nem atualização de LDP foi realizada.";
                }

                // =================================================================================
                // FASE A.1b — DISTRIBUIÇÃO COMPLETA DO REALIZADO (seção 6): calculada sobre TODAS as
                // tarefas com documento, não uma amostra de 5 — nunca decide identidade de campo (isso já
                // foi decidido estruturalmente acima), só decide se a LDP pode ser tocada nesta execução.
                // =================================================================================
                var distribuicaoRealizado = CalcularDistribuicaoRealizado(projectFile, realField, resolucaoRealizado.AliasOriginal);
                bool realizadoTodosValoresZero = distribuicaoRealizado.TotalComDocumento > 0 && distribuicaoRealizado.TotalNaoZero == 0;

                _logger.LogInformation(
                    "[CRONOGRAMA][REALIZADO][DISTRIBUICAO] Project_ID_ACC={ProjectIdAcc} | FieldType={FieldType} | Alias={Alias} | TotalTarefas={TotalTarefas} | " +
                    "TotalComDocumento={TotalComDocumento} | TotalVazio={TotalVazio} | TotalZero={TotalZero} | TotalNaoZero={TotalNaoZero} | " +
                    "Minimo={Minimo} | Maximo={Maximo} | Media={Media:0.##} | AmostrasNaoZero=[{Amostras}]",
                    projectId, distribuicaoRealizado.FieldType, distribuicaoRealizado.Alias, distribuicaoRealizado.TotalTarefas,
                    distribuicaoRealizado.TotalComDocumento, distribuicaoRealizado.TotalVazio, distribuicaoRealizado.TotalZero, distribuicaoRealizado.TotalNaoZero,
                    distribuicaoRealizado.Minimo?.ToString(CultureInfo.InvariantCulture) ?? "(nenhum valor)",
                    distribuicaoRealizado.Maximo?.ToString(CultureInfo.InvariantCulture) ?? "(nenhum valor)",
                    distribuicaoRealizado.Media, string.Join(", ", distribuicaoRealizado.AmostrasNaoZero));

                if (realizadoTodosValoresZero)
                {
                    _logger.LogWarning(
                        "[CRONOGRAMA][REALIZADO][TODOS_ZERO] Codigo=CRONOGRAMA_REALIZADO_TODOS_VALORES_ZERO | Project_ID_ACC={ProjectIdAcc} | FieldType={FieldType} | " +
                        "TotalComDocumento={TotalComDocumento} | O campo REALIZADO foi comprovado estruturalmente, mas TODAS as {TotalComDocumento} tarefas com documento " +
                        "retornaram zero — a LDP será preservada (nenhuma célula %PHYSICAL COMPLETED alterada nesta execução) até confirmação operacional. " +
                        "PREVISTO/datas/WBS continuam sendo processados normalmente.",
                        projectId, distribuicaoRealizado.FieldType, distribuicaoRealizado.TotalComDocumento);
                }

                // =================================================================================
                // FASE A.2 — PREPARAÇÃO E VALIDAÇÃO (seção 7): lê tudo, normaliza tudo, calcula todas as
                // alterações propostas — NENHUMA célula é alterada, NENHUM package.Save(), NENHUM SQL.
                // =================================================================================
                var plano = new PlanoSincronizacaoCronogramaTipo1 { RealizadoTodosValoresZero = realizadoTodosValoresZero };
                var dadosPorDocumento = new Dictionary<string, MppScheduleRowData>(StringComparer.OrdinalIgnoreCase);
                var esquemaDecimal = CarregarEsquemaDecimalCronograma();
                string projetoCodigo = configKey.Replace("b.", "");

                PrepararRegistrosSql(projectId, projectFile, mppFileName, realField, prevField, tipoTendencia,
                    esquemaDecimal, plano, dadosPorDocumento);

                plano.DocumentosUnicos = dadosPorDocumento.Count;

                if (plano.DocumentosDuplicadosConflitantes.Count > 0)
                {
                    plano.ErrosValidacao += plano.DocumentosDuplicadosConflitantes.Count;
                }

                // As "alterações simples" (datas/WBS, sem risco de zeramento) são preparadas junto com as
                // percentuais no mesmo laço de linhas da LDP, abaixo. Campo guarda o SchKey (seção 10:
                // contadores separados de datas/WBS).
                var alteracoesSimplesPreparadas = new List<(int Linha, int Coluna, string Valor, string Campo)>();

                using (var package = new ExcelPackage(new FileInfo(ldpPath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];

                    string codigoLido = ws.Cells[8, 25].Text?.Trim();
                    if (!string.IsNullOrEmpty(codigoLido)) projetoCodigo = codigoLido;

                    foreach (var registro in plano.RegistrosSqlPreparados) registro.ProjetoCodigo = projetoCodigo;

                    var ldpCols = MapLdpHeaders(ws);

                    // ---------------------------------------------------------------------------------
                    // COLUNA DE DESTINO %PHYSICAL COMPLETED (seção 7): igualdade normalizada EXATA, nunca
                    // Contains, nunca "REALIZADO" como cabeçalho da LDP. Resolvida UMA vez (a coluna é a
                    // mesma para todas as linhas) — zero ou mais de uma candidata bloqueia a publicação.
                    // ---------------------------------------------------------------------------------
                    int colunaRealizadoLdp = ResolverColunaLdpPhysicalCompleted(ws, ldpCols, projectId);
                    if (colunaRealizadoLdp <= 0)
                    {
                        return "ERRO: CRONOGRAMA_COLUNA_PHYSICAL_COMPLETED_NAO_UNICA - Não foi possível localizar de forma única a coluna %PHYSICAL COMPLETED na LDP " +
                               "(zero ou mais de uma coluna candidata). Nenhuma persistência SQL nem atualização de LDP foi realizada.";
                    }

                    PrepararAlteracoesLdp(ws, ldpCols, colunaRealizadoLdp, dadosPorDocumento, plano, alteracoesSimplesPreparadas, realField.ToString());

                    // -----------------------------------------------------------------------------------
                    // PROTEÇÃO CONTRA ZERAMENTO EM MASSA (seção 6) — barreira ADICIONAL, nunca substitui a
                    // resolução correta dos campos (que já é a defesa primária, seção 3/4).
                    // -----------------------------------------------------------------------------------
                    int alteracoesParaZero = plano.AlteracoesLdpPreparadas.Count(a => a.TipoAlteracao == TipoAlteracaoPercentual.ALTERACAO_PARA_ZERO);
                    foreach (var zeroProposto in plano.AlteracoesLdpPreparadas.Where(a => a.TipoAlteracao == TipoAlteracaoPercentual.ALTERACAO_PARA_ZERO))
                    {
                        _logger.LogWarning(
                            "[CRONOGRAMA][LDP][ZERO_PROPOSTO] Linha={Linha} | Documento={Documento} | Campo={Campo} | ValorAtual={ValorAtual} | ValorNovo={ValorNovo} | OrigemCampo={Origem}",
                            zeroProposto.Linha, zeroProposto.Documento, zeroProposto.Campo, zeroProposto.ValorAtualExibido, zeroProposto.ValorNovo, zeroProposto.OrigemCampo);
                    }

                    if (alteracoesParaZero > LimiteAbsolutoAlteracoesParaZero || plano.PercentualAlteracoesParaZero > LimitePercentualAlteracoesParaZero)
                    {
                        plano.ZeramentoEmMassa = true;
                        _logger.LogError(
                            "[CRONOGRAMA][LDP][ZERAMENTO_EM_MASSA_BLOQUEADO] Project_ID_ACC={ProjectIdAcc} | AlteracoesParaZero={AlteracoesParaZero} | PercentualParaZero={Percentual:0.##}% | " +
                            "LimiteAbsoluto={LimiteAbsoluto} | LimitePercentual={LimitePercentual}%",
                            projectId, alteracoesParaZero, plano.PercentualAlteracoesParaZero, LimiteAbsolutoAlteracoesParaZero, LimitePercentualAlteracoesParaZero);
                    }

                    plano.CodigoResultado = DeterminarCodigoResultado(plano);
                    plano.MensagemResumo =
                        $"TarefasLidas={plano.TarefasLidas} | TarefasComDocumento={plano.TarefasComDocumento} | DocumentosUnicos={plano.DocumentosUnicos} | " +
                        $"LinhasLdpCorrespondentes={plano.LinhasLdpCorrespondentes} | TarefasValidas={plano.TarefasValidas} | TarefasInvalidas={plano.TarefasInvalidas} | " +
                        $"RegistrosSqlPreparados={plano.RegistrosSqlPreparados.Count} | AtualizacoesRealizado={plano.AtualizacoesRealizado} | AtualizacoesPrevisto={plano.AtualizacoesPrevisto} | " +
                        $"AtualizacoesDatas={plano.AtualizacoesDatas} | AtualizacoesWbs={plano.AtualizacoesWbs} | RealizadoComZero={plano.RealizadoComZero} | " +
                        $"RealizadoComNaoZero={plano.RealizadoComNaoZero} | RealizadoPreservado={plano.RealizadoPreservado} | RealizadoSemOrigemComprovada={plano.RealizadoSemOrigemComprovada} | " +
                        $"AlteracoesParaZero={alteracoesParaZero} | PercentualParaZero={plano.PercentualAlteracoesParaZero:0.##}% | " +
                        $"ErrosAlias={plano.ErrosAlias} | ErrosConversao={plano.ErrosConversao} | ErrosValidacao={plano.ErrosValidacao}" +
                        (plano.RealizadoTodosValoresZero ? " | REALIZADO_NAO_ATUALIZADO (CRONOGRAMA_REALIZADO_TODOS_VALORES_ZERO)" : "") +
                        (plano.DocumentosDuplicadosConflitantes.Count > 0
                            ? $" | DocumentosDuplicadosConflitantes=[{string.Join("; ", plano.DocumentosDuplicadosConflitantes)}]"
                            : "");

                    if (!plano.PodePersistir)
                    {
                        _logger.LogError("[CRONOGRAMA][RESULTADO] Etapa 9 encerrada SEM sucesso | Project_ID_ACC={ProjectIdAcc} | Codigo={Codigo} | {Resumo}",
                            projectId, plano.CodigoResultado, plano.MensagemResumo);

                        return $"ERRO: {plano.CodigoResultado} - {plano.MensagemResumo}";
                    }

                    // =============================================================================
                    // FASE B — PERSISTÊNCIA ATÔMICA (seção 7/8): SQL primeiro (transação única), só
                    // depois do commit é que o Excel (já aberto, ainda sem nenhuma célula alterada) é
                    // modificado e salvo, e só depois disso é publicada nova versão no ACC.
                    // =============================================================================
                    try
                    {
                        _dbService.SalvarSnapshotCronogramaTipo1Transacional(projectId, plano.RegistrosSqlPreparados);
                    }
                    catch (Exception exSql)
                    {
                        // Rollback já foi executado dentro de SalvarSnapshotCronogramaTipo1Transacional.
                        // Nem uma célula do Excel foi tocada até aqui — nada para desfazer do lado da LDP.
                        _logger.LogError(exSql,
                            "[CRONOGRAMA][RESULTADO] Etapa 9 encerrada SEM sucesso (falha SQL, rollback executado) | Project_ID_ACC={ProjectIdAcc}", projectId);

                        return $"ERRO: CRONOGRAMA_SQL_FALHOU - {exSql.Message} (rollback executado; Excel não foi alterado).";
                    }

                    // ---------------------------------------------------------------------------------
                    // FALSO SUCESSO (seção 10): se havia linhas da LDP correspondentes mas NENHUMA
                    // proposta de REALIZADO teve sequer um valor determinável (nem para confirmar "sem
                    // alteração"), o objetivo desta etapa (REALIZADO) falhou por completo — o upload é
                    // bloqueado mesmo que PREVISTO/datas/WBS tenham gerado alterações válidas. O SQL já
                    // foi persistido acima (reflete o snapshot real lido do MPP); só a publicação da LDP
                    // é interrompida.
                    // ---------------------------------------------------------------------------------
                    if (plano.LinhasLdpCorrespondentes > 0 && plano.PropostasRealizadoComValor == 0)
                    {
                        _logger.LogError(
                            "[CRONOGRAMA][RESULTADO][FALSO_SUCESSO_EVITADO] Codigo=CRONOGRAMA_REALIZADO_NENHUMA_ATUALIZACAO | Project_ID_ACC={ProjectIdAcc} | " +
                            "LinhasLdpCorrespondentes={Linhas} | PropostasRealizadoComValor=0 | Upload da LDP bloqueado (SQL já persistido).",
                            projectId, plano.LinhasLdpCorrespondentes);

                        return $"ERRO: CRONOGRAMA_REALIZADO_NENHUMA_ATUALIZACAO - Nenhuma proposta válida de REALIZADO foi produzida apesar de " +
                               $"{plano.LinhasLdpCorrespondentes} linha(s) da LDP corresponderem a tarefas do cronograma. SQL já persistido (snapshot real). " +
                               $"Upload da LDP bloqueado. {plano.MensagemResumo}";
                    }

                    int totalUpdates = 0;

                    foreach (var alteracao in plano.AlteracoesLdpPreparadas.Where(a => a.DeveSerAplicada))
                    {
                        var celula = ws.Cells[alteracao.Linha, alteracao.ColunaLdp];
                        celula.Value = alteracao.CelulaFormatadaComoPercentual
                            ? (object)(double)(alteracao.ValorNovo.Value / 100m)
                            : (object)$"{alteracao.ValorNovo.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";
                        totalUpdates++;
                    }

                    foreach (var (linha, coluna, valor, campo) in alteracoesSimplesPreparadas)
                    {
                        ws.Cells[linha, coluna].Value = valor;
                        totalUpdates++;

                        if (campo == "REPLANNED DATE" || campo == "PLANNED DATA (BASE LINE)") plano.AtualizacoesDatas++;
                        else if (campo == "WBS_PERCENT") plano.AtualizacoesWbs++;
                    }

                    if (totalUpdates > 0)
                    {
                        package.Save();
                    }

                    if (totalUpdates > 0)
                    {
                        _logger.LogInformation($"⬆️ Subindo nova versão da LDP atualizada com {totalUpdates} linhas sincronizadas.");
                        using (var ul = new FileStream(ldpPath, FileMode.Open))
                            await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, ldpParentFolderId, ldpRealName, ul, accessToken);
                    }

                    return $"✅ Sucesso! Dados do Cronograma gravados na LDP Master e SQL. {plano.MensagemResumo}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro Crítico Cronograma: {ex.Message}");
                return $"Falha: {ex.Message}";
            }
            finally
            {
                if (File.Exists(mppPath)) File.Delete(mppPath);
                if (File.Exists(ldpPath)) File.Delete(ldpPath);
            }
        }

        private static string DeterminarCodigoResultado(PlanoSincronizacaoCronogramaTipo1 plano)
        {
            if (plano.ZeramentoEmMassa) return "CRONOGRAMA_ZERAMENTO_EM_MASSA_BLOQUEADO";
            if (plano.DocumentosDuplicadosConflitantes.Count > 0) return "CRONOGRAMA_DOCUMENTO_DUPLICADO_REALIZADO_CONFLITANTE";
            if (plano.ErrosAlias > 0) return "CRONOGRAMA_ALIAS_INVALIDO";
            if (plano.TarefasInvalidas > 0 || plano.ErrosConversao > 0 || plano.ErrosValidacao > 0) return "CRONOGRAMA_SNAPSHOT_INCOMPLETO";
            return "OK";
        }

        public async Task<ProjectDiscoveryResult> GetOrDiscoverProjectContext(string projectId, string ldpPath, string accessToken)
        {
            var cachedContext = _dbService.ObterContextoProjeto(projectId);
            if (cachedContext != null && !string.IsNullOrEmpty(cachedContext.NamingStandard)) return cachedContext;

            var discovery = await _discoveryService.ExecuteDiscoveryAsync(projectId, ldpPath, accessToken);
            _dbService.SalvarContextoProjeto(projectId, discovery.NamingStandard, discovery.Language);
            return discovery;
        }

        // =====================================================================================
        // RESOLUÇÃO DE CAMPOS (seção 3/4) — nunca fallback silencioso para TEXT2/TEXT3.
        // =====================================================================================

        /// <summary>
        /// Resolve um conceito (REALIZADO/PREVISTO) por alias EXATO (após normalização completa —
        /// maiúsculas, sem acento, sem pontuação/espaço) contra uma lista fechada e controlada de aliases
        /// — nunca por substring de palavra isolada. Um único campo cujo alias bate é aceito; zero campos
        /// vira NAO_ENCONTRADO; mais de um FieldType diferente batendo vira AMBIGUO (CRONOGRAMA_ALIAS_MULTIPLO).
        /// </summary>
        internal ResolucaoCampoCronograma ResolverConceitoPorAliasControlado(
            ProjectFile project, string conceito, string[] aliasesControlados, string codigoNaoEncontrado, out FieldType? tipoResolvido)
        {
            tipoResolvido = null;
            var resultado = new ResolucaoCampoCronograma { Conceito = conceito };
            var aliasesNormalizados = new HashSet<string>(aliasesControlados.Select(NormalizeString));

            var candidatos = new List<(FieldType Tipo, string AliasOriginal, string AliasNormalizado)>();

            try
            {
                var customFields = project.getCustomFields();
                if (customFields != null)
                {
                    var iterator = customFields.iterator();
                    while (iterator.hasNext())
                    {
                        var field = (net.sf.mpxj.CustomField)iterator.next();
                        string aliasOriginal = field.getAlias()?.ToString();
                        if (string.IsNullOrWhiteSpace(aliasOriginal)) continue;

                        string aliasNormalizado = NormalizeString(aliasOriginal);
                        if (aliasesNormalizados.Contains(aliasNormalizado))
                        {
                            candidatos.Add((field.getFieldType(), aliasOriginal, aliasNormalizado));
                        }
                    }
                }
            }
            catch { }

            var tiposDistintos = candidatos.Select(c => c.Tipo).Distinct().ToList();
            resultado.QuantidadeCandidatos = tiposDistintos.Count;
            resultado.Candidatos = candidatos.Select(c => $"{c.Tipo}='{c.AliasOriginal}'").ToList();
            resultado.RegraUtilizada = "ALIAS_CONTROLADO_EXATO";

            if (tiposDistintos.Count == 0)
            {
                resultado.Encontrado = false;
                resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.NAO_ENCONTRADO;
                resultado.CodigoErro = codigoNaoEncontrado;
                resultado.Mensagem = $"Nenhum campo customizado do MPP tem alias controlado de {conceito}. Aliases aceitos: {string.Join(", ", aliasesControlados)}.";
                return resultado;
            }

            if (tiposDistintos.Count > 1)
            {
                resultado.Encontrado = false;
                resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.AMBIGUO;
                resultado.CodigoErro = "CRONOGRAMA_ALIAS_MULTIPLO";
                resultado.Mensagem = $"{tiposDistintos.Count} campos diferentes do MPP têm alias controlado de {conceito}: {string.Join("; ", resultado.Candidatos)}.";
                return resultado;
            }

            var unico = candidatos.First();
            tipoResolvido = unico.Tipo;
            resultado.Encontrado = true;
            resultado.FieldType = unico.Tipo.ToString();
            resultado.AliasOriginal = unico.AliasOriginal;
            resultado.AliasNormalizado = unico.AliasNormalizado;
            resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.CUSTOM_ALIAS_EXATO;
            return resultado;
        }

        /// <summary>
        /// REALIZADO tem uma segunda via de resolução (seção 5 do pedido), MAS NUNCA baseada em valores de
        /// tarefas (nem "5 amostras", zero ou não — ver o incidente do log Execucao_Completa_20260729_133615:
        /// RealizadoEncontrado=True com AmostrasReais=[0,0,0,0,0] foi aceito como "comprovado" e o resultado
        /// publicou 901 tarefas com REALIZADO=0). A origem é resolvida SOMENTE por identidade estrutural:
        ///   1-3) alias EXATO de campo customizado (REALIZADO/% REALIZADO/PERCENTUAL REALIZADO/variações
        ///        PHYSICAL COMPLETED) — via ResolverConceitoPorAliasControlado;
        ///   4/6) título de coluna, IGUAL após normalização, em alguma Table do próprio MPP (a mesma
        ///        estrutura que a IU do MS Project usa para nomear a coluna visível "Realizado") — via
        ///        ResolverRealizadoViaTituloColunaTabela.
        /// Sem nenhuma das duas: CRONOGRAMA_CAMPO_REALIZADO_NAO_COMPROVADO — nada é gravado, todos os
        /// candidatos encontrados (mesmo que rejeitados) são listados para diagnóstico.
        /// </summary>
        internal ResolucaoCampoCronograma ResolverRealizadoComFallbackNativo(ProjectFile project, FieldType? tipoPrevisto, out FieldType? tipoResolvido)
        {
            var viaAlias = ResolverConceitoPorAliasControlado(
                project, "REALIZADO", AliasesControladosRealizado, "CRONOGRAMA_ALIAS_REALIZADO_NAO_ENCONTRADO", out tipoResolvido);

            if (viaAlias.Encontrado)
            {
                if (tipoPrevisto != null && viaAlias.FieldType == tipoPrevisto.ToString())
                {
                    // Mesmo campo que PREVISTO — não pode ser aceito; segue para a via estrutural.
                }
                else
                {
                    return viaAlias;
                }
            }

            var viaTitulo = ResolverRealizadoViaTituloColunaTabela(project, tipoPrevisto, out FieldType? tipoViaTitulo);
            if (viaTitulo.Encontrado)
            {
                tipoResolvido = tipoViaTitulo;
                return viaTitulo;
            }

            var candidatosCombinados = viaAlias.Candidatos.Concat(viaTitulo.Candidatos).Distinct().ToList();

            return new ResolucaoCampoCronograma
            {
                Conceito = "REALIZADO",
                Encontrado = false,
                OrigemResolucao = OrigemResolucaoCampoCronograma.NAO_ENCONTRADO,
                CodigoErro = "CRONOGRAMA_CAMPO_REALIZADO_NAO_COMPROVADO",
                RegraUtilizada = "ALIAS_EXATO_OU_TITULO_COLUNA_TABELA",
                QuantidadeCandidatos = candidatosCombinados.Count,
                Candidatos = candidatosCombinados,
                Mensagem =
                    $"A origem de REALIZADO não pôde ser comprovada estruturalmente. Nenhum campo customizado tem alias controlado de REALIZADO " +
                    $"({string.Join(", ", AliasesControladosRealizado)}) e nenhuma Table do MPP tem coluna com título igual (após normalização) a esses " +
                    $"mesmos aliases. Candidatos encontrados (rejeitados por ambiguidade, coincidência com PREVISTO, ou nenhum): " +
                    $"[{string.Join("; ", candidatosCombinados)}]. Amostras de valor NUNCA são usadas como prova de identidade — " +
                    $"nem cinco zeros, nem cinco valores reais."
            };
        }

        /// <summary>
        /// Comprovação ESTRUTURAL (seção 4/5/6 do pedido): procura, em TODAS as Table do MPP (as mesmas
        /// definições que o MS Project usa para nomear colunas visíveis em qualquer view), uma coluna cujo
        /// título normalizado seja EXATAMENTE igual a um alias controlado de REALIZADO. É assim que uma
        /// coluna nativa (ex.: PHYSICAL_PERCENT_COMPLETE) pode estar visivelmente rotulada "Realizado" no
        /// MS Project sem que exista NENHUM CustomField com alias — o título pertence à Table/coluna, não
        /// ao campo. Só aceita quando exatamente um FieldType distinto bate; nunca decide por valor de
        /// tarefa (isso é feito depois, separadamente, só para a distribuição — seção 6).
        /// </summary>
        internal ResolucaoCampoCronograma ResolverRealizadoViaTituloColunaTabela(ProjectFile project, FieldType? tipoPrevisto, out FieldType? tipoResolvido)
        {
            tipoResolvido = null;
            var resultado = new ResolucaoCampoCronograma { Conceito = "REALIZADO", RegraUtilizada = "TITULO_COLUNA_TABELA_EXATO" };
            var aliasesNormalizados = new HashSet<string>(AliasesControladosRealizado.Select(NormalizeString));

            var candidatos = new List<(FieldType Tipo, string Tabela, string TituloOriginal)>();

            try
            {
                var tables = project.getTables();
                if (tables != null)
                {
                    var itTables = tables.iterator();
                    while (itTables.hasNext())
                    {
                        var tabela = itTables.next() as net.sf.mpxj.Table;
                        if (tabela == null || tabela.getResourceFlag()) continue; // REALIZADO é campo de TAREFA — ignora tabelas de recurso

                        var colunas = tabela.getColumns();
                        if (colunas == null) continue;

                        var itColunas = colunas.iterator();
                        while (itColunas.hasNext())
                        {
                            var coluna = itColunas.next() as net.sf.mpxj.Column;
                            string titulo = coluna?.getTitle();
                            if (string.IsNullOrWhiteSpace(titulo)) continue;

                            string tituloNormalizado = NormalizeString(titulo);
                            if (aliasesNormalizados.Contains(tituloNormalizado))
                            {
                                candidatos.Add((coluna.getFieldType(), tabela.getName(), titulo));
                            }
                        }
                    }
                }
            }
            catch { }

            var tiposDistintos = candidatos.Select(c => c.Tipo).Where(t => tipoPrevisto == null || !t.Equals(tipoPrevisto)).Distinct().ToList();
            resultado.QuantidadeCandidatos = tiposDistintos.Count;
            resultado.Candidatos = candidatos.Select(c => $"{c.Tipo}=Tabela'{c.Tabela}'.Coluna'{c.TituloOriginal}'").ToList();

            if (tiposDistintos.Count == 0)
            {
                resultado.Encontrado = false;
                resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.NAO_ENCONTRADO;
                resultado.Mensagem = "Nenhuma Table do MPP tem coluna com título REALIZADO/variação controlada apontando para um FieldType distinto de PREVISTO.";
                return resultado;
            }

            if (tiposDistintos.Count > 1)
            {
                resultado.Encontrado = false;
                resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.AMBIGUO;
                resultado.CodigoErro = "CRONOGRAMA_TITULO_COLUNA_MULTIPLO";
                resultado.Mensagem = $"{tiposDistintos.Count} FieldTypes diferentes têm coluna de título REALIZADO em Tables distintas: {string.Join("; ", resultado.Candidatos)}.";
                return resultado;
            }

            FieldType tipoEscolhido = tiposDistintos[0];
            tipoResolvido = tipoEscolhido;
            var primeiraOcorrencia = candidatos.First(c => c.Tipo.Equals(tipoEscolhido));

            resultado.Encontrado = true;
            resultado.FieldType = tipoResolvido.ToString();
            resultado.AliasOriginal = $"Tabela'{primeiraOcorrencia.Tabela}'.Coluna'{primeiraOcorrencia.TituloOriginal}'";
            resultado.AliasNormalizado = NormalizeString(primeiraOcorrencia.TituloOriginal);
            resultado.OrigemResolucao = OrigemResolucaoCampoCronograma.TITULO_COLUNA_TABELA_COMPROVADO;

            _logger.LogInformation(
                "[CRONOGRAMA][ALIAS][TITULO_COLUNA_TABELA_COMPROVADO] Conceito=REALIZADO | FieldType={FieldType} | Tabela={Tabela} | TituloColuna={Titulo}",
                tipoResolvido, primeiraOcorrencia.Tabela, primeiraOcorrencia.TituloOriginal);

            return resultado;
        }

        /// <summary>Nome do documento de UMA tarefa, na mesma regra usada em toda a leitura do MPP (TEXT1, senão nome da tarefa).</summary>
        private static string ExtrairDocNumber(MpxjTask task)
        {
            string docNumber = task.get(TaskField.TEXT1)?.ToString();
            if (string.IsNullOrWhiteSpace(docNumber)) docNumber = task.getName();
            return docNumber;
        }

        /// <summary>
        /// Distribuição COMPLETA (seção 6 do pedido) do campo REALIZADO já comprovado — nunca decide
        /// identidade de campo, só relata. Passa por TODAS as tarefas-folha com documento, não uma
        /// amostra de 5. Valores fora de faixa/não numéricos entram como "vazio" aqui (o preflight real,
        /// em PrepararRegistrosSql, é quem rejeita a tarefa — este método é só diagnóstico agregado).
        /// </summary>
        internal DistribuicaoCampoCronograma CalcularDistribuicaoRealizado(ProjectFile project, FieldType campo, string alias)
        {
            var distribuicao = new DistribuicaoCampoCronograma { FieldType = campo.ToString(), Alias = alias };
            decimal soma = 0m;
            int totalConsiderado = 0;

            try
            {
                var tasks = project.getTasks();
                var it = tasks.iterator();
                while (it.hasNext())
                {
                    MpxjTask task = (MpxjTask)it.next();
                    if (task == null || task.getID() == null || task.getSummary()) continue;

                    distribuicao.TotalTarefas++;

                    string docNumber = ExtrairDocNumber(task);
                    if (string.IsNullOrWhiteSpace(docNumber)) continue;
                    distribuicao.TotalComDocumento++;

                    var valorBruto = task.get(campo);
                    bool ok = NormalizadorPercentualCronograma.TryNormalizarPercentualCronograma(valorBruto, campo.ToString(), alias, out var normalizado);

                    if (!ok && (normalizado.ClasseValor == ClasseValorPercentual.VALOR_NULO || normalizado.ClasseValor == ClasseValorPercentual.VALOR_VAZIO))
                    {
                        distribuicao.TotalVazio++;
                        continue;
                    }

                    if (!ok) continue; // não numérico/fora de faixa — não entra na distribuição agregada

                    totalConsiderado++;
                    soma += normalizado.ValorEmPontosPercentuais;
                    distribuicao.Minimo = distribuicao.Minimo.HasValue ? Math.Min(distribuicao.Minimo.Value, normalizado.ValorEmPontosPercentuais) : normalizado.ValorEmPontosPercentuais;
                    distribuicao.Maximo = distribuicao.Maximo.HasValue ? Math.Max(distribuicao.Maximo.Value, normalizado.ValorEmPontosPercentuais) : normalizado.ValorEmPontosPercentuais;

                    if (normalizado.ValorZeroExplicito)
                    {
                        distribuicao.TotalZero++;
                    }
                    else
                    {
                        distribuicao.TotalNaoZero++;
                        if (distribuicao.AmostrasNaoZero.Count < TamanhoAmostraCampoNativo)
                        {
                            distribuicao.AmostrasNaoZero.Add($"{docNumber}={normalizado.ValorEmPontosPercentuais.ToString(CultureInfo.InvariantCulture)}");
                        }
                    }
                }
            }
            catch { }

            distribuicao.Media = totalConsiderado > 0 ? soma / totalConsiderado : 0m;
            return distribuicao;
        }

        /// <summary>TENDÊNCIA continua com fallback (não é um dos dois conceitos protegidos pela seção 3) — risco muito menor: só afeta a data de reprogramação, nunca um percentual sobrescrito.</summary>
        private ResolucaoCampoCronograma ResolverTendencia(ProjectFile project, out FieldType tipoResolvido)
        {
            var encontrado = GetFieldTypeByAliasKeywords(project, _kwsTendencia);
            tipoResolvido = encontrado ?? TaskField.TEXT4;

            return new ResolucaoCampoCronograma
            {
                Conceito = "TENDENCIA",
                Encontrado = encontrado != null,
                FieldType = tipoResolvido.ToString(),
                OrigemResolucao = encontrado != null ? OrigemResolucaoCampoCronograma.CUSTOM_ALIAS_CONTROLADO : OrigemResolucaoCampoCronograma.NAO_ENCONTRADO,
                RegraUtilizada = "PALAVRA_CHAVE_TOLERANTE",
                Mensagem = encontrado != null ? null : "Nenhum alias de tendência encontrado — usando TEXT4 como padrão histórico (data de reprogramação, sem risco de zeramento de percentual)."
            };
        }

        private void LogResumoAlias(
            string projectId, string mppFileName, ProjectFile project,
            ResolucaoCampoCronograma realizado, ResolucaoCampoCronograma previsto, ResolucaoCampoCronograma tendencia)
        {
            string mapaAliases = DescreverAliasesCustomFields(project);
            string codigoFinal =
                !realizado.Encontrado ? realizado.CodigoErro :
                !previsto.Encontrado ? previsto.CodigoErro :
                string.Equals(realizado.FieldType, previsto.FieldType, StringComparison.OrdinalIgnoreCase) ? "CONFIGURACAO_CRONOGRAMA_AMBIGUA" :
                "OK";

            _logger.LogInformation(
                "[CRONOGRAMA][ALIAS][RESUMO] Project_ID_ACC={ProjectIdAcc} | ArquivoMPP={ArquivoMpp} | MapaAliases=[{MapaAliases}] | " +
                "RealizadoEncontrado={RealizadoEncontrado} | Realizado={Realizado} | PrevistoEncontrado={PrevistoEncontrado} | Previsto={Previsto} | " +
                "TendenciaEncontrada={TendenciaEncontrada} | Tendencia={Tendencia} | CodigoFinal={CodigoFinal}",
                projectId, mppFileName, mapaAliases,
                realizado.Encontrado, realizado, previsto.Encontrado, previsto,
                tendencia.Encontrado, tendencia, codigoFinal);
        }

        /// <summary>
        /// Lista "FieldType=Alias" de todos os campos customizados do MPP — usado só para diagnóstico no
        /// log [CRONOGRAMA][ALIAS][RESUMO].
        /// </summary>
        private string DescreverAliasesCustomFields(ProjectFile project)
        {
            try
            {
                var customFields = project.getCustomFields();
                if (customFields == null) return "(nenhum campo customizado)";

                var partes = new List<string>();
                var iterator = customFields.iterator();
                while (iterator.hasNext())
                {
                    var field = (net.sf.mpxj.CustomField)iterator.next();
                    string aliasName = field.getAlias()?.ToString();
                    if (!string.IsNullOrEmpty(aliasName)) partes.Add($"{field.getFieldType()}={aliasName}");
                }

                return partes.Count > 0 ? string.Join("; ", partes) : "(nenhum alias definido)";
            }
            catch
            {
                return "(falha ao ler aliases)";
            }
        }

        /// <summary>
        /// Nomes reais das colunas DECIMAL/NUMERIC de dbo.Cronograma_Projetos que recebem valores
        /// calculados a partir do MPP — usados para validar CADA valor (dígitos inteiros) contra o
        /// esquema REAL da tabela (lido via sys.columns, nunca hardcoded) antes do envio ao SQL.
        /// </summary>
        private const string ColunaPercentualRealizado = "PERCENT_PHYSICAL_COMPLETED";
        private const string ColunaPercentualPrevisto = "PERCENT_PREDICTED_PHYSICAL";
        private const string ColunaTrabalho = "Trabalho";
        private const string TabelaCronograma = "Cronograma_Projetos";

        // =====================================================================================
        // FASE A.2 — PREPARAÇÃO (seção 7/8): monta os registros SQL e os dados por documento (para a
        // LDP) inteiramente em memória. NENHUMA chamada a _dbService.SalvarTarefaCronograma aqui.
        // =====================================================================================
        internal void PrepararRegistrosSql(
            string projectIdAcc, ProjectFile project, string mppFileName,
            FieldType realField, FieldType prevField, FieldType trendField,
            Dictionary<string, NA_Connect.Models.ColunaEsquemaSql> esquemaDecimal,
            PlanoSincronizacaoCronogramaTipo1 plano,
            Dictionary<string, MppScheduleRowData> dadosPorDocumento)
        {
            var tasks = project.getTasks();
            var iterTasks = tasks.iterator();
            int indiceTarefa = 0;
            var discipline = CronogramaTipo1Analytics.ResolveDiscipline(project);
            var root = CronogramaTipo1Analytics.FindRoot(project);
            _logger.LogInformation("[CRONOGRAMA][ANALYTICS] Disciplina={Resolution} ResumoRaiz={Root}", discipline.Diagnostic, root != null);

            // Coleta TODAS as tarefas-folha por documento antes de decidir qual linha alimenta a LDP
            // (seção 9 do pedido) — nunca "a primeira que chegar" quando há mais de uma tarefa-folha
            // com o mesmo número de documento.
            var candidatosPorDocumento = new Dictionary<string, List<(MppScheduleRowData Linha, string TaskUid, string TaskName)>>(StringComparer.OrdinalIgnoreCase);

            while (iterTasks.hasNext())
            {
                MpxjTask task = (MpxjTask)iterTasks.next();
                if (task == null || task.getID() == null) continue;

                plano.TarefasLidas++;
                indiceTarefa++;

                string uid = task.getGUID()?.ToString() ?? task.getUniqueID()?.ToString() ?? "";
                string taskId = task.getID()?.ToString();

                string docNumber = task.get(TaskField.TEXT1)?.ToString();
                if (string.IsNullOrWhiteSpace(docNumber)) docNumber = task.getName();
                if (string.IsNullOrWhiteSpace(docNumber)) docNumber = "";
                if (!task.getSummary() && !string.IsNullOrWhiteSpace(task.get(TaskField.TEXT1)?.ToString())) plano.TarefasComDocumento++;

                string taskName = task.getName() ?? "Sem Nome";
                string edtWbs = task.getWBS() ?? "";

                var rawRealDone = task.getSummary() ? null : task.get(realField);
                bool realOk = NormalizadorPercentualCronograma.TryNormalizarPercentualCronograma(rawRealDone, realField.ToString(), "REALIZADO", out var realNormalizado);

                var rawPrevisto = task.getSummary() ? null : task.get(prevField);
                bool prevOk = NormalizadorPercentualCronograma.TryNormalizarPercentualCronograma(rawPrevisto, prevField.ToString(), "PREVISTO", out var prevNormalizado);

                LogNormalizacaoPercentual(projectIdAcc, mppFileName, uid, docNumber, "REALIZADO", realField, resolucao: null, realNormalizado);
                LogNormalizacaoPercentual(projectIdAcc, mppFileName, uid, docNumber, "PREVISTO", prevField, resolucao: null, prevNormalizado);

                // Missing percentages remain NULL; malformed documentary values still reject the batch.
                bool realGarbage = !realOk && realNormalizado.ClasseValor != ClasseValorPercentual.VALOR_NULO && realNormalizado.ClasseValor != ClasseValorPercentual.VALOR_VAZIO;
                bool prevGarbage = !prevOk && prevNormalizado.ClasseValor != ClasseValorPercentual.VALOR_NULO && prevNormalizado.ClasseValor != ClasseValorPercentual.VALOR_VAZIO;

                if (realGarbage || prevGarbage)
                {
                    plano.TarefasInvalidas++;
                    plano.ErrosConversao++;

                    _logger.LogError(
                        "[CRONOGRAMA][PERCENTUAL][ERRO] Project_ID_ACC={ProjectIdAcc} | ArquivoMPP={ArquivoMpp} | TaskUID={TaskUid} | TaskName={TaskName} | Indice={Indice} | " +
                        "Realizado=[{Realizado}] | Previsto=[{Previsto}]",
                        projectIdAcc, mppFileName, uid, taskName, indiceTarefa, realNormalizado, prevNormalizado);

                    continue; // linha causadora identificada e registrada — pula SOMENTE esta tarefa
                }

                decimal? physicalDone = realOk ? realNormalizado.ValorEmPontosPercentuais : null;
                decimal? predictedDone = prevOk ? prevNormalizado.ValorEmPontosPercentuais : null;
                string wbsPercent = task.get(TaskField.TEXT3)?.ToString() ?? "";

                decimal? trabalhoMpp = CronogramaTipo1Analytics.ReadWorkHours(task);
                object rawWorkDuration = task.getWork();

                DateTime? start = ConvertMpxjDate(task.getStart());

                DateTime? baseline = null;
                if (task.getBaselineFinish() != null)
                {
                    baseline = ConvertMpxjDate(task.getBaselineFinish());
                }

                DateTime? trendDate = ConvertMpxjDate(task.get(trendField));
                if (trendDate == null)
                {
                    trendDate = (physicalDone >= 100 && task.getActualFinish() != null)
                        ? ConvertMpxjDate(task.getActualFinish())
                        : ConvertMpxjDate(task.getFinish());
                }

                bool tarefaValida = true;

                tarefaValida &= !physicalDone.HasValue || ValidarCampoDecimal(esquemaDecimal, ColunaPercentualRealizado, physicalDone.Value,
                    projectIdAcc, mppFileName, uid, taskId, taskName, edtWbs, docNumber, indiceTarefa,
                    realField.ToString(), "REALIZADO", rawRealDone, "@phys");

                tarefaValida &= !predictedDone.HasValue || ValidarCampoDecimal(esquemaDecimal, ColunaPercentualPrevisto, predictedDone.Value,
                    projectIdAcc, mppFileName, uid, taskId, taskName, edtWbs, docNumber, indiceTarefa,
                    prevField.ToString(), "PREVISTO", rawPrevisto, "@pred");

                tarefaValida &= !trabalhoMpp.HasValue || ValidarCampoDecimal(esquemaDecimal, ColunaTrabalho, trabalhoMpp.Value,
                    projectIdAcc, mppFileName, uid, taskId, taskName, edtWbs, docNumber, indiceTarefa,
                    "Work.HOURS", "TRABALHO", rawWorkDuration, "@trabalho");

                if (!tarefaValida)
                {
                    plano.TarefasInvalidas++;
                    plano.ErrosValidacao++;
                    continue; // linha causadora identificada e registrada — pula SOMENTE esta tarefa
                }

                plano.TarefasValidas++;
                plano.RegistrosSqlPreparados.Add(new RegistroCronogramaTipo1Preparado
                {
                    ProjectIdAcc = projectIdAcc,
                    TaskUid = uid,
                    DocNumber = task.getSummary() ? null : task.get(TaskField.TEXT1)?.ToString(),
                    TaskName = taskName,
                    PhysicalDone = physicalDone,
                    PredictedDone = predictedDone,
                    WbsPercent = wbsPercent,
                    Start = start,
                    Baseline = baseline,
                    TrendDate = trendDate,
                    EdtWbs = edtWbs,
                    ProjetoCodigo = null, // preenchido depois de ler a LDP (projetoCodigo real)
                    TrabalhoMpp = trabalhoMpp,
                    EhTarefaResumo = task.getSummary(),
                    Disciplina = discipline.Field == null ? null : task.get(discipline.Field)?.ToString()?.Trim(),
                    AnalyticsJson = CronogramaTipo1Analytics.Metadata(task, ReferenceEquals(task, root), discipline.Diagnostic)
                });

                // Dados para casar com a LDP (nome-base do documento) — sempre indexados, mesmo quando o
                // valor percentual está ausente (a LDP decide, célula a célula, se preserva ou altera).
                if (!task.getSummary() && !string.IsNullOrWhiteSpace(docNumber))
                {
                    string key = NormalizeDoc(docNumber);
                    var linha = new MppScheduleRowData
                    {
                        Realizado = realNormalizado,
                        Previsto = prevNormalizado,
                        TaskUid = uid,
                        DocumentoOriginal = docNumber.Trim(),
                        OrigemNaDocumentNumber = !string.IsNullOrWhiteSpace(task.get(TaskField.TEXT1)?.ToString())
                    };

                    string formattedDate = FormatProjectDate(task.get(trendField));
                    if (string.IsNullOrEmpty(formattedDate)) formattedDate = FormatProjectDate(task.getFinish());
                    linha.Valores["REPLANNED DATE"] = formattedDate;

                    var baselineVal = task.getBaselineFinish();
                    linha.Valores["PLANNED DATA (BASE LINE)"] = baselineVal != null ? FormatProjectDate(baselineVal) : "-";
                    linha.Valores["WBS_PERCENT"] = wbsPercent;

                    if (!candidatosPorDocumento.TryGetValue(key, out var lista))
                    {
                        lista = new List<(MppScheduleRowData Linha, string TaskUid, string TaskName)>();
                        candidatosPorDocumento[key] = lista;
                    }
                    lista.Add((linha, uid, taskName));
                }
            }

            ResolverDuplicidadesRealizado(projectIdAcc, candidatosPorDocumento, dadosPorDocumento, plano);

            // O código do projeto só é conhecido depois de abrir a LDP — preenchido pelo chamador.
        }

        /// <summary>
        /// Resolve, POR DOCUMENTO, qual tarefa-folha alimenta a LDP (seção 9 do pedido). Tarefas-resumo
        /// nunca entram no dicionario documental (persistidas apenas para analytics). Documento com uma única
        /// tarefa-folha: aceito direto. Mais de uma: só aceito se todos os valores de REALIZADO válidos
        /// concordarem entre si (mesmo ponto percentual) — divergência real bloqueia o documento inteiro
        /// (nunca escolhe a primeira tarefa silenciosamente) e o gera CRONOGRAMA_DOCUMENTO_DUPLICADO_REALIZADO_CONFLITANTE.
        /// </summary>
        internal void ResolverDuplicidadesRealizado(
            string projectIdAcc,
            Dictionary<string, List<(MppScheduleRowData Linha, string TaskUid, string TaskName)>> candidatosPorDocumento,
            Dictionary<string, MppScheduleRowData> dadosPorDocumento,
            PlanoSincronizacaoCronogramaTipo1 plano)
        {
            foreach (var par in candidatosPorDocumento)
            {
                string key = par.Key;
                var candidatos = par.Value;

                if (candidatos.Count == 1)
                {
                    dadosPorDocumento[key] = candidatos[0].Linha;
                    continue;
                }

                var valoresValidosDistintos = candidatos
                    .Where(c => c.Linha.Realizado?.Valido == true)
                    .Select(c => c.Linha.Realizado.ValorEmPontosPercentuais)
                    .Distinct()
                    .ToList();

                if (valoresValidosDistintos.Count <= 1)
                {
                    var comValor = candidatos.FirstOrDefault(c => c.Linha.Realizado?.Valido == true);
                    dadosPorDocumento[key] = comValor.Linha ?? candidatos[0].Linha;

                    _logger.LogInformation(
                        "[CRONOGRAMA][DOCUMENTO_DUPLICADO] Project_ID_ACC={ProjectIdAcc} | Documento={Documento} | QuantidadeTarefasFolha={Quantidade} | " +
                        "TaskUIDs=[{TaskUids}] | ValorRealizadoConcordante={Valor} | Conflito=nao",
                        projectIdAcc, key, candidatos.Count, string.Join(", ", candidatos.Select(c => c.TaskUid)),
                        valoresValidosDistintos.Count == 1 ? valoresValidosDistintos[0].ToString(CultureInfo.InvariantCulture) : "(nenhuma tarefa com valor válido)");
                    continue;
                }

                string detalhe = $"Documento={key} | Tarefas=[{string.Join(", ", candidatos.Select(c => $"{c.TaskUid}('{c.TaskName}')={(c.Linha.Realizado?.Valido == true ? c.Linha.Realizado.ValorEmPontosPercentuais.ToString(CultureInfo.InvariantCulture) : "(sem valor)")}"))}]";

                // Chave vinda SÓ do fallback de nome da tarefa (nenhuma tarefa com NA DOCUMENT NUMBER):
                // subtarefas de atividade ("Preparation", "Issue", "Comments Resolution"...) repetidas sob
                // cada documento. Não é documento — só vira conflito bloqueante se casar com um N&A real
                // da LDP (verificado em PrepararAlteracoesLdp). Identidade documental da Etapa 09 é o
                // NA DOCUMENT NUMBER.
                if (!candidatos.Any(c => c.Linha.OrigemNaDocumentNumber))
                {
                    plano.ConflitosNomeTarefaSemNaDocumentNumber[key] = detalhe;
                    _logger.LogWarning(
                        "[CRONOGRAMA][DOCUMENTO_DUPLICADO][SEM_NA_DOCUMENT_NUMBER] Project_ID_ACC={ProjectIdAcc} | Chave={Chave} | QuantidadeTarefasFolha={Quantidade} | " +
                        "Bloqueante=somente_se_casar_com_N&A_da_LDP",
                        projectIdAcc, key, candidatos.Count);
                    continue;
                }

                plano.DocumentosDuplicadosConflitantes.Add(detalhe);

                _logger.LogError(
                    "[CRONOGRAMA][DOCUMENTO_DUPLICADO][ERRO] Codigo=CRONOGRAMA_DOCUMENTO_DUPLICADO_REALIZADO_CONFLITANTE | Project_ID_ACC={ProjectIdAcc} | {Detalhe}",
                    projectIdAcc, detalhe);

                // Documento não entra em dadosPorDocumento — nenhuma proposta de LDP para ele nesta
                // execução (o ErrosValidacao incrementado pelo chamador já bloqueia PodePersistir).
            }
        }

        /// <summary>[CRONOGRAMA][PERCENTUAL][NORMALIZACAO] — log de diagnóstico Debug (seção 9): permite comprovar depois que 6% chegou como 6.00.</summary>
        private void LogNormalizacaoPercentual(string projectIdAcc, string mppFileName, string taskUid, string docNumber, string conceito, FieldType campo, ResolucaoCampoCronograma resolucao, PercentualCronogramaNormalizado normalizado)
        {
            _logger.LogDebug(
                "[CRONOGRAMA][PERCENTUAL][NORMALIZACAO] Project_ID_ACC={ProjectIdAcc} | ArquivoMPP={ArquivoMpp} | TaskUID={TaskUid} | Documento={Documento} | Conceito={Conceito} | " +
                "FieldType={FieldType} | Alias={Alias} | ClasseJava={ClasseJava} | ValorBruto={ValorBruto} | TextoBruto={TextoBruto} | Estrategia={Estrategia} | " +
                "PontosPercentuais={Pontos} | ValorExcel={ValorExcel} | ValorSql={ValorSql}",
                projectIdAcc, mppFileName, taskUid, docNumber, conceito, campo, normalizado.Alias ?? "(nativo/customizado)",
                normalizado.TipoBruto, normalizado.ValorBruto ?? "(null)", normalizado.TextoBruto, normalizado.EstrategiaConversao,
                normalizado.ValorEmPontosPercentuais, normalizado.ValorParaExcel, normalizado.Valido ? normalizado.ValorEmPontosPercentuais.ToString(CultureInfo.InvariantCulture) : "(não gravado)");
        }

        /// <summary>
        /// Coluna-chave da LDP para a Etapa 09: "N&amp;A DOCUMENT NUMBER" explícita primeiro (nunca a do
        /// Client Document Number só por vir antes); heurística legada só se ela não existir.
        /// </summary>
        internal static int ResolverColunaChaveLdp(Dictionary<int, string> ldpCols)
        {
            int colunaNa = ldpCols.FirstOrDefault(x => x.Value != null &&
                Regex.Replace(x.Value, @"\s+", " ").Trim().ToUpperInvariant().Contains("N&A DOCUMENT NUMBER")).Key;
            if (colunaNa > 0) return colunaNa;

            int legado = ldpCols.FirstOrDefault(x =>
                x.Value.Contains("DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase) ||
                x.Value.Contains("CÓDIGO DO DOCUMENTO", StringComparison.OrdinalIgnoreCase) ||
                x.Value.Contains("CODIGO", StringComparison.OrdinalIgnoreCase)
            ).Key;
            return legado == 0 ? 4 : legado;
        }

        /// <summary>
        /// FASE A.2 (LDP): casa cada linha da LDP com o cronograma pela identidade documental
        /// NA DOCUMENT NUMBER (cronograma) ↔ N&amp;A DOCUMENT NUMBER (LDP) e prepara as propostas — sem
        /// alterar célula nenhuma. NÃO depende de arquivo físico no ACC, de Client Document Number nem
        /// de Workflow/Review: só da presença do código nos dois lados.
        /// </summary>
        internal void PrepararAlteracoesLdp(
            ExcelWorksheet ws, Dictionary<int, string> ldpCols, int colunaRealizadoLdp,
            Dictionary<string, MppScheduleRowData> dadosPorDocumento, PlanoSincronizacaoCronogramaTipo1 plano,
            List<(int Linha, int Coluna, string Valor, string Campo)> alteracoesSimplesPreparadas, string realField)
        {
            int matchColIndex = ResolverColunaChaveLdp(ldpCols);
            var chavesComMatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int ultimaLinha = ws.Dimension?.End.Row ?? 0;

            for (int row = 8; row <= ultimaLinha; row++)
            {
                string docCodeExcel = ws.Cells[row, matchColIndex].Text?.Trim();
                if (string.IsNullOrEmpty(docCodeExcel)) continue;

                string key = NormalizeDoc(docCodeExcel);

                // Conflito entre tarefas sem NA DOCUMENT NUMBER que CASA com um N&A real da LDP: aí sim é
                // erro real de matching Cronograma ↔ LDP — promovido a conflito bloqueante (uma vez).
                if (plano.ConflitosNomeTarefaSemNaDocumentNumber.TryGetValue(key, out var detalheConflito))
                {
                    plano.ConflitosNomeTarefaSemNaDocumentNumber.Remove(key);
                    plano.DocumentosDuplicadosConflitantes.Add(detalheConflito);
                    plano.ErrosValidacao++;
                    _logger.LogError(
                        "[CRONOGRAMA][DOCUMENTO_DUPLICADO][ERRO] Codigo=CRONOGRAMA_DOCUMENTO_DUPLICADO_REALIZADO_CONFLITANTE | Linha={Linha} | Documento={Documento} | " +
                        "Origem=NOME_TAREFA | {Detalhe}", row, docCodeExcel, detalheConflito);
                    continue;
                }

                if (!dadosPorDocumento.TryGetValue(key, out var mppRow)) continue;

                chavesComMatch.Add(key);
                plano.LinhasLdpCorrespondentes++;

                _logger.LogInformation(
                    "[CRONOGRAMA][LDP][MATCH] Documento={Documento} | Linha={Linha} | Origem={Origem} | LdpEncontrada=sim | ArquivoFisicoNecessario=nao",
                    docCodeExcel, row, mppRow.OrigemNaDocumentNumber ? "NA_DOCUMENT_NUMBER" : "NOME_TAREFA");

                // Preserve legacy name-based matching ONLY after a real LDP code proves the identity.
                foreach (var registro in plano.RegistrosSqlPreparados.Where(r => !r.EhTarefaResumo
                    && string.IsNullOrWhiteSpace(r.DocNumber) && NormalizeDoc(r.TaskName) == key))
                    registro.DocNumber = docCodeExcel;

                // -----------------------------------------------------------------------------
                // REALIZADO (seção 1/9): origem única — a mesma normalização que alimenta o SQL
                // (PrepararRegistrosSql). Se a distribuição inteira do campo é zero, a proposta
                // continua sendo CALCULADA e LOGADA (auditoria), mas nunca aplicada à célula.
                // -----------------------------------------------------------------------------
                var alteracaoRealizado = PrepararAlteracaoLdpPercentual(ws, row, colunaRealizadoLdp, docCodeExcel, "%PHYSICAL COMPLETED", mppRow.Realizado);

                bool proporiaAlterarCelula =
                    alteracaoRealizado.TipoAlteracao == TipoAlteracaoPercentual.AUMENTO ||
                    alteracaoRealizado.TipoAlteracao == TipoAlteracaoPercentual.REDUCAO ||
                    alteracaoRealizado.TipoAlteracao == TipoAlteracaoPercentual.ALTERACAO_PARA_ZERO ||
                    alteracaoRealizado.TipoAlteracao == TipoAlteracaoPercentual.PREENCHIMENTO_INICIAL;

                if (plano.RealizadoTodosValoresZero && proporiaAlterarCelula)
                {
                    alteracaoRealizado.ForcarNaoAplicar = true;
                }

                plano.AlteracoesLdpPreparadas.Add(alteracaoRealizado);

                _logger.LogInformation(
                    "[CRONOGRAMA][LDP][REALIZADO_PROPOSTA] Linha={Linha} | Documento={Documento} | TaskUID={TaskUid} | FieldTypeRealizado={FieldType} | " +
                    "ValorBruto={ValorBruto} | ValorNormalizado={ValorNormalizado} | ValorAtualLdp={ValorAtual} | ValorProposto={ValorProposto} | " +
                    "SeraAplicado={Aplicado} | Motivo={Motivo}",
                    row, docCodeExcel, mppRow.TaskUid, realField, alteracaoRealizado.ValorBrutoMpp ?? "(null)",
                    alteracaoRealizado.ValorNormalizado?.Valido == true ? alteracaoRealizado.ValorNormalizado.ValorEmPontosPercentuais.ToString(CultureInfo.InvariantCulture) : "(indisponível)",
                    alteracaoRealizado.ValorAtualExibido, alteracaoRealizado.ValorNovo?.ToString(CultureInfo.InvariantCulture) ?? "(preservado)",
                    alteracaoRealizado.DeveSerAplicada ? "sim" : "não", alteracaoRealizado.ForcarNaoAplicar ? "REALIZADO_TODOS_ZERO" : alteracaoRealizado.TipoAlteracao.ToString());

                foreach (var map in _columnMappings)
                {
                    int targetLdpCol = FindLdpCol(ldpCols, map.LdpKeywords);
                    if (targetLdpCol <= 0) continue;

                    if (map.SchKey == "%PLANNED PHYSICAL")
                    {
                        var alteracao = PrepararAlteracaoLdpPercentual(ws, row, targetLdpCol, docCodeExcel, map.SchKey, mppRow.Previsto);
                        plano.AlteracoesLdpPreparadas.Add(alteracao);
                        continue;
                    }

                    if (mppRow.Valores.TryGetValue(map.SchKey, out string val))
                    {
                        if (string.IsNullOrEmpty(val) && (map.SchKey.Contains("DATE") || map.SchKey.Contains("LINE")))
                        {
                            val = "-";
                        }

                        string currentVal = ws.Cells[row, targetLdpCol].Text?.Trim();
                        if (!string.Equals(currentVal, val, StringComparison.OrdinalIgnoreCase))
                        {
                            alteracoesSimplesPreparadas.Add((row, targetLdpCol, val, map.SchKey));
                        }
                    }
                }
            }

            // Documento do cronograma (com NA DOCUMENT NUMBER) ausente na LDP: nunca inventa linha.
            foreach (var par in dadosPorDocumento.Where(p => p.Value.OrigemNaDocumentNumber && !chavesComMatch.Contains(p.Key)))
            {
                _logger.LogWarning(
                    "[CRONOGRAMA][LDP][SEM_MATCH_LDP] Documento={Documento} | Origem=NA_DOCUMENT_NUMBER | LdpEncontrada=nao | Acao=IGNORADO",
                    par.Value.DocumentoOriginal ?? par.Key);
            }
        }

        /// <summary>
        /// Calcula (sem aplicar) a alteração proposta para UMA célula percentual da LDP — seção 6/7.
        /// Ausência de origem (VALOR_NULO/VALOR_VAZIO) NUNCA propõe zerar a célula: a proposta é
        /// VALOR_ORIGEM_AUSENTE e a célula é preservada.
        /// </summary>
        internal AlteracaoLdpCronogramaPreparada PrepararAlteracaoLdpPercentual(
            ExcelWorksheet ws, int row, int targetLdpCol, string documento, string campo, PercentualCronogramaNormalizado normalizado)
        {
            var celula = ws.Cells[row, targetLdpCol];
            string valorAtualExibido = celula.Text?.Trim();
            decimal? valorAtualInterno = null;

            if (celula.Value is double d) valorAtualInterno = (decimal)d * 100m;
            else if (celula.Value is decimal dec) valorAtualInterno = dec * 100m;
            else if (!string.IsNullOrEmpty(valorAtualExibido))
            {
                string limpo = valorAtualExibido.Replace("%", "").Trim().Replace(",", ".");
                if (decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parseado)) valorAtualInterno = parseado;
            }

            bool celulaFormatadaComoPercentual = celula.Style?.Numberformat?.Format?.Contains('%') ?? false;

            var alteracao = new AlteracaoLdpCronogramaPreparada
            {
                Linha = row,
                Documento = documento,
                Campo = campo,
                ValorAtualExibido = valorAtualExibido,
                ValorAtualInterno = valorAtualInterno,
                ValorBrutoMpp = normalizado.ValorBruto,
                OrigemCampo = normalizado.CampoMPP,
                ValorNormalizado = normalizado,
                ColunaLdp = targetLdpCol,
                CelulaFormatadaComoPercentual = celulaFormatadaComoPercentual
            };

            if (!normalizado.Valido)
            {
                // Ausente/vazio/não-numérico: célula preservada — nunca zera por ausência.
                alteracao.ValorNovo = null;
                alteracao.TipoAlteracao = TipoAlteracaoPercentual.VALOR_ORIGEM_AUSENTE;

                _logger.LogWarning(
                    "[CRONOGRAMA][LDP][PERCENTUAL_INVALIDO] Linha={Linha} | Documento={Documento} | Campo={Campo} | Classe={Classe} | Motivo={Motivo} | Célula preservada.",
                    row, documento, campo, normalizado.ClasseValor, normalizado.MotivoErro);

                return alteracao;
            }

            decimal novo = normalizado.ValorEmPontosPercentuais;
            alteracao.ValorNovo = novo;

            bool valorIgual = valorAtualInterno.HasValue && Math.Abs(valorAtualInterno.Value - novo) < 0.005m;

            if (valorIgual)
            {
                alteracao.TipoAlteracao = TipoAlteracaoPercentual.SEM_ALTERACAO;
            }
            else if (!valorAtualInterno.HasValue || valorAtualInterno.Value == 0m)
            {
                alteracao.TipoAlteracao = novo == 0m ? TipoAlteracaoPercentual.SEM_ALTERACAO : TipoAlteracaoPercentual.PREENCHIMENTO_INICIAL;
            }
            else if (novo == 0m)
            {
                // Só chega aqui como ALTERACAO_PARA_ZERO quando o normalizador confirmou VALOR_ZERO_EXPLICITO
                // (normalizado.Valido já garante isso — nunca um "ausente" chega até aqui, ver acima).
                alteracao.TipoAlteracao = TipoAlteracaoPercentual.ALTERACAO_PARA_ZERO;
            }
            else if (novo > valorAtualInterno.Value)
            {
                alteracao.TipoAlteracao = TipoAlteracaoPercentual.AUMENTO;
            }
            else
            {
                alteracao.TipoAlteracao = TipoAlteracaoPercentual.REDUCAO;
            }

            return alteracao;
        }

        /// <summary>
        /// Lê (somente leitura) o esquema real das colunas DECIMAL/NUMERIC de Cronograma_Projetos. Se a
        /// consulta falhar (ex.: permissão insuficiente), a validação preventiva fica desabilitada nesta
        /// execução.
        /// </summary>
        private Dictionary<string, NA_Connect.Models.ColunaEsquemaSql> CarregarEsquemaDecimalCronograma()
        {
            try
            {
                return _dbService.ObterEsquemaColunas(TabelaCronograma)
                    .Where(c => string.Equals(c.Tipo, "decimal", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(c.Tipo, "numeric", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(c => c.Coluna, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CRONOGRAMA] Não foi possível ler o esquema real de {Tabela} para validação preventiva — prosseguindo sem essa validação nesta execução.", TabelaCronograma);
                return new Dictionary<string, NA_Connect.Models.ColunaEsquemaSql>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private bool ValidarCampoDecimal(
            Dictionary<string, NA_Connect.Models.ColunaEsquemaSql> esquemaDecimal,
            string colunaSql, decimal valorConvertido,
            string projectIdAcc, string mppFileName, string taskUid, string taskId, string taskName, string wbs, string na,
            int indiceTarefa, string campoMpp, string aliasUtilizado, object valorBruto, string parametroDataTable)
        {
            if (!esquemaDecimal.TryGetValue(colunaSql, out var coluna))
            {
                return true; // coluna não encontrada no esquema lido — nada a validar contra
            }

            if (APS.WebApp.Models.ValidadorNumericoSql.Validar(valorConvertido, coluna.Precision, coluna.Scale, out string motivo, out int maxDigitosInteiros))
            {
                return true;
            }

            _logger.LogError(
                "[CRONOGRAMA][VALIDACAO_NUMERICA][ERRO] Project_ID_ACC={ProjectIdAcc} | ArquivoMPP={ArquivoMpp} | TaskUID={TaskUid} | TaskID={TaskId} | TaskName={TaskName} | WBS={Wbs} | NA={Na} | " +
                "Indice={Indice} | CampoMPP={CampoMpp} | AliasUtilizado={Alias} | ValorBruto={ValorBruto} | TipoValorBruto={TipoValorBruto} | ValorConvertido={ValorConvertido} | " +
                "ParametroDataTable={Parametro} | Destino={Tabela}.{Coluna} | SqlType={SqlType}({Precision},{Scale}) | MaximoDigitosInteiros={MaxDigitos} | Motivo={Motivo}",
                projectIdAcc, mppFileName, taskUid, taskId, taskName, wbs, na,
                indiceTarefa, campoMpp, aliasUtilizado, valorBruto ?? "(null)", valorBruto?.GetType().Name ?? "null", valorConvertido,
                parametroDataTable, TabelaCronograma, colunaSql, coluna.Tipo, coluna.Precision, coluna.Scale, maxDigitosInteiros, motivo);

            return false;
        }

        // 🎯 INTELIGÊNCIA BILINGUE E DINÂMICA (usada só por TENDÊNCIA agora — REALIZADO/PREVISTO usam alias controlado)
        private FieldType? GetFieldTypeByAliasKeywords(ProjectFile file, params string[] keywords)
        {
            try
            {
                var customFields = file.getCustomFields();
                if (customFields != null)
                {
                    var iterator = customFields.iterator();
                    while (iterator.hasNext())
                    {
                        var field = (net.sf.mpxj.CustomField)iterator.next();
                        string? aliasName = field.getAlias()?.ToString();

                        if (!string.IsNullOrEmpty(aliasName))
                        {
                            foreach (var keyword in keywords)
                            {
                                if (AliasMatchesKeyword(aliasName, keyword))
                                {
                                    return field.getFieldType();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private bool AliasMatchesKeyword(string? alias, string keyword)
        {
            if (string.IsNullOrEmpty(alias)) return false;
            return NormalizeString(alias).Contains(NormalizeString(keyword));
        }

        /// <summary>Normaliza para comparação de alias: maiúsculas/minúsculas, acentos e QUALQUER pontuação/espaço removidos — usada tanto para o alias controlado (igualdade exata) quanto para o fallback tolerante de TENDÊNCIA.</summary>
        internal string NormalizeString(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        private DateTime? ConvertMpxjDate(object? mpxjDate)
        {
            if (mpxjDate == null) return null;
            if (mpxjDate is java.util.Date javaDate)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(javaDate.getTime()).DateTime;
            }
            try { return DateTime.Parse(mpxjDate.ToString()); }
            catch { return null; }
        }

        private ProjectFile LoadMppFile(string path)
        {
            try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }
            return new UniversalProjectReader().read(path);
        }

        internal Dictionary<int, string> MapLdpHeaders(ExcelWorksheet ws)
        {
            var map = new Dictionary<int, string>();
            for (int c = 1; c <= 100; c++) {
                string txt = ws.Cells[7, c].Text?.Trim().ToUpper();
                if (!string.IsNullOrEmpty(txt)) map[c] = txt;
            }
            return map;
        }

        private int FindLdpCol(Dictionary<int, string> cols, string[] keywords)
        {
            var normalizedKeywords = keywords.Select(k => NormalizeString(k).ToUpper()).ToList();
            var match = cols.FirstOrDefault(c =>
                normalizedKeywords.Any(kw => NormalizeString(c.Value).ToUpper().Contains(kw))
            );
            return match.Key;
        }

        /// <summary>
        /// Localiza a coluna de destino %PHYSICAL COMPLETED na LDP por IGUALDADE NORMALIZADA EXATA
        /// (seção 7 do pedido) — nunca Contains, nunca "REALIZADO" como cabeçalho aceito (esse nome é
        /// reservado para a origem no Cronograma). Zero ou mais de uma coluna candidata retorna 0 (o
        /// chamador bloqueia a publicação com CRONOGRAMA_COLUNA_PHYSICAL_COMPLETED_NAO_UNICA).
        /// </summary>
        internal int ResolverColunaLdpPhysicalCompleted(ExcelWorksheet ws, Dictionary<int, string> ldpCols, string projectId)
        {
            var aliasesNormalizados = new HashSet<string>(AliasesLdpPhysicalCompleted.Select(a => NormalizeString(a).ToUpper()));
            var candidatas = ldpCols.Where(kv => aliasesNormalizados.Contains(NormalizeString(kv.Value).ToUpper())).ToList();

            if (candidatas.Count != 1)
            {
                _logger.LogError(
                    "[CRONOGRAMA][LDP][COLUNA_DESTINO][ERRO] Project_ID_ACC={ProjectIdAcc} | Motivo=CRONOGRAMA_COLUNA_PHYSICAL_COMPLETED_NAO_UNICA | " +
                    "QuantidadeCandidatas={Quantidade} | Candidatas=[{Detalhes}]",
                    projectId, candidatas.Count, string.Join(", ", candidatas.Select(c => $"col{c.Key}='{c.Value}'")));
                return 0;
            }

            var escolhida = candidatas[0];
            int celulasPreenchidas = 0;
            int maxRow = ws.Dimension?.End.Row ?? 7;
            for (int r = 8; r <= maxRow; r++)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cells[r, escolhida.Key].Text)) celulasPreenchidas++;
            }

            string formato = ws.Cells[7, escolhida.Key].Style?.Numberformat?.Format ?? "(geral)";

            _logger.LogInformation(
                "[CRONOGRAMA][LDP][COLUNA_DESTINO] Project_ID_ACC={ProjectIdAcc} | Aba={Aba} | LinhaCabecalho=7 | TextoBruto={TextoBruto} | " +
                "TextoNormalizado={TextoNormalizado} | Indice={Indice} | FormatoNumerico={Formato} | CelulasPreenchidasAntes={Celulas}",
                projectId, ws.Name, escolhida.Value, NormalizeString(escolhida.Value), escolhida.Key, formato, celulasPreenchidas);

            return escolhida.Key;
        }

        private string FormatProjectDate(object? dateObj)
        {
            if (dateObj == null) return "";
            if (DateTime.TryParse(dateObj.ToString(), out DateTime dt))
            {
                var culture = new CultureInfo("pt-BR");
                string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(dt.DayOfWeek).ToUpper();
                if (dayName.Length > 3) dayName = dayName.Substring(0, 3);
                return $"{dayName} {dt:dd/MM/yy}";
            }
            return "";
        }

        public async Task<string> FindFolderByPaths(string hub, string proj, string[][] paths, string token)
        {
            // [CANÁRIO][DIAGNÓSTICO] Fato estrutural do código atual, não uma nova regra: cada elemento de
            // "paths" é uma lista FIXA de nomes de pasta percorrida nível a nível (ver
            // AutodeskApiService.FindFolderIdByPathAsync) — não há varredura recursiva de subpastas com
            // nomes arbitrários.
            _logger.LogInformation(
                "[DISCOVERY][RECURSION] Discovery recursive = NO | CandidatosTentados={CandidatosTentados}",
                paths.Length);

            foreach (var pathArray in paths)
            {
                try
                {
                    var id = await _autodeskApi.FindFolderIdByPathAsync(hub, proj, pathArray, token);
                    if (!string.IsNullOrEmpty(id))
                    {
                        _logger.LogInformation(
                            "[DISCOVERY][GESTAO_SELECTED] Name={Name} | Id={Id} | Reason={Reason}",
                            pathArray[pathArray.Length - 1], id,
                            $"Caminho candidato correspondeu integralmente: {string.Join(" / ", pathArray)}");
                        return id;
                    }
                }
                catch { }
            }

            _logger.LogWarning(
                "[DISCOVERY][GESTAO_NOT_FOUND] Nenhum dos {Count} caminhos candidatos foi encontrado: {Candidatos}",
                paths.Length,
                string.Join(" | ", paths.Select(p => string.Join(" / ", p))));
            return null;
        }

        /// <summary>
        /// Correção 17/09/2026: localiza a pasta de Gestão pelo CÓDIGO NUMÉRICO ("09", depois "09.14"
        /// dentro dela), independente do texto descritivo que vem depois — esse texto tem variado
        /// entre projetos/idiomas no ACC (DESENVOLVIMENTO/DEVELOPING/DEVELOPMENT/MODEL DEVELOPING/
        /// ENGINEERING DOCUMENTS) e quebrava a lista fixa de candidatos nominais de FindFolderByPaths
        /// sempre que um nome novo aparecia (achado real: CHS001 tinha "09 - DEVELOPMENT", nome que não
        /// batia com nenhum dos 4 candidatos configurados). Usada como PRIMEIRA tentativa pelo chamador,
        /// antes dos candidatos nominais — nunca substitui FindFolderByPaths, que continua existindo
        /// como rede de segurança caso o prefixo numérico não seja encontrado por qualquer motivo.
        /// </summary>
        public async Task<string> FindGestaoFolderByNumericPrefixAsync(string hub, string proj, string token)
        {
            string id;
            try
            {
                id = await _autodeskApi.FindFolderIdByNumericPrefixPathAsync(hub, proj, new[] { "09", "09.14" }, token);
            }
            catch
            {
                id = null;
            }

            if (!string.IsNullOrEmpty(id))
            {
                _logger.LogInformation(
                    "[DISCOVERY][GESTAO_SELECTED] Id={Id} | Reason={Reason}",
                    id, "Prefixo numérico 09/09.14 encontrado (independente do nome completo da pasta)");
            }
            return id;
        }

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");

        // =====================================================================================
        // DIAGNÓSTICO SOMENTE LEITURA (seção 4 do pedido) — lê um .mpp LOCAL diretamente do disco,
        // nunca baixa do ACC, nunca escreve SQL, nunca toca a LDP, nunca faz upload. Usado para
        // investigar/comprovar a causa raiz de um incidente de REALIZADO antes de qualquer correção
        // ser aplicada em produção.
        // =====================================================================================

        /// <summary>Inventário somente-leitura, campo a campo, dos candidatos a REALIZADO num .mpp real (seção 4).</summary>
        public RelatorioDiagnosticoCronogramaTipo1 DiagnosticarArquivoMpp(string mppFilePath)
        {
            var relatorio = new RelatorioDiagnosticoCronogramaTipo1 { ArquivoMpp = mppFilePath };
            var project = LoadMppFile(mppFilePath);

            relatorio.ResolucaoPrevisto = ResolverConceitoPorAliasControlado(
                project, "PREVISTO", AliasesControladosPrevisto, "CRONOGRAMA_ALIAS_PREVISTO_NAO_ENCONTRADO", out FieldType? tipoPrevisto);
            relatorio.ResolucaoRealizado = ResolverRealizadoComFallbackNativo(project, tipoPrevisto, out FieldType? tipoRealizadoResolvido);

            if (relatorio.ResolucaoRealizado.Encontrado && tipoRealizadoResolvido != null)
            {
                relatorio.DistribuicaoRealizado = CalcularDistribuicaoRealizado(project, tipoRealizadoResolvido, relatorio.ResolucaoRealizado.AliasOriginal);
            }

            var camposCandidatos = new List<(net.sf.mpxj.FieldType Tipo, string Origem)>
            {
                (TaskField.PHYSICAL_PERCENT_COMPLETE, "NATIVO"),
                (TaskField.PERCENT_COMPLETE, "NATIVO"),
                (TaskField.PERCENT_WORK_COMPLETE, "NATIVO"),
                (TaskField.NUMBER1, "NATIVO"), (TaskField.NUMBER2, "NATIVO"), (TaskField.NUMBER3, "NATIVO"), (TaskField.NUMBER4, "NATIVO"), (TaskField.NUMBER5, "NATIVO"),
                (TaskField.NUMBER6, "NATIVO"), (TaskField.NUMBER7, "NATIVO"), (TaskField.NUMBER8, "NATIVO"), (TaskField.NUMBER9, "NATIVO"), (TaskField.NUMBER10, "NATIVO"),
                (TaskField.NUMBER11, "NATIVO"), (TaskField.NUMBER12, "NATIVO"), (TaskField.NUMBER13, "NATIVO"), (TaskField.NUMBER14, "NATIVO"), (TaskField.NUMBER15, "NATIVO"),
                (TaskField.NUMBER16, "NATIVO"), (TaskField.NUMBER17, "NATIVO"), (TaskField.NUMBER18, "NATIVO"), (TaskField.NUMBER19, "NATIVO"), (TaskField.NUMBER20, "NATIVO"),
                (TaskField.TEXT1, "NATIVO"), (TaskField.TEXT2, "NATIVO"), (TaskField.TEXT3, "NATIVO"), (TaskField.TEXT4, "NATIVO"), (TaskField.TEXT5, "NATIVO"),
                (TaskField.TEXT6, "NATIVO"), (TaskField.TEXT7, "NATIVO"), (TaskField.TEXT8, "NATIVO"), (TaskField.TEXT9, "NATIVO"), (TaskField.TEXT10, "NATIVO"),
                (TaskField.TEXT11, "NATIVO"), (TaskField.TEXT12, "NATIVO"), (TaskField.TEXT13, "NATIVO"), (TaskField.TEXT14, "NATIVO"), (TaskField.TEXT15, "NATIVO"),
                (TaskField.TEXT16, "NATIVO"), (TaskField.TEXT17, "NATIVO"), (TaskField.TEXT18, "NATIVO"), (TaskField.TEXT19, "NATIVO"), (TaskField.TEXT20, "NATIVO"),
                (TaskField.TEXT21, "NATIVO"), (TaskField.TEXT22, "NATIVO"), (TaskField.TEXT23, "NATIVO"), (TaskField.TEXT24, "NATIVO"), (TaskField.TEXT25, "NATIVO"),
                (TaskField.TEXT26, "NATIVO"), (TaskField.TEXT27, "NATIVO"), (TaskField.TEXT28, "NATIVO"), (TaskField.TEXT29, "NATIVO"), (TaskField.TEXT30, "NATIVO"),
            };

            // Campos customizados com alias — sempre incluídos no inventário (é onde REALIZADO
            // normalmente aparece quando configurado corretamente).
            var aliasesPorTipo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var customFields = project.getCustomFields();
                if (customFields != null)
                {
                    var it = customFields.iterator();
                    while (it.hasNext())
                    {
                        var field = (net.sf.mpxj.CustomField)it.next();
                        string alias = field.getAlias()?.ToString();
                        if (string.IsNullOrWhiteSpace(alias)) continue;

                        aliasesPorTipo[field.getFieldType().ToString()] = alias;
                        if (!camposCandidatos.Any(c => c.Tipo.Equals(field.getFieldType())))
                        {
                            camposCandidatos.Add((field.getFieldType(), "CUSTOMIZADO"));
                        }
                    }
                }
            }
            catch { }

            // Títulos de coluna em Tables (o mesmo mecanismo usado por ResolverRealizadoViaTituloColunaTabela)
            // relacionados aos conceitos pedidos na seção 4 — sempre logados, mesmo que não decidam nada aqui.
            var keywordsSecao4 = new[] { "REALIZADO", "% REALIZADO", "PERCENTUAL REALIZADO", "PHYSICAL COMPLETED", "PERCENT COMPLETE", "AVANCO FISICO", "AVANÇO FÍSICO" };
            var keywordsNormalizados = new HashSet<string>(keywordsSecao4.Select(NormalizeString));
            var titulosPorTipo = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var tables = project.getTables();
                if (tables != null)
                {
                    var itT = tables.iterator();
                    while (itT.hasNext())
                    {
                        var tabela = itT.next() as net.sf.mpxj.Table;
                        if (tabela == null || tabela.getResourceFlag()) continue;
                        var colunas = tabela.getColumns();
                        if (colunas == null) continue;
                        var itC = colunas.iterator();
                        while (itC.hasNext())
                        {
                            var coluna = itC.next() as net.sf.mpxj.Column;
                            string titulo = coluna?.getTitle();
                            if (string.IsNullOrWhiteSpace(titulo)) continue;
                            if (!keywordsNormalizados.Contains(NormalizeString(titulo))) continue;

                            string tipoNome = coluna.getFieldType().ToString();
                            if (!titulosPorTipo.TryGetValue(tipoNome, out var lista)) { lista = new List<string>(); titulosPorTipo[tipoNome] = lista; }
                            lista.Add($"Tabela'{tabela.getName()}'.Coluna'{titulo}'");

                            if (!camposCandidatos.Any(c => c.Tipo.Equals(coluna.getFieldType())))
                            {
                                camposCandidatos.Add((coluna.getFieldType(), "NATIVO_OU_CUSTOMIZADO_VIA_TITULO_TABELA"));
                            }
                        }
                    }
                }
            }
            catch { }

            var tasksParaContagem = new List<MpxjTask>();
            try
            {
                var it = project.getTasks().iterator();
                while (it.hasNext())
                {
                    var task = (MpxjTask)it.next();
                    if (task == null || task.getID() == null || task.getSummary()) continue;
                    tasksParaContagem.Add(task);
                }
            }
            catch { }

            relatorio.TotalTarefas = tasksParaContagem.Count;
            relatorio.TotalTarefasComDocumento = tasksParaContagem.Count(t => !string.IsNullOrWhiteSpace(ExtrairDocNumber(t)));

            foreach (var (tipo, origem) in camposCandidatos)
            {
                var diag = ColetarDiagnosticoCampo(tasksParaContagem, tipo, origem);
                diag.Alias = aliasesPorTipo.TryGetValue(tipo.ToString(), out var alias) ? alias : null;
                diag.TitulosColunaTabela = titulosPorTipo.TryGetValue(tipo.ToString(), out var titulos) ? titulos : new List<string>();

                relatorio.Candidatos.Add(diag);

                _logger.LogInformation(
                    "[CRONOGRAMA][DIAGNOSTICO_CAMPO][CANDIDATO] {Diag}", diag);
            }

            return relatorio;
        }

        private DiagnosticoCampoTarefaCronograma ColetarDiagnosticoCampo(List<MpxjTask> tasks, net.sf.mpxj.FieldType campo, string origem)
        {
            var diag = new DiagnosticoCampoTarefaCronograma
            {
                FieldType = campo.ToString(),
                NomeExibido = campo.getName(),
                Origem = origem
            };

            foreach (var task in tasks)
            {
                object valor;
                try { valor = task.get(campo); } catch { valor = null; }

                string docNumber = ExtrairDocNumber(task);

                if (valor == null)
                {
                    diag.QuantidadeVazio++;
                    continue;
                }

                diag.QuantidadeComValor++;
                if (string.IsNullOrEmpty(diag.TipoRetorno)) diag.TipoRetorno = valor.GetType().FullName;

                if (diag.CincoAmostras.Count < 5) diag.CincoAmostras.Add($"{docNumber}='{valor}'");

                NormalizadorPercentualCronograma.TryNormalizarPercentualCronograma(valor, campo.ToString(), null, out var normalizado);
                if (!normalizado.TemValor)
                {
                    diag.QuantidadeVazio++;
                    diag.QuantidadeComValor--;
                    continue;
                }

                if (normalizado.Valido)
                {
                    decimal pontos = normalizado.ValorEmPontosPercentuais;
                    diag.Minimo = diag.Minimo.HasValue ? Math.Min(diag.Minimo.Value, pontos) : pontos;
                    diag.Maximo = diag.Maximo.HasValue ? Math.Max(diag.Maximo.Value, pontos) : pontos;

                    if (pontos == 0m)
                    {
                        diag.QuantidadeZero++;
                    }
                    else
                    {
                        diag.QuantidadeNaoZero++;
                        if (diag.CincoTarefasNaoZero.Count < 5) diag.CincoTarefasNaoZero.Add($"{docNumber}={pontos.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
            }

            return diag;
        }
    }

    /// <summary>Distribuição completa (não amostra) de um campo REALIZADO já comprovado — seção 6 do pedido.</summary>
    public sealed class DistribuicaoCampoCronograma
    {
        public string FieldType { get; set; }
        public string Alias { get; set; }
        public int TotalTarefas { get; set; }
        public int TotalComDocumento { get; set; }
        public int TotalVazio { get; set; }
        public int TotalZero { get; set; }
        public int TotalNaoZero { get; set; }
        public decimal? Minimo { get; set; }
        public decimal? Maximo { get; set; }
        public decimal Media { get; set; }
        public List<string> AmostrasNaoZero { get; } = new List<string>();
    }

    /// <summary>Estatísticas somente-leitura de UM campo de tarefa, sobre TODAS as tarefas-folha do .mpp (seção 4 do pedido).</summary>
    public sealed class DiagnosticoCampoTarefaCronograma
    {
        public string FieldType { get; set; }
        public string NomeExibido { get; set; }
        public string Alias { get; set; }
        public string Origem { get; set; }
        public string TipoRetorno { get; set; }
        public int QuantidadeComValor { get; set; }
        public int QuantidadeVazio { get; set; }
        public int QuantidadeZero { get; set; }
        public int QuantidadeNaoZero { get; set; }
        public decimal? Minimo { get; set; }
        public decimal? Maximo { get; set; }
        public List<string> CincoAmostras { get; } = new List<string>();
        public List<string> CincoTarefasNaoZero { get; } = new List<string>();
        public List<string> TitulosColunaTabela { get; set; } = new List<string>();

        public override string ToString() =>
            $"{FieldType} | Exibido='{NomeExibido}' | Alias={(Alias ?? "(nenhum)")} | TituloTabela=[{string.Join(", ", TitulosColunaTabela)}] | Origem={Origem} | " +
            $"TipoRetorno={(TipoRetorno ?? "(sem valor)")} | ComValor={QuantidadeComValor} | Vazio={QuantidadeVazio} | Zero={QuantidadeZero} | NaoZero={QuantidadeNaoZero} | " +
            $"Min={(Minimo?.ToString(CultureInfo.InvariantCulture) ?? "-")} | Max={(Maximo?.ToString(CultureInfo.InvariantCulture) ?? "-")} | " +
            $"Amostras=[{string.Join(", ", CincoAmostras)}] | TarefasNaoZero=[{string.Join(", ", CincoTarefasNaoZero)}]";
    }

    /// <summary>Relatório completo do diagnóstico somente-leitura de um .mpp (seção 4 do pedido).</summary>
    public sealed class RelatorioDiagnosticoCronogramaTipo1
    {
        public string ArquivoMpp { get; set; }
        public int TotalTarefas { get; set; }
        public int TotalTarefasComDocumento { get; set; }
        public ResolucaoCampoCronograma ResolucaoRealizado { get; set; }
        public ResolucaoCampoCronograma ResolucaoPrevisto { get; set; }
        public DistribuicaoCampoCronograma DistribuicaoRealizado { get; set; }
        public List<DiagnosticoCampoTarefaCronograma> Candidatos { get; } = new List<DiagnosticoCampoTarefaCronograma>();
    }
}
