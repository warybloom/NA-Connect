import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface ScheduleRow {
  idCronograma: number; naDocumentNumber: string | null; taskName: string | null; edtWbs: string | null;
  disciplina: string | null; fase: string | null; gatePercentual: number | null; terminoLinhaBase: string | null;
  termino: string | null; percentPrevistoReprogramado: number | null; percentRealizado: number | null;
  trabalho: number | null; wbsPercentPorWork: number | null; statusValidacao: string | null;
  statusDocumentoEngenharia: string | null; crOs: string | null;
}
interface ScheduleReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: ScheduleRow[] }
const display = (v: string | number | null) => v ?? '—';
const pct = (v: number | null) => v == null ? '—' : `${v}%`;
const dateOnly = (v: string | null) => v ? new Date(v).toLocaleDateString('pt-BR') : '—';

/**
 * CONTROLE OPERACIONAL — Cronograma (visão "Excel", somente leitura). Fonte:
 * dbo.VW_Cronograma_Tipo2_Ultima_Versao (Tipo_LDP=2) via GET /api/reports/schedule — MESMA view do
 * matching real (CronogramaTipo2LdpSyncRepository), nunca reescrita/recalculada aqui. WBS calculado é
 * lido do snapshot já persistido (CronogramaTipo2Work), nunca recalculado no frontend. Hotfix de
 * Cronograma/LDP (parser, sync, WBS, datas, Physical Completed) permanece intocado.
 */
export default function ScheduleSection({ selection }: { selection: PmoSelecionado[] }) {
  const [disciplina, setDisciplina] = useState('');
  const [fase, setFase] = useState('');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<ScheduleRow>({ endpoint: '/api/reports/schedule', projects: selection, params: { disciplina, fase, status, search }, page, pageSize: 50 });
  const multi = selection.length > 1;

  const baseColumns: ColumnDef<MergedRow<ScheduleRow>, any>[] = [
    { accessorKey: 'naDocumentNumber', header: 'N&A Document Number', cell: ({ getValue }) => <span className="font-bold text-slate-200">{display(getValue())}</span> },
    { accessorKey: 'taskName', header: 'Tarefa/Documento', cell: ({ getValue }) => <span className="max-w-[240px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'edtWbs', header: 'EDT/WBS', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'disciplina', header: 'Disciplina', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'fase', header: 'Fase', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'gatePercentual', header: 'Gate', cell: ({ getValue }) => pct(getValue()) },
    { accessorKey: 'terminoLinhaBase', header: 'Baseline', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'termino', header: 'Reprogramado', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'percentPrevistoReprogramado', header: '% Previsto Reprog.', cell: ({ getValue }) => pct(getValue()) },
    { accessorKey: 'percentRealizado', header: '% Realizado', cell: ({ getValue }) => <span className="text-sky-400 font-bold">{pct(getValue())}</span> },
    { accessorKey: 'trabalho', header: 'Work (h)', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'wbsPercentPorWork', header: 'WBS (%) calculado', cell: ({ getValue }) => pct(getValue()) },
    { accessorKey: 'statusValidacao', header: 'Status Validação', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-[10px]">{display(getValue())}</span> },
    { accessorKey: 'statusDocumentoEngenharia', header: 'Status Documento Engenharia', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'crOs', header: 'CR/OS', cell: ({ getValue }) => display(getValue()) },
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label="Cronograma" className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Disciplina
        <input aria-label="Disciplina Cronograma" value={disciplina} onChange={e => { setDisciplina(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Fase
        <input aria-label="Fase Cronograma" value={fase} onChange={e => { setFase(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Status Validação
        <input aria-label="Status Cronograma" value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (documento/tarefa)
        <input aria-label="Buscar Cronograma" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setDisciplina(''); setFase(''); setStatus(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
    </div>

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename="Cronograma" emptyLabel="Nenhuma linha de cronograma encontrada para o(s) projeto(s) selecionado(s)."
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} linhas`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} height="h-[640px]" />
  </section>;
}
