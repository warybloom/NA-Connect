import { useEffect, useState } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { Wallet, Clock, AlertCircle, FileText, Layers, X, Filter } from "lucide-react";

export default function ViewTimesheet({ projetoCodigo }: { projetoCodigo: string }) {
  const [data, setData] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  
  // ESTADOS DE FILTRO
  const [filtroSemana, setFiltroSemana] = useState<string | null>(null);
  const [filtroDisciplina, setFiltroDisciplina] = useState<string | null>(null);
  const [filtroDocumento, setFiltroDocumento] = useState<string | null>(null);

  useEffect(() => {
    const fetchData = async () => {
      try {
        setLoading(true);
        let url = `http://localhost:5277/api/TimesheetReport/dashboard?projetoCodigo=${encodeURIComponent(projetoCodigo)}`;
        if (filtroSemana) url += `&semana=${encodeURIComponent(filtroSemana)}`;
        if (filtroDisciplina) url += `&disciplina=${encodeURIComponent(filtroDisciplina)}`;
        if (filtroDocumento) url += `&documento=${encodeURIComponent(filtroDocumento)}`;

        const response = await fetch(url);
        const result = await response.json();
        
        if (result.sucesso) {
          setData(result.dados);
        }
      } catch (error) {
        console.error("Erro ao buscar dados do Timesheet:", error);
      } finally {
        setLoading(false);
      }
    };

    fetchData();
  }, [projetoCodigo, filtroSemana, filtroDisciplina, filtroDocumento]);

  const formatHH = (num: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(num);
  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(num);

  if (!data && loading) {
    return <div className="flex items-center justify-center h-96 text-muted-foreground">Carregando painel de Timesheet...</div>;
  }

  if (!data) return null;

  return (
    <div className="space-y-4">
      
      {/* BARRA DE FILTROS SUPERIOR */}
      <div className="flex flex-col gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-2 text-sm font-semibold text-slate-300">
            <Filter className="w-4 h-4 text-sky-400" /> Filtros Ativos:
          </div>
          
          {!filtroSemana && !filtroDisciplina && !filtroDocumento && (
            <span className="text-sm text-slate-500 italic">Nenhum. Exibindo projeto completo.</span>
          )}

          {filtroSemana && (
            <div className="flex items-center gap-2 bg-sky-900/50 border border-sky-500/50 text-sky-300 px-3 py-1 rounded-full text-xs font-medium">
              Semana: {filtroSemana}
              <button 
                onClick={() => setFiltroSemana(null)} 
                title="Remover filtro de Semana" 
                aria-label="Remover filtro de Semana"
                className="hover:text-white"
              >
                <X className="w-3 h-3" />
              </button>
            </div>
          )}
          {filtroDisciplina && (
            <div className="flex items-center gap-2 bg-emerald-900/50 border border-emerald-500/50 text-emerald-300 px-3 py-1 rounded-full text-xs font-medium">
              Disc: {filtroDisciplina}
              <button 
                onClick={() => setFiltroDisciplina(null)} 
                title="Remover filtro de Disciplina" 
                aria-label="Remover filtro de Disciplina"
                className="hover:text-white"
              >
                <X className="w-3 h-3" />
              </button>
            </div>
          )}
          {filtroDocumento && (
            <div className="flex items-center gap-2 bg-purple-900/50 border border-purple-500/50 text-purple-300 px-3 py-1 rounded-full text-xs font-medium">
              Doc: {filtroDocumento}
              <button 
                onClick={() => setFiltroDocumento(null)} 
                title="Remover filtro de Documento" 
                aria-label="Remover filtro de Documento"
                className="hover:text-white"
              >
                <X className="w-3 h-3" />
              </button>
            </div>
          )}
        </div>

        <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2 pt-1">
          {data.filtroSemanas.map((s: any, idx: number) => (
            <button
              key={idx}
              title={`Filtrar pela semana ${s.Semana}`}
              aria-label={`Filtrar pela semana ${s.Semana}`}
              onClick={() => setFiltroSemana(filtroSemana === s.Semana ? null : s.Semana)}
              className={`whitespace-nowrap px-4 py-1.5 text-xs font-semibold rounded-md border transition-all duration-200 ${
                filtroSemana === s.Semana 
                  ? "bg-sky-500 border-sky-400 text-white shadow-[0_0_10px_rgba(14,165,233,0.3)]" 
                  : "bg-slate-900 border-slate-700 text-slate-400 hover:bg-slate-700 hover:text-slate-200"
              }`}
            >
              {s.Semana}
            </button>
          ))}
        </div>
      </div>

      {/* CARDS SUPERIORES (KPIs) */}
      <div className={`grid gap-4 md:grid-cols-4 lg:grid-cols-7 transition-opacity duration-300 ${loading ? 'opacity-50' : 'opacity-100'}`}>
        <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-400 font-bold mb-1"><Clock className="w-3 h-3" /> HH Previsto</div>
          <div className="text-xl font-bold">{formatHH(data.cards.hh_previsto)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-sky-400 font-bold mb-1"><Clock className="w-3 h-3" /> HH Realizado</div>
          <div className="text-xl font-bold text-sky-400">{formatHH(data.cards.hh_realizado)}</div>
        </div>
        <div className="rounded-xl border bg-white text-slate-900 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-600 font-bold mb-1"><AlertCircle className="w-3 h-3" /> Saldo HH</div>
          <div className="text-xl font-bold text-emerald-600">{formatHH(data.cards.hh_saldo)}</div>
        </div>

        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-l-4 border-l-amber-500">
          <div className="flex items-center gap-2 text-[10px] uppercase text-amber-400 font-bold mb-1">
             Desvio de Prazo
          </div>
          <div className={`text-xl font-bold ${data.cards.desvio_dias >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
            {data.cards.desvio_dias > 0 ? '+' : ''}{data.cards.desvio_dias} dias
          </div>
        </div>

        <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-slate-400 font-bold mb-1"><Wallet className="w-3 h-3" /> Custo Prev</div>
          <div className="text-lg font-bold">{formatCurrency(data.cards.custo_previsto)}</div>
        </div>
        <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-400 font-bold mb-1"><Wallet className="w-3 h-3" /> Custo Real</div>
          <div className="text-lg font-bold text-emerald-400">{formatCurrency(data.cards.custo_realizado)}</div>
        </div>
        <div className="rounded-xl border bg-white text-slate-900 shadow p-4">
          <div className="flex items-center gap-2 text-[10px] uppercase text-emerald-600 font-bold mb-1"><AlertCircle className="w-3 h-3" /> Saldo Custo</div>
          <div className="text-lg font-bold text-emerald-600">{formatCurrency(data.cards.custo_saldo)}</div>
        </div>
      </div>

      {/* TABELAS INTERATIVAS */}
      <div className={`grid gap-6 xl:grid-cols-2 transition-opacity duration-300 ${loading ? 'opacity-50' : 'opacity-100'}`}>
        
        {/* Tabela de Disciplinas */}
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 text-slate-200 shadow flex flex-col h-[350px]">
          <div className="p-3 border-b border-slate-700 flex items-center gap-2 font-semibold text-slate-100 text-sm">
            <Layers className="w-4 h-4 text-sky-400" /> Avanço por Disciplina <span className="text-xs text-slate-500 ml-auto font-normal">(Clique para filtrar)</span>
          </div>
          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
                <tr>
                  <th className="px-3 py-2 font-medium">Disciplina</th>
                  <th className="px-3 py-2 font-medium text-right">HH Prev</th>
                  <th className="px-3 py-2 font-medium text-right">HH Real</th>
                  <th className="px-3 py-2 font-medium text-right">Custo Prev</th>
                  <th className="px-3 py-2 font-medium text-right">Custo Real</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-700/50">
                {data.tabelas.disciplinas.map((d: any, idx: number) => (
                  <tr 
                    key={idx} 
                    onClick={() => setFiltroDisciplina(filtroDisciplina === d.Disciplina ? null : d.Disciplina)}
                    className={`cursor-pointer transition-colors ${filtroDisciplina === d.Disciplina ? 'bg-sky-900/40 border-l-2 border-sky-400' : 'hover:bg-slate-700/50'}`}
                  >
                    <td className="px-3 py-2 font-medium">{d.Disciplina}</td>
                    <td className="px-3 py-2 text-right text-slate-300">{formatHH(d.HH_Previsto)}</td>
                    <td className="px-3 py-2 text-right text-sky-400 font-bold">{formatHH(d.HH_Realizado)}</td>
                    <td className="px-3 py-2 text-right text-slate-300">{formatCurrency(d.Custo_Previsto)}</td>
                    <td className="px-3 py-2 text-right text-emerald-400 font-bold">{formatCurrency(d.Custo_Realizado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Tabela de Documentos */}
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 text-slate-200 shadow flex flex-col h-[350px]">
          <div className="p-3 border-b border-slate-700 flex items-center gap-2 font-semibold text-slate-100 text-sm">
            <FileText className="w-4 h-4 text-sky-400" /> Detalhamento por Documento <span className="text-xs text-slate-500 ml-auto font-normal">(Clique para filtrar)</span>
          </div>
          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
                <tr>
                  <th className="px-3 py-2 font-medium">Documento</th>
                  <th className="px-3 py-2 font-medium text-right">HH Prev</th>
                  <th className="px-3 py-2 font-medium text-right">HH Real</th>
                  <th className="px-3 py-2 font-medium text-right">Custo Prev</th>
                  <th className="px-3 py-2 font-medium text-right">Custo Real</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-700/50">
                {data.tabelas.documentos.map((doc: any, idx: number) => (
                  <tr 
                    key={idx} 
                    onClick={() => setFiltroDocumento(filtroDocumento === doc.Codigo ? null : doc.Codigo)}
                    className={`cursor-pointer transition-colors ${filtroDocumento === doc.Codigo ? 'bg-purple-900/40 border-l-2 border-purple-400' : 'hover:bg-slate-700/50'}`}
                  >
                    <td className="px-3 py-2">
                      <div className="font-medium truncate max-w-[200px]" title={doc.Documento}>{doc.Documento}</div>
                      <div className="text-[9px] text-slate-500">{doc.Codigo}</div>
                    </td>
                    <td className="px-3 py-2 text-right text-slate-300">{formatHH(doc.HH_Previsto)}</td>
                    <td className="px-3 py-2 text-right text-sky-400 font-bold">{formatHH(doc.HH_Realizado)}</td>
                    <td className="px-3 py-2 text-right text-slate-300">{formatCurrency(doc.Custo_Previsto)}</td>
                    <td className="px-3 py-2 text-right text-emerald-400 font-bold">{formatCurrency(doc.Custo_Realizado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* GRÁFICOS DIÁRIOS LADO A LADO (HH e CUSTO) */}
      <div className={`grid gap-6 md:grid-cols-2 transition-opacity duration-300 ${loading ? 'opacity-50' : 'opacity-100'}`}>
        
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm">
            <Clock className="w-4 h-4 text-sky-400"/> Esforço Diário (HH)
          </h3>
          <div className="h-[280px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data.grafico} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                <XAxis dataKey="DataFormatada" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} dy={10} />
                <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} />
                <Tooltip cursor={{ fill: 'rgba(255,255,255,0.05)' }} contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#f8fafc' }} itemStyle={{ color: '#e2e8f0' }} />
                <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: '#f8fafc' }}/>
                <Bar dataKey="HH_Previsto" name="HH Previsto" fill="#64748b" radius={[3, 3, 0, 0]} maxBarSize={40} />
                <Bar dataKey="HH_Realizado" name="HH Realizado" fill="#0ea5e9" radius={[3, 3, 0, 0]} maxBarSize={40} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>

        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm">
            <Wallet className="w-4 h-4 text-emerald-400"/> Desembolso Diário (R$)
          </h3>
          <div className="h-[280px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data.grafico} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                <XAxis dataKey="DataFormatada" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} dy={10} />
                <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} tickFormatter={(val) => `R$${(val / 1000).toFixed(0)}k`} width={60} />
                <Tooltip 
                  cursor={{ fill: 'rgba(255,255,255,0.05)' }} 
                  contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#f8fafc' }} 
                  itemStyle={{ color: '#e2e8f0' }}
                  formatter={(value: number) => formatCurrency(value)}
                />
                <Legend wrapperStyle={{ paddingTop: '10px', fontSize: '12px', color: '#f8fafc' }}/>
                <Bar dataKey="Custo_Previsto" name="Custo Previsto" fill="#94a3b8" radius={[3, 3, 0, 0]} maxBarSize={40} />
                <Bar dataKey="Custo_Realizado" name="Custo Realizado" fill="#10b981" radius={[3, 3, 0, 0]} maxBarSize={40} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>

      </div>
    </div>
  );
}