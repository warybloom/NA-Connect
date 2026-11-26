import catalog from "./catalog.json";
import { SIDEBAR_ICONS } from "./icons";
import { identify, type NavNode } from "./officialNavigation";
export interface SidebarItem { id: string; parentId: string | null; order: number; label: string; icon: string; hidden: boolean }
export interface SidebarConfiguration { revision: number; items: SidebarItem[] }
export const CATALOG = catalog;
export const DEFAULT_ITEMS: SidebarItem[] = catalog.map(({id,parentId,order,label,icon,hidden})=>({id,parentId,order,label,icon,hidden}));
const official = new Map(catalog.map(n=>[n.id,n]));
export function validateItems(value: unknown, complete = true): SidebarItem[] {
  if (!Array.isArray(value) || value.length > catalog.length || (complete && value.length !== catalog.length)) throw Error("Catálogo incompleto.");
  const map = new Map<string, SidebarItem>();
  for (const n of value) {
    if (!n || typeof n !== "object" || Object.keys(n).some(k=>!["id","parentId","order","label","icon","hidden"].includes(k))
      || !official.has(n.id) || map.has(n.id) || typeof n.label !== "string" || !n.label.trim() || n.label.length > 100 || /[\x00-\x1f\x7f]/.test(n.label)
      || typeof n.icon !== "string" || !Object.hasOwn(SIDEBAR_ICONS,n.icon) || typeof n.hidden !== "boolean" || !Number.isInteger(n.order) || n.order < 0 || n.order >= catalog.length
      || !(n.parentId === null || typeof n.parentId === "string" && official.get(n.parentId)?.group)) throw Error("Item inválido ou duplicado.");
    map.set(n.id,n);
  }
  if(complete && catalog.some(n=>!map.has(n.id)))throw Error("Faltam páginas oficiais.");
  const positions = new Set<string>();
  for (const n of map.values()) {
    const position=JSON.stringify([n.parentId,n.order]); if(positions.has(position))throw Error("Ordem duplicada."); positions.add(position);
    const seen = new Set([n.id]); let p=n.parentId;
    while(p!==null) { if(seen.has(p)||seen.size>=8||!map.has(p))throw Error("Ciclo, pai ausente ou profundidade excessiva."); seen.add(p); p=map.get(p)!.parentId; }
  }
  return value as SidebarItem[];
}
export function applyConfiguration(nodes: NavNode[], config: SidebarConfiguration | null, options: { serverFiltered?: boolean } = {}): NavNode[] {
  const defaults=identify(nodes);
  if (!config) return defaults;
  let items: SidebarItem[];
  try {
    if(!Number.isSafeInteger(config.revision)||config.revision<0)throw Error();
    // Nunca mutar a resposta/cache da revisão publicada ao complementar catálogo antigo em memória.
    items=validateItems(config.items,false).map(n=>({...n}));
    // O GET /api/sidebar entrega uma lista JA FILTRADA pelo backend (hidden + permissao).
    // Nesse caso, ausencia e uma exclusao AUTORITATIVA, nunca um item novo do catalogo.
    // Somente o editor (configuracao completa) pode complementar entradas de versoes antigas.
    if (options.serverFiltered) {
      const available=new Map<string,NavNode>();
      const collect=(ns:NavNode[])=>{for(const n of ns){available.set(n.id!,n);if(n.children)collect(n.children);}};
      collect(defaults);
      const build=(parentId:string|null):NavNode[]=>items
        .filter(n=>n.parentId===parentId&&!n.hidden&&available.has(n.id))
        .sort((a,b)=>a.order-b.order).flatMap(n=>{
          const original=available.get(n.id)!;
          if(original.children){const children=build(n.id);return children.length?[{...original,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[];}
          return [{...original,label:n.label,icon:SIDEBAR_ICONS[n.icon]}];
        });
      return build(null);
    }
    // Catálogo expandido: mescla somente entradas OFICIAIS ausentes, em memória.
    // Preserva a configuração salva sem reordenar, desocultar ou renomear qualquer item existente.
    const ids=new Set(items.map(n=>n.id));
    const missing=catalog.filter(n=>!ids.has(n.id));
    for(const fresh of missing){
      // Réplica do Resumo: inserir imediatamente após a tela original, mesmo
      // quando uma configuração antiga já está publicada no banco.
      // Preserva a ordem relativa, os rótulos e o estado hidden dos demais itens.
      const referencia = fresh.id === "page:/controle-producao/resumo-cronograma"
        ? items.find(n=>n.id==="page:/controle-producao/resumo") : undefined;
      if(referencia){
        const order=referencia.order+1;
        items=items.map(n=>n.parentId===referencia.parentId && n.order>=order ? {...n,order:n.order+1} : n);
        items.push({id:fresh.id,parentId:referencia.parentId,order,
          label:fresh.label,icon:fresh.icon,hidden:fresh.hidden});
      } else {
        const siblings=items.filter(n=>n.parentId===fresh.parentId);
        const order=Math.max(-1,...siblings.map(n=>n.order))+1;
        items.push({id:fresh.id,parentId:fresh.parentId,order,
          label:fresh.label,icon:fresh.icon,hidden:fresh.hidden});
      }
    }
    items=validateItems(items);
  } catch { return options.serverFiltered ? [] : defaults; }
  const available=new Map<string,NavNode>();
  function collect(ns:NavNode[]) { for(const n of ns) {available.set(n.id!,n);if(n.children)collect(n.children);} } collect(defaults);
  function build(parentId:string|null):NavNode[] {
    return items.filter(n=>n.parentId===parentId&&!n.hidden&&available.has(n.id)).sort((a,b)=>a.order-b.order).flatMap(n=>{
      const original=available.get(n.id);
      const custom=n.id.startsWith("custom:");
      if(!original&&!custom)return [];
      if(custom) {const children=build(n.id);return children.length?[{id:n.id,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[];}
      if(original!.children) { const children=build(n.id); return children.length?[{...original!,label:n.label,icon:SIDEBAR_ICONS[n.icon],children}]:[]; }
      return [{...original!,label:n.label,icon:SIDEBAR_ICONS[n.icon]}];
    });
  }
  return build(null);
}
export function moveItem(items:SidebarItem[],id:string,parentId:string|null,beforeId?:string):SidebarItem[] {
  const item=items.find(n=>n.id===id);if(!item)return items;
  const oldParent=item.parentId;
  let next=items.map(n=>n.id===id?{...n,parentId}: {...n});
  for(const parent of new Set([oldParent,parentId])) {
    const siblings=next.filter(n=>n.parentId===parent&&n.id!==id).sort((a,b)=>a.order-b.order);
    if(parent===parentId) {const index=siblings.findIndex(n=>n.id===beforeId);siblings.splice(index<0?siblings.length:index,0,{...item,parentId});}
    siblings.forEach((n,i)=>{ next=next.map(x=>x.id===n.id?{...x,order:i}:x); });
  }
  validateItems(next);return next;
}
