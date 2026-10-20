from pathlib import Path
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect\Services\Sidebar\SidebarRules.cs')
s=p.read_text(encoding='utf-8')
s=s.replace('public static readonly IReadOnlySet<string> Icons = Catalog.Select(n => n.Icon).ToHashSet(StringComparer.Ordinal);',
'''public static readonly IReadOnlySet<string> Icons = Catalog.Select(n => n.Icon)
        .Concat(new[] {"Settings","FolderOpen","Layers","LayoutDashboard","CalendarClock","Factory","ShieldCheck","BarChart3","FileText","FileStack","Coins","BookOpen","Users","Gauge","ClipboardList","Database","MonitorPlay","Palette","FolderCog","ListChecks","Timer","Scale","KeyRound","FileSpreadsheet","CircleHelp"})
        .ToHashSet(StringComparer.Ordinal);''')
s=s.replace('if (items is null || items.Count != Catalog.Count) throw new ArgumentException("Informe todos os itens do catálogo oficial.");',
'''if (items is null || items.Count < Catalog.Count || items.Count > Catalog.Count + 100) throw new ArgumentException("Informe todos os itens do catálogo oficial e até 100 grupos adicionais.");''')
s=s.replace('if (n is null || n.Id is null || !ById.ContainsKey(n.Id) || !found.TryAdd(n.Id,n)) throw new ArgumentException("ID desconhecido ou duplicado.");',
'''if (n is null || n.Id is null || (!ById.ContainsKey(n.Id) && !(n.Id.StartsWith("custom:", StringComparison.Ordinal) && Guid.TryParse(n.Id[7..],out _))) || !found.TryAdd(n.Id,n))
                throw new ArgumentException("ID desconhecido ou duplicado.");''')
s=s.replace('n.Order >= Catalog.Count','n.Order >= items.Count')
s=s.replace('if (n.ParentId is not null && (!ById.TryGetValue(n.ParentId, out var p) || !p.Group)) throw new ArgumentException("O pai deve ser um grupo oficial.");',
'''if (n.ParentId is not null && (!items.Any(p => p.Id == n.ParentId && (p.Id.StartsWith("custom:",StringComparison.Ordinal) || (ById.TryGetValue(p.Id,out var category) && category.Group)))))
                throw new ArgumentException("O pai deve ser um grupo.");''')
s=s.replace('foreach (var n in items)\n        {\n            var seen', '''if (Catalog.Any(c => !found.ContainsKey(c.Id))) throw new ArgumentException("Faltam páginas do catálogo.");
        foreach (var n in items)
        {
            var seen''')
s=s.replace('!ById[n.Id].Group && Visible(n) && can(ById[n.Id])','ById.TryGetValue(n.Id,out var catalog) && !catalog.Group && Visible(n) && can(catalog)')
p.write_text(s,encoding='utf-8')
print('rules patched')
