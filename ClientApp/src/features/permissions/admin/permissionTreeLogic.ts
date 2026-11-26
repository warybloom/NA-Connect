// Árvore do catálogo (GET /api/permissions/catalog) — lógica pura usada pela tela de administração.
// Marcar um pai marca todos os descendentes; desmarcar desmarca todos. Filhos continuam configuráveis um a um.

export interface CatalogResource {
  key: string;
  parentKey: string | null;
  label: string;
  type: string;
  scope: "GLOBAL" | "PROJECT";
  route: string | null;
  sortOrder: number;
}

export interface TreeNode { resource: CatalogResource; children: TreeNode[] }

export function montarArvore(catalogo: readonly CatalogResource[]): TreeNode[] {
  const porPai = new Map<string | null, CatalogResource[]>();
  const keys = new Set(catalogo.map((r) => r.key));
  for (const r of catalogo) {
    const pai = r.parentKey && keys.has(r.parentKey) ? r.parentKey : null;
    porPai.set(pai, [...(porPai.get(pai) ?? []), r]);
  }
  const montar = (pai: string | null): TreeNode[] =>
    (porPai.get(pai) ?? [])
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((resource) => ({ resource, children: montar(resource.key) }));
  return montar(null);
}

export function descendentes(node: TreeNode): string[] {
  return node.children.flatMap((c) => [c.resource.key, ...descendentes(c)]);
}

/** Alterna a key e TODOS os descendentes para o mesmo estado. */
export function alternar(selecionadas: ReadonlySet<string>, node: TreeNode, marcar: boolean): Set<string> {
  const proximo = new Set(selecionadas);
  for (const k of [node.resource.key, ...descendentes(node)]) {
    if (marcar) proximo.add(k);
    else proximo.delete(k);
  }
  return proximo;
}

export type EstadoNo = "todos" | "alguns" | "nenhum";

/** Estado visual: "alguns" = nó ou parte dos descendentes marcados (checkbox indeterminado). */
export function estadoDoNo(selecionadas: ReadonlySet<string>, node: TreeNode): EstadoNo {
  const todas = [node.resource.key, ...descendentes(node)];
  const marcadas = todas.filter((k) => selecionadas.has(k)).length;
  if (marcadas === 0) return "nenhum";
  return marcadas === todas.length ? "todos" : "alguns";
}

/** Filtro de busca: mantém o nó se ele ou algum descendente casar com o texto. */
export function filtrarArvore(nodes: readonly TreeNode[], texto: string): TreeNode[] {
  const t = texto.trim().toLowerCase();
  if (!t) return nodes.slice();
  const out: TreeNode[] = [];
  for (const n of nodes) {
    const casa = n.resource.label.toLowerCase().includes(t) || n.resource.key.toLowerCase().includes(t);
    const filhos = filtrarArvore(n.children, t);
    if (casa) out.push(n);
    else if (filhos.length > 0) out.push({ ...n, children: filhos });
  }
  return out;
}

/** Contador do grupo: quantas keys (o próprio nó + descendentes) estão marcadas, de quantas. */
export function contagem(selecionadas: ReadonlySet<string>, node: TreeNode): { marcadas: number; total: number } {
  const todas = [node.resource.key, ...descendentes(node)];
  return { marcadas: todas.filter((k) => selecionadas.has(k)).length, total: todas.length };
}

/** Diferença entre o estado salvo e o editado (para "Alterações não salvas" e para o salvamento). */
export function diferenca(salvas: readonly string[], editadas: ReadonlySet<string>): { adicionadas: string[]; removidas: string[] } {
  const base = new Set(salvas);
  return {
    adicionadas: [...editadas].filter((k) => !base.has(k)).sort(),
    removidas: [...base].filter((k) => !editadas.has(k)).sort(),
  };
}
