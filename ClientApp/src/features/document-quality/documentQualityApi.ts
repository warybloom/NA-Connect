import { getJson } from "@/lib/api";

export interface OcrPackage { id: number; nome: string; criadoEm: string }
export interface DocumentQualityConfiguration {
  projectIdAcc: string;
  projectName: string | null;
  packageId: number | null;
  packageName: string | null;
  auditFolderId: string | null;
  auditFolderPath: string | null;
  updatedAtUtc: string | null;
}
export interface FieldResult {
  field: string; label: string; expected: string | null; found: string | null; status: string; reason: string | null;
  /** Diagnóstico: valores como foram comparados (normalização segura). */
  expectedNormalized?: string | null; foundNormalized?: string | null;
}
export interface DocumentResult {
  fileName: string; folder: string; ldpRecordId: number | null; naDocumentNumber: string | null; clientDocumentNumber: string | null;
  document: string | null; titleExpected: string | null; titleFound: string | null; revisionExpected: string | null;
  revisionFound: string | null; discipline: string | null; status: string; reason: string; sheetType: string | null; fields: FieldResult[];
  /** "NOME DO ARQUIVO (N&A Document Number)", "CARIMBO (NÚMERO N&A)"… null = não identificado. */
  identifiedBy?: string | null;
  /** Format da LDP e por que o conjunto de regiões (formato de folha) foi escolhido. */
  formatLdp?: string | null; sheetSource?: string | null;
}
export interface RunResult {
  executionId: string; startedAtUtc: string; finishedAtUtc: string; projectIdAcc: string; projectName: string | null;
  folder: string; packageId: number; packageName: string | null;
  summary: { total: number; compliant: number; divergent: number; outsideLdp: number; notIdentified: number; duplicates: number; readErrors: number; ambiguous?: number };
  documents: DocumentResult[]; auditPersistenceSucceeded: boolean; auditPersistenceError: string | null;
}

// N&A-first: o frontend envia apenas Project_ID_ACC. O hub necessário para ACC é resolvido no backend.
const query = (_hubId: string, projectId: string) => new URLSearchParams({ projectId }).toString();

async function jsonRequest<T>(url: string, method: "PUT" | "POST", body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method, credentials: "same-origin", headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let payload: (T & { message?: string }) | null = null;
  try { payload = await response.json(); } catch { /* resposta sem JSON */ }
  if (!response.ok) throw new Error(payload?.message ?? `Falha HTTP ${response.status}.`);
  return payload as T;
}

export const listPackages = (signal?: AbortSignal) => getJson<OcrPackage[]>("/api/admin/ocr/pacotes", signal);
export const getConfiguration = (hubId: string, projectId: string, signal?: AbortSignal) =>
  getJson<DocumentQualityConfiguration>(`/api/document-quality/configuration?${query(hubId, projectId)}`, signal);
export const saveConfiguration = (hubId: string, projectId: string, packageId: number | null, auditFolderId: string | null, auditFolderPath: string | null) =>
  jsonRequest<DocumentQualityConfiguration>(`/api/document-quality/configuration?${query(hubId, projectId)}`, "PUT", { packageId, auditFolderId, auditFolderPath });

// "Auditar agora" roda em segundo plano no servidor: o POST só enfileira (HTTP 202) e a tela consulta o status pelo executionId.
export type RunState = "QUEUED" | "RUNNING" | "COMPLETED" | "FAILED";
export interface RunStatus {
  executionId: string; status: RunState; total: number | null; processed: number; currentFile: string | null;
  queuedAtUtc: string; startedAtUtc: string | null; finishedAtUtc: string | null; error: string | null; result: RunResult | null;
}
export const startAudit = (hubId: string, projectId: string) =>
  jsonRequest<{ executionId: string; status: RunState; alreadyRunning: boolean }>(`/api/document-quality/runs?${query(hubId, projectId)}`, "POST");
/** Execução mais recente do projeto (null = nenhuma). Execuções anteriores continuam guardadas no servidor. */
export async function getLatestRun(hubId: string, projectId: string, signal?: AbortSignal): Promise<RunResult | null> {
  const r = await fetch(`/api/document-quality/runs/latest?${query(hubId, projectId)}`, { signal, credentials: "same-origin", headers: { Accept: "application/json" } });
  if (r.status === 204) return null;
  if (!r.ok) throw new Error(r.status === 403 ? "Sem permissão." : `Falha ao carregar a última auditoria (HTTP ${r.status}).`);
  return (await r.json()) as RunResult;
}
export const getRunStatus = (hubId: string, projectId: string, executionId: string, signal?: AbortSignal) =>
  getJson<RunStatus>(`/api/document-quality/runs/${encodeURIComponent(executionId)}?${query(hubId, projectId)}`, signal);
export const reportUrl = (hubId: string, projectId: string, executionId: string) =>
  `/api/document-quality/audits/${encodeURIComponent(executionId)}/report.csv?${query(hubId, projectId)}`;

// Coluna "Template Auditoria" da Configuração do Projeto (config.ProjetoConfiguracao.PacoteOcrId).
export const listProjectTemplates = (signal?: AbortSignal) =>
  getJson<{ projectIdAcc: string; packageId: number; packageName: string | null }[]>("/api/document-quality/project-templates", signal);
export const saveProjectTemplate = (projectId: string, packageId: number | null) =>
  jsonRequest<DocumentQualityConfiguration>(`/api/document-quality/project-template?${new URLSearchParams({ projectId })}`, "PUT", { packageId });

export interface DocumentPresenceLocation {
  fileName: string; folderPath: string; folderId: string; itemId: string;
  matchedBy: "N&A Document Number" | "Client Document Number" | null;
  matchedCode: string | null;
}
export interface DocumentPresenceRow {
  ldpRecordId: number;
  naDocumentNumber: string | null;
  clientDocumentNumber: string | null;
  title: string | null;
  discipline: string | null;
  documentStatus: string | null;
  situation: string | null;
  statusInFlow: string | null;
  isCancelled: boolean;
  status: "PRESENTE_NO_ACC" | "AUSENTE_NO_ACC" | "CANCELADO_PRESENTE_NO_ACC" | "CANCELADO_SEM_ACC";
  occurrences: number;
  locations: DocumentPresenceLocation[];
}
export interface DocumentPresenceAuditResult {
  projectId: string; executedAt: string; rootsScanned: number; foldersVisited: number; filesScanned: number; totalLdpDocuments: number;
  activeLdpDocuments: number; cancelledDocuments: number;
  present: number; missing: number; cancelledPresent: number; cancelledMissing: number;
  multipleOccurrences: number; outsideLdp: number; ambiguousFiles: number; unidentifiedFiles: number;
  documents: DocumentPresenceRow[]; outsideFiles: { fileName: string; folderPath: string; reason: string }[];
}
export async function getLatestPresenceAudit(projectId: string, signal?: AbortSignal): Promise<DocumentPresenceAuditResult | null> {
  const r = await fetch(`/api/document-presence-audit/latest?${new URLSearchParams({ projectId })}`, {
    signal, credentials: "same-origin", headers: { Accept: "application/json" },
  });
  if (r.status === 204) return null;
  if (!r.ok) throw new Error(r.status === 403 ? "Sem permissão." : `Falha ao carregar a última auditoria (HTTP ${r.status}).`);
  return (await r.json()) as DocumentPresenceAuditResult;
}
export const runPresenceAudit = (projectId: string) =>
  jsonRequest<DocumentPresenceAuditResult>(`/api/document-presence-audit/run?${new URLSearchParams({ projectId })}`, "POST");

export interface DisciplineAuditRow {
  rawDiscipline: string | null;
  disciplineStandardId: number | null;
  standardName: string | null;
  standardPt: string | null;
  standardEn: string | null;
  status: "PADRONIZADO" | "NAO_MAPEADO";
  documents: number;
  projectsCount: number;
  projects: string[];
}
export interface DisciplineAuditResult {
  scope: "PROJECT" | "ALL_ALLOWED_PROJECTS" | "ALL_ACTIVE_PROJECTS";
  executedAt: string;
  projectsAnalyzed: number;
  documents: number;
  rawNames: number;
  standardizedNames: number;
  unmappedNames: number;
  rows: DisciplineAuditRow[];
}
export interface DisciplineStandard {
  disciplineStandardId: number;
  standardName: string;
  portugueseName: string | null;
  englishName: string | null;
  active: boolean;
  aliases: string[];
}
export const getDisciplineAudit = (projectId: string, scope: "project" | "all", signal?: AbortSignal) =>
  getJson<DisciplineAuditResult>(`/api/discipline-audit?${new URLSearchParams(scope === "all" ? { scope: "all" } : { projectId })}`, signal);
export const getDisciplineStandards = (signal?: AbortSignal) =>
  getJson<DisciplineStandard[]>("/api/discipline-audit/standards", signal);
export const assignDisciplineAlias = (alias: string, disciplineStandardId: number) =>
  jsonRequest<{ ok: boolean }>("/api/discipline-audit/aliases", "POST", { alias, disciplineStandardId });
export const createDisciplineStandard = (
  body: { standardName: string; portugueseName: string | null; englishName: string | null; aliases?: string[] },
) => jsonRequest<{ disciplineStandardId: number }>("/api/discipline-audit/standards", "POST", body);
export const updateDisciplineStandard = (
  disciplineStandardId: number,
  body: { standardName: string; portugueseName: string | null; englishName: string | null },
) => jsonRequest<{ ok: boolean }>(`/api/discipline-audit/standards/${disciplineStandardId}`, "PUT", body);
