using System;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>Resultado da resolução de UM currentStepId contra a definição de um workflow (WF-007/SNP-001).</summary>
    public enum WorkflowStepResolutionOutcome
    {
        /// <summary>A definição do workflow não pôde ser obtida — evidência insuficiente, nunca "não resolvido".</summary>
        DefinicaoIndisponivel,

        /// <summary>Nome resolvido por algum dos quatro níveis (direto/recursivo/JSON embutido/XML embutido).</summary>
        EtapaResolvida,

        /// <summary>Definição carregada, mas currentStepId não apareceu em nenhum id/stepId/laneId/key/nodeId, nem em texto/JSON/XML embutido.</summary>
        EtapaAusenteNaDefinicao,

        /// <summary>Nó encontrado pelo ID, mas nenhum campo name/displayName/label/title (nele ou em até 6 ancestrais) continha um nome.</summary>
        EtapaPresenteSemNome
    }

    /// <summary>
    /// Correção pós-Checkpoint 3 (WF-007): reprodução PURA e completa, em código próprio da auditoria, da
    /// MESMA resolução de <c>currentStepId</c> → nome de etapa usada pela automação real —
    /// <c>Services\AccReviewWorkflowService.cs</c>, método <c>ResolverNomeEtapaAtualAsync</c> (linhas
    /// 1081-1207) e seus quatro helpers privados (<c>EncontrarEtapaDireta</c>,
    /// <c>EncontrarObjetoDaEtapaRecursivamente</c>, <c>BuscarStringContendoStepId</c> +
    /// <c>ResolverNomeDentroDeJsonInterno</c>/<c>ResolverNomeDentroDeXmlInterno</c>), NUNCA chamado
    /// diretamente (arquivo protegido). Reproduz os QUATRO níveis reais, na mesma ordem:
    ///
    /// 1) Direto: <c>steps[].id == currentStepId</c> (linhas 1209-1220);
    /// 2) Recursivo: qualquer nó descendente cujo campo id/stepId/laneId/key/nodeId (nesta ordem, mesmos
    ///    nomes de <c>CamposIdEtapa</c>, linha 1071) bata com currentStepId (linhas 1222-1247), com o nome
    ///    procurado no próprio nó e, se ausente, em até 6 ancestrais (mesmos campos
    ///    name/displayName/label/title de <c>CamposNomeEtapa</c>, linha 1072 — linhas 1249-1292);
    /// 3) JSON embutido: quando currentStepId aparece dentro de uma STRING da definição que por sua vez é
    ///    um JSON válido, repete os mesmos campos de id/nome dentro desse JSON interno (linhas 1300-1354);
    /// 4) XML embutido: mesma ideia, mas o valor do elemento OU um atributo bate com currentStepId, e o
    ///    nome vem de um atributo (no próprio elemento ou no pai) com um dos nomes de CamposNomeEtapa
    ///    (linhas 1356-1381).
    ///
    /// Usa Newtonsoft.Json.Linq (já uma dependência direta do projeto, <c>AccIntegration.WebApp.csproj</c>)
    /// deliberadamente, e não System.Text.Json: a etapa 2/3 precisa de navegação para o NÓ PAI
    /// (<c>JToken.Parent</c>), que <c>JsonElement</c> do System.Text.Json não oferece — usar outra
    /// biblioteca aqui não duplica nem contorna nada do arquivo protegido, só reflete uma limitação real da
    /// API do .NET.
    ///
    /// Só <see cref="WorkflowStepResolutionOutcome.EtapaAusenteNaDefinicao"/> e
    /// <see cref="WorkflowStepResolutionOutcome.EtapaPresenteSemNome"/> representam "os quatro níveis rodaram
    /// por completo e mesmo assim não resolveram" — únicos casos em que a regra pode gerar WF-007.
    /// <see cref="WorkflowStepResolutionOutcome.DefinicaoIndisponivel"/> significa "não sabemos", nunca "não
    /// resolvido" — a regra chamadora NUNCA gera WF-007 nesse caso.
    /// </summary>
    public static class WorkflowStepNameResolver
    {
        private static readonly string[] CamposIdEtapa = { "id", "stepId", "laneId", "key", "nodeId" };
        private static readonly string[] CamposNomeEtapa = { "name", "displayName", "label", "title" };

        public static (string? NomeEtapa, WorkflowStepResolutionOutcome Resultado) Resolver(JObject? definicaoWorkflow, string currentStepId)
        {
            if (definicaoWorkflow == null || string.IsNullOrWhiteSpace(currentStepId))
            {
                return (null, WorkflowStepResolutionOutcome.DefinicaoIndisponivel);
            }

            JObject? objetoEtapa = EncontrarEtapaDireta(definicaoWorkflow, currentStepId)
                ?? EncontrarObjetoDaEtapaRecursivamente(definicaoWorkflow, currentStepId);

            string? nomeEtapa = null;
            if (objetoEtapa != null)
            {
                nomeEtapa = ExtrairNomeOriginalEtapa(objetoEtapa) ?? ExtrairNomeOriginalEtapaAncestral(objetoEtapa);
            }

            if (string.IsNullOrWhiteSpace(nomeEtapa))
            {
                var achado = BuscarStringContendoStepId(definicaoWorkflow, currentStepId);
                if (achado != null)
                {
                    nomeEtapa = achado.Value.Formato switch
                    {
                        "JSON" when achado.Value.JsonInterno != null => ResolverNomeDentroDeJsonInterno(achado.Value.JsonInterno, currentStepId),
                        "XML" when achado.Value.XmlInterno != null => ResolverNomeDentroDeXmlInterno(achado.Value.XmlInterno, currentStepId),
                        _ => null
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(nomeEtapa)) return (nomeEtapa, WorkflowStepResolutionOutcome.EtapaResolvida);
            return objetoEtapa == null
                ? (null, WorkflowStepResolutionOutcome.EtapaAusenteNaDefinicao)
                : (null, WorkflowStepResolutionOutcome.EtapaPresenteSemNome);
        }

        private static JObject? EncontrarEtapaDireta(JObject definicao, string currentStepId) =>
            definicao["steps"]?.OfType<JObject>()
                .FirstOrDefault(step => string.Equals(step["id"]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase));

        private static JObject? EncontrarObjetoDaEtapaRecursivamente(JObject definicao, string currentStepId)
        {
            foreach (JObject objeto in definicao.DescendantsAndSelf().OfType<JObject>())
            {
                foreach (var campo in CamposIdEtapa)
                {
                    if (string.Equals(objeto[campo]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase)) return objeto;
                }
            }
            return null;
        }

        private static string? ExtrairNomeOriginalEtapa(JObject objeto)
        {
            foreach (var campo in CamposNomeEtapa)
            {
                string? valor = objeto[campo]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(valor)) return valor;
            }
            return null;
        }

        private static string? ExtrairNomeOriginalEtapaAncestral(JObject objetoEtapa)
        {
            JToken? atual = objetoEtapa.Parent;
            int subidas = 0;
            while (atual != null && subidas < 6)
            {
                if (atual is JObject objAncestral)
                {
                    var nome = ExtrairNomeOriginalEtapa(objAncestral);
                    if (!string.IsNullOrWhiteSpace(nome)) return nome;
                }
                atual = atual.Parent;
                subidas++;
            }
            return null;
        }

        private static (string Formato, JToken? JsonInterno, XDocument? XmlInterno)? BuscarStringContendoStepId(JObject definicao, string currentStepId)
        {
            foreach (JValue valor in definicao.DescendantsAndSelf().OfType<JValue>().Where(v => v.Type == JTokenType.String))
            {
                string? texto = valor.Value?.ToString();
                if (string.IsNullOrEmpty(texto) || texto.IndexOf(currentStepId, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string formato = "TEXTO";
                JToken? jsonInterno = null;
                XDocument? xmlInterno = null;
                string inicioConteudo = texto.TrimStart();

                if (inicioConteudo.StartsWith("{", StringComparison.Ordinal) || inicioConteudo.StartsWith("[", StringComparison.Ordinal))
                {
                    try { jsonInterno = JToken.Parse(texto); formato = "JSON"; } catch { /* não é JSON válido — permanece TEXTO */ }
                }
                else if (inicioConteudo.StartsWith("<", StringComparison.Ordinal))
                {
                    try { xmlInterno = XDocument.Parse(texto); formato = "XML"; } catch { /* não é XML válido — permanece TEXTO */ }
                }

                return (formato, jsonInterno, xmlInterno);
            }
            return null;
        }

        private static string? ResolverNomeDentroDeJsonInterno(JToken jsonInterno, string currentStepId)
        {
            if (jsonInterno is not JContainer container) return null;
            foreach (JObject objeto in container.DescendantsAndSelf().OfType<JObject>())
            {
                foreach (var campoId in CamposIdEtapa)
                {
                    if (string.Equals(objeto[campoId]?.ToString(), currentStepId, StringComparison.OrdinalIgnoreCase))
                    {
                        var nome = ExtrairNomeOriginalEtapa(objeto);
                        if (!string.IsNullOrWhiteSpace(nome)) return nome;
                    }
                }
            }
            return null;
        }

        private static string? ResolverNomeDentroDeXmlInterno(XDocument xmlInterno, string currentStepId)
        {
            if (xmlInterno.Root == null) return null;
            foreach (var elemento in xmlInterno.Descendants())
            {
                bool idBate = string.Equals(elemento.Value?.Trim(), currentStepId, StringComparison.OrdinalIgnoreCase) ||
                              elemento.Attributes().Any(a => string.Equals(a.Value, currentStepId, StringComparison.OrdinalIgnoreCase));
                if (!idBate) continue;

                foreach (var nomeAttr in CamposNomeEtapa)
                {
                    var atributo = elemento.Attribute(nomeAttr) ?? elemento.Parent?.Attribute(nomeAttr);
                    if (atributo != null && !string.IsNullOrWhiteSpace(atributo.Value)) return atributo.Value;
                }
            }
            return null;
        }
    }
}
