using APS.WebApp.Services.Permissions.Sync;

namespace APS.WebApp.Services.Permissions.Bulk;

public sealed record BulkPerfisRequest(IReadOnlyList<int> UserIds, IReadOnlyList<string> ProjectIdsAcc, IReadOnlyList<int> ProfileIds, string Mode);

public sealed record BulkCopiaRequest(int UserId, string SourceProjectIdAcc, IReadOnlyList<string> TargetProjectIdsAcc,
    bool CopyProfiles, bool CopyOverrides, string Mode);

public sealed record BulkItem(int UserId, string Usuario, string Email, int ProjetoId, string ProjectIdAcc, string Projeto, string Status, string? Motivo,
    IReadOnlyList<string> PerfisAntes, IReadOnlyList<string> PerfisAdicionados, IReadOnlyList<string> PerfisRemovidos,
    int OverridesGravados, int OverridesRemovidos, string? Vinculo = null);

public sealed record BulkPreview(string Operacao, string Modo, int Usuarios, int Projetos, int Combinacoes, int Alterar, int JaPossui, int Ignoradas,
    IReadOnlyList<string> Perfis, bool CopiaPerfis, bool CopiaOverrides, IReadOnlyList<BulkItem> Itens, IReadOnlyList<string> Avisos);

public sealed record BulkResultado(bool Sucesso, int Alterados, int JaCorretos, int Ignorados, int Falhas, string? Erro, BulkPreview Plano,
    int VinculosCriados = 0, int VinculosReativados = 0, int PerfisAdicionados = 0);

/// <summary>Requisição de lote inválida (vira 400 com a mensagem).</summary>
public sealed class BulkValidationException(string message) : Exception(message);

/// <summary>
/// Operações administrativas EM LOTE sobre UserProjectProfiles / UserPermissionOverrides (mesma arquitetura, mesmas tabelas).
/// Prévia e aplicação usam o MESMO cálculo (BulkPermissionPlanner); a aplicação recalcula no servidor — nunca confia na
/// prévia enviada pelo cliente — e grava tudo numa transação única.
/// </summary>
public sealed class BulkPermissionService(IBulkPermissionRepository repo, IPermissionRepository permissions, IPermissionSyncRepository projetos)
{
    public const int MaxUsuarios = 500, MaxProjetos = 100, MaxCombinacoes = 5000;
    public const string OpPerfisAdicionar = "BULK_PROFILE_ASSIGNED", OpPerfisSubstituir = "BULK_PROFILE_REPLACED", OpCopia = "BULK_PROJECT_ACCESS_COPIED";

    private static BulkModo Modo(string? mode) => (mode ?? "").Trim().ToUpperInvariant() switch
    {
        "ADD" or "ADICIONAR" => BulkModo.Adicionar,
        "REPLACE" or "SUBSTITUIR" => BulkModo.Substituir,
        _ => throw new BulkValidationException("Modo inválido: use ADD ou REPLACE."),
    };

    private sealed record Contexto(string Operacao, BulkModo Modo, IReadOnlyList<ProjetoNa> Projetos, IReadOnlyList<int> UserIds,
        IReadOnlyDictionary<int, PermProfile> Perfis, IReadOnlyList<int> PerfisAlvo, bool CopiaPerfis, bool CopiaOverrides, List<string> Avisos,
        Func<EstadoLote, int, int, (IReadOnlySet<int>? Perfis, IReadOnlyDictionary<string, string>? Overrides)> Alvo, object Origem);

    private async Task<IReadOnlyList<ProjetoNa>> ResolverProjetosAsync(IEnumerable<string> accIds, CancellationToken ct)
    {
        var todos = (await projetos.ProjetosNaAsync(ct)).Where(p => p.Ativo)
            .GroupBy(p => p.ProjectIdAcc, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var lista = new List<ProjetoNa>();
        foreach (var id in accIds.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.StartsWith("b.", StringComparison.Ordinal) ? a : "b." + a).Distinct(StringComparer.OrdinalIgnoreCase))
            lista.Add(todos.TryGetValue(id, out var p) ? p : throw new BulkValidationException($"Projeto {id} não cadastrado/ativo no N&A Connect."));
        return lista;
    }

    private async Task<IReadOnlyDictionary<int, PermProfile>> PerfisAsync(CancellationToken ct) =>
        (await permissions.ListarPerfisAsync(ct)).ToDictionary(p => p.ProfileId);

    private static void ValidarPerfis(IEnumerable<int> ids, IReadOnlyDictionary<int, PermProfile> perfis)
    {
        foreach (var id in ids)
        {
            if (!perfis.TryGetValue(id, out var p)) throw new BulkValidationException($"Perfil {id} inexistente.");
            if (p.IsMasterAdmin) throw new BulkValidationException("MasterAdmin não pode ser atribuído em lote nem por projeto (é global e protegido).");
            if (!p.Ativo) throw new BulkValidationException($"Perfil '{p.Name}' está inativo.");
        }
    }

    // ------------------------------------------------------------------------ perfis em lote ----
    private async Task<Contexto> ContextoPerfisAsync(BulkPerfisRequest req, CancellationToken ct)
    {
        var modo = Modo(req.Mode);
        var users = (req.UserIds ?? Array.Empty<int>()).Distinct().ToList();
        if (users.Count == 0) throw new BulkValidationException("Selecione ao menos um usuário.");
        if (users.Count > MaxUsuarios) throw new BulkValidationException($"Máximo de {MaxUsuarios} usuários por operação.");
        var projs = await ResolverProjetosAsync(req.ProjectIdsAcc ?? Array.Empty<string>(), ct);
        if (projs.Count == 0) throw new BulkValidationException("Selecione ao menos um projeto.");
        if (projs.Count > MaxProjetos) throw new BulkValidationException($"Máximo de {MaxProjetos} projetos por operação.");
        if (users.Count * projs.Count > MaxCombinacoes) throw new BulkValidationException($"Máximo de {MaxCombinacoes} combinações por operação.");
        var perfis = await PerfisAsync(ct);
        var alvo = (req.ProfileIds ?? Array.Empty<int>()).Distinct().ToList();
        if (alvo.Count == 0 && modo == BulkModo.Adicionar) throw new BulkValidationException("Selecione ao menos um perfil para adicionar.");
        ValidarPerfis(alvo, perfis);
        var avisos = new List<string>();
        if (modo == BulkModo.Substituir)
            avisos.Add(alvo.Count == 0
                ? "SUBSTITUIR sem perfis remove TODOS os perfis destes usuários nos projetos selecionados."
                : "SUBSTITUIR remove os perfis atuais destes usuários nos projetos selecionados que não estiverem marcados.");
        var alvoSet = alvo.ToHashSet();
        return new Contexto(modo == BulkModo.Adicionar ? OpPerfisAdicionar : OpPerfisSubstituir, modo, projs, users, perfis, alvo, true, false, avisos,
            (_, _, _) => (alvoSet, null), new { });
    }

    public async Task<BulkPreview> PreverPerfisAsync(BulkPerfisRequest req, int? adminUserId, CancellationToken ct) =>
        (await PlanejarAsync(await ContextoPerfisAsync(req, ct), adminUserId, ct)).Preview;

    public async Task<BulkResultado> AplicarPerfisAsync(BulkPerfisRequest req, int? adminUserId, string? adminEmail, CancellationToken ct) =>
        await AplicarAsync(await ContextoPerfisAsync(req, ct), adminUserId, adminEmail, ct);

    // ------------------------------------------------------------- copiar acesso entre projetos ----
    private async Task<Contexto> ContextoCopiaAsync(BulkCopiaRequest req, CancellationToken ct)
    {
        var modo = Modo(req.Mode);
        if (!req.CopyProfiles && !req.CopyOverrides) throw new BulkValidationException("Escolha copiar perfis e/ou exceções.");
        var origem = (await ResolverProjetosAsync(new[] { req.SourceProjectIdAcc }, ct)).Single();
        var destinos = (await ResolverProjetosAsync(req.TargetProjectIdsAcc ?? Array.Empty<string>(), ct))
            .Where(p => p.ProjetoId != origem.ProjetoId).ToList();
        if (destinos.Count == 0) throw new BulkValidationException("Selecione ao menos um projeto de destino diferente da origem.");
        if (destinos.Count > MaxProjetos) throw new BulkValidationException($"Máximo de {MaxProjetos} projetos por operação.");

        var perfis = await PerfisAsync(ct);
        var estadoOrigem = (await repo.CarregarEstadoAsync(new[] { req.UserId }, new[] { origem.ProjetoId }, ct)).Pares.Single();
        // Perfil inativo/MasterAdmin na origem nunca é propagado.
        var perfisOrigem = estadoOrigem.Perfis.Where(id => perfis.TryGetValue(id, out var p) && p.Ativo && !p.IsMasterAdmin).ToHashSet();
        var overridesOrigem = estadoOrigem.Overrides.Where(kv => PermissionCatalog.Existe(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var avisos = new List<string>();
        if (req.CopyOverrides) avisos.Add($"As exceções ALLOW/DENY do projeto de origem ({overridesOrigem.Count}) serão replicadas nos destinos.");
        if (modo == BulkModo.Substituir)
            avisos.Add("SUBSTITUIR deixa os destinos iguais à origem: perfis" + (req.CopyOverrides ? " e exceções" : "") + " que não existem na origem serão removidos.");
        if (req.CopyProfiles && perfisOrigem.Count == 0) avisos.Add("O usuário não tem perfis no projeto de origem.");

        return new Contexto(OpCopia, modo, destinos, new[] { req.UserId }, perfis, perfisOrigem.OrderBy(x => x).ToList(), req.CopyProfiles, req.CopyOverrides, avisos,
            (_, _, _) => (req.CopyProfiles ? perfisOrigem : null, req.CopyOverrides ? overridesOrigem : null),
            new { userId = req.UserId, projetoId = origem.ProjetoId, projeto = origem.Nome, perfis = perfisOrigem.Select(id => perfis[id].Name), overrides = overridesOrigem.Count });
    }

    public async Task<BulkPreview> PreverCopiaAsync(BulkCopiaRequest req, int? adminUserId, CancellationToken ct) =>
        (await PlanejarAsync(await ContextoCopiaAsync(req, ct), adminUserId, ct)).Preview;

    public async Task<BulkResultado> AplicarCopiaAsync(BulkCopiaRequest req, int? adminUserId, string? adminEmail, CancellationToken ct) =>
        await AplicarAsync(await ContextoCopiaAsync(req, ct), adminUserId, adminEmail, ct);

    // ------------------------------------------------------------------------------- núcleo ----
    private async Task<(BulkPreview Preview, List<(PlanoPar, EstadoPar)> Planos)> PlanejarAsync(Contexto ctx, int? adminUserId, CancellationToken ct)
    {
        var estado = await repo.CarregarEstadoAsync(ctx.UserIds, ctx.Projetos.Select(p => p.ProjetoId).ToList(), ct);
        var projPorId = ctx.Projetos.ToDictionary(p => p.ProjetoId);
        string Nome(int id) => ctx.Perfis.TryGetValue(id, out var p) ? p.Name : $"#{id}";
        var planos = new List<(PlanoPar, EstadoPar)>();
        var itens = new List<BulkItem>();
        foreach (var e in estado.Pares.OrderBy(p => estado.Usuarios.TryGetValue(p.UserId, out var u) ? (u.Nome ?? u.Email) : "").ThenBy(p => projPorId[p.ProjetoId].Nome))
        {
            if (!estado.Usuarios.TryGetValue(e.UserId, out var usuario)) throw new BulkValidationException($"Usuário {e.UserId} inexistente.");
            var (perfisAlvo, overridesAlvo) = ctx.Alvo(estado, e.UserId, e.ProjetoId);
            var plano = BulkPermissionPlanner.Planejar(e, adminUserId == e.UserId, ctx.Modo, perfisAlvo, overridesAlvo);
            planos.Add((plano, e));
            var projeto = projPorId[e.ProjetoId];
            itens.Add(new BulkItem(e.UserId, usuario.Nome ?? usuario.Email, usuario.Email, e.ProjetoId, projeto.ProjectIdAcc, projeto.Nome, plano.Status, plano.Motivo,
                e.Perfis.Select(Nome).OrderBy(n => n).ToList(), plano.PerfisAdicionar.Select(Nome).ToList(), plano.PerfisRemover.Select(Nome).ToList(),
                plano.OverridesGravar.Count, plano.OverridesRemover.Count, plano.Vinculo));
        }
        var preview = new BulkPreview(ctx.Operacao, ctx.Modo == BulkModo.Adicionar ? "ADD" : "REPLACE", ctx.UserIds.Count, ctx.Projetos.Count, itens.Count,
            itens.Count(i => i.Status == BulkStatus.Alterar), itens.Count(i => i.Status == BulkStatus.JaPossui), itens.Count(i => i.Status == BulkStatus.Ignorar),
            ctx.PerfisAlvo.Select(Nome).ToList(), ctx.CopiaPerfis, ctx.CopiaOverrides, itens, ctx.Avisos);
        return (preview, planos);
    }

    private async Task<BulkResultado> AplicarAsync(Contexto ctx, int? adminUserId, string? adminEmail, CancellationToken ct)
    {
        var (preview, planos) = await PlanejarAsync(ctx, adminUserId, ct);
        if (preview.Alterar == 0) return new BulkResultado(true, 0, preview.JaPossui, preview.Ignoradas, 0, null, preview);
        var referencia = $"BULK:{Guid.NewGuid():N}";
        var detalhe = new
        {
            operacao = ctx.Operacao, modo = preview.Modo, usuarios = ctx.UserIds, projetos = ctx.Projetos.Select(p => new { p.ProjetoId, p.Nome }),
            perfis = preview.Perfis, copiarPerfis = ctx.CopiaPerfis, copiarOverrides = ctx.CopiaOverrides, origem = ctx.Origem,
            resultado = new { alterados = preview.Alterar, jaPossuiam = preview.JaPossui, ignorados = preview.Ignoradas },
        };
        try
        {
            await repo.AplicarAsync(planos, new AuditoriaLote(adminUserId, adminEmail, ctx.Operacao, referencia, detalhe), ct);
            var alterados = preview.Itens.Where(i => i.Status == BulkStatus.Alterar).ToList();
            return new BulkResultado(true, preview.Alterar, preview.JaPossui, preview.Ignoradas, 0, null, preview,
                alterados.Count(i => i.Vinculo == BulkVinculo.Criar), alterados.Count(i => i.Vinculo == BulkVinculo.Reativar),
                alterados.Sum(i => i.PerfisAdicionados.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Transação desfeita: nada foi gravado. Todas as combinações que seriam alteradas contam como falha.
            return new BulkResultado(false, 0, preview.JaPossui, preview.Ignoradas, preview.Alterar,
                $"Nenhuma alteração foi gravada (transação desfeita): {ex.GetType().Name}.", preview);
        }
    }
}
