// CONTRATO_03: filtros incluir/excluir, inventário filtrado, fases de medição e medição por fase.
// Prefixo /api/plant-integration (via plantApi). Nada é inventado: contagens ausentes ficam nulas.
import { ResultsStatus, plantRequest, projetoPath } from "./plantApi";

export type FilterSide = "pid" | "piping" | "both";
export type FilterAction = "include" | "exclude";
export type FilterTarget = "attribute" | "drawing" | "table";
export type FilterOperator =
  | "eq" | "neq" | "contains" | "notContains" | "startsWith" | "endsWith" | "in" | "notIn" | "isEmpty" | "isNotEmpty";

export type FilterRule = {
  id: string;
  side: FilterSide;
  action: FilterAction;
  target: FilterTarget;
  field: string;
  operator: FilterOperator;
  value: string;
  enabled: boolean;
};

export const FILTER_OPERATORS: Array<{ id: FilterOperator; label: string; semValor?: boolean; lista?: boolean }> = [
  { id: "eq", label: "é igual a" },
  { id: "neq", label: "é diferente de" },
  { id: "contains", label: "contém" },
  { id: "notContains", label: "não contém" },
  { id: "startsWith", label: "começa com" },
  { id: "endsWith", label: "termina com" },
  { id: "in", label: "está em (lista)", lista: true },
  { id: "notIn", label: "não está em (lista)", lista: true },
  { id: "isEmpty", label: "está vazio", semValor: true },
  { id: "isNotEmpty", label: "não está vazio", semValor: true },
];

export type PlantFilters = { filters: Record<string, FilterRule[]>; updatedAtUtc?: string | null };

export type InventorySide = "pid" | "piping";
export type InventoryItem = {
  rowId: string;
  table?: string | null;
  tag?: string | null;
  drawing?: string | null;
  attributes?: Record<string, string | null> | null;
  excluded: boolean;
  reasons?: string[] | null;
};
export type PlantInventory = {
  status: ResultsStatus;
  category: string;
  side: InventorySide;
  rawCount: number;
  includedCount: number;
  excludedCount: number;
  items: InventoryItem[];
  totalItems: number;
  offset: number;
  limit: number;
  revisionToken?: string | null;
};

export const MAX_PAGE = 500;

export async function getFilters(projectIdAcc: string, signal?: AbortSignal): Promise<PlantFilters> {
  const r = await plantRequest<Partial<PlantFilters>>(projetoPath(projectIdAcc) + "/filters", { signal });
  return { filters: r && typeof r.filters === "object" && r.filters ? r.filters : {}, updatedAtUtc: r?.updatedAtUtc ?? null };
}

/** Envia o mapa completo; o chamador preserva as categorias que não editou. */
export async function putFilters(projectIdAcc: string, filters: Record<string, FilterRule[]>): Promise<PlantFilters> {
  const r = await plantRequest<Partial<PlantFilters>>(projetoPath(projectIdAcc) + "/filters", {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ filters }),
  });
  return { filters: r && typeof r.filters === "object" && r.filters ? r.filters : filters, updatedAtUtc: r?.updatedAtUtc ?? null };
}

export async function getInventory(
  projectIdAcc: string, category: string, side: InventorySide, offset: number, limit: number, signal?: AbortSignal,
): Promise<PlantInventory> {
  const qs = new URLSearchParams({ side, offset: String(offset), limit: String(Math.min(limit, MAX_PAGE)) });
  const r = await plantRequest<PlantInventory>(`${projetoPath(projectIdAcc)}/inventory/${encodeURIComponent(category)}?${qs}`, { signal });
  if (!r || typeof r !== "object" || !r.status) throw new Error("Resposta inesperada de /inventory (campo status ausente).");
  return {
    ...r,
    side: r.side ?? side,
    category: r.category ?? category,
    rawCount: r.rawCount ?? 0,
    includedCount: r.includedCount ?? 0,
    excludedCount: r.excludedCount ?? 0,
    items: r.items ?? [],
    totalItems: r.totalItems ?? 0,
    offset: r.offset ?? offset,
    limit: r.limit ?? limit,
  };
}

/** Fase de medição: nome livre (30/60/90 ou outro). Metas são digitadas pelo usuário, nunca inferidas do Plant. */
export type PlantPhase = {
  id: string;
  name: string;
  sortOrder: number;
  expected: Record<string, number>;
  weights: Record<string, number>;
};

const num = (v: unknown): number | null =>
  typeof v === "number" && Number.isFinite(v) ? v
    : typeof v === "string" && v.trim() !== "" && Number.isFinite(Number(v)) ? Number(v) : null;

/** Aceita mapa {categoria: n} ou lista [{category|categoryId|id, expected|count|…}]. */
function lerMapa(raw: unknown, chaves: string[]): Record<string, number> {
  const out: Record<string, number> = {};
  if (Array.isArray(raw)) {
    for (const e of raw as Array<Record<string, unknown>>) {
      const k = [e?.category, e?.categoryId, e?.id].find(x => typeof x === "string") as string | undefined;
      const n = chaves.map(c => num(e?.[c])).find(x => x !== null) ?? null;
      if (k && n !== null) out[k] = n;
    }
  } else if (raw && typeof raw === "object") {
    for (const [k, v] of Object.entries(raw as Record<string, unknown>)) {
      const n = num(v);
      if (n !== null) out[k] = n;
    }
  }
  return out;
}

export function normalizarFase(raw: unknown, indice: number): PlantPhase {
  const r = (raw ?? {}) as Record<string, unknown>;
  return {
    id: String(r.id ?? r.phaseId ?? ""),
    name: String(r.name ?? ""),
    sortOrder: num(r.sortOrder) ?? indice,
    expected: lerMapa(r.expected ?? r.expectedCounts ?? r.targets ?? r.categories, ["expected", "expectedCount", "count", "target", "value"]),
    weights: lerMapa(r.weights ?? r.categoryWeights, ["weight", "value"]),
  };
}

export const ordenarFases = (fases: PlantPhase[]) =>
  [...fases].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, "pt-BR"));

export async function getPhases(projectIdAcc: string, signal?: AbortSignal): Promise<PlantPhase[]> {
  const r = await plantRequest<{ phases?: unknown[] }>(projetoPath(projectIdAcc) + "/phases", { signal });
  return ordenarFases((r?.phases ?? []).map(normalizarFase));
}

export async function putPhases(projectIdAcc: string, phases: PlantPhase[]): Promise<PlantPhase[]> {
  const r = await plantRequest<{ phases?: unknown[] }>(projetoPath(projectIdAcc) + "/phases", {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ phases }),
  });
  return r?.phases ? ordenarFases(r.phases.map(normalizarFase)) : ordenarFases(phases);
}

/** GET /phases com o carimbo de gravação: base da checagem anti-substituição silenciosa (PUT troca a lista inteira). */
export async function getPhasesComCarimbo(projectIdAcc: string, signal?: AbortSignal): Promise<{ phases: PlantPhase[]; updatedAtUtc: string | null }> {
  const r = await plantRequest<{ phases?: unknown[]; updatedAtUtc?: string | null }>(projetoPath(projectIdAcc) + "/phases", { signal });
  return { phases: ordenarFases((r?.phases ?? []).map(normalizarFase)), updatedAtUtc: r?.updatedAtUtc ?? null };
}

/** Categorias de meta por gate. Tie-ins só têm meta digitada: o DE/PARA atual não possui fonte P&ID/3D para eles. */
export const CATEGORIAS_META = [
  { id: "line", label: "Linhas" },
  { id: "equip", label: "Equipamentos" },
  { id: "inst", label: "Instrumentos" },
  { id: "valve", label: "Válvulas" },
  { id: "tiein", label: "Tie-ins" },
] as const;

/** Última fase = maior ordem (o contrato: o Resumo mostra a meta do último gate, sem somar fases cumulativas). */
export const ultimaFase = (fases: PlantPhase[]): PlantPhase | null => {
  const ord = ordenarFases(fases);
  return ord.length ? ord[ord.length - 1] : null;
};

export type MeasurementCategory = {
  id: string; label: string; expected: number | null; pid: number | null; piping: number | null;
};
export type PlantMeasurement = {
  phaseId: string; phaseName: string | null; status: ResultsStatus | null; categories: MeasurementCategory[];
};

/** Metas da fase × realidade filtrada P&ID/3D. Contagem ausente fica nula (nunca vira 0 inventado). */
export async function getMeasurement(projectIdAcc: string, phaseId: string, signal?: AbortSignal): Promise<PlantMeasurement> {
  const r = await plantRequest<Record<string, unknown>>(
    `${projetoPath(projectIdAcc)}/measurement?phaseId=${encodeURIComponent(phaseId)}`, { signal });
  const lista = Array.isArray(r?.categories) ? (r.categories as Array<Record<string, unknown>>) : [];
  return {
    phaseId: String(r?.phaseId ?? phaseId),
    phaseName: typeof r?.phaseName === "string" ? r.phaseName : null,
    status: typeof r?.status === "string" ? (r.status as ResultsStatus) : null,
    categories: lista.map(c => {
      const id = String(c.id ?? c.categoryId ?? c.category ?? "");
      return {
        id,
        label: String(c.label ?? id),
        expected: num(c.expected ?? c.expectedCount ?? c.target),
        pid: num(c.pid ?? c.pidCount),
        piping: num(c.piping ?? c.pipingCount ?? c.threeD),
      };
    }),
  };
}

export const percentual = (real: number | null, meta: number | null): number | null =>
  real === null || meta === null || meta <= 0 ? null : (real / meta) * 100;

export const fmtPct = (p: number | null) => (p === null ? "—" : p.toLocaleString("pt-BR", { maximumFractionDigits: 1 }) + "%");
export const fmtNum = (n: number | null | undefined) => (n === null || n === undefined ? "—" : n.toLocaleString("pt-BR"));

export const newId = () =>
  typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : Math.random().toString(36).slice(2);
