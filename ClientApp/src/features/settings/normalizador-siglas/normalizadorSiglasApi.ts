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
export type SituacaoSiglaSemMapeamento = "NAO_CADASTRADA" | "NAO_IDENTIFICADA" | "CONFIGURACAO_INCONSISTENTE" | "INFORMACAO_INSUFICIENTE";
export interface SiglaSemMapeamentoItem {
  sigla: string | null; projectId: string; projeto: string; documentoId: string; numeroDocumento: string | null;
  origemNumero: "NA" | "CLIENTE" | "AUSENTE"; nomeArquivo: string | null; identificadorOrigem: string;
  disciplinaOriginal: string | null; disciplinaNormalizada: string | null;
  situacao: SituacaoSiglaSemMapeamento; motivo: string;
}
export interface SiglasSemMapeamentoResumo {
  documentosAnalisados: number; comSiglaIdentificada: number; comDisciplinaNormalizada: number; siglasDistintas: number;
  documentosAfetados: number; projetosAfetados: number; documentosSemSiglaExtraivel: number; documentosComInformacaoInsuficiente: number;
  /** Linhas AT (atividade de esforço): válidas para HH/custo, fora da análise documental. */
  atividadesForaDaAnalise: number;
  /** Linhas do snapshot sem número identificável nem qualquer dado documental (ex.: "----" de linha vazia do template). */
  linhasDescartadas: number;
}
export interface LinhaDescartada { projectId: string; projeto: string; documentoId: string; identificadorOrigem: string; valorOriginal: string | null; motivo: string }
export interface SiglasSemMapeamentoResultado { versaoContrato: 4; itens: SiglaSemMapeamentoItem[]; resumo: SiglasSemMapeamentoResumo; descartadas: LinhaDescartada[] }
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
const validarDiagnostico = (valor: unknown): SiglasSemMapeamentoResultado => {
  const r = valor as Partial<SiglasSemMapeamentoResultado> | null;
  const resumo = r?.resumo as Record<string, unknown> | undefined;
  const campos = ["documentosAnalisados", "comSiglaIdentificada", "comDisciplinaNormalizada", "siglasDistintas",
    "documentosAfetados", "projetosAfetados", "documentosSemSiglaExtraivel", "documentosComInformacaoInsuficiente",
    "atividadesForaDaAnalise", "linhasDescartadas"];
  const itens = r?.itens;
  const itensValidos = Array.isArray(itens) && itens.every((x) => typeof x?.identificadorOrigem === "string" && x.identificadorOrigem.trim().length > 0);
  if (r?.versaoContrato !== 4 || !itensValidos || !Array.isArray(r.descartadas) || !resumo || campos.some((k) => !Number.isSafeInteger(resumo[k]) || Number(resumo[k]) < 0))
    throw new NormalizadorError(502, "Resposta incompatível do diagnóstico. Atualize/reinicie o backend para o contrato v4; os indicadores não foram calculados.");
  if ((itens?.length ?? -1) !== resumo.documentosAfetados)
    throw new NormalizadorError(502, "Resposta inconsistente: a quantidade de pendências difere dos documentos retornados.");
  if (r.descartadas.length !== resumo.linhasDescartadas)
    throw new NormalizadorError(502, "Resposta inconsistente: a quantidade de linhas descartadas difere das linhas retornadas.");
  return r as SiglasSemMapeamentoResultado;
};
export const diagnosticarSiglas = (projectIds: readonly string[], signal?: AbortSignal) =>
  chamar<unknown>(`${ENDPOINT_NORMALIZADOR}/sem-mapeamento?projectIds=${encodeURIComponent(projectIds.join(","))}`, { signal }).then(validarDiagnostico);
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

/* ------------------------------------------- EXCEÇÕES DE POSIÇÃO DA DISCIPLINA (26/10/2026, revisão BYD002) */
// Padrão SEMPRE DOC3. Exceção = número de documento exato (CODIGO) ou padrão limitado "DOC3 = X e DOC{posicao} = sigla exata"
// (PADRAO), só no projeto dela. Não existe configuração "DOC4 do projeto".
export const ENDPOINT_POSICAO = `${ENDPOINT_NORMALIZADOR}/posicao-disciplina`;
export type CriterioExcecao = "CODIGO" | "PADRAO";
export interface NovaExcecao { criterio: CriterioExcecao; codigoDocumento: string | null; segmentoDoc3: string | null; siglaEsperada: string; posicao: number }
export interface ExcecaoPosicao { id: number; criterio: CriterioExcecao; codigoDocumento: string | null; segmentoDoc3: string | null; siglaEsperada: string; posicao: number; ativo: boolean }
export interface ExcecaoRegistro extends ExcecaoPosicao {
  projectId: string; disciplinaNoCadastro: string; motivo: string | null; versao: number; criadoEm: string; criadoPor: string; alteradoEm: string; alteradoPor: string;
}
export type SituacaoImpacto = "CORRIGIDO" | "PIOROU" | "ALTERADO" | "PENDENTE";
export interface ImpactoDocumento {
  codigo: string; siglaAntes: string | null; posicaoAntes: number | null; disciplinaAntes: string | null;
  siglaDepois: string | null; posicaoDepois: number | null; disciplinaDepois: string | null; excecaoIdDepois: number | null; situacao: SituacaoImpacto; motivo: string | null;
}
export interface ImpactoResumo {
  documentosAnalisados: number; identificadosAntes: number; semMapeamentoAntes: number; identificadosDepois: number; semMapeamentoDepois: number;
  corrigidosPorExcecao: number; pioraram: number; disciplinaAlterada: number; lidosPorExcecaoDepois: number;
}
export interface ImpactoPosicao {
  projectId: string; projeto: string; semLdp: boolean; fonte: string; excecoesAntes: ExcecaoPosicao[]; excecoesDepois: ExcecaoPosicao[];
  resumo: ImpactoResumo; documentos: ImpactoDocumento[];
}
export interface CandidatoExcecao { codigo: string; doc3: string | null; sigla: string; posicao: number; disciplina: string }
export interface PendenteSemCandidato { codigo: string; doc3: string | null; segmentoPesquisado: string | null; motivo: string }
export interface CandidatosExcecao {
  projectId: string; projeto: string; semLdp: boolean; fonte: string; posicao: number; documentosAnalisados: number; semMapeamento: number;
  candidatos: CandidatoExcecao[]; pendentes: PendenteSemCandidato[];
}
const urlProjeto = (projectId: string) => `${ENDPOINT_POSICAO}/${encodeURIComponent(projectId)}`;
export const listarExcecoes = (projectId: string, signal?: AbortSignal) =>
  chamar<{ instalado: boolean; excecoes: ExcecaoRegistro[]; message?: string }>(`${ENDPOINT_POSICAO}?projectId=${encodeURIComponent(projectId)}`, { signal });
export const buscarCandidatos = (projectId: string, posicao: number, signal?: AbortSignal) =>
  chamar<CandidatosExcecao>(`${urlProjeto(projectId)}/candidatos?posicao=${posicao}`, { signal });
export const preverExcecoes = (projectId: string, novas: NovaExcecao[], desativar: number[] = [], signal?: AbortSignal) =>
  chamar<ImpactoPosicao>(`${urlProjeto(projectId)}/impacto`, { ...json("POST", { novas, desativar }), signal });
export const incluirExcecoes = (projectId: string, novas: NovaExcecao[], motivo: string | null, confirmarAlteracao: boolean) =>
  chamar<{ excecoes: ExcecaoRegistro[]; resumo: ImpactoResumo }>(`${urlProjeto(projectId)}/excecoes`, json("POST", { novas, motivo, confirmarAlteracao }));
export const definirAtivoExcecao = (projectId: string, id: number, versao: number, ativo: boolean) =>
  chamar<ExcecaoRegistro>(`${urlProjeto(projectId)}/excecoes/${id}/ativo`, json("POST", { ativo, versao }));
