using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using APS.WebApp.Services;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Implementação real de ICronogramaTipo2LdpPublicacaoService. Replica exatamente o padrão já usado
    /// pelo Tipo 1 (LdpUpdateService.UpdateLdpFromDatabaseAsync): baixa a versão mais recente da LDP,
    /// aplica as atualizações numa cópia local (diff célula-a-célula já feito por
    /// CronogramaTipo2LdpPlanilhaAtualizador), e só chama CreateNewVersionAsync quando pelo menos uma
    /// célula realmente mudou. Nunca abre/altera o arquivo original diretamente — só a cópia local, que
    /// vira a nova versão publicada. Qualquer falha é capturada aqui (nunca propaga) e nunca deixa
    /// arquivo temporário para trás.
    ///
    /// Modo teste controlado (checkpoint LDP Tipo 2, seção 13 — "fazer primeiro um teste
    /// controlado/read-only... depois PARE"): CronogramaTipo2:PublicarNoAcc=false faz TUDO normalmente
    /// (download real, diff real, logs [PHYSICAL] reais, SQL real) exceto o único passo irreversível —
    /// CreateNewVersionAsync — que é pulado e logado como "seria publicado". Ausente/true = comportamento
    /// normal (publica de verdade), nunca desabilita publicação silenciosamente por omissão da chave.
    /// </summary>
    public sealed class CronogramaTipo2LdpPublicacaoService : ICronogramaTipo2LdpPublicacaoService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly ILogger<CronogramaTipo2LdpPublicacaoService> _logger;
        private readonly bool _publicarNoAcc;

        public CronogramaTipo2LdpPublicacaoService(AutodeskApiService autodeskApi, ILogger<CronogramaTipo2LdpPublicacaoService> logger, IConfiguration configuration)
        {
            _autodeskApi = autodeskApi;
            _logger = logger;
            _publicarNoAcc = configuration.GetValue("CronogramaTipo2:PublicarNoAcc", true);
        }

        public async Task<CronogramaTipo2LdpPublicacaoResultado> PublicarSeHouverAlteracaoAsync(
            string hubId,
            string projectId,
            string accessToken,
            string ldpFileId,
            IReadOnlyList<CronogramaTipo2LdpPlanilhaAtualizacaoItem> atualizacoes,
            CancellationToken cancellationToken = default)
        {
            var resultado = new CronogramaTipo2LdpPublicacaoResultado();

            if (atualizacoes == null || atualizacoes.Count == 0)
            {
                resultado.Sucesso = true;
                resultado.NovaVersaoPublicada = false;
                resultado.Mensagem = "[CRONOGRAMA TIPO 2 -> LDP] Nenhuma atualização de gate encontrada — nada para publicar.";
                return resultado;
            }

            string origemTemp = Path.Combine(Path.GetTempPath(), $"LDP_T2_ORIGEM_{Guid.NewGuid()}.xlsx");
            string destinoTemp = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null)
                {
                    resultado.Erros.Add("[CRONOGRAMA TIPO 2 -> LDP] Não foi possível obter os detalhes do arquivo da LDP para publicação (GetItemDetailsAsync retornou nulo).");
                    return resultado;
                }

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, itemDetails.TipVersionUrn, accessToken))
                using (var fs = new FileStream(origemTemp, FileMode.Create, FileAccess.Write))
                {
                    await dl.CopyToAsync(fs, cancellationToken);
                }

                var resultadoPlanilha = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origemTemp, destinoTemp, atualizacoes, _logger);
                resultado.Avisos.AddRange(resultadoPlanilha.Avisos);

                if (!resultadoPlanilha.Sucesso || resultadoPlanilha.Erros.Count > 0)
                {
                    resultado.Erros.AddRange(resultadoPlanilha.Erros);
                    resultado.Sucesso = false;
                    return resultado;
                }

                resultado.CelulasAtualizadas =
                    resultadoPlanilha.CelulasDataBaselineAtualizadas +
                    resultadoPlanilha.CelulasDataReplanejadaAtualizadas +
                    resultadoPlanilha.CelulasPlannedPhysicalAtualizadas +
                    resultadoPlanilha.CelulasRealizadoAtualizadas + resultadoPlanilha.CelulasWbsAtualizadas;

                resultado.Gate30FisicoAtualizado = resultadoPlanilha.Gate30FisicoAtualizado;
                resultado.Gate60FisicoAtualizado = resultadoPlanilha.Gate60FisicoAtualizado;
                resultado.Gate90FisicoAtualizado = resultadoPlanilha.Gate90FisicoAtualizado;
                resultado.PlannedPhysicalAlterados = resultadoPlanilha.CelulasPlannedPhysicalAtualizadas;
                resultado.PhysicalCompletedAlterados = resultadoPlanilha.CelulasRealizadoAtualizadas;
                resultado.ValoresIguaisPhysical = resultadoPlanilha.ValoresIguaisPhysical;
                resultado.ValoresSemOrigemPhysical = resultadoPlanilha.ValoresSemOrigemPhysical;

                if (!resultadoPlanilha.HouveAlteracao)
                {
                    resultado.Sucesso = true;
                    resultado.NovaVersaoPublicada = false;
                    resultado.Mensagem = "[CRONOGRAMA TIPO 2 -> LDP] Nenhuma alteração necessária na LDP Tipo 2 — nada publicado.";
                    _logger.LogInformation(resultado.Mensagem);
                    return resultado;
                }

                if (!_publicarNoAcc)
                {
                    resultado.Sucesso = true;
                    resultado.NovaVersaoPublicada = false;
                    resultado.Mensagem = $"[CRONOGRAMA TIPO 2 -> LDP][DRY RUN] CronogramaTipo2:PublicarNoAcc=false — {resultado.CelulasAtualizadas} célula(s) TERIAM sido atualizadas, mas NENHUMA versão nova foi publicada no ACC.";
                    _logger.LogWarning("🧪 {Mensagem} | Projeto: {ProjectId} | Arquivo: {Arquivo}", resultado.Mensagem, projectId, itemDetails.DisplayName);
                    return resultado;
                }

                using (var ul = new FileStream(destinoTemp, FileMode.Open, FileAccess.Read))
                {
                    await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, itemDetails.ParentFolderId, itemDetails.DisplayName, ul, accessToken);
                }

                resultado.Sucesso = true;
                resultado.NovaVersaoPublicada = true;
                resultado.Mensagem = $"[CRONOGRAMA TIPO 2 -> LDP] Nova versão publicada com {resultado.CelulasAtualizadas} célula(s) atualizada(s).";
                _logger.LogInformation("✅ {Mensagem} | Projeto: {ProjectId} | Arquivo: {Arquivo}", resultado.Mensagem, projectId, itemDetails.DisplayName);
            }
            catch (Exception ex)
            {
                resultado.Sucesso = false;
                resultado.Erros.Add($"[CRONOGRAMA TIPO 2 -> LDP] Falha ao publicar nova versão da LDP: {ex.Message}");
                _logger.LogError(ex, "❌ [CRONOGRAMA TIPO 2 -> LDP] Falha ao publicar nova versão da LDP para o projeto {ProjectId}.", projectId);
            }
            finally
            {
                if (File.Exists(origemTemp)) File.Delete(origemTemp);
                if (File.Exists(destinoTemp)) File.Delete(destinoTemp);
            }

            return resultado;
        }
    }
}
