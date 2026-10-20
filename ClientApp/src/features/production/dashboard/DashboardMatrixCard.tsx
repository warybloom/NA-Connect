// Matriz compacta genérica (linhas × colunas de contagens clicáveis). Hoje: LDP Tipo 2 → "Status por fase" 30/60/90.
// Só é montada quando o backend envia a matriz (Tipo 1 não recebe nenhuma). Clique = cross-filter da célula.
import { cn } from "@/lib/utils";
import { formatarValor } from "./dashboardTheme";
import type { DashboardMatrix, DashboardMatrixCell } from "./types";

/** Filtro aplicado ao clicar na célula (o mesmo critério usado na contagem). */
export const filtroDaCelula = (c: DashboardMatrixCell): [string, string] => [c.filterDimension, c.filterValue];

export default function DashboardMatrixCard({ matrix, onToggle }: { matrix: DashboardMatrix; onToggle: (d: string, v: string) => void }) {
  const celula = (row: string, column: string) => matrix.cells.find((c) => c.row === row && c.column === column);
  return (
    <article className="rounded-xl border border-border bg-card/70 p-3" data-matrix={matrix.key}>
      <h3 className="mb-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">{matrix.title}</h3>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-[11px] uppercase tracking-wider text-muted-foreground">
            <th className="py-1 text-left font-medium" />
            {matrix.columns.map((c) => <th key={c.key} scope="col" className="w-24 py-1 text-right font-bold">{c.label}</th>)}
          </tr>
        </thead>
        <tbody>
          {matrix.rows.map((r) => (
            <tr key={r.key} className="border-t border-border/60">
              <th scope="row" className="py-1.5 pr-3 text-left text-xs font-semibold text-foreground">{r.label}</th>
              {matrix.columns.map((col) => {
                const c = celula(r.key, col.key);
                if (!c) return <td key={col.key} className="text-right text-muted-foreground">—</td>;
                return (
                  <td key={col.key} className="py-0.5 text-right">
                    <button type="button" onClick={() => onToggle(...filtroDaCelula(c))}
                      data-filter={`${c.filterDimension}=${c.filterValue}`}
                      title={c.selected ? "Remover filtro" : `Filtrar: ${r.label} em ${col.label}`}
                      className={cn("min-w-[3.5rem] rounded-md px-2 py-1 text-base font-black tabular-nums transition-colors hover:bg-primary/10",
                        c.selected && "bg-primary/15 text-primary ring-1 ring-primary/50", c.value === 0 && !c.selected && "text-muted-foreground/60")}>
                      {formatarValor(c.value, "numero")}
                    </button>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </article>
  );
}
