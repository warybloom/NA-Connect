using System;
using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Aggregation
{
    /// <summary>
    /// Calcula um <see cref="OperationalStageResult"/> a partir das listas de
    /// <see cref="OperationalRequirementResult"/> e <see cref="OperationalFinding"/> de UMA etapa dentro de
    /// UM projeto. Puro, sem SQL/IO — a única fonte de verdade para "aprovado/reprovado por etapa" (nunca
    /// montado à mão por uma regra ou pelo orquestrador diretamente).
    /// </summary>
    public static class RequirementStatusAggregator
    {
        public static OperationalStageResult Agregar(
            Guid executionId,
            string projectIdAcc,
            string etapa,
            string nomeEtapa,
            IReadOnlyList<OperationalRequirementResult> requisitos,
            IReadOnlyList<OperationalFinding> findings,
            DateTime iniciadoEmUtc,
            DateTime? finalizadoEmUtc)
        {
            requisitos ??= Array.Empty<OperationalRequirementResult>();
            findings ??= Array.Empty<OperationalFinding>();

            int aprovados = requisitos.Count(r => r.Status == OperationalRequirementStatus.Approved);
            int falhos = requisitos.Count(r => r.Status == OperationalRequirementStatus.Failed);
            int avisos = requisitos.Count(r => r.Status == OperationalRequirementStatus.Warning);
            int naoAplicavel = requisitos.Count(r => r.Status == OperationalRequirementStatus.NotApplicable);
            int naoExecutado = requisitos.Count(r => r.Status == OperationalRequirementStatus.NotExecuted);
            int inconclusivos = requisitos.Count(r => r.Status == OperationalRequirementStatus.Inconclusive);

            int errosOperacionais = findings.Count(f => f.ErrorDomain == OperationalAuditErrorDomain.Operacional);
            int errosAutomacao = findings.Count(f => f.ErrorDomain == OperationalAuditErrorDomain.Automacao);
            int bloqueadores = findings.Count(f => f.BloqueiaEtapa);

            int avaliados = aprovados + falhos + avisos + inconclusivos;
            decimal? percentual = avaliados > 0
                ? Math.Round(100m * aprovados / avaliados, 2)
                : null;

            var resultado = DecidirResultado(
                totalRequisitos: requisitos.Count,
                naoAplicavel: naoAplicavel,
                naoExecutado: naoExecutado,
                falhos: falhos,
                avisos: avisos,
                inconclusivos: inconclusivos,
                findings: findings,
                bloqueadores: bloqueadores);

            return new OperationalStageResult
            {
                ExecutionId = executionId,
                ProjectIdAcc = projectIdAcc,
                Etapa = etapa,
                NomeEtapa = nomeEtapa,
                Resultado = resultado,
                TotalRequisitos = requisitos.Count,
                Aprovados = aprovados,
                Falhos = falhos,
                Avisos = avisos,
                ErrosOperacionais = errosOperacionais,
                ErrosAutomacao = errosAutomacao,
                Inconclusivos = inconclusivos,
                Bloqueadores = bloqueadores,
                PercentualConformidade = percentual,
                IniciadoEmUtc = iniciadoEmUtc,
                FinalizadoEmUtc = finalizadoEmUtc
            };
        }

        /// <summary>
        /// Ordem de prioridade fixa (seção 23 da matriz da Fase 1 — nunca reduzir a só sucesso/erro):
        /// 1) sem nenhum requisito avaliado → NAO_EXECUTADA/NAO_APLICAVEL;
        /// 2) achado bloqueante da AUTOMACAO → REPROVADA_POR_AUTOMACAO (é sempre o mais grave: um bug do
        ///    robô não deve ser mascarado por um problema de dado concorrente);
        /// 3) achado bloqueante de CONFIGURACAO_PROJETO → REPROVADA_POR_CONFIGURACAO;
        /// 4) achado bloqueante de ESTRUTURA_ACC/ESTRUTURA_PLANILHA → REPROVADA_POR_ESTRUTURA;
        /// 5) requisito falho ou achado bloqueante restante (dado) → REPROVADA_POR_DADOS;
        /// 6) algum inconclusivo sem nada reprovado → INCONCLUSIVA;
        /// 7) algum aviso sem nada reprovado → APROVADA_COM_ALERTAS;
        /// 8) caso contrário → APROVADA.
        /// </summary>
        private static OperationalAuditStageStatus DecidirResultado(
            int totalRequisitos,
            int naoAplicavel,
            int naoExecutado,
            int falhos,
            int avisos,
            int inconclusivos,
            IReadOnlyList<OperationalFinding> findings,
            int bloqueadores)
        {
            if (totalRequisitos == 0)
            {
                return OperationalAuditStageStatus.NaoExecutada;
            }
            if (naoAplicavel == totalRequisitos)
            {
                return OperationalAuditStageStatus.NaoAplicavel;
            }
            if (naoExecutado == totalRequisitos)
            {
                return OperationalAuditStageStatus.NaoExecutada;
            }

            bool temBloqueioAutomacao = findings.Any(f => f.BloqueiaEtapa && f.ErrorDomain == OperationalAuditErrorDomain.Automacao);
            if (temBloqueioAutomacao) return OperationalAuditStageStatus.ReprovadaPorAutomacao;

            bool temBloqueioConfiguracao = findings.Any(f => f.BloqueiaEtapa && f.Classificacao == OperationalAuditClassification.ConfiguracaoProjeto);
            if (temBloqueioConfiguracao) return OperationalAuditStageStatus.ReprovadaPorConfiguracao;

            bool temBloqueioEstrutura = findings.Any(f => f.BloqueiaEtapa &&
                (f.Classificacao == OperationalAuditClassification.EstruturaAcc || f.Classificacao == OperationalAuditClassification.EstruturaPlanilha));
            if (temBloqueioEstrutura) return OperationalAuditStageStatus.ReprovadaPorEstrutura;

            if (falhos > 0 || bloqueadores > 0) return OperationalAuditStageStatus.ReprovadaPorDados;
            if (inconclusivos > 0) return OperationalAuditStageStatus.Inconclusiva;
            if (avisos > 0) return OperationalAuditStageStatus.AprovadaComAlertas;

            return OperationalAuditStageStatus.Aprovada;
        }
    }
}
