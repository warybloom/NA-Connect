// Gráfico clicável de uma dimensão (Recharts, já usado no projeto). Clique numa barra/fatia = alterna o
// filtro daquela dimensão. O gráfico recebe do backend o conjunto SEM o próprio filtro, então as demais
// categorias continuam visíveis (esmaecidas) e a seleção pode ser trocada.
import { Bar, BarChart, Cell, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { cn } from "@/lib/utils";
import { corDoPonto, formatarValor } from "./dashboardTheme";
import type { DashboardChart, DashboardPoint } from "./types";

const MAX_COLUNAS = 10;

function Dica({ active, payload, chart }: { active?: boolean; payload?: { payload: DashboardPoint }[]; chart: DashboardChart }) {
  if (!active || !payload?.length) return null;
  const p = payload[0].payload;
  return (
    <div className="rounded-md border border-border bg-popover px-3 py-2 text-xs shadow-lg">
      <p className="font-semibold">{p.label}</p>
      <p className="text-muted-foreground">{chart.measureLabel}: <span className="text-foreground font-semibold">{formatarValor(p.value, chart.measureFormat)}</span></p>
      <p className="text-[10px] text-muted-foreground mt-1">{p.selected ? "Clique para remover o filtro" : "Clique para filtrar"}</p>
    </div>
  );
}

export default function DashboardChartCard({ chart, onToggle }: { chart: DashboardChart; onToggle: (dimension: string, key: string) => void }) {
  const algumSelecionado = chart.points.some(p => p.selected);
  const opacidade = (p: DashboardPoint) => (!algumSelecionado || p.selected ? 1 : 0.3);
  const clicar = (p?: DashboardPoint) => p && onToggle(chart.dimension, p.key);
  // Coluna com muitas categorias vira barra horizontal (rótulos legíveis em 16:9).
  const tipo = chart.kind === "column" && chart.points.length > MAX_COLUNAS ? "bar" : chart.kind;
  const alturaBarras = Math.max(160, chart.points.length * 26);

  return (
    <article className="flex min-h-0 flex-col rounded-xl border border-border bg-card/70 p-4">
      <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">{chart.title}</h3>
      {chart.points.length === 0 ? (
        <div className="flex flex-1 items-center justify-center py-10 text-xs text-muted-foreground">Sem dados para os filtros atuais.</div>
      ) : tipo === "donut" ? (
        <div className="flex flex-1 items-center gap-3">
          <div className="h-44 w-44 flex-shrink-0">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={chart.points} dataKey="value" nameKey="label" innerRadius="55%" outerRadius="95%" paddingAngle={1}
                  onClick={(d: { payload?: DashboardPoint }) => clicar(d?.payload)} className="cursor-pointer" isAnimationActive={false}>
                  {chart.points.map((p, i) => (
                    <Cell key={p.key} fill={corDoPonto(p.key, i)} fillOpacity={opacidade(p)} stroke={p.selected ? "hsl(var(--foreground))" : "none"} />
                  ))}
                </Pie>
                <Tooltip content={<Dica chart={chart} />} />
              </PieChart>
            </ResponsiveContainer>
          </div>
          <ul className="min-w-0 flex-1 space-y-1 max-h-44 overflow-y-auto custom-scrollbar">
            {chart.points.map((p, i) => (
              <li key={p.key}>
                <button type="button" onClick={() => clicar(p)} title={p.selected ? "Remover filtro" : "Filtrar"}
                  className={cn("flex w-full items-center gap-2 rounded px-1.5 py-0.5 text-left text-xs hover:bg-accent/50", p.selected && "bg-primary/10 font-semibold")}
                  style={{ opacity: opacidade(p) }}>
                  <span className="h-2.5 w-2.5 flex-shrink-0 rounded-sm" style={{ background: corDoPonto(p.key, i) }} />
                  <span className="truncate flex-1">{p.label}</span>
                  <span className="tabular-nums">{formatarValor(p.value, chart.measureFormat)}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : tipo === "column" ? (
        <div className="h-52">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chart.points} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
              <XAxis dataKey="label" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} interval={0} angle={chart.points.length > 5 ? -25 : 0}
                textAnchor={chart.points.length > 5 ? "end" : "middle"} height={chart.points.length > 5 ? 56 : 24} />
              <YAxis tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} allowDecimals={false} />
              <Tooltip content={<Dica chart={chart} />} cursor={{ fill: "hsl(var(--accent) / 0.3)" }} />
              <Bar dataKey="value" radius={[4, 4, 0, 0]} onClick={(d: { payload?: DashboardPoint }) => clicar(d?.payload)} className="cursor-pointer" isAnimationActive={false}>
                {chart.points.map((p, i) => <Cell key={p.key} fill={corDoPonto(p.key, i)} fillOpacity={opacidade(p)} />)}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <div className="max-h-56 overflow-y-auto custom-scrollbar">
          <div style={{ height: alturaBarras }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chart.points} layout="vertical" margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
                <XAxis type="number" hide />
                <YAxis type="category" dataKey="label" width={130} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} interval={0} />
                <Tooltip content={<Dica chart={chart} />} cursor={{ fill: "hsl(var(--accent) / 0.3)" }} />
                <Bar dataKey="value" radius={[0, 4, 4, 0]} onClick={(d: { payload?: DashboardPoint }) => clicar(d?.payload)} className="cursor-pointer" isAnimationActive={false}
                  label={{ position: "right", fontSize: 10, fill: "hsl(var(--muted-foreground))", formatter: (v: number) => formatarValor(v, chart.measureFormat) }}>
                  {chart.points.map((p, i) => <Cell key={p.key} fill={corDoPonto(p.key, i)} fillOpacity={opacidade(p)} />)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
    </article>
  );
}
