import type { ContextoTipo, RegraSigla } from "@/features/disciplinas/normalizadorSiglas";

export const ENDPOINT_NORMALIZADOR = "/api/configuracoes/normalizador-siglas";

export interface MapeamentoColunas { sigla: string; disciplina: string; contextoTipo?: string | null; contextoValor?: string | null; ativo?: string | null }
export type AcaoImportacao = "Incluir" | "Alterar" | "Inalterada" | "Rejeitada" | "Duplicada";
export interface LinhaPlano {
  linha: number; siglaOriginal: string | null; disciplinaCanonica: string | null; contextoTipo: string | null; contextoValor: string | null;
  ativo: boolean | null; acao: AcaoImportacao; motivo: string | null; conflitoComExistente: boolean; regraExistenteId: number | null;
  disciplinaAtual: string | null; ativoAtual: boolean | null;
}
export interface ResumoImportacao { incluidas: number; alteradas: number; inalteradas: number; rejeitadas: number; duplicadas: number; conflitosComExistente: number }
export interface PreviaImportacao {
  hashSha256: string; aba: string; abas: string[]; cabecalhos: string[]; mapeamentoSugerido: MapeamentoColunas | null; mapeamento: MapeamentoColunas | null;
  erros: string[]; plano: { linhas: LinhaPlano[] } | null; resumo: ResumoImportacao | null; amostra: Record<string, string | null>[];
}
export interface ResultadoImportacao { importacaoId: number; resumo: ResumoImportacao; linhas: LinhaPlano[] }
export interface ImportacaoRegistrada {
  id: number; nomeArquivo: string; hashSha256: string; aba: string; incluidas: number; alteradas: number; inalteradas: number; rejeitadas: number;
  duplicadas: number; importadoEm: string; importadoPor: string;
}
export interface DadosRegra { siglaOriginal: string; disciplinaCanonica: string; contextoTipo: ContextoTipo | null; contextoValor: string | null; ativo: boolean }

/** Erro com o status HTTP: 503+instalado=false = migração não aplicada; 404 = backend em execução sem o controller novo. */
export class NormalizadorError extends Error {
  constructor(public status: number, message: string, public naoInstalado = false) { super(message); }
}

async function chamar<T>(url: string, init?: RequestInit): Promise<T> {
  let r: Response;
  try { r = await fetch(url, { credentials: "same-origin", ...init, headers: { Accept: "application/json", ...(init?.headers ?? {}) } }); }
  catch { throw new NormalizadorError(0, "Sem conexão com o servidor."); }
  let corpo: { message?: string; instalado?: boolean; erros?: string[] } | null = null;
  const json = r.headers.get("content-type")?.includes("application/json");
  if (json) { try { corpo = await r.json(); } catch { /* corpo inválido */ } }
  if (r.ok) { if (r.status === 204) return undefined as T; if (!json) throw new NormalizadorError(502, "Resposta não-JSON (proxy /api do Vite?)."); return corpo as T; }
  const msg = r.status === 404 ? "O backend em execução ainda não tem o Normalizador de Siglas (404). Ele passa a existir após o deploy/reinício feito pelo responsável."
    : r.status === 403 ? "Você não tem permissão para esta ação (Configurações)."
    : r.status === 401 ? "Sua sessão expirou. Entre novamente."
    : corpo?.message ?? `Falha (HTTP ${r.status}).`;
  throw new NormalizadorError(r.status, msg, r.status === 503 && corpo?.instalado === false);
}

const json = (method: string, body: unknown): RequestInit => ({ method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

export const listarRegras = (signal?: AbortSignal) => chamar<{ instalado: boolean; regras: RegraSigla[] }>(ENDPOINT_NORMALIZADOR, { signal });
export const listarRegrasAtivas = (signal?: AbortSignal) => chamar<{ instalado: boolean; regras: RegraSigla[] }>(`${ENDPOINT_NORMALIZADOR}/ativas`, { signal });
export const listarImportacoes = (signal?: AbortSignal) => chamar<ImportacaoRegistrada[]>(`${ENDPOINT_NORMALIZADOR}/importacoes`, { signal });
export const criarRegra = (d: DadosRegra) => chamar<RegraSigla>(ENDPOINT_NORMALIZADOR, json("POST", { ...d, versao: 0 }));
export const atualizarRegra = (id: number, versao: number, d: DadosRegra) => chamar<RegraSigla>(`${ENDPOINT_NORMALIZADOR}/${id}`, json("PUT", { ...d, versao }));
export const definirAtivo = (id: number, versao: number, ativo: boolean) => chamar<RegraSigla>(`${ENDPOINT_NORMALIZADOR}/${id}/ativo`, json("POST", { ativo, versao }));
export const excluirRegra = (id: number, versao: number) => chamar<void>(`${ENDPOINT_NORMALIZADOR}/${id}?versao=${versao}`, { method: "DELETE" });

function formulario(arquivo: File, campos: Record<string, string | null | undefined>) {
  const f = new FormData();
  f.append("arquivo", arquivo, arquivo.name);
  for (const [k, v] of Object.entries(campos)) if (v !== null && v !== undefined) f.append(k, v);
  return f;
}
export const preverImportacao = (arquivo: File, aba: string | null, mapeamento: MapeamentoColunas | null, aplicarAlteracoes: boolean) =>
  chamar<PreviaImportacao>(`${ENDPOINT_NORMALIZADOR}/importacao/previa`, { method: "POST", body: formulario(arquivo,
    { aba, mapeamento: mapeamento ? JSON.stringify(mapeamento) : null, aplicarAlteracoes: String(aplicarAlteracoes) }) });
export const confirmarImportacao = (arquivo: File, hash: string, aba: string | null, mapeamento: MapeamentoColunas, aplicarAlteracoes: boolean) =>
  chamar<ResultadoImportacao>(`${ENDPOINT_NORMALIZADOR}/importacao/confirmar`, { method: "POST", body: formulario(arquivo,
    { hash, aba, mapeamento: JSON.stringify(mapeamento), aplicarAlteracoes: String(aplicarAlteracoes) }) });
