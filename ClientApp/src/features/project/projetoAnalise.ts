// Regras PURAS do multiprojeto (PrimaryProject + projetos de análise). Sem React — testadas em __tests__.
export interface ProjetoRef { hubId: string; projectId: string; nome?: string }

const mesmo = (a: ProjetoRef, b: ProjetoRef) => a.projectId === b.projectId && a.hubId === b.hubId;

/**
 * Seleção analítica válida: sem duplicados; vazia → [principal]; sem principal → vazia. O principal pode ser
 * desmarcado pelo usuário (diretoria comparando outros projetos), mas a lista nunca fica vazia.
 */
export function normalizarAnalise(principal: ProjetoRef | null, lista: readonly ProjetoRef[]): ProjetoRef[] {
  if (!principal) return [];
  const unicos: ProjetoRef[] = [];
  for (const p of lista) if (p?.hubId && p?.projectId && !unicos.some((u) => mesmo(u, p))) unicos.push(p);
  return unicos.length ? unicos : [principal];
}

/** Trocar o projeto principal zera a análise para [novo principal]; só atualizar o nome do mesmo projeto mantém. */
export function analiseAoTrocarPrincipal(anterior: ProjetoRef | null, novo: ProjetoRef, analise: readonly ProjetoRef[]): ProjetoRef[] {
  if (anterior && mesmo(anterior, novo)) return normalizarAnalise(novo, analise.map((p) => (mesmo(p, novo) ? { ...p, nome: novo.nome ?? p.nome } : p)));
  return [novo];
}

/** Formato consumido pelas telas analíticas/PMO (PmoSelecionado). */
export const paraSelecao = (lista: readonly ProjetoRef[]) =>
  lista.map((p) => ({ id: p.projectId, hubId: p.hubId, name: p.nome ?? p.projectId }));

/** Filtra candidatos pelas permissões resolvidas POR PROJETO (projeto sem resposta = fora). */
export function projetosPermitidos<T extends { id: string }>(candidatos: readonly T[], permitidoPorProjeto: ReadonlyMap<string, boolean> | null): T[] {
  if (!permitidoPorProjeto) return [...candidatos];               // sistema não aplica permissões (safe mode / master)
  return candidatos.filter((c) => permitidoPorProjeto.get(c.id) === true);
}
