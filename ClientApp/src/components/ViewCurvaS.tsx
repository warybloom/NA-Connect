import { useEffect, useState } from "react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TrendingUp, Clock, Wallet, CalendarDays, CalendarRange, Calendar } from "lucide-react";

export default function ViewCurvaS({ projetoCodigo }: { projetoCodigo: string }) {
  const [dataOriginal, setDataOriginal] = useState<any[]>([]);
  const [dataExibicao, setDataExibicao] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  
  // 🚀 NOVO ESTADO: Controle do filtro de tempo
  const [visualizacao, setVisualizacao] = useState<'diario' | 'semanal' | 'mensal'>('diario');

  useEffect(() => {
    const fetchCurvaS = async () => {
      try {
        setLoading(true);
        const response = await fetch(`http://localhost:5277/api/TimesheetReport/curva-s?projetoCodigo=${projetoCodigo}`);
        const result = await response.json();
        if (result.sucesso) {
          setDataOriginal(result.dados);
          setDataExibicao(result.dados); // Por padrão, exibe diário (original)
        }
      } catch (error) {
        console.error("Erro ao buscar Curva S:", error);
      } finally {
        setLoading(false);
      }
    };
    fetchCurvaS();
  }, [projetoCodigo]);

  // 🚀 INTELIGÊNCIA DE AGRUPAMENTO (MENSAL E SEMANAL)
  useEffect(() => {
    if (!dataOriginal || dataOriginal.length === 0) return;

    if (visualizacao === 'diario') {
      setDataExibicao(dataOriginal);
      return;
    }

    const mapAgrupado = new Map<string, any>();

    dataOriginal.forEach(item => {
      // O item.Data_Date costuma vir como "YYYY-MM-DDTHH:mm:ss"
      const dataObj = new Date(item.Data_Date);
      let chaveAgrupamento = "";

      if (visualizacao === 'mensal') {
        // Chave: "YYYY-MM"
        chaveAgrupamento = `${dataObj.getFullYear()}-${String(dataObj.getMonth() + 1).padStart(2, '0')}`;
      } else if (visualizacao === 'semanal') {
        // Lógica simples de semana do ano (ISO 8601)
        const date = new Date(dataObj.getTime());
        date.setHours(0, 0, 0, 0);
        date.setDate(date.getDate() + 3 - (date.getDay() + 6) % 7);
        const week1 = new Date(date.getFullYear(), 0, 4);
        const weekNum = 1 + Math.round(((date.getTime() - week1.getTime()) / 86400000 - 3 + (week1.getDay() + 6) % 7) / 7);
        chaveAgrupamento = `${date.getFullYear()}-W${String(weekNum).padStart(2, '0')}`;
      }

      // No gráfico Acumulado (Curva S), nós sempre queremos exibir o ÚLTIMO valor conhecido daquele período
      // Então, sobrescrevemos o valor no Map com o dado mais recente encontrado para aquela chave
      mapAgrupado.set(chaveAgrupamento, {
        ...item,
        DataFormatada: visualizacao === 'mensal' ? `${String(dataObj.getMonth() + 1).padStart(2, '0')}/${dataObj.getFullYear()}` : chaveAgrupamento
      });
    });

    // Converte o Map de volta para Array mantendo a ordem cronológica
    setDataExibicao(Array.from(mapAgrupado.values()));

  }, [visualizacao, dataOriginal]);

  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(num);
  const formatHH = (num: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(num);

  if (loading) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Calculando Curva S (EVM)...</div>;
  }

  return (
    <div className="space-y-6 animate-enter">
      
      {/* CABEÇALHO */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-4 rounded-xl border border-slate-700 bg-slate-800/80 shadow">
        <div className="flex items-center gap-3">
          <TrendingUp className="w-6 h-6 text-sky-400" />
          <div>
            <h2 className="text-lg font-bold text-slate-100">Curva S (Earned Value Management)</h2>
            <p className="text-xs text-slate-400">Acompanhamento acumulado do projeto {projetoCodigo}</p>
          </div>
        </div>

        {/* 🚀 NOVOS BOTÕES DE FILTRO DE TEMPO */}
        <div className="flex items-center gap-1 bg-slate-950 p-1 rounded-lg border border-slate-700">
          <button 
            onClick={() => setVisualizacao('diario')}
            className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-md transition-colors ${visualizacao === 'diario' ? 'bg-sky-500 text-white' : 'text-slate-400 hover:text-slate-200'}`}
          >
            <CalendarDays className="w-3.5 h-3.5" /> Diário
          </button>
          <button 
            onClick={() => setVisualizacao('semanal')}
            className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-md transition-colors ${visualizacao === 'semanal' ? 'bg-sky-500 text-white' : 'text-slate-400 hover:text-slate-200'}`}
          >
            <CalendarRange className="w-3.5 h-3.5" /> Semanal
          </button>
          <button 
            onClick={() => setVisualizacao('mensal')}
            className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-md transition-colors ${visualizacao === 'mensal' ? 'bg-sky-500 text-white' : 'text-slate-400 hover:text-slate-200'}`}
          >
            <Calendar className="w-3.5 h-3.5" /> Mensal
          </button>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* GRÁFICO 1: CURVA S DE HH */}
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm">
            <Clock className="w-4 h-4 text-sky-400"/> Curva S - Esforço (HH Acumulado)
          </h3>
          <div className="h-[400px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              {/* O data foi trocado de dataOriginal para dataExibicao */}
              <AreaChart data={dataExibicao} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <defs>
                  <linearGradient id="colorHHPrev" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#64748b" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#64748b" stopOpacity={0}/>
                  </linearGradient>
                  <linearGradient id="colorHHReal" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#0ea5e9" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#0ea5e9" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                <XAxis dataKey="DataFormatada" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} dy={10} />
                <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#f8fafc' }} 
                  formatter={(value: number) => formatHH(value)}
                />
                <Legend wrapperStyle={{ paddingTop: '20px', color: '#f8fafc' }}/>
                <Area type="monotone" dataKey="HH_Previsto_Acumulado" name="HH Previsto (Baseline)" stroke="#64748b" strokeWidth={3} fillOpacity={1} fill="url(#colorHHPrev)" />
                <Area type="monotone" dataKey="HH_Realizado_Acumulado" name="HH Realizado (Atual)" stroke="#0ea5e9" strokeWidth={3} fillOpacity={1} fill="url(#colorHHReal)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* GRÁFICO 2: CURVA S DE CUSTOS */}
        <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4">
          <h3 className="font-semibold mb-4 text-slate-100 flex items-center gap-2 text-sm">
            <Wallet className="w-4 h-4 text-emerald-400"/> Curva S - Desembolso (R$ Acumulado)
          </h3>
          <div className="h-[400px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              {/* O data foi trocado de dataOriginal para dataExibicao */}
              <AreaChart data={dataExibicao} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <defs>
                  <linearGradient id="colorCustoPrev" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#94a3b8" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#94a3b8" stopOpacity={0}/>
                  </linearGradient>
                  <linearGradient id="colorCustoReal" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#10b981" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#10b981" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                <XAxis dataKey="DataFormatada" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} dy={10} />
                <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} tickFormatter={(val) => `R$${(val / 1000).toFixed(0)}k`} width={60} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#f8fafc' }} 
                  formatter={(value: number) => formatCurrency(value)}
                />
                <Legend wrapperStyle={{ paddingTop: '20px', color: '#f8fafc' }}/>
                <Area type="monotone" dataKey="Custo_Previsto_Acumulado" name="Custo Previsto (Baseline)" stroke="#94a3b8" strokeWidth={3} fillOpacity={1} fill="url(#colorCustoPrev)" />
                <Area type="monotone" dataKey="Custo_Realizado_Acumulado" name="Custo Realizado (Atual)" stroke="#10b981" strokeWidth={3} fillOpacity={1} fill="url(#colorCustoReal)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
    </div>
  );
}