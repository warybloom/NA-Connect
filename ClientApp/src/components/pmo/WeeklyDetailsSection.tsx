import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { CalendarDays } from "lucide-react";
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface WeeklyDetailRow {
  idRegistro: number; atividade: string | null; disciplina: string | null; tarefa: string | null;
  percentPrevistoSemana: string | null; percentRealizado: string | null; recurso: string | null; fase: string | null;
  status: string | null; baseline: string | null; reprogramado: string | null; preparacao: string | null;
  verificacao: string | null; aprovacao: string | null; hhPrevisto: number | null; hhVerificacao: number | null;
  hhAprovacao: number | null; liberadoExecucao: string | null; observacao: string | null; tipoRegistro: string | null;
  statusImportacao: string | null; diagnosticoImportacao: string | null;
}
interface WeeklyDetailsReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: WeeklyDetailRow[] }
const display = (v: string | number | null) => v ?? '—';
const dateOnly = (v: string | null) => v ? new Date(v).toLocaleDateString('pt-BR') : '—';

/**
 * CONTROLE OPERACIONAL — Programação Semanal (Weekly), visão detalhada. Fonte: MESMA tabela
 * dbo.tb_WeeklyEngineeringSchedule já usada por /api/reports/weekly (mantido intacto) — este é o
 * endpoint adicional (/api/reports/weekly/details) com os campos ricos de engenharia semanal.
 */
export default function WeeklyDetailsSection({ selection }: { selection: PmoSelecionado[] }) {
  const [disciplina, setDisciplina] = useState('');
  const [status, setStatus] = useState('');
  const [responsavel, setResponsavel] = useState('');
  const [fase, setFase] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<WeeklyDetailRow>({ endpoint: '/api/reports/weekly/details', projects: selection, params: { disciplina, status, responsavel, fase, search }, page, pageSize: 25 });
  const multi = selection.length > 1;

  const baseColumns: ColumnDef<MergedRow<WeeklyDetailRow>, any>[] = [
    { accessorKey: 'atividade', header: 'Atividade', cell: ({ getValue }) => <span className="font-bold text-slate-200">{display(getValue())}</span> },
    { accessorKey: 'tarefa', header: 'Documento/Tarefa', cell: ({ getValue }) => <span className="max-w-[220px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'disciplina', header: 'Disciplina', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'recurso', header: 'Responsável', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'fase', header: 'Fase', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'percentPrevistoSemana', header: '% Previsto', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'percentRealizado', header: '% Realizado', cell: ({ getValue }) => <span className="text-sky-400 font-bold">{display(getValue())}</span> },
    { accessorKey: 'status', header: 'Status', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-sky-400 text-[10px]">{display(getValue())}</span> },
    { accessorKey: 'baseline', header: 'Baseline', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'reprogramado', header: 'Reprogramado', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'preparacao', header: 'Preparação', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'verificacao', header: 'Verificação', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'aprovacao', header: 'Aprovação', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'hhPrevisto', header: 'HH Prev.', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'hhVerificacao', header: 'HH Verif.', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'hhAprovacao', header: 'HH Aprov.', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'liberadoExecucao', header: 'Liberado', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'observacao', header: 'Observação', cell: ({ getValue }) => <span className="max-w-[200px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'diagnosticoImportacao', header: 'Diagnóstico Importação', cell: ({ getValue }) => <span className="max-w-[220px] truncate block text-slate-500" title={getValue() ?? undefined}>{display(getValue())}</span> },
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label="Programação Semanal" className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Disciplina
        <input aria-label="Disciplina Weekly" value={disciplina} onChange={e => { setDisciplina(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Status
        <input aria-label="Status Weekly" value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Responsável
        <input aria-label="Responsável Weekly" value={responsavel} onChange={e => { setResponsavel(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Fase
        <input aria-label="Fase Weekly" value={fase} onChange={e => { setFase(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (atividade/documento)
        <input aria-label="Buscar Weekly" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setDisciplina(''); setStatus(''); setResponsavel(''); setFase(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
      <p className="text-[10px] text-slate-500 w-full flex items-center gap-1"><CalendarDays className="w-3 h-3" /> Filtros de texto usam correspondência exata como armazenada no banco (exceto a busca).</p>
    </div>

    {!m.loading && !m.error && <div className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
        <div className="text-[10px] uppercase text-sky-400 font-bold mb-1">Total de linhas</div>
        <div className="text-2xl font-black">{m.totalRows}</div>
      </div>
      <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-amber-500">
        <div className="text-[10px] uppercase text-amber-400 font-bold mb-1">No filtro atual</div>
        <div className="text-2xl font-black">{m.filteredRows}</div>
      </div>
    </div>}

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename="Programacao_Semanal" emptyLabel="Nenhuma linha de Weekly encontrada para o(s) projeto(s) selecionado(s)."
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} linhas`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} height="h-[620px]" />
  </section>;
}
