import { useEffect, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { Search, ChevronLeft, ChevronRight, LogOut, LayoutGrid, List } from "lucide-react";
import toast from 'react-hot-toast';

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ProjectEnvironmentCard, ProjectEnvironmentRow } from "@/components/projects/ProjectEnvironmentCard";
import { useUserPreference } from "@/features/preferences/useUserPreference";
import { ehUmDe } from "@/features/preferences/userPreferences";
import { colunasHome, GRADE_HOME, itensPorPaginaHome, MODOS_HOME, type ModoHome } from "./homeLayout";
import { ThemeLogo } from "@/features/theme/ThemeLogo";
import { buscarEfetivas, usePermissions } from "@/features/permissions/PermissionProvider";
import { criarCan, rotaDeEntradaDoProjeto } from "@/features/permissions/permissionLogic";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { RobotRunButton } from "@/components/RobotRunButton";
import { PERMISSAO_EXECUTAR_ROBO } from "@/features/robot/robotExecution";
import { ThemeSelector } from "@/features/theme/ThemeSelector";

interface HomeProject {
  projetoId: number;
  projectIdAcc: string;
  nomeProjeto: string;
  tipoLdp: number | null;
  isAtivo: boolean;
}

// HOME (30/09/2026): tela de ESCOLHA DO PROJETO, sem sidebar (rota fora do Layout). Ao entrar num card o projeto
// vira o PROJETO ATUAL (ProjetoAtualContext) e o usuário vai para a primeira área permitida do ambiente do projeto.
export default function Dashboard() {
  const navigate = useNavigate();
  const { can } = usePermissions();
  const { definirProjeto } = useProjetoAtual();
  const [projects, setProjects] = useState<HomeProject[]>([]);
  const [userName, setUserName] = useState("...");
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);

  // Modo CARDS/LISTA: preferência do usuário (camada central de preferências — lembrada entre sessões).
  const [modo, setModo] = useUserPreference<ModoHome>("home", "viewMode", "cards", { validar: ehUmDe(MODOS_HOME) });
  // Colunas responsivas (6 em telas largas, menos em telas menores) — a página mostra até 5 fileiras de cards.
  const [larguraTela, setLarguraTela] = useState(() => (typeof window === "undefined" ? 1920 : window.innerWidth));
  useEffect(() => {
    const h = () => setLarguraTela(window.innerWidth);
    window.addEventListener("resize", h);
    return () => window.removeEventListener("resize", h);
  }, []);
  const itemsPerPage = itensPorPaginaHome(modo, colunasHome(larguraTela));

  // Paginação
  const [currentPage, setCurrentPage] = useState(1);

  /**
   * DADOS INICIAIS: projetos internos permitidos e nome visual do usuário.
   */
  useEffect(() => {
    const fetchInitialData = async () => {
      try {
        const [pRes, uRes] = await Promise.all([
          fetch('/api/me/projects', { credentials: 'include' }),
          fetch('/Authentication/OAuth/GetUserName', { credentials: 'include' })
        ]);
        if (pRes.status === 401) { navigate('/login'); return; }
        if (!pRes.ok) {
          setProjects([]);
          toast.error(`Falha ao carregar projetos (HTTP ${pRes.status}).`);
        } else {
          const data = await pRes.json() as HomeProject[];
          setProjects(Array.isArray(data) ? data : []);
          setCurrentPage(1);
        }

        if (uRes.ok) {
          const userData = await uRes.json();
          setUserName(userData.userName);
        }
      } catch (error: unknown) {
        console.error('[DASHBOARD] erro ao carregar projetos internos:', error);
        setProjects([]);
        toast.error("Erro na conexão com o servidor N&A.");
      } finally {
        setLoading(false);
      }
    };
    fetchInitialData();
  }, [navigate]);

  const filteredProjects = useMemo(() => {
    return projects
      .filter(p => p.isAtivo)
      .filter(p => p.nomeProjeto.toLowerCase().includes(searchTerm.toLowerCase()));
  }, [projects, searchTerm]);

  const totalPages = Math.ceil(filteredProjects.length / itemsPerPage);

  const paginatedProjects = useMemo(() => {
    const start = (currentPage - 1) * itemsPerPage;
    return filteredProjects.slice(start, start + itemsPerPage);
  }, [filteredProjects, currentPage, itemsPerPage]);

  // Trocar modo/largura muda o tamanho da página: nunca ficar numa página que deixou de existir.
  useEffect(() => { setCurrentPage((p) => Math.min(p, Math.max(1, totalPages))); }, [totalPages]);

  /**
   * INICIAR ROBÔ (só com robot.run): RobotRunButton → POST /api/worker/run com Project_ID_ACC dos projetos ATIVOS que o usuário acessa
   * (/api/me/projects). O backend resolve o hub no cadastro e revalida UserProjects + robot.run. Acompanhamento = monitor global.
   */
  const podeExecutarRobo = can(PERMISSAO_EXECUTAR_ROBO);
  const projetosRobo = useMemo(() => projects.filter(p => p.isAtivo).map(p => ({ id: p.projectIdAcc, name: p.nomeProjeto })), [projects]);

  /**
   * ENTRAR NO PROJETO: define o projeto atual e abre as PASTAS do projeto (Project Files) — o N&A Connect como máscara do CDE.
   * Sem cde.docs.view (ou se a pasta raiz não for localizada), abre a primeira área permitida do ambiente, como antes.
   */
  const entrarNoProjeto = async (project: HomeProject) => {
    // NUNCA escolher a rota usando anyProject: um privilégio em CHS001
    // não concede visualização dos módulos em BML011.013.
    try {
      const effective = await buscarEfetivas(project.projectIdAcc);
      const projectCan = criarCan(effective, "project");
      definirProjeto({ hubId: "", projectId: project.projectIdAcc, nome: project.nomeProjeto });
      const destino = rotaDeEntradaDoProjeto(projectCan);
      if (destino) navigate(destino);
      else {
        toast("Nenhuma funcionalidade liberada neste projeto.");
        navigate("/dashboard", { replace: true });
      }
    } catch {
      // Falha fechada: não acessar telas com autorização indefinida.
      toast.error("Não foi possível verificar seu acesso ao projeto.");
    }
  };

  const propsDoProjeto = (project: HomeProject) => {
    return {
      projectId: project.projectIdAcc,
      nome: project.nomeProjeto,
      plataforma: undefined,
      tipoLdp: project.tipoLdp,
      ativo: project.isAtivo,
      onEntrar: () => void entrarNoProjeto(project),
    };
  };

  return (
    // App-shell: cabeçalho fixo + área central com rolagem própria (a página não cresce indefinidamente).
    <div className="flex h-screen flex-col overflow-hidden bg-background text-foreground">
      <header className="flex flex-shrink-0 items-center justify-between border-b border-border/60 bg-card/40 px-6 py-3 backdrop-blur">
        <div className="flex h-14 items-center"><ThemeLogo variant="default" /></div>
        <div className="flex items-center gap-3 text-xs">
          <ThemeSelector />
          <span className="font-semibold text-muted-foreground">{userName}</span>
          <a href="/Authentication/OAuth/Logout" className="flex items-center gap-1.5 rounded-md border border-border/60 px-2.5 py-1.5 font-bold uppercase tracking-wider text-muted-foreground hover:border-primary/50 hover:text-primary">
            <LogOut className="h-3.5 w-3.5" /> Sair
          </a>
        </div>
      </header>
    <main className="min-h-0 flex-1 overflow-y-auto custom-scrollbar">
    <div className="flex w-full flex-col px-4 py-6 animate-enter md:px-6 2xl:px-8">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 mb-6 shrink-0">
        <div className="space-y-1">
          <h1 className="text-3xl font-black text-foreground tracking-tight italic uppercase">
            Olá, {userName.split(' ')[0]}
          </h1>
          <p className="text-sm text-muted-foreground">Escolha o projeto para entrar no ambiente de trabalho.</p>

        </div>

        <div className="flex flex-col sm:flex-row items-center gap-3">
          {podeExecutarRobo && <RobotRunButton projects={projetosRobo} />}
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
            <Input
              placeholder="Pesquisar projetos..."
              className="pl-10 w-64 bg-card/30 border-border/50 focus:border-primary/50"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
          </div>
          {/* Modo de visualização (preferência salva por usuário). */}
          <div role="group" aria-label="Modo de visualização" className="flex rounded-lg border border-border/60 bg-card/30 p-0.5">
            {([["cards", "Cards", LayoutGrid], ["lista", "Lista", List]] as const).map(([valor, rotulo, Icone]) => (
              <button key={valor} type="button" aria-pressed={modo === valor} onClick={() => setModo(valor)} title={`Ver em ${rotulo.toLowerCase()}`}
                className={cn("flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[10px] font-black uppercase tracking-widest transition-colors",
                  modo === valor ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
                <Icone className="h-3.5 w-3.5" />{rotulo}
              </button>
            ))}
          </div>
        </div>
      </div>

      <div className="flex flex-col">
        {loading ? (
          <div className={GRADE_HOME}>
            {Array.from({ length: 12 }).map((_, i) => (
              <div key={i} className="h-44 rounded-xl border border-border/50 bg-card/20 animate-pulse" />
            ))}
          </div>
        ) : (
          <>
            {/* Cards (até 6 × 5 por página, responsivo) ou Lista compacta. Logo = <ProjectLogo> (fonte única). */}
            {paginatedProjects.length === 0 ? (
              <p className="rounded-xl border border-border/50 px-4 py-10 text-center text-sm text-muted-foreground">Nenhum projeto encontrado.</p>
            ) : modo === "cards" ? (
              <div className={GRADE_HOME}>
                {paginatedProjects.map((project) => (
                  <ProjectEnvironmentCard key={project.projectIdAcc} {...propsDoProjeto(project)} />
                ))}
              </div>
            ) : (
              <div className="overflow-hidden rounded-xl border border-border/50 bg-card/30">
                <div className="flex items-center gap-3 border-b border-border/50 bg-muted/30 px-3 py-2 text-[9px] font-black uppercase tracking-widest text-muted-foreground">
                  <span className="w-14 flex-shrink-0">Logo</span><span className="w-28 flex-shrink-0">Código</span><span className="flex-1">Projeto</span>
                  <span className="hidden w-28 md:block">LDP</span><span className="hidden w-16 lg:block">Situação</span><span className="w-8" />
                </div>
                {paginatedProjects.map((project) => (
                  <ProjectEnvironmentRow key={project.projectIdAcc} {...propsDoProjeto(project)} />
                ))}
              </div>
            )}

            {totalPages > 1 && (
              <div className="mt-6 pt-4 flex items-center justify-between border-t border-border/40 shrink-0">
                <p className="text-[10px] font-black text-muted-foreground uppercase tracking-widest">
                  Página {currentPage} de {totalPages} • {filteredProjects.length} Projetos
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="outline" size="icon" className="h-8 w-8 glass-card border-border/50 disabled:opacity-30"
                    onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
                    disabled={currentPage === 1}
                  >
                    <ChevronLeft className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="outline" size="icon" className="h-8 w-8 glass-card border-border/50 disabled:opacity-30"
                    onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
                    disabled={currentPage === totalPages}
                  >
                    <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </div>

    </div>
    </main>
    </div>
  );
}
