export type InformationQualitySeverity = "CRITICO" | "ATENCAO" | "INFORMATIVO";

export interface InformationQualityDetail {
  label: string;
  value: string;
}

export interface InformationQualityFinding {
  id: string;
  severity: InformationQualitySeverity;
  category: string;
  rule: string;
  title: string;
  description: string;
  documentCode: string | null;
  discipline: string | null;
  ldpHours: number | null;
  timesheetHours: number | null;
  decisionRequired: boolean;
  details: InformationQualityDetail[];
}

export interface InformationQualitySummary {
  critical: number;
  warning: number;
  info: number;
  total: number;
  ldpRows: number;
  ldpDocuments: number;
  timesheetDocuments: number;
  ldpActualHours: number;
  timesheetActualHours: number;
  differenceHours: number;
}

export interface InformationQualityAuditResult {
  projectId: string;
  generatedAtUtc: string;
  summary: InformationQualitySummary;
  findings: InformationQualityFinding[];
}

/** Regras cujo HH pode ser redistribuído para documentos da mesma disciplina na LDP. */
export const REDISTRIBUTABLE_RULES = new Set(["TIMESHEET_SEM_LDP", "DOCUMENTO_RETIRADO_COM_HH"]);

export interface RedistributionDestination {
  documentCode: string;
  title: string | null;
  plannedHours: number;
  /** Fração do HH de origem atribuída ao destino (0–1). */
  weight: number;
  hours: number;
  cost: number;
}

export interface RedistributionPreview {
  projectId: string;
  sourceDocumentCode: string;
  discipline: string | null;
  sourceHours: number;
  sourceCost: number;
  totalDestinationPlannedHours: number;
  canConfirm: boolean;
  blockingReason: string | null;
  destinations: RedistributionDestination[];
  sourceActivityCode?: string | null;
  activityMappingRequired?: boolean;
  disciplineResolution?: "NORMA" | "AT_MANUAL" | "NAO_RESOLVIDA" | string | null;
}

export interface ActivityDisciplineMapping {
  projectId: string;
  activityCode: string;
  type: string;
  discipline: string;
  updatedBy: string;
  updatedAtUtc: string;
}

export interface RedistributionDecision {
  id: string | number;
  projectId: string;
  sourceDocumentCode: string;
  discipline: string | null;
  sourceHours: number;
  sourceCost: number;
  /** Só "CONFIRMADO" conta como tratado; qualquer outro status continua pendente. */
  status: string | null;
  confirmedBy: string | null;
  confirmedAtUtc: string | null;
  items: RedistributionDestination[];
}

export interface RedistributionBatchPreviewItem {
  documentCode: string;
  preview: RedistributionPreview | null;
  error: string | null;
}

export interface RedistributionBatchConfirmItem {
  documentCode: string;
  success: boolean;
  decision: RedistributionDecision | null;
  error: string | null;
}

/** Regra emitida pelo backend quando o documento de origem já teve a redistribuição confirmada. */
export const REDISTRIBUTION_CONFIRMED_RULE = "REDISTRIBUICAO_CONFIRMADA";

export const redistributionKey = (code: string | null | undefined) => (code ?? "").trim().toLocaleUpperCase("pt-BR");

export const isConfirmedDecision = (decision: Pick<RedistributionDecision, "status">) =>
  (decision.status ?? "").trim().toLocaleUpperCase("pt-BR") === "CONFIRMADO";

/** Documentos já tratados: decisões CONFIRMADO do histórico + ocorrências REDISTRIBUICAO_CONFIRMADA da auditoria. */
export function confirmedRedistributionCodes(
  findings: Pick<InformationQualityFinding, "rule" | "documentCode">[],
  decisions: Pick<RedistributionDecision, "status" | "sourceDocumentCode">[],
): Set<string> {
  const codes = new Set<string>();
  for (const decision of decisions) {
    if (isConfirmedDecision(decision)) codes.add(redistributionKey(decision.sourceDocumentCode));
  }
  for (const finding of findings) {
    if (finding.rule === REDISTRIBUTION_CONFIRMED_RULE && redistributionKey(finding.documentCode)) {
      codes.add(redistributionKey(finding.documentCode));
    }
  }
  return codes;
}

export interface DestinationTotals {
  plannedHours: number;
  weight: number;
  hours: number;
  cost: number;
  /** Soma dos destinos − origem. Nunca omitida da tela, mesmo quando é zero. */
  hoursDifference: number;
  costDifference: number;
  hasDifference: boolean;
  /** Destinos cujo HH e custo não seguem o mesmo peso (HH/HH origem ≠ custo/custo origem ≠ peso). */
  inconsistentWeightCodes: string[];
}

const HOURS_TOLERANCE = 0.005;
const COST_TOLERANCE = 0.005;
const WEIGHT_TOLERANCE = 0.0005;
// Cada linha é arredondada em 2 casas pelo backend; acima disso o peso não foi aplicado igualmente.
const ROW_TOLERANCE = 0.01;

export function summarizeDestinations(rows: RedistributionDestination[], sourceHours: number, sourceCost: number): DestinationTotals {
  const totals = rows.reduce(
    (acc, row) => ({
      plannedHours: acc.plannedHours + (row.plannedHours ?? 0),
      weight: acc.weight + (row.weight ?? 0),
      hours: acc.hours + (row.hours ?? 0),
      cost: acc.cost + (row.cost ?? 0),
    }),
    { plannedHours: 0, weight: 0, hours: 0, cost: 0 },
  );
  const hoursDifference = totals.hours - sourceHours;
  const costDifference = totals.cost - sourceCost;
  const inconsistentWeightCodes = rows
    .filter((row) => {
      const hoursOff = Math.abs(row.hours - row.weight * sourceHours) > ROW_TOLERANCE;
      const costOff = Math.abs(row.cost - row.weight * sourceCost) > ROW_TOLERANCE;
      return hoursOff || costOff;
    })
    .map((row) => row.documentCode);

  return {
    ...totals,
    hoursDifference,
    costDifference,
    hasDifference: Math.abs(hoursDifference) > HOURS_TOLERANCE
      || Math.abs(costDifference) > COST_TOLERANCE
      || (rows.length > 0 && Math.abs(totals.weight - 1) > WEIGHT_TOLERANCE),
    inconsistentWeightCodes,
  };
}

export async function requestJson<T>(url: string, fallbackMessage: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    credentials: "same-origin",
    ...init,
    headers: init?.body
      ? { Accept: "application/json", "Content-Type": "application/json" }
      : { Accept: "application/json" },
  });

  let payload: (T & { message?: string }) | null = null;
  try { payload = await response.json(); } catch { /* resposta sem JSON */ }

  if (!response.ok) {
    throw new Error(payload?.message ?? `${fallbackMessage} (HTTP ${response.status}).`);
  }

  return payload as T;
}

type RawDecision = Partial<RedistributionDecision> & { destinations?: RedistributionDestination[] };

const toDecision = (raw: RawDecision): RedistributionDecision => ({
  id: raw.id ?? `${raw.projectId ?? ""}:${raw.sourceDocumentCode ?? ""}:${raw.confirmedAtUtc ?? ""}`,
  projectId: raw.projectId ?? "",
  sourceDocumentCode: raw.sourceDocumentCode ?? "",
  discipline: raw.discipline ?? null,
  sourceHours: raw.sourceHours ?? 0,
  sourceCost: raw.sourceCost ?? 0,
  status: raw.status ?? null,
  confirmedBy: raw.confirmedBy ?? null,
  confirmedAtUtc: raw.confirmedAtUtc ?? null,
  items: raw.items ?? raw.destinations ?? [],
});

export function getInformationQualityAudit(projectId: string, signal?: AbortSignal): Promise<InformationQualityAuditResult> {
  return requestJson(
    `/api/information-quality-audit?${new URLSearchParams({ projectId })}`,
    "Falha ao executar auditoria",
    { signal },
  );
}

export function getRedistributionPreview(projectId: string, documentCode: string, signal?: AbortSignal): Promise<RedistributionPreview> {
  return requestJson(
    `/api/information-quality-audit/redistribution-preview?${new URLSearchParams({ projectId, documentCode })}`,
    "Falha ao simular a redistribuição",
    { signal },
  );
}

export function getRedistributionBatchPreview(
  projectId: string,
  documentCodes: string[],
  signal?: AbortSignal,
): Promise<RedistributionBatchPreviewItem[]> {
  return requestJson(
    "/api/information-quality-audit/redistribution-batch-preview",
    "Falha ao simular a redistribuição em lote",
    { method: "POST", body: JSON.stringify({ projectId, documentCodes }), signal },
  );
}

export function confirmRedistributionBatch(
  projectId: string,
  documentCodes: string[],
): Promise<RedistributionBatchConfirmItem[]> {
  return requestJson(
    "/api/information-quality-audit/redistribution-batch-confirm",
    "Falha ao confirmar a redistribuição em lote",
    { method: "POST", body: JSON.stringify({ projectId, documentCodes }) },
  );
}

export function getActivityDisciplineOptions(projectId: string, signal?: AbortSignal): Promise<string[]> {
  return requestJson(
    `/api/information-quality-audit/activity-disciplines?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar as disciplinas disponíveis",
    { signal },
  );
}

export function saveActivityDisciplineMapping(
  projectId: string,
  activityCode: string,
  discipline: string,
): Promise<ActivityDisciplineMapping> {
  return requestJson(
    "/api/information-quality-audit/activity-discipline",
    "Falha ao salvar a classificação AT",
    {
      method: "PUT",
      body: JSON.stringify({ projectId, activityCode, discipline }),
    },
  );
}

/**
 * A resposta do POST não é a fonte da verdade: a tela recarrega auditoria + histórico depois de confirmar.
 * Devolve a decisão apenas quando o backend a envia no corpo.
 */
export async function confirmRedistribution(projectId: string, documentCode: string): Promise<RedistributionDecision | null> {
  const raw = await requestJson<RawDecision | null>(
    "/api/information-quality-audit/redistribution-confirm",
    "Falha ao confirmar a redistribuição",
    { method: "POST", body: JSON.stringify({ projectId, documentCode }) },
  );
  return raw && typeof raw === "object" && raw.sourceDocumentCode ? toDecision(raw) : null;
}

export async function getRedistributions(projectId: string, signal?: AbortSignal): Promise<RedistributionDecision[]> {
  const raw = await requestJson<RawDecision[]>(
    `/api/information-quality-audit/redistributions?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar o histórico de redistribuições",
    { signal },
  );
  return (Array.isArray(raw) ? raw : []).map(toDecision);
}

export type RedistributionActionKind = "SIMULAR" | "REDISTRIBUIDO" | "SEM_CODIGO" | "NENHUMA";

export function redistributionActionFor(
  finding: Pick<InformationQualityFinding, "rule" | "documentCode">,
  confirmed: { has(code: string): boolean },
): RedistributionActionKind {
  const isRow = REDISTRIBUTABLE_RULES.has(finding.rule) || finding.rule === REDISTRIBUTION_CONFIRMED_RULE;
  if (!isRow) return "NENHUMA";
  const key = redistributionKey(finding.documentCode);
  if (!key) return REDISTRIBUTABLE_RULES.has(finding.rule) ? "SEM_CODIGO" : "NENHUMA";
  if (confirmed.has(key)) return "REDISTRIBUIDO";
  return REDISTRIBUTABLE_RULES.has(finding.rule) ? "SIMULAR" : "REDISTRIBUIDO";
}

export function isRedistributionPending(
  finding: Pick<InformationQualityFinding, "rule" | "documentCode">,
  confirmed: { has(code: string): boolean },
): boolean {
  return REDISTRIBUTABLE_RULES.has(finding.rule)
    && !!redistributionKey(finding.documentCode)
    && !confirmed.has(redistributionKey(finding.documentCode));
}
