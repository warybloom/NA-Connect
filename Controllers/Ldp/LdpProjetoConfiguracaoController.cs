using APS.WebApp.Services.Permissions;
using System;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Ldp;

namespace APS.WebApp.Controllers.Ldp;

/// <summary>
/// Persistência de Estrutura do Projeto (Projeto Único x Projeto por Fase) do Novo Motor de LDP —
/// rodada 3. Schema ldp.* proposto em Migrations/ConfiguracaoLdp/05_criar_ldp_projeto_configuracao.sql,
/// NÃO EXECUTADO — falha explicitamente até a migration ser aplicada. Isolado de dbo.Projetos,
/// dbo.Projetos.Tipo_LDP e config.ProjetoConfiguracao (legado, schema config).
/// </summary>
[ApiController]
[Route("api/ldp-config/projeto")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpProjetoConfiguracaoController(LdpConfigDatabaseService db, ILogger<LdpProjetoConfiguracaoController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("settings.ldp.projeto.view")]
    [HttpGet("{projectIdAcc}")]
    public IActionResult Obter(string projectIdAcc)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            var config = db.GetProjetoConfiguracao(projectIdAcc);
            if (config == null) return NotFound(new { message = "Nenhuma configuração de Estrutura do Projeto salva ainda para este projeto." });
            return Ok(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][PROJETO_CONFIG][GET] Falha ao carregar configuração de {ProjectIdAcc}.", projectIdAcc);
            return StatusCode(500, new { message = "Erro ao carregar configuração. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.projeto.edit")]
    [HttpPut("{projectIdAcc}")]
    public IActionResult Salvar(string projectIdAcc, [FromBody] LdpProjetoConfiguracaoDto body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (body == null) return BadRequest(new { message = "Corpo da requisição vazio." });
        if (body.ModoEstrutura != "PROJETO_UNICO" && body.ModoEstrutura != "PROJETO_POR_FASE")
            return BadRequest(new { message = "ModoEstrutura inválido. Use 'PROJETO_UNICO' ou 'PROJETO_POR_FASE'." });

        try
        {
            body.ProjectIdAcc = projectIdAcc;
            db.SalvarProjetoConfiguracao(body);
            return Ok(new { message = "Configuração salva." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][PROJETO_CONFIG][PUT] Falha ao salvar configuração de {ProjectIdAcc}.", projectIdAcc);
            return StatusCode(500, new { message = "Erro ao salvar configuração. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.projeto.edit")]
    [HttpDelete("{projectIdAcc}")]
    public IActionResult Excluir(string projectIdAcc)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            db.DeleteProjetoConfiguracao(projectIdAcc);
            return Ok(new { message = "Configuração excluída." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][PROJETO_CONFIG][DELETE] Falha ao excluir configuração de {ProjectIdAcc}.", projectIdAcc);
            return StatusCode(500, new { message = "Erro ao excluir configuração. Detalhe: " + ex.Message });
        }
    }
}
