using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Tv;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record SalvarTvConfiguration(long Revision, IReadOnlyList<TvPresentation> Presentations);

/// <summary>
/// Configuração de Telas para TV (Gestão à Vista). GET: quem configura ou quem assiste (player). PUT: só quem configura.
/// Os dados de cada tela continuam vindo do endpoint do dashboard, que valida acesso ao projeto e a key da tela.
/// </summary>
[ApiController]
[Route("api/tv-config")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TvConfigurationController(TvConfigurationStore store, IPermissionService permissions,
    IPermissionRepository permissionRepository, ILogger<TvConfigurationController> logger) : ControllerBase
{
    // Flag de um perfil reutilizável. Não altera o comportamento dos demais perfis.
    private const string PerfilRestrito = "Controle de Produção + Configuração TV (Restrito)";

    private async Task<int?> GestorRestritoIdAsync(CancellationToken ct)
    {
        var usuario = await permissions.GetCurrentUserAsync(ct);
        if (usuario?.UserId is not int id || usuario.IsMasterAdmin) return null;
        var dados = await permissionRepository.CarregarDadosAsync(id, false, ct);
        return dados.Assignments.Any(a => a.ProjetoId is null && a.ProfileName == PerfilRestrito)
            ? id : null;
    }

    private static bool TelaExcluida(string? id) =>
        id?.StartsWith("gestao-vista-tipo2-", StringComparison.Ordinal) == true
        || string.Equals(id, "resumo-tv-teste", StringComparison.Ordinal);

    private async Task<bool> AutorizadaParaProjetosAsync(TvPresentation presentation, int userId, CancellationToken ct)
    {
        foreach (var slide in presentation.Slides)
        {
            if (TelaExcluida(slide.ScreenId)) return false;
            var projetos = slide.Projetos is { Count: > 0 } ps
                ? ps.Select(x => x.ProjectId)
                : new[] { slide.ProjectId };
            foreach (var projectId in projetos.Distinct(StringComparer.Ordinal))
                if (!await permissionRepository.UsuarioPossuiAcessoAtivoProjetoAsync(userId, projectId, ct))
                    return false;
        }
        return true;
    }

    [HttpGet]
    [RequirePermission("settings.tv.configure", "tv.view")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var identity = await HttpContext.RequestServices.GetRequiredService<APS.WebApp.Services.Tv.Portal.TvPortalIdentityService>().ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (identity.Restricted || (!identity.CanConfigure && !identity.CanView)) return StatusCode(403);
        try
        {
            var configuration = await store.ReadAsync(ct);
            var gestorRestrito = await GestorRestritoIdAsync(ct);
            if (gestorRestrito is int scopedId)
            {
                var visiveis = new List<TvPresentation>();
                foreach (var presentation in configuration.Presentations)
                    if (await AutorizadaParaProjetosAsync(presentation, scopedId, ct))
                        visiveis.Add(presentation);
                configuration = configuration with { Presentations = visiveis };
            }
            var autorizadas = await TelasAutorizadasAsync(ct);
            return Ok(new
            {
                configuration,
                telasPermitidas = gestorRestrito is null ? TvConfigurationRules.TelasPermitidas
                    : TvConfigurationRules.TelasPermitidas.Where(x => !TelaExcluida(x)).ToArray(),
                telasAutorizadas = gestorRestrito is null ? autorizadas
                    : autorizadas?.Where(x => !TelaExcluida(x)).ToArray(),
                // 20/10/2026: este servidor aceita a Programação Diária automática (DataReferencia = "AUTO").
                diariaAutomatica = true,
                // 20/10/2026: aceita a exibição por slide automático (LayoutDiario padrao/cards/disciplina + Disciplina).
                layoutsDiarios = true,
            });
        }
        catch (TvConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    /// <summary>
    /// Telas autorizadas = keys da tela (as MESMAS da rota) ∩ permissões efetivas do usuário (globais ou em algum projeto).
    /// Só filtra a oferta no seletor: configurar a TV não libera dado nenhum — cada endpoint de relatório valida a sua key.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TelasAutorizadasAsync(CancellationToken ct)
    {
        if (!permissions.Enforced) return TvTelas.Autorizadas(true, []);
        try
        {
            var eff = await permissions.GetEffectiveAsync(null, ct);
            return TvTelas.Autorizadas(eff.IsMasterAdmin || !eff.Enforced, eff.Permissions.Concat(eff.AnyProject));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("TV: permissões efetivas indisponíveis para listar telas autorizadas. Tipo={Tipo}", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Projetos elegíveis para configurar TVs: projetos N&A já sincronizados (LDP em dbo.Ldp_Unificada). Fonte
    /// administrativa interna — sem Autodesk/hubId e sem filtro por UserProjects de quem configura. Exige
    /// settings.tv.configure de forma ESTRITA (com Permissions:Enforce=false o [RequirePermission] libera qualquer sessão).
    /// </summary>
    [HttpGet("projects")]
    [RequirePermission("settings.tv.configure")]
    public async Task<IActionResult> Projects([FromServices] ITvProjetosElegiveisRepository projetos, CancellationToken ct)
    {
        try
        {
            var eu = await permissions.GetCurrentUserAsync(ct);
            if (eu is null) return Unauthorized(new { message = "Sessão expirada." });
            if (!eu.IsMasterAdmin)
            {
                if (eu.UserId is not int id) return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade." });
                var dados = await permissionRepository.CarregarDadosAsync(id, false, ct);
                if (!PermissionResolver.Decide(dados, "settings.tv.configure", null).Allowed)
                    return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = new[] { "settings.tv.configure" } });
            }
            var lista = await projetos.ListarAsync(ct);
            if (await GestorRestritoIdAsync(ct) is int scopedId)
            {
                var permitidos = new List<TvProjetoElegivel>();
                foreach (var project in lista)
                    if (await permissionRepository.UsuarioPossuiAcessoAtivoProjetoAsync(scopedId, project.ProjectIdAcc, ct))
                        permitidos.Add(project);
                return Ok(permitidos);
            }
            return Ok(lista);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Projetos elegíveis para TV indisponíveis. Tipo={Tipo}", ex.GetType().Name);
            return StatusCode(503, new { message = "Não foi possível carregar os projetos." });
        }
    }

    [HttpPut]
    [RequirePermission("settings.tv.configure")]
    public async Task<IActionResult> Save([FromBody] SalvarTvConfiguration request, CancellationToken ct)
    {
        var identity = await HttpContext.RequestServices.GetRequiredService<APS.WebApp.Services.Tv.Portal.TvPortalIdentityService>().ReadAsync(ct);
        if (identity is null) return Unauthorized();
        if (!identity.CanConfigure) return StatusCode(403);
        if (request?.Presentations is null) return BadRequest(new { message = "Configuração ausente." });
        try
        {
            string? usuario = null;
            try { usuario = (await permissions.GetCurrentUserAsync(ct))?.Email; }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug("TV: usuário não identificado ({Tipo}).", ex.GetType().Name); }
            var gestorRestrito = await GestorRestritoIdAsync(ct);
            if (gestorRestrito is int scopedId)
            {
                // Nunca permitir editar/apagar apresentações de outros projetos.
                foreach (var presentation in request.Presentations)
                    if (!await AutorizadaParaProjetosAsync(presentation, scopedId, ct))
                        return StatusCode(403, new { message = "Apresentação ou projeto não autorizado para este perfil." });
                var atual = await store.ReadAsync(ct);
                if (atual.Revision != request.Revision) return Conflict(new { message = "A configuração foi alterada. Recarregue." });
                var idsEditaveis = new HashSet<string>(StringComparer.Ordinal);
                foreach (var presentation in atual.Presentations)
                    if (await AutorizadaParaProjetosAsync(presentation, scopedId, ct))
                        idsEditaveis.Add(presentation.Id);
                var naoEditaveis = atual.Presentations.Where(p => !idsEditaveis.Contains(p.Id));
                var preservadas = naoEditaveis.Concat(request.Presentations).ToArray();
                return Ok(new { configuration = await store.SaveAsync(preservadas, request.Revision, usuario, ct) });
            }
            return Ok(new { configuration = await store.SaveAsync(request.Presentations, request.Revision, usuario, ct) });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (TvConfigurationConflictException ex) { return Conflict(new { message = ex.Message }); }
        catch (TvConfigurationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
}
