namespace APS.WebApp.Services.Permissions;

public static class PermissionEffects
{
    public const string Allow = "ALLOW", Deny = "DENY";
}

/// <summary>Perfil atribuído ao usuário (ProjetoId NULL = atribuição global, vale em todos os projetos).</summary>
public sealed record ProfileAssignment(int ProfileId, string ProfileName, int? ProjetoId, bool IsMasterAdmin, IReadOnlySet<string> Permissions);

/// <summary>Override individual (ProjetoId NULL = global).</summary>
public sealed record PermissionOverride(int? ProjetoId, string PermissionKey, string Effect);

/// <summary>Tudo o que o resolvedor precisa sobre UM usuário — carregado do banco uma vez por requisição.</summary>
public sealed record UserPermissionData(
    bool IsConfiguredMasterAdmin,
    IReadOnlyList<ProfileAssignment> Assignments,
    IReadOnlyList<PermissionOverride> Overrides,
    IReadOnlySet<int> InactiveProjects,
    // Projetos POSITIVAMENTE vinculados ao usuário. Null somente em cenários
    // legados de testes (sem informação de vínculos); no SQL nunca é null.
    IReadOnlySet<int>? ActiveProjects = null);

public enum PermissionDecisionSource { MasterAdmin, DenyIndividual, AllowIndividual, Perfil, NegadoPorPadrao }

public sealed record PermissionDecision(bool Allowed, PermissionDecisionSource Source, string? Detail = null);

/// <summary>
/// RESOLVEDOR ÚNICO de permissões (backend e frontend usam o resultado dele — o frontend nunca recalcula regras).
/// Precedência: MasterAdmin &gt; DENY individual &gt; ALLOW individual &gt; perfis (UNIÃO) &gt; negado por padrão.
/// Escopo: com projeto P valem atribuições/overrides de P e os globais (ProjetoId NULL); override de P vence override
/// global da mesma key. Sem projeto, só os globais. Usuário INATIVO no projeto P não herda nada em P (nem globais).
/// Pure: sem banco — testado em Tests/NA_Connect.Planning.Tests/PermissionResolverTests.cs.
/// </summary>
public static class PermissionResolver
{
    public static bool IsMasterAdmin(UserPermissionData u) =>
        u.IsConfiguredMasterAdmin || u.Assignments.Any(a => a.IsMasterAdmin && a.ProjetoId is null);

    public static PermissionDecision Decide(UserPermissionData u, string key, int? projetoId)
    {
        if (IsMasterAdmin(u)) return new(true, PermissionDecisionSource.MasterAdmin);
        if (!PermissionCatalog.Existe(key)) return new(false, PermissionDecisionSource.NegadoPorPadrao, "Key fora do catálogo.");
        if (projetoId is int p && u.InactiveProjects.Contains(p))
            return new(false, PermissionDecisionSource.NegadoPorPadrao, "Usuário inativo neste projeto.");

        // Override mais específico vence: projeto > global. No mesmo nível, DENY vence ALLOW.
        var doProjeto = projetoId is null ? Array.Empty<PermissionOverride>() : u.Overrides.Where(o => o.ProjetoId == projetoId && o.PermissionKey == key).ToArray();
        var globais = u.Overrides.Where(o => o.ProjetoId is null && o.PermissionKey == key).ToArray();
        var nivel = doProjeto.Length > 0 ? doProjeto : globais;
        if (nivel.Any(o => o.Effect == PermissionEffects.Deny)) return new(false, PermissionDecisionSource.DenyIndividual);
        if (nivel.Any(o => o.Effect == PermissionEffects.Allow)) return new(true, PermissionDecisionSource.AllowIndividual);

        var perfis = u.Assignments.Where(a => a.ProjetoId is null || (projetoId is not null && a.ProjetoId == projetoId));
        var perfil = perfis.FirstOrDefault(a => a.Permissions.Contains(key));
        return perfil is not null
            ? new(true, PermissionDecisionSource.Perfil, perfil.ProfileName)
            : new(false, PermissionDecisionSource.NegadoPorPadrao);
    }

    /// <summary>Todas as keys concedidas no contexto (projeto ou global).</summary>
    public static IReadOnlySet<string> Effective(UserPermissionData u, int? projetoId) =>
        PermissionCatalog.Keys.Where(k => Decide(u, k, projetoId).Allowed).ToHashSet(StringComparer.Ordinal);

    /// <summary>Keys concedidas em PELO MENOS um projeto (ou globalmente) — usado só para montar a navegação.</summary>
    public static IReadOnlySet<string> EffectiveAnyProject(UserPermissionData u)
    {
        var resultado = new HashSet<string>(Effective(u, null), StringComparer.Ordinal);
        IEnumerable<int> projetos = u.Assignments.Select(a => a.ProjetoId)
            .Concat(u.Overrides.Select(o => o.ProjetoId)).OfType<int>().Distinct();
        // Não anunciar funcionalidade atribuída em projeto ao qual não existe
        // vínculo de acesso ativo em perm.UserProjects.
        if (u.ActiveProjects is { } ativos) projetos = projetos.Where(ativos.Contains);
        foreach (var p in projetos) resultado.UnionWith(Effective(u, p));
        return resultado;
    }
}
