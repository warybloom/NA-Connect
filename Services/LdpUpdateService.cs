using AccIntegration.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NA_Connect.Models;
using NA_Connect.Services;

namespace APS.WebApp.Services
{
    public class LdpUpdateResult
    {
        public int DocumentosEncontradosNaLdp { get; set; }
        public int DocumentosAtualizados { get; set; }
        public List<string> DocumentosNaoEncontradosNoDb { get; set; } = new List<string>();
        public string NovaVersaoUrn { get; set; } = string.Empty;
        
        public string SummaryMessage { get; set; } = string.Empty;
        public int RegistrosTimesheetAdicionados { get; set; }
    }

    public class FileAttributeUpdate
    {
        public string Urn { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
    }

    public class LdpUpdateService
    {
        private class HorasInfo
        {
            public decimal? HorasRealizadas { get; set; }
        }

        private readonly ApplicationDbContext _context;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LdpUpdateService> _logger;
        private readonly AccMetadataService _metadataService;
        private readonly AccReviewWorkflowService _workflowService;
        private readonly AccStatusResolverService _statusResolver;
        private readonly AccReviewWorkflowService _workflowApi;
        private readonly DatabaseService _dbService;
        private readonly DocumentMatchingService _documentMatching;
        // Mesma "Visão" do Dump: LDP linha 8, Colunas X e Y
        private const int LINHA_FILTRO_LDP = 8;
        private const int COLUNA_X_CR = 24;
        private const int COLUNA_Y_CROS = 25;
        public LdpUpdateService(

        ApplicationDbContext context,
        AutodeskApiService autodeskApi,
        IConfiguration configuration,
        ILogger<LdpUpdateService> logger,
        AccMetadataService metadataService,
        AccReviewWorkflowService workflowApi,
        AccStatusResolverService statusResolver,
        AccReviewWorkflowService workflowService,
        DatabaseService dbService,
        DocumentMatchingService documentMatching)
        {
            _context = context;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
            _workflowService = workflowService;
            _metadataService = metadataService;
            _workflowApi = workflowApi;
        _statusResolver = statusResolver;
        _dbService = dbService;
        _documentMatching = documentMatching;
        }

        // =====================================================================================
        // SINCRONIZAÇÃO DE ATRIBUTOS PERSONALIZADOS DA LDP POR PASTA (ENGENHARIA x PRODUÇÃO)
        // =====================================================================================
        // Nomes dos atributos personalizados exatamente como cadastrados no ACC (grafia fornecida
        // pela operação). Não "corrigidos" para a grafia que pareceria mais correta em português/inglês
        // — ex.: "TRANMISTAL NUMBER" tem propositalmente um único "T" depois de "TRANS".
        private const string ATTR_DOCUMENT_TITLE = "DOCUMENT TITLE";
        private const string ATTR_CLIENT_DOCUMENT_NUMBER = "CLIENT DOCUMENT NUMBER";
        private const string ATTR_NA_DOCUMENT_NUMBER = "N&A DOCUMENT NUMBER";
        private const string ATTR_REV = "REV.";
        private const string ATTR_TRANSMITAL_NUMBER = "TRANMISTAL NUMBER";
        private const string ATTR_EMISSION_DATA = "EMISSION DATA";

        internal const string GRUPO_ENGENHARIA = "Engenharia";
        internal const string GRUPO_PRODUCAO = "Produção";

        /// <summary>
        /// Representa um arquivo do ACC já classificado dentro de uma configuração de pastas (identificada
        /// por ChaveConfiguracao, ex.: ENGENHARIA_04, PRODUCAO_10, PRODUCAO_MODELOS_3D), preservando a pasta
        /// onde ele foi encontrado. Documentos com o mesmo nome-base em pastas diferentes, ou com o mesmo
        /// nome-base e extensões diferentes (ex.: DOC-001.rvt e DOC-001.pdf, que têm VersionUrn distintas),
        /// geram entradas independentes — nunca são agrupados/deduplicados só pelo nome.
        /// </summary>
        private class ArquivoAccClassificado
        {
            public string FileName { get; set; }
            public string FileNameWithoutExtension { get; set; }
            public string Extension { get; set; }
            public string VersionUrn { get; set; }
            public string Grupo { get; set; }
            public string ChaveConfiguracao { get; set; }
            public string PastaEncontrada { get; set; }

            /// <summary>
            /// ID real da pasta do ACC que contém diretamente este arquivo (a pasta de disciplina, ex.:
            /// "10.01 - PROCESS", ou a subpasta recursiva no caso de PRODUCAO_MODELOS_3D) — nunca a pasta
            /// principal nem a raiz. Permite resolver as definições efetivas de atributos da pasta correta
            /// (raiz + pasta específica combinadas — ver ObterDefinicoesEfetivasAsync) sem repetir a busca
            /// de caminho por arquivo.
            /// </summary>
            public string FolderId { get; set; }
        }

        /// <summary>
        /// Configuração de uma "chave" de pastas (ex.: ENGENHARIA_04, PRODUCAO_10, PRODUCAO_MODELOS_3D).
        /// Duas configurações diferentes podem compartilhar o mesmo Grupo (ex.: PRODUCAO_10 e
        /// PRODUCAO_MODELOS_3D são ambas Grupo = "Produção") — por isso a indexação e a deduplicação usam
        /// sempre Chave, nunca Grupo.
        ///
        /// Existem dois modos de configuração, mutuamente exclusivos:
        ///
        /// 1) PastasPrincipaisAlternativas + CaminhosRelativos (usado por ENGENHARIA_04 e PRODUCAO_10):
        ///    a pasta principal é resolvida UMA ÚNICA VEZ dentre as alternativas (nomes reais confirmados
        ///    no ACC, que podem variar por projeto — ex.: "10 -  PRODUCTION" com dois espaços, "10 -
        ///    PRODUÇÃO", ou até "09 - ENGINEERING DOCUMENTS" em outra estrutura de projeto); a primeira
        ///    alternativa válida encontrada é usada e as demais NÃO são tentadas. Cada elemento de
        ///    CaminhosRelativos é relativo à pasta principal já resolvida, e a coleta dentro dele NÃO é
        ///    recursiva (só arquivos diretamente dentro da pasta de disciplina).
        ///
        /// 2) CaminhosRaiz (usado por PRODUCAO_MODELOS_3D): lista de caminhos completos no ACC; a última
        ///    pasta de cada caminho é o ponto onde a recursão livre começa (ver
        ///    ColetarArquivosDaPastaRecursivoAsync) — tudo abaixo dela é coletado, com qualquer
        ///    profundidade e qualquer nome de subpasta.
        ///
        /// IncluirSubpastasRecursivamente é só um marcador declarativo (documenta qual dos dois modos está
        /// em uso); o comportamento real é decidido por qual das duas coleções (PastasPrincipaisAlternativas
        /// ou CaminhosRaiz) está preenchida — ver ClassificarArquivosAccPorPastaAsync.
        /// </summary>
        internal class GrupoPastaConfig
        {
            public string Chave { get; set; }
            public string Grupo { get; set; }
            public List<string> PastasPrincipaisAlternativas { get; set; } = new List<string>();
            public List<string[]> CaminhosRelativos { get; set; } = new List<string[]>();
            public List<string[]> CaminhosRaiz { get; set; } = new List<string[]>();
            public bool IncluirSubpastasRecursivamente { get; set; }
            public List<string> Atributos { get; set; } = new List<string>();
        }

        /// <summary>
        /// Contadores para os logs obrigatórios da sincronização de atributos por pasta.
        /// </summary>
        private class SincronizacaoAtributosStats
        {
            public int RegistrosLidosDaLdp { get; set; }
            public int ArquivosEncontradosEngenharia { get; set; }
            public int ArquivosEncontradosProducao { get; set; }
            public int ArquivosIgnoradosForaDoDicionario { get; set; }
            public int PastasIgnoradasForaDoDicionario { get; set; }
            public int ArquivosAtualizadosEngenharia { get; set; }
            public int ArquivosAtualizadosProducao { get; set; }
            public int ArquivosCorrespondentes { get; set; }
            public int ArquivosDuplicadosIgnoradosMesmaConfiguracao { get; set; }
            public int ArquivosDuplicadosIgnoradosEntreConfiguracoes { get; set; }
            public int ErrosAtualizacao { get; set; }
            public List<string> DocumentosNaoEncontrados { get; set; } = new List<string>();
            public List<string> ErrosPorArquivo { get; set; } = new List<string>();

            // Cache persistente (dbo.ACC_Attribute_Sync_State) — não confundir com
            // ArquivosDuplicadosIgnoradosMesmaConfiguracao/EntreConfiguracoes (que contam duplicidade
            // DENTRO desta mesma execução): estes contadores refletem payloads já sincronizados com
            // sucesso em execuções ANTERIORES.
            public int EstadosCacheCarregados { get; set; }
            public int ArquivosSemAlteracao { get; set; }
            public int AtualizacoesEvitadasPorCache { get; set; }
            public int AtualizacoesNecessarias { get; set; }
            public int EstadosCacheAtualizados { get; set; }
            public int FalhasRegistradasNoCache { get; set; }

            // Observabilidade adicional do cache (seção 8 da manutenção técnica) — só contadores/logs,
            // nunca decisão: a regra de CACHE HIT continua sendo exclusivamente
            // "estadoAnterior != null && UltimaTentativaSucesso && hash igual" (inalterada).
            public int ArquivosSemPayload { get; set; }
            public int CacheMissSemHistorico { get; set; }
            public int CacheMissNovaVersao { get; set; }
            public int CacheMissPayloadAlterado { get; set; }
            public int CacheMissFalhaAnterior { get; set; }
            public int CacheMissChaveConfiguracaoDiferente { get; set; }
            public int CacheMissEstadoIncompleto { get; set; }
            public int PostsComSucesso { get; set; }
            public int PostsComFalha { get; set; }
            public int EstadosGravadosComSucesso { get; set; }
            public int EstadosGravadosComFalha { get; set; }

            // AttributeSync:ForceOverwriteAll (seção de força de reescrita) — CacheHitsNormais é o
            // subconjunto de AtualizacoesEvitadasPorCache que realmente pulou o POST (ForceOverwriteAll
            // desligado); PostsForcados e PostsNormais juntos sempre somam AtualizacoesNecessarias.
            public int CacheHitsNormais { get; set; }
            public int PostsForcados { get; set; }
            public int PostsNormais { get; set; }

            // Paginação (seção 6/7/10/21 da correção do limite de 200) — pastas cuja coleta foi abortada
            // por falha em alguma página (nunca contadas como sucesso parcial) e reconciliação por pasta.
            public List<string> PastasComSnapshotIncompleto { get; set; } = new List<string>();
            public Dictionary<string, ReconciliacaoPasta> ReconciliacaoPorPasta { get; set; } =
                new Dictionary<string, ReconciliacaoPasta>(StringComparer.OrdinalIgnoreCase);

            public ReconciliacaoPasta ObterOuCriarReconciliacao(string chaveConfiguracao, string pasta)
            {
                string chave = $"{chaveConfiguracao}|{pasta}";
                if (!ReconciliacaoPorPasta.TryGetValue(chave, out var reconciliacao))
                {
                    reconciliacao = new ReconciliacaoPasta { Configuracao = chaveConfiguracao, Pasta = pasta };
                    ReconciliacaoPorPasta[chave] = reconciliacao;
                }
                return reconciliacao;
            }
        }

        /// <summary>
        /// Reconciliação de TODOS os arquivos ativos de uma pasta de disciplina (seção 21 da correção do
        /// limite de 200) — garante que nenhum arquivo simplesmente "desapareça": todo arquivo ativo
        /// termina em exatamente um estado final (temporário ignorado, sem correspondência SQL, sem
        /// payload, cache hit, duplicado, sucesso ou falha de POST).
        /// </summary>
        private class ReconciliacaoPasta
        {
            public string Configuracao { get; set; }
            public string Pasta { get; set; }
            public int TotalArquivosAtivos { get; set; }
            public int TemporariosIgnorados { get; set; }
            public int ArquivosComCorrespondenciaSql { get; set; }
            public int ArquivosSemCorrespondenciaSql { get; set; }
            public int ArquivosComPayload { get; set; }
            public int ArquivosSemPayload { get; set; }
            public int CacheHits { get; set; }
            public int PostsNormais { get; set; }
            public int PostsForcados { get; set; }
            public int PostsSucesso { get; set; }
            public int PostsFalha { get; set; }
            public int DuplicadosVersionUrn { get; set; }
        }

        /// <summary>
        /// Dicionário central e extensível de configurações (Chave) x caminhos x atributos. Para adicionar
        /// um novo caminho basta incluir um novo array em "CaminhosRaiz" da configuração correspondente —
        /// a lógica de sincronização (SincronizarAtributosLdpPorPastaAsync / ClassificarArquivosAccPorPastaAsync)
        /// não precisa ser alterada. Múltiplas configurações podem compartilhar o mesmo Grupo (ex.:
        /// PRODUCAO_10, PRODUCAO_DOCUMENTOS_09 e PRODUCAO_MODELOS_3D são todas Grupo = "Produção"); é por
        /// isso que a indexação e a deduplicação usam sempre a Chave da configuração, nunca o Grupo.
        ///
        /// Existem QUATRO configurações independentes, cada uma processada separadamente no mesmo ciclo
        /// (encontrar uma nunca impede processar as outras):
        ///   ENGENHARIA_04            — pasta principal "04 - ..." (Engenharia)
        ///   PRODUCAO_DOCUMENTOS_09   — pasta principal "09 - ..." (Documentos de Engenharia; NUNCA tratada
        ///                              como alternativa de PRODUCAO_10 — tem Chave, cache e log próprios)
        ///   PRODUCAO_10              — pasta principal "10 - ..." (Produção/Produto)
        ///   PRODUCAO_MODELOS_3D      — inalterada, continua recursiva por CaminhosRaiz (07/08)
        ///
        /// As três primeiras repetem as MESMAS 14 disciplinas (01 a 14), mudando somente o prefixo
        /// numérico — geradas por CriarCaminhosDisciplinas a partir de SufixosDisciplinas, para nunca haver
        /// três listas manuais divergentes (foi assim que "09.14 - MANAGEMENT" acabou dentro da
        /// configuração 04, e que Equipment/Electrical ficaram com prefixos trocados numa lista antiga).
        /// O texto de cada disciplina é só o nome CANÔNICO da configuração; variações reais no ACC
        /// (português, "DIGITAL SYSTEMS" em vez de "ENGINEERING SYSTEMS" etc.) continuam sendo aceitas pela
        /// autoridade do prefixo numérico em ResolverPastaDisciplina (prioridade 2) — nunca é necessário
        /// cadastrar uma tradução aqui para que ela funcione no ACC real.
        /// </summary>
        internal const string CHAVE_ENGENHARIA_04 = "ENGENHARIA_04";
        internal const string CHAVE_PRODUCAO_DOCUMENTOS_09 = "PRODUCAO_DOCUMENTOS_09";
        internal const string CHAVE_PRODUCAO_10 = "PRODUCAO_10";
        internal const string CHAVE_PRODUCAO_MODELOS_3D = "PRODUCAO_MODELOS_3D";

        /// <summary>
        /// As 14 disciplinas compartilhadas pelas estruturas 04, 09 e 10 — única fonte usada pelas três
        /// (ver CriarCaminhosDisciplinas). Ordem e numeração fixas (01 a 14); "13" é ENGINEERING SYSTEMS e
        /// "14" é MANAGEMENT em qualquer uma das três estruturas.
        /// </summary>
        internal static readonly string[] SufixosDisciplinas =
        {
            "01 - PROCESS",
            "02 - MECHANICAL",
            "03 - TELECOMUNICATIONS",
            "04 - ARCHITECTURE",
            "05 - INSTRUMENTATION AND AUTOMATION",
            "06 - PIPING",
            "07 - INFRASTRUCTURE",
            "08 - STRUCTURAL ENGINEERING",
            "09 - EQUIPMENT",
            "10 - ELECTRICAL",
            "11 - CIVIL",
            "12 - HVAC",
            "13 - ENGINEERING SYSTEMS",
            "14 - MANAGEMENT"
        };

        /// <summary>
        /// Gera os 14 caminhos de disciplina (CaminhosRelativos) para um prefixo de estrutura ("04", "09"
        /// ou "10") a partir de SufixosDisciplinas. Resultado para "04": "04.01 - PROCESS" ... "04.14 -
        /// MANAGEMENT" (idem para "09"/"10" trocando só o prefixo).
        /// </summary>
        internal static List<string[]> CriarCaminhosDisciplinas(string prefixo)
        {
            return SufixosDisciplinas
                .Select(sufixo => new[] { $"{prefixo}.{sufixo}" })
                .ToList();
        }

        internal static readonly List<GrupoPastaConfig> PastasAtributos = new List<GrupoPastaConfig>
        {
            new GrupoPastaConfig
            {
                Chave = CHAVE_ENGENHARIA_04,
                Grupo = GRUPO_ENGENHARIA,
                PastasPrincipaisAlternativas = new List<string>
                {
                    "04 - ENGINEERING",
                    "04. ENGINEERING",
                    "04 - ENGENHARIA",
                    "04. ENGENHARIA",
                    // CHS001 (correção 16/09/2026): mesma estrutura de disciplinas, prefixo "06" em vez de
                    // "04" — nunca removido o prefixo "04" (compatibilidade com projetos existentes).
                   
                },
                CaminhosRelativos = CriarCaminhosDisciplinas("04"),
                IncluirSubpastasRecursivamente = false,
                Atributos = new List<string>
                {
                    ATTR_DOCUMENT_TITLE,
                    ATTR_CLIENT_DOCUMENT_NUMBER,
                    ATTR_NA_DOCUMENT_NUMBER,
                    ATTR_REV,
                    ATTR_TRANSMITAL_NUMBER,
                    ATTR_EMISSION_DATA
                }
            },
            new GrupoPastaConfig
            {
                Chave = CHAVE_PRODUCAO_DOCUMENTOS_09,
                Grupo = GRUPO_PRODUCAO,
                // Configuração PRÓPRIA (nunca reaproveita CHAVE_PRODUCAO_10) — classificação, cache, logs,
                // estatísticas, diagnóstico e deduplicação são todos por Chave, então "09 - ..." nunca é
                // tratada como sinônimo de "10 - ...".
                PastasPrincipaisAlternativas = new List<string>
                {
                    "09 - ENGINEERING DOCUMENTS",
                    "09 - ENGINEERING DOCUMENTATION",
                    "09 - DOCUMENTOS DE ENGENHARIA",
                    "09 - DOCUMENTAÇÃO DE ENGENHARIA",
                    "09 - DOCUMENTACAO DE ENGENHARIA"
                },
                CaminhosRelativos = CriarCaminhosDisciplinas("09"),
                IncluirSubpastasRecursivamente = false,
                Atributos = new List<string>
                {
                    ATTR_DOCUMENT_TITLE,
                    ATTR_CLIENT_DOCUMENT_NUMBER,
                    ATTR_NA_DOCUMENT_NUMBER,
                    ATTR_REV
                }
            },
            new GrupoPastaConfig
            {
                Chave = CHAVE_PRODUCAO_10,
                Grupo = GRUPO_PRODUCAO,
                // Nomes reais confirmados diretamente no ACC — variam por projeto. A normalização
                // (NormalizarNomePasta) já torna espaços duplicados/pontuação/maiúsculas equivalentes, então
                // "10 -  PRODUCTION" (dois espaços) e "10 - PRODUCTION" resolvem para o mesmo alias; e
                // "10 - PRODUÇÃO"/"10 - PRODUCAO" (com/sem acento) também resolvem para o mesmo alias. A
                // ordem importa: a primeira alternativa encontrada é usada e as demais NÃO são tentadas.
                // "09 - ENGINEERING DOCUMENTS" NÃO é mais alternativa desta configuração — tem a própria
                // (PRODUCAO_DOCUMENTOS_09, acima).
                PastasPrincipaisAlternativas = new List<string>
                {
                    "10 -  PRODUCTION",
                    "10 - PRODUCTION",
                    "10 - PRODUCT",
                    "10 - PRODUÇÃO",
                    "10 - PRODUCAO",
                    "10 - PRODUTO",
                    // CHS001 (correção 16/09/2026): mesma pasta de Produto/Produção, prefixo "12" em vez de
                    // "10" — nunca removido o prefixo "10" (compatibilidade com projetos existentes).
                    "12 - PRODUCT",
                    "12 - PRODUCTION",
                    "12 - PRODUÇÃO",
                    "12 - PRODUCAO",
                    "12 - PRODUTO"
                },
                CaminhosRelativos = CriarCaminhosDisciplinas("10"),
                IncluirSubpastasRecursivamente = false,
                Atributos = new List<string>
                {
                    ATTR_DOCUMENT_TITLE,
                    ATTR_CLIENT_DOCUMENT_NUMBER,
                    ATTR_NA_DOCUMENT_NUMBER,
                    ATTR_REV
                }
            },
            new GrupoPastaConfig
            {
                Chave = CHAVE_PRODUCAO_MODELOS_3D,
                Grupo = GRUPO_PRODUCAO,
                CaminhosRaiz = new List<string[]>
                {
                    new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.01 - AUTODESK REVIT" },
                    new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.02 - AUTODESK CIVIL 3D" },
                    new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.05 - AUTODESK PLANT 3D" },

                    // Correção 16/09/2026: raiz "08 - FEDERATED MODEL" inteira (um dos 4 roots exigidos —
                    // 06/08/09/12), substituindo as 3 subpastas de software específicas que existiam antes
                    // (removidas por ficarem estritamente redundantes com este caminho e causarem dupla
                    // contagem de reconciliação sem agregar cobertura nova — a árvore inteira já inclui
                    // "08.01 - 3D AUTHORING SOFTWARE" e tudo abaixo dela).
                    new[] { "08 - FEDERATED MODEL" },
                },
                IncluirSubpastasRecursivamente = true,
                Atributos = new List<string>
                {
                    ATTR_DOCUMENT_TITLE,
                    ATTR_CLIENT_DOCUMENT_NUMBER,
                    ATTR_NA_DOCUMENT_NUMBER,
                    ATTR_REV
                }
            }
        };

        /// <summary>
        /// Correção 17/09/2026 (Fase B — decisão do usuário, nunca uma limitação técnica): a Etapa 07
        /// (escrita de atributos ACC) NÃO PODE MAIS escrever atributos em arquivos dentro da árvore
        /// "09 - ENGINEERING DOCUMENTS" (raiz + qualquer subpasta, qualquer profundidade) — mas a pasta 09
        /// continua existindo normalmente para qualquer OUTRA etapa (GRDs, ACC Reviews, etc.) que precise
        /// localizar/ler/usar arquivos dela; só a escrita de atributos pela Etapa 07 está bloqueada. Nunca
        /// remove CHAVE_PRODUCAO_DOCUMENTOS_09 de PastasAtributos (preserva a config para quem mais a lê,
        /// ex.: Services/OperationalAudit/**) — só impede que a Etapa 07 a use para descoberta/escrita.
        /// </summary>
        internal static readonly HashSet<string> ChavesBloqueadasParaAtualizacaoDeAtributos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CHAVE_PRODUCAO_DOCUMENTOS_09 };

        /// <summary>
        /// Nomes normalizados (NormalizarNomePasta — mesma tolerância a espaço/pontuação/acentuação usada
        /// em toda a Etapa 07) de TODAS as alternativas conhecidas da(s) pasta(s) principal(is)
        /// bloqueada(s) — única fonte de verdade (reaproveita PastasPrincipaisAlternativas de
        /// PastasAtributos, nunca uma lista duplicada). Usado pela proteção DEFENSIVA final (seção "MUITO
        /// IMPORTANTE" do pedido): mesmo que um arquivo da árvore "09 - ENGINEERING DOCUMENTS" seja
        /// descoberto no futuro por qualquer OUTRA configuração (alias, sobreposição de root, caminho
        /// alternativo), a Etapa 07 nunca envia POST de atributos para ele.
        /// </summary>
        private static readonly HashSet<string> RaizesBloqueadasNormalizadas = new HashSet<string>(
            PastasAtributos
                .Where(g => ChavesBloqueadasParaAtualizacaoDeAtributos.Contains(g.Chave))
                .SelectMany(g => g.PastasPrincipaisAlternativas)
                .Select(NormalizarNomePasta),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True quando o PRIMEIRO segmento do caminho (a pasta-raiz onde o arquivo foi encontrado, ex.:
        /// "09 - ENGINEERING DOCUMENTS" em "09 - ENGINEERING DOCUMENTS/09.06 - PIPING/Related Files/x.pdf")
        /// pertence, após normalização tolerante, a uma das raízes bloqueadas para atualização de
        /// atributos. Nunca decide nada além de "esta pasta pode receber POST de atributos da Etapa 07?" —
        /// não impede leitura/uso por outras etapas.
        /// </summary>
        internal static bool PastaPertenceARaizBloqueadaParaAtributos(string pastaEncontrada)
        {
            if (string.IsNullOrWhiteSpace(pastaEncontrada)) return false;
            string primeiroSegmento = pastaEncontrada.Split('/')[0];
            return RaizesBloqueadasNormalizadas.Contains(NormalizarNomePasta(primeiroSegmento));
        }

        /// <summary>
        /// Lê o valor de uma célula com segurança, verificando merges e fazendo Trim.
        /// </summary>
        private string GetSafeCellValue(ExcelWorksheet sheet, int row, int col)
        {
            var cell = sheet.Cells[row, col];
            string value = cell.Text?.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
            if (cell.Merge)
            {
                var mergeAddress = sheet.MergedCells[row, col];
                if (mergeAddress != null)
                {
                    var firstCell = new ExcelAddress(mergeAddress).Start;
                    return sheet.Cells[firstCell.Row, firstCell.Column].Text?.Trim();
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Verifica se o valor da célula corresponde a uma das opções (PT/EN), ignorando maiúsculas/minúsculas.
        /// </summary>
        private bool ColumnMatch(string value, params string[] options)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return options.Any(opt => value.Contains(opt, StringComparison.OrdinalIgnoreCase));
        }
public async Task<LdpUpdateResult> UpdateLdpFromDatabaseAsync(string hubId, string projectId, string accessToken, string ldpFileId)
{
    _logger.LogWarning($"--- [ETAPA 1/7] REVERSE SYNC (BANCO HH + REV. COM TRAVA ACC) ---");

    var result = new LdpUpdateResult();
    string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_STEP1_{Guid.NewGuid()}.xlsx");
    
    try
    {
        // -----------------------------------------------------------------------------------------
        // 1. PREPARAÇÃO: OBTER DETALHES DO ITEM (Versão e Pasta Pai)
        // -----------------------------------------------------------------------------------------
        // Isso resolve o erro "The provided urn is invalid" (no download) 
        // e o erro "Value for id not found" (no upload)
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);

        if (itemDetails == null) 
            throw new Exception($"Não foi possível recuperar detalhes do item {ldpFileId}. Verifique se o método GetItemDetailsAsync foi implementado no AutodeskApiService.");

        string versionUrn = itemDetails.TipVersionUrn;      // URN da última versão (para download)
        string parentFolderId = itemDetails.ParentFolderId; // ID da pasta pai (para upload)
        string realFileName = itemDetails.DisplayName;      // Nome original do arquivo

        _logger.LogInformation($"📄 Arquivo: {realFileName} | Versão: {versionUrn} | Pasta Pai: {parentFolderId}");

        // -----------------------------------------------------------------------------------------
        // 2. DOWNLOAD DIRETO (Usando a URN da Versão)
        // -----------------------------------------------------------------------------------------
        using (var downloadStream = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write)) {
            await downloadStream.CopyToAsync(fileStream);
        }

        // -----------------------------------------------------------------------------------------
        // 3. PROCESSAMENTO (Lógica de Negócio Mantida)
        // -----------------------------------------------------------------------------------------
        
        // A. Ler Filtros (X8 e Y8)
        string valCR = "";
        string valCROS = "";
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            valCR = ws.Cells[8, 24].Text?.Trim();
            valCROS = ws.Cells[8, 25].Text?.Trim();
        }

        _logger.LogInformation($"🔍 Visão extraída: Projeto='{valCR}' AND Doc1='{valCROS}'");

        // B. Indexar Arquivos (Para validar existência e revisão)
        var arquivosAtivosNoAcc = await _autodeskApi.IndexarArquivosProjetoAsync(hubId, projectId, accessToken);
        var mapaArquivosVivos = arquivosAtivosNoAcc
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().VersionUrn, StringComparer.OrdinalIgnoreCase);

        // C. Buscar Dados do Banco (Horas)
        var sql = "SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}";
        var registrosBanco = await _context.TbTimeSheetRealizadoDocumentos
            .FromSqlRaw(sql, valCR, valCROS).AsNoTracking().ToListAsync();

        var dadosHorasBanco = registrosBanco.GroupBy(t => t.DocumentoCodigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Trabalhadas.GetValueOrDefault()), StringComparer.OrdinalIgnoreCase);

        // D. Buscar Atributos de Revisão
        var allUrnsAtivas = arquivosAtivosNoAcc.Select(f => f.VersionUrn).Distinct().ToList();
        var accAttributesMap = await _autodeskApi.GetBatchAttributesAsync(projectId, allUrnsAtivas, accessToken);

        // E. Atualizar o Excel
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            int colRev = 14; 
            int colNA_Number = 4;
            int colRealized = -1;

            // Localiza coluna de Horas
            for (int c = 1; c <= ws.Dimension.End.Column; c++) {
                string h = ws.Cells[7, c].Text?.Trim().ToUpper();
                if (h.Contains("HH REALIZADO") || h.Contains("ACTUAL HH")) { colRealized = c; break; }
            }

            // Loop nas linhas
            for (int row = 8; row <= ws.Dimension.End.Row; row++)
            {
                string docCode = ws.Cells[row, colNA_Number].Text?.Trim();
                if (string.IsNullOrWhiteSpace(docCode)) continue;
                bool changed = false;

                // Atualiza Revisão (se arquivo existir no ACC)
                if (mapaArquivosVivos.TryGetValue(docCode, out string urn)) {
                    if (accAttributesMap.TryGetValue(urn, out var attrs)) {
                        var revAttr = attrs.FirstOrDefault(a => a.Key.Equals("REV", StringComparison.OrdinalIgnoreCase) || a.Key.Equals("REV.", StringComparison.OrdinalIgnoreCase));
                        string valRev = revAttr.Value;
                        // Filtra datas que as vezes vem no campo de revisão por engano
                        if (!string.IsNullOrEmpty(valRev) && !(valRev.Contains(":") && valRev.Length > 10)) {
                            ws.Cells[row, colRev].Value = valRev;
                            changed = true;
                        }
                    }
                } else {
                    // Limpa revisão se não existir arquivo físico
                    if (ws.Cells[row, colRev].Value != null) {
                        ws.Cells[row, colRev].Value = null;
                        changed = true;
                    }
                }

                // Atualiza Horas Realizadas
                if (colRealized > 0 && dadosHorasBanco.TryGetValue(docCode, out var hrs)) {
                    // Verifica se mudou para não marcar 'changed' a toa
                    var valorAtual = ws.Cells[row, colRealized].Text;
                    if (valorAtual != hrs.ToString()) {
                        ws.Cells[row, colRealized].Value = hrs;
                        changed = true;
                    }
                }
                
                if (changed) result.DocumentosAtualizados++;
            }
            package.Save();
        }

        // -----------------------------------------------------------------------------------------
        // 4. UPLOAD CORRIGIDO (Passando ldpFileId + ParentFolderId)
        // -----------------------------------------------------------------------------------------
        if (result.DocumentosAtualizados > 0)
        {
            using (var ul = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read)) {
                // Aqui usamos o ParentFolderId descoberto no passo 1
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, realFileName, ul, accessToken);
            }
            result.SummaryMessage = $"Sucesso! {result.DocumentosAtualizados} itens atualizados e nova versão gerada.";
        }
        else
        {
            result.SummaryMessage = "Processamento concluído. Nenhuma alteração necessária na LDP.";
        }

        return result;
    }
    catch (Exception ex) 
    { 
        _logger.LogError(ex, "❌ Erro Fatal na Etapa 1 (Reverse Sync)");
        return new LdpUpdateResult { SummaryMessage = $"Erro: {ex.Message}" }; 
    }
    finally 
    { 
        if (File.Exists(tempFilePath)) File.Delete(tempFilePath); 
    }
}


       // Módulo: Services/LdpUpdateService.cs

// =====================================================================================
// CORRESPONDÊNCIA SEGURA ACC REVIEW -> LINHA DA LDP (usada exclusivamente por
// UpdateWorkflowStatusAsync). Prioridade fixa e definitiva para este fluxo, independente do
// NamingStandard do projeto (que continua controlando outras funcionalidades, ex.:
// SyncAccAttributesFromSqlAsync): sempre número N&A (coluna 4) primeiro, número do Cliente
// (coluna 5) depois como fallback, e o código composto antigo (colunas Y/Z/AA/AB/AC) apenas como
// último recurso de compatibilidade para projetos sem os números completos preenchidos.
//
// Correspondência por PREFIXO DE TOKENS (nunca por Contains/substring, nunca dependente da coluna
// de revisão): o código da LDP e o nome do arquivo são tokenizados (separando por espaço, ponto,
// hífen, underline); o arquivo só corresponde ao código quando os tokens do código forem
// EXATAMENTE um prefixo dos tokens do arquivo. Isso torna qualquer sufixo final do arquivo
// (revisão, "-01", "-REV02", "-A" etc.) indiferente, e ao mesmo tempo rejeita "DOC-001" contra
// "DOC-0010" (2º token não bate) e "DOC-001" contra "X-DOC-001-05" (1º token não bate).
// =====================================================================================

/// <summary>
/// Um nome de arquivo "corresponde" a um código de documento quando os tokens do código são
/// EXATAMENTE um prefixo dos tokens do arquivo, token a token — nunca por substring/Contains. O
/// sufixo final do arquivo (revisão "-01", "-REV02", "-A" etc.) fica indiferente automaticamente,
/// sem depender da coluna de revisão da LDP: "DOC-001" corresponde a "DOC-001-05" mas NÃO
/// corresponde a "DOC-0010" (2º token "001" != "0010") nem a "X-DOC-001-05" (1º token "X" != "DOC").
///
/// Delega para DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens — mesma tokenização
/// (remoção de extensão conhecida + acentos + separação por espaço/ponto/hífen/underline) usada pela
/// Etapa 7 (SincronizarAtributosLdpPorPastaAsync), agora com uma única implementação para as duas
/// correspondências por prefixo de tokens do projeto. O comportamento do Workflow não muda — é
/// literalmente o mesmo algoritmo que já existia aqui.
/// </summary>
internal static bool NomeArquivoCorrespondeAoDocumento(string nomeArquivo, string codigoDocumento)
{
    return NA_Connect.Services.DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(nomeArquivo, codigoDocumento);
}

/// <summary>
/// Busca PRIMEIRO o NOME do arquivo real que corresponde ao código informado (por prefixo de
/// tokens) — nunca devolve uma entrada arbitrária. Quando mais de um arquivo do mapa de workflow
/// corresponder ao mesmo código (ex.: duas extensões do mesmo documento, ou o mesmo documento em
/// Reviews históricos diferentes), escolhe o de Review mais recente. Só devolve o NOME; a entrada
/// completa só é recuperada por esse nome exato depois, num passo separado (ver
/// UpdateWorkflowStatusAsync) — nunca no mesmo passo em que o nome é descoberto.
/// </summary>
/// <summary>
/// Resultado de <see cref="EncontrarCorrespondenciaControlada"/> — seção 9 da correção Status_In_Flow,
/// revisado para separar nível FÍSICO (ItemUrn/VersionUrn/extensão/pasta — um por arquivo) de nível
/// LÓGICO (Status_In_Flow — um por documento da LDP, mesmo quando o documento tem várias extensões
/// companheiras como .dwg+.pdf). <see cref="Ambiguo"/> só é true para divergência REAL de decisão, nunca
/// só porque dois arquivos físicos têm ItemUrn diferente.
/// </summary>
internal readonly struct CandidatoCorrespondenciaControlada
{
    public string Arquivo { get; init; }
    public bool Ambiguo { get; init; }
    public List<string> Candidatos { get; init; }
    public bool CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes { get; init; }
    public List<string> ArquivosFisicos { get; init; }
    public string CodigoResultado { get; init; }
}

/// <summary>
/// Compara se um conjunto de WorkflowStatusEntry (normalmente as extensões companheiras de um mesmo
/// documento dentro do MESMO Review) representam a MESMA decisão lógica de Status_In_Flow. ItemUrn
/// diferente nunca entra nesta comparação — ele identifica o arquivo físico, não a decisão.
/// </summary>
private static bool RepresentamAMesmaDecisaoLogica(List<AccReviewWorkflowService.WorkflowStatusEntry> entradas)
{
    if (entradas.Count <= 1) return true;
    var primeira = entradas[0];
    return entradas.All(e =>
        string.Equals(e.StatusCalculado ?? "", primeira.StatusCalculado ?? "", StringComparison.OrdinalIgnoreCase) &&
        e.OrigemStatus == primeira.OrigemStatus &&
        e.PreservarCelula == primeira.PreservarCelula &&
        string.Equals(e.CodigoResolucao ?? "", primeira.CodigoResolucao ?? "", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(e.CurrentStepId ?? "", primeira.CurrentStepId ?? "", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Escolhe um representante físico só para fins de log/lookup posterior — nunca influencia o
/// StatusCalculado (que já foi confirmado idêntico entre todas as extensões do grupo vencedor).
/// Prioridade: identificadores completos (ItemUrn+VersionUrn) -> .dwg -> .docx -> .pdf -> ordem estável
/// por nome.
/// </summary>
private static string EscolherRepresentanteFisico(List<AccReviewWorkflowService.WorkflowStatusEntry> entradas)
{
    var comIdentificadoresCompletos = entradas.Where(e => !string.IsNullOrEmpty(e.ItemId) && !string.IsNullOrEmpty(e.VersionUrn)).ToList();
    var pool = comIdentificadoresCompletos.Count > 0 ? comIdentificadoresCompletos : entradas;

    return pool.FirstOrDefault(e => string.Equals(e.Extension, ".dwg", StringComparison.OrdinalIgnoreCase))?.FileName
        ?? pool.FirstOrDefault(e => string.Equals(e.Extension, ".docx", StringComparison.OrdinalIgnoreCase))?.FileName
        ?? pool.FirstOrDefault(e => string.Equals(e.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))?.FileName
        ?? pool.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).First().FileName;
}

private static List<string> DescreverArquivosFisicos(List<AccReviewWorkflowService.WorkflowStatusEntry> entradas)
{
    return entradas
        .GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .Select(e => $"{e.FileName}|ItemUrn={e.ItemId}|VersionUrn={e.VersionUrn}")
        .ToList();
}

/// <summary>
/// Busca o NOME do arquivo real (representante) que corresponde ao código informado (por prefixo de
/// tokens — nunca Contains/substring), em DOIS NÍVEIS (seção 3/4/5 da correção Status_In_Flow —
/// "extensões companheiras não são ambiguidade"):
///
///   1) Nível lógico — agrupa os candidatos por ReviewId e seleciona o grupo do Review MAIS RECENTE
///      (Reviews antigos nunca disputam ambiguidade contra o mais recente: são descartados antes de
///      qualquer comparação).
///   2) Dentro do Review vencedor, todas as extensões físicas (.dwg, .pdf, .docx, ItemUrns diferentes)
///      precisam representar a MESMA decisão (RepresentamAMesmaDecisaoLogica) para a correspondência ser
///      considerada CORRESPONDENCIA_LOGICA_CONFIRMADA — mesmo tendo ItemUrn/VersionUrn diferentes.
///
/// Só é reportado <see cref="CandidatoCorrespondenciaControlada.Ambiguo"/>=true quando há divergência
/// REAL: (a) dois ReviewIds diferentes empatados na mesma data mais recente E com decisões diferentes
/// (WORKFLOW_CORRESPONDENCIA_AMBIGUA_REAL), ou (b) extensões do MESMO Review vencedor com decisões
/// diferentes, ex. uma Aprovado e outra Reprovado (WORKFLOW_STATUS_CONFLITANTE_ENTRE_EXTENSOES). Nesses
/// casos a célula deve ser preservada e o upload da execução bloqueado — nunca escolhido o "primeiro"
/// arbitrariamente.
/// </summary>
internal static CandidatoCorrespondenciaControlada EncontrarCorrespondenciaControlada(string codigo, List<AccReviewWorkflowService.WorkflowStatusEntry> workflowEntries)
{
    var vazio = new List<string>();

    if (string.IsNullOrWhiteSpace(codigo))
        return new CandidatoCorrespondenciaControlada { Arquivo = null, Ambiguo = false, Candidatos = vazio, ArquivosFisicos = vazio, CodigoResultado = "CODIGO_VAZIO" };

    var correspondentes = workflowEntries
        .Where(e => NomeArquivoCorrespondeAoDocumento(e.FileNameWithoutExtension, codigo))
        .ToList();

    var nomesCandidatos = correspondentes.Select(e => e.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    if (correspondentes.Count == 0)
        return new CandidatoCorrespondenciaControlada { Arquivo = null, Ambiguo = false, Candidatos = nomesCandidatos, ArquivosFisicos = vazio, CodigoResultado = "SEM_CORRESPONDENCIA" };

    // Nível lógico: agrupa por ReviewId (mesmo Review = mesma decisão de workflow pretendida, ainda que
    // existam vários arquivos físicos/extensões dentro dele) e seleciona o(s) grupo(s) na data mais
    // recente.
    var gruposPorReview = correspondentes
        .GroupBy(e => e.ReviewId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .Select(g => new
        {
            ReviewId = g.Key,
            Data = g.Max(e => e.DataAtualizacaoReview ?? DateTime.MinValue),
            Entradas = g.ToList()
        })
        .ToList();

    DateTime dataMaisRecente = gruposPorReview.Max(g => g.Data);
    var gruposNaDataMaisRecente = gruposPorReview.Where(g => g.Data == dataMaisRecente).ToList();

    List<AccReviewWorkflowService.WorkflowStatusEntry> grupoVencedor;

    if (gruposNaDataMaisRecente.Count > 1)
    {
        // Mais de um ReviewId empatado na data mais recente — Reviews antigos já foram descartados pelo
        // filtro de data; entre os concorrentes remanescentes, só é ambiguidade REAL se as decisões
        // divergirem entre eles.
        var entradasDosConcorrentes = gruposNaDataMaisRecente.SelectMany(g => g.Entradas).ToList();
        if (!RepresentamAMesmaDecisaoLogica(entradasDosConcorrentes))
        {
            return new CandidatoCorrespondenciaControlada
            {
                Arquivo = null,
                Ambiguo = true,
                Candidatos = nomesCandidatos,
                ArquivosFisicos = DescreverArquivosFisicos(correspondentes),
                CodigoResultado = "WORKFLOW_CORRESPONDENCIA_AMBIGUA_REAL"
            };
        }
        grupoVencedor = entradasDosConcorrentes;
    }
    else
    {
        grupoVencedor = gruposNaDataMaisRecente[0].Entradas;
    }

    // Dentro do Review vencedor: todas as extensões físicas precisam concordar na mesma decisão. ItemUrn
    // diferente (.dwg x .pdf) NUNCA entra nesta comparação — só StatusCalculado/OrigemStatus/
    // PreservarCelula/CodigoResolucao/CurrentStepId.
    if (!RepresentamAMesmaDecisaoLogica(grupoVencedor))
    {
        return new CandidatoCorrespondenciaControlada
        {
            Arquivo = null,
            Ambiguo = true,
            Candidatos = nomesCandidatos,
            ArquivosFisicos = DescreverArquivosFisicos(correspondentes),
            CodigoResultado = "WORKFLOW_STATUS_CONFLITANTE_ENTRE_EXTENSOES"
        };
    }

    string representante = EscolherRepresentanteFisico(grupoVencedor);
    bool multiplasExtensoes = grupoVencedor.Select(e => e.ItemId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    return new CandidatoCorrespondenciaControlada
    {
        Arquivo = representante,
        Ambiguo = false,
        Candidatos = nomesCandidatos,
        CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes = multiplasExtensoes,
        ArquivosFisicos = DescreverArquivosFisicos(grupoVencedor),
        CodigoResultado = "CORRESPONDENCIA_LOGICA_CONFIRMADA"
    };
}

public async Task<string> UpdateWorkflowStatusAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId)
{
    // Sem WorkflowBuildResult pré-construído: busca aqui (mantém compatibilidade com qualquer chamador
    // que não tenha acesso a uma coleta já feita neste ciclo). Ver overload abaixo, usado pelo Worker
    // para reaproveitar a MESMA coleta também em AccReviewStageSnapshotService (seção 12 — nunca duas
    // varreduras completas dos Reviews no mesmo ciclo).
    var workflowResult = await _workflowService.GetProjectWorkflowResultAsync(hubId, projectId, accessToken);
    return await UpdateWorkflowStatusAsync(hubId, projectId, accessToken, userId, ldpFileId, workflowResult);
}

/// <summary>
/// Overload que recebe um <see cref="AccReviewWorkflowService.WorkflowBuildResult"/> JÁ CONSTRUÍDO —
/// usado pelo Worker para reaproveitar a mesma coleta de Reviews também em AccReviewStageSnapshotService,
/// sem repetir o GET da lista/detalhe/versões dos Reviews (seção 12 da funcionalidade "ACC_REVIEW_STAGE").
/// Corpo idêntico ao overload de compatibilidade acima, exceto que não busca o workflowResult sozinho.
/// </summary>
public async Task<string> UpdateWorkflowStatusAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId, AccReviewWorkflowService.WorkflowBuildResult workflowResult)
{
    _logger.LogInformation("🔄 [ETAPA 2/7] STATUS DE WORKFLOW...");
    string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_WF_{Guid.NewGuid()}.xlsx");

    try
    {
        // -----------------------------------------------------------------------
        // 1. RECUPERA DETALHES (Versão para Download e PASTA para Upload)
        // -----------------------------------------------------------------------
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
        if (itemDetails == null) throw new Exception("Detalhes do arquivo não encontrados (Item/Folder).");

        string versionAnterior = itemDetails.TipVersionUrn;
        string parentFolderId = itemDetails.ParentFolderId; // 🎯 ESSENCIAL PARA O UPLOAD
        string fileName = itemDetails.DisplayName;

        // 2. DADOS DE SUPORTE
        var rawAccMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);
        var filesInAccNorm = rawAccMap.Keys.Select(n => NormalizeKeyOnly(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Metadados completos (nome original, extensão, ItemUrn, VersionUrn, ReviewId, currentStepId,
        // workflowId, data do Review, pasta) de cada arquivo x Review ao qual pertence — construídos por
        // AccReviewWorkflowService de forma ORIENTADA A REVIEW (nunca por busca global de documento), o
        // que impede que o status de um documento "vaze" para outro. A prioridade N&A -> Cliente ->
        // composto é decidida abaixo, por linha da LDP; este serviço não decide prioridade nenhuma.
        //
        // Usa o resultado ESTRUTURADO (WorkflowBuildResult), não o atalho GetProjectWorkflowEntriesAsync,
        // porque só ele carrega Sucesso. Antes desta correção, uma falha técnica dentro de
        // BuildWorkflowResultAsync (ex.: approveStatus:null lançando exceção em cada versão) era engolida
        // e devolvia uma lista vazia indistinguível de "nenhum documento em revisão" — a Etapa 5 seguia em
        // frente, preservava todas as células (porque não achava correspondência nenhuma) e a LDP nunca
        // era corrigida, silenciosamente. Falha global agora aborta ANTES de baixar/alterar/publicar a LDP.
        // (workflowResult chega como parâmetro — ver os dois overloads acima — nunca buscado de novo aqui.)
        if (workflowResult == null || !workflowResult.Sucesso)
        {
            string codigoResultado = workflowResult?.CodigoResultado ?? "WORKFLOW_RESULT_NULO";
            string mensagemResultado = workflowResult?.Mensagem ?? "WorkflowBuildResult não foi fornecido (nulo).";

            _logger.LogError(
                "[STATUS_IN_FLOW][ESCRITA_PROPOSTA] Etapa 5 ABORTADA antes de tocar na LDP — CodigoResultado={CodigoResultado} | Mensagem={Mensagem} | ErroGlobal={ErroGlobal}",
                codigoResultado, mensagemResultado, workflowResult?.ErroGlobal ?? "(n/d)");
            throw new InvalidOperationException(
                $"Falha técnica ao construir o mapa de workflow (CodigoResultado={codigoResultado}): {mensagemResultado} " +
                "A LDP não foi baixada, alterada nem publicada nesta execução.");
        }

        var workflowEntries = workflowResult.Entries;

        // 3. DOWNLOAD (Usando a versão correta)
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionAnterior, accessToken))
        using (var fs = new FileStream(tempFilePath, FileMode.Create)) await dl.CopyToAsync(fs);

        int count = 0;
        int linhasAnalisadas = 0;
        int linhasComNa = 0;
        int linhasComCliente = 0;
        int encontradosNa = 0;
        int encontradosCliente = 0;
        int encontradosCompostos = 0;
        int bloqueiosValidacao = 0;
        int documentosEncontradosNoMapa = 0;
        int documentosSemReview = 0;
        int statusJaIguais = 0;
        int statusAlterados = 0;
        int etapasAtuaisAplicadas = 0;
        int vereditosFinaisAplicados = 0;
        int totalAprovados = 0;
        int preservadosPorHistoricoRecente = 0;
        int conflitosNaCliente = 0;
        int correspondenciasAmbiguas = 0;
        int gruposLogicosConfirmadosPorMultiplasExtensoes = 0;
        bool bloqueiaUploadPorConflito = false;
        const int colNaNumber = 4;
        const int colClienteNumber = 5;
        const int colStatus = 17; // Q
        const int LIMITE_CODIGOS_POR_ENTRADA = 3;

        // Seção 14 (proteção contra reutilização): rastreia, por (FileName + ReviewId), o conjunto de
        // códigos da LDP que foram associados a essa MESMA entrada nesta execução. Se um único arquivo
        // terminar associado a muitos códigos diferentes, é sinal de contaminação (ex.: código composto
        // degenerado batendo por acaso em várias linhas) — o upload é cancelado antes de publicar.
        var associacoesPorEntrada = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];

            // Correção 16/09/2026 (Etapa 05 única, sem variante por Tipo_LDP): a REGRA de Status_In_Flow
            // é a mesma acima e abaixo desta linha, para qualquer Tipo_LDP — só o ACESSO FÍSICO à coluna
            // varia, porque a LDP Tipo2 tem colunas extras (blocos de gate 30/60/90) que deslocam
            // "STATUS IN THE FLOW" para uma posição diferente da coluna 17 fixa do layout Tipo1 original.
            // Localiza pelo cabeçalho (já sabemos que o header "STATUS IN THE FLOW" existe no LDP Tipo2 —
            // ver LdpTipo2CabecalhosComuns.StatusInFlow) e só cai no valor fixo colStatus (17) quando o
            // cabeçalho não é encontrado (ou aparece mais de uma vez) — nunca falha, nunca bloqueia.
            int colStatusEfetivo = LocalizarColunaStatusInFlow(ws, linhaCabecalho: 7, colunaPadrao: colStatus);

            for (int r = 8; r <= ws.Dimension.End.Row; r++)
            {
                string codigoNa = GetSafeCellValue(ws, r, colNaNumber);
                string codigoCliente = GetSafeCellValue(ws, r, colClienteNumber);

                // Só ignora a linha quando os DOIS códigos (N&A e Cliente) estão vazios.
                if (string.IsNullOrWhiteSpace(codigoNa) && string.IsNullOrWhiteSpace(codigoCliente)) continue;

                linhasAnalisadas++;
                if (!string.IsNullOrWhiteSpace(codigoNa)) linhasComNa++;
                if (!string.IsNullOrWhiteSpace(codigoCliente)) linhasComCliente++;

                // Código composto antigo (Etapa 3 — só usado se N&A e Cliente não encontrarem nada), mantido
                // apenas para compatibilidade com projetos antigos sem os números completos preenchidos.
                string y = ws.Cells[r, 25].Text?.Trim();
                string ac = ws.Cells[r, 29].Text?.Trim();
                string codigoMontado = (!string.IsNullOrWhiteSpace(y) && !string.IsNullOrWhiteSpace(ac))
                    ? $"{y}-{ws.Cells[r, 26].Text}-{ws.Cells[r, 27].Text}-{ws.Cells[r, 28].Text}-{ac}"
                    : null;

                // Declaradas e INICIALIZADAS FRESCAS a cada linha — nunca reaproveitadas da iteração
                // anterior (essa era a causa da contaminação entre linhas diferentes).
                AccReviewWorkflowService.WorkflowStatusEntry entrada = null;
                string tipoCorrespondencia = null;
                string arquivoEncontrado = null;

                // Seção 9 da correção Status_In_Flow: N&A e Cliente são SEMPRE tentados os dois (nunca
                // short-circuit) — só assim dá para detectar quando apontam para arquivos DIFERENTES
                // (conflito) em vez de escolher silenciosamente um dos dois. EncontrarCorrespondenciaControlada
                // já resolve em dois níveis (físico x lógico): ItemUrn diferente entre .dwg/.pdf do MESMO
                // Review concordante NÃO é ambiguidade — só divergência real de decisão é.
                var candidatoNA = EncontrarCorrespondenciaControlada(codigoNa, workflowEntries);
                var candidatoCliente = EncontrarCorrespondenciaControlada(codigoCliente, workflowEntries);
                CandidatoCorrespondenciaControlada candidatoComposto = default;
                bool linhaAmbigua = candidatoNA.Ambiguo || candidatoCliente.Ambiguo;
                bool linhaConflitante = false;

                if (linhaAmbigua)
                {
                    correspondenciasAmbiguas++;
                    bloqueiaUploadPorConflito = true;
                    string codigoAmbiguidade = candidatoNA.Ambiguo ? candidatoNA.CodigoResultado : candidatoCliente.CodigoResultado;
                    _logger.LogError(
                        "[WORKFLOW][LDP][CORRESPONDENCIA] Linha={Linha} | N&A={NA} | Cliente={Cliente} | TipoCorrespondencia=AMBIGUA_REAL | CodigoResultado={Codigo} | " +
                        "CandidatosNA=[{CandidatosNA}] | CandidatosCliente=[{CandidatosCliente}] | ArquivosFisicosNA=[{ArquivosNA}] | ArquivosFisicosCliente=[{ArquivosCliente}] | " +
                        "Motivo=Divergência REAL de decisão entre Reviews concorrentes ou entre extensões do mesmo Review (nunca só por ItemUrn diferente) — upload desta execução bloqueado.",
                        r, codigoNa, codigoCliente, codigoAmbiguidade,
                        string.Join(", ", candidatoNA.Candidatos), string.Join(", ", candidatoCliente.Candidatos),
                        string.Join(", ", candidatoNA.ArquivosFisicos), string.Join(", ", candidatoCliente.ArquivosFisicos));
                }
                else if (candidatoNA.Arquivo != null && candidatoCliente.Arquivo != null)
                {
                    if (string.Equals(candidatoNA.Arquivo, candidatoCliente.Arquivo, StringComparison.OrdinalIgnoreCase))
                    {
                        // N&A e Cliente concordam — aceita, prioridade formal fica com N&A, mas os dois
                        // métodos são registrados no log de correspondência.
                        arquivoEncontrado = candidatoNA.Arquivo;
                        tipoCorrespondencia = "NA";
                        encontradosNa++;
                        if (candidatoNA.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes) gruposLogicosConfirmadosPorMultiplasExtensoes++;
                        _logger.LogDebug(
                            "[WORKFLOW][LDP][CORRESPONDENCIA] Linha={Linha} | N&A={NA} | Cliente={Cliente} | TipoCorrespondencia=NA (concordância com CLIENTE) | Arquivo={Arquivo} | " +
                            "CorrespondenciaLogica={CodigoResultado} | ArquivosFisicos=[{ArquivosFisicos}]",
                            r, codigoNa, codigoCliente, arquivoEncontrado, candidatoNA.CodigoResultado, string.Join(", ", candidatoNA.ArquivosFisicos));
                    }
                    else
                    {
                        // N&A e Cliente apontam para arquivos DIFERENTES — nunca escolher silenciosamente.
                        conflitosNaCliente++;
                        linhaConflitante = true;
                        bloqueiaUploadPorConflito = true;
                        _logger.LogError(
                            "[WORKFLOW][LDP][CORRESPONDENCIA] Linha={Linha} | N&A={NA} -> ArquivoNA={ArquivoNA} | Cliente={Cliente} -> ArquivoCliente={ArquivoCliente} | " +
                            "TipoCorrespondencia=CONFLITANTE | Motivo=WORKFLOW_CORRESPONDENCIA_CONFLITANTE_NA_CLIENTE — célula preservada, upload desta execução bloqueado.",
                            r, codigoNa, candidatoNA.Arquivo, codigoCliente, candidatoCliente.Arquivo);
                    }
                }
                else if (candidatoNA.Arquivo != null)
                {
                    arquivoEncontrado = candidatoNA.Arquivo;
                    tipoCorrespondencia = "NA";
                    encontradosNa++;
                    if (candidatoNA.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes) gruposLogicosConfirmadosPorMultiplasExtensoes++;
                }
                else if (candidatoCliente.Arquivo != null)
                {
                    arquivoEncontrado = candidatoCliente.Arquivo;
                    tipoCorrespondencia = "CLIENTE";
                    encontradosCliente++;
                    if (candidatoCliente.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes) gruposLogicosConfirmadosPorMultiplasExtensoes++;
                }
                else if (!string.IsNullOrWhiteSpace(codigoMontado))
                {
                    // Etapa 3: código composto antigo — só chega aqui quando N&A e Cliente falharam.
                    candidatoComposto = EncontrarCorrespondenciaControlada(codigoMontado, workflowEntries);
                    if (candidatoComposto.Ambiguo)
                    {
                        linhaAmbigua = true;
                        correspondenciasAmbiguas++;
                        bloqueiaUploadPorConflito = true;
                        _logger.LogError(
                            "[WORKFLOW][LDP][CORRESPONDENCIA] Linha={Linha} | Composto={Composto} | TipoCorrespondencia=AMBIGUA_REAL | CodigoResultado={Codigo} | CandidatosComposto=[{Candidatos}] | " +
                            "Motivo=Divergência REAL de decisão — upload desta execução bloqueado.",
                            r, codigoMontado, candidatoComposto.CodigoResultado, string.Join(", ", candidatoComposto.Candidatos));
                    }
                    else if (candidatoComposto.Arquivo != null)
                    {
                        arquivoEncontrado = candidatoComposto.Arquivo;
                        tipoCorrespondencia = "COMPOSTO";
                        encontradosCompostos++;
                        if (candidatoComposto.CorrespondenciaLogicaConfirmadaPorMultiplasExtensoes) gruposLogicosConfirmadosPorMultiplasExtensoes++;
                    }
                }

                if (!linhaAmbigua && !linhaConflitante)
                {
                    _logger.LogDebug(
                        "[WORKFLOW][LDP][CORRESPONDENCIA] Linha={Linha} | N&A={NA} | Cliente={Cliente} | Composto={Composto} | TipoCorrespondencia={Tipo} | Arquivo={Arquivo}",
                        r, codigoNa, codigoCliente, codigoMontado, tipoCorrespondencia ?? "NENHUMA", arquivoEncontrado ?? "(nenhum)");
                }

                // Só DEPOIS de descobrir o nome real é que a entrada completa é recuperada por ele.
                if (arquivoEncontrado != null)
                {
                    entrada = workflowEntries
                        .Where(e => string.Equals(e.FileName, arquivoEncontrado, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(e => e.DataAtualizacaoReview ?? DateTime.MinValue)
                        .FirstOrDefault();
                }

                // Validação obrigatória antes da escrita: revalida a entrada encontrada contra o MESMO
                // código que originou a correspondência, usando a mesma função de tokens — nunca confia
                // cegamente no resultado do passo anterior.
                if (entrada != null)
                {
                    string codigoQueEncontrou = tipoCorrespondencia switch
                    {
                        "NA" => codigoNa,
                        "CLIENTE" => codigoCliente,
                        "COMPOSTO" => codigoMontado,
                        _ => null
                    };

                    if (!NomeArquivoCorrespondeAoDocumento(entrada.FileName, codigoQueEncontrou))
                    {
                        bloqueiosValidacao++;
                        _logger.LogError(
                            $"❌ [Workflow Status] BLOQUEIO DE CORRESPONDÊNCIA: Linha {r} | N&A: '{codigoNa}' | Cliente: '{codigoCliente}' | Composto: '{codigoMontado}' | " +
                            $"Arquivo retornado: '{entrada.FileName}' | ReviewId: {entrada.ReviewId} | Status: '{entrada.Status}' | Tipo: {tipoCorrespondencia} | " +
                            "Motivo: falha na revalidação por tokens — resultado descartado, célula preservada.");

                        entrada = null;
                        tipoCorrespondencia = null;
                    }
                }

                _logger.LogDebug(
                    $"[Workflow Status] Linha: {r} | N&A: '{codigoNa}' | Cliente: '{codigoCliente}' | Composto: '{codigoMontado}' | Correspondência: {tipoCorrespondencia ?? "nenhuma"}" +
                    (entrada != null
                        ? $" | Arquivo: '{entrada.FileName}' | ReviewId: '{entrada.ReviewId}' | Status: '{entrada.Status}' | Data do Review: {entrada.DataAtualizacaoReview}"
                        : ""));

                if (entrada != null && entrada.PreservarCelula)
                {
                    // Seção 10 da manutenção técnica (Status_In_Flow): o Review mais recente deste
                    // documento não pôde ser resolvido (etapa não encontrada) OU aparenta concluído sem
                    // nenhum resultado final comprovado — a célula é preservada e NUNCA se reaproveita o
                    // status de um Review mais antigo (ex.: um "Aprovado" anterior não pode vencer aqui).
                    preservadosPorHistoricoRecente++;
                    string codigoDaLinha = tipoCorrespondencia switch
                    {
                        "NA" => codigoNa,
                        "CLIENTE" => codigoCliente,
                        "COMPOSTO" => codigoMontado,
                        _ => null
                    };

                    _logger.LogWarning(
                        "[WORKFLOW][HISTORICO][REVIEW_ATUAL_BLOQUEIA_ANTERIOR] Linha={Linha} | Documento={Documento} | Arquivo={Arquivo} | ReviewId={ReviewId} | " +
                        "CodigoResolucao={CodigoResolucao} | Motivo={Motivo} | Célula preservada.",
                        r, codigoDaLinha, entrada.FileName, entrada.ReviewId, entrada.CodigoResolucao, entrada.MensagemResolucao);

                    continue;
                }

                if (entrada != null)
                {
                    documentosEncontradosNoMapa++;
                    if (entrada.EhVereditoFinal) vereditosFinaisAplicados++; else etapasAtuaisAplicadas++;

                    // Proteção contra reutilização (seção 14): registra que ESTA entrada (arquivo + Review)
                    // foi associada a ESTE código da LDP nesta execução.
                    string chaveAssociacao = entrada.FileName + "::" + entrada.ReviewId;
                    string codigoUsadoNestaLinha = tipoCorrespondencia switch
                    {
                        "NA" => codigoNa,
                        "CLIENTE" => codigoCliente,
                        "COMPOSTO" => codigoMontado,
                        _ => null
                    };

                    if (!associacoesPorEntrada.TryGetValue(chaveAssociacao, out var codigosAssociados))
                    {
                        codigosAssociados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        associacoesPorEntrada[chaveAssociacao] = codigosAssociados;
                    }
                    if (!string.IsNullOrWhiteSpace(codigoUsadoNestaLinha)) codigosAssociados.Add(codigoUsadoNestaLinha.Trim());

                    string valorAtual = GetSafeCellValue(ws, r, colStatusEfetivo);
                    // Preserva EXATAMENTE o idioma e a capitalização retornados pelo ACC — nunca força
                    // maiúsculas aqui. A comparação abaixo (para decidir se mudou) ignora caixa; a escrita
                    // na célula usa o valor tal como veio.
                    string novoStatus = entrada.Status?.Trim() ?? "";

                    // Proteção contra aprovação indevida: "APROVADO" só pode ser gravado quando a própria
                    // entrada foi resolvida como veredito final explícito (AccReviewWorkflowService só monta
                    // EhVereditoFinal=true nesse caso). Cinto de segurança contra regressão futura — nunca
                    // deveria disparar com a resolução atual (currentStepId nunca vira "APROVADO").
                    if (string.Equals(novoStatus, "APROVADO", StringComparison.OrdinalIgnoreCase) && !entrada.EhVereditoFinal)
                    {
                        throw new InvalidOperationException(
                            $"Tentativa inválida de marcar o documento '{entrada.FileName}' (linha {r}, ReviewId {entrada.ReviewId}) como APROVADO sem veredito final explícito.");
                    }

                    if (novoStatus.Equals("APROVADO", StringComparison.OrdinalIgnoreCase)) totalAprovados++;

                    _logger.LogInformation(
                        "[STATUS_IN_FLOW][ESCRITA_PROPOSTA] Linha={Linha} | Documento={Documento} | Arquivo={Arquivo} | ValorAtual={ValorAtual} | ValorProposto={ValorProposto} | " +
                        "Origem={Origem} | MetodoChamador=UpdateWorkflowStatusAsync | ReviewId={ReviewId} | TipoCorrespondencia={TipoCorrespondencia}",
                        r, tipoCorrespondencia == "NA" ? codigoNa : tipoCorrespondencia == "CLIENTE" ? codigoCliente : codigoMontado,
                        entrada.FileName, valorAtual, novoStatus, entrada.OrigemStatus, entrada.ReviewId, tipoCorrespondencia);

                    if (!string.Equals(valorAtual, novoStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        ws.Cells[r, colStatusEfetivo].Value = string.IsNullOrEmpty(novoStatus) ? null : novoStatus;
                        count++;
                        statusAlterados++;

                        _logger.LogInformation(
                            "[STATUS_IN_FLOW][ESCRITA_APLICADA] Linha={Linha} | Arquivo={Arquivo} | ValorAnterior={ValorAnterior} | ValorNovo={ValorNovo} | Origem={Origem} | ReviewId={ReviewId}",
                            r, entrada.FileName, valorAtual, novoStatus, entrada.OrigemStatus, entrada.ReviewId);
                    }
                    else
                    {
                        statusJaIguais++;
                    }
                }
                else
                {
                    // Não encontrado (ou descartado pela validação): PRESERVA o valor existente da coluna
                    // 17. Uma falha pontual de API, a ausência de Review para este documento, ou um
                    // bloqueio de correspondência não pode apagar um status já gravado.
                    string chaveNa = NormalizeKeyOnly(codigoNa);
                    string chaveCliente = NormalizeKeyOnly(codigoCliente);
                    if ((!string.IsNullOrEmpty(chaveNa) && filesInAccNorm.Contains(chaveNa)) ||
                        (!string.IsNullOrEmpty(chaveCliente) && filesInAccNorm.Contains(chaveCliente)))
                    {
                        documentosSemReview++;
                    }
                }
            }

            if (count > 0)
            {
                package.Save();
            }
        }

        // Seção 14: se uma única entrada (mesmo arquivo + mesmo Review) foi associada a muitos códigos
        // diferentes da LDP nesta execução, é sinal de contaminação (ex.: o bug que originou esta correção,
        // em que um único arquivo acabou vinculado a centenas de linhas via código composto degenerado) —
        // cancela o upload antes de publicar o resultado.
        foreach (var associacao in associacoesPorEntrada)
        {
            if (associacao.Value.Count > LIMITE_CODIGOS_POR_ENTRADA)
            {
                throw new InvalidOperationException(
                    $"Reutilização indevida detectada: a entrada '{associacao.Key}' foi associada a {associacao.Value.Count} códigos diferentes da LDP nesta execução " +
                    $"(ex.: {string.Join(", ", associacao.Value.Take(10))}). Upload da LDP cancelado.");
            }
        }

        if (workflowEntries.Count > 0 && documentosEncontradosNoMapa == 0)
        {
            _logger.LogError(
                "❌ [Workflow Status] WORKFLOW/LDP INCONSISTENTE: o mapa possui documentos com status " +
                $"({workflowEntries.Count} entrada(s)), mas nenhuma linha da LDP foi associada a eles.");
        }

        // Proteção contra aprovação em massa (antes do upload): se existem Reviews genuinamente ativos
        // (etapa atual resolvida, não os fallbacks "EM REVISÃO"/"CONCLUÍDO SEM VEREDITO") e, mesmo assim,
        // TODOS os documentos encontrados nesta execução foram classificados como APROVADO, o resultado é
        // inconsistente — cancela o upload em vez de publicar um lote de aprovações suspeito.
        int reviewsAtivosComCurrentStep = workflowEntries
            .Where(e => !e.EhVereditoFinal
                && !string.IsNullOrWhiteSpace(e.Status)
                && !e.Status.Equals("EM REVISÃO", StringComparison.OrdinalIgnoreCase)
                && !e.Status.Equals("CONCLUÍDO SEM VEREDITO", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ReviewId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (reviewsAtivosComCurrentStep > 0 && documentosEncontradosNoMapa > 0 && totalAprovados == documentosEncontradosNoMapa)
        {
            throw new InvalidOperationException(
                "Resultado inconsistente: todos os documentos foram marcados como APROVADO, apesar de existirem " +
                $"{reviewsAtivosComCurrentStep} Review(s) ativo(s) com etapa em andamento (currentStepId preenchido). Upload da LDP cancelado.");
        }

        string resumo =
            $"Linhas analisadas: {linhasAnalisadas} | Linhas com N&A: {linhasComNa} | Linhas com Cliente: {linhasComCliente} | " +
            $"Encontrados pelo N&A: {encontradosNa} | Encontrados pelo Cliente: {encontradosCliente} | Encontrados pelo código composto: {encontradosCompostos} | " +
            $"Grupos lógicos confirmados por múltiplas extensões: {gruposLogicosConfirmadosPorMultiplasExtensoes} | " +
            $"Bloqueios de validação: {bloqueiosValidacao} | Conflitos N&A x Cliente: {conflitosNaCliente} | Correspondências ambíguas reais: {correspondenciasAmbiguas} | " +
            $"Sem correspondência: {linhasAnalisadas - documentosEncontradosNoMapa} | " +
            $"Documentos da pasta de Produção sem Review: {documentosSemReview} | Status já iguais: {statusJaIguais} | Status alterados: {statusAlterados} | " +
            $"Etapas atuais aplicadas: {etapasAtuaisAplicadas} | Vereditos finais aplicados: {vereditosFinaisAplicados} | " +
            $"Preservados por histórico mais recente não resolvido: {preservadosPorHistoricoRecente}";

        _logger.LogInformation(
            "[STATUS_IN_FLOW][RESULTADO_FINAL] {Resumo} | UploadBloqueadoPorConflito={Bloqueado}",
            resumo, bloqueiaUploadPorConflito);

        if (bloqueiaUploadPorConflito)
        {
            // Seção 9.4 da correção Status_In_Flow: conflito N&A x Cliente OU correspondência ambígua em
            // QUALQUER linha bloqueia o upload da execução inteira — nunca publica um lote parcialmente
            // confiável. As células dessas linhas já foram preservadas (arquivoEncontrado ficou null).
            _logger.LogError(
                "[STATUS_IN_FLOW][RESULTADO_FINAL] Upload BLOQUEADO nesta execução: {Conflitos} conflito(s) N&A x Cliente e {Ambiguas} correspondência(s) ambígua(s) detectada(s). " +
                "Nenhuma alteração foi publicada, mesmo havendo {Count} linha(s) com mudança válida calculada. Ver logs [WORKFLOW][LDP][CORRESPONDENCIA].",
                conflitosNaCliente, correspondenciasAmbiguas, count);

            return $"⚠️ Upload bloqueado: {conflitosNaCliente} conflito(s) N&A x Cliente e {correspondenciasAmbiguas} correspondência(s) ambígua(s) detectada(s) nesta execução. " +
                   "Nenhuma alteração foi publicada. Ver logs [WORKFLOW][LDP][CORRESPONDENCIA] para os detalhes por linha.";
        }

        if (count > 0)
        {
            // 4. UPLOAD CORRIGIDO (Passando parentFolderId)
            using (var ul = new FileStream(tempFilePath, FileMode.Open))
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, fileName, ul, accessToken);

            var itemDetailsPosUpload = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
            string versaoCriada = itemDetailsPosUpload?.TipVersionUrn ?? versionAnterior;

            _logger.LogInformation(
                $"[Workflow Status] {resumo} | Nova versão da LDP criada: sim | Versão anterior: {ResumirUrn(versionAnterior)} | Versão usada na persistência SQL: {ResumirUrn(versaoCriada)}.");

            return $"✅ Sucesso! {count} status atualizados.";
        }

        _logger.LogInformation($"[Workflow Status] {resumo} | Nova versão da LDP criada: não | Versão usada na persistência SQL: {ResumirUrn(versionAnterior)}.");

        return "Nada para atualizar na Etapa 2.";
    }
    catch (Exception ex) { throw; }
    finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
}


/// <summary>
/// DESATIVADO (seção 8 da correção Status_In_Flow). Este método era o segundo escritor independente
/// da coluna Q — resolvia o veredito por conta própria (composto Y-AC + nome, sem escopo de pasta, sem
/// prioridade de ItemUrn) e podia sobrescrever, na mesma execução, o valor que
/// <see cref="UpdateWorkflowStatusAsync"/> acabara de gravar corretamente (é assim que "DOC CONTROL"
/// sobrevivia mesmo com a Etapa 5 resolvendo "Aprovado" para o mesmo documento). A partir de agora só
/// existe UM escritor ativo de Status_In_Flow: <see cref="UpdateWorkflowStatusAsync"/> (via
/// AccReviewWorkflowService, Review mais recente, ItemUrn prioritário). Este método não baixa a LDP,
/// não recalcula veredito, não escreve a coluna Q, não salva e não publica nova versão — é mantido só
/// para não quebrar a assinatura pública caso algo externo ainda a referencie; o Worker não o chama mais
/// (ver GrdAutomationWorker.cs).
/// </summary>
public Task<string> RunApprovalVerdictSyncAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId)
{
    _logger.LogWarning(
        "[WORKFLOW][MAPA][FALHA_GLOBAL] RunApprovalVerdictSyncAsync foi chamado, mas está DESATIVADO desde a correção do escritor único de Status_In_Flow — " +
        "nenhuma leitura, cálculo, escrita ou upload foi realizada. A autoridade única é LdpUpdateService.UpdateWorkflowStatusAsync (via AccReviewWorkflowService).");
    return Task.FromResult("⏸️ Etapa 8 desativada — não escreve mais Status_In_Flow. Autoridade única: UpdateWorkflowStatusAsync.");
}
        
        private string NormalizeDoc(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Pega o nome sem extensão e coloca em Maiúsculo
            string name = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();

            // 2. Padroniza separadores para facilitar a remoção de sufixos
            name = name.Replace("_", "-").Replace(" ", "-");

            // 3. Remove os sufixos de revisão (ex: -R01, -REV02, -V1) 
            // Fazemos isso antes da limpeza final para não sobrar números da revisão
            name = Regex.Replace(name, @"-R\d+$", "");
            name = Regex.Replace(name, @"-REV\d+$", "");
            name = Regex.Replace(name, @"-V\d+$", "");

            // 4. LIMPEZA FINAL (O "Pulo do Gato"):
            // Remove TUDO o que não for Letra ou Número.
            // Ex: "CON005-000-AQM-3D-003" vira "CON005000AQM3D003"
            return Regex.Replace(name, @"[^A-Z0-9]", "");
        }

        // --- NOVO MÉTODO PARA LIMPAR CHAVES DO EXCEL (SEM CORTAR O PONTO) ---
        private string NormalizeKeyOnly(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string upper = input.ToUpperInvariant();
            return Regex.Replace(upper, @"[^A-Z0-9]", "");
        }

        /// <summary>
        /// Resume uma VersionUrn para uso em log (não expõe a URN completa nem o token).
        /// </summary>
        private static string ResumirUrn(string urn)
        {
            if (string.IsNullOrEmpty(urn)) return "(vazia)";
            return urn.Length <= 12 ? urn : $"{urn.Substring(0, 6)}...{urn.Substring(urn.Length - 4)}";
        }

        /// <summary>
        /// Resume um PayloadHash (SHA-256, 64 caracteres) para uso em log.
        /// </summary>
        private static string ResumirHash(string hash) =>
            string.IsNullOrEmpty(hash) ? "(vazio)" : (hash.Length <= 12 ? hash : $"{hash.Substring(0, 8)}...");

        /// <summary>
        /// Classifica, só para observabilidade (seção 8 da manutenção técnica), o motivo de uma decisão
        /// de cache — NUNCA participa da decisão real de CACHE HIT (essa continua sendo exclusivamente
        /// "estadoAnterior != null &amp;&amp; UltimaTentativaSucesso &amp;&amp; hash igual", calculada pelo chamador
        /// e passada aqui só como <paramref name="cacheHit"/>).
        /// </summary>
        private static string DeterminarMotivoDecisaoCache(
            AccAttributeSyncState estadoAnterior, bool cacheHit, string versionUrn, string chaveConfiguracao, string fileName,
            Dictionary<string, AccAttributeSyncState> estadosSincronizacao)
        {
            if (cacheHit) return "CACHE_HIT_PAYLOAD_IDENTICO";

            if (estadoAnterior == null)
            {
                bool existeChaveDiferenteMesmaUrn = estadosSincronizacao.Values.Any(e =>
                    string.Equals(e.VersionUrn, versionUrn, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(e.ChaveConfiguracao, chaveConfiguracao, StringComparison.OrdinalIgnoreCase));
                if (existeChaveDiferenteMesmaUrn) return "CACHE_MISS_CHAVE_CONFIGURACAO_DIFERENTE";

                bool existeVersaoAnteriorMesmoArquivo = estadosSincronizacao.Values.Any(e =>
                    string.Equals(e.ChaveConfiguracao, chaveConfiguracao, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                return existeVersaoAnteriorMesmoArquivo ? "CACHE_MISS_NOVA_VERSAO" : "CACHE_MISS_SEM_HISTORICO";
            }

            if (string.IsNullOrEmpty(estadoAnterior.PayloadHash)) return "CACHE_MISS_ESTADO_INCOMPLETO";
            if (!estadoAnterior.UltimaTentativaSucesso) return "CACHE_MISS_FALHA_ANTERIOR";
            return "CACHE_MISS_PAYLOAD_ALTERADO";
        }

        /// <summary>
        /// Lê AttributeSync:ForceOverwriteAll do appsettings (padrão false quando ausente). Único ponto
        /// de leitura da flag — nunca lida diretamente dentro do loop de arquivos.
        /// </summary>
        internal static bool LerForceOverwriteAll(IConfiguration configuration)
        {
            return configuration.GetValue("AttributeSync:ForceOverwriteAll", false);
        }

        /// <summary>
        /// AttributeSync:ForceOverwriteAll=false: um CACHE_HIT (payload idêntico já sincronizado com
        /// sucesso) continua pulando o POST — comportamento inalterado. Único predicado que decide isso;
        /// nunca reimplementado inline no loop.
        /// </summary>
        internal static bool DeveHonrarCacheHit(bool cacheHit, bool forceOverwriteAll)
        {
            return cacheHit && !forceOverwriteAll;
        }

        /// <summary>
        /// AttributeSync:ForceOverwriteAll=true: um CACHE_HIT que normalmente pularia o POST passa a
        /// forçar o reenvio (para restaurar atributos apagados manualmente no ACC, que não mudam
        /// VersionUrn/SQL/PayloadHash). Só é verdadeiro quando havia mesmo um CACHE_HIT — a flag nunca
        /// cria trabalho extra quando já é CACHE_MISS (esse caminho já envia normalmente).
        /// </summary>
        internal static bool EhReenvioForcado(bool cacheHit, bool forceOverwriteAll)
        {
            return cacheHit && forceOverwriteAll;
        }

        /// <summary>
        /// Valores que a LDP usa como "sem informação" e que NUNCA devem ser enviados como valor de
        /// atributo — enviá-los apagaria um valor real já existente no ACC (isso é diferente de omitir o
        /// atributo, que é o comportamento correto). Verificado antes de IsNullOrWhiteSpace para cobrir o
        /// caso comum (string vazia/nula), mais os literais "-" e "N/A" (com espaços e caixa variáveis)
        /// vindos da LDP.
        /// </summary>
        internal static bool EhValorVazioOuPlaceholder(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return true;

            string valorNormalizado = valor.Trim();
            return valorNormalizado.Equals("-", StringComparison.OrdinalIgnoreCase)
                || valorNormalizado.Equals("N/A", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Deduplicação por VersionUrn dentro da MESMA execução (independente de ForceOverwriteAll e do
        /// cache persistente): true quando esta VersionUrn já foi atualizada com sucesso nesta execução,
        /// por esta configuração ou por outra (<paramref name="mesmaConfiguracao"/> distingue os dois
        /// casos só para fins de log/estatística). Extraído de dentro do loop de arquivos apenas para
        /// tornar a regra testável em isolamento — o comportamento é idêntico ao lookup inline anterior.
        /// </summary>
        internal static bool VersionUrnJaAtualizadaNestaExecucao(
            Dictionary<string, string> versionUrnAtualizadaPorConfiguracao, string versionUrn, string chaveConfiguracaoAtual,
            out bool mesmaConfiguracao)
        {
            if (versionUrnAtualizadaPorConfiguracao.TryGetValue(versionUrn, out var chaveConfigQueAtualizouAntes))
            {
                mesmaConfiguracao = string.Equals(chaveConfigQueAtualizouAntes, chaveConfiguracaoAtual, StringComparison.OrdinalIgnoreCase);
                return true;
            }

            mesmaConfiguracao = false;
            return false;
        }

        /// <summary>
        /// Escreve um novo valor numa célula do Excel somente se ele for diferente do atual
        /// (comparação case-insensitive, com Trim dos dois lados), retornando se houve alteração —
        /// para que o contador de mudanças (`count`) só seja incrementado quando algo realmente mudou.
        /// Usado por UpdateWorkflowStatusAsync para nunca contar/gravar uma "mudança" que na verdade
        /// deixaria a célula com o mesmo texto que já tinha.
        /// </summary>
        private static bool DefinirCelulaSeDiferente(OfficeOpenXml.ExcelRange celula, string novoValor)
        {
            string atual = celula.Text?.Trim() ?? "";
            string novo = novoValor?.Trim() ?? "";

            if (string.Equals(atual, novo, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            celula.Value = string.IsNullOrEmpty(novo) ? null : novo;
            return true;
        }

        /// <summary>
        /// Chave em memória do cache persistente de sincronização de atributos (dbo.ACC_Attribute_Sync_State):
        /// VersionUrn + ChaveConfiguracao — nunca só VersionUrn, porque o estado precisa respeitar a
        /// coluna ChaveConfiguracao existente na tabela (o mesmo VersionUrn poderia, em tese, receber
        /// atributos diferentes sob configurações diferentes). Usa  (Unit Separator) para não colidir
        /// com nenhum caractere que apareça normalmente em uma URN ou em uma Chave (ex.: "PRODUCAO_10").
        /// Precisa ser IDÊNTICA à chave montada em DatabaseService.ObterEstadosSincronizacaoAtributos.
        /// </summary>
        private static string MontarChaveEstado(string versionUrn, string chaveConfiguracao)
        {
            return $"{versionUrn}{chaveConfiguracao}";
        }

        /// <summary>
        /// Calcula o hash SHA-256 (64 caracteres hexadecimais, compatível com PayloadHash CHAR(64)) do
        /// payload REAL que seria enviado a UpdateCustomAttributesForVersionAsync — uma List&lt;object&gt;
        /// de objetos anônimos { id, value } (é isso que o método realmente recebe; ver
        /// AutodeskApiService.UpdateCustomAttributesForVersionAsync e a montagem de customAttributesForFile
        /// logo acima). Não usa GetHashCode() (não é estável entre processos/execuções). Ordena por id do
        /// atributo para o resultado ser determinístico independentemente da ordem de inserção.
        /// </summary>
        private static string CalcularPayloadHash(List<object> payload)
        {
            var itensCanonicos = payload
                .Select(item =>
                {
                    var tipo = item.GetType();
                    object idBruto = tipo.GetProperty("id")?.GetValue(item);
                    object valorBruto = tipo.GetProperty("value")?.GetValue(item);
                    long id = idBruto != null ? Convert.ToInt64(idBruto) : 0;
                    return (Id: id, Valor: valorBruto);
                })
                .OrderBy(x => x.Id)
                .Select(x => $"{x.Id}={NormalizarValorHash(x.Valor)}");

            string conteudoCanonico = string.Join("|", itensCanonicos);

            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(conteudoCanonico);
            byte[] hash = sha256.ComputeHash(bytes);

            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Normaliza um valor para o cálculo do hash de forma estável entre execuções (o valor real
        /// enviado ao ACC, hoje, é sempre string — mas a normalização cobre outros tipos por segurança
        /// caso o payload mude no futuro).
        /// </summary>
        private static string NormalizarValorHash(object valor)
        {
            if (valor == null) return "<NULL>";
            if (valor is string texto) return texto.Trim();
            if (valor is DateTime data) return data.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            if (valor is bool booleano) return booleano ? "TRUE" : "FALSE";
            if (valor is IFormattable formatavel) return formatavel.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            return valor.ToString()?.Trim() ?? string.Empty;
        }

        private string GetProjectSetting(string projectId, string key)
{
    var val = _configuration[$"ProjectSettings:{projectId}:{key}"];
    return string.IsNullOrEmpty(val) ? _configuration[$"ProjectSettings:Default:{key}"] : val;
}

/// <summary>
        /// Verifica arquivos que existem fisicamente no ACC mas NÃO constam na planilha LDP.
        /// </summary>
        public async Task<List<string>> CheckFilesMissingInLdpAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("Iniciando auditoria: Arquivos ACC vs LDP...");

            // 1. Obter todos os arquivos do ACC (Nome -> URNs)
            // O método GetAllProjectFilesAsync já remove a extensão (.pdf, .dwg) e retorna o nome limpo
            var accFilesMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);
            var accFileNames = accFilesMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation($"Total de arquivos únicos encontrados no ACC: {accFileNames.Count}");

            // 2. Baixar e Ler a LDP para pegar os códigos esperados
            string ldpVersionUrn = await _autodeskApi.FindLdpFileUrnAsync(hubId, projectId, accessToken);
            string tempFilePath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, ldpVersionUrn, accessToken);
            
            var ldpCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
                {
                    var ldpWorksheet = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    if (ldpWorksheet == null || ldpWorksheet.Dimension == null) 
                        throw new Exception("Aba 'LDP' inválida ou não encontrada.");

                    // --- Lógica simplificada de detecção de cabeçalho (Reaproveitando a lógica robusta) ---
                    int headerRow = -1;
                    int colKey = -1;

                    // Busca a linha de cabeçalho
                    for (int r = 1; r <= Math.Min(20, ldpWorksheet.Dimension.End.Row); r++)
                    {
                        for (int c = 1; c <= Math.Min(50, ldpWorksheet.Dimension.End.Column); c++)
                        {
                            var val = GetSafeCellValue(ldpWorksheet, r, c);
                            // Tenta achar "CR" ou "NÚMERO DOCUMENTO CLIENTE" ou "CÓDIGO"
                            if (val.Equals("CR", StringComparison.OrdinalIgnoreCase) || 
                                val.Contains("DOCUMENTO CLIENTE", StringComparison.OrdinalIgnoreCase))
                            {
                                headerRow = r;
                                break;
                            }
                        }
                        if (headerRow != -1) break;
                    }

                    if (headerRow == -1) headerRow = 7; // Fallback padrão

                    // Busca a coluna CHAVE (A que contém o nome do arquivo)
                    // Geralmente é uma combinação ou o NÚMERO DOCUMENTO CLIENTE.
                    // Vou procurar por colunas comuns que identificam o documento.
                    string[] possibleHeaders = { "NÚMERO DOCUMENTO CLIENTE", "CODIGO", "CÓDIGO", "DOC. CLIENTE", "CR" };
                    
                    // Tenta achar a coluna exata através dos headers
                    for (int c = 1; c <= ldpWorksheet.Dimension.End.Column; c++)
                    {
                        string headerVal = GetSafeCellValue(ldpWorksheet, headerRow, c);
                        // Verifica se o cabeçalho bate com algum dos possíveis
                        if (possibleHeaders.Any(ph => headerVal.Contains(ph, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Validação extra: Se for "CR", precisamos garantir que não é apenas parte do código
                            // Mas assumindo que você usa a lógica de compor nome, vamos pegar a coluna principal se houver.
                            // Se o seu projeto usa concatenação (CR-AREA...), essa lógica simples pega a coluna explícita.
                            // Se precisar montar o nome (CR + AREA + DISC), avise. 
                            // *Assumindo aqui que existe uma coluna com o NOME FINAL ou NÚMERO CLIENTE*.
                            
                            // PRIORIDADE: NÚMERO DOCUMENTO CLIENTE
                            if (headerVal.Contains("DOCUMENTO CLIENTE", StringComparison.OrdinalIgnoreCase))
                            {
                                colKey = c;
                                break; 
                            }
                        }
                    }

                    // Se não achou "Documento Cliente", tenta pegar a primeira coluna válida (muitas vezes a coluna B ou C é o código)
                    if (colKey == -1) 
                    {
                        // Fallback: Tenta achar a coluna que tem dados parecidos com nomes de arquivos na linha seguinte
                        // Isso é um pouco arriscado, então idealmente você deve saber o nome da coluna.
                        // Vou assumir que se não achou "Documento Cliente", usa a lógica do "File Identifier" padrão do projeto ou erro.
                        _logger.LogWarning("Não foi possível identificar automaticamente a coluna de Código na LDP. Tentando coluna 'NÚMERO DOCUMENTO CLIENTE' via string exata.");
                        // Aqui você pode forçar uma busca se souber o nome exato que usa na sua LDP
                    }

                    // Loop de leitura
                    int startRow = headerRow + 1;
                    int endRow = ldpWorksheet.Dimension.End.Row;

                    // Busca dinâmica de "Qual coluna usar como chave" baseada no appsettings se necessário, 
                    // mas aqui vou varrer procurando a coluna "NÚMERO DOCUMENTO CLIENTE" que é o padrão da maioria.
                    
                    // REFAZENDO A BUSCA DE COLUNA MAIS SEGURA:
                    var headers = new Dictionary<string, int>();
                    for(int c=1; c <= ldpWorksheet.Dimension.End.Column; c++)
                    {
                         string h = GetSafeCellValue(ldpWorksheet, headerRow, c);
                         if(!string.IsNullOrEmpty(h)) headers[h.ToUpper()] = c;
                    }

                    // Tenta chaves comuns
                    if (headers.ContainsKey("NÚMERO DOCUMENTO CLIENTE")) colKey = headers["NÚMERO DOCUMENTO CLIENTE"];
                    else if (headers.ContainsKey("CODIGO")) colKey = headers["CODIGO"];
                    else if (headers.ContainsKey("CÓDIGO")) colKey = headers["CÓDIGO"];
                    
                    if (colKey != -1)
                    {
                        for (int row = startRow; row <= endRow; row++)
                        {
                            string code = GetSafeCellValue(ldpWorksheet, row, colKey);
                            if (!string.IsNullOrWhiteSpace(code))
                            {
                                ldpCodes.Add(code.Trim());
                            }
                        }
                    }
                    else
                    {
                         _logger.LogError("Coluna chave não encontrada na LDP para comparação.");
                         throw new Exception("Coluna 'NÚMERO DOCUMENTO CLIENTE' (ou similar) não encontrada na LDP.");
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }

            _logger.LogInformation($"Total de códigos encontrados na LDP: {ldpCodes.Count}");

            // 3. Comparação: (Arquivos ACC) - (Códigos LDP)
            // ANTES: "!ldpCodes.Contains(accFile)" comparava o nome do arquivo (que no ACC costuma
            // vir com sufixo de revisão, ex.: "P-0602.1-000-PM-LI-0001_Rev0A") contra o código puro
            // da LDP ("P-0602.1-000-PM-LI-0001") por igualdade exata via HashSet — como os dois quase
            // nunca batiam byte a byte, praticamente TODO arquivo do ACC era reportado como "sem
            // registro na LDP" (falso positivo). Usamos o DocumentMatchingService (mesmo utilizado em
            // AutodeskApiService.SyncLdpAttributesAsync) para achar, para cada código da LDP, o arquivo
            // do ACC correspondente por prefixo — cobrindo sufixos como "_RevA"/"-R01".
            var arquivosRegistradosNaLdp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ldpCode in ldpCodes)
            {
                var arquivoEncontrado = _documentMatching.EncontrarDocumentoNoAcc(ldpCode, null, accFileNames);
                if (arquivoEncontrado != null)
                {
                    arquivosRegistradosNaLdp.Add(arquivoEncontrado);
                }
            }

            // Retorna arquivos que estão no ACC mas NÃO estão na LDP
            var filesNotInLdp = accFileNames
                .Where(accFile => !arquivosRegistradosNaLdp.Contains(accFile) && !accFile.StartsWith("~$")) // Ignora temporários do Excel
                .OrderBy(x => x)
                .ToList();

            _logger.LogInformation($"Resultado: {filesNotInLdp.Count} arquivos no ACC sem registro na LDP.");

            return filesNotInLdp;
        }
// Modulo: APS.WebApp.Services/LdpUpdateService.cs

// Modulo: APS.WebApp.Services/LdpUpdateService.cs

// [ETAPA7][SQL] Distingue "Execution Timeout Expired" (SqlException.Number == -2, ou TimeoutException)
// de qualquer outro erro de SQL, para logar [ETAPA7][SQL][TIMEOUT] especificamente nesse caso antes de
// deixar a exceção seguir para o catch genérico existente em SyncAccAttributesFromSqlAsync.
private static bool EhErroDeTimeoutSql(Exception ex)
{
    return ex is TimeoutException
        || (ex is Microsoft.Data.SqlClient.SqlException sqlEx && sqlEx.Number == -2);
}

public async Task<string> SyncAccAttributesFromSqlAsync(string hubId, string projectId, string token)
{
    _logger.LogInformation("⚡ [ETAPA 7/11] Sincronizando Atributos no ACC (Lendo direto da Ldp_Unificada no SQL)...");

    // [ETAPA7][RESUMO] — política de roots permitidos/bloqueados para ESCRITA de atributos (Fase B,
    // 17/09/2026, decisão do usuário — nunca uma limitação técnica). A pasta "09 - ENGINEERING
    // DOCUMENTS" continua existindo e acessível a qualquer OUTRA etapa; só a Etapa 07 nunca mais escreve
    // atributos em arquivos dela.
    {
        var permitidos = PastasAtributos
            .Where(g => !ChavesBloqueadasParaAtualizacaoDeAtributos.Contains(g.Chave))
            .Select(g => g.PastasPrincipaisAlternativas.FirstOrDefault() ?? g.Chave)
            .ToList();
        var bloqueados = PastasAtributos
            .Where(g => ChavesBloqueadasParaAtualizacaoDeAtributos.Contains(g.Chave))
            .Select(g => g.PastasPrincipaisAlternativas.FirstOrDefault() ?? g.Chave)
            .ToList();

        _logger.LogInformation(
            "[ETAPA7][RESUMO] RootsPermitidos={QuantidadePermitidos} | RootsBloqueados={QuantidadeBloqueados} | Permitidos=[{Permitidos}] | Bloqueado=[{Bloqueados}]",
            permitidos.Count, bloqueados.Count, string.Join(", ", permitidos), string.Join(", ", bloqueados));
    }

    try
    {
        // 1. Busca o padrão de nomenclatura no banco de dados da N&A
        string padraoNomenclatura = _dbService.ObterContextoProjeto(projectId)?.NamingStandard ?? "NA";
        _logger.LogInformation($"[Sync] Padrão de Nomenclatura detectado via Banco: '{padraoNomenclatura}'");

        var ldpData = new List<Dictionary<string, string>>();

        // 🎯 CORREÇÃO CRÍTICA: Conexão bruta idêntica ao DatabaseService.cs
        // Isso garante que o robô acesse o banco exato onde a Ldp_Unificada foi criada.
        string connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;";

        // [ETAPA7][SQL] CommandTimeout explícito (nunca 0/infinito): o default do SqlCommand é 30s, e a
        // consulta abaixo pode esbarrar nisso em cenários pontuais (1ª conexão fria com o linked server
        // usado por VW_Ldp_Unificada para o HH_TimeSheet, ou contenção de lock com outra etapa do Worker
        // escrevendo em Ldp_Unificada no mesmo instante) mesmo a query rodando em menos de 1s em condições
        // normais. 180s dá margem para esses picos sem travar o Worker indefinidamente se o SQL realmente
        // estiver com problema.
        const int etapa7CommandTimeoutSegundos = 180;

        using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = etapa7CommandTimeoutSegundos;

                // Comando SQL limpo focando apenas no schema dbo
                // Modulo: APS.WebApp.Services/LdpUpdateService.cs
// Altere apenas o bloco do comando SQL dentro de SyncAccAttributesFromSqlAsync

cmd.CommandText = @"
    SELECT
        [NA_Document_Number],
        [Client_Document_Number],
        [Document_Title],
        [Format],
        [No_Pages],
        [Emission_Date],
        [Revision],
        [Document_Status],
        [Discipline],
        [Transmittal_Number], -- 🔥 COLUNA ADICIONADA AQUI 🔥
        [CR],
        [CR_OS]
    FROM [dbo].[VW_Ldp_Unificada]
    WHERE [Project_ID_ACC] = @projectId";
    // CORREÇÃO: [dbo].[Ldp_Unificada] é a tabela bruta com o HISTÓRICO de todas as sincronizações (25k+
    // linhas no total, todas acumuladas) — lê-la direto trazia um volume muito maior que a LDP atual do
    // projeto. [dbo].[VW_Ldp_Unificada] (confirmado via sys.sql_modules: internamente ainda contém o
    // texto "CREATE VIEW [dbo].[VW_Ldp_Unificada_Ultima_Versao_TESTE]", sinal de que foi renomeada com
    // sp_rename sem atualizar o corpo do CREATE VIEW — o nome real e consultável no banco é
    // VW_Ldp_Unificada) já filtra por INNER JOIN em Projetos.Is_Ativo = 1 e por
    // ROW_NUMBER() PARTITION BY Project_ID_ACC, NA_Document_Number ORDER BY Data_Sincronizacao DESC,
    // ID_Registro DESC (rn = 1) — ou seja, só a última sincronização válida de cada documento, e só de
    // projetos ativos. Expõe as mesmas colunas usadas aqui (NA_Document_Number, Client_Document_Number,
    // Document_Title, Format, No_Pages, Emission_Date, Revision, Document_Status, Discipline,
    // Transmittal_Number, CR, CR_OS), então nenhuma outra parte desta consulta precisou mudar.

                var param = cmd.CreateParameter();
                param.ParameterName = "@projectId";
                param.Value = projectId;
                cmd.Parameters.Add(param);

                _logger.LogInformation(
                    "[ETAPA7][SQL][INICIO] ProjectId={ProjectId} | Fonte=VW_Ldp_Unificada | CommandTimeout={CommandTimeout}s",
                    projectId, etapa7CommandTimeoutSegundos);

                var swSql = Stopwatch.StartNew();

                try
                {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        // 3. Identifica os códigos e aplica a prioridade do NamingStandard do projeto.
                        // CORREÇÃO: antes, quando o padrão era NA e o NA_Document_Number vinha vazio, a
                        // linha era descartada logo aqui (keyValue ficava vazio e caía no "continue")
                        // mesmo com o Client_Document_Number preenchido — o fallback N&A -> Cliente feito
                        // na etapa de correspondência (SincronizarAtributosLdpPorPastaAsync) nunca chegava
                        // a rodar para essa linha. Agora só descartamos quando os DOIS códigos (primário e
                        // secundário, conforme o NamingStandard) estiverem vazios.
                        string naCode = reader["NA_Document_Number"]?.ToString()?.Trim();
                        string clientCode = reader["Client_Document_Number"]?.ToString()?.Trim();

                        bool padraoCliente = padraoNomenclatura.Equals("CLIENT", StringComparison.OrdinalIgnoreCase);
                        string codigoPrimario = padraoCliente ? clientCode : naCode;
                        string codigoSecundario = padraoCliente ? naCode : clientCode;
                        string tipoPrimario = padraoCliente ? "CLIENTE" : "N&A";
                        string tipoSecundario = padraoCliente ? "N&A" : "CLIENTE";

                        if (string.IsNullOrWhiteSpace(codigoPrimario) && string.IsNullOrWhiteSpace(codigoSecundario))
                        {
                            continue; // Só descarta a linha quando os dois códigos estão vazios
                        }

                        string keyValue = !string.IsNullOrWhiteSpace(codigoPrimario) ? codigoPrimario : codigoSecundario;

                        // Modulo: APS.WebApp.Services/LdpUpdateService.cs
// Dentro de SyncAccAttributesFromSqlAsync, substitua APENAS o Bloco 4

// ...
var rowData = new Dictionary<string, string>();
rowData["_DOC_NUMBER_KEY_"] = keyValue;

// Guardados separadamente (além dos NADOCUMENTNUMBER/CLIENTDOCUMENTNUMBER de sempre) para que a etapa
// de correspondência (SincronizarAtributosLdpPorPastaAsync) saiba qual código tentar primeiro e qual
// usar como fallback, respeitando o NamingStandard do projeto em vez de sempre tentar o N&A.
rowData["_DOC_NUMBER_PRIMARY_"] = codigoPrimario;
rowData["_DOC_NUMBER_SECONDARY_"] = codigoSecundario;
rowData["_DOC_NUMBER_PRIMARY_TYPE_"] = tipoPrimario;
rowData["_DOC_NUMBER_SECONDARY_TYPE_"] = tipoSecundario;

// 4. Mapeamento De/Para Inteligente
// As chaves aqui são EXATAMENTE a versão limpa (sem espaço/ponto) do que está no print do ACC
rowData["NADOCUMENTNUMBER"] = reader["NA_Document_Number"]?.ToString();
rowData["CLIENTDOCUMENTNUMBER"] = reader["Client_Document_Number"]?.ToString();
rowData["DOCUMENTTITLE"] = reader["Document_Title"]?.ToString();

// Tratamento específico para a Revisão ("REV." no ACC)
string rev = reader["Revision"]?.ToString();
rowData["REV"] = rev;

// Status (Baseado no seu padrão de projeto, mesmo não aparecendo na imagem, mantemos flexível)
string status = reader["Document_Status"]?.ToString();
rowData["DOCUMENTSTATUS"] = status;
rowData["STATUS"] = status; 

// Atributos secundários
rowData["DISCIPLINE"] = reader["Discipline"]?.ToString();
rowData["FORMAT"] = reader["Format"]?.ToString();
rowData["NOPAGES"] = reader["No_Pages"]?.ToString();

// Cuidado com a grafia que está configurada no ACC (TRANMISTAL vs TRANSMITTAL)
rowData["TRANMISTALNUMBER"] = reader["Transmittal_Number"]?.ToString(); // Nome exato limpo do ACC
rowData["TRANSMITTALNUMBER"] = reader["Transmittal_Number"]?.ToString(); // Duplicado caso você arrume no ACC

// Cuidado com a grafia (EMISSION DATA vs EMISSION DATE)
if (reader["Emission_Date"] != DBNull.Value)
{
    string dateStr = Convert.ToDateTime(reader["Emission_Date"]).ToString("yyyy-MM-dd");
    rowData["EMISSIONDATA"] = dateStr; // Nome exato limpo do ACC
    rowData["EMISSIONDATE"] = dateStr; // Duplicado caso você arrume no ACC
}

ldpData.Add(rowData);
// ...
                    }
                }

                swSql.Stop();
                _logger.LogInformation(
                    "[ETAPA7][SQL][FIM] ProjectId={ProjectId} | DuracaoMs={DuracaoMs} | RegistrosLidos={RegistrosLidos}",
                    projectId, swSql.ElapsedMilliseconds, ldpData.Count);
                }
                catch (Exception exSql) when (EhErroDeTimeoutSql(exSql))
                {
                    swSql.Stop();
                    _logger.LogWarning(
                        "[ETAPA7][SQL][TIMEOUT] ProjectId={ProjectId} | DuracaoMs={DuracaoMs} | CommandTimeout={CommandTimeout}s",
                        projectId, swSql.ElapsedMilliseconds, etapa7CommandTimeoutSegundos);
                    throw;
                }
            }
        }

        if (ldpData.Count == 0) return "⚠️ Nenhum dado encontrado na tabela Ldp_Unificada para este projeto.";

        _logger.LogInformation($"[Sync] {ldpData.Count} registros extraídos do SQL Server. Preparando montagem de Payload para o ACC...");

        // 5. Sincronização por pasta (Engenharia x Produção), usando o dicionário PastasAtributos para
        // decidir quais dos seis atributos personalizados cada arquivo deve receber, localizando cada
        // arquivo pela pasta real onde ele está no ACC — não mais só pelo nome (isso evita misturar
        // documentos duplicados que existem em pastas diferentes). Substitui, apenas neste fluxo, a
        // chamada antiga a AutodeskApiService.SyncLdpAttributesAsync, que continua existindo e sendo
        // usada sem nenhuma alteração por WebhooksController e por DataController.
        string resumoSync = await SincronizarAtributosLdpPorPastaAsync(hubId, projectId, ldpData, token);
        return resumoSync;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Erro Crítico na Etapa 7 (Sincronização via SQL Unificada).");
        return $"Erro: {ex.Message}";
    }
}

// Módulo: APS.WebApp.Services/LdpUpdateService.cs
// Sincronização de atributos personalizados da LDP com o ACC, separando Engenharia e Produção
// via o dicionário PastasAtributos. Usada exclusivamente por SyncAccAttributesFromSqlAsync.
private async Task<string> SincronizarAtributosLdpPorPastaAsync(string hubId, string projectId, List<Dictionary<string, string>> ldpData, string accessToken)
{
    var stats = new SincronizacaoAtributosStats { RegistrosLidosDaLdp = ldpData.Count };
    _logger.LogInformation($"[Sync Atributos] {stats.RegistrosLidosDaLdp} registro(s) lido(s) da Ldp_Unificada.");

    // AttributeSync:ForceOverwriteAll (padrão false): lido UMA ÚNICA VEZ aqui e propagado pelos helpers
    // DeveHonrarCacheHit/EhReenvioForcado abaixo — nunca relido dentro do loop de arquivos. Não altera
    // localização de pastas, matching, montagem de payload, deduplicação por VersionUrn ou prioridade
    // CLIENT/N&A: o único efeito é ignorar um CACHE_HIT (ver bloco de decisão de cache mais abaixo).
    bool forceOverwriteAll = LerForceOverwriteAll(_configuration);
    _logger.LogInformation("[ATRIBUTOS][CONFIG] ForceOverwriteAll={ForceOverwriteAll}", forceOverwriteAll);

    // 0. Cache persistente (dbo.ACC_Attribute_Sync_State): carregado UMA ÚNICA VEZ para todo o projeto
    // (nunca um SELECT por arquivo). Se a consulta falhar, a sincronização NÃO é interrompida — apenas
    // segue sem cache, reenviando normalmente à Autodesk (o cache é uma otimização, não um requisito
    // para a função principal).
    Dictionary<string, AccAttributeSyncState> estadosSincronizacao;
    try
    {
        estadosSincronizacao = _dbService.ObterEstadosSincronizacaoAtributos(projectId);
        stats.EstadosCacheCarregados = estadosSincronizacao.Count;
        _logger.LogInformation($"[Sync Atributos] Estados carregados do cache SQL: {estadosSincronizacao.Count} | Projeto: {projectId}");

        if (estadosSincronizacao.Count > 0)
        {
            var porChaveConfiguracao = estadosSincronizacao.Values
                .GroupBy(e => e.ChaveConfiguracao, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}:{g.Count()}");
            int sucessosAnteriores = estadosSincronizacao.Values.Count(e => e.UltimaTentativaSucesso);
            int falhasAnteriores = estadosSincronizacao.Values.Count(e => !e.UltimaTentativaSucesso);
            int versoesDistintas = estadosSincronizacao.Values.Select(e => e.VersionUrn).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            DateTime dataMaisRecente = estadosSincronizacao.Values.Max(e => e.DataUltimaTentativa);

            _logger.LogInformation(
                "[ATRIBUTOS][CACHE][CARGA] Project_ID_ACC={ProjectId} | EstadosCarregados={Total} | PorChaveConfiguracao=[{PorChave}] | " +
                "SucessosAnteriores={Sucessos} | FalhasAnteriores={Falhas} | VersoesDistintas={Versoes} | DataMaisRecente={DataMaisRecente:yyyy-MM-dd HH:mm:ss}",
                projectId, estadosSincronizacao.Count, string.Join(", ", porChaveConfiguracao), sucessosAnteriores, falhasAnteriores, versoesDistintas, dataMaisRecente);
        }
        else
        {
            _logger.LogInformation("[ATRIBUTOS][CACHE][CARGA] Project_ID_ACC={ProjectId} | EstadosCarregados=0 | (nenhum histórico de cache para este projeto)", projectId);
        }
    }
    catch (Exception exCache)
    {
        estadosSincronizacao = new Dictionary<string, AccAttributeSyncState>(StringComparer.OrdinalIgnoreCase);
        _logger.LogError(exCache, "[Sync Atributos] Falha ao carregar o cache de sincronização (ACC_Attribute_Sync_State). Prosseguindo SEM cache nesta execução — todos os arquivos elegíveis serão reenviados normalmente à Autodesk.");
    }

    // 1. Definições de atributos personalizados do projeto — carregadas UMA ÚNICA VEZ na pasta RAIZ (seção
    // 13.1: preservada como primeira fonte) e combinadas por arquivo com as definições da PASTA ESPECÍFICA
    // (disciplina) onde ele foi encontrado, via ObterDefinicoesEfetivasAsync (cache em memória por
    // FolderId — nunca um GET por arquivo). A resolução tolerante (nome exato -> normalizado -> aliases
    // controlados) acontece em ResolverIdAtributo, dentro do loop de arquivos, usando as definições
    // efetivas de CADA arquivo (arquivo.FolderId) — não mais um único dicionário global por nome exato.
    var rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, projectId, accessToken);
    if (!rootFolder.HasValue)
    {
        _logger.LogError("[Sync Atributos] Pasta raiz do projeto não encontrada no ACC. Sincronização de atributos por pasta abortada.");
        return "Erro: não foi possível localizar a pasta raiz do projeto no ACC.";
    }
    string rootFolderId = rootFolder.Value.GetProperty("id").GetString();

    var definicoesRaiz = await _autodeskApi.GetFolderAttributeDefinitionsAsync(projectId, rootFolderId, accessToken);
    var cacheDefinicoesPorPasta = new Dictionary<string, DefinicoesEfetivasPasta>(StringComparer.OrdinalIgnoreCase);

    var nomesNormalizadosDaRaiz = new HashSet<string>(
        definicoesRaiz.Where(d => !string.IsNullOrWhiteSpace(d.Name)).Select(d => NormalizarNomeAtributo(d.Name)),
        StringComparer.OrdinalIgnoreCase);

    foreach (var nomeEsperado in new[] { ATTR_DOCUMENT_TITLE, ATTR_CLIENT_DOCUMENT_NUMBER, ATTR_NA_DOCUMENT_NUMBER, ATTR_REV, ATTR_TRANSMITAL_NUMBER, ATTR_EMISSION_DATA })
    {
        if (!nomesNormalizadosDaRaiz.Contains(NormalizarNomeAtributo(nomeEsperado)))
        {
            _logger.LogInformation($"[Sync Atributos] Atributo personalizado '{nomeEsperado}' não foi encontrado (nem por nome nem por variação tolerante) nas definições da pasta RAIZ — será tentado novamente por pasta específica antes de ser considerado não resolvido.");
        }
    }

    // 2. Classifica os arquivos do ACC por configuração (ENGENHARIA_04 / PRODUCAO_10 / PRODUCAO_MODELOS_3D)
    // usando o dicionário PastasAtributos. O resultado é indexado por Chave (nunca por Grupo), porque
    // PRODUCAO_10 e PRODUCAO_MODELOS_3D compartilham o mesmo Grupo ("Produção") e se sobrescreveriam se
    // o dicionário fosse indexado por Grupo.
    var arquivosPorConfiguracao = await ClassificarArquivosAccPorPastaAsync(hubId, projectId, accessToken, stats);

    // Os contadores de Engenharia/Produção somam todas as configurações daquele grupo — Produção soma
    // PRODUCAO_10 + PRODUCAO_MODELOS_3D; um valor nunca substitui o outro.
    stats.ArquivosEncontradosEngenharia = PastasAtributos
        .Where(config => config.Grupo == GRUPO_ENGENHARIA)
        .Sum(config => arquivosPorConfiguracao.TryGetValue(config.Chave, out var lista) ? lista.Count : 0);
    stats.ArquivosEncontradosProducao = PastasAtributos
        .Where(config => config.Grupo == GRUPO_PRODUCAO)
        .Sum(config => arquivosPorConfiguracao.TryGetValue(config.Chave, out var lista) ? lista.Count : 0);

    _logger.LogInformation($"[Sync Atributos] Arquivos indexados no ACC — Engenharia: {stats.ArquivosEncontradosEngenharia} | Produção: {stats.ArquivosEncontradosProducao} | Fora do dicionário: {stats.ArquivosIgnoradosForaDoDicionario} arquivo(s) solto(s) na pasta principal + {stats.PastasIgnoradasForaDoDicionario} subpasta(s) não mapeada(s).");

    var documentosEncontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Deduplicação por VersionUrn NO NÍVEL DA EXECUÇÃO INTEIRA: guarda, para cada VersionUrn já
    // atualizada com sucesso, qual grupo/configuração fez essa atualização. É compartilhado por TODOS
    // os grupos (Engenharia e Produção) percorridos abaixo, então se a mesma VersionUrn for encontrada
    // por dois grupos diferentes (ou duas vezes dentro do mesmo grupo), o arquivo físico só é enviado ao
    // ACC uma única vez nesta execução. Deduplicar é sempre por VersionUrn — nunca por nome de arquivo,
    // então "DOC-001.rvt" e "DOC-001.pdf" (URNs diferentes) continuam sendo atualizados normalmente.
    // A URN só é registrada aqui DEPOIS de uma chamada bem-sucedida ao ACC (ver dentro do loop de
    // arquivosCorrespondentes) — se a primeira tentativa de atualização falhar, a URN não fica marcada e
    // uma ocorrência posterior da mesma URN pode tentar novamente.
    var versionUrnAtualizadaPorConfiguracao = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // 3. Para cada configuração (Chave), monta um índice Nome-base (sem extensão) -> arquivos, e procura
    // correspondência da LDP DENTRO daquela configuração. Isso garante que o mesmo documento em
    // Engenharia e em Produção seja tratado individualmente (cada um recebe só os atributos do seu
    // próprio grupo), e que PRODUCAO_10 e PRODUCAO_MODELOS_3D — embora compartilhem o Grupo "Produção" —
    // sejam processados como configurações independentes.
    foreach (var grupoConfig in PastasAtributos)
    {
        string grupo = grupoConfig.Grupo;
        if (!arquivosPorConfiguracao.TryGetValue(grupoConfig.Chave, out var arquivosDoGrupo) || arquivosDoGrupo.Count == 0)
        {
            _logger.LogWarning($"[Sync Atributos] Nenhum arquivo encontrado no ACC para a configuração '{grupoConfig.Chave}' (grupo '{grupo}'). Nenhuma atualização será feita para esta configuração.");
            continue;
        }

        // Agrupado por nome-base (sem extensão): arquivos com o mesmo nome-base e extensões diferentes
        // (ex.: DOC-001.rvt e DOC-001.pdf, VersionUrn distintas) caem na mesma lista e são TODOS
        // atualizados abaixo — nunca se escolhe apenas uma extensão (sem First()/FirstOrDefault() aqui).
        var indicePorNome = arquivosDoGrupo
            .GroupBy(a => a.FileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Seção 22: todo arquivo ativo desta configuração que NUNCA aparecer em arquivosCorrespondentes de
        // nenhuma linha da LDP (nem por código primário, nem por fallback) é registrado como
        // SEM_CORRESPONDENCIA_SQL ao final deste grupoConfig — nunca fica sem estado final.
        var versionUrnsTocadasNestaConfiguracao = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ldpData)
        {
            if (!row.TryGetValue("_DOC_NUMBER_KEY_", out var docCode) || string.IsNullOrWhiteSpace(docCode)) continue;

            // Códigos e tipos definidos na Etapa 7 (SyncAccAttributesFromSqlAsync) conforme o
            // NamingStandard do projeto — primário é o que o padrão do projeto prioriza, secundário é o
            // outro. Um dos dois pode estar vazio (mas não os dois, isso já foi filtrado antes).
            row.TryGetValue("_DOC_NUMBER_PRIMARY_", out var codigoPrimario);
            row.TryGetValue("_DOC_NUMBER_SECONDARY_", out var codigoSecundario);
            row.TryGetValue("_DOC_NUMBER_PRIMARY_TYPE_", out var tipoPrimario);
            row.TryGetValue("_DOC_NUMBER_SECONDARY_TYPE_", out var tipoSecundario);

            bool primarioPreenchido = !string.IsNullOrWhiteSpace(codigoPrimario);
            bool secundarioPreenchido = !string.IsNullOrWhiteSpace(codigoSecundario);

            // Só tenta o código secundário se ele existir e não for equivalente (após normalização) ao
            // primário — DOC-001 / DOC_001 / DOC 001 são o mesmo código para o DocumentMatchingService,
            // então uma segunda tentativa com um código só diferente na grafia seria trabalho repetido.
            bool codigosEquivalentes = primarioPreenchido && secundarioPreenchido &&
                string.Equals(NormalizeKeyOnly(codigoPrimario), NormalizeKeyOnly(codigoSecundario), StringComparison.OrdinalIgnoreCase);
            string codigoSecundarioParaBusca = (secundarioPreenchido && !codigosEquivalentes) ? codigoSecundario : null;

            _logger.LogDebug(
                "[ATRIBUTOS][MATCH][INICIO] Configuracao={Configuracao} | Grupo={Grupo} | CodigoPrimario={CodigoPrimario} | CodigoSecundario={CodigoSecundario}",
                grupoConfig.Chave, grupo, primarioPreenchido ? codigoPrimario : "(vazio)", secundarioPreenchido ? codigoSecundario : "(vazio)");

            // 1ª tentativa: só roda se o código primário (definido pelo NamingStandard) estiver preenchido.
            // EncontrarDocumentosNoAccPorPrefixoDeTokens (nunca StartsWith cru) devolve TODOS os nomes-base
            // que correspondem por prefixo de tokens — não só o primeiro — porque podem existir vários
            // arquivos físicos com sufixos diferentes para o mesmo documento lógico (ex.: "...-0055-01" e
            // "...-0055-02" na mesma pasta), e todos precisam receber os atributos.
            List<string> nomesBaseCorrespondentes = new List<string>();
            bool primeiraTentativaExecutada = primarioPreenchido;
            bool primeiraTentativaOk = false;
            if (primeiraTentativaExecutada)
            {
                var candidatosPrimario = _documentMatching.EncontrarDocumentosNoAccPorPrefixoDeTokens(codigoPrimario, indicePorNome.Keys);
                _logger.LogDebug(
                    "[ATRIBUTOS][MATCH][CANDIDATOS] CodigoPesquisado={Codigo} | CandidatosFisicos={Candidatos}",
                    codigoPrimario, candidatosPrimario.Count == 0 ? "(nenhum)" : string.Join(", ", candidatosPrimario));

                if (candidatosPrimario.Count > 0)
                {
                    nomesBaseCorrespondentes = candidatosPrimario.ToList();
                    primeiraTentativaOk = true;
                }
            }

            // Fallback: só roda se a 1ª tentativa falhou (ou não existia) e há um código secundário
            // utilizável — é isso que garante o fallback N&A -> Cliente (ou Cliente -> N&A) mesmo quando
            // o código primário já não está vazio, mas simplesmente não bateu com nenhum arquivo do ACC.
            // Não depende da coluna de revisão em nenhum dos dois casos.
            bool fallbackExecutado = false;
            bool fallbackOk = false;
            if (!primeiraTentativaOk && codigoSecundarioParaBusca != null)
            {
                fallbackExecutado = true;
                var candidatosFallback = _documentMatching.EncontrarDocumentosNoAccPorPrefixoDeTokens(codigoSecundarioParaBusca, indicePorNome.Keys);
                _logger.LogDebug(
                    "[ATRIBUTOS][MATCH][CANDIDATOS] CodigoPesquisado={Codigo} | CandidatosFisicos={Candidatos}",
                    codigoSecundarioParaBusca, candidatosFallback.Count == 0 ? "(nenhum)" : string.Join(", ", candidatosFallback));

                if (candidatosFallback.Count > 0)
                {
                    nomesBaseCorrespondentes = candidatosFallback.ToList();
                    fallbackOk = true;
                }
            }

            string codigoVencedor = primeiraTentativaOk ? tipoPrimario : (fallbackOk ? tipoSecundario : null);
            string codigoPesquisadoVencedor = primeiraTentativaOk ? codigoPrimario : (fallbackOk ? codigoSecundarioParaBusca : null);

            _logger.LogDebug(
                $"[Sync Atributos] Grupo: {grupo} | Padrão primário: {tipoPrimario} | " +
                $"Primário ({tipoPrimario}): {(primarioPreenchido ? codigoPrimario : "vazio")} | " +
                $"Secundário ({tipoSecundario}): {(secundarioPreenchido ? codigoSecundario : "vazio")} | " +
                $"1ª tentativa executada: {(primeiraTentativaExecutada ? "sim" : "não")} | " +
                $"Resultado 1ª tentativa: {(primeiraTentativaExecutada ? (primeiraTentativaOk ? "encontrado" : "não encontrado") : "n/a")} | " +
                $"Fallback executado: {(fallbackExecutado ? "sim" : "não")} | " +
                $"Resultado fallback: {(fallbackExecutado ? (fallbackOk ? "encontrado" : "não encontrado") : "n/a")} | " +
                $"Código vencedor: {codigoVencedor ?? "nenhum"}");

            var arquivosCorrespondentes = nomesBaseCorrespondentes
                .Where(nomeBase => indicePorNome.ContainsKey(nomeBase))
                .SelectMany(nomeBase => indicePorNome[nomeBase])
                .ToList();

            if (nomesBaseCorrespondentes.Count > 0)
            {
                _logger.LogInformation(
                    "[ATRIBUTOS][MATCH][RESULTADO] CodigoPesquisado={Codigo} | ArquivosCorrespondentes={NomesBase} | TipoCorrespondencia={Tipo} | CodigoVencedor={Vencedor} | QuantidadeArquivosFisicos={QuantidadeFisicos}",
                    codigoPesquisadoVencedor, string.Join(", ", nomesBaseCorrespondentes), primeiraTentativaOk ? "PRIMARIO" : "FALLBACK", codigoVencedor ?? "nenhum", arquivosCorrespondentes.Count);
            }

            if (arquivosCorrespondentes.Count == 0)
            {
                continue; // Não encontrado NESTE grupo — pode existir no outro grupo, ou em nenhum (checado depois)
            }

            // Um log por arquivo físico já classificado (pasta + configuração conhecidas nesta altura),
            // independente da montagem do payload logo abaixo — confirma no log, sem suposição, onde
            // exatamente cada documento da LDP foi localizado no ACC.
            foreach (var arquivoLocalizado in arquivosCorrespondentes)
            {
                _logger.LogInformation(
                    "[ATRIBUTOS][ARQUIVO][LOCALIZADO] DocumentoLdp={DocumentoLdp} | Arquivo={Arquivo} | Configuracao={Configuracao} | Pasta={Pasta} | Extensao={Extensao}",
                    docCode.Trim(), arquivoLocalizado.FileName, arquivoLocalizado.ChaveConfiguracao, arquivoLocalizado.PastaEncontrada, arquivoLocalizado.Extension);
            }

            stats.ArquivosCorrespondentes += arquivosCorrespondentes.Count;

            // A linha é marcada como encontrada usando a MESMA chave verificada no loop de "não
            // encontrados" logo abaixo (_DOC_NUMBER_KEY_) — independentemente de a correspondência ter
            // vindo do código primário ou do fallback secundário, então uma linha achada pelo código do
            // cliente nunca aparece depois como "não encontrada" só porque a chave guardava o número N&A.
            documentosEncontrados.Add(docCode.Trim());

            foreach (var arquivo in arquivosCorrespondentes)
            {
                versionUrnsTocadasNestaConfiguracao.Add(arquivo.VersionUrn);
                var reconciliacao = stats.ObterOuCriarReconciliacao(grupoConfig.Chave, arquivo.PastaEncontrada);
                reconciliacao.ArquivosComCorrespondenciaSql++;

                // Proteção DEFENSIVA final (Fase B, 17/09/2026): independentemente de QUAL configuração
                // descobriu este arquivo (mesmo que uma alteração futura de discovery, alias ou
                // sobreposição de root faça um arquivo da árvore "09 - ENGINEERING DOCUMENTS" aparecer
                // aqui), nunca envia POST de atributos para ele — nunca confia só em ter removido
                // PRODUCAO_DOCUMENTOS_09 da descoberta (ver ClassificarArquivosAccPorPastaAsync). Conta
                // como ArquivosSemPayload (matched, mas nada enviado) — mantém a mesma identidade que a
                // reconciliação [ETAPA7][RECONCILIACAO] já valida em outro lugar.
                if (PastaPertenceARaizBloqueadaParaAtributos(arquivo.PastaEncontrada))
                {
                    stats.ArquivosSemPayload++;
                    reconciliacao.ArquivosSemPayload++;
                    _logger.LogInformation(
                        "[ETAPA7][ROOT] Arquivo={Arquivo} | Pasta={Pasta} | Configuracao={Configuracao} | Resultado=IGNORADO_POR_CONFIGURACAO | Motivo=ROOT_NAO_PERMITIDO_PARA_ATUALIZACAO_DE_ATRIBUTOS",
                        arquivo.FileName, arquivo.PastaEncontrada, grupoConfig.Chave);
                    continue;
                }

                _logger.LogInformation(
                    "[ETAPA7][MATCH][RESULTADO] Arquivo={Arquivo} | CodigoPrimario={CodigoPrimario} | CodigoFallback={CodigoFallback} | CodigoVencedor={CodigoVencedor} | Resultado={Resultado}",
                    arquivo.FileName, primarioPreenchido ? codigoPrimario : "(vazio)", codigoSecundarioParaBusca ?? "(vazio)", codigoPesquisadoVencedor ?? "(nenhum)",
                    primeiraTentativaOk ? "ENCONTRADO_PRIMARIO" : (fallbackOk ? "ENCONTRADO_FALLBACK" : "NAO_ENCONTRADO"));

                // 1) Deduplicação em memória por VersionUrn (execução atual): se esta VersionUrn já foi
                // atualizada com sucesso nesta execução (por esta configuração ou por outra), não envia de
                // novo — o arquivo físico é o mesmo. Comparamos pela CHAVE da configuração (não pelo
                // Grupo), porque PRODUCAO_10 e PRODUCAO_MODELOS_3D compartilham o Grupo "Produção" mas são
                // configurações distintas — isso é o que permite diferenciar duplicidade "mesma
                // configuração" de "entre configurações". Isto é INDEPENDENTE do cache persistente: aqui
                // tratamos duplicidade dentro desta mesma execução; o cache persistente (abaixo) trata
                // payloads já sincronizados em execuções ANTERIORES.
                if (VersionUrnJaAtualizadaNestaExecucao(versionUrnAtualizadaPorConfiguracao, arquivo.VersionUrn, grupoConfig.Chave, out bool mesmaConfiguracao))
                {
                    if (mesmaConfiguracao) stats.ArquivosDuplicadosIgnoradosMesmaConfiguracao++;
                    else stats.ArquivosDuplicadosIgnoradosEntreConfiguracoes++;
                    reconciliacao.DuplicadosVersionUrn++;

                    versionUrnAtualizadaPorConfiguracao.TryGetValue(arquivo.VersionUrn, out var chaveConfigQueAtualizouAntes);
                    _logger.LogInformation(
                        $"⏭️ [Sync Atributos] Arquivo: '{arquivo.FileName}' | Extensão: '{arquivo.Extension}' | Pasta: '{arquivo.PastaEncontrada}' | Configuração: {grupoConfig.Chave} | Grupo: {grupo} | " +
                        $"VersionUrn: {ResumirUrn(arquivo.VersionUrn)} | Motivo: VersionUrn já atualizada com sucesso nesta execução " +
                        $"(configuração original: {chaveConfigQueAtualizouAntes}) | Resultado: ignorado por já ter sido atualizado com sucesso " +
                        (mesmaConfiguracao ? "(duplicidade dentro da mesma configuração)." : "(duplicidade entre configurações diferentes)."));
                    continue;
                }

                // Resolução das definições EFETIVAS de atributos da pasta REAL deste arquivo (raiz + pasta
                // específica combinadas, cache em memória por FolderId — seção 13). Payload montado POR
                // ARQUIVO (não mais uma vez por docCode/configuração compartilhada entre extensões), porque
                // dois arquivos do mesmo docCode podem estar em pastas diferentes com definições distintas.
                var definicoesEfetivas = await ObterDefinicoesEfetivasAsync(
                    projectId, arquivo.FolderId, rootFolderId, definicoesRaiz, accessToken, cacheDefinicoesPorPasta, arquivo.PastaEncontrada);

                var comValorSql = new List<string>();
                var resolvidosNomes = new List<string>();
                var naoResolvidos = new List<string>();
                var customAttributesForFile = new List<object>();

                foreach (var nomeAtributo in grupoConfig.Atributos)
                {
                    string valor = ObterValorAtributoDaLinha(row, nomeAtributo);
                    // Atributo vazio ou placeholder ("-", "N/A") não sobrescreve valor existente — vale para
                    // os dois modos de ForceOverwriteAll: a flag reescreve valores REAIS já existentes no
                    // SQL, nunca apaga um atributo do ACC só porque o campo da LDP está em branco.
                    if (EhValorVazioOuPlaceholder(valor)) continue;
                    comValorSql.Add(nomeAtributo);

                    int? attributeId = ResolverIdAtributo(nomeAtributo, definicoesEfetivas, arquivo.PastaEncontrada, _logger);
                    if (attributeId == null)
                    {
                        naoResolvidos.Add(nomeAtributo);
                        continue;
                    }

                    resolvidosNomes.Add(nomeAtributo);
                    customAttributesForFile.Add(new { id = attributeId.Value, value = valor.Trim() });
                }

                _logger.LogInformation(
                    "[ETAPA7][ATRIBUTOS][RESOLUCAO] Arquivo={Arquivo} | Pasta={Pasta} | Configurados={Configurados} | ComValorSql={ComValorSql} | Resolvidos={Resolvidos} | NaoResolvidos={NaoResolvidos}",
                    arquivo.FileName, arquivo.PastaEncontrada, string.Join(", ", grupoConfig.Atributos),
                    comValorSql.Count == 0 ? "(nenhum)" : string.Join(", ", comValorSql),
                    resolvidosNomes.Count == 0 ? "(nenhum)" : string.Join(", ", resolvidosNomes),
                    naoResolvidos.Count == 0 ? "(nenhum)" : string.Join(", ", naoResolvidos));

                _logger.LogInformation(
                    "[ETAPA7][PAYLOAD] Arquivo={Arquivo} | AtributosEnviados={AtributosEnviados} | Quantidade={Quantidade}",
                    arquivo.FileName, resolvidosNomes.Count == 0 ? "(nenhum)" : string.Join(", ", resolvidosNomes), customAttributesForFile.Count);

                if (!customAttributesForFile.Any())
                {
                    stats.ArquivosSemPayload++;
                    reconciliacao.ArquivosSemPayload++;
                    _logger.LogWarning($"[Sync Atributos] '{arquivo.FileName}' ({grupo}, pasta: {arquivo.PastaEncontrada}) não teve nenhum atributo válido para enviar (configurados: {grupoConfig.Atributos.Count}, com valor no SQL: {comValorSql.Count}, resolvidos: {resolvidosNomes.Count}, não resolvidos: {naoResolvidos.Count}).");
                    continue;
                }

                reconciliacao.ArquivosComPayload++;

                // 2)/3) Payload real (agora resolvido POR ARQUIVO, usando as definições efetivas da pasta
                // real dele) e o hash determinístico desse payload — muda naturalmente quando a correção
                // passa de 2 para 6 atributos resolvidos (seção 20), permitindo reenvio mesmo com
                // ForceOverwriteAll=false.
                string payloadHash = CalcularPayloadHash(customAttributesForFile);
                string chaveEstado = MontarChaveEstado(arquivo.VersionUrn, grupoConfig.Chave);

                _logger.LogDebug(
                    "[ATRIBUTOS][PAYLOAD] Arquivo={Arquivo} | VersionUrn={VersionUrn} | AtributosPreparados={Atributos} | Quantidade={Quantidade}",
                    arquivo.FileName, ResumirUrn(arquivo.VersionUrn), string.Join(", ", comValorSql), customAttributesForFile.Count);

                // 4)/5) Cache persistente: só evita a chamada à Autodesk quando a ÚLTIMA tentativa desta
                // MESMA VersionUrn + Configuração já teve sucesso com o MESMO payload. Uma VersionUrn nova
                // (nova versão física do arquivo) nunca reaproveita o sucesso de outra VersionUrn, porque a
                // chave inclui o VersionUrn inteiro.
                estadosSincronizacao.TryGetValue(chaveEstado, out var estadoAnterior);

                bool cacheHit = estadoAnterior != null &&
                    estadoAnterior.UltimaTentativaSucesso &&
                    string.Equals(estadoAnterior.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase);

                string motivoCache = DeterminarMotivoDecisaoCache(estadoAnterior, cacheHit, arquivo.VersionUrn, grupoConfig.Chave, arquivo.FileName, estadosSincronizacao);
                switch (motivoCache)
                {
                    case "CACHE_MISS_SEM_HISTORICO": stats.CacheMissSemHistorico++; break;
                    case "CACHE_MISS_NOVA_VERSAO": stats.CacheMissNovaVersao++; break;
                    case "CACHE_MISS_PAYLOAD_ALTERADO": stats.CacheMissPayloadAlterado++; break;
                    case "CACHE_MISS_FALHA_ANTERIOR": stats.CacheMissFalhaAnterior++; break;
                    case "CACHE_MISS_CHAVE_CONFIGURACAO_DIFERENTE": stats.CacheMissChaveConfiguracaoDiferente++; break;
                    case "CACHE_MISS_ESTADO_INCOMPLETO": stats.CacheMissEstadoIncompleto++; break;
                }

                _logger.LogDebug(
                    "[ATRIBUTOS][CACHE][DECISAO] Documento={Documento} | Arquivo={Arquivo} | Extensao={Extensao} | VersionUrn={VersionUrn} | ChaveConfiguracao={ChaveConfiguracao} | " +
                    "EstadoAnteriorEncontrado={EstadoAnterior} | PayloadHashAtual={HashAtual} | PayloadHashAnterior={HashAnterior} | Decisao={Decisao} | Motivo={Motivo}",
                    docCode, arquivo.FileName, arquivo.Extension, ResumirUrn(arquivo.VersionUrn), grupoConfig.Chave,
                    estadoAnterior != null, ResumirHash(payloadHash), ResumirHash(estadoAnterior?.PayloadHash),
                    cacheHit ? "CACHE_HIT" : "CACHE_MISS", motivoCache);

                _logger.LogInformation(
                    "[ETAPA7][CACHE][DECISAO] Arquivo={Arquivo} | Configuracao={Configuracao} | Decisao={Decisao} | Motivo={Motivo} | ForceOverwriteAll={ForceOverwriteAll}",
                    arquivo.FileName, grupoConfig.Chave, cacheHit ? "CACHE_HIT" : "CACHE_MISS", motivoCache, forceOverwriteAll);

                // ForceOverwriteAll=false (padrão): comportamento 100% inalterado — CACHE_HIT continua
                // pulando o POST. ForceOverwriteAll=true: o cache acima já rodou (só para diagnóstico/log,
                // motivoCache continua "CACHE_HIT_PAYLOAD_IDENTICO"), mas aqui a decisão de PULAR é
                // ignorada — o arquivo cai no mesmo caminho de POST usado por um CACHE_MISS normal.
                if (DeveHonrarCacheHit(cacheHit, forceOverwriteAll))
                {
                    stats.ArquivosSemAlteracao++;
                    stats.AtualizacoesEvitadasPorCache++;
                    stats.CacheHitsNormais++;
                    reconciliacao.CacheHits++;

                    _logger.LogDebug(
                        "[ATRIBUTOS][CACHE][DECISAO] Arquivo={Arquivo} | CacheHit/CacheMiss=CACHE_HIT | Motivo={Motivo} | VersionUrnCache={VersionUrnCache} | DataUltimoSucesso={DataUltimoSucesso}",
                        arquivo.FileName, motivoCache, ResumirUrn(estadoAnterior?.VersionUrn), estadoAnterior?.DataUltimoSucesso);

                    // Considera esta VersionUrn tratada na execução atual, preservando a deduplicação
                    // global mesmo quando a chamada real ao ACC foi evitada pelo cache.
                    versionUrnAtualizadaPorConfiguracao[arquivo.VersionUrn] = grupoConfig.Chave;
                    continue;
                }

                bool reenvioForcado = EhReenvioForcado(cacheHit, forceOverwriteAll);
                if (reenvioForcado)
                {
                    stats.PostsForcados++;
                    reconciliacao.PostsForcados++;
                    _logger.LogInformation(
                        "[ATRIBUTOS][CACHE][FORCE_OVERWRITE] Arquivo={Arquivo} | Configuracao={Configuracao} | CacheAnterior={CacheAnterior} | Acao=POST_FORCADO",
                        arquivo.FileName, grupoConfig.Chave, motivoCache);
                }
                else
                {
                    stats.PostsNormais++;
                    reconciliacao.PostsNormais++;
                    _logger.LogDebug(
                        "[ATRIBUTOS][CACHE][DECISAO] Arquivo={Arquivo} | CacheHit/CacheMiss=CACHE_MISS | Motivo={Motivo}",
                        arquivo.FileName, motivoCache);
                }

                stats.AtualizacoesNecessarias++;
                string motivoAtualizacao = reenvioForcado
                    ? "AttributeSync:ForceOverwriteAll=true (cache indicava payload já sincronizado)"
                    : estadoAnterior == null
                        ? "sem histórico no cache"
                        : !estadoAnterior.UltimaTentativaSucesso
                            ? "tentativa anterior falhou"
                            : "payload alterado desde a última sincronização com sucesso";

                _logger.LogInformation(
                    "[ATRIBUTOS][POST][INICIO] Arquivo={Arquivo} | Configuracao={Configuracao} | QuantidadeAtributos={Quantidade} | ForceOverwriteAll={ForceOverwriteAll}",
                    arquivo.FileName, grupoConfig.Chave, customAttributesForFile.Count, forceOverwriteAll);

                try
                {
                    await _autodeskApi.UpdateCustomAttributesForVersionAsync(projectId, arquivo.VersionUrn, customAttributesForFile, accessToken);

                    // Só marca a URN como atualizada DEPOIS do sucesso: se esta chamada tivesse falhado,
                    // a URN continuaria livre para uma ocorrência posterior tentar de novo (ver catch abaixo).
                    versionUrnAtualizadaPorConfiguracao[arquivo.VersionUrn] = grupoConfig.Chave;

                    if (grupo == GRUPO_ENGENHARIA) stats.ArquivosAtualizadosEngenharia++;
                    else if (grupo == GRUPO_PRODUCAO) stats.ArquivosAtualizadosProducao++;
                    stats.PostsComSucesso++;
                    reconciliacao.PostsSucesso++;

                    // 7) Só registra UltimaTentativaSucesso = 1 depois que a Autodesk retornou sucesso.
                    var estadoAtualizado = new AccAttributeSyncState
                    {
                        ProjectIdAcc = projectId,
                        VersionUrn = arquivo.VersionUrn,
                        ChaveConfiguracao = grupoConfig.Chave,
                        PayloadHash = payloadHash,
                        FileName = arquivo.FileName,
                        PastaEncontrada = arquivo.PastaEncontrada,
                        UltimaTentativaSucesso = true,
                        DataUltimaTentativa = DateTime.UtcNow,
                        DataUltimoSucesso = DateTime.UtcNow,
                        UltimoErro = null
                    };

                    try
                    {
                        estadoAtualizado = _dbService.SalvarEstadoSincronizacaoAtributos(estadoAtualizado);
                        estadosSincronizacao[chaveEstado] = estadoAtualizado;
                        stats.EstadosCacheAtualizados++;
                        stats.EstadosGravadosComSucesso++;
                        _logger.LogDebug($"[Sync Atributos] Estado de sincronização atualizado no SQL para '{arquivo.FileName}' (Configuração: {grupoConfig.Chave}).");
                    }
                    catch (Exception exCacheGravacao)
                    {
                        // Falha ao GRAVAR o cache depois de um POST bem-sucedido: não trava a sincronização
                        // (os atributos já foram atualizados no ACC), mas NÃO considera o cache persistido —
                        // na próxima execução este arquivo pode ser reenviado, o que é seguro (idempotente).
                        stats.FalhasRegistradasNoCache++;
                        stats.EstadosGravadosComFalha++;
                        _logger.LogError(exCacheGravacao, $"[Sync Atributos] Falha CRÍTICA ao gravar o cache de sincronização para '{arquivo.FileName}' (Configuração: {grupoConfig.Chave}) — o POST foi bem-sucedido no ACC, mas o cache pode reenviar este arquivo na próxima execução.");
                    }

                    _logger.LogInformation($"✅ [Sync Atributos] Arquivo: '{arquivo.FileName}' | Extensão: '{arquivo.Extension}' | Pasta: '{arquivo.PastaEncontrada}' | Configuração: {grupoConfig.Chave} | Grupo: {grupo} | Código vencedor: {codigoVencedor ?? "nenhum"} | Motivo do envio: {motivoAtualizacao} | VersionUrn: {ResumirUrn(arquivo.VersionUrn)} | Atributos enviados: {customAttributesForFile.Count} | Resultado: sucesso.");
                    _logger.LogInformation(
                        "[ATRIBUTOS][POST] Arquivo={Arquivo} | Resultado=sucesso | AtributosEnviados={Quantidade}",
                        arquivo.FileName, customAttributesForFile.Count);
                    _logger.LogInformation(
                        "[ATRIBUTOS][POST][FIM] Arquivo={Arquivo} | Configuracao={Configuracao} | Resultado=SUCESSO | ForceOverwriteAll={ForceOverwriteAll}",
                        arquivo.FileName, grupoConfig.Chave, forceOverwriteAll);
                    _logger.LogInformation(
                        "[ETAPA7][POST][FIM] Arquivo={Arquivo} | Configuracao={Configuracao} | QuantidadeAtributos={QuantidadeAtributos} | Resultado={Resultado} | StatusHttp={StatusHttp}",
                        arquivo.FileName, grupoConfig.Chave, customAttributesForFile.Count, "SUCESSO", "(indisponivel)");
                }
                catch (Exception ex)
                {
                    stats.ErrosPorArquivo.Add($"{arquivo.FileName} ({grupoConfig.Chave}, {arquivo.PastaEncontrada}): {ex.Message}");
                    stats.ErrosAtualizacao++;
                    stats.PostsComFalha++;
                    reconciliacao.PostsFalha++;
                    _logger.LogError($"❌ [Sync Atributos] Arquivo: '{arquivo.FileName}' | Extensão: '{arquivo.Extension}' | Pasta: '{arquivo.PastaEncontrada}' | Configuração: {grupoConfig.Chave} | Grupo: {grupo} | Resultado: falha — {ex.Message}");
                    _logger.LogError(
                        "[ATRIBUTOS][POST] Arquivo={Arquivo} | Resultado=falha | AtributosEnviados={Quantidade}",
                        arquivo.FileName, customAttributesForFile.Count);
                    _logger.LogError(
                        "[ATRIBUTOS][POST][FIM] Arquivo={Arquivo} | Configuracao={Configuracao} | Resultado=FALHA | ForceOverwriteAll={ForceOverwriteAll}",
                        arquivo.FileName, grupoConfig.Chave, forceOverwriteAll);
                    _logger.LogError(
                        "[ETAPA7][POST][FIM] Arquivo={Arquivo} | Configuracao={Configuracao} | QuantidadeAtributos={QuantidadeAtributos} | Resultado={Resultado} | StatusHttp={StatusHttp}",
                        arquivo.FileName, grupoConfig.Chave, customAttributesForFile.Count, "FALHA", "(indisponivel)");

                    // 9) Registra a falha no cache (tentativa repetida na próxima execução mesmo com hash
                    // igual) — preserva DataUltimoSucesso anterior, se existir; nunca a preenche numa falha.
                    string erroLimitado = string.IsNullOrEmpty(ex.Message)
                        ? "Erro não informado."
                        : (ex.Message.Length <= 2000 ? ex.Message : ex.Message.Substring(0, 2000));

                    var estadoFalha = new AccAttributeSyncState
                    {
                        ProjectIdAcc = projectId,
                        VersionUrn = arquivo.VersionUrn,
                        ChaveConfiguracao = grupoConfig.Chave,
                        PayloadHash = payloadHash,
                        FileName = arquivo.FileName,
                        PastaEncontrada = arquivo.PastaEncontrada,
                        UltimaTentativaSucesso = false,
                        DataUltimaTentativa = DateTime.UtcNow,
                        DataUltimoSucesso = estadoAnterior?.DataUltimoSucesso,
                        UltimoErro = erroLimitado
                    };

                    try
                    {
                        estadoFalha = _dbService.SalvarEstadoSincronizacaoAtributos(estadoFalha);
                        estadosSincronizacao[chaveEstado] = estadoFalha;
                        stats.EstadosCacheAtualizados++;
                        stats.FalhasRegistradasNoCache++;
                        stats.EstadosGravadosComSucesso++;
                    }
                    catch (Exception exCacheGravacao)
                    {
                        stats.EstadosGravadosComFalha++;
                        _logger.LogError(exCacheGravacao, $"[Sync Atributos] Falha ao gravar o estado de FALHA no cache de sincronização para '{arquivo.FileName}' (Configuração: {grupoConfig.Chave}).");
                    }
                }
            }
        }

        // Seção 22: todo arquivo ativo de arquivosDoGrupo que NUNCA foi tocado por nenhuma linha da LDP
        // (nem por código primário, nem por fallback secundário, em NENHUMA linha) é um arquivo ativo do
        // ACC sem registro correspondente na LDP/SQL — recebe estado final explícito SEM_CORRESPONDENCIA_SQL,
        // nunca é tratado como falha de paginação nem inventa valores.
        foreach (var arquivoSemCorrespondencia in arquivosDoGrupo.Where(a => !versionUrnsTocadasNestaConfiguracao.Contains(a.VersionUrn)))
        {
            var reconciliacaoSemCorrespondencia = stats.ObterOuCriarReconciliacao(grupoConfig.Chave, arquivoSemCorrespondencia.PastaEncontrada);
            reconciliacaoSemCorrespondencia.ArquivosSemCorrespondenciaSql++;

            _logger.LogInformation(
                "[ETAPA7][MATCH][RESULTADO] Arquivo={Arquivo} | CodigoPrimario={CodigoPrimario} | CodigoFallback={CodigoFallback} | CodigoVencedor={CodigoVencedor} | Resultado={Resultado}",
                arquivoSemCorrespondencia.FileName, "(n/a)", "(n/a)", "(nenhum)", "SEM_CORRESPONDENCIA_SQL");
        }
    }

    // 4. Documentos da LDP que não foram encontrados em NENHUMA pasta permitida (nem Engenharia, nem Produção)
    foreach (var row in ldpData)
    {
        if (!row.TryGetValue("_DOC_NUMBER_KEY_", out var docCode) || string.IsNullOrWhiteSpace(docCode)) continue;
        string codeToSearch = docCode.Trim();
        if (!documentosEncontrados.Contains(codeToSearch))
        {
            stats.DocumentosNaoEncontrados.Add(codeToSearch);
        }
    }

    if (stats.DocumentosNaoEncontrados.Any())
    {
        _logger.LogWarning($"[Sync Atributos] {stats.DocumentosNaoEncontrados.Count} documento(s) da LDP não encontrados nas pastas permitidas (Engenharia/Produção). Exemplos:");
        foreach (var doc in stats.DocumentosNaoEncontrados.Take(10)) _logger.LogWarning($"  - {doc}");
    }

    if (stats.ErrosPorArquivo.Any())
    {
        _logger.LogWarning($"[Sync Atributos] {stats.ErrosPorArquivo.Count} erro(s) individuais durante a atualização de atributos.");
    }

    string resumo = $"Sincronização de atributos por pasta concluída. " +
        $"Engenharia: {stats.ArquivosAtualizadosEngenharia}/{stats.ArquivosEncontradosEngenharia} arquivo(s) atualizado(s). " +
        $"Produção: {stats.ArquivosAtualizadosProducao}/{stats.ArquivosEncontradosProducao} arquivo(s) atualizado(s). " +
        $"{stats.ArquivosIgnoradosForaDoDicionario + stats.PastasIgnoradasForaDoDicionario} item(ns) fora do dicionário ignorado(s). " +
        $"{stats.ArquivosCorrespondentes} arquivo(s) correspondente(s) encontrados na LDP. " +
        $"{stats.ArquivosDuplicadosIgnoradosMesmaConfiguracao} duplicado(s) ignorado(s) dentro da mesma configuração. " +
        $"{stats.ArquivosDuplicadosIgnoradosEntreConfiguracoes} duplicado(s) ignorado(s) entre configurações (mesma VersionUrn). " +
        $"{stats.DocumentosNaoEncontrados.Count} documento(s) da LDP não encontrados nas pastas permitidas. " +
        $"{stats.ErrosAtualizacao} erro(s) individuais. " +
        $"Cache: {stats.EstadosCacheCarregados} estado(s) carregado(s), {stats.ArquivosSemAlteracao} arquivo(s) sem alteração, " +
        $"{stats.AtualizacoesEvitadasPorCache} atualização(ões) evitada(s) pelo cache, {stats.AtualizacoesNecessarias} atualização(ões) necessária(s) (POST enviado), " +
        $"{stats.EstadosCacheAtualizados} estado(s) gravado(s)/atualizado(s) no SQL, {stats.FalhasRegistradasNoCache} falha(s) registrada(s) no cache. " +
        $"ForceOverwriteAll: {forceOverwriteAll} | CacheHitsNormais: {stats.CacheHitsNormais} | PostsForcados: {stats.PostsForcados} | PostsNormais: {stats.PostsNormais} | " +
        $"Sucessos: {stats.PostsComSucesso} | Falhas: {stats.PostsComFalha}.";

    _logger.LogInformation($"[Sync Atributos] RESUMO FINAL: {resumo}");

    // Seção 8: "arquivos encontrados; arquivos correspondentes; cache hits normais; posts forçados;
    // posts normais; sucessos; falhas" — resumo estruturado dedicado a ForceOverwriteAll (independente
    // do [ATRIBUTOS][CACHE][RESUMO] abaixo, que já existia antes desta funcionalidade).
    _logger.LogInformation(
        "[ATRIBUTOS][RESUMO] ForceOverwriteAll={ForceOverwriteAll} | ArquivosEncontrados={ArquivosEncontrados} | ArquivosCorrespondentes={ArquivosCorrespondentes} | " +
        "CacheHitsNormais={CacheHitsNormais} | PostsForcados={PostsForcados} | PostsNormais={PostsNormais} | Sucessos={Sucessos} | Falhas={Falhas}",
        forceOverwriteAll, stats.ArquivosEncontradosEngenharia + stats.ArquivosEncontradosProducao, stats.ArquivosCorrespondentes,
        stats.CacheHitsNormais, stats.PostsForcados, stats.PostsNormais, stats.PostsComSucesso, stats.PostsComFalha);

    // -------------------------------------------------------------------------------------------
    // [ATRIBUTOS][CACHE][RESUMO] — seção 8 da manutenção técnica. Identidade esperada:
    // ArquivosCorrespondentes (= arquivos físicos analisados) = AtualizacoesEvitadasPorCache +
    // AtualizacoesNecessarias + ArquivosSemPayload + duplicados ignorados nesta execução. Se não
    // fechar, é só um alerta de observabilidade (CACHE_COUNTER_MISMATCH) — não altera nenhum resultado.
    // -------------------------------------------------------------------------------------------
    int arquivosIgnoradosPorOutraRazao = stats.ArquivosDuplicadosIgnoradosMesmaConfiguracao + stats.ArquivosDuplicadosIgnoradosEntreConfiguracoes;
    int somaEsperada = stats.AtualizacoesEvitadasPorCache + stats.AtualizacoesNecessarias + stats.ArquivosSemPayload + arquivosIgnoradosPorOutraRazao;

    _logger.LogInformation(
        "[ATRIBUTOS][CACHE][RESUMO] EstadosCarregados={EstadosCarregados} | DocumentosAnalisados={DocumentosAnalisados} | ArquivosFisicosAnalisados={ArquivosFisicos} | " +
        "CacheHits={CacheHits} | AtualizacoesEvitadas={AtualizacoesEvitadas} | SemHistorico={SemHistorico} | NovasVersoes={NovasVersoes} | PayloadsAlterados={PayloadsAlterados} | " +
        "RetriesPorFalhaAnterior={RetriesFalha} | PostsNecessarios={PostsNecessarios} | PostsExecutados={PostsExecutados} | PostsComSucesso={PostsSucesso} | PostsComFalha={PostsFalha} | " +
        "EstadosGravadosComSucesso={EstadosGravSucesso} | EstadosGravadosComFalha={EstadosGravFalha}",
        stats.EstadosCacheCarregados, stats.RegistrosLidosDaLdp, stats.ArquivosCorrespondentes,
        stats.ArquivosSemAlteracao, stats.AtualizacoesEvitadasPorCache, stats.CacheMissSemHistorico, stats.CacheMissNovaVersao, stats.CacheMissPayloadAlterado,
        stats.CacheMissFalhaAnterior, stats.AtualizacoesNecessarias, stats.AtualizacoesNecessarias, stats.PostsComSucesso, stats.PostsComFalha,
        stats.EstadosGravadosComSucesso, stats.EstadosGravadosComFalha);

    if (somaEsperada != stats.ArquivosCorrespondentes)
    {
        _logger.LogWarning(
            "[ATRIBUTOS][CACHE][RESUMO] CACHE_COUNTER_MISMATCH | ArquivosFisicosAnalisados={ArquivosFisicos} != AtualizacoesEvitadas({AtualizacoesEvitadas}) + AtualizacoesNecessarias({AtualizacoesNecessarias}) + ArquivosSemPayload({SemPayload}) + IgnoradosPorDuplicidade({Duplicados}) = {SomaEsperada}",
            stats.ArquivosCorrespondentes, stats.AtualizacoesEvitadasPorCache, stats.AtualizacoesNecessarias, stats.ArquivosSemPayload, arquivosIgnoradosPorOutraRazao, somaEsperada);
    }

    // -------------------------------------------------------------------------------------------
    // Seção 21: reconciliação de TODOS os arquivos ativos, por pasta de disciplina. Identidade esperada:
    //   TotalArquivosAtivos = TemporariosIgnorados + ArquivosSemCorrespondenciaSql + ArquivosComCorrespondenciaSql
    //   ArquivosComCorrespondenciaSql = ArquivosSemPayload + CacheHits + PostsSucesso + PostsFalha + DuplicadosVersionUrn
    // Se não fechar, é só um alerta de observabilidade (RECONCILIACAO_MISMATCH) — nenhum arquivo fica sem
    // log, mesmo quando a identidade não fecha exatamente (ex.: um POST cuja gravação de cache falhou
    // continua contado normalmente em PostsSucesso/PostsFalha).
    // -------------------------------------------------------------------------------------------
    foreach (var r in stats.ReconciliacaoPorPasta.Values.OrderBy(r => r.Configuracao, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Pasta, StringComparer.OrdinalIgnoreCase))
    {
        _logger.LogInformation(
            "[ETAPA7][RECONCILIACAO] Configuracao={Configuracao} | Pasta={Pasta} | TotalArquivosAtivos={TotalArquivosAtivos} | TemporariosIgnorados={TemporariosIgnorados} | " +
            "ArquivosComCorrespondenciaSql={ArquivosComCorrespondenciaSql} | ArquivosSemCorrespondenciaSql={ArquivosSemCorrespondenciaSql} | ArquivosComPayload={ArquivosComPayload} | " +
            "ArquivosSemPayload={ArquivosSemPayload} | CacheHits={CacheHits} | PostsNormais={PostsNormais} | PostsForcados={PostsForcados} | PostsSucesso={PostsSucesso} | " +
            "PostsFalha={PostsFalha} | DuplicadosVersionUrn={DuplicadosVersionUrn}",
            r.Configuracao, r.Pasta, r.TotalArquivosAtivos, r.TemporariosIgnorados,
            r.ArquivosComCorrespondenciaSql, r.ArquivosSemCorrespondenciaSql, r.ArquivosComPayload,
            r.ArquivosSemPayload, r.CacheHits, r.PostsNormais, r.PostsForcados, r.PostsSucesso,
            r.PostsFalha, r.DuplicadosVersionUrn);

        int somaNivel1 = r.TemporariosIgnorados + r.ArquivosSemCorrespondenciaSql + r.ArquivosComCorrespondenciaSql;
        int somaNivel2 = r.ArquivosSemPayload + r.CacheHits + r.PostsSucesso + r.PostsFalha + r.DuplicadosVersionUrn;

        if (somaNivel1 != r.TotalArquivosAtivos || somaNivel2 != r.ArquivosComCorrespondenciaSql)
        {
            _logger.LogWarning(
                "[ETAPA7][RECONCILIACAO] RECONCILIACAO_MISMATCH | Configuracao={Configuracao} | Pasta={Pasta} | TotalArquivosAtivos({TotalArquivosAtivos}) != TemporariosIgnorados+SemCorrespondencia+ComCorrespondencia({SomaNivel1}) OU ArquivosComCorrespondenciaSql({ComCorrespondencia}) != SemPayload+CacheHits+PostsSucesso+PostsFalha+Duplicados({SomaNivel2})",
                r.Configuracao, r.Pasta, r.TotalArquivosAtivos, somaNivel1, r.ArquivosComCorrespondenciaSql, somaNivel2);
        }
    }

    // -------------------------------------------------------------------------------------------
    // [ETAPA7][ROOT] (correção 16/09/2026): agregação por pasta-raiz (primeiro segmento de r.Pasta) para
    // os 4 roots que a Etapa 07 precisa cobrir explicitamente no CHS001 — 06/08/09/12. Deriva de
    // stats.ReconciliacaoPorPasta, já populado por pasta durante classificação+matching — nenhum contador
    // novo e paralelo. "PastasVisitadas" = quantidade de Pasta distintas cujo primeiro segmento é este
    // root (inclui tanto as disciplinas fixas quanto as pastas cobertas pela varredura recursiva aditiva).
    // LIMITAÇÃO CONHECIDA: para PRODUCAO_MODELOS_3D, o novo caminho raiz "08 - FEDERATED MODEL" (inteiro)
    // e as 3 subpastas específicas antigas do mesmo grupo (mantidas por compatibilidade, nunca removidas)
    // descrevem a MESMA árvore física — ArquivosAtivos deste resumo pode ficar inflado por dupla contagem
    // de reconciliação nesse caso específico; isso NUNCA duplica POST nem arquivo em
    // ArquivosEncontrados/ArquivosCorrespondentes (dedup por VersionUrn, restrito à configuração, garante isso).
    // -------------------------------------------------------------------------------------------
    // Correção 16/09/2026 (achado na validação real do CHS001): a pasta real "09 - ENGINEERING
    // DOCUMENTS" no ACC tem espaçamento interno irregular ("09 -   ENGINEERING DOCUMENTS", múltiplos
    // espaços) — comparar r.Pasta.Split('/')[0] com igualdade exata (mesmo case-insensitive) contra a
    // string canônica esperada reportava Encontrado=False mesmo com a coleta/match/POST funcionando
    // perfeitamente para esse root (bug só no RESUMO, nunca na coleta real). Agrupa e compara usando
    // NormalizarNomePasta (mesma tolerância a espaço/hífen/acento já usada para resolver a pasta em
    // si) dos dois lados — nunca string literal exata.
    var raizesEsperadasEtapa7 = new[] { "08 - FEDERATED MODEL", "09 - ENGINEERING DOCUMENTS", "12 - PRODUCT" };
    var reconciliacaoPorRaiz = stats.ReconciliacaoPorPasta.Values
        .GroupBy(r => NormalizarNomePasta(r.Pasta.Split('/')[0]), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    int raizesEncontradas = 0;
    foreach (var raiz in raizesEsperadasEtapa7)
    {
        if (!reconciliacaoPorRaiz.TryGetValue(NormalizarNomePasta(raiz), out var pastasDaRaiz))
        {
            _logger.LogInformation("[ETAPA7][ROOT] Root={Root} | Encontrado=False", raiz);
            continue;
        }

        raizesEncontradas++;
        _logger.LogInformation(
            "[ETAPA7][ROOT] Root={Root} | Encontrado=True | PastasVisitadas={PastasVisitadas} | ArquivosAtivos={ArquivosAtivos} | MatchesLdp={MatchesLdp} | PostsSucesso={PostsSucesso} | PostsFalha={PostsFalha}",
            raiz, pastasDaRaiz.Count, pastasDaRaiz.Sum(p => p.TotalArquivosAtivos), pastasDaRaiz.Sum(p => p.ArquivosComCorrespondenciaSql),
            pastasDaRaiz.Sum(p => p.PostsSucesso), pastasDaRaiz.Sum(p => p.PostsFalha));
    }

    var raizesEsperadasNormalizadas = raizesEsperadasEtapa7.Select(NormalizarNomePasta).ToList();
    var pastasNasRaizesEsperadas = reconciliacaoPorRaiz.Where(kv => raizesEsperadasNormalizadas.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
        .SelectMany(kv => kv.Value).ToList();
    _logger.LogInformation(
        "[ETAPA7][ROOT][RESUMO] RootsEsperados={RootsEsperados} | RootsEncontrados={RootsEncontrados} | PastasVisitadas={PastasVisitadas} | ArquivosEncontrados={ArquivosEncontrados} | " +
        "MatchesLdp={MatchesLdp} | PostsSucesso={PostsSucesso} | PostsFalha={PostsFalha} | DuplicadosIgnorados={DuplicadosIgnorados}",
        raizesEsperadasEtapa7.Length, raizesEncontradas, pastasNasRaizesEsperadas.Count,
        pastasNasRaizesEsperadas.Sum(p => p.TotalArquivosAtivos), pastasNasRaizesEsperadas.Sum(p => p.ArquivosComCorrespondenciaSql),
        pastasNasRaizesEsperadas.Sum(p => p.PostsSucesso), pastasNasRaizesEsperadas.Sum(p => p.PostsFalha),
        stats.ArquivosDuplicadosIgnoradosMesmaConfiguracao + stats.ArquivosDuplicadosIgnoradosEntreConfiguracoes);

    if (stats.PastasComSnapshotIncompleto.Count > 0)
    {
        _logger.LogWarning(
            "[ETAPA7][ACC][PAGINACAO][RESUMO] SNAPSHOT_INCOMPLETO | Pastas={Pastas}",
            string.Join(", ", stats.PastasComSnapshotIncompleto));
    }

    return resumo;
}

/// <summary>
/// Para cada configuração em PastasAtributos, classifica os arquivos do ACC em um de dois modos:
///
/// - Configurações com PastasPrincipaisAlternativas (ENGENHARIA_04, PRODUCAO_10): resolve a pasta
///   principal UMA ÚNICA VEZ dentre as alternativas (ver ResolverPastaPrincipal) e coleta, de forma NÃO
///   recursiva, apenas os arquivos diretamente dentro de cada pasta de disciplina (CaminhosRelativos)
///   resolvida por busca tolerante (ver ResolverPastaDisciplina / ClassificarViaPastaPrincipalAlternativaAsync).
///
/// - Configurações com CaminhosRaiz (PRODUCAO_MODELOS_3D): resolve cada caminho completo (via
///   AutodeskApiService.FindFolderIdByPathAsync) e, a partir da última pasta de cada caminho, coleta
///   recursivamente TODOS os arquivos encontrados em qualquer profundidade abaixo dela — os nomes das
///   subpastas abaixo da última pasta configurada são livres/variáveis (ver
///   ColetarArquivosDaPastaRecursivoAsync).
///
/// Em ambos os modos, a ausência de um caminho/alternativa específica é logada e NÃO interrompe a
/// resolução dos demais, nem das demais configurações. O resultado é indexado por Chave da configuração
/// — nunca por Grupo — porque PRODUCAO_10 e PRODUCAO_MODELOS_3D compartilham o mesmo Grupo ("Produção").
/// </summary>
private async Task<Dictionary<string, List<ArquivoAccClassificado>>> ClassificarArquivosAccPorPastaAsync(string hubId, string projectId, string accessToken, SincronizacaoAtributosStats stats)
{
    var resultado = new Dictionary<string, List<ArquivoAccClassificado>>(StringComparer.OrdinalIgnoreCase);

    foreach (var grupoConfig in PastasAtributos)
    {
        // Fase B (17/09/2026): roots bloqueados para a Etapa 07 nunca são sequer descobertos aqui — a
        // configuração continua existindo em PastasAtributos (outras etapas podem lê-la), mas a Etapa 07
        // nunca varre a árvore dela. resultado[Chave] fica com lista vazia — o loop de match abaixo
        // (SyncAccAttributesFromSqlAsync) já pula configurações sem arquivos, sem precisar de lógica extra.
        if (ChavesBloqueadasParaAtualizacaoDeAtributos.Contains(grupoConfig.Chave))
        {
            _logger.LogInformation(
                "[ETAPA7][ROOT] Root={Root} | Resultado=IGNORADO_POR_CONFIGURACAO | Motivo=ROOT_NAO_PERMITIDO_PARA_ATUALIZACAO_DE_ATRIBUTOS",
                grupoConfig.PastasPrincipaisAlternativas.FirstOrDefault() ?? grupoConfig.Chave);
            resultado[grupoConfig.Chave] = new List<ArquivoAccClassificado>();
            continue;
        }

        var arquivosDaConfiguracao = new List<ArquivoAccClassificado>();
        resultado[grupoConfig.Chave] = arquivosDaConfiguracao;

        // Deduplicação local por VersionUrn, restrita a ESTA configuração: evita que caminhos sobrepostos
        // da mesma configuração coletem o mesmo arquivo duas vezes. Nunca deduplica por nome — arquivos
        // diferentes com o mesmo nome, e o mesmo nome-base com extensões diferentes, têm VersionUrn
        // distintas e continuam sendo coletados normalmente.
        var versionUrnsJaColetadasNestaConfiguracao = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (grupoConfig.PastasPrincipaisAlternativas.Count > 0)
        {
            await ClassificarViaPastaPrincipalAlternativaAsync(hubId, projectId, accessToken, grupoConfig, arquivosDaConfiguracao, versionUrnsJaColetadasNestaConfiguracao, stats);
            continue;
        }

        foreach (var caminhoRaiz in grupoConfig.CaminhosRaiz)
        {
            string caminhoLegivel = string.Join("/", caminhoRaiz);
            string folderId = null;

            try
            {
                folderId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, caminhoRaiz, accessToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Grupo: {grupoConfig.Grupo} | Caminho: {caminhoLegivel} | Resultado: falha ao localizar — {ex.Message}");
            }

            if (string.IsNullOrEmpty(folderId))
            {
                _logger.LogWarning($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Grupo: {grupoConfig.Grupo} | Caminho: {caminhoLegivel} | Resultado: não encontrado no ACC — nenhum arquivo será coletado a partir deste caminho.");
                continue; // A ausência deste caminho não interrompe os demais.
            }

            int antesDaColeta = arquivosDaConfiguracao.Count;

            try
            {
                await ColetarArquivosDaPastaRecursivoAsync(
                    projectId,
                    folderId,
                    grupoConfig.Chave,
                    grupoConfig.Grupo,
                    caminhoLegivel,
                    arquivosDaConfiguracao,
                    accessToken,
                    versionUrnsJaColetadasNestaConfiguracao,
                    stats);

                int coletados = arquivosDaConfiguracao.Count - antesDaColeta;
                _logger.LogInformation($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Grupo: {grupoConfig.Grupo} | Caminho: {caminhoLegivel} | Resultado: encontrado | Arquivos coletados: {coletados}.");
            }
            catch (Exception ex)
            {
                // Seção 7: qualquer falha de página (rede, HTTP, loop de URL, limite de páginas) aborta a
                // coleta DESTE caminho — nunca fingimos sucesso com uma árvore parcialmente lida. Os
                // arquivos já coletados antes da falha permanecem só para diagnóstico; o caminho inteiro é
                // marcado como incompleto e os demais caminhos da configuração continuam normalmente.
                stats.PastasComSnapshotIncompleto.Add($"{grupoConfig.Chave}|{caminhoLegivel}");
                _logger.LogError(ex,
                    "[ETAPA7][ACC][PAGINACAO][INCOMPLETO] Configuracao={Configuracao} | Pasta={Pasta} | Motivo={Motivo}",
                    grupoConfig.Chave, caminhoLegivel, ex.Message);
            }
        }
    }

    return resultado;
}

/// <summary>
/// Varre recursivamente uma pasta (a partir da última pasta configurada em CaminhosRaiz) coletando
/// todos os arquivos encontrados em qualquer profundidade — a árvore abaixo da pasta inicial é livre:
/// nomes e quantidade de níveis de subpasta não precisam ser conhecidos previamente. Marca cada arquivo
/// com a configuração, o grupo e o caminho completo onde foi encontrado. Usa a coleta PAGINADA (seção
/// 6/9) — cada nível da árvore percorre todas as páginas antes de entrar nas subpastas. Nunca engole
/// exceção: uma falha de paginação em qualquer nível propaga para o chamador (ver
/// ClassificarArquivosAccPorPastaAsync), que decide como marcar o caminho como incompleto.
/// </summary>
private async Task ColetarArquivosDaPastaRecursivoAsync(
    string projectId,
    string folderId,
    string chaveConfiguracao,
    string grupo,
    string caminhoPasta,
    List<ArquivoAccClassificado> destino,
    string accessToken,
    HashSet<string> versionUrnsJaColetadas,
    SincronizacaoAtributosStats stats)
{
    List<AccFolderItem> conteudo = await _autodeskApi.GetFolderContentsAllPagesAsync(
        projectId, folderId, accessToken, cancellationToken: default,
        configuracaoParaLog: chaveConfiguracao, pastaParaLog: caminhoPasta);

    var reconciliacao = stats.ObterOuCriarReconciliacao(chaveConfiguracao, caminhoPasta);

    foreach (var item in conteudo)
    {
        if (item.Type == "items")
        {
            reconciliacao.TotalArquivosAtivos++;

            if (EhArquivoTemporarioOuInvalido(item))
            {
                reconciliacao.TemporariosIgnorados++;
                continue;
            }

            if (!versionUrnsJaColetadas.Add(item.VersionUrn))
            {
                continue; // Mesma VersionUrn já coletada nesta configuração (caminhos sobrepostos) — não duplica.
            }

            destino.Add(new ArquivoAccClassificado
            {
                FileName = item.Name.Trim(),
                FileNameWithoutExtension = Path.GetFileNameWithoutExtension(item.Name).Trim(),
                Extension = Path.GetExtension(item.Name),
                VersionUrn = item.VersionUrn,
                Grupo = grupo,
                ChaveConfiguracao = chaveConfiguracao,
                PastaEncontrada = caminhoPasta,
                FolderId = folderId
            });

            _logger.LogInformation(
                "[ETAPA7][ARQUIVO][CLASSIFICADO] Arquivo={Arquivo} | Configuracao={Configuracao} | Pasta={Pasta} | Extensao={Extensao} | VersionUrn={VersionUrn}",
                item.Name.Trim(), chaveConfiguracao, caminhoPasta, Path.GetExtension(item.Name), ResumirUrn(item.VersionUrn));
        }
        else if (item.Type == "folders" && !item.IsHidden)
        {
            // Correção 16/09/2026 (Etapa 07 recursiva para 06/08/09/12): nunca desce em pastas de
            // versão antiga/backup/arquivo (mesma lista PalavrasPastaInvalida já usada para a pasta
            // principal e de disciplina — único ponto de verdade, ver ContemPalavraInvalida) — preserva a
            // exclusão que a coleta não recursiva original já garantia implicitamente ao nunca entrar em
            // nenhuma subpasta.
            if (ContemPalavraInvalida(NormalizarNomePasta(item.Name))) continue;

            await ColetarArquivosDaPastaRecursivoAsync(projectId, item.Id, chaveConfiguracao, grupo, $"{caminhoPasta}/{item.Name}", destino, accessToken, versionUrnsJaColetadas, stats);
        }
    }
}

/// <summary>
/// Resultado da busca tolerante por uma pasta (principal ou de disciplina) dentre uma lista de pastas
/// disponíveis num mesmo nível da árvore.
/// </summary>
internal enum ResultadoBuscaPasta { Encontrada, NaoEncontrada, Ambigua }

/// <summary>
/// Palavras que, quando presentes no nome normalizado de uma pasta candidata, a descartam mesmo que o
/// restante do nome bata com um alias ou com a regra de prefixo — evita atualizar arquivos em pastas de
/// backup/versões antigas que por coincidência tenham um nome parecido com a estrutura oficial.
/// </summary>
private static readonly string[] PalavrasPastaInvalida =
{
    "OLD", "BACKUP", "ARCHIVE", "ARCHIVED", "SUPERSEDED", "SUPERADOS",
    "OBSOLETE", "DEPRECATED", "TEMP", "TEMPORARY", "COPY", "COPIA", "TEST", "TESTE"
};

/// <summary>
/// Famílias de fallback tolerante (prefixo numérico + grupos de palavras aceitas) para a pasta principal,
/// usadas somente quando nenhum alias de PastasPrincipaisAlternativas bate exatamente após normalização —
/// cobre variantes não cadastradas explicitamente (ex.: "10 - PRODUCTION DOCUMENTS", "09 - ENGINEERING
/// DOCUMENTATION"). Indexado por Chave da CONFIGURAÇÃO DONA do prefixo — "09" pertence exclusivamente a
/// PRODUCAO_DOCUMENTOS_09, nunca é tentado como fallback de PRODUCAO_10 (nem vice-versa). Cada grupo
/// dentro de GruposDePalavrasAceitas é uma condição "E" (todas as palavras do grupo precisam estar
/// presentes); grupos diferentes são alternativas "OU".
/// </summary>
private static readonly Dictionary<string, (string PrefixoNumerico, string[][] GruposDePalavrasAceitas)[]> FamiliasFallbackPastaPrincipalPorConfiguracao =
    new Dictionary<string, (string, string[][])[]>(StringComparer.OrdinalIgnoreCase)
    {
        [CHAVE_PRODUCAO_10] = new[]
        {
            ("10", new[] { new[] { "PRODUCTION" }, new[] { "PRODUCAO" }, new[] { "PRODUCT" }, new[] { "PRODUTO" } })
        },
        [CHAVE_PRODUCAO_DOCUMENTOS_09] = new[]
        {
            ("09", new[] { new[] { "ENGINEERING", "DOCUMENT" } })
        }
    };

/// <summary>
/// Verifica se o nome normalizado de uma pasta contém alguma palavra de exclusão (ver
/// PalavrasPastaInvalida), comparando por token inteiro (não substring) para não descartar por
/// coincidência parcial (ex.: não confundir "TEMPORIZACAO" com "TEMP" — comparação é por token completo
/// separado por espaço, já vindo de NormalizarNomePasta).
/// </summary>
internal static bool ContemPalavraInvalida(string nomeNormalizado)
{
    if (string.IsNullOrEmpty(nomeNormalizado)) return false;
    var tokens = nomeNormalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return tokens.Any(t => PalavrasPastaInvalida.Contains(t));
}

/// <summary>
/// Um item de pasta do ACC é ignorado na coleta da Etapa 7 (seção 8) quando: não tem nome; é um arquivo
/// temporário (prefixo "~$", ex.: "~$Documento.xlsx" aberto no Excel/AutoCAD); ou não tem VersionUrn
/// (entrada inválida/incompleta da resposta). Usado igualmente pela coleta não recursiva (04/09/10) e
/// pela recursiva (PRODUCAO_MODELOS_3D) — único ponto de verdade para este filtro.
/// </summary>
internal static bool EhArquivoTemporarioOuInvalido(AccFolderItem item)
{
    return item == null
        || string.IsNullOrEmpty(item.Name)
        || item.Name.StartsWith("~$")
        || string.IsNullOrEmpty(item.VersionUrn);
}

/// <summary>
/// Extrai o prefixo numérico (ex.: "10.01" de "10.01 - PROCESS") do início de um nome configurado, para
/// uso na busca tolerante por prefixo (ver ResolverPastaDisciplina). Retorna null se o nome não começar
/// por dígitos.
/// </summary>
internal static string ExtrairPrefixoNumerico(string nomeConfigurado)
{
    if (string.IsNullOrWhiteSpace(nomeConfigurado)) return null;
    var match = Regex.Match(nomeConfigurado.Trim(), @"^(\d+(?:\.\d+)*)");
    return match.Success ? match.Value : null;
}

/// <summary>
/// Resolve a pasta principal de uma configuração (ENGENHARIA_04, PRODUCAO_DOCUMENTOS_09 ou PRODUCAO_10)
/// dentre as pastas do nível raiz do projeto (conteudoRaiz), tentando as alternativas de
/// GrupoPastaConfig.PastasPrincipaisAlternativas na ordem configurada. Prioridade:
///   1) Alias exato (após NormalizarNomePasta) — cobre espaço duplo, ponto/hífen, maiúsc./minúsc., acento.
///   2) Fallback por prefixo numérico + palavras aceitas (via FamiliasFallbackPastaPrincipalPorConfiguracao,
///      indexado pela PRÓPRIA Chave — "09" nunca resolve como alternativa de "10", e vice-versa) — cobre
///      variantes não cadastradas explicitamente.
/// Se mais de uma pasta candidata bater em qualquer uma das duas prioridades, o resultado é Ambígua e a
/// configuração NÃO deve ser processada (nunca se escolhe no chute com First()/FirstOrDefault()).
/// </summary>
internal static (ResultadoBuscaPasta Resultado, AccFolderItem Pasta, List<string> Candidatas) ResolverPastaPrincipal(
    List<AccFolderItem> conteudoRaiz,
    GrupoPastaConfig grupoConfig)
{
    var pastas = conteudoRaiz.Where(p => p.Type == "folders" && !p.IsHidden).ToList();

    // Prioridade 1: alias exato após normalização.
    var aliasesNormalizados = grupoConfig.PastasPrincipaisAlternativas
        .Select(NormalizarNomePasta)
        .Where(a => !string.IsNullOrEmpty(a))
        .Distinct()
        .ToList();

    var candidatosExatos = pastas
        .Where(p =>
        {
            string nomeNorm = NormalizarNomePasta(p.Name);
            return !ContemPalavraInvalida(nomeNorm) && aliasesNormalizados.Contains(nomeNorm);
        })
        .ToList();

    if (candidatosExatos.Count == 1) return (ResultadoBuscaPasta.Encontrada, candidatosExatos[0], null);
    if (candidatosExatos.Count > 1) return (ResultadoBuscaPasta.Ambigua, null, candidatosExatos.Select(p => p.Name).ToList());

    // Prioridade 2: fallback tolerante por prefixo numérico + palavras aceitas — só a família da PRÓPRIA
    // Chave é tentada (nunca a de outra configuração).
    if (FamiliasFallbackPastaPrincipalPorConfiguracao.TryGetValue(grupoConfig.Chave, out var familias))
    {
        foreach (var familia in familias)
        {
            string prefixoNormalizado = NormalizarNomePasta(familia.PrefixoNumerico);
            var candidatosFamilia = pastas
                .Where(p =>
                {
                    string nomeNorm = NormalizarNomePasta(p.Name);
                    if (ContemPalavraInvalida(nomeNorm)) return false;
                    bool prefixoOk = nomeNorm == prefixoNormalizado || nomeNorm.StartsWith(prefixoNormalizado + " ");
                    if (!prefixoOk) return false;
                    return familia.GruposDePalavrasAceitas.Any(grupo => grupo.All(palavra => nomeNorm.Contains(palavra)));
                })
                .ToList();

            if (candidatosFamilia.Count == 1) return (ResultadoBuscaPasta.Encontrada, candidatosFamilia[0], null);
            if (candidatosFamilia.Count > 1) return (ResultadoBuscaPasta.Ambigua, null, candidatosFamilia.Select(p => p.Name).ToList());
        }
    }

    return (ResultadoBuscaPasta.NaoEncontrada, null, null);
}

/// <summary>
/// Resolve uma pasta de disciplina (ex.: "10.01 - PROCESS") dentre o conteúdo já carregado da pasta
/// principal, com prioridade: 1) nome completo exato após normalização; 2) prefixo numérico obrigatório
/// (ex.: "10.01"), tolerando texto de disciplina diferente (tradução, sufixo extra, etc.) — nunca se
/// localiza uma disciplina só pela palavra (ex.: "PROCESS"), o número é sempre a autoridade. Ambiguidade
/// (mais de uma candidata) impede a escolha automática.
/// </summary>
internal static ResultadoBuscaPasta ResolverPastaDisciplina(
    List<AccFolderItem> conteudoPastaPrincipal,
    string nomeConfigurado,
    out AccFolderItem pastaEncontrada,
    out List<string> candidatosAmbiguos)
{
    pastaEncontrada = null;
    candidatosAmbiguos = null;

    var pastas = conteudoPastaPrincipal.Where(p => p.Type == "folders" && !p.IsHidden).ToList();
    string aliasNormalizado = NormalizarNomePasta(nomeConfigurado);

    // Prioridade 1: nome completo exato após normalização.
    var candidatosExatos = pastas
        .Where(p =>
        {
            string nomeNorm = NormalizarNomePasta(p.Name);
            return !ContemPalavraInvalida(nomeNorm) && nomeNorm == aliasNormalizado;
        })
        .ToList();

    if (candidatosExatos.Count == 1) { pastaEncontrada = candidatosExatos[0]; return ResultadoBuscaPasta.Encontrada; }
    if (candidatosExatos.Count > 1) { candidatosAmbiguos = candidatosExatos.Select(p => p.Name).ToList(); return ResultadoBuscaPasta.Ambigua; }

    // Prioridade 2: prefixo numérico obrigatório (ex.: "10.01"), texto da disciplina pode variar.
    string prefixo = ExtrairPrefixoNumerico(nomeConfigurado);
    if (!string.IsNullOrEmpty(prefixo))
    {
        string prefixoNormalizado = NormalizarNomePasta(prefixo);
        var candidatosPorPrefixo = pastas
            .Where(p =>
            {
                string nomeNorm = NormalizarNomePasta(p.Name);
                if (ContemPalavraInvalida(nomeNorm)) return false;
                return nomeNorm == prefixoNormalizado || nomeNorm.StartsWith(prefixoNormalizado + " ");
            })
            .ToList();

        if (candidatosPorPrefixo.Count == 1) { pastaEncontrada = candidatosPorPrefixo[0]; return ResultadoBuscaPasta.Encontrada; }
        if (candidatosPorPrefixo.Count > 1) { candidatosAmbiguos = candidatosPorPrefixo.Select(p => p.Name).ToList(); return ResultadoBuscaPasta.Ambigua; }
    }

    return ResultadoBuscaPasta.NaoEncontrada;
}

/// <summary>
/// Classifica os arquivos de uma configuração baseada em PastasPrincipaisAlternativas + CaminhosRelativos
/// (ENGENHARIA_04 / PRODUCAO_10). Fluxo otimizado para nunca repetir a busca da pasta principal por
/// disciplina:
///   1 chamada para localizar as pastas de topo do projeto ("Project Files");
///   1 chamada para listar o conteúdo do nível raiz (onde estão as alternativas da pasta principal);
///   1 chamada para listar o conteúdo da pasta principal já resolvida (onde estão as disciplinas);
///   as 14 disciplinas são resolvidas EM MEMÓRIA (ResolverPastaDisciplina), sem nenhuma chamada de rede;
///   1 chamada por disciplina efetivamente encontrada, só para ler seus arquivos diretos (sem recursão).
/// </summary>
private async Task ClassificarViaPastaPrincipalAlternativaAsync(
    string hubId,
    string projectId,
    string accessToken,
    GrupoPastaConfig grupoConfig,
    List<ArquivoAccClassificado> arquivosDaConfiguracao,
    HashSet<string> versionUrnsJaColetadasNestaConfiguracao,
    SincronizacaoAtributosStats stats)
{
    List<AccFolderItem> topFolders;
    try
    {
        topFolders = await _autodeskApi.GetTopFoldersAsync(hubId, projectId, accessToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Falha ao obter as pastas de topo do projeto: {ex.Message}");
        return;
    }

    var projectFilesRoot = topFolders?.FirstOrDefault(f => f.Name == "Project Files");
    if (projectFilesRoot == null)
    {
        _logger.LogWarning($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Pasta 'Project Files' não encontrada na raiz do projeto.");
        return;
    }

    List<AccFolderItem> conteudoRaiz;
    try
    {
        conteudoRaiz = await _autodeskApi.GetFolderContentsSimpleAsync(projectId, projectFilesRoot.Id, accessToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"[Sync Atributos] Configuração: {grupoConfig.Chave} | Falha ao ler o conteúdo da raiz do projeto: {ex.Message}");
        return;
    }

    var resultadoPrincipal = ResolverPastaPrincipal(conteudoRaiz, grupoConfig);

    if (resultadoPrincipal.Resultado == ResultadoBuscaPasta.Ambigua)
    {
        _logger.LogWarning(
            $"[Sync Atributos] Configuração: {grupoConfig.Chave} | Resultado: ambíguo | Pastas candidatas: {string.Join(", ", resultadoPrincipal.Candidatas)} | " +
            "A configuração não será processada para evitar atualizar arquivos na pasta errada.");
        return;
    }

    if (resultadoPrincipal.Resultado == ResultadoBuscaPasta.NaoEncontrada)
    {
        _logger.LogWarning(
            $"[Sync Atributos] Configuração: {grupoConfig.Chave} | Nenhuma pasta principal alternativa encontrada. " +
            $"Alternativas tentadas: {string.Join(", ", grupoConfig.PastasPrincipaisAlternativas)} | Resultado: configuração ignorada neste projeto.");
        return;
    }

    var pastaPrincipal = resultadoPrincipal.Pasta;
    var alternativasNaoExecutadas = grupoConfig.PastasPrincipaisAlternativas
        .Where(a => !string.Equals(NormalizarNomePasta(a), NormalizarNomePasta(pastaPrincipal.Name), StringComparison.OrdinalIgnoreCase))
        .Distinct()
        .ToList();

    _logger.LogInformation(
        $"[Sync Atributos] Configuração: {grupoConfig.Chave} | Pasta principal localizada: '{pastaPrincipal.Name}' | FolderId: {ResumirUrn(pastaPrincipal.Id)}" +
        (alternativasNaoExecutadas.Count > 0 ? $" | Alternativas não executadas após sucesso: {string.Join(", ", alternativasNaoExecutadas)}." : "."));

    _logger.LogInformation(
        "[ATRIBUTOS][PASTA][PRINCIPAL] Configuracao={Configuracao} | PastaPrincipal={PastaPrincipal}",
        grupoConfig.Chave, pastaPrincipal.Name);

    // Correção 16/09/2026 (simplificação da recursão — auditoria pediu arquitetura simples): varre a
    // árvore INTEIRA abaixo da pasta principal recursivamente, UM ÚNICO PASSE — nunca primeiro coleta
    // direto por disciplina e depois tenta completar recursivamente (isso deixava pastas de disciplina
    // já "reconhecidas" fora da varredura recursiva, então um arquivo profundo dentro de uma disciplina
    // reconhecida — ex.: "09.06 - PIPING/Related Files/Subpasta/arquivo.pdf" — nunca era visitado). Cobre
    // as 14 disciplinas nomeadas (CriarCaminhosDisciplinas/ResolverPastaDisciplina continuam existindo e
    // testadas como funções puras, só não são mais chamadas aqui) E qualquer estrutura fora do padrão, em
    // qualquer profundidade. Nunca duplica arquivo/POST: dedup por VersionUrn, restrito a esta
    // configuração (versionUrnsJaColetadasNestaConfiguracao). Pastas de versão antiga/backup continuam
    // excluídas (ver ColetarArquivosDaPastaRecursivoAsync). FolderId de cada arquivo passa a ser a pasta
    // REAL onde ele foi encontrado (qualquer profundidade) — ObterDefinicoesEfetivasAsync já resolve
    // definições de atributo por FolderId genérico, nunca dependeu de ser especificamente uma pasta de
    // disciplina.
    try
    {
        await ColetarArquivosDaPastaRecursivoAsync(
            projectId, pastaPrincipal.Id, grupoConfig.Chave, grupoConfig.Grupo, pastaPrincipal.Name,
            arquivosDaConfiguracao, accessToken, versionUrnsJaColetadasNestaConfiguracao, stats);
    }
    catch (Exception ex)
    {
        stats.PastasComSnapshotIncompleto.Add($"{grupoConfig.Chave}|{pastaPrincipal.Name}");
        _logger.LogError(ex,
            "[ETAPA7][ACC][PAGINACAO][INCOMPLETO] Configuracao={Configuracao} | Pasta={Pasta} | Motivo={Motivo}",
            grupoConfig.Chave, pastaPrincipal.Name, ex.Message);
    }
}

/// <summary>
/// Traduz o nome VISÍVEL do atributo no ACC (ex.: "EMISSION DATA") para a chave já usada no
/// dicionário rowData montado a partir da Ldp_Unificada (ex.: "EMISSIONDATA"). O valor de EMISSION
/// DATA já chega formatado como "yyyy-MM-dd" (mesmo formato que o restante deste arquivo usa para
/// atributos de data no ACC — ver montagem de rowData["EMISSIONDATA"] em SyncAccAttributesFromSqlAsync);
/// datas nulas nunca chegam aqui porque a chave simplesmente não existe na linha quando Emission_Date é
/// DBNull no banco.
/// </summary>
private static string ObterValorAtributoDaLinha(Dictionary<string, string> row, string nomeAtributoAcc)
{
    string chave;
    switch (nomeAtributoAcc)
    {
        case ATTR_DOCUMENT_TITLE: chave = "DOCUMENTTITLE"; break;
        case ATTR_CLIENT_DOCUMENT_NUMBER: chave = "CLIENTDOCUMENTNUMBER"; break;
        case ATTR_NA_DOCUMENT_NUMBER: chave = "NADOCUMENTNUMBER"; break;
        case ATTR_REV: chave = "REV"; break;
        case ATTR_TRANSMITAL_NUMBER: chave = "TRANMISTALNUMBER"; break;
        case ATTR_EMISSION_DATA: chave = "EMISSIONDATA"; break;
        default: return null;
    }

    return row.TryGetValue(chave, out var valor) ? valor : null;
}

// =====================================================================================
// RESOLUÇÃO TOLERANTE DE ATRIBUTOS PERSONALIZADOS (seção 11-14 da correção da Etapa 7): recupera o
// comportamento do motor antigo (AutodeskApiService.SyncLdpAttributesAsync — Regex.Replace(nome,
// "[^A-Z0-9]", "")), agora combinado com definições da pasta raiz + pasta específica e aliases
// controlados por atributo (nunca Contains genérico).
// =====================================================================================

/// <summary>
/// Aliases de negócio controlados — só para os três atributos com grafias alternativas conhecidas e
/// comprovadas (ver seção 12). DOCUMENT TITLE, CLIENT DOCUMENT NUMBER e N&amp;A DOCUMENT NUMBER não têm
/// alias: só igualdade exata ou normalizada. Nunca "REV" (Contains) — sempre igualdade após
/// NormalizarNomeAtributo, o que já torna "REV", "REV.", "Rev" etc. equivalentes sem arriscar casar
/// "REVIEW STATUS" ou "REVIEWER".
/// </summary>
private static readonly Dictionary<string, string[]> AliasesControladosPorAtributo =
    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [ATTR_REV] = new[] { "REV", "REV.", "REVISION", "REVISÃO", "REVISAO" },
        [ATTR_TRANSMITAL_NUMBER] = new[] { "TRANMISTAL NUMBER", "TRANSMITAL NUMBER", "TRANSMITTAL NUMBER" },
        [ATTR_EMISSION_DATA] = new[] { "EMISSION DATA", "EMISSION DATE", "DATA DE EMISSÃO", "DATA DE EMISSAO" }
    };

/// <summary>
/// Normaliza o NOME VISÍVEL de um atributo personalizado do ACC para comparação tolerante: Trim,
/// maiúsculas, remoção de acentos e remoção de QUALQUER caractere que não seja letra ou número (espaço,
/// ponto, hífen, underline, "&amp;", etc.) — mesmo comportamento comprovado do motor antigo
/// (Regex.Replace(nome, @"[^A-Z0-9]", "")), com o passo extra de remover acentos ANTES de descartar os
/// caracteres não alfanuméricos (senão "REVISÃO" perderia o "A" em vez de virar "REVISAO"). Resultado:
/// "DOCUMENT TITLE", "Document Title", "DOCUMENT_TITLE", "DOCUMENT-TITLE" e "DOCUMENTTITLE" normalizam
/// todos para "DOCUMENTTITLE"; "N&amp;A DOCUMENT NUMBER" e "NA DOCUMENT NUMBER" normalizam para o mesmo valor.
/// </summary>
internal static string NormalizarNomeAtributo(string nome)
{
    if (string.IsNullOrWhiteSpace(nome)) return string.Empty;

    string normalizado = nome.Trim().ToUpperInvariant();

    normalizado = normalizado.Normalize(NormalizationForm.FormD);
    normalizado = new string(normalizado
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .ToArray());
    normalizado = normalizado.Normalize(NormalizationForm.FormC);

    return Regex.Replace(normalizado, @"[^A-Z0-9]", "");
}

/// <summary>
/// Definições EFETIVAS de atributos personalizados para uma pasta específica (raiz + pasta combinadas —
/// ver ObterDefinicoesEfetivasAsync), já resolvidas para dois mapas (nome exato após Trim/OrdinalIgnoreCase,
/// e NormalizarNomeAtributo) mais o conjunto de chaves ambíguas (dois IDs diferentes que normalizam para a
/// mesma chave, sem uma definição da pasta específica para desempatar — nunca escolhida arbitrariamente).
/// </summary>
internal class DefinicoesEfetivasPasta
{
    public Dictionary<string, int> PorNomeExato { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PorNomeNormalizado { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChavesExatasAmbiguas { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChavesNormalizadasAmbiguas { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public List<(string Nome, int Id)> TodasDefinicoes { get; set; } = new List<(string, int)>();
}

/// <summary>
/// Agrupa entradas (Id, Chave, Especifica) por Chave (exata ou normalizada, conforme o chamador) e
/// resolve cada grupo: um único Id distinto -> resolvido; múltiplos Ids distintos mas só uma definição
/// "da pasta específica" -> a específica desempata (prioridade da pasta real sobre a raiz — seção 13.6);
/// caso contrário -> ambíguo (nunca escolhido arbitrariamente — seção 14).
/// </summary>
internal static (Dictionary<string, int> Mapa, HashSet<string> Ambiguos) ConstruirMapaComPreferenciaEspecifica(
    IEnumerable<(int Id, string Chave, bool Especifica)> entradas)
{
    var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var ambiguos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var grupo in entradas.Where(e => !string.IsNullOrEmpty(e.Chave)).GroupBy(e => e.Chave, StringComparer.OrdinalIgnoreCase))
    {
        var idsDistintos = grupo.Select(g => g.Id).Distinct().ToList();
        if (idsDistintos.Count == 1) { mapa[grupo.Key] = idsDistintos[0]; continue; }

        var especificas = grupo.Where(g => g.Especifica).Select(g => g.Id).Distinct().ToList();
        if (especificas.Count == 1) { mapa[grupo.Key] = especificas[0]; continue; }

        ambiguos.Add(grupo.Key);
    }

    return (mapa, ambiguos);
}

/// <summary>
/// Resolve as definições EFETIVAS de atributos de uma pasta: combina as definições já carregadas da
/// pasta RAIZ (buscadas uma única vez pelo chamador) com as definições da PASTA ESPECÍFICA (disciplina),
/// deduplicadas pelo ID real — nunca um GET por arquivo, sempre em cache por FolderId durante a execução
/// inteira (seção 13.7). Quando folderId é a própria raiz, não repete o GET (definicoesRaiz já é o
/// resultado completo).
/// </summary>
private async Task<DefinicoesEfetivasPasta> ObterDefinicoesEfetivasAsync(
    string projectId,
    string folderId,
    string rootFolderId,
    List<AttributeDefinition> definicoesRaiz,
    string accessToken,
    Dictionary<string, DefinicoesEfetivasPasta> cache,
    string pastaParaLog)
{
    if (string.IsNullOrEmpty(folderId)) folderId = rootFolderId;

    if (cache.TryGetValue(folderId, out var cacheado)) return cacheado;

    List<AttributeDefinition> definicoesPasta;
    if (string.Equals(folderId, rootFolderId, StringComparison.OrdinalIgnoreCase))
    {
        definicoesPasta = new List<AttributeDefinition>();
    }
    else
    {
        try
        {
            definicoesPasta = await _autodeskApi.GetFolderAttributeDefinitionsAsync(projectId, folderId, accessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sync Atributos] Falha ao carregar definições de atributos da pasta '{Pasta}' — usando somente as definições da raiz para esta pasta.", pastaParaLog);
            definicoesPasta = new List<AttributeDefinition>();
        }
    }

    // Combina por Id real: raiz primeiro, pasta específica sobrescreve/adiciona — o mesmo Id nunca gera
    // conflito (é a mesma definição), só nomes DIFERENTES que normalizam igual entram em disputa abaixo.
    var porId = new Dictionary<int, (string Nome, bool Especifica)>();
    foreach (var d in definicoesRaiz.Where(d => !string.IsNullOrWhiteSpace(d.Name)))
        porId[d.Id] = (d.Name.Trim(), false);
    foreach (var d in definicoesPasta.Where(d => !string.IsNullOrWhiteSpace(d.Name)))
        porId[d.Id] = (d.Name.Trim(), true);

    var entradasExato = porId.Select(kv => (Id: kv.Key, Chave: kv.Value.Nome, Especifica: kv.Value.Especifica)).ToList();
    var entradasNormalizado = porId.Select(kv => (Id: kv.Key, Chave: NormalizarNomeAtributo(kv.Value.Nome), Especifica: kv.Value.Especifica)).ToList();

    var (mapaExato, ambiguosExato) = ConstruirMapaComPreferenciaEspecifica(entradasExato);
    var (mapaNormalizado, ambiguosNormalizado) = ConstruirMapaComPreferenciaEspecifica(entradasNormalizado);

    var resultado = new DefinicoesEfetivasPasta
    {
        PorNomeExato = mapaExato,
        PorNomeNormalizado = mapaNormalizado,
        ChavesExatasAmbiguas = ambiguosExato,
        ChavesNormalizadasAmbiguas = ambiguosNormalizado,
        TodasDefinicoes = porId.Select(kv => (kv.Value.Nome, kv.Key)).ToList()
    };

    cache[folderId] = resultado;
    return resultado;
}

/// <summary>
/// Resolve o ID do atributo personalizado no ACC para um NOME CONFIGURADO (ex.: ATTR_DOCUMENT_TITLE),
/// seguindo a ordem fixa da seção 12: 1) igualdade exata após Trim/OrdinalIgnoreCase; 2) igualdade pelo
/// nome normalizado (NormalizarNomeAtributo); 3) aliases de negócio controlados (só para REV.,
/// TRANMISTAL NUMBER e EMISSION DATA); nunca Contains genérico. Ambiguidade (seção 14) interrompe a
/// resolução para aquele atributo/pasta e é logada — nunca escolhida arbitrariamente.
/// </summary>
internal static int? ResolverIdAtributo(string nomeAtributoConfigurado, DefinicoesEfetivasPasta defs, string pastaParaLog, ILogger logger)
{
    string nomeTrim = nomeAtributoConfigurado.Trim();

    if (defs.ChavesExatasAmbiguas.Contains(nomeTrim))
    {
        LogarAmbiguidade(nomeAtributoConfigurado, nomeTrim, defs, pastaParaLog, logger, porNomeExato: true);
        return null;
    }
    if (defs.PorNomeExato.TryGetValue(nomeTrim, out int idExato)) return idExato;

    string chaveNorm = NormalizarNomeAtributo(nomeAtributoConfigurado);
    if (defs.ChavesNormalizadasAmbiguas.Contains(chaveNorm))
    {
        LogarAmbiguidade(nomeAtributoConfigurado, chaveNorm, defs, pastaParaLog, logger, porNomeExato: false);
        return null;
    }
    if (defs.PorNomeNormalizado.TryGetValue(chaveNorm, out int idNorm)) return idNorm;

    if (AliasesControladosPorAtributo.TryGetValue(nomeAtributoConfigurado, out var aliases))
    {
        foreach (var alias in aliases)
        {
            string aliasNorm = NormalizarNomeAtributo(alias);
            if (defs.ChavesNormalizadasAmbiguas.Contains(aliasNorm))
            {
                LogarAmbiguidade(nomeAtributoConfigurado, aliasNorm, defs, pastaParaLog, logger, porNomeExato: false);
                return null;
            }
            if (defs.PorNomeNormalizado.TryGetValue(aliasNorm, out int idAlias)) return idAlias;
        }
    }

    return null; // Não resolvido — nenhuma definição no ACC bate (exata, normalizada ou por alias controlado).
}

private static void LogarAmbiguidade(string nomeAtributoConfigurado, string chave, DefinicoesEfetivasPasta defs, string pastaParaLog, ILogger logger, bool porNomeExato)
{
    var candidatas = defs.TodasDefinicoes
        .Where(d => porNomeExato
            ? string.Equals(d.Nome, chave, StringComparison.OrdinalIgnoreCase)
            : string.Equals(NormalizarNomeAtributo(d.Nome), chave, StringComparison.OrdinalIgnoreCase))
        .Select(d => $"{d.Nome}(Id={d.Id})");

    logger.LogWarning(
        "[ETAPA7][ATRIBUTO][AMBIGUO] AtributoConfigurado={AtributoConfigurado} | NomeNormalizado={NomeNormalizado} | DefinicoesCandidatas={DefinicoesCandidatas} | Pasta={Pasta}",
        nomeAtributoConfigurado, chave, string.Join(", ", candidatas), pastaParaLog);
}

/// <summary>
/// Normaliza um nome de pasta para comparação tolerante: maiúsculas, sem acentos, com ponto/hífen/
/// underline/travessão tratados como espaço, sem pontuação residual e sem espaços duplicados/nas pontas.
/// Isso torna equivalentes variações como "10 - PRODUCTION", "10 -  PRODUCTION" (dois espaços),
/// "10.PRODUCTION", "10_PRODUCTION" e "10 – PRODUCTION" (travessão), e também "10 - PRODUÇÃO" /
/// "10 - PRODUCAO" (com/sem acento). Ainda assim exige comparação de igualdade do texto completo (não
/// substring) quando usada como alias exato — "PRODUCTION" continua sendo uma palavra diferente de
/// "PRODUCAO"; só variações de grafia/pontuação/acentuação/espaçamento viram equivalentes, nunca
/// traduções ou palavras diferentes (essas exigem alias explícito ou a regra de prefixo+palavras).
/// </summary>
internal static string NormalizarNomePasta(string nome)
{
    if (string.IsNullOrWhiteSpace(nome)) return string.Empty;

    string normalizado = nome.Trim().ToUpperInvariant();

    // Remove acentos (ã -> a, ç -> c, etc.) decompondo em base + marca diacrítica e descartando a marca.
    normalizado = normalizado.Normalize(NormalizationForm.FormD);
    normalizado = new string(normalizado
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .ToArray());
    normalizado = normalizado.Normalize(NormalizationForm.FormC);

    // Ponto, hífen, underline e travessões (– —) viram espaço.
    normalizado = Regex.Replace(normalizado, @"[\.\-_–—]+", " ");

    // Remove qualquer caractere que não seja letra, dígito ou espaço.
    normalizado = Regex.Replace(normalizado, @"[^A-Z0-9\s]", " ");

    // Colapsa espaços consecutivos em um único espaço.
    normalizado = Regex.Replace(normalizado, @"\s+", " ");

    return normalizado.Trim();
}

/// <summary>
/// Localiza a coluna do cabeçalho "STATUS IN THE FLOW" (ou variante tolerante "STATUS IN FLOW") na
/// linha indicada, normalizando com a mesma tolerância a espaço/pontuação/acentuação de
/// NormalizarNomePasta. Usada por UpdateWorkflowStatusAsync (correção 16/09/2026: Etapa 05 é UMA SÓ
/// regra para qualquer Tipo_LDP — só o ACESSO FÍSICO à coluna pode variar, nunca a regra de negócio) —
/// a LDP Tipo2 tem colunas extras de gate 30/60/90 que deslocam esse cabeçalho para uma posição
/// diferente da coluna 17 fixa do layout Tipo1 original. Nunca decide nada de negócio, só a posição da
/// célula. Cai em colunaPadrao (nunca lança exceção, nunca bloqueia a Etapa 05) quando: a planilha/linha
/// é inválida, o cabeçalho não é encontrado, ou aparece mais de uma vez na linha (ambíguo).
/// </summary>
internal static int LocalizarColunaStatusInFlow(ExcelWorksheet ws, int linhaCabecalho, int colunaPadrao) =>
    LdpHeaderLookup.LocalizarColunaPorCabecalho(ws, linhaCabecalho, new[] { "STATUS IN THE FLOW", "STATUS IN FLOW" }, colunaPadrao);

private object TratarData(string valor)
{
    if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;
    
    string v = valor.Trim().ToUpper();
    // Lista de "lixos" comuns em datas de Excel
    if (v == "-" || v == "N/A" || v == "NA" || v == "TBD" || v == "A DEFINIR") return DBNull.Value;

    // Tenta converter formato PT-BR
    if (DateTime.TryParse(valor, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out DateTime data))
    {
        if (data.Year < 1900 || data.Year > 2100) return DBNull.Value; // Proteção contra datas absurdas
        return data;
    }
    
    // Tenta converter número serial do Excel (ex: 45321)
    if (double.TryParse(valor, out double serial))
    {
        try { return DateTime.FromOADate(serial); } catch {}
    }

    return DBNull.Value;
}

// 2. Use assim no seu loop de INSERT (Etapa 11):
// cmd.Parameters.AddWithValue("@DataEmissao", TratarData(ws.Cells[r, 10].Text));

private string GetLdpFileNameFromConfig(string projectId)
{
    // 1. Tenta pegar configuração específica do Projeto
    var fileName = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];

    // 2. Se não existir, tenta pegar o Default
    if (string.IsNullOrEmpty(fileName))
    {
        fileName = _configuration["ProjectSettings:Default:LdpFileName"];
    }

    // 3. Fallback de segurança (se esqueceram de configurar tudo)
    if (string.IsNullOrEmpty(fileName))
    {
        return ".xlsx"; // Procura qualquer Excel (comportamento antigo)
    }

    return fileName;
}

        public async Task<string> MirrorLdpControlToProjectAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation("🪞 [ETAPA 10] ESPELHAMENTO (CÓPIA PUBLICA)...");

            var pathsEspelho = new[] {
        new[] { "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO" },
        new[] { "01 - PLANNING & CONTROL", "01.06 - MASTER DOCUMENT LIST" }
    };

            string tempSourcePath = Path.Combine(Path.GetTempPath(), $"SRC_{Guid.NewGuid()}.xlsx");
            string tempTargetPath = Path.Combine(Path.GetTempPath(), $"TGT_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. RECUPERA DETALHES DA ORIGEM
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Não foi possível recuperar os detalhes da LDP.");

                string sourceVersionUrn = itemDetails.TipVersionUrn;
                string mirrorFileName = itemDetails.DisplayName; // Nome correto garantido

                // DOWNLOAD DA ORIGEM
                using (var sourceStream = await _autodeskApi.DownloadFileStreamAsync(projectId, sourceVersionUrn, accessToken))
                using (var fileStream = new FileStream(tempSourcePath, FileMode.Create, FileAccess.Write))
                {
                    await sourceStream.CopyToAsync(fileStream);
                }

                // 2. PROCESSAMENTO (FIX PARA #REF!)
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(tempSourcePath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP"))
                             ?? package.Workbook.Worksheets[0];

                    int colNA_Number = 4; // Coluna D conforme seu código anterior

                    // 🎯 SOLUÇÃO PARA O #REF!: Converte fórmulas em valores antes de deletar o resto
                    for (int r = 8; r <= ws.Dimension.End.Row; r++)
                    {
                        var cell = ws.Cells[r, colNA_Number];
                        if (cell.Formula != null)
                        {
                            // Pegamos o texto resultante (Value) e sobrescrevemos a fórmula com ele
                            string valorCalculado = cell.Text;
                            cell.Formula = null; // Remove a dependência
                            cell.Value = valorCalculado; // Fixa o texto
                        }
                    }

                    // Agora podemos deletar as outras abas e colunas sem medo de quebrar nada
                    int lastCol = ws.Dimension?.End.Column ?? 30;
                    if (lastCol > 15) ws.DeleteColumn(16, lastCol - 15);

                    // Definimos quais abas DEVEM ser mantidas
                    var extraSheets = package.Workbook.Worksheets
                        .Where(s => !s.Name.ToUpper().Contains("LDP") && 
                                    !s.Name.ToUpper().Contains("CAPA")) 
                        .ToList();

                    foreach (var s in extraSheets) 
                    {
                        _logger.LogInformation($"🗑️ Removendo aba desnecessária no espelho: {s.Name}");
                        package.Workbook.Worksheets.Delete(s);
                    }

                    package.SaveAs(new FileInfo(tempTargetPath));
                }

                // 3. UPLOAD (Mantendo o ID existente se houver)
                string targetFolderId = null;
                foreach (var path in pathsEspelho)
                {
                    targetFolderId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
                    if (!string.IsNullOrEmpty(targetFolderId)) break;
                }

                if (string.IsNullOrEmpty(targetFolderId)) return "Erro: Pasta 01.06 não encontrada.";

                var existingMirrorId = await _autodeskApi.FindItemIdByNameExactAsync(projectId, targetFolderId, mirrorFileName, accessToken);

                using (var uploadStream = new FileStream(tempTargetPath, FileMode.Open, FileAccess.Read))
                {
                    if (!string.IsNullOrEmpty(existingMirrorId))
                        await _autodeskApi.CreateNewVersionAsync(projectId, existingMirrorId, targetFolderId, mirrorFileName, uploadStream, accessToken);
                    else
                        await _autodeskApi.UploadFileToFolderAsync(projectId, targetFolderId, mirrorFileName, uploadStream, accessToken);
                }

                return $"LDP Espelhada com sucesso: {mirrorFileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erro no espelhamento.");
                throw;
            }
            finally
            {
                if (File.Exists(tempSourcePath)) File.Delete(tempSourcePath);
                if (File.Exists(tempTargetPath)) File.Delete(tempTargetPath);
            }
        }

        /// <summary>
        /// Tenta encontrar uma pasta usando múltiplos caminhos possíveis (ex: PT e EN).
        /// </summary>
        private async Task<string> FindFolderIdByAlternativePathsAsync(string hubId, string projectId, string[][] possiblePaths, string accessToken)
{
    foreach (var path in possiblePaths)
    {
        // Tenta achar o caminho atual
        var folderId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
        
        // Se achou, retorna e para de procurar
        if (!string.IsNullOrEmpty(folderId))
        {
            _logger.LogInformation($"Pasta encontrada no caminho: {string.Join(" -> ", path)}");
            return folderId;
        }
    }
    // Se rodou tudo e não achou nada
    return null;
}

// --- DENTRO DE LdpUpdateService.cs ---

public async Task ExecuteUpdateEtapa1Async(string projectId, string tempExcelPath)
{
    using (var package = new ExcelPackage(new FileInfo(tempExcelPath)))
    {
        var wsLdp = package.Workbook.Worksheets["LDP"];
        // PEGA A VISÃO NA LINHA 8 (CONFORME SEU CSV)
        string crFilter = wsLdp.Cells[8, 24].Text.Trim();   // Coluna X
        string crosFilter = wsLdp.Cells[8, 25].Text.Trim(); // Coluna Y

        // BUSCA NO BANCO COM O MESMO FILTRO DA TIMESHEET
        var dadosBanco = await _context.TbTimeSheetRealizadoDocumentos
            .Where(t => t.Projeto == crFilter && t.Doc1 == crosFilter)
            .ToListAsync();

        // Faz o mapeamento usando Trim() para ignorar erros de digitação no Excel
        foreach (var row in wsLdp.Cells[8, 1, wsLdp.Dimension.End.Row, 1])
        {
            int rowIdx = row.Start.Row;
            string docNoExcel = wsLdp.Cells[rowIdx, 4].Text.Trim(); // Coluna D (N&A Number)

            var dadosDoc = dadosBanco.FirstOrDefault(d => d.DocumentoCodigo?.Trim() == docNoExcel);
            if (dadosDoc != null)
            {
                // ATUALIZA REV (Coluna 14 / N)
                wsLdp.Cells[rowIdx, 14].Value = dadosDoc.Doc2; // Ou a coluna de REV no seu banco
                
                // ATUALIZA STATUS (Coluna 17 / Q) - Lógica do seu fluxo
                // wsLdp.Cells[rowIdx, 17].Value = ...; 
            }
        }
        package.Save();
    }
}
public async Task SincronizarAtributosAccAsync(string hubId, string projectId, string tempExcelPath, string accessToken)
        {
            _logger.LogInformation("⚡ [SYNC ATRIBUTOS] Iniciando sincronização baseada no CR-OS...");

            using (var package = new ExcelPackage(new System.IO.FileInfo(tempExcelPath)))
            {
                var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("LDP", StringComparison.OrdinalIgnoreCase));
                if (wsLdp == null) return;

                // 1. PEGAR A VISÃO: CR e CR-OS da planilha
                string valCR = wsLdp.Cells[LINHA_FILTRO_LDP, COLUNA_X_CR].Text?.Trim();
                string valCROS = wsLdp.Cells[LINHA_FILTRO_LDP, COLUNA_Y_CROS].Text?.Trim();

                _logger.LogInformation($"🔍 Sincronizando apenas arquivos da OS: {valCROS}");

                // 2. BUSCAR TODOS OS ARQUIVOS DO PROJETO NO ACC (Indexação)
                var arquivosNoAcc = await _autodeskApi.IndexarArquivosProjetoAsync(hubId, projectId, accessToken);

                // 3. FILTRAR OS ARQUIVOS DO ACC QUE COMEÇAM COM O CR-OS
                // Assim ele ignora BML009.01 se você estiver na BML009.04
                var arquivosDaMinhaOs = arquivosNoAcc
                    .Where(f => f.FileName.StartsWith(valCROS, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _logger.LogInformation($"📂 {arquivosDaMinhaOs.Count} arquivos encontrados no ACC para a OS {valCROS}.");

                // 4. ATUALIZAR ATRIBUTOS (Sem usar aquela chave N&A Document Number)
                foreach (var arquivo in arquivosDaMinhaOs)
                {
                    try 
                    {
                        // Aqui o robô atualiza os metadados do arquivo direto pelo nome que já casou com o filtro
                        _logger.LogInformation($"✅ Sincronizando metadados do arquivo: {arquivo.FileName}");
                        
                        // Exemplo: Atualiza a Revisão ou Status no ACC
                        await _autodeskApi.UpdateFileAttributesAsync(projectId, arquivo.FileUrn, new {
                            Projeto = valCR,
                            OS = valCROS
                            // Adicione outros atributos conforme sua necessidade
                        }, accessToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"⚠️ Falha ao atualizar atributos de {arquivo.FileName}: {ex.Message}");
                    }
                }
            }
        }
        

    }
}