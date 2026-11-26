// Painel do Planejador → Documentos de Gestão: o planejador escolhe, por projeto, quais documentos reais da LDP atual são
// Documentos de Gestão. Os demais documentos elegíveis são Engenharia. Grava em lote; nunca alega gravação sem resposta do backend.
import { useEffect, useMemo, useState } from 'react';
import { ArrowLeftRight, Loader2, Search, ShieldAlert } from 'lucide-react';
import { useProjetoAtual } from '@/features/project/ProjetoAtualContext';
import { documentosGestaoApi, DocumentosGestaoError } from './documentosGestaoApi';
import {
  FILTROS_INICIAIS, alternarTodosFiltrados, chavesDoLote, contadores, disciplinasDe, filtrarCandidatos, mensagemResultado,
  type CandidatosResposta, type Destino, type FiltrosLista,
} from './documentosGestaoModel';

const LIMITE_LINHAS = 500;

export function DocumentosGestaoSection() {
  const { projetoAtual } = useProjetoAtual();
  const projectId = projetoAtual?.projectId;
  const [dados, setDados] = useState<CandidatosResposta>();
  const [erro, setErro] = useState<string>();
  const [carregando, setCarregando] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  const [filtros, setFiltros] = useState<FiltrosLista>(FILTROS_INICIAIS);
  const [selecionados, setSelecionados] = useState<Set<string>>(new Set());
  const [enviando, setEnviando] = useState<Destino | null>(null);
  const [resultado, setResultado] = useState<{ ok: boolean; texto: string }>();

  useEffect(() => {
    if (!projectId) return;
    const abort = new AbortController();
    setCarregando(true); setErro(undefined);
    documentosGestaoApi.candidatos(projectId, abort.signal)
      .then(d => { if (!abort.signal.aborted) setDados(d); })
      .catch(e => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : 'Falha ao carregar documentos.'); })
      .finally(() => { if (!abort.signal.aborted) setCarregando(false); });
    return () => abort.abort();
  }, [projectId, tentativa]);
  // Trocar de projeto nunca carrega seleção de outro projeto.
  useEffect(() => { setSelecionados(new Set()); setFiltros(FILTROS_INICIAIS); setResultado(undefined); setDados(undefined); }, [projectId]);

  const docs = dados?.documentos ?? [];
  const filtrados = useMemo(() => filtrarCandidatos(docs, filtros), [docs, filtros]);
  const disciplinas = useMemo(() => disciplinasDe(docs), [docs]);
  const c = contadores(docs, selecionados);
  const todosFiltradosMarcados = filtrados.length > 0 && filtrados.every(d => selecionados.has(d.documentKey));
  const podeGravar = dados?.schemaAvailable === true && dados.canEdit;

  async function enviar(destino: Destino) {
    if (!projectId || !podeGravar) return;
    const lote = chavesDoLote(docs, selecionados);
    if (!lote.length) return;
    setEnviando(destino); setResultado(undefined);
    try {
      const r = await documentosGestaoApi.enviarLote(projectId, lote, destino);
      setResultado({ ok: true, texto: mensagemResultado(r) });
      setSelecionados(new Set());
      setTentativa(t => t + 1);
    } catch (e) {
      setResultado({ ok: false, texto: `Nada foi gravado: ${e instanceof DocumentosGestaoError || e instanceof Error ? e.message : 'falha desconhecida'}` });
    } finally { setEnviando(null); }
  }

  if (!projectId) return <p className="rounded-lg border border-border bg-card/60 p-6 text-sm text-muted-foreground">Selecione um projeto na Home para classificar os Documentos de Gestão.</p>;

  return (
    <section className="space-y-3" data-testid="documentos-gestao">
      <header className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h2 className="text-base font-bold">Documentos de Gestão {projetoAtual?.nome && <span className="font-normal text-muted-foreground">· {projetoAtual.nome}</span>}</h2>
          <p className="text-xs text-muted-foreground">Marque os documentos de gestão do projeto. Todo documento elegível não marcado é Engenharia nas telas Controle de HH / Custo. A marcação é por número do documento e se mantém entre cargas da LDP.</p>
        </div>
        <div className="flex flex-wrap gap-2 text-[11px]" aria-label="Contadores">
          {[['Documentos', c.total], ['Gestão', c.gestao], ['Engenharia', c.engenharia], ['Selecionados', c.selecionados]].map(([r, v]) => (
            <span key={r} className="rounded-md border border-border bg-card/60 px-2 py-1"><span className="text-muted-foreground">{r}</span> <b className="tabular-nums">{Number(v).toLocaleString('pt-BR')}</b></span>
          ))}
        </div>
      </header>

      {dados && !dados.schemaAvailable && (
        <p role="status" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-600 dark:text-amber-300">
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0" />
          Persistência pendente: a migração Migrations/Planning/20261029_ManagementDocuments.sql ainda não foi aplicada. A lista é real (LDP atual), mas nenhuma seleção pode ser salva até a migração ser executada.
        </p>
      )}
      {dados?.schemaAvailable && !dados.canEdit && (
        <p role="status" className="rounded-md border border-border bg-card/60 px-3 py-2 text-xs text-muted-foreground">Somente leitura: gravar exige a permissão “Classificar Documentos de Gestão” neste projeto.</p>
      )}
      {dados && (dados.linhasSemCodigo > 0 || dados.classificadosForaDaLdp.length > 0) && (
        <p className="text-[11px] text-amber-600 dark:text-amber-300">
          {dados.linhasSemCodigo > 0 && <>{dados.linhasSemCodigo} linha(s) da LDP sem número de documento não podem ser classificadas (permanecem em Engenharia). </>}
          {dados.classificadosForaDaLdp.length > 0 && <>{dados.classificadosForaDaLdp.length} documento(s) marcados como Gestão não estão na LDP atual (marcação preservada): {dados.classificadosForaDaLdp.slice(0, 5).join(', ')}{dados.classificadosForaDaLdp.length > 5 ? '…' : ''}.</>}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-card/60 p-2">
        <label className="flex min-w-[16rem] flex-1 items-center gap-2 rounded-md border border-border bg-background px-2 py-1 text-sm">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input value={filtros.busca} onChange={e => setFiltros({ ...filtros, busca: e.target.value })} placeholder="Buscar por código, título, disciplina ou status"
            className="w-full bg-transparent outline-none" aria-label="Buscar documentos" />
        </label>
        <select value={filtros.disciplina} onChange={e => setFiltros({ ...filtros, disciplina: e.target.value })} aria-label="Disciplina"
          className="rounded-md border border-border bg-background px-2 py-1 text-sm">
          <option value="">Todas as disciplinas</option>
          {disciplinas.map(d => <option key={d} value={d}>{d}</option>)}
        </select>
        <select value={filtros.classe} onChange={e => setFiltros({ ...filtros, classe: e.target.value as FiltrosLista['classe'] })} aria-label="Classificação"
          className="rounded-md border border-border bg-background px-2 py-1 text-sm">
          <option value="todos">Gestão e Engenharia</option><option value="gestao">Somente Gestão</option><option value="engenharia">Somente Engenharia</option>
        </select>
        <button type="button" onClick={() => setSelecionados(alternarTodosFiltrados(selecionados, filtrados))} disabled={!filtrados.length}
          className="rounded-md border border-border px-2 py-1 text-xs hover:bg-accent/40 disabled:opacity-50">
          {todosFiltradosMarcados ? 'Desmarcar filtrados' : `Selecionar todos os filtrados (${filtrados.length})`}
        </button>
        {selecionados.size > 0 && <button type="button" onClick={() => setSelecionados(new Set())} className="text-xs text-muted-foreground underline">Limpar seleção</button>}
        <div className="ml-auto flex flex-wrap gap-2">
          <button type="button" disabled={!podeGravar || !c.selecionados || enviando !== null} onClick={() => enviar('GESTAO')}
            className="inline-flex items-center gap-1 rounded-md bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-500 disabled:opacity-50">
            {enviando === 'GESTAO' ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <ArrowLeftRight className="h-3.5 w-3.5" />} Enviar selecionados para Documentos de Gestão
          </button>
          <button type="button" disabled={!podeGravar || !c.selecionados || enviando !== null} onClick={() => enviar('ENGENHARIA')}
            className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs font-semibold hover:bg-accent/40 disabled:opacity-50">
            {enviando === 'ENGENHARIA' && <Loader2 className="h-3.5 w-3.5 animate-spin" />} Retornar selecionados para Engenharia
          </button>
        </div>
      </div>
      {resultado && <p role="status" className={`text-xs ${resultado.ok ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-500'}`}>{resultado.texto}</p>}

      {erro ? <p role="alert" className="text-sm text-rose-500">{erro} <button className="underline" onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></p>
        : carregando && !dados ? <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" /> Carregando documentos da LDP…</p>
        : (
          <div className="max-h-[60vh] overflow-auto rounded-lg border border-border custom-scrollbar">
            <table className="w-full table-fixed text-xs">
              <thead className="sticky top-0 z-10 bg-card text-[10px] uppercase text-muted-foreground">
                <tr className="border-b border-border">
                  <th className="w-9 px-2 py-1.5"><input type="checkbox" checked={todosFiltradosMarcados} onChange={() => setSelecionados(alternarTodosFiltrados(selecionados, filtrados))} aria-label="Selecionar todos os filtrados" /></th>
                  <th className="w-[18%] px-2 py-1.5 text-left">Código</th><th className="px-2 py-1.5 text-left">Título</th>
                  <th className="w-[14%] px-2 py-1.5 text-left">Disciplina</th><th className="w-[13%] px-2 py-1.5 text-left">Status</th><th className="w-[11%] px-2 py-1.5 text-left">Classificação</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {filtrados.slice(0, LIMITE_LINHAS).map(d => (
                  <tr key={d.documentKey} className={selecionados.has(d.documentKey) ? 'bg-sky-500/10' : 'hover:bg-accent/40'}>
                    <td className="px-2 py-1"><input type="checkbox" checked={selecionados.has(d.documentKey)} aria-label={`Selecionar ${d.codigo}`}
                      onChange={() => { const n = new Set(selecionados); if (n.has(d.documentKey)) n.delete(d.documentKey); else n.add(d.documentKey); setSelecionados(n); }} /></td>
                    <td className="truncate px-2 py-1 font-medium" title={d.codigo}>{d.codigo}{d.atividade && <span className="ml-1 rounded bg-muted px-1 text-[9px]">AT</span>}</td>
                    <td className="truncate px-2 py-1" title={d.titulo ?? ''}>{d.titulo ?? '—'}</td>
                    <td className="truncate px-2 py-1" title={d.disciplina ?? ''}>{d.disciplina ?? '—'}</td>
                    <td className="truncate px-2 py-1" title={[d.status, d.situacao].filter(Boolean).join(' · ')}>{d.status ?? d.situacao ?? '—'}</td>
                    <td className="px-2 py-1">{d.gestao
                      ? <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-300">Gestão</span>
                      : <span className="rounded-full bg-sky-500/10 px-2 py-0.5 text-[10px] font-bold text-sky-600 dark:text-sky-300">Engenharia</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {filtrados.length === 0 && <p className="p-4 text-center text-xs text-muted-foreground">Nenhum documento para os filtros.</p>}
            {filtrados.length > LIMITE_LINHAS && <p className="p-2 text-center text-[11px] text-muted-foreground">Exibindo {LIMITE_LINHAS} de {filtrados.length} — refine a busca. “Selecionar todos os filtrados” inclui todos os {filtrados.length}.</p>}
          </div>
        )}
    </section>
  );
}

export default DocumentosGestaoSection;
