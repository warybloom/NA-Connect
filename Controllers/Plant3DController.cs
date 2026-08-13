using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json; 
using System.Text.Json.Serialization; 
using System.Linq;      
using NA_Connect.Services; 
using APS.WebApp.Models;   
using ExcelDataReader;
using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class Plant3DController : ControllerBase
    {
        private readonly DatabaseService _dbService;
        private readonly string _connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";

        public Plant3DController(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        [HttpPost("analyze-dcf")]
        public async Task<IActionResult> AnalyzeDcfFile(IFormFile file, [FromForm] string type)
        {
            if (file == null || file.Length == 0) return BadRequest("Nenhum arquivo enviado.");
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.FileName}");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create)) await file.CopyToAsync(stream);

                var schema = new Dictionary<string, List<string>>();
                var connectionString = $"Data Source={tempPath};Mode=ReadOnly;Pooling=False;";

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmdTabelas = connection.CreateCommand();
                    cmdTabelas.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'";
                    
                    var tabelas = new List<string>();
                    using (var reader = cmdTabelas.ExecuteReader())
                    {
                        while (reader.Read()) tabelas.Add(reader.GetString(0));
                    }

                    foreach (var tabela in tabelas)
                    {
                        var colunas = GetColunasDaTabela(connection, tabela);
                        if (colunas.Count > 0) schema[tabela] = colunas;
                    }
                } 
                SqliteConnection.ClearAllPools();
                return Ok(new { sucesso = true, mensagem = "Estrutura extraída", schema = schema });
            }
            catch (Exception ex) { return StatusCode(500, $"Erro SQLite: {ex.Message}"); }
            finally { GC.Collect(); GC.WaitForPendingFinalizers(); if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
        }

        [HttpPost("upload-cronograma-excel")]
        public IActionResult UploadCronogramaExcel(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Nenhum arquivo enviado.");
            if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls")) return BadRequest("O arquivo precisa ser um Excel (.xlsx ou .xls)");
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                var abasPreview = new Dictionary<string, List<List<string>>>();
                using (var stream = file.OpenReadStream())
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration() { ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = false, FilterRow = (rowReader) => true } }); 
                    foreach (DataTable table in result.Tables)
                    {
                        var linhasPreview = new List<List<string>>();
                        for (int i = 0; i < Math.Min(30, table.Rows.Count); i++)
                        {
                            var rowItems = table.Rows[i].ItemArray;
                            var rowStrings = rowItems.Select(x => x?.ToString()?.Trim() ?? "").ToList();
                            if (rowStrings.Any(s => !string.IsNullOrEmpty(s))) linhasPreview.Add(rowStrings);
                        }
                        if (linhasPreview.Count > 0) abasPreview.Add(table.TableName, linhasPreview);
                    }
                }
                return Ok(new { sucesso = true, abas = abasPreview });
            }
            catch (Exception ex) { return StatusCode(500, $"Erro Excel: {ex.Message}"); }
        }

        [HttpPost("save-measurement-rules")]
        public IActionResult SaveMeasurementRules([FromBody] JsonElement payload)
        {
            try
            {
                string projectId = payload.GetProperty("projectId").GetString();
                var pesos = payload.GetProperty("pesos");
                int tub = pesos.GetProperty("tubulacao").GetInt32();
                int val = pesos.GetProperty("valvulas").GetInt32();
                int inst = pesos.GetProperty("instrumentos").GetInt32();
                int sup = pesos.GetProperty("suportes").GetInt32();
                string statusRulesJson = payload.GetProperty("statusRules").GetRawText();
                string connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"
                        IF EXISTS (SELECT 1 FROM [Plant3D_RegrasMedicao] WHERE ProjectId = @projectId)
                        BEGIN
                            UPDATE [Plant3D_RegrasMedicao] SET PesoTubulacao = @tub, PesoValvulas = @val, PesoInstrumentos = @inst, PesoSuportes = @sup, StatusRulesJSON = @statusRules, DataAtualizacao = GETDATE() WHERE ProjectId = @projectId
                        END
                        ELSE
                        BEGIN
                            INSERT INTO [Plant3D_RegrasMedicao] (ProjectId, PesoTubulacao, PesoValvulas, PesoInstrumentos, PesoSuportes, StatusRulesJSON) VALUES (@projectId, @tub, @val, @inst, @sup, @statusRules)
                        END";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@projectId", projectId); cmd.Parameters.AddWithValue("@tub", tub); cmd.Parameters.AddWithValue("@val", val);
                        cmd.Parameters.AddWithValue("@inst", inst); cmd.Parameters.AddWithValue("@sup", sup); cmd.Parameters.AddWithValue("@statusRules", statusRulesJson);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { sucesso = true, mensagem = "Regras salvas com sucesso!" });
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

[HttpPost("calculate-4d")]
        public IActionResult Calculate4D([FromForm] string projectId, [FromForm] IFormFile file, [FromForm] string configJson)
        {
            Console.WriteLine("\n========== INICIANDO CÁLCULO 4D ==========");
            string safeProjectId = projectId ?? "PROJ-PADRAO";
            var tempStorageDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plant3D", safeProjectId);
            if (!Directory.Exists(tempStorageDir)) Directory.CreateDirectory(tempStorageDir);
            
            // 🎯 NOSSO CACHE DE CRONOGRAMA
            string schedulePath = Path.Combine(tempStorageDir, "Schedule.json");
            string agrupamento = "mes";
            var scheduleItems = new List<ScheduleItem>();

            try
            {
                // SE O USUÁRIO ENVIOU A PLANILHA, PROCESSA E SALVA O CACHE
                if (file != null && !string.IsNullOrEmpty(configJson))
                {
                    var config = JsonDocument.Parse(configJson).RootElement;
                    
                    var abasAlvo = new List<string>();
                    if (config.TryGetProperty("abas", out JsonElement abasProp) && abasProp.ValueKind == JsonValueKind.Array)
                        abasAlvo = abasProp.EnumerateArray().Select(x => x.GetString()).ToList();
                        
                    int linhaHeader = config.TryGetProperty("linhaCabecalho", out JsonElement lhProp) ? lhProp.GetInt32() : 0;
                    string colDataName = config.TryGetProperty("colunaData", out JsonElement cdProp) ? cdProp.GetString() : "";
                    if (config.TryGetProperty("agrupamentoTempo", out JsonElement agProp)) agrupamento = agProp.GetString();

                    var partesConfig = new List<string>();
                    if (config.TryGetProperty("partesTag", out JsonElement ptProp) && ptProp.ValueKind == JsonValueKind.Array) {
                        partesConfig = ptProp.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    }
                        
                    string separadorTag = config.TryGetProperty("separadorTag", out JsonElement sepProp) ? sepProp.GetString() : "";
                    string colunaTagUnica = config.TryGetProperty("colunaTag", out JsonElement ctProp) ? ctProp.GetString() : "";

                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    using (var stream = file.OpenReadStream())
                    using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet();
                        foreach (string nomeAba in abasAlvo)
                        {
                            if (!result.Tables.Contains(nomeAba)) continue;
                            var table = result.Tables[nomeAba];
                            
                            int idxData = -1;
                            int idxTagUnica = -1;

                            for (int i = 0; i < table.Columns.Count; i++) { 
                                string headerName = table.Rows[linhaHeader][i]?.ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(colDataName) && headerName.Equals(colDataName.Trim(), StringComparison.OrdinalIgnoreCase)) idxData = i; 
                                if (!string.IsNullOrEmpty(colunaTagUnica) && headerName.Equals(colunaTagUnica.Trim(), StringComparison.OrdinalIgnoreCase)) idxTagUnica = i;
                            }

                            for (int i = linhaHeader + 1; i < table.Rows.Count; i++) {
                                var row = table.Rows[i];
                                string tagMontada = "";
                                
                                if (partesConfig.Count > 0) {
                                    List<string> valoresTag = new List<string>();
                                    foreach(var colName in partesConfig) {
                                        for(int c=0; c < table.Columns.Count; c++) {
                                            string headerName = table.Rows[linhaHeader][c]?.ToString()?.Trim() ?? "";
                                            if(headerName.Equals(colName.Trim(), StringComparison.OrdinalIgnoreCase)) 
                                                valoresTag.Add(row[c]?.ToString()?.Trim() ?? "");
                                        }
                                    }
                                    tagMontada = string.Join(separadorTag ?? "", valoresTag);
                                } else if (idxTagUnica >= 0) {
                                    tagMontada = row[idxTagUnica]?.ToString()?.Trim();
                                }

                                if (string.IsNullOrEmpty(tagMontada)) continue;
                                if (!string.IsNullOrEmpty(separadorTag) && tagMontada.Replace(separadorTag, "").Trim() == "") continue;
                                
                                DateTime dt = DateTime.MaxValue;
                                if (idxData >= 0 && row[idxData] != DBNull.Value && row[idxData] != null) {
                                    var cellValue = row[idxData];
                                    if (cellValue is DateTime dateObj) dt = dateObj;
                                    else if (DateTime.TryParse(cellValue.ToString(), out DateTime parsedDate)) dt = parsedDate;
                                    else if (double.TryParse(cellValue.ToString(), out double oaDate)) {
                                        try { dt = DateTime.FromOADate(oaDate); } catch { }
                                    }
                                }
                                
                                scheduleItems.Add(new ScheduleItem { Tag = tagMontada, Data = dt });
                            }
                        }
                    }
                    
                    System.IO.File.WriteAllText(schedulePath, JsonSerializer.Serialize(scheduleItems));
                    Console.WriteLine($"[EXCEL] {scheduleItems.Count} itens salvos no cache (Schedule.json).");
                }
                else
                {
                    // SE O USUÁRIO APENAS CLICOU EM "CALCULAR AVANÇO", CARREGA DO CACHE!
                    if (!string.IsNullOrEmpty(configJson)) {
                        try {
                            var config = JsonDocument.Parse(configJson).RootElement;
                            if (config.TryGetProperty("agrupamentoTempo", out JsonElement agProp)) agrupamento = agProp.GetString();
                        } catch { }
                    }

                    if (!System.IO.File.Exists(schedulePath)) {
                        return BadRequest(new { sucesso = false, mensagem = "Nenhum escopo foi carregado. Faça o upload da planilha de cronograma primeiro." });
                    }
                    
                    scheduleItems = JsonSerializer.Deserialize<List<ScheduleItem>>(System.IO.File.ReadAllText(schedulePath));
                    Console.WriteLine($"[CACHE] {scheduleItems.Count} itens carregados do Schedule.json.");
                }

                if (scheduleItems.Count == 0) {
                    return BadRequest(new { sucesso = false, mensagem = "Nenhuma TAG válida encontrada no escopo." });
                }

                string connStr = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
                dynamic regras = null;
                using (SqlConnection conn = new SqlConnection(connStr)) {
                    conn.Open();
                    string sqlRules = "SELECT * FROM [Plant3D_RegrasMedicao] WHERE ProjectId = @pId";
                    using (SqlCommand cmd = new SqlCommand(sqlRules, conn)) {
                        cmd.Parameters.AddWithValue("@pId", projectId);
                        using (var reader = cmd.ExecuteReader()) {
                            if (reader.Read()) {
                                regras = new { 
                                    Tub = (int)reader["PesoTubulacao"], Val = (int)reader["PesoValvulas"], 
                                    Inst = (int)reader["PesoInstrumentos"], 
                                    StatusMap = JsonDocument.Parse(reader["StatusRulesJSON"].ToString()).RootElement 
                                };
                            }
                        }
                    }
                }
                
                if (regras == null) return BadRequest(new { sucesso = false, mensagem = "Defina as Regras de Medição para este projeto primeiro!" });

                var tagsNoExcel = scheduleItems.Select(x => x.Tag).Distinct().ToList();
                var planejadoPorData = new Dictionary<DateTime, int>();
                
                foreach(var item in scheduleItems) {
                    if (item.Data != DateTime.MaxValue) {
                        if (!planejadoPorData.ContainsKey(item.Data.Date)) planejadoPorData[item.Data.Date] = 0;
                        planejadoPorData[item.Data.Date]++;
                    }
                }

                // 4. CÁLCULO DE AWP COM A TAGLINE
                double somatorioAvancoReal = 0;
                int totalLinhasExcel = tagsNoExcel.Count;

                using (SqlConnection conn = new SqlConnection(connStr)) {
                    conn.Open();
                    
                    Console.WriteLine($"[BANCO] Calculando Avanço agrupado pela última auditoria...");

                    foreach (var tag in tagsNoExcel) {
                        
                        // 🎯 BYPASS SEGURO: Pega a última auditoria direto da tabela de Ativos
                        string sqlAtivos = @"
                            DECLARE @MaxAuditoria INT = (SELECT MAX(AuditoriaID) FROM [Plant3D_Ativos]);
                            
                            SELECT Categoria, Status_Cruzamento 
                            FROM [Plant3D_Ativos] 
                            WHERE AuditoriaID = @MaxAuditoria AND (
                                Tag = @tag 
                                OR Atributo_01 = @tag OR Atributo_02 = @tag OR Atributo_03 = @tag 
                                OR Atributo_04 = @tag OR Atributo_05 = @tag OR Atributo_06 = @tag
                            )";
                                                
                        using (SqlCommand cmd = new SqlCommand(sqlAtivos, conn)) {
                            cmd.Parameters.AddWithValue("@tag", tag);
                            
                            using (var reader = cmd.ExecuteReader()) {
                                double avancoTub = 0; int countTub = 0;
                                double avancoVal = 0; int countVal = 0;
                                double avancoInst = 0; int countInst = 0;
                                
                                while (reader.Read()) {
                                    string cat = reader["Categoria"].ToString();
                                    string status = reader["Status_Cruzamento"].ToString()?.Trim() ?? "";
                                    
                                    int penalidade = 0;
                                    if (regras.StatusMap.ValueKind == JsonValueKind.Array) {
                                        foreach (var s in regras.StatusMap.EnumerateArray()) {
                                            if (s.TryGetProperty("nome", out JsonElement nomeProp) && 
                                                nomeProp.GetString()?.Trim().Equals(status, StringComparison.OrdinalIgnoreCase) == true) { 
                                                penalidade = s.GetProperty("percentual").GetInt32(); 
                                                break; 
                                            }
                                        }
                                    }
                                    
                                    double avancoDoItem = (penalidade / 100.0);

                                    if (cat.Contains("Pipe") || cat.Contains("Linhas")) { avancoTub += avancoDoItem; countTub++; }
                                    else if (cat.Contains("Valve") || cat.Contains("Válvulas")) { avancoVal += avancoDoItem; countVal++; }
                                    else { avancoInst += avancoDoItem; countInst++; }
                                }

                                Console.WriteLine($"[MATCH] A linha '{tag}' reuniu: {countTub} tubo(s), {countVal} válvula(s), {countInst} instrumento(s).");

                                double pesoTotalPacote = 
                                    (countTub > 0 ? (regras.Tub / 100.0) : 0) +
                                    (countVal > 0 ? (regras.Val / 100.0) : 0) +
                                    (countInst > 0 ? (regras.Inst / 100.0) : 0);

                                if (pesoTotalPacote > 0) {
                                    double progressoTub = countTub > 0 ? (avancoTub / countTub) * (regras.Tub / 100.0) : 0;
                                    double progressoVal = countVal > 0 ? (avancoVal / countVal) * (regras.Val / 100.0) : 0;
                                    double progressoInst = countInst > 0 ? (avancoInst / countInst) * (regras.Inst / 100.0) : 0;
                                    
                                    somatorioAvancoReal += ((progressoTub + progressoVal + progressoInst) / pesoTotalPacote);
                                }
                            }
                        }
                    }
                }

                // 🎯 O percentual final correto (ex: 64.5%)
                double realizadoFinalPercent = totalLinhasExcel > 0 ? Math.Round((somatorioAvancoReal / totalLinhasExcel) * 100, 2) : 0;
                var pontosCurva = GerarPontosCurvaS(planejadoPorData, realizadoFinalPercent, totalLinhasExcel, agrupamento);
                
                Console.WriteLine($"========== FIM DO CÁLCULO: {realizadoFinalPercent}% ==========\n");

                return Ok(new { 
                    sucesso = true, 
                    mensagem = $"Avanço calculado com sucesso! {tagsNoExcel.Count} pacotes processados.",
                    curvaS = pontosCurva, 
                    kpiPlanejado = "100%", 
                    kpiRealizado = $"{realizadoFinalPercent}%" 
                });
            }
            catch (Exception ex) { 
                Console.WriteLine($"[ERRO FATAL] {ex.Message} \n {ex.StackTrace}");
                return StatusCode(500, new { sucesso = false, mensagem = $"Erro no cálculo 4D: {ex.Message}" }); 
            }
        }
    

        [HttpGet("get-database-tables")]
        public IActionResult GetTables([FromQuery] string projectId, [FromQuery] string dbType) 
        {
            try {
                string mainConnStr = @"Server=(localdb)\MSSQLLocalDB;Database=ForgeSync;Integrated Security=True;";
                string filePath = "";
                using (SqlConnection conn = new SqlConnection(mainConnStr)) {
                    conn.Open();
                    string sqlPath = dbType == "pid" ? "SELECT Pid_File_Path FROM [Plant3D_Configuracao] WHERE Project_ID_ACC = @id" : "SELECT Piping_File_Path FROM [Plant3D_Configuracao] WHERE Project_ID_ACC = @id";
                    using (SqlCommand cmd = new SqlCommand(sqlPath, conn)) {
                        cmd.Parameters.AddWithValue("@id", projectId);
                        filePath = cmd.ExecuteScalar()?.ToString();
                    }
                }
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return BadRequest(new { sucesso = false, mensagem = "Arquivo .dcf não encontrado no servidor." });

                List<string> tables = new List<string>();
                using (var sqliteConn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly;Pooling=False;")) {
                    sqliteConn.Open();
                    string sqlTables = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'";
                    using (var cmd = new SqliteCommand(sqlTables, sqliteConn)) {
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) tables.Add(reader["name"].ToString());
                        }
                    }
                }
                return Ok(new { sucesso = true, tabelas = tables });
            } catch (Exception ex) { return BadRequest(new { sucesso = false, mensagem = "Erro ao ler banco: " + ex.Message }); }
        }

        [HttpGet("get-table-columns")]
        public IActionResult GetColumns([FromQuery] string projectId, [FromQuery] string tableName, [FromQuery] string dbType)
        {
            try {
                string mainConnStr = @"Server=(localdb)\MSSQLLocalDB;Database=ForgeSync;Integrated Security=True;";
                string filePath = "";
                using (SqlConnection conn = new SqlConnection(mainConnStr)) {
                    conn.Open();
                    string sqlPath = dbType == "pid" ? "SELECT Pid_File_Path FROM [Plant3D_Configuracao] WHERE Project_ID_ACC = @id" : "SELECT Piping_File_Path FROM [Plant3D_Configuracao] WHERE Project_ID_ACC = @id";
                    using (SqlCommand cmd = new SqlCommand(sqlPath, conn)) {
                        cmd.Parameters.AddWithValue("@id", projectId);
                        filePath = cmd.ExecuteScalar()?.ToString();
                    }
                }
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return BadRequest(new { sucesso = false, mensagem = "Arquivo .dcf não encontrado." });

                List<string> columns = new List<string>();
                using (var sqliteConn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly;Pooling=False;")) {
                    sqliteConn.Open();
                    // AQUI CONTINUA USANDO A HIERARQUIA PARA PREENCHER OS DROPDOWNS DO FRONT-END
                    columns = GetColunasDaTabela(sqliteConn, tableName);
                }
                return Ok(new { sucesso = true, colunas = columns });
            } catch (Exception ex) { return BadRequest(new { sucesso = false, mensagem = ex.Message }); }
        }

       [HttpPost("run-audit")]
        [DisableRequestSizeLimit] 
        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)] 
        public async Task<IActionResult> RunAudit(IFormFile? pipingFile, IFormFile? pidFile, [FromForm] string? configJson, [FromForm] string? projectId, [FromForm] string? projectName)
        {
            string safeProjectId = projectId ?? "PROJ-PADRAO";
            var tempStorageDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plant3D", safeProjectId);
            if (!Directory.Exists(tempStorageDir)) Directory.CreateDirectory(tempStorageDir);

            string pathPiping = Path.Combine(tempStorageDir, "Piping.dcf");
            string pathPid = Path.Combine(tempStorageDir, "ProcessPower.dcf");

            try
            {
                if (pipingFile != null) { using (var stream = new FileStream(pathPiping, FileMode.Create)) await pipingFile.CopyToAsync(stream); }
                if (pidFile != null) { using (var stream = new FileStream(pathPid, FileMode.Create)) await pidFile.CopyToAsync(stream); }

                if (!System.IO.File.Exists(pathPiping) || !System.IO.File.Exists(pathPid))
                    return BadRequest("Arquivos DCF não encontrados.");

                // 1. DESCOBRE O NOME REAL
                string realProjectName = !string.IsNullOrWhiteSpace(projectName) ? projectName : "Projeto Sem Nome";
                using (var connName = new SqliteConnection($"Data Source={pathPid};Mode=ReadOnly;Pooling=False;"))
                {
                    connName.Open();
                    var nameFromDb = GetProjectNameFromDcf(connName);
                    if (nameFromDb != "Projeto Desconhecido" && string.IsNullOrWhiteSpace(projectName)) realProjectName = nameFromDb;
                }
                SqliteConnection.ClearAllPools();

                // 2. ESCUDO ANTI-DUPLICAÇÃO
                string connStrMain = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
                using (SqlConnection connSql = new SqlConnection(connStrMain))
                {
                    connSql.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT Project_ID_ACC FROM [Plant3D_Configuracao] WHERE Nome_Projeto = @nome", connSql))
                    {
                        cmd.Parameters.AddWithValue("@nome", realProjectName);
                        var existingId = cmd.ExecuteScalar()?.ToString();
                        
                        if (!string.IsNullOrEmpty(existingId) && existingId != safeProjectId)
                        {
                            safeProjectId = existingId; 
                            
                            var finalStorageDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plant3D", safeProjectId);
                            if (!Directory.Exists(finalStorageDir)) Directory.CreateDirectory(finalStorageDir);
                            
                            string finalPathPiping = Path.Combine(finalStorageDir, "Piping.dcf");
                            string finalPathPid = Path.Combine(finalStorageDir, "ProcessPower.dcf");
                            
                            if (System.IO.File.Exists(pathPiping)) System.IO.File.Copy(pathPiping, finalPathPiping, true);
                            if (System.IO.File.Exists(pathPid)) System.IO.File.Copy(pathPid, finalPathPid, true);
                            
                            pathPiping = finalPathPiping;
                            pathPid = finalPathPid;
                        }
                    }
                }

                // 3. CARREGA CONFIGURAÇÕES
                var configSalva = await _dbService.GetConfiguracaoPlant3DAsync(safeProjectId);
                string jsonRegras = configSalva?.Regras_Tag_JSON ?? "[]";
                
                string jsonMapeamento = "[]";
                if (!string.IsNullOrWhiteSpace(configJson) && configJson != "[]") {
                    jsonMapeamento = configJson;
                } else if (configSalva != null && !string.IsNullOrWhiteSpace(configSalva.Mapeamento_JSON)) {
                    jsonMapeamento = configSalva.Mapeamento_JSON;
                }

                var configParaSalvar = new Plant3DConfig {
                    Project_ID_ACC = safeProjectId, Nome_Projeto = realProjectName, Mapeamento_JSON = jsonMapeamento,
                    Piping_File_Path = pathPiping, Pid_File_Path = pathPid, Folder_ID_DCF = configSalva?.Folder_ID_DCF ?? "",
                    Regras_Tag_JSON = jsonRegras 
                };
                await _dbService.SalvarConfiguracaoPlant3DAsync(configParaSalvar);

                if (string.IsNullOrEmpty(jsonMapeamento) || jsonMapeamento == "[]") {
                    return Ok(new { sucesso = true, mensagem = $"Bancos salvos com sucesso!" });
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var categorias = JsonSerializer.Deserialize<List<CategoryMapping>>(jsonMapeamento, options);
                var regrasTag = new List<TagRule>();
                if (!string.IsNullOrWhiteSpace(jsonRegras) && jsonRegras != "[]") {
                    regrasTag = JsonSerializer.Deserialize<List<TagRule>>(jsonRegras, options) ?? new List<TagRule>();
                }
                
                var finalAssets = new List<Plant3DAtivo>();
                int divergenciasGerais = 0, faltamPidGeral = 0, faltam3dGeral = 0;

                foreach (var cat in categorias)
                {
                    var regraCat = regrasTag.FirstOrDefault(r => r.Categoria.Equals(cat.label, StringComparison.OrdinalIgnoreCase));
                    var tagAttrs = cat.attributes.Where(a => a.isTagPart || a.isFixedTag).ToList();
                    
                    if (!tagAttrs.Any()) {
                        var fallback = cat.attributes.FirstOrDefault(a => a.name.ToLower().Contains("tag")) 
                                       ?? cat.attributes.FirstOrDefault(a => a.pidCol?.ToLower().Contains("tag") == true)
                                       ?? cat.attributes.FirstOrDefault();
                        if (fallback != null) tagAttrs.Add(fallback);
                    }

                    var specAttr = cat.attributes.FirstOrDefault(a => a.name.ToLower().Contains("spec"));
                    var extraAttrs = cat.attributes.Where(a => !tagAttrs.Contains(a) && a != specAttr).Take(20).ToList();

                    if (!tagAttrs.Any() || string.IsNullOrEmpty(cat.pidTable) || string.IsNullOrEmpty(cat.pipingTable)) continue;

                    var pidAssetsList = new List<Plant3DAtivo>();
                    var pipingAssetsList = new List<Plant3DAtivo>();

                    // --- LER P&ID ---
                    using (var connPid = new SqliteConnection($"Data Source={pathPid};Mode=ReadOnly;Pooling=False;"))
                    {
                        connPid.Open();
                        var colunasEstritasPid = GetColunasEstritas(connPid, cat.pidTable);
                        
                        string fromPid = $"[{cat.pidTable}] T1";
                        string joinedTable = null;
                        var colunasMapeadasDisponiveis = new HashSet<string>(colunasEstritasPid);

                        if (cat.pidTable.Equals("PipeLines", StringComparison.OrdinalIgnoreCase) || cat.pidTable.Equals("PipeLineSegments", StringComparison.OrdinalIgnoreCase))
                        {
                            joinedTable = "PipeLineGroup";
                            fromPid += " LEFT JOIN [PipeLineGroupRelationship] ON T1.[PnPID] = [PipeLineGroupRelationship].[PipeLine]" +
                                       $" LEFT JOIN [{joinedTable}] ON [PipeLineGroupRelationship].[PipeLineGroup] = [{joinedTable}].[PnPID]";
                        }
                        else if (cat.pidTable.Equals("PipeLineGroup", StringComparison.OrdinalIgnoreCase))
                        {
                            joinedTable = "PipeLines";
                            fromPid += " LEFT JOIN [PipeLineGroupRelationship] ON T1.[PnPID] = [PipeLineGroupRelationship].[PipeLineGroup]" +
                                       $" LEFT JOIN [{joinedTable}] ON [PipeLineGroupRelationship].[PipeLine] = [{joinedTable}].[PnPID]";
                        }

                        if (joinedTable != null)
                        {
                            var colsJoin = GetColunasEstritas(connPid, joinedTable);
                            foreach (var c in colsJoin) colunasMapeadasDisponiveis.Add(c);
                        }

                        var colsPidToSelect = new HashSet<string>();
                        foreach (var ta in tagAttrs) if (!string.IsNullOrEmpty(ta.pidCol) && colunasMapeadasDisponiveis.Contains(ta.pidCol)) colsPidToSelect.Add(ta.pidCol);
                        if (specAttr != null && !string.IsNullOrEmpty(specAttr.pidCol) && colunasMapeadasDisponiveis.Contains(specAttr.pidCol)) colsPidToSelect.Add(specAttr.pidCol);
                        foreach (var e in extraAttrs.Where(x => !string.IsNullOrEmpty(x.pidCol) && colunasMapeadasDisponiveis.Contains(x.pidCol))) colsPidToSelect.Add(e.pidCol);

                        if (colsPidToSelect.Count > 0)
                        {
                            var selectFields = new List<string>();
                            foreach (var col in colsPidToSelect)
                            {
                                if (colunasEstritasPid.Contains(col)) selectFields.Add($"T1.[{col}] AS [{col}]");
                                else if (joinedTable != null) selectFields.Add($"[{joinedTable}].[{col}] AS [{col}]");
                            }
                            
                            string selPid = string.Join(", ", selectFields);
                            string baseTagCol = tagAttrs.FirstOrDefault(t => !string.IsNullOrEmpty(t.pidCol) && colunasMapeadasDisponiveis.Contains(t.pidCol))?.pidCol;
                            
                            if (!string.IsNullOrEmpty(baseTagCol))
                            {
                                string whereCol = colunasEstritasPid.Contains(baseTagCol) ? $"T1.[{baseTagCol}]" : $"[{joinedTable}].[{baseTagCol}]";

                                var cmd = connPid.CreateCommand();
                                cmd.CommandText = $"SELECT {selPid} FROM {fromPid} WHERE {whereCol} IS NOT NULL AND {whereCol} != ''";
                                
                                using (var reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        var tagPartsList = new List<string>();
                                        foreach (var ta in tagAttrs) {
                                            if (!string.IsNullOrEmpty(ta.pidCol) && colunasMapeadasDisponiveis.Contains(ta.pidCol)) 
                                                tagPartsList.Add(reader[ta.pidCol].ToString().Trim());
                                        }
                                        string tag = tagPartsList.Count == 1 ? tagPartsList[0] : string.Join(regraCat?.Delimitador ?? "-", tagPartsList);

                                        // 🎯 IGNORAR LIXOS E AGRUPAR DUPLICATAS
                                        if (tag.Contains("?")) continue;
                                        if (pidAssetsList.Any(a => a.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))) continue;

                                        string spec = (specAttr != null && !string.IsNullOrEmpty(specAttr.pidCol) && colunasMapeadasDisponiveis.Contains(specAttr.pidCol) && reader[specAttr.pidCol] != DBNull.Value) ? reader[specAttr.pidCol].ToString().Trim() : "—";
                                        
                                        var ativo = new Plant3DAtivo { 
                                            Tag = tag, Categoria = cat.label, 
                                            Spec_PID = spec, Spec_3D = "—", Desenho_PID = "P&ID", Desenho_3D = "—"
                                        };

                                        for (int i = 0; i < extraAttrs.Count; i++)
                                        {
                                            if (!string.IsNullOrEmpty(extraAttrs[i].pidCol) && colunasMapeadasDisponiveis.Contains(extraAttrs[i].pidCol) && reader[extraAttrs[i].pidCol] != DBNull.Value)
                                                SetAtributoDinamico(ativo, i + 1, reader[extraAttrs[i].pidCol].ToString());
                                        }
                                        pidAssetsList.Add(ativo);
                                    }
                                }
                            }
                        }
                    }

                    // --- LER PIPING (3D) ---
                    using (var connPiping = new SqliteConnection($"Data Source={pathPiping};Mode=ReadOnly;Pooling=False;"))
                    {
                        connPiping.Open();
                        var colunasEstritas3D = GetColunasEstritas(connPiping, cat.pipingTable);
                        
                        var cols3D = new HashSet<string>();
                        foreach (var ta in tagAttrs) if (!string.IsNullOrEmpty(ta.pipingCol) && colunasEstritas3D.Contains(ta.pipingCol)) cols3D.Add(ta.pipingCol);
                        if (specAttr != null && !string.IsNullOrEmpty(specAttr.pipingCol) && colunasEstritas3D.Contains(specAttr.pipingCol)) cols3D.Add(specAttr.pipingCol);
                        foreach (var e in extraAttrs.Where(x => !string.IsNullOrEmpty(x.pipingCol) && colunasEstritas3D.Contains(x.pipingCol))) cols3D.Add(e.pipingCol);

                        if (cols3D.Count > 0)
                        {
                            string sel3D = string.Join(", ", cols3D.Select(c => $"[{c}]"));
                            string baseTagCol3d = tagAttrs.FirstOrDefault(t => !string.IsNullOrEmpty(t.pipingCol) && colunasEstritas3D.Contains(t.pipingCol))?.pipingCol;
                            
                            if (!string.IsNullOrEmpty(baseTagCol3d))
                            {
                                var cmd = connPiping.CreateCommand();
                                cmd.CommandText = $"SELECT {sel3D} FROM [{cat.pipingTable}] WHERE [{baseTagCol3d}] IS NOT NULL AND [{baseTagCol3d}] != ''";
                                
                                using (var reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        var tagPartsList3d = new List<string>();
                                        foreach (var ta in tagAttrs) {
                                            if (!string.IsNullOrEmpty(ta.pipingCol) && colunasEstritas3D.Contains(ta.pipingCol)) tagPartsList3d.Add(reader[ta.pipingCol].ToString().Trim());
                                        }
                                        string tag3D = tagPartsList3d.Count == 1 ? tagPartsList3d[0] : string.Join(regraCat?.Delimitador ?? "-", tagPartsList3d);

                                        // 🎯 IGNORAR LIXOS E AGRUPAR DUPLICATAS
                                        if (tag3D.Contains("?")) continue;
                                        if (pipingAssetsList.Any(a => a.Tag.Equals(tag3D, StringComparison.OrdinalIgnoreCase))) continue;

                                        string spec3D = (specAttr != null && !string.IsNullOrEmpty(specAttr.pipingCol) && colunasEstritas3D.Contains(specAttr.pipingCol) && reader[specAttr.pipingCol] != DBNull.Value) ? reader[specAttr.pipingCol].ToString().Trim() : "—";

                                        var ativo = new Plant3DAtivo { 
                                            Tag = tag3D, Categoria = cat.label, 
                                            Spec_PID = "—", Spec_3D = spec3D, Desenho_PID = "—", Desenho_3D = "Modelo 3D"
                                        };

                                        for (int i = 0; i < extraAttrs.Count; i++)
                                        {
                                            if (!string.IsNullOrEmpty(extraAttrs[i].pipingCol) && colunasEstritas3D.Contains(extraAttrs[i].pipingCol) && reader[extraAttrs[i].pipingCol] != DBNull.Value)
                                                SetAtributoDinamico(ativo, i + 1, reader[extraAttrs[i].pipingCol].ToString());
                                        }
                                        pipingAssetsList.Add(ativo);
                                    }
                                }
                            }
                        }
                    }

                    // Cruzamento exato e mapeamento final
                    foreach (var a3d in pipingAssetsList)
                    {
                        var matchExato = pidAssetsList.FirstOrDefault(p => p.Tag.Equals(a3d.Tag, StringComparison.OrdinalIgnoreCase));
                        
                        if (matchExato != null)
                        {
                            matchExato.Spec_3D = a3d.Spec_3D;
                            matchExato.Desenho_3D = "Modelo 3D";
                            MesclarAtributos(matchExato, a3d, extraAttrs);
                            
                            if (matchExato.Spec_PID != a3d.Spec_3D && specAttr != null && matchExato.Spec_PID != "—" && a3d.Spec_3D != "—") {
                                matchExato.Status_Cruzamento = "Divergência de Spec";
                                divergenciasGerais++;
                            } else {
                                matchExato.Status_Cruzamento = "Match Perfeito";
                            }
                            
                            matchExato.Tag_Fatiado_JSON = JsonSerializer.Serialize(FatiarTag(matchExato.Tag, regraCat));
                            finalAssets.Add(matchExato);
                            pidAssetsList.Remove(matchExato);
                            continue;
                        }

                        string? chave3D = ObterChaveSequencial(a3d.Tag, regraCat);
                        Plant3DAtivo? matchSmart = null;

                        if (!string.IsNullOrEmpty(chave3D))
                        {
                            matchSmart = pidAssetsList.FirstOrDefault(p => {
                                string? chavePID = ObterChaveSequencial(p.Tag, regraCat);
                                return !string.IsNullOrEmpty(chavePID) && chavePID.Equals(chave3D, StringComparison.OrdinalIgnoreCase);
                            });
                        }

                        if (matchSmart != null)
                        {
                            var fatias3D = FatiarTag(a3d.Tag, regraCat);
                            var fatiasPID = FatiarTag(matchSmart.Tag, regraCat);
                            
                            List<string> divergencias = new List<string>();
                            foreach(var key in fatiasPID.Keys) {
                                if (fatias3D.TryGetValue(key, out var val3d) && !string.Equals(fatiasPID[key], val3d, StringComparison.OrdinalIgnoreCase)) {
                                    divergencias.Add($"{key} (PID: {fatiasPID[key]} x 3D: {val3d})");
                                }
                            }

                            matchSmart.Spec_3D = a3d.Spec_3D;
                            matchSmart.Desenho_3D = "Modelo 3D";
                            MesclarAtributos(matchSmart, a3d, extraAttrs);

                            matchSmart.Status_Cruzamento = divergencias.Any() ? "Divergência: " + string.Join(" | ", divergencias) : "Divergência de Tag (Ordem das posições)";
                            matchSmart.Tag_Fatiado_JSON = JsonSerializer.Serialize(fatiasPID); 
                            
                            matchSmart.Tag = $"{matchSmart.Tag} (PID) / {a3d.Tag} (3D)"; 
                            
                            divergenciasGerais++;
                            finalAssets.Add(matchSmart);
                            pidAssetsList.Remove(matchSmart);
                        }
                        else
                        {
                            a3d.Status_Cruzamento = "Não existe no P&ID";
                            a3d.Tag_Fatiado_JSON = JsonSerializer.Serialize(FatiarTag(a3d.Tag, regraCat));
                            faltamPidGeral++;
                            finalAssets.Add(a3d);
                        }
                    }

                    faltam3dGeral += pidAssetsList.Count;
                    foreach (var pendente in pidAssetsList)
                    {
                        pendente.Status_Cruzamento = "Falta Modelar no 3D";
                        pendente.Tag_Fatiado_JSON = JsonSerializer.Serialize(FatiarTag(pendente.Tag, regraCat));
                        finalAssets.Add(pendente);
                    }
                }

                var auditoriaInfo = new Plant3DAuditoria {
                     Project_ID_ACC = safeProjectId, Total_PID = finalAssets.Count(x => x.Desenho_PID != "—"),
                     Total_3D = finalAssets.Count(x => x.Desenho_3D != "—"), Faltam_No_3D = faltam3dGeral,
                     Faltam_No_PID = faltamPidGeral, Divergencias_Spec = divergenciasGerais
                };

                int auditoriaId = await _dbService.SalvarAuditoriaAsync(auditoriaInfo);
                foreach (var a in finalAssets) a.AuditoriaID = auditoriaId;
                
                int batchSize = 1000;
                for (int i = 0; i < finalAssets.Count; i += batchSize) {
                    var batch = finalAssets.Skip(i).Take(batchSize).ToList();
                    await _dbService.SalvarAtivosAuditoriaAsync(batch);
                }

                return Ok(new { sucesso = true, mensagem = $"Auditoria realizada! Analisados {finalAssets.Count} ativos." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro durante a auditoria: {ex.Message}");
            }
        }



        // 🎯 LÓGICA ESTRITA: Usada pelo RunAudit para garantir que ele só peça colunas que de fato existem na tabela consultada (Evita o Erro 500)
        private HashSet<string> GetColunasEstritas(SqliteConnection conn, string tableName)
        {
            var colunas = new HashSet<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info('{tableName}')";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) colunas.Add(reader.GetString(1));
                }
            }
            return colunas;
        }

        // 🎯 LÓGICA DE HIERARQUIA MANTIDA (Apenas para popular a tela do React com todas as opções herdadas)
        private List<string> GetColunasDaTabela(SqliteConnection conn, string tableName)
        {
            var colunas = new HashSet<string>(); 
            var tabelasHierarquia = new List<string> { tableName, "PipeRunComponent", "EngineeringItems", "PnPRows" };

            foreach (var tbl in tabelasHierarquia)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name='{tbl}'";
                    var exists = cmd.ExecuteScalar() != null;
                    
                    if (!exists) continue;

                    cmd.CommandText = $"PRAGMA table_info('{tbl}')";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) colunas.Add(reader.GetString(1));
                    }
                }
            }
            return colunas.ToList();
        }

    private List<object> GerarPontosCurvaS(Dictionary<DateTime, int> planejadoPorData, double percentualRealizadoFinal, int totalPlanejado, string agrupamento)
        {
            var curva = new List<object>();
            if (totalPlanejado == 0 || !planejadoPorData.Any()) return curva;

            var datasOrdenadas = planejadoPorData.Keys.OrderBy(d => d).ToList();
            DateTime dataInicio = datasOrdenadas.First();
            DateTime dataFim = datasOrdenadas.Last();
            
            // 🎯 O CONCEITO DE DATA DATE: A curva do Realizado corta aqui!
            DateTime dataAtual = DateTime.Now.Date; 
            
            // Se o Excel começar no futuro, ajustamos para o gráfico não quebrar
            if (dataInicio > dataAtual) dataInicio = dataAtual; 

            int acumuladoPlanejado = 0;

            if (agrupamento == "dia")
            {
                for (DateTime d = dataInicio.Date; d <= dataFim.Date; d = d.AddDays(1))
                {
                    if (planejadoPorData.ContainsKey(d)) acumuladoPlanejado += planejadoPorData[d];
                    int percPlanejado = (int)Math.Round((acumuladoPlanejado * 100.0) / totalPlanejado);
                    
                    double? percRealizado = null;
                    if (d <= dataAtual) 
                    {
                        double diasTotaisReal = (dataAtual - dataInicio).TotalDays;
                        double diasPassados = (d - dataInicio).TotalDays;
                        if (diasTotaisReal <= 0) percRealizado = percentualRealizadoFinal;
                        else percRealizado = Math.Round((diasPassados / diasTotaisReal) * percentualRealizadoFinal, 2);
                    }

                    curva.Add(new { data = d.ToString("dd/MM"), planejado = percPlanejado, realizado = percRealizado });
                }
            }
            else if (agrupamento == "semana")
            {
                DateTime inicioSemana = dataInicio.AddDays(-(int)dataInicio.DayOfWeek);
                int acumuladoSemana = 0;
                for (DateTime d = inicioSemana; d <= dataFim.AddDays(7); d = d.AddDays(7))
                {
                    var somaSemana = planejadoPorData.Where(x => x.Key >= d && x.Key < d.AddDays(7)).Sum(x => x.Value);
                    acumuladoSemana += somaSemana;
                    int percPlanejado = (int)Math.Round((acumuladoSemana * 100.0) / totalPlanejado);
                    
                    double? percRealizado = null;
                    // Dá uma margem de 7 dias para desenhar o ponto da semana atual
                    if (d <= dataAtual.AddDays(7)) 
                    {
                        double semanasTotaisReal = (dataAtual - dataInicio).TotalDays / 7.0;
                        double semanasPassadas = (d - dataInicio).TotalDays / 7.0;
                        if (semanasTotaisReal <= 0) percRealizado = percentualRealizadoFinal;
                        else percRealizado = Math.Round(Math.Min(1.0, semanasPassadas / semanasTotaisReal) * percentualRealizadoFinal, 2);
                    }

                    int numSemana = ((d.Day - 1) / 7) + 1;
                    curva.Add(new { data = $"S{numSemana}-{d.ToString("MMM")}", planejado = percPlanejado, realizado = percRealizado });
                    if (d > dataFim) break;
                }
            }
            else 
            {
                DateTime inicioMes = new DateTime(dataInicio.Year, dataInicio.Month, 1);
                int acumuladoMes = 0;
                for (DateTime d = inicioMes; d <= dataFim; d = d.AddMonths(1))
                {
                    var somaMes = planejadoPorData.Where(x => x.Key.Year == d.Year && x.Key.Month == d.Month).Sum(x => x.Value);
                    acumuladoMes += somaMes;
                    int percPlanejado = (int)Math.Round((acumuladoMes * 100.0) / totalPlanejado);
                    
                    double? percRealizado = null;
                    // Margem para o fim do mês atual
                    DateTime fimDoMesAtual = new DateTime(dataAtual.Year, dataAtual.Month, DateTime.DaysInMonth(dataAtual.Year, dataAtual.Month));
                    if (d <= fimDoMesAtual) 
                    {
                        double mesesTotaisReal = ((dataAtual.Year - dataInicio.Year) * 12) + dataAtual.Month - dataInicio.Month;
                        double mesesPassados = ((d.Year - dataInicio.Year) * 12) + d.Month - dataInicio.Month;
                        if (mesesTotaisReal <= 0) percRealizado = percentualRealizadoFinal;
                        else percRealizado = Math.Round(Math.Min(1.0, mesesPassados / mesesTotaisReal) * percentualRealizadoFinal, 2);
                    }

                    curva.Add(new { data = d.ToString("MMM/yy"), planejado = percPlanejado, realizado = percRealizado });
                }
            }
            return curva;
        }
        
        
        private string GetProjectNameFromDcf(SqliteConnection conn)
        {
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT [Project_Name] FROM [PnPProject_PNP] LIMIT 1";
                    var result = cmd.ExecuteScalar();
                    return result?.ToString() ?? "Projeto Sem Nome";
                }
            }
            catch { return "Projeto Desconhecido"; }
        }

        [HttpGet("dashboard-data")]
        public async Task<IActionResult> GetDashboardData([FromQuery] string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return BadRequest(new { sucesso = false, mensagem = "ID não fornecido." });
            try
            {
                var ativos = await _dbService.ObterUltimosResultadosAsync(projectId);
                var listaAtivos = ativos.ToList();
                if (!listaAtivos.Any()) return Ok(new { sucesso = true, temDados = false });

                var resumo = new {
                    totalAtivos = listaAtivos.Count, totalPID = listaAtivos.Count(x => x.Desenho_PID != "—"),
                    total3D = listaAtivos.Count(x => x.Desenho_3D != "—"), matchPerfeito = listaAtivos.Count(x => x.Status_Cruzamento == "Match Perfeito"),
                    divergencias = listaAtivos.Count(x => x.Status_Cruzamento == "Divergência de Spec"), faltamNo3D = listaAtivos.Count(x => x.Status_Cruzamento == "Falta Modelar no 3D"),
                    faltamNoPID = listaAtivos.Count(x => x.Status_Cruzamento == "Não existe no P&ID")
                };
                return Ok(new { sucesso = true, temDados = true, resumo = resumo, itens = listaAtivos });
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("project-schema")]
        public IActionResult GetProjectSchema([FromQuery] string projectId)
        {
            string safeProjectId = projectId ?? "PROJ-PADRAO";
            var storageDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plant3D", safeProjectId);
            if (!Directory.Exists(storageDir)) return NotFound(new { sucesso = false, mensagem = "Projeto não encontrado." });

            string pathPiping = Path.Combine(storageDir, "Piping.dcf");
            string pathPid = Path.Combine(storageDir, "ProcessPower.dcf");

            if (!System.IO.File.Exists(pathPiping) || !System.IO.File.Exists(pathPid))
                return BadRequest(new { sucesso = false, mensagem = "Arquivos ausentes." });

            try
            {
                return Ok(new { sucesso = true, schemaPid = GetFullSchema(pathPid), schemaPiping = GetFullSchema(pathPiping) });
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("save-tag-rules")]
        public async Task<IActionResult> SaveTagRules([FromForm] string projectId, [FromForm] string regrasJson)
        {
            if (string.IsNullOrEmpty(projectId)) return BadRequest("ID não fornecido.");
            try {
                var config = new Plant3DConfig { Project_ID_ACC = projectId, Regras_Tag_JSON = regrasJson };
                await _dbService.SalvarConfiguracaoPlant3DAsync(config);
                return Ok(new { sucesso = true });
            } catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        private Dictionary<string, List<string>> GetFullSchema(string filePath)
        {
            var schema = new Dictionary<string, List<string>>();
            using (var conn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly;Pooling=False;"))
            {
                conn.Open();
                var tabelas = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'";
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read()) tabelas.Add(reader.GetString(0));
                }
                foreach (var tabela in tabelas) schema[tabela] = GetColunasDaTabela(conn, tabela);
            }
            return schema;
        }

        [HttpGet("project-context")]
        public async Task<IActionResult> GetProjectContext([FromQuery] string projectId)
        {
            string safeProjectId = projectId ?? "PROJ-PADRAO";
            var storageDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plant3D", safeProjectId);
            try
            {
                var configSalva = await _dbService.GetConfiguracaoPlant3DAsync(safeProjectId);
                string pathPiping = Path.Combine(storageDir, "Piping.dcf");
                string pathPid = Path.Combine(storageDir, "ProcessPower.dcf");
                object? schema = null;
                if (System.IO.File.Exists(pathPiping) && System.IO.File.Exists(pathPid))
                {
                    schema = new { pid = GetFullSchema(pathPid), piping = GetFullSchema(pathPiping) };
                }
                return Ok(new { sucesso = true, mapeamento = configSalva?.Mapeamento_JSON, nomeProjeto = configSalva?.Nome_Projeto, schema = schema, regrasTagJson = configSalva?.Regras_Tag_JSON });
            } catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("list-projects")]
        public IActionResult ListProjects()
        {
            try {
                string connStr = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
                var projetos = new List<object>();
                using (SqlConnection conn = new SqlConnection(connStr)) {
                    conn.Open();
                    string sql = "SELECT Project_ID_ACC, Nome_Projeto FROM [Plant3D_Configuracao]";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read()) projetos.Add(new { projectId = reader["Project_ID_ACC"].ToString(), projectName = reader["Nome_Projeto"].ToString() });
                }
                return Ok(new { sucesso = true, projetos = projetos });
            } catch (Exception ex) { return BadRequest(ex.Message); }
        }

        private void SetAtributoDinamico(Plant3DAtivo ativo, int index, string valor)
        {
            switch (index) {
                case 1: ativo.Atributo_01 = valor; break; case 2: ativo.Atributo_02 = valor; break;
                case 3: ativo.Atributo_03 = valor; break; case 4: ativo.Atributo_04 = valor; break;
                case 5: ativo.Atributo_05 = valor; break; case 6: ativo.Atributo_06 = valor; break;
                case 7: ativo.Atributo_07 = valor; break; case 8: ativo.Atributo_08 = valor; break;
                case 9: ativo.Atributo_09 = valor; break; case 10: ativo.Atributo_10 = valor; break;
                case 11: ativo.Atributo_11 = valor; break; case 12: ativo.Atributo_12 = valor; break;
                case 13: ativo.Atributo_13 = valor; break; case 14: ativo.Atributo_14 = valor; break;
                case 15: ativo.Atributo_15 = valor; break; case 16: ativo.Atributo_16 = valor; break;
                case 17: ativo.Atributo_17 = valor; break; case 18: ativo.Atributo_18 = valor; break;
                case 19: ativo.Atributo_19 = valor; break; case 20: ativo.Atributo_20 = valor; break;
            }
        }

        private string GetAtributoDinamico(Plant3DAtivo ativo, int index)
        {
            return index switch {
                1 => ativo.Atributo_01, 2 => ativo.Atributo_02, 3 => ativo.Atributo_03, 4 => ativo.Atributo_04,
                5 => ativo.Atributo_05, 6 => ativo.Atributo_06, 7 => ativo.Atributo_07, 8 => ativo.Atributo_08,
                9 => ativo.Atributo_09, 10 => ativo.Atributo_10, 11 => ativo.Atributo_11, 12 => ativo.Atributo_12,
                13 => ativo.Atributo_13, 14 => ativo.Atributo_14, 15 => ativo.Atributo_15, 16 => ativo.Atributo_16,
                17 => ativo.Atributo_17, 18 => ativo.Atributo_18, 19 => ativo.Atributo_19, 20 => ativo.Atributo_20,
                _ => null
            };
        }

        private void MesclarAtributos(Plant3DAtivo baseAtivo, Plant3DAtivo ativo3D, List<AttributeMap> extraAttrs)
        {
            for (int i = 0; i < extraAttrs.Count; i++)
            {
                string valPid = GetAtributoDinamico(baseAtivo, i + 1);
                string val3D = GetAtributoDinamico(ativo3D, i + 1);

                if (string.IsNullOrEmpty(valPid) && !string.IsNullOrEmpty(val3D)) {
                    SetAtributoDinamico(baseAtivo, i + 1, val3D);
                }
                else if (!string.IsNullOrEmpty(valPid) && !string.IsNullOrEmpty(val3D) && valPid != val3D) {
                    SetAtributoDinamico(baseAtivo, i + 1, $"{valPid} | 3D: {val3D}");
                }
            }
        }

        private Dictionary<string, string> FatiarTag(string tag, TagRule? regra)
        {
            var dict = new Dictionary<string, string>();
            if (regra == null || string.IsNullOrEmpty(regra.Delimitador) || regra.Partes == null) return dict;

            var pedacos = tag?.Split(new string[] { regra.Delimitador }, StringSplitOptions.None) ?? new string[0];
            
            for (int i = 0; i < regra.Partes.Count; i++)
            {
                string nomeColuna = regra.Partes[i].Name;
                
                if (i < pedacos.Length && !string.IsNullOrWhiteSpace(pedacos[i]))
                {
                    dict[nomeColuna] = pedacos[i];
                }
                else
                {
                    dict[nomeColuna] = "—";
                }
            }
            
            return dict;
        }

        private string? ObterChaveSequencial(string tag, TagRule? regra)
        {
            if (regra == null || string.IsNullOrEmpty(regra.Delimitador) || regra.Partes == null || !regra.Partes.Any()) return null;

            var pedacos = tag.Split(new string[] { regra.Delimitador }, StringSplitOptions.None);

            string partSeq = "";
            string partFluido = "";
            string partArea = "";

            for (int i = 0; i < regra.Partes.Count && i < pedacos.Length; i++)
            {
                string name = regra.Partes[i].Name.ToLower();
                string val = pedacos[i].Trim();

                if (name.Contains("sequenc") || name.Contains("number") || name.Contains("numero")) partSeq = val;
                if (name.Contains("fluid") || name.Contains("servi") || name.Contains("service")) partFluido = val;
                if (name.Contains("area") || name.Contains("área")) partArea = val;
            }

            if (string.IsNullOrEmpty(partSeq)) return null;

            return $"{partArea}_{partFluido}_{partSeq}".ToUpper();
        }

        [HttpGet("audit-summary")]
        public IActionResult GetAuditSummary([FromQuery] string projectId)
        {
            var summary = new {
                totalPid = 1450,
                total3d = 1280,
                conforme = 1150,
                divergente = 130,
                faltante3d = 170
            };
            return Ok(summary);
        }

        [HttpGet("audit-results")]
        public IActionResult GetAuditResults()
        {
            var result = new {
                resumo = new[] {
                    new { categoria = "Equipamentos", totalPID = 120, total3D = 110, faltamNo3D = 10, faltamNoPID = 2 },
                    new { categoria = "Válvulas", totalPID = 450, total3D = 400, faltamNo3D = 50, faltamNoPID = 5 },
                    new { categoria = "Instrumentos", totalPID = 300, total3D = 280, faltamNo3D = 20, faltamNoPID = 0 },
                    new { categoria = "Linhas", totalPID = 580, total3D = 490, faltamNo3D = 90, faltamNoPID = 10 }
                },
                itens = new[] {
                    new { id = 1, tag = "P-100", categoria = "Equipamentos", desenhoPID = "DWG-001", desenho3D = "MOD-3D-01", specPID = "CS300", spec3D = "CS300", statusCruzamento = "Match Perfeito" },
                    new { id = 2, tag = "V-102", categoria = "Válvulas", desenhoPID = "DWG-001", desenho3D = "—", specPID = "S1", spec3D = "—", statusCruzamento = "Falta Modelar no 3D" }
                }
            };
            return Ok(result);
        }
    }

    public class AttributeMap
    {
        public string id { get; set; }
        public string name { get; set; }
        
        [JsonPropertyName("pidCol")] 
        public string pidCol { get; set; }
        
        [JsonPropertyName("pipingCol")]
        public string pipingCol { get; set; }
        
        public bool isTagPart { get; set; } 
        public bool isFixedTag { get; set; }
    }
public class ScheduleItem
    {
        public string Tag { get; set; }
        public DateTime Data { get; set; }
    }
    public class CategoryMapping
    {
        public string id { get; set; }
        public string label { get; set; }
        public string pidTable { get; set; }
        public string pipingTable { get; set; }
        
        public bool isComposedTag { get; set; } 
        
        public List<AttributeMap> attributes { get; set; }
    }
}