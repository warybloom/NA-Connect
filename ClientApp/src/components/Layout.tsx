import { useState, useEffect, useMemo } from "react";
import { Outlet, NavLink, useLocation, useNavigate, type Location } from "react-router-dom";
import {
  LogOut,
  ChevronRight,
  ChevronDown,
  Settings,
  Bell,
  LucideIcon,
  FileSpreadsheet,
  Box,
  FileText,
  BarChart3,
  LayoutTemplate,
  GitBranch,
  ClipboardList,
  CalendarClock,
  Calculator,
  FolderOpen,
  Factory,
  Layers,
  MonitorPlay,
  Bot,
  ShieldCheck,
  KeyRound,
  TrendingUp,
  FileStack,
  CalendarRange,
  ClipboardCheck,
  FileWarning,
  GitPullRequest,
  PauseCircle,
  Receipt,
  ShieldAlert,
  AlertOctagon,
  Coins,
  UserRound,
  Users,
  FolderKanban,
  CalendarCheck,
  Wallet,
  Gauge,
  BookOpen,
  CalendarDays,
  History,
  ListChecks,
  Timer,
  Database,
  ArrowLeftRight,
  FolderCog,
  Home,
  Palette,
  Scan,
  Scale,
  Languages,
} from "lucide-react";

import { contextualNavigation, type NavNode } from "@/features/sidebar/officialNavigation";
import { applyConfiguration } from "@/features/sidebar/sidebarModel";
import { useOfficialSidebar, useSidebarManagerAccess } from "@/features/sidebar/useOfficialSidebar";
import { cn } from "../lib/utils";
import { ThemeLogo } from "@/features/theme/ThemeLogo";
import { PageHeaderProvider, usePageHeaderSlot } from "./PageHeaderContext";
import { usePermissions, useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { navProducao, rotaProducao, type ProductionLeaf } from "@/features/production/productionRoutes";
import { PERMISSOES_CUSTOS_POR_CAMINHO, podeExibirCustosDuranteHomologacao, ROTAS_CUSTOS, TELAS_CUSTOS } from "@/features/planning/costs/custosRoutes";
import { PERMISSOES_AUDITORIA_PROJETO, ROTA_AUDITORIA_PROJETO } from "@/features/planning/auditoriaProjeto";
import { TEMPLATE_TYPES, rotaTemplate } from "@/features/settings/templateTypes";
import { filtrarPorPermissao } from "@/features/permissions/permissionLogic";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { ProjectLogo } from "@/features/project/ProjectIdentity";
import { ThemeSelector } from "@/features/theme/ThemeSelector";

// =============================================================================================
// SIDEBAR DO AMBIENTE DO PROJETO (30/09/2026). A Home (/dashboard, cards de projeto) fica FORA do Layout; ao escolher
// um projeto o usuário entra aqui. Grupos: Painel de Planejamento · Controle de Produção · CDE · Relatórios ·
// Configurações. Item sem permissão NÃO aparece (grupo vazio some) — filtrarPorPermissao. Rotas antigas fora do menu
// continuam registradas em App.tsx (/cde, /painel → redirect, /lancamentos, /plant3d, /templates, /configurar-ldp).
// =============================================================================================

type TomBadge = "dev" | "old" | "demo";
interface NavBadge { texto: string; tom: TomBadge }

/** Bloco do projeto atual no topo do sidebar: nome + "Trocar projeto" (volta à Home de cards). */
const ProjetoAtualBloco = ({ isOpen }: { isOpen: boolean }) => {
  const { projetoAtual } = useProjetoAtual();
  const navigate = useNavigate();
  if (!isOpen) {
    return (
      <button type="button" onClick={() => navigate("/dashboard")} title={projetoAtual?.nome ? `${projetoAtual.nome} — trocar projeto` : "Escolher projeto"}
        className="mx-auto mb-2 flex h-9 w-9 items-center justify-center rounded-lg border border-border text-muted-foreground hover:border-primary/50 hover:text-primary">
        <ArrowLeftRight className="h-4 w-4" />
      </button>
    );
  }
  return (
    <div className="mx-3 mb-2 rounded-lg border border-border bg-background/40 px-3 py-2.5">
      <p className="text-[9px] font-black uppercase tracking-[0.25em] text-muted-foreground">Projeto</p>
      <div className="mt-1 flex items-center gap-2">
        {projetoAtual && <ProjectLogo projectId={projetoAtual.projectId} nome={projetoAtual.nome ?? projetoAtual.projectId} tamanho="sm" />}
        <p className="min-w-0 truncate text-sm font-bold" title={projetoAtual?.nome}>{projetoAtual?.nome ?? "Nenhum projeto selecionado"}</p>
      </div>
      <button type="button" onClick={() => navigate("/dashboard")}
        className="mt-1.5 flex items-center gap-1.5 text-[10px] font-black uppercase tracking-wider text-primary hover:underline">
        <ArrowLeftRight className="h-3 w-3" />{projetoAtual ? "Trocar projeto" : "Escolher projeto"}
      </button>
    </div>
  );
};

/** Rota com ?tab= só é ativa na mesma aba; sem query, vale a rota ou qualquer sub-rota dela. */
const rotaAtiva = (to: string, location: Location): boolean => {
  const [caminho, query] = to.split("?");
  if (query) {
    if (location.pathname !== caminho) return false;
    const esperado = new URLSearchParams(query);
    const atual = new URLSearchParams(location.search);
    return Array.from(esperado.entries()).every(([k, v]) => atual.get(k) === v);
  }
  return location.pathname === caminho || location.pathname.startsWith(`${caminho}/`);
};

const noAtivo = (node: NavNode, location: Location): boolean =>
  (node.to != null && rotaAtiva(node.to, location)) || (node.children?.some((c) => noAtivo(c, location)) ?? false);

const estiloBadge: Record<TomBadge, string> = {
  dev: "border-amber-500/40 bg-amber-500/10 text-amber-500",
  old: "border-muted-foreground/40 bg-muted/40 text-muted-foreground",
  demo: "border-sky-500/40 bg-sky-500/10 text-sky-500",
};

const Badge = ({ badge }: { badge: NavBadge }) => (
  <span
    className={cn(
      "inline-block flex-shrink-0 rounded border px-1.5 py-px text-[8px] font-black uppercase leading-tight tracking-wider",
      estiloBadge[badge.tom]
    )}
  >
    {badge.texto}
  </span>
);

interface NavNodeViewProps {
  node: NavNode;
  depth: number;
  isOpen: boolean;
  onExpandirSidebar: () => void;
}

const NavNodeView = ({ node, depth, isOpen, onExpandirSidebar }: NavNodeViewProps) => {
  const location = useLocation();
  const ativo = noAtivo(node, location);
  const ehGrupo = (node.children?.length ?? 0) > 0;
  // Cada grupo tem estado próprio: só o grupo clicado expande/recolhe.
  const [expandido, setExpandido] = useState(ativo);

  // Navegar para uma rota filha (ex.: link direto) abre os grupos pais — nunca fecha à força.
  useEffect(() => {
    if (ativo && ehGrupo) setExpandido(true);
  }, [ativo, ehGrupo]);

  const Icon = node.icon;
  const raiz = depth === 0;

  // Sidebar recolhido: só ícones do nível raiz; grupo reabre o sidebar para mostrar os filhos.
  if (!isOpen) {
    const classe = cn(
      "group flex w-full items-center justify-center rounded-lg py-2.5 px-0 transition-all duration-200",
      ativo ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
    );
    if (ehGrupo) {
      return (
        <button
          type="button"
          className={classe}
          title={node.label}
          onClick={() => {
            setExpandido(true);
            onExpandirSidebar();
          }}
        >
          <Icon className="w-5 h-5 flex-shrink-0" />
        </button>
      );
    }
    return (
      <NavLink to={node.to!} className={classe} title={node.label}>
        <Icon className="w-5 h-5 flex-shrink-0" />
      </NavLink>
    );
  }

  const tamanho = raiz ? "gap-3 px-3 py-2.5 text-sm" : "gap-2.5 px-3 py-2 text-xs";
  const tamanhoIcone = raiz ? "w-5 h-5" : "w-3.5 h-3.5";

  const conteudo = (
    <>
      <Icon className={cn(tamanhoIcone, "flex-shrink-0")} />
      <span className="flex min-w-0 flex-1 flex-col items-start gap-1 text-left">
        <span className="flex w-full min-w-0 items-center gap-1.5">
          <span className={cn("leading-tight", raiz ? "truncate" : "break-words")}>{node.label}</span>
          {node.badgeInline && <Badge badge={node.badgeInline} />}
        </span>
        {node.badgeAbaixo && <Badge badge={node.badgeAbaixo} />}
      </span>
    </>
  );

  if (ehGrupo) {
    return (
      <div>
        <button
          type="button"
          onClick={() => setExpandido((v) => !v)}
          aria-expanded={expandido}
          className={cn(
            "w-full group flex items-center rounded-lg font-medium transition-all duration-200",
            tamanho,
            ativo ? "text-primary" : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
          )}
        >
          {conteudo}
          <ChevronDown className={cn("w-3.5 h-3.5 flex-shrink-0 transition-transform", expandido && "rotate-180")} />
        </button>
        {expandido && (
          <div className="ml-4 pl-3 border-l border-border/50 space-y-1 mt-1">
            {node.children!.map((filho) => (
              <NavNodeView key={filho.id ?? filho.to ?? filho.label} node={filho} depth={depth + 1} isOpen={isOpen} onExpandirSidebar={onExpandirSidebar} />
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <NavLink
      to={node.to!}
      // isActive do NavLink ignora a query (?tab=) — o destaque usa rotaAtiva.
      className={cn(
        "group flex items-center rounded-lg font-medium transition-all duration-200",
        tamanho,
        ativo
          ? "bg-primary/10 text-primary shadow-[inset_3px_0_0_0_hsl(var(--primary))]"
          : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
      )}
      title={node.label}
    >
      {conteudo}
      {raiz && <ChevronRight className="w-3 h-3 ml-auto flex-shrink-0 opacity-0 group-hover:opacity-50 transition-opacity" />}
    </NavLink>
  );
};

/** Título do header global: o da página (slot) ou o título padrão da rota. */
const HeaderTitle = ({ fallback }: { fallback: string }) => {
  const { title } = usePageHeaderSlot();
  return title
    ? <h1 className="truncate text-sm font-black tracking-tight text-foreground">{title}</h1>
    : <h1 className="text-[10px] font-black tracking-[0.3em] text-muted-foreground/90 uppercase">{fallback}</h1>;
};

const HeaderActions = () => <>{usePageHeaderSlot().actions}</>;

const Layout = () => {
  const [userName, setUserName] = useState<string>("Carregando...");
  const [isSidebarOpen, setIsSidebarOpen] = useState(true);

  const navigate = useNavigate();
  const location = useLocation();
  // Sidebar filtrado pelas permissões efetivas (união global + projetos). Item sem permissão não aparece.
  const { effective } = usePermissions();
  const { projetoAtual } = useProjetoAtual();
  const { can, loading: permissionLoading } = useProjectPermissions(projetoAtual?.projectId);
  const officialConfiguration = useOfficialSidebar(projetoAtual?.projectId);
  const managerAccess = useSidebarManagerAccess();
  // Menu restrito: usar a mesma autorização do backend, jamais comparar nome exibido.
  const [comparisonAccess,setComparisonAccess] = useState<{subject:typeof effective;allowed:boolean}|null>(null);
  const canViewComparison = comparisonAccess?.subject===effective && comparisonAccess.allowed;
  useEffect(() => {
    let active = true;
    fetch("/api/production-audit/experimental-summary/access", { credentials: "include" })
      .then(async (response) => response.ok ? (await response.json()) as { allowed: boolean } : { allowed: false })
      .then((result) => { if (active) setComparisonAccess({subject:effective,allowed:result.allowed === true}); })
      .catch(() => { if (active) setComparisonAccess({subject:effective,allowed:false}); });
    return () => { active = false; };
  }, [effective]);

  const navegacaoComCustosRestritos = useMemo(() => contextualNavigation(canViewComparison,effective?.enforced ?? false,effective?.isMasterAdmin), [canViewComparison,effective?.enforced,effective?.isMasterAdmin]);
  const navegacaoConfigurada = useMemo(() => applyConfiguration(navegacaoComCustosRestritos, officialConfiguration), [navegacaoComCustosRestritos, officialConfiguration]);
  const navegacaoVisivel = useMemo(() => {
    const restrictManager=(nodes:NavNode[]):NavNode[]=>nodes.filter(n=>n.to!=="/configuracoes/sidebar"||managerAccess.allowed).map(n=>n.children?{...n,children:restrictManager(n.children)}:n);
    return permissionLoading ? [] : filtrarPorPermissao(restrictManager(navegacaoConfigurada),can);
  }, [permissionLoading,navegacaoConfigurada,can,managerAccess.allowed]);

  const getDisplayTitle = () => {
    const path = location.pathname;
    if (path === "/automacao/configuracao-robo") return "CONFIGURAÇÕES DO ROBÔ";
    if (path === "/configuracoes/sidebar") return "GERENCIADOR DE SIDEBAR";
    if (path === "/configuracoes/permissoes") return "PERMISSÕES";
    if (path === "/planejamento/programacao-semanal/vigente") return "PROGRAMAÇÃO SEMANAL VIGENTE";
    if (path === "/planejamento/programacao-semanal/semana-anterior") return "PROGRAMAÇÃO DA SEMANA ANTERIOR";
    if (path === "/planejamento/programacao-semanal/histograma") return "HISTOGRAMA DO PROJETO";
    if (path.startsWith("/planejamento/programacao-semanal")) return "PROGRAMAÇÃO SEMANAL";
    if (path === "/planejamento/painel-do-planejador") return "PAINEL DO PLANEJADOR";
    if (path === ROTA_AUDITORIA_PROJETO) return "AUDITORIA DO PROJETO";
    if (path === ROTAS_CUSTOS.impostos) return "CUSTOS DO PROJETO — IMPOSTOS";
    if (path === ROTAS_CUSTOS.profissionais) return "CUSTOS DO PROJETO — PROFISSIONAIS";
    if (path === ROTAS_CUSTOS.softwares) return "CUSTOS DO PROJETO — SOFTWARES";
    if (path === ROTAS_CUSTOS.pacotes) return "CUSTOS DO PROJETO — PACOTES";
    if (path === "/planejamento/normas-de-coordenacao") return "NORMA DE COORDENAÇÃO";
    if (path === "/planejamento/configuracao-do-projeto") return "CONFIGURAÇÃO DO PROJETO";
    if (path === "/qualidade/auditoria") return "AUDITORIA";
    if (path === "/qualidade/hh-sem-documento") return "HH SEM DOCUMENTO";
    if (path === "/qualidade/configuracao-avanco-fisico") return "CONFIGURAÇÃO DE AVANÇO FÍSICO";
    if (path === "/planejamento/auditoria-qualidade-documentos") return "AUDITORIA DE QUALIDADE DE DOCUMENTOS";
    if (path === "/planejamento/pastas-documentos") return "CONFIGURAÇÃO DE PASTAS DE DOCUMENTOS";
    if (path === "/cde/envio-documentos") return "ENVIO DE DOCUMENTOS";
    if (path === "/cde/documentos" || path.startsWith("/projects/") || path.startsWith("/projetos/")) return "DOCUMENTOS";
    if (path === "/configuracoes/identidade-visual") return "IDENTIDADE VISUAL DOS PROJETOS";
    if (path === "/planejamento-4d") return "PLANEJAMENTO 4D";
    if (path === "/orcamento-5d") return "ORÇAMENTO 5D";
    if (path === "/cde" || path.startsWith("/cde/")) return "CDE";
    if (path.startsWith("/controle-producao")) return "CONTROLE DE PRODUÇÃO";
    if (path === "/modelo-3d") return "MODELO 3D";
    if (path === "/configuracoes/telas-tv") return "CONFIGURAÇÃO DE TELAS PARA TV";
    if (path.startsWith("/configuracoes/templates/")) return "CONFIGURAÇÃO DE TEMPLATES";
    if (path === "/plant3d") return "PLANT 3D";
    if (path === "/templates") return "CONFIGURAÇÕES DE TEMPLATE QUALIDADE";
    if (path === "/configurar-ldp") return "CONFIGURADOR DE LDP (OLD)";
    if (path === "/configuracao-ldp/template") return "TEMPLATE LDP";
    if (path === "/configuracao-ldp/cronograma") return "MAPEAMENTO DO CRONOGRAMA";
    if (path === "/configuracao-ldp/espelho") return "GERAÇÃO LDP ESPELHO";
    if (path === "/configuracao-ldp/controle") return "CONFIGURAÇÃO LDP CONTROLE";
    if (path.startsWith("/configuracao-ldp")) return "PAINEL DE PLANEJAMENTO";
    if (path === "/extracoes-engenharia") return "EXTRAÇÕES DE ENGENHARIA";
    if (path.includes("/relatorios")) return "PAINEL GERENCIAL PMO";
    if (path.includes("/lancamentos")) return "CADASTRO DE LANÇAMENTOS"; // 👈 Título para a nova aba
    if (path.includes("/agendamento")) return "AGENDADOR DO ROBÔ";
    if (path.includes("/admin")) return "CONFIGURAÇÕES DO SISTEMA";
    if (path.includes("/ocr")) return "AUDITORIA DE QUALIDADE DE DOCUMENTOS";
    if (path.includes("/auditoria-3d")) return "AUDITORIA PLANT 3D";

    return location.pathname.split("/")[1]?.toUpperCase() || "FORGE SYNC CONNECT";
  };

  const pageTitle = getDisplayTitle();

  useEffect(() => {
    const fetchUser = async () => {
      try {
        const response = await fetch("/Authentication/OAuth/GetUserName", {
          credentials: "include"
        });

        if (response.status === 401) {
          navigate("/login");
          return;
        }

        if (response.ok) {
          const data = await response.json();
          if (data.userName) setUserName(data.userName);
        }
      } catch (error: unknown) {
        console.error("Falha ao obter sessão do usuário:", error);
      }
    };
    fetchUser();
  }, [navigate]);

  return (
    <PageHeaderProvider>
    {/* APP-SHELL (30/09/2026): altura = viewport. Sidebar fixo com rolagem PRÓPRIA; header fixo; só a área central
        rola (e telas com muita informação rolam internamente). A página inteira nunca cresce com o menu. */}
    <div className="flex h-screen w-full overflow-hidden bg-background text-foreground font-sans selection:bg-primary/30">

      <aside
        className={cn(
          "relative flex h-full min-h-0 flex-col flex-shrink-0 border-r border-border bg-card/30 backdrop-blur-sm transition-all duration-300 ease-in-out z-20",
          isSidebarOpen ? "w-72" : "w-20"
        )}
      >
        <button
          onClick={() => setIsSidebarOpen(!isSidebarOpen)}
          className="absolute -right-3 top-10 flex h-6 w-6 items-center justify-center rounded-full border border-border bg-background text-muted-foreground hover:text-primary shadow-sm hover:scale-110 transition-all z-50"
          title={isSidebarOpen ? "Recolher Menu" : "Expandir Menu"}
        >
          <ChevronRight className={cn("h-4 w-4 transition-transform duration-300", isSidebarOpen && "rotate-180")} />
        </button>

        <div className={cn("flex flex-shrink-0 items-center justify-center overflow-hidden", isSidebarOpen ? "h-24 px-6 py-3" : "h-20 px-2")}>
          <ThemeLogo variant={isSidebarOpen ? "sidebar" : "sidebar-compact"} />
        </div>

        {/* INÍCIO: volta à Home (cards de projeto, sem sidebar). O projeto atual continua lembrado. */}
        <div className={cn("flex-shrink-0", isSidebarOpen ? "px-3 pb-2" : "flex justify-center pb-2")}>
          <NavLink to="/dashboard" title="Início — escolher projeto"
            className={cn("flex items-center rounded-lg text-sm font-bold text-muted-foreground transition-colors hover:bg-accent/50 hover:text-foreground",
              isSidebarOpen ? "gap-3 px-3 py-2" : "h-10 w-10 justify-center")}>
            <Home className="h-5 w-5 flex-shrink-0" />{isSidebarOpen && <span>Início</span>}
          </NavLink>
        </div>

        <div className="flex-shrink-0"><ProjetoAtualBloco isOpen={isSidebarOpen} /></div>

        {/* Rolagem própria do menu (barra discreta: .sidebar-scroll em index.css). */}
        <nav aria-label="Menu principal" className="sidebar-scroll min-h-0 flex-1 px-3 space-y-1.5 mt-1 pb-3">
          {navegacaoVisivel.map((node) => (
            <NavNodeView key={node.id ?? node.to ?? node.label} node={node} depth={0} isOpen={isSidebarOpen} onExpandirSidebar={() => setIsSidebarOpen(true)} />
          ))}
        </nav>

        <div className="flex-shrink-0 border-t border-border bg-background/20">
          <div className={cn("flex items-center", isSidebarOpen ? "gap-3 px-4 py-3" : "justify-center py-3")}>
            <div className="h-9 w-9 flex-shrink-0 rounded-full bg-primary/20 flex items-center justify-center text-primary font-bold text-sm border border-primary/30 shadow-sm">
              {userName.charAt(0).toUpperCase()}
            </div>

            {isSidebarOpen && (
              <div className="flex flex-col min-w-0 transition-opacity duration-300">
                <p className="text-sm font-bold truncate leading-none mb-1.5">{userName}</p>
                <p className="text-[10px] text-primary font-black uppercase tracking-widest opacity-80">BIM Specialist</p>
                <p className="text-[10px] text-muted-foreground truncate" title={effective?.email ?? "Sessão não verificada"}>
                  {effective?.email ?? "Verificando conta…"}
                </p>
              </div>
            )}
          </div>

          <div className={cn("pb-3", isSidebarOpen ? "px-3" : "px-0 flex justify-center")}>
            <a
              href="/Authentication/OAuth/Logout"
              className={cn(
                "group flex items-center rounded-lg py-2.5 text-xs font-bold text-muted-foreground hover:bg-destructive/10 hover:text-destructive transition-all duration-200",
                isSidebarOpen ? "gap-3 px-3" : "justify-center w-10 h-10"
              )}
              title="Sair do Portal"
            >
              <LogOut className="w-4 h-4 flex-shrink-0 group-hover:-translate-x-1 transition-transform" />
              {isSidebarOpen && <span>Sair do Portal</span>}
            </a>
          </div>
        </div>
      </aside>

      <main className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <header className="h-14 border-b border-border bg-card/10 backdrop-blur-md flex items-center px-8 justify-between flex-shrink-0 z-10">
          <div className="flex items-center gap-4">
            <div className="w-1.5 h-4 bg-primary rounded-full shadow-[0_0_10px_hsl(var(--primary))]" />
            <HeaderTitle fallback={pageTitle} />
          </div>

          <div className="flex items-center gap-2">
            <HeaderActions />
            {/* Seletor global de tema (Claro / Escuro / Automático) — sempre visível no header. */}
            <ThemeSelector className="mx-1" />
            <button
              title="Notificações"
              className="p-2 text-muted-foreground hover:text-primary transition-colors rounded-md hover:bg-primary/5"
            >
              <Bell className="w-4 h-4" />
            </button>
            <button
              onClick={() => navigate('/dashboard')}
              title="Início — escolher projeto"
              aria-label="Início"
              className="p-2 text-muted-foreground hover:text-primary transition-colors rounded-md hover:bg-primary/5"
            >
              <Home className="w-4 h-4" />
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto custom-scrollbar bg-background/50">
          <div className="w-full h-full p-6 md:p-8 animate-enter">
            <Outlet />
          </div>
        </div>
      </main>
    </div>
    </PageHeaderProvider>
  );
};

export default Layout;

export function SidebarPreview({nodes}:{nodes:NavNode[]}) {return <div className="rounded-lg border border-border bg-card p-3 space-y-1">{nodes.map(node=><NavNodeView key={node.id ?? node.label} node={node} depth={0} isOpen={true} onExpandirSidebar={()=>{}} />)}</div>;}
