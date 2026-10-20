import { useState } from "react";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Cell, PieChart, Pie, Legend } from "recharts";
import { AlertOctagon, Flame, Clock, TrendingDown, ShieldAlert, PauseCircle, FileWarning, FilterX, Download } from "lucide-react";
import PendenteBackend from "./PendenteBackend";
import { usePmoReports, day, formatHH } from "./pmoData";
import type { PmoSelecionado } from "./pmoMultiCore";
import ConsolidadoBanner from "./ConsolidadoBanner";
import { projectFacts, enrichDocuments, type FactsReport } from "./pmoFacts";

/**
 * Porte literal de ForgeSync Clientapp/src/components/ViewOfensores.tsx (é este arquivo — não
 * ViewAnaliseDesvios.tsx, que é código morto não importado por ninguém no ForgeSync real — que a aba
 * "Análise de Desvios" do TabTimesheet de fato usa). 4 sub-abas (Ofensores, Escopo, Holds, NC), cross-
 * filter real: clique em fatia de Pie/barra filtra a tabela e recalcula os dois gráficos; clique em linha
 * seleciona 1 item; "Limpar Filtros" reseta; exportação CSV client-side mantida.
 *
 * PENDENTE_BACKEND: fonte ForgeSync era GET /api/TimesheetReport/ofensores + GET /api/TimesheetReport/
 * qualidade-contratos (Desvios de Escopo/Pleitos, Holds, Não Conformidades e ofensores de HH/Custo). O
 * N&A não tem hoje tabelas/endpoints equivalentes comprovados para esses 4 conceitos — RNC (Qualidade)
 * já existe mas com contrato diferente (ver `/api/reports/quality`, mantido intacto e não substituído
 * aqui). A geração de documento oficial (Word/Excel) dependia de um backend de Templates que também não
 * existe no N&A ainda — por isso o fetch de template foi removido (nunca chama endpoint inexistente); o
 * botão "Gerar Documento" nunca aparece porque a condição `templateAtivo` nunca fica verdadeira, sem
 * remover o código/comportamento original.
 */
export default function ViewOfensores({ selection }: { selection: PmoSelecionado[] }) {
  const multi = selection.length > 1;
  const [subAba, setSubAba] = useState("ofensores");
  const [filtroStatus, setFiltroStatus] = useState<string | null>(null);
  const [filtroDisciplina, setFiltroDisciplina] = useState<string | null>(null);
  const [linhaSelecionada, setLinhaSelecionada] = useState<any>(null);
  const templateAtivo: any = null;

  // Ofensores: mesma fonte canônica da Gestão à Vista (payload gestao-vista). Sangramento = HH realizado acima do previsto por disciplina
  // (projectFacts); Atraso crônico = documentos ATRASADOS pela classificação canônica, com dias de atraso = referência − data alvo.
  const { reports: canon, loading: canonLoading, error: canonError } = usePmoReports('gestao-vista', { hub: selection[0].hubId, project: selection[0].id, selection });
  const dataOfensores = (() => {
    const fr = canon as FactsReport[];
    if (fr.length === 0) return { disciplinasOff: [] as any[], atrasosCronicos: [] as any[] };
    const nomes = new Map(fr.map(r => [r.projectId, r.nomeProjeto]));
    const ref = day(fr[0].dataReferencia)!;
    const dias = (alvo: string) => Math.round((new Date(ref + 'T12:00:00Z').getTime() - new Date(alvo.slice(0, 10) + 'T12:00:00Z').getTime()) / 86400000);
    const disciplinasOff = projectFacts(fr).disciplines
      .filter(d => d.HH_Realizado !== null && d.HH_Previsto !== null && d.HH_Realizado > d.HH_Previsto)
      .map(d => ({ Disciplina: d.Disciplina, DesvioHH: Math.round((d.HH_Realizado! - d.HH_Previsto!) * 100) / 100 }))
      .sort((a, b) => b.DesvioHH - a.DesvioHH).slice(0, 12);
    const atrasosCronicos = enrichDocuments(fr).filter(d => d.statusDocumento === 'ATRASADO' && d.dataAlvo)
      .map(d => ({ Titulo: d.titulo ?? '—', Codigo: d.codigo ?? '—', Projeto: nomes.get(d.projectId) ?? d.projectId, AvancoFisico: d.avancoFisico, DiasAtraso: dias(d.dataAlvo!) }))
      .sort((a, b) => b.DiasAtraso - a.DiasAtraso).slice(0, 100);
    return { disciplinasOff, atrasosCronicos };
  })();
  const dataQualidade = {
    desvios: { tabela: [] as any[], status: [] as any[], disciplinas: [] as any[] },
    holds: { tabela: [] as any[], status: [] as any[], disciplinas: [] as any[] },
    ncs: { tabela: [] as any[], status: [] as any[], disciplinas: [] as any[] },
  };

  const CORES_STATUS: Record<string, string> = {
    'PENDENTE': '#fb923c', 'CONCLUÍDO': '#22c55e', 'CONCLUÍDA': '#22c55e',
    'EM ANÁLISE': '#facc15', 'APROVADA': '#22c55e', 'REPROVADA': '#f43f5e',
    'ATRASADA': '#f43f5e', 'NO PRAZO': '#22c55e'
  };
  const CORES_DISCIPLINA = ['#38bdf8', '#f59e0b', '#22c55e', '#ef4444', '#a855f7', '#14b8a6', '#f97316'];
  const getCorDisciplina = (index: number) => CORES_DISCIPLINA[Math.abs(index) % CORES_DISCIPLINA.length];
  const tooltipStyle = { backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid hsl(var(--border))', color: 'hsl(var(--popover-foreground))', fontWeight: 'bold', fontSize: '11px' };

  const formatCurrency = (num: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(num);

  const toggleStatus = (status: string) => { setFiltroStatus(prev => prev === status ? null : status); setFiltroDisciplina(null); setLinhaSelecionada(null); };
  const toggleDisciplina = (disc: string) => { setFiltroDisciplina(prev => prev === disc ? null : disc); setFiltroStatus(null); setLinhaSelecionada(null); };
  const limparFiltros = () => { setFiltroStatus(null); setFiltroDisciplina(null); setLinhaSelecionada(null); };
  const handleRowClick = (item: any) => { setLinhaSelecionada((prev: any) => prev === item ? null : item); };

  const agruparDados = (dados: any[], chave: string) => {
    const mapa: Record<string, number> = {};
    dados.forEach(d => { const val = d[chave] || 'N/A'; mapa[val] = (mapa[val] || 0) + 1; });
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
    const csvContent = "﻿" + colunas + '\n' + linhas;
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `${nomeArquivo}_${new Date().toLocaleDateString('pt-BR').replace(/\//g, '-')}.csv`;
    link.click();
  };

  return (
    <div className="space-y-6 animate-enter">

      <div className="flex items-center gap-2">
        <ConsolidadoBanner selection={selection} />
        <span className="text-xs text-slate-500" data-testid="nota-desvios">Ofensores usa a fonte canônica da Gestão à Vista (HH acima do previsto por disciplina; documentos Atrasados). Escopo, Holds e NC aguardam fonte equivalente nesta aba — os dados reais estão em Desvios de Escopo, HOLD e NCR (Controle Operacional).{canonLoading ? ' Carregando…' : ''}{canonError ? ` ${canonError}` : ''}</span>
      </div>

      <div className="flex flex-col gap-4 p-4 rounded-xl border border-rose-900/50 bg-rose-950/20 shadow">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <AlertOctagon className="w-6 h-6 text-rose-500" />
            <div>
              <h2 className="text-lg font-bold text-rose-100">Central de Desvios e Contratos</h2>
              <p className="text-xs text-rose-300/70">Acompanhamento de ofensores financeiros, pleitos de escopo, holds e não conformidades.</p>
            </div>
          </div>
        </div>

        <div className="flex gap-2 overflow-x-auto custom-scrollbar pt-2">
          <button onClick={() => setSubAba("ofensores")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "ofensores" ? "bg-rose-500 text-white shadow-[0_0_10px_rgba(244,63,94,0.4)]" : "bg-slate-800 text-slate-400 hover:text-foreground border border-slate-700 hover:border-slate-500"}`}><TrendingDown className="w-3 h-3" /> Ofensores de HH/Custo</button>
          <button onClick={() => setSubAba("escopo")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "escopo" ? "bg-amber-500 text-white shadow-[0_0_10px_rgba(245,158,11,0.4)]" : "bg-slate-800 text-slate-400 hover:text-foreground border border-slate-700 hover:border-slate-500"}`}><FileWarning className="w-3 h-3" /> Desvios de Escopo (Pleitos)</button>
          <button onClick={() => setSubAba("holds")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "holds" ? "bg-sky-500 text-white shadow-[0_0_10px_rgba(14,165,233,0.4)]" : "bg-slate-800 text-slate-400 hover:text-foreground border border-slate-700 hover:border-slate-500"}`}><PauseCircle className="w-3 h-3" /> Controle de Holds</button>
          <button onClick={() => setSubAba("nc")} className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-xs font-bold rounded-full transition-all ${subAba === "nc" ? "bg-emerald-500 text-white shadow-[0_0_10px_rgba(16,185,129,0.4)]" : "bg-slate-800 text-slate-400 hover:text-foreground border border-slate-700 hover:border-slate-500"}`}><ShieldAlert className="w-3 h-3" /> Não Conformidades (NC)</button>
        </div>
      </div>

      {(filtroStatus || filtroDisciplina || linhaSelecionada) && subAba !== "ofensores" && (
        <div className="flex flex-col md:flex-row items-center justify-between bg-sky-500/10 border border-sky-500/30 p-3 rounded-lg text-sky-400 text-sm animate-enter gap-3">
          <div className="flex items-center gap-2">
            <FilterX className="w-4 h-4" />
            <span>Filtro Ativo: <strong>{linhaSelecionada ? `Destacando Item ${getNomeItemSelecionado()}` : (filtroStatus || filtroDisciplina)}</strong></span>
          </div>
          <div className="flex items-center gap-3">
            {linhaSelecionada && templateAtivo && (
              <button onClick={() => {}} className="flex items-center gap-2 text-[10px] font-black uppercase text-white px-4 py-2 rounded shadow-lg hover:scale-105 transition-all active:scale-95 bg-gradient-to-r from-indigo-600 to-blue-600 shadow-indigo-500/40">
                Imprimir Documento Oficial
              </button>
            )}
            <button onClick={limparFiltros} className="text-[10px] font-black uppercase bg-sky-500 text-white px-4 py-2 rounded transition-all shadow-lg shadow-sky-500/20 hover:bg-sky-400 active:scale-95">Limpar Filtros</button>
          </div>
        </div>
      )}

      {subAba === "ofensores" && (
        <div className="space-y-6 animate-enter">
          <div className="grid gap-6 lg:grid-cols-2">
            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 flex flex-col min-w-0 h-[400px]">
              <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm mb-4"><Flame className="w-4 h-4 text-orange-500" /> Sangramento de HH por Disciplina</h3>
              <div className="flex-1 w-full flex items-center justify-center">
                {dataOfensores.disciplinasOff.length > 0 ? (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={dataOfensores.disciplinasOff} layout="vertical" margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
                      <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                      <XAxis type="number" axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} />
                      <YAxis dataKey="Disciplina" type="category" axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} width={120} />
                      <Tooltip cursor={{ fill: 'hsl(var(--chart-cursor))' }} contentStyle={{ backgroundColor: 'hsl(var(--popover))', borderRadius: '8px', border: '1px solid #f43f5e', color: 'hsl(var(--popover-foreground))' }} />
                      <Bar dataKey="DesvioHH" radius={[0, 4, 4, 0]} barSize={16}>
                        {dataOfensores.disciplinasOff.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={index === 0 ? '#e11d48' : '#f59e0b'} />))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                ) : (<PendenteBackend />)}
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[400px]">
              <div className="p-4 border-b border-slate-700 flex items-center justify-between">
                <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2"><Clock className="w-4 h-4 text-rose-400" /> Atraso Crônico (Fila de Gargalos)</h3>
                <button onClick={() => exportToCSV(dataOfensores.atrasosCronicos, "Atrasos_Cronicos")} className="flex items-center gap-1.5 bg-slate-700 hover:bg-slate-600 text-foreground text-xs font-bold px-3 py-1.5 rounded transition-colors" title="Exportar para Excel"><Download className="w-3.5 h-3.5" /> Exportar</button>
              </div>
              <div className="p-0 overflow-auto custom-scrollbar flex-1">
                {dataOfensores.atrasosCronicos.length === 0 ? (
                  <div className="flex items-center justify-center h-full"><PendenteBackend /></div>
                ) : (
                  <table className="w-full text-xs text-left whitespace-nowrap">
                    <thead className="bg-slate-900 sticky top-0 z-10 text-slate-100"><tr><th className="px-4 py-3 font-bold uppercase tracking-wider">Documento</th><th className="px-4 py-3 font-bold text-center uppercase tracking-wider">% Físico</th><th className="px-4 py-3 font-bold text-right uppercase tracking-wider">Atraso</th></tr></thead>
                    <tbody className="divide-y divide-slate-700/50">
                      {dataOfensores.atrasosCronicos.map((doc: any, idx: number) => (
                        <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                          <td className="px-4 py-3"><div className="font-medium text-slate-200" title={doc.Titulo}>{doc.Titulo}</div><div className="text-[10px] text-slate-500 font-mono">{doc.Codigo}</div>{multi && <div className="text-[10px] text-amber-300 font-bold" data-testid="projeto-ofensor">{doc.Projeto}</div>}</td>
                          <td className="px-4 py-3 text-center"><span className="bg-slate-950 border border-slate-700 px-2 py-1 rounded text-sky-400 font-black">{doc.AvancoFisico === null ? '—' : `${formatHH(doc.AvancoFisico)}%`}</span></td>
                          <td className="px-4 py-3 text-right"><span className="text-rose-500 font-black text-sm">+{doc.DiasAtraso}d</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

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
                  <div className="px-1"><span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Aprovado</span><span className="text-xs font-black text-emerald-400">{formatCurrency(valorAprovado)}</span></div>
                  <div className="px-1"><span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Em Análise</span><span className="text-xs font-black text-amber-400">{formatCurrency(valorEmAnalise)}</span></div>
                  <div className="px-1"><span className="text-[9px] font-bold text-slate-500 uppercase tracking-tight block">Reprovado</span><span className="text-xs font-black text-rose-400">{formatCurrency(valorReprovado)}</span></div>
                </div>
              </div>
            </div>

            <div className="grid gap-6 md:grid-cols-2">
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 relative group flex items-center justify-center">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Status (Filtrável)</h3>
                {chartStatus.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={chartStatus} cx="50%" cy="40%" innerRadius={55} outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e: any) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                        {chartStatus.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={3} className="hover:opacity-80 transition-opacity" />))}
                      </Pie>
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))', fontWeight: 'bold' }} />
                      <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: 'hsl(var(--foreground))', fontWeight: 'bold', paddingTop: '10px' }} onClick={(e: any) => toggleStatus(e.value)} />
                    </PieChart>
                  </ResponsiveContainer>
                )}
              </div>

              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 relative group flex items-center justify-center">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Por Disciplina (Filtrável)</h3>
                {chartDisciplina.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e: any) => { if (e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                      <XAxis dataKey="name" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} allowDecimals={false} />
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))' }} cursor={{ fill: 'hsl(var(--chart-cursor))' }} />
                      <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                        {chartDisciplina.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={2} />))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
              <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                <button onClick={() => exportToCSV(tabelaFiltrada, "Desvios_Escopo")} className="flex items-center gap-1.5 bg-amber-500/20 text-amber-500 hover:bg-amber-500 hover:text-white border border-amber-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all"><Download className="w-3.5 h-3.5" /> Exportar Planilha</button>
              </div>
              <div className="overflow-x-auto custom-scrollbar">
                <table className="w-full text-xs text-left whitespace-nowrap">
                  <thead className="bg-slate-900 text-slate-100"><tr><th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Desvio</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Tipo</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Motivo</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Impacto</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Disciplina</th><th className="px-4 py-4 font-bold text-right uppercase tracking-wider">Esforço (HH)</th><th className="px-4 py-4 font-bold text-right uppercase tracking-wider">Valor CO</th><th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th></tr></thead>
                  <tbody className="divide-y divide-slate-700/50">
                    {tabelaFiltrada.map((d: any, idx: number) => (
                      <tr key={idx} onClick={() => handleRowClick(d)} className={`cursor-pointer transition-all ${linhaSelecionada === d ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`} title="Clique para habilitar a geração de documento">
                        <td className="px-4 py-4 font-black text-amber-500">{d.Num_Desvio}</td>
                        <td className="px-4 py-4"><span className="bg-slate-950 px-2 py-1 border border-slate-700 rounded text-slate-300 font-bold">{d.Tipo_Desvio || 'INTERNO'}</span></td>
                        <td className="px-4 py-4 text-slate-300 font-bold">{d.Categoria_Motivo || '-'}</td>
                        <td className="px-4 py-4 text-slate-300 font-bold">{d.Impacto || '-'}</td>
                        <td className="px-4 py-4 text-slate-400 font-bold">{d.Disciplina}</td>
                        <td className="px-4 py-4 text-right font-black text-sky-400">{d.Quant_HH}h</td>
                        <td className="px-4 py-4 text-right font-black text-emerald-400">{d.Valor_CO > 0 ? formatCurrency(d.Valor_CO) : '-'}</td>
                        <td className="px-4 py-4 text-center"><span className="inline-block w-28 py-1 rounded text-[10px] font-black uppercase tracking-tighter shadow-sm border border-border" style={{ backgroundColor: CORES_STATUS[d.Status], color: '#fff' }}>{d.Status}</span></td>
                      </tr>
                    ))}
                    {tabelaFiltrada.length === 0 && <tr><td colSpan={8} className="text-center py-6 text-slate-500"><PendenteBackend /></td></tr>}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        );
      })()}

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
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 flex items-center justify-center relative">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Status (Filtrável)</h3>
                {chartStatus.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={chartStatus} cx="50%" cy="40%" outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e: any) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                        {chartStatus.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={3} />))}
                      </Pie>
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))' }} />
                      <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: 'hsl(var(--foreground))', fontWeight: 'bold' }} onClick={(e: any) => toggleStatus(e.value)} />
                    </PieChart>
                  </ResponsiveContainer>
                )}
              </div>
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 flex items-center justify-center relative">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Disciplina (Filtrável)</h3>
                {chartDisciplina.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e: any) => { if (e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                      <XAxis dataKey="name" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} allowDecimals={false} />
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))' }} cursor={{ fill: 'hsl(var(--chart-cursor))' }} />
                      <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                        {chartDisciplina.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={2} />))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
              <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                <button onClick={() => exportToCSV(tabelaFiltrada, "Controle_Holds")} className="flex items-center gap-1.5 bg-sky-500/20 text-sky-500 hover:bg-sky-500 hover:text-white border border-sky-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all"><Download className="w-3.5 h-3.5" /> Exportar Planilha</button>
              </div>
              <div className="overflow-x-auto custom-scrollbar">
                <table className="w-full text-xs text-left whitespace-nowrap">
                  <thead className="bg-slate-900 text-slate-100"><tr><th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Hold</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Documento</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Motivo</th><th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Responsável</th></tr></thead>
                  <tbody className="divide-y divide-slate-700/50">
                    {tabelaFiltrada.map((h: any, idx: number) => (
                      <tr key={idx} onClick={() => handleRowClick(h)} className={`cursor-pointer transition-all ${linhaSelecionada === h ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`} title="Clique para habilitar a geração de documento">
                        <td className="px-4 py-4 font-black text-sky-500">{h.Num_Hold}</td>
                        <td className="px-4 py-4"><div className="font-bold text-slate-200">{h.Titulo_Documento} <span className="text-slate-500 font-normal text-[10px]">({h.Revisao})</span></div><div className="text-[10px] text-slate-500 font-mono">{h.Num_NA_Documento} • {h.Disciplina}</div></td>
                        <td className="px-4 py-4 text-slate-300 italic max-w-[250px] truncate" title={h.Motivo_Hold}>{h.Motivo_Hold}</td>
                        <td className="px-4 py-4 text-center"><span className="inline-block w-24 py-1 rounded text-[10px] font-black uppercase tracking-tighter" style={{ backgroundColor: CORES_STATUS[h.Status], color: '#fff' }}>{h.Status}</span></td>
                        <td className="px-4 py-4 text-slate-400 font-bold">{h.Responsavel}</td>
                      </tr>
                    ))}
                    {tabelaFiltrada.length === 0 && <tr><td colSpan={5} className="text-center py-6 text-slate-500"><PendenteBackend /></td></tr>}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        );
      })()}

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
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 flex items-center justify-center relative">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Status (Filtrável)</h3>
                {chartStatus.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={chartStatus} cx="50%" cy="40%" outerRadius={80} paddingAngle={4} dataKey="value" onClick={(e: any) => toggleStatus(e.name)} className="cursor-pointer outline-none">
                        {chartStatus.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={CORES_STATUS[entry.name] || '#64748b'} stroke={filtroStatus === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={3} />))}
                      </Pie>
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))' }} />
                      <Legend verticalAlign="bottom" height={36} wrapperStyle={{ fontSize: '11px', color: 'hsl(var(--foreground))', fontWeight: 'bold' }} onClick={(e: any) => toggleStatus(e.value)} />
                    </PieChart>
                  </ResponsiveContainer>
                )}
              </div>
              <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow p-4 h-72 flex items-center justify-center relative">
                <h3 className="absolute top-4 left-0 right-0 text-[10px] font-black text-slate-100 uppercase tracking-widest text-center">Disciplina (Filtrável)</h3>
                {chartDisciplina.length === 0 ? <PendenteBackend /> : (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={chartDisciplina} margin={{ top: 10, right: 10, left: -20, bottom: 0 }} onClick={(e: any) => { if (e?.activePayload) toggleDisciplina(e.activePayload[0].payload.name); }} className="cursor-pointer">
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.5} />
                      <XAxis dataKey="name" axisLine={{ stroke: 'hsl(var(--chart-grid))' }} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fill: 'hsl(var(--foreground))', fontSize: 10 }} allowDecimals={false} />
                      <Tooltip contentStyle={tooltipStyle} itemStyle={{ color: 'hsl(var(--popover-foreground))' }} cursor={{ fill: 'hsl(var(--chart-cursor))' }} />
                      <Bar dataKey="value" radius={[3, 3, 0, 0]} barSize={30}>
                        {chartDisciplina.map((entry: any, index: number) => (<Cell key={`cell-${index}`} fill={getCorDisciplina(index)} stroke={filtroDisciplina === entry.name ? 'hsl(var(--foreground))' : 'none'} strokeWidth={2} />))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </div>
            </div>

            <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow overflow-hidden">
              <div className="flex justify-end p-2 bg-slate-900 border-b border-slate-700">
                <button onClick={() => exportToCSV(tabelaFiltrada, "Nao_Conformidades")} className="flex items-center gap-1.5 bg-emerald-500/20 text-emerald-500 hover:bg-emerald-500 hover:text-white border border-emerald-500/50 text-xs font-bold px-3 py-1.5 rounded transition-all"><Download className="w-3.5 h-3.5" /> Exportar Planilha</button>
              </div>
              <div className="overflow-x-auto custom-scrollbar">
                <table className="w-full text-xs text-left whitespace-nowrap">
                  <thead className="bg-slate-900 text-slate-100"><tr><th className="px-4 py-4 font-bold uppercase tracking-wider">Nº Doc</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Tipo</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Empresa</th><th className="px-4 py-4 font-bold uppercase tracking-wider">Disciplina</th><th className="px-4 py-4 font-bold text-center uppercase tracking-wider">Status</th></tr></thead>
                  <tbody className="divide-y divide-slate-700/50">
                    {tabelaFiltrada.map((nc: any, idx: number) => (
                      <tr key={idx} onClick={() => handleRowClick(nc)} className={`cursor-pointer transition-all ${linhaSelecionada === nc ? 'bg-indigo-900/40 shadow-[inset_0_0_0_1px_#6366f1]' : 'hover:bg-slate-700/30'}`} title="Clique para habilitar a geração de documento">
                        <td className="px-4 py-4 font-black text-emerald-500">{nc.Num_Doc}</td>
                        <td className="px-4 py-4"><span className="bg-slate-950 px-2 py-1 rounded text-emerald-400 font-black border border-emerald-900/50">{nc.Tipo_Registro}</span></td>
                        <td className="px-4 py-4 text-slate-300 font-bold">{nc.Empresa}</td>
                        <td className="px-4 py-4 text-slate-400">{nc.Disciplina}</td>
                        <td className="px-4 py-4 text-center"><span className="inline-block w-24 py-1 rounded text-[10px] font-black uppercase tracking-tighter" style={{ backgroundColor: CORES_STATUS[nc.Status], color: '#fff' }}>{nc.Status}</span></td>
                      </tr>
                    ))}
                    {tabelaFiltrada.length === 0 && <tr><td colSpan={5} className="text-center py-6 text-slate-500"><PendenteBackend /></td></tr>}
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
