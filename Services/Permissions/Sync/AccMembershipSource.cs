using System.Text.Json;

namespace APS.WebApp.Services.Permissions.Sync;

/// <summary>Projetos ACC acessíveis ao usuário. Completa = TODOS os hubs responderam com sucesso.</summary>
public sealed record ProjetosAcc(IReadOnlySet<string> ProjectIdsAcc, bool Completa, string? Falha);

/// <summary>Membro de um projeto ACC (Admin API).</summary>
public sealed record MembroAcc(string? AutodeskUserId, string Email, string Nome);

/// <summary>
/// Adaptador sobre as integrações JÁ EXISTENTES do AutodeskApiService (GetHubsAsync/GetHubProjectsAsync e
/// GetProjectUsersAsync). Não há chamada Autodesk paralela aqui — só leitura e normalização.
/// </summary>
public interface IAccMembershipSource
{
    /// <summary>Nunca lança por falha Autodesk: devolve Completa=false (e o que conseguiu ler) para não desativar nada.</summary>
    Task<ProjetosAcc> ProjetosDoUsuarioAsync(string accessToken, CancellationToken ct);

    /// <summary>Lança em qualquer falha — lista parcial nunca é devolvida.</summary>
    Task<IReadOnlyList<MembroAcc>> MembrosDoProjetoAsync(string projectIdAcc, string accessToken, CancellationToken ct);
}

public sealed class AccMembershipSource(AutodeskApiService autodesk, ILogger<AccMembershipSource> logger) : IAccMembershipSource
{
    public async Task<ProjetosAcc> ProjetosDoUsuarioAsync(string accessToken, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement hubs;
        try { hubs = await autodesk.GetHubsAsync(accessToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Sincronização ACC: falha ao listar hubs ({Tipo}) — nenhum vínculo será desativado.", ex.GetType().Name);
            return new ProjetosAcc(ids, false, "Falha ao listar hubs Autodesk.");
        }
        if (!hubs.TryGetProperty("data", out var listaHubs) || listaHubs.ValueKind != JsonValueKind.Array)
            return new ProjetosAcc(ids, false, "Resposta de hubs inválida.");

        bool completa = true;
        foreach (var hub in listaHubs.EnumerateArray())
        {
            var hubId = hub.TryGetProperty("id", out var h) ? h.GetString() : null;
            if (string.IsNullOrWhiteSpace(hubId)) { completa = false; continue; }
            try
            {
                var projetos = await autodesk.GetHubProjectsAsync(hubId, accessToken);
                if (!projetos.TryGetProperty("data", out var lista) || lista.ValueKind != JsonValueKind.Array) { completa = false; continue; }
                foreach (var p in lista.EnumerateArray())
                    if (p.TryGetProperty("id", out var pid) && pid.GetString() is { Length: > 0 } id)
                        ids.Add(id.StartsWith("b.", StringComparison.Ordinal) ? id : "b." + id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                completa = false;
                logger.LogWarning("Sincronização ACC: falha ao listar projetos de um hub ({Tipo}) — desativações suspensas.", ex.GetType().Name);
            }
        }
        return new ProjetosAcc(ids, completa, completa ? null : "Listagem de projetos Autodesk incompleta.");
    }

    public async Task<IReadOnlyList<MembroAcc>> MembrosDoProjetoAsync(string projectIdAcc, string accessToken, CancellationToken ct)
    {
        var brutos = await autodesk.GetProjectUsersAsync(projectIdAcc, accessToken, ct);
        return MembrosAccParser.Parse(brutos);
    }
}

public static class MembrosAccParser
{
    private static readonly HashSet<string> StatusExcluidos = new(StringComparer.OrdinalIgnoreCase) { "deleted", "inactive" };

    /// <summary>autodeskId → AutodeskUserId (mesmo "sub" do UserInfo); email; name (ou firstName + lastName). Sem e-mail = ignorado.</summary>
    public static IReadOnlyList<MembroAcc> Parse(IEnumerable<JsonElement> brutos)
    {
        string Texto(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        var porEmail = new Dictionary<string, MembroAcc>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in brutos)
        {
            if (StatusExcluidos.Contains(Texto(u, "status"))) continue;
            var email = Texto(u, "email");
            if (email.Length == 0) continue;
            var nome = Texto(u, "name");
            if (nome.Length == 0) nome = $"{Texto(u, "firstName")} {Texto(u, "lastName")}".Trim();
            var acc = Texto(u, "autodeskId");
            porEmail[email] = new MembroAcc(acc.Length > 0 ? acc : null, email, nome);
        }
        return porEmail.Values.ToList();
    }
}
