import type { CandidatosResposta, Destino, ResultadoLote } from './documentosGestaoModel';

export const DOCUMENTOS_GESTAO_API = '/api/planning/management-documents';

export class DocumentosGestaoError extends Error { constructor(message: string, public status: number, public code?: string) { super(message); } }

async function chamar<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, { credentials: 'include', ...init, headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}) } });
  if (!r.ok) {
    const corpo = (await r.json().catch(() => ({}))) as { message?: string; code?: string };
    throw new DocumentosGestaoError(corpo.message || (r.status === 403 ? 'Você não tem permissão para esta funcionalidade.' : r.status === 401 ? 'Sessão expirada.' : 'Não foi possível concluir a operação.'), r.status, corpo.code);
  }
  return r.json() as Promise<T>;
}

const qs = (projectId: string) => `projectId=${encodeURIComponent(projectId)}`;

export const documentosGestaoApi = {
  candidatos: (projectId: string, signal?: AbortSignal) => chamar<CandidatosResposta>(`${DOCUMENTOS_GESTAO_API}/candidates?${qs(projectId)}`, { signal }),
  enviarLote: (projectId: string, documentKeys: string[], target: Destino) =>
    chamar<ResultadoLote>(`${DOCUMENTOS_GESTAO_API}/batch?${qs(projectId)}`, { method: 'POST', body: JSON.stringify({ projectId, documentKeys, target }) }),
};
