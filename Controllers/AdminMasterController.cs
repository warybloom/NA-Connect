#nullable disable
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Text;
using APS.WebApp.Services;
using APS.WebApp.Models;

namespace APS.WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    
    public class AdminMasterController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILdpValidacaoService _ldpValidationService;
        private readonly AccUploadService _accUploadService;

        public AdminMasterController(IConfiguration configuration, ILdpValidacaoService ldpValidationService, AccUploadService accUploadService)
        {
            // Usando a mesma string de conexão do seu ForgeSync
            _connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";
            _ldpValidationService = ldpValidationService;
            _accUploadService = accUploadService;
        }

        // =========================================================================
        // 1. LISTAR TODOS OS TENANTS (COM OS MÓDULOS INCLUÍDOS)
        // =========================================================================
        [HttpGet("tenants")]
        public async Task<IActionResult> GetTenants()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Busca os dados principais das empresas
                    string sql = @"
                        SELECT 
                            TenantID as Id,
                            TenantName as TenantName,
                            ContractedPlan as ContractedPlan,
                            MaxProjects as MaxProjects,
                            UsedProjects as UsedProjects,
                            MaxSimultaneousRobots as MaxSimultaneousRobots,
                            ActiveRobots as ActiveRobots,
                            EndDate as EndDate,
                            Status as Status
                        FROM Tenants
                        ORDER BY TenantID DESC;
                    ";

                    var empresas = (await conn.QueryAsync<TenantResponseDto>(sql)).ToList();

                    // 2. Busca todos os módulos de todas as empresas de uma vez só (mais rápido)
                    string sqlModules = "SELECT TenantID, ModuleID FROM Tenant_Modules";
                    var modulos = await conn.QueryAsync(sqlModules);

                    // 3. Distribui os módulos para dentro da lista de cada empresa correspondente
                    foreach (var emp in empresas)
                    {
                        emp.ActiveModules = modulos
                            .Where(m => (int)m.TenantID == emp.Id)
                            .Select(m => (string)m.ModuleID)
                            .ToList();
                    }

                    return Ok(empresas);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao buscar empresas: {ex.Message}" });
            }
        }

 [HttpGet("users")]
public async Task<IActionResult> GetUsers()
{
    try
    {
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    u.UserID AS Id,
                    u.Nome AS Nome,
                    u.Email AS Email,
                    u.RoleSistema AS Papel,
                    u.Ativo AS Ativo,
                    CONVERT(VARCHAR, u.DataCadastro, 103) AS UltimoAcesso,
                    (
                        SELECT COUNT(*) 
                        FROM [dbo].[PermissoesUsuario] pu
                        WHERE pu.UserID = u.UserID
                    ) AS QtdPermissoes,
                    (SELECT COUNT(*) FROM [dbo].[Paginas] WHERE Ativo = 1) AS TotalTelas
                FROM [dbo].[Usuarios] u
                ORDER BY u.UserID DESC;";

            var users = await conn.QueryAsync<UserListItemDto>(sql);
            return Ok(users);
        }
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = $"Erro ao buscar usuários: {ex.Message}" });
    }
}

        // 2.6 CADASTRAR NOVO USUÁRIO - NÃO USADO NO MOMENTO 
        [HttpPost("users")]
       /*   public async Task<IActionResult> CreateUser([FromBody] CreateUserDto data)
        {
            if (string.IsNullOrEmpty(data.Email) || string.IsNullOrEmpty(data.Nome))
                return BadRequest(new { message = "Nome e E-mail são obrigatórios." });

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Verifica se o e-mail já existe
                    var existe = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM [dbo].[Usuarios] WHERE Email = @Email", 
                        new { data.Email }
                    );

                    if (existe > 0)
                        return BadRequest(new { message = "Já existe um usuário cadastrado com este e-mail." });

                    string sql = @"
                        INSERT INTO [dbo].[Usuarios] (Nome, Email, RoleSistema, Ativo, DataCadastro)
                        VALUES (@Nome, @Email, @Role, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() as int);";

                    int newUserId = await conn.QuerySingleAsync<int>(sql, new 
                    { 
                        data.Nome, 
                        data.Email, 
                        Role = string.IsNullOrEmpty(data.Role) ? "USER" : data.Role 
                    });

                    return Ok(new { sucesso = true, userId = newUserId, message = "Usuário criado com sucesso!" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao criar usuário: {ex.Message}" });
            }
     }/*  */

        // 5.2 BUSCAR PERMISSÕES DO USUÁRIO
        [HttpGet("users/{userId}/permissions")]
        public async Task<IActionResult> GetUserPermissions(int userId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
                                SELECT 
                                    p.PaginaID AS Id,
                                    p.Rota AS Code,
                                    p.NomePagina AS Name,
                                    p.Rota AS Hint,
                                    CAST(CASE WHEN pu.PaginaID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS CanAccess
                                FROM [dbo].[Paginas] p
                                LEFT JOIN [dbo].[PermissoesUsuario] pu ON pu.UserID = @UserId AND pu.PaginaID = p.PaginaID
                                WHERE p.Ativo = 1;";

                    var permissions = await conn.QueryAsync<UserPermissionItemDto>(sql, new { UserId = userId });
                    return Ok(permissions);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao buscar permissões: {ex.Message}" });
            }
        }

        // 5.3 SALVAR PERMISSÕES CUSTOMIZADAS DO USUÁRIO
        [HttpPut("users/{userId}/permissions")]
        public async Task<IActionResult> SaveUserPermissions(int userId, [FromBody] Dictionary<int, bool> permissions)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Limpa permissões customizadas antigas
                            await conn.ExecuteAsync(
                                "DELETE FROM [dbo].[PermissoesUsuario] WHERE UserID = @UserId", 
                                new { UserId = userId }, 
                                transaction
                            );

                            // 2. Insere apenas os registros onde o valor for TRUE
                            var activePages = permissions
                                .Where(p => p.Value)
                                .Select(p => new { UserId = userId, PaginaID = p.Key });

                            if (activePages.Any())
                            {
                                string sqlInsert = "INSERT INTO [dbo].[PermissoesUsuario] (UserID, PaginaID) VALUES (@UserId, @PaginaID)";
                                await conn.ExecuteAsync(sqlInsert, activePages, transaction);
                            }

                            transaction.Commit();
                            return Ok(new { sucesso = true, message = "Permissões salvas com sucesso!" });
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao salvar permissões: {ex.Message}" });
            }
        }

[HttpGet("me/permissions")]
public async Task<IActionResult> GetMinhasPermissoes()
{
    var userId = HttpContext.Session.GetInt32("UserID");
    if (userId == null) return Unauthorized();

    using (var conn = new SqlConnection(_connectionString))
    {
        await conn.OpenAsync();

        string sql = @"
            SELECT p.Rota
            FROM [dbo].[Paginas] p
            INNER JOIN [dbo].[PermissoesUsuario] pu
                ON pu.PaginaID = p.PaginaID AND pu.UserID = @UserId
            WHERE p.Ativo = 1;
        ";

        var rotas = await conn.QueryAsync<string>(sql, new { UserId = userId.Value });
        return Ok(rotas);
    }
}
// GET /api/AdminMaster/roles
// Lista os perfis disponíveis (distinct) direto da PermissoesPerfil,
// pra popular dropdown e nunca mais deixar RoleSistema ser texto livre
[HttpGet("roles")]
public async Task<IActionResult> GetRolesDisponiveis()
{
    const string sql = @"
        SELECT DISTINCT RoleSistema
        FROM PermissoesPerfil
        ORDER BY RoleSistema";

    using var conn = new SqlConnection(_connectionString);
    var roles = await conn.QueryAsync<string>(sql);
    return Ok(roles);
}

// POST /api/AdminMaster/users/{id}/apply-default-profile
// Copia as permissões padrão do perfil atual do usuário (Usuarios.RoleSistema)
// para PermissoesUsuario, sem duplicar o que já existir
[HttpPost("users/{id}/apply-default-profile")]
public async Task<IActionResult> AplicarPerfilPadrao(int id)
{
    using var conn = new SqlConnection(_connectionString);

    var role = await conn.QuerySingleOrDefaultAsync<string>(
        "SELECT RoleSistema FROM Usuarios WHERE UserID = @Id", new { Id = id });

    if (string.IsNullOrEmpty(role))
        return NotFound($"Usuário {id} não encontrado ou sem RoleSistema definido.");

    const string sql = @"
        INSERT INTO PermissoesUsuario (UserID, PaginaID)
        SELECT @UserId, pp.PaginaID
        FROM PermissoesPerfil pp
        WHERE pp.RoleSistema = @Role
          AND NOT EXISTS (
              SELECT 1 FROM PermissoesUsuario pu
              WHERE pu.UserID = @UserId AND pu.PaginaID = pp.PaginaID
          )";

    var linhasInseridas = await conn.ExecuteAsync(sql, new { UserId = id, Role = role });

    return Ok(new
    {
        userId = id,
        role,
        paginasAdicionadas = linhasInseridas
    });
}

        // 5.4 ALTERAR PAPEL (ROLE) DO USUÁRIO
[HttpPut("users/{userId}/role")]
public async Task<IActionResult> UpdateUserRole(int userId, [FromBody] UpdateRoleDto data)
{
    using (var conn = new SqlConnection(_connectionString))
    {
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            // 1. Atualiza a role do usuário
            string sqlRole = "UPDATE [dbo].[Usuarios] SET RoleSistema = @Role WHERE UserID = @UserId";
            int affectedRows = await conn.ExecuteAsync(sqlRole, new { Role = data.Role, UserId = userId }, transaction);

            if (affectedRows == 0)
            {
                transaction.Rollback();
                return NotFound("Usuário não encontrado.");
            }
 
            // 2. Remove as permissões atuais do usuário (reseta)
            string sqlDelete = "DELETE FROM [dbo].[PermissoesUsuario] WHERE UserID = @UserId";
            await conn.ExecuteAsync(sqlDelete, new { UserId = userId }, transaction);

            // 3. Aplica o template padrão da nova role
            string sqlInsert = @"
                INSERT INTO [dbo].[PermissoesUsuario] (UserID, PaginaID)
                SELECT @UserId, PaginaID
                FROM [dbo].[RolePermissoesPadrao]
                WHERE Role = @Role;
            ";
            await conn.ExecuteAsync(sqlInsert, new { UserId = userId, Role = data.Role }, transaction);

            transaction.Commit();
            return Ok(new { sucesso = true, message = "Papel e permissões atualizados com sucesso!" });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return StatusCode(500, new { message = $"Erro ao alterar papel do usuário: {ex.Message}" });
        }
    }
}

        // 5.5 ALTERNAR STATUS (ATIVO / INATIVO)
        [HttpPut("users/{userId}/status")]
        public async Task<IActionResult> ToggleUserStatus(int userId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
                        UPDATE [dbo].[Usuarios] 
                        SET Ativo = CASE WHEN Ativo = 1 THEN 0 ELSE 1 END
                        OUTPUT INSERTED.Ativo
                        WHERE UserID = @UserId;";

                    var novoStatus = await conn.QuerySingleOrDefaultAsync<bool?>(sql, new { UserId = userId });

                    if (novoStatus == null)
                        return NotFound("Usuário não encontrado.");

                    return Ok(new { ativo = novoStatus.Value });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao alternar status: {ex.Message}" });
            }
        }

// 2.7 REMOVER USUÁRIO
[HttpDelete("users/{userId}")]
public async Task<IActionResult> DeleteUser(int userId)
{
    try
    {
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            // limpa permissões customizadas do usuário antes de remover (evita erro de FK)
            await conn.ExecuteAsync(
                "DELETE FROM [dbo].[PermissoesUsuario] WHERE UserID = @UserId",
                new { UserId = userId });

            int affectedRows = await conn.ExecuteAsync(
                "DELETE FROM [dbo].[Usuarios] WHERE UserID = @UserId",
                new { UserId = userId });

            if (affectedRows == 0)
                return NotFound(new { message = "Usuário não encontrado." });

            return Ok(new { sucesso = true, message = "Usuário removido com sucesso!" });
        }
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = $"Erro ao remover usuário: {ex.Message}" });
    }
}





        

[HttpPost("upload-documento")]
public async Task<IActionResult> UploadDocumento(
    [FromForm] IFormFile arquivo,   // a própria planilha LDP
    [FromForm] string projectId,
    [FromForm] string folderId,
    [FromForm] string? hubId = null)
{
    var accessToken = HttpContext.Session.GetString("AccessToken");
    if (string.IsNullOrEmpty(accessToken))
        return Unauthorized(new { message = "Sessão expirada. Faça login novamente." });

    // 1. Valida a estrutura + dados da planilha
    var validacao = await _ldpValidationService.ValidarDocumentoAsync(arquivo.OpenReadStream(), arquivo.FileName);
    if (!validacao.Existe)
        return UnprocessableEntity(new { aviso = true, mensagem = validacao.Mensagem });

    // 2. Sobe a planilha pro ACC
    using var stream = arquivo.OpenReadStream();
    var resultado = await _accUploadService.UploadArquivoAsync(
        projectId, folderId, arquivo.FileName, stream, accessToken);

    if (!resultado.Sucesso)
        return StatusCode(500, resultado);

    return Ok(resultado);
}

        

        // =========================================================================
        // 2. CADASTRAR NOVA EMPRESA 
        // =========================================================================
        [HttpPost("register-tenant")]
        public async Task<IActionResult> RegisterTenant([FromBody] TenantRegistrationDto data)
        {
            if (string.IsNullOrEmpty(data.Cnpj) || string.IsNullOrEmpty(data.TenantName))
                return BadRequest("CNPJ e Nome da Empresa são obrigatórios.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var existe = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Tenants WHERE CNPJ = @Cnpj", new { data.Cnpj });
                    if (existe > 0) return BadRequest("Esta empresa (CNPJ) já está cadastrada no sistema.");

                    string secretCriptografado = string.IsNullOrEmpty(data.ApsClientSecret) 
                        ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(data.ApsClientSecret));

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            string sqlTenant = @"
                                INSERT INTO Tenants 
                                (TenantName, CNPJ, AdminEmail, ContractedPlan, StartDate, EndDate, Status, MaxProjects, MaxSimultaneousRobots, ApsClientId, ApsClientSecret)
                                OUTPUT INSERTED.TenantID
                                VALUES 
                                (@TenantName, @Cnpj, @AdminEmail, @Plan, GETDATE(), DATEADD(YEAR, 1, GETDATE()), 'ACTIVE', @MaxProjects, @MaxRobots, @ApsClientId, @ApsClientSecret);
                            ";

                            int tenantId = await conn.QuerySingleAsync<int>(sqlTenant, new
                            {
                                data.TenantName,
                                data.Cnpj,
                                data.AdminEmail,
                                Plan = data.Plan,
                                data.MaxProjects,
                                data.MaxRobots,
                                ApsClientId = string.IsNullOrEmpty(data.ApsClientId) ? null : data.ApsClientId,
                                ApsClientSecret = secretCriptografado
                            }, transaction);

                            if (data.ActiveModules != null && data.ActiveModules.Any())
                            {
                                string sqlModules = "INSERT INTO Tenant_Modules (TenantID, ModuleID) VALUES (@TenantId, @ModuleId)";
                                foreach (var mod in data.ActiveModules)
                                {
                                    await conn.ExecuteAsync(sqlModules, new { TenantId = tenantId, ModuleId = mod }, transaction);
                                }
                            }

                            transaction.Commit();
                            return Ok(new { sucesso = true, message = "Empresa licenciada com sucesso!" });
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao provisionar empresa: {ex.Message}" });
            }
        }

        // =========================================================================
        // 3. GERAR LICENÇA DE TESTE (ATUALIZA UMA EMPRESA EXISTENTE)
        // =========================================================================
        [HttpPost("generate-trial")]
        public async Task<IActionResult> GenerateTrial([FromBody] TrialDto data)
        {
            if (data.TenantId <= 0) return BadRequest("Empresa inválida.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string status = data.Dias <= 0 ? "EXPIRED" : "ACTIVE";

                    // 🚀 AQUI A GENTE ATUALIZA A EMPRESA EM VEZ DE CRIAR OUTRA
                    string sql = @"
                        UPDATE Tenants 
                        SET EndDate = DATEADD(DAY, @Dias, GETDATE()),
                            Status = @Status,
                            ContractedPlan = 'TRIAL_FREE'
                        WHERE TenantID = @TenantId;
                    ";

                    int linhasAfetadas = await conn.ExecuteAsync(sql, new { Dias = data.Dias, Status = status, TenantId = data.TenantId });

                    if (linhasAfetadas == 0) return NotFound("Empresa não encontrada no banco de dados.");

                    return Ok(new { sucesso = true, message = "Licença atualizada com sucesso!" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao atualizar licença: {ex.Message}" });
            }
        }


        // =========================================================================
        // 4. ATUALIZAR MÓDULOS E CAPACIDADE (UPSELL)
        // =========================================================================
        [HttpPut("update-tenant")]
        public async Task<IActionResult> UpdateTenant([FromBody] TenantUpdateDto data)
        {
            if (data.TenantId <= 0) return BadRequest("Empresa inválida.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Atualiza a capacidade na tabela principal
                            string sqlUpdate = @"
                                UPDATE Tenants 
                                SET MaxProjects = @MaxProjects, 
                                    MaxSimultaneousRobots = @MaxRobots
                                WHERE TenantID = @TenantId;
                            ";
                            await conn.ExecuteAsync(sqlUpdate, new { data.MaxProjects, data.MaxRobots, data.TenantId }, transaction);

                            // 2. Limpa os módulos antigos
                            await conn.ExecuteAsync("DELETE FROM Tenant_Modules WHERE TenantID = @TenantId", new { data.TenantId }, transaction);

                            // 3. Insere os novos módulos escolhidos
                            if (data.ActiveModules != null && data.ActiveModules.Any())
                            {
                                string sqlModules = "INSERT INTO Tenant_Modules (TenantID, ModuleID) VALUES (@TenantId, @ModuleId)";
                                foreach (var mod in data.ActiveModules)
                                {
                                    await conn.ExecuteAsync(sqlModules, new { TenantId = data.TenantId, ModuleId = mod }, transaction);
                                }
                            }

                            transaction.Commit();
                            return Ok(new { sucesso = true, message = "Licença atualizada com sucesso!" });
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao atualizar licença: {ex.Message}" });
            }
        }
    }

    // =========================================================================
    // DTOs (Data Transfer Objects)
    // =========================================================================
    
    // 🚀 DTO QUE FAZ O REACT ENTENDER OS DADOS
    public class TenantResponseDto
    {
        public int Id { get; set; }
        public string TenantName { get; set; }
        public string ContractedPlan { get; set; }
        public int MaxProjects { get; set; }
        public int UsedProjects { get; set; }
        public int MaxSimultaneousRobots { get; set; }
        public int ActiveRobots { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; }
        
        // 🚀 ADICIONE ESTA LINHA AQUI:
        public List<string> ActiveModules { get; set; } = new List<string>();
    }
public class TenantUpdateDto
    {
        public int TenantId { get; set; }
        public int MaxProjects { get; set; }
        public int MaxRobots { get; set; }
        public List<string> ActiveModules { get; set; }
    }
    public class TenantRegistrationDto
    {
        public string TenantName { get; set; }
        public string Cnpj { get; set; }
        public string AdminEmail { get; set; }
        public string ApsClientId { get; set; }
        public string ApsClientSecret { get; set; }
        public string Plan { get; set; }
        public int MaxProjects { get; set; }
        public int MaxRobots { get; set; }
        public List<string> ActiveModules { get; set; }
    }

    // Deixei apenas a versão correta do TrialDto
    public class TrialDto
    {
        public int TenantId { get; set; }
        public int Dias { get; set; }
    }

    public class UserListItemDto
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Email { get; set; }
        public string Papel { get; set; }
        public bool Ativo { get; set; }
        public string UltimoAcesso { get; set; }
        public int QtdPermissoes { get; set; }
        public int TotalTelas { get; set; }
    }

    public class UserPermissionItemDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Hint { get; set; }
        public bool CanAccess { get; set; }
    }

    public class UpdateRoleDto
    {
        public string Role { get; set; }
    }

    public class CreateUserDto
    {
        public string Nome { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
    }
}