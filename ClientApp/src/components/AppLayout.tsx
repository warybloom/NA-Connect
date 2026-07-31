/**
 * AppLayout.tsx
 * Layout Command Center do ForgeSync — Fase A.
 *
 * Usa o Shadcn SidebarProvider + Sidebar com grupos por Pilares de Negócio.
 * Envolve <ProjectProvider> para que todos os filhos tenham acesso a useProject().
 *
 * Pilares implementados na Fase A:
 *   ✅ Engenharia Técnica  (rotas ativas)
 *   🔜 Gestão Executiva    (placeholder)
 *   🔜 Planejamento        (placeholder)
 *   🔜 Suprimentos         (placeholder)
 *   🔜 Campo               (placeholder)
 */

import { useEffect, useState } from "react";
import { Outlet, NavLink, useNavigate, useLocation, useParams } from "react-router-dom";
import {
  LayoutDashboard, Wrench, CalendarCheck2, Package, HardHat,
  Settings, Bell, LogOut, Sun, Moon, ChevronRight,
  ScanLine, UploadCloud, FileSpreadsheet, TrendingUp,
  Boxes, ClipboardCheck, AlertOctagon, Layers3,
  GitBranch, Settings2, Cpu // <-- ADICIONE O CPU AQUI
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Logo } from "@/components/ui/logo";
import { ProjectSwitcher } from "@/components/ProjectSwitcher";
import { ProjectProvider, useProject } from "@/contexts/ProjectContext";

// ── Tipos ──────────────────────────────────────────────────────────────────────

interface NavItem {
  label:    string;
  path:     string;    // relativo ao /projeto/:projectId/
  icon:     React.ElementType;
  badge?:   "novo" | "em breve";
}

interface NavGroup {
  label:   string;
  icon:    React.ElementType;
  items:   NavItem[];
  active:  boolean;     // Fase A: apenas Engenharia ativo
}

// ── Configuração de navegação ──────────────────────────────────────────────────

function buildNav(projectId: string): NavGroup[] {
  const base = `/projeto/${projectId}`;

  return [
    {
      label:  "Gestão Executiva",
      icon:   LayoutDashboard,
      active: true,
      items: [
        { label: "Dashboard EVM", path: `${base}/executivo/dashboard`, icon: LayoutDashboard },
        { label: "Curva S",       path: `${base}/executivo/curva-s`,   icon: TrendingUp      },
        { label: "Desvios",       path: `${base}/executivo/desvios`,   icon: AlertOctagon    },
      ],
    },
    {
      label:  "Engenharia Técnica",
      icon:   Wrench,
      active: true,
      items: [
        { label: "Auditoria P&ID·3D", path: `${base}/engenharia/auditoria`,    icon: ScanLine        },
        { label: "Insumos de Dados",  path: `${base}/engenharia/insumos`,      icon: UploadCloud     },
        { label: "Linhas",            path: `${base}/engenharia/linhas`,       icon: GitBranch       },
        { label: "Instrumentos",      path: `${base}/engenharia/instrumentos`, icon: Cpu             },
        { label: "Equipamentos",      path: `${base}/engenharia/equipamentos`, icon: Boxes           },
        { label: "Válvulas",          path: `${base}/engenharia/valvulas`,     icon: Settings2       },
        { label: "Árvore AWP",        path: `${base}/engenharia/arvore`,      icon: GitBranch       },
        { label: "Configurar AWP",    path: `${base}/engenharia/awp-config`,  icon: Layers3,  badge: "novo" },
      ]
    },
    {
      label:  "Planejamento & Controle",
      icon:   CalendarCheck2,
      active: false,
      items: [
        { label: "Cronograma 4D",   path: `${base}/planejamento/cronograma`, icon: CalendarCheck2, badge: "em breve" },
        { label: "Pacotes IWP/CWP", path: `${base}/planejamento/pacotes`,   icon: Boxes,          badge: "em breve" },
        { label: "Critérios",       path: `${base}/planejamento/medicao`,   icon: ClipboardCheck, badge: "em breve" },
      ],
    },
    {
      label:  "Recursos & Suprimentos",
      icon:   Package,
      active: false,
      items: [
        { label: "Requisições MR",    path: `${base}/suprimentos/requisicoes`, icon: Package, badge: "em breve" },
        { label: "Lista de Materiais",path: `${base}/suprimentos/bom`,         icon: Boxes,   badge: "em breve" },
      ],
    },
    {
      label:  "Gestão de Campo",
      icon:   HardHat,
      active: true,
      items: [
        { label: "Qualidade & Controle", path: `${base}/campo/qualidade`,  icon: ClipboardCheck  },
        { label: "Inspeções QC",         path: `${base}/campo/inspecoes`,  icon: ClipboardCheck, badge: "em breve" },
      ],
    },
  ];
}

// ── Componente de item de navegação ───────────────────────────────────────────

function SidebarNavItem({
  item,
  isOpen,
  isGroupActive,
}: {
  item:          NavItem;
  isOpen:        boolean;
  isGroupActive: boolean;
}) {
  const isSoon = item.badge === "em breve";

  if (isSoon) {
    return (
      <div
        title={isOpen ? undefined : item.label}
        className={cn(
          "flex items-center rounded-lg py-2 text-xs text-muted-foreground/40 cursor-not-allowed select-none",
          isOpen ? "px-3 gap-2.5" : "justify-center px-0"
        )}
      >
        <item.icon className="w-4 h-4 flex-shrink-0" />
        {isOpen && (
          <>
            <span className="flex-1 truncate">{item.label}</span>
            <span className="text-[8px] font-black uppercase tracking-widest text-muted-foreground/30 border border-muted-foreground/20 rounded px-1 py-0.5">
              breve
            </span>
          </>
        )}
      </div>
    );
  }

  return (
    <NavLink
      to={item.path}
      title={isOpen ? undefined : item.label}
      className={({ isActive }) =>
        cn(
          "flex items-center rounded-lg py-2 text-xs font-medium transition-all duration-200",
          isOpen ? "px-3 gap-2.5" : "justify-center px-0",
          isActive
            ? "bg-accent/12 text-accent font-bold shadow-[inset_2px_0_0_hsl(var(--accent))]"
            : "text-muted-foreground hover:bg-muted/50 hover:text-foreground"
        )
      }
    >
      <item.icon className="w-4 h-4 flex-shrink-0" />
      {isOpen && (
        <>
          <span className="flex-1 truncate">{item.label}</span>
          {item.badge === "novo" && (
            <span className="text-[8px] font-black uppercase tracking-widest text-emerald-400 border border-emerald-400/30 rounded px-1 py-0.5">
              novo
            </span>
          )}
        </>
      )}
    </NavLink>
  );
}

// ── Grupo de navegação ────────────────────────────────────────────────────────

function SidebarNavGroup({
  group,
  isOpen,
  defaultExpanded,
}: {
  group:           NavGroup;
  isOpen:          boolean;
  defaultExpanded: boolean;
}) {
  const [expanded, setExpanded] = useState(defaultExpanded);
  const GroupIcon = group.icon;

  return (
    <div className="mb-1">
      {/* Cabeçalho do grupo */}
      <button
        onClick={() => setExpanded(v => !v)}
        title={isOpen ? undefined : group.label}
        className={cn(
          "w-full flex items-center rounded-lg py-2 transition-colors duration-200",
          isOpen ? "px-3 gap-2.5" : "justify-center px-0",
          group.active
            ? "text-foreground hover:bg-muted/30"
            : "text-muted-foreground/50 cursor-default pointer-events-none"
        )}
      >
        <div className={cn(
          "flex h-5 w-5 flex-shrink-0 items-center justify-center rounded",
          group.active ? "text-accent" : "text-muted-foreground/30"
        )}>
          <GroupIcon className="w-4 h-4" />
        </div>

        {isOpen && (
          <>
            <span className={cn(
              "flex-1 text-left text-[9px] font-black uppercase tracking-[0.15em]",
              group.active ? "text-muted-foreground" : "text-muted-foreground/30"
            )}>
              {group.label}
            </span>
            {group.active && (
              <ChevronRight className={cn(
                "h-3 w-3 text-muted-foreground/40 transition-transform duration-200",
                expanded && "rotate-90"
              )} />
            )}
            {!group.active && isOpen && (
              <span className="text-[7px] font-black uppercase tracking-widest text-muted-foreground/25 border border-muted-foreground/15 rounded px-1">
                fase b
              </span>
            )}
          </>
        )}
      </button>

      {/* Itens do grupo */}
      {group.active && expanded && (
        <div className={cn("mt-0.5 space-y-0.5", isOpen ? "pl-2" : "")}>
          {group.items.map(item => (
            <SidebarNavItem
              key={item.path}
              item={item}
              isOpen={isOpen}
              isGroupActive={group.active}
            />
          ))}
        </div>
      )}
    </div>
  );
}

// ── Topbar breadcrumb ─────────────────────────────────────────────────────────

function Breadcrumb() {
  const location = useLocation();
  const parts    = location.pathname.split("/").filter(Boolean);
  // /projeto/:projectId/:pilar/:submodulo → ["projeto", ":id", "engenharia", "auditoria"]

  const labelMap: Record<string, string> = {
    projeto:       "Projetos",
    engenharia:    "Engenharia Técnica",
    executivo:     "Gestão Executiva",
    planejamento:  "Planejamento & Controle",
    suprimentos:   "Recursos & Suprimentos",
    campo:         "Gestão de Campo",
    auditoria:     "Auditoria P&ID · 3D",
    insumos:       "Insumos de Dados",
    listas:        "Listas Técnicas",
    nomenclatura:  "Nomenclatura",
    arvore:        "Árvore AWP",
    dashboard:     "Dashboard EVM",
    "curva-s":     "Curva S",
    desvios:       "Desvios",
    cronograma:    "Cronograma 4D",
    pacotes:       "Pacotes IWP/CWP",
    requisicoes:   "Requisições MR",
    bom:           "Lista de Materiais",
    inspecoes:     "Inspeções QC",
    ncs:           "Não Conformidades",
    holds:         "Holds",
    medicao:       "Critérios de Medição",
  };

  const crumbs: string[] = [];
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    // Pula "projeto" e o valor do projectId (i==1 quando part[0]=="projeto")
    if (part === "projeto") continue;
    if (i === 1 && parts[0] === "projeto") continue;
    crumbs.push(labelMap[part] ?? part.toUpperCase());
  }

  return (
    <div className="flex items-center gap-1.5">
      <div className="w-1.5 h-4 bg-primary rounded-full shadow-[0_0_6px_hsl(var(--primary))]" />
      <p className="text-[10px] font-black tracking-[0.25em] text-muted-foreground uppercase">
        {crumbs.join(" › ")}
      </p>
    </div>
  );
}

// ── Layout interno (precisa de useProject, portanto fica dentro do Provider) ──

function AppLayoutInner() {
  const navigate   = useNavigate();
  const { projectId } = useParams<{ projectId: string }>();
  const { setProject } = useProject();

  const [isSidebarOpen, setIsSidebarOpen] = useState(true);
  const [theme,         setTheme]         = useState<"dark" | "light">("dark");
  const [userProfile,   setUserProfile]   = useState<{ name: string; tenantName: string } | null>(null);

  // Sincroniza projectId da URL com o contexto (ex: link direto colado)
  useEffect(() => {
    if (projectId) {
      // Tenta obter o nome; se não tiver, usa o ID como fallback
      setProject(projectId, projectId);
    }
  }, [projectId, setProject]);

  // Carrega perfil do usuário
  useEffect(() => {
    const fetchProfile = async () => {
      try {
        const res = await fetch("/Authentication/OAuth/Me", { credentials: "include" });
        if (res.status === 401) { navigate("/login"); return; }
        if (res.ok) {
          const data = await res.json();
          setUserProfile({ name: data.user.name, tenantName: data.tenant.name });
        }
      } catch {
        // Silencioso — perfil é cosmético no layout
      }
    };
    fetchProfile();
  }, [navigate]);

  const toggleTheme = () => {
    const next = theme === "dark" ? "light" : "dark";
    setTheme(next);
    document.documentElement.classList.toggle("dark", next === "dark");
  };

  const navGroups = buildNav(projectId ?? "");

  return (
    <div className="min-h-screen w-full flex bg-background text-foreground font-sans transition-colors duration-500">

      {/* ── SIDEBAR ─────────────────────────────────────────────────────────── */}
      <aside className={cn(
        "relative border-r border-border bg-card/40 backdrop-blur-md flex flex-col flex-shrink-0",
        "transition-all duration-300 ease-in-out z-20 shadow-sm",
        isSidebarOpen ? "w-64" : "w-16"
      )}>

        {/* Toggle */}
        <button
          onClick={() => setIsSidebarOpen(v => !v)}
          title={isSidebarOpen ? "Recolher menu" : "Expandir menu"}
          className="absolute -right-3 top-10 flex h-6 w-6 items-center justify-center rounded-full border border-border bg-background text-muted-foreground hover:text-primary shadow-md hover:scale-110 transition-all z-50"
        >
          <ChevronRight className={cn("h-4 w-4 transition-transform duration-300", isSidebarOpen && "rotate-180")} />
        </button>

        {/* Logo */}
        <div className={cn("h-16 flex items-center overflow-hidden", isSidebarOpen ? "px-5" : "justify-center px-2")}>
          {isSidebarOpen
            ? <Logo />
            : <div className="font-black text-primary text-lg tracking-tighter">FS</div>
          }
        </div>

        {/* Navegação por pilares */}
        <nav className="flex-1 px-2 mt-2 space-y-0.5 overflow-y-auto custom-scrollbar">
          {navGroups.map((group, idx) => (
            <SidebarNavGroup
              key={group.label}
              group={group}
              isOpen={isSidebarOpen}
              defaultExpanded={idx === 0 || idx === 1}  // Executivo e Engenharia começam expandidos
            />
          ))}

          {/* Divider + Admin */}
          <div className={cn("pt-3 mt-3 border-t border-border/50")}>
            <NavLink
              to="/painel"
              title={isSidebarOpen ? undefined : "Administração"}
              className={({ isActive }) => cn(
                "flex items-center rounded-lg py-2 text-xs font-medium transition-all duration-200",
                isSidebarOpen ? "px-3 gap-2.5" : "justify-center px-0",
                isActive
                  ? "bg-primary/15 text-primary"
                  : "text-muted-foreground/60 hover:bg-muted/50 hover:text-foreground"
              )}
            >
              <Settings className="w-4 h-4 flex-shrink-0" />
              {isSidebarOpen && <span className="flex-1 truncate">Administração</span>}
            </NavLink>
          </div>
        </nav>

        {/* Perfil + logout */}
        <div className="mt-auto border-t border-border/50">
          {userProfile && (
            <div className={cn("flex items-center py-4", isSidebarOpen ? "gap-3 px-4" : "justify-center")}>
              <div className="h-8 w-8 flex-shrink-0 rounded-full bg-primary/20 flex items-center justify-center text-primary font-bold text-sm border border-primary/30">
                {userProfile.name.charAt(0).toUpperCase()}
              </div>
              {isSidebarOpen && (
                <div className="flex flex-col min-w-0">
                  <p className="text-xs font-bold truncate text-foreground">{userProfile.name}</p>
                  <p className="text-[9px] text-primary font-black uppercase tracking-widest opacity-70 truncate">{userProfile.tenantName}</p>
                </div>
              )}
            </div>
          )}
          <div className={cn("pb-4", isSidebarOpen ? "px-2" : "flex justify-center px-0")}>
            <a
              href="/Authentication/OAuth/Logout"
              title="Sair do Portal"
              className={cn(
                "group flex items-center rounded-lg py-2 text-xs font-bold text-muted-foreground hover:bg-destructive/10 hover:text-destructive transition-all",
                isSidebarOpen ? "gap-2.5 px-3" : "justify-center"
              )}
            >
              <LogOut className="w-4 h-4 flex-shrink-0 group-hover:-translate-x-0.5 transition-transform" />
              {isSidebarOpen && <span>Sair do Portal</span>}
            </a>
          </div>
        </div>
      </aside>

      {/* ── CONTEÚDO PRINCIPAL ───────────────────────────────────────────────── */}
      <main className="flex-1 flex flex-col min-w-0 overflow-hidden">

        {/* Topbar */}
        <header className="h-14 border-b border-border bg-card/40 backdrop-blur-md flex items-center px-6 justify-between flex-shrink-0 z-10 shadow-sm">
          <Breadcrumb />

          <div className="flex items-center gap-3">
            {/* Seletor de projeto — coração do Command Center */}
            <ProjectSwitcher />

            <div className="h-5 w-px bg-border/60" />

            <button onClick={toggleTheme} title="Alternar tema" className="p-2 text-muted-foreground hover:text-primary transition-all rounded-md hover:bg-primary/10">
              {theme === "dark" ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
            </button>
            <button title="Notificações" className="p-2 text-muted-foreground hover:text-primary transition-all rounded-md hover:bg-primary/10">
              <Bell className="w-4 h-4" />
            </button>
          </div>
        </header>

        {/* Área de conteúdo */}
        <div className="flex-1 overflow-y-auto custom-scrollbar bg-background transition-colors duration-500">
          <div className="w-full h-full p-6 md:p-8 animate-in fade-in slide-in-from-bottom-4 duration-400">
            <Outlet />
          </div>
        </div>
      </main>
    </div>
  );
}

// ── Export público: envolve o Provider ────────────────────────────────────────

export default function AppLayout() {
  return (
    <ProjectProvider>
      <AppLayoutInner />
    </ProjectProvider>
  );
}
