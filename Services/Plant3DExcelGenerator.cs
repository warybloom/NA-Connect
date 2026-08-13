using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace APS.WebApp.Services
{
    public class Plant3DExcelGenerator
    {
        public Plant3DExcelGenerator()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public byte[] GerarRelatorioExcel(byte[] templateBytes, string reportType, List<Dictionary<string, object>> data)
        {
            if (data == null || data.Count == 0) return templateBytes; // Retorna o template vazio se não houver dados

            using (var memoryStream = new MemoryStream())
            {
                using (var package = new ExcelPackage(new MemoryStream(templateBytes)))
                {
                    // ========================================================
                    // 1. CONFIGURAÇÃO DAS REGRAS POR TIPO DE RELATÓRIO
                    // ========================================================
                    int startRowData = 12;
                    int maxRow = 50;
                    string sheetNamePrefix = "DATA";
                    bool doMerge = false;
                    var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    switch (reportType.ToUpper())
                    {
                        case "VALVE LIST":
                            startRowData = 7; maxRow = 49; sheetNamePrefix = "VALVE LIST";
                            colMap.Add("REV", 1); colMap.Add("SubArea", 2); colMap.Add("Tag", 3);
                            colMap.Add("ClassName", 4); colMap.Add("Size", 5); colMap.Add("Material", 6);
                            colMap.Add("TagLine", 7); colMap.Add("DwgName", 8);
                            break;

                        case "INSTRUMENTATION":
                            startRowData = 7; maxRow = 49; sheetNamePrefix = "INSTRUMENT LIST"; doMerge = true;
                            colMap.Add("MergeKey", 1); colMap.Add("Tag", 2); colMap.Add("Type", 3); colMap.Add("Service", 4);
                            colMap.Add("Title", 5); colMap.Add("LineEquipment_Calc", 6); colMap.Add("Datasheet", 7); colMap.Add("SignalType", 8);
                            break;

                        case "LINE LIST":
                            startRowData = 8; maxRow = 46; sheetNamePrefix = "LINE LIST";
                            colMap.Add("Item", 1); colMap.Add("NominalSize", 2); colMap.Add("ServiceColor", 3); colMap.Add("SubArea", 4);
                            colMap.Add("LineNumber", 5); colMap.Add("NominalSpec", 6); colMap.Add("InsulationType", 7); colMap.Add("InsulationThickness", 8);
                            colMap.Add("DwgName", 9); colMap.Add("PipeLineTo", 10); colMap.Add("PipeLineFrom", 11);
                            break;

                        case "EQUIPMENT":
                            startRowData = 8; maxRow = 49; sheetNamePrefix = "EQUIPMENT LIST";
                            colMap.Add("Area", 1); colMap.Add("Tag", 2); colMap.Add("EquipmentType", 3); 
                            colMap.Add("Quant", 4); colMap.Add("DwgName", 17);
                            break;
                    }

                    // ========================================================
                    // 2. INICIALIZAÇÃO DA ABA E PAGINAÇÃO
                    // ========================================================
                    var sheetLayoutTemplate = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("SHEET 1") || w.Name.Contains("DATA")) 
                                              ?? package.Workbook.Worksheets[0];

                    string originalTemplateName = sheetLayoutTemplate.Name;
                    var currentWs = package.Workbook.Worksheets.Copy(originalTemplateName, $"{sheetNamePrefix} - Page 1");
                    package.Workbook.Worksheets.Delete(originalTemplateName);

                    int currentRow = startRowData;
                    int currentSheetIndex = 1;
                    int itemCounter = 1;

                    int mergeCol = colMap.ContainsKey("MergeKey") ? colMap["MergeKey"] : 1;
                    int mergeStartRow = currentRow;
                    string lastMergeKey = null;

                    // Limpa a aba clonada
                    currentWs.Cells[startRowData, 1, maxRow + 5, 30].Value = null;
                    currentWs.Cells[startRowData, 1, maxRow + 5, 30].Merge = false;

                    // ========================================================
                    // 3. PREENCHIMENTO DOS DADOS
                    // ========================================================
                    foreach (var rowData in data)
                    {
                        // Quebra de página
                        if (currentRow > maxRow)
                        {
                            if (doMerge && currentRow > mergeStartRow) ApplyMerge(currentWs, mergeCol, mergeStartRow, currentRow - 1);

                            currentSheetIndex++;
                            var newWs = package.Workbook.Worksheets.Copy(currentWs.Name, $"{sheetNamePrefix} - Page {currentSheetIndex}");
                            newWs.Cells[startRowData, 1, maxRow + 5, 30].Value = null;
                            newWs.Cells[startRowData, 1, maxRow + 5, 30].Merge = false;

                            currentWs = newWs;
                            currentRow = startRowData;
                            mergeStartRow = currentRow;
                            lastMergeKey = null;
                        }

                        // Coluna de Item Automática (Se tiver "Item" no map)
                        if (colMap.ContainsKey("Item")) rowData["Item"] = itemCounter++;

                        foreach (var pair in colMap)
                        {
                            string key = pair.Key;
                            int colIndex = pair.Value;

                            if (key.Equals("MergeKey", StringComparison.OrdinalIgnoreCase) && !doMerge) continue;

                            var matchingKey = rowData.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                            if (matchingKey != null && rowData[matchingKey] != null)
                            {
                                currentWs.Cells[currentRow, colIndex].Value = rowData[matchingKey];
                            }
                        }

                        if (doMerge)
                        {
                            string currentKey = rowData.ContainsKey("MergeKey") ? rowData["MergeKey"]?.ToString() : Guid.NewGuid().ToString();
                            if (currentRow == startRowData) { lastMergeKey = currentKey; mergeStartRow = currentRow; }
                            else if (currentKey != lastMergeKey)
                            {
                                if (lastMergeKey != null && currentRow > mergeStartRow) ApplyMerge(currentWs, mergeCol, mergeStartRow, currentRow - 1);
                                mergeStartRow = currentRow;
                                lastMergeKey = currentKey;
                            }
                        }
                        currentRow++;
                    }

                    if (doMerge && currentRow > mergeStartRow) ApplyMerge(currentWs, mergeCol, mergeStartRow, currentRow - 1);

                    // ========================================================
                    // 4. RODAPÉ (PAGINAÇÃO X OF Y)
                    // ========================================================
                    int totalFixas = package.Workbook.Worksheets.Count(w => w.Name.Contains("Capa") || w.Name.Contains("NOTE"));
                    int totalGeral = currentSheetIndex + totalFixas;

                    for (int i = 0; i < totalGeral; i++)
                    {
                        if (i >= package.Workbook.Worksheets.Count) break;
                        var ws = package.Workbook.Worksheets[i];
                        int pageReal = i + 1;

                        int r = 5, c = 10;
                        if (pageReal <= totalFixas) { r = 3; c = 32; } // Capa e Notes
                        else
                        {
                            if (reportType == "INSTRUMENTATION") c = 9;
                            else if (reportType == "LINE LIST") c = 16;
                            else if (reportType == "EQUIPMENT") c = 21;
                            else if (reportType == "VALVE LIST") c = 10;
                            else c = 13;
                        }

                        ws.Cells[r, c].Value = $"{pageReal} OF {totalGeral}";
                        ws.Cells[r, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }

                    package.SaveAs(memoryStream);
                }
                return memoryStream.ToArray();
            }
        }

        private void ApplyMerge(ExcelWorksheet ws, int col, int startRow, int endRow)
        {
            try
            {
                if (startRow == endRow) return;
                var range = ws.Cells[startRow, col, endRow, col];
                range.Merge = true;
                range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }
            catch { }
        }
    }
}   