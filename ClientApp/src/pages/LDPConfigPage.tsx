import React, { useState, useEffect, useCallback } from 'react';
import { 
    FileSpreadsheet, Clock, Save, Search, Settings2, Database, 
    AlertCircle, Loader2, ChevronDown, Network, Folder, 
    ChevronRight, Check, ArrowRightLeft, Table2, Plus, Copy, Edit3 
} from 'lucide-react';
import { Button } from "../components/ui/button";   
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "../components/ui/dialog"; 
import { Input } from "../components/ui/input"; 
import toast from 'react-hot-toast';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from '@/components/alternar-modo';

interface ProjetoLDP { id: number; nome: string; accId: string; configId?: number; }
interface AccFolder { id: string; name: string; type: string; hasChildren?: boolean; }
interface TemplateLDP { configLDPID: string; nomeConfig: string; }

// --- COMPONENTE DE MAPEAMENTO MODERNIZADO ---
const LDPMappingSection: React.FC<{
    mapping: Record<string, string>;
    onMappingChange: (dbField: string, excelCol: string) => void;
}> = ({ mapping, onMappingChange }) => {
    const dbFields = [
        { key: "Document_Code", label: "Código do Documento", required: true },
        { key: "Document_Title", label: "Título", required: true },
        { key: "Discipline", label: "Disciplina", required: false },
        { key: "Revision", label: "Revisão", required: true },
        { key: "Planned_Date", label: "Data Planejada", required: true },
        { key: "Current_Status", label: "Status Atual", required: false }
    ];

    const excelColumns = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T"];

    return (
        
        <div className="relative p-8 bg-card/60 backdrop-blur-xl border border-border rounded-3xl mt-8 shadow-sm animate-in fade-in slide-in-from-bottom-8 duration-700">
            <div className="absolute inset-0 bg-gradient-to-br from-primary/5 via-transparent to-transparent rounded-3xl pointer-events-none" />
            
            <div className="relative flex items-center gap-4 mb-8">
                <div className="p-3.5 bg-gradient-to-br from-primary to-blue-600 rounded-2xl shadow-lg shadow-primary/30">
                    <Table2 size={24} className="text-white" />
                </div>
                <div>
                    <h2 className="text-2xl font-black uppercase italic text-foreground leading-none">
                        Data Binding
                    </h2>
                    <p className="text-[10px] text-primary font-bold uppercase tracking-[0.2em] mt-1.5">
                        Mapeamento de Colunas do Excel
                    </p>
                </div>
            </div>

            <div className="relative grid grid-cols-1 md:grid-cols-2 gap-4">
                {dbFields.map((field) => (
                    <div key={field.key} className="flex items-center justify-between p-5 bg-background rounded-2xl border border-border hover:border-primary/50 hover:bg-primary/5 transition-all duration-300 group">
                        <div className="flex flex-col">
                            <span className="text-xs font-black text-foreground uppercase tracking-wider">
                                {field.label} {field.required && <span className="text-rose-500">*</span>}
                            </span>
                            <span className="text-[10px] text-muted-foreground font-mono mt-1 group-hover:text-primary/70 transition-colors">{field.key}</span>
                        </div>
                        
                        <div className="flex items-center gap-3">
                            <ArrowRightLeft size={14} className="text-muted-foreground group-hover:text-primary animate-pulse" />
                            <select 
                                id={`mapping-${field.key}`} 
                                title={`Selecionar coluna do Excel para ${field.label}`} 
                                aria-label={`Coluna para ${field.label}`}
                                value={mapping[field.key] || ""}
                                onChange={(e) => onMappingChange(field.key, e.target.value)}
                                className="bg-background border border-border rounded-xl px-4 py-2.5 text-xs font-bold text-foreground outline-none focus:ring-2 focus:ring-primary focus:border-transparent min-w-[130px] transition-all cursor-pointer hover:bg-muted"
                            >
                                <option value="" className="text-muted-foreground">IGNORAR</option>
                                {excelColumns.map(col => (
                                    <option key={col} value={col}>Coluna {col}</option>
                                ))}
                            </select>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};

export const LDPConfigPage: React.FC = () => {
    const [projetos, setProjetos] = useState<ProjetoLDP[]>([]);
    const [selectedProject, setSelectedProject] = useState<ProjetoLDP | null>(null);
    
    const [templatesLDP, setTemplatesLDP] = useState<TemplateLDP[]>([]);
    const [selectedTemplate, setSelectedTemplate] = useState<TemplateLDP | null>(null);
    const [isNewTemplateModalOpen, setIsNewTemplateModalOpen] = useState(false);
    const [newTemplateName, setNewTemplateName] = useState("");

    const [modalOpen, setModalOpen] = useState(false);
    const [targetType, setTargetType] = useState<'LDP' | 'CRONOGRAMA' | null>(null);
    const [ldpFolder, setLdpFolder] = useState<{id: string, name: string} | null>(null);
    const [cronFolder, setCronFolder] = useState<{id: string, name: string} | null>(null);
    
    const [treeData, setTreeData] = useState<AccFolder[]>([]);
    const [expandedFolders, setExpandedFolders] = useState<Record<string, AccFolder[]>>({});
    const [loadingTree, setLoadingTree] = useState(false);
    const [loadingProjetos, setLoadingProjetos] = useState<boolean>(true);
    const [isSaving, setIsSaving] = useState<boolean>(false);
    
    const [isLoadingDetails, setIsLoadingDetails] = useState<boolean>(false);

    const [sheetName, setSheetName] = useState("Sheet1");
    const [headerRow, setHeaderRow] = useState(1);
    const [mapping, setMapping] = useState<Record<string, string>>({
        "Document_Code": "", "Document_Title": "", "Revision": "", "Discipline": "", "Planned_Date": "", "Current_Status": ""
    });

    const handleMappingChange = (dbField: string, excelCol: string) => {
        setMapping(prev => ({ ...prev, [dbField]: excelCol }));
    };

const fetchTemplatesLDP = useCallback(async () => {
    try {
        const res = await fetch('/api/admin/projeto/listar-configuracoes-ldp');
        console.log('[LDP] status:', res.status, res.ok);
        if (!res.ok) {
            toast.error(`Erro ao buscar templates: ${res.status}`);
            return;
        }
        const data = await res.json();
        console.log('[LDP] payload recebido:', data);
        setTemplatesLDP(data);
    } catch (error) {
        console.error("Erro ao buscar templates LDP", error);
        toast.error("Falha de rede ao buscar templates.");
    }
}, []);

    useEffect(() => { fetchTemplatesLDP(); }, [fetchTemplatesLDP]);

    const handleSelectTemplate = async (templateId: string) => {
        const tpl = templatesLDP.find(t => t.configLDPID === templateId);
        setSelectedTemplate(tpl || null);

        if (!tpl) {
            setLdpFolder(null); setCronFolder(null); setSheetName("Sheet1"); setHeaderRow(1);
            setMapping({ "Document_Code": "", "Document_Title": "", "Revision": "", "Discipline": "", "Planned_Date": "", "Current_Status": "" });
            return;
        }

        setIsLoadingDetails(true);
        try {
            const res = await fetch(`/api/admin/projeto/configuracao-ldp/${templateId}`);
            if (res.ok) {
                const data = await res.json();
                
                setSheetName(data.SheetName || "Sheet1");
                setHeaderRow(data.HeaderRow || 1);
                
                if (data.Folder_ID_LDP) setLdpFolder({ id: data.Folder_ID_LDP, name: "Pasta LDP (URN Salva)" });
                if (data.Folder_ID_Cronograma) setCronFolder({ id: data.Folder_ID_Cronograma, name: "Pasta Crono (URN Salva)" });

                if (data.MappingJSON) {
                    try { setMapping(JSON.parse(data.MappingJSON)); } catch(e) { console.error("Erro no JSON", e); }
                }
                toast.success(`Dados do template '${tpl.nomeConfig}' carregados para edição!`);
            }
        } catch (err) {
            toast.error("Erro ao carregar detalhes do template do banco de dados.");
        } finally {
            setIsLoadingDetails(false);
        }
    };

    const handleCreateTemplate = async () => {
        if (!newTemplateName.trim()) return toast.error("Digite um nome para o template.");
        const mockNewTemplate = { configLDPID: Date.now().toString(), nomeConfig: newTemplateName };
        setTemplatesLDP([...templatesLDP, mockNewTemplate]);
        setSelectedTemplate(mockNewTemplate);
        setIsNewTemplateModalOpen(false);
        setNewTemplateName("");
        
        setLdpFolder(null); setCronFolder(null); setSheetName("Sheet1"); setHeaderRow(1);
        setMapping({ "Document_Code": "", "Document_Title": "", "Revision": "", "Discipline": "", "Planned_Date": "", "Current_Status": "" });
        
        toast.success(`Template ${newTemplateName} criado! Configure-o abaixo.`);
    };

    const carregarProjetosSql = useCallback(async () => {
        setLoadingProjetos(true);
        try {
            const res = await fetch('/api/admin/projetos');
            if (!res.ok) throw new Error(`Status: ${res.status}`);
            const rawData = await res.json();
            
            const projetosNormalizados: ProjetoLDP[] = rawData.map((p: any, index: number) => {
                const getVal = (chaves: string[]) => {
                    const chaveEncontrada = Object.keys(p).find(k => chaves.includes(k.toLowerCase()));
                    return chaveEncontrada ? p[chaveEncontrada] : undefined;
                };
                return {
                    id: getVal(['projetoid', 'id']) ? Number(getVal(['projetoid', 'id'])) : (index + 9000), 
                    nome: getVal(['nome_projeto', 'nomeprojeto', 'nome', 'name']) || "Projeto Desconhecido",
                    accId: p.Project_ID_ACC || p.project_id_acc || getVal(['project_id_acc', 'projectidacc', 'accid']) || "",
                    configId: getVal(['configldpid', 'configid']) ? Number(getVal(['configldpid', 'configid'])) : undefined
                };
            });
            setProjetos(projetosNormalizados);
        } catch (err) {
            toast.error("Erro ao carregar projetos.");
        } finally {
            setLoadingProjetos(false);
        }
    }, []);

    useEffect(() => { carregarProjetosSql(); }, [carregarProjetosSql]);

    const fetchLevel = async (folderId?: string) => {
        if (!selectedProject) return [];
        try {
            const url = `/api/admin/pastas/${selectedProject.accId}${folderId ? `?folderId=${folderId}` : ''}`;
            const res = await fetch(url);
            
            if (!res.ok) {
                const errorData = await res.json().catch(() => ({}));
                toast.error(`Erro Autodesk: ${errorData.message || res.statusText || "Verifique se o token de acesso expirou."}`);
                return [];
            }
            
            const rawData = await res.json();
            if (rawData.length === 0) toast.error("Nenhuma pasta foi encontrada neste nível.");
            
            return rawData.map((f: any) => ({ id: f.id ?? f.Id, name: f.name ?? f.Name ?? "Pasta", type: (f.type ?? f.Type ?? "").toLowerCase() }));
        } catch (err) { 
            toast.error("Erro de conexão ao comunicar com a Autodesk.");
            return []; 
        }
    };

    const openFolderSelector = async (type: 'LDP' | 'CRONOGRAMA') => {
        if (!selectedProject) return toast.error("Selecione um projeto primeiro.");
        if (!selectedProject.accId) return toast.error("Este projeto não possui um ID da Autodesk (ACC_ID) válido.");
        setTargetType(type);
        setModalOpen(true);
        setLoadingTree(true);
        const root = await fetchLevel();
        setTreeData(root);
        setLoadingTree(false);
    };

    const toggleFolderExpansion = async (folderId: string) => {
        if (expandedFolders[folderId]) {
            const next = { ...expandedFolders }; delete next[folderId]; setExpandedFolders(next);
        } else {
            const children = await fetchLevel(folderId);
            setExpandedFolders({ ...expandedFolders, [folderId]: children });
        }
    };

    const handleSelectFolder = (folder: AccFolder) => {
        if (targetType === 'LDP') setLdpFolder({ id: folder.id, name: folder.name });
        else setCronFolder({ id: folder.id, name: folder.name });
        setModalOpen(false); setExpandedFolders({}); 
    };

    const handleSaveConfig = async () => {
        if (!selectedTemplate) return toast.error("Você precisa selecionar ou criar um Template LDP primeiro.");
        if (!selectedProject || !ldpFolder || !cronFolder) return toast.error("Selecione o projeto e ambas as pastas.");
        
        setIsSaving(true);
        const loadingToast = toast.loading("Salvando ecossistema...");

        try {
            const payload = {
                projetoId: selectedProject.id,
                nomeConfig: selectedTemplate.nomeConfig,
                folderIdLdp: ldpFolder.id,
                folderIdCronograma: cronFolder.id,
                sheetName: sheetName,
                headerRow: headerRow,
                mappingJSON: JSON.stringify(mapping),
                statusAtivo: true
            };

            const res = await fetch('/api/admin/projeto/configurar-ldp', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
            if (!res.ok) throw new Error("Erro ao salvar.");

            toast.success("Ecossistema LDP configurado!", { id: loadingToast });
            carregarProjetosSql();
        } catch (err: any) {
            toast.error("Falha ao gravar configuração.", { id: loadingToast });
        } finally {
            setIsSaving(false);
        }
    };

    return (
            <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col gap-8 animate-enter m-4">
          <div className="flex items-center gap-4 mb-2">
            <SidebarTrigger className="-ml-6"/>
              <ModeToggle/>
                                        <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
                                        </a>
          </div>

        <div className="p-6 md:p-10 min-h-screen bg-background animate-in fade-in duration-700 font-sans">
            
            {/* CABEÇALHO HERO - VISUAL MODERNIZADO */}
            <div className="relative mb-12 p-8 rounded-[2rem] overflow-hidden border border-border bg-card/40 shadow-sm">
                <div className="absolute top-0 right-0 w-[500px] h-[500px] bg-primary/10 blur-[120px] rounded-full pointer-events-none -translate-y-1/2 translate-x-1/3" />
                <div className="relative flex flex-col md:flex-row items-start md:items-center justify-between gap-6 z-10">
                    <div className="flex items-center gap-6">
                        <div className="p-4 bg-gradient-to-br from-primary to-blue-600 rounded-2xl shadow-xl shadow-primary/20">
                            <Network className="text-white w-8 h-8" />
                        </div>
                        <div>
                            <h1 className="text-3xl md:text-4xl font-black text-foreground uppercase italic tracking-tight">
                                Ecossistema <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary to-blue-400">LDP</span>
                            </h1>
                            <p className="text-muted-foreground text-xs font-bold uppercase tracking-[0.2em] flex items-center gap-2 mt-2">
                                <Database size={14} className="text-primary" /> Automação Inteligente ACC → SQL Server
                            </p>
                        </div>
                    </div>
                </div>
            </div>

            <div className="grid grid-cols-1 xl:grid-cols-3 gap-8">
                
                {/* ÁREA PRINCIPAL */}
                <div className="xl:col-span-2 space-y-6">
                    <div className="bg-card/60 backdrop-blur-xl p-8 md:p-12 border border-border rounded-[2.5rem] shadow-md relative overflow-hidden">
                        
                        <div className="space-y-10 relative z-10">
                            
                            {/* SELETOR DE TEMPLATE */}
                            <div className="p-6 bg-background border border-border rounded-3xl flex flex-col md:flex-row items-center justify-between gap-6 shadow-sm">
                                <div className="flex items-center gap-4">
                                    <div className="p-3 bg-primary/10 rounded-xl text-primary border border-primary/20"><Copy size={24} /></div>
                                    <div>
                                        <h3 className="text-base font-black uppercase text-foreground">Gerenciador de Templates</h3>
                                        <p className="text-[10px] text-primary font-bold uppercase tracking-widest mt-1">Carregue ou crie uma configuração</p>
                                    </div>
                                </div>
                                <div className="flex items-center gap-3 w-full md:w-auto">
                                    <div className="relative flex-1 md:w-64">
                                        <select 
                                            value={selectedTemplate?.configLDPID || ""}
                                            onChange={(e) => handleSelectTemplate(e.target.value)} 
                                            title="Selecionar Template"
                                            aria-label="Selecionar Template"
                                            className="w-full bg-background border border-border rounded-xl pl-4 pr-10 py-3 text-xs font-bold text-foreground outline-none focus:ring-2 focus:ring-primary appearance-none cursor-pointer transition-all hover:bg-muted"
                                        >
                                            <option value="">Selecione um Template...</option>
                                            {templatesLDP.map(t => <option key={t.configLDPID} value={t.configLDPID}>{t.nomeConfig}</option>)}
                                        </select>
                                        <ChevronDown size={16} className="absolute right-4 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none" />
                                    </div>
                                    <Button onClick={() => setIsNewTemplateModalOpen(true)} aria-label="Novo Template" className="bg-gradient-to-r from-primary to-blue-600 hover:from-primary/80 hover:to-blue-600/80 text-white font-black text-[11px] uppercase tracking-wider gap-2 px-6 h-11 rounded-xl shadow-md shadow-primary/20">
                                        <Plus size={16} /> Novo
                                    </Button>
                                </div>
                            </div>

                            {isLoadingDetails ? (
                                <div className="flex flex-col items-center justify-center py-20 gap-4">
                                    <Loader2 className="animate-spin text-primary" size={40} />
                                    <p className="text-xs font-bold uppercase tracking-widest text-muted-foreground">Puxando dados do banco...</p>
                                </div>
                            ) : selectedTemplate && (
                                <div className="animate-in fade-in duration-500 space-y-8">
                                    
                                    {/* PROJETO PONTE */}
                                    <div className="space-y-3">
                                        <label htmlFor="projeto-select" className="text-[11px] font-black text-primary uppercase tracking-[0.15em]">1. Projeto Referência (Navegador ACC)</label>
                                        <div className="relative group">
                                            <select 
                                                id="projeto-select" 
                                                title="Selecione o Projeto" 
                                                aria-label="Projeto Referência"
                                                onChange={(e) => setSelectedProject(projetos.find(p => p.id.toString() === e.target.value) || null)} 
                                                className="w-full bg-background border-2 border-border rounded-2xl p-5 text-sm text-foreground outline-none focus:border-primary/50 appearance-none font-medium transition-all group-hover:bg-muted/50"
                                            >
                                                <option value="">{loadingProjetos ? "Buscando projetos..." : "Selecione um projeto para abrir a árvore de pastas..."}</option>
                                                {projetos.map((p) => <option key={p.id} value={p.id}>{p.nome}</option>)}
                                            </select>
                                            <ChevronDown className="absolute right-5 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none group-hover:text-primary transition-colors" size={20} />
                                        </div>
                                    </div>

                                    <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                                        <div className="space-y-4">
                                            <label className="text-[11px] font-black text-muted-foreground uppercase tracking-widest">2. Pasta LDP (Excel)</label>
                                            <button onClick={() => openFolderSelector('LDP')} title="Selecionar Pasta LDP" aria-label="Selecionar Pasta LDP" className="w-full flex items-center justify-between bg-background border-2 border-dashed border-border rounded-2xl p-5 hover:border-primary/50 transition-all group">
                                                <div className="flex items-center gap-3"><FileSpreadsheet className={ldpFolder ? "text-emerald-500" : "text-muted-foreground"} size={20} /><span className="text-sm text-foreground font-bold truncate max-w-[140px]">{ldpFolder ? ldpFolder.name : "Selecionar Pasta..."}</span></div><ChevronRight size={16} className="text-muted-foreground group-hover:text-primary" />
                                            </button>
                                        </div>
                                        <div className="space-y-4">
                                            <label className="text-[11px] font-black text-muted-foreground uppercase tracking-widest">3. Pasta Cronograma</label>
                                            <button onClick={() => openFolderSelector('CRONOGRAMA')} title="Selecionar Pasta Cronograma" aria-label="Selecionar Pasta Cronograma" className="w-full flex items-center justify-between bg-background border-2 border-dashed border-border rounded-2xl p-5 hover:border-primary/50 transition-all group">
                                                <div className="flex items-center gap-3"><Clock className={cronFolder ? "text-emerald-500" : "text-muted-foreground"} size={20} /><span className="text-sm text-foreground font-bold truncate max-w-[140px]">{cronFolder ? cronFolder.name : "Selecionar Pasta..."}</span></div><ChevronRight size={16} className="text-muted-foreground group-hover:text-primary" />
                                            </button>
                                        </div>
                                    </div>

                                    {ldpFolder && (
                                        <div className="grid grid-cols-1 md:grid-cols-2 gap-8 animate-in fade-in slide-in-from-top-4 duration-500">
                                            <div className="space-y-4">
                                                <label htmlFor="sheet-name" className="text-[11px] font-black text-muted-foreground uppercase tracking-widest">4. Nome da Aba (Excel)</label>
                                                <input id="sheet-name" type="text" title="Nome da Aba" aria-label="Nome da Aba do Excel" value={sheetName} onChange={(e) => setSheetName(e.target.value)} className="w-full bg-background border-2 border-border rounded-2xl p-5 text-sm text-foreground outline-none focus:border-primary/50 font-medium" placeholder="Ex: Sheet1 ou LDP" />
                                            </div>
                                            <div className="space-y-4">
                                                <label htmlFor="header-row-input" className="text-[11px] font-black text-muted-foreground uppercase tracking-widest">5. Linha do Cabeçalho</label>
                                                <input id="header-row-input" type="number" title="Linha do Cabeçalho" aria-label="Número da linha do cabeçalho" value={headerRow} onChange={(e) => setHeaderRow(Number(e.target.value))} className="w-full bg-background border-2 border-border rounded-2xl p-5 text-sm text-foreground outline-none focus:border-primary/50 font-medium" />
                                            </div>
                                        </div>
                                    )}

                                    {ldpFolder && <LDPMappingSection mapping={mapping} onMappingChange={handleMappingChange} />}

                                    <div className="mt-12 pt-8 border-t border-border">
                                        <Button onClick={handleSaveConfig} disabled={isSaving || !ldpFolder || !cronFolder} aria-label="Salvar Definições" className="w-full h-16 bg-gradient-to-r from-primary to-blue-600 hover:from-primary/80 hover:to-blue-600/80 text-white font-black uppercase text-xs rounded-2xl gap-4 transition-all shadow-lg shadow-primary/20">
                                            {isSaving ? <Loader2 className="animate-spin" /> : <Save size={20} />} Salvar Definições do {selectedTemplate.nomeConfig}
                                        </Button>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </div>

                {/* COLUNA LATERAL - INFOS FORGE SYNC */}
                <div className="space-y-6">
                    <div className="p-8 border border-primary/20 rounded-[2.5rem] bg-primary/5 backdrop-blur-md shadow-[0_0_30px_rgba(var(--primary),0.05)]">
                        <div className="flex items-center gap-3 mb-6 text-primary">
                            <AlertCircle size={24} />
                            <h3 className="font-black text-sm uppercase italic tracking-wider">N&A Engine</h3>
                        </div>
                        <p className="text-xs text-muted-foreground leading-relaxed font-medium">
                            Navegue na estrutura da Autodesk em tempo real. As subpastas são carregadas apenas quando expandidas para garantir máxima performance na API.
                        </p>
                    </div>
                </div>
            </div>

            {/* MODAL DE NAVEGAÇÃO DE PASTAS */}
            <Dialog open={modalOpen} onOpenChange={setModalOpen}>
                <DialogContent className="bg-card border-border text-foreground max-w-xl rounded-[2rem] p-8 shadow-2xl backdrop-blur-3xl">
                    <DialogHeader>
                        <DialogTitle className="text-2xl font-black italic uppercase text-foreground">
                            Estrutura <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary to-blue-400">Autodesk Cloud</span>
                        </DialogTitle>
                    </DialogHeader>
                    
                    <div className="max-h-[450px] overflow-y-auto mt-6 space-y-1 custom-scrollbar pr-2">
                        {loadingTree ? (
                            <div className="flex flex-col items-center justify-center py-20 gap-4">
                                <Loader2 className="animate-spin text-primary" size={40} />
                                <span className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Conectando ao Forge...</span>
                            </div>
                        ) : treeData.length === 0 ? (
                            <div className="flex flex-col items-center justify-center py-16 gap-3 text-muted-foreground">
                                <Folder size={48} className="opacity-30" />
                                <p className="text-sm font-bold text-center">Nenhuma pasta encontrada.</p>
                                <p className="text-[10px] uppercase tracking-widest text-center">Verifique se o seu Token da Autodesk ainda está válido.</p>
                            </div>
                        ) : (
                            treeData.map(folder => (
                                <div key={folder.id} className="space-y-1">
                                    <div className="flex items-center group">
                                        <button onClick={() => toggleFolderExpansion(folder.id)} title="Expandir Pasta" aria-label="Expandir Pasta" className="p-2 hover:bg-muted rounded text-muted-foreground hover:text-primary transition-colors">
                                            {expandedFolders[folder.id] ? <ChevronDown size={14}/> : <ChevronRight size={14}/>}
                                        </button>
                                        <div className="flex-1 flex items-center justify-between p-3 rounded-xl hover:bg-muted cursor-pointer transition-all">
                                            <div className="flex items-center gap-3" onClick={() => toggleFolderExpansion(folder.id)}>
                                                <Folder size={18} className="text-primary/70" />
                                                <span className="text-xs font-bold text-foreground">{folder.name}</span>
                                            </div>
                                            <button onClick={() => handleSelectFolder(folder)} title="Confirmar Pasta" aria-label="Confirmar Pasta" className="bg-primary/10 text-primary p-2 rounded-lg opacity-0 group-hover:opacity-100 hover:bg-primary hover:text-white transition-all">
                                                <Check size={14} />
                                            </button>
                                        </div>
                                    </div>
                                    {expandedFolders[folder.id] && (
                                        <div className="pl-6 border-l border-border ml-4 space-y-1">
                                            {expandedFolders[folder.id].map(sub => (
                                                <div key={sub.id} className="flex items-center group">
                                                    <button onClick={() => toggleFolderExpansion(sub.id)} title="Expandir Subpasta" aria-label="Expandir Subpasta" className="p-2 text-muted-foreground hover:text-primary">
                                                        {expandedFolders[sub.id] ? <ChevronDown size={12}/> : <ChevronRight size={12}/>}
                                                    </button>
                                                    <div className="flex-1 flex items-center justify-between p-2 rounded-lg hover:bg-muted cursor-pointer transition-all">
                                                        <span className="text-[11px] text-muted-foreground font-medium" onClick={() => toggleFolderExpansion(sub.id)}>{sub.name}</span>
                                                        <button onClick={() => handleSelectFolder(sub)} title="Confirmar Subpasta" aria-label="Confirmar Subpasta" className="text-emerald-500 opacity-0 group-hover:opacity-100 hover:scale-110 transition-all p-1">
                                                            <Check size={16} />
                                                        </button>
                                                    </div>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                            ))
                        )}
                    </div>
                    <DialogFooter className="mt-8">
                        <Button variant="ghost" onClick={() => setModalOpen(false)} aria-label="Fechar Navegador" className="text-[10px] font-black uppercase text-muted-foreground hover:text-foreground hover:bg-muted">Fechar Navegador</Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            {/* MODAL NOVO TEMPLATE */}
            <Dialog open={isNewTemplateModalOpen} onOpenChange={setIsNewTemplateModalOpen}>
                <DialogContent className="bg-card border border-border text-foreground max-w-sm rounded-[2rem] shadow-2xl backdrop-blur-3xl">
                    <DialogHeader>
                        <DialogTitle className="text-xl font-black uppercase italic text-foreground">Novo Template LDP</DialogTitle>
                    </DialogHeader>
                    <div className="space-y-4 py-4">
                        <div className="space-y-3">
                            <label htmlFor="new-template-name" className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Nome do Template (Ex: Padrão Civil)</label>
                            <Input id="new-template-name" title="Nome do Novo Template" aria-label="Nome do Template" value={newTemplateName} onChange={(e) => setNewTemplateName(e.target.value)} placeholder="Digite o nome..." className="bg-background border-border uppercase font-bold text-xs p-4 rounded-xl focus:border-primary/50 transition-all" />
                        </div>
                    </div>
                    <DialogFooter>
                        <Button variant="ghost" onClick={() => setIsNewTemplateModalOpen(false)} aria-label="Cancelar criação" className="font-black text-[10px] uppercase text-muted-foreground hover:text-foreground hover:bg-muted">Cancelar</Button>
                        <Button onClick={handleCreateTemplate} aria-label="Criar Base de Template" className="font-black text-[10px] uppercase tracking-widest bg-gradient-to-r from-primary to-blue-600 hover:from-primary/80 hover:to-blue-600/80 text-white rounded-xl px-6">Criar Base</Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </div>
        	</div>
      </SidebarInset>
    </SidebarProvider>
    );
};