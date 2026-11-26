from pathlib import Path
r=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
p=r/'Services/Sidebar/SidebarRules.cs'
s=p.read_text('utf-8')
a='if (items is null || items.Count != Catalog.Count) throw new ArgumentException("Informe todos os itens do catálogo oficial.");'
b='if (items is null || items.Count < Catalog.Count || items.Count > Catalog.Count + 100) throw new ArgumentException("Informe os itens do catálogo e até 100 grupos personalizados.");'
assert a in s;s=s.replace(a,b)
a='if (n is null || n.Id is null || !ById.ContainsKey(n.Id) || !found.TryAdd(n.Id,n))'
b='if (n is null || n.Id is null || (!ById.ContainsKey(n.Id) && !(n.Id.StartsWith("custom:", StringComparison.Ordinal) && Guid.TryParse(n.Id[7..],out _))) || !found.TryAdd(n.Id,n))'
assert a in s;s=s.replace(a,b)
p.write_text(s,'utf-8')
p=r/'ClientApp/src/features/sidebar/sidebarModel.ts'
s=p.read_text('utf-8')
s=s.replace('value.length > catalog.length || (complete && value.length !== catalog.length)', 'value.length > catalog.length+100 || (complete && value.length < catalog.length)')
s=s.replace('!official.has(n.id) || map.has(n.id)', '(!official.has(n.id) && !/^custom:[a-f\\d-]{36}$/i.test(n.id)) || map.has(n.id)')
s=s.replace('n.order >= catalog.length','n.order >= catalog.length+100')
s=s.replace('official.get(n.parentId)?.group','(official.get(n.parentId)?.group || /^custom:[a-f\\d-]{36}$/i.test(n.parentId))')
s=s.replace('  const positions = new Set<string>();','  if(complete && catalog.some(n=>!map.has(n.id)))throw Error("Faltam páginas oficiais.");\n  const positions = new Set<string>();')
s=s.replace('''      const original=available.get(n.id)!;
      if(original.children) { const children=build(n.id); return children.length?[{...original,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[]; }
      return [{...original,label:n.label,icon:SIDEBAR_ICONS[n.icon]}];''',
'''      const original=available.get(n.id);
      const custom=n.id.startsWith("custom:");
      if(!original&&!custom)return [];
      if(custom) {const children=build(n.id);return children.length?[{id:n.id,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[];}
      if(original!.children) { const children=build(n.id); return children.length?[{...original!,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[]; }
      return [{...original!,label:n.label,icon:SIDEBAR_ICONS[n.icon]}];''')
s=s.replace('items.filter(n=>n.parentId===parentId&&!n.hidden&&available.has(n.id))','items.filter(n=>n.parentId===parentId&&!n.hidden&&(available.has(n.id)||n.id.startsWith("custom:")))')
p.write_text(s,'utf-8')
print('Custom group groundwork patched')
