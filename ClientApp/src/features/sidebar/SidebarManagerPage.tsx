import { useEffect, useMemo, useRef, useState } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { ArrowDown, ArrowUp, ChevronDown, ChevronRight, Eye, EyeOff, GripVertical, Search } from "lucide-react";
import { SidebarPreview } from "@/components/Layout";
import { AcessoNegado } from "../permissions/PermissionRouteGuard";
import { filtrarPorPermissao } from "../permissions/permissionLogic";
import { usePermissions, useProjectPermissions } from "../permissions/PermissionProvider";
import { useProjetoAtual } from "../project/ProjetoAtualContext";
import { contextualNavigation } from "./officialNavigation";
import { applyConfiguration, DEFAULT_ITEMS, moveItem, validateItems, type SidebarConfiguration, type SidebarItem } from "./sidebarModel";
import { SIDEBAR_ICONS } from "./icons";
import { getSidebarProfile, listSidebarProfiles, published, publishSidebarProfile, SidebarApiError, sidebarRequest, type SidebarErrorKind, type SidebarProfileDetail, type SidebarProfileSummary } from "./sidebarApi";
import { ReportState } from "@/components/reports/ReportComponents";
import { useSidebarManagerAccess } from "./useOfficialSidebar";
import { filteredTreeRows, isSidebarGroup, sidebarRoute, topLevelGroups, visibilityCounts, type VisibilityFilter } from "./sidebarEditorModel";
import { SidebarManagerIconPicker } from "./SidebarManagerIconPicker";
import { canPublishProfile, completeCatalogItems, fullyVisibleDefault, GLOBAL_SCOPE, isDirty, previewAsMasterAdmin, profileBaseConfiguration, scopeKey, scopeLabel, stashDraft, takeDraft, type ScopeDrafts, type SidebarScope } from "./sidebarProfileScope";
import { SidebarScopeBar } from "./SidebarScopeBar";
interface History { revision:number;updatedAt:string;updatedBy:string;restoredFrom:number|null }
// Falha da API nunca vira "sem permissão": cada causa tem sua mensagem.
const ACCESS_ERRORS:Record<SidebarErrorKind,[string,string]>={
  session:["Sessão expirada","Entre novamente para abrir o Gerenciador de Sidebar."],
  forbidden:["Sem permissão administrativa","É necessário MasterAdmin ou permissions.manage global."],
  conflict:["Conflito de revisão","O menu mudou durante a verificação. Tente novamente."],
  database:["Banco de dados indisponível","Não foi possível consultar DB_NA_CONNECT. Verifique o SQL Server e tente novamente."],
  missing:["Gerenciador indisponível no backend","O backend em execução não possui a API /api/sidebar — provavelmente está desatualizado. Reinicie o backend com a versão atual."],
  network:["Falha de conexão com a API","O backend (porta 5277) não respondeu. Verifique se ele está em execução."],
  other:["Falha ao verificar acesso","Tente novamente em instantes."],
};
interface AdminResponse {configuration:SidebarConfiguration;csrfToken:string}
export default function SidebarManagerPage() {
  const reduceMotion=useReducedMotion();
  const {effective}=usePermissions();const [comparison,setComparison]=useState(false);const viewerMaster=effective?.isMasterAdmin===true;
  useEffect(()=>{const ctrl=new AbortController();fetch("/api/production-audit/experimental-summary/access",{credentials:"include",signal:ctrl.signal}).then(r=>r.ok?r.json():{allowed:false}).then(r=>{if(!ctrl.signal.aborted)setComparison(r.allowed===true);}).catch(()=>{if(!ctrl.signal.aborted)setComparison(false);});return()=>ctrl.abort();},[effective]);
  const access=useSidebarManagerAccess();const {projetoAtual}=useProjetoAtual();const {can,loading:permissionLoading}=useProjectPermissions(projetoAtual?.projectId);
  const [config,setConfig]=useState<SidebarConfiguration|null>(null);const [items,setItems]=useState<SidebarItem[]>([]);
  const [token,setToken]=useState("");const [history,setHistory]=useState<History[]>([]);const [busy,setBusy]=useState(false);const [message,setMessage]=useState("");const [epoch,setEpoch]=useState(0);const [drag,setDrag]=useState<string|null>(null);const [dragPreviewItems,setDragPreviewItems]=useState<SidebarItem[]|null>(null);
  const [drop,setDrop]=useState<{id:string;position:"before"|"inside"|"after"}|null>(null);const [selectedRoot,setSelectedRoot]=useState("");const [query,setQuery]=useState("");const [visibility,setVisibility]=useState<VisibilityFilter>("all");const [collapsed,setCollapsed]=useState<Set<string>>(()=>new Set());
  // Escopo GLOBAL (legado) ou layout independente de um perfil; rascunho não publicado fica preso à chave do seu escopo.
  const [scope,setScope]=useState<SidebarScope>(GLOBAL_SCOPE);const key=scopeKey(scope);const [drafts,setDrafts]=useState<ScopeDrafts>({});const restored=useRef<string|null>(null);
  const [profiles,setProfiles]=useState<SidebarProfileSummary[]>([]);const [profilesLoading,setProfilesLoading]=useState(false);const [profilesError,setProfilesError]=useState<SidebarApiError|null>(null);const [detail,setDetail]=useState<SidebarProfileDetail|null>(null);
  useEffect(()=>{if(restored.current===key){restored.current=null;return;}
    setConfig(null);setItems([]);setToken("");setDetail(null);if(scope.kind==="global")setHistory([]);if(!access.allowed)return;const ctrl=new AbortController();setBusy(true);
    const load=scope.kind==="global"
      ?Promise.all([sidebarRequest<AdminResponse>("/admin","GET",undefined,undefined,ctrl.signal),sidebarRequest<History[]>("/history","GET",undefined,undefined,ctrl.signal)]).then(([r,h])=>{
        validateItems(r.configuration.items);if(!ctrl.signal.aborted){setConfig(r.configuration);setItems(r.configuration.items);setToken(r.csrfToken);setHistory(h);setMessage("");}})
      :getSidebarProfile(scope.profileId,ctrl.signal).then(d=>{const base=profileBaseConfiguration(d);if(!ctrl.signal.aborted){setDetail(d);setConfig(base);setItems(base.items.map(n=>({...n})));setToken(d.csrfToken);setMessage("");}});
    load.catch(e=>{if(!ctrl.signal.aborted)setMessage(e.message);}).finally(()=>{if(!ctrl.signal.aborted)setBusy(false);});return()=>ctrl.abort();},[access.allowed,epoch,key]);
  useEffect(()=>{setProfiles([]);setProfilesError(null);if(!access.allowed)return;const ctrl=new AbortController();setProfilesLoading(true);
    listSidebarProfiles(ctrl.signal).then(p=>{if(!ctrl.signal.aborted)setProfiles(p.filter(x=>x.ativo!==false));}).catch(e=>{if(!ctrl.signal.aborted)setProfilesError(e instanceof SidebarApiError?e:new SidebarApiError("Não foi possível listar os perfis.","other",0));}).finally(()=>{if(!ctrl.signal.aborted)setProfilesLoading(false);});
    return()=>ctrl.abort();},[access.allowed,epoch]);
  const dirty=!!config&&isDirty(config.items,items);const unpublished=dirty||Object.keys(drafts).length>0;
  useEffect(()=>{if(!unpublished)return;const warn=(e:BeforeUnloadEvent)=>{e.preventDefault();e.returnValue="";};window.addEventListener("beforeunload",warn);return()=>window.removeEventListener("beforeunload",warn);},[unpublished]);
  const selectedProfile=scope.kind==="profile"?profiles.find(p=>p.profileId===scope.profileId)??null:null;
  const profileLocked=scope.kind==="profile"&&!!detail&&!canPublishProfile(detail,viewerMaster);
  const pendingDrafts=useMemo(()=>[...Object.keys(drafts),...(dirty?[key]:[])].map(k=>({key:k,label:scopeLabel(k,profiles)})),[drafts,dirty,key,profiles]);
  const preview=useMemo(()=>{
    // Preview de perfil não-Master nunca promete telas Master-only, mesmo para quem administra como Master.
    const tree=contextualNavigation(comparison,effective?.enforced ?? false,previewAsMasterAdmin(scope,detail??selectedProfile,viewerMaster));
    return permissionLoading?[]:filtrarPorPermissao(applyConfiguration(tree,{revision:config?.revision??0,items}),can);
  },[items,config?.revision,can,permissionLoading,effective,comparison,scope,detail,selectedProfile,viewerMaster]);
  function switchScope(next:SidebarScope){const nextKey=scopeKey(next);if(nextKey===key||busy)return;
    const leaving=config?{config,items,token,detail}:null;const leftDirty=!!leaving&&isDirty(leaving.config.items,leaving.items);
    const [draft,rest]=takeDraft(stashDraft(drafts,key,leaving),nextKey);setDrafts(rest);
    if(draft){restored.current=nextKey;setConfig(draft.config);setItems(draft.items);setToken(draft.token);setDetail(draft.detail);}
    setDrag(null);setDrop(null);setDragPreviewItems(null);setScope(next);
    setMessage([leftDirty?"Rascunho não publicado de “"+scopeLabel(key,profiles)+"” preservado; ele não foi aplicado a “"+scopeLabel(nextKey,profiles)+"”.":"",draft?"Rascunho não publicado de “"+scopeLabel(nextKey,profiles)+"” restaurado.":""].filter(Boolean).join(" "));}
  async function copyGlobal(){if(busy)return;setBusy(true);setMessage("");try{const r=await sidebarRequest<AdminResponse>("/admin");setItems(completeCatalogItems(r.configuration.items));setMessage("Menu global publicado (revisão "+r.configuration.revision+") copiado para o rascunho deste perfil. Nada foi publicado.");}
    catch(e){setMessage(e instanceof Error?e.message:"Não foi possível ler o menu global.");}finally{setBusy(false);}}
  const displayedItems=dragPreviewItems??items;
  const roots=useMemo(()=>topLevelGroups(items),[items]);
  const counts=useMemo(()=>visibilityCounts(items,selectedRoot||null),[items,selectedRoot]);
  const rows=useMemo(()=>filteredTreeRows(displayedItems,selectedRoot||null,query,visibility,collapsed),[displayedItems,selectedRoot,query,visibility,collapsed]);
  function patch(id:string,change:Partial<SidebarItem>){setItems(current=>current.map(n=>n.id===id?{...n,...change}:n));}
  function move(id:string,parent:string|null,before?:string){try{setItems(current=>moveItem(current,id,parent,before));setMessage("");}catch(e){setMessage(e instanceof Error?e.message:"Movimento inválido.");}}
  function previewDrop(source:string,target:SidebarItem,position:"before"|"inside"|"after"){
    if(source===target.id)return;
    const base=dragPreviewItems??items;
    try{
      if(position==="inside"){
        if(!isSidebarGroup(target))return;
        setDragPreviewItems(moveItem(base,source,target.id));
      }else{
        const siblings=base.filter(n=>n.parentId===target.parentId&&n.id!==source).sort((a,b)=>a.order-b.order);
        const index=siblings.findIndex(n=>n.id===target.id);
        setDragPreviewItems(moveItem(base,source,target.parentId,position==="before"?target.id:siblings[index+1]?.id));
      }
      setMessage("");
    }catch(e){setMessage(e instanceof Error?e.message:"Movimento inválido.");}
  }
  function finishDrag(commit:boolean){if(commit&&dragPreviewItems)setItems(dragPreviewItems);setDrag(null);setDrop(null);setDragPreviewItems(null);}
  async function publishProfile(){if(!config||!detail||busy)return;
    if(!canPublishProfile(detail,viewerMaster)){setMessage("Somente Master Admin pode publicar o layout do perfil "+detail.profileName+".");return;}
    setBusy(true);setMessage("");try{
      const next=await publishSidebarProfile(detail.profileId,config.revision,validateItems(items),token);
      const saved={revision:next.revision,items:completeCatalogItems(next.items)};
      setConfig(saved);setItems(saved.items.map(n=>({...n})));setDetail({...detail,configured:true,configuration:saved});
      setProfiles(current=>current.map(p=>p.profileId===detail.profileId?{...p,configured:true,revision:next.revision}:p));published(next);
      setMessage("Menu do perfil "+detail.profileName+" publicado. Revisão "+next.revision+". Menu global e demais perfis não foram alterados.");
    }catch(e){setMessage(e instanceof SidebarApiError&&e.kind==="conflict"
      ?"Conflito: o layout de "+detail.profileName+" foi publicado por outra pessoa depois que você o carregou (revisão esperada "+config.revision+"). Seu rascunho continua nesta tela; use “Recarregar” para obter a versão atual (descarta o rascunho)."
      :e instanceof Error?e.message:"Não foi possível publicar o perfil.");}finally{setBusy(false);}}
  async function publish(source?:number){if(!config||busy)return;if(scope.kind==="profile"){if(source===undefined)await publishProfile();return;}setBusy(true);setMessage("");try{
    const next=source===undefined?await sidebarRequest<SidebarConfiguration>("/publish","PUT",{revision:config.revision,items:validateItems(items)},token):await sidebarRequest<SidebarConfiguration>("/restore","POST",{revision:config.revision,sourceRevision:source},token);
    setConfig(next);setItems(next.items);published(next);setHistory(await sidebarRequest<History[]>("/history"));setMessage("Menu oficial publicado globalmente. Revisão "+next.revision+".");
  }catch(e){setMessage(e instanceof Error?e.message:"Não foi possível publicar.");}finally{setBusy(false);}}
  if(access.loading)return <main className="p-6"><ReportState title="Verificando acesso ao gerenciador…">Consultando suas permissões globais.</ReportState></main>;
  if(access.error){const e=access.error;const [title,hint]=ACCESS_ERRORS[e.kind];
    return <main className="p-6"><ReportState title={title} error><p>{e.message}</p><p className="mt-2 text-sm">{hint}</p>
      {e.kind==="session"?<a className="mt-3 inline-block rounded border px-4 py-2" href="/login">Entrar novamente</a>:<button className="mt-3 rounded border px-4 py-2" onClick={()=>window.location.reload()}>Tentar novamente</button>}</ReportState></main>;}
  if(!access.allowed)return <AcessoNegado detalhe={"A conta "+(access.email??"atual")+" não possui MasterAdmin nem permissions.manage global. Permissões concedidas apenas em projeto não habilitam o Gerenciador de Sidebar. Solicite a concessão a um administrador."} />;
  return <main className="p-6 space-y-6">
    <div><h1 className="text-2xl font-bold">Gerenciador de Sidebar</h1><p className="text-sm text-muted-foreground">{scope.kind==="global"?<>Menu oficial global · revisão {config?.revision??"—"}</>:<>Menu do perfil {detail?.profileName??selectedProfile?.name??"—"} · {detail?.configured?"revisão "+detail.configuration.revision:"sem layout próprio"}</>}. As permissões do usuário continuam sendo aplicadas.</p></div>
    <SidebarScopeBar scope={scope} profiles={profiles} profilesLoading={profilesLoading} profilesError={profilesError} detail={detail} viewerIsMasterAdmin={viewerMaster} pendingDrafts={pendingDrafts} disabled={busy} onScopeChange={switchScope}/>
    <div className="flex flex-wrap items-center gap-3"><button className="rounded border px-4 py-2" disabled={busy} onClick={()=>setEpoch(x=>x+1)} title="Recarrega o escopo atual e descarta o rascunho dele">Recarregar</button><button className="rounded border px-4 py-2" disabled={busy||!config} onClick={()=>setItems(scope.kind==="global"?DEFAULT_ITEMS.map(n=>({...n})):fullyVisibleDefault())}>Padrão oficial no rascunho</button>{scope.kind==="profile"&&<button className="rounded border px-4 py-2" disabled={busy||!config} onClick={()=>void copyGlobal()}>Copiar menu global no rascunho</button>}<button className="rounded bg-primary text-primary-foreground px-4 py-2 disabled:opacity-50" disabled={busy||!config||profileLocked} onClick={()=>void publish()}>{busy?"Aguarde…":scope.kind==="global"?"Publicar menu global":"Publicar perfil "+(detail?.profileName??"")}</button>{dirty&&<span className="text-sm text-amber-700 dark:text-amber-300">Alterações não publicadas neste escopo.</span>}</div>
    {message&&<p role="status" className="rounded border p-3">{message}</p>}
    {!config&&busy&&<ReportState title="Carregando configuração…">{scope.kind==="global"?"Lendo o menu oficial publicado e o histórico.":"Lendo o layout do perfil."}</ReportState>}
    {!config&&!busy&&<p>{scope.kind==="global"?"Não foi possível carregar a configuração. Use “Recarregar”; se o erro for de banco, verifique as tabelas dbo.SidebarOfficial em DB_NA_CONNECT.":"Não foi possível carregar o layout do perfil. Nada pode ser publicado até o backend responder; se o erro for de banco, o schema dbo.SidebarProfileConfiguration pode não estar aplicado."}</p>}
    {config&&<div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <section className="space-y-3 min-w-0"><div className="rounded-lg border bg-card p-3 space-y-3"><div className="grid gap-3 md:grid-cols-[minmax(180px,1fr)_minmax(220px,1.4fr)_auto]">
        <label className="text-sm">Grupo principal<select className="mt-1 block w-full rounded border bg-background p-2" value={selectedRoot} onChange={e=>setSelectedRoot(e.target.value)}><option value="">Todos</option>{roots.map(root=><option key={root.id} value={root.id}>{root.label}</option>)}</select></label>
        <label className="text-sm">Buscar por nome ou rota<span className="mt-1 flex items-center gap-2 rounded border bg-background px-2"><Search className="h-4 w-4"/><input className="min-w-0 flex-1 bg-transparent py-2 outline-none" value={query} onChange={e=>setQuery(e.target.value)} placeholder="Ex.: Curva S"/></span></label>
        <label className="text-sm">Exibição<select className="mt-1 block w-full rounded border bg-background p-2" value={visibility} onChange={e=>setVisibility(e.target.value as VisibilityFilter)}><option value="all">Todos</option><option value="visible">Somente visíveis</option><option value="hidden">Somente ocultos</option></select></label>
      </div><div className="flex flex-wrap justify-between gap-2 text-sm text-muted-foreground"><span>{rows.length} nós na árvore · {counts.visible} visíveis · {counts.hidden} ocultos</span><span>Solte antes, dentro ou depois. Use “Grupo pai” como alternativa.</span></div></div>
        <div role="tree" aria-label="Estrutura da sidebar" className="space-y-1 rounded-lg border bg-muted/20 p-2">{rows.length===0&&<p className="p-5 text-center text-sm text-muted-foreground">Nenhum item encontrado.</p>}
        {rows.map(({item:n,depth,hasChildren,match})=>{const Icon=SIDEBAR_ICONS[n.icon];const siblings=displayedItems.filter(x=>x.parentId===n.parentId).sort((a,b)=>a.order-b.order);const index=siblings.findIndex(x=>x.id===n.id);const closed=collapsed.has(n.id);const isDragging=drag===n.id;return <motion.div layout={!reduceMotion} transition={reduceMotion?{duration:0}:{layout:{duration:.2,ease:"easeOut"}}} key={n.id} role="treeitem" aria-expanded={hasChildren?!closed:undefined} className={`relative rounded-xl border-l-4 p-2.5 transition-[background-color,border-color,box-shadow,opacity] ${isSidebarGroup(n)?"border-l-primary/70 bg-card":"border-y-transparent border-r-transparent border-l-border bg-background/70"} ${match?"":"opacity-60"} ${isDragging?"z-20 scale-[1.01] cursor-grabbing opacity-70 shadow-xl ring-1 ring-primary/30":"hover:bg-muted/50"}`} style={{marginLeft:Math.min(depth,6)*18}} onDragOver={e=>{e.preventDefault();if(!drag)return;const rect=e.currentTarget.getBoundingClientRect();const ratio=(e.clientY-rect.top)/rect.height;const position=!isSidebarGroup(n)?(ratio<.5?"before":"after"):ratio<.3?"before":ratio>.7?"after":"inside";if(drop?.id!==n.id||drop.position!==position){setDrop({id:n.id,position});previewDrop(drag,n,position);}e.dataTransfer.dropEffect="move";}} onDrop={e=>{e.preventDefault();finishDrag(true);}}>
          {drop?.id===n.id&&<div className={`pointer-events-none absolute z-10 ${drop.position==="inside"?"inset-0 rounded-xl ring-2 ring-primary/70":"left-2 right-2 h-0.5 rounded-full bg-primary "+(drop.position==="before"?"-top-1":"-bottom-1")}`}/>}<div className="flex items-center gap-1.5"><button type="button" className="rounded-full p-1 hover:bg-muted disabled:opacity-20" disabled={!hasChildren} aria-label={(closed?"Expandir ":"Recolher ")+n.label} onClick={()=>setCollapsed(current=>{const next=new Set(current);next.has(n.id)?next.delete(n.id):next.add(n.id);return next;})}>{closed?<ChevronRight className="h-4 w-4"/>:<ChevronDown className="h-4 w-4"/>}</button><span draggable={!busy} onDragStart={e=>{setDrag(n.id);setDragPreviewItems(items);e.dataTransfer.setData("text/plain",n.id);e.dataTransfer.effectAllowed="move";}} onDragEnd={()=>finishDrag(false)} className="cursor-grab touch-none rounded-full p-1 text-muted-foreground hover:bg-muted hover:text-foreground active:cursor-grabbing" title="Arrastar item" aria-label={"Arrastar "+n.label}><GripVertical className="h-4 w-4"/></span><span className={`rounded-lg p-1.5 ${isSidebarGroup(n)?"bg-primary/10 text-primary":"bg-muted text-muted-foreground"}`}><Icon className="h-4 w-4 shrink-0"/></span><input aria-label={"Nome de "+n.label} className="min-w-24 flex-1 border-0 border-b border-transparent bg-transparent px-2 py-1.5 font-medium outline-none hover:border-border focus:border-primary" value={n.label} maxLength={100} disabled={busy} onChange={e=>patch(n.id,{label:e.target.value})}/><button type="button" disabled={busy} onClick={()=>patch(n.id,{hidden:!n.hidden})} aria-label={(n.hidden?"Mostrar ":"Ocultar ")+n.label} className={`inline-flex items-center gap-1 rounded-full px-2 py-1 text-xs ${n.hidden?"bg-muted text-muted-foreground":"bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"}`}>{n.hidden?<EyeOff className="h-3.5 w-3.5"/>:<Eye className="h-3.5 w-3.5"/>}<span className="hidden sm:inline">{n.hidden?"Oculto":"Visível"}</span></button></div>
          <div className="mt-2 flex flex-wrap items-center gap-2 pl-8 text-xs"><SidebarManagerIconPicker value={n.icon} itemLabel={n.label} disabled={busy} onChange={icon=>patch(n.id,{icon})}/><label className="inline-flex items-center gap-1 text-muted-foreground"><span>Grupo pai</span><select aria-label={"Grupo pai de "+n.label} className="max-w-52 rounded-full border bg-background px-2 py-1.5 text-foreground" disabled={busy} value={n.parentId??""} onChange={e=>move(n.id,e.target.value||null)}><option value="">Raiz</option>{items.filter(g=>isSidebarGroup(g)&&g.id!==n.id).map(g=><option key={g.id} value={g.id}>{g.label}</option>)}</select></label><button className="rounded-full border p-1.5 hover:bg-muted" title="Mover para cima" aria-label={"Mover "+n.label+" para cima"} disabled={busy||index===0} onClick={()=>move(n.id,n.parentId,siblings[index-1]?.id)}><ArrowUp className="h-3.5 w-3.5"/></button><button className="rounded-full border p-1.5 hover:bg-muted" title="Mover para baixo" aria-label={"Mover "+n.label+" para baixo"} disabled={busy||index===siblings.length-1} onClick={()=>move(n.id,n.parentId,siblings[index+2]?.id)}><ArrowDown className="h-3.5 w-3.5"/></button>{sidebarRoute(n)&&<span title={sidebarRoute(n)} className="min-w-0 max-w-full truncate text-muted-foreground sm:max-w-80">{sidebarRoute(n)}</span>}</div>
        </motion.div>;})}</div>
      </section><aside className="space-y-3"><h2 className="font-semibold">{scope.kind==="global"?"Preview com suas permissões":"Preview de visibilidade do perfil"}</h2><SidebarPreview nodes={preview}/><p className="text-xs text-muted-foreground">Entradas condicionais permanecem sujeitas às condições do ambiente ao executar o menu.</p>{scope.kind==="profile"&&<p className="text-xs text-muted-foreground">O preview combina este layout com as SUAS permissões. Para quem recebe o perfil, as permissões do usuário continuam sendo aplicadas: o layout só esconde itens, nunca libera acesso.</p>}<p className="text-xs text-muted-foreground">O atalho “Administrar Sidebar” fica fora deste editor e nunca é ocultado.</p></aside>
    </div>}
    {config&&scope.kind==="profile"&&<section className="space-y-2"><h2 className="font-semibold">Histórico do perfil</h2><p className="text-sm text-muted-foreground">{detail?.configured?"Revisão atual "+detail.configuration.revision+". Cada publicação grava revisão, autor e data no histórico do perfil (backend).":"Este perfil ainda não tem layout próprio; a primeira publicação cria a revisão 1."} A restauração de revisões anteriores por perfil não faz parte do contrato atual.</p></section>}
    {config&&scope.kind==="global"&&<section className="space-y-3"><h2 className="font-semibold">Histórico e restauração</h2><p className="text-sm text-muted-foreground">Restaurar publica uma nova revisão global e preserva o histórico.</p><button className="rounded border px-3 py-2" disabled={busy} onClick={()=>void publish(0)}>Restaurar padrão oficial (revisão 0)</button>{history.map(h=><div key={h.revision} className="flex flex-wrap items-center justify-between gap-3 rounded border p-3"><span>Revisão {h.revision} · {new Date(h.updatedAt+(/Z$/.test(h.updatedAt)?"":"Z")).toLocaleString()} · {h.updatedBy}{h.restoredFrom!==null?" · restaurada de "+h.restoredFrom:""}</span><button className="rounded border px-3 py-1" disabled={busy||h.revision===config.revision} onClick={()=>void publish(h.revision)}>Restaurar</button></div>)}</section>}
  </main>;
}
