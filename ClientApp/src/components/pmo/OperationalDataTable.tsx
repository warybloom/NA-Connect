import { ReactNode } from "react";
import { ColumnDef, flexRender, getCoreRowModel, getSortedRowModel, SortingState, useReactTable } from "@tanstack/react-table";
import { useState } from "react";
import { ArrowDown, ArrowUp, ArrowUpDown, Download } from "lucide-react";

/**
 * Grade densa reutilizável para as abas de CONTROLE OPERACIONAL (Weekly, Cronograma, LDP, Desvios,
 * HOLD, CAP Interno/Externo) — mesmo tema visual escuro das 6 views executivas ForgeSync (rounded-xl
 * border-slate-700 bg-slate-800/50, cabeçalho sticky bg-slate-900), nunca o tema claro legado de
 * ElegantTable/ui/table.tsx. Usa @tanstack/react-table (já instalado, reaproveitado — nenhuma lib nova).
 * Responsabilidade única: renderizar linhas/colunas com sticky header, ordenação por coluna, scroll
 * horizontal, export CSV e paginação. Filtros ficam no componente de cada domínio (aqui só a grade).
 */
export function exportRowsToCsv<T extends object>(rows: T[], filename: string) {
  if (!rows || rows.length === 0) return;
  const columns = Object.keys(rows[0]);
  const header = columns.join(';');
  const body = rows.map(row => columns.map(c => {
    const val = (row as Record<string, unknown>)[c];
    return `"${val != null ? String(val).replace(/"/g, '""') : ''}"`;
  }).join(';')).join('\n');
  const csv = '﻿' + header + '\n' + body;
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = `${filename}_${new Date().toLocaleDateString('pt-BR').replace(/\//g, '-')}.csv`;
  link.click();
}

interface OperationalDataTableProps<T> {
  columns: ColumnDef<T, any>[];
  rows: T[];
  loading: boolean;
  error?: string;
  onRetry?: () => void;
  emptyLabel?: string;
  caption?: ReactNode;
  exportFilename?: string;
  page?: number;
  totalPages?: number;
  onPageChange?: (page: number) => void;
  height?: string;
}

export default function OperationalDataTable<T extends object>({
  columns, rows, loading, error, onRetry, emptyLabel = 'Nenhum registro encontrado.', caption,
  exportFilename, page, totalPages, onPageChange, height = 'h-[560px]',
}: OperationalDataTableProps<T>) {
  const [sorting, setSorting] = useState<SortingState>([]);
  const table = useReactTable({ data: rows, columns, state: { sorting }, onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(), getSortedRowModel: getSortedRowModel() });

  if (loading) return <div className="flex items-center justify-center h-64 text-slate-400">Carregando...</div>;
  if (error) return <div className="flex flex-col items-center justify-center h-64 gap-3 text-rose-400"><p>{error}</p>{onRetry && <button className="text-sky-400 underline" onClick={onRetry}>Tentar novamente</button>}</div>;

  return (
    <div className={`rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 ${height}`}>
      {(caption || exportFilename) && (
        <div className="p-3 border-b border-slate-700 flex items-center justify-between gap-3">
          <div className="text-sm text-slate-300">{caption}</div>
          {exportFilename && rows.length > 0 && (
            <button onClick={() => exportRowsToCsv(rows, exportFilename)} className="flex items-center gap-1.5 bg-slate-700 hover:bg-slate-600 text-foreground text-xs font-bold px-3 py-1.5 rounded transition-colors shrink-0">
              <Download className="w-3.5 h-3.5" /> Exportar CSV
            </button>
          )}
        </div>
      )}
      <div className="overflow-auto custom-scrollbar flex-1">
        {rows.length === 0 ? (
          <div className="flex items-center justify-center h-full text-slate-500 p-6 text-center">{emptyLabel}</div>
        ) : (
          <table className="w-full text-xs text-left whitespace-nowrap">
            <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
              {table.getHeaderGroups().map(headerGroup => (
                <tr key={headerGroup.id}>
                  {headerGroup.headers.map(header => (
                    <th key={header.id} colSpan={header.colSpan} className="px-3 py-2 font-medium">
                      {header.isPlaceholder ? null : (
                        <button className="flex items-center gap-1 hover:text-slate-200" onClick={header.column.getToggleSortingHandler()} disabled={!header.column.getCanSort()}>
                          {flexRender(header.column.columnDef.header, header.getContext())}
                          {header.column.getCanSort() && (
                            header.column.getIsSorted() === 'asc' ? <ArrowUp className="w-3 h-3" /> :
                            header.column.getIsSorted() === 'desc' ? <ArrowDown className="w-3 h-3" /> :
                            <ArrowUpDown className="w-3 h-3 opacity-40" />
                          )}
                        </button>
                      )}
                    </th>
                  ))}
                </tr>
              ))}
            </thead>
            <tbody className="divide-y divide-slate-700/50">
              {table.getRowModel().rows.map(row => (
                <tr key={row.id} className="hover:bg-slate-700/30 transition-colors">
                  {row.getVisibleCells().map(cell => (
                    <td key={cell.id} className="px-3 py-2">{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {page != null && totalPages != null && onPageChange && (
        <div className="flex justify-between items-center p-3 border-t border-slate-700 gap-3">
          <button className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-300 disabled:opacity-40" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>Anterior</button>
          <p className="text-xs text-slate-400" aria-live="polite">Página {page} de {Math.max(1, totalPages)}</p>
          <button className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-300 disabled:opacity-40" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>Próxima</button>
        </div>
      )}
    </div>
  );
}
