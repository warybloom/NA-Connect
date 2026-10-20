namespace APS.WebApp.Services.Planning;

/// <summary>Perfis de Programação, sem relação com Tipo_LDP. O catálogo de campos continua compartilhado.</summary>
public static class WeeklyProcessorRegistry
{
    public const string Official = "WeeklyLayoutOfficial";
    public const string Legacy = "WeeklyLayoutLegacy26";
    public sealed record Profile(string Id, bool Enabled);
    public static IReadOnlyList<Profile> All { get; } = Array.AsReadOnly(new[] { new Profile(Official, true), new Profile(Legacy, false) });
    public static IReadOnlyList<Profile> Active => All.Where(p => p.Enabled).ToArray();
    public static Profile? Select(string id) => Active.SingleOrDefault(p => p.Id == id);
    public static void RequireActive(string id)
    {
        if (Select(id) == null) throw new InvalidOperationException("PADRAO_DESATIVADO: " + id);
    }
}
