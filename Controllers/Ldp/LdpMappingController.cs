using APS.WebApp.Services.Permissions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Ldp;

namespace APS.WebApp.Controllers.Ldp;

/// <summary>
/// Persistência de Mapeamento de Cronograma + importação de estrutura de .mpp do Novo Motor de LDP
/// (fundação). Schema ldp.* NÃO EXECUTADO — CRUD falha explicitamente até a migration ser aplicada.
/// O import de MPP (GET de estrutura, não persistência) FUNCIONA hoje, independente do schema, porque
/// só lê o arquivo enviado e devolve JSON — não grava nada.
/// </summary>
[ApiController]
[Route("api/ldp-config")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpMappingController(
    LdpConfigDatabaseService db,
    LdpMppParserService mppParser,
    ILogger<LdpMappingController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("settings.ldp.cronograma.view")]
    [HttpGet("templates/{templateId:int}/mapeamentos")]
    public IActionResult Listar(int templateId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(db.GetMapeamentos(templateId)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][MAPEAMENTO][GET] Falha ao listar mapeamentos do template {TemplateId}.", templateId);
            return StatusCode(500, new { message = "Erro ao carregar mapeamentos. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    public sealed class CriarMapeamentoRequest { public string NomeMapeamento { get; set; } }

    [RequirePermission("settings.ldp.cronograma.edit")]
    [HttpPost("templates/{templateId:int}/mapeamentos")]
    public IActionResult Criar(int templateId, [FromBody] CriarMapeamentoRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.NomeMapeamento)) return BadRequest(new { message = "NomeMapeamento é obrigatório." });
        try
        {
            var id = db.CreateMapeamento(templateId, body.NomeMapeamento);
            return Ok(new { cronogramaMapeamentoId = id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][MAPEAMENTO][POST] Falha ao criar mapeamento.");
            return StatusCode(500, new { message = "Erro ao criar mapeamento. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.cronograma.edit")]
    [HttpPut("mapeamentos/{id:int}/campos")]
    public IActionResult SalvarCampos(int id, [FromBody] List<LdpCronogramaMapeamentoCampoDto> body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            db.ReplaceMapeamentoCampos(id, body ?? new List<LdpCronogramaMapeamentoCampoDto>());
            return Ok(new { message = "Mapeamento salvo." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][MAPEAMENTO][PUT] Falha ao salvar campos do mapeamento {Id}.", id);
            return StatusCode(500, new { message = "Erro ao salvar mapeamento. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.cronograma.edit")]
    [HttpDelete("mapeamentos/{id:int}")]
    public IActionResult Excluir(int id)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { db.DeleteMapeamento(id); return Ok(new { message = "Mapeamento excluído." }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][MAPEAMENTO][DELETE] Falha ao excluir mapeamento {Id}.", id);
            return StatusCode(500, new { message = "Erro ao excluir mapeamento. Detalhe: " + ex.Message });
        }
    }

    /// <summary>
    /// Lê um .mpp real (MPXJ.Net — mesma lib já usada por Cronograma Tipo 1/2, nunca instala/depende
    /// do MS Project) e devolve os campos EFETIVAMENTE presentes + preview de linhas. Não persiste
    /// nada — funciona mesmo sem o schema ldp.* aplicado.
    /// </summary>
    [RequirePermission("settings.ldp.cronograma.edit")]
    [HttpPost("cronograma/importar-mpp")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportarMpp(IFormFile arquivo)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { message = "Envie um arquivo .mpp." });

        try
        {
            using var ms = new MemoryStream();
            await arquivo.CopyToAsync(ms);
            var resultado = mppParser.Ler(ms.ToArray(), arquivo.FileName);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][MPP][IMPORT] Falha ao ler '{Arquivo}'.", arquivo.FileName);
            return StatusCode(500, new { message = "Erro ao ler o arquivo MPP: " + ex.Message });
        }
    }
}
