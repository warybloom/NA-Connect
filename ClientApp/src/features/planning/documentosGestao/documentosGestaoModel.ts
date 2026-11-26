// Documentos de Gestão (Painel do Planejador) — lógica pura da lista: busca, filtros, seleção em lote e contadores.
// A classificação vem SEMPRE do backend (/api/planning/management-documents); aqui nada é inferido de título/disciplina/pessoa.

export interface DocumentoCandidato {
  documentKey: string; codigo: string; titulo: string | null; disciplina: string | null;
  status: string | null; situacao: string | null; atividade: boolean; linhas: number; gestao: boolean;
}
export interface CandidatosResposta {
  projectId: string; schemaAvailable: boolean; canEdit: boolean;
  documentos: DocumentoCandidato[]; linhasSemCodigo: number; classificadosForaDaLdp: string[];
}
export type Destino = 'GESTAO' | 'ENGENHARIA';
export interface ResultadoLote { target: Destino; solicitados: number; alterados: number; jaEstavam: number; foraDaLdp: string[]; invalidos: number }

export type FiltroClasse = 'todos' | 'gestao' | 'engenharia';
export interface FiltrosLista { busca: string; disciplina: string; classe: FiltroClasse }
export const FILTROS_INICIAIS: FiltrosLista = { busca: '', disciplina: '', classe: 'todos' };

const normalizar = (v: string | null | undefined) => (v ?? '').normalize('NFD').replace(/\p{Mn}/gu, '').toUpperCase().trim();

export function filtrarCandidatos(docs: DocumentoCandidato[], f: FiltrosLista): DocumentoCandidato[] {
  const termos = normalizar(f.busca).split(/\s+/).filter(Boolean);
  return docs.filter(d => {
    if (f.classe === 'gestao' && !d.gestao) return false;
    if (f.classe === 'engenharia' && d.gestao) return false;
    if (f.disciplina && normalizar(d.disciplina) !== normalizar(f.disciplina)) return false;
    if (!termos.length) return true;
    const texto = `${normalizar(d.codigo)} ${normalizar(d.titulo)} ${normalizar(d.disciplina)} ${normalizar(d.status)}`;
    return termos.every(t => texto.includes(t));
  });
}

export const disciplinasDe = (docs: DocumentoCandidato[]) =>
  [...new Set(docs.map(d => d.disciplina?.trim()).filter((x): x is string => !!x))].sort((a, b) => a.localeCompare(b, 'pt-BR'));

/** Todos os filtrados já selecionados → desmarca só os filtrados; senão marca todos os filtrados (mantém o resto da seleção). */
export function alternarTodosFiltrados(selecionados: Set<string>, filtrados: DocumentoCandidato[]): Set<string> {
  const n = new Set(selecionados);
  const todos = filtrados.length > 0 && filtrados.every(d => n.has(d.documentKey));
  filtrados.forEach(d => (todos ? n.delete(d.documentKey) : n.add(d.documentKey)));
  return n;
}

export function contadores(docs: DocumentoCandidato[], selecionados: Set<string>) {
  const gestao = docs.filter(d => d.gestao).length;
  const sel = docs.filter(d => selecionados.has(d.documentKey));
  return { total: docs.length, gestao, engenharia: docs.length - gestao, selecionados: sel.length,
    selecionadosParaGestao: sel.filter(d => !d.gestao).length, selecionadosParaEngenharia: sel.filter(d => d.gestao).length };
}

/** Somente chaves que existem na lista atual (seleção que sobrou de outro projeto/carga nunca vai ao backend). */
export const chavesDoLote = (docs: DocumentoCandidato[], selecionados: Set<string>) =>
  docs.filter(d => selecionados.has(d.documentKey)).map(d => d.documentKey);

export function mensagemResultado(r: ResultadoLote): string {
  const acao = r.target === 'GESTAO' ? 'enviado(s) para Documentos de Gestão' : 'retornado(s) para Engenharia';
  const partes = [`${r.alterados} ${acao}`];
  if (r.jaEstavam) partes.push(`${r.jaEstavam} já estava(m) nesse grupo`);
  if (r.foraDaLdp.length) partes.push(`${r.foraDaLdp.length} fora da LDP atual (ignorado[s])`);
  if (r.invalidos) partes.push(`${r.invalidos} inválido(s)`);
  return partes.join(' · ') + '.';
}
