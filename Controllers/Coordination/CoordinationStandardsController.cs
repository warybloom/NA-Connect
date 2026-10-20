using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Controllers.Coordination;

/// <summary>
/// Cadastro FUNCIONAL de Norma de Coordenação (Sigla -&gt; Disciplina), consumido por
/// PmoActualFactsProvider para classificar HH/Custo REALIZADO. Persistência real (schema
/// coordenacao.*, Migrations/Coordenacao/*.sql) — nunca demo. EPPlus.LicenseContext já é setado
/// globalmente em Program.cs.
/// </summary>
[ApiController]
[Route("api/coordination-standards")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoordinationStandardsController(
    CoordinationStandardRepository repository,
    CoordinationDiagnosticsService diagnostics,
    ILogger<CoordinationStandardsController> logger) : ControllerBase
{
    private bool SessaoValida() => !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));

    [RequirePermission("planning.coordination.view")]
    [HttpGet("normas")]
    public IActionResult ListarNormas()
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(repository.ListarNormas()); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][NORMAS][GET] Falha ao listar.");
            return StatusCode(500, new { message = "Erro ao carregar Normas de Coordenação. O schema coordenacao.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.view")]
    [HttpGet("normas/{normaId:int}")]
    public IActionResult ObterNorma(int normaId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            var norma = repository.ObterNorma(normaId);
            return norma == null ? NotFound(new { message = "Norma não encontrada." }) : Ok(norma);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][NORMAS][GET/id] Falha ao obter norma {NormaId}.", normaId);
            return StatusCode(500, new { message = "Erro ao carregar a Norma. Detalhe: " + ex.Message });
        }
    }

    public sealed class NormaRequest { public string Nome { get; set; } public string Descricao { get; set; } public string Origem { get; set; } public bool Ativa { get; set; } = true; }

    [RequirePermission("planning.coordination.edit")]
    [HttpPost("normas")]
    public IActionResult CriarNorma([FromBody] NormaRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.Nome)) return BadRequest(new { message = "Nome é obrigatório." });
        if (!CoordinationOrigens.EhValida(body.Origem)) return BadRequest(new { message = "Origem deve ser 'N&A' ou 'CLIENTE'." });
        try { return Ok(new { normaId = repository.CriarNorma(body.Nome.Trim(), body.Descricao, body.Origem) }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][NORMAS][POST] Falha ao criar norma.");
            return StatusCode(500, new { message = "Erro ao criar Norma. O schema coordenacao.* foi aplicado? Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.edit")]
    [HttpPut("normas/{normaId:int}")]
    public IActionResult AtualizarNorma(int normaId, [FromBody] NormaRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (string.IsNullOrWhiteSpace(body?.Nome)) return BadRequest(new { message = "Nome é obrigatório." });
        if (!CoordinationOrigens.EhValida(body.Origem)) return BadRequest(new { message = "Origem deve ser 'N&A' ou 'CLIENTE'." });
        try { repository.AtualizarNorma(normaId, body.Nome.Trim(), body.Descricao, body.Origem, body.Ativa); return Ok(new { message = "Norma atualizada." }); }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][NORMAS][PUT] Falha ao atualizar norma {NormaId}.", normaId);
            return StatusCode(500, new { message = "Erro ao atualizar Norma. Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.edit")]
    [HttpDelete("normas/{normaId:int}")]
    public IActionResult ExcluirNorma(int normaId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { repository.ExcluirNorma(normaId); return Ok(new { message = "Norma excluída." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][NORMAS][DELETE] Falha ao excluir norma {NormaId}.", normaId);
            return StatusCode(500, new { message = "Erro ao excluir Norma. Detalhe: " + ex.Message });
        }
    }

    public sealed class MapeamentoRequest { public string Sigla { get; set; } public string Disciplina { get; set; } }

    [RequirePermission("planning.coordination.edit")]
    [HttpPost("normas/{normaId:int}/mapeamentos")]
    public IActionResult AdicionarMapeamento(int normaId, [FromBody] MapeamentoRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(new { normaMapeamentoId = repository.AdicionarMapeamento(normaId, body?.Sigla, body?.Disciplina) }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][MAPEAMENTOS][POST] Falha ao adicionar mapeamento (norma {NormaId}).", normaId);
            return StatusCode(500, new { message = "Erro ao adicionar sigla. Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.edit")]
    [HttpPut("mapeamentos/{mapeamentoId:int}")]
    public IActionResult AtualizarMapeamento(int mapeamentoId, [FromBody] MapeamentoRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { repository.AtualizarMapeamento(mapeamentoId, body?.Sigla, body?.Disciplina); return Ok(new { message = "Mapeamento atualizado." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][MAPEAMENTOS][PUT] Falha ao atualizar mapeamento {Id}.", mapeamentoId);
            return StatusCode(500, new { message = "Erro ao atualizar sigla. Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.edit")]
    [HttpDelete("mapeamentos/{mapeamentoId:int}")]
    public IActionResult RemoverMapeamento(int mapeamentoId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { repository.RemoverMapeamento(mapeamentoId); return Ok(new { message = "Sigla removida." }); }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][MAPEAMENTOS][DELETE] Falha ao remover mapeamento {Id}.", mapeamentoId);
            return StatusCode(500, new { message = "Erro ao remover sigla. Detalhe: " + ex.Message });
        }
    }

    /// <summary>Lê o Excel (colunas SIGLA/DISCIPLINA) e SÓ devolve o preview validado — nunca grava.
    /// Cabeçalho aceita variação de caixa/espaço; a ordem oficial é SIGLA, DISCIPLINA.</summary>
    [RequirePermission("planning.coordination.edit")]
    [HttpPost("normas/{normaId:int}/importar-excel/preview")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> PreviewImportarExcel(int normaId, IFormFile arquivo)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { message = "Envie um arquivo Excel (.xlsx)." });
        try
        {
            using var ms = new MemoryStream();
            await arquivo.CopyToAsync(ms);
            var norma = repository.ObterNorma(normaId);
            if (norma == null) return NotFound(new { message = "Norma não encontrada." });
            return Ok(CoordinationExcelImport.Validar(ms.ToArray(), norma.Mapeamentos.Select(m => m.Sigla).ToHashSet()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][IMPORT][PREVIEW] Falha ao ler '{Arquivo}'.", arquivo.FileName);
            return StatusCode(500, new { message = "Erro ao ler o Excel: " + ex.Message });
        }
    }

    public sealed class ConfirmarImportacaoRequest { public List<LinhaImportacao> Linhas { get; set; } = new(); public sealed class LinhaImportacao { public string Sigla { get; set; } public string Disciplina { get; set; } } }

    /// <summary>Confirma a importação: recebe as linhas JÁ validadas pelo preview (o frontend nunca deixa
    /// enviar linha inválida) e persiste em UMA transação (upsert por sigla).</summary>
    [RequirePermission("planning.coordination.edit")]
    [HttpPost("normas/{normaId:int}/importar-excel/confirmar")]
    public IActionResult ConfirmarImportacao(int normaId, [FromBody] ConfirmarImportacaoRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (body?.Linhas == null || body.Linhas.Count == 0) return BadRequest(new { message = "Nenhuma linha para importar." });
        try
        {
            repository.ImportarMapeamentos(normaId, body.Linhas.Select(l => (l.Sigla, l.Disciplina)).ToArray());
            return Ok(new { message = $"{body.Linhas.Count} sigla(s) importada(s).", norma = repository.ObterNorma(normaId) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][IMPORT][CONFIRMAR] Falha ao importar (norma {NormaId}).", normaId);
            return StatusCode(500, new { message = "Erro ao importar. Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.view")]
    [HttpGet("projetos/{projectId}")]
    public IActionResult ObterNormaDoProjeto(string projectId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            var normaId = repository.ObterNormaIdDoProjeto(projectId);
            return Ok(new { normaId, norma = normaId.HasValue ? repository.ObterNorma(normaId.Value) : null });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][PROJETOS][GET] Falha ao obter norma do projeto {ProjectId}.", projectId);
            return StatusCode(500, new { message = "Erro ao carregar a Norma do projeto. Detalhe: " + ex.Message });
        }
    }

    public sealed class DefinirNormaRequest { public int NormaId { get; set; } }

    [RequirePermission("planning.coordination.edit")]
    [HttpPut("projetos/{projectId}")]
    public IActionResult DefinirNormaDoProjeto(string projectId, [FromBody] DefinirNormaRequest body)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (body == null || body.NormaId <= 0) return BadRequest(new { message = "normaId é obrigatório." });
        try { repository.DefinirNormaDoProjeto(projectId, body.NormaId); return Ok(new { message = "Norma do projeto atualizada." }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][PROJETOS][PUT] Falha ao definir norma do projeto {ProjectId}.", projectId);
            return StatusCode(500, new { message = "Erro ao definir a Norma do projeto. Detalhe: " + ex.Message });
        }
    }

    [RequirePermission("planning.coordination.edit")]
    [HttpDelete("projetos/{projectId}")]
    public IActionResult RemoverNormaDoProjeto(string projectId)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        try { repository.RemoverNormaDoProjeto(projectId); return Ok(new { message = "Vínculo removido." }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][PROJETOS][DELETE] Falha ao remover vínculo do projeto {ProjectId}.", projectId);
            return StatusCode(500, new { message = "Erro ao remover o vínculo. Detalhe: " + ex.Message });
        }
    }

    /// <summary>Diagnóstico de auditoria: siglas encontradas no Timesheet sem correspondência na Norma do
    /// próprio projeto, com quantidade de lançamentos, HH e projetos — nunca mistura a norma de um
    /// projeto com o doc3 de outro.</summary>
    [RequirePermission("planning.coordination.view")]
    [HttpGet("diagnostico/siglas-nao-mapeadas")]
    public async Task<IActionResult> SiglasNaoMapeadas([FromQuery] string[] projectIds, CancellationToken ct)
    {
        if (!SessaoValida()) return Unauthorized(new { message = "Sessão expirada." });
        if (projectIds == null || projectIds.Length == 0) return BadRequest(new { message = "Informe ao menos um projectId." });
        try { return Ok(await diagnostics.ObterSiglasNaoMapeadasAsync(projectIds, ct)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[COORDENACAO][DIAGNOSTICO] Falha ao apurar siglas não mapeadas.");
            return StatusCode(500, new { message = "Erro ao apurar diagnóstico. Detalhe: " + ex.Message });
        }
    }
}
