using APS.WebApp.Services.LegacyProjects;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/legacy-projects")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LegacyProjectsController(
    IConfiguration configuration, IPermissionService permissions, IPmoActualFactsProvider actual) : ControllerBase
{
    private readonly LegacyProjectRegistryService legacy = new(configuration);
    private async Task<IActionResult?> Guard(CancellationToken ct)
    {
        var user=await permissions.GetCurrentUserAsync(ct);
        if(user is null)return Unauthorized(new {message="Sessão expirada."});
        if(!user.IsMasterAdmin)return StatusCode(403,new {message="Cadastro de projetos sem LDP exclusivo do MasterAdmin."});
        return null;
    }
    public sealed record PreviewRequest(string? TimesheetProject,string? CrOs,string? Afe,
        int AfeOffset=0,int DocumentOffset=0);

    [HttpGet]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if(await Guard(ct) is {} denied)return denied;
        try{return Ok(await legacy.ListAsync(ct));}
        catch(SqlException ex) when(ex.Number==208){return StatusCode(503,new{message="Aplique a migração audit.LegacyProjectRegistry."});}
    }
    [HttpPost("preview")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest body,CancellationToken ct)
    {
        if(await Guard(ct) is {} denied)return denied;
        try{return Ok(await legacy.PreviewAsync(body.TimesheetProject??"",body.CrOs??"",body.Afe??"",
            body.AfeOffset,body.DocumentOffset,ct));}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(SqlException ex) when(ex.Number==-2){return StatusCode(504,new{message="Consulta ao Timesheet excedeu o tempo limite."});}
    }
    [HttpPost]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Save([FromBody] LegacyProjectRegistryService.LegacyInput input,CancellationToken ct)
    {
        if(await Guard(ct) is {} denied)return denied;
        try
        {
            var user=await permissions.GetCurrentUserAsync(ct);
            return Ok(await legacy.SaveAsync(input,user?.Email??"MasterAdmin",user?.UserId,ct));
        }
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}
        catch(SqlException ex) when(ex.Number is 51011 or 51012 or 51013){return Conflict(new{message=ex.Message});}
        catch(SqlException ex) when(ex.Number==208){return StatusCode(503,new{message="Configuração de projeto histórico indisponível."});}
    }
    /// <summary>Simulação somente leitura: documentos do Timesheet são provisoriamente aceitos.
    /// Retorna disciplinas e reconciliação sem criar nem alterar LDP, HH ou custos.</summary>
    [HttpGet("{projectIdAcc}/simulate")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Simulate(string projectIdAcc,CancellationToken ct)
    {
        if(await Guard(ct) is {} denied)return denied;
        var entry=(await legacy.ListAsync(ct)).FirstOrDefault(x=>
            x.ProjectIdAcc.Equals(projectIdAcc,StringComparison.OrdinalIgnoreCase));
        if(entry is null)return NotFound(new{message="Projeto sem LDP não cadastrado."});
        var facts=await actual.ReadAsync(projectIdAcc,ct);
        var total=facts.Total.Hh;
        var porDisciplina=facts.ByDiscipline.Select(x=>new {
            disciplina=x.Discipline,hh=x.Values.Hh,lancamentos=x.Values.Entries,
            documentos=facts.ProvisionalDocuments.Count(d=>string.Equals(d.Discipline,x.Discipline,StringComparison.OrdinalIgnoreCase))
        }).OrderByDescending(x=>x.hh).ToArray();
        var soma=porDisciplina.Sum(x=>x.hh??0);
        return Ok(new {
            projectIdAcc,totalHh=total,totalLancamentos=facts.Total.Entries,
            totalDocumentos=facts.ProvisionalDocuments.Count,
            disciplinas=porDisciplina,
            hhSemDisciplina=facts.ByDiscipline.Where(d=>d.Discipline=="SEM DISCIPLINA").Sum(d=>d.Values.Hh??0),
            diferencaHh=total.HasValue?total.Value-soma:(decimal?)null,
            somenteSimulacao=true,
            aviso="Documentos aceitos provisoriamente do Timesheet corrigido. HH previstas, status de emissão e custos não podem ser inferidos destes lançamentos."
        });
    }

    [HttpGet("{projectIdAcc}")]
    [RequirePermission("permissions.manage")]
    public async Task<IActionResult> Details(string projectIdAcc,int afeOffset=0,int documentOffset=0,CancellationToken ct=default)
    {
        if(await Guard(ct) is {} denied)return denied;
        var entry=(await legacy.ListAsync(ct)).FirstOrDefault(x=>x.ProjectIdAcc.Equals(projectIdAcc,StringComparison.OrdinalIgnoreCase));
        if(entry is null)return NotFound(new{message="Projeto não cadastrado."});
        try{return Ok(await legacy.PreviewAsync(entry.TimesheetProject,entry.CrOs,entry.Afe,afeOffset,documentOffset,ct));}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(SqlException ex) when(ex.Number==-2){return StatusCode(504,new{message="Consulta ao Timesheet excedeu o tempo limite."});}
    }
}
