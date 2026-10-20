import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { ShieldAlert } from "lucide-react";
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface ActionLogRow {
  idRegistro: number; id: string | null; date: string | null; name: string | null; company: string | null;
  cycle: string | null; frontOfServices: string | null; afe: string | null; description: string | null;
  name2: string | null; company3: string | null; schedule: string | null; reschedule: string | null;
  concl: string | null; critical: string | null; status: string | null; comments: string | null;
}
interface ActionLogReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: ActionLogRow[] }
const display = (v: string | null) => v ?? '—';

/**
 * CONTROLE OPERACIONAL — CAP Interno / CAP Externo. Mesmo formato de coluna nas duas fontes reais
 * (dbo.vw_Latest_InternalAction / dbo.vw_Latest_ExternalAction), mas endpoints e dados SEPARADOS —
 * nenhuma mistura, só a UI é compartilhada (parametrizada por endpoint/título). Sem coluna de disciplina
 * nesta fonte.
 */
export default function CapActionSection({ selection, endpoint, title }: { selection: PmoSelecionado[]; endpoint: string; title: string }) {
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<ActionLogRow>({ endpoint: endpoint, projects: selection, params: { status, search }, page, pageSize: 25 });
  const multi = selection.length > 1;

  const baseColumns: ColumnDef<MergedRow<ActionLogRow>, any>[] = [
    { accessorKey: 'id', header: 'ID', cell: ({ getValue }) => <span className="font-black text-emerald-500">{display(getValue())}</span> },
    { accessorKey: 'date', header: 'Data', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'name', header: 'Nome', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'company', header: 'Empresa', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'description', header: 'Descrição', cell: ({ getValue }) => <span className="max-w-[280px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'schedule', header: 'Programado', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'reschedule', header: 'Reprogramado', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'concl', header: 'Conclusão', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'critical', header: 'Crítico', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'status', header: 'Status', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-emerald-400 text-[10px]">{display(getValue())}</span> },
    { accessorKey: 'comments', header: 'Comentários', cell: ({ getValue }) => <span className="max-w-[240px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label={title} className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Status (texto exato)
        <input aria-label={`Status de ${title}`} value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (nome, empresa, descrição, comentários)
        <input aria-label={`Buscar em ${title}`} value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setStatus(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
    </div>

    {!m.loading && !m.error && <div className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-emerald-500">
        <div className="text-[10px] uppercase text-emerald-400 font-bold mb-1 flex items-center gap-1"><ShieldAlert className="w-3 h-3" /> Total — {title}</div>
        <div className="text-2xl font-black">{m.totalRows}</div>
      </div>
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
        <div className="text-[10px] uppercase text-sky-400 font-bold mb-1">No filtro atual</div>
        <div className="text-2xl font-black">{m.filteredRows}</div>
      </div>
    </div>}

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename={title.replace(/\s+/g, '_')} emptyLabel={`Nenhum registro de ${title} para o(s) projeto(s) selecionado(s).`}
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} registros`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} />
  </section>;
}
