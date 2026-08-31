import { useState, useEffect, useCallback, useRef } from "react";
import { 
  Settings, 
  CheckCircle2, 
  AlertCircle, 
  Loader2, 
  ListChecks, 
  TriangleAlert, 
  Bot,
  PlayCircle,
  FolderKanban,
  ScrollText,
  RefreshCw,
  Clock,
  ChevronRight,
  AlertTriangle,
  X,
  Check
} from "lucide-react";
import toast from 'react-hot-toast';
import { ScrollArea } from "@/components/ui/scroll-area"
import { AppSidebar } from "@/components/app-sidebar";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { ModeToggle } from "@/components/alternar-modo";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { VisualizadorLogResumido } from "@/components/VisualizadorLogResumido";
import { ProgressoRobo } from '@/components/ProgressoRobo';

// ================================================================
// 📦 TIPOS
// ================================================================
interface ProjectAdminDTO {
  projetoId: number;
  nome_Projeto: string;
  project_ID_ACC: string;
  is_Ativo: boolean;
  normaID: number | null;
  pacoteOCRID: number | null;
  configLDPID: number | null;
  ldpFolderId: string | null;
  cronFolderId: string | null;
}

interface LogStep {
  tipo: "sucesso" | "erro" | "aviso" | "info";
  texto: string;
}

function parseLogResumido(conteudo: string): LogStep[] {
  const linhas = conteudo.split("\n");
  const steps: LogStep[] = [];

  for (const linhaBruta of linhas) {
    const linha = linhaBruta.trim();
    if (!linha) continue;

    if (linha.includes("❌")) {
      steps.push({ tipo: "erro", texto: linha.replace(/❌/g, "").trim() });
    } else if (linha.includes("⚠️")) {
      steps.push({ tipo: "aviso", texto: linha.replace(/⚠️/g, "").trim() });
    } else if (linha.includes("✅")) {
      steps.push({ tipo: "sucesso", texto: linha.replace(/✅/g, "").trim() });
    } else if (linha.includes("🏗️") || linha.includes("🏁")) {
      steps.push({ tipo: "info", texto: linha.replace(/[🏗️🏁]/g, "").trim() });
    }
  }

  return steps;
}

interface LogFile {
  nomeArquivo: string;
  dataExecucao: string;
  tamanhoKb: number;
}

export default function AndamentoRobo() {
  const [activeTab, setActiveTab] = useState("projetos");

  // ---- Projetos ----
  const [projetos, setProjetos] = useState<ProjectAdminDTO[]>([]);
  const [carregandoProjetos, setCarregandoProjetos] = useState(true);
  const [erroProjetos, setErroProjetos] = useState<string | null>(null);
  const [projetoAtualizando, setProjetoAtualizando] = useState<number | null>(null);

  // ---- Status/Execução do robô ----
  const [executando, setExecutando] = useState(false);
  const [disparando, setDisparando] = useState(false);
  const [porcentagem, setPorcentagem] = useState(0);
  const [etapaAtual, setEtapaAtual] = useState("Aguardando Início");
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ---- Logs ----
  const [logs, setLogs] = useState<LogFile[]>([]);
  const [carregandoLogs, setCarregandoLogs] = useState(true);
  const [logSelecionado, setLogSelecionado] = useState<string | null>(null);
  const [conteudoLog, setConteudoLog] = useState<string>("");
  const [carregandoConteudoLog, setCarregandoConteudoLog] = useState(false);

  // ------ Paginação ---- 
  const ITEMS_PER_PAGE = 10;
  const [currentPage, setCurrentPage] = useState(1);


  

  // Referência para Auto-Scroll do Terminal de Logs
  const terminalRef = useRef<HTMLPreElement>(null);

  // ================================================================
  // 🔍 FETCH: Projetos
  // ================================================================
  const carregarProjetos = useCallback(async () => {
    setCarregandoProjetos(true);
    setErroProjetos(null);
    try {
      const response = await fetch('/api/admin/projetos');
      if (!response.ok) {
        const texto = await response.text();
        throw new Error(texto || `Erro ${response.status}`);
      }
      const data = await response.json();
      setProjetos(data);
    } catch (error: any) {
      console.error("Erro ao carregar projetos:", error);
      setErroProjetos(error.message || "Falha ao carregar projetos.");
    } finally {
      setCarregandoProjetos(false);
    }
  }, []);

  // ================================================================
  // 🔁 TOGGLE: Ativar/Desativar projeto
  // ================================================================
  const handleToggle = useCallback(async (project: any) => {
    let idDoBancoLocal = project.projetoId;
    const novoStatus = !project.is_Ativo;
    
    const accId = project.project_ID_ACC || project.ProjetoID;

    try {
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

        setProjetos((prevProjetos: any[]) =>
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

  // ================================================================
  // 🤖 STATUS DO ROBÔ
  // ================================================================
  const verificarStatusRobo = useCallback(async () => {
    try {
      const response = await fetch('/DataManagement/StatusRobo');
      if (!response.ok) return;
      const data = await response.json();
      setExecutando(data.executando);
      setPorcentagem(data.progresso ?? 0);
      setEtapaAtual(data.etapaAtual || "Aguardando Início");
    } catch (error) {
      console.error("Erro ao verificar status do robô:", error);
    }
  }, []);

  // ================================================================
  // 📄 LOGS
  // ================================================================
  const carregarLogs = useCallback(async (forcarMaisRecente = false) => {
    setCarregandoLogs(true);
    try {
      const response = await fetch('/DataManagement/Logs');

      if (!response.ok) {
        const errorText = await response.text();
        console.error("Detalhes do erro no Backend:", errorText);
        throw new Error(`Erro ${response.status}: ${response.statusText}`);
      }

      const data = await response.json();
      const listaLogs: LogFile[] = data.logs || [];
      setLogs(listaLogs);

      if (listaLogs.length > 0 && (forcarMaisRecente || !logSelecionado)) {
        setLogSelecionado(listaLogs[0].nomeArquivo);
      }
    } catch (error: any) {
      console.error("Erro na requisição dos logs:", error);
      
    } finally {
      setCarregandoLogs(false);
    }
  }, [logSelecionado]);

  const abrirLog = async (nomeArquivo: string, emBackground = false) => {
    if (!emBackground) setCarregandoConteudoLog(true);
    
    try {
      const response = await fetch(`/DataManagement/Logs/${encodeURIComponent(nomeArquivo)}`);
      if (!response.ok) throw new Error(`Erro ${response.status}`);
      const data = await response.json();
      setConteudoLog(data.conteudo || "");
    } catch (error: any) {
      if (!emBackground) {
        
        setConteudoLog("Carregando a lista dos Logs.");
      }
    } finally {
      if (!emBackground) setCarregandoConteudoLog(false);
    }
  };

  // ================================================================
  // 🚀 INICIAR ROBÔ
  // ================================================================
  const iniciarRobo = async () => {
    setDisparando(true);
    try {
      setLogSelecionado(null);
      setConteudoLog("🚀 Disparando lote... Aguardando criação do arquivo de log no servidor.");

      const response = await fetch('/DataManagement/IniciarRobo', { method: 'POST' });

      if (response.status === 409) {
        toast.error("O robô já está em execução no momento.");
        setExecutando(true);
        return;
      }

      if (!response.ok) {
        const texto = await response.text();
        throw new Error(texto || `Erro ${response.status}`);
      }

      toast.success("Robô iniciado em background.");
      setExecutando(true);

      setTimeout(() => {
        carregarLogs(true);
      }, 2000);

    } catch (error: any) {
      toast.error(`Falha ao iniciar robô: ${error.message}`);
    } finally {
      setDisparando(false);
    }
  };

  // ================================================================
  // ⏰ EFFECTS
  // ================================================================
  useEffect(() => {
    carregarProjetos();
    verificarStatusRobo();
    carregarLogs();
  }, [carregarProjetos, verificarStatusRobo, carregarLogs]);

  useEffect(() => {
    if (executando) {
      pollingRef.current = setInterval(async () => {
        try {
          const response = await fetch('/DataManagement/StatusRobo');
          if (!response.ok) return;
          const data = await response.json();
          setExecutando(data.executando);
          setPorcentagem(data.progresso ?? 0);
          setEtapaAtual(data.etapaAtual || "");

          carregarLogs(false);
          if (logSelecionado) {
            abrirLog(logSelecionado, true);
          }

          if (!data.executando) {
            toast.success("Ciclo do robô finalizado!");
            carregarLogs(true);
          }
        } catch (error) {
          console.error("Erro no polling de status:", error);
        }
      }, 2000);
    }

    return () => {
      if (pollingRef.current) clearInterval(pollingRef.current);
    };
  }, [executando, logSelecionado, carregarLogs]);

  useEffect(() => {
    if (logSelecionado) {
      abrirLog(logSelecionado);
    }
  }, [logSelecionado]);

  useEffect(() => {
    if (terminalRef.current && executando) {
      terminalRef.current.scrollTop = terminalRef.current.scrollHeight;
    }
  }, [conteudoLog, executando]);

  const projetosAtivos = projetos.filter(p => p.is_Ativo).length;

  const totalPage = Math.max(1, Math.ceil(projetos.length / ITEMS_PER_PAGE));
  const projetosPaginados = projetos.slice(
    (currentPage - 1) * ITEMS_PER_PAGE,
    currentPage * ITEMS_PER_PAGE
  );

  useEffect(() => {
    setCurrentPage(1);
  },[projetos.length]
  )

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col min-h-screen lg:h-screen lg:overflow-hidden bg-background">

          {/* TOPO FIXO */}
          <div className="flex items-center gap-4 p-4 shrink-0">
            <SidebarTrigger className="-ml-6" />
            <ModeToggle />
            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary"></a>
          </div>
          
          {/* CONTEÚDO PRINCIPAL */}
          <div className="flex-1 min-h-0 flex flex-col gap-6 px-4 sm:px-6 lg:px-9 pb-9 overflow-hidden">

            {/* CABEÇALHO */}
            <div className="shrink-0 flex flex-col sm:flex-row justify-between items-start md:items-center gap-4 border-b border-slate-700 pb-4">
              <div className="flex items-center gap-3">
                <div className="p-2.5 bg-indigo-500/10 border border-indigo-500/30 text-indigo-400 rounded-lg shadow-[0_0_15px_rgba(99,102,241,0.15)]">
                  <Bot className="w-5 h-5" />
                </div>
                <div>
                  <h1 className="text-xl font-black text-foreground uppercase tracking-tight">Andamento do Robô</h1>
                  <p className="text-xs text-muted-foreground font-medium">Acompanhe o andamento do robô.</p>
                </div>
              </div>

              {/* STATUS + BOTÃO DISPARAR */}
              <div className="flex items-center gap-3 flex-wrap w-full md:w-auto">
                <div className={`flex items-center gap-2 px-3 py-1.5 rounded-lg border text-[10px] font-black uppercase tracking-widest ${
                  executando 
                    ? "bg-amber-500/10 border-amber-500/30 text-amber-400" 
                    : "bg-emerald-500/10 border-emerald-500/30 text-emerald-400"
                }`}>
                  {executando ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <CheckCircle2 className="w-3.5 h-3.5" />
                  )}
                  {executando ? "Executando" : "Ocioso"}
                </div>

                <button
                  onClick={iniciarRobo}
                  disabled={executando || disparando}
                  className="  flex items-center gap-2 px-4 py-2 bg-indigo-500 hover:bg-indigo-600 disabled:bg-slate-700 disabled:cursor-not-allowed disabled:text-muted-foreground text-white text-[10px] font-black uppercase tracking-widest rounded-lg transition-colors"
                >
                  {disparando ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <PlayCircle className="w-3.5 h-3.5" />
                  )}
                  {disparando ? "Iniciando..." : "Executar Agora"}
                </button>
              </div>
            </div>

            {/* TABS */}
            <div className="w-full flex-1 min-h-0 flex flex-col">
              <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full flex-1 min-h-0 flex flex-col gap-4">
                <TabsList className="bg-card/85 border border-border/50 backdrop-blur-sm p-1 h-auto gap-1 shrink-0 overflow-x-auto max-w-full">
                  <TabsTrigger value="projetos" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-4 sm:px-6 py-2.5 flex items-center gap-2 whitespace-nowrap">
                    <FolderKanban className="w-3.5 h-3.5" />
                    Projetos
                  </TabsTrigger>
                  <TabsTrigger value="logs" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-primary px-4 sm:px-6 py-2.5 flex items-center gap-2 whitespace-nowrap">
                    <ScrollText className="w-3.5 h-3.5" />
                    Logs de Execução
                  </TabsTrigger>
                  
                </TabsList>
                
                {/* ======================= ABA: PROJETOS ======================= */}
                <TabsContent value="projetos" className="flex-1 min-h-0 m-0 overflow-hidden">
                  <div className="h-full w-full border border-border/50 bg-card flex flex-col overflow-hidden rounded-md flex-wrap ">

                    <div className="shrink-0 flex items-center justify-between px-5 py-4 border-b border-border/50">
                      <div className="flex items-center gap-1">
                        <ListChecks className="w-4 h-4 text-indigo-400" />
                        <span className="text-xs font-black uppercase tracking-widest text-foreground">
                          Projetos Cadastrados
                        </span>
                        {!carregandoProjetos && (
                          <span className="text-[10px] font-bold text-muted-foreground">
                            ({projetosAtivos} ativo{projetosAtivos !== 1 ? "s" : ""} de {projetos.length})
                          </span>
                        )}
                      </div>
                      <button
                        onClick={carregarProjetos}
                        disabled={carregandoProjetos}
                        className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-widest text-muted-foreground hover:text-indigo-400 transition-colors disabled:opacity-50"
                      >
                        <RefreshCw className={`w-3 h-3 ${carregandoProjetos ? "animate-spin" : ""}`} />
                        Atualizar
                      </button>
                    </div>

                    <div className="flex-1 min-h-0 overflow-hidden">
                      {carregandoProjetos ? (
                        <div className="flex flex-col items-center justify-center h-full gap-3 text-muted-foreground py-16">
                          <Loader2 className="w-6 h-6 animate-spin" />
                          <span className="text-xs font-medium">Carregando projetos...</span>
                        </div>
                      ) : erroProjetos ? (
                        <div className="flex flex-col items-center justify-center h-full gap-3 text-red-400 py-16">
                          <TriangleAlert className="w-6 h-6" />
                          <span className="text-xs font-medium">{erroProjetos}</span>
                        </div>
                      ) : projetos.length === 0 ? (
                        <div className="flex flex-col items-center justify-center h-full gap-3 text-muted-foreground py-16">
                          <FolderKanban className="w-6 h-6" />
                          <span className="text-xs font-medium">Nenhum projeto cadastrado.</span>
                        </div>
                      ) : (
                        <div className="divide-y divide-border/50">
                          {projetosPaginados.map((projeto) => (
                            <div 
                              key={projeto.projetoId} 
                              className="flex items-center justify-between gap-3 px-4 sm:px-5 py-3 hover:bg-card/50 transition-colors"
                            >
                              <div className="flex items-center gap-3 min-w-0">
                                <div className={`w-2 h-2 rounded-full shrink-0 ${
                                  projeto.is_Ativo ? "bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.6)]" : "bg-slate-600"
                                }`} />
                                <div className="min-w-0">
                                  <p className="text-xs sm:text-sm font-semibold text-foreground truncate">
                                    {projeto.nome_Projeto}
                                  </p>
                                  <p className="text-[10px] text-muted-foreground font-mono truncate">
                                    {projeto.project_ID_ACC}
                                  </p>
                                </div>
                              </div>

                              <button
                                onClick={() => handleToggle(projeto)}
                                disabled={projetoAtualizando === projeto.projetoId}
                                className={`relative shrink-0 w-12 h-6 rounded-full transition-colors disabled:opacity-50 ${
                                  projeto.is_Ativo ? "bg-indigo-500" : "bg-slate-700"
                                }`}
                              >
                                {projetoAtualizando === projeto.projetoId ? (
                                  <Loader2 className="w-3.5 h-3.5 animate-spin text-black absolute top-1.25 left-1/2 -translate-x-1/2" />
                                ) : (
                                  <span className={`flex items-center justify-center absolute top-1 left-1 w-4 h-4 rounded-full bg-white transition-transform ${
                                    projeto.is_Ativo ? "translate-x-6" : "translate-x-0"
                                  }`} />
                                )}
                              </button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                                      {!carregandoProjetos && !erroProjetos && projetos.length > ITEMS_PER_PAGE && (
  <div className="shrink-0 flex items-center justify-between px-5 py-3 border-t border-border/50">
    <span className="text-[10px] font-bold uppercase text-muted-foreground">
      Página {currentPage} de {totalPage}
    </span>
    <div className="flex items-center gap-2">
      <button
        onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
        disabled={currentPage === 1}
        className="px-3 py-1.5 text-[10px] font-black uppercase tracking-widest rounded-lg border border-border/50 text-muted-foreground hover:text-indigo-400 hover:border-indigo-500/30 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
      >
        Anterior
      </button>
      <button
        onClick={() => setCurrentPage((p) => Math.min(totalPage, p + 1))}
        disabled={currentPage === totalPage}
        className="px-3 py-1.5 text-[10px] font-black uppercase tracking-widest rounded-lg border border-border/50 text-muted-foreground hover:text-indigo-400 hover:border-indigo-500/30 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
      >
        Próxima
      </button>
    </div>
  </div>
)}
                  </div>

                  
                </TabsContent>
                

                {/* ======================= ABA: LOGS ======================= */}
                <TabsContent value="logs" className="flex-1 min-h-0 m-0 overflow-hidden">
                  
                  <div className="h-full w-full border border-border/50 bg-card/30 flex flex-col lg:flex-row overflow-hidden rounded-lg">

                    {/* Lista de logs (Sidebar esquerda) */}
                    <div className="w-full lg:w-72 border-b lg:border-b-0 lg:border-r border-border/50 flex flex-col shrink-0 min-h-0 h-48 sm:h-60 lg:h-full">
                      <div className="shrink-0 flex items-center justify-between px-5 py-4 border-b border-border/50">
                        <div className="flex items-center gap-2">
                          <ScrollText className="w-4 h-4 text-indigo-400" />
                          <span className="text-xs font-black uppercase tracking-widest text-foreground">
                            Execuções
                          </span>
                        </div>
                        <button
                          onClick={() => carregarLogs(false)}
                          disabled={carregandoLogs}
                          className="text-muted-foreground hover:text-indigo-400 transition-colors disabled:opacity-50"
                        >
                          <RefreshCw className={`w-3.5 h-3.5 ${carregandoLogs ? "animate-spin" : ""}`} />
                        </button>
                      </div>

                    <ScrollArea className="flex-1 min-h-0">
                      <div className="flex-1 min-h-0 overflow-y-auto">
                        {carregandoLogs ? (
                          <div className="flex flex-col items-center justify-center h-full gap-1 text-muted-foreground py-10">
                            <Loader2 className="w-5 h-5 animate-spin" />
                          </div>
                        ) : logs.length === 0 ? (
                          <div className="flex flex-col items-center justify-center h-full gap-2 text-muted-foreground py-10 px-4 text-center">
                            <AlertCircle className="w-5 h-5" />
                            <span className="text-xs font-medium">Nenhuma execução registrada.</span>
                          </div>
                        ) : (
                          <div className="divide-y divide-border/50">
                            {logs.map((log) => (
                              <button
                                key={log.nomeArquivo}
                                onClick={() => setLogSelecionado(log.nomeArquivo)}
                                className={`w-full flex items-center justify-between px-4 py-3 text-left hover:bg-card/50 transition-colors ${
                                  logSelecionado === log.nomeArquivo ? "bg-indigo-500/10 border-l-2 border-indigo-400" : ""
                                }`}
                              >
                                <div className="min-w-0">
                                  <div className="flex items-center gap-1.5 text-[11px] font-semibold text-foreground">
                                    <Clock className="w-3 h-3 text-muted-foreground shrink-0" />
                                    {new Date(log.dataExecucao).toLocaleString('pt-BR')}
                                  </div>
                                  <p className="text-[10px] text-muted-foreground mt-0.5">{log.tamanhoKb} KB</p>
                                </div>
                                <ChevronRight className="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                      </ScrollArea>
                    </div>

                    {/* Conteúdo do log (Painel principal) */}
                    <div className="flex-1 flex flex-col min-h-0 h-2/3 lg:h-full">
                      {!logSelecionado ? (
                        <div className="flex flex-col items-center justify-center h-full gap-2 text-muted-foreground">
                          <ScrollText className="w-8 h-8" />
                          <span className="text-xs font-medium">Selecione uma execução para ver o log.</span>
                        </div>
                      ) : (
                        <>
                          <div className="shrink-0 flex items-center justify-between px-5 py-4 border-b border-border/50">
                            <span className="text-xs font-mono text-foreground truncate">{logSelecionado}</span>
                           
                          </div>
                          <ScrollArea className="flex-1 min-h-0">
                          <div className="flex-1 min-h-0 overflow-y-auto p-5">
                            {carregandoConteudoLog ? (
                              <div className="flex items-center justify-center h-full gap-2 text-muted-foreground">
                                <Loader2 className="w-5 h-5 animate-spin" />
                              </div>
                            ) : (
                              <VisualizadorLogResumido conteudoLog={conteudoLog} />
                            )}
                          </div>
                          </ScrollArea>
                        </>
                      )}
                    </div>
                    
                  </div>
                  

                  
                </TabsContent>

                    

                
              </Tabs>

              
            </div>

          </div>

          <div>
            <ProgressoRobo
              executando={executando}
              porcentagem={porcentagem}
              etapaAtualTexto={etapaAtual}
            />
          </div>
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}