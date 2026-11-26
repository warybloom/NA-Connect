import type { SidebarItem } from "./sidebarModel";

export type VisibilityFilter = "all" | "visible" | "hidden";
export interface SidebarEditorRow { item: SidebarItem; depth: number; hasChildren: boolean; match: boolean }

export const isSidebarGroup = (item: SidebarItem) => !item.id.startsWith("page:");
export const sidebarRoute = (item: SidebarItem) => item.id.startsWith("page:") ? item.id.slice(5) : "";

export function topLevelGroups(items: SidebarItem[]): SidebarItem[] {
  return items.filter(item => item.parentId === null && isSidebarGroup(item)).sort((a, b) => a.order - b.order);
}

export function descendantIds(items: SidebarItem[], rootId: string): Set<string> {
  const result = new Set<string>([rootId]);
  const visit = (parentId: string) => items.filter(item => item.parentId === parentId).forEach(item => {
    if (result.has(item.id)) return;
    result.add(item.id);
    visit(item.id);
  });
  visit(rootId);
  return result;
}

export function filteredTreeRows(items: SidebarItem[], rootId: string | null, query: string, visibility: VisibilityFilter, collapsed: ReadonlySet<string>): SidebarEditorRow[] {
  const scope = rootId ? descendantIds(items, rootId) : new Set(items.map(item => item.id));
  const normalized = query.trim().toLocaleLowerCase("pt-BR");
  const directMatch = (item: SidebarItem) => (visibility === "all" || (visibility === "hidden" ? item.hidden : !item.hidden)) && (!normalized || `${item.label} ${sidebarRoute(item)}`.toLocaleLowerCase("pt-BR").includes(normalized));
  const included = new Set(items.filter(item => scope.has(item.id) && directMatch(item)).map(item => item.id));
  for (const id of [...included]) {
    let parent = items.find(item => item.id === id)?.parentId ?? null;
    while (parent && scope.has(parent)) { included.add(parent); parent = items.find(item => item.id === parent)?.parentId ?? null; }
  }
  const rows: SidebarEditorRow[] = [];
  const visit = (parentId: string | null, depth: number) => items.filter(item => item.parentId === parentId && scope.has(item.id) && included.has(item.id)).sort((a, b) => a.order - b.order).forEach(item => {
    const hasChildren = items.some(child => child.parentId === item.id && scope.has(child.id) && included.has(child.id));
    rows.push({ item, depth, hasChildren, match: directMatch(item) });
    if (!collapsed.has(item.id) || normalized) visit(item.id, depth + 1);
  });
  if (rootId) {
    const root = items.find(item => item.id === rootId);
    if (root && included.has(root.id)) {
      const hasChildren = items.some(child => child.parentId === root.id && included.has(child.id));
      rows.push({ item: root, depth: 0, hasChildren, match: directMatch(root) });
      if (!collapsed.has(root.id) || normalized) visit(root.id, 1);
    }
  } else visit(null, 0);
  return rows;
}

export function visibilityCounts(items: SidebarItem[], rootId: string | null): { visible: number; hidden: number } {
  const scope = rootId ? descendantIds(items, rootId) : new Set(items.map(item => item.id));
  return items.filter(item => scope.has(item.id)).reduce((counts, item) => { counts[item.hidden ? "hidden" : "visible"]++; return counts; }, { visible: 0, hidden: 0 });
}
