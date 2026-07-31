import { useState, useEffect } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Cell, PieChart, Pie, Legend } from "recharts";
import { AlertOctagon, Flame, Clock, TrendingDown, ShieldAlert, PauseCircle, FileWarning, FilterX, Download, FileText, FileSpreadsheet } from "lucide-react";
import toast from 'react-hot-toast'; // 🚀 Para mensagens de notificação

export default function ViewAnaliseDesvios({ projetoCodigo }: { projetoCodigo: string }) {
  const [subAba, setSubAba] = useState("ofensores");
  const [dataOfensores, setDataOfensores] = useState<any>(null);
  const [dataQualidade, setDataQualidade] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  // ESTADOS PARA O FILTRO CRUZADO E GERAÇÃO DE DOCUMENTOS
  const [filtroStatus, setFiltroStatus] = useState<string | null>(null);
  const [filtroDisciplina, setFiltroDisciplina] = useState<string | null>(null);
  const [linhaSelecionada, setLinhaSelecionada] = useState<any>(null);
  
  // 🚀 NOVO: Armazena o template oficial carregado para aquela aba
  const [templateAtivo, setTemplateAtivo] = useState<any>(null);

  useEffect(() => {
    setFiltroStatus(null);
    setFiltroDisciplina(null);
    setLinhaSelecionada(null);
    setTemplateAtivo(null);
  }, [subAba]);

  // 🚀 NOVO: Sempre que mudar a aba, busca no banco se existe template para imprimir
  useEffect(() => {
    if (subAba === 'ofensores') return;

    const fetchTemplate = async () => {
      try {
        const moduloDb = subAba === 'escopo' ? 'desvios' : subAba === 'nc' ? 'ncs' : 'holds';
        const res = await fetch(`http://localhost:5277/api/Templates/ativo?modulo=${moduloDb}`);
        const json = await res.json();
        if (json.sucesso) setTemplateAtivo(json.dados);
      } catch (e) {
        console.error("Falha ao buscar template", e);
      }
    };
    fetchTemplate();
  }, [subAba]);

  const CORES_STATUS: Record<string, string> = {
    'PENDENTE': '#fb923c', 'CONCLUÍDO': '#22c55e', 'CONCLUÍDA': '#22c55e',
    'EM ANÁLISE': '#facc15', 'APROVADA': '#22c55e', 'REPROVADA': '#f43f5e',   
    'ATRASADA': '#f43f5e', 'NO PRAZO': '#22c55e'
  };

  const CORES_DISCIPLINA = ['#38bdf8', '#f59e0b', '#22c55e', '#ef4444', '#a855f7', '#14b8a6', '#f97316'];
  const getCorDisciplina = (index: number) => CORES_DISCIPLINA[Math.abs(index) % CORES_DISCIPLINA.length]; 

  const tooltipStyle = { backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #334155', color: '#fff', fontWeight: 'bold', fontSize: '11px' };

  useEffect(() => {
    const fetchData = async () => {
      try {
        setLoading(true);
        const [resOfensores, resQualidade] = await Promise.all([
          fetch(`http://localhost:5277/api/TimesheetReport/ofensores?projetoCodigo=${encodeURIComponent(projetoCodigo)}`),
          fetch(`http://localhost:5277/api/TimesheetReport/qualidade-contratos?projetoCodigo=${encodeURIComponent(projetoCodigo)}`)
        ]);
        
        const jsonOfensores = await resOfensores.json();
        const jsonQualidade = await resQualidade.json();
        
        if (jsonOfensores.sucesso) setDataOfensores(jsonOfensores.dados);
        if (jsonQualidade.sucesso) setDataQualidade(jsonQualidade.dados);
        
      } catch (error) {
        console.error("Erro ao buscar dados:", error);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [projetoCodigo]);

  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(num);

  const toggleStatus = (status: string) => { setFiltroStatus(prev => prev === status ? null : status); setFiltroDisciplina(null); setLinhaSelecionada(null); };
  const toggleDisciplina = (disc: string) => { setFiltroDisciplina(prev => prev === disc ? null : disc); setFiltroStatus(null); setLinhaSelecionada(null); };
  const limparFiltros = () => { setFiltroStatus(null); setFiltroDisciplina(null); setLinhaSelecionada(null); };
  const handleRowClick = (item: any) => { setLinhaSelecionada((prev: any) => prev === item ? null : item); };

  // 🚀 NOVO: Função Mágica que pede o arquivo Word preenchido pro Backend
  const handleGerarDocumento = async () => {
    if (!linhaSelecionada || !templateAtivo) return;
    
    const moduloDb = subAba === 'escopo' ? 'desvios' : subAba === 'nc' ? 'ncs' : 'holds';
    const loadingToast = toast.loading(`Injetando dados no ${templateAtivo.NomeArquivo}...`);
    
    try {
      const res = await fetch(`http://localhost:5277/api/Templates/gerar?modulo=${moduloDb}&idRegistro=${linhaSelecionada.ID}`);
      
      if (!res.ok) {
        const errData = await res.json();
        throw new Error(errData.mensagem || "Erro na API ao gerar documento.");
      }

      // Faz o download do arquivo de forma oculta pelo navegador
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${moduloDb.toUpperCase()}_${linhaSelecionada.ID}_Oficial.docx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);

      toast.success("Documento gerado com sucesso!", { id: loadingToast });
    } catch(err: any) {
      toast.error(`Falha: ${err.message}`, { id: loadingToast });
    }
  };

  const agruparDados = (dados: any[], chave: string) => {
    const mapa: Record<string, number> = {};
    dados.forEach(d => {
      const val = d[chave] || 'N/A';
      mapa[val] = (mapa[val] || 0) + 1;
    });
    return Object.keys(mapa).map(k => ({ name: k, value: mapa[k] }));
  };

  const getNomeItemSelecionado = () => {
    if (!linhaSelecionada) return "";
    return linhaSelecionada.Num_Desvio || linhaSelecionada.Num_Hold || linhaSelecionada.Num_Doc || "Registro";
  };

  const exportToCSV = (dados: any[], nomeArquivo: string) => {
    if (!dados || dados.length === 0) return;
    const colunas = Object.keys(dados[0]).join(';');
    const linhas = dados.map(row => Object.values(row).map(val => `"${val ? String(val).replace(/"/g, '""') : ''}"`).join(';')).join('\n');
    const csvContent = "\uFEFF" + colunas + '\n' + linhas; 
    
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `${nomeArquivo}_${new Date().toLocaleDateString('pt-BR').replace(/\//g, '-')}.csv`;
    link.click();
  };

  if (loading || !dataOfensores || !dataQualidade) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Sincronizando Módulo de Desvios...</div>;
  }

  return (
    <div className="space-y-6 animate-enter">
      
      <div className="flex flex-col gap-4 p-4 rounded-xl border border-rose-900/50 bg-rose-950/20 shadow">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <AlertOctagon className="w-6 h-6 text-rose-500" />
            <div>
              <h2 className="text-lg font-bold text-rose-100">Central de Desvios e Contratos</h2>
              <p className="text-xs text-rose-300/70">Acompanhamento de ofensores financeiros, pleitos de escopo, holds e não conformidades.</p>
            </div>
          </div>
          <div className="hidden md:flex text-[10px] text-rose-300/50 bg-rose-950/30 border border-rose-900/30 px-3 py-1.5 rounded-full uppercase tracking-widest font-black">
            Para editar status, acesse a aba "Lançamentos PMO"
          </div>
        </div>

        <div className="flex gap-2 overflow-x-auto custom-scrollbar pt-2">
          <button onClick={() => setSubAba("ofensores")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "ofensores" ? "bg-rose-500 text-white shadow-[0_0_10px_rgba(244,63,94,0.4)]" : "bg-slate-800 text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500"}`}>
            <TrendingDown className="w-3 h-3" /> Ofensores de HH/Custo
          </button>
          <button onClick={() => setSubAba("escopo")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "escopo" ? "bg-amber-500 text-white shadow-[0_0_10px_rgba(245,158,11,0.4)]" : "bg-slate-800 text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500"}`}>
            <FileWarning className="w-3 h-3" /> Desvios de Escopo (Pleitos)
          </button>
          <button onClick={() => setSubAba("holds")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "holds" ? "bg-sky-500 text-white shadow-[0_0_10px_rgba(14,165,233,0.4)]" : "bg-slate-800 text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500"}`}>
            <PauseCircle className="w-3 h-3" /> Controle de Holds
          </button>
          <button onClick={() => setSubAba("nc")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "nc" ? "bg-emerald-500 text-white shadow-[0_0_10px_rgba(16,185,129,0.4)]" : "bg-slate-800 text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500"}`}>
            <ShieldAlert className="w-3 h-3" /> Não Conformidades (NC)
          </button>
        </div>
      </div>

      {/* 🚀 O NOVO BOTÃO DE GERAR DOCUMENTO APARECE AQUI SE VOCÊ CLICAR EM UMA LINHA E EXISTIR TEMPLATE */}
      {(filtroStatus || filtroDisciplina || linhaSelecionada) && subAba !== "ofensores" && (
        <div className="flex flex-col md:flex-row items-center justify-between bg-sky-500/10 border border-sky-500/30 p-3 rounded-lg text-sky-400 text-sm animate-enter gap-3">
          <div className="flex items-center gap-2">
            <FilterX className="w-4 h-4" />
            <span>Filtro Ativo: <strong>{linhaSelecionada ? `Destacando Item ${getNomeItemSelecionado()}` : (filtroStatus || filtroDisciplina)}</strong></span>
          </div>
          
          <div className="flex items-center gap-3">
            {/* O BOTÃO APARECE MAGICAMENTE QUANDO A LINHA É CLICADA E TEM TEMPLATE ATIVO */}
            {linhaSelecionada && templateAtivo && (
              <button 
                onClick={handleGerarDocumento} 
                // 🎨 Inovação Visual: Classe dinâmica muda a cor conforme a extensão do arquivo no banco
                className={`flex items-center gap-2 text-[10px] font-black uppercase text-white px-4 py-2 rounded shadow-lg hover:scale-105 transition-all active:scale-95 ${
                  templateAtivo.NomeArquivo.toLowerCase().endsWith('.xlsx') 
                    ? 'bg-gradient-to-r from-emerald-600 to-teal-500 shadow-emerald-500/40' // Verde/Esmeralda para Excel
                    : 'bg-gradient-to-r from-indigo-600 to-blue-600 shadow-indigo-500/40'    // Azul/Índigo para Word
                }`}
              >
                {/* 👁️ Inovação Funcional: Muda Ícone e Texto reagindo ao tipo de template salvo */}
                {templateAtivo.NomeArquivo.toLowerCase().endsWith('.xlsx') ? (
                  <>
                    <FileSpreadsheet className="w-4 h-4" /> Imprimir Ficha Excel Oficial
                  </>
                ) : (
                  <>
                    <FileText className="w-4 h-4" /> Imprimir Word Oficial
                  </>
                )}
              </button>
            )}

            <button onClick={limparFiltros} className="text-[10px] font-black uppercase bg-sky-500 text-white px-4 py-2 rounded transition-all shadow-lg shadow-sky-500/20 hover:bg-sky-400 active:scale-95">
              Limpar Filtros
            </button>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* ABA 1: OFENSORES */}
      {/* ========================================================================= */}
      {subAba === "ofensores" && (
        <div className="space-y-6 animate-enter">
          <div className="grid gap-6 lg:grid-cols-2">
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col h-[400px]">
              <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-4"><Flame className="w-4 h-4 text-orange-500"/> Sangramento de HH por Disciplina</h3>
              <div className="flex-1 w-full">
                {dataOfensores.disciplinasOff.length > 0 ? (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={dataOfensores.disciplinasOff} layout="vertical" margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
                      <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="#334155" opacity={0.5} />
                      <XAxis type="number" axisLine={false} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} />
                      <YAxis dataKey="Disciplina" type="category" axisLine={false} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} width={120} />
                      <Tooltip cursor={{ fill: 'rgba(255,255,255,0.05)' }} contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #f43f5e', color: '#f8fafc' }} />
                      <Bar dataKey="DesvioHH" radius={[0, 4, 4, 0]} barSize={16}>
                        {dataOfensores.disciplinasOff.map((entry: any, index: number) => (
                          <Cell key={`cell-${index}`} fill={index === 0 ? '#e11d48' : '#f59e0b'} />
                        ))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                ) : (
                  <div className="flex flex-col items-center justify-center h-full text-slate-500"><span className="text-emerald-500 text-3xl mb-2">🎉</span><p>Nenhuma disciplina estourou o HH previsto!</p></div>
                )}
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col h-[400px]">
              <div className="p-4 border-b border-slate-700 flex items-center justify-between">
                <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2"><Clock className="w-4 h-4 text-rose-400"/> Atraso Crônico (Fila de Gargalos)</h3>
                <button onClick={() => exportToCSV(dataOfensores.atrasosCronicos, "Atrasos_Cronicos")} className="flex items-center gap-1.5 bg-slate-700 hover:bg-slate-600 text-white text-xs font-bold px-3 py-1.5 rounded transition-colors" title="Exportar para Excel">
                  <Download className="w-3.5 h-3.5" /> Exportar
                </button>
              </div>
              <div className="p-0 overflow-auto custom-scrollbar flex-1">
                <table className="w-full text-xs text-left whitespace-nowrap">
                  <thead className="bg-slate-900 sticky top-0 z-10 text-slate-100">
                    <tr>
                      <th className="px-4 py-3 font-bold uppercase tracking-wider">Documento</th>
                      <th className="px-4 py-3 font-bold text-center uppercase tracking-wider">% Físico</th>
                      <th className="px-4 py-3 font-bold text-right uppercase tracking-wider">Atraso</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-700/50">
                    {dataOfensores.atrasosCronicos.map((doc: any, idx: number) => (
                      <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                        <td className="px-4 py-3"><div className="font-medium text-slate-200" title={doc.Titulo}>{doc.Titulo}</div><div className="text-[10px] text-slate-500 font-mono">{doc.Codigo}</div></td>
                        <td className="px-4 py-3 text-center"><span className="bg-slate-950 border border-slate-700 px-2 py-1 rounded text-sky-400 font-black">{doc.AvancoFisico}%</span></td>
                        <td className="px-4 py-3 text-right"><span className="text-rose-500 font-black text-sm">+{doc.DiasAtraso}d</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* ABA 2: ESCOPO */}
      {/* ========================================================================= */}
      {subAba === "escopo" && (() => {
        const tabelaFiltrada = dataQualidade.desvios.tabela.filter((d: any) => {
          if (filtroStatus) return d.Status === filtroStatus;
          if (filtroDisciplina) return d.Disciplina === filtroDisciplina;
          return true;
        });

        const dadosBaseGraficos = linhaSelecionada ? [linhaSelecionada] : tabelaFiltrada;
        const chartStatus = linhaSelecionada ? agruparDados([linhaSelecionada], 'Status') : dataQualidade.desvios.status;
        const chartDisciplina = linhaSelecionada ? agruparDados([linhaSelecionada], 'Disciplina') : dataQualidade.desvios.disciplinas;

        const qtdInternos = dadosBaseGraficos.filter((d: any) => d.Tipo_Desvio !== 'EXTERNO').length;
        const qtdExternos = dadosBaseGraficos.filter((d: any) => d.Tipo_Desvio === 'EXTERNO').length;
        
        const valorTotalCO = dadosBaseGraficos.reduce((acc: number, curr: any) => acc + (curr.Valor_CO || 0), 0);
        const valorAprovado = dadosBaseGraficos.filter((d: any) => d.Status === 'APROVADA').reduce((acc: number, curr: any) => acc + (curr.Valor_CO || 0), 0);
        const valorReprovado = dadosBaseGraficos.filter((d: any) => d.Status === 'REPROVADA').reduce((acc: number, curr: any) => acc + (curr.Valor_CO || 0), 0);
        const valorEmAnalise = dadosBaseGraficos.filter((d: any) => d.Status === 'EM ANÁLISE').reduce((acc: number, curr: any) => acc + (curr.Valor_CO || 0), 0);

        return (
          <div className="space-y-6 animate-enter">
            <div className="grid gap-6 md:grid-cols-3">
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
                <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest mb-2">Desvios (Internos)</span>
                <span className="text-4xl font-black text-amber-500 drop-shadow-[0_0_15px_rgba(245,158,11,0.3)]">{qtdInternos}</span>
              </div>
              
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
                <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest mb-2">Change Orders (Externos)</span>
                <span className="text-4xl font-black text-sky-500 drop-shadow-[0_0_15px_rgba(14,165,233,0.3)]">{qtdExternos}</span>
              </div>
              
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center w-full">
                <div className="text-center mb-3 border-b border-slate-700/50 pb-3">
                  <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest block mb-1">Valor Total Pleiteado</span>
                  <span className="text-3xl font-black text-emerald-500 drop-shadow-[0_0_15px_rgba(16,185,129,0.3)]">{formatCurrency(valorTotalCO)}</span>
                </div>
                <div className="grid grid-cols-3 gap-2 text-center divide-x divide-slate-700/50">
                   <div className="px-1">
                     <span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Aprovado</span>
                     <span className="text-xs font-black text-emerald-400">{formatCurrency(valorAprovado)}</span>
                   </div>
                   <div className="px-1">
                     <span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Em Análise</span>
                     <span className="text-xs font-black text-amber-400">{formatCurrency(valorEmAnalise)}</span>
                   </div>
                   <div className="px-1">
                     <span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Reprovado</span>
                     <span className="text-xs font-black text-rose-400">{formatCurrency(valorReprovado)}</span>
                   </div>
                </div>
              </div>
            </div>
            
            <div className="grid gap-6 md:grid-cols-2">
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 relative group">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Status (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie data={chartStatus} cx="50%" cy="40%" innerRadius={55} outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                      {chartStatus.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? '#fff' : 'none'} strokeWidth={3} className="hover:opacity-80 transition-opacity" />
                      ))}
                    </Pie>
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff', fontWeight: 'bold' }} />
                    <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: '#fff', fontWeight: 'bold', paddingTop: '10px' }} onClick={(e) => toggleStatus(e.value)} />
                  </PieChart>
                </ResponsiveContainer>
              </div>
              
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 relative group">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Por Disciplina (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e) => { if(e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                    <XAxis dataKey="name" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} />
                    <YAxis axisLine={false} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} allowDecimals={false} />
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff' }} cursor={{ fill: 'rgba(255,255,255,0.05)' }} />
                    <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                      {chartDisciplina.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? '#fff' : 'none'} strokeWidth={2} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
               <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                 <button onClick={() => exportToCSV(tabelaFiltrada, "Desvios_Escopo")} className="flex items-center gap-1.5 bg-amber-500/20 text-amber-500 hover:bg-amber-500 hover:text-white border border-amber-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all">
                   <Download className="w-3.5 h-3.5" /> Exportar Planilha
                 </button>
               </div>
               <div className="overflow-x-auto custom-scrollbar">
                 <table className="w-full text-xs text-left whitespace-nowrap">
                   <thead className="bg-slate-900 text-slate-100">
                     <tr>
                       <th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Desvio</th>
                       <th className="px-4 py-4 font-bold uppercase tracking-wider">Tipo</th>
                       <th className="px-4 py-4 font-bold uppercase tracking-wider">Motivo</th>
                       <th className="px-4 py-4 font-bold uppercase tracking-wider">Impacto</th>
                       <th className="px-4 py-4 font-bold uppercase tracking-wider">Disciplina</th>
                       <th className="px-4 py-4 font-bold text-right uppercase tracking-wider">Esforço (HH)</th>
                       <th className="px-4 py-4 font-bold text-right uppercase tracking-wider">Valor CO</th>
                       <th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th>
                     </tr>
                   </thead>
                   <tbody className="divide-y divide-slate-700/50">
                     {tabelaFiltrada.map((d: any, idx: number) => (
                       <tr 
                         key={idx} 
                         onClick={() => handleRowClick(d)}
                         className={`cursor-pointer transition-all ${linhaSelecionada === d ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`}
                         title="Clique para habilitar a geração de documento"
                       >
                         <td className="px-4 py-4 font-black text-amber-500">{d.Num_Desvio}</td>
                         <td className="px-4 py-4"><span className="bg-slate-950 px-2 py-1 border border-slate-700 rounded text-slate-300 font-bold">{d.Tipo_Desvio || 'INTERNO'}</span></td>
                         <td className="px-4 py-4 text-slate-300 font-bold">{d.Categoria_Motivo || '-'}</td>
                         <td className="px-4 py-4 text-slate-300 font-bold">{d.Impacto || '-'}</td>
                         <td className="px-4 py-4 text-slate-400 font-bold">{d.Disciplina}</td>
                         <td className="px-4 py-4 text-right font-black text-sky-400">{d.Quant_HH}h</td>
                         <td className="px-4 py-4 text-right font-black text-emerald-400">{d.Valor_CO > 0 ? formatCurrency(d.Valor_CO) : '-'}</td>
                         <td className="px-4 py-4 text-center">
                           <span className="inline-block w-28 py-1 rounded text-[10px] font-black uppercase tracking-tighter shadow-sm border border-white/10" style={{backgroundColor: CORES_STATUS[d.Status], color: '#fff'}}>{d.Status}</span>
                         </td>
                       </tr>
                     ))}
                     {tabelaFiltrada.length === 0 && <tr><td colSpan={8} className="text-center py-6 text-slate-500">Nenhum desvio atende ao filtro selecionado.</td></tr>}
                   </tbody>
                 </table>
               </div>
            </div>
          </div>
        );
      })()}

      {/* ========================================================================= */}
      {/* ABA 3: HOLDS */}
      {/* ========================================================================= */}
      {subAba === "holds" && (() => {
        const tabelaFiltrada = dataQualidade.holds.tabela.filter((d: any) => {
          if (filtroStatus) return d.Status === filtroStatus;
          if (filtroDisciplina) return d.Disciplina === filtroDisciplina;
          return true;
        });

        const dadosBaseGraficos = linhaSelecionada ? [linhaSelecionada] : tabelaFiltrada;
        const chartStatus = linhaSelecionada ? agruparDados([linhaSelecionada], 'Status') : dataQualidade.holds.status;
        const chartDisciplina = linhaSelecionada ? agruparDados([linhaSelecionada], 'Disciplina') : dataQualidade.holds.disciplinas;

        return (
          <div className="space-y-6 animate-enter">
            <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
                <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest mb-2">Holds Ativos</span>
                <span className="text-6xl font-black text-sky-500 drop-shadow-[0_0_15px_rgba(14,165,233,0.3)]">{dadosBaseGraficos.length}</span>
              </div>
              
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Status (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie data={chartStatus} cx="50%" cy="40%" outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                      {chartStatus.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? '#fff' : 'none'} strokeWidth={3} />
                      ))}
                    </Pie>
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff' }} />
                    <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: '#fff', fontWeight: 'bold' }} onClick={(e) => toggleStatus(e.value)} />
                  </PieChart>
                </ResponsiveContainer>
              </div>

              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Disciplina (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e) => { if(e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                    <XAxis dataKey="name" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} />
                    <YAxis axisLine={false} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} allowDecimals={false} />
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff' }} cursor={{ fill: 'rgba(255,255,255,0.05)' }} />
                    <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                      {chartDisciplina.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? '#fff' : 'none'} strokeWidth={2} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
               <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                 <button onClick={() => exportToCSV(tabelaFiltrada, "Controle_Holds")} className="flex items-center gap-1.5 bg-sky-500/20 text-sky-500 hover:bg-sky-500 hover:text-white border border-sky-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all">
                   <Download className="w-3.5 h-3.5" /> Exportar Planilha
                 </button>
               </div>
               <div className="overflow-x-auto custom-scrollbar">
                 <table className="w-full text-xs text-left whitespace-nowrap">
                    <thead className="bg-slate-900 text-slate-100">
                      <tr>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Hold</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Documento</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Motivo</th>
                        <th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Responsável</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-700/50">
                      {tabelaFiltrada.map((h: any, idx: number) => (
                        <tr 
                          key={idx} 
                          onClick={() => handleRowClick(h)}
                          className={`cursor-pointer transition-all ${linhaSelecionada === h ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`}
                          title="Clique para habilitar a geração de documento"
                        >
                          <td className="px-4 py-4 font-black text-sky-500">{h.Num_Hold}</td>
                          <td className="px-4 py-4">
                            <div className="font-bold text-slate-200">{h.Titulo_Documento} <span className="text-slate-500 font-normal text-[10px]">({h.Revisao})</span></div>
                            <div className="text-[10px] text-slate-500 font-mono">{h.Num_NA_Documento} • {h.Disciplina}</div>
                          </td>
                          <td className="px-4 py-4 text-slate-300 italic max-w-[250px] truncate" title={h.Motivo_Hold}>{h.Motivo_Hold}</td>
                          <td className="px-4 py-4 text-center">
                            <span className="inline-block w-24 py-1 rounded text-[10px] font-black uppercase tracking-tighter" style={{backgroundColor: CORES_STATUS[h.Status], color: '#fff'}}>{h.Status}</span>
                          </td>
                          <td className="px-4 py-4 text-slate-400 font-bold">{h.Responsavel}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
               </div>
            </div>
          </div>
        );
      })()}

      {/* ========================================================================= */}
      {/* ABA 4: NÃO CONFORMIDADES */}
      {/* ========================================================================= */}
      {subAba === "nc" && (() => {
        const tabelaFiltrada = dataQualidade.ncs.tabela.filter((d: any) => {
          if (filtroStatus) return d.Status === filtroStatus;
          if (filtroDisciplina) return d.Disciplina === filtroDisciplina;
          return true;
        });

        const dadosBaseGraficos = linhaSelecionada ? [linhaSelecionada] : tabelaFiltrada;
        const chartStatus = linhaSelecionada ? agruparDados([linhaSelecionada], 'Status') : dataQualidade.ncs.status;
        const chartDisciplina = linhaSelecionada ? agruparDados([linhaSelecionada], 'Disciplina') : dataQualidade.ncs.disciplinas;

        return (
          <div className="space-y-6 animate-enter">
            <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
                <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest mb-2">Não Conformidades</span>
                <span className="text-6xl font-black text-emerald-500 drop-shadow-[0_0_15px_rgba(16,185,129,0.3)]">{dadosBaseGraficos.length}</span>
              </div>
              
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Status (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie data={chartStatus} cx="50%" cy="40%" outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                      {chartStatus.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? '#fff' : 'none'} strokeWidth={3} />
                      ))}
                    </Pie>
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff' }} />
                    <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: '#fff', fontWeight: 'bold' }} onClick={(e) => toggleStatus(e.value)} />
                  </PieChart>
                </ResponsiveContainer>
              </div>

              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72">
                <h3 className="text-[10px] font-black text-slate-100 uppercase tracking-widest text-center mb-4">Disciplina (Filtrável)</h3>
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e) => { if(e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                    <XAxis dataKey="name" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} />
                    <YAxis axisLine={false} tickLine={false} tick={{ fill: '#f8fafc', fontSize: 10 }} allowDecimals={false} />
                    <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: '#fff' }} cursor={{ fill: 'rgba(255,255,255,0.05)' }} />
                    <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                      {chartDisciplina.map((entry: any, index: number) => (
                        <Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? '#fff' : 'none'} strokeWidth={2} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
               <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                 <button onClick={() => exportToCSV(tabelaFiltrada, "Nao_Conformidades")} className="flex items-center gap-1.5 bg-emerald-500/20 text-emerald-500 hover:bg-emerald-500 hover:text-white border border-emerald-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all">
                   <Download className="w-3.5 h-3.5" /> Exportar Planilha
                 </button>
               </div>
               <div className="overflow-x-auto custom-scrollbar">
                 <table className="w-full text-xs text-left whitespace-nowrap">
                    <thead className="bg-slate-900 text-slate-100">
                      <tr>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Doc</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Tipo</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Empresa</th>
                        <th className="px-4 py-4 font-bold uppercase tracking-wider">Disciplina</th>
                        <th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-700/50">
                      {tabelaFiltrada.map((nc: any, idx: number) => (
                        <tr 
                          key={idx} 
                          onClick={() => handleRowClick(nc)}
                          className={`cursor-pointer transition-all ${linhaSelecionada === nc ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`}
                          title="Clique para habilitar a geração de documento"
                        >
                          <td className="px-4 py-4 font-black text-emerald-500">{nc.Num_Doc}</td>
                          <td className="px-4 py-4"><span className="bg-slate-950 px-2 py-1 rounded text-emerald-400 font-black border border-emerald-900/50">{nc.Tipo_Registro}</span></td>
                          <td className="px-4 py-4 text-slate-300 font-bold">{nc.Empresa}</td>
                          <td className="px-4 py-4 text-slate-400">{nc.Disciplina}</td>
                          <td className="px-4 py-4 text-center">
                            <span className="inline-block w-24 py-1 rounded text-[10px] font-black uppercase tracking-tighter" style={{backgroundColor: CORES_STATUS[nc.Status], color: '#fff'}}>{nc.Status}</span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
               </div>
            </div>
          </div>
        );
      })()}

    </div>
  );
}