import { useEffect, useMemo, useState } from "react";
import { SidebarPreview } from "@/components/Layout";
import { AcessoNegado } from "../permissions/PermissionRouteGuard";
import { filtrarPorPermissao } from "../permissions/permissionLogic";
import { usePermissions, useProjectPermissions } from "../permissions/PermissionProvider";
import { useProjetoAtual } from "../project/ProjetoAtualContext";
import { contextualNavigation } from "./officialNavigation";
import { applyConfiguration, DEFAULT_ITEMS, moveItem, validateItems, type SidebarConfiguration, type SidebarItem } from "./sidebarModel";
import { SIDEBAR_ICONS } from "./icons";
import { published, sidebarRequest, type SidebarErrorKind } from "./sidebarApi";
import { ReportState } from "@/components/reports/ReportComponents";
import { useSidebarManagerAccess } from "./useOfficialSidebar";
interface History { revision:number;updatedAt:string;updatedBy:string;restoredFrom:number|null }
// Falha da API nunca vira "sem permissão": cada causa tem sua mensagem.
const ACCESS_ERRORS:Record<SidebarErrorKind,[string,string]>={
  session:["Sessão expirada","Entre novamente para abrir o Gerenciador de Sidebar."],
  forbidden:["Sem permissão administrativa","É necessário MasterAdmin ou permissions.manage global."],
  database:["Banco de dados indisponível","Não foi possível consultar DB_NA_CONNECT. Verifique o SQL Server e tente novamente."],
  missing:["Gerenciador indisponível no backend","O backend em execução não possui a API /api/sidebar — provavelmente está desatualizado. Reinicie o backend com a versão atual."],
  network:["Falha de conexão com a API","O backend (porta 5277) não respondeu. Verifique se ele está em execução."],
  other:["Falha ao verificar acesso","Tente novamente em instantes."],
};
interface AdminResponse {configuration:SidebarConfiguration;csrfToken:string}
export default function SidebarManagerPage() {
  const {effective}=usePermissions();const [comparison,setComparison]=useState(false);
  useEffect(()=>{const ctrl=new AbortController();fetch("/api/production-audit/experimental-summary/access",{credentials:"include",signal:ctrl.signal}).then(r=>r.ok?r.json():{allowed:false}).then(r=>{if(!ctrl.signal.aborted)setComparison(r.allowed===true);}).catch(()=>{if(!ctrl.signal.aborted)setComparison(false);});return()=>ctrl.abort();},[effective]);
  const access=useSidebarManagerAccess();const {projetoAtual}=useProjetoAtual();const {can,loading:permissionLoading}=useProjectPermissions(projetoAtual?.projectId);
  const [config,setConfig]=useState<SidebarConfiguration|null>(null);const [items,setItems]=useState<SidebarItem[]>([]);
  const [token,setToken]=useState("");const [history,setHistory]=useState<History[]>([]);const [busy,setBusy]=useState(false);const [message,setMessage]=useState("");const [epoch,setEpoch]=useState(0);const [drag,setDrag]=useState<string|null>(null);
  useEffect(()=>{setConfig(null);setItems([]);setToken("");setHistory([]);if(!access.allowed)return;const ctrl=new AbortController();setBusy(true);
    Promise.all([sidebarRequest<AdminResponse>("/admin","GET",undefined,undefined,ctrl.signal),sidebarRequest<History[]>("/history","GET",undefined,undefined,ctrl.signal)]).then(([r,h])=>{
      validateItems(r.configuration.items);if(!ctrl.signal.aborted){setConfig(r.configuration);setItems(r.configuration.items);setToken(r.csrfToken);setHistory(h);setMessage("");}
    }).catch(e=>{if(!ctrl.signal.aborted)setMessage(e.message);}).finally(()=>{if(!ctrl.signal.aborted)setBusy(false);});return()=>ctrl.abort();},[access.allowed,epoch]);
  const preview=useMemo(()=>{
    const tree=contextualNavigation(comparison,effective?.enforced ?? false,effective?.isMasterAdmin);
    return permissionLoading?[]:filtrarPorPermissao(applyConfiguration(tree,{revision:config?.revision??0,items}),can);
  },[items,config?.revision,can,permissionLoading,effective,comparison]);
  const rows=useMemo(()=>{const out:{item:SidebarItem;depth:number}[]=[];function add(parent:string|null,depth:number){items.filter(n=>n.parentId===parent).sort((a,b)=>a.order-b.order).forEach(item=>{out.push({item,depth});add(item.id,depth+1);});}add(null,0);return out;},[items]);
  function patch(id:string,change:Partial<SidebarItem>){setItems(current=>current.map(n=>n.id===id?{...n,...change}:n));}
  function move(id:string,parent:string|null,before?:string){try{setItems(moveItem(items,id,parent,before));setMessage("");}catch(e){setMessage(e instanceof Error?e.message:"Movimento inválido.");}}
  async function publish(source?:number){if(!config||busy)return;setBusy(true);setMessage("");try{
    const next=source===undefined?await sidebarRequest<SidebarConfiguration>("/publish","PUT",{revision:config.revision,items:validateItems(items)},token):await sidebarRequest<SidebarConfiguration>("/restore","POST",{revision:config.revision,sourceRevision:source},token);
    setConfig(next);setItems(next.items);published(next);setHistory(await sidebarRequest<History[]>("/history"));setMessage("Menu oficial publicado globalmente. Revisão "+next.revision+".");
  }catch(e){setMessage(e instanceof Error?e.message:"Não foi possível publicar.");}finally{setBusy(false);}}
  if(access.loading)return <main className="p-6"><ReportState title="Verificando acesso ao gerenciador…">Consultando suas permissões globais.</ReportState></main>;
  if(access.error){const e=access.error;const [title,hint]=ACCESS_ERRORS[e.kind];
    return <main className="p-6"><ReportState title={title} error><p>{e.message}</p><p className="mt-2 text-sm">{hint}</p>
      {e.kind==="session"?<a className="mt-3 inline-block rounded border px-4 py-2" href="/login">Entrar novamente</a>:<button className="mt-3 rounded border px-4 py-2" onClick={()=>window.location.reload()}>Tentar novamente</button>}</ReportState></main>;}
  if(!access.allowed)return <AcessoNegado detalhe={"A conta "+(access.email??"atual")+" não possui MasterAdmin nem permissions.manage global. Permissões concedidas apenas em projeto não habilitam o Gerenciador de Sidebar. Solicite a concessão a um administrador."} />;
  const groups=items.filter(n=>!n.id.startsWith("page:"));
  return <main className="p-6 space-y-6">
    <div><h1 className="text-2xl font-bold">Gerenciador de Sidebar</h1><p className="text-sm text-muted-foreground">Menu oficial global · revisão {config?.revision??"—"}. As permissões de cada página continuam sendo aplicadas.</p></div>
    <div className="flex flex-wrap gap-3"><button className="rounded border px-4 py-2" disabled={busy} onClick={()=>setEpoch(x=>x+1)}>Recarregar</button><button className="rounded border px-4 py-2" disabled={busy||!config} onClick={()=>setItems(DEFAULT_ITEMS.map(n=>({...n})))}>Padrão oficial no rascunho</button><button className="rounded bg-primary text-primary-foreground px-4 py-2" disabled={busy||!config} onClick={()=>void publish()}>{busy?"Aguarde…":"Publicar menu global"}</button></div>
    {message&&<p role="status" className="rounded border p-3">{message}</p>}
    {!config&&busy&&<ReportState title="Carregando configuração…">Lendo o menu oficial publicado e o histórico.</ReportState>}
    {!config&&!busy&&<p>Não foi possível carregar a configuração. Use “Recarregar”; se o erro for de banco, verifique as tabelas dbo.SidebarOfficial em DB_NA_CONNECT.</p>}
    {config&&<div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <section className="space-y-3 min-w-0"><h2 className="font-semibold">Grupos, subgrupos e páginas</h2><p className="text-sm text-muted-foreground">Arraste pela alça para inserir antes de um item. Use “Grupo pai” para mover entre grupos ou criar um subgrupo com os grupos oficiais existentes. As setas também permitem ordenar pelo teclado.</p>
        {rows.map(({item:n,depth})=>{const Icon=SIDEBAR_ICONS[n.icon];const siblings=items.filter(x=>x.parentId===n.parentId).sort((a,b)=>a.order-b.order);const index=siblings.findIndex(x=>x.id===n.id);return <div key={n.id} className="rounded border p-3 space-y-2 bg-card" style={{marginLeft:depth*12}} onDragOver={e=>{e.preventDefault();e.dataTransfer.dropEffect="move";}} onDrop={e=>{e.preventDefault();if(drag&&drag!==n.id)move(drag,n.parentId,n.id);setDrag(null);}}>
          <div className="flex items-center gap-2"><span draggable={!busy} onDragStart={e=>{setDrag(n.id);e.dataTransfer.setData("text/plain",n.id);e.dataTransfer.effectAllowed="move";}} onDragEnd={()=>setDrag(null)} className="cursor-grab p-2" title="Arrastar item" aria-label={"Arrastar "+n.label}>⠿</span><Icon className="w-4 h-4 shrink-0"/><input aria-label={"Nome de "+n.label} className="min-w-0 flex-1 rounded border bg-background p-2" value={n.label} maxLength={100} disabled={busy} onChange={e=>patch(n.id,{label:e.target.value})}/><label className="text-sm whitespace-nowrap"><input type="checkbox" disabled={busy} checked={!n.hidden} onChange={e=>patch(n.id,{hidden:!e.target.checked})}/> Mostrar</label></div>
          <div className="flex flex-wrap items-center gap-2 text-sm"><label>Ícone <select className="rounded border bg-background p-1" aria-label={"Ícone de "+n.label} disabled={busy} value={n.icon} onChange={e=>patch(n.id,{icon:e.target.value})}>{Object.keys(SIDEBAR_ICONS).map(icon=><option key={icon}>{icon}</option>)}</select></label><label>Grupo pai <select className="max-w-56 rounded border bg-background p-1" disabled={busy} value={n.parentId??""} onChange={e=>move(n.id,e.target.value||null)}><option value="">Raiz</option>{groups.filter(g=>g.id!==n.id).map(g=><option key={g.id} value={g.id}>{g.label}</option>)}</select></label><button className="rounded border px-2" aria-label={"Mover "+n.label+" para cima"} disabled={busy||index===0} onClick={()=>move(n.id,n.parentId,siblings[index-1]?.id)}>↑</button><button className="rounded border px-2" aria-label={"Mover "+n.label+" para baixo"} disabled={busy||index===siblings.length-1} onClick={()=>move(n.id,n.parentId,siblings[index+2]?.id)}>↓</button></div>
          {n.id.startsWith("page:")&&<p className="text-xs text-muted-foreground break-all">{n.id.slice(5)}</p>}
        </div>;})}
      </section><aside className="space-y-3"><h2 className="font-semibold">Preview com suas permissões</h2><SidebarPreview nodes={preview}/><p className="text-xs text-muted-foreground">Entradas condicionais permanecem sujeitas às condições do ambiente ao executar o menu.</p></aside>
    </div>}
    {config&&<section className="space-y-3"><h2 className="font-semibold">Histórico e restauração</h2><p className="text-sm text-muted-foreground">Restaurar publica uma nova revisão global e preserva o histórico.</p><button className="rounded border px-3 py-2" disabled={busy} onClick={()=>void publish(0)}>Restaurar padrão oficial (revisão 0)</button>{history.map(h=><div key={h.revision} className="flex flex-wrap items-center justify-between gap-3 rounded border p-3"><span>Revisão {h.revision} · {new Date(h.updatedAt+(/Z$/.test(h.updatedAt)?"":"Z")).toLocaleString()} · {h.updatedBy}{h.restoredFrom!==null?" · restaurada de "+h.restoredFrom:""}</span><button className="rounded border px-3 py-1" disabled={busy||h.revision===config.revision} onClick={()=>void publish(h.revision)}>Restaurar</button></div>)}</section>}
  </main>;
}
