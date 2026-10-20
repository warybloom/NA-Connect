import { useEffect, useMemo, useState } from "react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TrendingUp, Clock, Wallet, FileText, CalendarDays, CalendarRange, Calendar, Info } from "lucide-react";
import { ApiError, getJson } from "@/lib/api";
import { addDays, day, formatCurrency, formatData, formatHH, limitedMap, weekStart } from "./pmoData";

import { consolidarCurvaS, type CurvaSPonto, type CurvaSReport } from "./pmoMultiCore";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";
import type { FalhaProjeto } from "./useMergedPages";
type Visualizacao = 'diario' | 'semanal' | 'mensal';

/**
 * Curva S documental. Previsto: HH/custo previsto dos documentos não cancelados na data baseline. Realizado: valor agregado por
 * emissão (HH/custo previsto dos documentos EMITIDOS na Emission_Date). O Timesheet não tem data de trabalho na fonte, por isso
 * HH/custo REALIZADOS (horas efetivamente gastas) não podem ser posicionados no tempo — o painel avisa isso. As séries já chegam
 * acumuladas do backend (PmoCurvaSRules); aqui só se escolhe o último acumulado de cada período.
 */
export default function ViewCurvaS({ selection }: { selection: PmoSelecionado[] }) {
  const selectionKey = JSON.stringify(selection.map(p => [p.hubId, p.id]));
  const [falhas, setFalhas] = useState<FalhaProjeto[]>([]);
  const [visualizacao, setVisualizacao] = useState<Visualizacao>('semanal');
  const [report, setReport] = useState<CurvaSReport>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined); setReport(undefined);
    (async () => {
      // Consolidado: cada projeto devolve seu acumulado; a Curva S junta as datas e soma os acumulados (aditivo). Projeto que falha não derruba a tela.
      const fs: FalhaProjeto[] = [];
      const partes = await limitedMap(selection, async p => {
        try { return await getJson<CurvaSReport>(`/api/reports/curva-s?${new URLSearchParams({ hubId: p.hubId, projectId: p.id })}`, abort.signal); }
        catch (e) { if (abort.signal.aborted) throw e; fs.push({ projectId: p.id, projectName: p.name, message: e instanceof ApiError ? e.message : 'Falha ao consultar o projeto.' }); return null; }
      });
      if (abort.signal.aborted) return;
      setFalhas(fs);
      const ok = partes.filter((x): x is CurvaSReport => x !== null);
      if (ok.length === 0) { setError(fs[0]?.message ?? 'Não foi possível carregar a Curva S.'); return; }
      setReport(ok.length === 1 ? ok[0] : consolidarCurvaS(ok));
    })().catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar a Curva S.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [selectionKey, attempt]);

  const serie = useMemo(() => {
    if (!report || report.pontos.length === 0) return [];
    const pontos = report.pontos.map(p => ({ ...p, data: day(p.data)! }));
    const ref = day(report.dataReferencia)!;
    const inicio = pontos[0].data;
    const fim = pontos[pontos.length - 1].data > ref ? pontos[pontos.length - 1].data : ref;
    const chave = (d: string) => visualizacao === 'diario' ? d : visualizacao === 'semanal' ? weekStart(d, 0) : d.slice(0, 7) + '-01';
    const proximo = (d: string) => visualizacao === 'diario' ? addDays(d, 1) : visualizacao === 'semanal' ? addDays(d, 7)
      : (() => { const x = new Date(d + 'T12:00:00Z'); x.setUTCMonth(x.getUTCMonth() + 1, 1); return x.toISOString().slice(0, 10); })();
    const linhas: any[] = [];
    let i = 0; let ultimo: CurvaSPonto | undefined;
    for (let inicioPeriodo = chave(inicio); inicioPeriodo <= chave(fim); inicioPeriodo = proximo(inicioPeriodo)) {
      const fimPeriodo = addDays(proximo(inicioPeriodo), -1);
      while (i < pontos.length && pontos[i].data <= fimPeriodo) { ultimo = pontos[i]; i++; }
      const futuro = inicioPeriodo > ref;
      linhas.push({ Data: inicioPeriodo, DataFormatada: formatData(inicioPeriodo),
        HH_Previsto: ultimo?.previstoHh ?? 0, HH_Realizado: futuro ? null : (ultimo?.realizadoHh ?? 0),
        Custo_Previsto: ultimo?.previstoCusto ?? 0, Custo_Realizado: futuro ? null : (ultimo?.realizadoCusto ?? 0),
        Docs_Previsto: ultimo?.previstoDocumentos ?? 0, Docs_Emitidos: futuro ? null : (ultimo?.emitidosDocumentos ?? 0) });
    }
    return linhas;
  }, [report, visualizacao]);

  if (loading) return <div className="p-12 text-slate-400">Carregando Curva S...</div>;
  if (error) return <div role="alert" className="p-12 text-rose-400">{error} <button className="underline" onClick={() => setAttempt(a => a + 1)}>Tentar novamente</button></div>;
  if (!report) return null;
  const c = report.cobertura;
  const semDado = report.pontos.length < 2;
  const somaHh = c.hhCurvaPrevisto + c.hhCancelados + c.hhSemBaseline + c.hhAtividades + (c.hhSemDocumento ?? 0);
  const btn = (v: Visualizacao) => `flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-md transition-colors ${visualizacao === v ? 'bg-sky-500 text-white' : 'text-slate-400 hover:text-foreground'}`;
  const grafico = (titulo: string, icone: React.ReactNode, prev: string, real: string, nomePrev: string, nomeReal: string, cores: [string, string],
    fmt: (n: number) => string, testId: string, eixo?: (n: number) => string) => (
    <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid={testId}>
      <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm">{icone} {titulo}</h3>
      <div className="h-[360px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={serie} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
            <XAxis dataKey="DataFormatada" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} minTickGap={24} dy={10} />
            <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} tickFormatter={eixo} width={64} />
            <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }}
              formatter={(v: number) => fmt(v)} labelFormatter={l => `Até ${l}`} />
            <Legend wrapperStyle={{ paddingTop: '12px', color: 'hsl(var(--foreground))' }} />
            <Area type="monotone" dataKey={prev} name={nomePrev} stroke={cores[0]} strokeWidth={3} fillOpacity={0.15} fill={cores[0]} isAnimationActive={false} />
            <Area type="monotone" dataKey={real} name={nomeReal} stroke={cores[1]} strokeWidth={3} fillOpacity={0.15} fill={cores[1]} connectNulls={false} isAnimationActive={false} />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );

  return (
    <div className="space-y-6 animate-enter">
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-4 rounded-xl border border-slate-700 bg-slate-800/80 shadow">
        <div className="flex items-center gap-3">
          <TrendingUp className="w-6 h-6 text-sky-400" />
          <div>
            <h2 className="text-lg font-bold text-slate-100">Curva S — Previsto × Emitido (valor agregado documental)</h2>
            <ConsolidadoBanner selection={selection} falhas={falhas} />
            <p className="text-xs text-slate-400">Acumulado do projeto inteiro (todas as datas), sem filtro de período. Referência: {formatData(report.dataReferencia)}.</p>
          </div>
        </div>
        <div className="flex items-center gap-1 bg-slate-950 p-1 rounded-lg border border-slate-700" aria-label="Granularidade da Curva S">
          <button onClick={() => setVisualizacao('diario')} className={btn('diario')} aria-pressed={visualizacao === 'diario'}><CalendarDays className="w-3.5 h-3.5" /> Diário</button>
          <button onClick={() => setVisualizacao('semanal')} className={btn('semanal')} aria-pressed={visualizacao === 'semanal'}><CalendarRange className="w-3.5 h-3.5" /> Semanal</button>
          <button onClick={() => setVisualizacao('mensal')} className={btn('mensal')} aria-pressed={visualizacao === 'mensal'}><Calendar className="w-3.5 h-3.5" /> Mensal</button>
        </div>
      </div>

      <div className="rounded-lg border border-amber-500/40 bg-amber-950/20 p-3 text-xs text-amber-200 space-y-1" data-testid="aviso-curva-s">
        <p className="flex items-center gap-2 font-bold"><Info className="w-4 h-4" /> Como ler esta curva</p>
        <p><b>Previsto:</b> {report.fontePrevisto}.</p>
        <p><b>Realizado (emitido):</b> {report.fonteRealizado}. As horas e o custo efetivamente gastos (HH/Custo Realizado) só existem como total acumulado do Timesheet
          (ver Gestão à Vista) e não podem ser distribuídos no tempo sem inventar datas.</p>
      </div>

      {semDado ? (
        <div className="border border-amber-500/40 bg-amber-950/20 rounded-xl p-6 text-sm text-amber-100 space-y-1" data-testid="curva-s-sem-dados">
          <p className="font-bold">Curva S indisponível com as fontes atuais: {report.pontos.length === 0 ? 'nenhum documento' : 'apenas 1 data'} com data baseline (Planned_Date_Baseline) ou emissão (Emission_Date) válida na LDP.</p>
          <p className="text-xs text-amber-200/80">{c.totalDocumentos} documentos na LDP atual · {c.cancelados} cancelados · {c.semBaseline} sem data baseline. Uma curva acumulada exige ao menos duas datas; nenhum gráfico é exibido para não simular evolução.</p>
        </div>
      ) : (
        <>
          <div className="grid gap-6 lg:grid-cols-2">
            {grafico('Curva S — HH (acumulado)', <Clock className="w-4 h-4 text-sky-400" />, 'HH_Previsto', 'HH_Realizado', 'HH Previsto (baseline)', 'HH dos documentos emitidos',
              ['#64748b', '#0ea5e9'], n => `${formatHH(n)} h`, 'curva-hh')}
            {grafico('Curva S — Custo (R$ acumulado)', <Wallet className="w-4 h-4 text-emerald-400" />, 'Custo_Previsto', 'Custo_Realizado', 'Custo Previsto (baseline)', 'Custo dos documentos emitidos',
              ['#94a3b8', '#10b981'], n => formatCurrency(n), 'curva-custo', n => `R$${(n / 1000).toFixed(0)}k`)}
          </div>
          {grafico('Curva S — Documentos (quantidade acumulada)', <FileText className="w-4 h-4 text-violet-400" />, 'Docs_Previsto', 'Docs_Emitidos', 'Documentos previstos (baseline)', 'Documentos emitidos',
            ['#a78bfa', '#22c55e'], n => `${n} docs`, 'curva-docs')}
        </>
      )}

      <div className="rounded-lg border border-slate-700 p-3 text-xs text-slate-400 space-y-1" data-testid="reconciliacao-curva-s">
        <p className="font-bold text-slate-300">Reconciliação (fim da curva × totais do projeto)</p>
        <p>HH Previsto total do projeto {formatHH(c.hhPrevistoTotalProjeto)} = curva documental {formatHH(c.hhCurvaPrevisto)} + cancelados {formatHH(c.hhCancelados)} + documentos sem baseline {formatHH(c.hhSemBaseline)}
          + atividades AT {formatHH(c.hhAtividades)} + sem documento/ambíguos {formatHH(c.hhSemDocumento)} = {formatHH(somaHh)}; diferença {c.hhPrevistoTotalProjeto === null ? '—' : formatHH(c.hhPrevistoTotalProjeto - somaHh)} HH.</p>
        <p>HH agregado dos documentos emitidos: {formatHH(c.hhCurvaRealizado)} · Custo previsto na curva: {formatCurrency(c.custoCurvaPrevisto)} · Custo agregado dos emitidos: {formatCurrency(c.custoCurvaRealizado)}.</p>
        <p>Documentos: {c.totalDocumentos} no total · {c.cancelados} cancelados (fora) · {c.semBaseline} sem data baseline (fora do previsto) · {c.ambiguos} ambíguos na LDP (HH/custo desconhecidos) · {c.semHhPrevisto} sem HH previsto · {c.semCustoPrevisto} sem custo previsto.</p>
      </div>
    </div>
  );
}
