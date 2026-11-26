import { tvPortalUrl } from "@/features/tv/portal/portalTransport";

/* 03 DOCUMENTAÇÃO — contrato frontend dos fluxos de revisão (/api/review-flows, backend a cargo do Codex). Cadastro em
   Painel de Planejamento → Fluxos de Revisão; a 03 só lê.
   Contrato documentado em _agent_handoffs/DOC03_FRONTEND_CONTRACT.md. Tudo o que toca a API fica concentrado aqui. */

export const ENDPOINT_REVIEW_FLOWS = "/api/review-flows";

/** Etapa de um fluxo. `statusInFlowValues` = valores de LdpAtual.Status_In_Flow (Forma/ACC) que significam esta etapa. */
export interface ReviewFlowStage { id: string; name: string; order: number; statusInFlowValues: string[] }
export interface ReviewFlow { id: string; name: string; active: boolean; stages: ReviewFlowStage[]; version: number }
/**
 * Vínculo de um fluxo a UM projeto (scope "project", projectId) ou a um GRUPO de projetos (scope "group", groupName + projectIds).
 * Projeto com vínculo direto e também em grupo: o vínculo direto prevalece. Projeto sem nenhum = PENDENTE DE CONFIGURAÇÃO.
 */
export interface ReviewFlowBinding {
  id: string; flowId: string; scope: "project" | "group";
  projectId: string | null; groupName: string | null;
  /** Projetos do grupo (ACC Project IDs); [] em vínculo de projeto. */
  projectIds: string[];
  version: number;
}

/** Etapa efetiva para um projeto; applicable=false = a etapa existe no fluxo mas não se aplica ao projeto (exibida N/A). */
export interface EffectiveStage { id: string; name: string; order: number; applicable: boolean; statusInFlowValues: string[] }
export interface EffectiveProjectFlow {
  projectId: string;
  flowId: string | null; flowName: string | null;
  /** De onde veio o fluxo: vínculo do projeto › vínculo de grupo › nenhum (PENDENTE DE CONFIGURAÇÃO, stages = []). */
  source: "project" | "group" | "none";
  /** Nome do grupo quando source = "group". */
  groupName?: string | null;
  stages: EffectiveStage[];
}
export interface EffectiveFlowsResponse { projects: EffectiveProjectFlow[] }

export interface DadosFluxo { name: string; active: boolean; stages: (Omit<ReviewFlowStage, "id"> & { id?: string })[] }
export interface DadosVinculo { flowId: string; scope: "project" | "group"; projectId: string | null; groupName: string | null; projectIds: string[] }

/** Erro com status HTTP: 0 = sem conexão; 404 = backend em execução ainda sem /api/review-flows. */
export class ReviewFlowsError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function chamar<T>(url: string, init?: RequestInit): Promise<T> {
  let r: Response;
  try { r = await fetch(tvPortalUrl(url), { credentials: "same-origin", ...init, headers: { Accept: "application/json", ...(init?.headers ?? {}) } }); }
  catch (e) { if (e instanceof DOMException && e.name === "AbortError") throw e; throw new ReviewFlowsError(0, "Sem conexão com o servidor."); }
  let corpo: { message?: string } | null = null;
  const json = r.headers.get("content-type")?.includes("application/json");
  if (json) { try { corpo = await r.json(); } catch { /* corpo inválido */ } }
  if (r.ok) { if (r.status === 204) return undefined as T; if (!json) throw new ReviewFlowsError(502, "Resposta não-JSON de /api/review-flows (proxy /api do Vite?)."); return corpo as T; }
  const msg = r.status === 404 ? "O backend em execução ainda não tem a API de fluxos de revisão (/api/review-flows, 404)."
    : r.status === 403 ? "Você não tem permissão para esta ação (Painel de Planejamento → Fluxos de Revisão)."
    : r.status === 401 ? "Sua sessão expirou. Entre novamente."
    : corpo?.message ?? `Falha em /api/review-flows (HTTP ${r.status}).`;
  throw new ReviewFlowsError(r.status, msg);
}
const json = (method: string, body: unknown): RequestInit => ({ method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

export const obterFluxosEfetivos = (projectIds: readonly string[], signal?: AbortSignal) =>
  chamar<EffectiveFlowsResponse>(`${ENDPOINT_REVIEW_FLOWS}/effective?` + new URLSearchParams({ projectIds: projectIds.join(",") }), { signal });
export const listarFluxos = (signal?: AbortSignal) => chamar<{ flows: ReviewFlow[]; bindings: ReviewFlowBinding[] }>(ENDPOINT_REVIEW_FLOWS, { signal })
  .then(r => ({ flows: Array.isArray(r?.flows) ? r.flows : [],
    bindings: (Array.isArray(r?.bindings) ? r.bindings : []).map(b => ({ ...b, projectIds: Array.isArray(b.projectIds) ? b.projectIds : [] })) }));
export const criarFluxo = (d: DadosFluxo) => chamar<ReviewFlow>(ENDPOINT_REVIEW_FLOWS, json("POST", { ...d, version: 0 }));
export const atualizarFluxo = (id: string, version: number, d: DadosFluxo) => chamar<ReviewFlow>(`${ENDPOINT_REVIEW_FLOWS}/${encodeURIComponent(id)}`, json("PUT", { ...d, version }));
export const criarVinculo = (d: DadosVinculo) => chamar<ReviewFlowBinding>(`${ENDPOINT_REVIEW_FLOWS}/bindings`, json("POST", { ...d, version: 0 }));
export const atualizarVinculo = (id: string, version: number, d: DadosVinculo) =>
  chamar<ReviewFlowBinding>(`${ENDPOINT_REVIEW_FLOWS}/bindings/${encodeURIComponent(id)}`, json("PUT", { ...d, version }));

export interface ReviewFlowCandidate { portuguese: string; english: string; kind: string; aliases: string[] }
export interface ReviewStatusSuggestion {
  projectId: string; original: string; count: number; portuguese: string | null; english: string | null;
  kind: string | null; candidates: string[]; suggestedStage: string | null;
  mappedStageId: string | null; situation: "mapeado" | "pendente" | "sem correspondência";
}
export const obterCatalogo = (signal?: AbortSignal) =>
  chamar<{ candidates: ReviewFlowCandidate[] }>(`${ENDPOINT_REVIEW_FLOWS}/catalog`, { signal });
export const obterSugestoes = (projectIds: readonly string[], signal?: AbortSignal) =>
  chamar<{ statuses: ReviewStatusSuggestion[] }>(`${ENDPOINT_REVIEW_FLOWS}/suggestions?` + new URLSearchParams({ projectIds: projectIds.join(",") }), { signal });
