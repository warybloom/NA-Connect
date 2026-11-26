using APS.WebApp.Services;
using APS.WebApp.Services.AutodeskAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.RobotRun;
using APS.WebApp.Services.UploadGovernado;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record SalvarPastasDocumentos(IReadOnlyList<PastaProtegida> Raizes);
public sealed record ValidarArquivos(IReadOnlyList<string> Arquivos);

/// <summary>
/// Envio de documentos governado pela LDP + Configuração de Pastas de Documentos (por projeto).
/// projectId/hubId sempre na query (o [RequirePermission] resolve a permissão NO projeto). O acesso ao projeto é validado pela
/// mesma regra dos relatórios (IProjectReportAccess); o envio usa o token do próprio usuário no ACC (AutodeskApiService).
/// </summary>
[ApiController]
[Route("api/upload-governado")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UploadGovernadoController(PastasDocumentosStore store, ILdpDocumentosAtuaisRepository ldp, IProjectAccessService projectAccess,
    IRobotProjectDirectory projectDirectory, AutodeskApiService acc, UploadGovernadoLog log, IPermissionService permissions,
    IAutodeskUserTokenProvider autodeskUserTokens, ILogger<UploadGovernadoController> logger) : ControllerBase
{
    public const long TamanhoMaximoArquivo = 500L * 1024 * 1024;
    public const int MaxArquivosPorLote = 200;

    private string? Token => HttpContext.Items["AutodeskUserToken"] as string;
    private static bool EhAccId(string? v) => v is { Length: 38 } && v.StartsWith("b.", StringComparison.Ordinal) && Guid.TryParseExact(v[2..], "D", out _);

    private async Task<IActionResult?> AcessoInterno(string projectId, CancellationToken ct)
    {
        if (!EhAccId(projectId)) return BadRequest(new { message = "Projeto inválido." });
        return await projectAccess.CheckProjectAccessAsync(projectId, ct) switch
        {
            PermissionCheck.Allowed => null,
            PermissionCheck.Unauthenticated => Unauthorized(new { message = "Sessão expirada." }),
            PermissionCheck.Denied => StatusCode(403, new { message = "Projeto não autorizado." }),
            _ => StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." }),
        };
    }

    private async Task<IActionResult?> GarantirTokenAutodesk(CancellationToken ct)
    {
        var tokenResult = await autodeskUserTokens.GetValidAccessTokenAsync(ct);
        if (!tokenResult.IsValid)
            return Conflict(new { code = "autodeskReconnectRequired", message = "Reconecte a Autodesk para continuar." });
        HttpContext.Items["AutodeskUserToken"] = tokenResult.AccessToken;
        return string.IsNullOrWhiteSpace(Token) ? Unauthorized(new { message = "Sessão expirada." }) : null;
    }

    private async Task<(IActionResult? Error, string? HubId)> ResolverHub(string projectId, CancellationToken ct)
    {
        var project = (await projectDirectory.ReadAsync(ct))
            .SingleOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        if (project is null) return (NotFound(new { message = "Projeto não encontrado no cadastro interno." }), null);
        var raw = project.HubId?.StartsWith("b.", StringComparison.OrdinalIgnoreCase) == true ? project.HubId[2..] : project.HubId;
        return Guid.TryParseExact(raw, "D", out _)
            ? (null, "b." + raw)
            : (BadRequest(new { message = "Projeto sem Account_ID_ACC/Hub válido no cadastro." }), null);
    }

    /// <summary>Pastas-raiz protegidas pela LDP no projeto.</summary>
    [HttpGet("pastas-config")]
    [RequirePermission("quality.folders.view", "settings.projects.view", "cde.docs.upload")]
    public async Task<IActionResult> Configuracao([FromQuery] string projectId, [FromQuery] string? hubId, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        return Ok(new { configuracao = await store.ReadAsync(projectId, ct) });
    }

    [HttpPut("pastas-config")]
    [RequirePermission("quality.folders.edit", "settings.projects.edit")]
    public async Task<IActionResult> SalvarConfiguracao([FromQuery] string projectId, [FromQuery] string? hubId, [FromBody] SalvarPastasDocumentos body, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        try { return Ok(new { configuracao = await store.SaveAsync(projectId, body?.Raizes ?? Array.Empty<PastaProtegida>(), await Usuario(ct), ct) }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Subpastas de uma pasta do ACC (ou da raiz "Project Files") — para escolher a pasta de cada disciplina.</summary>
    [HttpGet("pastas")]
    [RequirePermission("quality.folders.view", "settings.projects.view")]
    public async Task<IActionResult> Pastas([FromQuery] string projectId, [FromQuery] string? hubId, [FromQuery] string? folderId, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        if (await GarantirTokenAutodesk(ct) is { } tokenError) return tokenError;
        var resolved = await ResolverHub(projectId, ct);
        if (resolved.Error is not null) return resolved.Error;
        try
        {
            var pasta = folderId;
            if (string.IsNullOrWhiteSpace(pasta))
            {
                var raiz = await acc.GetProjectRootFolderAsync(resolved.HubId!, projectId, Token!);
                if (raiz is null) return NotFound(new { message = "Pasta 'Project Files' não encontrada." });
                pasta = raiz.Value.GetProperty("id").GetString();
            }
            var itens = await acc.GetFolderContentsAllPagesAsync(projectId, pasta!, Token!, ct);
            return Ok(new
            {
                folderId = pasta,
                pastas = itens.Where(i => i.Type == "folders" && !i.IsHidden).OrderBy(i => i.Name).Select(i => new { id = i.Id, nome = i.Name }),
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("[UPLOAD_LDP] Falha ao listar pastas ({Status}).", ex.StatusCode);
            return StatusCode(502, new { message = "Não foi possível listar as pastas do ACC." });
        }
    }

    /// <summary>Raiz protegida que contém a pasta de destino (ancestralidade por ID/parent do ACC) ou null.</summary>
    private async Task<PastaProtegida?> RaizDoDestino(string projectId, string folderId, CancellationToken ct)
    {
        var config = await store.ReadAsync(projectId, ct);
        return await UploadGovernadoRules.RaizQueContemAsync(folderId, config.Raizes, async id =>
        {
            var detalhes = await acc.GetFolderDetailsAsync(projectId, id, Token!);
            return detalhes is { } d && d.TryGetProperty("data", out var data) && data.TryGetProperty("relationships", out var rel)
                && rel.TryGetProperty("parent", out var parent) && parent.TryGetProperty("data", out var pd) && pd.ValueKind == System.Text.Json.JsonValueKind.Object
                && pd.TryGetProperty("id", out var pid) ? pid.GetString() : null;
        });
    }

    /// <summary>
    /// Proteção da pasta ABERTA no navegador do CDE (sem arquivos): a pasta está numa árvore protegida pela LDP? Mesma herança do
    /// envio (RaizQueContemAsync). Só leitura — serve ao indicador "Pasta protegida pela LDP" ao abrir a pasta.
    /// </summary>
    [HttpGet("protecao")]
    [RequirePermission("cde.docs.view")]
    public async Task<IActionResult> Protecao([FromQuery] string projectId, [FromQuery] string? hubId, [FromQuery] string folderId, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        if (await GarantirTokenAutodesk(ct) is { } tokenError) return tokenError;
        if (string.IsNullOrWhiteSpace(folderId)) return BadRequest(new { message = "Pasta inválida." });
        try
        {
            var raiz = await RaizDoDestino(projectId, folderId, ct);
            return Ok(new { protegida = raiz is not null, raiz });
        }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Não foi possível consultar a pasta no ACC." }); }
    }

    /// <summary>PRÉ-VALIDAÇÃO (só nomes) para a pasta de destino escolhida: nada é enviado.</summary>
    [HttpPost("validar")]
    [RequirePermission("cde.docs.upload")]
    public async Task<IActionResult> Validar([FromQuery] string projectId, [FromQuery] string? hubId, [FromQuery] string folderId, [FromQuery] string? caminho,
        [FromBody] ValidarArquivos body, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        if (await GarantirTokenAutodesk(ct) is { } tokenError) return tokenError;
        if (string.IsNullOrWhiteSpace(folderId)) return BadRequest(new { message = "Escolha a pasta de destino." });
        var nomes = body?.Arquivos ?? Array.Empty<string>();
        if (nomes.Count == 0 || nomes.Count > MaxArquivosPorLote) return BadRequest(new { message = $"Envie de 1 a {MaxArquivosPorLote} arquivos." });
        PastaProtegida? raiz;
        try { raiz = await RaizDoDestino(projectId, folderId, ct); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Não foi possível consultar a pasta no ACC." }); }
        var docs = raiz is null ? Array.Empty<LdpDocumentoAtual>() : await ldp.ReadAsync(projectId, ct);
        return Ok(new { protegida = raiz is not null, raiz, resultados = nomes.Select(n => UploadGovernadoRules.Validar(n, docs, raiz, folderId, caminho)).ToArray() });
    }

    /// <summary>
    /// ENVIO: revalida CADA arquivo no servidor (nunca confia no cliente) e só envia os PRONTOS para a pasta configurada.
    /// Um arquivo bloqueado não impede os demais; todo resultado é registrado no log.
    /// </summary>
    [HttpPost("enviar")]
    [RequirePermission("cde.docs.upload")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Enviar([FromQuery] string projectId, [FromQuery] string? hubId, [FromQuery] string folderId, [FromQuery] string? caminho,
        [FromForm] List<IFormFile> arquivos, CancellationToken ct)
    {
        if (await AcessoInterno(projectId, ct) is { } negado) return negado;
        if (await GarantirTokenAutodesk(ct) is { } tokenError) return tokenError;
        if (string.IsNullOrWhiteSpace(folderId)) return BadRequest(new { message = "Escolha a pasta de destino." });
        if (arquivos is not { Count: > 0 } || arquivos.Count > MaxArquivosPorLote) return BadRequest(new { message = $"Envie de 1 a {MaxArquivosPorLote} arquivos." });
        // O servidor resolve de novo a proteção do destino e revalida cada arquivo (nunca confia na pré-validação do cliente).
        PastaProtegida? raiz;
        try { raiz = await RaizDoDestino(projectId, folderId, ct); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Não foi possível consultar a pasta no ACC." }); }
        var docs = raiz is null ? Array.Empty<LdpDocumentoAtual>() : await ldp.ReadAsync(projectId, ct);
        var usuario = await Usuario(ct);
        var resultados = new List<object>();
        foreach (var arquivo in arquivos)
        {
            var v = UploadGovernadoRules.Validar(arquivo.FileName, docs, raiz, folderId, caminho);
            string resultado, motivo; string? itemId = null, versionId = null;
            if (!UploadGovernadoStatus.PermiteEnvio(v.Status)) { resultado = "BLOQUEADO"; motivo = v.Status + ": " + v.Mensagem; }
            else if (arquivo.Length <= 0 || arquivo.Length > TamanhoMaximoArquivo) { resultado = "BLOQUEADO"; motivo = "Arquivo vazio ou maior que 500 MB."; }
            else
            {
                try
                {
                    await using var stream = arquivo.OpenReadStream();
                    var r = await acc.UploadArquivoNaPastaAsync(projectId, folderId, v.Arquivo, stream, Token!, ct);
                    (resultado, motivo, itemId, versionId) = ("ENVIADO", r.NovaVersao ? "Nova versão do arquivo existente." : "Novo arquivo na pasta.", r.ItemId, r.VersionId);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning("[UPLOAD_LDP] Falha no envio ao ACC ({Status}).", ex.StatusCode);
                    (resultado, motivo) = ("ERRO", "O ACC recusou o envio (verifique permissão na pasta).");
                }
            }
            await log.RegistrarAsync(new UploadLogEntrada(DateTimeOffset.UtcNow, projectId, usuario, v.Arquivo, v.Documento, v.Pasta, resultado, motivo, itemId, versionId), ct);
            resultados.Add(new { validacao = v, resultado, motivo, itemId, versionId });
        }
        return Ok(new { resultados });
    }

    private async Task<string?> Usuario(CancellationToken ct)
    {
        try { return (await permissions.GetCurrentUserAsync(ct))?.Email; }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug("[UPLOAD_LDP] Usuário não identificado ({Tipo}).", ex.GetType().Name); return null; }
    }
}
