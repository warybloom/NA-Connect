using System;
using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>Resultado da avaliação de UM caminho de arquivo contra a configuração de subpastas permitidas de uma árvore (ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA, correção pós-Checkpoint 2, item 3).</summary>
    public enum AvaliacaoCaminhoPermitido
    {
        /// <summary>Há configuração e o caminho está dentro dela.</summary>
        ConfiguradaEPermitida,

        /// <summary>Há configuração e o caminho está comprovadamente FORA dela — único caso que pode gerar finding com ErrorDomain=Operacional.</summary>
        ConfiguradaENaoPermitida,

        /// <summary>Não há configuração suficiente (lista de caminhos permitidos vazia) para provar que o caminho é proibido — nunca inventa um erro operacional aqui.</summary>
        SemConfiguracaoSuficiente
    }

    /// <summary>
    /// Decide, de forma pura e testável, se o caminho relativo de um item está dentro da configuração de
    /// subpastas permitidas de uma árvore — nunca por uma whitelist curta hardcoded na regra, sempre a
    /// partir de <see cref="AuditAttributeStructureProfile.CaminhosPermitidosRelativos"/> (que por sua vez é
    /// uma projeção da configuração operacional real, não uma invenção da auditoria).
    /// </summary>
    public static class AttributePathPermissionResolver
    {
        public static AvaliacaoCaminhoPermitido Resolver(string caminhoRelativoDoItem, IReadOnlyList<string> caminhosPermitidosConfigurados)
        {
            if (caminhosPermitidosConfigurados == null || caminhosPermitidosConfigurados.Count == 0)
            {
                return AvaliacaoCaminhoPermitido.SemConfiguracaoSuficiente;
            }

            // Item diretamente na raiz já resolvida (sem sub-navegação) — trivialmente dentro; a
            // configuração de subpastas só se aplica a itens que de fato desceram para alguma subpasta.
            if (string.IsNullOrEmpty(caminhoRelativoDoItem))
            {
                return AvaliacaoCaminhoPermitido.ConfiguradaEPermitida;
            }

            bool permitido = caminhosPermitidosConfigurados.Any(permitido =>
                caminhoRelativoDoItem.StartsWith(permitido, StringComparison.OrdinalIgnoreCase));

            return permitido ? AvaliacaoCaminhoPermitido.ConfiguradaEPermitida : AvaliacaoCaminhoPermitido.ConfiguradaENaoPermitida;
        }
    }
}
