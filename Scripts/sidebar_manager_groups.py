from pathlib import Path
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp\src\features\sidebar\SidebarManagerPage.tsx')
s=p.read_text('utf-8')
needle='  function move(id:string,parent:string|null,before?:string){try{setItems(moveItem(items,id,parent,before));setMessage("");}catch(e){setMessage(e instanceof Error?e.message:"Movimento inválido.");}}'
assert needle in s
s=s.replace(needle,needle+'''
  function createGroup(){
    const id="custom:"+crypto.randomUUID();
    const group:SidebarItem={id,parentId:null,order:items.filter(n=>n.parentId===null).length,label:"Novo grupo",icon:"FolderOpen",hidden:false};
    try {setItems(validateItems([...items,group]));setMessage("Grupo criado no rascunho.");}catch(e){setMessage(e instanceof Error?e.message:"Erro ao criar grupo.");}
  }
  function removeGroup(id:string) {
    if(!id.startsWith("custom:"))return;
    const group=items.find(n=>n.id===id);if(!group)return;
    const changed=items.filter(n=>n.id!==id).map(n=>n.parentId===id?{...n,parentId:group.parentId}:n);
    const parents=[...new Set(changed.map(n=>n.parentId))];
    const normalized=changed.map(n=>({...n,order:changed.filter(x=>x.parentId===n.parentId).sort((a,b)=>a.order-b.order).findIndex(x=>x.id===n.id)}));
    void parents;
    try {setItems(validateItems(normalized));setMessage("Grupo excluído do rascunho; os filhos foram preservados.");}catch(e){setMessage(e instanceof Error?e.message:"Erro ao excluir grupo.");}
  }
''')
needle='<button className="rounded border px-4 py-2" disabled={busy} onClick={()=>setEpoch(x=>x+1)}>Recarregar</button>'
assert needle in s
s=s.replace(needle,'<button className="rounded border px-4 py-2" disabled={busy||!config} onClick={createGroup}>+ Novo grupo</button>'+needle)
needle='{n.id.startsWith("page:")&&<p className="text-xs text-muted-foreground break-all">{n.id.slice(5)}</p>}'
assert needle in s
s=s.replace(needle,'{n.id.startsWith("custom:")&&<button type="button" className="text-xs text-red-500 underline" disabled={busy} onClick={()=>removeGroup(n.id)}>Excluir grupo (preservar filhos)</button>}'+needle)
p.write_text(s,'utf-8')
print('Editor group management added')
