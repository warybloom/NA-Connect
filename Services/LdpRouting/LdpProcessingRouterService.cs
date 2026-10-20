using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using APS.WebApp.Services.LdpTipo2;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Roteador Tipo 1 / Tipo 2 — integrado a GrdAutomationWorker.ProcessarCicloCompleto (checkpoint
    /// pré-canário): para projetos com Tipo_LDP = 2, o Worker chama este roteador em vez de executar as
    /// 17 etapas do Tipo 1. Para Tipo_LDP = 1/default, devolve informativo — o Worker continua
    /// executando o fluxo Tipo 1 exatamente como sempre executou, sem qualquer alteração.
    ///
    /// Para Tipo 2, orquestra as 4 etapas completas em sequência, cada uma podendo interromper a cadeia
    /// (nunca avança com dados parciais/inconsistentes): LDP (parse+persist) -> Cronograma (parse+persist)
    /// -> matching real Cronograma->LDP (Fase 4B, SQL) -> publicação condicional no ACC (só se algo
    /// realmente mudou). TODA a orquestração está dentro de um único try/catch — nenhuma exceção escapa
    /// deste roteador, e em nenhuma circunstância há fallback para o Tipo 1.
    /// </summary>
    public sealed class LdpProcessingRouterService : ILdpProcessingRouterService
    {
        private readonly ILdpTipo2Service _ldpTipo2Service;
        private readonly ICronogramaTipo2Service _cronogramaTipo2Service;
        private readonly ICronogramaTipo2LdpSyncService _cronogramaTipo2LdpSyncService;
        private readonly ICronogramaTipo2LdpPublicacaoService _publicacaoService;
        private readonly ILogger<LdpProcessingRouterService> _logger;

        public LdpProcessingRouterService(
            ILdpTipo2Service ldpTipo2Service,
            ICronogramaTipo2Service cronogramaTipo2Service,
            ICronogramaTipo2LdpSyncService cronogramaTipo2LdpSyncService,
            ICronogramaTipo2LdpPublicacaoService publicacaoService,
            ILogger<LdpProcessingRouterService> logger)
        {
            _ldpTipo2Service = ldpTipo2Service;
            _cronogramaTipo2Service = cronogramaTipo2Service;
            _cronogramaTipo2LdpSyncService = cronogramaTipo2LdpSyncService;
            _publicacaoService = publicacaoService;
            _logger = logger;
        }

        public async Task<ILdpProcessingResult> ProcessarAsync(
            Projeto projeto,
            string hubId,
            string accessToken,
            string ldpFileId,
            CancellationToken cancellationToken = default)
        {
            if (projeto == null) throw new ArgumentNullException(nameof(projeto));

            TipoLdp tipoResolvido = Enum.IsDefined(typeof(TipoLdp), projeto.TipoLdp) ? projeto.TipoLdp : TipoLdp.Tipo1;
            if (tipoResolvido != projeto.TipoLdp)
            {
                _logger.LogWarning(
                    $"[LDP ROUTER] Projeto: {projeto.NomeProjeto} | ProjectId: {projeto.ProjectIdAcc} | " +
                    $"Tipo_LDP: {projeto.TipoLdp} (valor não reconhecido) | Fallback seguro: {TipoLdp.Tipo1}.");
            }

            switch (tipoResolvido)
            {
                case TipoLdp.Tipo2:
                    return await ProcessarTipo2Async(projeto, hubId, accessToken, ldpFileId, cancellationToken);

                case TipoLdp.Tipo1:
                default:
                    _logger.LogInformation(
                        $"[LDP ROUTER] Projeto: {projeto.NomeProjeto} | ProjectId: {projeto.ProjectIdAcc} | " +
                        $"Tipo_LDP: {tipoResolvido} | Serviço selecionado: fluxo atual (Tipo 1) — orquestrado por " +
                        "GrdAutomationWorker, não invocado por este roteador.");

                    return new LdpProcessingRouterResult
                    {
                        TipoLdp = TipoLdp.Tipo1,
                        Sucesso = true,
                        Mensagem = "Tipo 1 é o fluxo atual, orquestrado inteiramente por GrdAutomationWorker.ProcessarCicloCompleto. " +
                                   "Este roteador não invoca essa lógica."
                    };
            }
        }

        /// <summary>
        /// Orquestra as 4 etapas do Tipo 2. Rede de segurança final: qualquer exceção não tratada em
        /// QUALQUER uma das 4 chamadas é capturada aqui — nunca propaga para o chamador (Worker), e
        /// nunca aciona nada do Tipo 1. Cada etapa que retorna erro interrompe a cadeia imediatamente.
        /// </summary>
        private async Task<ILdpProcessingResult> ProcessarTipo2Async(
            Projeto projeto,
            string hubId,
            string accessToken,
            string ldpFileId,
            CancellationToken cancellationToken)
        {
            string projectIdAcc = projeto.ProjectIdAcc;

            try
            {
                _logger.LogInformation(
                    $"[LDP ROUTER] Projeto: {projeto.NomeProjeto} | ProjectId: {projectIdAcc} | Tipo_LDP: Tipo2 | Iniciando fluxo completo.");

                // 1/4 — LDP Tipo 2 (parse + persistência transacional). Equivalente Tipo2 da Etapa 11 legada.
                var resultadoLdp = await _ldpTipo2Service.ProcessarAsync(hubId, projectIdAcc, accessToken, ldpFileId, cancellationToken);
                if (resultadoLdp.Erros.Count > 0)
                {
                    return Falha(projectIdAcc, "LDP Tipo 2", resultadoLdp.Erros, ldpOk: false, cronogramaOk: null);
                }

                // 2/4 — Cronograma Tipo 2 (localiza/baixa o .mpp, parse + persistência transacional).
                // Junto com 3/4 abaixo, é o equivalente Tipo2 da Etapa 09 legada (Cronograma -> LDP).
                var resultadoCronograma = await _cronogramaTipo2Service.ProcessarAsync(hubId, projectIdAcc, accessToken, cancellationToken);
                if (resultadoCronograma.Erros.Count > 0)
                {
                    return Falha(projectIdAcc, "Cronograma Tipo 2", resultadoCronograma.Erros, ldpOk: true, cronogramaOk: false,
                        ldpDocumentos: resultadoLdp.DocumentosEncontrados);
                }

                // 3/4 — Matching real Cronograma -> LDP (Fase 4B: SQL, nunca ativa/desativa gate).
                var resultadoSync = await _cronogramaTipo2LdpSyncService.SincronizarAsync(projectIdAcc, cancellationToken);
                if (!resultadoSync.Sucesso)
                {
                    return Falha(projectIdAcc, "Matching Cronograma->LDP", resultadoSync.Erros, ldpOk: true, cronogramaOk: false,
                        ldpDocumentos: resultadoLdp.DocumentosEncontrados,
                        cronogramaTarefasValidas: resultadoCronograma.RegistrosValidos, cronogramaTarefasAnalisadas: resultadoCronograma.TarefasAnalisadas);
                }

                // 4/4 — Publicação condicional no ACC (só se algo realmente mudou).
                var resultadoPublicacao = await _publicacaoService.PublicarSeHouverAlteracaoAsync(
                    hubId, projectIdAcc, accessToken, ldpFileId, resultadoSync.AtualizacoesParaPublicar, cancellationToken);

                // Resumo "PHYSICAL SYNC" (checkpoint LDP Tipo 2, seção 12): combina os números de matching
                // (só o sync service tem, via SQL) com o diff célula-a-célula (só a publicação tem, via
                // download/leitura real da planilha) — nenhum dos dois serviços sozinho tem os dois lados.
                _logger.LogInformation(
                    "[CRONOGRAMA TIPO 2 -> LDP][PHYSICAL SYNC] Projeto: {ProjectId} | Matches analisados={MatchesAnalisados} | " +
                    "30% atualizados={Gate30} | 60% atualizados={Gate60} | 90% atualizados={Gate90} | " +
                    "Planned Physical alterados={PlannedPhysical} | Physical Completed alterados={PhysicalCompleted} | " +
                    "Valores iguais={ValoresIguais} | Valores sem origem={ValoresSemOrigem} | Gates inativos={GatesInativos}",
                    projectIdAcc, resultadoSync.MatchesEncontrados,
                    resultadoPublicacao.Gate30FisicoAtualizado, resultadoPublicacao.Gate60FisicoAtualizado, resultadoPublicacao.Gate90FisicoAtualizado,
                    resultadoPublicacao.PlannedPhysicalAlterados, resultadoPublicacao.PhysicalCompletedAlterados,
                    resultadoPublicacao.ValoresIguaisPhysical, resultadoPublicacao.ValoresSemOrigemPhysical, resultadoSync.GatesInativos);

                string mensagemFinal =
                    $"LDP: {resultadoLdp.DocumentosEncontrados} documento(s). " +
                    $"Cronograma: {resultadoCronograma.RegistrosValidos} tarefa(s) válida(s) de {resultadoCronograma.TarefasAnalisadas}. " +
                    $"Matching: {resultadoSync.MatchesEncontrados} encontrado(s), {resultadoSync.MatchesAmbiguos} ambíguo(s), " +
                    $"{resultadoSync.CronogramasNaoEncontrados} não encontrado(s). " +
                    resultadoPublicacao.Mensagem;

                _logger.LogInformation($"[LDP ROUTER] Projeto: {projeto.NomeProjeto} | Tipo2 concluído | {mensagemFinal}");

                return new LdpProcessingRouterResult
                {
                    TipoLdp = TipoLdp.Tipo2,
                    Sucesso = resultadoPublicacao.Sucesso,
                    Mensagem = mensagemFinal,
                    LdpPersistidoComSucesso = true,
                    LdpDocumentosEncontrados = resultadoLdp.DocumentosEncontrados,
                    CronogramaSincronizadoComSucesso = true,
                    CronogramaTarefasValidas = resultadoCronograma.RegistrosValidos,
                    CronogramaTarefasAnalisadas = resultadoCronograma.TarefasAnalisadas,
                    CronogramaMatchesEncontrados = resultadoSync.MatchesEncontrados
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ [LDP ROUTER] Falha não tratada ao processar Tipo 2 para o projeto {ProjectId} — nenhum fallback para Tipo 1.",
                    projectIdAcc);

                return new LdpProcessingRouterResult
                {
                    TipoLdp = TipoLdp.Tipo2,
                    Sucesso = false,
                    Mensagem = $"Falha não tratada no fluxo Tipo 2: {ex.Message}",
                    LdpPersistidoComSucesso = null,
                    CronogramaSincronizadoComSucesso = null
                };
            }
        }

        private LdpProcessingRouterResult Falha(string projectIdAcc, string etapa, System.Collections.Generic.List<string> erros,
            bool? ldpOk, bool? cronogramaOk, int? ldpDocumentos = null, int? cronogramaTarefasValidas = null, int? cronogramaTarefasAnalisadas = null)
        {
            string mensagem = $"[{etapa}] {string.Join(" | ", erros)}";
            _logger.LogError($"❌ [LDP ROUTER] Projeto: {projectIdAcc} | Etapa '{etapa}' falhou — cadeia interrompida, etapas seguintes não executadas. {mensagem}");

            return new LdpProcessingRouterResult
            {
                TipoLdp = TipoLdp.Tipo2,
                Sucesso = false,
                Mensagem = mensagem,
                LdpPersistidoComSucesso = ldpOk,
                LdpDocumentosEncontrados = ldpDocumentos,
                CronogramaSincronizadoComSucesso = cronogramaOk,
                CronogramaTarefasValidas = cronogramaTarefasValidas,
                CronogramaTarefasAnalisadas = cronogramaTarefasAnalisadas
            };
        }
    }
}
