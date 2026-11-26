import type { Location } from "react-router-dom";
import type { NavNode } from "./officialNavigation";

/** Query presente no destino faz parte da identidade da folha. */
export function rotaAtiva(to: string, location: Pick<Location, "pathname" | "search">): boolean {
  const separator = to.indexOf("?");
  const caminho = separator >= 0 ? to.slice(0, separator) : to;
  const query = separator >= 0 ? to.slice(separator + 1) : "";
  if (query) {
    if (location.pathname !== caminho) return false;
    const esperado = new URLSearchParams(query);
    const atual = new URLSearchParams(location.search);
    return Array.from(esperado.entries()).every(([key, value]) => atual.get(key) === value);
  }
  return location.pathname === caminho || location.pathname.startsWith(`${caminho}/`);
}

export const noAtivo = (node: NavNode, location: Pick<Location, "pathname" | "search">): boolean =>
  (node.to != null && rotaAtiva(node.to, location))
  || (node.children?.some((child) => noAtivo(child, location)) ?? false);

/**
 * Uma revalidação não deve desmontar a árvore que o usuário está usando.
 * Na primeira carga ainda não existe menu seguro para reter.
 */
export function navigationDuringPermissionLoad(
  current: NavNode[],
  loading: boolean,
  previous: NavNode[],
): NavNode[] {
  return loading && previous.length > 0 ? previous : current;
}

export interface ScrollContainer {
  scrollTop: number;
}

export function restoreSidebarScroll(container: ScrollContainer | null, scrollTop: number): void {
  if (container && container.scrollTop !== scrollTop) container.scrollTop = scrollTop;
}

/** Mudança de rota abre o ancestral ativo, mas jamais recolhe grupos escolhidos pelo usuário. */
export function expandedAfterLocationChange(expanded: boolean, active: boolean, isGroup: boolean): boolean {
  return expanded || (active && isGroup);
}
