using APS.WebApp.Services.Identity;

namespace APS.WebApp.Services.Permissions;

// Identidade do usuário: ICurrentUserAccessor (Services/Identity/AutodeskUserIdentity.cs) — fonte única.

/// <summary>
/// SERVIÇO CENTRAL de permissões. Toda decisão passa por PermissionResolver (mesma regra para attribute,
/// endpoints e /api/permissions/me que alimenta o frontend).
///   • SAFE MODE — Permissions:Enforce = false (padrão até a migração ser aplicada): tudo liberado para quem tem sessão,
///     SEM consultar banco nem Autodesk para decidir; /me responde enforced=false e nunca falha por schema/banco.
///   • Enforce = true com Permissions:MasterAdmins VAZIO: continua em safe mode (proteção contra se trancar para fora).
///   • Enforce = true: NEGA POR PADRÃO. Schema ausente/erro de banco → Unavailable (503), EXCETO MasterAdmin configurado.
///   • MasterAdmin por configuração (e-mail ou userId Autodesk em Permissions:MasterAdmins) é decidido ANTES de qualquer
///     acesso ao banco: acesso total, não depende de perfis/overrides/migração e nenhum DENY o bloqueia.
///     O perfil de sistema MasterAdmin global também dá acesso total (esse depende do banco).
/// </summary>
public sealed class PermissionService(IPermissionRepository repository, INaCurrentUserAccessor naCurrentUser,
    ICurrentUserAccessor currentUser, IConfiguration configuration, IHttpContextAccessor http,
    ILogger<PermissionService> logger) : IPermissionService, IProjectAccessService
{
    private const string ItemDados = "Perm:Dados", ItemUsuario = "Perm:Usuario";

    private bool EnforceConfigurado => configuration.GetValue("Permissions:Enforce", false);

    private string[] MasterAdminsConfigurados() =>
        (configuration.GetSection("Permissions:MasterAdmins").Get<string[]>() ?? Array.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToArray();

    // Homologação controlada: aplicar o mesmo resolvedor real APENAS às sessões locais
    // cujos IDs estejam explicitamente autorizados em Permissions:PilotUserIds.
    // A configuração global Enforce=false continua preservando os demais usuários.
    private bool PilotoAutenticado
    {
        get
        {
            var session = http.HttpContext?.Session;
            // Login N&A usa Auth:UserId; sessões Autodesk antigas possuem o ID
            // interno validado em Perm:UserId após o UPSERT da identidade.
            var userId = session?.GetInt32("Auth:UserId");
            if (userId is null && !string.IsNullOrWhiteSpace(session?.GetString("AccessToken")))
                userId = session?.GetInt32("Perm:UserId");
            if (userId is not int id) return false;
            var pilots = configuration.GetSection("Permissions:PilotUserIds").Get<int[]>() ?? Array.Empty<int>();
            return pilots.Contains(id);
        }
    }

    /// <summary>Enforce global ou piloto por identidade autenticada, sempre com MasterAdmin configurado para recuperação.</summary>
    public bool Enforced
    {
        get
        {
            if (!EnforceConfigurado && !PilotoAutenticado) return false;
            if (MasterAdminsConfigurados().Length > 0) return true;
            logger.LogWarning("Fiscalização não ativada: nenhum MasterAdmin de recuperação configurado.");
            return false;
        }
    }

    private bool MasterPorConfiguracao(string accUserId, string email) =>
        MasterAdminsConfigurados().Any(m =>
            (email.Length > 0 && string.Equals(m, email, StringComparison.OrdinalIgnoreCase)) ||
            (accUserId.Length > 0 && string.Equals(m, accUserId, StringComparison.Ordinal)));

    private static EffectivePermissions Tudo(bool enforced, bool schema, bool master, string? email, string? projectIdAcc)
    {
        var todas = PermissionCatalog.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return new(enforced, schema, master, email, projectIdAcc, todas, todas);
    }

    private async Task<bool> SchemaSemFalharAsync(CancellationToken ct)
    {
        try { return await repository.SchemaInstaladoAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Não foi possível verificar o schema de permissões. Tipo={Tipo}", ex.GetType().Name);
            return false;
        }
    }

    public async Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct)
    {
        var items = http.HttpContext?.Items;
        if (items?[ItemUsuario] is CurrentPermissionUser cache) return cache;

        // Auth:UserId é canônico. Uma sessão N&A nunca faz UPSERT nem chama Autodesk.
        // Se o identificador interno for inválido, não é seguro cair no token legado.
        if (await naCurrentUser.GetAsync(ct) is { } na)
        {
            var interno = await repository.ObterUsuarioAsync(na.UserId, ct);
            if (interno is null)
            {
                logger.LogWarning("Sessão N&A referencia UserId inexistente. UserId={UserId}", na.UserId);
                return null;
            }
            if (!interno.Ativo)
            {
                logger.LogWarning("Sessão N&A referencia usuário inativo. UserId={UserId}", na.UserId);
                return null;
            }

            var accUserId = interno.AccUserId ?? "";
            var nome = string.IsNullOrWhiteSpace(interno.DisplayName) ? na.Nome : interno.DisplayName;
            var chave = $"{accUserId}|{interno.Email.ToLowerInvariant()}";
            var session = http.HttpContext?.Session;
            // Reconciliar explicitamente o cache legado quando ele divergir de Auth:UserId.
            session?.SetInt32("Perm:UserId", interno.UserId);
            session?.SetString("Perm:UserKey", chave);

            var masterInterno = MasterPorConfiguracao(accUserId, interno.Email);
            var dadosInternos = Enforced
                ? await repository.CarregarDadosAsync(interno.UserId, masterInterno, ct)
                : new UserPermissionData(masterInterno, Array.Empty<ProfileAssignment>(),
                    Array.Empty<PermissionOverride>(), new HashSet<int>());
            var usuarioInterno = new CurrentPermissionUser(interno.UserId, accUserId, interno.Email, nome ?? "",
                PermissionResolver.IsMasterAdmin(dadosInternos));
            if (items != null) { items[ItemUsuario] = usuarioInterno; items[ItemDados] = dadosInternos; }
            return usuarioInterno;
        }

        // Compatibilidade temporária para sessões anteriores à Auth:UserId.
        // Sem sessão N&A, a identidade Autodesk só é válida se o token legado existir.
        // Isso impede que um accessor legado, isoladamente, transforme uma requisição anônima
        // em uma sessão autenticada.
        if (string.IsNullOrWhiteSpace(http.HttpContext?.Session.GetString("AccessToken")))
            return null;

        var identidade = await currentUser.GetAsync(ct);
        if (identidade is not { } id) return null;
        int? userId = null;
        var masterPorConfiguracao = MasterPorConfiguracao(id.AutodeskUserId, id.Email);
        if (id.Email.Length == 0)
            logger.LogWarning("UserInfo sem e-mail: usuário não registrado em perm.Users (e-mail é obrigatório na tabela).");
        else
        {
            try
            {
                if (await repository.SchemaInstaladoAsync(ct))
                {
                    // UPSERT em perm.Users (casa por AutodeskUserId, senão por e-mail — case-insensitive), uma vez por sessão.
                    var session = http.HttpContext?.Session;
                    var chave = $"{id.AutodeskUserId}|{id.Email.ToLowerInvariant()}";
                    if (session?.GetString("Perm:UserKey") == chave && session.GetInt32("Perm:UserId") is int cacheado) userId = cacheado;
                    else
                    {
                        userId = await repository.RegistrarAcessoAsync(string.IsNullOrWhiteSpace(id.AutodeskUserId) ? null : id.AutodeskUserId, id.Email, id.Nome, ct);
                        session?.SetInt32("Perm:UserId", userId.Value); session?.SetString("Perm:UserKey", chave);
                    }
                }
            }
            catch when (masterPorConfiguracao)
            {
                // Bootstrap legado de MasterAdmin configurado: mantém acesso mesmo sem banco.
            }
        }
        var dados = masterPorConfiguracao
            ? new UserPermissionData(true, Array.Empty<ProfileAssignment>(), Array.Empty<PermissionOverride>(), new HashSet<int>())
            : userId is int u
                ? await repository.CarregarDadosAsync(u, false, ct)
                : new UserPermissionData(false, Array.Empty<ProfileAssignment>(), Array.Empty<PermissionOverride>(), new HashSet<int>());
        var usuario = new CurrentPermissionUser(userId, id.AutodeskUserId, id.Email, id.Nome, PermissionResolver.IsMasterAdmin(dados));
        if (items != null) { items[ItemUsuario] = usuario; items[ItemDados] = dados; }
        return usuario;
    }

    private async Task<UserPermissionData?> DadosAsync(CancellationToken ct)
    {
        if (await GetCurrentUserAsync(ct) is null) return null;
        return http.HttpContext?.Items[ItemDados] as UserPermissionData;
    }

    public async Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(projectIdAcc) ? null : await repository.ResolverProjetoIdAsync(projectIdAcc, ct);

    private async Task<PermissionCheck> CheckProjectAccessAsync(CurrentPermissionUser usuario, string? projectIdAcc, CancellationToken ct)
    {
        if (usuario.UserId is not int userId || string.IsNullOrWhiteSpace(projectIdAcc))
            return PermissionCheck.Denied;

        return await repository.UsuarioPossuiAcessoAtivoProjetoAsync(userId, projectIdAcc, ct)
            ? PermissionCheck.Allowed
            : PermissionCheck.Denied;
    }

    public async Task<PermissionCheck> CheckProjectAccessAsync(string? projectIdAcc, CancellationToken ct)
    {
        try
        {
            var usuario = await GetCurrentUserAsync(ct);
            return usuario is null
                ? PermissionCheck.Unauthenticated
                : await CheckProjectAccessAsync(usuario, projectIdAcc, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Falha ao verificar gate positivo de projeto. Tipo={Tipo}", ex.GetType().Name);
            return PermissionCheck.Unavailable;
        }
    }

    public async Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false)
    {
        try
        {
            // 1) Bootstrap: MasterAdmin configurado decide ANTES do banco (schema ausente, erro de banco ou DENY não o bloqueiam).
            var usuario = await GetCurrentUserAsync(ct);
            if (usuario is null) return PermissionCheck.Unauthenticated;
            if (!Enforced) return PermissionCheck.Allowed;
            if (!string.IsNullOrWhiteSpace(projectIdAcc))
            {
                var gate = await CheckProjectAccessAsync(usuario, projectIdAcc, ct);
                if (gate != PermissionCheck.Allowed) return gate;
            }
            if (usuario.IsMasterAdmin) return PermissionCheck.Allowed;

            // 2) Demais usuários: banco obrigatório; sem schema = falha fechada.
            if (!await repository.SchemaInstaladoAsync(ct))
            {
                logger.LogError("Permissions:Enforce=true mas o schema perm não está instalado — acesso negado (falha fechada).");
                return PermissionCheck.Unavailable;
            }
            var dados = await DadosAsync(ct);
            if (dados is null) return PermissionCheck.Unauthenticated;
            if (anyProject && string.IsNullOrWhiteSpace(projectIdAcc))
            {
                var qualquer = PermissionResolver.EffectiveAnyProject(dados);
                return anyOfKeys.Any(qualquer.Contains) ? PermissionCheck.Allowed : PermissionCheck.Denied;
            }
            var projetoId = await ResolverProjetoIdAsync(projectIdAcc, ct);
            // Projeto informado mas não cadastrado no N&A Connect: só as permissões globais valem (projetoId null).
            return anyOfKeys.Any(k => PermissionResolver.Decide(dados, k, projetoId).Allowed) ? PermissionCheck.Allowed : PermissionCheck.Denied;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (!Enforced && !string.IsNullOrWhiteSpace(http.HttpContext?.Session.GetString("AccessToken")))
            {
                logger.LogWarning("Safe mode: mantendo compatibilidade da sessão Autodesk após falha de identidade. Tipo={Tipo}", ex.GetType().Name);
                return PermissionCheck.Allowed;
            }
            logger.LogWarning("Falha ao resolver permissões. Tipo={Tipo}", ex.GetType().Name);
            return PermissionCheck.Unavailable;
        }
    }

    public async Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct)
    {
        // A conta Autodesk pode iniciar a sessão antes de Perm:UserId estar populado.
        // Resolver a identidade ANTES de decidir sobre safe mode garante que /me na
        // primeira requisição já fiscalize os usuários-piloto (44, 15 etc.).
        CurrentPermissionUser? usuarioResolvido = null;
        if (!Enforced)
        {
            try { usuarioResolvido = await GetCurrentUserAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("Safe mode: identidade não obtida. Tipo={Tipo}", ex.GetType().Name);
            }
        }
        if (!Enforced)
        {
            // Safe mode somente para não pilotos; sem permissão, o usuário-piloto
            // NUNCA deve receber o catálogo integral por causa da ordem de resolução.
            return Tudo(false, await SchemaSemFalharAsync(ct),
                usuarioResolvido?.IsMasterAdmin == true, usuarioResolvido?.Email, projectIdAcc);
        }

        var usuarioAtual = usuarioResolvido ?? await GetCurrentUserAsync(ct);
        if (usuarioAtual is null) return new(true, false, false, null, projectIdAcc, Array.Empty<string>(), Array.Empty<string>());

        if (!string.IsNullOrWhiteSpace(projectIdAcc))
        {
            var gate = await CheckProjectAccessAsync(usuarioAtual, projectIdAcc, ct);
            if (gate != PermissionCheck.Allowed)
                return new(true, await SchemaSemFalharAsync(ct), usuarioAtual.IsMasterAdmin, usuarioAtual.Email, projectIdAcc,
                    Array.Empty<string>(), Array.Empty<string>());
        }

        if (usuarioAtual.IsMasterAdmin)
            return Tudo(true, await SchemaSemFalharAsync(ct), true, usuarioAtual.Email, projectIdAcc);

        bool schemaOk = await repository.SchemaInstaladoAsync(ct);
        var dados = schemaOk ? await DadosAsync(ct) : null;
        if (dados is null) return new(true, schemaOk, false, usuarioAtual.Email, projectIdAcc, Array.Empty<string>(), Array.Empty<string>());
        var projetoId = await ResolverProjetoIdAsync(projectIdAcc, ct);
        // Projeto informado mas desconhecido: só vale o escopo global.
        return new(true, schemaOk, PermissionResolver.IsMasterAdmin(dados), usuarioAtual.Email, projectIdAcc,
            PermissionResolver.Effective(dados, projetoId).OrderBy(k => k).ToArray(),
            PermissionResolver.EffectiveAnyProject(dados).OrderBy(k => k).ToArray());
    }
}
