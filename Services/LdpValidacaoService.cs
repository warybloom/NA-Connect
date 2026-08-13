using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    public class LdpValidacaoService : ILdpValidacaoService
    {
        private readonly ILogger<LdpValidacaoService> _logger;

        private static readonly string[] VARIACOES_NA_DOC =
        {
            "N&A DOCUMENT NUMBER",
            "CÓDIGO N&A",
            "CODIGO N&A"
        };

        private static readonly string[] VARIACOES_CLIENT_DOC =
        {
            "CLIENT DOCUMENT NUMBER",
            "CÓDIGO CLIENTE",
            "CODIGO CLIENTE"
        };

        public LdpValidacaoService(ILogger<LdpValidacaoService> logger)
        {
            _logger = logger;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public async Task<LdpValidacaoResult> ValidarDocumentoAsync(Stream excelStream)
        {
            try
            {
                if (excelStream == null || excelStream.Length == 0)
                {
                    return new LdpValidacaoResult
                    {
                        Existe = false,
                        Mensagem = "O arquivo Excel enviado está vazio ou é inválido."
                    };
                }

                using var memoryStream = new MemoryStream();
                await excelStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                using var package = new ExcelPackage(memoryStream);

                if (package.Workbook.Worksheets.Count == 0)
                {
                    return new LdpValidacaoResult
                    {
                        Existe = false,
                        Mensagem = "O arquivo não contém nenhuma aba."
                    };
                }

                var diagnosticoPorAba = new System.Collections.Generic.List<string>();

                // 🎯 Testa TODAS as abas até achar uma com o cabeçalho válido
                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    if (worksheet.Dimension == null)
                    {
                        diagnosticoPorAba.Add($"[{worksheet.Name}]: aba vazia");
                        continue;
                    }

                    int totalCols = worksheet.Dimension.End.Column;
                    int totalRows = worksheet.Dimension.End.Row;

                    int headerRow = -1;
                    int colNaDoc = -1;
                    int colClientDoc = -1;

                    for (int row = 1; row <= Math.Min(10, totalRows); row++)
                    {
                        for (int col = 1; col <= totalCols; col++)
                        {
                            var headerValue = NormalizarTexto(worksheet.Cells[row, col].Text);

                            if (VARIACOES_NA_DOC.Any(v => string.Equals(headerValue, v, StringComparison.OrdinalIgnoreCase)))
                                colNaDoc = col;
                            else if (VARIACOES_CLIENT_DOC.Any(v => string.Equals(headerValue, v, StringComparison.OrdinalIgnoreCase)))
                                colClientDoc = col;
                        }

                        if (colNaDoc != -1 && colClientDoc != -1)
                        {
                            headerRow = row;
                            break;
                        }
                    }

                    // Essa aba não tem o cabeçalho — registra pro diagnóstico e passa pra próxima
                    if (headerRow == -1)
                    {
                        diagnosticoPorAba.Add($"[{worksheet.Name}]: cabeçalho não encontrado");
                        continue;
                    }

                    // 🎯 Achou o cabeçalho nessa aba — varre as linhas procurando dado preenchido
                    for (int row = headerRow + 1; row <= totalRows; row++)
                    {
                        var naValue = worksheet.Cells[row, colNaDoc].Text?.Trim();
                        var clientValue = worksheet.Cells[row, colClientDoc].Text?.Trim();

                        if (!string.IsNullOrEmpty(naValue) && !string.IsNullOrEmpty(clientValue))
                        {
                            return new LdpValidacaoResult
                            {
                                Existe = true,
                                Mensagem = $"Planilha LDP validada com sucesso (aba '{worksheet.Name}')."
                            };
                        }
                    }

                    diagnosticoPorAba.Add($"[{worksheet.Name}]: cabeçalho ok, mas nenhuma linha com NA e Client preenchidos juntos");
                }

                // Nenhuma aba passou na validação
                return new LdpValidacaoResult
                {
                    Existe = false,
                    Mensagem = $"Não foi encontrado o COD N&A e nem o COD CLIENTE"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao ler a planilha Excel.");
                return new LdpValidacaoResult
                {
                    Existe = false,
                    Mensagem = $"Erro ao processar o arquivo Excel: {ex.Message}"
                };
            }
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return string.Empty;
            return texto
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\u00A0', ' ')
                .Trim();
        }
    }
}