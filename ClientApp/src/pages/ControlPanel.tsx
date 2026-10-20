// ATUALIZADO 2026-09-17: aba "Gestão de Projetos" agora é REAL para Is_Ativo/Tipo_LDP/Naming_Standard
// (AdminProjetosController, dados de dbo.Projetos). BACKEND_PENDENTE continua valendo só para: Normas
// (CRUD universal sigla→pasta→tipo, congelado por decisão do usuário — ver AUDITORIA_FORGESYNC_NA/15-*.md),
// Templates, PacotesOCR/aba OCR, Presets LDP (api/admin/projeto/listar-configuracoes-ldp,
// vincular-config-ldp) e aba Plant3D. Não inventar backend para esses.
// Localização: src/pages/ControlPanel.tsx
import { useState, useMemo, useCallback, useEffect } from "react";
// ✅ Ícones completos para suporte total à UI (Adicionados Package e FolderOpen)
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
  Package,     // Novo
  FolderOpen,   // Novo
  Eye,           // Novo para o botão de calibração
  Clock,         // ✅ Novo para Cronograma
  CheckCircle2,  // ✅ Novo para Status Ativo
  XCircle,        // ✅ Novo para Status Inativo
  Copy           // ✅ Novo para Presets
} from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import toast from 'react-hot-toast';
import { useNavigate } from "react-router-dom"; // Importado para o botão navegar
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils"; // ✅ Importado para estilização condicional
import type { Project, Norma, Mapping } from "@/types/control-panel";
import { getProjetosRegistrados } from "@/lib/projectsRegistry";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { priorizarProjetoAtual } from "@/features/project/priorizarProjetoAtual";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { ProjectAuditTemplateSelect, useProjectAuditTemplates } from "@/features/document-quality/ProjectAuditTemplateColumn";
import * as XLSX from "xlsx";
import PlantMappingPanel from "./PlantMappingPanel"; // Certifique-se de que o arquivo .tsx tenha este nome
// Interface para o agrupamento visual
interface TemplateGroup {
  normaId: number | null; // null = Global
  normaNome: string;
  templates: any[];
}
// ✅ Versão modular e completa para inserir antes do seu ControlPanel
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
      // Usando fetch para manter o padrão do seu código
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
        {/* Seleção do Pacote */}
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

        {/* Seleção da Folha */}
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
/**
 * somenteProjetos (30/09/2026): modo embutido no Painel do Planejador — só a listagem/configuração de projetos
 * (10 por página), sem o painel de abas antigo (Normas/Templates/Plant 3D têm backend congelado) e sem os controles
 * OCR não funcionais (vínculo inexistente em dbo.Projetos / endpoint de pacotes inexistente).
 */
export default function ControlPanel({ somenteProjetos = false }: { somenteProjetos?: boolean } = {}) {
  const navigate = useNavigate(); // Hook para navegação
  const [paginaProjetos, setPaginaProjetos] = useState(1);
  const PROJETOS_POR_PAGINA = 10;

  // ---------------------------------------------------------
  // 📦 ESTADOS DE DADOS (DATABASE SYNC)
  // ---------------------------------------------------------
  const [projects, setProjects] = useState<Project[]>([]);
  const [normas, setNormas] = useState<Norma[]>([]);
  const [templates, setTemplates] = useState<any[]>([]);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeTab, setActiveTab] = useState("projetos");
  const [loading, setLoading] = useState(true);
  // Checkpoint 17: distingue loading/empty(HTTP 200 + [])/error — tabela vazia sem mensagem escondia
  // falhas reais de carregamento (proxy/sessão) atrás de "0 projetos".
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isNewOCRDocOpen, setIsNewOCRDocOpen] = useState(false);
  const [newOCRName, setNewOCRName] = useState("");
  
  // savedConfigsLDP removido (checkpoint Gestão de Projetos, 2026-09-18) — só alimentava o bloco
  // "Biblioteca de Presets LDP" e a coluna "Template LDP", ambos removidos desta aba.

  // ✅ Estado para os pacotes de OCR
  const [pacotesOCR, setPacotesOCR] = useState<any[]>([]);
  
  // Estado para arquivos físicos no Upload de Pacote
  const [filesToUpload, setFilesToUpload] = useState<File[]>([]);

  // ---------------------------------------------------------
  // 🛠️ ESTADOS DE EDIÇÃO: NORMAS
  // ---------------------------------------------------------
  const [editingNorma, setEditingNorma] = useState<Norma | null>(null);
  const [isNewNormaOpen, setIsNewNormaOpen] = useState(false);
  const [newNormaName, setNewNormaName] = useState("");
  const [newNormaDesc, setNewNormaDesc] = useState("");

  // ---------------------------------------------------------
  // 📋 ESTADOS DE EDIÇÃO: TEMPLATES (PACOTE & INDIVIDUAL)
  // ---------------------------------------------------------
  const [editingTemplate, setEditingTemplate] = useState<any | null>(null); 
  
  // ✅ NOVO ESTADO: Controla a edição de PACOTES (Excel + Arquivos)
  const [editingPackage, setEditingPackage] = useState<{ normaId: number | null, itens: any[] } | null>(null);

  const [newTplName, setNewTplName] = useState(""); 
  const [newTplSigla, setNewTplSigla] = useState("");
  const [isNewTemplateOpen, setIsNewTemplateOpen] = useState(false);

  // ---------------------------------------------------------
  // ⚠️ ESTADOS DE EXCLUSÃO (SISTEMA UNIFICADO)
  // ---------------------------------------------------------
  const [isConfirmDeleteOpen, setIsConfirmDeleteOpen] = useState(false);
  const [itemToDelete, setItemToDelete] = useState<{ id: number, type: 'norma' | 'template' | 'pacote' } | null>(null);

  // ---------------------------------------------------------
  // 🔄 CARREGAMENTO DE DADOS REAIS
  // ---------------------------------------------------------
  
  const fetchNormas = async () => {
    try {
      const resN = await fetch('/api/admin/normas');
      if (resN.ok) setNormas(await resN.json());
    } catch (error) {
      console.error("Erro ao buscar normas:", error);
    }
  };

  const fetchTemplates = async () => {
    try {
      const resT = await fetch('/api/admin/templates');
      if (resT.ok) setTemplates(await resT.json());
    } catch (error) {
      console.error("Erro ao buscar templates:", error);
    }
  };

  const carregarDadosReais = async () => {
    setLoading(true);
    setLoadError(null);
    try {
        // ✅ Lista de projetos vem do registry central (mesma fonte que Dashboard/Projects usam para
        // cruzar Is_Ativo) — nunca um fetch direto a /api/admin/projetos duplicado aqui. force=true
        // porque esta tela precisa de dados sempre atuais após cada PATCH (o registry cacheia por
        // padrão para o resto da SPA). Diferente de Dashboard/Projects, esta tela NÃO é fail-open:
        // se getProjetosRegistrados rejeitar, mostramos um erro real (ver loadError), nunca uma
        // tabela vazia disfarçada de "nenhum projeto" (checkpoint 17).
        //
        // BACKEND_PENDENTE (checkpoint 16, fechamento 2026-09-17): normas/templates/pacotesOCR/
        // savedConfigsLDP NÃO são mais buscados na abertura da tela — os endpoints
        // /api/admin/normas, /api/admin/templates, /api/admin/ocr/pacotes e
        // /api/admin/projeto/listar-configuracoes-ldp não existem no N&A (módulos congelados por
        // decisão do usuário). Os estados ficam em [] (valor inicial do useState), o que já renderiza
        // a UI transplantada normalmente em estado vazio/DADOS_AINDA_NAO_INTEGRADOS — nenhum mock,
        // nenhuma UI removida. Não reativar sem endpoint real correspondente.
        const registro = await getProjetosRegistrados(true);

        // NormaID/PacoteOCRID removidos deste mapeamento (checkpoint Gestão de Projetos, 2026-09-18) —
        // eram sempre null (ForgeSync original, sem tabela/vínculo no N&A) e não são mais lidos por
        // nenhuma célula desta tabela.
        const projetosFormatados = Array.from(registro.values()).map((p) => ({
            projetoId: p.projetoId,
            nome_Projeto: p.nomeProjeto,
            project_ID_ACC: p.projectIdAcc,
            is_Ativo: p.isAtivo,
            tipoLdp: p.tipoLdp,
            namingStandard: p.namingStandard,
            projectLanguage: p.projectLanguage,
            fileIdLdpAcc: p.fileIdLdpAcc,
        }));
        setProjects(projetosFormatados);

    } catch (error) {
        const msg = error instanceof Error ? error.message : "Erro ao conectar com o servidor.";
        setLoadError(msg);
        toast.error(msg);
    } finally {
        setLoading(false);
    }
  };

  useEffect(() => {
    carregarDadosReais();
  }, []);

  // Projeto atual SEMPRE primeiro (depois da pesquisa); os demais continuam na ordem original e paginados como antes.
  const { projetoAtual } = useProjetoAtual();
  const filteredProjects = useMemo(
    () => priorizarProjetoAtual(projects.filter((p) => p.nome_Projeto.toLowerCase().includes(searchTerm.toLowerCase())),
      projetoAtual?.projectId, (p) => p.project_ID_ACC),
    [projects, searchTerm, projetoAtual?.projectId]
  );
  // Coluna "Template Auditoria" (config.ProjetoConfiguracao.PacoteOcrId).
  const templatesAuditoria = useProjectAuditTemplates();
  const { can } = usePermissions();

  // handleApplyPreset removido (checkpoint Gestão de Projetos, 2026-09-18) — só era usado pelo bloco
  // "Biblioteca de Presets LDP" e pela coluna "Template LDP", ambos removidos desta aba. Tipo_LDP já
  // define o comportamento de LDP necessário nesta camada; configuração detalhada de LDP (presets)
  // fica para uma tela própria e futura. LDPConfigPage/LDPConfigModal/config.PresetLdp não foram
  // tocados.

  // ✅ AGRUPAMENTO VISUAL DOS TEMPLATES (PACOTES)
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

  // ---------------------------------------------------------
  // ⚡ HANDLERS (SISTEMA UNIFICADO DE EXCLUSÃO)
  // ---------------------------------------------------------

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
      } else {
          throw new Error();
      }
    } catch (error) {
      toast.error("FALHA NA EXCLUSÃO CRÍTICA", { id: tid });
    }
  };

  // ---------------------------------------------------------
  // 📂 EXCEL UPLOAD (INTELIGENTE)
  // ---------------------------------------------------------

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
          const deveIgnorar = gerar === "não" || gerar === "nao" || gerar === "no" || gerar === "false" || gerar === "0";
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

  // ---------------------------------------------------------
  // ⚡ HANDLERS: ROBÔ
  // ---------------------------------------------------------

  const handleTriggerRobot = async (projectIdAcc: string) => {
    const tid = toast.loading("🤖 Robô em ação...");
    try {
      const res = await fetch(`/api/admin/projeto/${projectIdAcc}/sincronizar-tudo`, { method: 'POST' });
      if (res.ok) toast.success("✅ Ciclo concluído!", { id: tid });
      else toast.error("❌ Falha no Robô", { id: tid });
    } catch (error) {
      toast.error("Erro crítico de conexão", { id: tid });
    }
  };

  // ---------------------------------------------------------
  // ⚡ HANDLERS: PROJETOS & NORMAS
  // ---------------------------------------------------------

const handleToggle = useCallback(async (id: number) => {
    const project = projects.find(p => p.projetoId === id);
    if (!project) return;

    const novaAtivacao = !project.is_Ativo;

    setProjects(prev => prev.map(p => p.projetoId === id ? { ...p, is_Ativo: novaAtivacao } : p));

    try {
        // Payload restrito a Is_Ativo (checkpoint Gestão de Projetos, 2026-09-18) — NormaID/
        // PacoteOCRID removidos: não existem no DTO do controller, não persistiam nada, e o PATCH
        // parcial não deve enviar campos de conceitos que esta tela não controla.
        const res = await fetch(`/api/admin/projeto/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ is_Ativo: novaAtivacao })
        });

        if (!res.ok) throw new Error();
        toast.success(novaAtivacao ? "PROJETO ATIVADO" : "PROJETO DESATIVADO");
    } catch {
        setProjects(prev => prev.map(p => p.projetoId === id ? { ...p, is_Ativo: !novaAtivacao } : p));
        toast.error("Erro ao alterar status no servidor.");
    }
}, [projects]);

// handleNormaChange removido (checkpoint Gestão de Projetos, 2026-09-18) — escrevia em NormaID
// (conceito config.Norma/ForgeSync, distinto de Naming_Standard) através do mesmo PATCH genérico;
// coluna "Norma Aplicada" duplicada foi removida da tabela junto com este handler. Naming_Standard
// continua sendo controlado por handleNamingStandardChange, agora com o rótulo visual "Norma Aplicada".

// ✅ Naming_Standard: já existe em dbo.Projetos e já é consumido pelo Worker (LdpUpdateService/
// WorkerStageRouting/AccScheduleSyncService). NÃO é o CRUD universal de Normas do ForgeSync (esse
// continua congelado — ver Select "Norma Aplicada" acima). Valores reais: "NA" ou "CLIENT".
const handleNamingStandardChange = useCallback(async (projetoId: number, val: string) => {
    setProjects(prev => prev.map(p => p.projetoId === projetoId ? { ...p, namingStandard: val } : p));

    try {
        const res = await fetch(`/api/admin/projeto/${projetoId}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ namingStandard: val })
        });
        if (!res.ok) throw new Error();
        toast.success("Padrão de nomenclatura salvo!");
    } catch {
        toast.error("Erro ao salvar padrão de nomenclatura.");
        carregarDadosReais();
    }
}, []);

// ✅ Tipo_LDP: já existe em dbo.Projetos e controla o roteamento Tipo1/Tipo2 do Worker (não alterado
// por esta tela — só persiste o valor; a leitura/uso continua exatamente onde já era feita).
const handleTipoLdpChange = useCallback(async (projetoId: number, val: string) => {
    const tipoLdpNum = Number(val);
    setProjects(prev => prev.map(p => p.projetoId === projetoId ? { ...p, tipoLdp: tipoLdpNum } : p));

    try {
        const res = await fetch(`/api/admin/projeto/${projetoId}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tipoLdp: tipoLdpNum })
        });
        if (!res.ok) throw new Error();
        toast.success("Tipo de LDP salvo!");
    } catch {
        toast.error("Erro ao salvar Tipo de LDP.");
        carregarDadosReais();
    }
}, []);

// ✅ Project_Language: já existe em dbo.Projetos. Valores auditados em produção (17/09/2026): só "EN"
// e "PT" — nenhuma outra opção inventada.
const handleProjectLanguageChange = useCallback(async (projetoId: number, val: string) => {
    setProjects(prev => prev.map(p => p.projetoId === projetoId ? { ...p, projectLanguage: val } : p));

    try {
        const res = await fetch(`/api/admin/projeto/${projetoId}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectLanguage: val })
        });
        if (!res.ok) throw new Error();
        toast.success("Idioma do projeto salvo!");
    } catch {
        toast.error("Erro ao salvar idioma do projeto.");
        carregarDadosReais();
    }
}, []);

// handleUpdatePacoteOCR removido (checkpoint Gestão de Projetos, 2026-09-18) — chamava PATCH
// /api/admin/projeto/{id} com PacoteOCRID, propriedade que nunca existiu no DTO do controller; a
// "gravação" nunca persistia nada e o toast de sucesso era falso. Célula da tabela agora mostra
// "Vínculo pendente" sem chamar rede. Ver relatório desta tarefa para o ALTER TABLE necessário
// (proposto, não executado) se o vínculo Projeto→Pacote OCR for funcionalizado no futuro.

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

  // ---------------------------------------------------------
  // ⚡ HANDLERS: TEMPLATES
  // ---------------------------------------------------------

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
    const currentTemplates = templates.filter(t => (t.NormaID || t.NormaID) === normaId);
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
    <div className="flex flex-col gap-8 animate-enter">
      {!somenteProjetos && <div className="space-y-2">
        <div className="flex items-center gap-3 text-primary mb-1">
          <Settings2 className="w-5 h-5" />
          <span className="text-[10px] font-black uppercase tracking-[0.3em]">Administração</span>
        </div>
        <h1 className="text-3xl font-black text-foreground tracking-tighter uppercase italic">Painel de Controle</h1>
      </div>}

      <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
        {!somenteProjetos && <TabsList className="bg-card/30 border border-border/50 backdrop-blur-sm p-1 h-auto gap-1">
          <TabsTrigger value="projetos" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Gestão de Projetos</TabsTrigger>
          <TabsTrigger value="normas" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração de Normas</TabsTrigger>
          <TabsTrigger value="templates" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração de Templates</TabsTrigger>
          <TabsTrigger value="plant3d" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-6 py-2.5">Configuração Plant 3D</TabsTrigger>
        </TabsList>}

        <TabsContent value="projetos" className="mt-6">

          {/* Estado de ERRO real (checkpoint 17) — distinto de "0 projetos". Nunca mostrar tabela
              vazia disfarçando uma falha de carregamento. */}
          {loadError && (
            <div className="mb-6 p-4 bg-destructive/10 border border-destructive/30 rounded-xl flex items-center gap-3">
              <AlertTriangle className="w-5 h-5 text-destructive shrink-0" />
              <div className="flex-1">
                <p className="text-xs font-black uppercase tracking-widest text-destructive">Falha ao carregar projetos</p>
                <p className="text-[11px] text-muted-foreground">{loadError}</p>
              </div>
              <Button variant="outline" size="sm" onClick={carregarDadosReais} className="font-black text-[10px] uppercase border-destructive/30 text-destructive hover:bg-destructive/10">
                Tentar novamente
              </Button>
            </div>
          )}

          {/* Bloco "Biblioteca de Presets LDP" removido desta aba (checkpoint Gestão de Projetos,
              2026-09-18) — dependia exclusivamente do conceito Template/Preset LDP, que passou a ser
              tratado em uma tela própria e futura de Configuração de LDP. Não removido: LDPConfigPage,
              LDPConfigModal, config.PresetLdp (tabela e migration) — só a UI redundante nesta aba. */}

          <div className="mb-6 flex justify-between items-center">
            <div className="relative max-w-sm w-full">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <Input placeholder="Pesquisar projeto..." className="pl-10 bg-card/30 border-border/50" value={searchTerm} onChange={(e) => { setSearchTerm(e.target.value); setPaginaProjetos(1); }} />
            </div>
            
            <div className="flex items-center gap-4">
              {!somenteProjetos && <Button 
                onClick={() => navigate('/ocr')}
                className="bg-primary hover:bg-primary/90 text-white font-black text-[10px] uppercase italic tracking-widest shadow-lg shadow-primary/20"
              >
                <Eye className="w-4 h-4 mr-2" />
                Configurar Novo Carimbo
              </Button>}
              {loading && <span className="text-[10px] font-black animate-pulse uppercase">Sincronizando...</span>}
            </div>
          </div>

          <div className="w-full overflow-hidden rounded-xl border border-border/50 glass-card shadow-2xl">
            <table className="w-full text-left border-collapse table-fixed">
              <thead>
                <tr className="bg-muted/40 text-[10px] uppercase tracking-[0.2em] text-muted-foreground font-black border-b border-border/50">
                  <th className="px-6 py-5 w-[22%]">Projeto</th>
                  <th className="px-4 py-5 text-center w-[9%]">Status</th>
                  <th className="px-4 py-5 w-[9%]">Tipo LDP</th>
                  <th className="px-4 py-5 w-[8%]">Idioma</th>
                  {/* "Nomenclatura" e "Norma Aplicada" eram dois selects escrevendo no mesmo conceito
                      (dbo.Projetos.Naming_Standard) — duplicidade removida (checkpoint Gestão de
                      Projetos, 2026-09-18). Mantido UM único controle, com o rótulo "Norma Aplicada".
                      "Template LDP" removido desta aba — Tipo_LDP já define o comportamento de LDP
                      necessário nesta camada; configuração detalhada de LDP fica para tela própria. */}
                  <th className="px-4 py-5 w-[15%]">Norma Aplicada</th>
                  <th className="px-4 py-5 w-[13%]" title={templatesAuditoria.erro ?? "Template usado pela Auditoria de Qualidade de Documentos"}>Template Auditoria</th>
                  <th className="px-4 py-5 w-[9%] text-left text-[10px] font-black uppercase tracking-widest text-muted-foreground">ECOSSISTEMA</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/30 bg-card/10">
              
              <TabsContent value="plant3d" className="mt-6">
              {/* Bug próprio do ForgeSync corrigido: passava o texto da busca (searchTerm) como se fosse
                  o ID do projeto. Não existe seleção de projeto real para esta aba nem backend
                  (api/plant/*) no N&A — ver nota BACKEND_PENDENTE no topo do arquivo. */}
              <PlantMappingPanel projectId={undefined} />
              </TabsContent>
{filteredProjects.slice((paginaProjetos - 1) * PROJETOS_POR_PAGINA, paginaProjetos * PROJETOS_POR_PAGINA).map((project: any) => {
    return (
      <tr key={project.projetoId} className="group hover:bg-primary/5 transition-all duration-300">
        <td className="px-6 py-4">
          <div className="flex flex-col gap-0.5">
            <span className="text-sm font-bold text-foreground">{project.nome_Projeto}</span>
            <span className="text-[10px] text-muted-foreground font-medium uppercase">{project.project_ID_ACC?.slice(0, 12)}...</span>
          </div>
        </td>

        <td className="px-4 py-4 text-center">
          <div className="flex items-center justify-center gap-2">
            <Switch checked={project.is_Ativo} onCheckedChange={() => handleToggle(project.projetoId)} />
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
            value={String(project.tipoLdp || 1)}
            onValueChange={(val) => handleTipoLdpChange(project.projetoId, val)}
          >
            <SelectTrigger className="bg-card/30 border-border/50 text-[10px] font-black uppercase h-9">
              <SelectValue placeholder="Tipo LDP" />
            </SelectTrigger>
            <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
              <SelectItem value="1" className="text-[10px] font-black uppercase">Tipo 1</SelectItem>
              <SelectItem value="2" className="text-[10px] font-black uppercase">Tipo 2</SelectItem>
            </SelectContent>
          </Select>
        </td>

        <td className="px-4 py-4">
          <Select
            value={project.projectLanguage || "EN"}
            onValueChange={(val) => handleProjectLanguageChange(project.projetoId, val)}
          >
            <SelectTrigger className="bg-card/30 border-border/50 text-[10px] font-black uppercase h-9">
              <SelectValue placeholder="Idioma" />
            </SelectTrigger>
            <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
              <SelectItem value="EN" className="text-[10px] font-black uppercase">EN</SelectItem>
              <SelectItem value="PT" className="text-[10px] font-black uppercase">PT</SelectItem>
            </SelectContent>
          </Select>
        </td>

        {/* Único controle de dbo.Projetos.Naming_Standard nesta tela — rótulo "Norma Aplicada"
            (checkpoint Gestão de Projetos, 2026-09-18). O select antigo de mesmo nome, que escrevia em
            NormaID/config.Norma (conceito diferente, ForgeSync), foi removido desta aba junto com
            handleNormaChange — "Norma Aplicada" aqui é e continua sendo Naming_Standard. */}
        <td className="px-4 py-4">
          <Select
            value={project.namingStandard || "NA"}
            onValueChange={(val) => handleNamingStandardChange(project.projetoId, val)}
          >
            <SelectTrigger className="bg-card/30 border-border/50 text-[10px] font-black uppercase h-9">
              <SelectValue placeholder="Norma Aplicada" />
            </SelectTrigger>
            <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
              <SelectItem value="NA" className="text-[10px] font-black uppercase">N&A</SelectItem>
              <SelectItem value="CLIENT" className="text-[10px] font-black uppercase">Cliente</SelectItem>
            </SelectContent>
          </Select>
        </td>

        {/* Template de auditoria do projeto (antes "Vínculo pendente"): config.ProjetoConfiguracao.PacoteOcrId. */}
        <td className="px-4 py-4">
          <ProjectAuditTemplateSelect projectIdAcc={project.project_ID_ACC} pacotes={templatesAuditoria.pacotes}
            valor={project.project_ID_ACC ? templatesAuditoria.porProjeto[project.project_ID_ACC] : undefined}
            podeEditar={can("settings.projects.edit")} onChange={(id) => void templatesAuditoria.alterar(project.project_ID_ACC, id)} />
        </td>

        <td className="px-4 py-4">
          <div className="flex items-center gap-3">
            {/* Ícone LDP fantasma removido (checkpoint Gestão de Projetos, 2026-09-18) — usava
                project.ldpFolderId, campo que nenhum mapper real do frontend jamais popula (sempre
                undefined). Era um SEGUNDO indicador "LDP" duplicando, de forma sempre negativa/quebrada
                na aparência, o indicador real abaixo (File_ID_LDP_ACC). "O indicador LDP pode usar
                dbo.Projetos.File_ID_LDP_ACC" — um único indicador, não dois. */}

            {/* Ícone Cronograma — sem fonte real de dados ainda (dbo.Projetos não tem coluna de vínculo
                de pasta de Cronograma); mantido como indicador explicitamente pendente/read-only, sem
                inventar vínculo, conforme decisão desta tarefa. */}
            <div
              title="Cronograma: sem fonte real de vínculo em dbo.Projetos ainda — indicador pendente (somente leitura)"
              className="p-2 rounded-lg border transition-all bg-slate-800/50 border-border/60 text-slate-600 opacity-40"
            >
              <Clock size={16} />
            </div>

            {/* Ícone File_ID_LDP_ACC (campo real de dbo.Projetos — somente leitura: não existe hoje
                contrato seguro de edição manual, ver AUDITORIA_FORGESYNC_NA/15-*.md/16-*.md).
                Auditado 17/09/2026: hoje está NULL para todos os projetos em produção. */}
            <div
              title={project.fileIdLdpAcc ? `LDP vinculada: ${project.fileIdLdpAcc}` : "File_ID_LDP_ACC ainda não vinculado (somente leitura)"}
              className={cn(
                "p-2 rounded-lg border transition-all",
                project.fileIdLdpAcc
                  ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-500 shadow-[0_0_10px_rgba(16,185,129,0.1)]"
                  : "bg-slate-800/50 border-border/60 text-slate-600 opacity-40"
              )}
            >
              {project.fileIdLdpAcc ? <CheckCircle2 size={16} /> : <XCircle size={16} />}
            </div>
          </div>
        </td>
      </tr>
    );
})}
              </tbody>
            </table>
          </div>
          {filteredProjects.length > PROJETOS_POR_PAGINA && (
            <div className="mt-4 flex items-center justify-between text-[10px] font-black uppercase tracking-widest text-muted-foreground">
              <span>Página {paginaProjetos} de {Math.ceil(filteredProjects.length / PROJETOS_POR_PAGINA)} • {filteredProjects.length} projetos</span>
              <span className="flex gap-2">
                <Button variant="outline" size="sm" className="h-8" disabled={paginaProjetos <= 1} onClick={() => setPaginaProjetos(p => p - 1)}>Anterior</Button>
                <Button variant="outline" size="sm" className="h-8" disabled={paginaProjetos >= Math.ceil(filteredProjects.length / PROJETOS_POR_PAGINA)} onClick={() => setPaginaProjetos(p => p + 1)}>Próxima</Button>
              </span>
            </div>
          )}
          {!somenteProjetos && <div className="mt-8 max-w-2xl">
             <h3 className="text-[10px] font-black uppercase tracking-widest text-muted-foreground mb-4 italic">Manutenção de Dados — Auditoria de Qualidade de Documentos</h3>
             <OCRCleanupTool pacotes={pacotesOCR} onRefresh={carregarDadosReais} />
          </div>}
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
                    <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); triggerDeleteDialog(norma.normaId, 'norma'); }} className="absolute top-2 right-2 h-8 w-8 p-0 opacity-0 group-hover:opacity-100 border border-border/60 bg-foreground/5 hover:bg-destructive/20 hover:text-destructive transition-all rounded-lg backdrop-blur-md"><Trash2 className="w-3.5 h-3.5" /></Button>
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
                            <div key={tpl.templateId || tpl.TemplateID || `tpl-${tpl.sigla_Doc}`} className="group glass-card ... relative p-4 rounded-xl border border-border/50">
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
      </Tabs>
            
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
            <Button variant="ghost" onClick={() => setIsConfirmDeleteOpen(false)} className="font-black text-[10px] uppercase tracking-widest hover:bg-foreground/5 flex-1 font-sans">Cancelar</Button>
            <Button onClick={handleConfirmDelete} className="bg-red-600 hover:bg-red-700 text-white font-black text-[10px] uppercase tracking-widest italic transition-all duration-300 active:scale-95 flex-[1.5] font-sans">Confirmar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      {/* ✅ MODAL PARA NOVO PACOTE OCR */}
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
  );
}