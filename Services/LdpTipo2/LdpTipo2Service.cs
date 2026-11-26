using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using APS.WebApp.Services;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Orquestração do processamento da LDP Tipo 2: localiza a versão mais recente do arquivo indicado
    /// por ldpFileId, baixa o stream usando os métodos reais já existentes em AutodeskApiService (os
    /// mesmos usados pelo fluxo Tipo 1 — nunca reimplementados aqui), repassa o stream ao
    /// ILdpTipo2Parser e, se o resultado da leitura for válido, persiste via ILdpTipo2PersistenceService
    /// (Fase 3 — uma transação por arquivo/projeto). Nunca faz upload da LDP, nunca altera a LDP no ACC;
    /// é chamado hoje apenas por LdpProcessingRouterService, que por sua vez ainda não é chamado pelo
    /// GrdAutomationWorker — o fluxo Tipo 2 continua isolado do ciclo automático.
    /// </summary>
    public sealed class LdpTipo2Service : ILdpTipo2Service
    {
        private readonly ILogger<LdpTipo2Service> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly ILdpTipo2Parser _parser;
        private readonly ILdpTipo2PersistenceService _persistenceService;

        public LdpTipo2Service(
            ILogger<LdpTipo2Service> logger,
            AutodeskApiService autodeskApi,
            ILdpTipo2Parser parser,
            ILdpTipo2PersistenceService persistenceService)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _parser = parser;
            _persistenceService = persistenceService;
        }

        public async Task<LdpTipo2Resultado> ProcessarAsync(
            string hubId,
            string projectId,
            string accessToken,
            string ldpFileId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hubId)) throw new ArgumentException("hubId não pode ser vazio.", nameof(hubId));
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("projectId não pode ser vazio.", nameof(projectId));
            if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("accessToken não pode ser vazio.", nameof(accessToken));
            if (string.IsNullOrWhiteSpace(ldpFileId)) throw new ArgumentException("ldpFileId não pode ser vazio.", nameof(ldpFileId));

            var resultado = new LdpTipo2Resultado { ProjectIdAcc = projectId };

            // 1/2/3. Localiza a versão mais recente do item indicado por ldpFileId e baixa o stream —
            // mesmos métodos reais (GetItemDetailsAsync/DownloadFileStreamAsync) já usados pelo fluxo
            // Tipo 1 em LdpUpdateService, nunca reimplementados aqui. Envolvidos em try/catch (mesmo
            // padrão de CronogramaTipo2Service.ProcessarAsync) para que uma falha de rede/Autodesk API
            // nunca escape como exceção .NET não tratada — vira resultado.Erros, auto-contido.
            AutodeskApiService.ItemDetails itemDetails;
            try
            {
                itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null)
                {
                    const string erro = "[LDP TIPO 2] Não foi possível obter os detalhes do arquivo (GetItemDetailsAsync retornou nulo).";
                    _logger.LogError(erro);
                    resultado.Erros.Add(erro);
                    return resultado;
                }

                _logger.LogInformation(
                    $"[LDP TIPO 2] Projeto direcionado ao Tipo 2 | ProjectId: {projectId} | Arquivo: {itemDetails.DisplayName} | Iniciando leitura.");

                using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, itemDetails.TipVersionUrn, accessToken))
                {
                    // 4. Parser puro (sem Autodesk/SQL) processa o stream e devolve o resultado em memória.
                    resultado = await _parser.ProcessarAsync(stream, projectId, itemDetails.DisplayName, DateTime.UtcNow, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                string erro = $"[LDP TIPO 2] Falha ao localizar/baixar a LDP: {ex.Message}";
                _logger.LogError(ex, $"[LDP TIPO 2] Falha ao localizar/baixar a LDP do projeto {projectId}.");
                resultado.Erros.Add(erro);
                return resultado;
            }

            // 5. Validação do resultado antes de persistir: erro crítico de layout ou nenhum documento
            // encontrado significa que não há nada seguro para gravar — a leitura já registrou o motivo
            // em resultado.Erros/resultado.Avisos, só registramos aqui que a persistência foi pulada.
            if (resultado.Erros != null && resultado.Erros.Count > 0)
            {
                _logger.LogError(
                    $"❌ [LDP TIPO 2] Resultado da leitura contém {resultado.Erros.Count} erro(s) crítico(s) — persistência NÃO será executada. Projeto: {projectId}.");
                return resultado;
            }

            if (resultado.Documentos == null || resultado.Documentos.Count == 0)
            {
                _logger.LogWarning($"[LDP TIPO 2] Nenhum documento encontrado na leitura — persistência não será executada. Projeto: {projectId}.");
                return resultado;
            }

            // 6. Persistência transacional (Fase 3): nunca faz upload, nunca ativa o Worker — só grava a
            // linha principal + os três gates de cada documento numa única transação por arquivo.
            var resultadoPersistencia = await _persistenceService.PersistirAsync(resultado, cancellationToken);

            // 7. Logs do resultado (sucesso ou falha da persistência) — a falha de persistência não é
            // silenciada: fica registrada também em resultado.Erros para quem só olhar o LdpTipo2Resultado.
            if (resultadoPersistencia.Sucesso)
            {
                _logger.LogInformation(
                    $"✅ [LDP TIPO 2] Persistência concluída | Projeto: {projectId} | Arquivo: {itemDetails.DisplayName} | " +
                    $"DataSincronizacao: {resultadoPersistencia.DataSincronizacao:O} | Documentos inseridos: {resultadoPersistencia.DocumentosInseridos} | " +
                    $"Gates inseridos: {resultadoPersistencia.GatesInseridos} (ativos: {resultadoPersistencia.GatesAtivosInseridos}, inativos: {resultadoPersistencia.GatesInativosInseridos}).");
            }
            else
            {
                _logger.LogError(
                    $"❌ [LDP TIPO 2] Persistência FALHOU (rollback executado) | Projeto: {projectId} | Arquivo: {itemDetails.DisplayName} | " +
                    $"Erros: {string.Join(" | ", resultadoPersistencia.Erros)}");
                resultado.Erros.AddRange(resultadoPersistencia.Erros.Select(e => $"[Persistência] {e}"));
            }

            return resultado;
        }
    }
}
