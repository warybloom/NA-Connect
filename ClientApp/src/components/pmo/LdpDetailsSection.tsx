import { useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { usePmoReports, docKey, formatHH, formatCurrency } from './pmoData';
import { enrichDocuments, type FactsReport } from './pmoFacts';
import OperationalDataTable from "./OperationalDataTable";
import { useMergedPages, comColunaProjeto, type MergedRow } from "./useMergedPages";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";

interface LdpGateInfo {
  gate: number; gateDescricao: string | null; gateAtivo: boolean; fase: string | null; documentStatus: string | null;
  situation: string | null; plannedBaseline: string | null; replannedDate: string | null;
  plannedPhysical: number | null; physicalCompleted: number | null;
}
interface LdpDetailRow {
  idRegistro: number; naDocumentNumber: string | null; clientDocumentNumber: string | null; documentTitle: string | null;
  discipline: string | null; documentStatus: string | null; statusInFlow: string | null; situation: string | null;
  wbsPercent: number | null; percentPlannedPhysical: number | null; percentPhysicalCompleted: number | null;
  plannedDateBaseline: string | null; replannedDate: string | null; emissionDate: string | null; commentStatus: string | null;
  plannedHh: number | null; actualHh: number | null; plannedCost: number | null; actualCost: number | null;
  gates: LdpGateInfo[];
}
interface LdpDetailsReport { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: LdpDetailRow[] }
const display = (v: string | number | null | undefined) => v ?? '—';
const pct = (v: number | null | undefined) => v == null ? '—' : `${v}%`;
const dateOnly = (v: string | null | undefined) => v ? new Date(v).toLocaleDateString('pt-BR') : '—';
const gateOf = (row: LdpDetailRow, gate: number) => row.gates.find(g => g.gate === gate);

/**
 * CONTROLE OPERACIONAL — LDP (visão "Excel", somente leitura). Base: dbo.VW_Ldp_Unificada (mesma fonte
 * de Produção/Documentos e Mapa de Calor). Gates 30/60/90: dbo.VW_Ldp_Documento_Gates_Ultima_Versao
 * (mesma view do matching real de Cronograma->LDP), agrupados por documento com cabeçalho de coluna
 * agrupado (30%/60%/90%) via TanStack Table. Nenhuma alteração no hotfix aprovado.
 */
export default function LdpDetailsSection({ selection }: { selection: PmoSelecionado[] }) {
  const [discipline, setDiscipline] = useState('');
  const [documentStatus, setDocumentStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const m = useMergedPages<LdpDetailRow>({ endpoint: '/api/reports/ldp', projects: selection, params: { discipline, documentStatus, search }, page, pageSize: 50 });
  const multi = selection.length > 1;

  // Colunas canônicas: mesmo payload e mesmas regras da Gestão à Vista (enrichDocuments), unidas por N&A Document Number.
  const { reports: canonReports } = usePmoReports('gestao-vista', { hub: selection[0].hubId, project: selection[0].id, selection });
  const canon = new Map(enrichDocuments(canonReports as FactsReport[]).map(d => [docKey(d.projectId, d.codigo), d] as const));
  const canonOf = (row: MergedRow<LdpDetailRow>) => canon.get(docKey(row.__projectId, row.naDocumentNumber));
  const ROTULO_STATUS: Record<string, string> = { EMITIDO: 'Emitido', NO_PRAZO: 'No prazo', ATRASADO: 'Atrasado', SEM_PRAZO: 'Sem prazo', CANCELADO: 'Cancelado' };

  const gateColumns = (gate: number): ColumnDef<MergedRow<LdpDetailRow>, any>[] => [
    { id: `gate${gate}-fase`, header: 'Fase', cell: ({ row }) => display(gateOf(row.original, gate)?.fase) },
    { id: `gate${gate}-baseline`, header: 'Baseline', cell: ({ row }) => dateOnly(gateOf(row.original, gate)?.plannedBaseline) },
    { id: `gate${gate}-replanned`, header: 'Reprogramado', cell: ({ row }) => dateOnly(gateOf(row.original, gate)?.replannedDate) },
    { id: `gate${gate}-planned`, header: 'Planned Physical', cell: ({ row }) => pct(gateOf(row.original, gate)?.plannedPhysical) },
    { id: `gate${gate}-completed`, header: 'Physical Completed', cell: ({ row }) => <span className="text-sky-400 font-bold">{pct(gateOf(row.original, gate)?.physicalCompleted)}</span> },
  ];

  const baseColumns: ColumnDef<MergedRow<LdpDetailRow>, any>[] = [
    { accessorKey: 'naDocumentNumber', header: 'N&A Document Number', cell: ({ getValue }) => <span className="font-bold text-slate-200">{display(getValue())}</span> },
    { accessorKey: 'clientDocumentNumber', header: 'Client Document Number', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'documentTitle', header: 'Document Title', cell: ({ getValue }) => <span className="max-w-[220px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'discipline', header: 'Discipline', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'documentStatus', header: 'Document Status', cell: ({ getValue }) => <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-[10px]">{display(getValue())}</span> },
    { accessorKey: 'statusInFlow', header: 'Status In Flow', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'situation', header: 'Situation', cell: ({ getValue }) => display(getValue()) },
    { accessorKey: 'wbsPercent', header: 'WBS (%)', cell: ({ getValue }) => pct(getValue()) },
    { accessorKey: 'percentPlannedPhysical', header: 'Planned Physical', cell: ({ getValue }) => pct(getValue()) },
    { accessorKey: 'percentPhysicalCompleted', header: 'Physical Completed', cell: ({ getValue }) => <span className="text-sky-400 font-bold">{pct(getValue())}</span> },
    { accessorKey: 'plannedDateBaseline', header: 'Baseline', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'replannedDate', header: 'Replanned', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'emissionDate', header: 'Emission', cell: ({ getValue }) => dateOnly(getValue()) },
    { accessorKey: 'commentStatus', header: 'Comentários', cell: ({ getValue }) => <span className="max-w-[160px] truncate block" title={getValue() ?? undefined}>{display(getValue())}</span> },
    { accessorKey: 'plannedHh', header: 'HH Prev. (Planned_HH)', cell: ({ getValue }) => display(getValue()) },
    // 30/09/2026: HH/custo realizados BRUTOS da LDP removidos da grade — o HH realizado oficial está na coluna canônica abaixo.
    { accessorKey: 'plannedCost', header: 'Custo Prev. (Planned_Cost)', cell: ({ getValue }) => display(getValue()) },
    { id: 'canon-status', header: 'Status (Gestão)', cell: ({ row }) => { const d = canonOf(row.original); return d ? <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-[10px]" data-status={d.statusDocumento}>{ROTULO_STATUS[d.statusDocumento]}</span> : '—'; } },
    { id: 'canon-avanco', header: 'Avanço Físico (Gestão)', cell: ({ row }) => { const d = canonOf(row.original); return d?.avancoFisico == null ? '—' : <span className="text-sky-400 font-bold">{formatHH(d.avancoFisico)}%</span>; } },
    { id: 'canon-hhreal', header: 'HH Real.', cell: ({ row }) => formatHH(canonOf(row.original)?.hhRealizado ?? null) },
    { id: 'canon-custoreal', header: 'Custo Real. (Timesheet)', cell: ({ row }) => formatCurrency(canonOf(row.original)?.custoRealizado ?? null) },
    { id: 'gate30', header: '30%', columns: gateColumns(30) } as ColumnDef<MergedRow<LdpDetailRow>, any>,
    { id: 'gate60', header: '60%', columns: gateColumns(60) } as ColumnDef<MergedRow<LdpDetailRow>, any>,
    { id: 'gate90', header: '90%', columns: gateColumns(90) } as ColumnDef<MergedRow<LdpDetailRow>, any>,
  ];
  const columns = comColunaProjeto(baseColumns, multi);

  return <section aria-label="LDP" className="space-y-6 animate-enter">
    <div className="flex flex-wrap items-end gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
      <label className="text-xs text-slate-400">Discipline
        <input aria-label="Discipline LDP" value={discipline} onChange={e => { setDiscipline(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400">Document Status
        <input aria-label="Document Status LDP" value={documentStatus} onChange={e => { setDocumentStatus(e.target.value); setPage(1); }} className="mt-1 block bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <label className="text-xs text-slate-400 flex-1 min-w-[200px]">Buscar (N&A/Client Document Number, título)
        <input aria-label="Buscar LDP" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} className="mt-1 block w-full bg-slate-900 border border-slate-700 rounded-md px-2 py-1.5 text-slate-200 text-sm" />
      </label>
      <button onClick={() => { setDiscipline(''); setDocumentStatus(''); setSearch(''); setPage(1); }} className="text-xs text-slate-400 hover:text-foreground underline">Limpar filtros</button>
    </div>

    <OperationalDataTable columns={columns} rows={m.rows} loading={m.loading} error={m.error}
      onRetry={m.retry} exportFilename="LDP" emptyLabel="Nenhum documento LDP encontrado para o(s) projeto(s) selecionado(s)."
      caption={<>{!m.loading && `${m.filteredRows} de ${m.totalRows} documentos`}<ConsolidadoBanner selection={selection} falhas={m.falhas} /></>}
      page={page} totalPages={m.totalPages} onPageChange={setPage} height="h-[660px]" />
  </section>;
}
