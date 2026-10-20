// Compatibility exports share the validated official model; unknown IDs/icons cannot create navigation.
import type { NavNode } from "./officialNavigation";
import { SIDEBAR_ICONS } from "./icons";
import { applyConfiguration } from "./sidebarModel";
export type { SidebarItem, SidebarConfiguration } from "./sidebarModel";
import type { SidebarItem } from "./sidebarModel";
export type SidebarCatalogItem = SidebarItem & { to: string | null; group: boolean; permissionKeys: string[]; badgeInline?: unknown; badgeAbaixo?: unknown };
export const iconNames=Object.keys(SIDEBAR_ICONS);
export const resolveIcon=(key:string)=>Object.hasOwn(SIDEBAR_ICONS,key)?SIDEBAR_ICONS[key]:SIDEBAR_ICONS.FolderOpen;
export const configuredNavigation=(original:NavNode[],items:SidebarItem[])=>applyConfiguration(original,{revision:1,items});
export async function sidebarFetch<T>(path="",init?:RequestInit):Promise<T> {
  const r=await fetch("/api/sidebar"+path,{credentials:"include",...init});
  if(!r.ok)throw Error("Não foi possível acessar o menu oficial (HTTP "+r.status+").");return r.json();
}
