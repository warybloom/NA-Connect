import { useEffect, useState } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { Wallet, Clock, AlertCircle, FileText, Layers, X, Filter } from "lucide-react";
import PendenteBackend from "./PendenteBackend";
import { financeiroCanonico } from './pmoFinanceiro';
import ConsolidadoBanner from './ConsolidadoBanner';
import HistoricalMeasurementPanel from './HistoricalMeasurementPanel';
import type { FactsReport } from './pmoFacts';
import { sumSkipNull, PmoProps, Periodo, usePmoReports, formatHH, formatCurrency, formatData, toggleInSet, rotuloDe } from './pmoData';

export default function ViewTimesheet(props: PmoProps) {
  const { reports, falhas, loading, error, retry, selectionKey } = usePmoReports('gestao-vista', props);
  const [filtroSemana, setFiltroSemana] = useState<Set<string>>(new Set());
  const [filtroDisciplina, setFiltroDisciplina] = useState<Set<string>>(new Set());
  const [filtroDocumento, setFiltroDocumento] = useState<Set<string>>(new Set());
  const [periodo, setPeriodo] = useState<Periodo>('semanal');
  useEffect(() => { setFiltroSemana(new Set()); setFiltroDisciplina(new Set()); setFiltroDocumento(new Set()); }, [selectionKey]);
  const result = financeiroCanonico(reports as FactsReport[], { semanas: filtroSemana, disciplinas: filtroDisciplina, documentos: filtroDocumento }, periodo);
  const { cards, disciplinasTabela, documentosTabela, grafico } = result;
  const filtroSemanas = result.weeks.map(semana => ({ semana }));
  const comparativoDisciplina = disciplinasTabela.map(d => ({ Disciplina: d.Disciplina, HH_Previsto: d.HH_Previsto ?? 0, HH_Realizado: d.HH_Realizado ?? 0,
    Custo_Previsto: d.Custo_Previsto ?? 0, Custo_Realizado: d.Custo_Realizado ?? 0 }));
  const somaGrafico = { hhPrev: sumSkipNull(disciplinasTabela.map(d => d.HH_Previsto)), hhReal: sumSkipNull(disciplinasTabela.map(d => d.HH_Realizado)),
    custoPrev: sumSkipNull(disciplinasTabela.map(d => d.Custo_Previsto)), custoReal: sumSkipNull(disciplinasTabela.map(d => d.Custo_Realizado)) };
  const dif = (a: number | null, b: number | null) => a === null || b === null ? null : Math.round((a - b) * 100) / 100;
  const totalFiltrosAtivos = filtroSemana.size + filtroDisciplina.size + filtroDocumento.size;
  const limparFiltros = () => { setFiltroSemana(new Set()); setFiltroDisciplina(new Set()); setFiltroDocumento(new Set()); };
  if (loading) return <div className="p-12 text-slate-400">Carregando Financeiro/Horas...</div>;
  if (error) return <div role="alert" className="p-12 text-rose-400">{error} <button onClick={retry}>Tentar novamente</button></div>;
  return (
    <div className="space-y-4 animate-enter">
      <ConsolidadoBanner selection={props.selection ?? []} falhas={falhas} />
      <HistoricalMeasurementPanel reports={reports as FactsReport[]} />
      <div className="rounded-lg border border-slate-700 bg-slate-900 p-3 text-xs text-slate-400 space-y-2">
        <p>Mesma fonte da Gestão à Vista. HH previsto: Planned_HH da última carga da LDP; custo previsto: Planned_Cost da última carga da LDP; realizado: Timesheet atual (HH e Custo Calculado) pela data do lançamento. Séries semanais/mensais posicionam o previsto na semana LDP do documento. Mês agrupa semanas pelo seu início.</p>
        <p>{result.semData} lançamentos sem data não entram nas séries. {result.semCusto} lançamentos do filtro sem custo calculado.
          {result.semCusto > 0 && <> Custo conhecido (parcial): {formatCurrency(result.custoConhecido)}.</>}</p>
        <div className="flex gap-2" aria-label="Granularidade financeira">
          {(['diario', 'semanal', 'mensal'] as const).map(p => <button key={p} onClick={() => setPeriodo(p)} aria-pressed={periodo === p}
            className={`rounded px-3 py-2 ${periodo === p ? 'bg-sky-600 text-white' : 'bg-slate-800'}`}>{p === 'diario' ? 'Diário' : p === 'semanal' ? 'Semanal' : 'Mensal'}</button>)}
        </div>
        {periodo === 'diario' && <p>Planejado diário indisponível: não há distribuição diária comprovada. Use Semanal para comparar o planejamento documental.</p>}
      </div>


      <div className="flex flex-col gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-2 text-sm font-semibold text-slate-300">
            <Filter className="w-4 h-4 text-sky-400" /> Filtros Ativos:
          </div>
          {totalFiltrosAtivos === 0 && (
            <span className="text-sm text-slate-500 italic">Nenhum. Exibindo projeto completo.</span>
          )}
          {[...filtroSemana].map(v => (
            <div key={`s-${v}`} className="flex items-center gap-2 bg-sky-900/50 border border-sky-500/50 text-sky-300 px-3 py-1 rounded-full text-xs font-medium">
              Semana LDP: {formatData(v)}
              <button onClick={() => setFiltroSemana(toggleInSet(filtroSemana, v))} title="Remover filtro de Semana" className="hover:text-foreground"><X className="w-3 h-3" /></button>
            </div>
          ))}
          {[...filtroDisciplina].map(v => (
            <div key={`d-${v}`} className="flex items-center gap-2 bg-emerald-900/50 border border-emerald-500/50 text-emerald-300 px-3 py-1 rounded-full text-xs font-medium">
              Disc: {rotuloDe(v)}
              <button onClick={() => setFiltroDisciplina(toggleInSet(filtroDisciplina, v))} title="Remover filtro de Disciplina" className="hover:text-foreground"><X className="w-3 h-3" /></button>
            </div>
          ))}
          {[...filtroDocumento].map(v => (
            <div key={`doc-${v}`} className="flex items-center gap-2 bg-purple-900/50 border border-purple-500/50 text-purple-300 px-3 py-1 rounded-full text-xs font-medium">
              Doc: {documentosTabela.find(d => d.rowId === v)?.codigo ?? 'Selecionado'}
              <button onClick={() => setFiltroDocumento(toggleInSet(filtroDocumento, v))} title="Remover filtro de Documento" className="hover:text-foreground"><X className="w-3 h-3" /></button>
            </div>
          ))}
          {totalFiltrosAtivos > 0 && (
            <button className="ml-auto text-[10px] uppercase font-bold text-rose-400 hover:text-rose-300 underline" onClick={limparFiltros}>Limpar Filtros</button>
          )}
        </div>

        {filtroSemanas.length > 0 && (
          <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2 pt-1">
            {filtroSemanas.map((s) => (
              <button key={s.semana} title={`Filtrar pela semana ${s.semana}`}
                onClick={() => setFiltroSemana(toggleInSet(filtroSemana, s.semana))}
                className={`whitespace-nowrap px-4 py-1.5 text-xs font-semibold rounded-md border transition-all duration-200 ${filtroSemana.has(s.semana) ? "bg-sky-500 border-sky-400 text-white shadow-[0_0_10px_rgba(14,165,233,0.3)]" : "bg-slate-900 border-slate-700 text-slate-400 hover:bg-slate-700 hover:text-slate-200"}`}>
                {formatData(s.semana)}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="grid gap-4 md:grid-cols-4 lg:grid-cols-7">
        <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-4" title="HH previsto = SUM(Planned_HH) da última carga da LDP (mesma regra da Gestão à Vista)">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-400 font-bold mb-1"><Clock className="w-3 h-3" /> HH Previsto</div>
          <div className="text-xl font-bold text-slate-200">{formatHH(cards.hh_previsto)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-sky-400 font-bold mb-1"><Clock className="w-3 h-3" /> HH Realizado</div>
          <div className="text-xl font-bold text-sky-400">{formatHH(cards.hh_realizado)}</div>
        </div>
        <div className="rounded-xl border bg-card text-card-foreground shadow p-4" title="HH previsto menos HH realizado">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-600 font-bold mb-1"><AlertCircle className="w-3 h-3" /> Saldo HH</div>
          <div className="text-xl font-bold text-emerald-600">{formatHH(cards.hh_saldo)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-l-4 border-l-amber-500" title="Maior diferença em dias corridos: término atual menos término baseline, nas tarefas filtradas, sem resumos">
          <div className="flex items-center gap-2 text-[10px] uppercase text-amber-400 font-bold mb-1">Maior Desvio de Prazo</div>
          <div className="text-xl font-bold text-amber-400">{cards.desvio_dias === null ? "—" : `${cards.desvio_dias} dias`}</div><span className="text-[10px] text-slate-400">{cards.tarefasComDesvio} tarefas com desvio positivo</span>
        </div>
        <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-4" title="Planejado informado na LDP, por documento único">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-400 font-bold mb-1"><Wallet className="w-3 h-3" /> Custo Prev</div>
          <div className="text-lg font-bold text-slate-200">{formatCurrency(cards.custo_previsto)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-400 font-bold mb-1"><Wallet className="w-3 h-3" /> Custo Real</div>
          <div className="text-lg font-bold text-emerald-400">{formatCurrency(cards.custo_realizado)}</div>
        </div>
        <div className="rounded-xl border bg-card text-card-foreground shadow p-4" title="Custo previsto menos custo realizado; ausência de custo mantém saldo indisponível">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-600 font-bold mb-1"><AlertCircle className="w-3 h-3" /> Saldo Custo</div>
          <div className="text-lg font-bold text-emerald-600">{formatCurrency(cards.custo_saldo)}</div>
        </div>
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 text-slate-200 shadow flex flex-col min-w-0 h-[350px]">
          <div className="p-3 border-b border-slate-700 flex items-center gap-2 font-semibold text-slate-100 text-sm">
            <Layers className="w-4 h-4 text-sky-400" /> HH e Custo por Disciplina <span className="text-xs text-slate-500 ml-auto font-normal">(Clique para filtrar)</span>
          </div>
          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            {disciplinasTabela.length === 0 ? (
              <div className="flex items-center justify-center h-full"><PendenteBackend label="Sem lançamentos para os filtros atuais" /></div>
            ) : (
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
                  <tr><th className="px-3 py-2 font-medium">Disciplina</th><th className="px-3 py-2 font-medium text-right">HH Prev</th><th className="px-3 py-2 font-medium text-right">HH Real</th><th className="px-3 py-2 font-medium text-right">Custo Prev</th><th className="px-3 py-2 font-medium text-right">Custo Real</th></tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {disciplinasTabela.map((d) => (
                    <tr key={d.chave} onClick={() => setFiltroDisciplina(toggleInSet(filtroDisciplina, d.chave))}
                      className={`cursor-pointer transition-colors ${filtroDisciplina.has(d.chave) ? 'bg-sky-900/40 border-l-2 border-sky-400' : 'hover:bg-slate-700/50'}`}>
                      <td className="px-3 py-2 font-medium">{d.Disciplina}</td>
                      <td className="px-3 py-2 text-right text-slate-500">{formatHH(d.HH_Previsto)}</td>
                      <td className="px-3 py-2 text-right text-sky-400 font-bold">{formatHH(d.HH_Realizado)}</td>
                      <td className="px-3 py-2 text-right text-slate-500">{formatCurrency(d.Custo_Previsto)}</td>
                      <td className="px-3 py-2 text-right text-emerald-400 font-bold">{formatCurrency(d.Custo_Realizado)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        <div className="rounded-xl border border-slate-700 bg-slate-800/50 text-slate-200 shadow flex flex-col min-w-0 h-[350px]">
          <div className="p-3 border-b border-slate-700 flex items-center gap-2 font-semibold text-slate-100 text-sm">
            <FileText className="w-4 h-4 text-sky-400" /> Detalhamento por Documento <span className="text-xs text-slate-500 ml-auto font-normal">(Clique para filtrar)</span>
          </div>
          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            {documentosTabela.length === 0 ? (
              <div className="flex items-center justify-center h-full"><PendenteBackend label="Sem documentos para os filtros atuais" /></div>
            ) : (
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
                  <tr>{reports.length > 1 && <th className="px-3 py-2 font-medium">Projeto</th>}<th className="px-3 py-2 font-medium">Documento</th><th className="px-3 py-2 font-medium text-right">HH Prev</th><th className="px-3 py-2 font-medium text-right">HH Real</th><th className="px-3 py-2 font-medium text-right">Custo Prev</th><th className="px-3 py-2 font-medium text-right">Custo Real</th></tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {documentosTabela.map((doc) => {
                    const chaveDoc = doc.rowId;
                    return (
                      <tr key={doc.rowId} onClick={() => setFiltroDocumento(toggleInSet(filtroDocumento, chaveDoc))}
                        className={`cursor-pointer transition-colors ${filtroDocumento.has(chaveDoc) ? 'bg-purple-900/40 border-l-2 border-purple-400' : 'hover:bg-slate-700/50'}`}>
                        {reports.length > 1 && <td className="px-3 py-2 text-amber-300 font-bold text-[11px] whitespace-nowrap" data-testid="col-projeto">{doc.nomeProjeto}</td>}
                        <td className="px-3 py-2"><div className="font-medium truncate max-w-[200px]" title={doc.documento ?? undefined}>{doc.documento ?? '—'}</div><div className="text-[9px] text-slate-500">{doc.codigo ?? 'Sem código'}{reports.length > 1 && <span className="block text-amber-300">{doc.nomeProjeto}</span>}</div></td>
                        <td className="px-3 py-2 text-right text-slate-500">{formatHH(doc.HH_Previsto)}</td>
                        <td className="px-3 py-2 text-right text-sky-400 font-bold">{doc.HH_Realizado === null ? <span className="text-amber-400 italic font-normal">Sem dado</span> : formatHH(doc.HH_Realizado)}</td>
                        <td className="px-3 py-2 text-right text-slate-500">{formatCurrency(doc.Custo_Previsto)}</td>
                        <td className="px-3 py-2 text-right text-emerald-400 font-bold">{doc.Custo_Realizado === null ? <span className="text-amber-400 italic font-normal">Sem dado</span> : formatCurrency(doc.Custo_Realizado)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>

      <div className="space-y-2" data-testid="comparativo-disciplina">
        <div className="grid gap-6 md:grid-cols-2">
          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid="comparativo-hh">
            <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm"><Clock className="w-4 h-4 text-sky-400" /> HH Previsto × Realizado por Disciplina</h3>
            <div className="h-[320px] w-full">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={comparativoDisciplina} margin={{ top: 10, right: 10, left: -10, bottom: 40 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                  <XAxis dataKey="Disciplina" interval={0} angle={-35} textAnchor="end" height={80} axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 9 }} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} />
                  <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }} itemStyle={{ color: '#e2e8f0' }} formatter={(v: number) => `${formatHH(v)} h`} />
                  <Legend wrapperStyle={{ paddingTop: '4px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                  <Bar dataKey="HH_Previsto" name="HH Previsto" fill="#64748b" radius={[3, 3, 0, 0]} maxBarSize={28} isAnimationActive={false} />
                  <Bar dataKey="HH_Realizado" name="HH Realizado" fill="#0ea5e9" radius={[3, 3, 0, 0]} maxBarSize={28} isAnimationActive={false} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4" data-testid="comparativo-custo">
            <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm"><Wallet className="w-4 h-4 text-emerald-400" /> Custo Previsto × Realizado por Disciplina</h3>
            <div className="h-[320px] w-full">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={comparativoDisciplina} margin={{ top: 10, right: 10, left: 0, bottom: 40 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                  <XAxis dataKey="Disciplina" interval={0} angle={-35} textAnchor="end" height={80} axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 9 }} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} tickFormatter={(val) => `R$${(val / 1000).toFixed(0)}k`} width={60} />
                  <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }} itemStyle={{ color: '#e2e8f0' }} formatter={(v: number) => formatCurrency(v)} />
                  <Legend wrapperStyle={{ paddingTop: '4px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                  <Bar dataKey="Custo_Previsto" name="Custo Previsto" fill="#94a3b8" radius={[3, 3, 0, 0]} maxBarSize={28} isAnimationActive={false} />
                  <Bar dataKey="Custo_Realizado" name="Custo Realizado" fill="#10b981" radius={[3, 3, 0, 0]} maxBarSize={28} isAnimationActive={false} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
        <p className="text-[11px] text-slate-500 px-1" data-testid="reconciliacao-comparativo">
          Reconciliação: soma das disciplinas do gráfico — HH previsto {formatHH(somaGrafico.hhPrev)} · HH realizado {formatHH(somaGrafico.hhReal)} · custo previsto {formatCurrency(somaGrafico.custoPrev)} · custo realizado {formatCurrency(somaGrafico.custoReal)}.
          Cards: HH {formatHH(cards.hh_previsto)} / {formatHH(cards.hh_realizado)} · custo {formatCurrency(cards.custo_previsto)} / {formatCurrency(cards.custo_realizado)}.
          Diferença: HH {formatHH(dif(somaGrafico.hhPrev, cards.hh_previsto))} / {formatHH(dif(somaGrafico.hhReal, cards.hh_realizado))} · custo {formatCurrency(dif(somaGrafico.custoPrev, cards.custo_previsto))} / {formatCurrency(dif(somaGrafico.custoReal, cards.custo_realizado))}.
        </p>
      </div>

      <div className="rounded-lg border border-amber-500/40 bg-amber-950/20 p-3 text-xs text-amber-200" data-testid="aviso-series-temporais">Séries por período abaixo: o <b>Previsto</b> é posicionado na semana LDP de cada documento. O <b>Realizado</b> do Timesheet não possui data de trabalho na fonte ({result.semData} lançamentos sem data), portanto não pode ser comparado por período; a comparação Previsto × Realizado completa é a por disciplina acima e a Curva S documental.</div>

      <div className="grid gap-6 md:grid-cols-2">
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm"><Clock className="w-4 h-4 text-sky-400" /> Esforço — {periodo === 'diario' ? 'Diário' : periodo === 'semanal' ? 'Semanal' : 'Mensal'} (HH)</h3>
          <div className="h-[280px] w-full">
            {grafico.length === 0 ? <div className="flex items-center justify-center h-full"><PendenteBackend label="Sem lançamentos datados" /></div> : (
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={grafico} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                  <XAxis dataKey="DataFormatada" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} dy={10} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} />
                  <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }} itemStyle={{ color: '#e2e8f0' }} />
                  <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                  <Bar dataKey="HH_Previsto" name="HH Previsto" fill="#64748b" radius={[3, 3, 0, 0]} maxBarSize={40} />
                  <Bar dataKey="HH_Realizado" name="HH Realizado" fill="#0ea5e9" radius={[3, 3, 0, 0]} maxBarSize={40} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>
        </div>

        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm"><Wallet className="w-4 h-4 text-emerald-400" /> Custo de mão de obra — {periodo === 'diario' ? 'Diário' : periodo === 'semanal' ? 'Semanal' : 'Mensal'} (R$)</h3>
          <div className="h-[280px] w-full">
            {grafico.length === 0 ? <div className="flex items-center justify-center h-full"><PendenteBackend label="Sem lançamentos datados" /></div> : (
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={grafico} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                  <XAxis dataKey="DataFormatada" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} dy={10} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--chart-axis))', fontSize: 10 }} tickFormatter={(val) => `R$${(val / 1000).toFixed(0)}k`} width={60} />
                  <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))' }} itemStyle={{ color: '#e2e8f0' }} formatter={(value: number) => formatCurrency(value)} />
                  <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: 'hsl(var(--foreground))' }} />
                  <Bar dataKey="Custo_Previsto" name="Custo Previsto" fill="#94a3b8" radius={[3, 3, 0, 0]} maxBarSize={40} />
                  <Bar dataKey="Custo_Realizado" name="Custo Realizado" fill="#10b981" radius={[3, 3, 0, 0]} maxBarSize={40} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
