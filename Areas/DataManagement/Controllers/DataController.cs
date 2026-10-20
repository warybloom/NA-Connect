using APS.WebApp.Services.Permissions;
// Ficheiro: Areas/DataManagement/Controllers/DataController.cs

using APS.WebApp.Models;
using APS.WebApp.Services;
using APS.WebApp.Services.AutodeskAuth;
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
        private readonly IAutodeskUserTokenProvider _autodeskUserTokens;
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
            AccScheduleSyncService scheduleService,
            IAutodeskUserTokenProvider autodeskUserTokens)
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
            _autodeskUserTokens = autodeskUserTokens;
        }

        // Ficheiro: Areas/DataManagement/Controllers/DataController.cs
        // Dentro da classe DataController

        // Ficheiro: Areas/DataManagement/Controllers/DataController.cs
        // Dentro da classe DataController

        [RequirePermission("projects.create")]
        [HttpPost]
    public async Task<IActionResult> CreateProjectFromTemplate([FromForm] string hubId, [FromForm] string templateId, IFormFile memberSheet)
    {
        // Log para verificar o Hub ID recebido
        _logger.LogInformation(">>> CreateProjectFromTemplate recebido hubId do form: {HubId}", hubId);

        var accessToken = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão expirada." });
        if (memberSheet == null || memberSheet.Length == 0) return BadRequest(new { message = "Planilha de membros não enviada." });

        // --- DEFINA OS IDs COMPLETOS DOS HUBS AQUI (Importante!) ---
        const string naHubId = "b.2ee333e9-3e57-42dd-ad89-86864dd86845"; // ID N&A com 'b.'
        const string bimHubId = "b.72a17677-f6d2-42b8-8a37-e1e7c8a93ea3"; // ID BIM Env com 'b.'
        // ----------------------------------------------------------

        try
        {
            // Obter accountId (sem 'b.') para buscar usuários
            var accountId = hubId.StartsWith("b.") ? hubId.Substring(2) : hubId;
            var allAccountUsers = await _accountUsersService.GetAccountUsersAsync(accountId);
            var userEmailToIdMap = allAccountUsers.ToDictionary(u => u.Email, u => u.Id, StringComparer.OrdinalIgnoreCase);

            // --- Selecionar a seção de RoleMappings com base no HubId ---
            string roleMappingSectionKey;
            if (hubId.Equals(naHubId, StringComparison.OrdinalIgnoreCase))
            {
                roleMappingSectionKey = "RoleMappings_NA"; // Usa a seção da N&A
            }
            else if (hubId.Equals(bimHubId, StringComparison.OrdinalIgnoreCase))
            {
                roleMappingSectionKey = "RoleMappings_BIM"; // Usa a seção do BIM Env
            }
            else
            {
                _logger.LogError("HubId {HubId} recebido não corresponde a nenhuma conta configurada (N&A ou BIM).", hubId);
                return BadRequest(new { message = $"A conta selecionada ({hubId}) não está configurada para esta operação." });
            }

            _logger.LogInformation("Usando a seção de configuração de roles: {RoleMappingSectionKey}", roleMappingSectionKey);

            // Carrega o dicionário de roles correto da configuração
            var roleNameToIdMap = _configuration.GetSection(roleMappingSectionKey).Get<Dictionary<string, string>>();

            if (roleNameToIdMap == null || !roleNameToIdMap.Any())
            {
                throw new Exception($"A seção de mapeamento de roles '{roleMappingSectionKey}' não foi encontrada ou está vazia no appsettings.json."); //
            }
            // --- FIM DA SELEÇÃO ---

            // Declarações
            string projectName = string.Empty;
            string jobNumber = string.Empty;
            var usersToImport = new List<AccUser>();
            var userRolesToAssign = new Dictionary<string, List<string>>();
            var disciplinesToCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var servicesByRoleMap = _configuration.GetSection("ServiceActivationByRole").Get<Dictionary<string, RoleServiceConfig>>() ?? new Dictionary<string, RoleServiceConfig>(); //
            var disciplineFolderMap = _configuration.GetSection("DisciplineFolderMapping").Get<Dictionary<string, string>>() ?? new Dictionary<string, string>(); //

            // Leitura da Planilha
            using (var stream = new MemoryStream())
            {
                await memberSheet.CopyToAsync(stream);
                // Define o contexto da licença do EPPlus (faça isso uma vez, talvez no Program.cs)
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Ou outra licença válida
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

                    // Mapeamento de disciplinas
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


                    // Processamento de usuários e roles
                    for (int row = 20; row <= worksheet.Dimension.Rows; row += 2)
                    {
                        var roleName = worksheet.Cells[row, 9].Value?.ToString()?.Trim();
                        var email = worksheet.Cells[row, 19].Value?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(roleName)) continue;

                        if (userEmailToIdMap.ContainsKey(email) &&
                            roleNameToIdMap.TryGetValue(roleName, out var roleId)) // Usa o dicionário correto
                        {
                            // Adicionar usuário para importação com produtos corretos
                            if (servicesByRoleMap.TryGetValue(roleName, out var serviceConfig)) // Verifica se a role tem config
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

                            // Adicionar role para atribuição posterior
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
                } // Fim using ExcelPackage
            } // Fim using MemoryStream

            // Adicionar usuário atual/admin
            var currentUserEmail = "christian.santana@naconsult.com.br"; // Ou buscar dinamicamente
            const string adminRoleName = "13. ADMINISTRAÇÃO 3D";

            if (!string.IsNullOrEmpty(currentUserEmail) && userEmailToIdMap.ContainsKey(currentUserEmail))
            {
                // Adiciona aos usersToImport se não estiver
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

                // Adiciona a role de admin para atribuição usando o roleNameToIdMap correto
                if (roleNameToIdMap.TryGetValue(adminRoleName, out var adminRoleId))
                {
                    if (!userRolesToAssign.ContainsKey(currentUserEmail)) userRolesToAssign[currentUserEmail] = new List<string>();
                    if (!userRolesToAssign[currentUserEmail].Contains(adminRoleId)) userRolesToAssign[currentUserEmail].Add(adminRoleId);
                }
                 else { _logger.LogWarning("Role admin '{AdminRoleName}' não encontrada na seção {Section}.", adminRoleName, roleMappingSectionKey); }
            }

            // Criação do Projeto
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

            // Importação e Atribuição de Roles
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
                    for (int i = 0; i < 12; i++) // Tenta buscar o usuário por até 1 minuto
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

            // Criação de Pastas
            if (disciplinesToCreate.Any())
            {
                _logger.LogInformation("Iniciando criação de pastas de disciplina...");
                JsonElement? rootFolder = null; int attempts = 0; const int maxAttempts = 30;
                while (attempts < maxAttempts) // Busca Pasta Raiz com Retry
                {
                    attempts++;
                    try {
                        _logger.LogDebug("[CreateFolders] Tentativa {Attempt}/{Max} buscar pasta raiz...", attempts, maxAttempts);
                        rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, newProjectId, accessToken); // Usa hubId com 'b.'
                        if (rootFolder != null) { _logger.LogDebug("[CreateFolders] Pasta raiz encontrada."); break; }
                         _logger.LogWarning("[CreateFolders] GetProjectRootFolderAsync retornou null na tentativa {Attempt}.", attempts);
                    } catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound || httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden || (httpEx.Message != null && httpEx.Message.Contains("Only active projects may be accessed"))) {
                        _logger.LogWarning("[CreateFolders] Pasta raiz não pronta (Tentativa {A}/{M}): {S}. Aguardando 5s...", attempts, maxAttempts, httpEx.StatusCode);
                        if (attempts >= maxAttempts) throw new Exception($"Pastas não ficaram disponíveis após {maxAttempts} tentativas. Erro final: {httpEx.Message}");
                    } catch(Exception ex) {
                         _logger.LogError(ex, "[CreateFolders] Erro buscar pasta raiz (Tentativa {A}). Aguardando 5s...", attempts);
                         if (attempts >= maxAttempts) throw; // Lança se for erro inesperado e já tentou muito
                    }
                    await Task.Delay(5000); // Espera 5s entre tentativas
                }
                if (rootFolder == null) throw new Exception($"Pasta raiz 'Project Files' não encontrada para {newProjectId} após {maxAttempts} tentativas.");

                var rootFolderId = rootFolder.Value.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(rootFolderId)) throw new Exception($"ID da Pasta Raiz inválido para {newProjectId}.");

                 _logger.LogInformation("Pasta raiz encontrada (ID: {RootFolderId}). Criando '04 - ENGENHARIA'...", rootFolderId);

                // --- Loop de Retry para Criar/Obter Pasta '04 - ENGENHARIA' ---
                string? engineeringFolderId = null;
                int folderCreationAttempts = 0;
                const int maxFolderCreationAttempts = 12; // Tentar por até 1 minuto
                const int folderCreationDelayMs = 5000;    // Esperar 5 segundos
                bool attemptFindExisting = false;

                while (engineeringFolderId == null && folderCreationAttempts < maxFolderCreationAttempts)
                {
                    folderCreationAttempts++;
                    string? currentAttemptResult = null;

                    try
                    {
                        _logger.LogInformation("[CreateFolders][Attempt {Attempt}/{MaxAttempts}] Trying API: CreateFolderAsync for '04 - ENGENHARIA'...", folderCreationAttempts, maxFolderCreationAttempts);
                        currentAttemptResult = await _autodeskApi.CreateFolderAsync(newProjectId, rootFolderId, "04 - ENGENHARIA", accessToken); //

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
                              engineeringFolderId = await _autodeskApi.FindFolderByNameRecursiveAsync(newProjectId, rootFolderId, "04 - ENGENHARIA", accessToken); //

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
                } // Fim do While Retry

                if (string.IsNullOrEmpty(engineeringFolderId))
                {
                     _logger.LogError("[CreateFolders] CRITICAL FAILURE: engineeringFolderId is still null/empty after retry loop for project {ProjectId}.", newProjectId);
                     throw new Exception($"Falha crítica inesperada ao obter ID da pasta '04 - ENGENHARIA' para {newProjectId}.");
                } else { _logger.LogInformation("[CreateFolders] FINAL CHECK SUCCESS: Proceeding with engineeringFolderId: {FolderId}", engineeringFolderId); }
                // --- FIM Retry Loop ---


                // Criar Subpastas de Disciplina
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
            } // Fim if (disciplinesToCreate.Any())

            _logger.LogInformation("Processo concluído com sucesso para projeto '{ProjectName}' (Hub: {HubId}).", projectName, hubId);
            return Ok(new { message = $"Projeto '{projectName}' criado com sucesso usando config '{roleMappingSectionKey}'!", projectId = newProjectId });

        } // Fim Try Principal
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro GERAL durante o processo de criação do projeto a partir do template (Hub: {HubId}).", hubId);
            // Retorna apenas a mensagem principal para o frontend para evitar expor detalhes internos
            return StatusCode(500, new { message = "Ocorreu um erro no servidor durante a criação do projeto.", details = ex.Message });
        }
    } // Fim da Action

        // Em DataController.cs

        [RequirePermission("projects.create")]
        [HttpGet]
        // Adiciona [FromQuery] string hubId
        public async Task<IActionResult> GetProjectTemplates([FromQuery] string hubId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized(new { message = "Sessão não encontrada." });

            // Adiciona verificação para o hubId recebido
            if (string.IsNullOrEmpty(hubId))
            {
                return BadRequest(new { message = "Hub ID é obrigatório para buscar templates." });
            }

            try
            {
                // --- MUDANÇA: Extrai accountId do hubId recebido ---
                // var rawAccountId = _configuration["APS:AccountId"]; // REMOVIDO
                var accountId = hubId.StartsWith("b.") ? hubId.Substring(2) : hubId; // Usa o hubId do parâmetro
                                                                                     // --------------------------------------------------

                _logger.LogInformation("Buscando templates para a conta via GetProjectTemplates: {AccountId}", accountId); // Log atualizado

                // Usa o accountId extraído na chamada da API
                var templates = await _autodeskApi.GetAccProjectTemplatesAsync(accountId, accessToken);
                return Ok(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar templates de projeto para a conta {AccountId}", hubId); // Log com hubId original
                return StatusCode(500, new { message = "Erro ao carregar templates de projeto.", details = ex.Message });
            }
        }


        [RequirePermission("projects.create")]
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

        [PermissionExempt("Lista os hubs do PRÓPRIO usuário na Autodesk (o ACC já restringe); base de todos os seletores de projeto.")]
        [HttpGet]
        public async Task<IActionResult> Hubs()
        {
            try
            {
                var hubsData = await AutodeskUserRequestExecutor.ExecuteAsync(_autodeskUserTokens,
                    (token, ct) => _autodeskApi.GetHubsAsync(token), HttpContext.RequestAborted);
                return Ok(hubsData);
            }
            catch (AutodeskUserReconnectRequiredException)
            {
                return Conflict(new { autodeskReconnectRequired = true, code = "autodeskReconnectRequired", message = "Reconecte a Autodesk para continuar." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao carregar Hubs: {ex.Message}", details = ex.ToString() });
            }
        }

        [PermissionExempt("Lista os projetos ACC do PRÓPRIO usuário (o ACC já restringe); base de todos os seletores de projeto.")]
        [HttpGet]
        public async Task<IActionResult> Projetos(string hubId)
        {
            try
            {
                var projectsData = await AutodeskUserRequestExecutor.ExecuteAsync(_autodeskUserTokens,
                    (token, ct) => _autodeskApi.GetHubProjectsAsync(hubId, token), HttpContext.RequestAborted);
                return Ok(projectsData);
            }
            catch (AutodeskUserReconnectRequiredException)
            {
                return Conflict(new { autodeskReconnectRequired = true, code = "autodeskReconnectRequired", message = "Reconecte a Autodesk para continuar." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao carregar projetos: {ex.Message}", details = ex.ToString() });
            }
        }

        [RequirePermission("cde.docs.view")]
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

        [RequirePermission("cde.docs.view")]
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> GetFolderPath(string projectId, string folderId)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                // Retornar Unauthorized se não houver token
                return Unauthorized(new { message = "Sessão expirada." });
            }

            var path = new List<BreadcrumbItem>();
            var currentFolderId = folderId;

            // Loop para subir na hierarquia de pastas
            for (int i = 0; i < 20 && !string.IsNullOrEmpty(currentFolderId); i++) // Limite de 20 níveis para segurança
            {
                try // <-- TRY...CATCH AGORA ESTÁ DENTRO DO LOOP
                {
                    // Tenta buscar os detalhes da pasta atual
                    var folderDetailsElement = await _autodeskApi.GetFolderDetailsAsync(projectId, currentFolderId, accessToken);

                    // Verifica se a API retornou detalhes (pode retornar null se não encontrado)
                    if (folderDetailsElement == null || !folderDetailsElement.HasValue)
                    {
                         Console.WriteLine($"Detalhes não encontrados para a pasta {currentFolderId}. Interrompendo busca.");
                         break; // Interrompe se a pasta não for encontrada
                    }
                    var folderDetails = folderDetailsElement.Value;

                    // Extrai nome e adiciona ao caminho
                    var attributes = folderDetails.GetProperty("data").GetProperty("attributes");
                    var name = attributes.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : // Tenta displayName primeiro (BIM360/ACC)
                               attributes.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : // Fallback para name (pastas raiz?)
                               "Nome Desconhecido";
                    
                    path.Add(new BreadcrumbItem { Id = currentFolderId, Name = name });

                    // Verifica se chegou na pasta raiz do projeto (sem pai) ou raiz do hub
                     if (!folderDetails.GetProperty("data").GetProperty("relationships").TryGetProperty("parent", out var parentRel) ||
                        !parentRel.TryGetProperty("data", out var parentData) ||
                        parentData.ValueKind == JsonValueKind.Null)
                    {
                        break; // Chegou à raiz, para o loop
                    }

                    // Pega o ID da pasta pai para a próxima iteração
                    currentFolderId = parentData.GetProperty("id").GetString();
                }
                catch (System.Net.Http.HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Se for erro 403 Forbidden (sem permissão), regista e interrompe a subida
                    Console.WriteLine($"Permissão negada (403) ao acessar a pasta {currentFolderId} durante a busca do caminho. Retornando caminho parcial.");
                    break; // Interrompe o loop
                }
                catch (Exception ex)
                {
                    // Qualquer outro erro durante a busca de UMA pasta específica, regista e interrompe
                    Console.WriteLine($"Erro inesperado ao buscar detalhes da pasta {currentFolderId}: {ex.Message}. Retornando caminho parcial.");
                    // Considerar logar o ex.ToString() para mais detalhes em produção
                    break; // Interrompe o loop
                }
            } // Fim do loop for

            // Inverte a lista para ter a ordem correta (Raiz -> ... -> Pasta Atual)
            path.Reverse();

            // Retorna o caminho encontrado (pode ser parcial se houve erro) com status 200 OK
            return Ok(path);
        }

        [RequirePermission("cde.docs.view")]
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

        [RequirePermission("cde.docs.upload")]
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

        [RequirePermission("cde.docs.download", AnyProject = true)]
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

        [RequirePermission("cde.docs.view")]
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

        // EM: DataController.cs
        // SUBSTITUA A AÇÃO INTEIRA PELA VERSÃO ABAIXO

        [RequirePermission("ldp.sync.execute")]
        [HttpPost]
public async Task<IActionResult> SyncFromProjectLdp(string hubId, string projectId)
{
    var accessToken = HttpContext.Session.GetString("AccessToken");
    if (string.IsNullOrEmpty(accessToken))
        return Unauthorized(new { message = "Sessão expirada." });

    try
    {
        // 1. Obtém a URN e baixa o arquivo
        string ldpVersionUrn = await _autodeskApi.FindLdpFileUrnAsync(hubId, projectId, accessToken);
        var ldpData = new List<Dictionary<string, string>>();

        using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken))
        {
            using (var package = new ExcelPackage(stream))
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets["LDP"];
                if (worksheet == null) return BadRequest(new { message = "A planilha LDP não contém uma aba chamada 'LDP'." });
                if (worksheet.Dimension == null) return BadRequest(new { message = "A aba 'LDP' na planilha está vazia." });

                const int groupHeaderRow = 6;
                const int headerRow = 7;
                const string keyColumnName = "NÚMERO DOCUMENTO CLIENTE"; // Verifique se este ainda é o nome correto
                
                if (worksheet.Dimension.Rows < headerRow) return BadRequest(new { message = $"Aba 'LDP' não possui as linhas de cabeçalho esperadas (linha {groupHeaderRow} e {headerRow})." });

                // =========================================================================================
                // ÁREA DE CONFIGURAÇÃO MANUAL (Edite aqui para alterar nomes de colunas)
                // =========================================================================================
                // A chave é: "NOME_DO_GRUPO|NOME_DA_COLUNA" (Use | como separador)
                // O valor é: "NOME FINAL QUE VOCÊ QUER NO SISTEMA"
                var headerOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Exceções para Propósito de Emissão (Substituindo a lógica do switch)
                    { "1º EMISSÃO|PROPÓSITO DE EMISSÃO", "1º PROPÓSITO DE EMISSÃO" },
                    { "2º EMISSÃO|PROPÓSITO DE EMISSÃO", "2º PROPÓSITO DE EMISSÃO" },
                    { "3º EMISSÃO|PROPÓSITO DE EMISSÃO", "3º PROPÓSITO DE EMISSÃO" },
                    // Exemplo de facilidade para adicionar futuros
                    
                    // Você pode forçar o nome de qualquer outra coluna aqui se quiser:
                    // { "DADOS GERAIS|DISCIPLINA", "DISCIPLINA DO PROJETO" } 
                };
                // =========================================================================================

                // 2. Lê os cabeçalhos brutos
                var rawHeaders = new List<string>();
                string currentGroup = "";

                for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                {
                    var groupValue = worksheet.Cells[groupHeaderRow, col].Value?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(groupValue)) currentGroup = groupValue;

                    var headerValue = worksheet.Cells[headerRow, col].Value?.ToString()?.Trim();
                    
                    // Se a coluna não tiver cabeçalho na linha 7, ignora
                    if (string.IsNullOrEmpty(headerValue))
                    {
                        rawHeaders.Add(string.Empty); 
                        continue;
                    }

                    string combinedHeader;
                    string mapKey = $"{currentGroup}|{headerValue}"; // Cria a chave para verificar no seu dicionário manual

                    // 1. Verifica se existe uma regra manual para essa combinação
                    if (headerOverrides.ContainsKey(mapKey))
                    {
                        combinedHeader = headerOverrides[mapKey];
                    }
                    // 2. Se não houver regra manual, aplica a lógica padrão de grupos de emissão
                    else if (!string.IsNullOrEmpty(currentGroup) && 
                            (currentGroup.Contains("EMISSÃO", StringComparison.OrdinalIgnoreCase))) // Verifica se é grupo de emissão genérico
                    {
                        combinedHeader = $"{currentGroup} - {headerValue}"; // Padrão: "1º EMISSÃO - STATUS"
                    }
                    // 3. Caso contrário, usa apenas o nome da coluna
                    else
                    {
                        combinedHeader = headerValue;
                    }

                    // Limpa espaços extras e adiciona
                    rawHeaders.Add(Regex.Replace(combinedHeader, @"\s+", " ").Trim());
                }

                // 3. Garante unicidade adicionando contador (Lógica mantida)
                var headers = new List<string>();
                var headerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var header in rawHeaders)
                {
                    if (string.IsNullOrEmpty(header)) { headers.Add(string.Empty); continue; }

                    // Verifica se já existe para adicionar contador, exceto se for um nome que já sabemos ser único por natureza
                    // Mas a lógica de contagem é segura para manter em tudo
                    if (headerCounts.ContainsKey(header))
                    {
                        headerCounts[header]++;
                        headers.Add($"{header} ({headerCounts[header]})");
                    }
                    else
                    {
                        headers.Add(header);
                        headerCounts.Add(header, 1);
                    }
                }

                // 4. Validação da coluna chave e leitura dos dados (Lógica mantida)
                var actualKeyColumnName = headers.FirstOrDefault(h => h.StartsWith(keyColumnName, StringComparison.OrdinalIgnoreCase));
                
                if (actualKeyColumnName == null) 
                    return BadRequest(new { message = $"Não foi possível encontrar a coluna de cabeçalho obrigatória '{keyColumnName}'." });
                
                var keyColumnIndex = headers.FindIndex(h => h.Equals(actualKeyColumnName, StringComparison.OrdinalIgnoreCase));
                // O FindIndex é base 0, o Excel é base 1, mas como headers[] está alinhado com as colunas (exceto o offset do loop),
                // precisamos garantir o indice correto do Excel.
                // headers[0] corresponde a col=1. Entao index 0 + 1 = col 1.
                keyColumnIndex += 1; 

                for (int row = headerRow + 1; row <= worksheet.Dimension.Rows; row++)
                {
                    var keyValue = worksheet.Cells[row, keyColumnIndex].Value?.ToString();
                    if (string.IsNullOrWhiteSpace(keyValue)) continue; // Pula linhas sem a chave primária

                    var rowData = new Dictionary<string, string>();
                    
                    for (int col = 1; col <= headers.Count; col++)
                    {
                        // Só processa se tiver um cabeçalho válido nessa posição
                        if (!string.IsNullOrEmpty(headers[col - 1]))
                        {
                            rowData[headers[col - 1]] = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? string.Empty;
                        }
                    }
                    ldpData.Add(rowData);
                }

            } // Fecha using ExcelPackage
        } // Fecha using MemoryStream

        // 5. Envia para a API da Autodesk/Banco de Dados
                var result = await _autodeskApi.SyncLdpAttributesAsync(hubId, projectId, ldpData, accessToken);

                // --- CORREÇÃO AQUI ---
                // Se result for null, criamos um objeto manual para não quebrar o Front-end
                if (result == null)
                {
                    return Ok(new 
                    { 
                        // O front-end espera 'summaryMessage', então fornecemos um padrão
                        summaryMessage = "Sincronização processada, mas a API interna não retornou detalhes.",
                        totalLdpFiles = ldpData.Count,
                        success = true
                    });
                }

                return Ok(result);
            } // Fecha o try
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocorreu um erro durante a sincronização da LDP.");
                // O retorno de erro também deve ser um JSON válido
                return StatusCode(500, new 
                { 
                    message = "Ocorreu um erro na sincronização.", 
                    details = ex.Message 
                });
            }
        }

        public class WebhookInstallRequest { public string NgrokUrl { get; set; } }

        [RequirePermission("ldp.sync.execute")]
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

        [RequirePermission("system.diagnostics.execute")]
        [HttpPost]
        public async Task<IActionResult> TesteCriacaoPasta()
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken)) return Unauthorized("Sessão inválida.");

            try
            {
                // ============================================================================
                // VALORES PREENCHIDOS COM AS INFORMAÇÕES DA URL
                // ============================================================================
                string hubId = "b.72a17677-f6d2-42b8-8a37-e1e7c8a93ea3"; // Hub ID da sua conta
                string projectId = "de784007-78d0-4865-bacb-38f0ca690145"; // ID do Projeto extraído da URL
                string pastaPaiId = "urn:adsk.wipprod:fs.folder:co.xN9eS5N1T--2fcfja9oVFg"; // ID da Pasta Raiz extraído da URL
                // ============================================================================

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

        // ▼▼▼ ACTION MODIFICADA ▼▼▼
        [RequirePermission("ldp.sync.execute")]
        [HttpPost]
        // [Route("CreatePlaceholdersFromLdp")] // Removido ou comentado, usaremos o nome da action
        // Removemos [FromForm] IFormFile ldpFile
        // Alteramos para [FromQuery] para simplicidade na chamada do frontend
        public async Task<IActionResult> CreatePlaceholdersFromProjectLdp(
            [FromQuery] string hubId,
            [FromQuery] string projectId,
            [FromQuery] string norma)
        {
            var accessToken = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized(new { message = "Sessão expirada." });
            }
            // REMOVIDO: Verificação do ldpFile

            if (!Enum.TryParse<NormaNomenclatura>(norma, true, out var normaEnum))
            {
                return BadRequest(new { message = "O valor para 'norma' é inválido. Use 'NA' ou 'Cliente'." });
            }

            try
            {
                // REMOVIDO: using var ldpStream = ldpFile.OpenReadStream();

                // Chama o MÉTODO RENOMEADO/MODIFICADO no serviço, sem passar o stream
                var result = await _placeholderService.CreatePlaceholdersFromProjectLdpAsync( // <--- Chamada atualizada
                    hubId,
                    projectId,
                    normaEnum,
                    accessToken);

                // A lógica de retorno permanece a mesma
                if (result.Errors.Any() && result.PlaceholdersCreated == 0)
                {
                    // Se houve erros e nada foi criado, retorna 500 com os detalhes
                    return StatusCode(500, result);
                }
                // Caso contrário, retorna 200 OK com o resultado (mesmo que contenha erros parciais)
                return Ok(result);
            }
            catch (FileNotFoundException fnfEx) // Captura especificamente se a LDP não for encontrada
            {
                _logger.LogWarning(fnfEx, "Arquivo LDP não encontrado no projeto {ProjectId} durante a criação de placeholders.", projectId);
                // Retorna um erro 404 Not Found, que é mais específico
                return NotFound(new { message = fnfEx.Message, details = fnfEx.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro crítico ao criar placeholders a partir da LDP do projeto {ProjectId}.", projectId);
                return StatusCode(500, new { message = "Ocorreu um erro crítico no servidor durante a criação dos placeholders.", details = ex.ToString() });
            }
        }
    

        [RequirePermission("system.commercial.view")]
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

        [RequirePermission("ldp.sync.execute")]
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
                        new[] { "09 - DEVELOPING", "09.14 - MANAGEMENT" }
                    }, accessToken);

                    if (!string.IsNullOrEmpty(folderLdpId))
                    {
                        // Pega o primeiro Excel da pasta
                        var fileInfo = await _autodeskApi.GetFirstExcelInFolderAsync(formattedProjectId, folderLdpId, accessToken);
                        if (!string.IsNullOrEmpty(fileInfo.FileId))
                        {
                            ldpFileId = fileInfo.FileId;
                            // Opcional: Salvar no banco agora para agilizar a próxima vez
                            _dbService.RegistrarDnaProjeto(projectId, ldpFileId, hubId, fileInfo.FileName, "EN", "NA"); // Valores default ou descobrir
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
