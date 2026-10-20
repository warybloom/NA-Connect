import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { FileWarning } from "lucide-react";
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface ScopeDeviationRow {
  idRegistro: number; item: string | null; descricao: string | null; disciplina: string | null;
  numeroDesvio: string | null; data: string | null; quantidadeHH: string | null; status: string | null;
  desvioInterno: string | null; changeOrder: string | null;
}
interface ScopeDeviationReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: ScopeDeviationRow[] }
const display = (v: string | null) => v ?? '—';

/**
 * CONTROLE OPERACIONAL — Desvios de Escopo / Pleitos. Fonte: dbo.vw_Latest_ScopeDeviation via
 * GET /api/reports/scope-deviations. Sem coluna de custo/valor, causa, impacto ou responsável na fonte
 * real — não mostrados (nada inferido). DATA/QUANT_HH são texto como armazenados.
 */
export default function ScopeDeviationsSection({ selection }: { selection: PmoSelecionado[] }) {
  const [status, setStatus] = useState('');
  const [disciplina, setDisciplina] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<ScopeDeviationRow>({ endpoint: '/api/reports/scope-deviations', projects: selection, params: { status, disciplina, search }, page, pageSize: 25 });
  const multi = selection.length > 1;

  const baseColumns: ColumnDef<MergedRow<ScopeDeviationRow>, any>[] = [
    { accessorKey: 'numeroDesvio', header: 'Nº Desvio', cell: ({ getValue }) => <span className="font-black text-amber-500">{display(getValue())}</span> },
    { accessorKey: 'descricao', header: 'Descrição', cell: ({ getValue }) => <span className="max-w-[280px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'disciplina', header: 'Disciplina', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'data', header: 'Data', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'quantidadeHH', header: 'HH', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'desvioInterno', header: 'Tipo', cell: ({ getValue }) => <span className="bg-slate-950 px-2 py-1 border border-slate-700 rounded text-slate-300">{display(getValue())}</span> },
    { accessorKey: 'changeOrder', header: 'Change Order', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'status', header: 'Status', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-amber-400 text-[10px]">{display(getValue())}</span> },
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label="Desvios de Escopo" className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Status (texto exato)
        <input aria-label="Status do Desvio" value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Disciplina (texto exato)
        <input aria-label="Disciplina do Desvio" value={disciplina} onChange={e => { setDisciplina(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (nº do desvio, descrição)
        <input aria-label="Buscar Desvio de Escopo" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setStatus(''); setDisciplina(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
    </div>

    {!m.loading && !m.error && <div className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-amber-500">
        <div className="text-[10px] uppercase text-amber-400 font-bold mb-1 flex items-center gap-1"><FileWarning className="w-3 h-3" /> Total de Desvios</div>
        <div className="text-2xl font-black">{m.totalRows}</div>
      </div>
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
        <div className="text-[10px] uppercase text-sky-400 font-bold mb-1">No filtro atual</div>
        <div className="text-2xl font-black">{m.filteredRows}</div>
      </div>
    </div>}

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename="Desvios_Escopo" emptyLabel="Nenhum desvio de escopo encontrado para o(s) projeto(s) selecionado(s)."
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} desvios`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} />
  </section>;
}
