import { useState, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { Save, Database, Loader2, Plus, Trash2, RefreshCw, AlertCircle, LayoutList, Key, ListTree } from "lucide-react";
import { toast } from "react-hot-toast";
import { motion } from "framer-motion";
import { useParams } from 'react-router-dom';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from "@/components/alternar-modo";

type AttributeMap = { id: string; name: string; pidCol: string; pipingCol: string; isTagPart?: boolean; isFixedTag?: boolean };
type CategoryMapping = {
  id: string;
  label: string;
  pidTable: string;
  pipingTable: string;
  isComposedTag?: boolean; 
  attributes: AttributeMap[];
};

type ProjectOption = {
  projectId: string; 
  projectName: string;
};

// 🎯 A TAG AGORA NASCE COM A OPÇÃO DE COMPOSIÇÃO
const DEFAULT_MAPPINGS: CategoryMapping[] = [
  {
    id: "equip", label: "Equipamentos",
    pidTable: "Equipment", pipingTable: "Equipment",
    isComposedTag: false,
    attributes: [
      { id: "tag-fixa", name: "TAG do Equipamento", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true },
      { id: "a2", name: "Status", pidCol: "Status", pipingCol: "Status" }
    ]
  },
  {
    id: "valve", label: "Válvulas",
    pidTable: "HandValves", pipingTable: "Valve",
    isComposedTag: false,
    attributes: [
      { id: "tag-fixa", name: "TAG da Válvula", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true },
      { id: "a2", name: "Especificação (Spec)", pidCol: "NominalSpec", pipingCol: "Spec" }
    ]
  },
  {
    id: "inst", label: "Instrumentos",
    pidTable: "Instrumentation", pipingTable: "Instrument",
    isComposedTag: false,
    attributes: [
      { id: "tag-fixa", name: "TAG do Instrumento", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true }
    ]
  },
  {
    id: "line", label: "Linhas",
    pidTable: "PipeLineGroup", pipingTable: "P3dLineGroup",
    isComposedTag: false,
    attributes: [
      { id: "tag-fixa", name: "Número da Linha", pidCol: "Number", pipingCol: "Number", isTagPart: true, isFixedTag: true },
      { id: "a2", name: "Spec da Linha", pidCol: "NominalSpec", pipingCol: "NominalSpec" }
    ]
  }
];

export default function TabSettings() {
  const { projectId: urlProjectId } = useParams();
  const [isSaving, setIsSaving] = useState(false);
  const [isLoadingSchema, setIsLoadingSchema] = useState(false);
  
  const [availableProjects, setAvailableProjects] = useState<ProjectOption[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string>(urlProjectId || "");

  // 🎯 A SUA LÓGICA DE SCHEMA (MANTIDA INTACTA)
  const [schemaPiping, setSchemaPiping] = useState<Record<string, string[]>>({});
  const [schemaPid, setSchemaPid] = useState<Record<string, string[]>>({});

  const [mappings, setMappings] = useState<CategoryMapping[]>(() => JSON.parse(JSON.stringify(DEFAULT_MAPPINGS)));

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

  const loadProjectContext = async (id: string) => {
    // 🎯 SUA LOUSA MÁGICA: Limpa TUDO antes de buscar o novo projeto.
    setMappings(JSON.parse(JSON.stringify(DEFAULT_MAPPINGS)));
    setSchemaPid({});
    setSchemaPiping({});

    if (!id || id === "PROJ-PADRAO") return;
    
    setIsLoadingSchema(true);
    try {
      const response = await fetch(`http://localhost:5277/api/Plant3D/project-context?projectId=${id}`);
      
      if (response.ok) {
        const data = await response.json();
        
        if (data.sucesso) {
          if (data.schema) {
            setSchemaPid(data.schema.pid);
            setSchemaPiping(data.schema.piping);
          }

          if (data.mapeamento && data.mapeamento !== "[]") {
            const parsed: CategoryMapping[] = JSON.parse(data.mapeamento);
            
            // Retrocompatibilidade para garantir que o json antigo funcione no novo design
            parsed.forEach(c => {
               if (c.isComposedTag === undefined) c.isComposedTag = false;
               if (!c.attributes.some(a => a.isFixedTag)) {
                   const tagIndex = c.attributes.findIndex(a => a.name.toLowerCase().includes("tag")) >= 0 
                                  ? c.attributes.findIndex(a => a.name.toLowerCase().includes("tag")) 
                                  : 0;
                   if (c.attributes.length > 0) {
                     c.attributes[tagIndex].isFixedTag = true;
                     c.attributes[tagIndex].isTagPart = true;
                   }
               }
            });

            setMappings(parsed);
          }
        }
      }
    } catch (error) {
      console.error("Erro ao carregar contexto.");
    } finally {
      setIsLoadingSchema(false);
    }
  };

  useEffect(() => {
    fetchProjects();
  }, []); 

  useEffect(() => {
    if (selectedProjectId) {
      loadProjectContext(selectedProjectId);
    }
  }, [selectedProjectId]);

  const handleSaveMapping = async () => {
    if (!selectedProjectId) {
      toast.error("Selecione um projeto antes de salvar.");
      return;
    }
    
    setIsSaving(true);
    const formData = new FormData();
    formData.append("configJson", JSON.stringify(mappings));
    formData.append("projectId", selectedProjectId);
    formData.append("projectName", ""); 

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/run-audit", {
        method: "POST",
        body: formData,
      });
      if (response.ok) toast.success("Configuração do projeto salva!");
      else toast.error("Erro ao salvar no servidor.");
    } catch (error) {
      toast.error("Falha na conexão.");
    } finally {
      setIsSaving(false);
    }
  };

  const updateAttribute = (catIdx: number, attrIdx: number, field: keyof AttributeMap, value: string) => {
    const newM = [...mappings];
    newM[catIdx].attributes[attrIdx][field] = value as never;
    setMappings(newM);
  };

  const updateFixedTag = (catIdx: number, field: keyof AttributeMap, value: string) => {
    const newM = [...mappings];
    const fixedIdx = newM[catIdx].attributes.findIndex(a => a.isFixedTag);
    if(fixedIdx !== -1) newM[catIdx].attributes[fixedIdx][field] = value as never;
    setMappings(newM);
  };

  const toggleComposition = (catIdx: number, isComposed: boolean) => {
    const newM = [...mappings];
    newM[catIdx].isComposedTag = isComposed;
    
    if (!isComposed) {
      const firstTagPart = newM[catIdx].attributes.find(a => a.isTagPart);
      if (firstTagPart) {
        newM[catIdx].attributes = newM[catIdx].attributes.map(a => {
          if (a === firstTagPart) return { ...a, isTagPart: true, isFixedTag: true, name: `TAG Completa` };
          return { ...a, isTagPart: false, isFixedTag: false };
        });
      }
    } else {
      const hasTagPart = newM[catIdx].attributes.some(a => a.isTagPart);
      if (!hasTagPart && newM[catIdx].attributes.length > 0) {
        newM[catIdx].attributes[0].isTagPart = true;
        newM[catIdx].attributes[0].isFixedTag = true;
      }
    }
    
    setMappings(newM);
  };

  const addTagPart = (catIdx: number) => {
    const newM = [...mappings];
    newM[catIdx].attributes.unshift({ id: Math.random().toString(), name: "Nova Parte da Tag", pidCol: "", pipingCol: "", isTagPart: true, isFixedTag: false });
    setMappings(newM);
  };

  return (
            <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col gap-8 animate-enter m-4">
          <div className="flex items-center gap-9 mb-2">
            <SidebarTrigger className="-m-5"/>
            <ModeToggle/>
                            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
                            </a>
          </div>
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      
      <div className="glass-card p-6 rounded-xl border border-border/50">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex-1">
            <h2 className="text-xl font-bold flex items-center gap-2 text-primary">
              <Database className="w-6 h-6" /> Mapeamento DE/PARA
            </h2>
            <div className="mt-4 flex items-center gap-3">
              <label htmlFor="projectSelect" className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                <LayoutList className="w-4 h-4" /> Selecionar Projeto:
              </label>
              <select 
                id="projectSelect"
                aria-label="Selecionar Projeto"
                title="Selecionar Projeto"
                value={selectedProjectId}
                onChange={(e) => setSelectedProjectId(e.target.value)}
                className="bg-background border border-border rounded-lg px-3 py-2 text-sm font-bold text-foreground focus:ring-2 focus:ring-primary outline-none min-w-[300px]"
              >
                <option value="">-- Escolha um Projeto Disponível --</option>
                {availableProjects.map(p => (
                  <option key={p.projectId} value={p.projectId}>{p.projectName}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="flex gap-3 h-fit">
            <Button variant="outline" onClick={() => loadProjectContext(selectedProjectId)} disabled={isLoadingSchema || !selectedProjectId} title="Recarregar dados" aria-label="Recarregar">
              <RefreshCw className={`w-4 h-4 ${isLoadingSchema ? 'animate-spin' : ''}`} />
            </Button>
            <Button onClick={handleSaveMapping} disabled={isSaving || isLoadingSchema || !selectedProjectId} className="font-bold gap-2" title="Salvar Configuração">
              {isSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
              SALVAR CONFIGURAÇÃO
            </Button>
          </div>
        </div>
        
        {(!selectedProjectId || Object.keys(schemaPid).length === 0) && !isLoadingSchema && (
          <div className="mt-6 p-4 bg-amber-500/10 border border-amber-500/30 rounded-lg flex items-center gap-3 text-amber-500 text-sm">
            <AlertCircle className="w-5 h-5" />
            {selectedProjectId 
              ? "Este projeto ainda não tem bancos de dados carregados. Vá na aba 'Banco de Dados'."
              : "Selecione um projeto acima para começar a configurar o mapeamento."}
          </div>
        )}
      </div>

      <div className="space-y-6">
        {mappings.map((cat, catIdx) => {
          
          const tagParts = cat.attributes.filter(a => a.isTagPart || a.isFixedTag);
          const extraAttrs = cat.attributes.filter(a => !a.isTagPart && !a.isFixedTag);

          return (
          <div key={cat.id} className="glass-card p-6 rounded-lg border border-border/50 bg-card/30">
            
            <div className="flex flex-col md:flex-row md:items-center justify-between mb-6 border-b border-border/50 pb-4 gap-4">
              <h4 className="font-bold text-primary text-lg flex items-center gap-2">{cat.label}</h4>
              <div className="flex flex-wrap gap-4">
                <div className="flex items-center gap-2">
                  <span className="text-emerald-400 font-bold text-[10px] uppercase tracking-wider">Tabela P&ID:</span>
                  <select 
                    aria-label={`Tabela P&ID para ${cat.label}`}
                    title={`Tabela P&ID para ${cat.label}`}
                    value={cat.pidTable}
                    onChange={(e) => {
                      const newM = [...mappings]; newM[catIdx].pidTable = e.target.value; setMappings(newM);
                    }}
                    className="bg-background border border-border rounded px-3 py-1.5 text-xs font-semibold min-w-[180px]"
                  >
                    <option value={cat.pidTable}>{cat.pidTable}</option>
                    {Object.keys(schemaPid).map(t => <option key={t} value={t}>{t}</option>)}
                  </select>
                </div>

                <div className="flex items-center gap-2">
                  <span className="text-blue-400 font-bold text-[10px] uppercase tracking-wider">Tabela 3D:</span>
                  <select 
                    aria-label={`Tabela 3D para ${cat.label}`}
                    title={`Tabela 3D para ${cat.label}`}
                    value={cat.pipingTable}
                    onChange={(e) => {
                      const newM = [...mappings]; newM[catIdx].pipingTable = e.target.value; setMappings(newM);
                    }}
                    className="bg-background border border-border rounded px-3 py-1.5 text-xs font-semibold min-w-[180px]"
                  >
                    <option value={cat.pipingTable}>{cat.pipingTable}</option>
                    {Object.keys(schemaPiping).map(t => <option key={t} value={t}>{t}</option>)}
                  </select>
                </div>
              </div>
            </div>

            <div className="bg-primary/5 border border-primary/20 p-4 rounded-xl mb-6 shadow-inner">
              <div className="flex items-center justify-between mb-4">
                <h5 className="text-xs font-black text-primary uppercase flex items-center gap-2">
                  <Key className="w-4 h-4" /> Chave Principal de Cruzamento (TAG)
                </h5>
                
                <div className="flex bg-background/50 border border-primary/20 p-1 rounded-lg">
                  <button 
                    onClick={() => toggleComposition(catIdx, false)}
                    className={`px-3 py-1 text-xs font-bold rounded-md transition-colors ${!cat.isComposedTag ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}
                  >
                    TAG Única
                  </button>
                  <button 
                    onClick={() => toggleComposition(catIdx, true)}
                    className={`px-3 py-1 text-xs font-bold rounded-md transition-colors ${cat.isComposedTag ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}
                  >
                    Compor TAG
                  </button>
                </div>
              </div>

              <div className="space-y-3">
                {tagParts.map((attr, idx) => {
                  const globalAttrIdx = cat.attributes.findIndex(a => a.id === attr.id);
                  return (
                  <div key={attr.id} className="grid grid-cols-12 gap-4 items-center">
                    <div className="col-span-12 md:col-span-4 flex items-center gap-2">
                      {cat.isComposedTag && (
                        <div className="w-6 h-6 rounded-full bg-primary/20 flex items-center justify-center text-xs font-bold text-primary shrink-0">{idx + 1}</div>
                      )}
                      <input 
                        type="text" 
                        title={`Nome da TAG ${idx + 1}`}
                        disabled={!cat.isComposedTag && attr.isFixedTag} 
                        value={!cat.isComposedTag && attr.isFixedTag ? "TAG Completa" : attr.name} 
                        onChange={(e) => updateAttribute(catIdx, globalAttrIdx, 'name', e.target.value)}
                        placeholder="Ex: Área"
                        className={`w-full bg-background border border-primary/30 rounded-lg px-3 py-2 text-sm font-bold text-primary outline-none ${!cat.isComposedTag && attr.isFixedTag ? 'cursor-not-allowed opacity-80' : 'focus:border-primary'}`}
                      />
                    </div>
                    <div className="col-span-5 md:col-span-3">
                      <select 
                        aria-label={`Coluna P&ID para TAG ${idx + 1}`}
                        title={`Coluna P&ID para TAG ${idx + 1}`}
                        value={attr.pidCol} onChange={(e) => updateAttribute(catIdx, globalAttrIdx, 'pidCol', e.target.value)}
                        className="w-full bg-background border border-emerald-500/30 focus:border-emerald-500 rounded-lg px-3 py-2 text-sm font-semibold outline-none"
                      >
                        <option value={attr.pidCol}>{attr.pidCol || "-- Coluna no P&ID --"}</option>
                        {schemaPid[cat.pidTable]?.map(c => <option key={c} value={c}>{c}</option>)}
                      </select>
                    </div>
                    <div className="col-span-5 md:col-span-3">
                      <select 
                        aria-label={`Coluna 3D para TAG ${idx + 1}`}
                        title={`Coluna 3D para TAG ${idx + 1}`}
                        value={attr.pipingCol} onChange={(e) => updateAttribute(catIdx, globalAttrIdx, 'pipingCol', e.target.value)}
                        className="w-full bg-background border border-blue-500/30 focus:border-blue-500 rounded-lg px-3 py-2 text-sm font-semibold outline-none"
                      >
                        <option value={attr.pipingCol}>{attr.pipingCol || "-- Coluna no 3D --"}</option>
                        {schemaPiping[cat.pipingTable]?.map(c => <option key={c} value={c}>{c}</option>)}
                      </select>
                    </div>
                    
                    {cat.isComposedTag && !attr.isFixedTag && (
                      <div className="col-span-2 text-right">
                         <button 
                          type="button" 
                          title="Remover Parte da TAG"
                          aria-label="Remover Parte da TAG"
                          onClick={() => {
                            const newM = [...mappings];
                            newM[catIdx].attributes.splice(globalAttrIdx, 1);
                            setMappings(newM);
                          }} 
                          className="p-1.5 text-muted-foreground hover:text-destructive transition-colors"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    )}
                  </div>
                )})}
                
                {cat.isComposedTag && (
                  <Button variant="ghost" size="sm" onClick={() => addTagPart(catIdx)} className="text-xs text-primary font-bold hover:bg-primary/10 mt-2 border border-dashed border-primary/30 w-full sm:w-auto">
                    <Plus className="w-4 h-4 mr-2" /> ADICIONAR PARTE DA TAG
                  </Button>
                )}
              </div>
            </div>

            <div className="space-y-3">
              <h5 className="text-xs font-bold text-muted-foreground uppercase flex items-center gap-2 mb-2">
                <ListTree className="w-4 h-4" /> Atributos Extras (Opcional)
              </h5>
              
              <div className="grid grid-cols-12 gap-4 text-[10px] font-bold uppercase text-muted-foreground pb-1 border-b border-border/30 px-2">
                <div className="col-span-4">Nome do Atributo</div>
                <div className="col-span-3 text-emerald-400">Coluna P&ID</div>
                <div className="col-span-3 text-blue-400">Coluna 3D</div>
                <div className="col-span-2 text-right">Ação</div>
              </div>

              {extraAttrs.length === 0 && (
                 <div className="text-xs text-center py-4 text-muted-foreground italic border border-dashed border-border/50 rounded-lg">
                    Nenhum atributo extra mapeado. O cruzamento será feito apenas pela TAG principal.
                 </div>
              )}

              {extraAttrs.map((attr) => {
                const attrIdx = cat.attributes.findIndex(a => a.id === attr.id);
                return (
                <div key={attr.id} className="grid grid-cols-12 gap-3 items-center group bg-background/30 p-2 rounded-lg border border-transparent hover:border-border/50 transition-all">
                  <div className="col-span-4">
                    <input 
                      type="text" placeholder="Ex: Diâmetro, Spec..." value={attr.name} 
                      title="Nome do Atributo Extra"
                      onChange={(e) => updateAttribute(catIdx, attrIdx, 'name', e.target.value)}
                      className="w-full bg-background/80 border border-border rounded-md px-3 py-2 text-sm focus:border-primary outline-none"
                    />
                  </div>
                  <div className="col-span-3">
                    <select 
                      aria-label="Coluna P&ID Atributo Extra"
                      title="Coluna P&ID Atributo Extra"
                      value={attr.pidCol} onChange={(e) => updateAttribute(catIdx, attrIdx, 'pidCol', e.target.value)}
                      className="w-full bg-background border border-border rounded-md px-2 py-2 text-sm outline-none"
                    >
                      <option value={attr.pidCol}>{attr.pidCol || "-- Coluna --"}</option>
                      {schemaPid[cat.pidTable]?.map(c => <option key={c} value={c}>{c}</option>)}
                    </select>
                  </div>
                  <div className="col-span-3">
                    <select 
                      aria-label="Coluna 3D Atributo Extra"
                      title="Coluna 3D Atributo Extra"
                      value={attr.pipingCol} onChange={(e) => updateAttribute(catIdx, attrIdx, 'pipingCol', e.target.value)}
                      className="w-full bg-background border border-border rounded-md px-2 py-2 text-sm outline-none"
                    >
                      <option value={attr.pipingCol}>{attr.pipingCol || "-- Coluna --"}</option>
                      {schemaPiping[cat.pipingTable]?.map(c => <option key={c} value={c}>{c}</option>)}
                    </select>
                  </div>
                  <div className="col-span-2 flex justify-end">
                    <button 
                      type="button" 
                      title="Remover Atributo Extra"
                      aria-label="Remover Atributo Extra"
                      onClick={() => {
                        const newM = [...mappings];
                        newM[catIdx].attributes.splice(attrIdx, 1);
                        setMappings(newM);
                      }} 
                      className="p-2 text-muted-foreground hover:text-destructive hover:bg-destructive/10 rounded-lg transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              )})}
              
              <Button variant="ghost" size="sm" onClick={() => {
                  const newM = [...mappings];
                  newM[catIdx].attributes.push({ id: Math.random().toString(), name: "", pidCol: "", pipingCol: "", isTagPart: false });
                  setMappings(newM);
              }} className="text-xs text-primary font-bold hover:bg-primary/10 mt-2">
                <Plus className="w-4 h-4 mr-2" /> ADICIONAR ATRIBUTO EXTRA
              </Button>
            </div>
          </div>
        )})}
      </div>
    </motion.div>
        	</div>
      </SidebarInset>
    </SidebarProvider>
  );
}