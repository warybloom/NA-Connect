using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using NA_Connect.Services; 
using APS.WebApp.Services;
using Microsoft.Extensions.Logging;
using NA_Connect.Models; 
using Newtonsoft.Json; 
using APS.WebApp.Models;
using System.Threading.Tasks; 
using System; 
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace NA_Connect.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly DatabaseService _db;
        private readonly DatabaseService _dbService;
        private readonly GrdAutomationService _grdAutomationService;       
        private readonly ILogger<AdminController> _logger;
        private readonly IConfiguration _configuration;
        private readonly AutodeskApiService _autodeskApi; 
        private readonly string _connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
        
        public AdminController(
            DatabaseService dbService, 
            GrdAutomationService grdAutomationService,
            ILogger<AdminController> logger,
            IConfiguration configuration,
            AutodeskApiService autodeskApi) 
        {
            _db = dbService; 
            _dbService = dbService; 
            _grdAutomationService = grdAutomationService;
            _logger = logger;
            _configuration = configuration;
            _autodeskApi = autodeskApi; 
        }

        // ==========================================
        // 🏗️ GESTÃO DE PROJETOS
        // ==========================================

        [HttpGet("projetos")]
        public IActionResult GetProjetos() 
        {
            try {
                var lista = _db.ListarProjetosAdmin();
                return Ok(lista ?? new List<ProjectAdminDTO>());
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("atualizar-configuracao")]
        public IActionResult AtualizarConfiguracao([FromBody] UpdateConfigPayload payload)
        {
            try 
            {
                _dbService.SalvarConfiguracaoProjetoManual(
                    payload.projectIdAcc, 
                    payload.NormaID, 
                    payload.PacoteOCRID
                );
                return Ok(new { mensagem = "Configuração salva com sucesso!" });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch("projeto/{id}")]
        public IActionResult PatchProjeto(int id, [FromBody] JsonElement payload)
        {
            try 
            {
                bool isAtivo = payload.GetProperty("is_Ativo").GetBoolean();
                
                int? normaId = payload.TryGetProperty("NormaID", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetInt32() : null;
                int? pacoteId = payload.TryGetProperty("PacoteOCRID", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetInt32() : null;

                int statusInt = isAtivo ? 1 : 0;
                
                _dbService.AtualizarStatusProjeto(id, statusInt, normaId, pacoteId);
                
                return Ok(new { mensagem = "Configuração sincronizada com sucesso!" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ==========================================
        // 🌐 ECOSSISTEMA LDP (LAZY LOADING)
        // ==========================================

        /// <summary>
        /// Busca pastas do Autodesk ACC de forma performática (Nível por Nível).
        /// </summary>
        [HttpGet("pastas/{projectIdAcc}")]
        public async Task<IActionResult> GetPastasProjetoAutodesk(string projectIdAcc, [FromQuery] string? folderId = null)
        {
            try
            {
                var accessToken = HttpContext.Session.GetString("AccessToken");
                if (string.IsNullOrEmpty(accessToken)) 
                    return Unauthorized(new { message = "Sessão expirada." });

                var projeto = _dbService.ObterProjetoPorIdAcc(projectIdAcc);
                if (projeto == null) return NotFound();

                string targetFolderId = folderId;

                // Se não informar folderId no QueryString, buscamos a pasta raiz "Project Files"
                if (string.IsNullOrEmpty(targetFolderId))
                {
                    var rootFolderRaw = await _autodeskApi.GetProjectRootFolderAsync(projeto.Account_ID_ACC, projectIdAcc, accessToken);
                    if (rootFolderRaw is null) return NotFound("Raiz não encontrada.");
                    
                    JsonElement rootFolder = (JsonElement)rootFolderRaw;
                    targetFolderId = rootFolder.GetProperty("id").GetString();
                }

                // Busca apenas o conteúdo direto desta pasta (Muito mais rápido que recursão)
                var conteudos = await _autodeskApi.GetFolderContentsSimpleAsync(projectIdAcc, targetFolderId, accessToken);
                
                // Filtra apenas pastas para o mapeamento do ecossistema
                var pastas = conteudos.Where(f => f.Type == "folders" || f.Type == "projectFiles").ToList();

                return Ok(pastas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar nível de pasta para o projeto {projectIdAcc}");
                return StatusCode(500, ex.Message);
            }
        }

        // 📦 DTO para transporte de dados do Ecossistema LDP
        public class LdpConfigDto
{
    public int ProjetoId { get; set; }
    public string NomeConfig { get; set; }
    public string FolderIdLdp { get; set; }
    public string FolderIdCronograma { get; set; }
    public string? FolderIdMasterList { get; set; }
    public string SheetName { get; set; }
    public int HeaderRow { get; set; }
    public string MappingJSON { get; set; }
}


[HttpPost("projeto/configurar-ldp")]
public async Task<IActionResult> SalvarConfiguracaoLDP([FromBody] LdpConfigDto payload)
{
    // Declaramos a variável fora para ser acessível no retorno final
    int configIdGerado = 0;

    try 
    {
        using (var conexao = new SqlConnection(_configuration.GetConnectionString("LocalConnection")))
        {
            await conexao.OpenAsync();

            var sqlUpsertConfig = @"
                MERGE INTO [dbo].[ConfiguracoesLDP] AS target
                USING (SELECT @NomeConfig AS NomeConfig) AS source
                ON target.[NomeConfig] = source.NomeConfig
                WHEN MATCHED THEN
                    UPDATE SET
                        [Folder_ID_LDP] = @FolderIdLdp,
                        [Folder_ID_Cronograma] = @FolderIdCronograma,
                        [Folder_MasterList_URN] = @FolderIdMasterList,
                        [SheetName] = @SheetName,
                        [HeaderRow] = @HeaderRow,
                        [MappingJSON] = @MappingJSON,
                        [StatusAtivo] = 1
                WHEN NOT MATCHED THEN
                    INSERT ([NomeConfig], [Folder_ID_LDP], [Folder_ID_Cronograma], [Folder_MasterList_URN], [StatusAtivo], [MappingJSON], [SheetName], [HeaderRow])
                    VALUES (@NomeConfig, @FolderIdLdp, @FolderIdCronograma, @FolderIdMasterList, 1, @MappingJSON, @SheetName, @HeaderRow)
                OUTPUT inserted.[ConfigLDPID];";

            configIdGerado = await conexao.QuerySingleAsync<int>(sqlUpsertConfig, new {
                NomeConfig = payload.NomeConfig,
                FolderIdLdp = payload.FolderIdLdp,
                FolderIdCronograma = payload.FolderIdCronograma,
                FolderIdMasterList = payload.FolderIdMasterList,
                SheetName = payload.SheetName,
                HeaderRow = payload.HeaderRow,
                MappingJSON = payload.MappingJSON
            });

            var sqlUpdateMetadados = @"
                UPDATE M
                SET M.[ConfigLDPID] = @ConfigId
                FROM [dbo].[Metadados_Projetos] M
                INNER JOIN [dbo].[Projetos] P ON M.[Project_ID_ACC] = P.[Project_ID_ACC]
                WHERE P.[ProjetoID] = @ProjetoId";

            var sqlUpdateProjeto = @"
                UPDATE [dbo].[Projetos] 
                SET [ConfigLDPID] = @ConfigId 
                WHERE [ProjetoID] = @ProjetoId";

            await conexao.ExecuteAsync(sqlUpdateMetadados, new { ConfigId = configIdGerado, ProjetoId = payload.ProjetoId });
            await conexao.ExecuteAsync(sqlUpdateProjeto, new { ConfigId = configIdGerado, ProjetoId = payload.ProjetoId });
        }

        // Agora o configIdGerado existe neste contexto
        return Ok(new { message = "Ecossistema LDP ativado com sucesso!", configId = configIdGerado });
    }
    catch (Exception ex) 
    { 
        _logger.LogError(ex, "Erro crítico ao gravar Ecossistema LDP.");
        return StatusCode(500, $"Falha na gravação: {ex.Message}"); 
    }
}

[HttpGet("projeto/listar-configuracoes-ldp")]
public async Task<IActionResult> ListarConfiguracoesLDP()
{
    try
    {
        using (var conexao = new SqlConnection(_configuration.GetConnectionString("LocalConnection")))
        {
            await conexao.OpenAsync();

            var sql = "SELECT ConfigLDPID, NomeConfig FROM [dbo].[ConfiguracoesLDP] WHERE StatusAtivo = 1 ORDER BY NomeConfig ASC";

            var lista = await conexao.QueryAsync(sql);

            // Projeta explicitamente para bater com a interface TemplateLDP do front
            var resultado = lista.Select(x => new
            {
                configLDPID = x.ConfigLDPID.ToString(),
                nomeConfig = (string)x.NomeConfig
            });

            return Ok(resultado);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Erro ao listar configurações LDP.");
        return StatusCode(500, "Erro ao carregar modelos de configuração.");
    }
}
        // ==========================================
        // 📜 CONFIGURAÇÃO DE NORMAS
        // ==========================================

        [HttpGet("normas")]
        public IActionResult GetNormas() 
        {
            try {
                var lista = _db.ListarNormas();
                return Ok(lista ?? new List<NormaDTO>());
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao listar normas");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("salvar-norma")]
        public IActionResult SaveNorma([FromBody] NormaDTO norma) 
        {
            try {
                if (string.IsNullOrEmpty(norma.Nome_Norma)) return BadRequest("Nome da norma obrigatório.");
                _db.SalvarNormaCompleta(norma);
                return Ok(new { message = "Norma salva com sucesso!" });
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("norma/{id}")]
        public IActionResult DeleteNorma(int id)
        {
            try {
                _db.ExcluirNorma(id);
                return Ok(new { message = "Norma removida com sucesso!" });
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // 📋 CONFIGURAÇÃO DE TEMPLATES
        // ==========================================

        [HttpGet("templates")]
        public IActionResult GetTemplates()
        {
            try {
                return Ok(_db.ListarTemplates());
            } catch (Exception ex) { 
                return StatusCode(500, ex.Message); 
            }
        }

        

        [HttpPost("template")]
        public IActionResult CreateTemplate([FromBody] TemplateDTO tpl)
        {
            try 
            {
                if (string.IsNullOrEmpty(tpl.Sigla_Doc) || string.IsNullOrEmpty(tpl.Template_Nome)) 
                    return BadRequest("A Sigla e o Nome do Arquivo são obrigatórios.");

                _db.CriarTemplate(tpl);
                return Ok(new { message = "Template cadastrado com sucesso!" });
            } 
            catch (Exception ex) 
            {
                return StatusCode(500, "Erro interno ao criar template.");
            }
        }

        [HttpPost("templates/upload-pacote/{normaId}")]
        public IActionResult UploadPacoteCompleto(int normaId, [FromForm] IFormCollection data)
        {
            try 
            {
                var jsonMetadados = data["metadados"];
                var listaTemplates = JsonConvert.DeserializeObject<List<TemplateDTO>>(jsonMetadados);
                var arquivos = data.Files;

                if (listaTemplates == null || !listaTemplates.Any()) 
                    return BadRequest("Lista de templates vazia.");

                _db.SalvarPacoteTemplatesComArquivos(listaTemplates, arquivos, normaId);
                return Ok(new { message = "Pacote atualizado com sucesso!" });
            }
            catch (Exception ex) 
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // 👁️ GESTÃO DE VISÃO (OCR)
        // ==========================================

        [HttpGet("ocr/pacotes")]
        public IActionResult GetPacotes()
        {
            try {
                return Ok(_db.ListarPacotesOCR()); 
            } catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("ocr/pacotes")]
        public IActionResult CriarPacote([FromBody] JsonElement data)
        {
            try 
            {
                if (!data.TryGetProperty("Nome_Pacote", out JsonElement nomeElement))
                    return BadRequest("Nome do pacote é obrigatório.");

                string nome = nomeElement.GetString();
                if (string.IsNullOrEmpty(nome)) return BadRequest("Nome é obrigatório.");

                int newId = _db.InserirNovoPacoteOCR(nome);
                return Ok(new { pacoteID = newId, nome_Pacote = nome });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar novo pacote OCR");
                return StatusCode(500, $"Erro interno: {ex.Message}");
            }
        }

        [HttpPost("projeto/{id}/vincular-ocr")]
        public IActionResult VincularOcr(string id, [FromBody] VincularOcrRequest request)
        {
            try 
            {
                if (request == null || request.PacoteID <= 0) return BadRequest("ID do pacote inválido.");
                _db.VincularProjetoAoPacoteOCR(id, request.PacoteID);
                return Ok(new { message = "Vínculo de OCR atualizado com sucesso!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro interno: {ex.Message}");
            }
        }

        [HttpPatch("projeto/{projectId}/vincular-ocr")]
        public async Task<IActionResult> VincularOCR(int projectId, [FromBody] JsonElement data)
        {
            try 
            {
                int? pacoteId = null;
                if (data.TryGetProperty("pacoteId", out JsonElement idElement))
                {
                    if (idElement.ValueKind != JsonValueKind.Null) pacoteId = idElement.GetInt32();
                }
                _db.AtualizarProjetoPacoteOCR(projectId, pacoteId);
                return Ok(new { message = "Vínculo de OCR atualizado com sucesso!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("ocr/limpar-coordenadas")]
        public IActionResult LimparCoordenadas([FromQuery] int pacoteId, [FromQuery] string tipoFolha)
        {
            try 
            {
                _db.LimparCoordenadasAntigas(pacoteId, tipoFolha);
                return Ok(new { message = $"Calibração {tipoFolha} removida!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Erro ao deletar dados.");
            }
        }

        [HttpDelete("normas/deletar-template/{id}")]
public IActionResult DeletarTemplate(int id)
{
    try
    {
        _db.DeletarTemplate(id);
        return Ok(new { message = "Template removido com sucesso!" });
    }
    catch (Exception ex)
    {
        return StatusCode(500, "Erro ao deletar template.");
    }
}

        [HttpPost("ocr/salvar-coordenadas")]
        public IActionResult SalvarCoordenadas([FromBody] List<OCRCoordenadaDTO> coords, [FromQuery] int pacoteId, [FromQuery] string tipoFolha)
        {
            try 
            {
                _db.LimparCoordenadasAntigas(pacoteId, tipoFolha); 
                foreach(var c in coords)
                {
                    _db.SalvarNovaCoordenada(pacoteId, tipoFolha, c);
                }
                return Ok(new { message = "Calibração salva com sucesso!" });
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        // ==========================================
        // 🤖 AUTOMAÇÃO & ROBÔ
        // ==========================================

        [HttpPost("robot/trigger-all")]
        public async Task<IActionResult> TriggerAllProjects()
        {
            try
            {
                var projetosAtivos = _dbService.GetProjetosAtivos();
                if (projetosAtivos.Rows.Count == 0) return Ok(new { message = "Nenhum projeto ativo." });

                foreach (System.Data.DataRow row in projetosAtivos.Rows)
                {
                    await _grdAutomationService.ExecuteFullProjectSyncAsync(
                        row["Project_ID_ACC"].ToString(),
                        row["Account_ID_ACC"].ToString(),
                        row["Nome_Projeto"].ToString()
                    );
                }
                return Ok(new { message = "Sincronização global concluída!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("projeto/{projectIdAcc}/sincronizar-tudo")]
        public async Task<IActionResult> SincronizarTudo(string projectIdAcc)
        {
            try
            {
                var projeto = _dbService.ObterProjetoPorIdAcc(projectIdAcc); 
                if (projeto == null) return NotFound("Projeto não encontrado.");

                await _grdAutomationService.ExecuteFullProjectSyncAsync(
                    projeto.Project_ID_ACC, 
                    projeto.Account_ID_ACC, 
                    projeto.Nome_Projeto
                );
                return Ok(new { message = "Sincronização concluída." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        [HttpGet("projeto/configuracao-ldp/{id}")]
public async Task<IActionResult> GetDetalhesConfiguracaoLDP(int id)
{
    try
    {
        using (var conexao = new SqlConnection(_configuration.GetConnectionString("LocalConnection")))
        {
            await conexao.OpenAsync();
            var sql = "SELECT * FROM [dbo].[ConfiguracoesLDP] WHERE ConfigLDPID = @id";
            var config = await conexao.QueryFirstOrDefaultAsync(sql, new { id });
            
            if (config == null) return NotFound("Template não encontrado.");
            return Ok(config);
        }
    }
    catch (Exception ex)
    {
        return StatusCode(500, "Erro ao carregar detalhes do template.");
    }
}
    }

    public class VincularOcrRequest { public int PacoteID { get; set; } }
}