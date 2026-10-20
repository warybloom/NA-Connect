using System.Collections.Generic;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Um perfil de auditoria (seção 10 da correção de Checkpoint 2) — define, para um projeto ou grupo de
    /// projetos, quais estruturas de atributo (ENGENHARIA_04, PRODUCAO_10, etc.) são obrigatórias, opcionais,
    /// ou alternativas entre si. NUNCA hardcoded no código de regra — sempre lido de
    /// <c>OperationalAudit:ProjectProfiles</c> via <see cref="OperationalAuditOptions.LerConfiguracao"/>.
    /// </summary>
    public sealed class AuditProjectProfile
    {
        public List<string> RequiredAttributeStructures { get; set; } = new();
        public List<string> OptionalAttributeStructures { get; set; } = new();

        /// <summary>
        /// Estrutura -> lista de outras estruturas que, se encontradas, tornam a primeira NAO_APLICAVEL em
        /// vez de reprovada (seção 10: "alternativa a outra estrutura encontrada"). Ex.:
        /// "PRODUCAO_DOCUMENTOS_09" pode ser satisfeita alternativamente por "PRODUCAO_10" em certos perfis.
        /// </summary>
        public Dictionary<string, List<string>> AlternativeStructures { get; set; } = new();
    }
}
