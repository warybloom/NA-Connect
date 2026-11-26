using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using APS.WebApp.Services;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Fonte ÚNICA dos perfis de PRODUCAO_MODELOS_3D_* (correção pós-Checkpoint 2, item "duas fontes de
    /// verdade"): lê <see cref="LdpUpdateService.PastasAtributos"/> DIRETAMENTE em tempo de execução —
    /// nunca uma cópia estática mantida manualmente dentro da auditoria. <c>PastasAtributos</c> e
    /// <c>GrupoPastaConfig</c> são <c>internal</c> (não <c>private</c>) no mesmo assembly
    /// (AccIntegration.WebApp.csproj) desde antes desta correção — acessível diretamente, sem reflection,
    /// sem parsear o arquivo .cs, sem depender de número de linha, e sem alterar
    /// <c>LdpUpdateService.cs</c> nem tornar nada mais público do que já é.
    ///
    /// Se amanhã <c>LdpUpdateService.PastasAtributos</c> mudar (novo software 3D, disciplina adicional,
    /// árvore renomeada), esta classe reflete a mudança automaticamente no próximo carregamento do
    /// assembly — não existe mais nenhum valor de caminho permitido escrito à mão dentro da auditoria
    /// para PRODUCAO_MODELOS_3D. Isso elimina o cenário descrito na correção: "a automação aceita uma
    /// pasta enquanto a auditoria gera falsamente ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA".
    /// </summary>
    public static class AuditAttributeStructureProfileProvider
    {
        /// <summary>
        /// Constrói um <see cref="AuditAttributeStructureProfile"/> independente para CADA raiz distinta
        /// encontrada em <c>CaminhosRaiz</c> da configuração real (hoje: "07 - 3D AUTHORING SOFTWARE" e
        /// "08 - FEDERATED MODEL" — mas nenhum desses nomes está hardcoded aqui; o particionamento é
        /// puramente pelo PRIMEIRO segmento de cada caminho real, e a Chave de cada perfil usa o prefixo
        /// numérico desse segmento, também extraído dinamicamente, nunca "07"/"08" literais).
        /// </summary>
        public static IReadOnlyList<AuditAttributeStructureProfile> ConstruirPerfisModelos3D()
        {
            var grupo = LdpUpdateService.PastasAtributos.FirstOrDefault(g => g.Chave == LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D);

            return ConstruirPerfisModelos3DDeCaminhos(
                grupo?.CaminhosRaiz ?? new List<string[]>(),
                grupo?.IncluirSubpastasRecursivamente ?? true,
                grupo?.Atributos);
        }

        /// <summary>
        /// Núcleo PURO e testável isoladamente (internal, ver AssemblyInfo.cs InternalsVisibleTo) —
        /// recebe os dados já extraídos (nunca acessa <c>LdpUpdateService</c> diretamente), então qualquer
        /// entrada produz a saída correspondente de forma determinística. Usado para provar, em teste, que
        /// a derivação segue QUALQUER configuração dada — nunca uma lista fixa reescrita dentro desta
        /// classe (a prova de que não existe uma segunda fonte de verdade escondida aqui).
        /// </summary>
        internal static IReadOnlyList<AuditAttributeStructureProfile> ConstruirPerfisModelos3DDeCaminhos(
            IReadOnlyList<string[]> caminhosRaiz, bool recursivo, IReadOnlyList<string>? atributos)
        {
            if (caminhosRaiz == null || caminhosRaiz.Count == 0) return Array.Empty<AuditAttributeStructureProfile>();

            var atributosObrigatorios = (atributos != null && atributos.Count > 0) ? atributos.ToArray() : Array.Empty<string>();

            var perfis = new List<AuditAttributeStructureProfile>();
            foreach (var grupoPorRaiz in caminhosRaiz.Where(c => c != null && c.Length > 0).GroupBy(c => c[0], StringComparer.OrdinalIgnoreCase))
            {
                string raiz = grupoPorRaiz.Key;
                string chave = $"PRODUCAO_MODELOS_3D_{ExtrairPrefixoOuRaiz(raiz)}";

                var caminhosPermitidos = grupoPorRaiz
                    .Select(c => string.Join("/", c.Skip(1)))
                    .Where(c => c.Length > 0)
                    .ToArray();

                perfis.Add(new AuditAttributeStructureProfile
                {
                    ChaveConfiguracao = chave,
                    AliasesRaiz = new[] { raiz },
                    Recursivo = recursivo,
                    AtributosObrigatorios = atributosObrigatorios,
                    CaminhosPermitidosRelativos = caminhosPermitidos
                });
            }
            return perfis;
        }

        /// <summary>Prefixo numérico inicial do nome da raiz ("07 - 3D AUTHORING SOFTWARE" -&gt; "07"); se a raiz não começar com dígitos, usa o texto inteiro como sufixo da chave (nunca lança, nunca inventa "07"/"08" fixos).</summary>
        private static string ExtrairPrefixoOuRaiz(string raiz)
        {
            var match = Regex.Match(raiz ?? string.Empty, @"^(\d+)");
            return match.Success ? match.Value : (raiz ?? string.Empty);
        }
    }
}
