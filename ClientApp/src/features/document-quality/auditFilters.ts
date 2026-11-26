// Filtros da grade da Auditoria de Qualidade (lógica pura, testada em __tests__).
// Disciplina = Discipline da LDP do documento identificado (nunca o nome da pasta); não identificado = SEM_DISCIPLINA.
import type { DocumentResult } from "./documentQualityApi";

export const TODAS = "__TODAS__";
export const SEM_DISCIPLINA = "__SEM__";

/** Disciplinas presentes nos resultados (sem lista fixa), ordenadas; inclui SEM_DISCIPLINA se houver não identificados. */
export function disciplinasDe(docs: readonly DocumentResult[]): string[] {
  const lista = [...new Set(docs.map((d) => d.discipline?.trim()).filter((d): d is string => !!d))].sort((a, b) => a.localeCompare(b, "pt-BR"));
  return docs.some((d) => !d.discipline?.trim()) ? [...lista, SEM_DISCIPLINA] : lista;
}

export const porDisciplina = (docs: readonly DocumentResult[], disciplina: string) =>
  disciplina === TODAS ? [...docs]
    : docs.filter((d) => (disciplina === SEM_DISCIPLINA ? !d.discipline?.trim() : d.discipline?.trim() === disciplina));

export const porStatus = (docs: readonly DocumentResult[], status: string) => (status === "TODOS" ? [...docs] : docs.filter((d) => d.status === status));

/** Indicadores da disciplina filtrada (mesmos grupos do resumo do servidor). */
export function resumoDe(docs: readonly DocumentResult[]) {
  const n = (...s: string[]) => docs.filter((d) => s.includes(d.status)).length;
  return { total: docs.length, compliant: n("CONFORME"), divergent: n("DIVERGÊNCIA"), outsideLdp: n("FORA DA LDP"), notIdentified: n("NÃO IDENTIFICADO", "AMBÍGUO"), notCalibrated: n("NÃO CALIBRADO"), readErrors: n("ERRO DE LEITURA") };
}
