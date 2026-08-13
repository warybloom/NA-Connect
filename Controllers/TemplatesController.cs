using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Text.Json;

// ALIASES PARA EVITAR AMBIGUIDADE ENTRE WORD E EXCEL
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Excel = DocumentFormat.OpenXml.Spreadsheet;

namespace APS.WebApp.Controllers 
{
    [ApiController]
    [Route("api/[controller]")]
    public class TemplatesController : ControllerBase
    {
        // Conexão direta com o banco ForgeSync
        private readonly string _connectionStringForge = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";

        // ====================================================================
        // 1. ROTA DE ESCANEAMENTO E UPLOAD AUTOMÁTICO DO ARQUIVO
        // ====================================================================
        [HttpPost("scanner")]
        public async Task<IActionResult> ScanTags([FromForm] IFormFile file, [FromForm] string modulo)
        {
            if (file == null || file.Length == 0) return BadRequest(new { sucesso = false, mensagem = "O arquivo está vazio." });
            
            var tagsFoundList = new List<string>();

            try 
            {
                // Guarda o arquivo fisicamente no servidor automaticamente
                string templateFolder = Path.Combine(Directory.GetCurrentDirectory(), "Templates");
                if (!Directory.Exists(templateFolder)) Directory.CreateDirectory(templateFolder);
                
                string filePath = Path.Combine(templateFolder, file.FileName);
                using (var fs = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fs);
                }

                string rawText = string.Empty;
                if (file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) rawText = ExtrairTextoDoWord(file);
                else if (file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) rawText = ExtrairTextoDoExcel(file);
                else return BadRequest(new { sucesso = false, mensagem = "Apenas arquivos .docx e .xlsx suportados." });

                var matches = Regex.Matches(rawText, @"<<([A-Z0-9_]+)>>", RegexOptions.IgnoreCase);
                foreach (Match match in matches) tagsFoundList.Add(match.Groups[1].Value.ToUpper());

                return Ok(new { sucesso = true, tagsFound = tagsFoundList.Distinct().ToList() });
            } 
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro ao escanear: {ex.Message}" }); }
        }

        // ====================================================================
        // 2. ROTA DE SALVAMENTO (GRAVA O MAPEAMENTO NO BANCO)
        // ====================================================================
        [HttpPost("salvar")]
        public async Task<IActionResult> SalvarTemplateNoBanco([FromBody] TemplateSaveModel model)
        {
            if (string.IsNullOrEmpty(model.Modulo) || string.IsNullOrEmpty(model.NomeArquivo))
                return BadRequest(new { sucesso = false, mensagem = "Dados incompletos." });

            try
            {
                using (var conn = new SqlConnection(_connectionStringForge))
                {
                    await conn.OpenAsync();
                    await conn.ExecuteAsync("UPDATE TB_Templates_Binding SET IsAtivo = 0 WHERE Modulo = @Modulo", new { model.Modulo });

                    string jsonTags = JsonSerializer.Serialize(model.TagsMapeadas);
                    string sqlInserir = @"INSERT INTO TB_Templates_Binding (Modulo, NomeArquivo, TagsMapeadas, Data_Upload, IsAtivo) 
                                          VALUES (@Modulo, @NomeArquivo, @TagsMapeadas, GETDATE(), 1)";
                    
                    await conn.ExecuteAsync(sqlInserir, new { model.Modulo, model.NomeArquivo, TagsMapeadas = jsonTags });
                    return Ok(new { sucesso = true, mensagem = "Template oficializado!" });
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        // ====================================================================
        // 3. ROTA DE BUSCA (VERIFICA SE EXISTE TEMPLATE PARA O MÓDULO)
        // ====================================================================
        [HttpGet("ativo")]
        public async Task<IActionResult> GetTemplateAtivo([FromQuery] string modulo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionStringForge))
                {
                    var template = await conn.QueryFirstOrDefaultAsync(
                        "SELECT * FROM TB_Templates_Binding WHERE Modulo = @modulo AND IsAtivo = 1", 
                        new { modulo });
                    
                    if (template == null) return Ok(new { sucesso = false });
                    return Ok(new { sucesso = true, dados = template });
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        // ====================================================================
        // 4. ROTA DE GERAÇÃO (O MOTOR DE MESCLAGEM HÍBRIDO)
        // ====================================================================
        [HttpGet("gerar")]
        public async Task<IActionResult> GerarDocumento([FromQuery] string modulo, [FromQuery] int idRegistro)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionStringForge))
                {
                    var template = await conn.QueryFirstOrDefaultAsync("SELECT * FROM TB_Templates_Binding WHERE Modulo = @modulo AND IsAtivo = 1", new { modulo });
                    if (template == null) return NotFound(new { sucesso = false, mensagem = "Nenhum template ativo." });

                    string sqlTabela = modulo == "desvios" ? "TB_Desvio_Escopo" : modulo == "holds" ? "TB_Controle_Holds" : "TB_Nao_Conformidade";
                    var dadosRegistro = await conn.QueryFirstOrDefaultAsync($"SELECT * FROM {sqlTabela} WHERE ID = @id", new { id = idRegistro });
                    if (dadosRegistro == null) return NotFound(new { sucesso = false, mensagem = "Registro não encontrado." });

                    var dictDados = ((IDictionary<string, object>)dadosRegistro).ToDictionary(
                        kvp => kvp.Key.ToUpper(), 
                        kvp => kvp.Value?.ToString() ?? ""
                    );

                    string templateFolder = Path.Combine(Directory.GetCurrentDirectory(), "Templates");
                    string caminhoArquivo = Path.Combine(templateFolder, template.NomeArquivo);
                    if (!System.IO.File.Exists(caminhoArquivo))
                        return NotFound(new { sucesso = false, mensagem = $"O arquivo original '{template.NomeArquivo}' não se encontra no servidor." });

                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(caminhoArquivo);
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(fileBytes, 0, fileBytes.Length);
                        ms.Position = 0;

                        bool isWord = template.NomeArquivo.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
                        bool isExcel = template.NomeArquivo.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

                        if (isWord)
                        {
                            using (var wordDoc = WordprocessingDocument.Open(ms, true))
                            {
                                ProcessarParagrafosWord(wordDoc.MainDocumentPart.Document.Descendants<Word.Paragraph>(), dictDados);
                                foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
                                    ProcessarParagrafosWord(headerPart.Header.Descendants<Word.Paragraph>(), dictDados);
                                foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
                                    ProcessarParagrafosWord(footerPart.Footer.Descendants<Word.Paragraph>(), dictDados);

                                wordDoc.MainDocumentPart.Document.Save();
                            }
                            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{modulo.ToUpper()}_{idRegistro}.docx");
                        }
                        else if (isExcel)
                        {
                            using (var excelDoc = SpreadsheetDocument.Open(ms, true))
                            {
                                ProcessarCelulasExcel(excelDoc, dictDados);
                            }
                            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{modulo.ToUpper()}_{idRegistro}.xlsx");
                        }
                        else
                        {
                            return BadRequest(new { sucesso = false, mensagem = "Extensão de arquivo não suportada para geração." });
                        }
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        // ====================================================================
        // MÓDULOS ISOLADOS DE INJEÇÃO (WORD E EXCEL)
        // ====================================================================
        
        // MOTOR WORD
        private void ProcessarParagrafosWord(IEnumerable<Word.Paragraph> paragrafos, Dictionary<string, string> dictDados)
        {
            foreach (var para in paragrafos)
            {
                string text = para.InnerText;
                if (text.Contains("<<") && text.Contains(">>"))
                {
                    string newText = text;
                    foreach (var kvp in dictDados)
                    {
                        if (newText.Contains($"<<{kvp.Key}>>"))
                        {
                            newText = newText.Replace($"<<{kvp.Key}>>", EscapeXml(kvp.Value));
                        }
                    }

                    if (newText != text)
                    {
                        var runs = para.Elements<Word.Run>().ToList();
                        if (runs.Any())
                        {
                            var firstRun = runs.First();
                            var rPr = firstRun.RunProperties != null ? (Word.RunProperties)firstRun.RunProperties.CloneNode(true) : null;
                            
                            foreach (var run in runs) run.Remove();

                            var newRun = new Word.Run();
                            if (rPr != null) newRun.Append(rPr);
                            newRun.Append(new Word.Text(newText) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                            
                            para.Append(newRun);
                        }
                    }
                }
            }
        }

        // MOTOR EXCEL (NOVO)
        private void ProcessarCelulasExcel(SpreadsheetDocument excelDoc, Dictionary<string, string> dictDados)
        {
            var stringTablePart = excelDoc.WorkbookPart.SharedStringTablePart;
            if (stringTablePart != null && stringTablePart.SharedStringTable != null)
            {
                bool modificouAlgo = false;

                foreach (var item in stringTablePart.SharedStringTable.Elements<Excel.SharedStringItem>())
                {
                    string text = item.InnerText;
                    if (text.Contains("<<") && text.Contains(">>"))
                    {
                        string newText = text;
                        foreach (var kvp in dictDados)
                        {
                            if (newText.Contains($"<<{kvp.Key}>>"))
                            {
                                // No excel não usamos o EscapeXml bruto igual no word porque ele trata o texto puro na célula
                                newText = newText.Replace($"<<{kvp.Key}>>", kvp.Value); 
                            }
                        }

                        if (newText != text)
                        {
                            // Remove fragmentos complexos (rich text do excel) e aplica texto limpo
                            item.RemoveAllChildren();
                            item.Append(new Excel.Text(newText));
                            modificouAlgo = true;
                        }
                    }
                }

                if (modificouAlgo)
                {
                    stringTablePart.SharedStringTable.Save();
                }
            }
        }

        private string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private string ExtrairTextoDoWord(IFormFile file)
        {
            var sb = new StringBuilder();
            using (var stream = file.OpenReadStream())
            using (var wordDoc = WordprocessingDocument.Open(stream, isEditable: false))
            {
                var body = wordDoc.MainDocumentPart.Document.Body;
                foreach (var para in body.Elements<Word.Paragraph>()) sb.AppendLine(para.InnerText);
                foreach (var table in body.Elements<Word.Table>())
                foreach (var row in table.Elements<Word.TableRow>())
                foreach (var cell in row.Elements<Word.TableCell>()) sb.AppendLine(cell.InnerText);
            }
            return sb.ToString();
        }

        private string ExtrairTextoDoExcel(IFormFile file)
        {
            var sb = new StringBuilder();
            using (var stream = file.OpenReadStream())
            using (var spreadsheetDocument = SpreadsheetDocument.Open(stream, isEditable: false))
            {
                var workbookPart = spreadsheetDocument.WorkbookPart;
                if (workbookPart == null) return string.Empty;
                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                foreach (var worksheetPart in workbookPart.WorksheetParts)
                {
                    var sheetData = worksheetPart.Worksheet.Elements<Excel.SheetData>().FirstOrDefault();
                    if (sheetData == null) continue;
                    foreach (var row in sheetData.Elements<Excel.Row>())
                    foreach (var cell in row.Elements<Excel.Cell>())
                    {
                        string cellValue = cell.CellValue?.InnerText;
                        if (!string.IsNullOrEmpty(cellValue))
                        {
                            if (cell.DataType != null && cell.DataType.Value == Excel.CellValues.SharedString && sharedStringTable != null && int.TryParse(cellValue, out int ssid))
                                sb.AppendLine(sharedStringTable.ElementAt(ssid).InnerText);
                            else sb.AppendLine(cellValue);
                        }
                    }
                }
            }
            return sb.ToString();
        }
    }

    public class TemplateSaveModel 
    {
        public string Modulo { get; set; }
        public string NomeArquivo { get; set; }
        public List<string> TagsMapeadas { get; set; }
    }
}