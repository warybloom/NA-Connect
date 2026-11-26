// Central de Relatórios Plant (F6): tipos, cliente da API /reports e exportação segura.
// Regras: o servidor aplica SEMPRE os filtros globais do projeto (a UI não envia nem pode omitir regras); o navegador nunca carrega
// o projeto inteiro — só páginas ≤ MAX_PAGINA. CSV vem em streaming do servidor; XLSX é montado aqui com teto de linhas.
import { PLANT_API, PlantApiError, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { PLANT_ROTAS } from "./plantRoutes";
import { linhasAExportar, neutralizarFormula } from "./plantReportsSafe";

export type ReportKind = "lines" | "equipment" | "instruments" | "valves" | "tie-ins" | "materials" | "supports";
export const REPORT_KINDS: ReadonlyArray<{ kind: ReportKind; label: string; rota: string }> = [
  { kind: "lines", label: "Linhas", rota: PLANT_ROTAS.relatorios.linhas },
  { kind: "equipment", label: "Equipamentos", rota: PLANT_ROTAS.relatorios.equipamentos },
  { kind: "instruments", label: "Instrumentos", rota: PLANT_ROTAS.relatorios.instrumentos },
  { kind: "valves", label: "Válvulas", rota: PLANT_ROTAS.relatorios.valvulas },
  { kind: "tie-ins", label: "Tie-ins", rota: PLANT_ROTAS.relatorios.tieIns },
  { kind: "materials", label: "Materiais", rota: PLANT_ROTAS.relatorios.materiais },
  { kind: "supports", label: "Suportes", rota: PLANT_ROTAS.relatorios.suportes },
];

/** Descobre o relatório pela URL (permite mapear uma única página em todas as rotas /plant3d/relatorios/*). */
export function reportKindDaRota(pathname: string): ReportKind {
  const limpo = pathname.replace(/\/+$/, "");
  return REPORT_KINDS.find(r => limpo === r.rota)?.kind ?? "lines";
}

export type ReportMode = "mapped" | "table" | "unmapped";
export type ReportField = { id: string; label: string; group: string };
export type ReportDescriptor = {
  kind: ReportKind; label: string; mode: ReportMode; categoryId: string | null; pidTable: string | null; pipingTable: string | null;
  reason: string | null; fields: ReportField[]; defaultFields: string[];
};
export type ReportCatalog = { status: string; reports: ReportDescriptor[]; maxExportRows: number; maxPage: number };
export type ReportData = {
  status: string; kind: ReportKind; mode: ReportMode; columns: ReportField[]; rows: string[][]; total: number; offset: number; limit: number;
  filterRules: number; reason: string | null; revisionToken: string | null;
};
export type ReportSource = { categoryId: string | null; pidTable: string | null; pipingTable: string | null };
export type ReportConfig = { sources: Partial<Record<ReportKind, ReportSource>>; updatedAtUtc: string | null };
export type ReportPrefs = { fields: string[]; classification: string | null; search: string | null };

export const MAX_PAGINA = 500;
/** Teto do XLSX gerado no navegador (memória/tempo). Acima disso o usuário usa o CSV do servidor (até maxExportRows). */
export const XLSX_MAX_LINHAS = 20_000;

export const SITUACOES: ReadonlyArray<{ id: string; label: string }> = [
  { id: "matched", label: "Conforme" }, { id: "divergent", label: "Divergente" }, { id: "missing3d", label: "Falta no 3D" },
  { id: "extra3d", label: "Extra no 3D" }, { id: "taglessPid", label: "P&ID sem TAG" }, { id: "tagless3d", label: "3D sem TAG" },
];

const base = (p: string) => `${projetoPath(p)}/reports`;

export async function getReportCatalog(p: string, signal?: AbortSignal): Promise<ReportCatalog> {
  const r = await plantRequest<Partial<ReportCatalog>>(base(p), { signal });
  if (!r || typeof r.status !== "string") throw new Error("Resposta inesperada de /reports (status ausente).");
  return { status: r.status, reports: Array.isArray(r.reports) ? r.reports : [], maxExportRows: r.maxExportRows ?? 250_000, maxPage: Math.min(r.maxPage ?? MAX_PAGINA, MAX_PAGINA) };
}

export async function getReportConfig(p: string, signal?: AbortSignal): Promise<ReportConfig> {
  const r = await plantRequest<Partial<ReportConfig>>(base(p) + "/config", { signal });
  return { sources: r?.sources && typeof r.sources === "object" ? r.sources : {}, updatedAtUtc: r?.updatedAtUtc ?? null };
}

export async function putReportConfig(p: string, sources: ReportConfig["sources"]): Promise<ReportConfig> {
  const r = await plantRequest<Partial<ReportConfig>>(base(p) + "/config", {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ sources }),
  });
  return { sources: r?.sources ?? sources, updatedAtUtc: r?.updatedAtUtc ?? null };
}

export async function getReportPrefs(p: string, kind: ReportKind, signal?: AbortSignal): Promise<{ prefs: ReportPrefs | null; updatedAtUtc: string | null }> {
  const r = await plantRequest<{ preferences?: ReportPrefs | null; updatedAtUtc?: string | null }>(`${base(p)}/${kind}/preferences`, { signal });
  const pr = r?.preferences;
  return { prefs: pr && Array.isArray(pr.fields) ? { fields: pr.fields, classification: pr.classification ?? null, search: pr.search ?? null } : null, updatedAtUtc: r?.updatedAtUtc ?? null };
}

export async function putReportPrefs(p: string, kind: ReportKind, prefs: ReportPrefs): Promise<void> {
  await plantRequest(`${base(p)}/${kind}/preferences`, {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(prefs),
  });
}

export type ReportQuery = { fields: string[]; classification?: string | null; search?: string | null };
function consulta(q: ReportQuery, extra: Record<string, string> = {}) {
  const sp = new URLSearchParams(extra);
  if (q.fields.length) sp.set("fields", q.fields.join(","));
  if (q.classification) sp.set("classification", q.classification);
  if (q.search?.trim()) sp.set("q", q.search.trim());
  return sp;
}

export async function getReportData(p: string, kind: ReportKind, q: ReportQuery, offset: number, limit: number, signal?: AbortSignal): Promise<ReportData> {
  const sp = consulta(q, { offset: String(offset), limit: String(Math.min(limit, MAX_PAGINA)) });
  const r = await plantRequest<Partial<ReportData>>(`${base(p)}/${kind}/data?${sp}`, { signal });
  if (!r || typeof r.status !== "string") throw new Error("Resposta inesperada de /reports/data (status ausente).");
  return {
    status: r.status, kind, mode: (r.mode ?? "unmapped") as ReportMode, columns: r.columns ?? [], rows: r.rows ?? [], total: r.total ?? 0,
    offset: r.offset ?? offset, limit: r.limit ?? limit, filterRules: r.filterRules ?? 0, reason: r.reason ?? null, revisionToken: r.revisionToken ?? null,
  };
}

// ───────── exportação ─────────
export { neutralizarFormula };

const nomeArquivo = (kind: string, ext: string) => `plant-${kind}-${new Date().toISOString().slice(0, 16).replace(/[-:T]/g, "")}.${ext}`;

function baixar(blob: Blob, nome: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = nome; document.body.appendChild(a); a.click(); a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** CSV em streaming do servidor (mesmo caminho filtrado do /data; ≤ maxExportRows). Devolve o aviso de truncamento, se houver. */
export async function exportarCsv(p: string, kind: ReportKind, q: ReportQuery): Promise<{ total: number; truncado: boolean }> {
  let resp: Response;
  try { resp = await fetch(`${PLANT_API}${base(p)}/${kind}/export?${consulta(q)}`, { credentials: "same-origin" }); }
  catch { throw new PlantApiError(0, "Sem resposta do servidor N&A."); }
  if (!resp.ok) {
    let msg = "";
    try { msg = (JSON.parse(await resp.text()) as { message?: string })?.message ?? ""; } catch { /* corpo ilegível */ }
    throw new PlantApiError(resp.status, msg);
  }
  baixar(await resp.blob(), nomeArquivo(kind, "csv"));
  return { total: Number(resp.headers.get("X-Plant-Report-Total") ?? "0") || 0, truncado: resp.headers.get("X-Plant-Report-Truncated") === "true" };
}

/** XLSX montado no navegador, paginando /data (500 por vez, teto XLSX_MAX_LINHAS). Células sempre como texto, já neutralizadas. */
export async function exportarXlsx(
  p: string, kind: ReportKind, q: ReportQuery, aoProgresso?: (feito: number, total: number) => void, signal?: AbortSignal,
): Promise<{ linhas: number; total: number; truncado: boolean }> {
  const primeira = await getReportData(p, kind, q, 0, MAX_PAGINA, signal);
  if (primeira.status !== "ready" || primeira.columns.length === 0) throw new Error(primeira.reason ?? "Relatório indisponível para exportação.");
  const alvo = linhasAExportar(primeira.total, XLSX_MAX_LINHAS);
  const linhas: string[][] = [...primeira.rows];
  aoProgresso?.(Math.min(linhas.length, alvo), alvo);
  while (linhas.length < alvo) {
    const pg = await getReportData(p, kind, q, linhas.length, MAX_PAGINA, signal);
    if (pg.rows.length === 0) break;
    linhas.push(...pg.rows);
    aoProgresso?.(Math.min(linhas.length, alvo), alvo);
  }
  const corte = linhas.slice(0, alvo);
  const XLSX = await import("xlsx");
  const aoa = [primeira.columns.map(c => neutralizarFormula(c.label)), ...corte.map(r => primeira.columns.map((_, i) => neutralizarFormula(r[i])))];
  const ws = XLSX.utils.aoa_to_sheet(aoa, { cellDates: false });
  // aoa_to_sheet infere número/data; força tipo texto para que nada seja reinterpretado como fórmula/número.
  for (const ref of Object.keys(ws)) {
    if (ref.startsWith("!")) continue;
    const cell = ws[ref];
    if (cell && typeof cell.v !== "string") { cell.v = String(cell.v); cell.t = "s"; delete cell.f; }
  }
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, kind.slice(0, 31));
  const bytes = XLSX.write(wb, { bookType: "xlsx", type: "array" }) as ArrayBuffer;
  baixar(new Blob([bytes], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }), nomeArquivo(kind, "xlsx"));
  return { linhas: corte.length, total: primeira.total, truncado: primeira.total > corte.length };
}
