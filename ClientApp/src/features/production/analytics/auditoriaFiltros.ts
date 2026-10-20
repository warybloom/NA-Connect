// Auditoria do Projeto — filtros de APRESENTAÇÃO sobre o resultado de /api/production-audit/reconciliation.
// O backend é a fonte de HH LDP (Actual_HH), HH Timesheet (Trabalhadas), Diferença (LDP − TIMESHEET), Situação, Motivo
// e da tolerância de conciliação. Aqui nada é recalculado: só se filtra o que veio.

export interface DocumentoAuditoria {
  documentCode: string; discipline?: string | null; ldpPlanned?: number | null; timesheetPlanned?: number | null;
  ldpActual: number; timesheetActual: number; difference: number; status: string; reason: string;
}

export const CONCILIADO = "CONCILIADO";
export type VisaoAuditoria = "divergencias" | "todos" | "conciliados";

export interface FiltrosAuditoria {
  visao: VisaoAuditoria;
  documento: string;
  /** Vazio = todas. */
  disciplinas: string[];
  ldpMin: string; ldpMax: string;
  tsMin: string; tsMax: string;
  difMin: string; difMax: string;
  /** |Diferença de Horas| > tolerância do backend. */
  somenteComDiferenca: boolean;
  /** Vazio = todas. */
  situacoes: string[];
  motivo: string;
}

/** Inicial = experiência anterior (somente divergências), mas os conciliados continuam no dataset e podem ser exibidos. */
export const FILTROS_AUDITORIA_INICIAIS: FiltrosAuditoria = {
  visao: "divergencias", documento: "", disciplinas: [], ldpMin: "", ldpMax: "", tsMin: "", tsMax: "", difMin: "", difMax: "",
  somenteComDiferenca: false, situacoes: [], motivo: "",
};

/** Rótulo do "sem disciplina" (só apresentação; o backend não inventa disciplina). */
export const SEM_DISCIPLINA = "Sem disciplina";

/** "1.234,5" / "-10" / "10.5" → número; vazio/inválido → null (filtro desligado). */
export function numeroFiltro(texto: string): number | null {
  const t = texto.trim();
  if (!t) return null;
  const n = Number(t.includes(",") ? t.replace(/\./g, "").replace(",", ".") : t);
  return Number.isFinite(n) ? n : null;
}

const texto = (s: string) => s.normalize("NFD").replace(/\p{Mn}/gu, "").trim().toLowerCase();
const contem = (valor: string | null | undefined, busca: string) => !busca.trim() || texto(valor ?? "").includes(texto(busca));
const naFaixa = (v: number, min: string, max: string) => {
  const a = numeroFiltro(min); const b = numeroFiltro(max);
  return (a === null || v >= a) && (b === null || v <= b);
};
export const disciplinaDe = (d: DocumentoAuditoria) => (d.discipline?.trim() ? d.discipline.trim() : SEM_DISCIPLINA);

/** Aplica TODOS os filtros (visão + por coluna) sobre o resultado completo do backend. */
export function filtrarAuditoria(docs: readonly DocumentoAuditoria[], f: FiltrosAuditoria, tolerancia: number): DocumentoAuditoria[] {
  return docs.filter((d) =>
    (f.visao === "todos" || (f.visao === "conciliados" ? d.status === CONCILIADO : d.status !== CONCILIADO))
    && contem(d.documentCode, f.documento)
    && (f.disciplinas.length === 0 || f.disciplinas.includes(disciplinaDe(d)))
    && naFaixa(d.ldpActual, f.ldpMin, f.ldpMax)
    && naFaixa(d.timesheetActual, f.tsMin, f.tsMax)
    && naFaixa(d.difference, f.difMin, f.difMax)
    && (!f.somenteComDiferenca || Math.abs(d.difference) > tolerancia)
    && (f.situacoes.length === 0 || f.situacoes.includes(d.status))
    && contem(d.reason, f.motivo));
}

/** Opções dos seletores a partir do PRÓPRIO resultado (nada fixo). */
export function opcoesAuditoria(docs: readonly DocumentoAuditoria[]) {
  const ordenar = (xs: Iterable<string>) => [...new Set(xs)].sort((a, b) => a.localeCompare(b, "pt-BR"));
  return { disciplinas: ordenar(docs.map(disciplinaDe)), situacoes: ordenar(docs.map((d) => d.status)) };
}

/** Quantos filtros por coluna estão ativos (a visão não conta). */
export function filtrosAtivos(f: FiltrosAuditoria): number {
  return [f.documento.trim(), f.disciplinas.length, f.ldpMin.trim() || f.ldpMax.trim(), f.tsMin.trim() || f.tsMax.trim(),
    f.difMin.trim() || f.difMax.trim() || f.somenteComDiferenca, f.situacoes.length, f.motivo.trim()].filter(Boolean).length;
}
