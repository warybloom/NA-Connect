using APS.WebApp.Services.Permissions;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Ldp;

namespace APS.WebApp.Controllers.Ldp;

/// <summary>
/// Persistência de configuração de LDP Controle. Schema ldp.* NÃO EXECUTADO — falha explicitamente
/// até a migration ser aplicada. Geração do arquivo de teste da LDP Controle acontece 100%
/// client-side (planilha limpa, sem XLSX base a preservar — ver ClientApp/src/pages/ldp/ExcelControle.tsx),
/// por isso não há endpoint de "gerar-teste" aqui.
/// </summary>
[ApiController]
[Route("api/ldp-config/controle")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpControleController(LdpConfigDatabaseService db, ILogger<LdpControleController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("settings.ldp.controle.view")]
    [HttpGet("templates/{templateId:int}")]
    public IActionResult Listar(int templateId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(db.GetControles(templateId)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][CONTROLE][GET] Falha ao listar controles do template {TemplateId}.", templateId);
            return StatusCode(500, new { message = "Erro ao carregar configurações Controle. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    public sealed class CriarControleRequest { public int TemplateId { get; set; } public string NomeConfiguracao { get; set; } }

    [RequirePermission("settings.ldp.controle.edit")]
    [HttpPost]
    public IActionResult Criar([FromBody] CriarControleRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.NomeConfiguracao)) return BadRequest(new { message = "NomeConfiguracao é obrigatório." });
        try
        {
            var id = db.CreateControle(body.TemplateId, body.NomeConfiguracao);
            return Ok(new { excelControleId = id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][CONTROLE][POST] Falha ao criar controle.");
            return StatusCode(500, new { message = "Erro ao criar configuração Controle. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.controle.edit")]
    [HttpPut("{id:int}/abas")]
    public IActionResult SalvarAbas(int id, [FromBody] List<LdpExcelControleAbaDto> body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            db.ReplaceControleAbas(id, body ?? new List<LdpExcelControleAbaDto>());
            return Ok(new { message = "Configuração salva." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][CONTROLE][PUT] Falha ao salvar abas do controle {Id}.", id);
            return StatusCode(500, new { message = "Erro ao salvar configuração. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.controle.edit")]
    [HttpDelete("{id:int}")]
    public IActionResult Excluir(int id)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { db.DeleteControle(id); return Ok(new { message = "Configuração Controle excluída." }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][CONTROLE][DELETE] Falha ao excluir controle {Id}.", id);
            return StatusCode(500, new { message = "Erro ao excluir. Detalhe: " + ex.Message });
        }
    }
}
