// Cliente e tipos da integração Plant 3D ↔ Dashboard BIM (CONTRATO_02). Prefixo /api/plant-integration.
// Chave de projeto = Project_ID_ACC (b.<guid>). Nenhum dado é inventado aqui: o que o backend não informa fica nulo.

export const PLANT_API = "/api/plant-integration";

export type AttributeMap = { id: string; name: string; pidCol: string; pipingCol: string; isTagPart?: boolean; isFixedTag?: boolean };
export type CategoryMapping = {
  id: string;
  label: string;
  pidTable: string;
  pipingTable: string;
  isComposedTag?: boolean;
  delimiter?: string;
  attributes: AttributeMap[];
};

export type PlantProject = { projectIdAcc: string; name: string; dashboardProjectId: string | null };
export type DashboardCatalogProject = {
  dashboardProjectId: string;
  projectCode: string;
  sourceProvider?: string | null;
  externalProjectId?: string | null;
};

export type VersionInfo = { completedVersions?: number; latestCompletedAtUtc?: string | null; hasBaseline?: boolean };
export type PlantStatus = {
  projectIdAcc: string;
  dashboardProjectId?: string | null;
  pnId?: VersionInfo;
  piping?: VersionInfo;
  pnIdDelta?: VersionInfo;
  pipingDelta?: VersionInfo;
  revisionToken?: string | null;
  filtersUpdatedAtUtc?: string | null;
  message?: string;
};

export type LatestVersion = { versionId?: string; atUtc?: string | null; rows?: number } | null;
export type PlantSchema = {
  projectIdAcc: string;
  dashboardProjectId?: string | null;
  pid: Record<string, string[]>;
  piping: Record<string, string[]>;
  latest?: { pnid?: LatestVersion; piping?: LatestVersion };
};

export type ResultsStatus = "ready" | "no-baseline" | "no-mapping" | "unlinked";
export type ResultsTotals = {
  pid: number; piping: number; matched: number; missing3d: number; extra3d: number;
  divergent: number; taglessPid: number; tagless3d: number;
};
export type ResultsCategory = {
  id: string; label: string; pidCount: number; pipingCount: number; matched: number; missing3d: number;
  extra3d: number; divergent: number; taglessPid: number; tagless3d: number;
};
export type ResultItem = {
  id: string; category: string; tag: string | null; classification: string;
  pidTable?: string | null; pipingTable?: string | null; pidRowId?: string | null; pipingRowId?: string | null;
  pidDrawing?: string | null; pipingDrawing?: string | null; details?: unknown;
};
export type PlantResults = {
  projectIdAcc: string;
  dashboardProjectId?: string | null;
  updatedAtUtc?: string | null;
  revisionToken?: string | null;
  status: ResultsStatus;
  totals: ResultsTotals;
  categories: ResultsCategory[];
  items: ResultItem[];
  totalItems: number;
  offset: number;
  limit: number;
};

export class PlantApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function plantRequest<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(PLANT_API + path, { credentials: "same-origin", ...init });
  } catch (error) {
    if ((error as { name?: string })?.name === "AbortError") throw error;
    throw new PlantApiError(0, "Sem resposta do servidor N&A.");
  }
  if (!response.ok) {
    let detalhe = "";
    try {
      const texto = await response.text();
      try { detalhe = (JSON.parse(texto) as { message?: string })?.message ?? ""; } catch { detalhe = texto.slice(0, 200); }
    } catch { /* corpo ilegível */ }
    throw new PlantApiError(response.status, detalhe);
  }
  return response.json() as Promise<T>;
}

export const projetoPath = (projectIdAcc: string) => "/projects/" + encodeURIComponent(projectIdAcc);

export const isAbort = (error: unknown) => (error as { name?: string })?.name === "AbortError";

/** Mensagem explícita e acionável por código HTTP. Nunca devolve "sucesso". */
export function descreverErro(error: unknown, acao: string): string {
  if (error instanceof PlantApiError) {
    const msg = error.message ? " " + error.message : "";
    switch (error.status) {
      case 0: return `${acao}: sem resposta do servidor. Verifique se o backend N&A está no ar.`;
      case 400: return `${acao}: dados inválidos.${msg}`;
      case 401: return `${acao}: sessão expirada. Entre novamente.`;
      case 403: return `${acao}: sem permissão para este projeto/ação.${msg}`;
      case 404: return `${acao}: recurso não encontrado — este endpoint ainda não existe no backend ou o projeto não está vinculado.${msg}`;
      case 409: return `${acao}: conflito com o histórico do Dashboard BIM (versão-base ausente, cadeia inconsistente ou vínculo já usado).${msg}`;
      case 503: return `${acao}: integração indisponível.${msg}`;
      default: return `${acao}: HTTP ${error.status}.${msg}`;
    }
  }
  return `${acao}: ${error instanceof Error ? error.message : String(error)}`;
}

/** UTC do backend → horário local. Sem fuso explícito, assume UTC. Nulo/ inválido → null (nunca inventa data). */
export function formatarUtc(valor?: string | null): string | null {
  if (!valor) return null;
  const temFuso = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(valor);
  const data = new Date(temFuso ? valor : valor + "Z");
  if (Number.isNaN(data.getTime())) return null;
  return data.toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "medium" });
}
