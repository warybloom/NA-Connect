using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Resolve o <see cref="CustomAttributeState"/> de UM valor de atributo já lido — puro, testável (seção
    /// 9 do Checkpoint 2). NUNCA afirma <see cref="CustomAttributeState.PresentHidden"/> só porque a
    /// definição não apareceu numa resposta da API: só quando
    /// <see cref="AccCustomAttributeValueRef.EvidenciaVisibilidadeExplicita"/> vier explicitamente true —
    /// e a API real do ACC hoje nunca preenche esse campo (ver <c>AuditAccReadOnlyAdapter</c>), então na
    /// prática hoje o resultado para "sem valor" é sempre <see cref="CustomAttributeState.ApiVisibilityInconclusive"/>
    /// ou <see cref="CustomAttributeState.Missing"/> (quando alguma fonte explícita provar ausência), nunca
    /// PresentHidden por adivinhação.
    /// </summary>
    public static class CustomAttributeStateResolver
    {
        public static CustomAttributeState ResolverEstado(AccCustomAttributeValueRef? valor)
        {
            if (valor == null) return CustomAttributeState.Missing;
            if (valor.ValorPresente) return CustomAttributeState.PresentVisible;
            if (valor.EvidenciaVisibilidadeExplicita == true) return CustomAttributeState.PresentHidden;
            if (valor.EvidenciaVisibilidadeExplicita == false) return CustomAttributeState.Missing;
            return CustomAttributeState.ApiVisibilityInconclusive;
        }

        public static bool DefinicaoEhAmbigua(IReadOnlyList<AccCustomAttributeDefinitionRef> definicoes, string nomeAtributo) =>
            definicoes.Count(d => Normalizar(d.Name) == Normalizar(nomeAtributo)) > 1;

        public static bool DefinicaoExiste(IReadOnlyList<AccCustomAttributeDefinitionRef> definicoes, string nomeAtributo) =>
            definicoes.Any(d => Normalizar(d.Name) == Normalizar(nomeAtributo));

        private static string Normalizar(string valor) => valor.Trim().ToUpperInvariant();
    }
}
