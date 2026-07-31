import { useState, useEffect } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Cell, PieChart, Pie, Legend } from "recharts";
import { AlertOctagon, Flame, Clock, TrendingDown, ShieldAlert, PauseCircle, FileWarning } from "lucide-react";

export default function ViewAnaliseDesvios({ projetoCodigo }: { projetoCodigo: string }) {
  const [subAba, setSubAba] = useState("ofensores");
  const [dataOfensores, setDataOfensores] = useState<any>(null);
  const [dataQualidade, setDataQualidade] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  // Cores personalizadas para os gráficos de Rosca
  const CORES_STATUS: Record<string, string> = {
    'PENDENTE': '#f97316', 'CONCLUÍDO': '#10b981', 'CONCLUÍDA': '#10b981',
    'EM ANÁLISE': '#eab308', 'APROVADA': '#10b981', 'REPROVADA': '#ef4444',
    'ATRASADA': '#ef4444', 'NO PRAZO': '#10b981'
  };
  const CORES_DISCIPLINA = ['#0ea5e9', '#8b5cf6', '#6366f1', '#ec4899', '#f43f5e', '#64748b'];

  useEffect(() => {
    const fetchData = async () => {
      try {
        setLoading(true);
        // Busca os dois painéis de uma vez
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

  const formatHH = (num: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(num);
  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 }).format(num);

  if (loading || !dataOfensores || !dataQualidade) {
    return <div className="flex items-center justify-center h-64 text-muted-foreground">Sincronizando Módulo de Desvios...</div>;
  }

  return (
    <div className="space-y-6 animate-enter">
      
      {/* CABEÇALHO HUB */}
      <div className="flex flex-col gap-4 p-4 rounded-xl border border-rose-900/50 bg-rose-950/20 shadow">
        <div className="flex items-center gap-3">
          <AlertOctagon className="w-6 h-6 text-rose-500" />
          <div>
            <h2 className="text-lg font-bold text-rose-100">Central de Desvios e Contratos</h2>
            <p className="text-xs text-rose-300/70">Acompanhamento de ofensores financeiros, pleitos de escopo, holds e não conformidades.</p>
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

      {/* ========================================================================= */}
      {/* ABA 1: OFENSORES DE HH E CUSTO */}
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
                      <XAxis type="number" axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} />
                      <YAxis dataKey="Disciplina" type="category" axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 9 }} width={120} />
                      <Tooltip cursor={{ fill: 'rgba(255,255,255,0.05)' }} contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: '1px solid #f43f5e', color: '#f8fafc' }} formatter={(value: number) => [`${formatHH(value)}h excedidas`, "Desvio"]} />
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
              </div>
              <div className="p-0 overflow-auto custom-scrollbar flex-1">
                <table className="w-full text-xs text-left">
                  <thead className="bg-slate-900 sticky top-0 z-10 text-slate-400">
                    <tr>
                      <th className="px-4 py-2 font-medium">Documento</th>
                      <th className="px-4 py-2 font-medium text-center">% Físico</th>
                      <th className="px-4 py-2 font-medium text-right">Dias de Atraso</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-700/50">
                    {dataOfensores.atrasosCronicos.map((doc: any, idx: number) => (
                      <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                        <td className="px-4 py-2"><div className="font-medium truncate max-w-[200px] text-slate-200" title={doc.Titulo}>{doc.Titulo}</div><div className="text-[9px] text-slate-500">{doc.Codigo}</div></td>
                        <td className="px-4 py-2 text-center"><span className="bg-slate-900 border border-slate-700 px-1.5 py-0.5 rounded text-sky-400 font-bold">{doc.AvancoFisico}%</span></td>
                        <td className="px-4 py-2 text-right"><span className="text-rose-500 font-black text-sm">+{doc.DiasAtraso}</span> <span className="text-[10px] text-slate-500">dias</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col">
            <div className="p-4 border-b border-slate-700 flex items-center justify-between">
              <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2"><TrendingDown className="w-4 h-4 text-rose-500"/> Top 10 Ralos Financeiros (Estouro de Custo)</h3>
            </div>
            <div className="p-0 overflow-auto custom-scrollbar max-h-[400px]">
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 sticky top-0 text-slate-400">
                  <tr>
                    <th className="px-4 py-3 font-medium">Ranking</th>
                    <th className="px-4 py-3 font-medium">Documento</th>
                    <th className="px-4 py-3 font-medium">Disciplina</th>
                    <th className="px-4 py-3 font-medium text-right">Custo Previsto</th>
                    <th className="px-4 py-3 font-medium text-right">Custo Realizado</th>
                    <th className="px-4 py-3 font-medium text-right text-rose-400">Prejuízo (Desvio)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {dataOfensores.documentosCustoOff.map((doc: any, idx: number) => (
                    <tr key={idx} className="hover:bg-slate-700/30 transition-colors bg-rose-950/5">
                      <td className="px-4 py-3 text-slate-500 font-black text-lg">#{idx + 1}</td>
                      <td className="px-4 py-3"><div className="font-bold text-slate-200" title={doc.Titulo}>{doc.Titulo}</div><div className="text-[10px] text-slate-500">{doc.Codigo}</div></td>
                      <td className="px-4 py-3 text-slate-400 font-medium">{doc.Disciplina}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatCurrency(doc.CustoPrevisto)}</td>
                      <td className="px-4 py-3 text-right text-amber-400 font-bold">{formatCurrency(doc.CustoRealizado)}</td>
                      <td className="px-4 py-3 text-right"><span className="bg-rose-500/10 text-rose-400 border border-rose-500/20 px-2 py-1 rounded font-black text-sm">+ {formatCurrency(doc.DesvioCusto)}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* ABA 2: DESVIOS DE ESCOPO (PLEITOS) */}
      {/* ========================================================================= */}
      {subAba === "escopo" && (
        <div className="space-y-6 animate-enter">
          <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
              <span className="text-xs font-bold text-slate-400 uppercase mb-2">Total de Change Orders</span>
              <span className="text-5xl font-black text-amber-500">{dataQualidade.desvios.tabela.length}</span>
            </div>
            
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Status dos Desvios</h3>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={dataQualidade.desvios.status} cx="50%" cy="40%" innerRadius={50} outerRadius={80} paddingAngle={2} dataKey="value">
                    {dataQualidade.desvios.status.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                  <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Por Disciplina</h3>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={dataQualidade.desvios.disciplinas} cx="50%" cy="40%" outerRadius={80} dataKey="value" label={({name, percent}) => `${name} ${(percent * 100).toFixed(0)}%`} labelLine={false} style={{fontSize: '9px'}}>
                    {dataQualidade.desvios.disciplinas.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={CORES_DISCIPLINA[index % CORES_DISCIPLINA.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
             <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 text-slate-400">
                  <tr>
                    <th className="px-4 py-3 font-medium">Nº Desvio</th>
                    <th className="px-4 py-3 font-medium">Descrição</th>
                    <th className="px-4 py-3 font-medium">Disciplina</th>
                    <th className="px-4 py-3 font-medium">Data</th>
                    <th className="px-4 py-3 font-medium">Quant HH</th>
                    <th className="px-4 py-3 font-medium">Status</th>
                    <th className="px-4 py-3 font-medium">Change Order</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {dataQualidade.desvios.tabela.map((d: any, idx: number) => (
                    <tr key={idx} className="hover:bg-slate-700/30">
                      <td className="px-4 py-3 font-bold text-amber-400">{d.Num_Desvio}</td>
                      <td className="px-4 py-3 font-medium text-slate-200 max-w-[200px] truncate">{d.Descricao}</td>
                      <td className="px-4 py-3 text-slate-400">{d.Disciplina}</td>
                      <td className="px-4 py-3 text-slate-400">{new Date(d.Data_Registro).toLocaleDateString('pt-BR')}</td>
                      <td className="px-4 py-3 font-bold text-sky-400">{d.Quant_HH}h</td>
                      <td className="px-4 py-3"><span className="bg-slate-900 border border-slate-700 px-2 py-1 rounded text-[10px]" style={{color: CORES_STATUS[d.Status]}}>{d.Status}</span></td>
                      <td className="px-4 py-3 text-slate-400">{d.Change_Order || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* ABA 3: CONTROLE DE HOLDS */}
      {/* ========================================================================= */}
      {subAba === "holds" && (
        <div className="space-y-6 animate-enter">
          <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
              <span className="text-xs font-bold text-slate-400 uppercase mb-2">Qtd Holds Registrados</span>
              <span className="text-5xl font-black text-sky-500">{dataQualidade.holds.tabela.length}</span>
            </div>
            
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Status</h3>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={dataQualidade.holds.status} cx="50%" cy="40%" outerRadius={80} dataKey="value">
                    {dataQualidade.holds.status.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                  <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Disciplina</h3>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={dataQualidade.holds.disciplinas} cx="50%" cy="40%" outerRadius={80} dataKey="value" label={({name, percent}) => `${name} ${(percent * 100).toFixed(0)}%`} labelLine={false} style={{fontSize: '9px'}}>
                    {dataQualidade.holds.disciplinas.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={CORES_DISCIPLINA[index % CORES_DISCIPLINA.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
             <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 text-slate-400">
                  <tr>
                    <th className="px-4 py-3 font-medium">Nº Hold</th>
                    <th className="px-4 py-3 font-medium">Documento</th>
                    <th className="px-4 py-3 font-medium">Motivo do Hold</th>
                    <th className="px-4 py-3 font-medium">Status</th>
                    <th className="px-4 py-3 font-medium">Responsável</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {dataQualidade.holds.tabela.map((h: any, idx: number) => (
                    <tr key={idx} className="hover:bg-slate-700/30">
                      <td className="px-4 py-3 font-bold text-sky-400">{h.Num_Hold}</td>
                      <td className="px-4 py-3">
                        <div className="font-bold text-slate-200">{h.Titulo_Documento} <span className="text-slate-500 font-normal">({h.Revisao})</span></div>
                        <div className="text-[10px] text-slate-500">{h.Num_NA_Documento} • {h.Disciplina}</div>
                      </td>
                      <td className="px-4 py-3 font-medium text-slate-300 max-w-[200px] truncate">{h.Motivo_Hold}</td>
                      <td className="px-4 py-3"><span className="bg-slate-900 border border-slate-700 px-2 py-1 rounded text-[10px]" style={{color: CORES_STATUS[h.Status]}}>{h.Status}</span></td>
                      <td className="px-4 py-3 text-slate-400">{h.Responsavel}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* ABA 4: NÃO CONFORMIDADES */}
      {/* ========================================================================= */}
      {subAba === "nc" && (
        <div className="space-y-6 animate-enter">
          <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col justify-center items-center text-center">
              <span className="text-xs font-bold text-slate-400 uppercase mb-2">Qtd Não Conformidades</span>
              <span className="text-5xl font-black text-emerald-500">{dataQualidade.ncs.tabela.length}</span>
            </div>
            
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Status</h3>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={dataQualidade.ncs.status} cx="50%" cy="40%" outerRadius={80} dataKey="value">
                    {dataQualidade.ncs.status.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                  <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-64">
              <h3 className="text-xs font-bold text-slate-400 uppercase text-center mb-2">Disciplina</h3>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={dataQualidade.ncs.disciplinas} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#334155" opacity={0.5} />
                  <XAxis dataKey="name" axisLine={{ stroke: '#475569' }} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 9 }} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 10 }} allowDecimals={false} />
                  <Tooltip cursor={{ fill: 'rgba(255,255,255,0.05)' }} contentStyle={{ backgroundColor: '#0f172a', borderRadius: '8px', border: 'none' }} />
                  <Bar dataKey="value" fill="#10b981" radius={[3, 3, 0, 0]} barSize={30} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
             <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 text-slate-400">
                  <tr>
                    <th className="px-4 py-3 font-medium">Nº Doc</th>
                    <th className="px-4 py-3 font-medium">Responsável</th>
                    <th className="px-4 py-3 font-medium">Tipo</th>
                    <th className="px-4 py-3 font-medium">Empresa</th>
                    <th className="px-4 py-3 font-medium">Disciplina</th>
                    <th className="px-4 py-3 font-medium">Descrição</th>
                    <th className="px-4 py-3 font-medium">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {dataQualidade.ncs.tabela.map((nc: any, idx: number) => (
                    <tr key={idx} className="hover:bg-slate-700/30">
                      <td className="px-4 py-3 font-bold text-slate-200">{nc.Num_Doc}</td>
                      <td className="px-4 py-3 text-slate-400">{nc.Responsavel_Abertura} <br/><span className="text-[9px]">{new Date(nc.Data_Registro).toLocaleDateString('pt-BR')}</span></td>
                      <td className="px-4 py-3"><span className="bg-slate-800 px-2 py-1 rounded text-slate-300 font-bold">{nc.Tipo_Registro}</span></td>
                      <td className="px-4 py-3 text-slate-400">{nc.Empresa}</td>
                      <td className="px-4 py-3 text-slate-400">{nc.Disciplina}</td>
                      <td className="px-4 py-3 text-slate-300 max-w-[200px] truncate">{nc.Descricao}</td>
                      <td className="px-4 py-3"><span className="bg-slate-900 border border-slate-700 px-2 py-1 rounded text-[10px]" style={{color: CORES_STATUS[nc.Status]}}>{nc.Status}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
          </div>
        </div>
      )}

    </div>
  );
}