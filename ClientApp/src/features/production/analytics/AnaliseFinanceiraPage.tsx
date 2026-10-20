// CONTROLE DE PRODUÇÃO → ANÁLISE FINANCEIRA (primeira versão, 30/09/2026) — SOMENTE dados existentes:
// fonte /api/reports/gestao-vista (Timesheet realizado + LDP previsto) e as MESMAS funções da Gestão à Vista
// (projectFacts/enrichDocuments). Nenhuma regra nova: custo previsto = documentPlannedCost (LDP), custo realizado =
// CustoCalculado do Timesheet, consumo = realizado ÷ previsto. Evolução no tempo: a tela existente "Controle de HH /
// Custo" (por data de lançamento) e a Curva S — linkadas, não recalculadas aqui. Por pessoa: sem fonte (não existe).
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ArrowDown, ArrowUp, ArrowUpDown, LineChart, Search } from "lucide-react";
import ConsolidadoBanner from "@/components/pmo/ConsolidadoBanner";
import HistoricalMeasurementPanel from "@/components/pmo/HistoricalMeasurementPanel";
import { formatCurrency, formatHH, usePmoReports } from "@/components/pmo/pmoData";
import { balance, consumption, enrichDocuments, projectFacts, type EnrichedDocument, type FactsReport } from "@/components/pmo/pmoFacts";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { rotaPermitida } from "@/features/permissions/permissionLogic";
import { useUserPreference } from "@/features/preferences/useUserPreference";
import { cn } from "@/lib/utils";
import { rotaProducao } from "../productionRoutes";

type Visao = "geral" | "projeto" | "documento";
const pct = (v: number | null) => (v === null || !Number.isFinite(v) ? "—" : `${formatHH(Math.round(v * 100) / 100)}%`);

interface Linha { chave: string; nome: string; hhPrev: number | null; hhReal: number | null; custoPrev: number | null; custoReal: number | null }
const metricas = (l: Linha) => ({ saldoCusto: balance(l.custoPrev, l.custoReal), consumoCusto: consumption(l.custoReal, l.custoPrev),
  saldoHh: balance(l.hhPrev, l.hhReal), consumoHh: consumption(l.hhReal, l.hhPrev) });

export default function AnaliseFinanceiraPage({ visao, selection }: { visao: Visao; selection: PmoSelecionado[] }) {
  const { reports, falhas, loading, error, retry } = usePmoReports("gestao-vista", { hub: "", project: "", selection });
  const { can } = usePermissions();
  const fr = reports as FactsReport[];
  const facts = useMemo(() => projectFacts(fr), [fr]);

  if (loading) return <ReportState title="Carregando Análise Financeira…">Mesma fonte da Gestão à Vista.</ReportState>;
  if (error) return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;

  const c = facts.cards;
  const links = [
    { rota: rotaProducao("controle-hh-custo"), rotulo: "Evolução no tempo (Controle de HH / Custo)" },
    { rota: rotaProducao("curva-s"), rotulo: "Previsto × emitido no tempo (Curva S)" },
  ].filter((l) => rotaPermitida(l.rota, "", can));

  const porDisciplina: Linha[] = facts.disciplines.map((d) => ({ chave: d.chave, nome: d.Disciplina,
    hhPrev: d.HH_Previsto, hhReal: d.HH_Realizado, custoPrev: d.Custo_Previsto, custoReal: d.Custo_Realizado }));
  const porProjeto: Linha[] = fr.map((r) => { const f = projectFacts([r]).cards;
    return { chave: r.projectId, nome: r.nomeProjeto, hhPrev: f.HH_Previsto, hhReal: f.HH_Realizado, custoPrev: f.Custo_Previsto, custoReal: f.Custo_Realizado }; });

  return (
    <div className="space-y-5">
      <ConsolidadoBanner selection={selection} falhas={falhas} />
      <HistoricalMeasurementPanel reports={fr} />
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 2xl:grid-cols-7">
        <Kpi titulo="Custo previsto" valor={formatCurrency(c.Custo_Previsto)} />
        <Kpi titulo="Custo realizado" valor={formatCurrency(c.Custo_Realizado)} />
        <Kpi titulo="Saldo de custo" valor={formatCurrency(c.SaldoCusto)} alerta={c.SaldoCusto !== null && c.SaldoCusto < 0} />
        <Kpi titulo="% consumo custo" valor={pct(c.ConsumoCusto)} alerta={c.ConsumoCusto !== null && c.ConsumoCusto > 100} />
        <Kpi titulo="HH previsto" valor={formatHH(c.HH_Previsto)} />
        <Kpi titulo="HH realizado" valor={formatHH(c.HH_Realizado)} />
        <Kpi titulo="% consumo HH" valor={pct(c.ConsumoHH)} alerta={c.ConsumoHH !== null && c.ConsumoHH > 100} />
      </div>
      <p className="text-[11px] text-muted-foreground">
        Saldo = previsto − realizado; consumo = realizado ÷ previsto.
        {links.length > 0 && <> Evolução temporal: {links.map((l, i) => <span key={l.rota}>{i > 0 && " · "}<Link className="text-primary underline" to={l.rota}>{l.rotulo}</Link></span>)}.</>}
      </p>

      {visao === "geral" && (
        <>
          <Grafico titulo="Previsto × realizado por disciplina (custo)" linhas={porDisciplina} />
          <TabelaLinhas titulo="Por disciplina" rotuloChave="Disciplina" linhas={porDisciplina} />
          {porProjeto.length > 1 && <TabelaLinhas titulo="Por projeto" rotuloChave="Projeto" linhas={porProjeto} />}
        </>
      )}
      {visao === "projeto" && (
        <>
          <Grafico titulo="Previsto × realizado por projeto (custo)" linhas={porProjeto} />
          <TabelaLinhas titulo="Por projeto" rotuloChave="Projeto" linhas={porProjeto} />
          {porProjeto.length === 1 && <p className="text-xs text-muted-foreground">Para comparar projetos, use “Analisar” no topo e marque outros projetos.</p>}
        </>
      )}
      {visao === "documento" && <TabelaDocumentos docs={enrichDocuments(fr)} nomes={new Map(fr.map((r) => [r.projectId, r.nomeProjeto]))} multi={fr.length > 1} />}
    </div>
  );
}

const Kpi = ({ titulo, valor, alerta }: { titulo: string; valor: string; alerta?: boolean }) => (
  <div className={cn("rounded-xl border border-border bg-card/60 p-3", alerta && "border-rose-500/50")}>
    <span className="block text-[10px] font-black uppercase tracking-wider text-muted-foreground">{titulo}</span>
    <span className={cn("mt-1 block truncate text-xl font-black", alerta && "text-rose-500")} title={valor}>{valor}</span>
  </div>
);

function Grafico({ titulo, linhas }: { titulo: string; linhas: Linha[] }) {
  const dados = linhas.filter((l) => l.custoPrev !== null || l.custoReal !== null).map((l) => ({ nome: l.nome, Previsto: l.custoPrev ?? 0, Realizado: l.custoReal ?? 0 }));
  if (dados.length === 0) return null;
  return (
    <section className="rounded-xl border border-border bg-card/40 p-4">
      <h2 className="mb-3 text-[10px] font-black uppercase tracking-widest text-muted-foreground">{titulo}</h2>
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={dados} margin={{ top: 5, right: 10, left: 10, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} opacity={0.3} />
            <XAxis dataKey="nome" tick={{ fontSize: 10 }} interval={0} angle={dados.length > 6 ? -20 : 0} textAnchor={dados.length > 6 ? "end" : "middle"} height={dados.length > 6 ? 60 : 30} />
            <YAxis tick={{ fontSize: 10 }} width={80} tickFormatter={(v: number) => new Intl.NumberFormat("pt-BR", { notation: "compact" }).format(v)} />
            <Tooltip formatter={(v: number) => formatCurrency(v)} />
            <Legend />
            <Bar dataKey="Previsto" fill="#0ea5e9" isAnimationActive={false} />
            <Bar dataKey="Realizado" fill="#10b981" isAnimationActive={false} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </section>
  );
}

function TabelaLinhas({ titulo, rotuloChave, linhas }: { titulo: string; rotuloChave: string; linhas: Linha[] }) {
  return (
    <section className="rounded-xl border border-border bg-card/40">
      <h2 className="border-b border-border px-4 py-2.5 text-[10px] font-black uppercase tracking-widest text-muted-foreground">{titulo}</h2>
      <div className="max-h-[50vh] overflow-auto custom-scrollbar">
        <table className="w-full text-xs">
          <thead className="sticky top-0 bg-secondary text-left text-muted-foreground">
            <tr>{[rotuloChave, "Custo prev.", "Custo real.", "Saldo custo", "% custo", "HH prev.", "HH real.", "Saldo HH", "% HH"].map((h, i) =>
              <th key={h} className={cn("px-3 py-2 font-semibold", i > 0 && "text-right")}>{h}</th>)}</tr>
          </thead>
          <tbody>
            {linhas.map((l) => { const m = metricas(l); return (
              <tr key={l.chave} className="border-t border-border">
                <td className="px-3 py-1.5 font-bold">{l.nome}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{formatCurrency(l.custoPrev)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{formatCurrency(l.custoReal)}</td>
                <td className={cn("px-3 py-1.5 text-right tabular-nums", m.saldoCusto !== null && m.saldoCusto < 0 && "text-rose-500")}>{formatCurrency(m.saldoCusto)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{pct(m.consumoCusto)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{formatHH(l.hhPrev)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{formatHH(l.hhReal)}</td>
                <td className={cn("px-3 py-1.5 text-right tabular-nums", m.saldoHh !== null && m.saldoHh < 0 && "text-rose-500")}>{formatHH(m.saldoHh)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums">{pct(m.consumoHh)}</td>
              </tr>
            ); })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

const COLS_DOC: { key: keyof EnrichedDocument; label: string; tipo: "texto" | "hh" | "moeda" | "pct" }[] = [
  { key: "codigo", label: "Documento", tipo: "texto" }, { key: "titulo", label: "Título", tipo: "texto" }, { key: "disciplina", label: "Disciplina", tipo: "texto" },
  { key: "custoPrevisto", label: "Custo prev.", tipo: "moeda" }, { key: "custoRealizado", label: "Custo real.", tipo: "moeda" },
  { key: "saldoCusto", label: "Saldo custo", tipo: "moeda" }, { key: "consumoCusto", label: "% custo", tipo: "pct" },
  { key: "hhPrevisto", label: "HH prev.", tipo: "hh" }, { key: "hhRealizado", label: "HH real.", tipo: "hh" }, { key: "consumoHh", label: "% HH", tipo: "pct" },
];

function TabelaDocumentos({ docs, nomes, multi }: { docs: EnrichedDocument[]; nomes: Map<string, string>; multi: boolean }) {
  const [busca, setBusca] = useState("");
  const [ordem, setOrdem] = useUserPreference<{ key: string; dir: "asc" | "desc" } | null>("analise-financeira-documento", "sort", { key: "consumoCusto", dir: "desc" });
  const linhas = useMemo(() => {
    const q = busca.trim().toLowerCase();
    const base = docs.filter((d) => !q || [d.codigo, d.titulo, d.disciplina].some((v) => v?.toLowerCase().includes(q)));
    if (!ordem) return base;
    const k = ordem.key as keyof EnrichedDocument;
    return [...base].sort((a, b) => {
      const va = a[k], vb = b[k];
      const c = va == null ? 1 : vb == null ? -1 : typeof va === "number" && typeof vb === "number" ? va - vb : String(va).localeCompare(String(vb), "pt-BR");
      return ordem.dir === "asc" ? c : -c;
    });
  }, [docs, busca, ordem]);
  const fmt = (d: EnrichedDocument, c: (typeof COLS_DOC)[number]) => {
    const v = d[c.key] as number | string | null;
    if (c.tipo === "moeda") return formatCurrency(v as number | null);
    if (c.tipo === "hh") return formatHH(v as number | null);
    if (c.tipo === "pct") return pct(v as number | null);
    return v == null ? "—" : String(v);
  };
  return (
    <section className="rounded-xl border border-border bg-card/40">
      <div className="flex items-center gap-2 border-b border-border px-4 py-2">
        <h2 className="mr-auto text-[10px] font-black uppercase tracking-widest text-muted-foreground">Por documento ({linhas.length})</h2>
        <span className="relative">
          <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input aria-label="Pesquisar documento" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Pesquisar"
            className="h-8 w-56 rounded-md border border-border bg-background pl-8 pr-2 text-xs" />
        </span>
      </div>
      <div className="max-h-[calc(100vh-22rem)] min-h-[240px] overflow-auto custom-scrollbar">
        <table className="w-full text-xs">
          <thead className="sticky top-0 z-10 bg-secondary text-left text-muted-foreground">
            <tr>
              {multi && <th className="px-2 py-2">Projeto</th>}
              {COLS_DOC.map((c) => { const ativa = ordem?.key === c.key; const I = !ativa ? ArrowUpDown : ordem!.dir === "asc" ? ArrowUp : ArrowDown; return (
                <th key={c.key} className={cn("px-2 py-2 font-semibold", c.tipo !== "texto" && "text-right")}>
                  <button type="button" className="inline-flex items-center gap-1 hover:text-foreground"
                    onClick={() => setOrdem((o) => (o?.key !== c.key ? { key: c.key, dir: "asc" } : o.dir === "asc" ? { key: c.key, dir: "desc" } : null))}>
                    {c.label}<I className={cn("h-3 w-3", ativa ? "text-primary" : "opacity-40")} />
                  </button>
                </th>); })}
            </tr>
          </thead>
          <tbody>
            {linhas.map((d) => (
              <tr key={d.rowId} className="border-t border-border hover:bg-secondary/40">
                {multi && <td className="px-2 py-1.5 font-bold text-amber-500">{nomes.get(d.projectId)}</td>}
                {COLS_DOC.map((c) => <td key={c.key} className={cn("max-w-[300px] truncate px-2 py-1.5", c.tipo !== "texto" && "text-right tabular-nums")} title={fmt(d, c)}>{fmt(d, c)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="flex items-center gap-1.5 px-4 py-2 text-[10px] text-muted-foreground"><LineChart className="h-3 w-3" />Realizado por documento: só quando o código é único na LDP (mesma regra da Gestão à Vista).</p>
    </section>
  );
}
