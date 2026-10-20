using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

/// <summary>Projetos internos permitidos ao usuário atual. Não consulta Autodesk.</summary>
[ApiController]
[Route("api/me")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MeProjectsController(INaCurrentUserAccessor currentUser, IPermissionRepository repository) : ControllerBase
{
    public sealed record MeProjectDto(int ProjetoId, string ProjectIdAcc, string NomeProjeto, byte? TipoLdp, bool IsAtivo);

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<MeProjectDto>>> Projects(CancellationToken ct)
    {
        var identidade = await currentUser.GetAsync(ct);
        if (identidade is null) return Unauthorized();

        var usuario = await repository.ObterUsuarioAsync(identidade.UserId, ct);
        if (usuario is null || !usuario.Ativo) return Unauthorized();

        var projetos = await repository.ListarProjetosPermitidosAsync(usuario.UserId, ct);
        return Ok(projetos.Select(p => new MeProjectDto(p.ProjetoId, p.ProjectIdAcc, p.NomeProjeto, p.TipoLdp, p.IsAtivo)).ToArray());
    }
}
