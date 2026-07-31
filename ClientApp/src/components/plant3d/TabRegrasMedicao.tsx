import { useState, useEffect } from "react";
import { motion } from "framer-motion";
import { Scale, ShieldAlert, Save, CheckCircle2, AlertOctagon, SlidersHorizontal, Settings2, LayoutList } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "react-hot-toast";

// 🎯 TIPO PARA O PROJETO
type ProjectOption = { projectId: string; projectName: string; };

export default function TabRegrasMedicao() {
  const [isLoading, setIsLoading] = useState(false);
  
  // 🎯 ESTADOS DO PROJETO
  const [availableProjects, setAvailableProjects] = useState<ProjectOption[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string>("");

  const [pesos, setPesos] = useState({ tubulacao: 60, valvulas: 25, instrumentos: 15, suportes: 0 });
  const [statusRules, setStatusRules] = useState([
    { id: "match", nome: "Match Perfeito", percentual: 100, cor: "text-emerald-400" },
    { id: "div_spec", nome: "Divergência de Spec", percentual: 50, cor: "text-amber-400" },
    { id: "div_tag", nome: "Divergência de TAG", percentual: 0, cor: "text-red-400" },
    { id: "falta_3d", nome: "Falta no Modelo 3D", percentual: 0, cor: "text-muted-foreground" },
    { id: "nao_previsto", nome: "Não Previsto no P&ID", percentual: 0, cor: "text-blue-400" }
  ]);

  const somaPesos = pesos.tubulacao + pesos.valvulas + pesos.instrumentos + pesos.suportes;

  // 🎯 CARREGA OS PROJETOS QUANDO A TELA ABRE
  useEffect(() => {
    const fetchProjects = async () => {
      try {
        const response = await fetch("http://localhost:5277/api/Plant3D/list-projects");
        const data = await response.json();
        if (data.sucesso) {
          setAvailableProjects(data.projetos);
          if (data.projetos.length > 0) setSelectedProjectId(data.projetos[0].projectId);
        }
      } catch (error) { console.error("Erro ao listar projetos:", error); }
    };
    fetchProjects();
  }, []);

  // 🎯 CARREGA AS REGRAS DO PROJETO QUANDO ELE MUDA
  useEffect(() => {
    if (!selectedProjectId) return;
    
    const loadRules = async () => {
      try {
        // Vamos buscar as regras reais do banco depois, por enquanto tenta pegar do cache do projeto
        const cachedRules = localStorage.getItem(`plant3d_regras_${selectedProjectId}`);
        if (cachedRules) {
          const parsed = JSON.parse(cachedRules);
          setPesos(parsed.pesos);
          setStatusRules(parsed.statusRules);
        } else {
          // Se não tiver regra, volta pro padrão
          setPesos({ tubulacao: 60, valvulas: 25, instrumentos: 15, suportes: 0 });
        }
      } catch (error) { console.error(error); }
    };
    loadRules();
  }, [selectedProjectId]);

  const handlePesoChange = (categoria: keyof typeof pesos, valor: string) => {
    let num = parseInt(valor) || 0;
    if (num < 0) num = 0;
    if (num > 100) num = 100;

    const somaOutros = Object.entries(pesos).filter(([key]) => key !== categoria).reduce((soma, [, val]) => soma + val, 0);
    if (num + somaOutros > 100) num = 100 - somaOutros;

    setPesos(prev => ({ ...prev, [categoria]: num }));
  };

  const handleStatusChange = (id: string, valor: string) => {
    const num = parseInt(valor) || 0;
    setStatusRules(prev => prev.map(rule => rule.id === id ? { ...rule, percentual: num > 100 ? 100 : num < 0 ? 0 : num } : rule));
  };

  // 🎯 AGORA SALVA DE VERDADE NO BACKEND (E NO CACHE DO PROJETO)
  const handleSaveRules = async () => {
    if (!selectedProjectId) return toast.error("Selecione o projeto primeiro!");
    if (somaPesos !== 100) return toast.error(`A soma dos pesos deve ser 100%. (Atual: ${somaPesos}%)`);

    setIsLoading(true);
    
    try {
      const payload = {
        projectId: selectedProjectId,
        pesos: pesos,
        statusRules: statusRules
      };

      const response = await fetch("http://localhost:5277/api/Plant3D/save-measurement-rules", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      const data = await response.json();

      if (response.ok && data.sucesso) {
        // Salva localmente também só para carregar rápido na interface
        localStorage.setItem(`plant3d_regras_${selectedProjectId}`, JSON.stringify({ pesos, statusRules }));
        toast.success("Regras salvas no banco de dados com sucesso!");
      } else {
        toast.error(data.mensagem || "Falha ao salvar no banco de dados.");
      }
    } catch (error) {
      toast.error("Erro de conexão com o servidor C#.");
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      
      {/* 🎯 CABEÇALHO ATUALIZADO COM SELETOR DE PROJETO */}
      <div className="glass-card p-6 rounded-xl border border-border/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex-1">
          <h2 className="text-2xl font-bold text-primary flex items-center gap-2">
            <Scale className="w-6 h-6" /> Critérios de Medição (Rules of Credit)
          </h2>
          <div className="mt-4 flex items-center gap-3">
            <label htmlFor="projectSelectorRules" className="text-sm font-medium text-muted-foreground flex items-center gap-2">
              <LayoutList className="w-4 h-4" /> Selecionar Projeto:
            </label>
            <select 
              id="projectSelectorRules" 
              title="Selecione o Projeto" 
              value={selectedProjectId} 
              onChange={(e) => setSelectedProjectId(e.target.value)} 
              className="bg-background border border-border rounded-lg px-3 py-2 text-sm font-bold text-foreground focus:ring-2 focus:ring-primary outline-none min-w-[300px]"
            >
              <option value="">-- Escolha um Projeto --</option>
              {availableProjects.map(p => (<option key={p.projectId} value={p.projectId}>{p.projectName}</option>))}
            </select>
          </div>
        </div>
        <Button onClick={handleSaveRules} disabled={isLoading || somaPesos !== 100 || !selectedProjectId} className="gap-2 font-bold bg-primary hover:bg-primary/90">
          {isLoading ? <span className="animate-spin text-xl">⚙️</span> : <Save className="w-4 h-4" />}
          GUARDAR REGRAS
        </Button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        <div className="glass-card rounded-xl border border-border/50 overflow-hidden flex flex-col">
          <div className="bg-card/40 border-b border-border/50 p-4 flex items-center gap-2">
            <SlidersHorizontal className="w-5 h-5 text-primary" />
            <h3 className="font-bold text-lg text-foreground">Distribuição de Pesos (Linha)</h3>
          </div>
          
          <div className="p-6 flex-1 flex flex-col space-y-6">
            <div className="space-y-4">
              <p className="text-sm text-muted-foreground mb-4">
                Quanto representa cada componente no esforço total de conclusão de uma linha de tubulação?
              </p>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="space-y-2 bg-background/50 p-3 rounded-lg border border-border/50">
                  <label htmlFor="peso-tubulacao" className="text-xs font-bold uppercase text-muted-foreground flex justify-between">
                    Tubulação / Tubos <span>{pesos.tubulacao}%</span>
                  </label>
                  <input id="peso-tubulacao" title="Peso da Tubulação" aria-label="Peso da Tubulação" type="range" min="0" max="100" value={pesos.tubulacao} onChange={(e) => handlePesoChange('tubulacao', e.target.value)} className="w-full accent-primary" />
                </div>
                
                <div className="space-y-2 bg-background/50 p-3 rounded-lg border border-border/50">
                  <label htmlFor="peso-valvulas" className="text-xs font-bold uppercase text-muted-foreground flex justify-between">
                    Válvulas <span>{pesos.valvulas}%</span>
                  </label>
                  <input id="peso-valvulas" title="Peso das Válvulas" aria-label="Peso das Válvulas" type="range" min="0" max="100" value={pesos.valvulas} onChange={(e) => handlePesoChange('valvulas', e.target.value)} className="w-full accent-primary" />
                </div>

                <div className="space-y-2 bg-background/50 p-3 rounded-lg border border-border/50">
                  <label htmlFor="peso-instrumentos" className="text-xs font-bold uppercase text-muted-foreground flex justify-between">
                    Instrumentos <span>{pesos.instrumentos}%</span>
                  </label>
                  <input id="peso-instrumentos" title="Peso dos Instrumentos" aria-label="Peso dos Instrumentos" type="range" min="0" max="100" value={pesos.instrumentos} onChange={(e) => handlePesoChange('instrumentos', e.target.value)} className="w-full accent-primary" />
                </div>

                <div className="space-y-2 bg-background/50 p-3 rounded-lg border border-border/50">
                  <label htmlFor="peso-suportes" className="text-xs font-bold uppercase text-muted-foreground flex justify-between">
                    Suportes <span>{pesos.suportes}%</span>
                  </label>
                  <input id="peso-suportes" title="Peso dos Suportes" aria-label="Peso dos Suportes" type="range" min="0" max="100" value={pesos.suportes} onChange={(e) => handlePesoChange('suportes', e.target.value)} className="w-full accent-primary" />
                </div>
              </div>
            </div>

            <div className="mt-auto pt-4 border-t border-border/50">
              <div className="flex justify-between items-end mb-2">
                <span className="text-sm font-bold text-foreground">Soma Total:</span>
                <span className={`text-2xl font-black ${somaPesos === 100 ? 'text-emerald-400' : 'text-red-400'}`}>
                  {somaPesos}%
                </span>
              </div>
              <div className="w-full h-3 bg-secondary rounded-full overflow-hidden flex">
                <div style={{ width: `${(pesos.tubulacao / 100) * 100}%` }} className="bg-blue-500 h-full transition-all" title={`Tubulação: ${pesos.tubulacao}%`}></div>
                <div style={{ width: `${(pesos.valvulas / 100) * 100}%` }} className="bg-amber-500 h-full transition-all" title={`Válvulas: ${pesos.valvulas}%`}></div>
                <div style={{ width: `${(pesos.instrumentos / 100) * 100}%` }} className="bg-purple-500 h-full transition-all" title={`Instrumentos: ${pesos.instrumentos}%`}></div>
                <div style={{ width: `${(pesos.suportes / 100) * 100}%` }} className="bg-gray-400 h-full transition-all" title={`Suportes: ${pesos.suportes}%`}></div>
              </div>
            </div>
          </div>
        </div>

        <div className="glass-card rounded-xl border border-border/50 overflow-hidden">
          <div className="bg-card/40 border-b border-border/50 p-4 flex items-center gap-2">
            <ShieldAlert className="w-5 h-5 text-primary" />
            <h3 className="font-bold text-lg text-foreground">Matriz de Qualidade e Penalização</h3>
          </div>
          
          <div className="p-6 space-y-4">
            <p className="text-sm text-muted-foreground mb-4">
              Se um item for modelado, quanto avanço é libertado com base na auditoria (Status)?
            </p>

            <div className="space-y-3">
              {statusRules.map((rule) => (
                <div key={rule.id} className="flex items-center justify-between p-3 bg-background/50 rounded-lg border border-border/50 hover:border-primary/30 transition-colors">
                  <div className="flex items-center gap-3">
                    <CheckCircle2 className={`w-5 h-5 ${rule.cor}`} />
                    <div>
                      <label htmlFor={`status-${rule.id}`} className="text-sm font-bold text-foreground cursor-pointer">{rule.nome}</label>
                      <p className="text-xs text-muted-foreground">Progresso atribuído ao item</p>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <input 
                      id={`status-${rule.id}`}
                      title={`Percentual para ${rule.nome}`}
                      aria-label={`Percentual para ${rule.nome}`}
                      type="number" 
                      min="0" max="100" 
                      value={rule.percentual} 
                      onChange={(e) => handleStatusChange(rule.id, e.target.value)}
                      className="w-16 bg-background border border-border rounded px-2 py-1.5 text-sm font-bold text-center focus:border-primary outline-none"
                    />
                    <span className="text-muted-foreground font-bold">%</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

      </div>
    </motion.div>
  );
}