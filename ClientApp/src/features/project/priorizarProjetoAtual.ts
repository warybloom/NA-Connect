// Listas de projetos que abrem com o PROJETO ATUAL em primeiro lugar (os demais continuam visíveis, na ordem original).
const norm = (id: string | null | undefined) => (id ? (id.startsWith("b.") ? id : `b.${id}`).toLowerCase() : "");

export function priorizarProjetoAtual<T>(lista: readonly T[], projetoAtualId: string | null | undefined, idDe: (item: T) => string | null | undefined): T[] {
  const atual = norm(projetoAtualId);
  if (!atual) return [...lista];
  const primeiro = lista.filter((p) => norm(idDe(p)) === atual);
  return primeiro.length === 0 ? [...lista] : [...primeiro, ...lista.filter((p) => norm(idDe(p)) !== atual)];
}
