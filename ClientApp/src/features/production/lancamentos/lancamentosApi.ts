// Lançamentos (Controle de Produção) — transporte das agregações do Timesheet (/api/production-audit/*).
// Fonte interna: dbo.VW_TimeSheet_NA_Corrigida_Projeto pelo Project_ID_ACC canônico + CR/OS. Sem Autodesk, sem hubId.

export const LANCAMENTOS_API = "/api/production-audit";
export const PAGE_SIZE = 50;

export interface FiltrosLancamentos {
  from: string;
  to: string;
  person: string;
  discipline: string;
  document: string;
  /** Só Documentos: ESTOURADO | DENTRO DO PLANEJADO | SEM HH PLANEJADA. */
  status: string;
}

export const FILTROS_VAZIOS: FiltrosLancamentos = { from: "", to: "", person: "", discipline: "", document: "", status: "" };

export const STATUS_DOCUMENTO = ["ESTOURADO", "DENTRO DO PLANEJADO", "SEM HH PLANEJADA"] as const;

export interface Pessoa { name: string; plannedHours: number; workedHours: number; difference: number; entries: number; documents: number; lastEntry: string | null }
export interface PaginaPessoas { items: Pessoa[]; total: number; crOs: string; totalPlannedHours: number; totalWorkedHours: number }

export interface Disciplina {
  discipline: string; plannedHours: number; workedHours: number; difference: number; consumedPercent: number | null;
  documents: number; people: number; entries: number; overrunDocuments: number;
}
export interface ResultadoDisciplinas { items: Disciplina[]; crOs: string }

export interface Documento {
  documentCode: string; document: string | null; discipline: string; plannedHours: number | null; workedHours: number; totalWorkedHours: number;
  difference: number; excess: number; consumedPercent: number | null; status: string; people: number; entries: number;
}
export interface PaginaDocumentos {
  items: Documento[]; total: number; crOs: string; totalPlannedHours: number; totalWorkedHours: number; workedHoursWithoutDocument: number;
}

export interface Lancamento {
  date: string | null; name: string | null; plannedHours: number | null; workedHours: number;
  documentCode: string | null; document: string | null; description: string | null; discipline: string | null;
}
export interface PaginaLancamentos { items: Lancamento[]; total: number; crOs: string }

/** Erro com o código do backend (crOsNotConfigured / crOsAmbiguous → estado próprio na tela). */
export class LancamentosError extends Error {
  constructor(message: string, public status: number, public code?: string) { super(message); }
}

type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

/** Monta a query: projectId sempre; campos vazios nunca são enviados. */
export function montarQuery(projectId: string, filtros: Partial<FiltrosLancamentos> & { page?: number; pageSize?: number; exact?: boolean }) {
  const q = new URLSearchParams({ projectId });
  for (const [k, v] of Object.entries(filtros)) {
    if (v === undefined || v === null || v === "" || v === false) continue;
    q.set(k, String(typeof v === "string" ? v.trim() : v));
  }
  return q.toString();
}

async function obter<T>(url: string, fetchImpl: FetchLike): Promise<T> {
  const r = await fetchImpl(url, { credentials: "include", headers: { Accept: "application/json" } });
  if (!r.ok) {
    const corpo = (await r.json().catch(() => ({}))) as { message?: string; code?: string };
    throw new LancamentosError(corpo.message || "Não foi possível carregar os dados.", r.status, corpo.code);
  }
  return (await r.json()) as T;
}

const semStatus = ({ status: _s, ...resto }: FiltrosLancamentos) => resto;

export const buscarPessoas = (projectId: string, f: FiltrosLancamentos, page: number, fetchImpl: FetchLike = fetch) =>
  obter<PaginaPessoas>(`${LANCAMENTOS_API}/people?${montarQuery(projectId, { ...semStatus(f), page, pageSize: PAGE_SIZE })}`, fetchImpl);

export const buscarDisciplinas = (projectId: string, f: FiltrosLancamentos, fetchImpl: FetchLike = fetch) =>
  obter<ResultadoDisciplinas>(`${LANCAMENTOS_API}/disciplines?${montarQuery(projectId, semStatus(f))}`, fetchImpl);

export const buscarDocumentos = (projectId: string, f: FiltrosLancamentos, page: number, fetchImpl: FetchLike = fetch) =>
  obter<PaginaDocumentos>(`${LANCAMENTOS_API}/documents?${montarQuery(projectId, { ...f, page, pageSize: PAGE_SIZE })}`, fetchImpl);

/** Drilldown: lançamentos individuais com igualdade exata no recorte (pessoa/documento/disciplina) + filtros da tela. */
export const buscarLancamentos = (projectId: string, f: FiltrosLancamentos, recorte: Partial<Pick<FiltrosLancamentos, "person" | "discipline" | "document">>,
  page: number, fetchImpl: FetchLike = fetch) =>
  obter<PaginaLancamentos>(`${LANCAMENTOS_API}/entries?${montarQuery(projectId, { ...semStatus(f), ...recorte, page, pageSize: PAGE_SIZE, exact: true })}`, fetchImpl);

// ------------------------------------------------------------------------------------------------ formatação ----
const fmt2 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmt1 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
export const horas = (v: number | null | undefined) => (v === null || v === undefined ? "—" : fmt2.format(v));
export const percentual = (v: number | null | undefined) => (v === null || v === undefined ? "—" : `${fmt1.format(v)}%`);
export const dataCurta = (iso: string | null | undefined) => (iso ? new Date(iso.length === 10 ? `${iso}T00:00:00` : iso).toLocaleDateString("pt-BR") : "—");
