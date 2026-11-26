// Renderer ÚNICO das 12 dashboards do Relatório de Produção: [filtros ativos] [KPIs] [gráficos] [tabela resumida].
// O conteúdo (KPIs, gráficos, dimensões, colunas) vem do backend; aqui só há apresentação + interação.
import { useEffect, useRef, useState } from "react";
import { FilterX, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { LoadingState, ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import DashboardChartCard from "./DashboardChartCard";
import DashboardMatrixCard from "./DashboardMatrixCard";
import { formatarCelula, formatarValor } from "./dashboardTheme";
import { useDashboardFilters } from "./useDashboardFilters";
import { lerFiltrosDaUrl } from "./filtrosLogic";
import { useProductionDashboard } from "./useProductionDashboard";
import type { DashboardKpi, DashboardResult, GateCelula } from "./types";

const PAGE_SIZE = 12;

function Kpi({ kpi, onToggle }: { kpi: DashboardKpi; onToggle: (d: string, v: string) => void }) {
  const clicavel = !!kpi.filterDimension && !!kpi.filterValue;
  const conteudo = (
    <>
      <p className="truncate text-[10px] font-bold uppercase tracking-wider text-muted-foreground">{kpi.label}</p>
      <p className="my-1 text-2xl font-black tabular-nums">{formatarValor(kpi.value, kpi.format)}</p>
      <p className="line-clamp-2 text-[10px] text-muted-foreground" title={kpi.detail}>{kpi.detail}</p>
    </>
  );
  const base = "rounded-xl border bg-card/70 p-3 text-left min-w-0";
  if (!clicavel) return <article className={cn(base, "border-border")}>{conteudo}</article>;
  return (
    <button type="button" onClick={() => onToggle(kpi.filterDimension!, kpi.filterValue!)}
      title={kpi.selected ? "Remover filtro" : "Filtrar por este indicador"}
      className={cn(base, "transition-colors hover:border-primary/60", kpi.selected ? "border-primary bg-primary/10" : "border-border")}>
      {conteudo}
    </button>
  );
}

/** Célula de gate (LDP Tipo 2): rótulo curto; detalhe (Document Status / Situation do gate) no tooltip. */
function CelulaGate({ valor }: { valor: unknown }) {
  const g = valor as GateCelula | null;
  if (!g || typeof g !== "object") return <>—</>;
  if (!g.ativo) return <span className="text-muted-foreground" title="Gate inativo para este documento">N/A</span>;
  return (
    <span title={`Document Status: ${g.documentStatus ?? "—"} · Situation: ${g.situation ?? "—"}`}
      className={cn(g.classe === "CANCELADO" && "text-slate-400", g.classe === "EMITIDO" && "text-emerald-500")}>
      {g.rotulo}
    </span>
  );
}

function Tabela({ data, page, onPage, onToggle }: { data: DashboardResult; page: number; onPage: (p: number) => void; onToggle: (d: string, v: string) => void }) {
  const t = data.table;
  const dimLinha = t.rowFilterDimension;
  const paginas = Math.max(1, Math.ceil(t.total / t.pageSize));
  return (
    <article className="rounded-xl border border-border bg-card/70">
      <div className="flex items-center justify-between gap-3 px-4 py-2 text-xs text-muted-foreground">
        <span>{t.total} registro(s) no filtro atual{dimLinha ? " · clique numa linha para filtrar a tela por ela" : ""}</span>
        {paginas > 1 && (
          <span className="flex items-center gap-2">
            <button className="rounded border border-border px-2 py-0.5 disabled:opacity-40" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹</button>
            {page} / {paginas}
            <button className="rounded border border-border px-2 py-0.5 disabled:opacity-40" disabled={page >= paginas} onClick={() => onPage(page + 1)}>›</button>
          </span>
        )}
      </div>
      <div className="max-h-72 overflow-auto custom-scrollbar">
        <table className="w-full text-xs text-left">
          <thead className="sticky top-0 bg-secondary text-muted-foreground">
            <tr>{t.columns.map(c => <th key={c.key} scope="col" className="px-3 py-2 font-medium whitespace-nowrap">{c.label}</th>)}</tr>
          </thead>
          <tbody>
            {t.rows.length === 0
              ? <tr><td colSpan={t.columns.length} className="px-3 py-6 text-center text-muted-foreground">Nenhum registro para os filtros atuais.</td></tr>
              : t.rows.map((r, i) => {
                const chave = typeof r.__rowKey === "string" ? r.__rowKey : null;
                const selecionada = r.__selected === true;
                return (
                <tr key={i} onClick={dimLinha && chave ? () => onToggle(dimLinha, chave) : undefined}
                  title={dimLinha && chave ? (selecionada ? "Remover filtro deste documento" : "Filtrar a tela por este documento") : undefined}
                  className={cn("border-t border-border", dimLinha && chave && "cursor-pointer hover:bg-accent/40",
                    selecionada && "bg-primary/15 shadow-[inset_3px_0_0_0_hsl(var(--primary))]")}>
                  {t.columns.map(c => c.type === "gate" ? (
                    <td key={c.key} className="whitespace-nowrap px-3 py-1.5" data-col={c.key}><CelulaGate valor={r[c.key]} /></td>
                  ) : (
                    <td key={c.key} className={cn("px-3 py-1.5", c.type === "texto" ? "max-w-[280px] truncate" : "whitespace-nowrap tabular-nums")}
                      title={c.type === "texto" ? formatarCelula(r[c.key], c.type) : undefined}>
                      {formatarCelula(r[c.key], c.type)}
                    </td>
                  ))}
                </tr>
                );
              })}
          </tbody>
        </table>
      </div>
    </article>
  );
}

export default function ProductionDashboard({ dashboardId, selection }: { dashboardId: string; selection: PmoSelecionado[] }) {
  // Filtros iniciais da URL (?f.gate60=EMITIDO): drill-down dos cards do Resumo. Sem parâmetros = comportamento atual.
  const f = useDashboardFilters(() => lerFiltrosDaUrl(typeof window === "undefined" ? "" : window.location.search));
  const [page, setPage] = useState(1);
  const { data, loading, error, retry } = useProductionDashboard(dashboardId, selection, f.queryEntries, page, PAGE_SIZE);

  // Filtro mudou → volta à primeira página da tabela.
  useEffect(() => { setPage(1); }, [f.queryEntries]);

  // Filtro padrão do backend (ex.: semana atual no Nivelamento): aplicado UMA vez, na primeira resposta da tela.
  const padraoAplicado = useRef(false);
  useEffect(() => {
    if (!data || padraoAplicado.current) return;
    padraoAplicado.current = true;
    if (data.defaultFilters && Object.keys(data.defaultFilters).length > 0 && !f.hasFilters) f.replace(data.defaultFilters);
  }, [data, f]);

  if (!data && loading) return <LoadingState />;
  if (error && !data) return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;
  if (!data) return null;

  return <DashboardConteudo data={data} loading={loading} page={page} onPage={setPage} onToggle={f.toggle} onRemove={f.remove} onClear={f.clear} />;
}

/**
 * Apresentação PURA de uma resposta do dashboard (sem busca/estado de rede) — testável por renderização estática.
 * Blocos opcionais (matrizes) só são montados quando vêm na resposta: nada de CSS escondendo componente.
 */
export function DashboardConteudo({ data, loading = false, page, onPage, onToggle, onRemove, onClear }: {
  data: DashboardResult; loading?: boolean; page: number; onPage: (p: number) => void;
  onToggle: (d: string, v: string) => void; onRemove: (d: string, v: string) => void; onClear: () => void;
}) {
  if (data.totalRows === 0)
    return <ReportState title="Sem dados para este projeto">Fonte: {data.source}. Nenhum registro sincronizado para o projeto selecionado.</ReportState>;

  return (
    <section aria-label={data.title} className={cn("space-y-3 transition-opacity", loading && "opacity-60")}>
      <div className="flex flex-wrap items-center gap-2 min-h-8">
        {data.activeFilters.length === 0 ? (
          <p className="text-xs text-muted-foreground">Clique em barras, fatias ou indicadores para filtrar esta tela.</p>
        ) : (
          <>
            <span className="text-xs font-semibold text-muted-foreground">Filtros ativos:</span>
            {data.activeFilters.flatMap(a => a.values.map(v => (
              <button key={`${a.dimension}:${v.key}`} type="button" onClick={() => onRemove(a.dimension, v.key)}
                className="flex items-center gap-1 rounded-full border border-primary/40 bg-primary/10 px-2.5 py-0.5 text-xs text-primary hover:bg-primary/20"
                title={`Remover ${a.dimensionLabel}: ${v.label}`}>
                <span className="text-muted-foreground">{a.dimensionLabel}:</span> {v.label} <X className="h-3 w-3" />
              </button>
            )))}
            <button type="button" onClick={onClear} className="ml-1 flex items-center gap-1 rounded-full border border-border px-2.5 py-0.5 text-xs hover:bg-accent/50">
              <FilterX className="h-3 w-3" /> Limpar filtros
            </button>
          </>
        )}
        <span className="ml-auto text-[10px] text-muted-foreground">
          {data.filteredRows} de {data.totalRows} · {data.source}{data.lastSync ? ` · Sinc.: ${new Date(data.lastSync).toLocaleString("pt-BR")}` : ""}
        </span>
      </div>

      <div className="grid gap-3" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(150px, 1fr))" }}>
        {data.kpis.map(k => <Kpi key={k.key} kpi={k} onToggle={onToggle} />)}
      </div>

      {(data.matrices?.length ?? 0) > 0 && (
        <div className="grid gap-3 lg:grid-cols-2">
          {data.matrices!.map(m => <DashboardMatrixCard key={m.key} matrix={m} onToggle={onToggle} />)}
        </div>
      )}

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {data.charts.map(c => <DashboardChartCard key={c.key} chart={c} onToggle={onToggle} />)}
      </div>

      <Tabela data={data} page={page} onPage={onPage} onToggle={onToggle} />

      {data.notes.length > 0 && (
        <ul className="space-y-0.5 text-[10px] text-muted-foreground">{data.notes.map(n => <li key={n}>• {n}</li>)}</ul>
      )}
    </section>
  );
}
