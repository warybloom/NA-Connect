import type { SidebarConfiguration, SidebarItem } from "./sidebarModel";
export interface SidebarAccess { canManage: boolean; allowed: boolean; email?: string }
export interface SidebarHistory { revision: number; updatedAt: string; updatedBy: string; restoredFrom: number | null }
/** session=401, forbidden=403, database=503, missing=404 (rota ausente no backend em execução), network=sem resposta/proxy. */
export type SidebarErrorKind = "session" | "forbidden" | "database" | "missing" | "network" | "other";
export class SidebarApiError extends Error {
  constructor(message: string, readonly kind: SidebarErrorKind, readonly status: number) { super(message); }
}
function kindOf(status: number): SidebarErrorKind {
  if (status === 401) return "session";
  if (status === 403) return "forbidden";
  if (status === 503) return "database";
  if (status === 404) return "missing";
  // 500/502/504 sem corpo JSON = proxy do Vite sem backend em 5277.
  if (status === 500 || status === 502 || status === 504) return "network";
  return "other";
}
const FALLBACK: Record<SidebarErrorKind, string> = {
  session: "Sessão expirada. Entre novamente.",
  forbidden: "Você não tem permissão para gerenciar o menu oficial.",
  database: "Banco de dados indisponível.",
  missing: "A API do Gerenciador de Sidebar não foi encontrada no backend em execução.",
  network: "Sem conexão com a API do N&A Connect.",
  other: "Menu indisponível.",
};
export async function sidebarRequest<T>(path: string, methodOrBody: string | unknown = "GET", body?: unknown, token?: string, signal?: AbortSignal): Promise<T> {
  let method=typeof methodOrBody==="string"?methodOrBody:"PUT";
  if(typeof methodOrBody!=="string") {body=methodOrBody;token=(await sidebarRequest<{token:string}>("/token")).token;}
  let r:Response;
  try {
    r=await fetch("/api/sidebar"+path,{method,credentials:"same-origin",signal,headers:{Accept:"application/json",...(body!==undefined?{"Content-Type":"application/json"}:{}),...(token?{"X-Sidebar-CSRF":token}:{})},...(body!==undefined?{body:JSON.stringify(body)}:{})});
  } catch(e) {
    if(signal?.aborted) throw e;
    throw new SidebarApiError(FALLBACK.network,"network",0);
  }
  if(!r.ok) {
    const message=await r.json().then(b=>typeof b?.message==="string"?b.message:null).catch(()=>null);
    const kind=message&&r.status>=500&&r.status!==503?"other":kindOf(r.status);
    throw new SidebarApiError(message || FALLBACK[kind]+(kind==="other"?" (HTTP "+r.status+")":""),kind,r.status);
  }
  return r.json();
}
export function published(config: SidebarConfiguration) {window.dispatchEvent(new CustomEvent("sidebar-published",{detail:config.revision}));}
export const publishSidebar=(revision:number,items:SidebarItem[])=>sidebarRequest<SidebarConfiguration>("/publish",{revision,items});
