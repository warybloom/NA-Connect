using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>Resultado da avaliação de obrigatoriedade de UMA estrutura de atributo para UM projeto.</summary>
    public enum AttributeStructureRequirementOutcome
    {
        /// <summary>Estrutura encontrada — nada a reportar como ausência.</summary>
        Presente,

        /// <summary>Obrigatória e ausente — gera finding (seção 10).</summary>
        ObrigatoriaAusente,

        /// <summary>Opcional e ausente — NOT_APPLICABLE/informativo, nunca finding bloqueante.</summary>
        OpcionalAusente,

        /// <summary>Obrigatória, ausente, mas uma estrutura alternativa configurada foi encontrada — NOT_APPLICABLE.</summary>
        SatisfeitaPorAlternativa,

        /// <summary>Nem obrigatória nem opcional no perfil — configuração insuficiente para decidir — INCONCLUSIVE.</summary>
        SemConfiguracaoSuficiente
    }

    /// <summary>
    /// Decide, de forma pura e testável (seção 10 da correção de Checkpoint 2), o que uma estrutura de
    /// atributo ausente/presente significa para UM projeto, à luz do <see cref="AuditProjectProfile"/>
    /// aplicável — nunca assume que toda pasta 04/07/08/09/10 é obrigatória em todo projeto.
    /// </summary>
    public static class AttributeStructureRequirementResolver
    {
        public static AttributeStructureRequirementOutcome Resolver(
            string chaveDaEstrutura,
            bool estruturaEncontrada,
            AuditProjectProfile? perfil,
            IReadOnlySet<string> estruturasEncontradasNoProjeto)
        {
            if (estruturaEncontrada) return AttributeStructureRequirementOutcome.Presente;

            if (perfil == null) return AttributeStructureRequirementOutcome.SemConfiguracaoSuficiente;

            bool obrigatoria = perfil.RequiredAttributeStructures.Contains(chaveDaEstrutura);
            bool opcional = perfil.OptionalAttributeStructures.Contains(chaveDaEstrutura);

            if (!obrigatoria && !opcional) return AttributeStructureRequirementOutcome.SemConfiguracaoSuficiente;

            if (obrigatoria)
            {
                if (perfil.AlternativeStructures.TryGetValue(chaveDaEstrutura, out var alternativas) &&
                    alternativas.Any(estruturasEncontradasNoProjeto.Contains))
                {
                    return AttributeStructureRequirementOutcome.SatisfeitaPorAlternativa;
                }
                return AttributeStructureRequirementOutcome.ObrigatoriaAusente;
            }

            return AttributeStructureRequirementOutcome.OpcionalAusente;
        }
    }
}
