using System.Text.Json;
using System.Text.Json.Serialization;
namespace APS.WebApp.Services.Sidebar;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SidebarItem(string Id, string? ParentId, int Order, string Label, string Icon, bool Hidden);
public sealed record SidebarCatalogItem(string Id, string? ParentId, int Order, string Label, string Icon, bool Hidden, string? To, string[] PermissionKeys, JsonElement? BadgeInline, JsonElement? BadgeAbaixo, bool Group);
public sealed record SidebarConfiguration(long Revision, IReadOnlyList<SidebarItem> Items);
public sealed record SidebarHistory(long Revision, DateTime UpdatedAt, string UpdatedBy, long? RestoredFrom);
public sealed class SidebarConflictException : Exception;
public static partial class SidebarRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly IReadOnlyList<SidebarCatalogItem> Catalog = JsonSerializer.Deserialize<SidebarCatalogItem[]>(CatalogJson, Json)!;
    public static readonly IReadOnlyDictionary<string, SidebarCatalogItem> ById = Catalog.ToDictionary(n => n.Id, StringComparer.Ordinal);
    public static readonly IReadOnlySet<string> Icons = Catalog.Select(n => n.Icon)
        .Concat(new[] { "AlertOctagon", "BarChart3", "BookOpen", "Bot", "Box", "Calculator", "CalendarCheck", "CalendarClock", "CalendarDays", "CalendarRange", "ClipboardCheck", "ClipboardList", "Coins", "Database", "Factory", "FileSpreadsheet", "FileStack", "FileText", "FileWarning", "FolderCog", "FolderKanban", "FolderOpen", "Gauge", "GitBranch", "GitPullRequest", "History", "KeyRound", "Languages", "Layers", "LayoutTemplate", "ListChecks", "MonitorPlay", "Palette", "PauseCircle", "Receipt", "Scale", "Scan", "Settings", "ShieldAlert", "ShieldCheck", "Timer", "TrendingUp", "UserRound", "Users", "Wallet" })
        .ToHashSet(StringComparer.Ordinal);
    public static SidebarConfiguration Default => new(0, Catalog.Select(n => new SidebarItem(n.Id,n.ParentId,n.Order,n.Label,n.Icon,false)).ToArray());
    public static IReadOnlyList<SidebarItem> Validate(IReadOnlyList<SidebarItem>? items)
    {
        if (items is null || items.Count != Catalog.Count) throw new ArgumentException("Informe todos os itens do catálogo oficial.");
        var found = new Dictionary<string, SidebarItem>(StringComparer.Ordinal);
        foreach (var n in items)
        {
            if (n is null || n.Id is null || !ById.ContainsKey(n.Id) || !found.TryAdd(n.Id,n))
                throw new ArgumentException("ID desconhecido ou duplicado.");
            if (string.IsNullOrWhiteSpace(n.Label) || n.Label.Length > 100 || n.Label.Any(char.IsControl)) throw new ArgumentException("Rótulo inválido (1 a 100 caracteres).");
            if (n.Icon is null || !Icons.Contains(n.Icon) || n.Order < 0 || n.Order >= items.Count) throw new ArgumentException("Ícone ou ordem inválidos.");
            if (n.ParentId is not null && !items.Any(p => p.Id == n.ParentId && ById.TryGetValue(p.Id,out var category) && category.Group))
                throw new ArgumentException("O pai deve ser um grupo.");
        }
        if (Catalog.Any(c => !found.ContainsKey(c.Id))) throw new ArgumentException("Faltam páginas do catálogo.");
        foreach (var n in items)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) {n.Id};
            var parent = n.ParentId;
            while (parent is not null)
            {
                if (!seen.Add(parent) || seen.Count > 8) throw new ArgumentException("Ciclo ou profundidade excessiva.");
                parent = found[parent].ParentId;
            }
        }
        foreach (var siblings in items.GroupBy(n => n.ParentId))
            if (siblings.Select(n => n.Order).Distinct().Count() != siblings.Count()) throw new ArgumentException("Ordem duplicada no grupo.");
        return items.ToArray();
    }
    // Groups survive only if a permitted descendant survives; hidden ancestry is also respected.
    public static IReadOnlyList<SidebarItem> Filter(IReadOnlyList<SidebarItem> items, Func<SidebarCatalogItem, bool> can)
    {
        var byId = items.ToDictionary(n => n.Id, StringComparer.Ordinal);
        bool Visible(SidebarItem n) => !n.Hidden && (n.ParentId is null || Visible(byId[n.ParentId]));
        var allowed = items.Where(n => ById.TryGetValue(n.Id,out var catalog) && !catalog.Group && Visible(n) && can(catalog)).Select(n=>n.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var id in allowed.ToArray()) { var p=byId[id].ParentId; while(p is not null) { allowed.Add(p); p=byId[p].ParentId; } }
        return items.Where(n=>allowed.Contains(n.Id)).ToArray();
    }
}
