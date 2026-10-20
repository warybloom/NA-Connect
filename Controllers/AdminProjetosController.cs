using APS.WebApp.Services.Permissions;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services;

namespace APS.WebApp.Controllers;

/// <summary>
/// Tela de Configuração (ClientApp/src/pages/ControlPanel.tsx, aba "Gestão de Projetos") — expõe
/// somente o que já existe em dbo.Projetos (Is_Ativo, Tipo_LDP, Naming_Standard) e já é consumido pelo
/// Worker. NÃO cobre Normas/Templates/PacotesOCR/Presets LDP do ForgeSync original — esses conceitos não
/// têm tabela no N&A e ficam fora de escopo (ver AUDITORIA_FORGESYNC_NA/15-*.md).
/// </summary>
[ApiController]
[Route("api/admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminProjetosController(DatabaseService dbService, IPermissionService permissions,
    IPermissionRepository permissionRepository, ILogger<AdminProjetosController> logger) : ControllerBase
{
    public sealed class ProjetoAdminDto
    {
        public int ProjetoId { get; set; }
        public string Nome_Projeto { get; set; }
        public string Project_ID_ACC { get; set; }
        public bool Is_Ativo { get; set; }
        public byte TipoLdp { get; set; }
        public string NamingStandard { get; set; }
        public string ProjectLanguage { get; set; }
        public string FileIdLdpAcc { get; set; }
    }

    public sealed class AtualizarConfiguracaoProjetoRequest
    {
        public bool? Is_Ativo { get; set; }
        public byte? TipoLdp { get; set; }
        // string? (não string) — checkpoint de diagnóstico 2026-09-18: com <Nullable>enable</Nullable>
        // no .csproj, o [ApiController] trata propriedade string não anotada como implicitamente
        // [Required] na validação automática de model binding. Isso fazia QUALQUER PATCH que omitisse
        // este campo (ou seja, todo PATCH parcial real da tela — cada handler do frontend envia só o
        // campo que o usuário mudou) falhar com 400 "The NamingStandard field is required." ANTES de
        // chegar no corpo do método, mesmo a intenção sendo update parcial via COALESCE no
        // DatabaseService. Confirmado via teste HTTP real contra o processo em execução (porta 5277,
        // sem sessão — a validação de model binding roda antes do check de sessão).
        public string? NamingStandard { get; set; }
        public string? ProjectLanguage { get; set; }
    }

    [RequirePermission("settings.projects.view", "home.view", "cde.view", "production.view", "robot.view", "planning.coordination.view", "settings.ldp.view", "permissions.manage", AnyProject = true)]
    [HttpGet("projetos")]
    public async Task<IActionResult> Projetos(CancellationToken ct)
    {
        // A sessão N&A local também é válida. A filtragem abaixo nunca retorna
        // projetos sem vínculo positivo para um usuário com fiscalização ativa.
        var usuario = await permissions.GetCurrentUserAsync(ct);
        if (usuario is null) return Unauthorized();

        try
        {
            HashSet<int>? projetosPermitidos = null;
            if (permissions.Enforced && !usuario.IsMasterAdmin)
            {
                if (usuario.UserId is not int interno) return Ok(Array.Empty<ProjetoAdminDto>());
                projetosPermitidos = (await permissionRepository.ListarProjetosPermitidosAsync(interno, ct))
                    .Where(p => p.IsAtivo).Select(p => p.ProjetoId).ToHashSet();
            }
            var dt = dbService.GetTodosProjetosParaAdmin();
            var resultado = new List<ProjetoAdminDto>();
            foreach (DataRow row in dt.Rows)
            {
                if (projetosPermitidos is not null
                    && !projetosPermitidos.Contains(Convert.ToInt32(row["ProjetoID"]))) continue;
                resultado.Add(new ProjetoAdminDto
                {
                    ProjetoId = Convert.ToInt32(row["ProjetoID"]),
                    Nome_Projeto = row["Nome_Projeto"] as string,
                    Project_ID_ACC = row["Project_ID_ACC"] as string,
                    Is_Ativo = row["Is_Ativo"] != DBNull.Value && Convert.ToBoolean(row["Is_Ativo"]),
                    TipoLdp = row["Tipo_LDP"] == DBNull.Value ? (byte)1 : Convert.ToByte(row["Tipo_LDP"]),
                    NamingStandard = row["Naming_Standard"] as string,
                    ProjectLanguage = row["Project_Language"] as string,
                    FileIdLdpAcc = row["File_ID_LDP_ACC"] as string,
                });
            }
            logger.LogInformation("[ADMIN_PROJETOS][GET][RESULTADO] Count={Count}", resultado.Count);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ADMIN_PROJETOS][GET][ERRO] Falha ao listar projetos para a tela de Configuração.");
            return StatusCode(500, new { message = "Erro ao carregar projetos." });
        }
    }

    [RequirePermission("settings.projects.edit")]
    [HttpPatch("projeto/{id:int}")]
    public IActionResult AtualizarConfiguracao(int id, [FromBody] AtualizarConfiguracaoProjetoRequest body)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized(new { message = "Sessão expirada." });

        if (body?.NamingStandard is { Length: > 0 } ns && ns != "NA" && ns != "CLIENT")
            return BadRequest(new { message = "Naming_Standard inválido. Use 'NA' ou 'CLIENT'." });
        if (body?.TipoLdp is { } tl && tl != 1 && tl != 2)
            return BadRequest(new { message = "Tipo_LDP inválido. Use 1 (Tipo1) ou 2 (Tipo2)." });
        // Valores auditados em produção (17/09/2026): só "EN" e "PT" existem hoje em dbo.Projetos —
        // nenhuma outra opção foi inventada.
        if (body?.ProjectLanguage is { Length: > 0 } pl && pl != "EN" && pl != "PT")
            return BadRequest(new { message = "Project_Language inválido. Use 'EN' ou 'PT'." });

        try
        {
            dbService.AtualizarConfiguracaoProjeto(id, body?.Is_Ativo, body?.TipoLdp, body?.NamingStandard, body?.ProjectLanguage);
            return Ok(new { message = "Configuração atualizada." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao atualizar configuração do projeto {ProjetoId}.", id);
            return StatusCode(500, new { message = "Erro ao salvar configuração." });
        }
    }
}
