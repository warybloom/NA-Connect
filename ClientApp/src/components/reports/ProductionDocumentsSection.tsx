import { FormEvent, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, getJson } from '@/lib/api';
import { KpiCard, LoadingState, ReportState } from './ReportComponents';

interface ProductionDocumentsReport {
  totalRows: number; filteredRows: number; totalGroups: number;
  totalDisciplines: number; documentsWithoutStatus: number; documentsCompleted: number;
  lastSync: string | null; page: number; pageSize: number;
  groups: { documentStatus: string | null; discipline: string | null; count: number }[];
}
const display = (value: string | null) => value === null ? 'Não informado' : value.trim() === '' ? '(Vazio)' : value;

export default function ProductionDocumentsSection({ hub, project }: { hub: string; project: string }) {
  const [draft, setDraft] = useState('');
  const [documentStatus, setDocumentStatus] = useState('');
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);
  const [report, setReport] = useState<ProductionDocumentsReport>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined); setReport(undefined);
    const query = new URLSearchParams({ hubId: hub, projectId: project, documentStatus, page: String(page), pageSize: '25' });
    getJson<ProductionDocumentsReport>(`/api/reports/documents?${query}`, abort.signal)
      .then(data => { if (!abort.signal.aborted) setReport(data); })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar os dados. Tente novamente.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [hub, project, documentStatus, page, attempt]);
  function filter(event: FormEvent) {
    event.preventDefault(); setReport(undefined); setDocumentStatus(draft); setPage(1); setAttempt(attempt + 1);
  }
  const pages = report ? Math.max(1, Math.ceil(report.totalGroups / report.pageSize)) : 1;
  return <section aria-label="Produção — Documentos">
    <form onSubmit={filter} className="flex flex-wrap items-end gap-3 mb-6">
      <label className="text-sm text-muted-foreground">Document Status
        <input aria-label="Document Status" maxLength={100} value={draft} onChange={e => setDraft(e.target.value)} placeholder="Digite o status completo"
          className="mt-2 block rounded-lg border border-border bg-background px-3 py-2 text-foreground" />
      </label>
      <button className="rounded-lg border border-border px-4 py-2" type="submit">Filtrar</button>
      <button className="rounded-lg border border-border px-4 py-2" type="button" onClick={() => { setDraft(''); setDocumentStatus(''); setPage(1); setReport(undefined); setAttempt(attempt + 1); }}>Limpar</button>
      <p className="text-xs text-muted-foreground w-full">Deixe vazio para todos os status. Use o texto exibido na tabela; a comparação segue o banco.</p>
    </form>
    {loading ? <LoadingState /> : error ? <ReportState title={error} error><button className="text-primary underline mr-4" onClick={() => setAttempt(attempt + 1)}>Tentar novamente</button><Link to="/login" className="underline">Entrar novamente</Link></ReportState> : report && <>
      <div className="grid sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-5">
        <KpiCard label="Documentos ativos" value={report.totalRows} detail="Última sincronização de cada documento, projeto ativo" />
        <KpiCard label="Disciplinas" value={report.totalDisciplines} detail="Disciplinas distintas neste projeto" />
        <KpiCard label="Sem Document Status" value={report.documentsWithoutStatus} detail="Documentos com o campo vazio na LDP" />
        <KpiCard label="Physical Completed = 100%" value={report.documentsCompleted} detail="Leitura direta do percentual já publicado" />
      </div>
      <p className="text-sm text-muted-foreground mb-6">Os cartões consideram todo o projeto; o filtro afeta a tabela. Fonte: LDP unificada (última versão sincronizada de cada documento). Estes números não classificam atraso, aderência ou produtividade.</p>
      {report.totalRows === 0 ? <ReportState title="Sem documentos ativos disponíveis">Não há documentos LDP ativos para este projeto.</ReportState> : report.groups.length === 0 ? <ReportState title="Nenhum grupo nesta página">Ajuste o filtro ou volte à página anterior.</ReportState> : <div className="overflow-x-auto rounded-xl border border-border bg-card/50">
        <table className="w-full text-sm text-left"><caption className="text-left p-4 font-medium">Documentos · {report.filteredRows} registros em {report.totalGroups} grupos</caption>
          <thead className="bg-secondary text-muted-foreground"><tr>{['Document Status', 'Disciplina', 'Documentos'].map(h => <th key={h} scope="col" className="px-4 py-3 font-medium">{h}</th>)}</tr></thead>
          <tbody>{report.groups.map((group, index) => <tr key={index} className="border-t border-border hover:bg-secondary/50"><td className="px-4 py-4 whitespace-pre-wrap">{display(group.documentStatus)}</td><td className="px-4 py-4 whitespace-pre-wrap">{display(group.discipline)}</td><td className="px-4 py-4 tabular-nums">{group.count}</td></tr>)}</tbody>
        </table>
      </div>}
      {report.totalRows > 0 && <div className="flex justify-between items-center mt-5 gap-3"><button className="rounded-lg border border-border px-4 py-2 disabled:opacity-40" disabled={page === 1} onClick={() => { setReport(undefined); setPage(page - 1); }}>Anterior</button><p className="text-sm text-muted-foreground" aria-live="polite">Página {page} de {pages}</p><button className="rounded-lg border border-border px-4 py-2 disabled:opacity-40" disabled={page >= pages} onClick={() => { setReport(undefined); setPage(page + 1); }}>Próxima</button></div>}
    </>}
  </section>;
}
