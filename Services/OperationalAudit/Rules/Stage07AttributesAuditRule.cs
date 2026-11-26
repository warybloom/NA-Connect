using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using NA_Connect.Services;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 7 — Atributos ACC (seção 8/9/10 do Checkpoint 2, corrigida pós-Checkpoint 2 seções 1-6).
    /// Subdividida por configuração (ENGENHARIA_04/PRODUCAO_DOCUMENTOS_09/PRODUCAO_10/
    /// PRODUCAO_MODELOS_3D_07/PRODUCAO_MODELOS_3D_08) — nunca copia o método operacional inteiro, só
    /// reconstrói a leitura via adaptadores somente-leitura. Nunca atualiza o cache
    /// (<c>ACC_Attribute_Sync_State</c>) — só SELECT via <see cref="IAuditDatabaseReadOnlyAdapter"/>.
    ///
    /// PRODUCAO_MODELOS_3D_07/_08 são as DUAS árvores reais e independentes (07 - 3D AUTHORING SOFTWARE /
    /// 08 - FEDERATED MODEL) — cada uma localizada e auditada separadamente (nunca uma "alternativa
    /// automática" da outra; isso é decidido pelo perfil do projeto via
    /// <see cref="AuditProjectProfile.AlternativeStructures"/>, o mesmo mecanismo já usado pelas outras
    /// estruturas). Os caminhos permitidos de cada árvore são uma PROJEÇÃO da configuração operacional real
    /// — ver <see cref="AuditAttributeStructureProfile"/> para a origem exata — nunca uma whitelist
    /// inventada nesta regra.
    /// </summary>
    public sealed class Stage07AttributesAuditRule : IOperationalAuditRule
    {
        public string Etapa => "7";
        public string NomeEtapa => "Atributos Customizados ACC";

        private static readonly string[] AtributosPadraoDeProducao = { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV." };

        private static readonly string[] PalavrasPastaInvalida =
        {
            "OLD", "BACKUP", "ARCHIVE", "ARCHIVED", "SUPERSEDED", "SUPERADOS", "OBSOLETE", "DEPRECATED",
            "TEMP", "TEMPORARY", "COPY", "COPIA", "TEST", "TESTE"
        };

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };

        /// <summary>
        /// internal (não private) só para permitir teste direto de AuditarConfiguracaoAsync com um perfil
        /// sob medida — ex.: "configuração insuficiente de subpastas", "disciplina configurada
        /// diferente" — sem precisar reconstruir toda a árvore de pastas real (ver AssemblyInfo.cs
        /// InternalsVisibleTo).
        /// </summary>
        internal static readonly IReadOnlyList<AuditAttributeStructureProfile> Configuracoes = ConstruirConfiguracoes();

        /// <summary>
        /// ENGENHARIA_04/PRODUCAO_DOCUMENTOS_09/PRODUCAO_10 permanecem literais (fora do escopo desta
        /// correção: não têm "subpastas permitidas" — Recursivo=false, então ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA
        /// nunca se aplica a elas). As duas árvores de PRODUCAO_MODELOS_3D (correção "segunda fonte de
        /// verdade" pós-Checkpoint 2) são construídas por <see cref="AuditAttributeStructureProfileProvider"/>,
        /// que lê <c>LdpUpdateService.PastasAtributos</c> DIRETAMENTE em tempo de execução — nunca uma
        /// cópia estática mantida aqui. Se a configuração operacional real mudar, estes dois perfis mudam
        /// junto, no próximo carregamento do assembly, sem exigir nenhuma edição nesta regra.
        /// </summary>
        private static IReadOnlyList<AuditAttributeStructureProfile> ConstruirConfiguracoes()
        {
            var lista = new List<AuditAttributeStructureProfile>
            {
                new AuditAttributeStructureProfile
                {
                    ChaveConfiguracao = "ENGENHARIA_04",
                    AliasesRaiz = new[] { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" },
                    Recursivo = false,
                    AtributosObrigatorios = new[] { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV.", "TRANMISTAL NUMBER", "EMISSION DATA" }
                },
                new AuditAttributeStructureProfile
                {
                    ChaveConfiguracao = "PRODUCAO_DOCUMENTOS_09",
                    AliasesRaiz = new[] { "09 - ENGINEERING DOCUMENTS", "09 - ENGINEERING DOCUMENTATION", "09 - DOCUMENTOS DE ENGENHARIA" },
                    Recursivo = false,
                    AtributosObrigatorios = AtributosPadraoDeProducao
                },
                new AuditAttributeStructureProfile
                {
                    ChaveConfiguracao = "PRODUCAO_10",
                    AliasesRaiz = new[] { "10 -  PRODUCTION", "10 - PRODUCTION", "10 - PRODUCT", "10 - PRODUÇÃO" },
                    Recursivo = false,
                    AtributosObrigatorios = AtributosPadraoDeProducao
                }
            };
            lista.AddRange(AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3D());
            return lista;
        }

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditDatabaseReadOnlyAdapter _database;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage07AttributesAuditRule(IAuditAccReadOnlyAdapter acc, IAuditDatabaseReadOnlyAdapter database, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _acc = acc;
            _database = database;
            _workbook = workbook;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var perfil = contexto.Options.ObterPerfil(null);
            var estruturasEncontradasNoProjeto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Primeira passada: descobre quais estruturas existem, para resolver alternativas corretamente
            // (seção 4 da correção: "quando ambas existirem, auditar ambas"; "quando só uma existir e o
            // perfil configurar alternativa, a outra vira NOT_APPLICABLE" — ambos via
            // AttributeStructureRequirementResolver + AuditProjectProfile.AlternativeStructures, o mesmo
            // mecanismo já usado pelas outras 3 estruturas, nunca uma lógica nova aqui).
            var pastasPorConfig = new Dictionary<string, AccFolderRef?>();
            foreach (var config in Configuracoes)
            {
                var pasta = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, config.AliasesRaiz, config.Recursivo, ct);
                pastasPorConfig[config.ChaveConfiguracao] = pasta;
                if (pasta != null) estruturasEncontradasNoProjeto.Add(config.ChaveConfiguracao);
            }

            // Documentos da LDP, para reconciliação (seção 8) — mesma pasta de Gestão usada pela Etapa 0.
            var codigosNaNaLdp = await ObterCodigosDaLdpAsync(contexto, ct);

            foreach (var config in Configuracoes)
            {
                var pasta = pastasPorConfig[config.ChaveConfiguracao];
                var obrigatoriedade = AttributeStructureRequirementResolver.Resolver(config.ChaveConfiguracao, pasta != null, perfil, estruturasEncontradasNoProjeto);

                requirements.Add(NovoRequisito(contexto, $"ATR-ESTRUTURA-{config.ChaveConfiguracao}", config.ChaveConfiguracao,
                    MapearParaStatus(obrigatoriedade), "estrutura presente conforme perfil", pasta != null ? "encontrada" : obrigatoriedade.ToString()));

                if (pasta == null)
                {
                    if (obrigatoriedade == AttributeStructureRequirementOutcome.ObrigatoriaAusente)
                    {
                        // Código distinto de ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA (que é sobre um ARQUIVO
                        // encontrado numa pasta errada, não sobre a ESTRUTURA inteira estar ausente).
                        findings.Add(NovoFinding(contexto, "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE",
                            $"Estrutura obrigatória '{config.ChaveConfiguracao}' não encontrada nos aliases permitidos",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                            bloqueiaEtapa: true, "ESTRUTURA_OBRIGATORIA_AUSENTE", configurationKey: config.ChaveConfiguracao));
                    }
                    continue;
                }

                await AuditarConfiguracaoAsync(contexto, config, pasta, codigosNaNaLdp, findings, requirements, ct);
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        internal async Task AuditarConfiguracaoAsync(
            OperationalAuditContext contexto, AuditAttributeStructureProfile config, AccFolderRef pasta, IReadOnlySet<string> codigosNaNaLdp,
            List<OperationalFinding> findings, List<OperationalRequirementResult> requirements, System.Threading.CancellationToken ct)
        {
            var listagem = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pasta.FolderId, config.Recursivo, ct);

            requirements.Add(NovoRequisito(contexto, "ATR-PAGINACAO", $"{config.ChaveConfiguracao}: paginação completa",
                OperationalRequirementStatus.Approved, "todas as páginas percorridas", $"{listagem.QuantidadeDePaginas} páginas, {listagem.Itens.Count} itens"));

            // ATR-SNAPSHOT_INCOMPLETO — preservado sem alteração de comportamento (seção 9 da correção):
            // só com evidência OBJETIVA vinda do adaptador (PaginacaoCompleta), nunca "poucos arquivos".
            requirements.Add(NovoRequisito(contexto, "ATR-SNAPSHOT_INCOMPLETO", $"{config.ChaveConfiguracao}: snapshot ACC completo",
                listagem.PaginacaoCompleta ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "coleta completa da população ACC", listagem.PaginacaoCompleta ? "completa" : listagem.MotivoPaginacaoIncompleta ?? "incompleta"));

            if (!listagem.PaginacaoCompleta)
            {
                bool pareceFalhaDeAutomacao = listagem.MotivoPaginacaoIncompleta?.Contains("Limite de segurança", StringComparison.OrdinalIgnoreCase) == true
                    || listagem.MotivoPaginacaoIncompleta?.Contains("Nenhum link de próxima página", StringComparison.OrdinalIgnoreCase) == true;

                findings.Add(new OperationalFinding
                {
                    ExecutionId = contexto.ExecutionId,
                    DataHoraUtc = DateTime.UtcNow,
                    ProjectIdAcc = contexto.ProjectIdAcc,
                    Etapa = "7",
                    RegraId = "ATR-SNAPSHOT_INCOMPLETO",
                    CodigoProblema = "ATR-SNAPSHOT_INCOMPLETO",
                    Titulo = $"Snapshot ACC de {config.ChaveConfiguracao} incompleto — a auditoria não pode considerar a população coletada como completa",
                    Descricao = listagem.MotivoPaginacaoIncompleta,
                    ErrorDomain = pareceFalhaDeAutomacao ? OperationalAuditErrorDomain.Automacao : OperationalAuditErrorDomain.Inconclusivo,
                    Classificacao = pareceFalhaDeAutomacao ? OperationalAuditClassification.RiscoTecnico : OperationalAuditClassification.IntegracaoExterna,
                    Severidade = OperationalAuditSeverity.Critico,
                    BloqueiaEtapa = true,
                    AutomacaoPodeCorrigir = false,
                    MotivoNormalizado = "SNAPSHOT_ACC_INCOMPLETO",
                    ConfigurationKey = config.ChaveConfiguracao,
                    PastaEncontrada = pasta.ResolvedPath,
                    ValorEsperado = listagem.TotalDeclaradoPelaApi?.ToString(),
                    ValorEncontrado = listagem.Itens.Count.ToString(),
                    Evidencia = listagem.MotivoPaginacaoIncompleta
                });
            }

            var itensValidos = new List<AccItemRef>();
            bool algumArquivoForaDaPastaPermitida = false;
            bool avaliouAlgumaVezComConfiguracao = false;

            foreach (var item in listagem.Itens)
            {
                if (string.IsNullOrEmpty(item.Name) || item.Name.StartsWith("~$", StringComparison.Ordinal) || string.IsNullOrEmpty(item.LatestVersionUrn))
                {
                    continue; // arquivo temporário/inválido — ignorado, nunca gera finding (seção 8).
                }

                if (item.IsHiddenFolder)
                {
                    findings.Add(NovoFinding(contexto, "ATR-ARQUIVO_EM_RECYCLE_BIN",
                        $"Arquivo '{item.Name}' está em uma pasta oculta (Recycle Bin) dentro de {config.ChaveConfiguracao}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "ARQUIVO_EM_RECYCLE_BIN", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name, pastaEncontrada: item.ParentFolderName));
                    continue;
                }

                if (PalavrasPastaInvalida.Any(p => string.Equals(p, item.ParentFolderName?.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(NovoFinding(contexto, "ATR-ARQUIVO_EM_BACKUP",
                        $"Arquivo '{item.Name}' está em uma pasta de backup/old/archive ('{item.ParentFolderName}') dentro de {config.ChaveConfiguracao}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "ARQUIVO_EM_BACKUP", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name, pastaEncontrada: item.ParentFolderName));
                    continue;
                }

                // ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA — 3 estados explícitos (seção 3 da correção), nunca um
                // booleano "dentro/fora" implícito. Só CONFIGURADA_E_NAO_PERMITIDA pode gerar finding; SEM
                // configuração suficiente (lista vazia) nunca inventa um erro operacional.
                if (config.Recursivo && !string.IsNullOrEmpty(item.CaminhoRelativoAPastaRaiz))
                {
                    var avaliacao = AttributePathPermissionResolver.Resolver(item.CaminhoRelativoAPastaRaiz, config.CaminhosPermitidosRelativos);
                    if (avaliacao != AvaliacaoCaminhoPermitido.SemConfiguracaoSuficiente) avaliouAlgumaVezComConfiguracao = true;

                    if (avaliacao == AvaliacaoCaminhoPermitido.ConfiguradaENaoPermitida)
                    {
                        algumArquivoForaDaPastaPermitida = true;
                        findings.Add(NovoFinding(contexto, "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA",
                            $"Arquivo '{item.Name}' localizado em '{item.CaminhoRelativoAPastaRaiz}', fora das subpastas configuradas de {config.ChaveConfiguracao}",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                            bloqueiaEtapa: false, "ARQUIVO_FORA_DE_SUBPASTA_PERMITIDA", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name,
                            pastaEncontrada: item.CaminhoRelativoAPastaRaiz));
                        continue;
                    }
                }

                itensValidos.Add(item);
            }

            if (config.Recursivo)
            {
                bool temConfigDeSubpastas = config.CaminhosPermitidosRelativos.Length > 0;
                requirements.Add(NovoRequisito(contexto, "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA", $"{config.ChaveConfiguracao}: arquivos só em subpastas configuradas",
                    !temConfigDeSubpastas ? OperationalRequirementStatus.Inconclusive
                        : algumArquivoForaDaPastaPermitida ? OperationalRequirementStatus.Failed : OperationalRequirementStatus.Approved,
                    "todos dentro das subpastas configuradas",
                    !temConfigDeSubpastas ? "sem configuração suficiente" : algumArquivoForaDaPastaPermitida ? "arquivo(s) fora" : "todos dentro"));
            }

            var definicoes = await _acc.ObterDefinicoesDeAtributoAsync(contexto.ProjectIdAcc, pasta.FolderId, ct);

            foreach (var nomeAtributo in config.AtributosObrigatorios)
            {
                bool existe = CustomAttributeStateResolver.DefinicaoExiste(definicoes, nomeAtributo);
                bool ambigua = CustomAttributeStateResolver.DefinicaoEhAmbigua(definicoes, nomeAtributo);

                requirements.Add(NovoRequisito(contexto, "ATR-CUSTOM-001", $"{config.ChaveConfiguracao}: definição de '{nomeAtributo}'",
                    existe ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, "cadastrado", existe ? "cadastrado" : "ausente"));

                if (!existe)
                {
                    findings.Add(NovoFinding(contexto, "ATR-CUSTOM-001", $"Atributo obrigatório '{nomeAtributo}' não cadastrado em {config.ChaveConfiguracao}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.QualidadeConfiguracaoAcc, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: true, "ATRIBUTO_NAO_CADASTRADO", configurationKey: config.ChaveConfiguracao, customAttributeName: nomeAtributo,
                        probableOwner: "BIM Manager / Administrador ACC"));
                }
                else if (ambigua)
                {
                    findings.Add(NovoFinding(contexto, "ATR-CUSTOM-003", $"Múltiplas definições correspondem ao nome normalizado de '{nomeAtributo}' em {config.ChaveConfiguracao}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.AtributoAccAmbiguo, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: false, "ATRIBUTO_AMBIGUO", configurationKey: config.ChaveConfiguracao, customAttributeName: nomeAtributo));
                }
            }

            int documentosComPayloadCompleto = 0;
            var codigosNaEncontradosNoAcc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payloadCompletoPorItem = new Dictionary<string, bool>();

            foreach (var item in itensValidos)
            {
                var valores = await _acc.ObterValoresDeAtributoDaVersaoAsync(contexto.ProjectIdAcc, item.LatestVersionUrn!, ct);
                bool payloadCompleto = true;

                foreach (var nomeAtributo in config.AtributosObrigatorios)
                {
                    if (!CustomAttributeStateResolver.DefinicaoExiste(definicoes, nomeAtributo)) continue; // já reportado por ATR-CUSTOM-001.

                    var valor = valores.FirstOrDefault(v => string.Equals(v.Name.Trim(), nomeAtributo, StringComparison.OrdinalIgnoreCase));
                    var estado = CustomAttributeStateResolver.ResolverEstado(valor);

                    if (estado != CustomAttributeState.PresentVisible) payloadCompleto = false;

                    switch (estado)
                    {
                        case CustomAttributeState.PresentHidden:
                            findings.Add(NovoFinding(contexto, "ATR-CUSTOM-002", $"Atributo '{nomeAtributo}' comprovadamente oculto em '{item.Name}'",
                                OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.AtributoAccOculto, OperationalAuditSeverity.Alto,
                                bloqueiaEtapa: false, "ATRIBUTO_COMPROVADAMENTE_OCULTO", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name,
                                customAttributeName: nomeAtributo, customAttributeState: estado, isCustomAttributeHidden: true));
                            break;
                        case CustomAttributeState.ApiVisibilityInconclusive:
                            findings.Add(NovoFinding(contexto, "ATR-CUSTOM-004", $"API não fornece evidência suficiente sobre '{nomeAtributo}' em '{item.Name}' (ausente ou oculto — inconclusivo)",
                                OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.QualidadeConfiguracaoAcc, OperationalAuditSeverity.Medio,
                                bloqueiaEtapa: false, "API_SEM_EVIDENCIA_DE_VISIBILIDADE", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name,
                                customAttributeName: nomeAtributo, customAttributeState: estado));
                            break;
                    }
                }

                payloadCompletoPorItem[item.ItemId] = payloadCompleto;

                if (payloadCompleto) documentosComPayloadCompleto++;
                else
                {
                    findings.Add(NovoFinding(contexto, "ATR-PAYLOAD_INCOMPLETO", $"Payload potencial incompleto para '{item.Name}' em {config.ChaveConfiguracao}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "PAYLOAD_POTENCIAL_INCOMPLETO", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name));
                }

                // Correspondência por PREFIXO DE TOKENS (seção 5/6 da correção) — nunca Contains/substring
                // cru. Mesma semântica pura já usada pela Etapa 7 operacional real
                // (DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens), nunca uma regra nova
                // inventada aqui.
                string? codigoNaDoNome = codigosNaNaLdp.FirstOrDefault(codigo => DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(item.Name, codigo));
                if (codigoNaDoNome != null) codigosNaEncontradosNoAcc.Add(codigoNaDoNome);

                // ATR-CACHE_SEM_EVIDENCIA_DE_POST — SELECT-only; nunca atualiza o cache.
                bool cacheTemEvidenciaDeSucesso = await CacheTemEvidenciaDeSucessoAsync(contexto.ProjectIdAcc, item.LatestVersionUrn!, config.ChaveConfiguracao, ct);
                if (payloadCompleto && !cacheTemEvidenciaDeSucesso)
                {
                    findings.Add(NovoFinding(contexto, "ATR-CACHE_SEM_EVIDENCIA_DE_POST",
                        $"'{item.Name}' tem payload potencial completo, mas o cache não registra sucesso de POST anterior",
                        OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.QualidadeConfiguracaoAcc, OperationalAuditSeverity.Baixo,
                        bloqueiaEtapa: false, "CACHE_SEM_EVIDENCIA_DE_POST", configurationKey: config.ChaveConfiguracao, nomeArquivo: item.Name));
                }
            }

            // ATR-CODIGO_AMBIGUO / ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS (preservados sem alteração de
            // comportamento, seção 9 da correção — só a forma de encontrar candidatos mudou, seção 5/6):
            // candidatos agora vêm de DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens, nunca
            // item.Name.Contains(codigo). Candidatos com o MESMO nome-base (ignorando extensão) são o
            // mesmo documento lógico em formatos diferentes — completude é comparada entre eles
            // (MULTIPLAS_EXTENSOES). Candidatos com nomes-base DIFERENTES que batem no mesmo código são
            // ambiguidade real — a regra NUNCA escolhe um vencedor, só relata todos.
            foreach (var codigo in codigosNaNaLdp)
            {
                var candidatos = itensValidos.Where(i => DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(i.Name, codigo)).ToList();
                if (candidatos.Count < 2) continue;

                var gruposPorNomeBase = candidatos
                    .GroupBy(i => NormalizarNomeBase(i.Name), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (gruposPorNomeBase.Count > 1)
                {
                    string evidencia = string.Join(" | ", candidatos.Select(c =>
                        $"{c.Name} (pasta={c.ParentFolderName}, versionUrn={c.LatestVersionUrn ?? "N/D"})"));

                    requirements.Add(NovoRequisito(contexto, "ATR-CODIGO_AMBIGUO", $"{config.ChaveConfiguracao}: código '{codigo}' sem candidatos incompatíveis",
                        OperationalRequirementStatus.Failed, "1 grupo de candidatos", $"{gruposPorNomeBase.Count} grupos distintos"));

                    findings.Add(new OperationalFinding
                    {
                        ExecutionId = contexto.ExecutionId,
                        DataHoraUtc = DateTime.UtcNow,
                        ProjectIdAcc = contexto.ProjectIdAcc,
                        Etapa = "7",
                        RegraId = "ATR-CODIGO_AMBIGUO",
                        CodigoProblema = "ATR-CODIGO_AMBIGUO",
                        Titulo = $"Código '{codigo}' possui {gruposPorNomeBase.Count} candidatos ACC incompatíveis em {config.ChaveConfiguracao} — sem evidência suficiente para escolher um vencedor",
                        ErrorDomain = OperationalAuditErrorDomain.Operacional,
                        Classificacao = OperationalAuditClassification.AtributoAccAmbiguo,
                        Severidade = OperationalAuditSeverity.Alto,
                        BloqueiaEtapa = true,
                        AutomacaoPodeCorrigir = false,
                        MotivoNormalizado = "CODIGO_COM_CANDIDATOS_INCOMPATIVEIS",
                        ConfigurationKey = config.ChaveConfiguracao,
                        DocumentoNa = codigo,
                        NomeArquivo = string.Join(", ", candidatos.Select(c => c.Name)),
                        PastaEncontrada = candidatos[0].ParentFolderName,
                        Evidencia = evidencia
                    });
                    continue;
                }

                requirements.Add(NovoRequisito(contexto, "ATR-CODIGO_AMBIGUO", $"{config.ChaveConfiguracao}: código '{codigo}' sem candidatos incompatíveis",
                    OperationalRequirementStatus.Approved, "1 grupo de candidatos", "1 grupo"));

                var grupo = gruposPorNomeBase[0].ToList();
                var extensoesDistintas = grupo.Select(i => i.Extension ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (extensoesDistintas.Count <= 1) continue; // extensão única — nada a comparar (não gera falso positivo).

                var statusPorExtensao = grupo.ToDictionary(
                    i => i.Extension ?? string.Empty,
                    i => payloadCompletoPorItem.TryGetValue(i.ItemId, out var completo) && completo,
                    StringComparer.OrdinalIgnoreCase);

                bool algumCompleto = statusPorExtensao.Values.Any(v => v);
                bool algumIncompleto = statusPorExtensao.Values.Any(v => !v);
                string resumoPorExtensao = string.Join(", ", statusPorExtensao.Select(kv => $"{kv.Key}={(kv.Value ? "completo" : "incompleto")}"));

                requirements.Add(NovoRequisito(contexto, "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS", $"{config.ChaveConfiguracao}: '{codigo}' consistente entre extensões",
                    algumCompleto && algumIncompleto ? OperationalRequirementStatus.Failed : OperationalRequirementStatus.Approved,
                    "todas completas ou todas incompletas", resumoPorExtensao));

                if (algumCompleto && algumIncompleto)
                {
                    findings.Add(new OperationalFinding
                    {
                        ExecutionId = contexto.ExecutionId,
                        DataHoraUtc = DateTime.UtcNow,
                        ProjectIdAcc = contexto.ProjectIdAcc,
                        Etapa = "7",
                        RegraId = "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS",
                        CodigoProblema = "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS",
                        Titulo = $"Documento '{codigo}' com extensões divergentes em {config.ChaveConfiguracao}: {resumoPorExtensao}",
                        ErrorDomain = OperationalAuditErrorDomain.Operacional,
                        Classificacao = OperationalAuditClassification.DadoAusente,
                        Severidade = OperationalAuditSeverity.Medio,
                        BloqueiaEtapa = false,
                        AutomacaoPodeCorrigir = false,
                        MotivoNormalizado = "EXTENSOES_COM_PAYLOAD_DIVERGENTE",
                        ConfigurationKey = config.ChaveConfiguracao,
                        DocumentoNa = codigo,
                        NomeArquivo = string.Join(", ", grupo.Select(g => g.Name)),
                        Evidencia = resumoPorExtensao
                    });
                }
            }

            // Reconciliação LDP x ACC (seção 8) — mesma correspondência por prefixo de tokens.
            if (codigosNaNaLdp.Count > 0)
            {
                foreach (var codigoNa in codigosNaNaLdp)
                {
                    if (!codigosNaEncontradosNoAcc.Contains(codigoNa))
                    {
                        findings.Add(NovoFinding(contexto, "ATR-LDP_SEM_ARQUIVO_ACC", $"Documento '{codigoNa}' presente na LDP sem arquivo correspondente em {config.ChaveConfiguracao}",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "LDP_SEM_ARQUIVO_ACC", configurationKey: config.ChaveConfiguracao, documentoNa: codigoNa));
                    }
                }
                foreach (var codigoNa in codigosNaEncontradosNoAcc)
                {
                    if (!codigosNaNaLdp.Contains(codigoNa))
                    {
                        findings.Add(NovoFinding(contexto, "ATR-ACC_SEM_DOCUMENTO_LDP", $"Arquivo '{codigoNa}' em {config.ChaveConfiguracao} sem documento correspondente na LDP",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoAccSemLdp, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "ACC_SEM_DOCUMENTO_LDP", configurationKey: config.ChaveConfiguracao, documentoNa: codigoNa));
                    }
                }
            }
        }

        private async Task<bool> CacheTemEvidenciaDeSucessoAsync(string projectIdAcc, string versionUrn, string chaveConfiguracao, System.Threading.CancellationToken ct)
        {
            var parametros = new Dictionary<string, object?>
            {
                ["@ProjectIdAcc"] = projectIdAcc,
                ["@VersionUrn"] = versionUrn,
                ["@ChaveConfiguracao"] = chaveConfiguracao
            };
            DataTable tabela = await _database.ConsultarAsync(
                "SELECT TOP 1 UltimaTentativaSucesso FROM ACC_Attribute_Sync_State " +
                "WHERE Project_ID_ACC = @ProjectIdAcc AND VersionUrn = @VersionUrn AND ChaveConfiguracao = @ChaveConfiguracao " +
                "ORDER BY DataUltimaTentativa DESC",
                parametros, ct);

            if (tabela.Rows.Count == 0) return false;
            var valor = tabela.Rows[0]["UltimaTentativaSucesso"];
            return valor != DBNull.Value && Convert.ToBoolean(valor);
        }

        /// <summary>
        /// Código documental de cada linha real da LDP, com a prioridade N&amp;A → fallback Cliente (seção
        /// 6 da correção pós-Checkpoint 2) via <see cref="AuditDocumentCodeResolver"/> — nunca escolhe
        /// nenhum candidato quando os dois estão vazios.
        /// </summary>
        private async Task<IReadOnlySet<string>> ObterCodigosDaLdpAsync(OperationalAuditContext contexto, System.Threading.CancellationToken ct)
        {
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            if (pastaGestao == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidato = conteudo.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidato == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
            var workbook = _workbook.AbrirSomenteLeitura(stream);
            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));
            if (nomeAbaLdp == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, valores) in workbook.Abas[nomeAbaLdp].Linhas)
            {
                string? codigoNa = ObterValorDeColuna(valores, "N&A DOCUMENT NUMBER", "N&A");
                string? codigoCliente = ObterValorDeColuna(valores, "CLIENT DOCUMENT NUMBER", "NÚMERO DOCUMENTO CLIENTE", "NUMERO DOCUMENTO CLIENTE");

                string? codigoResolvido = AuditDocumentCodeResolver.ResolverCodigoDaLinha(codigoNa, codigoCliente);
                if (codigoResolvido != null) codigos.Add(codigoResolvido);
            }
            return codigos;
        }

        private static string? ObterValorDeColuna(IReadOnlyDictionary<string, string?> valores, params string[] candidatos)
        {
            foreach (var candidato in candidatos)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), candidato, StringComparison.OrdinalIgnoreCase));
                if (chave != null) return valores[chave];
            }
            return null;
        }

        /// <summary>Nome do arquivo sem extensão, maiúsculo e sem espaços nas pontas — usado só para agrupar candidatos do MESMO documento lógico em formatos diferentes (ATR-CODIGO_AMBIGUO / ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS).</summary>
        private static string NormalizarNomeBase(string nomeArquivo) =>
            System.IO.Path.GetFileNameWithoutExtension(nomeArquivo).Trim().ToUpperInvariant();

        private static OperationalRequirementStatus MapearParaStatus(AttributeStructureRequirementOutcome outcome) => outcome switch
        {
            AttributeStructureRequirementOutcome.Presente => OperationalRequirementStatus.Approved,
            AttributeStructureRequirementOutcome.ObrigatoriaAusente => OperationalRequirementStatus.Failed,
            AttributeStructureRequirementOutcome.OpcionalAusente => OperationalRequirementStatus.NotApplicable,
            AttributeStructureRequirementOutcome.SatisfeitaPorAlternativa => OperationalRequirementStatus.NotApplicable,
            AttributeStructureRequirementOutcome.SemConfiguracaoSuficiente => OperationalRequirementStatus.Inconclusive,
            _ => OperationalRequirementStatus.Inconclusive
        };

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "7", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? configurationKey = null, string? nomeArquivo = null, string? pastaEncontrada = null,
            string? customAttributeName = null, CustomAttributeState? customAttributeState = null, bool? isCustomAttributeHidden = null,
            string? probableOwner = null, string? documentoNa = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "7", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                ConfigurationKey = configurationKey, NomeArquivo = nomeArquivo, PastaEncontrada = pastaEncontrada,
                CustomAttributeName = customAttributeName, CustomAttributeState = customAttributeState, IsCustomAttributeHidden = isCustomAttributeHidden,
                ResponsavelProvavel = probableOwner, DocumentoNa = documentoNa
            };
    }
}
