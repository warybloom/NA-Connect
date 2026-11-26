import { cn } from "@/lib/utils";

interface Props {
  rows: string[][];
  headerRowIndex: number;
  maxPreviewRows?: number;
  selectedColumns?: Set<number>;
  onToggleColumn?: (colIndex: number) => void;
}

// Grade visual de preview — mostra cabeçalhos reais (não "A", "B", "C"), conforme pedido explícito
// de nunca exigir que o usuário pense em notação de coluna do Excel.
export const ExcelPreviewTable = ({ rows, headerRowIndex, maxPreviewRows = 5, selectedColumns, onToggleColumn }: Props) => {
  const headerRow = rows[headerRowIndex] ?? [];
  const dataRows = rows.slice(headerRowIndex + 1, headerRowIndex + 1 + maxPreviewRows);
  const colCount = Math.max(headerRow.length, ...dataRows.map((r) => r.length), 1);

  if (rows.length === 0) {
    return <p className="text-xs text-muted-foreground italic">Planilha vazia.</p>;
  }

  return (
    <div className="w-full overflow-x-auto rounded-xl border border-border/50">
      <table className="w-full text-left border-collapse text-xs">
        <thead>
          <tr className="bg-primary/10">
            {Array.from({ length: colCount }).map((_, c) => {
              const label = String(headerRow[c] ?? "").trim() || `Coluna ${c + 1}`;
              const clickable = !!onToggleColumn;
              const selected = selectedColumns?.has(c);
              return (
                <th
                  key={c}
                  onClick={() => onToggleColumn?.(c)}
                  className={cn(
                    "px-3 py-2 font-black uppercase text-[10px] tracking-wide border-b border-border/50 whitespace-nowrap",
                    clickable && "cursor-pointer select-none",
                    clickable && (selected ? "bg-primary/30 text-primary" : "hover:bg-primary/15")
                  )}
                  title={clickable ? "Clique para incluir/excluir esta coluna" : undefined}
                >
                  {clickable && (
                    <input type="checkbox" checked={!!selected} readOnly className="mr-1.5 accent-primary" />
                  )}
                  {label}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody className="divide-y divide-border/30">
          {dataRows.map((row, r) => (
            <tr key={r} className="hover:bg-card/40">
              {Array.from({ length: colCount }).map((_, c) => (
                <td key={c} className="px-3 py-1.5 text-muted-foreground truncate max-w-[220px]">
                  {String(row[c] ?? "")}
                </td>
              ))}
            </tr>
          ))}
          {dataRows.length === 0 && (
            <tr>
              <td colSpan={colCount} className="px-3 py-3 text-center italic text-muted-foreground">
                Sem linhas de dados após o cabeçalho escolhido.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};
