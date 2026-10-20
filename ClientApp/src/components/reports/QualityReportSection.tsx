import { FormEvent, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, getJson } from '@/lib/api';
import { KpiCard, LoadingState, ReportState } from './ReportComponents';
import { limitedMap } from '@/components/pmo/pmoData';
import { mergeGruposQualidade, type PmoSelecionado } from '@/components/pmo/pmoMultiCore';
import ConsolidadoBanner from '@/components/pmo/ConsolidadoBanner';
import type { FalhaProjeto } from '@/components/pmo/useMergedPages';

interface QualityReport {
  totalRows: number; filteredRows: number; totalGroups: number; lastSync: string | null;
  page: number; pageSize: number;
  groups: { status: string | null; discipline: string | null; count: number }[];
}
const display = (value: string | null) => value === null ? 'Não informado' : value.trim() === '' ? '(Vazio)' : value;

export default function QualityReportSection({ selection }: { selection: PmoSelecionado[] }) {
  const [falhas, setFalhas] = useState<FalhaProjeto[]>([]);
  const [draft, setDraft] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);
  const [report, setReport] = useState<QualityReport>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const selectionKey = JSON.stringify(selection.map(p => [p.hubId, p.id]));
  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined); setReport(undefined);
    const PAGE = 25;
    (async () => {
      // Consolidado: as contagens por (status × disciplina) são aditivas; busca TODOS os grupos de cada projeto e pagina no cliente.
      const fs: FalhaProjeto[] = [];
      const parts = await limitedMap(selection, async p => {
        try {
          const load = (n: number) => getJson<QualityReport>(`/api/reports/quality?${new URLSearchParams({ hubId: p.hubId, projectId: p.id, status, page: String(n), pageSize: String(PAGE) })}`, abort.signal);
          const first = await load(1);
          const restantes = Array.from({ length: Math.min(200, Math.max(1, Math.ceil(first.totalGroups / first.pageSize))) - 1 }, (_, k) => k + 2);
          const more = await Promise.all(restantes.map(load));
          return { first, groups: [first, ...more].flatMap(r => r.groups) };
        } catch (e) { if (abort.signal.aborted) throw e; fs.push({ projectId: p.id, projectName: p.name, message: e instanceof ApiError ? e.message : 'Falha ao consultar o projeto.' }); return null; }
      });
      if (abort.signal.aborted) return;
      setFalhas(fs);
      if (fs.length === selection.length) { setError(fs[0]?.message ?? 'Não foi possível carregar os dados. Tente novamente.'); return; }
      const ok = parts.filter((x): x is NonNullable<typeof x> => x !== null);
      const groups = mergeGruposQualidade(ok.map(x => x.groups));
      const datas = ok.map(x => x.first.lastSync).filter((d): d is string => !!d).sort();
      setReport({ totalRows: ok.reduce((a, x) => a + x.first.totalRows, 0), filteredRows: ok.reduce((a, x) => a + x.first.filteredRows, 0), totalGroups: groups.length,
        lastSync: datas.length ? datas[datas.length - 1] : null, page, pageSize: PAGE, groups: groups.slice((page - 1) * PAGE, page * PAGE) });
    })().catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar os dados. Tente novamente.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [selectionKey, status, page, attempt]);
  function filter(event: FormEvent) {
    event.preventDefault(); setReport(undefined); setStatus(draft); setPage(1); setAttempt(attempt + 1);
  }
  const pages = report ? Math.max(1, Math.ceil(report.totalGroups / report.pageSize)) : 1;
  return <section aria-label="Qualidade — RNC">
    <form onSubmit={filter} className="flex flex-wrap items-end gap-3 mb-6">
      <label className="text-sm text-muted-foreground">Status da RNC
        <input aria-label="Status da RNC" maxLength={100} value={draft} onChange={e => setDraft(e.target.value)} placeholder="Digite o status completo"
          className="mt-2 block rounded-lg border border-border bg-background px-3 py-2 text-foreground" />
      </label>
      <button className="rounded-lg border border-border px-4 py-2" type="submit">Filtrar</button>
      <button className="rounded-lg border border-border px-4 py-2" type="button" onClick={() => { setDraft(''); setStatus(''); setPage(1); setReport(undefined); setAttempt(attempt + 1); }}>Limpar</button>
      <p className="text-xs text-muted-foreground w-full">Deixe vazio para todos os status. Use o texto exibido na tabela; a comparação segue o banco.</p>
    </form>
    <div className="mb-4"><ConsolidadoBanner selection={selection} falhas={falhas} /></div>
    {loading ? <LoadingState /> : error ? <ReportState title={error} error><button className="text-primary underline mr-4" onClick={() => setAttempt(attempt + 1)}>Tentar novamente</button><Link to="/login" className="underline">Entrar novamente</Link></ReportState> : report && <>
      <div className="grid sm:grid-cols-2 xl:grid-cols-3 gap-4 mb-5">
        <KpiCard label={selection.length > 1 ? 'Registros RNC (consolidado)' : 'Registros RNC do projeto'} value={report.totalRows} detail="Linhas no carregamento atual" />
        <KpiCard label="Registros no filtro" value={report.filteredRows} detail="Contagem em todas as páginas" />
        <KpiCard label="Última sincronização" value={<span className="text-base">{report.lastSync ? new Date(report.lastSync).toLocaleString('pt-BR') : 'Não informada'}</span>} detail="Horário de carga do projeto; não é a data da RNC" />
      </div>
      <p className="text-sm text-muted-foreground mb-6">Contagem de registros importados, agrupados por status e disciplina. Os status são apresentados como armazenados, sem classificação automática em abertas ou fechadas. O carregamento pode conter dados preservados de uma importação anterior.</p>
      {report.totalRows === 0 ? <ReportState title="Sem registros RNC disponíveis">Não há registros para este projeto. Isso não comprova ausência de não conformidades.</ReportState> : report.groups.length === 0 ? <ReportState title="Nenhum grupo nesta página">Ajuste o filtro ou volte à página anterior.</ReportState> : <div className="overflow-x-auto rounded-xl border border-border bg-card/50">
        <table className="w-full text-sm text-left"><caption className="text-left p-4 font-medium">Qualidade · {report.filteredRows} registros em {report.totalGroups} grupos</caption>
          <thead className="bg-secondary text-muted-foreground"><tr>{['Status da RNC', 'Disciplina', 'Registros'].map(h => <th key={h} scope="col" className="px-4 py-3 font-medium">{h}</th>)}</tr></thead>
          <tbody>{report.groups.map((group, index) => <tr key={index} className="border-t border-border hover:bg-secondary/50"><td className="px-4 py-4 whitespace-pre-wrap">{display(group.status)}</td><td className="px-4 py-4 whitespace-pre-wrap">{display(group.discipline)}</td><td className="px-4 py-4 tabular-nums">{group.count}</td></tr>)}</tbody>
        </table>
      </div>}
      {report.totalRows > 0 && <div className="flex justify-between items-center mt-5 gap-3"><button className="rounded-lg border border-border px-4 py-2 disabled:opacity-40" disabled={page === 1} onClick={() => { setReport(undefined); setPage(page - 1); }}>Anterior</button><p className="text-sm text-muted-foreground" aria-live="polite">Página {page} de {pages}</p><button className="rounded-lg border border-border px-4 py-2 disabled:opacity-40" disabled={page >= pages} onClick={() => { setReport(undefined); setPage(page + 1); }}>Próxima</button></div>}
    </>}
  </section>;
}
