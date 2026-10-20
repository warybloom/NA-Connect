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
    /// <summary>
    /// ETAPA 01 — reconstrução UNIVERSAL da aba TIMESHEET da LDP, para qualquer Tipo_LDP (correção
    /// 17/09/2026: regressão funcional corrigida — Gestão à Vista havia sido indevidamente tratada como
    /// substituta desta etapa para Tipo2; não existe "Etapa 01 Tipo1"/"Etapa 01 Tipo2", é uma só regra).
    /// Fonte operacional original preservada: tbTimeSheetRealizadoDocumento (banco N&A, via
    /// ApplicationDbContext.TbTimeSheetRealizadoDocumentos) — nunca gestao.TimesheetLancamento (esse é
    /// destino de outra etapa, Gestão à Vista, que LÊ o Timesheet já reconstruído por este serviço,
    /// nunca o contrário).
    /// </summary>
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

        /// <summary>
        /// Cabeçalhos fixos da aba TIMESHEET (posição 1..15) — reconstruídos do zero a cada execução,
        /// nunca herdados do conteúdo anterior da aba.
        /// </summary>
        internal static readonly string[] HeadersTimesheet =
        {
            "Nome", "2. Planejadas", "3. Trabalhadas", "4. Projeto", "5. Função",
            "6. Disciplina", "7. Documento Código", "8. Documento", "9. Descrição",
            "10. Doc 1", "11. Doc 2", "12. Doc 3", "13. Doc 4", "14. Doc 5", "15. CUSTO"
        };

        /// <summary>
        /// Escreve os 15 cabeçalhos fixos na linha 1, em negrito. Função pura (sem ACC/SQL) — testável
        /// isoladamente com EPPlus em memória.
        /// </summary>
        internal static void EscreverCabecalhosTimesheet(ExcelWorksheet wsTs)
        {
            for (int i = 0; i < HeadersTimesheet.Length; i++)
            {
                wsTs.Cells[1, i + 1].Value = HeadersTimesheet[i];
                wsTs.Cells[1, i + 1].Style.Font.Bold = true;
            }
        }

        /// <summary>
        /// Escreve uma linha por registro, a partir da linha 2, e a fórmula de CUSTO (coluna 15) em cada
        /// linha. Retorna a quantidade de linhas (= fórmulas) escritas. Função pura (sem ACC/SQL) —
        /// testável isoladamente com EPPlus em memória e uma lista de TbTimeSheetRealizadoDocumento
        /// construída à mão (POCO simples, sem DbContext).
        /// </summary>
        internal static int EscreverLinhasTimesheet(ExcelWorksheet wsTs, IReadOnlyList<TbTimeSheetRealizadoDocumento> registros)
        {
            int linha = 2;
            int escritas = 0;
            foreach (var reg in registros)
            {
                wsTs.Cells[linha, 1].Value = reg.Nome;
                wsTs.Cells[linha, 2].Value = reg.Planejadas;
                wsTs.Cells[linha, 3].Value = reg.Trabalhadas;
                wsTs.Cells[linha, 4].Value = reg.Projeto;
                wsTs.Cells[linha, 5].Value = reg.Funcao;
                wsTs.Cells[linha, 6].Value = reg.Disciplina;
                wsTs.Cells[linha, 7].Value = reg.DocumentoCodigo;
                wsTs.Cells[linha, 8].Value = reg.Documento;
                wsTs.Cells[linha, 9].Value = reg.Descricao;
                wsTs.Cells[linha, 10].Value = reg.Doc1;
                wsTs.Cells[linha, 11].Value = reg.Doc2;
                wsTs.Cells[linha, 12].Value = reg.Doc3;
                wsTs.Cells[linha, 13].Value = reg.Doc4;
                wsTs.Cells[linha, 14].Value = reg.Doc5;

                // Fórmula de CUSTO (SUMIF) — preservada exatamente como no comportamento original Tipo1.
                wsTs.Cells[linha, 15].Formula = $"=C{linha}*(SUMIF('Custo Real'!C:C, TIMESHEET!A{linha}, 'Custo Real'!I:I))";

                escritas++;
                linha++;
            }
            return escritas;
        }

        /// <summary>
        /// Conta as linhas de dados atuais da aba (exclui a linha 1, de cabeçalho). Função pura — usada
        /// tanto para medir "linhas antigas" (antes de limpar) quanto "linhas escritas" (depois de
        /// reescrever), para a validação Banco==Excel.
        /// </summary>
        internal static int ContarLinhasDeDados(ExcelWorksheet ws)
        {
            if (ws?.Dimension == null) return 0;
            return Math.Max(0, ws.Dimension.End.Row - 1);
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
                int totalGeral;
                int linhasAntigas;
                int formulasCusto;
                decimal hhTrabalhadas;

                // 3. PROCESSAMENTO DO EXCEL
                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper() == "LDP") ?? package.Workbook.Worksheets[0];
                    var wsTs = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper() == "TIMESHEET");

                    if (wsTs == null) wsTs = package.Workbook.Worksheets.Add("TIMESHEET");

                    // Correção 17/09/2026 (Etapa 01 universal — auditoria de X8/Y8): a REGRA de negócio
                    // (filtrar tbTimeSheetRealizadoDocumento por Projeto/doc1) é a mesma para qualquer
                    // Tipo_LDP — só o ACESSO FÍSICO à célula pode variar. X8/Y8 (colunas 24/25 fixas) são,
                    // por nome de cabeçalho, "CR" e "CR-OS" (confirmado: DatabaseService.
                    // SalvarDadosLdpUnificada mapeia essas mesmas colunas para @CR/@CROS). Auditoria via
                    // SQL confirmou que a LDP Tipo2 real do CHS001 tem CR/CR_OS presentes e populados
                    // ("CHS001"/"CHS001"), e que tbTimeSheetRealizadoDocumento tem 1385 registros reais
                    // com Projeto='CHS001' AND doc1='CHS001' — os mesmos valores. A LDP Tipo2 tem blocos de
                    // gate 30/60/90 que deslocam colunas subsequentes, então a posição física de CR/CR-OS
                    // pode não ser 24/25 ali — por isso a busca é por CABEÇALHO (linha 7), como já feito
                    // para "STATUS IN THE FLOW" na Etapa 05 (ver LdpHeaderLookup). Cai no valor fixo
                    // original (24/25) só quando o cabeçalho não é encontrado — preserva 100% o
                    // comportamento Tipo1 já homologado, nunca inventa uma fonte de filtro nova.
                    int colCr = LdpHeaderLookup.LocalizarColunaPorCabecalho(wsLdp, linhaCabecalho: 7, new[] { "CR" }, colunaPadrao: 24);
                    int colCrOs = LdpHeaderLookup.LocalizarColunaPorCabecalho(wsLdp, linhaCabecalho: 7, new[] { "CR-OS", "CR OS", "CROS" }, colunaPadrao: 25);

                    string filtroCR = wsLdp.Cells[8, colCr].Text?.Trim();
                    string filtroCROS = wsLdp.Cells[8, colCrOs].Text?.Trim();

                    _logger.LogInformation(
                        "[ETAPA 01][TIMESHEET][FILTROS] CR={FiltroCR} (coluna {ColCr}) | CR_OS={FiltroCROS} (coluna {ColCrOs})",
                        filtroCR, colCr, filtroCROS, colCrOs);

                    // Validação de segurança (nunca consultar/limpar com filtro vazio — evita um
                    // WHERE Projeto=NULL AND doc1=NULL que poderia devolver um resultado sem sentido).
                    if (string.IsNullOrWhiteSpace(filtroCR) || string.IsNullOrWhiteSpace(filtroCROS))
                    {
                        throw new InvalidOperationException(
                            $"[ETAPA 01][TIMESHEET] Filtros de projeto (CR/CR-OS) não encontrados na LDP (linha 8, colunas {colCr}/{colCrOs}) — a aba TIMESHEET NÃO foi alterada.");
                    }

                    // 4. BUSCA NO BANCO BI_NA (Query RAW) — fonte operacional original, preservada.
                    var registrosDoBanco = await _context.TbTimeSheetRealizadoDocumentos
                        .FromSqlRaw("SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}", filtroCR, filtroCROS)
                        .AsNoTracking()
                        .ToListAsync();

                    totalGeral = registrosDoBanco.Count;
                    hhTrabalhadas = registrosDoBanco.Sum(r => r.Trabalhadas ?? 0m);
                    _logger.LogInformation("[ETAPA 01][TIMESHEET][BANCO] Registros={Registros} | HHTrabalhadas={HHTrabalhadas}", totalGeral, hhTrabalhadas);

                    // Validação de segurança (seção "antes de limpar" do pedido): só chegamos até aqui
                    // porque a consulta SQL teve sucesso — se FromSqlRaw/ToListAsync tivesse lançado
                    // exceção, o Clear() abaixo NUNCA seria alcançado (propaga direto para o catch no
                    // fim do método) e a LDP não seria tocada.
                    linhasAntigas = ContarLinhasDeDados(wsTs);
                    _logger.LogInformation("[ETAPA 01][TIMESHEET][LIMPEZA] LinhasAntigas={LinhasAntigas}", linhasAntigas);

                    // 5. LIMPAR E ESCREVER NA TIMESHEET
                    wsTs.Cells.Clear();
                    EscreverCabecalhosTimesheet(wsTs);
                    formulasCusto = EscreverLinhasTimesheet(wsTs, registrosDoBanco);

                    _logger.LogInformation(
                        "[ETAPA 01][TIMESHEET][ESCRITA] LinhasEscritas={LinhasEscritas} | FormulasCusto={FormulasCusto}",
                        totalGeral, formulasCusto);

                    // Validação do resultado (seção "validação do resultado" do pedido): garante que
                    // nenhuma linha antiga sobrou e que nada foi perdido na escrita — nunca publica uma
                    // aba cuja contagem não bate com o banco.
                    int linhasExcel = ContarLinhasDeDados(wsTs);
                    string resultadoValidacao = linhasExcel == totalGeral ? "OK" : "DIVERGENTE";
                    _logger.LogInformation(
                        "[ETAPA 01][TIMESHEET][VALIDACAO] Banco={Banco} | Excel={Excel} | Resultado={Resultado}",
                        totalGeral, linhasExcel, resultadoValidacao);

                    if (resultadoValidacao != "OK")
                    {
                        throw new InvalidOperationException(
                            $"[ETAPA 01][TIMESHEET] Validação falhou: banco={totalGeral} vs excel={linhasExcel}. Upload cancelado.");
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

                var itemDetailsPosUpload = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                string novaVersao = itemDetailsPosUpload?.TipVersionUrn ?? versionUrn;
                _logger.LogInformation("[ETAPA 01][TIMESHEET][UPLOAD] NovaVersao={NovaVersao}", novaVersao);

                _logger.LogInformation("✅ Sucesso: {TotalRegistros} registros injetados na TIMESHEET.", totalGeral);
                return $"{totalGeral} registro(s) injetado(s) na TIMESHEET (HH trabalhadas: {hhTrabalhadas}) | Nova versão publicada.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [ETAPA 01][TIMESHEET] Erro crítico no Dump — LDP não foi publicada além do que já estava salvo.");
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
