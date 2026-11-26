using System;
using System.Collections.Generic;
using APS.WebApp.Services.CronogramaTipo2;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da sincronização Cronograma Tipo 2 → LDP Tipo 2 (Fase 4B): matching por
    /// Project_ID_ACC + Número N&amp;A normalizado + Gate, aplicado somente aos gates que a LDP já
    /// declarou ativos (Gate_Ativo = X é sempre a autoridade — este módulo nunca ativa/desativa gate).
    /// </summary>
    public sealed class CronogramaTipo2LdpSyncResult
    {
        public string ProjectIdAcc { get; set; }
        public DateTime DataExecucao { get; set; }

        public int GatesLdpAnalisados { get; set; }
        public int GatesAtivos { get; set; }
        public int GatesInativos { get; set; }

        public int MatchesEncontrados { get; set; }
        public int CronogramasNaoEncontrados { get; set; }
        public int MatchesAmbiguos { get; set; }
        public int CronogramasParaGatesInativos { get; set; }
        public int CronogramasSemGateNaLdp { get; set; }

        public int GatesAtualizadosBanco { get; set; }
        public int DocumentosComWbsAtualizado { get; set; }
        public int DocumentosComWbsDivergente { get; set; }

        public int CelulasDataBaselineAtualizadas { get; set; }
        public int CelulasDataReplanejadaAtualizadas { get; set; }
        public int CelulasPlannedPhysicalAtualizadas { get; set; }
        public int CelulasRealizadoAtualizadas { get; set; }
        public int CelulasWbsAtualizadas { get; set; }

        public List<string> Avisos { get; set; } = new List<string>();
        public List<string> Erros { get; set; } = new List<string>();

        public bool Sucesso { get; set; }

        /// <summary>Um item por gate com MATCH_ENCONTRADO já persistido no SQL nesta execução — consumido
        /// por ICronogramaTipo2LdpPublicacaoService para escrever/publicar a LDP no ACC (só se algo
        /// realmente mudou, ver CronogramaTipo2LdpPlanilhaAtualizador.HouveAlteracao). Nunca inclui
        /// gates ambíguos/inativos/não encontrados.</summary>
        public List<CronogramaTipo2LdpPlanilhaAtualizacaoItem> AtualizacoesParaPublicar { get; set; } = new List<CronogramaTipo2LdpPlanilhaAtualizacaoItem>();
    }
}
