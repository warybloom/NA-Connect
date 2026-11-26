// Central de Direcionamentos de HH (Qualidade > Auditoria).
// Contrato HTTP: _agent_tasks/20261029/CONTRATO_CENTRAL_HH_20261029.md — o backend (Codex) implementa, esta tela só consome.
// De/Para = vínculo ESPECÍFICO origem → documento da LDP. Redistribuição = rateio POR DISCIPLINA.
// Para a mesma origem os dois são mutuamente exclusivos: a tela nunca soma os dois e nunca grava sem preview + confirmação.

export type RoutingMethod = "DE_PARA" | "REDISTRIBUICAO" | "AT" | "SEM_DIRECIONAMENTO";
export type RoutingOperation = "EDIT_DE_PARA" | "RESET_DE_PARA" | "RESET_REDISTRIBUICAO";

export interface RoutingDestination {
  documentCode: string;
  title: string | null;
  hours: number;
  cost: number;
}

export interface RoutingControlItem {
  sourceDocumentCode: string;
  sourceTitle: string | null;
  discipline: string | null;
  sourceHours: number;
  sourceCost: number;
  professionals: string[];
  method: RoutingMethod;
  active: boolean;
  decisionId: string | null;
  destinationDocumentCode: string | null;
  destinations: RoutingDestination[];
  decisionBy: string | null;
  decisionAtUtc: string | null;
  reason: string | null;
  conflict: boolean;
  warning: string | null;
  version: string | null;
}

export interface RoutingHistoryEntry {
  sourceDocumentCode: string;
  method: string;
  details: string | null;
  by: string | null;
  atUtc: string | null;
  reason: string | null;
}

export interface RoutingControlResult {
  projectId: string;
  generatedAtUtc: string | null;
  items: RoutingControlItem[];
  history: RoutingHistoryEntry[];
}

export interface RoutingOperationRequest {
  projectId: string;
  sourceDocumentCode: string;
  operation: RoutingOperation;
  destinationDocumentCode?: string;
  reason: string;
  version: string;
}

export interface RoutingSide {
  sourceHours: number;
  sourceCost: number;
  destinations: RoutingDestination[];
}

export interface RoutingPreview {
  canConfirm: boolean;
  blockingReason: string | null;
  before: RoutingSide;
  after: RoutingSide;
  totalHoursDelta: number;
  totalCostDelta: number;
  version: string | null;
}

export interface RoutingApplyResult {
  success: boolean;
  message: string | null;
  version: string | null;
}

export const ROUTING_BASE = "/api/information-quality-audit/routing-control";
export const ROUTING_UNAVAILABLE_TITLE = "Central de Direcionamentos indisponível";
export const MIN_REASON_LENGTH = 10;

export const METHOD_LABEL: Record<RoutingMethod, string> = {
  DE_PARA: "De/Para específico",
  REDISTRIBUICAO: "Redistribuição por disciplina",
  AT: "Atividade AT",
  SEM_DIRECIONAMENTO: "Sem direcionamento",
};

export const OPERATION_LABEL: Record<RoutingOperation, string> = {
  EDIT_DE_PARA: "Editar De/Para",
  RESET_DE_PARA: "Resetar De/Para",
  RESET_REDISTRIBUICAO: "Resetar redistribuição",
};

// ------------------------------------------------------------------ HTTP ----

/** "unavailable": endpoint ausente/fora do ar; "forbidden": sem permissão; "conflict": versão/dado mudou no servidor. */
export type RoutingErrorKind = "unavailable" | "forbidden" | "conflict" | "invalid" | "failed";

export class RoutingControlError extends Error {
  constructor(message: string, readonly kind: RoutingErrorKind, readonly status: number) {
    super(message);
    this.name = "RoutingControlError";
  }
}

export function classifyStatus(status: number): RoutingErrorKind {
  if (status === 0 || status === 404 || status === 405 || status === 501 || status === 502 || status === 503 || status === 504) return "unavailable";
  if (status === 401 || status === 403) return "forbidden";
  if (status === 409 || status === 412) return "conflict";
  if (status === 400 || status === 422) return "invalid";
  return "failed";
}

export const isAbortError = (e: unknown) =>
  (e instanceof DOMException && e.name === "AbortError") || (e instanceof Error && e.name === "AbortError");

async function routingRequest<T>(url: string, fallback: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    // Preservar headers de seguranca fornecidos pelo chamador (ex. X-Sidebar-CSRF).
    const headers = new Headers(init?.headers);
    headers.set("Accept", "application/json");
    if (init?.body) headers.set("Content-Type", "application/json");
    response = await fetch(url, {
      credentials: "same-origin",
      ...init,
      headers,
    });
  } catch (e) {
    if (isAbortError(e)) throw e;
    throw new RoutingControlError(`${fallback} (sem resposta do servidor).`, "unavailable", 0);
  }

  let payload: (T & { message?: string }) | null = null;
  try { payload = await response.json(); } catch { /* resposta sem JSON */ }

  if (!response.ok) {
    const kind = classifyStatus(response.status);
    throw new RoutingControlError(payload?.message ?? `${fallback} (HTTP ${response.status}).`, kind, response.status);
  }
  if (payload == null || typeof payload !== "object") {
    throw new RoutingControlError(`${fallback} (resposta vazia ou inválida).`, "unavailable", response.status);
  }
  return payload as T;
}

const num = (value: unknown) => (typeof value === "number" && Number.isFinite(value) ? value : 0);
const str = (value: unknown) => (typeof value === "string" && value.trim() ? value : null);
const KNOWN_METHODS = new Set<RoutingMethod>(["DE_PARA", "REDISTRIBUICAO", "AT", "SEM_DIRECIONAMENTO"]);

const toDestination = (raw: Partial<RoutingDestination> | null | undefined): RoutingDestination => ({
  documentCode: String(raw?.documentCode ?? ""),
  title: str(raw?.title),
  hours: num(raw?.hours),
  cost: num(raw?.cost),
});

const toProfessionals = (raw: unknown): string[] => {
  const list = Array.isArray(raw) ? raw.map(String) : typeof raw === "string" ? raw.split(/[;,]/) : [];
  return [...new Set(list.map((x) => x.trim()).filter(Boolean))];
};

const toSide = (raw: Partial<RoutingSide> | null | undefined): RoutingSide => ({
  sourceHours: num(raw?.sourceHours),
  sourceCost: num(raw?.sourceCost),
  destinations: (Array.isArray(raw?.destinations) ? raw.destinations : []).map(toDestination),
});

/** Normaliza o GET sem criar campos fora do contrato: só converte tipos e completa listas ausentes. */
export function normalizeRoutingControl(raw: unknown, projectId: string): RoutingControlResult {
  const data = (raw ?? {}) as Partial<Record<keyof RoutingControlResult, unknown>>;
  const items = (Array.isArray(data.items) ? data.items : []).map((value): RoutingControlItem => {
    const item = (value ?? {}) as Record<string, unknown>;
    const method = String(item.method ?? "").toUpperCase() as RoutingMethod;
    return {
      sourceDocumentCode: String(item.sourceDocumentCode ?? ""),
      sourceTitle: str(item.sourceTitle),
      discipline: str(item.discipline),
      sourceHours: num(item.sourceHours),
      sourceCost: num(item.sourceCost),
      professionals: toProfessionals(item.professionals),
      method: KNOWN_METHODS.has(method) ? method : "SEM_DIRECIONAMENTO",
      active: item.active === true,
      decisionId: item.decisionId == null ? null : String(item.decisionId),
      destinationDocumentCode: str(item.destinationDocumentCode),
      destinations: (Array.isArray(item.destinations) ? item.destinations : []).map(toDestination),
      decisionBy: str(item.decisionBy),
      decisionAtUtc: str(item.decisionAtUtc),
      reason: str(item.reason),
      conflict: item.conflict === true,
      warning: str(item.warning),
      version: item.version == null ? null : String(item.version),
    };
  });
  const history = (Array.isArray(data.history) ? data.history : []).map((value): RoutingHistoryEntry => {
    const entry = (value ?? {}) as Record<string, unknown>;
    return {
      sourceDocumentCode: String(entry.sourceDocumentCode ?? ""),
      method: String(entry.method ?? ""),
      details: str(entry.details),
      by: str(entry.by),
      atUtc: str(entry.atUtc),
      reason: str(entry.reason),
    };
  });
  return {
    projectId: str(data.projectId) ?? projectId,
    generatedAtUtc: str(data.generatedAtUtc),
    items,
    history,
  };
}

export async function getRoutingControl(projectId: string, signal?: AbortSignal): Promise<RoutingControlResult> {
  // Sem projeto não há consulta: a central é sempre por projeto (RBAC do backend é por projeto).
  if (!projectId?.trim()) throw new RoutingControlError("Selecione um projeto para consultar os direcionamentos.", "invalid", 0);
  const raw = await routingRequest<unknown>(
    `${ROUTING_BASE}?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar os direcionamentos de HH",
    { signal },
  );
  return normalizeRoutingControl(raw, projectId);
}

/** Corpo exato do contrato; destinationDocumentCode só vai em EDIT_DE_PARA. */
export function buildOperationBody(request: RoutingOperationRequest): RoutingOperationRequest {
  const body: RoutingOperationRequest = {
    projectId: request.projectId,
    sourceDocumentCode: request.sourceDocumentCode,
    operation: request.operation,
    reason: request.reason.trim(),
    version: request.version,
  };
  if (request.operation === "EDIT_DE_PARA") body.destinationDocumentCode = (request.destinationDocumentCode ?? "").trim();
  return body;
}

/** POST de simulação: não grava. Só é chamado por ação explícita do usuário dentro do modal. */
export async function previewRoutingOperation(request: RoutingOperationRequest, signal?: AbortSignal): Promise<RoutingPreview> {
  const raw = await routingRequest<Partial<RoutingPreview> & { before?: Partial<RoutingSide>; after?: Partial<RoutingSide> }>(
    `${ROUTING_BASE}/preview`,
    "Falha ao simular o direcionamento",
    { method: "POST", body: JSON.stringify(buildOperationBody(request)), signal },
  );
  return {
    canConfirm: raw.canConfirm === true,
    blockingReason: str(raw.blockingReason),
    before: toSide(raw.before),
    after: toSide(raw.after),
    totalHoursDelta: num(raw.totalHoursDelta),
    totalCostDelta: num(raw.totalCostDelta),
    version: raw.version == null ? null : String(raw.version),
  };
}

/** Única chamada que grava. Nunca em lote: uma origem e uma operação por vez. */
export async function applyRoutingOperation(request: RoutingOperationRequest): Promise<RoutingApplyResult> {
  // O backend exige antiforgery no apply. Buscar token somente no clique de confirmar,
  // na mesma sessao autenticada, e envia-lo no header definido pelo Program.cs.
  const csrf = await routingRequest<{ token: string }>(
    `${ROUTING_BASE}/token?${new URLSearchParams({ projectId: request.projectId })}`,
    "Falha ao obter autorizacao para salvar o direcionamento",
  );
  if (!csrf.token || typeof csrf.token !== "string") {
    throw new RoutingControlError("Token de seguranca ausente. Recarregue a pagina.", "invalid", 400);
  }
  const raw = await routingRequest<Partial<RoutingApplyResult>>(
    `${ROUTING_BASE}/apply`,
    "Falha ao aplicar o direcionamento",
    { method: "POST", headers: { "X-Sidebar-CSRF": csrf.token }, body: JSON.stringify(buildOperationBody(request)) },
  );
  return {
    success: raw.success === true,
    message: str(raw.message),
    version: raw.version == null ? null : String(raw.version),
  };
}

/**
 * Documentos existentes na LDP do projeto para o seletor do "Editar De/Para".
 * Reaproveita o GET já existente da conciliação "Documentos do Timesheet fora da LDP" (somente leitura).
 */
export async function getLdpDocumentOptions(projectId: string, signal?: AbortSignal): Promise<string[]> {
  const raw = await routingRequest<{ documentDestinations?: unknown }>(
    `/api/production-audit/timesheet-outside-ldp?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar os documentos da LDP",
    { signal },
  );
  const list = Array.isArray(raw.documentDestinations) ? raw.documentDestinations.map(String) : [];
  return [...new Set(list.map((x) => x.trim()).filter(Boolean))].sort((a, b) => a.localeCompare(b, "pt-BR"));
}

// ------------------------------------------------------------------ AGRUPAMENTO POR ORIGEM ----

export const routingKey = (code: string | null | undefined) => (code ?? "").trim().toLocaleUpperCase("pt-BR");

export type RoutingAlertKind =
  | "AMBOS_ATIVOS"
  | "CONFLITO"
  | "DESTINO_CANCELADO"
  | "DIVERGENCIA_DISCIPLINA"
  | "ORIGEM_ATIVA_NA_LDP"
  | "HH_ALTERADAS"
  | "SEM_LDP"
  | "HH_ORIGEM_DIVERGENTE"
  | "AVISO";

export interface RoutingAlert {
  kind: RoutingAlertKind;
  text: string;
}

export interface RoutingOrigin {
  key: string;
  sourceDocumentCode: string;
  sourceTitle: string | null;
  discipline: string | null;
  /** HH/custo da ORIGEM, contados uma única vez (nunca somados entre métodos). */
  sourceHours: number;
  sourceCost: number;
  professionals: string[];
  items: RoutingControlItem[];
  activeItems: RoutingControlItem[];
  methods: Set<RoutingMethod>;
  activeMethods: Set<RoutingMethod>;
  bothActive: boolean;
  conflict: boolean;
  alerts: RoutingAlert[];
  history: RoutingHistoryEntry[];
}

const ALERT_PATTERNS: Array<[RoutingAlertKind, RegExp]> = [
  ["DESTINO_CANCELADO", /CANCELAD|RETIRAD/],
  ["DIVERGENCIA_DISCIPLINA", /DISCIPLINA/],
  ["HH_ALTERADAS", /HH\s*(ALTERAD|MUDOU|MUDAR)|ALTERAD[OA]S?\s*DESDE|DIVERG.*HH/],
  ["SEM_LDP", /SEM\s*LDP|AUSENTE\s*(DA|NA)\s*LDP|AUS[ÊE]NCIA\s*DE\s*LDP|LDP\s*INDISPON/],
  ["ORIGEM_ATIVA_NA_LDP", /ATIV[OA]\s*NA\s*LDP|AINDA\s*ATIV/],
];

/** O contrato envia `warning` como texto livre; a tela só escolhe o tom do selo — o texto do servidor é exibido sempre. */
export function classifyWarning(text: string): RoutingAlertKind {
  const upper = text.toLocaleUpperCase("pt-BR");
  for (const [kind, pattern] of ALERT_PATTERNS) if (pattern.test(upper)) return kind;
  return "AVISO";
}

export const splitWarnings = (warning: string | null) =>
  (warning ?? "").split(/\s*[;|\n]\s*/).map((x) => x.trim()).filter(Boolean);

const HOURS_TOLERANCE = 0.005;

export function groupByOrigin(result: Pick<RoutingControlResult, "items" | "history">): RoutingOrigin[] {
  const map = new Map<string, RoutingOrigin>();
  for (const item of result.items) {
    const key = routingKey(item.sourceDocumentCode);
    if (!key) continue;
    let origin = map.get(key);
    if (!origin) {
      origin = {
        key,
        sourceDocumentCode: item.sourceDocumentCode.trim(),
        sourceTitle: item.sourceTitle,
        discipline: item.discipline,
        sourceHours: item.sourceHours,
        sourceCost: item.sourceCost,
        professionals: [],
        items: [],
        activeItems: [],
        methods: new Set(),
        activeMethods: new Set(),
        bothActive: false,
        conflict: false,
        alerts: [],
        history: [],
      };
      map.set(key, origin);
    }
    origin.items.push(item);
    origin.methods.add(item.method);
    if (item.active) {
      origin.activeItems.push(item);
      origin.activeMethods.add(item.method);
    }
    origin.sourceTitle ??= item.sourceTitle;
    origin.discipline ??= item.discipline;
    origin.professionals = [...new Set([...origin.professionals, ...item.professionals])];
    origin.conflict ||= item.conflict;
  }

  for (const entry of result.history) {
    const origin = map.get(routingKey(entry.sourceDocumentCode));
    if (origin) origin.history.push(entry);
  }

  const origins = [...map.values()];
  for (const origin of origins) {
    origin.bothActive = origin.activeMethods.has("DE_PARA") && origin.activeMethods.has("REDISTRIBUICAO");
    // Quando há mais de um registro, a origem vem do método ativo (ou do mais recente) — nunca da soma.
    const reference = origin.activeItems[0] ?? origin.items[0];
    origin.sourceHours = reference.sourceHours;
    origin.sourceCost = reference.sourceCost;
    origin.history.sort((a, b) => (b.atUtc ?? "").localeCompare(a.atUtc ?? ""));

    const alerts: RoutingAlert[] = [];
    if (origin.bothActive) {
      alerts.push({ kind: "AMBOS_ATIVOS", text: "De/Para e Redistribuição ativos na mesma origem — os valores não se somam. Revise e desative o método incorreto." });
    }
    if (origin.conflict && !origin.bothActive) alerts.push({ kind: "CONFLITO", text: "Conflito sinalizado pelo servidor." });
    if (origin.items.some((item) => Math.abs(item.sourceHours - reference.sourceHours) > HOURS_TOLERANCE)) {
      alerts.push({ kind: "HH_ORIGEM_DIVERGENTE", text: "HH de origem diferente entre os registros desta origem." });
    }
    const seen = new Set<string>();
    for (const item of origin.items) {
      for (const text of splitWarnings(item.warning)) {
        if (seen.has(text)) continue;
        seen.add(text);
        alerts.push({ kind: classifyWarning(text), text });
      }
    }
    origin.alerts = alerts;
  }

  return origins.sort((a, b) =>
    Number(b.bothActive || b.conflict) - Number(a.bothActive || a.conflict)
    || a.sourceDocumentCode.localeCompare(b.sourceDocumentCode, "pt-BR"));
}

// ------------------------------------------------------------------ FILTROS ----

export type RoutingTypeFilter = "TODOS" | RoutingMethod | "CONFLITANTE" | "HISTORICO";
export type RoutingStatusFilter = "TODOS" | "ATIVO" | "INATIVO";

export interface RoutingFilters {
  search: string;
  professional: string;
  discipline: string;
  status: RoutingStatusFilter;
  type: RoutingTypeFilter;
}

export const EMPTY_ROUTING_FILTERS: RoutingFilters = { search: "", professional: "", discipline: "", status: "TODOS", type: "TODOS" };

export const isConflicting = (origin: Pick<RoutingOrigin, "bothActive" | "conflict">) => origin.bothActive || origin.conflict;

export function filterOrigins(origins: RoutingOrigin[], filters: RoutingFilters): RoutingOrigin[] {
  const q = filters.search.trim().toLocaleUpperCase("pt-BR");
  return origins.filter((origin) => {
    if (filters.discipline && origin.discipline !== filters.discipline) return false;
    if (filters.professional && !origin.professionals.includes(filters.professional)) return false;
    if (filters.status === "ATIVO" && origin.activeItems.length === 0) return false;
    if (filters.status === "INATIVO" && !origin.items.some((item) => !item.active)) return false;
    switch (filters.type) {
      case "TODOS": break;
      case "CONFLITANTE": if (!isConflicting(origin)) return false; break;
      case "HISTORICO": if (origin.history.length === 0 && !origin.items.some((item) => !item.active)) return false; break;
      default: if (!origin.methods.has(filters.type)) return false;
    }
    if (!q) return true;
    return [
      origin.sourceDocumentCode,
      origin.sourceTitle,
      ...origin.items.flatMap((item) => [item.destinationDocumentCode, ...item.destinations.flatMap((d) => [d.documentCode, d.title])]),
    ].some((value) => (value ?? "").toLocaleUpperCase("pt-BR").includes(q));
  });
}

export interface RoutingTotals {
  origins: number;
  /** HH/custo das origens filtradas, cada origem uma única vez. */
  hours: number;
  cost: number;
  byMethod: Record<RoutingMethod, number>;
  conflicting: number;
}

export function summarizeOrigins(origins: RoutingOrigin[]): RoutingTotals {
  const byMethod: Record<RoutingMethod, number> = { DE_PARA: 0, REDISTRIBUICAO: 0, AT: 0, SEM_DIRECIONAMENTO: 0 };
  let hours = 0;
  let cost = 0;
  let conflicting = 0;
  for (const origin of origins) {
    hours += origin.sourceHours;
    cost += origin.sourceCost;
    if (isConflicting(origin)) conflicting += 1;
    const methods = origin.activeMethods.size ? origin.activeMethods : new Set<RoutingMethod>(["SEM_DIRECIONAMENTO"]);
    for (const method of methods) byMethod[method] += 1;
  }
  return { origins: origins.length, hours, cost, byMethod, conflicting };
}

// ------------------------------------------------------------------ AÇÕES ----

/**
 * Operações oferecidas para um registro. Sem edição → nenhuma. Registro inativo → nenhuma (nunca reativar).
 * AT e sem direcionamento não têm operação nesta central.
 */
export function operationsFor(item: Pick<RoutingControlItem, "method" | "active" | "version">, canEdit: boolean): RoutingOperation[] {
  if (!canEdit || !item.active || !item.version) return [];
  if (item.method === "DE_PARA") return ["EDIT_DE_PARA", "RESET_DE_PARA"];
  if (item.method === "REDISTRIBUICAO") return ["RESET_REDISTRIBUICAO"];
  return [];
}

export interface OperationDraft {
  operation: RoutingOperation;
  destinationDocumentCode: string;
  reason: string;
}

/** Validação local antes de qualquer POST. O servidor revalida tudo. */
export function validateDraft(
  draft: OperationDraft,
  item: Pick<RoutingControlItem, "sourceDocumentCode" | "destinationDocumentCode">,
  ldpDocuments?: ReadonlySet<string> | null,
): string | null {
  if (draft.reason.trim().length < MIN_REASON_LENGTH) return `Justificativa obrigatória (mínimo ${MIN_REASON_LENGTH} caracteres).`;
  if (draft.operation !== "EDIT_DE_PARA") return null;
  const destination = routingKey(draft.destinationDocumentCode);
  if (!destination) return "Escolha o documento de destino na LDP.";
  if (destination === routingKey(item.sourceDocumentCode)) return "O destino não pode ser a própria origem.";
  if (destination === routingKey(item.destinationDocumentCode)) return "O destino escolhido é o destino atual do De/Para.";
  if (ldpDocuments && !ldpDocuments.has(destination)) return "O destino precisa ser um documento existente na LDP do projeto.";
  return null;
}

/** Identidade do pedido simulado: qualquer mudança em destino/justificativa/versão invalida o preview. */
export const draftFingerprint = (request: RoutingOperationRequest) => JSON.stringify(buildOperationBody(request));

const sumHours = (rows: RoutingDestination[]) => rows.reduce((acc, row) => acc + row.hours, 0);
const sumCost = (rows: RoutingDestination[]) => rows.reduce((acc, row) => acc + row.cost, 0);

export interface PreviewCheck {
  beforeResidualHours: number;
  beforeResidualCost: number;
  afterResidualHours: number;
  afterResidualCost: number;
  /** Bloqueio local adicional (o servidor é a fonte, mas a tela nunca confirma HH multiplicadas ou origem alterada). */
  localBlock: string | null;
}

export function checkPreview(preview: RoutingPreview): PreviewCheck {
  const beforeResidualHours = preview.before.sourceHours - sumHours(preview.before.destinations);
  const beforeResidualCost = preview.before.sourceCost - sumCost(preview.before.destinations);
  const afterResidualHours = preview.after.sourceHours - sumHours(preview.after.destinations);
  const afterResidualCost = preview.after.sourceCost - sumCost(preview.after.destinations);
  let localBlock: string | null = null;
  if (Math.abs(preview.before.sourceHours - preview.after.sourceHours) > HOURS_TOLERANCE) {
    localBlock = "O HH da origem mudaria com esta operação; o Timesheet original nunca é alterado.";
  } else if (afterResidualHours < -HOURS_TOLERANCE || afterResidualCost < -HOURS_TOLERANCE) {
    localBlock = "Os destinos receberiam mais HH/custo do que a origem possui (multiplicação de HH).";
  }
  return { beforeResidualHours, beforeResidualCost, afterResidualHours, afterResidualCost, localBlock };
}

export function canApply(
  preview: RoutingPreview | null,
  previewFingerprint: string | null,
  currentFingerprint: string,
  acknowledged: boolean,
): boolean {
  if (!preview || !preview.canConfirm || preview.blockingReason) return false;
  if (!preview.version || previewFingerprint !== currentFingerprint) return false;
  if (checkPreview(preview).localBlock) return false;
  return acknowledged;
}

/** Texto explícito do que será desativado e para onde vão as HH. */
export function operationEffectText(operation: RoutingOperation, origin: Pick<RoutingOrigin, "activeMethods">): string {
  if (operation === "EDIT_DE_PARA") {
    return "O De/Para continua ativo e passa a apontar para o novo documento. O vínculo anterior é preservado no histórico.";
  }
  if (operation === "RESET_DE_PARA") {
    return origin.activeMethods.has("REDISTRIBUICAO")
      ? "Somente o De/Para será desativado (não apagado). A Redistribuição por disciplina ativa permanece como método desta origem."
      : "Somente o De/Para será desativado (não apagado). Sem outro método ativo, as HH voltam a ficar sem direcionamento documental e reaparecem como pendentes na Auditoria.";
  }
  return origin.activeMethods.has("DE_PARA")
    ? "Somente a Redistribuição por disciplina será desativada (não apagada). O De/Para específico ativo permanece como método desta origem."
    : "Somente a Redistribuição por disciplina será desativada (não apagada). Sem outro método ativo, as HH voltam a ficar pendentes na Auditoria.";
}
