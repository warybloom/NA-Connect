using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using NA_Connect.Services;

namespace APS.WebApp.Services
{
    public class LdpValidacaoService : ILdpValidacaoService
    {
        private readonly ILogger<LdpValidacaoService> _logger;
        private readonly DatabaseService _databaseService;

        public LdpValidacaoService(ILogger<LdpValidacaoService> logger, DatabaseService databaseService)
        {
            _logger = logger;
            _databaseService = databaseService;
        }

        public async Task<LdpValidacaoResult> ValidarDocumentoAsync(Stream arquivoStream, string nomeArquivo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nomeArquivo))
                {
                    return new LdpValidacaoResult
                    {
                        Existe = false,
                        Mensagem = "Nome do arquivo inválido."
                    };
                }

                var nomeSemExtensao = Path.GetFileNameWithoutExtension(nomeArquivo).Trim();

                var existe = await _databaseService.ExisteDocumentoAsync(nomeSemExtensao);

                return new LdpValidacaoResult
                {
                    Existe = existe,
                    Mensagem = existe
                        ? $"Documento '{nomeSemExtensao}' encontrado na base de dados."
                        : $"Não foi encontrado nenhum registro com NA_Document_Number ou Client_Document_Number procurar o planejamento para inserção do documento."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao validar o arquivo '{Arquivo}'.", nomeArquivo);
                return new LdpValidacaoResult
                {
                    Existe = false,
                    Mensagem = $"Erro ao processar o arquivo: {ex.Message}"
                };
            }
        }
    }
}