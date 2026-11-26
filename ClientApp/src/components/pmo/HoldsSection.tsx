import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { PauseCircle } from "lucide-react";
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface HoldRow {
  idRegistro: number; item: string | null; numeroHold: string | null; numeroNaDocumento: string | null;
  disciplina: string | null; tituloDocumento: string | null; revisao: string | null; motivoHold: string | null;
  status: string | null; responsavel: string | null;
}
interface HoldReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: HoldRow[] }
const display = (v: string | null) => v ?? '—';

/**
 * CONTROLE OPERACIONAL — HOLD. Fonte: dbo.vw_Latest_HoldControl via GET /api/reports/holds. Sem colunas
 * de data de abertura/prazo/fechamento na fonte real — por isso não há cartão de "Vencidos"/aging aqui
 * (calcular isso seria inventar uma data que a fonte não tem).
 */
export default function HoldsSection({ selection }: { selection: PmoSelecionado[] }) {
  const [status, setStatus] = useState('');
  const [disciplina, setDisciplina] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<HoldRow>({ endpoint: '/api/reports/holds', projects: selection, params: { status, disciplina, search }, page, pageSize: 25 });
  const multi = selection.length > 1;

  const baseColumns: ColumnDef<MergedRow<HoldRow>, any>[] = [
    { accessorKey: 'numeroHold', header: 'Nº Hold', cell: ({ getValue }) => <span className="font-black text-sky-400">{display(getValue())}</span> },
    { accessorKey: 'numeroNaDocumento', header: 'Documento', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'tituloDocumento', header: 'Título', cell: ({ getValue }) => <span className="max-w-[240px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'revisao', header: 'Rev.', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'disciplina', header: 'Disciplina', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'motivoHold', header: 'Motivo', cell: ({ getValue }) => <span className="max-w-[280px] truncate block italic" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'status', header: 'Status', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-sky-400 text-[10px]">{display(getValue())}</span> },
    { accessorKey: 'responsavel', header: 'Responsável', cell: ({ getValue }) => display(getValue()) },
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label="HOLD" className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Status (texto exato)
        <input aria-label="Status do HOLD" value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} placeholder="Ex.: ABERTO" className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Disciplina (texto exato)
        <input aria-label="Disciplina do HOLD" value={disciplina} onChange={e => { setDisciplina(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (Nº Hold, documento, título, motivo)
        <input aria-label="Buscar HOLD" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setStatus(''); setDisciplina(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
      <p className="text-[10px] text-slate-500 w-full">Status/Disciplina usam o texto exato como armazenado no banco.</p>
    </div>

    {!m.loading && !m.error && <div className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
        <div className="text-[10px] uppercase text-sky-400 font-bold mb-1 flex items-center gap-1"><PauseCircle className="w-3 h-3" /> Total de HOLDs</div>
        <div className="text-2xl font-black">{m.totalRows}</div>
      </div>
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-amber-500">
        <div className="text-[10px] uppercase text-amber-400 font-bold mb-1">No filtro atual</div>
        <div className="text-2xl font-black">{m.filteredRows}</div>
      </div>
    </div>}

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename="HOLD" emptyLabel="Nenhum HOLD encontrado para o(s) projeto(s) selecionado(s)."
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} HOLDs`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} />
  </section>;
}
