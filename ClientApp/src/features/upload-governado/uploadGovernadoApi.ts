// Cliente do Envio governado pela LDP (/api/upload-governado). projectId/hubId sempre na query.
import { ApiError, getJson } from "@/lib/api";

/** Pasta-RAIZ protegida pela LDP: vale para ela e todas as subpastas (só a raiz é salva). */
export interface PastaProtegida { folderId: string; caminho: string }
export interface ConfiguracaoPastas { raizes: PastaProtegida[]; atualizadoEm: string | null; atualizadoPor: string | null }
export type StatusValidacao = "PRONTO" | "FORA_DA_LDP" | "DOCUMENTO_AMBIGUO" | "NOME_INVALIDO" | "PASTA_NAO_PROTEGIDA";
/** Status que permitem o envio (mesma regra do backend). */
export const permiteEnvio = (s: StatusValidacao) => s === "PRONTO" || s === "PASTA_NAO_PROTEGIDA";
export interface Validacao {
  arquivo: string; status: StatusValidacao; mensagem: string; documento: string | null; campoIdentificador: string | null;
  idRegistroLdp: number | null; disciplina: string | null; revisao: string | null; folderId: string | null; pasta: string | null;
}
export interface ResultadoEnvio { validacao: Validacao; resultado: "ENVIADO" | "BLOQUEADO" | "ERRO"; motivo: string; itemId: string | null; versionId: string | null }

export const ROTULO_STATUS: Record<StatusValidacao, string> = {
  PRONTO: "Pronto para envio", FORA_DA_LDP: "Fora da LDP", DOCUMENTO_AMBIGUO: "Documento ambíguo", NOME_INVALIDO: "Nome inválido",
  PASTA_NAO_PROTEGIDA: "Pasta sem regra da LDP",
};

// N&A-first: o frontend envia apenas Project_ID_ACC. O hub necessário para chamadas ACC é resolvido no backend.
const q = (_hubId: string, projectId: string, extra: Record<string, string> = {}) => new URLSearchParams({ projectId, ...extra }).toString();

async function enviarJson<T>(url: string, metodo: "PUT" | "POST", corpo: unknown): Promise<T> {
  const r = await fetch(url, { method: metodo, credentials: "same-origin", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify(corpo) });
  let b: (T & { message?: string }) | null = null;
  try { b = await r.json(); } catch { /* sem corpo */ }
  if (!r.ok) throw new Error(b?.message ?? (r.status === 403 ? "Sem permissão." : `Falha (HTTP ${r.status}).`));
  return b as T;
}

export const carregarConfiguracao = (hubId: string, projectId: string, signal?: AbortSignal) =>
  getJson<{ configuracao: ConfiguracaoPastas }>(`/api/upload-governado/pastas-config?${q(hubId, projectId)}`, signal);

export const salvarConfiguracao = (hubId: string, projectId: string, raizes: PastaProtegida[]) =>
  enviarJson<{ configuracao: ConfiguracaoPastas }>(`/api/upload-governado/pastas-config?${q(hubId, projectId)}`, "PUT", { raizes });

export const listarPastas = (hubId: string, projectId: string, folderId?: string, signal?: AbortSignal) =>
  getJson<{ folderId: string; pastas: { id: string; nome: string }[] }>(`/api/upload-governado/pastas?${q(hubId, projectId, folderId ? { folderId } : {})}`, signal);

/** Pasta de destino escolhida no envio. */
export interface Destino { folderId: string; caminho: string }

export const validarArquivos = (hubId: string, projectId: string, destino: Destino, arquivos: string[]) =>
  enviarJson<{ protegida: boolean; raiz: PastaProtegida | null; resultados: Validacao[] }>(
    `/api/upload-governado/validar?${q(hubId, projectId, { folderId: destino.folderId, caminho: destino.caminho })}`, "POST", { arquivos });

export async function enviarArquivos(hubId: string, projectId: string, destino: Destino, arquivos: File[]): Promise<ResultadoEnvio[]> {
  const form = new FormData();
  arquivos.forEach((f) => form.append("arquivos", f, f.name));
  const r = await fetch(`/api/upload-governado/enviar?${q(hubId, projectId, { folderId: destino.folderId, caminho: destino.caminho })}`,
    { method: "POST", credentials: "same-origin", body: form });
  let b: { resultados?: ResultadoEnvio[]; message?: string } | null = null;
  try { b = await r.json(); } catch { /* sem corpo */ }
  if (!r.ok || !b?.resultados) throw new ApiError(r.status, b?.message);
  return b.resultados;
}

/**
 * Envio de UM arquivo com progresso (XHR) — mesmo endpoint /enviar (o servidor revalida e grava o log).
 * Usado pelo envio dentro do navegador de pastas, para mostrar status/progresso por arquivo.
 */
export function enviarArquivoComProgresso(hubId: string, projectId: string, destino: Destino, arquivo: File,
  onProgresso: (pct: number) => void): Promise<ResultadoEnvio> {
  return new Promise((resolve, reject) => {
    const form = new FormData();
    form.append("arquivos", arquivo, arquivo.name);
    const xhr = new XMLHttpRequest();
    xhr.open("POST", `/api/upload-governado/enviar?${q(hubId, projectId, { folderId: destino.folderId, caminho: destino.caminho })}`);
    xhr.withCredentials = true;
    xhr.setRequestHeader("Accept", "application/json");
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgresso(Math.round((e.loaded / e.total) * 100)); };
    xhr.onerror = () => reject(new Error("Falha de conexão durante o envio."));
    xhr.onload = () => {
      let b: { resultados?: ResultadoEnvio[]; message?: string } | null = null;
      try { b = JSON.parse(xhr.responseText); } catch { /* sem corpo */ }
      if (xhr.status >= 200 && xhr.status < 300 && b?.resultados?.[0]) resolve(b.resultados[0]);
      else reject(new Error(b?.message ?? (xhr.status === 401 ? "Sessão expirada. Entre novamente." : xhr.status === 403 ? "Sem permissão para enviar arquivos neste projeto." : `Falha no envio (HTTP ${xhr.status}).`)));
    };
    xhr.send(form);
  });
}

/** Proteção da pasta (sem arquivos): a pasta está numa árvore protegida pela LDP? */
export const consultarProtecao = (hubId: string, projectId: string, folderId: string, signal?: AbortSignal) =>
  getJson<{ protegida: boolean; raiz: PastaProtegida | null }>(`/api/upload-governado/protecao?${q(hubId, projectId, { folderId })}`, signal);
