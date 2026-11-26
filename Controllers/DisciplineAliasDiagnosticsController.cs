using APS.WebApp.Services.DisciplineAlias;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/configuracoes/normalizador-siglas/sem-mapeamento")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisciplineAliasDiagnosticsController(SiglaDisciplinaService service, IPermissionService permissions,
    IProjectAccessService projectAccess, IConfiguration configuration, ILogger<DisciplineAliasDiagnosticsController> logger) : ControllerBase
{
    [HttpGet]
    [RequirePermission(DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar, AnyProject = true)]
    public async Task<IActionResult> Get([FromQuery] string? projectIds, CancellationToken ct)
    {
        var ids = (projectIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length is 0 or > 100) return BadRequest(new { message = "Selecione de 1 a 100 projetos." });
        try
        {
            foreach (var id in ids)
            {
                var acesso = await projectAccess.CheckProjectAccessAsync(id, ct);
                if (acesso == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
                if (acesso == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
                if (acesso != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
                var permissao = await permissions.CheckAsync([DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar], id, ct);
                if (permissao != PermissionCheck.Allowed)
                    return permissao == PermissionCheck.Unauthenticated ? Unauthorized(new { message = "Sessão expirada." })
                        : StatusCode(403, new { message = "Você não tem permissão para consultar este projeto." });
            }

            var projetos = new List<ProjetoSiglasDiagnostico>(ids.Length);
            var semLdpReader = new DisciplineSegmentImpactReader(configuration);
            foreach (var id in ids)
            {
                // Projeto sem LDP: os documentos vêm do Timesheet corrigido (mesmo inventário do Resumo), não da LDP vazia.
                if (await semLdpReader.SemLdpAsync(id, ct))
                {
                    if (await semLdpReader.DiagnosticoSemLdpAsync(id, ct) is { } semLdp) projetos.Add(semLdp);
                    continue;
                }
                var r = await PmoExecutiveData.ReadAsync(configuration, id, false, ct, currentPlanningSnapshot: true);
                projetos.Add(new(r.ProjectId, r.NomeProjeto, r.Documentos.Select(d => new DocumentoSiglaDiagnostico(d.RowId, d.Codigo,
                    d.ClientDocumentNumber, d.Titulo,
                    d.IsDocument, CoordinationDisciplineResolver.DisciplineKey(d.SiglaDoc3), d.SiglaDoc3, d.Disciplina,
                    d.DisciplinaNorma, d.DisciplinaCanonicaDoc3 ?? d.DisciplinaCanonicaCadastrada,
                    d.DiagnosticoDisciplinaNorma, d.NormaCoordenacaoId, null, IdentificadorOrigem(d), !d.SemConteudoDocumental)).ToArray()));
            }
            return Ok(SiglasSemMapeamentoDiagnostico.Analisar(projetos, await service.AtivasAsync(ct)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (NormalizadorNaoInstaladoException ex) { return StatusCode(503, new { instalado = false, message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Diagnóstico de siglas indisponível. Ref={Ref}", HttpContext.TraceIdentifier);
            return StatusCode(503, new { message = $"Diagnóstico de siglas temporariamente indisponível. (ref: {HttpContext.TraceIdentifier})" });
        }
    }

    private static string IdentificadorOrigem(PmoDocumento documento)
    {
        var partes = new List<string> { $"LDP ID_Registro {documento.SourceRecordId ?? documento.RowId}" };
        if (!string.IsNullOrWhiteSpace(documento.LdpItem)) partes.Add($"Item {documento.LdpItem.Trim()}");
        if (!string.IsNullOrWhiteSpace(documento.ScheduleId)) partes.Add($"Schedule_ID {documento.ScheduleId.Trim()}");
        return string.Join(" · ", partes);
    }
}
