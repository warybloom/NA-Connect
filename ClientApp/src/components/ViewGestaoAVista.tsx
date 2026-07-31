import { useEffect, useState } from "react";
import { CheckCircle2, AlertTriangle, Clock, CalendarDays, FileText, Target, Layers, Filter, X } from "lucide-react";

export default function ViewGestaoAVista({ projetoCodigo }: { projetoCodigo: string }) {
  const [data, setData] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  // 🚀 ESTADOS DE FILTRO CRUZADO (Igual ao Power BI)
  const [filtroDisciplina, setFiltroDisciplina] = useState<string | null>(null);
  const [filtroDocumento, setFiltroDocumento] = useState<string | null>(null);

  useEffect(() => {
    const fetchGestao = async () => {
      try {
        setLoading(true);
        let url = `http://localhost:5277/api/TimesheetReport/gestao-vista?projetoCodigo=${encodeURIComponent(projetoCodigo)}`;
        if (filtroDisciplina) url += `&disciplina=${encodeURIComponent(filtroDisciplina)}`;
        if (filtroDocumento) url += `&documento=${encodeURIComponent(filtroDocumento)}`;

        const response = await fetch(url);
        const result = await response.json();
        if (result.sucesso) setData(result.dados);
      } catch (error) {
        console.error("Erro ao buscar Gestão à Vista:", error);
      } finally {
        setLoading(false);
      }
    };
    fetchGestao();
  }, [projetoCodigo, filtroDisciplina, filtroDocumento]);

  const formatHH = (num: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(num);
  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 }).format(num);

  if (!data && loading) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Atualizando Cockpit de Gestão à Vista...</div>;
  }

  if (!data) return null;

  const progressoGeral = data.cardsHH.HH_Previsto > 0 
    ? Math.min(100, Math.round((data.cardsHH.HH_Realizado / data.cardsHH.HH_Previsto) * 100)) 
    : 0;

  const totalEntregasSemana = data.programacaoSemanal.length;

  return (
    <div className="space-y-6 animate-enter">
      
      {/* 🎯 BARRA DE FILTROS ATIVOS */}
      {(filtroDisciplina || filtroDocumento) && (
        <div className="flex flex-col gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="flex items-center gap-2 text-sm font-semibold text-slate-300">
              <Filter className="w-4 h-4 text-amber-400" /> Detalhando Cockpit:
            </div>

            {filtroDisciplina && (
              <div className="flex items-center gap-2 bg-emerald-900/50 border border-emerald-500/50 text-emerald-300 px-3 py-1 rounded-full text-xs font-medium">
                Disc: {filtroDisciplina}
                <button onClick={() => setFiltroDisciplina(null)} title="Remover filtro" className="hover:text-white"><X className="w-3 h-3" /></button>
              </div>
            )}
            {filtroDocumento && (
              <div className="flex items-center gap-2 bg-purple-900/50 border border-purple-500/50 text-purple-300 px-3 py-1 rounded-full text-xs font-medium">
                Doc: {filtroDocumento}
                <button onClick={() => setFiltroDocumento(null)} title="Remover filtro" className="hover:text-white"><X className="w-3 h-3" /></button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* LINHA 1: CARDS DE DOCUMENTOS & MEDIDOR DE SAÚDE */}
      <div className={`grid gap-6 lg:grid-cols-12 transition-opacity duration-300 ${loading ? 'opacity-50' : 'opacity-100'}`}>
        
        {/* Bloco de Documentos */}
        <div className="lg:col-span-8 grid grid-cols-2 md:grid-cols-4 gap-4">
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-t-4 border-t-slate-500">
            <span className="text-xs font-bold text-slate-400 uppercase flex items-center gap-1 mb-2"><FileText className="w-3 h-3"/> Total Docs</span>
            <span className="text-3xl font-black">{data.cardsDocs.TotalDocumentos}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-t-4 border-t-emerald-500">
            <span className="text-xs font-bold text-emerald-400 uppercase flex items-center gap-1 mb-2"><CheckCircle2 className="w-3 h-3"/> Emitidos</span>
            <span className="text-3xl font-black text-emerald-400">{data.cardsDocs.Emitidos}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-t-4 border-t-sky-500">
            <span className="text-xs font-bold text-sky-400 uppercase flex items-center gap-1 mb-2"><Target className="w-3 h-3"/> No Prazo</span>
            <span className="text-3xl font-black text-sky-400">{data.cardsDocs.NoPrazo}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-4 flex flex-col justify-center border-t-4 border-t-rose-500 relative overflow-hidden">
            <span className="text-xs font-bold text-rose-400 uppercase flex items-center gap-1 mb-2"><AlertTriangle className="w-3 h-3"/> Atrasados</span>
            <span className="text-3xl font-black text-rose-400">{data.cardsDocs.Atrasados}</span>
            {data.cardsDocs.Atrasados > 0 && <div className="absolute top-0 right-0 w-16 h-16 bg-rose-500/10 rounded-bl-full"></div>}
          </div>
        </div>

        {/* Bloco de Termômetro Geral */}
        <div className="lg:col-span-4 rounded-xl border bg-slate-900 text-slate-50 shadow p-4 flex items-center justify-between">
          <div>
            <span className="text-xs font-bold text-slate-400 uppercase flex items-center gap-1 mb-2"><Clock className="w-3 h-3"/> Progresso HH</span>
            <div className="flex flex-col gap-1">
              <span className="text-lg font-bold text-sky-400">{formatHH(data.cardsHH.HH_Realizado)} <span className="text-xs text-slate-500">Real</span></span>
              <span className="text-lg font-bold text-slate-300">{formatHH(data.cardsHH.HH_Previsto)} <span className="text-xs text-slate-500">Prev</span></span>
            </div>
          </div>
          
          <div className="relative w-24 h-24 flex items-center justify-center">
            <svg className="w-full h-full transform -rotate-90">
              <circle cx="48" cy="48" r="36" stroke="currentColor" strokeWidth="8" fill="transparent" className="text-slate-800" />
              <circle cx="48" cy="48" r="36" stroke="currentColor" strokeWidth="8" fill="transparent" strokeDasharray="226.2" strokeDashoffset={226.2 - (226.2 * progressoGeral) / 100} className={progressoGeral > 100 ? 'text-rose-500' : 'text-sky-500'} strokeLinecap="round" />
            </svg>
            <div className="absolute inset-0 flex items-center justify-center flex-col">
              <span className="text-xl font-black">{progressoGeral}%</span>
            </div>
          </div>
        </div>

      </div>

      {/* LINHA 2: GRID DE DISCIPLINAS & PROGRAMAÇÃO SEMANAL */}
      <div className={`grid gap-6 lg:grid-cols-12 transition-opacity duration-300 ${loading ? 'opacity-50' : 'opacity-100'}`}>
        
        {/* GRID DE KPIs POR DISCIPLINA (Clicável) */}
        <div className="lg:col-span-7 rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col h-[460px]">
          <div className="p-4 border-b border-slate-700 flex items-center justify-between">
            <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm">
              <Layers className="w-4 h-4 text-sky-400"/> Saúde Financeira e de HH por Disciplina <span className="text-xs text-slate-500 ml-2 font-normal">(Clique para filtrar)</span>
            </h3>
          </div>
          
          <div className="p-4 overflow-y-auto custom-scrollbar flex-1">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {data.graficosDisciplina.map((disc: any, idx: number) => {
                const percentHH = disc.HH_Previsto > 0 ? Math.round((disc.HH_Realizado / disc.HH_Previsto) * 100) : 0;
                const percentCusto = disc.Custo_Previsto > 0 ? Math.round((disc.Custo_Realizado / disc.Custo_Previsto) * 100) : 0;
                
                const corHH = percentHH > 105 ? 'bg-rose-500' : 'bg-sky-500';
                const corTextoHH = percentHH > 105 ? 'text-rose-400' : 'text-sky-400';
                
                const corCusto = percentCusto > 105 ? 'bg-rose-500' : 'bg-emerald-500';
                const corTextoCusto = percentCusto > 105 ? 'text-rose-400' : 'text-emerald-400';

                const isSelected = filtroDisciplina === disc.Disciplina;

                return (
                  <div 
                    key={idx} 
                    onClick={() => setFiltroDisciplina(isSelected ? null : disc.Disciplina)}
                    className={`bg-slate-900 border rounded-xl p-4 flex flex-col gap-3 cursor-pointer transition-all duration-200 
                      ${isSelected ? 'border-sky-500 ring-1 ring-sky-500/50 bg-slate-800' : 'border-slate-700 hover:border-slate-500 hover:bg-slate-800'}`}
                  >
                    <h4 className="text-sm font-bold text-slate-100 border-b border-slate-700/50 pb-2 truncate" title={disc.Disciplina}>
                      {disc.Disciplina}
                    </h4>
                    
                    <div className="flex flex-col gap-1">
                      <div className="flex justify-between items-end text-xs">
                        <span className="text-slate-400 font-medium">Esforço (HH)</span>
                        <span className={`font-bold ${corTextoHH}`}>{percentHH}%</span>
                      </div>
                      <div className="w-full bg-slate-800 rounded-full h-2">
                        <div className={`h-2 rounded-full ${corHH}`} style={{ width: `${Math.min(percentHH, 100)}%` }}></div>
                      </div>
                      <div className="flex justify-between text-[10px] text-slate-500 mt-0.5">
                        <span>Real: {formatHH(disc.HH_Realizado)}</span>
                        <span>Prev: {formatHH(disc.HH_Previsto)}</span>
                      </div>
                    </div>

                    <div className="flex flex-col gap-1">
                      <div className="flex justify-between items-end text-xs">
                        <span className="text-slate-400 font-medium">Custo (R$)</span>
                        <span className={`font-bold ${corTextoCusto}`}>{percentCusto}%</span>
                      </div>
                      <div className="w-full bg-slate-800 rounded-full h-2">
                        <div className={`h-2 rounded-full ${corCusto}`} style={{ width: `${Math.min(percentCusto, 100)}%` }}></div>
                      </div>
                      <div className="flex justify-between text-[10px] text-slate-500 mt-0.5">
                        <span>Real: {formatCurrency(disc.Custo_Realizado)}</span>
                        <span>Prev: {formatCurrency(disc.Custo_Previsto)}</span>
                      </div>
                    </div>

                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Tabela de Programação Semanal (Clicável) */}
        <div className="lg:col-span-5 rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col h-[460px]">
          <div className="p-4 border-b border-slate-700 flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2">
                <CalendarDays className="w-4 h-4 text-amber-400"/> Foco da Semana <span className="text-xs text-slate-500 ml-2 font-normal">(Clique para filtrar)</span>
              </h3>
            </div>
            
            <div className="bg-slate-900 border border-slate-700 rounded-lg p-3 flex items-center justify-between">
              <div className="flex flex-col">
                <span className="text-xs text-slate-400 uppercase font-bold">Volume Esperado</span>
                <span className="text-[10px] text-slate-500">Atrasados + Próximos 7 dias</span>
              </div>
              <div className="text-2xl font-black text-amber-400">
                {totalEntregasSemana} <span className="text-sm font-medium text-slate-400">docs</span>
              </div>
            </div>
          </div>

          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            <table className="w-full text-xs text-left cursor-pointer">
              <thead className="bg-slate-900 sticky top-0 z-10 text-slate-400">
                <tr>
                  <th className="px-4 py-2 font-medium">Documento</th>
                  <th className="px-4 py-2 font-medium text-center">Data Alvo</th>
                  <th className="px-4 py-2 font-medium text-right">% Físico</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-700/50">
                {data.programacaoSemanal.map((doc: any, idx: number) => {
                  const isSelected = filtroDocumento === doc.Codigo;

                  return (
                    <tr 
                      key={idx} 
                      onClick={() => setFiltroDocumento(isSelected ? null : doc.Codigo)}
                      className={`transition-colors group ${isSelected ? 'bg-purple-900/40 border-l-2 border-purple-400' : 'hover:bg-slate-700/50'}`}
                    >
                      <td className="px-4 py-2">
                        <div className={`font-medium truncate max-w-[200px] transition-colors ${isSelected ? 'text-purple-300' : 'text-slate-200 group-hover:text-amber-400'}`} title={doc.Titulo}>
                          {doc.Titulo}
                        </div>
                        <div className="text-[9px] text-slate-500">{doc.Codigo}</div>
                      </td>
                      <td className="px-4 py-2 text-center whitespace-nowrap">
                        {doc.IsAtrasado === 1 ? (
                          <span className="text-rose-400 font-bold flex items-center justify-center gap-1"><AlertTriangle className="w-3 h-3"/> {doc.DataAlvo}</span>
                        ) : (
                          <span className="text-slate-300">{doc.DataAlvo}</span>
                        )}
                      </td>
                      <td className="px-4 py-2 text-right">
                        <span className="bg-slate-900 border border-slate-700 px-1.5 py-0.5 rounded text-sky-400 font-bold">{doc.AvancoFisico}%</span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            
            {totalEntregasSemana === 0 && (
              <div className="flex flex-col items-center justify-center h-full text-slate-500 p-6 text-center">
                <CheckCircle2 className="w-12 h-12 mb-3 opacity-20 text-emerald-500" />
                <p className="text-sm font-medium text-slate-300">Tudo limpo!</p>
                <p className="text-xs">Nenhum documento na fila de entregas desta semana.</p>
              </div>
            )}
          </div>
        </div>

      </div>
    </div>
  );
}