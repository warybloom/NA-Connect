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
    var validacao = await _ldpValidationService.ValidarDocumentoAsync(arquivo.OpenReadStream());
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
}