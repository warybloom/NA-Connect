// Localização: src/pages/ControlPanel.tsx
import { useState, useMemo, useCallback, useEffect } from "react";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
// Ícones do Lucide
import { 
  Search, 
  Upload, 
  Plus, 
  FileSpreadsheet, 
  Settings2, 
  Trash2, 
  AlertTriangle, 
  FileCode, 
  ArrowLeft,
  Package,
  FolderOpen,
  Eye,
  Clock,
  Copy
} from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ModeToggle } from "@/components/alternar-modo";
import toast from 'react-hot-toast';
import { useNavigate } from "react-router-dom";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/Select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import type { Project, Norma, Mapping } from "@/types/control-painel";
import * as XLSX from "xlsx";
import PlantMappingPanel from "./PlantMappingPanel";

interface TemplateGroup {
  normaId: number | null;
  normaNome: string;
  templates: any[];
}

// =========================================================================
// FERRAMENTA DE LIMPEZA OCR
// =========================================================================
const OCRCleanupTool = ({ pacotes, onRefresh }: { pacotes: any[], onRefresh: () => void }) => {
  const [selectedPacote, setSelectedPacote] = useState<string>("");
  const [tipoFolha, setTipoFolha] = useState<string>("");
  const [loading, setLoading] = useState(false);

  const handleClear = async () => {
    if (!selectedPacote || !tipoFolha) {
      toast.error("Selecione o Pacote e o Tipo de Folha.");
      return;
    }

    if (!confirm(`⚠️ ATENÇÃO: Deseja apagar todas as coordenadas de ${tipoFolha} do pacote selecionado?`)) return;

    setLoading(true);
    try {
      const res = await fetch(`/api/admin/ocr/limpar-coordenadas?pacoteId=${selectedPacote}&tipoFolha=${tipoFolha}`, {
        method: 'DELETE'
      });
      
      if (res.ok) {
        toast.success(`Calibração ${tipoFolha} removida com sucesso!`);
        setTipoFolha("");
        onRefresh();
      } else {
        throw new Error();
      }
    } catch (error) {
      toast.error("Falha ao limpar dados no banco.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6 bg-destructive/5 border border-destructive/20 rounded-xl backdrop-blur-md">
      <div className="flex items-center gap-2 text-destructive">
        <AlertTriangle className="w-4 h-4" />
        <span className="text-[10px] font-black uppercase tracking-tighter italic">Zona de Limpeza de Templates</span>
      </div>
      
      <div className="flex flex-wrap gap-3">
        <Select value={selectedPacote} onValueChange={setSelectedPacote}>
          <SelectTrigger className="w-[220px] bg-card/50 border-border/50 text-[10px] font-black uppercase">
            <SelectValue placeholder="SELECIONE O PACOTE" />
          </SelectTrigger>
          <SelectContent>
            {pacotes.map(p => (
              <SelectItem key={p.id} value={String(p.id)} className="text-[10px] font-black uppercase">
                {p.nome}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={tipoFolha} onValueChange={setTipoFolha}>
          <SelectTrigger className="w-[180px] bg-card/50 border-border/50 text-[10px] font-black uppercase">
            <SelectValue placeholder="TIPO DE FOLHA" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="A0">FOLHA A0</SelectItem>
            <SelectItem value="A1">FOLHA A1</SelectItem>
            <SelectItem value="A2">FOLHA A2</SelectItem>
            <SelectItem value="A3">FOLHA A3</SelectItem>
            <SelectItem value="ANSI_A">FOLHA ANSI A</SelectItem>
          </SelectContent>
        </Select>

        <Button 
          variant="destructive" 
          onClick={handleClear}
          disabled={loading || !tipoFolha || !selectedPacote}
          className="gap-2 font-black text-[10px] uppercase italic tracking-widest px-6"
        >
          <Trash2 className="w-4 h-4" />
          {loading ? "PROCESSANDO..." : "LIMPAR COORDENADAS"}
        </Button>
      </div>
      <p className="text-[9px] text-muted-foreground italic font-medium">
        * Isso removerá permanentemente os pontos de leitura (X, Y, Largura, Altura) para este template específico.
      </p>
    </div>
  );
};

// =========================================================================
// COMPONENTE PRINCIPAL: CONTROL PANEL
// =========================================================================
export default function ControlPanel() {
  const navigate = useNavigate();

  // ---------------------------------------------------------
  // 📦 ESTADOS DE DADOS (DATABASE & ACC SYNC)
  // ---------------------------------------------------------
  const [projects, setProjects] = useState<any[]>([]);
  const [normas, setNormas] = useState<Norma[]>([]);
  const [templates, setTemplates] = useState<any[]>([]);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeTab, setActiveTab] = useState("projetos");
  const [loading, setLoading] = useState(true);
  const [isNewOCRDocOpen, setIsNewOCRDocOpen] = useState(false);
  const [newOCRName, setNewOCRName] = useState("");
  
  // Presets LDP & Pacotes OCR
  const [savedConfigsLDP, setSavedConfigsLDP] = useState<any[]>([]);
  const [pacotesOCR, setPacotesOCR] = useState<any[]>([]);
  
  // Upload de Pacotes de Template
  const [filesToUpload, setFilesToUpload] = useState<File[]>([]);

  // ---------------------------------------------------------
  // 🛠️ ESTADOS DE EDIÇÃO: NORMAS & TEMPLATES
  // ---------------------------------------------------------
  const [editingNorma, setEditingNorma] = useState<Norma | null>(null);
  const [isNewNormaOpen, setIsNewNormaOpen] = useState(false);
  const [newNormaName, setNewNormaName] = useState("");
  const [newNormaDesc, setNewNormaDesc] = useState("");

  const [editingPackage, setEditingPackage] = useState<{ normaId: number | null, itens: any[] } | null>(null);
  const [newTplName, setNewTplName] = useState(""); 
  const [newTplSigla, setNewTplSigla] = useState("");
  const [isNewTemplateOpen, setIsNewTemplateOpen] = useState(false);

  // ---------------------------------------------------------
  // ⚠️ ESTADO DE EXCLUSÃO
  // ---------------------------------------------------------
  const [isConfirmDeleteOpen, setIsConfirmDeleteOpen] = useState(false);
  const [itemToDelete, setItemToDelete] = useState<{ id: number, type: 'norma' | 'template' | 'pacote' } | null>(null);

  // ---------------------------------------------------------
  // 🔄 CARREGAMENTO SINCRO COM ACC E BANCO LOCAL
  // ---------------------------------------------------------
  const fetchNormas = async () => {
    try {
      const resN = await fetch('/api/admin/normas');
      if (resN.ok) setNormas(await resN.json());
    } catch (error) {
      console.error("Erro ao buscar normas:", error);
    }
  };

  const carregarDadosReais = async () => {
    setLoading(true);
    try {
      // 1. Recupera o HubID do LocalStorage ou endpoint
      let hubId = localStorage.getItem("hubId");
      if (hubId === null) {
        try {
          const resHubs = await fetch('/DataManagement/Data/Hubs');
          if (resHubs.ok) {
            const hubsData = await resHubs.json();
            const hubs = Array.isArray(hubsData) ? hubsData : (hubsData.data || []);
            if (hubs.length > 0) {
              hubId = hubs[0].id;
              if (hubId !== null) localStorage.setItem("hubId", hubId);
            }
          }
        } catch (e) {
          console.error("Erro ao buscar hubs:", e);
        }
      }

      // 2. Chamadas paralelas
      const [resP, resN, resT, resOCR, resLDP, resACC] = await Promise.all([
        fetch('/api/admin/projetos'),
        fetch('/api/admin/normas'),
        fetch('/api/admin/templates'),
        fetch('/api/admin/ocr/pacotes'),
        fetch('/api/admin/projeto/listar-configuracoes-ldp'),
        hubId ? fetch(`/DataManagement/Data/Projetos?hubId=${hubId}`) : Promise.resolve(null)
      ]);

      let projetosBanco: any[] = [];
      if (resP.ok) {
        const dadosVindosDoBanco = await resP.json();
        projetosBanco = dadosVindosDoBanco.map((p: any) => ({
          ...p,
          is_Ativo: Boolean(p.is_Ativo)
        }));
      }

      let projetosACC: any[] = [];
      if (resACC && resACC.ok) {
        const text = await resACC.text();
        if (text && text.trim().length > 0) {
          const jsonACC = JSON.parse(text);
          projetosACC = Array.isArray(jsonACC) ? jsonACC : (jsonACC.data || []);
        }
      }

      // 3. Mesclagem entre ACC e Banco Local
      if (projetosACC.length > 0) {
        const projetosUnificados = projetosACC.map((accProj: any) => {
          const accId = accProj.id;
          const accName = accProj.attributes?.name || accProj.name || "Sem Nome";

          const matchBanco = projetosBanco.find(
            (p: any) => p.project_ID_ACC === accId || String(p.projetoId) === String(accId)
          );

          if (matchBanco) return matchBanco;

          return {
            projetoId: null,
            nome_Projeto: accName,
            project_ID_ACC: accId,
            is_Ativo: false,
            NormaID: null,
            PacoteOCRID: null,
            configId: "none"
          };
        });

        projetosBanco.forEach((pBanco: any) => {
          const jaExiste = projetosUnificados.some(
            (u: any) => u.project_ID_ACC === pBanco.project_ID_ACC || u.projetoId === pBanco.projetoId
          );
          if (!jaExiste) projetosUnificados.push(pBanco);
        });

        setProjects(projetosUnificados);
      } else {
        setProjects(projetosBanco);
      }

      if (resN && resN.ok) setNormas(await resN.json());
      if (resT && resT.ok) setTemplates(await resT.json());
      if (resOCR && resOCR.ok) setPacotesOCR(await resOCR.json());
      if (resLDP && resLDP.ok) setSavedConfigsLDP(await resLDP.json());

    } catch (error) {
      toast.error("Erro ao conectar com o servidor.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    carregarDadosReais();
  }, []);

  const filteredProjects = useMemo(
    () => projects.filter((p) => p.nome_Projeto?.toLowerCase().includes(searchTerm.toLowerCase())),
    [projects, searchTerm]
  );

  // ---------------------------------------------------------
  // ⚡ ALTERNAR STATUS E VINCULAÇÃO COM BANCO LOCAL (PATCH + SINCRONIZAÇÃO)
  // ---------------------------------------------------------
const handleToggle = useCallback(async (project: any) => {
  let idDoBancoLocal = project.projetoId;
  const novoStatus = !project.is_Ativo;
  
  // ✅ Normaliza a leitura da propriedade do ACC para evitar 'undefined'
  const accId = project.project_ID_ACC || project.ProjetoID;

  try {
    // ETAPA A: Auto-cadastro no Banco Local se veio do ACC
    if (!idDoBancoLocal) {
      toast.loading("Cadastrando projeto do ACC no banco local...", { id: "sinc-toast" });

      const resSinc = await fetch('/api/admin/projetos/sincronizar', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          Projetos: [
            { Id: accId, Nome: project.nome_Projeto }
          ]
        })
      });

      if (!resSinc.ok) {
        toast.error("Erro ao sincronizar projeto com o banco local.", { id: "sinc-toast" });
        return;
      }

      const resProjetosNovos = await fetch('/api/admin/projetos');
      if (resProjetosNovos.ok) {
        const listaAtualizada = await resProjetosNovos.json();
        const projetoCadastrado = listaAtualizada.find(
          (p: any) => p.project_ID_ACC === accId || p.ProjetoID === accId
        );
        if (projetoCadastrado) {
          idDoBancoLocal = projetoCadastrado.projetoId;
        }
      }
      toast.dismiss("sinc-toast");
    }

    if (!idDoBancoLocal) {
      toast.error("Não foi possível identificar o ID do projeto no banco local.");
      return;
    }

    // ETAPA B: PATCH no status
    const res = await fetch(`/api/admin/projeto/${idDoBancoLocal}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        is_Ativo: novoStatus,
        NormaID: project.NormaID || null,
        PacoteOCRID: project.PacoteOCRID || null
      }),
    });

    if (res.ok) {
      toast.success(novoStatus ? "Projeto ativado!" : "Projeto desativado!");

      setProjects((prevProjetos: any[]) =>
        prevProjetos.map((p) => {
          const match = (p.projetoId === idDoBancoLocal) || 
                        (p.project_ID_ACC && p.project_ID_ACC === accId);
          return match ? { ...p, projetoId: idDoBancoLocal, is_Ativo: novoStatus } : p;
        })
      );
    } else {
      const erroData = await res.json().catch(() => ({}));
      toast.error(`Erro: ${erroData.error || "Falha ao atualizar o status."}`);
    }
  } catch (error) {
    console.error("Erro ao alterar status:", error);
    toast.error("Erro de conexão ao atualizar status.");
  }
}, []);

// ---------------------------------------------------------
// ⚡ OUTROS HANDLERS DE PROJETO
// ---------------------------------------------------------

const handleApplyPreset = async (projectId: number, configId: string) => {
  if (!projectId) {
    toast.error("Ative ou sincronize o projeto antes de aplicar um preset.");
    return;
  }
  const tid = toast.loading("Vinculando configuração...");
  try {
    const res = await fetch(`/api/admin/projeto/vincular-config-ldp`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ 
        ProjetoId: projectId, 
        ConfigLDPID: configId === "none" ? null : configId 
      })
    });

    if (res.ok) {
      toast.success("Configuração vinculada!", { id: tid });
      carregarDadosReais();
    } else throw new Error();
  } catch (error) {
    toast.error("Erro ao vincular preset.", { id: tid });
  }
};

const handleNormaChange = useCallback(async (project: any, val: string) => {
  if (!project.projetoId) {
    toast.error("Ative o projeto antes de definir uma norma.");
    return;
  }
  const novaNormaId = val === "none" ? null : Number(val);
  
  setProjects(prev => prev.map(p => p.projetoId === project.projetoId ? { ...p, NormaID: novaNormaId } : p));

  try {
    const res = await fetch(`/api/admin/projeto/${project.projetoId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ 
        is_Ativo: project.is_Ativo, 
        NormaID: novaNormaId, 
        PacoteOCRID: project.PacoteOCRID 
      })
    });

    if (!res.ok) throw new Error();
    toast.success("Norma salva no banco!");
  } catch (err) {
    toast.error("Erro ao salvar norma no servidor.");
    carregarDadosReais(); 
  }
}, []);

  const handleUpdatePacoteOCR = async (project: any, val: string) => {
    if (!project.projetoId) {
      toast.error("Ative o projeto antes de definir um pacote OCR.");
      return;
    }
    const pacoteId = val === "" ? null : Number(val);
    
    setProjects(prev => prev.map(p => p.projetoId === project.projetoId ? { ...p, PacoteOCRID: pacoteId } : p));

    try {
      const res = await fetch(`/api/admin/projeto/${project.projetoId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ 
          is_Ativo: project.is_Ativo, 
          NormaID: project.NormaID, 
          PacoteOCRID: pacoteId 
        })
      });

      if (!res.ok) throw new Error();
      toast.success("Pacote OCR salvo no banco!");
    } catch (err) {
      toast.error("Erro ao salvar pacote OCR no servidor.");
      carregarDadosReais();
    }
  };

  const handleCreateOCRPackage = async () => {
    if (!newOCRName.trim()) {
      toast.error("Digite um nome para o pacote.");
      return;
    }

    const tid = toast.loading("Criando novo pacote OCR...");
    try {
      const res = await fetch('/api/admin/ocr/criar-pacote', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newOCRName) 
      });

      if (res.ok) {
        toast.success("Pacote criado com sucesso!", { id: tid });
        setNewOCRName("");
        setIsNewOCRDocOpen(false);
        carregarDadosReais(); 
      } else {
        throw new Error();
      }
    } catch (error) {
      toast.error("Erro ao criar pacote no banco.", { id: tid });
    }
  };

  // Grouping Templates by Norma
  const templateGroups = useMemo(() => {
    const groups: Record<string, TemplateGroup> = {};
    groups['global'] = { normaId: null, normaNome: "Templates Globais / Sem Vínculo", templates: [] };
    normas.forEach(n => {
      groups[n.normaId] = { normaId: n.normaId, normaNome: n.nome_Norma, templates: [] };
    });
    templates.forEach(tpl => {
      const nId = tpl.NormaID || tpl.normaID || tpl.Norma_ID;
      const key = nId ? String(nId) : 'global';
      if (groups[key]) groups[key].templates.push(tpl);
      else groups['global'].templates.push(tpl);
    });
    return Object.values(groups).sort((a, b) => (a.normaId === null ? -1 : 1)); 
  }, [templates, normas]);

  // Exclusões e Excel Handlers
  const triggerDeleteDialog = (id: number, type: 'norma' | 'template' | 'pacote') => {
    setItemToDelete({ id, type });
    setIsConfirmDeleteOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!itemToDelete) return;
    const { id, type } = itemToDelete;
    let endpoint = "";
    if (type === 'norma') endpoint = `/api/admin/norma/${id}`;
    if (type === 'template') endpoint = `/api/admin/template/${id}`;
    const tid = toast.loading(`ELIMINANDO ${type.toUpperCase()}...`);
    try {
      const res = await fetch(endpoint, { method: 'DELETE' });
      if (res.ok) {
        toast.success(`${type.toUpperCase()} REMOVIDO COM SUCESSO`, { id: tid });
        setIsConfirmDeleteOpen(false);
        if (type === 'norma') { setEditingNorma(null); fetchNormas(); }
        carregarDadosReais(); 
      } else throw new Error();
    } catch (error) {
      toast.error("FALHA NA EXCLUSÃO CRÍTICA", { id: tid });
    }
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>, type: 'norma' | 'template') => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (evt) => {
      const data = new Uint8Array(evt.target?.result as ArrayBuffer);
      const workbook = XLSX.read(data, { type: "array" });
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      const json = XLSX.utils.sheet_to_json<any>(sheet);

      if (type === 'norma' && editingNorma) {
        const mappings: Mapping[] = json.map((row) => {
          const gerar = String(row["Gerar Placeholder"] || row["Gerar"] || row["Processar"] || "Sim").trim().toLowerCase();
          const deveIgnorar = ["não", "nao", "no", "false", "0"].includes(gerar);
          return {
            sigla: String(row["Sigla"] || row["sigla"] || row["SIGLA"] || ""),
            pasta_Destino: String(row["Pasta de Destino"] || row["Pasta Destino"] || row["pasta_destino"] || ""),
            tipo_Documento: String(row["Tipo"] || row["tipo"] || row["TIPO"] || ""),
            ignorar: deveIgnorar 
          };
        }).filter(m => m.sigla);
        const updated = { ...editingNorma, mappings };
        setEditingNorma(updated);
        handleSaveNorma(updated);
      }
      
      if (type === 'template' && editingPackage) {
        const novosItens = json.map(row => {
          let nomeArquivo = row["Arquivo"] || row["ARQUIVO"] || row["Nome do Arquivo"] || row["Template"] || row["TEMPLATE"] || row["Nome"];
          let tipoDoc = row["Tipo"] || row["TIPO"] || "Geral";
          if (!nomeArquivo && String(tipoDoc).includes(".")) {
            nomeArquivo = tipoDoc; 
            tipoDoc = "Template Base"; 
          }
          return {
            sigla_Doc: String(row["Sigla"] || row["SIGLA"] || row["Codigo"] || ""),
            template_Nome: String(nomeArquivo || ""),
            tipo_Doc: String(tipoDoc)
          };
        }).filter(item => item.sigla_Doc !== "" && item.template_Nome !== ""); 
        if (novosItens.length === 0) {
          toast.error("Erro: Planilha vazia ou colunas não identificadas.");
          return;
        }
        setEditingPackage({ ...editingPackage, itens: novosItens });
        toast.success(`${novosItens.length} templates identificados!`);
      }
    };
    reader.readAsArrayBuffer(file);
  };

  const handleSaveNorma = async (normaParaSalvar: Norma) => {
    const tid = toast.loading("Salvando norma...");
    try {
      const res = await fetch('/api/admin/salvar-norma', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(normaParaSalvar)
      });
      if (res.ok) { toast.success("Salva!", { id: tid }); fetchNormas(); }
      else throw new Error();
    } catch { toast.error("Erro ao salvar.", { id: tid }); }
  };

  const handleMappingEdit = (index: number, field: keyof Mapping, value: string) => {
    if (!editingNorma) return;
    const updatedMappings = [...editingNorma.mappings];
    updatedMappings[index] = { ...updatedMappings[index], [field]: value };
    setEditingNorma({ ...editingNorma, mappings: updatedMappings });
  };

  const handleCreateNorma = () => {
    if (!newNormaName.trim()) { toast.error("Dê um nome."); return; }
    const newNorma: Norma = { normaId: 0, nome_Norma: newNormaName, descricao: newNormaDesc, mappings: [] };
    handleSaveNorma(newNorma);
    setIsNewNormaOpen(false);
    setNewNormaName("");
    setNewNormaDesc("");
  };

  const handleCreateTemplate = async () => {
    if (!newTplName || !newTplSigla) { toast.error("Preencha os campos."); return; }
    const tid = toast.loading("Salvando...");
    const payload = { Sigla_Doc: newTplSigla, Template_Nome: newTplName, Tipo_Doc: "Geral", NormaID: null };
    try {
      const res = await fetch('/api/admin/template', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        toast.success("Criado!", { id: tid });
        setIsNewTemplateOpen(false);
        setNewTplName("");
        setNewTplSigla("");
        carregarDadosReais(); 
      } else toast.error("Erro no Backend", { id: tid });
    } catch (error) { toast.error("Erro de conexão", { id: tid }); }
  };

  const handleSavePackage = async () => {
    if (!editingPackage || !editingPackage.normaId) return;
    const tid = toast.loading("Subindo Pacote...");
    const formData = new FormData();
    formData.append("metadados", JSON.stringify(editingPackage.itens || []));
    filesToUpload.forEach(file => formData.append("files", file));
    try {
      const res = await fetch(`/api/admin/templates/upload-pacote/${editingPackage.normaId}`, {
        method: 'POST',
        body: formData 
      });
      if (res.ok) {
        toast.success("Sincronizado!", { id: tid });
        setEditingPackage(null);
        setFilesToUpload([]);
        carregarDadosReais(); 
      } else throw new Error();
    } catch (err) { toast.error("Falha no upload", { id: tid }); }
  };

  const openPackageEditor = (normaId: number | null, normaNome: string) => {
    if (!normaId) return;
    const currentTemplates = templates.filter(t => (t.NormaID || t.normaID) === normaId);
    setEditingPackage({
      normaId: normaId,
      itens: currentTemplates.map(t => ({
        sigla_Doc: t.sigla_Doc || t.Sigla_Doc,
        template_Nome: t.template_Nome || t.Template_Nome,
        tipo_Doc: t.tipo_Doc || t.Tipo_Doc || "Geral"
      }))
    });
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
          
    
    <div className="flex flex-col gap-4 animate-enter m-4">
      
      <div className="space-y-2">
        
        <div className="flex items-center gap-2 text-primary mb-1">
          
          <Settings2 className="w-5 h-5" />
          
          
          <span className="text-[10px] font-black uppercase tracking-[0.3em]">Administração
            
          </span>
        </div>
        
        <h1 className="text-3xl font-black text-foreground uppercase italic tracking-tight ">Painel de Controle</h1>
        
      </div>
      
      

      <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
        <TabsList className="bg-card/30 border border-border/50 backdrop-blur-sm p-1 h-auto gap-1">
          <TabsTrigger value="projetos" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Gestão de Projetos</TabsTrigger>
          <TabsTrigger value="normas" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração de Normas</TabsTrigger>
          <TabsTrigger value="templates" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração de Templates</TabsTrigger>
          <TabsTrigger value="plant3d" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração Plant 3D</TabsTrigger>
        </TabsList>

        <TabsContent value="projetos" className="mt-6">
          <div className="mb-8 p-6 bg-primary/5 border border-primary/20 rounded-3xl flex items-center justify-between gap-6">
            <div className="flex items-center gap-4">
              <div className="p-3 bg-primary/10 rounded-2xl text-primary"><Copy size={24} /></div>
              <div>
                <h4 className="text-xs font-black uppercase italic tracking-tighter text-foreground">Biblioteca de Presets LDP</h4>
                <p className="text-[10px] text-muted-foreground uppercase font-bold">Vincule rapidamente configurações existentes a novos projetos</p>
              </div>
            </div>
            
            <div className="flex-1 max-w-md">
              <Select onValueChange={(val) => {
                const [projId, configId] = val.split("|");
                handleApplyPreset(Number(projId), configId);
              }}>
                <SelectTrigger className="bg-card/50 border-border/50 text-[10px] font-black uppercase">
                  <SelectValue placeholder="SELECIONE UM MODELO PARA COPIAR" />
                </SelectTrigger>
                <SelectContent>
                  {savedConfigsLDP.map((cfg: any) => {
                    const id = String(cfg.configLDPID || cfg.ConfigLDPID || cfg.id);
                    const name = cfg.nomeConfig || cfg.NomeConfig || cfg.nome || "Template Sem Nome";
                    return (
                      <SelectItem key={id} value={`0|${id}`}>
                        📋 {name}
                      </SelectItem>
                    );
                  })}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="mb-6 flex justify-between items-center">
            <div className="relative max-w-sm w-full">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <Input placeholder="Pesquisar projeto..." className="pl-10 bg-card/30 border-border/50" value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} />
            </div>
            
            <div className="flex items-center gap-4">
              <Button 
                onClick={() => navigate('/ocr')}
                className="bg-primary hover:bg-primary/90 text-white font-black text-[10px] uppercase italic tracking-widest shadow-lg shadow-primary/20"
              >
                <Eye className="w-4 h-4 mr-2" />
                Configurar Novo Carimbo
              </Button>
              {loading && <span className="text-[10px] font-black animate-pulse uppercase">Sincronizando...</span>}
            </div>
          </div>

          <div className="w-full overflow-hidden rounded-xl border border-border/50 glass-card shadow-2xl">
            <table className="w-full text-left border-collapse table-fixed">
              <thead>
                <tr className="bg-muted/40 text-[10px] uppercase tracking-[0.2em] text-muted-foreground font-black border-b border-border/50">
                  <th className="px-6 py-5 w-[35%]">Projeto</th>
                  <th className="px-4 py-5 w-[15%]">PACOTE OCR</th>
                  <th className="px-4 py-5 text-center w-[15%]">Status</th>
                  <th className="px-4 py-5 w-[20%]">Norma Aplicada</th>
                  <th className="px-4 py-5 w-[15%]">Template LDP</th>
                  <th className="px-4 py-5 w-[15%] text-left text-[10px] font-black uppercase tracking-widest text-muted-foreground">ECOSSISTEMA</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/30 bg-card/10">
                {filteredProjects.map((project: any) => {
                  const currentConfigId = project.configId || project.ConfigLDPID || project.configLDPID;
                  const selectValue = currentConfigId ? String(currentConfigId) : "none";

                  const currentPacoteId = project.PacoteOCRID || project.pacoteOCRID;
                  const pacoteValue = currentPacoteId ? String(currentPacoteId) : "";

                  const currentNormaId = project.NormaID || project.normaID;
                  const normaValue = currentNormaId ? String(currentNormaId) : "none";

                  return (
                    <tr key={project.projetoId || project.project_ID_ACC} className="group hover:bg-primary/5 transition-all duration-300">
                      <td className="px-6 py-4">
                        <div className="flex flex-col gap-0.5">
                          <span className="text-sm font-bold text-foreground">{project.nome_Projeto}</span>
                          <span className="text-[10px] text-muted-foreground font-medium uppercase">{project.project_ID_ACC?.slice(0, 12)}...</span>
                        </div>
                      </td>

                      <td className="px-4 py-4">
                        <select
                          title="Selecione o Pacote de Visão OCR"
                          value={pacoteValue}
                          onChange={(e) => handleUpdatePacoteOCR(project, e.target.value)}
                          className="w-full bg-card/30 border border-border/50 rounded-md px-2 py-1 text-[10px] font-black uppercase"
                        >
                          <option value="">NENHUM PACOTE</option>
                          {pacotesOCR.map(p => (
                            <option key={p.id} value={p.id}>🏢 {p.nome}</option>
                          ))}
                        </select>
                      </td>

                      <td className="px-4 py-4 text-center">
                        <div className="flex items-center justify-center gap-2">
                          <Switch checked={project.is_Ativo} onCheckedChange={() => handleToggle(project)} />
                          <span className={cn(
                            "text-[9px] font-black uppercase",
                            project.is_Ativo ? "text-emerald-500" : "text-muted-foreground"
                          )}>
                            {project.is_Ativo ? "Ativo" : "Inativo"}
                          </span>
                        </div>
                      </td>

                      <td className="px-4 py-4">
                        <Select 
                          value={normaValue}
                          onValueChange={(val) => handleNormaChange(project, val)}
                        >
                          <SelectTrigger className="bg-card/30 border-border/50 text-[10px] font-black uppercase h-9">
                            <SelectValue placeholder="Norma" />
                          </SelectTrigger>
                          <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
                            <SelectItem value="none" className="text-[10px] font-black uppercase italic">Nenhuma</SelectItem>
                            {normas.map((n) => (
                              <SelectItem key={n.normaId} value={String(n.normaId)} className="text-[10px] font-black uppercase">
                                {n.nome_Norma}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </td>

                      <td className="px-4 py-4">
                        <Select 
                          value={selectValue}
                          onValueChange={(val) => handleApplyPreset(project.projetoId, val)}
                        >
                          <SelectTrigger className="bg-card/30 border-border/50 text-[10px] font-black uppercase h-9">
                            <SelectValue placeholder="Template LDP" />
                          </SelectTrigger>
                          <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
                            <SelectItem value="none" className="text-[10px] font-black uppercase italic">Personalizado</SelectItem>
                            {savedConfigsLDP.map((cfg: any) => {
                              const id = String(cfg.configLDPID || cfg.ConfigLDPID || cfg.id);
                              const name = cfg.nomeConfig || cfg.NomeConfig || cfg.nome || "Template Sem Nome";
                              return (
                                <SelectItem key={id} value={id} className="text-[10px] font-black uppercase">
                                  {name}
                                </SelectItem>
                              );
                            })}
                          </SelectContent>
                        </Select>
                      </td>

                      <td className="px-4 py-4">
                        <div className="flex items-center gap-3">
                          <div 
                            title={project.ldpFolderId ? "Pasta LDP (Excel) vinculada com sucesso" : "Pasta LDP não configurada"}
                            className={cn(
                              "p-2 rounded-lg border transition-all",
                              project.ldpFolderId 
                                ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-500 shadow-[0_0_10px_rgba(16,185,129,0.1)]" 
                                : "bg-slate-800/50 border-white/5 text-slate-600 opacity-40"
                            )}
                          >
                            <FileSpreadsheet size={16} />
                          </div>

                          <div 
                            title={project.cronFolderId ? "Pasta do Cronograma vinculada com sucesso" : "Cronograma não configurado"}
                            className={cn(
                              "p-2 rounded-lg border transition-all",
                              project.cronFolderId 
                                ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-500 shadow-[0_0_10px_rgba(16,185,129,0.1)]" 
                                : "bg-slate-800/50 border-white/5 text-slate-600 opacity-40"
                            )}
                          >
                            <Clock size={16} />
                          </div>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="mt-8 max-w-2xl">
            <h3 className="text-[10px] font-black uppercase tracking-widest text-muted-foreground mb-4 italic">Manutenção de Dados OCR</h3>
            <OCRCleanupTool pacotes={pacotesOCR} onRefresh={carregarDadosReais} />
          </div>
        </TabsContent>

        <TabsContent value="normas" className="mt-6">
          {editingNorma ? (
            <div className="space-y-6 animate-enter">
              <div className="flex items-center justify-between">
                <div>
                  <button onClick={() => setEditingNorma(null)} className="text-[10px] font-black uppercase text-muted-foreground hover:text-primary mb-2 flex items-center gap-1 transition-colors"><ArrowLeft className="w-3 h-3"/> Voltar</button>
                  <h2 className="text-2xl font-black uppercase italic text-foreground tracking-tighter">{editingNorma.nome_Norma}</h2>
                </div>
                <Button onClick={() => handleSaveNorma(editingNorma)} className="bg-orange-600 hover:bg-orange-700 text-white font-black text-[10px] uppercase italic tracking-widest shadow-lg shadow-orange-900/20">Salvar Norma</Button>
              </div>
              <div className="glass-card border-2 border-dashed border-border/50 hover:border-primary/50 rounded-xl p-10 text-center cursor-pointer relative transition-colors">
                <input type="file" accept=".xlsx,.xls" onChange={(e) => handleFileUpload(e, 'norma')} className="hidden" id="file-norma" />
                <label htmlFor="file-norma" className="cursor-pointer flex flex-col items-center gap-4">
                  <Upload className="w-8 h-8 text-primary" />
                  <p className="text-sm font-bold uppercase italic tracking-tighter text-foreground">Importar Tabela (Excel)</p>
                </label>
              </div>
              {editingNorma.mappings.length > 0 && (
                <div className="w-full overflow-hidden rounded-xl border border-border/50 glass-card">
                  <table className="w-full text-left border-collapse">
                    <thead><tr className="bg-muted/40 text-[10px] uppercase text-muted-foreground font-black border-b border-border/50"><th className="px-6 py-5 w-[20%]">Sigla</th><th className="px-6 py-5 w-[55%]">Pasta de Destino</th><th className="px-6 py-5 w-[25%]">Tipo</th></tr></thead>
                    <tbody className="divide-y divide-border/30 bg-card/10">
                      {editingNorma.mappings.map((m, i) => (
                        <tr key={i} className="hover:bg-primary/5 transition-colors">
                          <td className="px-6 py-3"><span className="px-3 py-1 rounded-md text-[10px] font-black bg-primary/10 text-primary border border-primary/20">{m.sigla}</span></td>
                          <td className="px-6 py-3"><Input value={m.pasta_Destino} onChange={(e) => handleMappingEdit(i, "pasta_Destino", e.target.value)} className="bg-transparent border-none text-sm font-bold focus-visible:ring-1 focus-visible:ring-primary/50" /></td>
                          <td className="px-6 py-3 font-black text-[10px] uppercase text-muted-foreground">{m.tipo_Documento}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          ) : (
            <div className="space-y-6">
              <div className="flex justify-end"><Button onClick={() => setIsNewNormaOpen(true)} className="font-black text-[10px] uppercase gap-2"><Plus className="w-4 h-4" /> Nova Norma</Button></div>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {normas.map((norma) => (
                  <div key={norma.normaId} onClick={() => setEditingNorma(norma)} className="group cursor-pointer glass-card p-6 rounded-xl border border-border/50 hover:border-primary/50 transition-all shadow-2xl relative">
                    <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); triggerDeleteDialog(norma.normaId, 'norma'); }} className="absolute top-2 right-2 h-8 w-8 p-0 opacity-0 group-hover:opacity-100 border border-white/5 bg-white/5 hover:bg-destructive/20 hover:text-destructive transition-all rounded-lg backdrop-blur-md"><Trash2 className="w-3.5 h-3.5" /></Button>
                    <div className="w-12 h-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4 group-hover:scale-110 transition-transform"><FileSpreadsheet className="w-6 h-6 text-primary" /></div>
                    <h3 className="font-bold text-foreground text-lg uppercase italic pr-8">{norma.nome_Norma}</h3>
                    <div className="mt-4 flex items-center justify-between text-[10px] text-muted-foreground uppercase font-black"><span>{norma.mappings.length} REGRAS</span><span className="opacity-0 group-hover:opacity-100 transition-opacity text-primary italic">CONFIGURAR →</span></div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </TabsContent>

        <TabsContent value="templates" className="mt-6">
          {editingPackage ? (
            <div className="space-y-6 animate-enter">
              <div className="flex items-center justify-between">
                <div>
                  <button onClick={() => setEditingPackage(null)} className="text-[10px] font-black uppercase text-muted-foreground flex items-center gap-1"><ArrowLeft className="w-3 h-3"/> Voltar</button>
                  <h2 className="text-2xl font-black uppercase italic tracking-tighter text-primary">Editando Pacote</h2>
                </div>
                <Button onClick={handleSavePackage} className="bg-orange-600 hover:bg-orange-700 text-white font-black text-[10px] uppercase italic tracking-widest shadow-lg shadow-orange-900/20">Salvar Pacote</Button>
              </div>

              <div className="grid grid-cols-12 gap-8">
                <div className="col-span-7 space-y-4">
                  <div className="glass-card p-6 border-dashed border-2 border-primary/30 text-center relative hover:bg-primary/5 transition-colors cursor-pointer">
                    <input type="file" accept=".xlsx" onChange={(e) => handleFileUpload(e, 'template')} className="absolute inset-0 opacity-0 cursor-pointer" title="Importar planilha de mapeamento Excel" />
                    <div className="flex flex-col items-center gap-2 pointer-events-none">
                      <FileSpreadsheet className="w-8 h-8 text-primary" />
                      <span className="font-black text-[10px] uppercase italic text-primary">1. Arraste a Planilha aqui</span>
                    </div>
                  </div>
                  <div className="w-full overflow-hidden rounded-xl border border-border/50 glass-card">
                    <table className="w-full text-left">
                      <thead className="bg-muted/40 text-[9px] uppercase font-black text-muted-foreground border-b border-border/50">
                        <tr><th className="px-4 py-3">Sigla</th><th className="px-4 py-3">Arquivo</th><th className="px-4 py-3">Status</th></tr>
                      </thead>
                      <tbody className="bg-card/10 italic divide-y divide-border/20">
                        {editingPackage.itens.map((item: any, i: number) => {
                          const fileMatch = filesToUpload.find(f => f.name === item.template_Nome);
                          return (
                            <tr key={i}>
                              <td className="px-4 py-2 font-black text-primary text-[10px]">{item.sigla_Doc}</td>
                              <td className="px-4 py-2 text-[11px] truncate max-w-[150px]">{item.template_Nome}</td>
                              <td className="px-4 py-2">
                                {fileMatch ? <span className="text-green-500 font-black text-[9px] uppercase">Pronto</span> : <span className="text-orange-500 font-black text-[9px] uppercase">Pendente</span>}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>

                <div className="col-span-5 space-y-4">
                  <div className="glass-card p-6 border-dashed border-2 border-orange-500/30 text-center relative hover:bg-orange-500/5 transition-colors cursor-pointer">
                    <input type="file" multiple onChange={(e) => setFilesToUpload(Array.from(e.target.files || []))} className="absolute inset-0 opacity-0 cursor-pointer" title="Upload dos arquivos de template" />
                    <div className="flex flex-col items-center gap-2 pointer-events-none">
                      <FolderOpen className="w-8 h-8 text-orange-500" />
                      <span className="font-black text-[10px] uppercase italic text-orange-500">2. Arraste os Arquivos</span>
                    </div>
                  </div>
                  <div className="bg-black/20 p-4 rounded-lg text-[10px] font-mono text-muted-foreground max-h-[300px] overflow-y-auto">
                    {filesToUpload.length === 0 && <span className="italic opacity-50 text-[10px]">Nenhum arquivo selecionado...</span>}
                    {filesToUpload.map(f => <div key={f.name} className="flex items-center gap-2 mb-1"><div className="w-1.5 h-1.5 rounded-full bg-orange-500"></div><span className="truncate">{f.name}</span></div>)}
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <div className="space-y-8 animate-enter">
              <div className="flex justify-end"><Button onClick={() => setIsNewTemplateOpen(true)} className="font-black text-[10px] uppercase gap-2 bg-primary hover:bg-primary/80"><Plus className="w-4 h-4" /> Novo Template Avulso</Button></div>
              {templateGroups.map(group => (
                <div key={group.normaId || 'global'} className="space-y-4">
                  <div className="flex items-center gap-4 border-b border-border/30 pb-2">
                    <Package className="w-5 h-5 text-primary" />
                    <div>
                      <h3 className="text-lg font-black uppercase italic tracking-tighter text-foreground">{group.normaNome}</h3>
                      <p className="text-[10px] text-muted-foreground font-bold uppercase">{group.templates.length} vinculados</p>
                    </div>
                    {group.normaId && (
                      <Button variant="outline" size="sm" onClick={() => openPackageEditor(group.normaId, group.normaNome)} className="ml-auto font-black text-[9px] uppercase border-primary/30 text-primary hover:bg-primary/10">Gerenciar Pacote</Button>
                    )}
                  </div>
                  <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                    {group.templates.map((tpl) => (
                      <div key={tpl.templateId || tpl.TemplateID || `tpl-${tpl.sigla_Doc}`} className="group glass-card relative p-4 rounded-xl border border-border/50">
                        <Button variant="ghost" size="sm" onClick={() => triggerDeleteDialog(tpl.templateId || tpl.TemplateID, 'template')} className="absolute top-2 right-2 h-6 w-6 p-0 opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all rounded-md"><Trash2 className="w-3.5 h-3.5" /></Button>
                        <div className="flex items-center gap-3">
                          <div className="w-8 h-8 rounded bg-secondary/50 flex items-center justify-center"><FileCode className="w-4 h-4 text-muted-foreground" /></div>
                          <div className="overflow-hidden">
                            <div className="px-1.5 py-0.5 rounded bg-primary/10 text-primary text-[7px] font-black uppercase w-fit mb-1 border border-primary/20">{tpl.sigla_Doc || tpl.Sigla_Doc}</div>
                            <h4 className="font-bold text-foreground text-xs uppercase italic truncate w-full" title={tpl.template_Nome || tpl.Template_Nome}>{tpl.template_Nome || tpl.Template_Nome}</h4>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="plant3d" className="mt-6">
          <PlantMappingPanel projectId={searchTerm} />
        </TabsContent>
      </Tabs>
            
      {/* DIÁLOGOS DE CONFIRMAÇÃO E CRIAÇÃO */}
      <Dialog open={isNewNormaOpen} onOpenChange={setIsNewNormaOpen}>
        <DialogContent className="glass-card border-border/50 backdrop-blur-xl bg-popover text-foreground">
          <DialogHeader><DialogTitle className="text-xl font-black uppercase italic">Nova Norma Técnica</DialogTitle></DialogHeader>
          <div className="space-y-4 py-4">
            <div className="space-y-2"><label className="text-[10px] font-black uppercase text-muted-foreground">Nome da Norma</label><Input value={newNormaName} onChange={(e) => setNewNormaName(e.target.value)} placeholder="Ex: Padrão N&A" className="bg-card/30 border-border/50" /></div>
            <div className="space-y-2"><label className="text-[10px] font-black uppercase text-muted-foreground">Descrição</label><Input value={newNormaDesc} onChange={(e) => setNewNormaDesc(e.target.value)} placeholder="..." className="bg-card/30 border-border/50" /></div>
          </div>
          <DialogFooter><Button variant="outline" onClick={() => setIsNewNormaOpen(false)} className="font-black text-[10px] uppercase font-sans">Cancelar</Button><Button onClick={handleCreateNorma} className="font-black text-[10px] uppercase font-sans tracking-widest bg-primary hover:bg-primary/80">Criar</Button></DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isNewTemplateOpen} onOpenChange={setIsNewTemplateOpen}>
        <DialogContent className="glass-card border-border/50 backdrop-blur-xl bg-popover text-foreground">
          <DialogHeader><DialogTitle className="text-xl font-black uppercase italic">Novo Template</DialogTitle></DialogHeader>
          <div className="space-y-4 py-4">
            <div className="space-y-2"><label className="text-[10px] font-black uppercase text-muted-foreground">Sigla</label><Input value={newTplSigla} onChange={(e) => setNewTplSigla(e.target.value.toUpperCase())} placeholder="Ex: LD" className="bg-card/30 border-border/50" /></div>
            <div className="space-y-2"><label className="text-[10px] font-black uppercase text-muted-foreground">Arquivo</label><Input value={newTplName} onChange={(e) => setNewTplName(e.target.value)} placeholder="Ex: template.xlsx" className="bg-card/30 border-border/50" /></div>
          </div>
          <DialogFooter><Button variant="outline" onClick={() => setIsNewTemplateOpen(false)} className="font-black text-[10px] uppercase font-sans">Cancelar</Button><Button onClick={handleCreateTemplate} className="font-black text-[10px] uppercase font-sans tracking-widest bg-primary hover:bg-primary/80">Salvar</Button></DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isConfirmDeleteOpen} onOpenChange={setIsConfirmDeleteOpen}>
        <DialogContent className="glass-card border-destructive/20 backdrop-blur-2xl bg-background/95 text-foreground max-w-[400px]">
          <DialogHeader>
            <div className="flex items-center gap-3 text-destructive mb-2"><AlertTriangle className="w-5 h-5" /><span className="text-[10px] font-black uppercase tracking-[0.3em]">Ação Crítica</span></div>
            <DialogTitle className="text-xl font-black uppercase italic tracking-tighter">Confirmar Eliminação?</DialogTitle>
            <p className="text-muted-foreground text-xs font-medium leading-relaxed pt-2 text-center">Esta operação é irreversível.</p>
          </DialogHeader>
          <DialogFooter className="mt-6 flex gap-2">
            <Button variant="ghost" onClick={() => setIsConfirmDeleteOpen(false)} className="font-black text-[10px] uppercase tracking-widest hover:bg-white/5 flex-1 font-sans">Cancelar</Button>
            <Button onClick={handleConfirmDelete} className="bg-red-600 hover:bg-red-700 text-white font-black text-[10px] uppercase tracking-widest italic transition-all duration-300 active:scale-95 flex-[1.5] font-sans">Confirmar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isNewOCRDocOpen} onOpenChange={setIsNewOCRDocOpen}>
        <DialogContent className="glass-card border-border/50 backdrop-blur-xl bg-popover text-foreground">
          <DialogHeader>
            <div className="flex items-center gap-2 text-primary mb-2">
              <Package className="w-5 h-5" />
              <span className="text-[10px] font-black uppercase tracking-[0.2em]">Configuração OCR</span>
            </div>
            <DialogTitle className="text-xl font-black uppercase italic tracking-tighter">Novo Pacote de Visão</DialogTitle>
          </DialogHeader>
          
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <label className="text-[10px] font-black uppercase text-muted-foreground">Nome do Template (Ex: Petrobras - Revisão 2024)</label>
              <Input 
                value={newOCRName} 
                onChange={(e) => setNewOCRName(e.target.value)} 
                placeholder="Digite o nome..." 
                className="bg-card/30 border-border/50 uppercase font-bold text-xs" 
              />
            </div>
            <p className="text-[9px] text-muted-foreground italic">
              * Após criar, você poderá selecionar este pacote na tabela de projetos e calibrar as coordenadas na página de Visão.
            </p>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNewOCRDocOpen(false)} className="font-black text-[10px] uppercase">Cancelar</Button>
            <Button onClick={handleCreateOCRPackage} className="font-black text-[10px] uppercase tracking-widest bg-primary hover:bg-primary/80">Criar Pacote</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
    </div>
      </SidebarInset>
    </SidebarProvider>
  );
}