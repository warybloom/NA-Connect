using Microsoft.AspNetCore.Mvc;
using ForgeSync.Services;
using System.Collections.Generic;

namespace DB_NA_CONNECT.Controllers
{
    [ApiController]
    [Route("api/admin/saas")]
    public class AdminSaaSController : ControllerBase
    {
        private readonly SaaSService _saasService;

        public AdminSaaSController(SaaSService saasService)
        {
            _saasService = saasService;
        }

        // Endpoint para você listar todas as empresas e seus limites
        [HttpGet("empresas")]
        public IActionResult ListarEmpresas([FromHeader(Name = "X-Admin-Token")] string token)
        {
            // Validação simples de segurança (Etapa 8)
            if (token != "N&A_ADMIN_2026_SECRET_KEY") 
                return Unauthorized("Acesso negado ao painel mestre.");

            // Aqui você chamaria um método no SaaSService para buscar do banco ForgeSync
            return Ok(new { message = "Dados carregados com sucesso" });
        }
    }
}