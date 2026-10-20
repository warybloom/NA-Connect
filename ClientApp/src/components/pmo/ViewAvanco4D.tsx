import { useEffect, useMemo, useState } from "react";
import { BarChart, Bar, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, ReferenceLine } from "recharts";
import { CalendarClock, AlertTriangle, CheckCircle2, ListTree, TrendingUp, Info, Layers, Gauge } from "lucide-react";
import { ApiError, getJson } from "@/lib/api";
import { formatData, limitedMap } from "./pmoData";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";
import type { FalhaProjeto } from "./useMergedPages";

interface Tarefa4D {
  wbs: string | null; tarefa: string | null; documento: string | null; disciplina: string | null; previsto: number | null; realizado: number | null;
  desvioPp: number | null; situacaoRealizado: 'VALOR' | 'ZERO_CONFIRMADO' | 'SEM_DADO'; situacao: 'ATRASADA' | 'NO_PRAZO' | 'SEM_DADO';
  diasAtraso: number | null; inicio: string | null; fim: string | null; tendencia: string | null;
}
interface Avanco4DReport {
  projectId: string; tipoLdp: number; disponivel: boolean; motivo: string | null; dataReferencia: string; dataCorte: string | null;
  previstoGeral: number | null; realizadoGeral: number | null; desvioPp: number | null; fonteGeral: string; fontePrevisto: string; fonteRealizado: string;
  timeline: { data: string; previsto: number | null; realizado: number | null }[];
  porDisciplina: { disciplina: string; previsto: number; realizado: number; desvioPp: number; tarefas: number }[];
  tarefas: Tarefa4D[];
  kpis: { totalTarefas: number; noPrazo: number; atrasadas: number; semDado: number };
  diagnostico: { tarefasFolha: number; consideradas: number; semRealizado: number; semPrevisto: number; semData: number; zeroConfirmado: number; invalidos: number;
    coberturaPct: number; coberturaMinimaPct: number; previstoPreenchido: boolean; realizadoPreenchido: boolean };
}

const num = (n: number | null | undefined, d = 1) => n === null || n === undefined ? '—' : new Intl.NumberFormat('pt-BR', { minimumFractionDigits: d, maximumFractionDigits: d }).format(n);
const pct = (n: number | null | undefined) => n === null || n === undefined ? '—' : `${num(n)}%`;
const pp = (n: number | null | undefined) => n === null || n === undefined ? '—' : `${n > 0 ? '+' : ''}${num(n)} p.p.`;
const corDesvio = (n: number | null | undefined) => n === null || n === undefined ? 'text-slate-400' : n < 0 ? 'text-rose-400' : 'text-emerald-400';
const dataHora = (v: string | null) => v ? `${formatData(v)} ${v.slice(11, 16)}` : '—';

/**
 * Avanço 4D = % físico Previsto × Realizado do Cronograma (intenção original do porte ForgeSync: KPIs de tarefas no prazo/atrasadas, avanço médio
 * geral, comparativo Previsto × Realizado e detalhamento por WBS). Leitura pura do que o motor homologado já gravou (GET /api/reports/avanco-4d).
 * Sem fonte suficiente a tela diz isso explicitamente — nunca mostra 0% como se fosse dado.
 */
function Avanco4DProjeto({ report }: { report: Avanco4DReport }) {
  const [visao, setVisao] = useState<'ofensores' | 'todas'>('ofensores');

  const timeline = useMemo(() => report.timeline.map(p => ({ ...p, ts: new Date(p.data.slice(0, 10) + 'T12:00:00Z').getTime(),
    DataFormatada: formatData(p.data) })), [report]);
  const linhas = useMemo(() => {
    const t = report.tarefas;
    return (visao === 'ofensores' ? t.filter(x => x.situacao === 'ATRASADA') : t).slice(0, 300);
  }, [report, visao]);

  const d = report.diagnostico;
  const hoje = new Date(report.dataReferencia.slice(0, 10) + 'T12:00:00Z').getTime();

  const diagnostico = (
    <div className="rounded-lg border border-slate-700 p-3 text-xs text-slate-400 space-y-1" data-testid="diagnostico-4d">
      <p className="font-bold text-slate-300 flex items-center gap-2"><Info className="w-4 h-4" /> Diagnóstico de cobertura</p>
      <p>Fonte do previsto: {report.fontePrevisto}. Fonte do realizado: {report.fonteRealizado}.</p>
      <p>Tarefas-folha: {d.tarefasFolha} · consideradas (Previsto e Realizado conhecidos): {d.consideradas} · sem Realizado (SEM DADO): {d.semRealizado} · sem Previsto: {d.semPrevisto} ·
        sem data: {d.semData} · 0% confirmado: {d.zeroConfirmado} · valores inválidos ignorados: {d.invalidos}.</p>
      <p>Previsto preenchido no projeto: {d.previstoPreenchido ? 'sim' : 'não'} · Realizado preenchido (algum &gt; 0): {d.realizadoPreenchido ? 'sim' : 'não'} ·
        Cobertura (tarefas com Previsto &gt; 0 e Realizado conhecido): {num(d.coberturaPct, 2)}% (mínimo {num(d.coberturaMinimaPct, 0)}%).</p>
    </div>
  );

  if (!report.disponivel) return (
    <div className="space-y-6 animate-enter">
      <div className="rounded-xl border border-amber-500/40 bg-amber-950/20 p-6 space-y-2" data-testid="4d-indisponivel">
        <h2 className="text-lg font-bold text-amber-200 flex items-center gap-2"><CalendarClock className="w-5 h-5" /> Avanço 4D não disponível para este projeto com as fontes atuais.</h2>
        <p className="text-sm text-amber-100/80">{report.motivo}</p>
        <p className="text-xs text-slate-400">Projeto Tipo {report.tipoLdp}. Nenhum gráfico é exibido para não confundir ausência de dado com 0% de avanço.</p>
      </div>
      {diagnostico}
    </div>
  );

  return (
    <div className="space-y-6 animate-enter">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-1 p-3 rounded-xl border border-slate-700 bg-slate-800/80 text-xs text-slate-300" data-testid="data-corte">
        <span className="flex items-center gap-2 font-bold text-slate-100"><Gauge className="w-4 h-4 text-sky-400" /> DATA DE CORTE: {dataHora(report.dataCorte)}</span>
        <span>Última leitura do Cronograma (Tipo {report.tipoLdp}). Data de referência (hoje): {formatData(report.dataReferencia)}.</span>
        <span>Avanço geral: {report.fonteGeral}. Desvio em pontos percentuais (Realizado − Previsto).</span>
      </div>

      <div className="grid gap-4 md:grid-cols-5">
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-slate-500">
          <div className="text-[10px] uppercase text-slate-400 font-bold mb-1">Avanço Previsto</div>
          <div className="text-3xl font-black text-slate-200" data-testid="kpi-previsto">{pct(report.previstoGeral)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
          <div className="text-[10px] uppercase text-sky-400 font-bold mb-1">Avanço Realizado</div>
          <div className="text-3xl font-black text-sky-400" data-testid="kpi-realizado">{pct(report.realizadoGeral)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-amber-500">
          <div className="text-[10px] uppercase text-amber-400 font-bold mb-1 flex items-center gap-1"><CalendarClock className="w-4 h-4" /> Desvio</div>
          <div className={`text-3xl font-black ${corDesvio(report.desvioPp)}`} data-testid="kpi-desvio">{pp(report.desvioPp)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-emerald-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-400 font-bold mb-1"><CheckCircle2 className="w-4 h-4" /> No Prazo / Adiantadas</div>
          <div className="text-3xl font-black text-emerald-400">{report.kpis.noPrazo} <span className="text-sm font-normal text-slate-400">tarefas</span></div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-rose-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-rose-400 font-bold mb-1"><AlertTriangle className="w-4 h-4" /> Atrasadas</div>
          <div className="text-3xl font-black text-rose-400">{report.kpis.atrasadas} <span className="text-sm font-normal text-slate-400">de {report.kpis.totalTarefas} · {report.kpis.semDado} sem dado</span></div>
        </div>
      </div>

      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid="timeline-4d">
        <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-1"><TrendingUp className="w-4 h-4 text-sky-400" /> Timeline: % físico Previsto × Realizado por leitura do Cronograma</h3>
        <p className="text-[11px] text-slate-400 mb-3">Cada ponto é a última leitura do dia (resumo do projeto no MS Project). Antes da primeira leitura com Realizado a linha azul não existe (sem dado).</p>
        {timeline.length < 2 ? <div className="text-xs text-slate-500 py-8 text-center">Histórico de leituras insuficiente para desenhar a timeline (menos de 2 datas).</div> : (
          <div className="h-[320px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={timeline} margin={{ top: 10, right: 20, left: -10, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                <XAxis dataKey="ts" type="number" scale="time" domain={['dataMin', Math.max(hoje, timeline[timeline.length - 1].ts)]}
                  tickFormatter={(t: number) => formatData(new Date(t).toISOString())} tick={{ fill: '#94a3b8', fontSize: 10 }} axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} />
                <YAxis domain={[0, 100]} tickFormatter={v => `${v}%`} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} axisLine={false} tickLine={false} />
                <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }}
                  labelFormatter={(t: number) => formatData(new Date(t).toISOString())} formatter={(v: number) => pct(v)} />
                <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                <ReferenceLine x={hoje} stroke="#f59e0b" strokeDasharray="4 4" label={{ value: 'Hoje', fill: '#f59e0b', fontSize: 11, position: 'insideTopRight' }} />
                <Line type="monotone" dataKey="previsto" name="% Previsto" stroke="#94a3b8" strokeWidth={3} dot={{ r: 3 }} connectNulls={false} isAnimationActive={false} />
                <Line type="monotone" dataKey="realizado" name="% Realizado" stroke="#0ea5e9" strokeWidth={3} dot={{ r: 3 }} connectNulls={false} isAnimationActive={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid="4d-disciplinas">
          <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-1"><Layers className="w-4 h-4 text-violet-400" /> Previsto × Realizado por Disciplina</h3>
          <p className="text-[11px] text-slate-400 mb-3">Média simples das tarefas-folha de cada disciplina (Previsto e Realizado conhecidos).</p>
          <div className="h-[300px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={report.porDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 40 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                <XAxis dataKey="disciplina" interval={0} angle={-35} textAnchor="end" height={70} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 9 }} axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} />
                <YAxis domain={[0, 100]} tickFormatter={v => `${v}%`} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} axisLine={false} tickLine={false} />
                <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }} formatter={(v: number) => pct(v)} />
                <Legend wrapperStyle={{ paddingTop: '4px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                <Bar dataKey="previsto" name="% Previsto" fill="#64748b" radius={[3, 3, 0, 0]} maxBarSize={22} isAnimationActive={false} />
                <Bar dataKey="realizado" name="% Realizado" fill="#0ea5e9" radius={[3, 3, 0, 0]} maxBarSize={22} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[420px]">
          <div className="p-4 border-b border-slate-700 text-sm font-semibold text-slate-100">Desvio por Disciplina</div>
          <div className="overflow-auto custom-scrollbar flex-1">
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-900 sticky top-0 uppercase text-slate-400"><tr>
                <th className="px-3 py-2 font-medium">Disciplina</th><th className="px-3 py-2 font-medium text-right">Previsto</th><th className="px-3 py-2 font-medium text-right">Realizado</th>
                <th className="px-3 py-2 font-medium text-right">Desvio</th><th className="px-3 py-2 font-medium text-right">Tarefas</th></tr></thead>
              <tbody className="divide-y divide-slate-700/50">
                {report.porDisciplina.map(x => (
                  <tr key={x.disciplina} data-disciplina={x.disciplina}>
                    <td className="px-3 py-2 font-medium text-slate-200">{x.disciplina}</td>
                    <td className="px-3 py-2 text-right text-slate-300">{pct(x.previsto)}</td>
                    <td className="px-3 py-2 text-right text-sky-400 font-bold">{pct(x.realizado)}</td>
                    <td className={`px-3 py-2 text-right font-bold ${corDesvio(x.desvioPp)}`}>{pp(x.desvioPp)}</td>
                    <td className="px-3 py-2 text-right text-slate-400">{x.tarefas}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[480px]" data-testid="4d-tarefas">
        <div className="p-4 border-b border-slate-700 flex items-center justify-between gap-3 flex-wrap">
          <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2"><ListTree className="w-4 h-4 text-sky-400" /> Maiores ofensores 4D e detalhamento por WBS</h3>
          <div className="flex items-center gap-2 text-xs">
            <button onClick={() => setVisao('ofensores')} aria-pressed={visao === 'ofensores'} className={`px-3 py-1.5 rounded ${visao === 'ofensores' ? 'bg-rose-500 text-white' : 'bg-slate-800 text-slate-300'}`}>Atrasadas ({report.kpis.atrasadas})</button>
            <button onClick={() => setVisao('todas')} aria-pressed={visao === 'todas'} className={`px-3 py-1.5 rounded ${visao === 'todas' ? 'bg-sky-500 text-white' : 'bg-slate-800 text-slate-300'}`}>Todas ({report.kpis.totalTarefas})</button>
            <span className="text-slate-500">exibindo até 300, ordenadas pelo maior desvio negativo</span>
          </div>
        </div>
        <div className="overflow-auto custom-scrollbar flex-1">
          <table className="w-full text-xs text-left">
            <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400"><tr>
              <th className="px-3 py-2 font-medium w-20">WBS</th><th className="px-3 py-2 font-medium">Atividade / Documento</th><th className="px-3 py-2 font-medium">Disciplina</th>
              <th className="px-3 py-2 font-medium text-right">Previsto</th><th className="px-3 py-2 font-medium text-right">Realizado</th><th className="px-3 py-2 font-medium text-right">Desvio</th>
              <th className="px-3 py-2 font-medium">Data prevista</th><th className="px-3 py-2 font-medium text-right">Dias de atraso</th><th className="px-3 py-2 font-medium text-center">Situação</th></tr></thead>
            <tbody className="divide-y divide-slate-700/50">
              {linhas.map((t, i) => (
                <tr key={`${t.wbs}-${i}`} className="hover:bg-slate-700/30">
                  <td className="px-3 py-2 font-semibold text-sky-400">{t.wbs ?? '—'}</td>
                  <td className="px-3 py-2"><div className="font-medium truncate max-w-[300px] text-slate-200" title={t.tarefa ?? undefined}>{t.tarefa ?? '—'}</div><div className="text-[10px] text-slate-500 font-mono">{t.documento ?? ''}</div></td>
                  <td className="px-3 py-2 text-slate-300">{t.disciplina ?? '—'}</td>
                  <td className="px-3 py-2 text-right text-slate-300">{pct(t.previsto)}</td>
                  <td className="px-3 py-2 text-right font-bold text-sky-400">{t.situacaoRealizado === 'SEM_DADO' ? <span className="text-amber-400 italic font-normal">Sem dado</span> : t.situacaoRealizado === 'ZERO_CONFIRMADO' ? '0% (confirmado)' : pct(t.realizado)}</td>
                  <td className={`px-3 py-2 text-right font-bold ${corDesvio(t.desvioPp)}`}>{pp(t.desvioPp)}</td>
                  <td className="px-3 py-2 text-slate-400" title="Término atual (tendência) ou baseline">{formatData(t.tendencia ?? t.fim)}</td>
                  <td className="px-3 py-2 text-right text-rose-400 font-bold">{t.diasAtraso === null ? '—' : `+${t.diasAtraso}d`}</td>
                  <td className="px-3 py-2 text-center">{t.situacao === 'ATRASADA' ? <span className="px-2 py-1 rounded bg-rose-900/30 border border-rose-500/30 text-rose-400 text-[10px]">Atrasada</span>
                    : t.situacao === 'NO_PRAZO' ? <span className="px-2 py-1 rounded bg-emerald-900/30 border border-emerald-500/30 text-emerald-400 text-[10px]">No prazo</span>
                    : <span className="px-2 py-1 rounded bg-slate-800 border border-slate-600 text-slate-400 text-[10px]">Sem dado</span>}</td>
                </tr>
              ))}
              {linhas.length === 0 && <tr><td colSpan={9} className="text-center py-6 text-slate-500">Nenhuma tarefa nesta visão.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>

      {diagnostico}
    </div>
  );
}


/**
 * Avanço 4D multi-projeto. Avanço % NÃO é aditivo e não há ponderação canônica entre projetos: com 2+ projetos a tela mostra a VISÃO CONSOLIDADA POR
 * PROJETO (uma linha cada, sem total/média inventados) e, abaixo, o detalhe completo de cada projeto — nunca só o primeiro ou o último.
 */
export default function ViewAvanco4D({ selection }: { selection: PmoSelecionado[] }) {
  const selectionKey = JSON.stringify(selection.map(p => [p.hubId, p.id]));
  const [reports, setReports] = useState<{ project: PmoSelecionado; report: Avanco4DReport }[]>([]);
  const [falhas, setFalhas] = useState<FalhaProjeto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined); setReports([]);
    (async () => {
      const fs: FalhaProjeto[] = [];
      const partes = await limitedMap(selection, async p => {
        try { return { project: p, report: await getJson<Avanco4DReport>(`/api/reports/avanco-4d?${new URLSearchParams({ hubId: p.hubId, projectId: p.id })}`, abort.signal) }; }
        catch (e) { if (abort.signal.aborted) throw e; fs.push({ projectId: p.id, projectName: p.name, message: e instanceof ApiError ? e.message : 'Falha ao consultar o projeto.' }); return null; }
      });
      if (abort.signal.aborted) return;
      setFalhas(fs);
      const ok = partes.filter((x): x is NonNullable<typeof x> => x !== null);
      if (ok.length === 0) { setError(fs[0]?.message ?? 'Não foi possível carregar o Avanço 4D.'); return; }
      setReports(ok);
    })().catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar o Avanço 4D.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [selectionKey, attempt]);

  if (loading) return <div className="p-12 text-slate-400">Carregando Avanço 4D...</div>;
  if (error) return <div role="alert" className="p-12 text-rose-400">{error} <button className="underline" onClick={() => setAttempt(a => a + 1)}>Tentar novamente</button></div>;
  if (reports.length === 1 && selection.length === 1) return <Avanco4DProjeto report={reports[0].report} />;
  return (
    <div className="space-y-6 animate-enter">
      <ConsolidadoBanner selection={selection} falhas={falhas} />
      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid="4d-por-projeto">
        <h3 className="font-semibold text-slate-100 text-sm mb-1">Visão consolidada por projeto</h3>
        <p className="text-[11px] text-slate-400 mb-3">Avanço % não é aditivo e não existe ponderação canônica entre projetos; por isso não há total geral — cada projeto selecionado aparece com o seu próprio avanço (desvio em pontos percentuais = Realizado − Previsto).</p>
        <table className="w-full text-xs text-left">
          <thead className="bg-slate-900 uppercase text-slate-400"><tr>
            <th className="px-3 py-2 font-medium">Projeto</th><th className="px-3 py-2 font-medium">Tipo</th><th className="px-3 py-2 font-medium">Data de corte</th>
            <th className="px-3 py-2 font-medium text-right">Previsto</th><th className="px-3 py-2 font-medium text-right">Realizado</th><th className="px-3 py-2 font-medium text-right">Desvio</th>
            <th className="px-3 py-2 font-medium text-right">Cobertura</th><th className="px-3 py-2 font-medium">Situação</th></tr></thead>
          <tbody className="divide-y divide-slate-700/50">
            {reports.map(({ project, report }) => (
              <tr key={project.id} data-projeto={project.name}>
                <td className="px-3 py-2 font-bold text-amber-300">{project.name}</td>
                <td className="px-3 py-2 text-slate-300">Tipo {report.tipoLdp}</td>
                <td className="px-3 py-2 text-slate-300">{dataHora(report.dataCorte)}</td>
                <td className="px-3 py-2 text-right text-slate-300">{report.disponivel ? pct(report.previstoGeral) : '—'}</td>
                <td className="px-3 py-2 text-right text-sky-400 font-bold">{report.disponivel ? pct(report.realizadoGeral) : '—'}</td>
                <td className={`px-3 py-2 text-right font-bold ${corDesvio(report.disponivel ? report.desvioPp : null)}`}>{report.disponivel ? pp(report.desvioPp) : '—'}</td>
                <td className="px-3 py-2 text-right text-slate-400">{num(report.diagnostico.coberturaPct, 1)}%</td>
                <td className="px-3 py-2 text-slate-300">{report.disponivel ? 'Disponível' : 'Indisponível com as fontes atuais'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {reports.map(({ project, report }) => (
        <details key={project.id} className="rounded-xl border border-slate-700 bg-slate-900/40" open={reports.length <= 3} data-testid={`4d-projeto-${project.id}`}>
          <summary className="cursor-pointer px-4 py-3 font-bold text-amber-300 text-sm">{project.name} — detalhe do Avanço 4D</summary>
          <div className="p-4"><Avanco4DProjeto report={report} /></div>
        </details>
      ))}
    </div>
  );
}
