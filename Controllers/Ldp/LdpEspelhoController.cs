using APS.WebApp.Services.Permissions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Ldp;

namespace APS.WebApp.Controllers.Ldp;

/// <summary>
/// Persistência de configuração de LDP Espelho + upload do XLSX base + geração REAL de teste. CRUD de
/// configuração usa o schema ldp.* (NÃO EXECUTADO — falha explicitamente até a migration ser
/// aplicada). "Gerar Teste" NÃO depende do schema: recebe a configuração inteira no corpo da
/// requisição (estado do frontend) e devolve um .xlsx real gerado a partir do arquivo base já enviado
/// — funciona hoje, sem tabela nenhuma.
/// </summary>
[ApiController]
[Route("api/ldp-config/espelho")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LdpEspelhoController(
    LdpConfigDatabaseService db,
    LdpArquivoStorageService storage,
    LdpEspelhoGeradorService gerador,
    ILogger<LdpEspelhoController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("settings.ldp.espelho.view")]
    [HttpGet("templates/{templateId:int}")]
    public IActionResult Listar(int templateId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(db.GetEspelhos(templateId)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][GET] Falha ao listar espelhos do template {TemplateId}.", templateId);
            return StatusCode(500, new { message = "Erro ao carregar modelos Espelho. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    public sealed class CriarEspelhoRequest { public int TemplateId { get; set; } public string NomeModelo { get; set; } public string ArquivoReferencia { get; set; } }

    [RequirePermission("settings.ldp.espelho.edit")]
    [HttpPost]
    public IActionResult Criar([FromBody] CriarEspelhoRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.NomeModelo)) return BadRequest(new { message = "NomeModelo é obrigatório." });
        try
        {
            var id = db.CreateEspelho(body.TemplateId, body.NomeModelo, body.ArquivoReferencia);
            return Ok(new { excelEspelhoId = id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][POST] Falha ao criar espelho.");
            return StatusCode(500, new { message = "Erro ao criar modelo Espelho. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.espelho.edit")]
    [HttpPut("{id:int}/abas")]
    public IActionResult SalvarAbas(int id, [FromBody] List<LdpExcelEspelhoAbaDto> body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            db.ReplaceEspelhoAbas(id, body ?? new List<LdpExcelEspelhoAbaDto>());
            return Ok(new { message = "Configuração salva." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][PUT] Falha ao salvar abas do espelho {Id}.", id);
            return StatusCode(500, new { message = "Erro ao salvar configuração. O schema ldp.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("settings.ldp.espelho.edit")]
    [HttpDelete("{id:int}")]
    public IActionResult Excluir(int id)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { db.DeleteEspelho(id); return Ok(new { message = "Modelo Espelho excluído." }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][DELETE] Falha ao excluir espelho {Id}.", id);
            return StatusCode(500, new { message = "Erro ao excluir. Detalhe: " + ex.Message });
        }
    }

    /// <summary>
    /// Recebe o XLSX base (logo/carimbo/estilos) e guarda no armazenamento TEMPORÁRIO configurado
    /// (appsettings.json:LdpConfig:ArmazenamentoBasePath — decisão definitiva de infraestrutura ainda
    /// pendente, ver documento 17 §9/§20). Devolve um token para usar em SalvarAbas/GerarTeste.
    /// </summary>
    [RequirePermission("settings.ldp.espelho.edit")]
    [HttpPost("upload-base")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> UploadBase(IFormFile arquivo)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { message = "Envie um arquivo .xlsx." });

        try
        {
            using var ms = new MemoryStream();
            await arquivo.CopyToAsync(ms);
            var token = storage.Salvar(ms.ToArray(), arquivo.FileName);
            return Ok(new { arquivoToken = token, nomeOriginal = arquivo.FileName });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][UPLOAD_BASE] Falha ao salvar arquivo base.");
            return StatusCode(500, new { message = "Erro ao salvar arquivo base: " + ex.Message });
        }
    }

    public sealed class GerarTesteRequest
    {
        public string ArquivoToken { get; set; }
        public List<LdpEspelhoAbaGeracao> Abas { get; set; } = new();
    }

    /// <summary>
    /// GERA UM ARQUIVO REAL para validação — não conecta ao Worker, não envia ao ACC, não sobrescreve
    /// a LDP atual. Não depende do schema ldp.* estar aplicado: a configuração inteira vem no corpo da
    /// requisição (estado atual do frontend), só o arquivo base precisa já ter sido enviado via
    /// upload-base.
    /// </summary>
    [RequirePermission("settings.ldp.espelho.edit")]
    [HttpPost("gerar-teste")]
    public IActionResult GerarTeste([FromBody] GerarTesteRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.ArquivoToken)) return BadRequest(new { message = "ArquivoToken é obrigatório — faça upload do XLSX base primeiro." });
        if (body.Abas == null || body.Abas.Count == 0) return BadRequest(new { message = "Configure ao menos uma aba para gerar o teste." });

        try
        {
            var baseBytes = storage.Ler(body.ArquivoToken);
            var gerado = gerador.Gerar(baseBytes, body.Abas);
            return File(gerado, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "LDP_Espelho_TESTE.xlsx");
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LDP][ESPELHO][GERAR_TESTE] Falha ao gerar arquivo de teste.");
            return StatusCode(500, new { message = "Erro ao gerar o arquivo de teste: " + ex.Message });
        }
    }
}
