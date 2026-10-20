using System.Text.Json;
using APS.WebApp.Services.DisciplineAlias;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record SalvarRegraSigla(string? SiglaOriginal, string? DisciplinaCanonica, string? ContextoTipo, string? ContextoValor, bool Ativo, int Versao);
public sealed record AlterarAtivoRegra(bool Ativo, int Versao);

/// <summary>
/// Configurações → Normalizador de Siglas. Edição com as keys EXISTENTES de configurações (settings.projects.*);
/// leitura das regras ativas também para quem vê os painéis da Gerência da Engenharia (só sigla → nome, sem dado de projeto).
/// Sem a migração aplicada responde 503 com a instrução (nada é criado automaticamente).
/// </summary>
[ApiController]
[Route("api/configuracoes/normalizador-siglas")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisciplineAliasController(SiglaDisciplinaService service, IPermissionService permissions,
    ILogger<DisciplineAliasController> logger) : ControllerBase
{
    public const string PermissaoVer = "settings.projects.view";
    public const string PermissaoEditar = "settings.projects.edit";

    [HttpGet]
    [RequirePermission(PermissaoVer, PermissaoEditar)]
    public Task<IActionResult> Listar(CancellationToken ct) => Executar(async () =>
        Ok(new { instalado = true, regras = await service.ListarAsync(ct) }), ct);

    /// <summary>Regras ATIVAS para os painéis multiprojeto (Gerência da Engenharia).</summary>
    [HttpGet("ativas")]
    [RequirePermission(PermissaoVer, PermissaoEditar, "reports.gestao.view", "reports.ldp.view", "reports.desvios.view", "reports.timesheet.view",
        "production.managerial.hh-custo.view", "production.financial.project.view", "production.financial.document.view", AnyProject = true)]
    public Task<IActionResult> Ativas(CancellationToken ct) => Executar(async () =>
        Ok(new { instalado = true, regras = await service.AtivasAsync(ct) }), ct);

    [HttpGet("importacoes")]
    [RequirePermission(PermissaoVer, PermissaoEditar)]
    public Task<IActionResult> Importacoes(CancellationToken ct) => Executar(async () => Ok(await service.ListarImportacoesAsync(ct)), ct);

    [HttpPost]
    [RequirePermission(PermissaoEditar)]
    public Task<IActionResult> Criar([FromBody] SalvarRegraSigla body, CancellationToken ct) => ComUsuario(async usuario =>
        Ok(await service.CriarAsync(body.SiglaOriginal, body.DisciplinaCanonica, body.ContextoTipo, body.ContextoValor, body.Ativo, usuario, ct)), ct);

    [HttpPut("{id:long}")]
    [RequirePermission(PermissaoEditar)]
    public Task<IActionResult> Atualizar(long id, [FromBody] SalvarRegraSigla body, CancellationToken ct) => ComUsuario(async usuario =>
        Ok(await service.AtualizarAsync(id, body.Versao, body.SiglaOriginal, body.DisciplinaCanonica, body.ContextoTipo, body.ContextoValor,
            body.Ativo, usuario, ct)), ct);

    [HttpPost("{id:long}/ativo")]
    [RequirePermission(PermissaoEditar)]
    public Task<IActionResult> DefinirAtivo(long id, [FromBody] AlterarAtivoRegra body, CancellationToken ct) => ComUsuario(async usuario =>
        Ok(await service.DefinirAtivoAsync(id, body.Versao, body.Ativo, usuario, ct)), ct);

    [HttpDelete("{id:long}")]
    [RequirePermission(PermissaoEditar)]
    public Task<IActionResult> Excluir(long id, [FromQuery] int versao, CancellationToken ct) => ComUsuario(async usuario =>
    {
        await service.ExcluirAsync(id, versao, usuario, ct);
        return NoContent();
    }, ct);

    /// <summary>Prévia (não grava): cabeçalhos reais, mapeamento sugerido e, com mapeamento, a classificação linha a linha.</summary>
    [HttpPost("importacao/previa")]
    [RequirePermission(PermissaoEditar)]
    [RequestSizeLimit(SiglaPlanilhaReader.TamanhoMaximoBytes + 64 * 1024)]
    public Task<IActionResult> Previa(IFormFile? arquivo, [FromForm] string? aba, [FromForm] string? mapeamento,
        [FromForm] bool aplicarAlteracoes, CancellationToken ct) => Executar(async () =>
    {
        var (bytes, erro) = await LerArquivoAsync(arquivo, ct);
        if (erro is not null) return BadRequest(new { message = erro });
        if (!TentarMapeamento(mapeamento, out var m)) return BadRequest(new { message = "Mapeamento de colunas inválido." });
        return Ok(await service.PreverAsync(bytes!, aba, m, aplicarAlteracoes, ct));
    }, ct);

    [HttpPost("importacao/confirmar")]
    [RequirePermission(PermissaoEditar)]
    [RequestSizeLimit(SiglaPlanilhaReader.TamanhoMaximoBytes + 64 * 1024)]
    public Task<IActionResult> Confirmar(IFormFile? arquivo, [FromForm] string? hash, [FromForm] string? aba, [FromForm] string? mapeamento,
        [FromForm] bool aplicarAlteracoes, CancellationToken ct) => ComUsuario(async usuario =>
    {
        var (bytes, erro) = await LerArquivoAsync(arquivo, ct);
        if (erro is not null) return BadRequest(new { message = erro });
        if (!TentarMapeamento(mapeamento, out var m) || m is null) return BadRequest(new { message = "Mapeamento de colunas obrigatório." });
        if (string.IsNullOrWhiteSpace(hash)) return BadRequest(new { message = "Gere a prévia antes de confirmar." });
        return Ok(await service.ConfirmarAsync(bytes!, arquivo!.FileName, hash, aba, m, aplicarAlteracoes, usuario, ct));
    }, ct);

    private static async Task<(byte[]? Bytes, string? Erro)> LerArquivoAsync(IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0) return (null, "Envie um arquivo .xlsx.");
        if (!string.Equals(Path.GetExtension(arquivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return (null, "Formato não suportado: envie um arquivo .xlsx.");
        if (arquivo.Length > SiglaPlanilhaReader.TamanhoMaximoBytes) return (null, "Arquivo maior que 5 MB.");
        using var ms = new MemoryStream();
        await arquivo.CopyToAsync(ms, ct);
        return (ms.ToArray(), null);
    }

    private static bool TentarMapeamento(string? json, out MapeamentoColunas? mapeamento)
    {
        mapeamento = null;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            mapeamento = JsonSerializer.Deserialize<MapeamentoColunas>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return mapeamento is not null;
        }
        catch (JsonException) { return false; }
    }

    private async Task<IActionResult> ComUsuario(Func<string, Task<IActionResult>> acao, CancellationToken ct)
    {
        var eu = await permissions.GetCurrentUserAsync(ct);
        var usuario = eu is null ? null : !string.IsNullOrWhiteSpace(eu.Email) ? eu.Email : eu.Nome;
        if (string.IsNullOrWhiteSpace(usuario)) return Unauthorized(new { message = "Sessão expirada." });
        return await Executar(() => acao(usuario), ct);
    }

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao, CancellationToken ct)
    {
        try { return await acao(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (NormalizadorNaoInstaladoException ex) { return StatusCode(503, new { instalado = false, message = ex.Message }); }
        catch (RegraInvalidaException ex) { return BadRequest(new { message = ex.Message, erros = ex.Erros }); }
        catch (RegraDuplicadaException ex) { return Conflict(new { message = ex.Message }); }
        catch (ConcorrenciaException ex) { return Conflict(new { message = ex.Message }); }
        catch (RegraNaoEncontradaException ex) { return NotFound(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogWarning("Normalizador de Siglas indisponível. Ref={Ref} Tipo={Tipo}", HttpContext.TraceIdentifier, ex.GetType().Name);
            return StatusCode(503, new { message = $"Normalizador de Siglas temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }
}
