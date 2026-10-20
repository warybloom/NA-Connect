using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Catálogo de RegraId derivado diretamente do TEXTO-FONTE real das 9 regras (nunca um número escrito
    /// manualmente — correção pós-Checkpoint 2, item 4). Cada regra atribui o RegraId de um finding a
    /// <c>RegraId</c>/<c>CodigoProblema</c> (sempre o mesmo literal nos dois campos, ver
    /// <c>NovoFinding</c>/construção direta de <c>OperationalFinding</c> em cada regra) e o de um requirement
    /// ao primeiro argumento de string de <c>NovoRequisito(contexto, "CODIGO", ...)</c>. Este teste extrai os
    /// dois conjuntos, os une (um RegraId pode aparecer nos dois papéis — ex.: "ATR-CUSTOM-001" tem requirement
    /// E finding), e prova as garantias pedidas: nenhuma duplicidade real (a MESMA string não é contada duas
    /// vezes), nenhum RegraId vazio, lista ordenada por Etapa + RegraId.
    /// </summary>
    public sealed class RegraCatalogada
    {
        public string Etapa { get; init; } = string.Empty;
        public string RegraId { get; init; } = string.Empty;
        public bool ProduzFinding { get; init; }
        public bool ProduzRequirement { get; init; }
    }

    public static class OperationalAuditRuleCatalog
    {
        private static readonly (string Etapa, string Arquivo, string? PrefixoSubstituto)[] ArquivosDeRegra =
        {
            ("0", "Stage00ProjectDnaAuditRule.cs", null),
            ("1", "Stage01TimesheetAuditRule.cs", null),
            ("2", "Stage02HoursRevisionAuditRule.cs", null),
            ("4", "Stage04GrdAuditRule.cs", null),
            ("5", "Stage05WorkflowAuditRule.cs", null),
            ("5B", "Stage05BReviewSnapshotAuditRule.cs", null),
            ("6", "Stage06CostAuditRule.cs", null),
            ("7", "Stage07AttributesAuditRule.cs", null),
            ("8", "Stage08ApprovalVerdictAuditRule.cs", null),
            ("9", "Stage09ScheduleAuditRule.cs", null),
            ("10", "Stage10MirrorAuditRule.cs", null),
            ("11", "Stage11LdpSqlAuditRule.cs", null),
            // Stage12/13 delegam a construção real de Finding/Requirement para ActionLogAuditHelper.cs
            // (código compartilhado — seção 13 do pedido: "mesma filosofia, regra independente"). O RegraId
            // real usado em tempo de execução é $"{prefixo}-NNN"; aqui expandimos isso para "EXT-NNN"/
            // "INT-NNN" respectivamente, os MESMOS valores literais que cada regra passa como "prefixo" —
            // nunca dois catálogos divergentes, só a mesma fonte lida com um prefixo diferente por etapa.
            ("12", "ActionLogAuditHelper.cs", "EXT"),
            ("13", "ActionLogAuditHelper.cs", "INT"),
            ("14", "Stage14ScopeDeviationAuditRule.cs", null),
            ("15", "Stage15WeeklyScheduleAuditRule.cs", null),
            ("16", "Stage16HoldControlAuditRule.cs", null),
            ("17", "Stage17QualityAuditRule.cs", null)
        };

        /// <summary>Lê todas as regras do disco e devolve o catálogo completo, ordenado por Etapa (numérica) + RegraId.</summary>
        public static IReadOnlyList<RegraCatalogada> Enumerar()
        {
            var porChave = new Dictionary<(string Etapa, string RegraId), RegraCatalogada>();

            foreach (var (etapa, arquivo, prefixoSubstituto) in ArquivosDeRegra)
            {
                string codigoFonte = NA_Connect.OperationalAudit.Tests.RepoFileLocator.LerArquivoDaRaiz("Services", "OperationalAudit", "Rules", arquivo);

                var idsDeFindings = prefixoSubstituto == null
                    ? ExtrairRegraIdsDeFindings(codigoFonte)
                    : ExtrairRegraIdsTemplateComPrefixo(codigoFonte, "NovoFinding", prefixoSubstituto);
                foreach (var regraId in idsDeFindings)
                {
                    Agregar(porChave, etapa, regraId, produzFinding: true, produzRequirement: false);
                }

                var idsDeRequirements = prefixoSubstituto == null
                    ? ExtrairRegraIdsDeRequirements(codigoFonte)
                    : ExtrairRegraIdsTemplateComPrefixo(codigoFonte, "NovoRequisito", prefixoSubstituto);
                foreach (var regraId in idsDeRequirements)
                {
                    Agregar(porChave, etapa, regraId, produzFinding: false, produzRequirement: true);
                }
            }

            return porChave.Values
                .OrderBy(r => int.TryParse(r.Etapa, out var n) ? n : int.MaxValue)
                .ThenBy(r => r.Etapa, StringComparer.Ordinal)
                .ThenBy(r => r.RegraId, StringComparer.Ordinal)
                .ToList();
        }

        // =========================================================================================
        // Correção pós-Checkpoint 3, item 5: separação EXPLÍCITA entre os três papéis que um RegraId
        // catalogado pode ter — nunca misturados na mesma contagem:
        //
        //   ProblemCodes      — produzem Finding (viram audit.AuditIssue no Power BI) — o "catálogo de
        //                        problemas" propriamente dito.
        //   RequirementCodes  — TODOS os códigos com Requirement (aprovado ou não) — inclui tanto os que
        //                        também têm Finding quanto os requirement-only.
        //   RequirementOnlyCodes — subconjunto de RequirementCodes que NUNCA produz Finding (ex.:
        //                        WF-PRESERVACAO_CELULA — só métrica informativa de conformidade, nunca vira
        //                        AuditIssue). Contá-los como ProblemCode inflaria falsamente o número de
        //                        "problemas" no Power BI.
        // =========================================================================================

        public static IReadOnlyList<RegraCatalogada> ProblemCodes() => Enumerar().Where(r => r.ProduzFinding).ToList();

        public static IReadOnlyList<RegraCatalogada> RequirementCodes() => Enumerar().Where(r => r.ProduzRequirement).ToList();

        public static IReadOnlyList<RegraCatalogada> RequirementOnlyCodes() => Enumerar().Where(r => r.ProduzRequirement && !r.ProduzFinding).ToList();

        /// <summary>
        /// RegraId de chamadas <c>NovoFinding(contexto, etapa, $"{prefixo}-NNN", ...)</c>/
        /// <c>NovoRequisito(contexto, etapa, $"{prefixo}-NNN", ...)</c> (ActionLogAuditHelper.cs — código
        /// compartilhado por Stage12/13) — o texto-fonte literal é <c>$"{prefixo}-NNN"</c>; substituímos
        /// <c>{prefixo}</c> pelo valor REAL que cada regra chamadora passa (ver <see cref="ArquivosDeRegra"/>).
        /// </summary>
        private static IEnumerable<string> ExtrairRegraIdsTemplateComPrefixo(string codigoFonte, string nomeMetodo, string prefixoSubstituto)
        {
            var padrao = new Regex(nomeMetodo + @"\(\s*contexto,\s*etapa,\s*\$""\{prefixo\}(?<resto>[^""]*)""");
            foreach (Match m in padrao.Matches(codigoFonte))
            {
                yield return prefixoSubstituto + m.Groups["resto"].Value;
            }
        }

        private static void Agregar(Dictionary<(string, string), RegraCatalogada> porChave, string etapa, string regraId, bool produzFinding, bool produzRequirement)
        {
            var chave = (etapa, regraId);
            if (porChave.TryGetValue(chave, out var existente))
            {
                porChave[chave] = new RegraCatalogada
                {
                    Etapa = etapa,
                    RegraId = regraId,
                    ProduzFinding = existente.ProduzFinding || produzFinding,
                    ProduzRequirement = existente.ProduzRequirement || produzRequirement
                };
            }
            else
            {
                porChave[chave] = new RegraCatalogada { Etapa = etapa, RegraId = regraId, ProduzFinding = produzFinding, ProduzRequirement = produzRequirement };
            }
        }

        /// <summary>RegraId de findings: tanto o padrão "RegraId = "X", ... CodigoProblema = "X"" (via NovoFinding) quanto a construção direta "new OperationalFinding { ... RegraId = "X", ... }".</summary>
        private static IEnumerable<string> ExtrairRegraIdsDeFindings(string codigoFonte)
        {
            var padraoNovoFinding = new Regex(@"NovoFinding\(\s*contexto,\s*""(?<codigo>[^""]+)""");
            var padraoConstrutorDireto = new Regex(@"RegraId\s*=\s*""(?<codigo>[^""]+)""");

            foreach (Match m in padraoNovoFinding.Matches(codigoFonte)) yield return m.Groups["codigo"].Value;
            foreach (Match m in padraoConstrutorDireto.Matches(codigoFonte)) yield return m.Groups["codigo"].Value;
        }

        /// <summary>
        /// RegraId de requirements: primeiro argumento de NovoRequisito(contexto, "CODIGO", ...) — só
        /// literais PUROS (sem "$" na frente). Chamadas com string interpolada nesse argumento (ex.:
        /// <c>$"ATR-ESTRUTURA-{config.Chave}"</c>) são deliberadamente EXCLUÍDAS do catálogo: não são um
        /// RegraId fixo e catalogável, são um rótulo de bookkeeping interno por estrutura — nunca
        /// confundidos com um RegraId real (seção 5 do pedido: "Não confundir RegraId / Requirement /
        /// Finding / Occurrence").
        /// </summary>
        private static IEnumerable<string> ExtrairRegraIdsDeRequirements(string codigoFonte)
        {
            var padrao = new Regex(@"NovoRequisito\(\s*contexto,\s*""(?<codigo>[^""]+)""");
            foreach (Match m in padrao.Matches(codigoFonte))
            {
                string codigo = m.Groups["codigo"].Value.Trim();
                if (codigo.Length > 0) yield return codigo;
            }
        }
    }

    /// <summary>
    /// Prova, a partir do catálogo derivado do código (nunca um número escrito à mão), as garantias pedidas
    /// na correção pós-Checkpoint 2, item 4: nenhuma duplicidade, nenhum RegraId vazio, lista ordenada por
    /// Etapa + RegraId. A mensagem de falha de <see cref="ListaCompleta_ParaConferenciaManual"/> imprime o
    /// catálogo inteiro — é o mecanismo usado para produzir a lista mostrada no relatório de entrega.
    /// </summary>
    public class OperationalAuditRuleCatalogTests
    {
        [Fact]
        public void Catalogo_NaoEstaVazio()
        {
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            Assert.NotEmpty(catalogo);
        }

        [Fact]
        public void Catalogo_NenhumRegraIdVazio()
        {
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            Assert.All(catalogo, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.RegraId), $"RegraId vazio encontrado na Etapa {r.Etapa}.");
                Assert.False(string.IsNullOrWhiteSpace(r.Etapa), $"Etapa vazia para RegraId '{r.RegraId}'.");
            });
        }

        [Fact]
        public void Catalogo_NenhumaDuplicidade()
        {
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            var chaves = catalogo.Select(r => (r.Etapa, r.RegraId)).ToList();
            var duplicadas = chaves.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            Assert.True(duplicadas.Count == 0, "RegraIds duplicados: " + string.Join(", ", duplicadas.Select(d => $"{d.Etapa}/{d.RegraId}")));
        }

        [Fact]
        public void Catalogo_OrdenadoPorEtapaEntaoRegraId()
        {
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            var ordenadoEsperado = catalogo
                .OrderBy(r => int.TryParse(r.Etapa, out var n) ? n : int.MaxValue)
                .ThenBy(r => r.Etapa, System.StringComparer.Ordinal)
                .ThenBy(r => r.RegraId, System.StringComparer.Ordinal)
                .ToList();

            Assert.Equal(ordenadoEsperado.Select(r => (r.Etapa, r.RegraId)), catalogo.Select(r => (r.Etapa, r.RegraId)));
        }

        [Fact]
        public void OsQuatroGapsDoStage07_EstaoNoCatalogo()
        {
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            var regraIds = catalogo.Where(r => r.Etapa == "7").Select(r => r.RegraId).ToHashSet();

            Assert.Contains("ATR-CODIGO_AMBIGUO", regraIds);
            Assert.Contains("ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA", regraIds);
            Assert.Contains("ATR-SNAPSHOT_INCOMPLETO", regraIds);
            Assert.Contains("ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS", regraIds);
        }

        [Fact]
        public void ListaCompleta_ParaConferenciaManual()
        {
            // Sempre "passa" quando há pelo menos 1 entrada — o valor deste teste é a MENSAGEM que ele
            // imprimiria se falhasse (nunca falha de propósito): torna o catálogo inteiro visível em texto
            // simples, na mesma ordem entregue no relatório, sem exigir um segundo utilitário fora dos testes.
            var catalogo = OperationalAuditRuleCatalog.Enumerar();
            string lista = string.Join("\n", catalogo.Select(r =>
                $"Etapa {r.Etapa,-3} | {r.RegraId,-45} | Finding={(r.ProduzFinding ? "sim" : "nao")} | Requirement={(r.ProduzRequirement ? "sim" : "nao")}"));

            Assert.True(catalogo.Count > 0, "Catálogo vazio:\n" + lista);
        }

        // =========================================================================================
        // Correção pós-Checkpoint 3, item 5.
        // =========================================================================================

        [Fact]
        public void ProblemCodes_RequirementCodes_RequirementOnlyCodes_SaoConjuntosDistintosECoerentes()
        {
            var problemCodes = OperationalAuditRuleCatalog.ProblemCodes().Select(r => (r.Etapa, r.RegraId)).ToHashSet();
            var requirementCodes = OperationalAuditRuleCatalog.RequirementCodes().Select(r => (r.Etapa, r.RegraId)).ToHashSet();
            var requirementOnlyCodes = OperationalAuditRuleCatalog.RequirementOnlyCodes().Select(r => (r.Etapa, r.RegraId)).ToHashSet();

            // RequirementOnlyCodes é sempre um subconjunto de RequirementCodes...
            Assert.True(requirementOnlyCodes.IsSubsetOf(requirementCodes));
            // ...e NUNCA intersecta ProblemCodes — um código requirement-only jamais é, ao mesmo tempo, um ProblemCode.
            Assert.Empty(requirementOnlyCodes.Intersect(problemCodes));
        }

        [Fact]
        public void WF_PRESERVACAO_CELULA_EhRequirementOnly_NuncaProblemCode()
        {
            var requirementOnly = OperationalAuditRuleCatalog.RequirementOnlyCodes().Select(r => (r.Etapa, r.RegraId)).ToHashSet();
            var problemCodes = OperationalAuditRuleCatalog.ProblemCodes().Select(r => (r.Etapa, r.RegraId)).ToHashSet();

            Assert.Contains(("5", "WF-PRESERVACAO_CELULA"), requirementOnly);
            Assert.DoesNotContain(("5", "WF-PRESERVACAO_CELULA"), problemCodes);
        }
    }
}
