// Disciplinas dos Lançamentos: sugestões vêm da NORMA DE COORDENAÇÃO vinculada ao projeto (mapeamentos[].disciplina),
// nunca de lista fixa. As disciplinas encontradas no Timesheet que não estão na Norma também aparecem (marcadas), e o
// que não tem correspondência continua como NÃO MAPEADO — nenhuma HH é escondida.
import type { Norma } from "@/features/planning/coordination/coordinationApi";

/** Chave de comparação: sem acento, sem caixa, espaços simples. "Mecânica " ≡ "MECANICA". */
export function chaveDisciplina(nome: string | null | undefined): string {
  return (nome ?? "").normalize("NFD").replace(/\p{Mn}/gu, "").replace(/\s+/g, " ").trim().toUpperCase();
}

/** "NÃO MAPEADO", "NÃO MAPEADO - AJUSTAR", vazio → sem disciplina mapeada. */
export const ehNaoMapeado = (nome: string | null | undefined) => {
  const k = chaveDisciplina(nome);
  return k === "" || k.startsWith("NAO MAPEADO") || k === "SEM DISCIPLINA";
};

/** Disciplinas distintas da Norma (ordem alfabética pt-BR), sem duplicadas por acento/caixa/espaço. */
export function disciplinasDaNorma(norma: Pick<Norma, "mapeamentos"> | null | undefined): string[] {
  const vistas = new Map<string, string>();
  for (const m of norma?.mapeamentos ?? []) {
    const nome = (m.disciplina ?? "").replace(/\s+/g, " ").trim();
    const k = chaveDisciplina(nome);
    if (k && !vistas.has(k)) vistas.set(k, nome);
  }
  return [...vistas.values()].sort((a, b) => a.localeCompare(b, "pt-BR"));
}

export type OrigemDisciplina = "norma" | "timesheet" | "nao-mapeado";
export interface OpcaoDisciplina { nome: string; chave: string; origem: OrigemDisciplina; noTimesheet: boolean }

/**
 * Lista única para filtros/sugestões: primeiro as da Norma (com ou sem lançamento), depois as do Timesheet fora da
 * Norma, por último o bucket NÃO MAPEADO. O nome exibido para itens da Norma é o da Norma; os do Timesheet, o do Timesheet.
 */
export function mesclarDisciplinas(daNorma: readonly string[], doTimesheet: readonly string[]): OpcaoDisciplina[] {
  const norma = new Map(daNorma.map((n) => [chaveDisciplina(n), n]));
  const ts = new Map<string, string>();
  for (const n of doTimesheet) { const k = chaveDisciplina(n); if (k && !ts.has(k)) ts.set(k, n); }
  const out: OpcaoDisciplina[] = [];
  for (const [k, nome] of norma) out.push({ nome, chave: k, origem: "norma", noTimesheet: ts.has(k) });
  const fora = [...ts].filter(([k]) => !norma.has(k)).sort((a, b) => a[1].localeCompare(b[1], "pt-BR"));
  for (const [k, nome] of fora) out.push({ nome, chave: k, origem: ehNaoMapeado(nome) ? "nao-mapeado" : "timesheet", noTimesheet: true });
  return out.sort((a, b) => ordem(a.origem) - ordem(b.origem));
}
const ordem = (o: OrigemDisciplina) => (o === "norma" ? 0 : o === "timesheet" ? 1 : 2);

/** Classificação de uma disciplina vinda do Timesheet frente à Norma. */
export function origemDe(nome: string, daNorma: readonly string[]): OrigemDisciplina {
  if (ehNaoMapeado(nome)) return "nao-mapeado";
  const k = chaveDisciplina(nome);
  return daNorma.some((n) => chaveDisciplina(n) === k) ? "norma" : "timesheet";
}
