namespace APS.WebApp.Services.Permissions.Bulk;

/// <summary>ADICIONAR mantém o que existe e acrescenta; SUBSTITUIR deixa o par exatamente igual ao alvo.</summary>
public enum BulkModo { Adicionar, Substituir }

public static class BulkStatus
{
    public const string Alterar = "ALTERAR", JaPossui = "JA_POSSUI", Ignorar = "IGNORAR";
}

/// <summary>O que acontece com o vínculo interno (perm.UserProjects) do par.</summary>
public static class BulkVinculo
{
    /// <summary>Não existe linha: cria UserProjects ativo.</summary>
    public const string Criar = "CRIAR";
    /// <summary>Existe linha inativa: reativa (nunca duplica — PK UserId+ProjetoId).</summary>
    public const string Reativar = "REATIVAR";
}

/// <summary>
/// Estado atual de um par usuário × projeto (somente perfis e exceções DO PROJETO; globais nunca são tocados).
/// VinculoExiste: há linha em perm.UserProjects (ativa ou não). UsuarioAtivo: perm.Users.Ativo.
/// </summary>
public sealed record EstadoPar(int UserId, int ProjetoId, bool VinculoAtivo, IReadOnlySet<int> Perfis, IReadOnlyDictionary<string, string> Overrides,
    bool VinculoExiste = false, bool UsuarioAtivo = true);

/// <summary>Alterações decididas para um par. Perfis/overrides nulos = camada não tocada. Vinculo = CRIAR/REATIVAR ou null.</summary>
public sealed record PlanoPar(
    int UserId, int ProjetoId, string Status, string? Motivo,
    IReadOnlyList<int> PerfisAdicionar, IReadOnlyList<int> PerfisRemover,
    IReadOnlyDictionary<string, string> OverridesGravar, IReadOnlyList<string> OverridesRemover,
    string? Vinculo = null)
{
    public bool TemMudanca => Vinculo is not null || PerfisAdicionar.Count + PerfisRemover.Count + OverridesGravar.Count + OverridesRemover.Count > 0;
}

/// <summary>
/// REGRAS das operações em lote (puras, sem banco):
///  • Vínculo INTERNO (perm.UserProjects): o lote é o cadastro administrativo do usuário no projeto — não depende de
///    Autodesk/ACC/AccUserId. Sem vínculo → CRIAR; vínculo inativo → REATIVAR; ativo → mantém. Nunca duplica.
///  • Usuário inativo no N&A (perm.Users.Ativo = 0) e o próprio administrador são IGNORADOS (motivos reais).
///  • Perfis: ADICIONAR = atuais ∪ alvo; SUBSTITUIR = exatamente o alvo. Perfil já presente não é reinserido (JÁ POSSUI).
///  • Exceções (ALLOW/DENY) só mudam quando explicitamente pedidas (cópia entre projetos com "Exceções" marcado):
///    ADICIONAR grava só as keys que o destino ainda não tem (não inverte ALLOW/DENY existente);
///    SUBSTITUIR deixa as exceções do destino iguais às da origem.
/// </summary>
public static class BulkPermissionPlanner
{
    private static PlanoPar Ignorado(EstadoPar e, string motivo) =>
        new(e.UserId, e.ProjetoId, BulkStatus.Ignorar, motivo, Array.Empty<int>(), Array.Empty<int>(),
            new Dictionary<string, string>(), Array.Empty<string>());

    public const string MotivoProprioAcesso = "você não pode alterar o seu próprio acesso";
    public const string MotivoUsuarioInativo = "usuário inativo no N&A Connect";

    public static PlanoPar Planejar(EstadoPar estado, bool ehOProprioAdmin, BulkModo modo,
        IReadOnlySet<int>? perfisAlvo, IReadOnlyDictionary<string, string>? overridesAlvo)
    {
        if (ehOProprioAdmin) return Ignorado(estado, MotivoProprioAcesso);
        if (!estado.UsuarioAtivo) return Ignorado(estado, MotivoUsuarioInativo);
        string? vinculo = estado.VinculoAtivo ? null : estado.VinculoExiste ? BulkVinculo.Reativar : BulkVinculo.Criar;

        var adicionar = new List<int>();
        var remover = new List<int>();
        if (perfisAlvo is not null)
        {
            var final = modo == BulkModo.Adicionar ? estado.Perfis.Union(perfisAlvo).ToHashSet() : perfisAlvo.ToHashSet();
            adicionar.AddRange(final.Except(estado.Perfis).OrderBy(x => x));
            remover.AddRange(estado.Perfis.Except(final).OrderBy(x => x));
        }

        var gravar = new Dictionary<string, string>(StringComparer.Ordinal);
        var removerOv = new List<string>();
        if (overridesAlvo is not null)
        {
            foreach (var (key, effect) in overridesAlvo)
            {
                bool existe = estado.Overrides.TryGetValue(key, out var atual);
                if (!existe || (modo == BulkModo.Substituir && atual != effect)) gravar[key] = effect;
            }
            if (modo == BulkModo.Substituir)
                removerOv.AddRange(estado.Overrides.Keys.Where(k => !overridesAlvo.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal));
        }

        var plano = new PlanoPar(estado.UserId, estado.ProjetoId, BulkStatus.Alterar, null, adicionar, remover, gravar, removerOv, vinculo);
        return plano.TemMudanca ? plano : plano with { Status = BulkStatus.JaPossui };
    }
}
