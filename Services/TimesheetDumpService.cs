using AccIntegration.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class TimesheetDumpService
    {
        private readonly ApplicationDbContext _context;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TimesheetDumpService> _logger;

        public TimesheetDumpService(ApplicationDbContext context, AutodeskApiService autodeskApi, IConfiguration configuration, ILogger<TimesheetDumpService> logger)
        {
            _context = context;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> ResetAndDumpTimesheetAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("🚀 [TIMESHEET DUMP] Iniciando reconstrução via SQL Direto...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"TS_DUMP_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. TENTA LOCALIZAR A PASTA (PT ou EN)
                // A correção está aqui: Ele tenta as duas línguas. Se não achar, retorna NULL mas NÃO TRAVA.
                var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, accessToken);

                // --- PROTEÇÃO CONTRA CRASH (O que faltava no V48) ---
                if (string.IsNullOrEmpty(folderId))
                {
                    _logger.LogError("❌ ERRO NO STEP 0: Nenhuma pasta de Gestão encontrada (Nem PT, nem EN).");
                    // Retorna mensagem de erro limpa em vez de travar o robô
                    return "Falha: Pasta não encontrada. O robô pulará esta etapa.";
                }
                // ----------------------------------------------------

                // 2. TENTA LOCALIZAR O ARQUIVO
                string ldpSearchName = GetLdpFileNameFromConfig(projectId);
                _logger.LogInformation($"🔍 Buscando arquivo '{ldpSearchName}' na pasta encontrada...");

                var (itemId, actualFileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, ldpSearchName, accessToken);

                if (string.IsNullOrEmpty(itemId))
                {
                    _logger.LogError($"❌ ARQUIVO NÃO ENCONTRADO: '{ldpSearchName}' não existe na pasta.");
                    return "Falha: Arquivo LDP inexistente (Step 0 pulado).";
                }

                // 3. DOWNLOAD E PROCESSAMENTO (Segue normal se achou tudo)
                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, itemId, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) await dl.CopyToAsync(fs);

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                int totalGeral = 0;

                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    // Tenta achar LDP (Case Insensitive para evitar erro bobo)
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    var wsTs = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("TIMESHEET", StringComparison.OrdinalIgnoreCase));

                    if (wsLdp == null)
                    {
                        _logger.LogError("❌ O arquivo Excel foi baixado, mas NÃO tem a aba 'LDP'.");
                        return "Erro: Aba LDP inexistente.";
                    }

                    if (wsTs == null) wsTs = package.Workbook.Worksheets.Add("TIMESHEET");

                    // Extrai filtros
                    string filtroCR = wsLdp.Cells[8, 24].Text?.Trim();
                    string filtroCROS = wsLdp.Cells[8, 25].Text?.Trim();
                    _logger.LogInformation($"🔍 Filtros LDP: Projeto='{filtroCR}' | Doc1='{filtroCROS}'");

                    // Busca no Banco
                    var sqlString = "SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}";
                    var registrosDoBanco = await _context.TbTimeSheetRealizadoDocumentos
                        .FromSqlRaw(sqlString, filtroCR, filtroCROS)
                        .AsNoTracking()
                        .ToListAsync();

                    totalGeral = registrosDoBanco.Count;

                    // Escreve Timesheet
                    wsTs.Cells.Clear();
                    // Localize esta linha e adicione o 15. CUSTO
                    string[] headers = { "Nome", "2. Planejadas", "3. Trabalhadas", "4. Projeto", "5. Função", "6. Disciplina", "7. Documento Código", "8. Documento", "9. Descrição", "10. Doc 1", "11. Doc 2", "12. Doc 3", "13. Doc 4", "14. Doc 5", "15. CUSTO" };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        wsTs.Cells[1, i + 1].Value = headers[i];
                        wsTs.Cells[1, i + 1].Style.Font.Bold = true;
                    }

                    int linhaCorrente = 2;
                    foreach (var reg in registrosDoBanco)
                    {
                        wsTs.Cells[linhaCorrente, 1].Value = reg.Nome;
                        wsTs.Cells[linhaCorrente, 2].Value = reg.Planejadas;
                        wsTs.Cells[linhaCorrente, 3].Value = reg.Trabalhadas;
                        wsTs.Cells[linhaCorrente, 4].Value = reg.Projeto;
                        wsTs.Cells[linhaCorrente, 5].Value = reg.Funcao;
                        wsTs.Cells[linhaCorrente, 6].Value = reg.Disciplina;
                        wsTs.Cells[linhaCorrente, 7].Value = reg.DocumentoCodigo;
                        wsTs.Cells[linhaCorrente, 8].Value = reg.Documento;
                        wsTs.Cells[linhaCorrente, 9].Value = reg.Descricao;
                        wsTs.Cells[linhaCorrente, 10].Value = reg.Doc1;
                        wsTs.Cells[linhaCorrente, 11].Value = reg.Doc2;
                        wsTs.Cells[linhaCorrente, 12].Value = reg.Doc3;
                        wsTs.Cells[linhaCorrente, 13].Value = reg.Doc4;
                        wsTs.Cells[linhaCorrente, 14].Value = reg.Doc5;
                        // Adicione logo após o wsTs.Cells[linhaCorrente, 14].Value = reg.Doc5;
                        wsTs.Cells[linhaCorrente, 15].Formula = $"C{linhaCorrente}*(SUMIF('Custo Real'!C:C, Timesheet!A{linhaCorrente}, 'Custo Real'!I:I))";
                        wsTs.Cells[linhaCorrente, 15].Style.Numberformat.Format = "R$ #,##0.00";
                        linhaCorrente++;
                    }
                    if (wsTs.Dimension != null) wsTs.Cells[wsTs.Dimension.Address].AutoFitColumns();
                    package.Save();
                }

                // 4. UPLOAD
                using (var ul = new FileStream(tempPath, FileMode.Open))
                    await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, actualFileName, ul, accessToken);

                _logger.LogInformation($"✅ SUCESSO STEP 0: {totalGeral} registros atualizados.");
                return "Dump concluído.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro Controlado no Dump: {ex.Message}");
                return $"Erro Step 0: {ex.Message}";
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }

        // --- LÓGICA DE BUSCA INTELIGENTE (PT -> EN) ---
        private async Task<string> FindFolderIdByAlternativePathsAsync(string hubId, string projectId, string accessToken)
        {
            var paths = new[] {
                new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },      // Tenta Português
                new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }  // Tenta Inglês
            };

            foreach (var p in paths)
            {
                try
                {
                    var id = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, p, accessToken);
                    if (!string.IsNullOrEmpty(id))
                    {
                        // Achou! Retorna o ID
                        return id;
                    }
                }
                catch
                {
                    // Se der erro (ex: pasta não existe), silencia e tenta a próxima
                }
            }
            // Se tentou tudo e não achou nada, retorna null (mas o método principal agora trata isso!)
            return null;
        }

        private string GetLdpFileNameFromConfig(string projectId)
        {
            var name = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];
            return string.IsNullOrEmpty(name) ? _configuration["ProjectSettings:Default:LdpFileName"] : name;
        }
    }
}