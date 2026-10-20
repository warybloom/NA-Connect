import { ApiError, getJson } from "@/lib/api";
import type { TvConfigResponse, TvConfiguration, TvPresentation } from "./tvLogic";

/**
 * Diagnóstico explícito (30/09/2026): a mensagem genérica "Não foi possível carregar os dados" escondia a causa real
 * — o Vite devolvia index.html (sem proxy para /api/tv-config → ApiError 502) e o backend em execução era anterior ao
 * controller (404). Cada causa agora aparece com o que fazer.
 */
export async function carregarConfiguracaoTv(signal?: AbortSignal): Promise<TvConfigResponse> {
  try {
    return await getJson<TvConfigResponse>("/api/tv-config", signal);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404)
      throw new Error("O servidor em execução ainda não tem o serviço de Telas para TV (/api/tv-config → 404). Reinicie o backend do N&A Connect.");
    if (e instanceof ApiError && e.status === 502)
      throw new Error("A chamada /api/tv-config não chegou ao backend (resposta não-JSON, ex.: proxy do Vite). Reinicie o `npm run dev` para aplicar o proxy /api.");
    if (e instanceof ApiError && e.status === 403) throw new Error("Você não tem permissão para ver as Telas para TV.");
    throw e;
  }
}

export class TvSaveError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

/** PUT com a revisão lida: 409 = outra sessão salvou antes (nunca sobrescreve em silêncio). */
export async function salvarConfiguracaoTv(revision: number, presentations: TvPresentation[]): Promise<TvConfiguration> {
  const r = await fetch("/api/tv-config", {
    method: "PUT",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ revision, presentations }),
  });
  let corpo: { configuration?: TvConfiguration; message?: string } | null = null;
  try { corpo = await r.json(); } catch { /* corpo não-JSON */ }
  if (r.ok && corpo?.configuration) return corpo.configuration;
  const padrao = r.status === 403 ? "Você não tem permissão para configurar as telas da TV."
    : r.status === 409 ? "A configuração foi alterada por outra sessão. Recarregue antes de salvar."
    : r.status === 401 ? "Sua sessão expirou. Entre novamente."
    : `Não foi possível salvar (HTTP ${r.status}).`;
  throw new TvSaveError(r.status, corpo?.message ?? padrao);
}
