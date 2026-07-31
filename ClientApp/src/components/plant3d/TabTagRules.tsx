import { useState, useEffect } from "react";
import { motion } from "framer-motion";
import { LayoutList, Save, Loader2, Plus, Trash2, SplitSquareHorizontal, AlertOctagon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "react-hot-toast";

type ProjectOption = { projectId: string; projectName: string; };
type TagPart = { id: string; name: string; };
type CategoryRule = { categoria: string; delimitador: string; partes: TagPart[]; };

const CATEGORIAS_BASE = ["Equipamentos", "Válvulas", "Instrumentos", "Linhas"];

export default function TabTagRules() {
  const [availableProjects, setAvailableProjects] = useState<ProjectOption[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string>("");
  const [isSaving, setIsSaving] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  const [rules, setRules] = useState<CategoryRule[]>(
    CATEGORIAS_BASE.map(cat => ({ categoria: cat, delimitador: "-", partes: [] }))
  );

  const fetchProjects = async () => {
    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/list-projects");
      const data = await response.json();
      if (data.sucesso) {
        setAvailableProjects(data.projetos);
        if (data.projetos.length > 0 && !selectedProjectId) {
          setSelectedProjectId(data.projetos[0].projectId);
        }
      }
    } catch (error) {
      console.error("Erro ao listar projetos:", error);
    }
  };

  const loadRules = async (id: string) => {
    if (!id || id === "PROJ-PADRAO") return;
    setIsLoading(true);
    try {
      const response = await fetch(`http://localhost:5277/api/Plant3D/project-context?projectId=${id}`);
      const data = await response.json();
      
      if (data.sucesso && data.regrasTagJson && data.regrasTagJson !== "[]") {
        setRules(JSON.parse(data.regrasTagJson));
      } else {
        setRules(CATEGORIAS_BASE.map(cat => ({ categoria: cat, delimitador: "-", partes: [] })));
      }
    } catch (error) {
      console.error("Erro ao carregar regras.");
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => { fetchProjects(); }, []);
  useEffect(() => { if (selectedProjectId) loadRules(selectedProjectId); }, [selectedProjectId]);

  const handleSaveRules = async () => {
    if (!selectedProjectId) { toast.error("Selecione um projeto antes de salvar."); return; }
    
    setIsSaving(true);
    const formData = new FormData();
    formData.append("projectId", selectedProjectId);
    formData.append("regrasJson", JSON.stringify(rules));

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/save-tag-rules", { method: "POST", body: formData });
      if (response.ok) toast.success("Regras salvas! Não esqueça de rodar uma nova auditoria.");
      else toast.error("Erro ao salvar regras no servidor.");
    } catch (error) { toast.error("Falha na conexão."); } 
    finally { setIsSaving(false); }
  };

  // 🎯 NOVA FUNÇÃO DE EXCLUSÃO (Zera as regras)
  const handleDeleteRules = async () => {
    if (!selectedProjectId) return;
    
    if (!window.confirm("Atenção: Isso apagará todas as regras de tag deste projeto. Deseja continuar?")) return;

    setIsSaving(true);
    
    // Reseta para o estado vazio
    const emptyRules = CATEGORIAS_BASE.map(cat => ({ categoria: cat, delimitador: "-", partes: [] }));
    
    const formData = new FormData();
    formData.append("projectId", selectedProjectId);
    formData.append("regrasJson", "[]"); // Envia array vazio pro banco

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/save-tag-rules", { method: "POST", body: formData });
      if (response.ok) {
        setRules(emptyRules);
        toast.success("Regras excluídas. O projeto voltou ao padrão.");
      } else {
        toast.error("Erro ao excluir regras no servidor.");
      }
    } catch (error) { toast.error("Falha na conexão."); } 
    finally { setIsSaving(false); }
  };

  const updateDelimiter = (index: number, val: string) => {
    const newR = [...rules]; newR[index].delimitador = val; setRules(newR);
  };

  const addPart = (catIndex: number) => {
    const newR = [...rules];
    newR[catIndex].partes.push({ id: Math.random().toString(), name: `Posição ${newR[catIndex].partes.length + 1}` });
    setRules(newR);
  };

  const removePart = (catIndex: number, partIndex: number) => {
    const newR = [...rules]; newR[catIndex].partes.splice(partIndex, 1); setRules(newR);
  };

  const updatePartName = (catIndex: number, partIndex: number, val: string) => {
    const newR = [...rules]; newR[catIndex].partes[partIndex].name = val; setRules(newR);
  };

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      
      <div className="glass-card p-6 rounded-xl border border-border/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex-1">
          <h2 className="text-xl font-bold flex items-center gap-2 text-primary">
            <SplitSquareHorizontal className="w-6 h-6" /> Regras de Nomenclatura (TAGs)
          </h2>
          <div className="mt-4 flex items-center gap-3">
            <label htmlFor="projectSelector" className="text-sm font-medium text-muted-foreground flex items-center gap-2">
              <LayoutList className="w-4 h-4" /> Projeto:
            </label>
            <select 
              id="projectSelector" title="Selecione o Projeto" aria-label="Selecione o Projeto"
              value={selectedProjectId} onChange={(e) => setSelectedProjectId(e.target.value)}
              className="bg-background border border-border rounded-lg px-3 py-2 text-sm font-bold focus:ring-2 focus:ring-primary outline-none min-w-[300px]"
            >
              <option value="">-- Escolha um Projeto --</option>
              {availableProjects.map(p => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}
            </select>
          </div>
        </div>

        {/* 🎯 BOTÕES DE AÇÃO (SALVAR E EXCLUIR) */}
        <div className="flex items-center gap-3">
          <Button 
            variant="outline"
            onClick={handleDeleteRules} 
            disabled={isSaving || isLoading || !selectedProjectId} 
            title="Excluir todas as regras" 
            className="font-bold gap-2 text-red-400 hover:text-red-500 hover:bg-red-500/10 border-red-500/20"
          >
            <AlertOctagon className="w-4 h-4" /> LIMPAR REGRAS
          </Button>
          
          <Button onClick={handleSaveRules} disabled={isSaving || isLoading || !selectedProjectId} title="Salvar Regras" className="font-bold gap-2">
            {isSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            SALVAR REGRAS
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        {rules.map((rule, catIdx) => (
          <div key={rule.categoria} className="glass-card p-5 rounded-lg border border-border/50">
            <div className="flex items-center justify-between mb-4 border-b border-border/30 pb-3">
              <h4 className="font-bold text-foreground text-lg">{rule.categoria}</h4>
              <div className="flex items-center gap-2 bg-background px-3 py-1.5 rounded-md border border-border">
                <label htmlFor={`delimitador-${rule.categoria}`} className="text-xs font-semibold text-muted-foreground uppercase">Separador:</label>
                <input 
                  id={`delimitador-${rule.categoria}`} title={`Separador para ${rule.categoria}`}
                  type="text" maxLength={3} value={rule.delimitador} onChange={(e) => updateDelimiter(catIdx, e.target.value)}
                  className="w-10 text-center font-mono font-bold bg-transparent outline-none border-b border-dashed border-primary text-primary"
                />
              </div>
            </div>

            <div className="space-y-3">
              <p className="text-xs text-muted-foreground mb-2">Defina as partes que compõem o TAG na ordem correta.</p>
              
              {rule.partes.length === 0 && (
                <div className="text-xs text-amber-500 bg-amber-500/10 p-2 rounded">Nenhuma regra definida. A auditoria usará o Tag inteiro.</div>
              )}

              {rule.partes.map((part, pIdx) => (
                <div key={part.id} className="flex items-center gap-3">
                  <div className="w-6 h-6 rounded-full bg-primary/20 text-primary flex items-center justify-center text-xs font-bold shrink-0">{pIdx + 1}</div>
                  <input 
                    type="text" title={`Nome da Posição ${pIdx + 1}`} value={part.name} placeholder="Ex: Diâmetro, Fluido..."
                    onChange={(e) => updatePartName(catIdx, pIdx, e.target.value)}
                    className="flex-1 bg-background border border-border rounded px-3 py-1.5 text-sm outline-none focus:border-primary"
                  />
                  <button type="button" title="Remover Posição" onClick={() => removePart(catIdx, pIdx)} className="p-1.5 text-muted-foreground hover:text-destructive transition-colors">
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              ))}

              <Button type="button" variant="outline" size="sm" onClick={() => addPart(catIdx)} title="Adicionar Posição" className="w-full mt-2 text-xs font-bold border-dashed border-primary/50 text-primary hover:bg-primary/5">
                <Plus className="w-4 h-4 mr-2" /> ADICIONAR POSIÇÃO
              </Button>
            </div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}