import { useState, useEffect } from "react";
import { motion } from "framer-motion";
import { LayoutList, RefreshCw, Loader2, CheckCircle2, AlertTriangle, Box, GitBranch, XCircle, Search, Filter } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "react-hot-toast";
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip as RechartsTooltip, Legend } from 'recharts';

type ProjectOption = { projectId: string; projectName: string; };

type DashboardData = {
  resumo: {
    totalAtivos: number; totalPID: number; total3D: number;
    matchPerfeito: number; divergencias: number; faltamNo3D: number; faltamNoPID: number;
  };
  itens: any[];
};

const escapeRegExp = (string: string) => string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

export default function TabDashboard() {
  const [availableProjects, setAvailableProjects] = useState<ProjectOption[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string>("");
  const [isLoading, setIsLoading] = useState(false);
  const [dashboardData, setDashboardData] = useState<DashboardData | null>(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeCategory, setActiveCategory] = useState<string>("Todos");

  const fetchProjects = async () => {
    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/list-projects");
      const data = await response.json();
      if (data.sucesso) {
        setAvailableProjects(data.projetos);
        if (data.projetos.length > 0) {
          setSelectedProjectId(data.projetos[0].projectId);
          loadDashboard(data.projetos[0].projectId);
        }
      }
    } catch (error) { console.error("Erro ao listar projetos:", error); }
  };

  const loadDashboard = async (id: string) => {
    if (!id) return;
    setIsLoading(true);
    setDashboardData(null); 
    try {
      const response = await fetch(`http://localhost:5277/api/Plant3D/dashboard-data?projectId=${id}`);
      const data = await response.json();
      if (data.sucesso && data.temDados) setDashboardData(data);
    } catch (error) { toast.error("Erro ao carregar dados do dashboard."); } 
    finally { setIsLoading(false); }
  };

  useEffect(() => { fetchProjects(); }, []);

  const handleProjectChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const id = e.target.value;
    setSelectedProjectId(id);
    setSearchTerm("");
    setActiveCategory("Todos");
    loadDashboard(id);
  };

  const categoriasDisponiveis = dashboardData ? Array.from(new Set(dashboardData.itens.map(item => item.categoria))) : [];

  const filteredItems = dashboardData?.itens.filter(item => {
    const matchSearch = item.tag?.toLowerCase().includes(searchTerm.toLowerCase()) || item.status_Cruzamento?.toLowerCase().includes(searchTerm.toLowerCase());
    const matchCategory = activeCategory === "Todos" || item.categoria === activeCategory;
    return matchSearch && matchCategory;
  }) || [];

  const kpiMatchPerfeito = filteredItems.filter(i => i.status_Cruzamento === "Match Perfeito").length;
  const kpiDivergencias = filteredItems.filter(i => i.status_Cruzamento?.includes("Divergência")).length;
  const kpiFaltam3D = filteredItems.filter(i => i.status_Cruzamento === "Falta Modelar no 3D").length;
  const kpiFaltamPID = filteredItems.filter(i => i.status_Cruzamento === "Não existe no P&ID").length;

  // 🎯 CORREÇÃO AQUI: Varre TODAS as linhas filtradas para descobrir as colunas
  let colunasDinamicas: string[] = [];
  if (activeCategory !== "Todos" && filteredItems.length > 0) {
    const chavesEncontradas = new Set<string>();
    
  filteredItems.forEach(item => {
      try {
        const jsonCru = item.tag_Fatiado_JSON || item.Tag_Fatiado_JSON;
        if (jsonCru && jsonCru !== "[]") {
          Object.keys(JSON.parse(jsonCru)).forEach(k => chavesEncontradas.add(k));
        }
      } catch (e) {
        // O linter exige que o bloco não fique vazio.
        console.warn("Ignorando JSON inválido nesta linha:", e);
      }
    });

    // Converte o Set para Array e ordena (para "Posição 1", "Posição 2" ficarem na ordem)
    colunasDinamicas = Array.from(chavesEncontradas).sort((a, b) => a.localeCompare(b));
  }

  // 🎯 DADOS DO GRÁFICO DE SAÚDE
  const pieData = [
    { name: 'OK (Sincronizado)', value: kpiMatchPerfeito, color: '#10b981' }, 
    { name: 'Erros de Atributo', value: kpiDivergencias, color: '#f59e0b' },  
    { name: 'Erros de TAG/Modelagem', value: kpiFaltam3D + kpiFaltamPID, color: '#ef4444' } 
  ].filter(d => d.value > 0); 

  const totalItensAvaliados = kpiMatchPerfeito + kpiDivergencias + kpiFaltam3D + kpiFaltamPID;
  const percentualSaude = totalItensAvaliados > 0 ? ((kpiMatchPerfeito / totalItensAvaliados) * 100).toFixed(1) : "0.0";

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      
      {/* HEADER */}
      <div className="glass-card p-6 rounded-xl border border-border/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex-1">
          <h2 className="text-2xl font-bold text-primary">Resultados da Auditoria</h2>
          <div className="mt-4 flex items-center gap-3">
            <label htmlFor="dashboardProject" className="text-sm font-medium text-muted-foreground flex items-center gap-2">
              <LayoutList className="w-4 h-4" /> Selecionar Projeto:
            </label>
            <select id="dashboardProject" title="Selecione o Projeto" value={selectedProjectId} onChange={handleProjectChange} className="bg-background border border-border rounded-lg px-3 py-2 text-sm font-bold text-foreground focus:ring-2 focus:ring-primary outline-none min-w-[300px]">
              <option value="">-- Escolha um Projeto --</option>
              {availableProjects.map(p => (<option key={p.projectId} value={p.projectId}>{p.projectName}</option>))}
            </select>
          </div>
        </div>
        <Button onClick={() => loadDashboard(selectedProjectId)} disabled={isLoading || !selectedProjectId} className="gap-2 font-bold" title="Atualizar Dados">
          {isLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCw className="w-4 h-4" />}
          ATUALIZAR DADOS
        </Button>
      </div>

      {isLoading && <div className="flex justify-center p-12"><Loader2 className="w-12 h-12 animate-spin text-primary" /></div>}

      {!isLoading && !dashboardData && selectedProjectId && (
        <div className="glass-card p-12 rounded-xl border border-border/50 text-center">
          <Box className="w-16 h-16 text-muted-foreground/30 mx-auto mb-4" />
          <h3 className="text-xl font-bold text-foreground">Nenhuma auditoria encontrada</h3>
          <p className="text-muted-foreground mt-2 max-w-md mx-auto">Vá até a aba <b>Mapeamento DE/PARA</b> e clique em <b>Salvar Configuração</b>.</p>
        </div>
      )}

      {!isLoading && dashboardData && (
        <>
          {/* 🎯 GRÁFICO EXECUTIVO DE SAÚDE DO MODELO */}
          {totalItensAvaliados > 0 && (
            <div className="glass-card p-6 rounded-xl border border-border/50 flex flex-col md:flex-row items-center gap-8 bg-card/10">
              <div className="w-full md:w-1/3 h-64 flex flex-col items-center">
                <h3 className="text-lg font-bold text-foreground mb-2 text-center flex items-center gap-2">
                  <Box className="w-5 h-5 text-primary" /> Saúde do Modelo (Digital Twin)
                </h3>
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie
                      data={pieData}
                      cx="50%" cy="50%"
                      innerRadius={65} outerRadius={85}
                      paddingAngle={5} dataKey="value"
                      stroke="none"
                    >
                      {pieData.map((entry, index) => (
                        <Cell key={`cell-${index}`} fill={entry.color} />
                      ))}
                    </Pie>
                    <RechartsTooltip 
                      contentStyle={{ backgroundColor: '#0f172a', borderColor: '#334155', borderRadius: '8px', color: '#f8fafc', fontWeight: 'bold' }}
                      itemStyle={{ color: '#f8fafc' }}
                      formatter={(value: number) => [`${value} itens`, 'Quantidade']}
                    />
                    <Legend verticalAlign="bottom" height={36} iconType="circle" />
                  </PieChart>
                </ResponsiveContainer>
              </div>
              
              <div className="flex-1 space-y-4">
                <div className="p-5 rounded-lg border border-border bg-background/50">
                  <p className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">Aderência ao P&ID</p>
                  <div className="flex items-end gap-3 mt-1">
                    <h1 className={`text-5xl font-black ${Number(percentualSaude) > 90 ? 'text-emerald-400' : Number(percentualSaude) > 70 ? 'text-amber-400' : 'text-red-400'}`}>
                      {percentualSaude}%
                    </h1>
                    <span className="text-sm font-medium text-muted-foreground mb-1.5">de precisão de engenharia</span>
                  </div>
                  <div className="w-full h-2 bg-secondary rounded-full mt-4 overflow-hidden">
                    <motion.div 
                      initial={{ width: 0 }}
                      animate={{ width: `${percentualSaude}%` }}
                      transition={{ duration: 1, ease: "easeOut" }}
                      className={`h-full ${Number(percentualSaude) > 90 ? 'bg-emerald-500' : Number(percentualSaude) > 70 ? 'bg-amber-500' : 'bg-red-500'}`}
                    />
                  </div>
                </div>
                <p className="text-sm text-muted-foreground leading-relaxed">
  Este painel reflete a <strong>Confiabilidade do Modelo (P&ID vs 3D)</strong>. Ele analisa a correspondência exata de elementos e metadados, garantindo que o modelo tridimensional seja um Gêmeo Digital fiel ao fluxograma de engenharia.
</p>
                <p className="text-sm text-muted-foreground leading-relaxed">
                  Este painel reflete o <strong>Avanço Físico Real</strong>. Ele analisa não apenas a presença do elemento físico no 3D, mas também a integridade de todas as suas propriedades (metadados) em relação ao fluxograma base.
                </p>
              </div>
            </div>
          )}

          {/* CARDS */}
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="glass-card p-4 rounded-xl border-l-4 border-l-emerald-500">
              <p className="text-sm text-muted-foreground font-semibold">Match Perfeito</p>
              <div className="flex items-center justify-between mt-2"><h3 className="text-3xl font-bold text-emerald-400">{kpiMatchPerfeito}</h3><CheckCircle2 className="w-8 h-8 text-emerald-500/20" /></div>
            </div>
            <div className="glass-card p-4 rounded-xl border-l-4 border-l-amber-500">
              <p className="text-sm text-muted-foreground font-semibold">Divergências</p>
              <div className="flex items-center justify-between mt-2"><h3 className="text-3xl font-bold text-amber-400">{kpiDivergencias}</h3><AlertTriangle className="w-8 h-8 text-amber-500/20" /></div>
            </div>
            <div className="glass-card p-4 rounded-xl border-l-4 border-l-red-500">
              <p className="text-sm text-muted-foreground font-semibold">Faltam no 3D</p>
              <div className="flex items-center justify-between mt-2"><h3 className="text-3xl font-bold text-red-400">{kpiFaltam3D}</h3><Box className="w-8 h-8 text-red-500/20" /></div>
            </div>
            <div className="glass-card p-4 rounded-xl border-l-4 border-l-orange-500">
              <p className="text-sm text-muted-foreground font-semibold">Não Existem no P&ID</p>
              <div className="flex items-center justify-between mt-2"><h3 className="text-3xl font-bold text-orange-400">{kpiFaltamPID}</h3><GitBranch className="w-8 h-8 text-orange-500/20" /></div>
            </div>
          </div>

          {/* TABELA DE DETALHAMENTO FICA AQUI */}
          <div className="glass-card rounded-xl border border-border/50 overflow-hidden flex flex-col">
            <div className="p-4 border-b border-border/50 bg-card/30 space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                <h3 className="font-bold text-lg flex items-center gap-2"><Filter className="w-5 h-5 text-primary" /> Detalhamento</h3>
                <div className="relative">
                  <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
                  <input type="text" placeholder="Pesquisar..." value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} className="pl-9 pr-4 py-2 bg-background border border-border rounded-lg text-sm w-full sm:w-64 focus:outline-none focus:border-primary" />
                </div>
              </div>
              <div className="flex flex-wrap gap-2 pt-2">
                <button type="button" onClick={() => setActiveCategory("Todos")} className={`px-4 py-1.5 rounded-full text-xs font-bold transition-colors ${activeCategory === "Todos" ? "bg-primary text-primary-foreground" : "bg-background/50 text-muted-foreground hover:bg-background border border-border/50"}`}>Todos</button>
                {categoriasDisponiveis.map(cat => (
                  <button key={cat} type="button" onClick={() => setActiveCategory(cat as string)} className={`px-4 py-1.5 rounded-full text-xs font-bold transition-colors ${activeCategory === cat ? "bg-primary text-primary-foreground" : "bg-background/50 text-muted-foreground hover:bg-background border border-border/50"}`}>{cat as string}</button>
                ))}
              </div>
            </div>

            <div className="overflow-x-auto max-h-[500px] overflow-y-auto custom-scrollbar">
              <table className="w-full text-sm text-left">
                <thead className="text-[10px] text-muted-foreground uppercase bg-background/90 sticky top-0 z-10 backdrop-blur-md border-b border-border/50">
                  <tr>
                    <th className="px-5 py-4 font-bold tracking-wider">TAG COMPLETA</th>
                    {activeCategory === "Todos" && <th className="px-5 py-4 font-bold tracking-wider">Categoria</th>}
                    {colunasDinamicas.map(col => <th key={col} className="px-5 py-4 font-black text-primary tracking-wider">{col}</th>)}
                    <th className="px-5 py-4 font-bold tracking-wider text-center">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/30">
                  {filteredItems.map((item, idx) => {
                    let pedacosTag: Record<string, string> = {};
                    try {
                      const jsonStr = item.tag_Fatiado_JSON || item.Tag_Fatiado_JSON;
                      if (jsonStr) pedacosTag = JSON.parse(jsonStr);
                    } catch(e) { console.warn("Aviso: JSON incorreto.", e); }

                    const statusStr = item.status_Cruzamento || "";
                    const isDivergente = statusStr.includes("Divergência");
                    const isMatch = statusStr === "Match Perfeito";
                    const isFalta3D = statusStr === "Falta Modelar no 3D";
                    const isFaltaPID = statusStr === "Não existe no P&ID";

                    let displayTagPid = item.tag;
                    let displayTag3D = "";
                    if (item.tag.includes("(PID)") && item.tag.includes("(3D)")) {
                        const parts = item.tag.split("/");
                        displayTagPid = parts[0].replace("(PID)", "").trim();
                        displayTag3D = parts[1]?.replace("(3D)", "").trim();
                    }

                    const rowBg = isDivergente ? "bg-amber-500/5 hover:bg-amber-500/10" 
                                : isFalta3D ? "bg-red-500/5 hover:bg-red-500/10" 
                                : isFaltaPID ? "bg-orange-500/5 hover:bg-orange-500/10" 
                                : "hover:bg-primary/5";

                    return (
                      <tr key={idx} className={`transition-colors ${rowBg}`}>
                        <td className="px-5 py-3">
                          <div className="flex flex-col gap-1 w-max">
                            <span className="font-bold text-emerald-400/90 text-xs flex items-center gap-1.5"><span className="text-[9px] bg-emerald-500/20 px-1.5 py-0.5 rounded text-emerald-300">P&ID</span> {displayTagPid}</span>
                            {displayTag3D && <span className="font-bold text-blue-400/90 text-xs flex items-center gap-1.5"><span className="text-[9px] bg-blue-500/20 px-1.5 py-0.5 rounded text-blue-300">3D</span> {displayTag3D}</span>}
                          </div>
                        </td>
                        
                        {activeCategory === "Todos" && <td className="px-5 py-3 text-muted-foreground text-xs">{item.categoria}</td>}
                        
                        {colunasDinamicas.map(col => {
                          const colVal = pedacosTag[col] || "—";
                          const regex = new RegExp(`${escapeRegExp(col)} \\(PID: (.*?) x 3D: (.*?)\\)`);
                          const match = statusStr.match(regex);

                          if (match) {
                            return (
                              <td key={col} className="px-5 py-2 align-middle">
                                <div className="flex flex-col w-max">
                                  <span className="text-[11px] text-emerald-400 font-black tracking-wide bg-emerald-500/10 px-2 py-0.5 rounded-t-md border-b border-border/50">{match[1]}</span>
                                  <span className="text-[11px] text-blue-400 font-black tracking-wide bg-blue-500/10 px-2 py-0.5 rounded-b-md">{match[2]}</span>
                                </div>
                              </td>
                            );
                          }
                          return (
                            <td key={col} className={`px-5 py-3 font-mono text-[11px] font-semibold ${isMatch ? 'text-foreground/80' : 'text-muted-foreground'}`}>
                              {colVal}
                            </td>
                          );
                        })}

                        <td className="px-5 py-3 text-center">
                          <div className="flex justify-center">
                            {isMatch && <div className="px-3 py-1 bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 rounded-full text-xs font-bold flex items-center gap-1.5" title="Match Perfeito"><CheckCircle2 className="w-3.5 h-3.5" /> OK</div>}
                            {isFalta3D && <div className="px-3 py-1 bg-red-500/10 border border-red-500/20 text-red-400 rounded-full text-xs font-bold flex items-center gap-1.5" title="Falta Modelar no 3D"><XCircle className="w-3.5 h-3.5" /> Faltando no 3D</div>}
                            {isFaltaPID && <div className="px-3 py-1 bg-orange-500/10 border border-orange-500/20 text-orange-400 rounded-full text-xs font-bold flex items-center gap-1.5" title="Não existe no P&ID"><GitBranch className="w-3.5 h-3.5" /> Sobrando no 3D</div>}
                            {isDivergente && <div className="px-3 py-1 bg-amber-500/10 border border-amber-500/20 text-amber-400 rounded-full text-xs font-bold flex items-center gap-1.5" title="Divergência Encontrada"><AlertTriangle className="w-3.5 h-3.5" /> Divergência</div>}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </motion.div>
  );
}