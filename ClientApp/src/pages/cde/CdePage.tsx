// CDE — seleção de UM projeto por cards. Mesmas fontes do Dashboard (/DataManagement/Data/Hubs,
// /DataManagement/Data/Projetos, registro dbo.Projetos para Is_Ativo), mas página independente: o
// clique abre a área CDE do projeto (/cde/:hubId/:projectId), não a pasta raiz.
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Building2, ChevronDown, ChevronLeft, ChevronRight, Search } from "lucide-react";
import toast from "react-hot-toast";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger } from "@/components/ui/select";
import { ProjectCard } from "@/components/projects/ProjectCard";
import { getProjetosRegistrados, normalizeAccId, type ProjetoRegistrado } from "@/lib/projectsRegistry";

interface Hub {
  id: string;
  attributes: { name: string; region: string };
}

interface Project {
  id: string;
  attributes: { name: string; extension: { type: string } };
}

interface AutodeskApiResponse<T> {
  data: T[];
}

const ITENS_POR_PAGINA = 8;

export default function CdePage() {
  const navigate = useNavigate();
  const [hubs, setHubs] = useState<Hub[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [registro, setRegistro] = useState<Map<string, ProjetoRegistrado>>(new Map());
  const [selectedHubId, setSelectedHubId] = useState("");
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);

  useEffect(() => {
    const carregar = async () => {
      try {
        const res = await fetch("/DataManagement/Data/Hubs", { credentials: "include" });
        if (res.status === 401) { navigate("/login"); return; }
        if (res.status === 409) {
          const body = await res.json().catch(() => ({}));
          if (body.autodeskReconnectRequired || body.code === "autodeskReconnectRequired")
            toast.error("Conecte sua conta Autodesk para acessar o CDE.");
          setLoading(false);
          return;
        }
        if (!res.ok) { toast.error(`Falha ao carregar contas (HTTP ${res.status}).`); setLoading(false); return; }
        const data: AutodeskApiResponse<Hub> = await res.json();
        const lista = data.data || [];
        setHubs(lista);
        if (lista.length > 0) setSelectedHubId(lista[0].id);
        else setLoading(false);
      } catch {
        toast.error("Erro na conexão com o servidor N&A.");
        setLoading(false);
      }
      getProjetosRegistrados().then(setRegistro).catch(() => setRegistro(new Map()));
    };
    carregar();
  }, [navigate]);

  useEffect(() => {
    if (!selectedHubId) return;
    const carregar = async () => {
      setLoading(true);
      try {
        const res = await fetch(`/DataManagement/Data/Projetos?hubId=${selectedHubId}`, { credentials: "include" });
        if (!res.ok) {
          if (res.status === 401) { navigate("/login"); return; }
          if (res.status === 409) {
            const body = await res.json().catch(() => ({}));
            if (body.autodeskReconnectRequired || body.code === "autodeskReconnectRequired")
              toast.error("Conecte sua conta Autodesk para acessar o CDE.");
            setProjects([]);
            return;
          }
          toast.error(`Falha ao carregar projetos (HTTP ${res.status}).`);
          setProjects([]);
          return;
        }
        const data: AutodeskApiResponse<Project> = await res.json();
        setProjects(data.data || []);
        setCurrentPage(1);
      } catch {
        toast.error("Erro ao carregar projetos.");
      } finally {
        setLoading(false);
      }
    };
    carregar();
  }, [selectedHubId, navigate]);

  // Mesmo filtro do Dashboard: oculta só os marcados Is_Ativo=0 em dbo.Projetos.
  const filtrados = useMemo(
    () =>
      projects
        .filter((p) => registro.get(normalizeAccId(p.id))?.isAtivo !== false)
        .filter((p) => p.attributes.name.toLowerCase().includes(searchTerm.toLowerCase())),
    [projects, searchTerm, registro]
  );

  const totalPages = Math.ceil(filtrados.length / ITENS_POR_PAGINA);
  const paginados = useMemo(
    () => filtrados.slice((currentPage - 1) * ITENS_POR_PAGINA, currentPage * ITENS_POR_PAGINA),
    [filtrados, currentPage]
  );

  const abrirProjeto = (project: Project) =>
    navigate(`/cde/${encodeURIComponent(selectedHubId)}/${encodeURIComponent(project.id)}`, {
      state: { projectName: project.attributes.name },
    });

  return (
    <div className="flex flex-col h-full animate-enter">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 mb-8 shrink-0">
        <div className="space-y-1">
          <h1 className="text-3xl font-black text-foreground tracking-tight italic uppercase">CDE</h1>
          <div className="flex items-center gap-2">
            <Label htmlFor="cde-hub-selector" className="sr-only">Selecionar conta</Label>
            <Select value={selectedHubId} onValueChange={setSelectedHubId}>
              <SelectTrigger id="cde-hub-selector" className="w-auto bg-transparent border-none p-0 h-auto focus:ring-0 shadow-none group transition-all">
                <div className="flex items-center gap-2">
                  <div className="p-1.5 rounded bg-primary/10 border border-primary/20 group-hover:border-primary/50 transition-colors">
                    <Building2 className="w-4 h-4 text-primary" />
                  </div>
                  <span className="text-[11px] font-black text-muted-foreground uppercase tracking-[0.2em] group-hover:text-primary transition-colors">
                    CONTA: {hubs.find((h) => h.id === selectedHubId)?.attributes.name || "CARREGANDO..."}
                  </span>
                  <ChevronDown className="w-3 h-3 text-muted-foreground group-hover:text-primary transition-colors" />
                </div>
              </SelectTrigger>
              <SelectContent className="glass-card border-border/50 min-w-[280px]">
                {hubs.map((h) => (
                  <SelectItem key={h.id} value={h.id} className="text-[10px] font-black uppercase tracking-widest cursor-pointer focus:text-primary transition-colors">
                    {h.attributes.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <p className="text-xs text-muted-foreground">Selecione um projeto para abrir Lista de Documentos, Docs e Curva S.</p>
        </div>

        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
          <Input
            placeholder="Pesquisar projetos..."
            className="pl-10 w-64 bg-card/30 border-border/50 focus:border-primary/50"
            value={searchTerm}
            onChange={(e) => { setSearchTerm(e.target.value); setCurrentPage(1); }}
          />
        </div>
      </div>

      <div className="flex-1 overflow-hidden flex flex-col min-h-0">
        {loading ? (
          <div className="grid grid-cols-1 md:grid-cols-4 gap-6">
            {[1, 2, 3, 4].map((i) => (
              <div key={i} className="h-44 bg-card/20 rounded-xl border border-border/50 animate-pulse" />
            ))}
          </div>
        ) : filtrados.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhum projeto encontrado.</p>
        ) : (
          <>
            <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-6 overflow-y-auto pr-2 custom-scrollbar">
              {paginados.map((project) => (
                <ProjectCard
                  key={project.id}
                  name={project.attributes.name}
                  extensionType={project.attributes.extension.type}
                  naoRegistrado={!registro.has(normalizeAccId(project.id))}
                  actionLabel="ABRIR CDE →"
                  onClick={() => abrirProjeto(project)}
                />
              ))}
            </div>

            {totalPages > 1 && (
              <div className="mt-auto pt-8 flex items-center justify-between border-t border-border/40 shrink-0">
                <p className="text-[10px] font-black text-muted-foreground uppercase tracking-widest">
                  Página {currentPage} de {totalPages} • {filtrados.length} Projetos
                </p>
                <div className="flex gap-2">
                  <Button variant="outline" size="icon" className="h-8 w-8 glass-card border-border/50 disabled:opacity-30"
                    onClick={() => setCurrentPage((p) => Math.max(1, p - 1))} disabled={currentPage === 1}>
                    <ChevronLeft className="h-4 w-4" />
                  </Button>
                  <Button variant="outline" size="icon" className="h-8 w-8 glass-card border-border/50 disabled:opacity-30"
                    onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))} disabled={currentPage === totalPages}>
                    <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
