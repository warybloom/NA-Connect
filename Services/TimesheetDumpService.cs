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

        public async Task<string> ResetAndDumpTimesheetAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation("🚀 [TIMESHEET DUMP] Iniciando reconstrução via SQL Direto...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"TS_DUMP_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. RECUPERA DETALHES DO ARQUIVO NO ACC
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Não foi possível localizar o arquivo no ACC.");

                string actualFileName = itemDetails.DisplayName;
                string folderId = itemDetails.ParentFolderId;
                string versionUrn = itemDetails.TipVersionUrn;

                // 2. DOWNLOAD DO STREAM
                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) 
                {
                    await dl.CopyToAsync(fs);
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                byte[] fileBytes;
                int totalGeral = 0;

                // 3. PROCESSAMENTO DO EXCEL
                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper() == "LDP") ?? package.Workbook.Worksheets[0];
                    var wsTs = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper() == "TIMESHEET");
                    
                    if (wsTs == null) wsTs = package.Workbook.Worksheets.Add("TIMESHEET");

                    // Pegar filtros da LDP (X8 e Y8)
                    string filtroCR = wsLdp.Cells[8, 24].Text?.Trim();
                    string filtroCROS = wsLdp.Cells[8, 25].Text?.Trim();

                    _logger.LogInformation("🔍 Query SQL: Projeto='{Projeto}' AND doc1='{Doc1}'", filtroCR, filtroCROS);

                    // 4. BUSCA NO BANCO BI_NA (Query RAW)
                    // 👇 A VARIÁVEL É DECLARADA AQUI 👇
                    var registrosDoBanco = await _context.TbTimeSheetRealizadoDocumentos
                        .FromSqlRaw("SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}", filtroCR, filtroCROS)
                        .AsNoTracking()
                        .ToListAsync();

                    totalGeral = registrosDoBanco.Count;

                    // 5. LIMPAR E ESCREVER NA TIMESHEET
                    wsTs.Cells.Clear();

                    string[] headers = { 
                        "Nome", "2. Planejadas", "3. Trabalhadas", "4. Projeto", "5. Função", 
                        "6. Disciplina", "7. Documento Código", "8. Documento", "9. Descrição", 
                        "10. Doc 1", "11. Doc 2", "12. Doc 3", "13. Doc 4", "14. Doc 5", "15. CUSTO" 
                    };

                    for (int i = 0; i < headers.Length; i++) {
                        wsTs.Cells[1, i + 1].Value = headers[i];
                        wsTs.Cells[1, i + 1].Style.Font.Bold = true;
                    }

                    int linhaCorrente = 2;
                    // 👇 O LOOP USA O MESMO NOME DA VARIÁVEL 👇
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

                        // 🎯 FÓRMULA DE CUSTO (SUMIF)
                        wsTs.Cells[linhaCorrente, 15].Formula = $"=C{linhaCorrente}*(SUMIF('Custo Real'!C:C, TIMESHEET!A{linhaCorrente}, 'Custo Real'!I:I))";

                        linhaCorrente++;
                    }

                    if (wsTs.Dimension != null) wsTs.Cells[wsTs.Dimension.Address].AutoFitColumns();
                    
                    package.Save();
                    fileBytes = package.GetAsByteArray();
                }

                // 6. UPLOAD PARA O ACC
                using (var uploadStream = new MemoryStream(fileBytes))
                {
                    await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, folderId, actualFileName, uploadStream, accessToken);
                }

                _logger.LogInformation("✅ Sucesso: {TotalRegistros} registros injetados na TIMESHEET.", totalGeral);
                return "Dump concluído.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro crítico no Dump: {ex.Message}");
                throw;
            }
            finally 
            { 
                if (File.Exists(tempPath)) File.Delete(tempPath); 
            }
        }
    }
}