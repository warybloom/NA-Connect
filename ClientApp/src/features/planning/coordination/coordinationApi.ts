// Cliente HTTP da Norma de Coordenação (api/coordination-standards/*) — persistência real, nunca demo.
// Ver Controllers/Coordination/CoordinationStandardsController.cs.

export interface NormaMapeamento { normaMapeamentoId: number; sigla: string; disciplina: string }
export interface Norma {
  normaId: number; nome: string; descricao: string | null; origem: 'N&A' | 'CLIENTE'; ativa: boolean;
  dataCriacao: string | null; dataAtualizacao: string | null; mapeamentos: NormaMapeamento[]; projetosVinculados: number;
}
export interface ImportPreviewLinha {
  linha: number; siglaOriginal: string | null; disciplinaOriginal: string | null;
  siglaNormalizada: string; valida: boolean; erros: string[]; avisos: string[];
}
export interface ImportPreviewResultado { linhas: ImportPreviewLinha[]; totalLinhas: number; linhasValidas: number; linhasInvalidas: number }
export interface UnmappedSiglaDiagnostic { sigla: string; lancamentos: number; hh: number | null; projetos: string[] }

async function json<T>(response: Response): Promise<T> {
  const contentType = response.headers.get('content-type') ?? '';
  let body: any = null;
  let parseFailed = false;
  try { body = await response.json(); } catch { parseFailed = true; }
  if (!response.ok) throw new Error(body?.message || `HTTP ${response.status}`);
  // Vite (dev) devolve o index.html da SPA (200, text/html) quando falta a regra de proxy — nunca
  // tratar isso como sucesso silencioso (ver checkpoint 17 / vite.config.ts).
  if (parseFailed || !contentType.includes('application/json'))
    throw new Error(`Resposta inesperada (content-type "${contentType}") — verifique a regra de proxy em vite.config.ts.`);
  return body as T;
}
const headers = { 'Content-Type': 'application/json' };
const opts = (method: string, body?: unknown): RequestInit =>
  ({ method, credentials: 'include', headers, body: body === undefined ? undefined : JSON.stringify(body) });

export const listarNormas = () => fetch('/api/coordination-standards/normas', { credentials: 'include' }).then(r => json<Norma[]>(r));
export const obterNorma = (normaId: number) => fetch(`/api/coordination-standards/normas/${normaId}`, { credentials: 'include' }).then(r => json<Norma>(r));
export const criarNorma = (nome: string, descricao: string, origem: string) =>
  fetch('/api/coordination-standards/normas', opts('POST', { nome, descricao, origem })).then(r => json<{ normaId: number }>(r));
export const atualizarNorma = (normaId: number, nome: string, descricao: string, origem: string, ativa: boolean) =>
  fetch(`/api/coordination-standards/normas/${normaId}`, opts('PUT', { nome, descricao, origem, ativa })).then(r => json<{ message: string }>(r));
export const excluirNorma = (normaId: number) =>
  fetch(`/api/coordination-standards/normas/${normaId}`, opts('DELETE')).then(r => json<{ message: string }>(r));

export const adicionarMapeamento = (normaId: number, sigla: string, disciplina: string) =>
  fetch(`/api/coordination-standards/normas/${normaId}/mapeamentos`, opts('POST', { sigla, disciplina })).then(r => json<{ normaMapeamentoId: number }>(r));
export const atualizarMapeamento = (mapeamentoId: number, sigla: string, disciplina: string) =>
  fetch(`/api/coordination-standards/mapeamentos/${mapeamentoId}`, opts('PUT', { sigla, disciplina })).then(r => json<{ message: string }>(r));
export const removerMapeamento = (mapeamentoId: number) =>
  fetch(`/api/coordination-standards/mapeamentos/${mapeamentoId}`, opts('DELETE')).then(r => json<{ message: string }>(r));

export const previewImportarExcel = (normaId: number, arquivo: File) => {
  const formData = new FormData(); formData.append('arquivo', arquivo);
  return fetch(`/api/coordination-standards/normas/${normaId}/importar-excel/preview`, { method: 'POST', credentials: 'include', body: formData })
    .then(r => json<ImportPreviewResultado>(r));
};
export const confirmarImportacao = (normaId: number, linhas: { sigla: string; disciplina: string }[]) =>
  fetch(`/api/coordination-standards/normas/${normaId}/importar-excel/confirmar`, opts('POST', { linhas })).then(r => json<{ message: string; norma: Norma }>(r));

export const obterNormaDoProjeto = (projectId: string) =>
  fetch(`/api/coordination-standards/projetos/${encodeURIComponent(projectId)}`, { credentials: 'include' }).then(r => json<{ normaId: number | null; norma: Norma | null }>(r));
export const definirNormaDoProjeto = (projectId: string, normaId: number) =>
  fetch(`/api/coordination-standards/projetos/${encodeURIComponent(projectId)}`, opts('PUT', { normaId })).then(r => json<{ message: string }>(r));

export const siglasNaoMapeadas = (projectIds: string[]) => {
  const params = new URLSearchParams(); projectIds.forEach(p => params.append('projectIds', p));
  return fetch(`/api/coordination-standards/diagnostico/siglas-nao-mapeadas?${params.toString()}`, { credentials: 'include' })
    .then(r => json<UnmappedSiglaDiagnostic[]>(r));
};
