using System.Text.Json;
using APS.WebApp.Services.Identity;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Permissions.Bulk;
using APS.WebApp.Services.Permissions.Sync;
using Microsoft.AspNetCore.Mvc;

namespace APS.WebApp.Controllers;

public sealed record PerfilRequest(string Name, string? Description, bool Ativo = true, IReadOnlyList<string>? Permissions = null);
public sealed record DuplicarPerfilRequest(string Name);
public sealed record PermissoesRequest(IReadOnlyList<string> Permissions);
public sealed record UsuarioRequest(string Email, string? Name);
public sealed record PerfisUsuarioRequest(IReadOnlyList<int> ProfileIds);
public sealed record OverridesRequest(IReadOnlyList<PermissionOverrideDto> Overrides);
public sealed record PermissionOverrideDto(string PermissionKey, string Effect);
public sealed record AtivoRequest(bool Ativo);
public sealed record DefinirSenhaRequest(string? Password, bool MustChangePassword = false);

/// <summary>
/// Permissões: /me (efetivas já resolvidas para o frontend) e administração (perfis, usuários, overrides, auditoria).
/// A administração exige permissions.manage (validado no backend) e nunca permite alterar o PRÓPRIO acesso.
/// </summary>
[ApiController]
[Route("api/permissions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PermissionsController(IPermissionService service, IPermissionRepository repository, ILogger<PermissionsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("me")]
    [PermissionExempt("Retorna as permissões EFETIVAS do próprio usuário (já resolvidas); não expõe dados de terceiros.")]
    public async Task<IActionResult> Me([FromQuery] string? projectId, CancellationToken ct)
    {
        // Sessão = identidade N&A (Auth:UserId, login N&A ou Autodesk) OU token Autodesk legado. Exigir só AccessToken
        // devolvia 401 a todo usuário N&A (o PermissionService já resolve a identidade interna).
        if (!TemSessao()) return Unauthorized(new { message = "Sessão expirada." });
        try { return Ok(await service.GetEffectiveAsync(string.IsNullOrWhiteSpace(projectId) ? null : projectId, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha em /api/permissions/me. Tipo={Tipo}", ex.GetType().Name);
            return StatusCode(503, new { message = "Não foi possível carregar as permissões." });
        }
    }

    [HttpGet("catalog")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> Catalogo(CancellationToken ct) =>
        Admin(_ => Task.FromResult<IActionResult>(Ok(PermissionCatalog.Todos)), ct);

    // ------------------------------------------------------------------------------ perfis ----
    [HttpGet("profiles")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> Perfis(CancellationToken ct) => Admin(async _ => Ok(await repository.ListarPerfisAsync(ct)), ct);

    [HttpPost("profiles")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> CriarPerfil([FromBody] PerfilRequest req, CancellationToken ct) => Admin(async eu =>
    {
        if (Invalido(req.Name, 100) || req.Description is { Length: > 500 }) return BadRequest(new { message = "Nome/descrição inválidos." });
        var keys = req.Permissions ?? Array.Empty<string>();
        if (keys.Any(k => !PermissionCatalog.Existe(k))) return BadRequest(new { message = "Permissão fora do catálogo." });
        if ((await repository.ListarPerfisAsync(ct)).Any(p => string.Equals(p.Name, req.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "Já existe um perfil com esse nome." });
        int id = await repository.CriarPerfilAsync(req.Name.Trim(), req.Description, keys, ct);
        await Auditar(eu, null, null, "PROFILE", $"profile:{id}", null, new { req.Name, keys }, ct);
        return Ok(new { profileId = id });
    }, ct);

    [HttpPost("profiles/{profileId:int}/duplicate")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> DuplicarPerfil(int profileId, [FromBody] DuplicarPerfilRequest req, CancellationToken ct) => Admin(async eu =>
    {
        if (Invalido(req.Name, 100)) return BadRequest(new { message = "Nome inválido." });
        var perfis = await repository.ListarPerfisAsync(ct);
        var origem = perfis.FirstOrDefault(p => p.ProfileId == profileId);
        if (origem is null) return NotFound(new { message = "Perfil inexistente." });
        if (origem.IsMasterAdmin) return StatusCode(403, new { message = "O perfil MasterAdmin não pode ser duplicado." });
        if (perfis.Any(p => string.Equals(p.Name, req.Name.Trim(), StringComparison.OrdinalIgnoreCase))) return Conflict(new { message = "Já existe um perfil com esse nome." });
        int id = await repository.CriarPerfilAsync(req.Name.Trim(), origem.Description, origem.Permissions, ct);
        await Auditar(eu, null, null, "PROFILE", $"profile:{id}", null, new { duplicadoDe = profileId, req.Name }, ct);
        return Ok(new { profileId = id });
    }, ct);

    [HttpPut("profiles/{profileId:int}")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> AtualizarPerfil(int profileId, [FromBody] PerfilRequest req, CancellationToken ct) => Admin(async eu =>
    {
        if (Invalido(req.Name, 100) || req.Description is { Length: > 500 }) return BadRequest(new { message = "Nome/descrição inválidos." });
        var perfis = await repository.ListarPerfisAsync(ct);
        var perfil = perfis.FirstOrDefault(p => p.ProfileId == profileId);
        if (perfil is null) return NotFound(new { message = "Perfil inexistente." });
        if (perfil.IsSystem) return StatusCode(403, new { message = "Perfil de sistema é protegido." });
        if (perfis.Any(p => p.ProfileId != profileId && string.Equals(p.Name, req.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "Já existe um perfil com esse nome." });
        await repository.AtualizarPerfilAsync(profileId, req.Name.Trim(), req.Description, req.Ativo, ct);
        await Auditar(eu, null, null, "PROFILE", $"profile:{profileId}", new { perfil.Name, perfil.Description, perfil.Ativo }, new { req.Name, req.Description, req.Ativo }, ct);
        return Ok();
    }, ct);

    [HttpDelete("profiles/{profileId:int}")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> ExcluirPerfil(int profileId, CancellationToken ct) => Admin(async eu =>
    {
        // Exclusao e uma operacao administrativa destrutiva: somente MasterAdmin, inclusive em safe mode.
        if (!eu.IsMasterAdmin)
            return StatusCode(403, new { message = "Somente MasterAdmin pode excluir perfis." });

        var perfil = (await repository.ListarPerfisAsync(ct)).FirstOrDefault(p => p.ProfileId == profileId);
        if (perfil is null) return NotFound(new { message = "Perfil inexistente." });
        if (perfil.IsSystem)
            return StatusCode(403, new { message = "Perfil de sistema e protegido e nao pode ser excluido." });

        await repository.ExcluirPerfilAsync(profileId, ct);
        await Auditar(eu, null, null, "PROFILE_DELETED", $"profile:{profileId}",
            new { perfil.ProfileId, perfil.Name, perfil.Description, perfil.Ativo, perfil.Permissions }, null, ct);
        return NoContent();
    }, ct);
    [HttpPut("profiles/{profileId:int}/permissions")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> PermissoesPerfil(int profileId, [FromBody] PermissoesRequest req, CancellationToken ct) => Admin(async eu =>
    {
        if (req.Permissions.Any(k => !PermissionCatalog.Existe(k))) return BadRequest(new { message = "Permissão fora do catálogo." });
        var perfil = (await repository.ListarPerfisAsync(ct)).FirstOrDefault(p => p.ProfileId == profileId);
        if (perfil is null) return NotFound(new { message = "Perfil inexistente." });
        if (perfil.IsSystem) return StatusCode(403, new { message = "Perfil de sistema é protegido." });
        // Quem gerencia não pode ampliar/reduzir um perfil que ele mesmo usa (evita autoconcessão indireta).
        if (!eu.IsMasterAdmin && eu.UserId is int meuId && (await repository.CarregarDadosAsync(meuId, false, ct)).Assignments.Any(a => a.ProfileId == profileId))
            return StatusCode(403, new { message = "Você não pode alterar um perfil atribuído a você." });
        await repository.DefinirPermissoesPerfilAsync(profileId, req.Permissions, ct);
        await Auditar(eu, null, null, "PROFILE_PERMISSION", $"profile:{profileId}", perfil.Permissions, req.Permissions, ct);
        return Ok();
    }, ct);

    // ----------------------------------------------------------------------------- usuários ----
    [HttpGet("users")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> Usuarios(CancellationToken ct) => Admin(async _ => Ok(await repository.ListarUsuariosAsync(ct)), ct);

    [HttpPost("users")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> CadastrarUsuario([FromBody] UsuarioRequest req, CancellationToken ct) => Admin(async eu =>
    {
        // Criar conta exige permissions.manage de fato, mesmo com Permissions:Enforce=false (o login N&A local torna
        // o usuário criado aqui uma conta real). O usuário nasce sem AccUserId e sem projetos.
        if (!await GerenciaPermissoesEstritoAsync(eu, ct))
            return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = new[] { "permissions.manage" } });
        if (Invalido(req.Email, 256) || !req.Email.Contains('@') || req.Name is { Length: > 200 }) return BadRequest(new { message = "E-mail inválido." });
        int id = await repository.CadastrarUsuarioAsync(req.Email.Trim(), req.Name, ct);
        await Auditar(eu, id, null, "USER", req.Email.Trim(), null, new { req.Email, req.Name }, ct);
        return Ok(new { userId = id });
    }, ct);

    /// <summary>
    /// Define/redefine a senha do login N&A local de um usuário EXISTENTE (não cria usuário; o hash nunca é devolvido).
    /// Exige permissions.manage de forma ESTRITA: com Permissions:Enforce=false o [RequirePermission] libera qualquer
    /// sessão, o que aqui permitiria tomar a conta de outro usuário.
    /// </summary>
    [HttpPut("users/{userId:int}/password")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> DefinirSenha(int userId, [FromBody] DefinirSenhaRequest req, [FromServices] INaLocalAuthService localAuth,
        CancellationToken ct) => AdminSobre(userId, async eu =>
    {
        if (!await GerenciaPermissoesEstritoAsync(eu, ct))
            return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = new[] { "permissions.manage" } });
        var resultado = await localAuth.SetPasswordAsync(userId, req?.Password, req?.MustChangePassword ?? false, ct);
        switch (resultado.Status)
        {
            case NaSetPasswordStatus.InvalidPassword: return BadRequest(new { message = resultado.Message });
            case NaSetPasswordStatus.UserNotFound: return NotFound(new { message = "Registro inexistente." });
            case NaSetPasswordStatus.Unavailable: return StatusCode(503, new { message = "Operação indisponível." });
        }
        await Auditar(eu, userId, null, "USER_PASSWORD", $"user:{userId}", null, new { mustChangePassword = req!.MustChangePassword }, ct);
        return NoContent();
    }, ct);

    /// <summary>Independe de Permissions:Enforce: MasterAdmin (config) ou permissions.manage efetivo global.</summary>
    private async Task<bool> GerenciaPermissoesEstritoAsync(CurrentPermissionUser eu, CancellationToken ct)
    {
        if (eu.IsMasterAdmin) return true;
        if (eu.UserId is not int id) return false;
        var dados = await repository.CarregarDadosAsync(id, false, ct);
        // Em modo de compatibilidade, IsMasterAdmin do request pode estar falso;
        // considerar o perfil MasterAdmin global real armazenado no banco.
        return PermissionResolver.IsMasterAdmin(dados)
            || PermissionResolver.Decide(dados, "permissions.manage", null).Allowed;
    }

    /// <summary>Vínculo do usuário no projeto (projectId ACC) ou global (sem projectId) + resolução key a key para a tela.</summary>
    [HttpGet("users/{userId:int}/access")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> Acesso(int userId, [FromQuery] string? projectId, CancellationToken ct) => Admin(async _ =>
    {
        var escopo = await Escopo(projectId, ct);
        if (escopo.Erro != null) return escopo.Erro;
        var vinculo = await repository.ObterUsuarioNoProjetoAsync(userId, escopo.ProjetoId, ct);
        var dados = await repository.CarregarDadosAsync(userId, false, ct);
        var perfis = await repository.ListarPerfisAsync(ct);
        var nomes = perfis.ToDictionary(p => p.ProfileId, p => p.Name);
        var chaves = PermissionCatalog.Todos.Select(r =>
        {
            var herdadoDe = dados.Assignments.Where(a => (a.ProjetoId is null || a.ProjetoId == escopo.ProjetoId) && a.Permissions.Contains(r.Key))
                .Select(a => nomes.TryGetValue(a.ProfileId, out var n) ? n : a.ProfileName).Distinct().ToArray();
            var decisao = PermissionResolver.Decide(dados, r.Key, escopo.ProjetoId);
            var ov = vinculo.Overrides.FirstOrDefault(o => o.PermissionKey == r.Key)?.Effect;
            return new { r.Key, herdadoDe, overrideEfetivo = ov, efetivo = decisao.Allowed, origem = decisao.Source.ToString() };
        }).ToList();
        return Ok(new { vinculo.User, vinculo.AtivoNoProjeto, vinculo.ProfileIds, vinculo.Overrides, projetoId = escopo.ProjetoId,
            isMasterAdmin = PermissionResolver.IsMasterAdmin(dados), permissoes = chaves });
    }, ct);

    [HttpPut("users/{userId:int}/profiles")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> PerfisUsuario(int userId, [FromQuery] string? projectId, [FromBody] PerfisUsuarioRequest req, CancellationToken ct) => AdminSobre(userId, async eu =>
    {
        var escopo = await Escopo(projectId, ct);
        if (escopo.Erro != null) return escopo.Erro;
        var perfis = await repository.ListarPerfisAsync(ct);
        if (req.ProfileIds.Any(id => perfis.All(p => p.ProfileId != id))) return BadRequest(new { message = "Perfil inexistente." });
        if (!eu.IsMasterAdmin && req.ProfileIds.Any(id => perfis.First(p => p.ProfileId == id).IsMasterAdmin))
            return StatusCode(403, new { message = "Somente MasterAdmin pode atribuir o perfil MasterAdmin." });
        if (escopo.ProjetoId is not null && req.ProfileIds.Any(id => perfis.First(p => p.ProfileId == id).IsMasterAdmin))
            return BadRequest(new { message = "MasterAdmin só pode ser atribuído globalmente." });
        var antes = await repository.ObterUsuarioNoProjetoAsync(userId, escopo.ProjetoId, ct);
        await repository.DefinirPerfisUsuarioAsync(userId, escopo.ProjetoId, req.ProfileIds, ct);
        await Auditar(eu, userId, escopo.ProjetoId, PermissionAuditAlvos.ProfileChanged, projectId ?? "GLOBAL", antes.ProfileIds, req.ProfileIds, ct);
        return Ok();
    }, ct);

    [HttpPut("users/{userId:int}/overrides")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> OverridesUsuario(int userId, [FromQuery] string? projectId, [FromBody] OverridesRequest req, CancellationToken ct) => AdminSobre(userId, async eu =>
    {
        var escopo = await Escopo(projectId, ct);
        if (escopo.Erro != null) return escopo.Erro;
        if (req.Overrides.Any(o => !PermissionCatalog.Existe(o.PermissionKey) || o.Effect is not (PermissionEffects.Allow or PermissionEffects.Deny)))
            return BadRequest(new { message = "Override inválido (key fora do catálogo ou efeito diferente de ALLOW/DENY)." });
        if (!eu.IsMasterAdmin && req.Overrides.Any(o => o.PermissionKey == "permissions.manage" && o.Effect == PermissionEffects.Allow))
            return StatusCode(403, new { message = "Somente MasterAdmin concede permissions.manage individualmente." });
        var antes = await repository.ObterUsuarioNoProjetoAsync(userId, escopo.ProjetoId, ct);
        var novos = req.Overrides.Select(o => new PermissionOverride(escopo.ProjetoId, o.PermissionKey, o.Effect)).ToList();
        await repository.DefinirOverridesUsuarioAsync(userId, escopo.ProjetoId, novos, ct);
        await Auditar(eu, userId, escopo.ProjetoId, PermissionAuditAlvos.OverrideChanged, projectId ?? "GLOBAL", antes.Overrides, novos, ct);
        return Ok();
    }, ct);

    [HttpPut("users/{userId:int}/active")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> AtivoNoProjeto(int userId, [FromQuery] string projectId, [FromBody] AtivoRequest req, CancellationToken ct) => AdminSobre(userId, async eu =>
    {
        var escopo = await Escopo(projectId, ct);
        if (escopo.Erro != null) return escopo.Erro;
        if (escopo.ProjetoId is not int p) return BadRequest(new { message = "Projeto obrigatório." });
        await repository.DefinirAtivoNoProjetoAsync(userId, p, req.Ativo, ct);
        // Referência ADMIN: a sincronização ACC não reativa um vínculo desativado manualmente pelo administrador.
        await Auditar(eu, userId, p, req.Ativo ? PermissionAuditAlvos.ProjectReactivated : PermissionAuditAlvos.ProjectDeactivated,
            PermissionAuditAlvos.OrigemAdmin, new { ativo = !req.Ativo }, new { ativo = req.Ativo }, ct);
        return Ok();
    }, ct);

    // ------------------------------------------------------------ membros do projeto (ACC) ----
    /// <summary>Usuários vinculados ao projeto (via sincronização ACC ou configuração), com perfis e exceções do projeto.</summary>
    [HttpGet("projects/{projectIdAcc}/members")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> MembrosDoProjeto(string projectIdAcc, [FromServices] IPermissionSyncRepository sync, CancellationToken ct) => Admin(async _ =>
    {
        var projeto = await ProjetoNaAsync(sync, projectIdAcc, ct);
        if (projeto is null) return NotFound(new { message = "Projeto não cadastrado no N&A Connect." });
        return Ok(new { projeto, ultimaSincronizacao = await sync.UltimoResumoAsync(projeto.ProjetoId, ct), membros = await sync.MembrosAsync(projeto.ProjetoId, ct) });
    }, ct);

    /// <summary>
    /// Sincroniza os membros ACC do projeto (usa o token 3-legged de quem clicou). Falha Autodesk → 502 e NADA é alterado.
    /// </summary>
    [HttpPost("projects/{projectIdAcc}/sync-acc")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> SincronizarProjeto(string projectIdAcc, [FromServices] IPermissionSyncRepository sync,
        [FromServices] PermissionSyncService sincronizador, CancellationToken ct) => Admin(async eu =>
    {
        var projeto = await ProjetoNaAsync(sync, projectIdAcc, ct);
        if (projeto is null) return NotFound(new { message = "Projeto não cadastrado no N&A Connect." });
        if (!projeto.Ativo) return BadRequest(new { message = "Projeto inativo no N&A Connect: não entra no sistema de permissões." });
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized(new { message = "Sessão expirada." });
        try
        {
            return Ok(await sincronizador.SincronizarProjetoAsync(projeto, token, eu.UserId, eu.Email, ct));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Sincronização ACC do projeto {ProjetoId} falhou. Status={Status}", projeto.ProjetoId, (int?)ex.StatusCode);
            var msg = ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized
                ? "A Autodesk recusou a listagem de membros deste projeto (é preciso ser administrador do projeto/conta no ACC). Nada foi alterado."
                : $"Falha ao consultar a Autodesk (HTTP {(int?)ex.StatusCode}). Nada foi alterado.";
            return StatusCode(502, new { message = msg });
        }
    }, ct);

    private static async Task<ProjetoNa?> ProjetoNaAsync(IPermissionSyncRepository sync, string projectIdAcc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectIdAcc) || projectIdAcc.Length > 100) return null;
        var acc = projectIdAcc.StartsWith("b.", StringComparison.Ordinal) ? projectIdAcc : "b." + projectIdAcc;
        return (await sync.ProjetosNaAsync(ct)).FirstOrDefault(p => string.Equals(p.ProjectIdAcc, acc, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------------ operações em lote ----
    // Prévia e aplicação usam o mesmo cálculo; a aplicação recalcula no servidor e grava tudo numa transação.
    [HttpPost("bulk/profiles/preview")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> LotePerfisPrevia([FromBody] BulkPerfisRequest req, [FromServices] BulkPermissionService lote, CancellationToken ct) =>
        Lote(async eu => Ok(await lote.PreverPerfisAsync(req, eu.UserId, ct)), ct);

    [HttpPost("bulk/profiles/apply")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> LotePerfisAplicar([FromBody] BulkPerfisRequest req, [FromServices] BulkPermissionService lote, CancellationToken ct) =>
        Lote(async eu => Resultado(await lote.AplicarPerfisAsync(req, eu.UserId, eu.Email, ct)), ct);

    [HttpPost("bulk/copy-access/preview")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> LoteCopiaPrevia([FromBody] BulkCopiaRequest req, [FromServices] BulkPermissionService lote, CancellationToken ct) =>
        Lote(async eu => Ok(await lote.PreverCopiaAsync(req, eu.UserId, ct)), ct);

    [HttpPost("bulk/copy-access/apply")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> LoteCopiaAplicar([FromBody] BulkCopiaRequest req, [FromServices] BulkPermissionService lote, CancellationToken ct) =>
        Lote(async eu => Resultado(await lote.AplicarCopiaAsync(req, eu.UserId, eu.Email, ct)), ct);

    private Task<IActionResult> Lote(Func<CurrentPermissionUser, Task<IActionResult>> acao, CancellationToken ct) => Admin(async eu =>
    {
        // O lote CRIA/REATIVA vínculos (UserProjects) e atribui perfis: exige permissions.manage de fato, mesmo com
        // Permissions:Enforce=false (o [RequirePermission] libera qualquer sessão nesse modo).
        if (!await GerenciaPermissoesEstritoAsync(eu, ct))
            return StatusCode(403, new { message = "Você não tem permissão para esta funcionalidade.", permission = new[] { "permissions.manage" } });
        try { return await acao(eu); }
        catch (BulkValidationException ex) { return BadRequest(new { message = ex.Message }); }
    }, ct);

    /// <summary>Falha na gravação: 500 com o resumo (nada gravado) — o erro nunca é engolido.</summary>
    private IActionResult Resultado(BulkResultado r)
    {
        if (!r.Sucesso) logger.LogWarning("Operação em lote de permissões falhou e foi desfeita: {Erro}", r.Erro);
        return r.Sucesso ? Ok(r) : StatusCode(500, r);
    }

    [HttpGet("audit")]
    [RequirePermission("permissions.manage")]
    public Task<IActionResult> Auditoria([FromQuery] int top = 200, CancellationToken ct = default) =>
        Admin(async _ => Ok(await repository.LerAuditoriaAsync(Math.Clamp(top, 1, 1000), ct)), ct);

    // -------------------------------------------------------------------------------- apoio ----
    private async Task<IActionResult> Admin(Func<CurrentPermissionUser, Task<IActionResult>> acao, CancellationToken ct)
    {
        try
        {
            if (!await repository.SchemaInstaladoAsync(ct))
                return StatusCode(503, new { message = "Sistema de permissões não instalado: aplique Migrations/Permissions/20260929_001_Permissions.sql." });
            var eu = await service.GetCurrentUserAsync(ct);
            if (eu is null) return Unauthorized(new { message = "Sessão expirada." });
            // Gate ESTRITO mesmo durante a migração Enforce=false:
            // não deixar nenhuma operação administrativa acessível a qualquer sessão.
            if (!await GerenciaPermissoesEstritoAsync(eu, ct))
                return NotFound();
            return await acao(eu);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AutodeskUserInfoException ex)
        {
            logger.LogWarning("Administração de permissões: usuário Autodesk não resolvido. Status={Status}", ex.StatusCode);
            return ex.ExigeNovoLogin
                ? StatusCode(401, new { message = AutodeskUserInfoException.MensagemNovoLogin, reloginRequired = true })
                : StatusCode(503, new { message = $"Não foi possível identificar o usuário na Autodesk (HTTP {ex.StatusCode})." });
        }
        catch (KeyNotFoundException) { return NotFound(new { message = "Registro inexistente." }); }
        catch (Exception ex)
        {
            logger.LogWarning("Falha na administração de permissões. Tipo={Tipo}", ex.GetType().Name);
            return StatusCode(503, new { message = "Operação de permissões indisponível." });
        }
    }

    /// <summary>Alteração sobre um usuário: bloqueia alterar o PRÓPRIO acesso (inclusive MasterAdmin por perfil).</summary>
    private Task<IActionResult> AdminSobre(int alvoUserId, Func<CurrentPermissionUser, Task<IActionResult>> acao, CancellationToken ct) =>
        Admin(async eu => eu.UserId == alvoUserId
            ? StatusCode(403, new { message = "Você não pode alterar o seu próprio acesso." })
            : await acao(eu), ct);

    private async Task<(int? ProjetoId, IActionResult? Erro)> Escopo(string? projectIdAcc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectIdAcc)) return (null, null);
        if (projectIdAcc.Length > 100) return (null, BadRequest(new { message = "Projeto inválido." }));
        var id = await service.ResolverProjetoIdAsync(projectIdAcc, ct);
        return id is null ? (null, NotFound(new { message = "Projeto não cadastrado no N&A Connect." })) : (id, null);
    }

    private Task Auditar(CurrentPermissionUser eu, int? afetado, int? projetoId, string alvo, string? referencia, object? antes, object? depois, CancellationToken ct) =>
        repository.AuditarAsync(eu.UserId, eu.Email, afetado, projetoId, alvo, referencia,
            antes is null ? null : JsonSerializer.Serialize(antes, Json), depois is null ? null : JsonSerializer.Serialize(depois, Json), ct);

    private bool TemSessao()
    {
        try { if (HttpContext.Session.GetInt32(NaSessionKeys.UserId) is > 0) return true; }
        catch (ArgumentException) { /* bytes inválidos: tratar como ausente */ }
        return !string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken"));
    }

    private static bool Invalido(string? s, int max) => string.IsNullOrWhiteSpace(s) || s.Trim().Length > max;
}
