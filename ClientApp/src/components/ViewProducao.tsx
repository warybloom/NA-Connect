import { useEffect, useState } from "react";
import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer, Legend } from "recharts";
import { CheckCircle2, Activity, FileSearch, Box } from "lucide-react";

export default function ViewProducao({ projetoCodigo }: { projetoCodigo: string }) {
  const [data, setData] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  // Paleta de cores moderna para os gráficos
  const CORES_INTERNAS = ['#0ea5e9', '#8b5cf6', '#6366f1', '#ec4899', '#f43f5e', '#64748b'];
  const CORES_CLIENTE = ['#10b981', '#f59e0b', '#f97316', '#ef4444', '#84cc16', '#64748b'];

  useEffect(() => {
    const fetchProducao = async () => {
      try {
        setLoading(true);
        const response = await fetch(`http://localhost:5277/api/TimesheetReport/producao?projetoCodigo=${projetoCodigo}`);
        const result = await response.json();
        if (result.sucesso) setData(result.dados);
      } catch (error) {
        console.error("Erro ao buscar Produção:", error);
      } finally {
        setLoading(false);
      }
    };
    fetchProducao();
  }, [projetoCodigo]);

  if (loading || !data) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Mapeando Produção e Status...</div>;
  }

  return (
    <div className="space-y-6 animate-enter">
      
      {/* KPIs DE PRODUÇÃO */}
      <div className="grid gap-4 md:grid-cols-4">
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-slate-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-400 font-bold mb-1"><Box className="w-4 h-4" /> Escopo Total</div>
          <div className="text-3xl font-black">{data.kpis.TotalDocumentos} docs</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-emerald-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-400 font-bold mb-1"><CheckCircle2 className="w-4 h-4" /> Concluídos (100%)</div>
          <div className="text-3xl font-black text-emerald-400">{data.kpis.TotalConcluidos} docs</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-sky-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-sky-400 font-bold mb-1"><Activity className="w-4 h-4" /> Em Andamento</div>
          <div className="text-3xl font-black text-sky-400">{data.kpis.TotalEmAndamento} docs</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 border-l-4 border-l-slate-700">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-500 font-bold mb-1"><FileSearch className="w-4 h-4" /> Não Iniciados (0%)</div>
          <div className="text-3xl font-black text-slate-500">{data.kpis.TotalNaoIniciados} docs</div>
        </div>
      </div>

      {/* GRÁFICOS DE ROSCA LADO A LADO */}
      <div className="grid gap-6 md:grid-cols-2">
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-4">
            <Activity className="w-4 h-4 text-sky-400"/> Mapa de Calor: Fluxo Interno (N&A)
          </h3>
          <div className="h-[300px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={data.statusInterno} cx="50%" cy="50%" innerRadius={70} outerRadius={110} paddingAngle={2} dataKey="value">
                  {data.statusInterno.map((entry: any, index: number) => (
                    <Cell key={`cell-${index}`} fill={CORES_INTERNAS[index % CORES_INTERNAS.length]} />
                  ))}
                </Pie>
                <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} itemStyle={{ color: '#f8fafc' }} />
                <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: '#cbd5e1' }} />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>

        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-4">
            <CheckCircle2 className="w-4 h-4 text-emerald-400"/> Mapa de Calor: Status do Cliente
          </h3>
          <div className="h-[300px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={data.statusCliente} cx="50%" cy="50%" innerRadius={70} outerRadius={110} paddingAngle={2} dataKey="value">
                  {data.statusCliente.map((entry: any, index: number) => (
                    <Cell key={`cell-${index}`} fill={CORES_CLIENTE[index % CORES_CLIENTE.length]} />
                  ))}
                </Pie>
                <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} itemStyle={{ color: '#f8fafc' }} />
                <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: '#cbd5e1' }} />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* TABELA DETALHADA COM BARRA DE PROGRESSO */}
      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col h-[400px]">
        <div className="p-4 border-b border-slate-700 flex items-center justify-between">
          <h3 className="font-semibold text-slate-100 text-sm">Raio-X de Entregáveis</h3>
          <span className="text-xs text-slate-400">Exibindo última versão da LDP</span>
        </div>
        <div className="p-0 overflow-auto custom-scrollbar flex-1">
          <table className="w-full text-xs text-left">
            <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
              <tr>
                <th className="px-4 py-3 font-medium">Documento</th>
                <th className="px-4 py-3 font-medium">Disciplina</th>
                <th className="px-4 py-3 font-medium">Fluxo Interno</th>
                <th className="px-4 py-3 font-medium">Status Cliente</th>
                <th className="px-4 py-3 font-medium min-w-[150px]">Avanço Físico</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/50">
              {data.tabela.map((doc: any, idx: number) => (
                <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                  <td className="px-4 py-3">
                    <div className="font-medium truncate max-w-[250px] text-slate-200" title={doc.Titulo}>{doc.Titulo}</div>
                    <div className="text-[10px] text-slate-500">{doc.Codigo}</div>
                  </td>
                  <td className="px-4 py-3 text-slate-300">{doc.Disciplina}</td>
                  <td className="px-4 py-3">
                    <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-sky-400 text-[10px] whitespace-nowrap">{doc.StatusInterno}</span>
                  </td>
                  <td className="px-4 py-3">
                    <span className="px-2 py-1 rounded bg-slate-900 border border-slate-700 text-emerald-400 text-[10px] whitespace-nowrap">{doc.StatusCliente}</span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <div className="w-full bg-slate-900 rounded-full h-2.5 border border-slate-700 overflow-hidden">
                        <div 
                          className={`h-2.5 rounded-full ${doc.AvancoFisico === 100 ? 'bg-emerald-500' : 'bg-sky-500'}`} 
                          style={{ width: `${doc.AvancoFisico}%` }}
                        ></div>
                      </div>
                      <span className="text-[10px] font-bold text-slate-300 w-8">{doc.AvancoFisico}%</span>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}