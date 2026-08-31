// Ficheiro: Areas/DataManagement/Controllers/DataController.cs

#nullable disable
using APS.WebApp.Models;
using APS.WebApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AccIntegration.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NA_Connect.Services;

namespace APS.WebApp.Areas.DataManagement.Controllers
{
    public class RoleServiceConfig
    {
        public string AccessLevel { get; set; } = "member";
        public List<string> Products { get; set; } = new List<string> { "docs" };
    }

    [Area("DataManagement")]
    [ApiController]
    [Route("[area]/[controller]/[action]")]
    public class DataController : Controller
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly TokenManager _tokenManager;
        private readonly AccountUsersService _accountUsersService;
        private readonly PlaceholderCreationService _placeholderService;
        private readonly NomenclaturaService _nomenclaturaService;
        private readonly ApplicationDbContext _context;
        private readonly LdpUpdateService _ldpUpdateService;
        private readonly ILogger<DataController> _logger;
        private readonly DatabaseService _dbService;
        private readonly AccScheduleSyncService _scheduleService;
        
        public DataController(
            AutodeskApiService autodeskApi,
            IConfiguration configuration,
            TokenManager tokenManager,
            AccountUsersService accountUsersService,
            PlaceholderCreationService placeholderService,
            NomenclaturaService nomenclaturaService,
            ApplicationDbContext context,
            LdpUpdateService ldpUpdateService,
            ILogger<DataController> logger,
            DatabaseService dbService,
            AccScheduleSyncService scheduleService)
        {
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _tokenManager = tokenManager;
            _accountUsersService = accountUsersService;
            _placeholderService = placeholderService;
            _nomenclaturaService = nomenclaturaService;
            _context = context;
            _ldpUpdateService = ldpUpdateService;
            _logger = logger;
            _dbService = dbService;
            _scheduleService = scheduleService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateProjectFromTemplate([FromForm] string hubId, [FromForm] string templateId, IFormFile memberSheet)
        {
            _logger.LogInformation(">>> CreateProjectFromTemplate recebido hubId do form: {HubId}", hubId);

            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });
            if (memberSheet == null || memberSheet.Length == 0) return BadRequest(new { message = "Planilha de membros não enviada." });

            const string naHubId = "b.2ee333e9-3e57-42dd-ad89-86864dd86845"; 
            const string bimHubId = "b.72a17677-f6d2-42b8-8a37-e1e7c8a93ea3"; 

            try
            {
                var accountId = hubId.StartsWith("b.") ? hubId.Substring(2) : hubId;
                var allAccountUsers = await _accountUsersService.GetAccountUsersAsync(accountId);
                var userEmailToIdMap = allAccountUsers.ToDictionary(u => u.Email, u => u.Id, StringComparer.OrdinalIgnoreCase);

                string roleMappingSectionKey;
                if (hubId.Equals(naHubId, StringComparison.OrdinalIgnoreCase))
                {
                    roleMappingSectionKey = "RoleMappings_NA"; 
                }
                else if (hubId.Equals(bimHubId, StringComparison.OrdinalIgnoreCase))
                {
                    roleMappingSectionKey = "RoleMappings_BIM"; 
                }
                else
                {
                    _logger.LogError("HubId {HubId} recebido não corresponde a nenhuma conta configurada (N&A ou BIM).", hubId);
                    return BadRequest(new { message = $"A conta selecionada ({hubId}) não está configurada para esta operação." });
                }

                _logger.LogInformation("Usando a seção de configuração de roles: {RoleMappingSectionKey}", roleMappingSectionKey);

                var roleNameToIdMap = _configuration.GetSection(roleMappingSectionKey).Get<Dictionary<string, string>>();

                if (roleNameToIdMap == null || !roleNameToIdMap.Any())
                {
                    throw new Exception($"A seção de mapeamento de roles '{roleMappingSectionKey}' não foi encontrada ou está vazia no appsettings.json."); 
                }

                string projectName = string.Empty;
                string jobNumber = string.Empty;
                var usersToImport = new List<AccUser>();
                var userRolesToAssign = new Dictionary<string, List<string>>();
                var disciplinesToCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var servicesByRoleMap = _configuration.GetSection("ServiceActivationByRole").Get<Dictionary<string, RoleServiceConfig>>() ?? new Dictionary<string, RoleServiceConfig>(); 
                var disciplineFolderMap = _configuration.GetSection("DisciplineFolderMapping").Get<Dictionary<string, string>>() ?? new Dictionary<string, string>(); 

                using (var stream = new MemoryStream())
                {
                    await memberSheet.CopyToAsync(stream);
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial; 
                    using (var package = new ExcelPackage(stream))
                    {
                        ExcelWorksheet worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null || worksheet.Dimension == null) throw new Exception("A planilha está vazia ou corrompida.");

                        projectName = worksheet.Cells["M3"].Value?.ToString()?.Trim() ?? string.Empty;
                        jobNumber = worksheet.Cells["G3"].Value?.ToString()?.Trim() ?? string.Empty;

                        if (string.IsNullOrEmpty(projectName) || string.IsNullOrEmpty(jobNumber))
                        {
                            throw new Exception("Nome do Projeto (M3) ou Número (G3) não encontrado na planilha.");
                        }

                         var disciplineMappings = new[]
                        {
                            (checkCell: "B10", nameCell: "C10"), (checkCell: "I10", nameCell: "J10"), (checkCell: "P10", nameCell: "Q10"), (checkCell: "W10", nameCell: "X10"),
                            (checkCell: "B12", nameCell: "C12"), (checkCell: "I12", nameCell: "J12"), (checkCell: "P12", nameCell: "Q12"), (checkCell: "W12", nameCell: "X12"),
                            (checkCell: "B14", nameCell: "C14"), (checkCell: "I14", nameCell: "J14"), (checkCell: "P14", nameCell: "Q14"), (checkCell: "W14", nameCell: "X14"),
                            (checkCell: "B16", nameCell: "C16")
                        };
                        foreach (var mapping in disciplineMappings)
                        {
                            var checkboxValue = worksheet.Cells[mapping.checkCell].Value?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(checkboxValue))
                            {
                                var disciplineName = worksheet.Cells[mapping.nameCell].Value?.ToString()?.Trim();
                                if (!string.IsNullOrEmpty(disciplineName) && !disciplineName.ToLower().Contains("vazio"))
                                {
                                    disciplinesToCreate.Add(disciplineName);
                                }
                            }
                        }

                        for (int row = 20; row <= worksheet.Dimension.Rows; row += 2)
                        {
                            var roleName = worksheet.Cells[row, 9].Value?.ToString()?.Trim();
                            var email = worksheet.Cells[row, 19].Value?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(roleName)) continue;

                            if (userEmailToIdMap.ContainsKey(email) &&
                                roleNameToIdMap.TryGetValue(roleName, out var roleId)) 
                            {
                                if (servicesByRoleMap.TryGetValue(roleName, out var serviceConfig)) 
                                {
                                    if (!usersToImport.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var accessLevelForApi = serviceConfig.AccessLevel.Equals("admin", StringComparison.OrdinalIgnoreCase) ? "administrator" : "member";
                                        var products = serviceConfig.Products.Select(productKey => new AccUserProductAccess { Key = productKey, Access = accessLevelForApi }).ToList();

                                        if (serviceConfig.AccessLevel.Equals("admin", StringComparison.OrdinalIgnoreCase) && !products.Any(p => p.Key == "projectAdministration"))
                                        {
                                            products.Add(new AccUserProductAccess { Key = "projectAdministration", Access = "administrator" });
                                        }
                                        usersToImport.Add(new AccUser
                                        {
                                            Email = email,
                                            Products = products
                                        });
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("Configuração de serviço para a role '{RoleName}' não encontrada. Serviços podem não ser ativados para {Email}.", roleName, email);
                                }

                                if (!userRolesToAssign.ContainsKey(email)) userRolesToAssign[email] = new List<string>();
                                if (!userRolesToAssign[email].Contains(roleId))
                                {
                                    userRolesToAssign[email].Add(roleId);
                                }
                            }
                            else if (!roleNameToIdMap.ContainsKey(roleName))
                            {
                                 _logger.LogWarning("A role '{RoleName}' (planilha, user {Email}) não foi encontrada na seção {Section}. Atribuição ignorada.", roleName, email, roleMappingSectionKey);
                            }
                        }
                    } 
                } 

                var currentUserEmail = "christian.santana@naconsult.com.br"; 
                const string adminRoleName = "13. ADMINISTRAÇÃO 3D";

                if (!string.IsNullOrEmpty(currentUserEmail) && userEmailToIdMap.ContainsKey(currentUserEmail))
                {
                    if (!usersToImport.Any(u => u.Email.Equals(currentUserEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (servicesByRoleMap.TryGetValue(adminRoleName, out var adminServiceConfig))
                        {
                             var products = adminServiceConfig.Products.Select(p => new AccUserProductAccess { Key = p, Access = "administrator" }).ToList();
                            if (!products.Any(p => p.Key == "projectAdministration"))
                            {
                                products.Add(new AccUserProductAccess { Key = "projectAdministration", Access = "administrator" });
                            }
                            usersToImport.Add(new AccUser { Email = currentUserEmail, Products = products });
                        }
                         else { _logger.LogWarning("Configuração de serviço para role admin '{AdminRoleName}' não encontrada.", adminRoleName); }
                    }

                    if (roleNameToIdMap.TryGetValue(adminRoleName, out var adminRoleId))
                    {
                        if (!userRolesToAssign.ContainsKey(currentUserEmail)) userRolesToAssign[currentUserEmail] = new List<string>();
                        if (!userRolesToAssign[currentUserEmail].Contains(adminRoleId)) userRolesToAssign[currentUserEmail].Add(adminRoleId);
                    }
                     else { _logger.LogWarning("Role admin '{AdminRoleName}' não encontrada na seção {Section}.", adminRoleName, roleMappingSectionKey); }
                }

                var startDate = DateTime.UtcNow;
                var endDate = startDate.AddYears(5);
                var projectData = new AccProjectData
                {
                    Name = projectName, JobNumber = jobNumber,
                    StartDate = startDate.ToString("yyyy-MM-dd"), EndDate = endDate.ToString("yyyy-MM-dd"),
                    Template = new ProjectTemplateInfo { ProjectId = templateId }, Type = "Commercial"
                };
                _logger.LogInformation("Criando projeto '{ProjectName}' (Hub: {HubId})...", projectName, hubId);
                string newProjectId = await _autodeskApi.CreateAccProjectFromTemplateAsync(accountId, projectData, accessToken);
                _logger.LogInformation("Projeto criado com ID: {NewProjectId}.", newProjectId);

                if (usersToImport.Any())
                {
                    _logger.LogInformation("Importando {Count} usuários...", usersToImport.Count);
                    await _autodeskApi.ImportUsersToAccProjectAsync(newProjectId, new AccUserImport { Users = usersToImport }, accessToken);
                    _logger.LogInformation("Importação enviada. Aguardando processamento (15s)...");
                    await Task.Delay(15000);

                     _logger.LogInformation("Atribuindo roles...");
                    foreach (var userRolePair in userRolesToAssign)
                    {
                        var userEmail = userRolePair.Key;
                        var roleIds = userRolePair.Value.Distinct().ToList();
                        AccProjectUser? projectUser = null;
                        for (int i = 0; i < 12; i++) 
                        {
                            _logger.LogDebug("[AssignRoles] Tentativa {Attempt}/12 buscar {Email}...", i + 1, userEmail);
                            projectUser = await _autodeskApi.GetProjectUserByEmailAsync(newProjectId, userEmail, accessToken);
                            if (projectUser != null && !string.IsNullOrEmpty(projectUser.Id)) break;
                            _logger.LogDebug("[AssignRoles] Usuário {Email} não encontrado, esperando 5s...", userEmail);
                            await Task.Delay(5000);
                        }
                         if (projectUser != null && !string.IsNullOrEmpty(projectUser.Id)) {
                            try {
                                _logger.LogInformation("[AssignRoles] Atribuindo roles {RoleIds} a {Email} (ID: {UserId})...", string.Join(",", roleIds), userEmail, projectUser.Id);
                                await _autodeskApi.AssignAccRolesToUserAsync(newProjectId, projectUser.Id, roleIds, accessToken);
                                _logger.LogInformation("[AssignRoles] Roles atribuídas com sucesso para {Email}.", userEmail);
                            } catch (Exception assignEx) { _logger.LogError(assignEx, "[AssignRoles] Erro ao atribuir roles a {Email}", userEmail); }
                         } else { _logger.LogWarning("[AssignRoles] Usuário {Email} não encontrado após tentativas p/ atribuir roles.", userEmail); }
                    }
                }

                if (disciplinesToCreate.Any())
                {
                    _logger.LogInformation("Iniciando criação de pastas de disciplina...");
                    JsonElement? rootFolder = null; int attempts = 0; const int maxAttempts = 30;
                    while (attempts < maxAttempts) 
                    {
                        attempts++;
                        try {
                            _logger.LogDebug("[CreateFolders] Tentativa {Attempt}/{Max} buscar pasta raiz...", attempts, maxAttempts);
                            rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, newProjectId, accessToken); 
                            if (rootFolder != null) { _logger.LogDebug("[CreateFolders] Pasta raiz encontrada."); break; }
                             _logger.LogWarning("[CreateFolders] GetProjectRootFolderAsync retornou null na tentativa {Attempt}.", attempts);
                        } catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound || httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden || (httpEx.Message != null && httpEx.Message.Contains("Only active projects may be accessed"))) {
                            _logger.LogWarning("[CreateFolders] Pasta raiz não pronta (Tentativa {A}/{M}): {S}. Aguardando 5s...", attempts, maxAttempts, httpEx.StatusCode);
                            if (attempts >= maxAttempts) throw new Exception($"Pastas não ficaram disponíveis após {maxAttempts} tentativas. Erro final: {httpEx.Message}");
                        } catch(Exception ex) {
                             _logger.LogError(ex, "[CreateFolders] Erro buscar pasta raiz (Tentativa {A}). Aguardando 5s...", attempts);
                             if (attempts >= maxAttempts) throw; 
                        }
                        await Task.Delay(5000); 
                    }
                    if (rootFolder == null) throw new Exception($"Pasta raiz 'Project Files' não encontrada para {newProjectId} após {maxAttempts} tentativas.");

                    var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
                    if (string.IsNullOrEmpty(rootFolderId)) throw new Exception($"ID da Pasta Raiz inválido para {newProjectId}.");

                     _logger.LogInformation("Pasta raiz encontrada (ID: {RootFolderId}). Criando '04 - ENGENHARIA'...", rootFolderId);

                    string? engineeringFolderId = null;
                    int folderCreationAttempts = 0;
                    const int maxFolderCreationAttempts = 12; 
                    const int folderCreationDelayMs = 5000;    
                    bool attemptFindExisting = false;

                    while (engineeringFolderId == null && folderCreationAttempts < maxFolderCreationAttempts)
                    {
                        folderCreationAttempts++;
                        string? currentAttemptResult = null;

                        try
                        {
                            _logger.LogInformation("[CreateFolders][Attempt {Attempt}/{MaxAttempts}] Trying API: CreateFolderAsync for '04 - ENGENHARIA'...", folderCreationAttempts, maxFolderCreationAttempts);
                            currentAttemptResult = await _autodeskApi.CreateFolderAsync(newProjectId, rootFolderId, "04 - ENGENHARIA", accessToken); 

                            if (currentAttemptResult != null)
                            {
                                engineeringFolderId = currentAttemptResult;
                                _logger.LogInformation("[CreateFolders][Attempt {Attempt}] SUCCESS via Create: Folder '04 - ENGENHARIA' created with ID: {FolderId}", folderCreationAttempts, engineeringFolderId);
                                break;
                            }
                            else
                            {
                                 _logger.LogWarning("[CreateFolders][Attempt {Attempt}] CreateFolderAsync returned null (409 Conflict or 423 Locked?). Will attempt to find existing.", folderCreationAttempts);
                                 attemptFindExisting = true;
                            }
                        }
                        catch (HttpRequestException httpEx) {
                            _logger.LogError(httpEx, "[CreateFolders][Attempt {Attempt}] HttpRequestException during CreateFolderAsync.", folderCreationAttempts); throw;
                        } catch (Exception ex) {
                             _logger.LogError(ex, "[CreateFolders][Attempt {Attempt}] Unexpected error during CreateFolderAsync.", folderCreationAttempts); throw;
                        }

                        if (engineeringFolderId == null && attemptFindExisting)
                        {
                             try
                             {
                                  _logger.LogInformation("[CreateFolders][Attempt {Attempt}] Trying API: FindFolderByNameRecursiveAsync for '04 - ENGENHARIA'...", folderCreationAttempts);
                                  engineeringFolderId = await _autodeskApi.FindFolderByNameRecursiveAsync(newProjectId, rootFolderId, "04 - ENGENHARIA", accessToken); 

                                  if (engineeringFolderId != null)
                                  {
                                       _logger.LogInformation("[CreateFolders][Attempt {Attempt}] SUCCESS via Find: Found existing folder '04 - ENGENHARIA' with ID: {FolderId}", folderCreationAttempts, engineeringFolderId);
                                       break;
                                  }
                                  else { _logger.LogWarning("[CreateFolders][Attempt {Attempt}] FindFolderByNameRecursiveAsync did not find '04 - ENGENHARIA'.", folderCreationAttempts); }
                             }
                             catch (Exception findEx) { _logger.LogError(findEx, "[CreateFolders][Attempt {Attempt}] Error during FindFolderByNameRecursiveAsync.", folderCreationAttempts); }
                        }

                        if (engineeringFolderId == null && folderCreationAttempts < maxFolderCreationAttempts)
                        {
                            _logger.LogWarning("[CreateFolders][Attempt {Attempt}] Folder '04 - ENGENHARIA' not obtained. Waiting {Delay}ms...", folderCreationAttempts, folderCreationDelayMs);
                            await Task.Delay(folderCreationDelayMs);
                        }
                        else if (engineeringFolderId == null && folderCreationAttempts >= maxFolderCreationAttempts)
                        {
                             _logger.LogError("[CreateFolders] FINAL CHECK FAILED: Could not create or find folder '04 - ENGENHARIA' after {MaxAttempts} attempts.", maxFolderCreationAttempts);
                             throw new Exception($"Não foi possível criar ou encontrar a pasta '04 - ENGENHARIA' para o projeto {newProjectId} após {maxFolderCreationAttempts} tentativas.");
                        }
                    } 

                    if (string.IsNullOrEmpty(engineeringFolderId))
                    {
                         _logger.LogError("[CreateFolders] CRITICAL FAILURE: engineeringFolderId is still null/empty after retry loop for project {ProjectId}.", newProjectId);
                         throw new Exception($"Falha crítica inesperada ao obter ID da pasta '04 - ENGENHARIA' para {newProjectId}.");
                    } else { _logger.LogInformation("[CreateFolders] FINAL CHECK SUCCESS: Proceeding with engineeringFolderId: {FolderId}", engineeringFolderId); }


                    _logger.LogInformation("Criando subpastas de disciplina dentro de {EngineeringFolderId}...", engineeringFolderId);
                    foreach (var discipline in disciplinesToCreate)
                    {
                        if (disciplineFolderMap.TryGetValue(discipline, out var fullFolderName))
                        {
                            try {
                                 _logger.LogDebug("[CreateSubFolders] Criando '{Folder}'...", fullFolderName);
                                await _autodeskApi.CreateFolderAsync(newProjectId, engineeringFolderId, fullFolderName, accessToken);
                                 _logger.LogDebug("[CreateSubFolders] Pasta '{Folder}' criada ou já existia.", fullFolderName);
                            } catch (Exception folderEx) { _logger.LogError(folderEx, "[CreateSubFolders] Erro ao criar subpasta '{Folder}'.", fullFolderName); }
                        } else { _logger.LogWarning("[CreateSubFolders] Mapeamento não encontrado para '{Discipline}'. Subpasta não criada.", discipline); }
                    }
                } 

                _logger.LogInformation("Processo concluído com sucesso para projeto '{ProjectName}' (Hub: {HubId}).", projectName, hubId);
                return Ok(new { message = $"Projeto '{projectName}' criado com sucesso usando config '{roleMappingSectionKey}'!", projectId = newProjectId });

            } 
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro GERAL durante o processo de criação do projeto a partir do template (Hub: {HubId}).", hubId);
                return StatusCode(500, new { message = "Ocorreu um erro no servidor durante a criação do projeto.", details = ex.Message });
            }
        } 

        [HttpGet]
            public IActionResult GetNormaProjeto(string projectId)
            {
                if (string.IsNullOrWhiteSpace(projectId))
                    return BadRequest(new { message = "projectId é obrigatório." });

                var norma = _dbService.ObterNormaDoProjeto(projectId);

                return Ok(new
                {
                    normaId = norma?.NormaID,
                    nomeNorma = norma?.Nome_Norma ?? "Nenhuma norma configurada",
                    descricao = norma?.Descricao
                });
            }

        [HttpGet]
        public async Task<IActionResult> GetProjectTemplates([FromQuery] string hubId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão não encontrada." });

            if (string.IsNullOrEmpty(hubId))
            {
                return BadRequest(new { message = "Hub ID é obrigatório para buscar templates." });
            }

            try
            {
                var accountId = hubId.StartsWith("b.") ? hubId.Substring(2) : hubId; 

                _logger.LogInformation("Buscando templates para a conta via GetProjectTemplates: {AccountId}", accountId); 

                var templates = await _autodeskApi.GetAccProjectTemplatesAsync(accountId, accessToken);
                return Ok(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar templates de projeto para a conta {AccountId}", hubId); 
                return StatusCode(500, new { message = "Erro ao carregar templates de projeto.", details = ex.Message });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ListAccountRoles(string hubId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão não encontrada." });

            var accountId = hubId.StartsWith("b.") ? hubId.Substring(2) : hubId;

            try
            {
                var roles = await _autodeskApi.GetAccountRolesAsync(accountId, accessToken);
                return Ok(roles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Não foi possível buscar as Funções (Roles).", details = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Hubs()
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
                return Unauthorized(new { message = "Sessão não encontrada ou expirada." });
            try
            {
                var hubsData = await _autodeskApi.GetHubsAsync(accessToken);
                return Ok(hubsData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao carregar Hubs: {ex.Message}", details = ex.ToString() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Projects(string hubId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão não encontrada ou expirada." });
            try
            {
                var projectsData = await _autodeskApi.GetHubProjectsAsync(hubId, accessToken);
                return Ok(projectsData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao carregar projetos: {ex.Message}", details = ex.ToString() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProjectRootFolder(string hubId, string projectId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão inválida ou expirada." });
            if (string.IsNullOrEmpty(hubId) || string.IsNullOrEmpty(projectId)) return BadRequest(new { message = "HubId e ProjectId são obrigatórios." });
            try
            {
                var rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, projectId, accessToken);
                if (rootFolder == null) return NotFound(new { message = "A pasta raiz 'Project Files' não foi encontrada neste projeto." });
                var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
                return Ok(new { rootFolderId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Ocorreu um erro interno ao buscar a pasta raiz: {ex.Message}" });
            }
        }

        public class BreadcrumbItem { public string Id { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; }

        [HttpGet]
        public async Task<IActionResult> GetFolderPath(string projectId, string folderId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized(new { message = "Sessão expirada." });
            }

            var path = new List<BreadcrumbItem>();
            var currentFolderId = folderId;

            for (int i = 0; i < 20 && !string.IsNullOrEmpty(currentFolderId); i++) 
            {
                try 
                {
                    var folderDetailsElement = await _autodeskApi.GetFolderDetailsAsync(projectId, currentFolderId, accessToken);

                    if (folderDetailsElement == null || !folderDetailsElement.HasValue)
                    {
                         Console.WriteLine($"Detalhes não encontrados para a pasta {currentFolderId}. Interrompendo busca.");
                         break; 
                    }
                    var folderDetails = folderDetailsElement.Value;

                    var attributes = folderDetails.GetProperty("data").GetProperty("attributes");
                    var name = attributes.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : 
                               attributes.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : 
                               "Nome Desconhecido";
                    
                    path.Add(new BreadcrumbItem { Id = currentFolderId, Name = name });

                     if (!folderDetails.GetProperty("data").GetProperty("relationships").TryGetProperty("parent", out var parentRel) ||
                        !parentRel.TryGetProperty("data", out var parentData) ||
                        parentData.ValueKind == JsonValueKind.Null)
                    {
                        break; 
                    }

                    currentFolderId = parentData.GetProperty("id").GetString();
                }
                catch (System.Net.Http.HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine($"Permissão negada (403) ao acessar a pasta {currentFolderId} durante a busca do caminho. Retornando caminho parcial.");
                    break; 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro inesperado ao buscar detalhes da pasta {currentFolderId}: {ex.Message}. Retornando caminho parcial.");
                    break; 
                }
            } 

            path.Reverse();

            return Ok(path);
        }

        [HttpGet]
        public async Task<IActionResult> GetItemVersions(string projectId, string itemId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });
            try
            {
                var versions = await _autodeskApi.GetItemVersionsAsync(projectId, itemId, accessToken);
                return Ok(versions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Erro ao buscar versões.", details = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadNewVersion(string projectId, string itemId, string folderId, IFormFile newVersionFile)
        {
            if (newVersionFile == null || newVersionFile.Length == 0) return BadRequest(new { message = "Nenhum ficheiro foi selecionado." });
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });
            using var fileStream = newVersionFile.OpenReadStream();
            try
            {
                var result = await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, newVersionFile.FileName, fileStream, accessToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ocorreu um erro ao criar a nova versão.", details = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadArquivo(string storageUrn)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized("Sessão expirada.");
            try
            {
                var downloadUrl = await _autodeskApi.GetSignedDownloadUrlAsync(storageUrn, accessToken);
                if (string.IsNullOrEmpty(downloadUrl)) return NotFound("Não foi possível gerar a URL de download.");
                return Ok(new { downloadUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro no download: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ConteudoPasta(string projectId, string folderId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });
            try
            {
                return Ok(await _autodeskApi.GetFolderContentsWithAttributesAsync(projectId, folderId, accessToken));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao carregar conteúdo da pasta: {ex.Message}", details = ex.ToString() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SyncFromProjectLdp(string hubId, string projectId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });

            try
            {
                var contexto = _dbService.ObterContextoProjeto(projectId);

                if (contexto == null || string.IsNullOrEmpty(contexto.LdpName))
                {
                    return BadRequest(new { 
                        message = "O DNA deste projeto ainda não foi mapeado no Banco de Dados.",
                        details = "Execute a automação 'SQL SERVER' primeiro para registrar o LdpName e o Idioma." 
                    });
                }

                string ldpUrn = contexto.LdpName;
                string idioma = contexto.Language?.ToUpper() ?? "PT"; 
                string padrao = contexto.NamingStandard?.ToUpper() ?? "NA"; 

                _logger.LogInformation($"[Sync] Iniciando via DB. Projeto: {projectId} | Idioma: {idioma} | Padrão: {padrao} | URN: {ldpUrn}");

                string keyColumnName;

                if (padrao == "NA")
                {
                    keyColumnName = (idioma == "EN") ? "N&A DOCUMENT NUMBER" : "NÚMERO DOCUMENTO N&A";
                }
                else 
                {
                    keyColumnName = (idioma == "EN") ? "CLIENT DOCUMENT NUMBER" : "NÚMERO DOCUMENTO CLIENTE";
                }

                _logger.LogInformation($"[Sync] Coluna chave definida: '{keyColumnName}'");

                var ldpData = new List<Dictionary<string, string>>();

                using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpUrn, accessToken))
                {
                    using (var package = new ExcelPackage(stream))
                    {
                        ExcelWorksheet worksheet = package.Workbook.Worksheets["LDP"] ?? package.Workbook.Worksheets.FirstOrDefault();
                        
                        if (worksheet == null || worksheet.Dimension == null) 
                            return BadRequest(new { message = "A planilha baixada está vazia ou inválida." });

                        string crOsDaPlanilha = worksheet.Cells[8, 25].Value?.ToString()?.Trim();

                        if (!string.IsNullOrEmpty(crOsDaPlanilha))
                        {
                            try
                            {
                                int linhasAfetadas = await _context.Database.ExecuteSqlInterpolatedAsync(
                                    $"UPDATE Metadados_Projetos SET CR_OS = {crOsDaPlanilha} WHERE Project_ID_ACC = {projectId}");

                                if (linhasAfetadas > 0)
                                {
                                    _logger.LogInformation($"💾 [BANCO DE DADOS] CR-OS '{crOsDaPlanilha}' registrado com sucesso na tabela Metadados_Projetos para o projeto {projectId}!");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"⚠️ Erro ao tentar salvar CR_OS no banco: {ex.Message}. Verifique se a coluna CR_OS existe na tabela Metadados_Projetos.");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ Aviso: A célula Y8 (CR-OS) estava vazia na planilha LDP deste projeto.");
                        }

                        int headerRow = 7;
                        int groupHeaderRow = 6;

                        for (int r = 1; r <= 20; r++) {
                            var cellVal = worksheet.Cells[r, 1].Value?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(cellVal) && (cellVal.Equals("ITEM", StringComparison.OrdinalIgnoreCase) || cellVal.Equals("ID", StringComparison.OrdinalIgnoreCase))) 
                            { 
                                headerRow = r; 
                                groupHeaderRow = r - 1; 
                                break; 
                            }
                        }

                        var rawHeaders = new List<string>();
                        string currentGroup = "";

                        for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                        {
                            var groupValue = worksheet.Cells[groupHeaderRow, col].Value?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(groupValue)) currentGroup = groupValue;

                            var headerValue = worksheet.Cells[headerRow, col].Value?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(headerValue)) { rawHeaders.Add(string.Empty); continue; }

                            string combinedHeader = headerValue;

                            if (!string.IsNullOrEmpty(currentGroup) && 
                               (currentGroup.ToUpper().Contains("EMISSÃO") || currentGroup.ToUpper().Contains("ISSUE")))
                            {
                                if (headerValue.ToUpper().Contains("PROPÓSITO") || headerValue.ToUpper().Contains("PURPOSE"))
                                    combinedHeader = $"{currentGroup} - {headerValue}"; 
                            }

                            rawHeaders.Add(Regex.Replace(combinedHeader, @"\s+", " ").Trim());
                        }

                        int keyColumnIndex = rawHeaders.FindIndex(h => h.Equals(keyColumnName, StringComparison.OrdinalIgnoreCase));
                        
                        if (keyColumnIndex == -1)
                        {
                            return BadRequest(new { message = $"A coluna chave obrigatória '{keyColumnName}' não foi encontrada na linha {headerRow} da planilha. Verifique se o padrão do projeto no banco ({padrao}/{idioma}) corresponde ao arquivo Excel." });
                        }
                        
                        keyColumnIndex += 1; 

                        for (int row = headerRow + 1; row <= worksheet.Dimension.Rows; row++)
                        {
                            var keyValue = worksheet.Cells[row, keyColumnIndex].Value?.ToString();
                            if (string.IsNullOrWhiteSpace(keyValue)) continue;

                            var rowData = new Dictionary<string, string>();
                            
                            for (int col = 1; col <= rawHeaders.Count; col++)
                            {
                                if (!string.IsNullOrEmpty(rawHeaders[col - 1]))
                                {
                                    rowData[rawHeaders[col - 1]] = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? string.Empty;
                                }
                            }
                            rowData["_DOC_NUMBER_KEY_"] = keyValue.Trim();
                            
                            ldpData.Add(rowData);
                        }
                    } 
                }

                if (ldpData.Count == 0) return Ok(new { summaryMessage = "A planilha foi lida, mas nenhuma linha de dados válida foi encontrada.", success = true });

                var result = await _autodeskApi.SyncLdpAttributesAsync(hubId, projectId, ldpData, accessToken, keyColumnName);

                if (result == null)
                {
                    return Ok(new { summaryMessage = "Sincronização enviada (sem detalhes de retorno).", totalLdpFiles = ldpData.Count, success = true });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro crítico no SyncFromProjectLdp (Via Banco).");
                return StatusCode(500, new { message = "Erro ao sincronizar.", details = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SyncLdpAttributesAction(string hubId, string projectId, [FromBody] List<Dictionary<string, string>> ldpData, string keyColumnName)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized();

            var result = await SyncLdpAttributesAsync(hubId, projectId, ldpData, accessToken, keyColumnName);
            return Ok(result);
        }

        [NonAction]
        public async Task<SyncResult> SyncLdpAttributesAsync(
            string hubId,
            string projectId,
            List<Dictionary<string, string>> ldpData,
            string accessToken,
            string keyColumnName)
        {
            if (string.IsNullOrEmpty(keyColumnName)) throw new ArgumentException("Nome da coluna chave não fornecido.");

            _logger.LogInformation($">>> Iniciando Sync. Chave usada: '{keyColumnName}'");

            var rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, projectId, accessToken);
            if (!rootFolder.HasValue) throw new Exception("Pasta raiz não encontrada.");
            
            var rootFolderId = rootFolder.Value.GetProperty("id").GetString();

            var attributeDefinitions = await _autodeskApi.GetFolderAttributeDefinitionsAsync(projectId, rootFolderId, accessToken);

            var accAttributes = attributeDefinitions.ToDictionary(
                def => def.Name.Trim().ToUpper(),
                def => def.Id.ToString()
            );

            _logger.LogInformation($"[DEBUG] Atributos Disponíveis no ACC: {string.Join(" | ", accAttributes.Keys)}");

            var translationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "N&A DOCUMENT NUMBER", "N&A DOCUMENT NUMBER" },
                { "CLIENT DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" },
                { "DOCUMENT TITLE", "DOCUMENT TITLE" },
                { "TITLE", "TITLE" },
                { "REV.", "REV." },
                { "REVISÃO", "REV." },
                { "EMISSION DATA", "EMISSION DATA" },
                { "DATA DE EMISSÃO", "DATA DE EMISSÃO" },
                { "TRANMISTAL NUMBER", "TRANSMITTAL NUMBER" },
                { "TRANSMITTAL NUMBER", "TRANSMITTAL NUMBER" },
                { "ISSUED POUPURSE", "ISSUED PURPOSE" },
                { "ISSUED PURPOSE", "ISSUED PURPOSE" },
                { "PROPÓSITO DE EMISSÃO", "PROPÓSITO DE EMISSÃO" },
                { "NUMERO DA GRD", "NUMERO DA GRD" },
                { "TÍTULO DO DOCUMENTO", "TÍTULO DO DOCUMENTO" },
                { "DESCRIÇÃO", "DESCRIPTION" }
            };

            var fileMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);

            var result = new SyncResult
            {
                TotalLdpFiles = ldpData.Count,
                FilesUpdatedSuccess = new List<string>(),
                FilesNotFound = new List<string>(),
                AllLdpFiles = new List<string>()
            };

            foreach (var row in ldpData)
            {
                if (row.TryGetValue("_DOC_NUMBER_KEY_", out var docCode) || row.TryGetValue(keyColumnName, out docCode))
                {
                    if (string.IsNullOrEmpty(docCode)) continue;

                    result.AllLdpFiles.Add(docCode);
                    var codeToSearch = docCode.Trim();
                    List<string> targetUrns = new List<string>();

                    if (fileMap.TryGetValue(codeToSearch, out var exactMatches)) targetUrns.AddRange(exactMatches);
                    else
                    {
                        var partialMatches = fileMap.Keys.Where(k => k.StartsWith(codeToSearch, StringComparison.OrdinalIgnoreCase));
                        foreach (var key in partialMatches) targetUrns.AddRange(fileMap[key]);
                    }

                    if (targetUrns.Any())
                    {
                        var customAttributesForFile = new List<object>();
                        var debugMatched = new List<string>();
                        var debugFailed = new List<string>();

                        foreach (var excelColumn in row)
                        {
                            if (excelColumn.Key == "_DOC_NUMBER_KEY_" || excelColumn.Key == keyColumnName) continue;
                            if (string.IsNullOrWhiteSpace(excelColumn.Value)) continue;

                            string accAttributeId = null;
                            string excelHeaderUpper = excelColumn.Key.Trim().ToUpper();

                            if (accAttributes.ContainsKey(excelHeaderUpper))
                            {
                                accAttributeId = accAttributes[excelHeaderUpper];
                            }
                            else if (translationMap.TryGetValue(excelHeaderUpper, out var mappedName))
                            {
                                var mappedUpper = mappedName.Trim().ToUpper();
                                if (accAttributes.ContainsKey(mappedUpper)) accAttributeId = accAttributes[mappedUpper];
                                else if (mappedUpper.Contains("TITLE") && accAttributes.ContainsKey("TITLE")) accAttributeId = accAttributes["TITLE"];
                                else if (mappedUpper.Contains("REV") && accAttributes.ContainsKey("REVISION")) accAttributeId = accAttributes["REVISION"];
                                else if (mappedUpper.Contains("REV") && accAttributes.ContainsKey("REVISÃO")) accAttributeId = accAttributes["REVISÃO"];
                                else if (mappedUpper.Contains("TRAN") && accAttributes.ContainsKey("TRANSMITTAL NUMBER")) accAttributeId = accAttributes["TRANSMITTAL NUMBER"];
                            }

                            if (accAttributeId != null)
                            {
                                object finalValue = excelColumn.Value;
                                if (excelHeaderUpper.Contains("DATA") || excelHeaderUpper.Contains("DATE"))
                                {
                                    if (DateTime.TryParse(excelColumn.Value, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out var dt) ||
                                        DateTime.TryParse(excelColumn.Value, out dt))
                                    {
                                        finalValue = dt.ToString("yyyy-MM-dd");
                                    }
                                }
                                customAttributesForFile.Add(new { id = int.Parse(accAttributeId), value = finalValue });
                                debugMatched.Add(excelColumn.Key);
                            }
                            else
                            {
                                debugFailed.Add(excelColumn.Key);
                            }
                        }

                        if (customAttributesForFile.Any())
                        {
                            foreach (var versionUrn in targetUrns.Distinct())
                            {
                                try
                                {
                                    await _autodeskApi.UpdateCustomAttributesForVersionAsync(projectId, versionUrn, customAttributesForFile, accessToken);

                                    result.FilesUpdated++;
                                    if (!result.FilesUpdatedSuccess.Contains(docCode)) result.FilesUpdatedSuccess.Add(docCode);

                                    _logger.LogInformation($"   -> '{docCode}' Atualizado. Campos: {string.Join(", ", debugMatched)}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"   -> Erro ao atualizar ACC '{docCode}': {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        result.FilesNotFound.Add(docCode);
                    }
                }
            }

            result.SummaryMessage = $"Sincronização concluída. {result.FilesUpdated} atualizações enviadas. {result.FilesNotFound.Count} não encontrados.";
            return result;
        }

        [HttpPost]
        public async Task<IActionResult> CheckAuditFilesAccVsLdp(string hubId, string projectId)
        {
            return Ok(new 
            { 
                count = 0, 
                differences = new List<string>(), 
                message = "Funcionalidade de Auditoria em manutenção (Retorno temporário)."
            });
        }

        public class WebhookInstallRequest { public string NgrokUrl { get; set; } }

        [HttpPost]
        public async Task<IActionResult> InstallWebhookForLdp(string hubId, string projectId, [FromBody] WebhookInstallRequest data)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
                return Unauthorized(new { message = "Sessão expirada." });
            try
            {
                const string ldpFolderName = "Arquivos de projeto.2";
                var rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, projectId, accessToken);
                if (rootFolder == null) throw new Exception("A pasta raiz 'Project Files' não foi encontrada.");

                var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
                var rootContents = await _autodeskApi.GetFolderContentsWithAttributesAsync(projectId, rootFolderId, accessToken);
                string? ldpFolderUrn = null;

                if (rootContents.HasValue && rootContents.Value.TryGetProperty("data", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.GetProperty("type").GetString() == "folders" &&
                            item.GetProperty("attributes").GetProperty("displayName").GetString().Equals(ldpFolderName, StringComparison.OrdinalIgnoreCase))
                        {
                            ldpFolderUrn = item.GetProperty("id").GetString();
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(ldpFolderUrn))
                    throw new Exception($"A pasta específica '{ldpFolderName}' não foi encontrada para a configuração do Webhook.");

                var appToken = await _tokenManager.GetTokenAsync();
                string callbackUrl = $"{data.NgrokUrl}/api/webhooks/callback";

                await _autodeskApi.CreateWebhookAsync(ldpFolderUrn, callbackUrl, appToken);

                return Ok(new { message = $"Webhook configurado com sucesso para a pasta '{ldpFolderName}'. As atualizações da LDP agora serão automáticas." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ocorreu um erro ao instalar o Webhook.", details = ex.ToString() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TesteCriacaoPasta()
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized("Sessão inválida.");

            try
            {
                string hubId = "b.72a17677-f6d2-42b8-8a37-e1e7c8a93ea3"; 
                string projectId = "de784007-78d0-4865-bacb-38f0ca690145"; 
                string pastaPaiId = "urn:adsk.wipprod:fs.folder:co.xN9eS5N1T--2fcfja9oVFg"; 

                string novaPastaId = await _autodeskApi.CreateFolderAsync(projectId, pastaPaiId, "PASTA-DE-TESTE-ISOLADO", accessToken);

                if (!string.IsNullOrEmpty(novaPastaId))
                {
                    return Ok(new { message = "TESTE BEM-SUCEDIDO! A pasta foi criada!", folderId = novaPastaId });
                }
                else
                {
                    return StatusCode(500, new { message = "TESTE FALHOU, mas sem exceção. A pasta pode já existir." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "TESTE FALHOU COM ERRO.", details = ex.ToString() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlaceholdersFromProjectLdp(
            [FromQuery] string hubId,
            [FromQuery] string projectId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });

            try
            {
                _logger.LogInformation("🎯 [PLACEHOLDERS] Motor Autónomo ativado para {ProjectId}", projectId);

                var result = await _placeholderService.CreatePlaceholdersFromProjectLdpAsync(hubId, projectId, NormaNomenclatura.Cliente,accessToken);

                if (result.Errors.Any() && result.PlaceholdersCreated == 0)
                {
                    return StatusCode(500, new { 
                        message = string.Join(" | ", result.Errors.Distinct()), 
                        details = result 
                    });
                }
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no Controller de Placeholders.");
                return StatusCode(500, new { message = "Erro crítico no servidor.", details = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDadosComerciais()
        {
            try
            {
                var dados = await _context.TbComercials.Take(100).ToListAsync();
                return Ok(dados);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ocorreu um erro ao buscar os dados: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateLdpFromDatabase([FromQuery] string hubId, [FromQuery] string projectId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
                return Unauthorized(new { message = "Sessão expirada." });

            try
            {
                // 1. TENTA BUSCAR O ID NO BANCO (CACHE DO ROBÔ)
                string ldpFileId = _dbService.GetLdpFileIdFromCache(projectId);

                // 2. SE NÃO TIVER NO BANCO, TENTA DESCOBRIR NA HORA (MANUAL)
                if (string.IsNullOrEmpty(ldpFileId))
                {
                    _logger.LogWarning($"[Manual Sync] ID da LDP não encontrado no banco para {projectId}. Tentando busca via API...");

                    // Garante formatação com 'b.' para API da Autodesk
                    string formattedProjectId = projectId.StartsWith("b.") ? projectId : $"b.{projectId}";

                    // Tenta achar a pasta de Gestão (Mesma lógica do Robô)
                    var folderLdpId = await _scheduleService.FindFolderByPaths(hubId, formattedProjectId, new[] {
                        new[] { "09 - DESENVOLVIMENTO", "09.14 - GESTÃO" },
                        new[] { "09 - MODEL DEVELOPING", "09.14 - MANAGEMENT" }
                    }, accessToken);

                    if (!string.IsNullOrEmpty(folderLdpId))
                    {
                        // 🎯 ADICIONADO AQUI: Busca a sigla configurada ou usa o padrão "-LD-"
                        string padraoNomeLdp = _configuration[$"ProjectSettings:{projectId}:LdpFileName"] 
                                            ?? _configuration["ProjectSettings:Default:LdpFileName"] 
                                            ?? "-LD-";

                        // 🎯 ALTERADO AQUI: Pega o primeiro Excel da pasta passando os 4 parâmetros
                        var fileInfo = await _autodeskApi.GetFirstExcelInFolderAsync(formattedProjectId, folderLdpId, padraoNomeLdp, accessToken);
                        
                        if (!string.IsNullOrEmpty(fileInfo.FileId))
                        {
                            ldpFileId = fileInfo.FileId;
                            // Opcional: Salvar no banco agora para agilizar a próxima vez
                            // ✅ Correção para o DataController.cs usando suas variáveis reais
                            _dbService.RegistrarDnaProjeto(
                                projectId,            // 1
                                ldpFileId,            // 2
                                hubId,                // 3
                                fileInfo.FileName,    // 4
                                "EN",                 // 5
                                "NA",                 // 6
                                null,                 // 7 🎯 Novo: normaId (O banco buscará o valor real)
                                null,                 // 8 🎯 Novo: pacoteOcrId (O banco buscará o valor real)
                                "Registro via Interface DataController" // 9 🎯 Novo: mensagem
                            ); // Valores default ou descobrir
                        }
                    }
                }

                // 3. VALIDAÇÃO FINAL
                if (string.IsNullOrEmpty(ldpFileId))
                {
                    return BadRequest(new { message = "❌ Erro: LDP não encontrada. O Robô ainda não mapeou este projeto e a busca manual falhou na pasta 09.10." });
                }

                // 4. CHAMADA CORRIGIDA (AGORA PASSANDO O ldpFileId)
                var result = await _ldpUpdateService.UpdateLdpFromDatabaseAsync(hubId, projectId, accessToken, ldpFileId);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha crítica ao atualizar a LDP a partir do banco de dados.");
                return StatusCode(500, new { message = "Ocorreu um erro no servidor ao atualizar a LDP.", details = ex.Message });
            }
        }
    }
}