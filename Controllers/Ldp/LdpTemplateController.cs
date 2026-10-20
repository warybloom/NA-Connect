using APS.WebApp.Services.Permissions;
using System;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Ldp;

namespace APS.WebApp.Controllers.Ldp;

/// <summary>
/// Persistência de Template/Aba/Campo do Novo Motor de LDP (fundação). Schema ldp.* proposto em
/// Migrations/ConfiguracaoLdp/*.sql — NÃO EXECUTADO. Toda chamada aqui falha com erro real e explícito
/// até a migration ser aplicada manualmente; nunca finge sucesso. Ver
/// AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md.
/// </summary>
[ApiController]
[Route("api/ldp-config/templates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpTemplateController(LdpConfigDatabaseService db, ILogger<LdpTemplateController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("settings.ldp.template.view", "settings.ldp.cronograma.view", "settings.ldp.espelho.view", "settings.ldp.controle.view")]
    [HttpGet]
    public IActionResult Listar()
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            return Ok(db.GetTemplates());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][TEMPLATE][GET] Falha ao listar templates.");
            return StatusCode(500, new { message = "Erro ao carregar templates. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    public sealed class CriarTemplateRequest
    {
        public string NomeTemplate { get; set; }
        public string Descricao { get; set; }
        public string OrigemCriacao { get; set; } = "MANUAL";
    }

    [RequirePermission("settings.ldp.template.edit")]
    [HttpPost]
    public IActionResult Criar([FromBody] CriarTemplateRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.NomeTemplate)) return BadRequest(new { message = "NomeTemplate é obrigatório." });

        try
        {
            var id = db.CreateTemplate(body.NomeTemplate, body.Descricao, body.OrigemCriacao);
            return Ok(new { templateId = id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][TEMPLATE][POST] Falha ao criar template.");
            return StatusCode(500, new { message = "Erro ao criar template. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.template.edit")]
    [HttpPut("{id:int}")]
    public IActionResult SalvarArvore(int id, [FromBody] LdpTemplateDto body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (body == null) return BadRequest(new { message = "Corpo da requisição vazio." });

        try
        {
            db.ReplaceTemplateTree(id, body);
            return Ok(new { message = "Template salvo." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][TEMPLATE][PUT] Falha ao salvar árvore do template {TemplateId}.", id);
            return StatusCode(500, new { message = "Erro ao salvar template. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.template.edit")]
    [HttpDelete("{id:int}")]
    public IActionResult Excluir(int id)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            db.DeleteTemplate(id);
            return Ok(new { message = "Template excluído." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][TEMPLATE][DELETE] Falha ao excluir template {TemplateId}.", id);
            return StatusCode(500, new { message = "Erro ao excluir template. Detalhe: " + ex.Message });
        }
    }
}
