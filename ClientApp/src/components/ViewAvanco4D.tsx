import { useEffect, useState } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { CalendarClock, AlertTriangle, CheckCircle2, ListTree } from "lucide-react";

export default function ViewAvanco4D({ projetoCodigo }: { projetoCodigo: string }) {
  const [data, setData] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchAvanco = async () => {
      try {
        setLoading(true);
        const response = await fetch(`http://localhost:5277/api/TimesheetReport/avanco-4d?projetoCodigo=${projetoCodigo}`);
        const result = await response.json();
        if (result.sucesso) setData(result.dados);
      } catch (error) {
        console.error("Erro ao buscar Avanço 4D:", error);
      } finally {
        setLoading(false);
      }
    };
    fetchAvanco();
  }, [projetoCodigo]);

  if (loading || !data) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Sincronizando Cronograma e Avanço Físico...</div>;
  }

  // Pega apenas as 15 primeiras tarefas com maior peso/avanço para não poluir o gráfico
  const chartData = data.cronograma.slice(0, 15);

  return (
    <div className="space-y-6 animate-enter">
      
      {/* KPIs DE CRONOGRAMA */}
      <div className="grid gap-4 md:grid-cols-4">
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500 flex flex-col justify-center">
          <div className="flex items-center gap-2 text-[10px] uppercase text-sky-400 font-bold mb-1">
            <ListTree className="w-4 h-4" /> Total de Tarefas
          </div>
          <div className="text-3xl font-black text-slate-100">{data.kpis.TotalTarefas}</div>
        </div>

        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-emerald-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-400 font-bold mb-1">
            <CheckCircle2 className="w-4 h-4" /> No Prazo / Adiantadas
          </div>
          <div className="text-3xl font-black text-emerald-400">{data.kpis.TarefasNoPrazo} <span className="text-sm font-normal text-slate-400">tarefas</span></div>
        </div>

        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-rose-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-rose-400 font-bold mb-1">
            <AlertTriangle className="w-4 h-4" /> Tarefas Atrasadas
          </div>
          <div className="text-3xl font-black text-rose-400">{data.kpis.TarefasAtrasadas} <span className="text-sm font-normal text-slate-400">tarefas</span></div>
        </div>

        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-amber-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-amber-400 font-bold mb-1">
            <CalendarClock className="w-4 h-4" /> Avanço Médio Geral
          </div>
          <div className="flex items-end gap-2">
            <div className="text-3xl font-black text-amber-400">{data.kpis.Media_Realizado.toFixed(1)}%</div>
            <div className="text-sm font-medium text-slate-400 mb-1">vs {data.kpis.Media_Previsto.toFixed(1)}% Prev.</div>
          </div>
        </div>
      </div>

      {/* GRÁFICO COMPARATIVO (Top Tarefas) */}
      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
        <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-4">
          <CalendarClock className="w-4 h-4 text-sky-400"/> Planejado vs Realizado (% Físico) - Principais Tarefas
        </h3>
        <div className="h-[300px] w-full">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
              <XAxis dataKey="EDT_WBS" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} dy={10} />
              <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} tickFormatter={(val) => `${val}%`} />
              <Tooltip 
                cursor={{ fill: 'rgba(255,255,255,0.05)' }} 
                contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#f8fafc' }} 
                itemStyle={{ color: '#e2e8f0' }}
                labelFormatter={(label) => `WBS: ${label}`}
              />
              <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: '#f8fafc' }}/>
              <Bar dataKey="Previsto" name="% Previsto Físico" fill="#64748b" radius={[3, 3, 0, 0]} maxBarSize={40} />
              <Bar dataKey="Realizado" name="% Realizado Físico" fill="#0ea5e9" radius={[3, 3, 0, 0]} maxBarSize={40} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* TABELA DE DESVIOS DO CRONOGRAMA */}
      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col h-[400px]">
        <div className="p-4 border-b border-slate-700 flex items-center justify-between">
          <h3 className="font-semibold text-slate-100 text-sm">Detalhamento da Estrutura Analítica (WBS)</h3>
          <span className="text-xs text-slate-400">Comparação de Progresso 4D</span>
        </div>
        <div className="p-0 overflow-auto custom-scrollbar flex-1">
          <table className="w-full text-xs text-left">
            <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
              <tr>
                <th className="px-4 py-3 font-medium w-24">WBS</th>
                <th className="px-4 py-3 font-medium">Nome da Tarefa</th>
                <th className="px-4 py-3 font-medium">INÍCIO</th>
                <th className="px-4 py-3 font-medium">TÉRMINO TENDÊNCIA</th>
                <th className="px-4 py-3 font-medium">LINHA DE BASE</th>
                <th className="px-4 py-3 font-medium text-right">% Previsto</th>
                <th className="px-4 py-3 font-medium text-right">% Realizado</th>
                <th className="px-4 py-3 font-medium text-center">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/50">
              {data.cronograma.map((task: any, idx: number) => {
                const isAtrasado = task.Realizado < task.Previsto;
                
                return (
                  <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                    <td className="px-4 py-3 font-semibold text-sky-400">{task.EDT_WBS}</td>
                    <td className="px-4 py-3">
                      <div className="font-medium truncate max-w-[300px] text-slate-200" title={task.Tarefa}>{task.Tarefa}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-400">{task.DataInicio}</td>
                    <td className="px-4 py-3 text-amber-400 font-bold">{task.DataTendencia || '-'}</td>
                    <td className="px-4 py-3 text-slate-400">{task.DataFim}</td>
                    <td className="px-4 py-3 text-right text-slate-300 font-medium">{task.Previsto}%</td>
                    <td className={`px-4 py-3 text-right font-bold ${isAtrasado ? 'text-rose-400' : 'text-emerald-400'}`}>
                      {task.Realizado}%
                    </td>
                    <td className="px-4 py-3 text-center">
                      {isAtrasado ? (
                        <span className="inline-flex items-center gap-1 px-2 py-1 rounded bg-rose-900/30 border border-rose-500/30 text-rose-400 text-[10px]">
                          <AlertTriangle className="w-3 h-3" /> Atrasada
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 px-2 py-1 rounded bg-emerald-900/30 border border-emerald-500/30 text-emerald-400 text-[10px]">
                          <CheckCircle2 className="w-3 h-3" /> No Prazo
                        </span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

    </div>
  );
}