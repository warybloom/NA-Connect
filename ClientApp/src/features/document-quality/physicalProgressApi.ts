
// AvanÃ§o fÃ­sico por Document_Status (nunca Status_In_Flow), configurÃ¡vel POR PROJETO em Qualidade.
// A auditoria usa somente configuraÃ§Ãµes salvas: o modelo inicial e as sugestÃµes apenas preenchem a tela antes de salvar.
import {
  REDISTRIBUTABLE_RULES,
  REDISTRIBUTION_CONFIRMED_RULE,
  redistributionKey,
  requestJson,
  type InformationQualityFinding,
} from "./informationQualityApi";

export const ROTA_CONFIGURACAO_AVANCO_FISICO = "/qualidade/configuracao-avanco-fisico";

/** Documento com Document_Status configurado, mas sem avanÃ§o fÃ­sico na LDP: oferece "Simular avanÃ§o fÃ­sico". */
export const PROGRESS_MISSING_RULE = "AVANCO_FISICO_AUSENTE_STATUS";
/** Document_Status observado sem configuraÃ§Ã£o salva no projeto: leva Ã  configuraÃ§Ã£o, nunca confirma. */
export const PROGRESS_UNCONFIGURED_RULE = "AVANCO_STATUS_SEM_CONFIGURACAO";

// ------------------------------------------------------------------------------------------------
// NormalizaÃ§Ã£o
// ------------------------------------------------------------------------------------------------

/** Chave de identidade do status (duplicidade na tela): espaÃ§os e caixa nÃ£o diferenciam. */
export const documentStatusKey = (value: string | null | undefined) =>
  (value ?? "").replace(/\s+/g, " ").trim().toLocaleUpperCase("pt-BR");

/** Chave solta para equivalÃªncias visuais (acentos, "_" e "-" ignorados). Nunca usada para persistir. */
export const equivalenceKey = (value: string | null | undefined) =>
  documentStatusKey((value ?? "").normalize("NFD").replace(/\p{Mn}/gu, "").replace(/[_-]+/g, " "));

/**
 * De-para EXPLÃCITO portuguÃªs â‡„ inglÃªs do Document_Status (ex.: APROVADO = APPROVED) â€” espelho de Services/DocumentQuality/DocumentStatusCanonical.cs (conferido por teste).
 * O backend Ã© quem resolve a configuraÃ§Ã£o; aqui a tabela sÃ³ evita salvar o mesmo significado duas vezes e sugere o modelo.
 */
export const STATUS_ALIASES: Readonly<Record<string, readonly string[]>> = {
  "NAO INICIADO": ["NOT STARTED", "NOT INITIATED", "NAO INICIADA"],
  "COLETA DE DADOS": ["DATA COLLECTION", "DATA GATHERING", "COLLECTING DATA", "EM COLETA DE DADOS"],
  "INICIADO": ["STARTED", "INITIATED", "INICIADA"],
  "ELABORANDO": ["EM ELABORACAO", "IN PROGRESS", "WORK IN PROGRESS", "IN ELABORATION", "UNDER ELABORATION", "ELABORATING", "UNDER DEVELOPMENT"],
  "FINALIZADO ELABORACAO": ["ELABORACAO FINALIZADA", "ELABORACAO CONCLUIDA", "ELABORATION FINISHED", "ELABORATION COMPLETED", "ELABORATION COMPLETE"],
  "AUTORIZACAO": ["EM AUTORIZACAO", "AGUARDANDO AUTORIZACAO", "AUTHORIZATION", "UNDER AUTHORIZATION", "AWAITING AUTHORIZATION"],
  "EMITIDO PARA CLIENTE": ["EMITIDO AO CLIENTE", "ENVIADO AO CLIENTE", "ENVIADO PARA CLIENTE", "ISSUED TO CLIENT", "ISSUED FOR CLIENT", "SENT TO CLIENT"],
  "EM ATENDIMENTO DE COMENTARIO": ["EM ATENDIMENTO DE COMENTARIOS", "ATENDIMENTO DE COMENTARIOS", "ADDRESSING COMMENTS", "ANSWERING COMMENTS", "COMMENTS IN PROGRESS"],
  "APROVADO": ["APPROVED", "APROVADA"],
};
const CANONICAL_BY_ALIAS = new Map(Object.entries(STATUS_ALIASES).flatMap(([canonical, aliases]) => aliases.map((a) => [a, canonical] as const)));

/** Mesma chave canÃ´nica do backend (DocumentStatusCanonical.Key): texto normalizado + alias explÃ­cito. Sem fuzzy. */
export const canonicalStatusKey = (value: string | null | undefined) => {
  const key = equivalenceKey(value);
  return CANONICAL_BY_ALIAS.get(key) ?? key;
};

const CANCELLED_STATUS = new Set(["CANCELADO", "CANCELADA", "CANCELED", "CANCELLED", "RETIRADO", "RETIRADA", "WITHDRAWN"]);

/** Cancelados/retirados nunca recebem avanÃ§o fÃ­sico gerencial. */
export const isCancelledStatus = (value: string | null | undefined) => CANCELLED_STATUS.has(equivalenceKey(value));

export const formatPercent = (value?: number | null) =>
  value == null ? "â€”" : `${new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2 }).format(value)}%`;

// ------------------------------------------------------------------------------------------------
// ConfiguraÃ§Ã£o por projeto
// ------------------------------------------------------------------------------------------------

export interface ProgressSettingItem {
  documentStatus: string;
  /** Documentos na LDP (snapshot atual) com este Document_Status. */
  count: number;
  isConfigured: boolean;
  /** 0â€“100. */
  percentual: number | null;
  ativo: boolean;
  updatedBy: string | null;
  updatedAtUtc: string | null;
}

export interface ProgressSettings {
  projectId: string;
  items: ProgressSettingItem[];
  updatedBy: string | null;
  updatedAtUtc: string | null;
}

export interface ProgressSettingInput {
  documentStatus: string;
  percentual: number;
  ativo: boolean;
}

type RawSettingItem = Partial<ProgressSettingItem> & { percent?: number | null; active?: boolean; documentCount?: number };
type RawSettings = Partial<Omit<ProgressSettings, "items">> & { items?: RawSettingItem[] };

const toSettingItem = (raw: RawSettingItem): ProgressSettingItem => {
  const percentual = raw.percentual ?? raw.percent ?? null;
  return {
    documentStatus: (raw.documentStatus ?? "").trim(),
    count: raw.count ?? raw.documentCount ?? 0,
    isConfigured: raw.isConfigured ?? percentual != null,
    percentual,
    ativo: raw.ativo ?? raw.active ?? true,
    updatedBy: raw.updatedBy ?? null,
    updatedAtUtc: raw.updatedAtUtc ?? null,
  };
};

/** Resposta de outro projeto Ã© descartada: configuraÃ§Ã£o nunca Ã© compartilhada entre projetos. */
function assertSameProject(expected: string, received: string | null | undefined) {
  if (received && received !== expected) {
    throw new Error("A resposta do servidor pertence a outro projeto e foi descartada.");
  }
}

export async function getProgressSettings(projectId: string, signal?: AbortSignal): Promise<ProgressSettings> {
  const response = await requestJson<RawSettings | RawSettingItem[]>(
    `/api/information-quality-audit/progress-settings?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar a configuraÃ§Ã£o de avanÃ§o fÃ­sico",
    { signal },
  );
  return parseProgressSettings(projectId, response);
}

/**
 * GET progress-settings devolve a LISTA (ProgressSettingResult[]: documentStatus, documentCount, configId, percentual, ativo,
 * isConfigured). O formato { projectId, items } continua aceito. Antes sÃ³ o objeto era lido e a lista virava vazia.
 */
export function parseProgressSettings(projectId: string, response: RawSettings | RawSettingItem[] | null | undefined): ProgressSettings {
  const raw: RawSettings | null | undefined = Array.isArray(response) ? { items: response } : response;
  assertSameProject(projectId, raw?.projectId);
  const items = (raw?.items ?? []).map(toSettingItem).filter((item) => item.documentStatus);
  // Ãšltima atualizaÃ§Ã£o: a do projeto quando vier, senÃ£o a mais recente entre as linhas salvas.
  const latest = items
    .filter((item) => item.updatedAtUtc)
    .sort((a, b) => (b.updatedAtUtc ?? "").localeCompare(a.updatedAtUtc ?? ""))[0];
  return {
    projectId,
    items,
    updatedBy: raw?.updatedBy ?? latest?.updatedBy ?? null,
    updatedAtUtc: raw?.updatedAtUtc ?? latest?.updatedAtUtc ?? null,
  };
}

export async function saveProgressSettings(projectId: string, items: ProgressSettingInput[]): Promise<void> {
  await requestJson<unknown>(
    "/api/information-quality-audit/progress-settings",
    "Falha ao salvar a configuraÃ§Ã£o de avanÃ§o fÃ­sico",
    { method: "PUT", body: JSON.stringify({ projectId, items }) },
  );
}

// ------------------------------------------------------------------------------------------------
// Modelo inicial (somente preenchimento visual; nada Ã© salvo automaticamente)
// ------------------------------------------------------------------------------------------------

export interface InitialModelEntry {
  documentStatus: string;
  percentual: number;
  /** Valores observados equivalentes (comparados por equivalenceKey). */
  equivalentes: string[];
}

export const MODELO_INICIAL_AVANCO: readonly InitialModelEntry[] = [
  { documentStatus: "NÃ£o iniciado", percentual: 0, equivalentes: ["NOT STARTED"] },
  { documentStatus: "Coleta de dados", percentual: 10, equivalentes: ["DATA COLLECTION", "DATA GATHERING"] },
  { documentStatus: "Iniciado", percentual: 20, equivalentes: ["STARTED"] },
  { documentStatus: "Elaborando", percentual: 40, equivalentes: ["EM ELABORACAO", "IN PROGRESS", "WORK IN PROGRESS"] },
  { documentStatus: "Finalizado elaboraÃ§Ã£o", percentual: 60, equivalentes: ["ELABORACAO FINALIZADA"] },
  { documentStatus: "AutorizaÃ§Ã£o", percentual: 70, equivalentes: ["EM AUTORIZACAO", "AUTHORIZATION"] },
  { documentStatus: "Emitido para cliente", percentual: 80, equivalentes: ["EMITIDO AO CLIENTE", "ISSUED TO CLIENT", "ISSUED FOR CLIENT"] },
  { documentStatus: "Em atendimento de comentÃ¡rio", percentual: 90, equivalentes: ["EM ATENDIMENTO DE COMENTARIOS", "ADDRESSING COMMENTS"] },
  { documentStatus: "Aprovado", percentual: 100, equivalentes: ["APPROVED"] },
];

const modelEntryFor = (status: string) => {
  const key = canonicalStatusKey(status);
  return MODELO_INICIAL_AVANCO.find((entry) =>
    canonicalStatusKey(entry.documentStatus) === key || entry.equivalentes.some((alias) => canonicalStatusKey(alias) === key));
};

// ------------------------------------------------------------------------------------------------
// Linhas editÃ¡veis da tela
// ------------------------------------------------------------------------------------------------

/** salvo = configuraÃ§Ã£o persistida; observado = existe na LDP sem configuraÃ§Ã£o; demais sÃ³ existem na tela. */
export type ProgressRowOrigin = "salvo" | "observado" | "modelo" | "sugestao" | "manual";

export interface ProgressRow {
  documentStatus: string;
  count: number;
  /** ConfiguraÃ§Ã£o persistida (null = nÃ£o configurado no projeto). */
  saved: ProgressSettingItem | null;
  /** Texto do input (0â€“100); vazio = sem percentual. */
  percentual: string;
  ativo: boolean;
  origin: ProgressRowOrigin;
}

export function rowsFromSettings(settings: ProgressSettings): ProgressRow[] {
  return settings.items.map((item): ProgressRow => item.isConfigured
    ? { documentStatus: item.documentStatus, count: item.count, saved: item, percentual: item.percentual == null ? "" : String(item.percentual), ativo: item.ativo, origin: "salvo" }
    : { documentStatus: item.documentStatus, count: item.count, saved: null, percentual: "", ativo: true, origin: "observado" });
}

/** Aceita vÃ­rgula decimal; null = vazio; NaN = invÃ¡lido. */
export function parsePercent(text: string): number | null {
  const value = text.trim().replace(",", ".");
  if (!value) return null;
  return /^\d{1,3}(\.\d{1,2})?$/.test(value) ? Number(value) : Number.NaN;
}

const isValidPercent = (value: number | null): value is number => value != null && !Number.isNaN(value) && value >= 0 && value <= 100;

export const isRowChanged = (row: ProgressRow) =>
  !!row.saved && (parsePercent(row.percentual) !== row.saved.percentual || row.ativo !== row.saved.ativo);

export const isRowPendingSave = (row: ProgressRow) =>
  row.saved ? isRowChanged(row) : parsePercent(row.percentual) != null;

/**
 * Preenche linhas SEM configuraÃ§Ã£o salva e sem valor digitado: status observado equivalente recebe sugestÃ£o;
 * entradas do modelo sem linha correspondente sÃ£o adicionadas. Linhas salvas (ou jÃ¡ digitadas) nÃ£o mudam.
 */
export function applyInitialModel(rows: ProgressRow[]): { rows: ProgressRow[]; filled: number; added: number } {
  const covered = new Set<InitialModelEntry>();
  let filled = 0;

  const next = rows.map((row): ProgressRow => {
    const entry = modelEntryFor(row.documentStatus);
    if (entry) covered.add(entry);
    if (!entry || row.saved || row.percentual.trim()) return row;
    filled++;
    const origin: ProgressRowOrigin = row.origin === "observado" ? "sugestao" : "modelo";
    return { ...row, percentual: String(entry.percentual), origin };
  });

  const missing = MODELO_INICIAL_AVANCO.filter((entry) => !covered.has(entry));
  for (const entry of missing) {
    next.push({ documentStatus: entry.documentStatus, count: 0, saved: null, percentual: String(entry.percentual), ativo: true, origin: "modelo" });
  }
  return { rows: next, filled, added: missing.length };
}

export interface ProgressValidation {
  /** Erro por Ã­ndice da linha. */
  rowErrors: Map<number, string>;
  items: ProgressSettingInput[];
}

/**
 * Monta o PUT: linhas salvas + linhas novas com percentual. Linha salva nÃ£o pode ficar vazia
 * (desligar Ã© feito por "Ativo"); status duplicado bloqueia o salvamento.
 */
export function validateRows(rows: ProgressRow[]): ProgressValidation {
  const rowErrors = new Map<number, string>();
  const items: ProgressSettingInput[] = [];
  const seen = new Map<string, number>();

  rows.forEach((row, index) => {
    const status = row.documentStatus.replace(/\s+/g, " ").trim();
    const key = canonicalStatusKey(status);
    if (!key) { rowErrors.set(index, "Informe o Document Status."); return; }
    // Mesma chave canÃ´nica do backend: "Elaborando" e "EM ELABORAÃ‡ÃƒO" sÃ£o o MESMO significado e nÃ£o podem ter dois percentuais.
    if (seen.has(key)) { rowErrors.set(index, "Document Status repetido."); return; }
    seen.set(key, index);

    const percent = parsePercent(row.percentual);
    if (percent == null) {
      if (row.saved) rowErrors.set(index, "Status jÃ¡ configurado nÃ£o pode ficar sem percentual. Use \"Ativo\" para desligar.");
      return;
    }
    if (!isValidPercent(percent)) { rowErrors.set(index, "Percentual deve estar entre 0 e 100 (atÃ© 2 casas decimais)."); return; }
    items.push({ documentStatus: status, percentual: percent, ativo: row.ativo });
  });

  return { rowErrors, items };
}

// ------------------------------------------------------------------------------------------------
// Auditoria: simulaÃ§Ã£o e confirmaÃ§Ã£o do avanÃ§o gerencial
// ------------------------------------------------------------------------------------------------

export interface ProgressPreview {
  projectId: string;
  documentCode: string;
  documentStatus: string | null;
  /** AvanÃ§o atual da LDP (0â€“100); null quando ausente. */
  originalPercent: number | null;
  /** Percentual SALVO na configuraÃ§Ã£o do projeto para o Document_Status (0â€“100). */
  configuredPercent: number | null;
  canConfirm: boolean;
  blockingReason: string | null;
}

type RawPreview = Partial<ProgressPreview> & { suggestedPercent?: number | null };

export async function getProgressPreview(projectId: string, documentCode: string, signal?: AbortSignal): Promise<ProgressPreview> {
  const raw = await requestJson<RawPreview>(
    `/api/information-quality-audit/progress-preview?${new URLSearchParams({ projectId, documentCode })}`,
    "Falha ao simular o avanÃ§o fÃ­sico",
    { signal },
  );
  assertSameProject(projectId, raw?.projectId);
  return {
    projectId,
    documentCode: raw?.documentCode ?? documentCode,
    documentStatus: raw?.documentStatus ?? null,
    originalPercent: raw?.originalPercent ?? null,
    configuredPercent: raw?.configuredPercent ?? raw?.suggestedPercent ?? null,
    canConfirm: raw?.canConfirm ?? false,
    blockingReason: raw?.blockingReason ?? null,
  };
}

/** Motivo local que impede confirmar (soma-se ao canConfirm do servidor). */
export function progressBlockedReason(preview: ProgressPreview): string | null {
  if (!preview.canConfirm) return preview.blockingReason ?? "O servidor nÃ£o permite confirmar este avanÃ§o fÃ­sico.";
  if (!preview.documentStatus) return "Documento sem Document Status na LDP.";
  if (isCancelledStatus(preview.documentStatus)) return "Documentos cancelados ou retirados nÃ£o recebem avanÃ§o fÃ­sico.";
  if (preview.configuredPercent == null) return "Este Document Status nÃ£o tem percentual salvo na configuraÃ§Ã£o do projeto.";
  return null;
}

export interface ProgressDecision {
  id: string | number;
  projectId: string;
  documentCode: string;
  documentStatus: string | null;
  originalPercent: number | null;
  validatedPercent: number | null;
  /** SÃ³ "CONFIRMADO" conta como validado. */
  status: string | null;
  confirmedBy: string | null;
  confirmedAtUtc: string | null;
}

type RawProgressDecision = Partial<ProgressDecision> & { configuredPercent?: number | null; percentual?: number | null };

const toProgressDecision = (raw: RawProgressDecision): ProgressDecision => ({
  id: raw.id ?? `${raw.projectId ?? ""}:${raw.documentCode ?? ""}:${raw.confirmedAtUtc ?? ""}`,
  projectId: raw.projectId ?? "",
  documentCode: raw.documentCode ?? "",
  documentStatus: raw.documentStatus ?? null,
  originalPercent: raw.originalPercent ?? null,
  validatedPercent: raw.validatedPercent ?? raw.configuredPercent ?? raw.percentual ?? null,
  status: raw.status ?? null,
  confirmedBy: raw.confirmedBy ?? null,
  confirmedAtUtc: raw.confirmedAtUtc ?? null,
});

export const isConfirmedProgress = (decision: Pick<ProgressDecision, "status">) =>
  (decision.status ?? "").trim().toLocaleUpperCase("pt-BR") === "CONFIRMADO";

/**
 * Envia o percentual que o usuÃ¡rio viu; o backend deve recalcular e recusar (409) se a configuraÃ§Ã£o mudou.
 * A resposta nÃ£o Ã© fonte da verdade: a tela recarrega auditoria + decisÃµes.
 */
export async function confirmProgress(
  projectId: string,
  documentCode: string,
  documentStatus: string,
  validatedPercent: number,
): Promise<void> {
  await requestJson<unknown>(
    "/api/information-quality-audit/progress-confirm",
    "Falha ao confirmar o avanÃ§o fÃ­sico",
    { method: "POST", body: JSON.stringify({ projectId, documentCode, documentStatus, validatedPercent }) },
  );
}

export async function getProgressDecisions(projectId: string, signal?: AbortSignal): Promise<ProgressDecision[]> {
  const raw = await requestJson<RawProgressDecision[]>(
    `/api/information-quality-audit/progress-decisions?${new URLSearchParams({ projectId })}`,
    "Falha ao carregar os avanÃ§os fÃ­sicos validados",
    { signal },
  );
  // DecisÃµes de outro projeto nunca entram na tela.
  return (Array.isArray(raw) ? raw : [])
    .map(toProgressDecision)
    .filter((item) => !item.projectId || item.projectId === projectId);
}

// ------------------------------------------------------------------------------------------------
// Regras de exibiÃ§Ã£o na auditoria
// ------------------------------------------------------------------------------------------------

const DOCUMENT_STATUS_LABELS = new Set(["DOCUMENT STATUS", "STATUS DO DOCUMENTO"]);

/** Document_Status informado nas evidÃªncias da ocorrÃªncia, quando houver. */
export function findingDocumentStatus(finding: Pick<InformationQualityFinding, "details">): string | null {
  const detail = finding.details.find((item) => DOCUMENT_STATUS_LABELS.has(equivalenceKey(item.label)));
  return detail?.value?.trim() || null;
}

/**
 * NÃºmeros de documento que nunca recebem "Simular avanÃ§o fÃ­sico": fora da LDP / retirados (regras de redistribuiÃ§Ã£o)
 * e jÃ¡ redistribuÃ­dos. O backend continua sendo a validaÃ§Ã£o definitiva.
 */
export function progressExcludedCodes(
  findings: Pick<InformationQualityFinding, "rule" | "documentCode">[],
  redistributedCodes: Iterable<string>,
): Set<string> {
  const codes = new Set<string>(redistributedCodes);
  for (const finding of findings) {
    if (REDISTRIBUTABLE_RULES.has(finding.rule) || finding.rule === REDISTRIBUTION_CONFIRMED_RULE) {
      const key = redistributionKey(finding.documentCode);
      if (key) codes.add(key);
    }
  }
  return codes;
}

export function isProgressSimulationPending(
  finding: Pick<InformationQualityFinding, "rule" | "documentCode" | "details">,
  validatedCodes: { has(code: string): boolean },
  excludedCodes: { has(code: string): boolean },
): boolean {
  const key = redistributionKey(finding.documentCode);
  return finding.rule === PROGRESS_MISSING_RULE
    && !!key
    && !validatedCodes.has(key)
    && !excludedCodes.has(key)
    && !isCancelledStatus(findingDocumentStatus(finding));
}

/** Link para a configuraÃ§Ã£o, destacando o status quando a ocorrÃªncia o informa. */
export function progressSettingsLink(status: string | null): string {
  return status ? `${ROTA_CONFIGURACAO_AVANCO_FISICO}?${new URLSearchParams({ status })}` : ROTA_CONFIGURACAO_AVANCO_FISICO;
}

