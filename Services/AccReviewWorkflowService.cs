using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System.IO;
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using APS.WebApp.Models;
using NA_Connect.Services;

namespace APS.WebApp.Services
{
    public class AccReviewWorkflowService
    {
        private readonly HttpClient _http;
        private readonly ILogger<AccReviewWorkflowService>? _logger;
        private readonly AccStatusResolverService? _statusResolver;

        // ✅ ID do Administrador (Pego do seu token manual que funcionou)
        private const string AdminAutodeskId = "WDV284PFQTJDQZEG";

        private const int PageLimit = 50;

        /// <summary>
        /// Cache de definições de Workflow (endpoint /workflows/{workflowId}), persistente na instância do
        /// serviço — não recriado a cada chamada de BuildWorkflowResultAsync.
        /// </summary>
        private readonly Dictionary<string, JObject> _workflowDefinitionCache = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        public AccReviewWorkflowService(HttpClient http, ILogger<AccReviewWorkflowService> logger, AccStatusResolverService statusResolver)
        {
            _http = http;
            _logger = logger;
            _statusResolver = statusResolver;
        }

        public AccReviewWorkflowService(HttpClient http, ILogger<AccReviewWorkflowService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public AccReviewWorkflowService(HttpClient httpClient)
        {
            _http = httpClient;
        }

        // =======================================================================================
        // MODELOS DE APOIO
        // =======================================================================================

        internal class ArquivoProducaoAcc
        {
            public string ItemId { get; set; }
            public string VersionUrn { get; set; }
            public string FileName { get; set; }
            public string FileNameWithoutExtension { get; set; }
            public string Extension { get; set; }
            public string FileNameNormalizado { get; set; }
            public string NomeBaseNormalizado { get; set; }
            public string PastaEncontrada { get; set; }
        }

        private class PastaCandidata
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// Origem do status calculado para uma WorkflowStatusEntry — conjunto fechado (seção 8 da
        /// manutenção técnica "Status_In_Flow da LDP"). Nunca um valor livre.
        /// </summary>
        public enum OrigemStatusWorkflow
        {
            REVIEW_STATUS_DO_ITEM,
            REVIEW_VERSION_VERDICT,
            REVIEW_RESULT_EXPLICITO,
            ETAPA_ATUAL,
            PRESERVAR_CELULA,
            NAO_RESOLVIDO
        }

        /// <summary>
        /// Entrada pública do mapa de workflow: UM arquivo físico da árvore permitida da pasta de Produção
        /// + o status calculado para o Review AO QUAL AQUELA VERSÃO ESPECÍFICA PERTENCE (nunca um status
        /// obtido por busca global entre documentos). Uma entrada é criada MESMO QUANDO o status não pôde
        /// ser resolvido (PreservarCelula=true) — isso é o que permite ao consumidor (LdpUpdateService)
        /// saber que o Review MAIS RECENTE deste documento existe mas não tem valor confiável, e por isso
        /// NUNCA reaproveitar o resultado de um Review mais antigo (ver seção 6/7 da manutenção técnica).
        /// </summary>
        public class WorkflowStatusEntry
        {
            public string FileName { get; set; }
            public string FileNameWithoutExtension { get; set; }
            public string Extension { get; set; }
            public string ItemId { get; set; }
            public string VersionUrn { get; set; }
            public string PastaEncontrada { get; set; }
            public string Status { get; set; }
            public string ReviewId { get; set; }
            public string CurrentStepId { get; set; }
            public string WorkflowId { get; set; }
            public DateTime? DataAtualizacaoReview { get; set; }
            public bool EhVereditoFinal { get; set; }

            // ---- Campos novos (seção 8) ----
            public string ReviewStatusRaw { get; set; }
            public string ReviewStatusDisplay { get; set; }
            public string StatusCalculado { get; set; }
            public OrigemStatusWorkflow OrigemStatus { get; set; }
            public bool ReviewAberto { get; set; }
            public bool ReviewConcluido { get; set; }
            public DateTime? DataCriacaoReview { get; set; }
            public string ReviewVersionItemId { get; set; }
            public string ReviewVersionUrn { get; set; }
            public string PastaOrigemId { get; set; }
            public string PastaOrigemPath { get; set; }
            public string PastaDestinoPath { get; set; }
            public bool PreservarCelula { get; set; }
            public bool BloqueiaHistoricoAnterior { get; set; }
            public string CodigoResolucao { get; set; }
            public string MensagemResolucao { get; set; }

            // ---- Identificadores reais da versão (seção 3 da correção Status_In_Flow) ----
            // O payload de /reviews/{id}/versions usa "itemUrn" (não "itemId") e "urn" (nem sempre
            // "versionUrn"). Estes campos registram QUAL propriedade forneceu o valor, para auditoria —
            // nunca assuma silenciosamente qual delas existiu.
            public string ItemUrnOrigemPropriedade { get; set; }
            public string VersionUrnOrigemPropriedade { get; set; }

            /// <summary>
            /// true quando esta versão foi confirmada por ItemUrn/nome/prefixo de tokens contra um
            /// arquivo REALMENTE presente no snapshot atual de "10 - PRODUCTION" (evidência física forte).
            /// false quando a entrada foi criada diretamente a partir dos dados da própria versão do
            /// Review, sem correspondência no snapshot atual — nunca prova de que está "fora da árvore"
            /// (o snapshot só reflete a revisão mais recente viva de cada documento; a maioria das
            /// versões históricas de Review não terá um arquivo fisicamente idêntico nele). A segurança
            /// real de "é o mesmo documento lógico" acontece depois, em
            /// LdpUpdateService.EncontrarCorrespondenciaControlada, contra N&A/Client Document Number da
            /// LDP — este campo é só para diagnóstico/auditoria, nunca usado para bloquear a escrita.
            /// </summary>
            public bool LocalizacaoConfirmadaNaArvoreAtual { get; set; }
        }

        /// <summary>
        /// Resultado estruturado de <see cref="BuildWorkflowResultAsync"/> (seção 4 da correção
        /// Status_In_Flow). Substitui o retorno de tupla anterior, que não distinguia "mapa vazio porque
        /// não há documentos em revisão" de "mapa vazio porque uma falha técnica abortou a construção".
        /// Um consumidor (ex.: LdpUpdateService) DEVE checar <see cref="Sucesso"/> antes de tratar
        /// <see cref="Entries"/> vazio como "sem correspondência" — mapa vazio com arquivos e Reviews
        /// encontrados nunca é sucesso.
        /// </summary>
        public class WorkflowBuildResult
        {
            public List<WorkflowStatusEntry> Entries { get; set; } = new List<WorkflowStatusEntry>();
            public Dictionary<string, string> Map { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Metadados aditivos de etapa/responsável/vencimento de CADA Review — chave ReviewId (seção 3
            /// da funcionalidade "ACC_REVIEW_STAGE"). Construído na MESMA varredura de BuildWorkflowResultAsync
            /// (nenhum GET adicional de Reviews/versões/detalhe). Nunca influencia <see cref="Entries"/> nem
            /// <see cref="Map"/> — puramente aditivo, consumido por AccReviewStageSnapshotService.
            /// </summary>
            public Dictionary<string, ReviewStageMetadata> ReviewStagesByReviewId { get; set; } = new Dictionary<string, ReviewStageMetadata>(StringComparer.OrdinalIgnoreCase);

            public bool Sucesso { get; set; } = true;
            public string CodigoResultado { get; set; } = "OK";
            public string Mensagem { get; set; }
            public string ErroGlobal { get; set; }

            public int QuantidadeArquivosProducao { get; set; }
            public int QuantidadeReviews { get; set; }
            public int QuantidadeVersoes { get; set; }
            public int QuantidadeVersoesProcessadas { get; set; }
            public int QuantidadeVersoesPreservadas { get; set; }
            public int QuantidadeErrosPontuais { get; set; }
        }

        /// <summary>
        /// Resultado da extração segura do campo de sistema "Status da revisão" (approveStatus) de uma
        /// versão — seção 2 da correção Status_In_Flow. Nunca lança exceção: trata explicitamente
        /// propriedade ausente, JTokenType.Null, JObject e qualquer tipo inesperado.
        /// </summary>
        public readonly struct ResultadoApproveStatus
        {
            public bool TemResultadoFinalExplicito { get; init; }
            public string Label { get; init; }
            public string Value { get; init; }
            public string TipoTokenBruto { get; init; }
            public string CodigoExtracao { get; init; }
        }

        /// <summary>
        /// Extração segura de approveStatus (seção 2 da correção Status_In_Flow). NUNCA usa
        /// <c>token?["label"]</c>/<c>token?["value"]</c> sem antes confirmar <c>token is JObject</c> — um
        /// JValue representando JSON <c>null</c> não é uma referência C# nula, e indexá-lo por nome
        /// (<c>JValue["value"]</c>) lança "Cannot access child value on Newtonsoft.Json.Linq.JValue.",
        /// exatamente a exceção que antes derrubava BuildWorkflowResultAsync inteiro (silenciada pelo
        /// catch geral do método, devolvendo mapa vazio como se fosse sucesso).
        /// </summary>
        internal static ResultadoApproveStatus ExtrairApproveStatusSeguro(JToken versao)
        {
            JToken token = versao?["approveStatus"];

            if (token is JObject approveObject)
            {
                string label = approveObject["label"]?.ToString()?.Trim();
                string value = approveObject["value"]?.ToString()?.Trim();

                // Label é o valor exibido quando preenchido; value só é usado como exibição quando
                // label estiver vazio. Nunca traduzido, nunca normalizado, nunca hardcoded (um Review em
                // inglês retornando "Approved" chega como "Approved", nunca "Aprovado").
                string display = !string.IsNullOrWhiteSpace(label) ? label : value;

                return new ResultadoApproveStatus
                {
                    TemResultadoFinalExplicito = !string.IsNullOrWhiteSpace(display),
                    Label = display,
                    Value = value,
                    TipoTokenBruto = nameof(JObject),
                    CodigoExtracao = !string.IsNullOrWhiteSpace(display) ? "OBJETO_COM_RESULTADO" : "OBJETO_SEM_LABEL_NEM_VALUE"
                };
            }

            if (token == null || token.Type == JTokenType.Null)
            {
                return new ResultadoApproveStatus
                {
                    TemResultadoFinalExplicito = false,
                    Label = null,
                    Value = null,
                    TipoTokenBruto = token == null ? "AUSENTE" : nameof(JTokenType.Null),
                    CodigoExtracao = token == null ? "PROPRIEDADE_AUSENTE" : "JSON_NULL"
                };
            }

            // Tipo inesperado (string/number/array/etc.) — nunca indexar por nome; registra e preserva.
            return new ResultadoApproveStatus
            {
                TemResultadoFinalExplicito = false,
                Label = null,
                Value = null,
                TipoTokenBruto = token.Type.ToString(),
                CodigoExtracao = "TIPO_INESPERADO"
            };
        }

        /// <summary>
        /// Extração central do identificador do item físico de uma versão de Review (seção 3 da correção
        /// Status_In_Flow). O payload real de GET /reviews/{id}/versions usa <c>itemUrn</c> — o código
        /// antigo lia <c>itemId</c> (propriedade inexistente nesse payload), fazendo a correspondência por
        /// ItemId nunca disparar e cair sempre para nome completo/nome-base. <c>itemId</c> é mantido só
        /// como fallback de compatibilidade, caso algum payload futuro/diferente realmente o traga.
        /// </summary>
        internal static (string ItemUrn, string Origem) ExtrairItemUrnDaVersao(JToken versao)
        {
            string itemUrn = versao?["itemUrn"]?.ToString();
            if (!string.IsNullOrWhiteSpace(itemUrn)) return (itemUrn, "ITEM_URN");

            string itemId = versao?["itemId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(itemId)) return (itemId, "ITEM_ID_COMPATIBILIDADE");

            return (string.Empty, "NAO_ENCONTRADO");
        }

        /// <summary>
        /// Extração central da VersionUrn de uma versão de Review — o payload real usa <c>urn</c>;
        /// <c>versionUrn</c> é mantido como possibilidade caso apareça em outra variante do payload.
        /// </summary>
        internal static (string VersionUrn, string Origem) ExtrairVersionUrnDaVersao(JToken versao)
        {
            string versionUrn = versao?["versionUrn"]?.ToString();
            if (!string.IsNullOrWhiteSpace(versionUrn)) return (versionUrn, "VERSION_URN");

            string urn = versao?["urn"]?.ToString();
            if (!string.IsNullOrWhiteSpace(urn)) return (urn, "URN");

            return (string.Empty, "NAO_ENCONTRADO");
        }

        // =======================================================================================
        // NORMALIZAÇÃO E RESOLUÇÃO TOLERANTE DA PASTA DE PRODUÇÃO
        // =======================================================================================

        private static readonly string[] AliasesPastaProducao =
        {
            "10 -  PRODUCTION",
            "10 - PRODUCTION",
            "10 - PRODUÇÃO",
            "10 - PRODUCAO",
            "09 - ENGINEERING DOCUMENTS"
        };

        private static readonly (string PrefixoNumerico, string[][] GruposDePalavrasAceitas)[] FamiliasFallbackPastaProducao =
        {
            ("10", new[] { new[] { "PRODUCTION" }, new[] { "PRODUCAO" } }),
            ("09", new[] { new[] { "ENGINEERING", "DOCUMENT" } })
        };

        private static readonly string[] PalavrasPastaInvalida =
        {
            "OLD", "BACKUP", "ARCHIVE", "ARCHIVED", "SUPERSEDED", "SUPERADOS",
            "OBSOLETE", "DEPRECATED", "TEMP", "TEMPORARY", "COPY", "COPIA", "TEST", "TESTE"
        };

        private static string NormalizarNomePasta(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return string.Empty;

            string normalizado = nome.Trim().ToUpperInvariant();
            normalizado = normalizado.Normalize(NormalizationForm.FormD);
            normalizado = new string(normalizado
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray());
            normalizado = normalizado.Normalize(NormalizationForm.FormC);
            normalizado = Regex.Replace(normalizado, @"[\.\-_–—]+", " ");
            normalizado = Regex.Replace(normalizado, @"[^A-Z0-9\s]", " ");
            normalizado = Regex.Replace(normalizado, @"\s+", " ");
            return normalizado.Trim();
        }

        internal static string NormalizarNomeDocumento(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

            string normalizado = valor.Trim().ToUpperInvariant();
            normalizado = normalizado.Normalize(NormalizationForm.FormD);
            normalizado = new string(normalizado
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray());
            normalizado = normalizado.Normalize(NormalizationForm.FormC);
            normalizado = Regex.Replace(normalizado, @"[^A-Z0-9]", "");
            return normalizado;
        }

        private static bool ContemPalavraInvalida(string nomeNormalizado)
        {
            if (string.IsNullOrEmpty(nomeNormalizado)) return false;
            var tokens = nomeNormalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return tokens.Any(t => PalavrasPastaInvalida.Contains(t));
        }

        private static bool EhPastaDeProducao(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return false;

            string nomeNorm = NormalizarNomePasta(folderName);
            if (ContemPalavraInvalida(nomeNorm)) return false;

            var aliasesNormalizados = AliasesPastaProducao.Select(NormalizarNomePasta).Distinct().ToList();
            if (aliasesNormalizados.Contains(nomeNorm)) return true;

            foreach (var familia in FamiliasFallbackPastaProducao)
            {
                string prefixoNorm = NormalizarNomePasta(familia.PrefixoNumerico);
                bool prefixoOk = nomeNorm == prefixoNorm || nomeNorm.StartsWith(prefixoNorm + " ");
                if (prefixoOk && familia.GruposDePalavrasAceitas.Any(g => g.All(p => nomeNorm.Contains(p))))
                {
                    return true;
                }
            }

            return false;
        }

        private static DateTime LerData(JToken token, string campo)
        {
            var raw = token?[campo]?.ToString();
            return DateTime.TryParse(raw, out var dt) ? dt : DateTime.MinValue;
        }

        // =======================================================================================
        // MAPA DE STATUS DE WORKFLOW ("Status_In_Flow" da LDP) — PASTA DE PRODUÇÃO → ACC REVIEW → LDP
        // =======================================================================================
        //
        // NOVA PRIORIDADE (seção 2 da manutenção técnica "Status_In_Flow"): currentStepId deixou de ser
        // autoridade incondicional. Para CADA VERSÃO (nunca por busca global de documento):
        //   1) existe resultado final explícito (campo de sistema do ACC "Status da revisão", exposto
        //      como version["approveStatus"]["label"]/["value"] no payload de GET /reviews/{id}/versions
        //      — ver seção 4)? Se sim, ele VENCE, independente de currentStepId, preservado exatamente
        //      como veio (idioma/acentos/maiúsculas — nunca traduzido/normalizado/hardcoded).
        //   2) senão, o Review está aberto e currentStepId resolve para um nome de etapa? Usa a etapa.
        //   3) senão (aberto sem etapa resolvida, ou concluído sem resultado comprovado): preserva a
        //      célula existente — nunca inventa "EM REVISÃO" nem "CONCLUÍDO SEM VEREDITO" sem evidência.
        //
        // Uma entrada é SEMPRE criada (mesmo no caso 3, com PreservarCelula=true) para que o Review mais
        // recente de um documento sempre vença um Review mais antigo já resolvido no agrupamento feito
        // pelo consumidor (LdpUpdateService) — nunca um "Aprovado" antigo reaparece só porque o Review
        // novo não gerou entrada (ver seção 6/7).

        public async Task<Dictionary<string, string>> GetProjectWorkflowMapAsync(string hubId, string projectId, string accessToken)
        {
            var resultado = await BuildWorkflowResultAsync(hubId, projectId, accessToken);
            return resultado.Map;
        }

        public async Task<List<WorkflowStatusEntry>> GetProjectWorkflowEntriesAsync(string hubId, string projectId, string accessToken)
        {
            var resultado = await BuildWorkflowResultAsync(hubId, projectId, accessToken);
            return resultado.Entries;
        }

        /// <summary>
        /// Ponto de entrada estruturado (seção 4 da correção Status_In_Flow) — o único que carrega
        /// <see cref="WorkflowBuildResult.Sucesso"/>. Consumidores que precisam distinguir "mapa vazio
        /// porque não há nada para processar" de "mapa vazio porque a construção falhou tecnicamente"
        /// (ex.: LdpUpdateService antes de gravar/upload) DEVEM usar este método, não os dois acima.
        /// </summary>
        public async Task<WorkflowBuildResult> GetProjectWorkflowResultAsync(string hubId, string projectId, string accessToken)
        {
            return await BuildWorkflowResultAsync(hubId, projectId, accessToken);
        }

        private async Task<WorkflowBuildResult> BuildWorkflowResultAsync(string hubId, string projectId, string accessToken)
        {
            var resultado = new WorkflowBuildResult();
            string projectIdShort = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;

            // -----------------------------------------------------------------------------------------
            // ETAPA ESTRUTURAL: coleta da pasta de Produção + lista de Reviews. Uma falha AQUI é
            // estrutural (rede, autenticação, paginação, formato inesperado) — nunca deve virar "0
            // entradas, sucesso silencioso". Sucesso=false e a construção é interrompida.
            // -----------------------------------------------------------------------------------------
            List<ArquivoProducaoAcc> arquivosProducao;
            List<JToken> allReviews;

            try
            {
                _logger?.LogInformation($"📂 [WORKFLOW] Localizando pasta de Produção para o projeto {projectId}...");

                arquivosProducao = await ColetarArquivosDaProducaoAsync(hubId, projectId, accessToken);
                resultado.QuantidadeArquivosProducao = arquivosProducao.Count;

                _logger?.LogInformation($"🔍 [WORKFLOW] {arquivosProducao.Count} arquivo(s) coletado(s) na árvore da pasta de Produção (árvore permitida).");

                if (arquivosProducao.Count == 0)
                {
                    _logger?.LogWarning("⚠️ [WORKFLOW] Nenhuma pasta de Produção localizada ou nenhum arquivo encontrado. Cancelando mapeamento.");
                    resultado.CodigoResultado = "SEM_ARQUIVOS_PRODUCAO";
                    resultado.Mensagem = "Nenhuma pasta de Produção localizada ou nenhum arquivo encontrado — nada para processar (não é falha técnica).";
                    return resultado;
                }

                string reviewsUrl = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews";
                allReviews = await SendGetAllResultsAsync(reviewsUrl, accessToken, null);
                resultado.QuantidadeReviews = allReviews.Count;
                _logger?.LogInformation($"🗂️ [WORKFLOW] {allReviews.Count} review(s) encontrado(s) no projeto.");

                if (allReviews.Count == 0)
                {
                    _logger?.LogWarning("⚠️ [WORKFLOW] Nenhum review encontrado no projeto.");
                    resultado.CodigoResultado = "SEM_REVIEWS";
                    resultado.Mensagem = "Nenhum review encontrado no projeto — nada para processar (não é falha técnica).";
                    return resultado;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[WORKFLOW][MAPA][FALHA_GLOBAL] Falha estrutural ao coletar a pasta de Produção ou a lista de Reviews do projeto {ProjectId}.", projectId);
                resultado.Sucesso = false;
                resultado.CodigoResultado = "WORKFLOW_MAP_CONSTRUCAO_FALHOU";
                resultado.Mensagem = "Falha técnica na coleta estrutural (pasta de Produção e/ou lista de Reviews). Nenhuma entrada é confiável nesta execução.";
                resultado.ErroGlobal = ex.ToString();
                return resultado;
            }

            var indicePorBase = arquivosProducao
                .GroupBy(a => a.NomeBaseNormalizado, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var porItemUrn = arquivosProducao
                .Where(a => !string.IsNullOrEmpty(a.ItemId))
                .GroupBy(a => a.ItemId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var porNomeCompleto = arquivosProducao
                .Where(a => !string.IsNullOrEmpty(a.FileNameNormalizado))
                .GroupBy(a => a.FileNameNormalizado, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // -----------------------------------------------------------------------------------------
            // PASSO A: para cada Review, busca o DETALHE completo e suas versões. Falha pontual (um
            // review específico) é registrada e o loop continua — não é motivo para abortar tudo.
            // -----------------------------------------------------------------------------------------
            var detalhesPorReviewId = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var versoesPorReviewId = new Dictionary<string, List<JToken>>(StringComparer.OrdinalIgnoreCase);
            int versoesAnalisadas = 0;
            int errosPontuais = 0;

            foreach (var review in allReviews)
            {
                string reviewId = review["id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(reviewId)) continue;

                try
                {
                    var detail = await SendGetAsync(
                        $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews/{reviewId}",
                        accessToken);

                    if (detail != null)
                    {
                        detalhesPorReviewId[reviewId] = detail;
                    }
                    else
                    {
                        _logger?.LogWarning($"⚠️ [WORKFLOW] Review {reviewId}: falha ao obter o detalhe completo (GET /reviews/{{id}}) — nenhuma versão deste Review receberá status nesta execução.");
                    }

                    string versionsUrl = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/reviews/{reviewId}/versions";
                    var versions = await SendGetAllResultsAsync(versionsUrl, accessToken, null);

                    var versionsDoReview = new List<JToken>();
                    foreach (var v in versions)
                    {
                        if (v is JObject vObj) vObj["reviewId"] = reviewId;
                        versionsDoReview.Add(v);
                    }
                    versoesPorReviewId[reviewId] = versionsDoReview;
                    versoesAnalisadas += versions.Count;
                }
                catch (Exception ex)
                {
                    errosPontuais++;
                    _logger?.LogWarning(ex, "[WORKFLOW][VERSAO][ERRO_PONTUAL] ReviewId={ReviewId} | Falha ao buscar detalhe/versões deste Review — demais Reviews continuam sendo processados.", ResumirIdentificador(reviewId));
                }
            }

            resultado.QuantidadeVersoes = versoesAnalisadas;
            _logger?.LogInformation($"📄 [WORKFLOW] {versoesAnalisadas} versão(ões) de review analisadas | {detalhesPorReviewId.Count} de {allReviews.Count} detalhe(s) de Review obtidos com sucesso.");

            // -----------------------------------------------------------------------------------------
            // PASSO B: para cada Review, resolve a etapa atual (se aberto) UMA VEZ, e decide o status
            // FINAL de CADA VERSÃO isoladamente. Cada versão tem seu próprio try/catch: uma versão
            // malformada (ex.: approveStatus em formato inesperado, itemUrn ausente) NUNCA apaga as
            // entradas já coletadas de outras versões — só essa versão específica é descartada e
            // registrada como erro pontual.
            // -----------------------------------------------------------------------------------------
            const int MaxAmostrasRejeicaoArvoreNoLog = 5;

            var entries = new List<WorkflowStatusEntry>();
            int totalVereditoFinal = 0;
            int totalEtapaAtual = 0;
            int totalEtapaNaoResolvida = 0;
            int totalPreservarCelula = 0;
            int totalSemCorrespondenciaArvoreAtual = 0;
            int totalPorItemUrn = 0;
            int totalPorNomeCompleto = 0;
            int totalPorNomeBase = 0;
            int totalPorPrefixoDeTokens = 0;

            foreach (var review in allReviews)
            {
                string reviewId = review["id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(reviewId)) continue;
                if (!detalhesPorReviewId.TryGetValue(reviewId, out var detail)) continue;
                if (!versoesPorReviewId.TryGetValue(reviewId, out var versoesDoReview) || versoesDoReview.Count == 0) continue;

                string workflowId = detail["workflowId"]?.ToString()?.Trim() ?? string.Empty;
                string currentStepId = detail["currentStepId"]?.ToString()?.Trim() ?? string.Empty;

                DateTime dataAtualizacao = LerData(detail, "updatedAt");
                DateTime dataCriacao = LerData(detail, "createdAt");
                DateTime? dataResultado = dataAtualizacao != DateTime.MinValue ? dataAtualizacao : (dataCriacao != DateTime.MinValue ? dataCriacao : (DateTime?)null);
                DateTime? dataCriacaoResultado = dataCriacao == DateTime.MinValue ? (DateTime?)null : dataCriacao;

                // status VOID (documento retirado de circulação): nunca é "aberto" (não guarda mais
                // ação pendente) nem "concluído com resultado" — ReviewEstaAberto/ReviewEstaConcluido já
                // tratam isso corretamente (só reconhecem OPEN/INITIATED e CLOSED/COMPLETED
                // respectivamente), então VOID cai direto no ramo "preservar célula" de
                // DecidirStatusDaVersao — nunca vence um Review CLOSED mais recente, fica só como
                // histórico.
                bool reviewAberto = ReviewEstaAberto(detail);
                bool reviewConcluido = ReviewEstaConcluido(detail);

                // A etapa atual é a MESMA para todas as versões deste Review (currentStepId vive no
                // Review, não na versão) — resolvida uma única vez, só quando o Review está aberto. Uma
                // falha aqui é pontual deste Review — não derruba os demais.
                string nomeEtapaResolvida = null;
                string logEtapaResolucao = "Review não está aberto, ou currentStepId vazio — etapa não tentada.";

                if (reviewAberto && !string.IsNullOrWhiteSpace(currentStepId))
                {
                    try
                    {
                        (nomeEtapaResolvida, logEtapaResolucao) = await ResolverNomeEtapaAtualAsync(projectIdShort, workflowId, currentStepId, accessToken);
                    }
                    catch (Exception ex)
                    {
                        errosPontuais++;
                        logEtapaResolucao = $"Falha ao resolver nome da etapa (currentStepId={currentStepId}): {ex.Message}";
                        _logger?.LogWarning(ex, "[WORKFLOW][VERSAO][ERRO_PONTUAL] ReviewId={ReviewId} | Falha ao resolver nome da etapa atual — célula será preservada para as versões deste Review.", ResumirIdentificador(reviewId));
                    }
                }

                // ---------------------------------------------------------------------------------
                // ACC_REVIEW_STAGE (aditivo — seção 3): metadados de etapa/responsável/vencimento do
                // Review, construídos do MESMO `detail` já obtido acima (nenhum GET adicional). Nunca
                // participa da decisão de Status_In_Flow (DecidirStatusDaVersao/ExtrairApproveStatusSeguro
                // permanecem intocados) — falha aqui nunca aborta nem altera o mapeamento de status.
                // ---------------------------------------------------------------------------------
                try
                {
                    resultado.ReviewStagesByReviewId[reviewId] = ConstruirMetadadosEtapaReview(
                        review, detail, reviewId, workflowId, currentStepId, nomeEtapaResolvida, reviewAberto, dataResultado, _logger);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[ACC_REVIEW_STAGE][ERRO_PONTUAL] ReviewId={ReviewId} | Falha isolada ao montar metadados de etapa/responsável/vencimento — não afeta Status_In_Flow.", ResumirIdentificador(reviewId));
                }

                foreach (var v in versoesDoReview)
                {
                    string versionName = null;
                    try
                    {
                        versionName = v["name"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(versionName)) continue;

                        var (versionItemUrn, itemUrnOrigem) = ExtrairItemUrnDaVersao(v);
                        var (versionUrnAtual, versionUrnOrigem) = ExtrairVersionUrnDaVersao(v);

                        var (arquivosCorrespondentes, metodoCorrespondencia, confirmadaNaArvoreAtual) = ResolverArquivosDaVersaoComFallback(
                            versionItemUrn, versionName, versionUrnAtual, porItemUrn, porNomeCompleto, indicePorBase, arquivosProducao);

                        _logger?.LogDebug(
                            "[WORKFLOW][VERSAO][IDENTIFICADORES] ReviewId={ReviewId} | Arquivo={Arquivo} | ItemUrn={ItemUrn} | ItemUrnOrigem={ItemUrnOrigem} | VersionUrn={VersionUrn} | " +
                            "VersionUrnOrigem={VersionUrnOrigem} | MetodoCorrespondencia={Metodo}",
                            ResumirIdentificador(reviewId), versionName, ResumirIdentificador(versionItemUrn), itemUrnOrigem,
                            ResumirIdentificador(versionUrnAtual), versionUrnOrigem, metodoCorrespondencia);

                        // -----------------------------------------------------------------------------
                        // CORREÇÃO DA CAUSA RAIZ REAL (o run contra BML011.013 provou que a hipótese de
                        // sufixo de revisão — PREFIXO_TOKENS — não era a causa; PorPrefixoDeTokens=0).
                        //
                        // Prova por git history: o último AccReviewWorkflowService.cs commitado (antes da
                        // reescrita "Status_In_Flow", `git show 4797da5:...`) NUNCA teve esta árvore —
                        // ele confiava DIRETAMENTE no nome da versão do Review para popular o mapa. A
                        // árvore de "10 - PRODUCTION" foi introduzida só nesta reescrita e reflete SOMENTE
                        // o estado ATUAL (a última revisão viva de cada documento) — a maioria das
                        // versões HISTÓRICAS de Review (140 Reviews acumulados ao longo da vida do
                        // projeto) nunca terá um arquivo fisicamente idêntico nesse snapshot de 41
                        // arquivos, porque documentos superados são naturalmente substituídos/removidos.
                        // Isso NUNCA foi um problema real de segurança: a segurança de "é realmente o
                        // mesmo documento lógico" já é aplicada DEPOIS, em
                        // LdpUpdateService.EncontrarCorrespondenciaControlada — que só aceita uma entrada
                        // quando ela corresponde, por prefixo de tokens (nunca Contains), ao N&A Document
                        // Number OU ao Client Document Number de uma linha REAL da LDP, com revalidação
                        // obrigatória antes de qualquer escrita (linha ~1065). Rejeitar aqui, antes dessa
                        // checagem, descartava documentos legítimos sem nunca lhes dar a chance de serem
                        // confirmados pelo único critério que realmente prova "mesmo documento lógico".
                        //
                        // A partir daqui (ResolverArquivosDaVersaoComFallback): nenhuma versão é descartada
                        // só por não estar no snapshot atual da árvore. Quando os 4 níveis físicos não
                        // confirmam nada, a entrada é criada MESMO ASSIM, a partir dos dados da própria
                        // versão do Review — ela só se torna uma escrita real se depois for reivindicada
                        // por um código real da LDP.
                        // -----------------------------------------------------------------------------
                        if (!confirmadaNaArvoreAtual)
                        {
                            totalSemCorrespondenciaArvoreAtual++;

                            // Seção 11: amostra só as primeiras ocorrências em detalhe — o restante só
                            // incrementa o contador do resumo agregado, para nunca inundar o log. Nunca
                            // "REJEITADO"/"FORA_DA_ARVORE" — não há evidência de exclusão, só ausência de
                            // correspondência no snapshot atual (LOCALIZACAO_INCONCLUSIVA quanto à árvore;
                            // a entrada segue para a validação real por documento lógico).
                            if (totalSemCorrespondenciaArvoreAtual <= MaxAmostrasRejeicaoArvoreNoLog)
                            {
                                _logger?.LogDebug(
                                    "[WORKFLOW][ARVORE][SEM_MATCH_ATUAL] ReviewId={ReviewId} | Arquivo={Arquivo} | ItemUrn={ItemUrn} | VersionUrn={VersionUrn} | " +
                                    "Resultado=LOCALIZACAO_INCONCLUSIVA (não confirmado contra os {QtdArquivosArvore} arquivo(s) atuais de Produção) | " +
                                    "Entrada criada mesmo assim — validação real acontece depois, em LdpUpdateService, contra N&A/Client Document Number da LDP.",
                                    ResumirIdentificador(reviewId), versionName, ResumirIdentificador(versionItemUrn),
                                    ResumirIdentificador(versionUrnAtual), arquivosProducao.Count);
                            }
                        }

                        switch (metodoCorrespondencia)
                        {
                            case "ITEM_URN": totalPorItemUrn++; break;
                            case "NOME_COMPLETO": totalPorNomeCompleto++; break;
                            case "NOME_BASE": totalPorNomeBase++; break;
                            case "PREFIXO_TOKENS": totalPorPrefixoDeTokens++; break;
                            // SEM_CORRESPONDENCIA_ARVORE_ATUAL já contabilizado acima.
                        }

                        var decisao = DecidirStatusDaVersao(v, reviewAberto, currentStepId, nomeEtapaResolvida, logEtapaResolucao);

                        _logger?.LogDebug(
                            "[WORKFLOW][APPROVE_STATUS][EXTRACAO] ReviewId={ReviewId} | Arquivo={Arquivo} | TipoToken={TipoToken} | Label={Label} | Value={Value} | CodigoExtracao={CodigoExtracao}",
                            ResumirIdentificador(reviewId), versionName, decisao.ApproveStatusExtraido.TipoTokenBruto,
                            decisao.ApproveStatusExtraido.Label ?? "(null)", decisao.ApproveStatusExtraido.Value ?? "(null)",
                            decisao.ApproveStatusExtraido.CodigoExtracao);

                        switch (decisao.OrigemStatus)
                        {
                            case OrigemStatusWorkflow.REVIEW_VERSION_VERDICT: totalVereditoFinal++; break;
                            case OrigemStatusWorkflow.ETAPA_ATUAL: totalEtapaAtual++; break;
                            case OrigemStatusWorkflow.NAO_RESOLVIDO: totalEtapaNaoResolvida++; totalPreservarCelula++; break;
                            case OrigemStatusWorkflow.PRESERVAR_CELULA: totalPreservarCelula++; break;
                        }

                        string pastaOrigem = arquivosCorrespondentes.FirstOrDefault()?.PastaEncontrada;

                        _logger?.LogInformation(
                            "[WORKFLOW][REVIEW_DECISION] Documento={Documento} | ReviewId={ReviewId} | Data={Data} | StatusReview={StatusReview} | CurrentStepId={CurrentStepId} | " +
                            "StatusRevisaoBruto={StatusRevisaoBruto} | StatusRevisaoExibido={StatusRevisaoExibido} | OrigemEscolhida={Origem} | ResultadoFinalAplicado={ResultadoAplicado} | " +
                            "PastaOrigem={PastaOrigem} | PastaDestino={PastaDestino}",
                            versionName, ResumirIdentificador(reviewId), dataResultado, detail["status"]?.ToString(), currentStepId,
                            decisao.ReviewStatusRaw ?? "(null)", decisao.ReviewStatusDisplay ?? "(null)", decisao.OrigemStatus,
                            decisao.StatusCalculado ?? "(preservar célula)", pastaOrigem,
                            "(nenhum campo de \"pasta de destino para copiar arquivos\" é lido/usado como origem — ver seção 3)");

                        if (decisao.PreservarCelula)
                        {
                            _logger?.LogWarning(
                                "[WORKFLOW][HISTORICO][REVIEW_ATUAL_BLOQUEIA_ANTERIOR] Documento={Documento} | ReviewId={ReviewId} | CodigoResolucao={CodigoResolucao} | Motivo={Motivo}",
                                versionName, ResumirIdentificador(reviewId), decisao.CodigoResolucao, decisao.MensagemResolucao);
                        }

                        foreach (var arquivo in arquivosCorrespondentes)
                        {
                            entries.Add(new WorkflowStatusEntry
                            {
                                FileName = arquivo.FileName,
                                FileNameWithoutExtension = arquivo.FileNameWithoutExtension,
                                Extension = arquivo.Extension,
                                ItemId = arquivo.ItemId,
                                VersionUrn = arquivo.VersionUrn,
                                PastaEncontrada = arquivo.PastaEncontrada,
                                Status = decisao.StatusCalculado,
                                EhVereditoFinal = decisao.OrigemStatus == OrigemStatusWorkflow.REVIEW_VERSION_VERDICT,
                                ReviewId = reviewId,
                                CurrentStepId = currentStepId,
                                WorkflowId = workflowId,
                                DataAtualizacaoReview = dataResultado,

                                ReviewStatusRaw = decisao.ReviewStatusRaw,
                                ReviewStatusDisplay = decisao.ReviewStatusDisplay,
                                StatusCalculado = decisao.StatusCalculado,
                                OrigemStatus = decisao.OrigemStatus,
                                ReviewAberto = reviewAberto,
                                ReviewConcluido = reviewConcluido,
                                DataCriacaoReview = dataCriacaoResultado,
                                ReviewVersionItemId = versionItemUrn,
                                ReviewVersionUrn = versionUrnAtual,
                                PastaOrigemId = null,
                                PastaOrigemPath = arquivo.PastaEncontrada,
                                PastaDestinoPath = null,
                                PreservarCelula = decisao.PreservarCelula,
                                BloqueiaHistoricoAnterior = decisao.PreservarCelula,
                                CodigoResolucao = decisao.CodigoResolucao,
                                MensagemResolucao = decisao.MensagemResolucao,
                                ItemUrnOrigemPropriedade = itemUrnOrigem,
                                VersionUrnOrigemPropriedade = versionUrnOrigem,
                                LocalizacaoConfirmadaNaArvoreAtual = confirmadaNaArvoreAtual
                            });

                            _logger?.LogDebug(
                                $"[WORKFLOW] Arquivo: {arquivo.FileName} | Pasta: {arquivo.PastaEncontrada} | ReviewId: {reviewId} | Correspondência: {metodoCorrespondencia} | " +
                                $"Status: {decisao.StatusCalculado ?? "(preservar)"} | Origem: {decisao.OrigemStatus}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errosPontuais++;
                        _logger?.LogWarning(
                            ex,
                            "[WORKFLOW][VERSAO][ERRO_PONTUAL] ReviewId={ReviewId} | Versao={Versao} | Falha isolada ao processar esta versão — as demais versões e Reviews continuam sendo processados normalmente.",
                            ResumirIdentificador(reviewId), versionName ?? "(desconhecida)");
                    }
                }
            }

            // Reduz para um valor por documento (Nome-base) — Review mais recente por
            // DataAtualizacaoReview, mesmo critério usado pelo consumidor (LdpUpdateService). Quando a
            // entrada mais recente tem PreservarCelula=true, o valor no dicionário legado fica null —
            // este dicionário (GetProjectWorkflowMapAsync) não tem consumidor hoje; o fluxo real é
            // GetProjectWorkflowEntriesAsync, consumido por LdpUpdateService, que trata PreservarCelula
            // explicitamente (nunca grava, nunca reaproveita uma entrada mais antiga).
            var workflowMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var grupo in entries.GroupBy(e => NormalizarNomeDocumento(e.FileNameWithoutExtension), StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(grupo.Key)) continue;
                var maisRecente = grupo.OrderByDescending(e => e.DataAtualizacaoReview ?? DateTime.MinValue).First();
                workflowMap[grupo.Key] = maisRecente.Status;
            }

            int totalSemCorrespondencia = Math.Max(0, indicePorBase.Count - workflowMap.Count);

            _logger?.LogInformation(
                $"✅ [WORKFLOW] Mapeamento concluído: {workflowMap.Count} de {indicePorBase.Count} documento(s) único(s) da Produção com entrada ({entries.Count} entrada(s) de arquivo no total). " +
                $"Veredito final: {totalVereditoFinal} | Etapa atual: {totalEtapaAtual} | Etapas não resolvidas: {totalEtapaNaoResolvida} | Preservar célula: {totalPreservarCelula} | " +
                $"Confirmadas no snapshot atual — ItemUrn: {totalPorItemUrn} | nome completo: {totalPorNomeCompleto} | nome-base: {totalPorNomeBase} | prefixo de tokens: {totalPorPrefixoDeTokens} | " +
                $"Sem correspondência no snapshot atual (entrada criada mesmo assim, validada depois por N&A/Cliente na LDP): {totalSemCorrespondenciaArvoreAtual} | Erros pontuais: {errosPontuais}.");

            // Seção 11: resumo agregado — sempre UMA linha, nunca proporcional ao número de versões, mesmo
            // em projetos com milhares delas. "SemCorrespondenciaArvoreAtual" NUNCA significa "comprovadamente
            // fora da árvore" — só que o snapshot atual de Produção (última revisão viva de cada documento)
            // não tinha um arquivo fisicamente idêntico; a entrada foi criada e segue para validação real.
            _logger?.LogInformation(
                "[WORKFLOW][ARVORE][RESUMO] Reviews={Reviews} | VersoesAnalisadas={VersoesAnalisadas} | EntradasCriadas={EntradasCriadas} | " +
                "ConfirmadasNoSnapshotAtual={ConfirmadasNoSnapshotAtual} | SemCorrespondenciaArvoreAtual={SemCorrespondenciaArvoreAtual} | PorItemUrn={PorItemUrn} | " +
                "PorNomeCompleto={PorNomeCompleto} | PorNomeBase={PorNomeBase} | PorPrefixoDeTokens={PorPrefixoDeTokens} | DocumentosMapeados={DocumentosMapeados}",
                allReviews.Count, versoesAnalisadas, entries.Count, entries.Count(e => e.LocalizacaoConfirmadaNaArvoreAtual), totalSemCorrespondenciaArvoreAtual,
                totalPorItemUrn, totalPorNomeCompleto, totalPorNomeBase, totalPorPrefixoDeTokens, workflowMap.Count);

            resultado.Entries = entries;
            resultado.Map = workflowMap;
            resultado.QuantidadeVersoesProcessadas = entries.Count;
            resultado.QuantidadeVersoesPreservadas = totalPreservarCelula;
            resultado.QuantidadeErrosPontuais = errosPontuais;

            if (entries.Count == 0)
            {
                // Mapa vazio com arquivos de Produção E Reviews encontrados nunca é sucesso — antes desta
                // correção, isso era exatamente o sintoma de uma exceção engolida pelo catch geral (ver
                // seção 1 da correção Status_In_Flow). Sucesso=false força o consumidor (LdpUpdateService)
                // a abortar sem alterar a LDP, em vez de tratar como "nenhum documento em revisão".
                _logger?.LogError(
                    "[WORKFLOW][MAPA][FALHA_GLOBAL] WORKFLOW INCONSISTENTE: {QtdArquivos} arquivo(s) da pasta de Produção e {QtdReviews} review(s) foram encontrados, " +
                    "mas nenhuma versão de Review resultou em entrada válida ({ErrosPontuais} erro(s) pontual(is) registrados). Mapa vazio não é tratado como sucesso.",
                    arquivosProducao.Count, allReviews.Count, errosPontuais);
                resultado.Sucesso = false;
                resultado.CodigoResultado = "WORKFLOW_MAPA_VAZIO_INESPERADO";
                resultado.Mensagem = $"{arquivosProducao.Count} arquivo(s) de Produção e {allReviews.Count} review(s) encontrados, mas nenhuma versão resultou em entrada válida ({errosPontuais} erro(s) pontual(is)).";
            }
            else
            {
                resultado.CodigoResultado = "OK";
                resultado.Mensagem = $"{entries.Count} entrada(s) construída(s) com sucesso ({errosPontuais} erro(s) pontual(is) isolados, sem afetar as demais).";
            }

            return resultado;
        }

        // =======================================================================================
        // DECISÃO ISOLADA POR VERSÃO (seção 2/5/9) — nunca usa a coleção global de Reviews/versões.
        // =======================================================================================

        /// <summary>
        /// Resultado interno da decisão de status de UMA versão — nunca calculado a partir de outra
        /// versão/Review/documento.
        /// </summary>
        internal readonly struct DecisaoStatusVersao
        {
            public string StatusCalculado { get; init; }
            public OrigemStatusWorkflow OrigemStatus { get; init; }
            public bool PreservarCelula { get; init; }
            public string CodigoResolucao { get; init; }
            public string MensagemResolucao { get; init; }
            public string ReviewStatusRaw { get; init; }
            public string ReviewStatusDisplay { get; init; }
            public ResultadoApproveStatus ApproveStatusExtraido { get; init; }
        }

        /// <summary>
        /// Equivalente a ResolverStatusFinalDaVersao (seção 5/9). Recebe SOMENTE dados da versão atual
        /// (a versão já contém approveStatus embutido — o campo de sistema "Status da revisão" do ACC,
        /// ver seção 4) e da etapa já resolvida (se o Review estava aberto). Ordem obrigatória (seção 2):
        ///   1) resultado final explícito na VERSÃO -> vence, sempre, preservado exatamente como veio;
        ///   2) Review aberto + etapa resolvida -> nome da etapa;
        ///   3) Review aberto + etapa não resolvida -> preservar célula;
        ///   4) Review não aberto e sem resultado final comprovado -> preservar célula (nunca inventa
        ///      "CONCLUÍDO SEM VEREDITO").
        /// </summary>
        internal static DecisaoStatusVersao DecidirStatusDaVersao(
            JToken versao, bool reviewAberto, string currentStepId, string nomeEtapaResolvida, string logEtapaResolucao)
        {
            // Extração SEMPRE via ExtrairApproveStatusSeguro — nunca indexar approveStatus diretamente
            // aqui (ver seção 2 da correção Status_In_Flow: JValue["value"] em approveStatus:null lançava
            // "Cannot access child value on Newtonsoft.Json.Linq.JValue.").
            var approve = ExtrairApproveStatusSeguro(versao);

            if (approve.TemResultadoFinalExplicito)
            {
                return new DecisaoStatusVersao
                {
                    StatusCalculado = approve.Label,
                    OrigemStatus = OrigemStatusWorkflow.REVIEW_VERSION_VERDICT,
                    PreservarCelula = false,
                    CodigoResolucao = "RESULTADO_FINAL_EXPLICITO",
                    MensagemResolucao = "Status da revisão (approveStatus) presente na versão — preservado exatamente como retornado pelo ACC. Vence currentStepId.",
                    ReviewStatusRaw = approve.Value,
                    ReviewStatusDisplay = approve.Label,
                    ApproveStatusExtraido = approve
                };
            }

            if (reviewAberto)
            {
                if (!string.IsNullOrWhiteSpace(currentStepId))
                {
                    if (!string.IsNullOrWhiteSpace(nomeEtapaResolvida))
                    {
                        return new DecisaoStatusVersao
                        {
                            StatusCalculado = nomeEtapaResolvida,
                            OrigemStatus = OrigemStatusWorkflow.ETAPA_ATUAL,
                            PreservarCelula = false,
                            CodigoResolucao = "ETAPA_RESOLVIDA",
                            MensagemResolucao = logEtapaResolucao,
                            ApproveStatusExtraido = approve
                        };
                    }

                    return new DecisaoStatusVersao
                    {
                        StatusCalculado = null,
                        OrigemStatus = OrigemStatusWorkflow.NAO_RESOLVIDO,
                        PreservarCelula = true,
                        CodigoResolucao = "STEP_NAO_RESOLVIDO",
                        MensagemResolucao = logEtapaResolucao,
                        ApproveStatusExtraido = approve
                    };
                }

                return new DecisaoStatusVersao
                {
                    StatusCalculado = null,
                    OrigemStatus = OrigemStatusWorkflow.PRESERVAR_CELULA,
                    PreservarCelula = true,
                    CodigoResolucao = "REVIEW_ABERTO_SEM_ETAPA",
                    MensagemResolucao = "Review aberto, sem currentStepId e sem resultado final explícito — célula preservada (nunca inventa 'EM REVISÃO' sem evidência real).",
                    ApproveStatusExtraido = approve
                };
            }

            return new DecisaoStatusVersao
            {
                StatusCalculado = null,
                OrigemStatus = OrigemStatusWorkflow.PRESERVAR_CELULA,
                PreservarCelula = true,
                CodigoResolucao = "CONCLUIDO_SEM_RESULTADO_COMPROVADO",
                MensagemResolucao = "Review não está aberto (inclui VOID) e nenhum resultado final (approveStatus) foi encontrado na versão — célula preservada (nunca inventa 'CONCLUÍDO SEM VEREDITO'). VOID nunca é tratado como aberto nem como resultado final.",
                ApproveStatusExtraido = approve
            };
        }

        // =======================================================================================
        // ACC_REVIEW_STAGE (aditivo) — metadados de etapa/responsável/vencimento de UM Review, extraídos
        // do payload já lido por BuildWorkflowResultAsync (GET .../reviews e GET .../reviews/{id}, ambos
        // no MESMO shape). Nunca participa de Status_In_Flow. JSON Paths comprovados empiricamente contra
        // os 134 Reviews reais do projeto BML011.013 (Review 603 = CHECKER / Roberto Andrei / candidato):
        //   - review.sequenceId            -> número visual do Review (ex.: 603)
        //   - review.name / review.status  -> nome e status do Review
        //   - review.nextActionBy.claimedBy[]            -> responsável EFETIVAMENTE atribuído (nunca
        //     observado preenchido nos 134 Reviews reais — mantido como prioridade 1 para quando existir)
        //   - review.nextActionBy.candidates.users[]     -> candidatos da etapa (nome + autodeskId)
        //   - review.currentStepDueDate    -> vencimento da ETAPA ATUAL (ISO-8601 UTC, sufixo 'Z'); é
        //     null em Reviews CLOSED/VOID — nunca createdAt/updatedAt/estimativa.
        // =======================================================================================

        internal static ReviewStageMetadata ConstruirMetadadosEtapaReview(
            JToken review, JObject detail, string reviewId, string workflowId, string currentStepId,
            string nomeEtapaResolvida, bool reviewAberto, DateTime? dataAtualizacaoReview, ILogger? logger)
        {
            var metadados = new ReviewStageMetadata
            {
                ReviewId = reviewId,
                ReviewNumber = review?["sequenceId"]?.ToString(),
                ReviewName = review?["name"]?.ToString(),
                ReviewStatus = detail?["status"]?.ToString(),
                WorkflowId = workflowId,
                ReviewUpdatedAt = dataAtualizacaoReview
            };

            if (!reviewAberto)
            {
                // Seção 9: Review não aberto (inclui CLOSED e VOID) nunca tem etapa/responsável/vencimento
                // "atual" — nunca grava "Aprovado" nem qualquer outro valor como EtapaAtual aqui.
                metadados.CurrentStepId = null;
                metadados.CurrentStepName = null;
                metadados.ResponsibleSource = "REVIEW_NAO_ABERTO";
                metadados.DueDateSource = "REVIEW_NAO_ABERTO";
                return metadados;
            }

            metadados.CurrentStepId = currentStepId;
            metadados.CurrentStepName = nomeEtapaResolvida;

            var (nomes, ids, origemResponsavel) = ExtrairResponsavelEtapa(detail);
            metadados.ResponsibleNames = nomes;
            metadados.ResponsibleIds = ids;
            metadados.ResponsibleSource = origemResponsavel;

            var (dueOriginal, dueLocal, origemVencimento) = ExtrairVencimentoEtapa(detail, logger, reviewId);
            metadados.DueDateRaw = detail?["currentStepDueDate"]?.ToString();
            metadados.DueDateOriginal = dueOriginal;
            metadados.DueDateLocal = dueLocal;
            metadados.DueDateSource = origemVencimento;

            if (origemResponsavel == "NAO_ENCONTRADO")
            {
                logger?.LogWarning(
                    "[ACC_REVIEW_STAGE][RESPONSAVEL_NAO_RESOLVIDO] ReviewId={ReviewId} | Numero={Numero} | Etapa={Etapa}",
                    ResumirIdentificador(reviewId), metadados.ReviewNumber ?? "(n/d)", nomeEtapaResolvida ?? "(n/d)");
            }

            if (dueOriginal == null)
            {
                logger?.LogWarning(
                    "[ACC_REVIEW_STAGE][VENCIMENTO_NAO_RESOLVIDO] ReviewId={ReviewId} | Numero={Numero} | OrigemVencimento={OrigemVencimento}",
                    ResumirIdentificador(reviewId), metadados.ReviewNumber ?? "(n/d)", origemVencimento);
            }

            logger?.LogInformation(
                "[ACC_REVIEW_STAGE][REVIEW] ReviewId={ReviewId} | Numero={Numero} | Nome={Nome} | Status={Status} | Etapa={Etapa} | " +
                "Responsavel={Responsavel} | OrigemResponsavel={OrigemResponsavel} | Vencimento={Vencimento} | OrigemVencimento={OrigemVencimento}",
                ResumirIdentificador(reviewId), metadados.ReviewNumber ?? "(n/d)", metadados.ReviewName ?? "(n/d)", metadados.ReviewStatus ?? "(n/d)",
                metadados.CurrentStepName ?? "(n/d)", metadados.ResponsibleNames.Count > 0 ? string.Join("; ", metadados.ResponsibleNames) : "(nenhum)",
                metadados.ResponsibleSource, metadados.DueDateLocal?.ToString("yyyy-MM-dd") ?? "(n/d)", metadados.DueDateSource);

            return metadados;
        }

        /// <summary>
        /// Prioridade fixa (seção 5): 1) RESPONSAVEL_ATRIBUIDO (nextActionBy.claimedBy — responsável
        /// efetivamente atribuído); 2)/3) PENDENTE_POR/PARTICIPANTE_ATUAL não têm campo próprio comprovado
        /// neste payload (não implementados por falta de evidência — nunca inventados); 4) CANDIDATO_ETAPA
        /// (nextActionBy.candidates.users — candidatos da etapa, não necessariamente já atribuídos);
        /// 5) NAO_ENCONTRADO. Nunca escolhe só o primeiro nome — todos os usuários do array vencedor são
        /// retornados, nomes e IDs sempre pareados na mesma ordem (ordenados por nome, estável).
        /// </summary>
        internal static (List<string> Names, List<string> Ids, string Origem) ExtrairResponsavelEtapa(JObject reviewDetail)
        {
            var nextActionBy = reviewDetail?["nextActionBy"] as JObject;
            if (nextActionBy == null)
            {
                return (new List<string>(), new List<string>(), "NAO_ENCONTRADO");
            }

            var claimedBy = nextActionBy["claimedBy"] as JArray;
            if (claimedBy != null && claimedBy.Count > 0)
            {
                var (nomesAtribuido, idsAtribuido) = ExtrairNomesEIdsDeUsuarios(claimedBy);
                if (nomesAtribuido.Count > 0) return (nomesAtribuido, idsAtribuido, "RESPONSAVEL_ATRIBUIDO");
            }

            var candidateUsers = nextActionBy["candidates"]?["users"] as JArray;
            if (candidateUsers != null && candidateUsers.Count > 0)
            {
                var (nomesCandidato, idsCandidato) = ExtrairNomesEIdsDeUsuarios(candidateUsers);
                if (nomesCandidato.Count > 0) return (nomesCandidato, idsCandidato, "CANDIDATO_ETAPA");
            }

            return (new List<string>(), new List<string>(), "NAO_ENCONTRADO");
        }

        private static (List<string> Names, List<string> Ids) ExtrairNomesEIdsDeUsuarios(JArray usuarios)
        {
            var nomes = new List<string>();
            var ids = new List<string>();

            foreach (var usuario in usuarios.OfType<JObject>())
            {
                string nome = usuario["name"]?.ToString()?.Trim();
                string id = usuario["autodeskId"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(nome) && string.IsNullOrWhiteSpace(id)) continue;

                nomes.Add(nome ?? string.Empty);
                ids.Add(id ?? string.Empty);
            }

            // Ordenação estável por nome — nomes e IDs permanecem pareados na mesma ordem (nunca só o
            // primeiro é escolhido; a lista inteira é preservada).
            var ordemEstavel = Enumerable.Range(0, nomes.Count)
                .OrderBy(i => nomes[i], StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (
                ordemEstavel.Select(i => nomes[i]).ToList(),
                ordemEstavel.Select(i => ids[i]).ToList());
        }

        /// <summary>
        /// Fuso horário fixo de referência (America/Sao_Paulo = UTC-3, sem horário de verão desde 2019).
        /// Tenta o ID IANA primeiro (Linux/containers/.NET com ICU) e cai para o ID do Windows; se nenhum
        /// dos dois existir no SO, usa um fuso customizado fixo em -03:00 — nunca falha silenciosamente
        /// convertendo como se fosse UTC.
        /// </summary>
        private static readonly Lazy<TimeZoneInfo> FusoSaoPaulo = new Lazy<TimeZoneInfo>(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            return TimeZoneInfo.CreateCustomTimeZone("America/Sao_Paulo (fixo)", TimeSpan.FromHours(-3), "America/Sao_Paulo (fixo)", "America/Sao_Paulo (fixo)");
        });

        /// <summary>
        /// Vencimento da etapa atual (seção 6): SEMPRE review.currentStepDueDate — nunca createdAt/
        /// updatedAt/estimativa de outra etapa. Converte o timestamp real (com offset/UTC) para
        /// America/Sao_Paulo e SÓ ENTÃO extrai a data (nunca DateTime.Parse(...).Date direto sobre o
        /// valor UTC bruto, que erraria o dia perto da meia-noite). logger/reviewId são opcionais (testes
        /// não precisam fornecer) — só usados para o diagnóstico [VENCIMENTO_RAW] (seção 2 do pedido
        /// 16/09/2026), nunca para decidir o resultado.
        /// </summary>
        internal static (DateTimeOffset? Original, DateOnly? Local, string Origem) ExtrairVencimentoEtapa(
            JObject reviewDetail, ILogger? logger = null, string? reviewId = null)
        {
            var token = reviewDetail?["currentStepDueDate"];
            string bruto = token?.ToString();
            if (string.IsNullOrWhiteSpace(bruto))
            {
                return (null, null, "NAO_ENCONTRADO");
            }

            // Diagnóstico seguro: nunca loga token/credencial, só o valor bruto do campo de vencimento e o
            // tipo JSON dele — necessário para confirmar o formato real devolvido pela Autodesk antes (e
            // depois) de estender o parser abaixo.
            logger?.LogInformation(
                "[ACC_REVIEW_STAGE][VENCIMENTO_RAW] ReviewId={ReviewId} | TipoToken={TipoToken} | ValorRaw={ValorRaw} | Fonte=currentStepDueDate",
                ResumirIdentificador(reviewId ?? ""), token?.Type.ToString() ?? "(ausente)", bruto);

            if (!TentarParseVencimento(bruto, out var original))
            {
                return (null, null, "FORMATO_INVALIDO");
            }

            var local = TimeZoneInfo.ConvertTime(original, FusoSaoPaulo.Value);
            return (original, DateOnly.FromDateTime(local.DateTime), "CURRENT_STEP_DUE_DATE");
        }

        /// <summary>
        /// Tolerante aos formatos realmente observados além do ISO-8601 padrão, sem nunca usar
        /// DateTime.Parse dependente da cultura da máquina (sempre InvariantCulture/UTC explícito):
        /// 1) ISO-8601 com sufixo de zona entre colchetes ao estilo java.time.ZonedDateTime.toString()
        /// (ex.: "2026-09-15T00:00:00-04:00[America/New_York]") — .NET não reconhece o colchete; removemos
        /// só esse sufixo e reparseamos o offset real que já estava presente antes dele — nunca assumimos
        /// UTC no lugar do offset informado. 2) epoch UTC em milissegundos ou segundos, quando o valor é
        /// puramente numérico (nunca um texto que só coincidentemente começa com dígito).
        /// </summary>
        private static bool TentarParseVencimento(string bruto, out DateTimeOffset original)
        {
            if (DateTimeOffset.TryParse(bruto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out original))
                return true;

            int colchete = bruto.IndexOf('[');
            if (colchete > 0)
            {
                string semZoneId = bruto.Substring(0, colchete);
                if (DateTimeOffset.TryParse(semZoneId, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out original))
                    return true;
            }

            if (long.TryParse(bruto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            {
                try
                {
                    original = epoch > 99_999_999_999L // acima disso só faz sentido como milissegundos
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    original = default;
                    return false;
                }
            }

            original = default;
            return false;
        }

        // =======================================================================================
        // RESOLUÇÃO DA ETAPA ATUAL — currentStepId (do DETALHE do Review) só é consultado quando o
        // Review está aberto E não existe resultado final explícito na versão (ver DecidirStatusDaVersao).
        // =======================================================================================

        private static bool ReviewEstaConcluido(JToken review)
        {
            var st = (review?["status"]?.ToString() ?? "").Trim();
            return st.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) || st.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReviewEstaAberto(JToken review)
        {
            var st = (review?["status"]?.ToString() ?? "").Trim();
            return st.Equals("OPEN", StringComparison.OrdinalIgnoreCase) || st.Equals("INITIATED", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] CamposIdEtapa = { "id", "stepId", "laneId", "key", "nodeId" };
        private static readonly string[] CamposNomeEtapa = { "name", "displayName", "label", "title" };

        /// <summary>
        /// Resolve o nome da etapa atual (currentStepId -> workflowId -> definição do Workflow -> nome),
        /// UMA VEZ por Review (reaproveitado por todas as versões desse Review — currentStepId vive no
        /// Review, não na versão). Lógica estruturada + recursiva + JSON/XML embutido preservada
        /// integralmente da versão anterior (comprovadamente funcional); só foi extraída para um método
        /// próprio para poder ser chamada a partir da nova decisão por versão.
        /// </summary>
        private async Task<(string NomeEtapa, string ResultadoLog)> ResolverNomeEtapaAtualAsync(string projectIdShort, string workflowId, string currentStepId, string accessToken)
        {
            string nomeEtapa = null;
            string campoNomeCorrespondente = null;
            bool definicaoCarregada = false;
            bool stepEncontradoDireto = false;
            bool stepEncontradoRecursivo = false;
            bool veioDoCache = false;
            int objetosExaminados = 0;
            JObject objetoEtapa = null;
            string resolvidoVia = "ESTRUTURADO";
            string apareceuEmString = "não";
            string caminhoPropriedadeString = null;
            int tamanhoConteudoString = 0;
            string formatoDetectado = null;

            if (!string.IsNullOrWhiteSpace(workflowId))
            {
                string cacheKey = projectIdShort + "::" + workflowId;
                veioDoCache = _workflowDefinitionCache.TryGetValue(cacheKey, out var wfDef);
                if (!veioDoCache)
                {
                    wfDef = await SendGetAsync($"https://developer.api.autodesk.com/construction/reviews/v1/projects/{projectIdShort}/workflows/{workflowId}", accessToken);
                    if (wfDef != null) _workflowDefinitionCache[cacheKey] = wfDef;
                }
                definicaoCarregada = wfDef != null;

                objetoEtapa = EncontrarEtapaDireta(wfDef, currentStepId);
                stepEncontradoDireto = objetoEtapa != null;
                string campoIdCorrespondente = stepEncontradoDireto ? "id" : null;

                if (!stepEncontradoDireto)
                {
                    objetoEtapa = EncontrarObjetoDaEtapaRecursivamente(wfDef, currentStepId, out campoIdCorrespondente, out objetosExaminados);
                    stepEncontradoRecursivo = objetoEtapa != null;
                }

                if (objetoEtapa != null)
                {
                    nomeEtapa = ExtrairNomeOriginalEtapa(objetoEtapa, out campoNomeCorrespondente);
                    if (string.IsNullOrWhiteSpace(nomeEtapa))
                    {
                        nomeEtapa = ExtrairNomeOriginalEtapaAncestral(objetoEtapa, out campoNomeCorrespondente, out _);
                    }
                }

                if (string.IsNullOrWhiteSpace(nomeEtapa) && definicaoCarregada)
                {
                    var achado = BuscarStringContendoStepId(wfDef, currentStepId);
                    if (achado.Apareceu)
                    {
                        apareceuEmString = "sim";
                        caminhoPropriedadeString = achado.CaminhoPropriedade;
                        tamanhoConteudoString = achado.Tamanho;
                        formatoDetectado = achado.Formato;

                        if (achado.Formato == "JSON" && achado.JsonInterno != null)
                        {
                            string nomeInterno = ResolverNomeDentroDeJsonInterno(achado.JsonInterno, currentStepId, out string campoInterno);
                            if (!string.IsNullOrWhiteSpace(nomeInterno))
                            {
                                nomeEtapa = nomeInterno;
                                campoNomeCorrespondente = campoInterno;
                                resolvidoVia = "JSON_INTERNO";
                            }
                        }
                        else if (achado.Formato == "XML" && achado.XmlInterno != null)
                        {
                            string nomeInterno = ResolverNomeDentroDeXmlInterno(achado.XmlInterno, currentStepId, out string campoInterno);
                            if (!string.IsNullOrWhiteSpace(nomeInterno))
                            {
                                nomeEtapa = nomeInterno;
                                campoNomeCorrespondente = campoInterno;
                                resolvidoVia = "XML_INTERNO";
                            }
                        }
                    }
                }

                string resultadoFinalLogLocal;
                string motivoFallbackLog = null;

                if (!definicaoCarregada)
                {
                    resultadoFinalLogLocal = "DEFINICAO_WORKFLOW_INDISPONIVEL";
                    motivoFallbackLog = string.IsNullOrWhiteSpace(workflowId)
                        ? "workflowId ausente no detalhe do Review."
                        : "Definição do Workflow não pôde ser carregada (nem cache, nem API).";
                }
                else if (!string.IsNullOrWhiteSpace(nomeEtapa))
                {
                    resultadoFinalLogLocal = resolvidoVia switch
                    {
                        "JSON_INTERNO" => "STEP_RESOLVIDO_JSON_INTERNO",
                        "XML_INTERNO" => "STEP_RESOLVIDO_XML_INTERNO",
                        _ => "STEP_RESOLVIDO_ESTRUTURADO"
                    };
                }
                else if (objetoEtapa == null)
                {
                    resultadoFinalLogLocal = "LANE_ID_AUSENTE_NA_DEFINICAO";
                    motivoFallbackLog = "currentStepId não encontrado em nenhum id/stepId/laneId/key/nodeId da definição, nem em texto/JSON/XML embutido.";
                }
                else
                {
                    resultadoFinalLogLocal = "LANE_ID_PRESENTE_SEM_NOME";
                    motivoFallbackLog = "Nó encontrado pelo ID, mas nenhum campo name/displayName/label/title (nele ou em ancestrais) continha um nome.";
                }

                _logger?.LogDebug(
                    "[WORKFLOW][STEP_RESOLUTION] WorkflowId={WorkflowId} | CurrentStepId={CurrentStepId} | " +
                    "DefinicaoVeioDe={DefinicaoVeioDe} | DefinicaoCarregada={DefinicaoCarregada} | EncontradoComoIdEstruturado={EncontradoEstruturado} | " +
                    "CaminhoJson={CaminhoJson} | TipoNo={TipoNo} | CampoNome={CampoNome} | NomeExtraido={NomeExtraido} | " +
                    "NosExaminados={NosExaminados} | ApareceuEmString={ApareceuEmString} | CaminhoPropriedadeString={CaminhoPropriedadeString} | " +
                    "TamanhoConteudoString={TamanhoConteudoString} | FormatoDetectado={FormatoDetectado} | ResultadoFinal={ResultadoFinal} | MotivoFallback={MotivoFallback}",
                    workflowId, currentStepId,
                    veioDoCache ? "cache" : (definicaoCarregada ? "API" : "falha"), definicaoCarregada, stepEncontradoDireto || stepEncontradoRecursivo,
                    objetoEtapa?.Path ?? "-", objetoEtapa?.Type.ToString() ?? "-",
                    campoNomeCorrespondente ?? "-", nomeEtapa ?? "-",
                    objetosExaminados, apareceuEmString, caminhoPropriedadeString ?? "-", tamanhoConteudoString, formatoDetectado ?? "-",
                    resultadoFinalLogLocal, motivoFallbackLog ?? "-");

                return (nomeEtapa, resultadoFinalLogLocal);
            }

            return (null, "WORKFLOW_ID_AUSENTE_NO_DETALHE_DO_REVIEW");
        }

        private static JObject EncontrarEtapaDireta(JObject workflowDefinition, string currentStepId)
        {
            if (workflowDefinition == null || string.IsNullOrWhiteSpace(currentStepId))
            {
                return null;
            }

            return workflowDefinition["steps"]?
                .OfType<JObject>()
                .FirstOrDefault(step =>
                    string.Equals(step["id"]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase));
        }

        private static JObject EncontrarObjetoDaEtapaRecursivamente(JObject workflowDefinition, string currentStepId, out string campoCorrespondente, out int objetosExaminados)
        {
            campoCorrespondente = null;
            objetosExaminados = 0;

            if (workflowDefinition == null || string.IsNullOrWhiteSpace(currentStepId))
            {
                return null;
            }

            foreach (JObject objeto in workflowDefinition.DescendantsAndSelf().OfType<JObject>())
            {
                objetosExaminados++;

                foreach (string campo in CamposIdEtapa)
                {
                    if (string.Equals(objeto[campo]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase))
                    {
                        campoCorrespondente = campo;
                        return objeto;
                    }
                }
            }

            return null;
        }

        private static string ExtrairNomeOriginalEtapa(JObject objeto, out string campoNome)
        {
            campoNome = null;
            if (objeto == null) return null;

            foreach (var campo in CamposNomeEtapa)
            {
                string valor = objeto[campo]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    campoNome = campo;
                    return valor;
                }
            }

            return null;
        }

        private static string ExtrairNomeOriginalEtapaAncestral(JObject objetoEtapa, out string campoNome, out int niveisSubidos)
        {
            campoNome = null;
            niveisSubidos = 0;

            JToken atual = objetoEtapa?.Parent;
            int subidas = 0;

            while (atual != null && subidas < 6)
            {
                if (atual is JObject objAncestral)
                {
                    string nome = ExtrairNomeOriginalEtapa(objAncestral, out campoNome);
                    if (!string.IsNullOrWhiteSpace(nome))
                    {
                        niveisSubidos = subidas + 1;
                        return nome;
                    }
                }

                atual = atual.Parent;
                subidas++;
            }

            return null;
        }

        private static string ResumirIdentificador(string valor, int inicio = 10, int fim = 6)
        {
            if (string.IsNullOrEmpty(valor) || valor.Length <= inicio + fim + 3) return valor;
            return $"{valor.Substring(0, inicio)}...{valor.Substring(valor.Length - fim)}";
        }

        private static (bool Apareceu, string CaminhoPropriedade, int Tamanho, string Formato, JToken JsonInterno, XDocument XmlInterno) BuscarStringContendoStepId(
            JObject definicao, string currentStepId)
        {
            if (definicao == null || string.IsNullOrWhiteSpace(currentStepId))
            {
                return (false, null, 0, null, null, null);
            }

            foreach (JValue valor in definicao.DescendantsAndSelf().OfType<JValue>().Where(v => v.Type == JTokenType.String))
            {
                string texto = valor.Value?.ToString();
                if (string.IsNullOrEmpty(texto) || !texto.Contains(currentStepId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string formato = "TEXTO";
                JToken jsonInterno = null;
                XDocument xmlInterno = null;
                string inicioConteudo = texto.TrimStart();

                if (inicioConteudo.StartsWith("{") || inicioConteudo.StartsWith("["))
                {
                    try { jsonInterno = JToken.Parse(texto); formato = "JSON"; } catch { /* não é JSON válido — permanece TEXTO */ }
                }
                else if (inicioConteudo.StartsWith("<"))
                {
                    try { xmlInterno = XDocument.Parse(texto); formato = "XML"; } catch { /* não é XML válido — permanece TEXTO */ }
                }

                return (true, valor.Path, texto.Length, formato, jsonInterno, xmlInterno);
            }

            return (false, null, 0, null, null, null);
        }

        private static string ResolverNomeDentroDeJsonInterno(JToken jsonInterno, string currentStepId, out string campoNome)
        {
            campoNome = null;
            if (jsonInterno is not JContainer container) return null;

            foreach (JObject objeto in container.DescendantsAndSelf().OfType<JObject>())
            {
                foreach (string campoId in CamposIdEtapa)
                {
                    if (string.Equals(objeto[campoId]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase))
                    {
                        string nome = ExtrairNomeOriginalEtapa(objeto, out campoNome);
                        if (!string.IsNullOrWhiteSpace(nome)) return nome;
                    }
                }
            }

            return null;
        }

        private static string ResolverNomeDentroDeXmlInterno(XDocument xmlInterno, string currentStepId, out string campoNome)
        {
            campoNome = null;
            if (xmlInterno?.Root == null) return null;

            foreach (var elemento in xmlInterno.Descendants())
            {
                bool idBate =
                    string.Equals(elemento.Value?.Trim(), currentStepId, StringComparison.OrdinalIgnoreCase) ||
                    elemento.Attributes().Any(a => string.Equals(a.Value, currentStepId, StringComparison.OrdinalIgnoreCase));

                if (!idBate) continue;

                foreach (var nomeAttr in CamposNomeEtapa)
                {
                    var atributo = elemento.Attribute(nomeAttr) ?? elemento.Parent?.Attribute(nomeAttr);
                    if (atributo != null && !string.IsNullOrWhiteSpace(atributo.Value))
                    {
                        campoNome = nomeAttr;
                        return atributo.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Vincula uma VERSÃO de Review (por ItemUrn e/ou nome) ao(s) arquivo(s) físico(s) da árvore
        /// permitida da pasta de Produção — a origem real vem SEMPRE dessa árvore já crawleada (ver
        /// ColetarArquivosDaProducaoAsync), nunca de um campo "pasta de destino para copiar arquivos" do
        /// Review (ver seção 3 da manutenção técnica: essa é a pasta de destino da CÓPIA pós-processo,
        /// não a origem do documento revisado). <paramref name="versionItemUrn"/> deve vir de
        /// <see cref="ExtrairItemUrnDaVersao"/> (payload real usa "itemUrn", não "itemId").
        ///
        /// CORREÇÃO (regressão WORKFLOW_MAPA_VAZIO_INESPERADO — projeto BML011.013/BML011.022): os três
        /// níveis originais (ItemUrn / nome completo / nome-base) exigem igualdade EXATA após
        /// normalização — nenhum tolera o caso real e frequente em que o nome da versão referenciada pelo
        /// Review e o nome do arquivo atualmente na Produção representam o MESMO documento lógico, mas com
        /// sufixo de revisão diferente (ex.: Review aponta para "...-0055" e a Produção já avançou para
        /// "...-0055-02", ou quando o item foi reenviado como um item novo — ItemUrn muda, mas o documento
        /// é o mesmo). Isso descartava centenas de versões legítimas como "fora da árvore permitida",
        /// mesmo estando dentro dela (o candidato sempre vem de <paramref name="todosOsArquivosDaArvore"/>,
        /// já restrito à árvore real — este nível 4 NUNCA aceita nada fora dela, só relaxa a comparação de
        /// NOME dentro do conjunto já elegível). Nível 4 reaproveita
        /// <see cref="DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens"/> — a MESMA lógica de
        /// fronteira de tokens já testada e usada por LdpUpdateService/DocumentMatchingService para esta
        /// exata classe de variação de nome (nunca um Contains/substring solto) — em AMBAS as direções,
        /// porque tanto o Review quanto a Produção podem ser o lado "mais revisado".
        /// </summary>
        internal static (List<ArquivoProducaoAcc> Arquivos, string Metodo) LocalizarArquivosProducao(
            string versionItemUrn,
            string versionName,
            Dictionary<string, List<ArquivoProducaoAcc>> porItemUrn,
            Dictionary<string, List<ArquivoProducaoAcc>> porNomeCompleto,
            Dictionary<string, List<ArquivoProducaoAcc>> porNomeBase,
            IReadOnlyList<ArquivoProducaoAcc> todosOsArquivosDaArvore)
        {
            if (!string.IsNullOrEmpty(versionItemUrn) && porItemUrn.TryGetValue(versionItemUrn, out var porItem) && porItem.Count > 0)
            {
                return (porItem, "ITEM_URN");
            }

            string nomeCompletoNorm = NormalizarNomeDocumento(versionName);
            if (!string.IsNullOrEmpty(nomeCompletoNorm) && porNomeCompleto.TryGetValue(nomeCompletoNorm, out var porNome) && porNome.Count > 0)
            {
                return (porNome, "NOME_COMPLETO");
            }

            string nomeBaseVersaoSemExtensao = Path.GetFileNameWithoutExtension(versionName ?? "");
            string nomeBaseNorm = NormalizarNomeDocumento(nomeBaseVersaoSemExtensao);
            if (!string.IsNullOrEmpty(nomeBaseNorm) && porNomeBase.TryGetValue(nomeBaseNorm, out var porBase) && porBase.Count > 0)
            {
                return (porBase, "NOME_BASE");
            }

            if (!string.IsNullOrWhiteSpace(nomeBaseVersaoSemExtensao) && todosOsArquivosDaArvore != null)
            {
                var porPrefixo = todosOsArquivosDaArvore
                    .Where(a =>
                        DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(a.FileNameWithoutExtension, nomeBaseVersaoSemExtensao) ||
                        DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(nomeBaseVersaoSemExtensao, a.FileNameWithoutExtension))
                    .ToList();

                if (porPrefixo.Count > 0)
                {
                    return (porPrefixo, "PREFIXO_TOKENS");
                }
            }

            return (new List<ArquivoProducaoAcc>(), "NENHUM");
        }

        /// <summary>
        /// CORREÇÃO DA CAUSA RAIZ REAL do WORKFLOW_MAPA_VAZIO_INESPERADO — checkpoint posterior ao de
        /// PREFIXO_TOKENS, que o run real contra BML011.013 provou insuficiente (PorPrefixoDeTokens=0).
        ///
        /// <see cref="LocalizarArquivosProducao"/> só pode confirmar uma versão contra o snapshot ATUAL de
        /// "10 - PRODUCTION" (a última revisão viva de cada documento — 41 arquivos no caso real). Prova
        /// por git history (<c>git show 4797da5:Services/AccReviewWorkflowService.cs</c>, o último commit
        /// antes da reescrita "Status_In_Flow"): a implementação que funcionava NUNCA teve essa árvore —
        /// usava o nome de CADA versão do Review diretamente, sem nenhum gate físico. A segurança real de
        /// "é o mesmo documento lógico" sempre foi aplicada DEPOIS, em
        /// <c>LdpUpdateService.EncontrarCorrespondenciaControlada</c>, contra o N&amp;A Document Number e o
        /// Client Document Number de uma linha REAL da LDP (com revalidação obrigatória antes de qualquer
        /// escrita). Um "sem correspondência no snapshot atual" NUNCA é prova de exclusão — é esperado e
        /// frequente para versões de Reviews antigos, cujos arquivos originais já foram substituídos.
        ///
        /// Por isso este método NUNCA descarta: quando <see cref="LocalizarArquivosProducao"/> não confirma
        /// nada, cria uma entrada sintética a partir dos próprios dados da versão do Review (nome/ItemUrn/
        /// VersionUrn, sem pasta) e devolve <c>ConfirmadaNaArvoreAtual=false</c> — só para diagnóstico,
        /// nunca para bloquear. A entrada segue para o pipeline normal e só produz uma escrita real se for
        /// reivindicada por um código real da LDP no consumidor.
        /// </summary>
        internal static (List<ArquivoProducaoAcc> Arquivos, string Metodo, bool ConfirmadaNaArvoreAtual) ResolverArquivosDaVersaoComFallback(
            string versionItemUrn,
            string versionName,
            string versionUrnAtual,
            Dictionary<string, List<ArquivoProducaoAcc>> porItemUrn,
            Dictionary<string, List<ArquivoProducaoAcc>> porNomeCompleto,
            Dictionary<string, List<ArquivoProducaoAcc>> porNomeBase,
            IReadOnlyList<ArquivoProducaoAcc> todosOsArquivosDaArvore)
        {
            var (arquivosCorrespondentes, metodoCorrespondencia) = LocalizarArquivosProducao(
                versionItemUrn, versionName, porItemUrn, porNomeCompleto, porNomeBase, todosOsArquivosDaArvore);

            if (arquivosCorrespondentes.Count > 0)
            {
                return (arquivosCorrespondentes, metodoCorrespondencia, true);
            }

            var fallback = new List<ArquivoProducaoAcc>
            {
                new ArquivoProducaoAcc
                {
                    ItemId = versionItemUrn,
                    VersionUrn = versionUrnAtual,
                    FileName = versionName,
                    FileNameWithoutExtension = Path.GetFileNameWithoutExtension(versionName ?? ""),
                    Extension = Path.GetExtension(versionName ?? ""),
                    FileNameNormalizado = NormalizarNomeDocumento(versionName),
                    NomeBaseNormalizado = NormalizarNomeDocumento(Path.GetFileNameWithoutExtension(versionName ?? "")),
                    PastaEncontrada = null
                }
            };

            return (fallback, "SEM_CORRESPONDENCIA_ARVORE_ATUAL", false);
        }

        // =======================================================================================
        // COLETA RECURSIVA DA PASTA DE PRODUÇÃO (DATA MANAGEMENT API) — origem autoritativa real dos
        // documentos (nunca a pasta de destino de cópia do Review — ver seção 3).
        // =======================================================================================

        private async Task<List<ArquivoProducaoAcc>> ColetarArquivosDaProducaoAsync(string hubId, string projectId, string token)
        {
            var resultado = new List<ArquivoProducaoAcc>();

            string urlTop = $"https://developer.api.autodesk.com/project/v1/hubs/{hubId}/projects/{projectId}/topFolders";
            var topFoldersResp = await SendGetAsync(urlTop, token);
            var topFolders = topFoldersResp?["data"] as JArray;

            if (topFolders == null || topFolders.Count == 0)
            {
                _logger?.LogWarning("[WORKFLOW] Nenhuma pasta de topo encontrada no projeto.");
                return resultado;
            }

            PastaCandidata pastaProducao = null;

            foreach (var tf in topFolders)
            {
                string rootFolderId = tf["id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(rootFolderId)) continue;

                string rootContentsUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{rootFolderId}/contents";
                var candidatasNesteNivel = new List<PastaCandidata>();

                while (!string.IsNullOrEmpty(rootContentsUrl))
                {
                    var rootContentsResp = await SendGetAsync(rootContentsUrl, token);
                    var rootContents = rootContentsResp?["data"] as JArray;
                    if (rootContents == null) break;

                    foreach (var child in rootContents)
                    {
                        if ((child["type"]?.ToString() ?? "") != "folders") continue;

                        string childName = child["attributes"]?["displayName"]?.ToString()
                            ?? child["attributes"]?["name"]?.ToString()
                            ?? "";

                        if (EhPastaDeProducao(childName))
                        {
                            candidatasNesteNivel.Add(new PastaCandidata { Id = child["id"]?.ToString() ?? "", Name = childName });
                        }
                    }

                    rootContentsUrl = rootContentsResp?["links"]?["next"]?["href"]?.ToString();
                }

                if (candidatasNesteNivel.Count == 1)
                {
                    if (pastaProducao == null)
                    {
                        pastaProducao = candidatasNesteNivel[0];
                    }
                }
                else if (candidatasNesteNivel.Count > 1)
                {
                    _logger?.LogWarning(
                        $"[WORKFLOW] Resultado: ambíguo | Pastas candidatas de Produção: {string.Join(", ", candidatasNesteNivel.Select(c => c.Name))} | " +
                        "Nenhuma será usada para evitar rastrear a árvore errada.");
                }
            }

            if (pastaProducao == null)
            {
                _logger?.LogWarning(
                    $"[WORKFLOW] Nenhuma pasta de Produção compatível encontrada. Alternativas aceitas: {string.Join(", ", AliasesPastaProducao)} (ou variantes tolerantes de prefixo 10/09).");
                return resultado;
            }

            _logger?.LogInformation($"[WORKFLOW] Pasta de Produção localizada: '{pastaProducao.Name}'.");

            await CrawlAllItemsInFolderAsync(projectId, pastaProducao.Id, token, pastaProducao.Name, resultado);
            return resultado;
        }

        private async Task CrawlAllItemsInFolderAsync(string projectId, string folderId, string token, string caminhoPasta, List<ArquivoProducaoAcc> destino)
        {
            string url = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{folderId}/contents";

            while (!string.IsNullOrEmpty(url))
            {
                var contentsResp = await SendGetAsync(url, token);
                var data = contentsResp?["data"] as JArray;
                if (data == null) break;

                foreach (var item in data)
                {
                    string type = item["type"]?.ToString() ?? "";
                    string id = item["id"]?.ToString() ?? "";
                    string name = item["attributes"]?["displayName"]?.ToString()
                        ?? item["attributes"]?["name"]?.ToString()
                        ?? "";

                    if (type == "folders")
                    {
                        await CrawlAllItemsInFolderAsync(projectId, id, token, $"{caminhoPasta}/{name}", destino);
                    }
                    else if (type == "items")
                    {
                        if (string.IsNullOrEmpty(name) || name.StartsWith("~$")) continue;

                        string versionUrn = item["relationships"]?["tip"]?["data"]?["id"]?.ToString() ?? "";

                        destino.Add(new ArquivoProducaoAcc
                        {
                            ItemId = id,
                            VersionUrn = versionUrn,
                            FileName = name.Trim(),
                            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(name).Trim(),
                            Extension = Path.GetExtension(name),
                            FileNameNormalizado = NormalizarNomeDocumento(name),
                            NomeBaseNormalizado = NormalizarNomeDocumento(Path.GetFileNameWithoutExtension(name)),
                            PastaEncontrada = caminhoPasta
                        });
                    }
                }

                url = contentsResp?["links"]?["next"]?["href"]?.ToString();
            }
        }

        // =======================================================================================
        // UTILITÁRIOS BASE HTTP
        // =======================================================================================

        private string NormalizeDoc(string s) => Regex.Replace((s ?? "").Trim().ToUpper(), @"[^A-Z0-9]", "");

        private async Task<JObject?> SendGetAsync(string url, string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("x-user-id", AdminAutodeskId);

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }

        private async Task<List<JToken>> SendGetAllResultsAsync(string baseUrl, string token, string? userId)
        {
            var resultados = new List<JToken>();
            int offset = 0;

            while (true)
            {
                string separador = baseUrl.Contains("?") ? "&" : "?";
                string url = $"{baseUrl}{separador}limit={PageLimit}&offset={offset}";

                var resp = userId != null
                    ? await SendGetWithUserContextAsync(url, token, userId)
                    : await SendGetAsync(url, token);

                var paginaResultados = resp?["results"] as JArray;
                if (paginaResultados == null || paginaResultados.Count == 0) break;

                resultados.AddRange(paginaResultados);
                offset += paginaResultados.Count;

                int? totalResults = resp?["pagination"]?["totalResults"]?.Value<int>();
                if (totalResults.HasValue)
                {
                    if (offset >= totalResults.Value) break;
                }
                else if (paginaResultados.Count < PageLimit)
                {
                    break;
                }
            }

            return resultados;
        }

        public async Task<List<JToken>> GetProjectReviewsAsync(string projectId, string accessToken, string userId)
        {
            string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            string url = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{cleanId}/reviews";
            return await SendGetAllResultsAsync(url, accessToken, userId);
        }

        public async Task<List<JToken>> GetReviewVersionsAsync(string projectId, string reviewId, string accessToken, string userId)
        {
            string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
            string url = $"https://developer.api.autodesk.com/construction/reviews/v1/projects/{cleanId}/reviews/{reviewId}/versions";
            return await SendGetAllResultsAsync(url, accessToken, userId);
        }

        private async Task<JObject?> SendGetWithUserContextAsync(string url, string token, string userId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Headers.Add("x-user-id", userId);

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }
    }
}
