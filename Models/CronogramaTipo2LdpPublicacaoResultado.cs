using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da publicação condicional da LDP Tipo 2 no ACC (Fase de checkpoint pré-canário): só
    /// baixa/escreve/publica quando há atualizações de gate a aplicar, e só efetivamente cria uma nova
    /// versão quando pelo menos uma célula realmente mudou de valor (nunca publica incondicionalmente).
    /// </summary>
    public sealed class CronogramaTipo2LdpPublicacaoResultado
    {
        public bool Sucesso { get; set; }

        /// <summary>true somente quando CreateNewVersionAsync foi de fato chamado.</summary>
        public bool NovaVersaoPublicada { get; set; }

        public int CelulasAtualizadas { get; set; }

        /// <summary>Repasse do diff físico feito por CronogramaTipo2LdpPlanilhaAtualizador — usado pelo
        /// resumo "PHYSICAL SYNC" (seção 12 do checkpoint LDP Tipo 2), montado por quem tem também os
        /// números de matching (LdpProcessingRouterService, que já recebe CronogramaTipo2LdpSyncResult).</summary>
        public int Gate30FisicoAtualizado { get; set; }
        public int Gate60FisicoAtualizado { get; set; }
        public int Gate90FisicoAtualizado { get; set; }
        public int PlannedPhysicalAlterados { get; set; }
        public int PhysicalCompletedAlterados { get; set; }
        public int ValoresIguaisPhysical { get; set; }
        public int ValoresSemOrigemPhysical { get; set; }

        public string Mensagem { get; set; }

        public List<string> Avisos { get; set; } = new List<string>();
        public List<string> Erros { get; set; } = new List<string>();
    }
}
