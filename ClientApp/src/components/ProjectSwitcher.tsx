/**
 * ProjectSwitcher.tsx
 * Seletor de projeto premium na topbar do Command Center.
 *
 * Design: badge ATIVO em destaque, nome completo visível,
 * dropdown com busca, avatar de iniciais e indicador accent.
 */

import { useState, useEffect, useRef } from "react";
import { useNavigate, useParams, useLocation } from "react-router-dom";
import {
  FolderKanban, ChevronDown, Search, Loader2,
  CheckCircle2, FolderOpen, Zap,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useProject } from "@/contexts/ProjectContext";

interface ProjectOption {
  projectId:   string;
  projectName: string;
}

export function ProjectSwitcher() {
  const { projectId: activeId, projectName: activeName, setProject } = useProject();
  const navigate  = useNavigate();
  const location  = useLocation();
  const params    = useParams<{ projectId?: string }>();

  const [isOpen,   setIsOpen]   = useState(false);
  const [projects, setProjects] = useState<ProjectOption[]>([]);
  const [search,   setSearch]   = useState("");
  const [loading,  setLoading]  = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // ── Fechar ao clicar fora ──────────────────────────────────────────────────
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node))
        setIsOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  // ── Carregar projetos ao abrir ─────────────────────────────────────────────
  useEffect(() => {
    if (!isOpen || projects.length > 0) return;
    const load = async () => {
      setLoading(true);
      try {
        const res  = await fetch("/api/Plant3D/list-projects", { credentials: "include" });
        const data = await res.json();
        if (data.sucesso && Array.isArray(data.projetos)) {
          setProjects(data.projetos);
          if (params.projectId && !activeId) {
            const found = data.projetos.find((p: ProjectOption) => p.projectId === params.projectId);
            if (found) setProject(found.projectId, found.projectName);
          }
        }
      } catch { /* silencioso */ }
      finally { setLoading(false); }
    };
    load();
  }, [isOpen, projects.length, activeId, params.projectId, setProject]);

  // ── Seleção de projeto ─────────────────────────────────────────────────────
  const handleSelect = (proj: ProjectOption) => {
    setProject(proj.projectId, proj.projectName);
    setIsOpen(false);
    setSearch("");
    // Mantém o sub-módulo ativo se estiver no mesmo pillar; caso contrário, vai para auditoria
    const pathParts = location.pathname.split("/");
    const pillarIdx = 3; // /projeto/:id/:pillar/...
    const pillar    = pathParts[pillarIdx] ?? "engenharia";
    const sub       = pathParts[4] ?? "auditoria";
    navigate(`/projeto/${proj.projectId}/${pillar}/${sub}`);
  };

  const filtered = projects.filter(p =>
    p.projectName.toLowerCase().includes(search.toLowerCase()) ||
    p.projectId.toLowerCase().includes(search.toLowerCase())
  );

  const hasProject  = Boolean(activeId);
  const displayName = activeName || activeId || null;

  // Iniciais para o avatar
  const initials = displayName
    ? displayName.split(/[\s_-]/).slice(0, 2).map(w => w[0]).join("").toUpperCase()
    : "—";

  return (
    <div ref={dropdownRef} className="relative">
      {/* ── Trigger ─────────────────────────────────────────────────────────── */}
      <button
        onClick={() => setIsOpen(v => !v)}
        title={displayName ?? "Selecionar projeto"}
        className={cn(
          "group flex items-center gap-3 rounded-xl border px-3 py-2 transition-all duration-200",
          "bg-card/60 backdrop-blur-sm shadow-sm",
          hasProject
            ? "border-accent/30 hover:border-accent/60 hover:bg-accent/5"
            : "border-border/60 hover:border-primary/40 hover:bg-primary/5",
          isOpen && (hasProject ? "border-accent/60 bg-accent/5" : "border-primary/40 bg-primary/5")
        )}
      >
        {/* Avatar de iniciais */}
        <div className={cn(
          "flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg text-[10px] font-black transition-colors",
          hasProject
            ? "bg-accent/20 text-accent"
            : "bg-muted text-muted-foreground"
        )}>
          {hasProject ? initials : <FolderKanban className="h-3.5 w-3.5" />}
        </div>

        {/* Nome + badge */}
        <div className="flex flex-col items-start min-w-0">
          {hasProject && (
            <div className="flex items-center gap-1.5 mb-0.5">
              <div className="h-1.5 w-1.5 rounded-full bg-accent animate-pulse" />
              <span className="text-[8px] font-black uppercase tracking-[0.25em] text-accent/80">
                ATIVO
              </span>
            </div>
          )}
          <span className={cn(
            "max-w-[200px] truncate text-[11px] font-black tracking-wide uppercase leading-none",
            hasProject ? "text-foreground" : "text-muted-foreground"
          )}>
            {displayName ?? "Selecionar Projeto"}
          </span>
        </div>

        <ChevronDown className={cn(
          "h-3.5 w-3.5 flex-shrink-0 text-muted-foreground/60 transition-transform duration-200 ml-1",
          isOpen && "rotate-180"
        )} />
      </button>

      {/* ── Dropdown ────────────────────────────────────────────────────────── */}
      {isOpen && (
        <div className={cn(
          "absolute right-0 top-full z-50 mt-2 w-80 rounded-2xl border border-border/60",
          "bg-card/95 backdrop-blur-md shadow-2xl shadow-black/30",
          "animate-in fade-in slide-in-from-top-3 duration-150"
        )}>
          {/* Header do dropdown */}
          <div className="px-4 pt-3 pb-2 border-b border-border/40 flex items-center gap-2">
            <Zap className="h-3.5 w-3.5 text-accent/70" />
            <span className="text-[9px] font-black uppercase tracking-[0.25em] text-muted-foreground">
              Command Center
            </span>
          </div>

          {/* Busca */}
          <div className="p-2">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground/60" />
              <input
                autoFocus
                type="text"
                placeholder="Buscar projeto..."
                value={search}
                onChange={e => setSearch(e.target.value)}
                className={cn(
                  "w-full rounded-xl border border-border/50 bg-background/60 py-2 pl-9 pr-3",
                  "text-xs font-medium placeholder:text-muted-foreground/50",
                  "focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent/40",
                  "transition-all"
                )}
              />
            </div>
          </div>

          {/* Lista */}
          <div className="max-h-60 overflow-y-auto custom-scrollbar px-2 pb-2">
            {loading ? (
              <div className="flex items-center justify-center gap-2 py-8 text-xs text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin text-accent/60" />
                Carregando projetos...
              </div>
            ) : filtered.length === 0 ? (
              <div className="flex flex-col items-center gap-2 py-8 text-xs text-muted-foreground">
                <FolderOpen className="h-5 w-5 opacity-40" />
                {search ? "Nenhum projeto encontrado" : "Nenhum projeto disponível"}
              </div>
            ) : (
              filtered.map(proj => {
                const isActive = proj.projectId === activeId;
                const pInitials = proj.projectName
                  .split(/[\s_-]/).slice(0, 2).map(w => w[0]).join("").toUpperCase() || "PR";

                return (
                  <button
                    key={proj.projectId}
                    onClick={() => handleSelect(proj)}
                    className={cn(
                      "flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left transition-all duration-150",
                      isActive
                        ? "bg-accent/10 border border-accent/20"
                        : "hover:bg-muted/50 border border-transparent"
                    )}
                  >
                    {/* Avatar */}
                    <div className={cn(
                      "flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg text-[10px] font-black",
                      isActive
                        ? "bg-accent/20 text-accent"
                        : "bg-muted text-muted-foreground"
                    )}>
                      {pInitials}
                    </div>

                    {/* Texto */}
                    <div className="min-w-0 flex-1">
                      <p className={cn(
                        "text-[11px] font-black uppercase tracking-wide leading-tight",
                        isActive ? "text-accent" : "text-foreground"
                      )}>
                        {proj.projectName || proj.projectId}
                      </p>
                      <p className="text-[9px] text-muted-foreground font-mono mt-0.5 truncate">
                        ID: {proj.projectId}
                      </p>
                    </div>

                    {isActive && (
                      <CheckCircle2 className="h-4 w-4 flex-shrink-0 text-accent" />
                    )}
                  </button>
                );
              })
            )}
          </div>

          {/* Rodapé */}
          {!loading && projects.length > 0 && (
            <div className="border-t border-border/40 px-4 py-2">
              <p className="text-[9px] text-muted-foreground/60 text-center font-mono">
                {projects.length} projeto{projects.length !== 1 ? "s" : ""} disponíve{projects.length !== 1 ? "is" : "l"} · ForgeSync
              </p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
