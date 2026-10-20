import { useState, useEffect, useMemo, useCallback } from "react";
import { AnimatedBackground } from "@/components/ui/animated-background";
import { ThemeLogo } from "@/features/theme/ThemeLogo";
import { ElegantCard } from "@/components/ui/elegant-card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Search } from "lucide-react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { CreateProjectModal } from "@/components/CreateProjectModal";
import toast from 'react-hot-toast';
import { getProjetosRegistrados, normalizeAccId, type ProjetoRegistrado } from "@/lib/projectsRegistry";

// Interface flexível
interface Project {
    id: string;
    name?: string;
    attributes?: {
        name: string;
    };
    relationships?: {
        rootFolder?: {
            data?: {
                id: string;
            }
        }
    };
    rootFolderId?: string;
}

const CardSkeleton = () => (
    <div className="bg-card/80 border-border rounded-lg p-4 animate-pulse">
        <div className="h-6 bg-muted rounded w-3/4 mb-2"></div>
        <div className="h-4 bg-muted rounded w-1/2"></div>
        <div className="mt-4 h-10 bg-secondary rounded"></div>
    </div>
);

export default function Projects() {
  const navigate = useNavigate();
  const { hubId } = useParams<{ hubId: string }>();

  const [allProjects, setAllProjects] = useState<Project[]>([]);
  const [registro, setRegistro] = useState<Map<string, ProjetoRegistrado>>(new Map());
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);
  const PROJECTS_PER_PAGE = 12;
  const [isModalOpen, setIsModalOpen] = useState(false);

  useEffect(() => {
    // Registro N&A (dbo.Projetos) para respeitar Is_Ativo — fail-open explícito (ver Dashboard.tsx).
    getProjetosRegistrados().then(setRegistro).catch(() => setRegistro(new Map()));
  }, []);

  // 1. BUSCA DE PROJETOS
  const fetchProjects = useCallback(async () => {
    if (!hubId) return;
    setLoading(true);

    try {
        const response = await fetch(`/DataManagement/Data/Projetos?hubId=${hubId}`);

        if (response.ok) {
            const text = await response.text();

            // ✅ CORREÇÃO: Removido ': any' para não travar o ESLint
            let json = [];
            if (text && text.trim().length > 0) {
                try {
                    json = JSON.parse(text);
                } catch (e) {
                    console.error("Erro parse:", e);
                }
            }

            // Blindagem
            let lista: Project[] = [];
            if (Array.isArray(json)) {
                lista = json;
            } else if (json && (json as any).data && Array.isArray((json as any).data)) {
                // (json as any) é seguro aqui se o ESLint permitir, senão removemos o cast
                lista = (json as any).data;
            }

            setAllProjects(lista);
        } else if (response.status === 401) {
            navigate("/login");
        } else {
            toast.error("Erro ao carregar projetos.");
        }
    } catch (error) {
        // ✅ CORREÇÃO: Removido ': any'
        console.error("Erro fetch:", error);
        toast.error("Falha de conexão.");
    } finally {
        setLoading(false);
    }
  }, [hubId, navigate]);

  useEffect(() => {
    fetchProjects();
  }, [fetchProjects]);

  // 2. HELPER DE NOME
  const getProjectName = (p: Project) => {
      return p.attributes?.name || p.name || "Sem Nome";
  };

  // Respeita Is_Ativo de dbo.Projetos quando o projeto já está registrado no N&A (mesma regra e mesma
  // ressalva de Dashboard.tsx — ver AUDITORIA_FORGESYNC_NA/15-*.md §3).
  const filteredProjects = useMemo(() => {
    return allProjects
      .filter(p => registro.get(normalizeAccId(p.id))?.isAtivo !== false)
      .filter(p => getProjectName(p).toLowerCase().includes(searchTerm.toLowerCase()));
  }, [allProjects, searchTerm, registro]);

  const totalPages = Math.ceil(filteredProjects.length / PROJECTS_PER_PAGE);

  const paginatedProjects = useMemo(() => {
      const startIndex = (currentPage - 1) * PROJECTS_PER_PAGE;
      return filteredProjects.slice(startIndex, startIndex + PROJECTS_PER_PAGE);
  }, [filteredProjects, currentPage]);

  const getPageNumbers = () => {
    const pageNumbers = [];
    const maxPagesToShow = 5;
    if (totalPages <= maxPagesToShow) {
        for (let i = 1; i <= totalPages; i++) pageNumbers.push(i);
    } else {
        let startPage = Math.max(1, currentPage - 2);
        let endPage = Math.min(totalPages, currentPage + 2);
        if (currentPage < 3) endPage = maxPagesToShow;
        if (currentPage > totalPages - 2) startPage = totalPages - maxPagesToShow + 1;
        if (startPage > 1) pageNumbers.push(1, '...');
        for (let i = startPage; i <= endPage; i++) pageNumbers.push(i);
        if (endPage < totalPages) pageNumbers.push('...', totalPages);
    }
    return pageNumbers;
  };

  // 3. ABRIR PROJETO
  const handleOpenProject = async (project: Project) => {
      const projectName = getProjectName(project);
      const loadingToast = toast.loading(`A abrir ${projectName}...`);

      try {
          let rootFolderId = project.relationships?.rootFolder?.data?.id || project.rootFolderId;

          if (!rootFolderId) {
              const res = await fetch(
                  `/DataManagement/Data/GetProjectRootFolder?hubId=${hubId}&projectId=${project.id}`
              );

              if (res.ok) {
                  const text = await res.text();
                  if (text) {
                      const data = JSON.parse(text);
                      rootFolderId = data.rootFolderId;
                  }
              }
          }

          if (!rootFolderId) {
              throw new Error('Pasta raiz não encontrada.');
          }

          navigate(
              `/projetos/${project.id}/conteudo/${rootFolderId}`,
              {
                state: {
                  projectName: projectName,
                  hubId: hubId
                }
              }
          );
          toast.dismiss(loadingToast);

      } catch (error) {
          // ✅ CORREÇÃO: Removido ': any'
          console.error("Erro ao abrir:", error);

          let msg = "Erro desconhecido";
          if (error instanceof Error) msg = error.message;

          toast.error(msg, { id: loadingToast });
      }
  };

  return (
    <AnimatedBackground>
      <CreateProjectModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onProjectCreated={fetchProjects}
        hubId={hubId}
      />

      <div className="min-h-screen p-6">
        <header className="flex items-center justify-between mb-8">
          <ThemeLogo />
          <nav className="flex items-center gap-4">
            <Button variant="ghost" className="text-foreground hover:text-primary" onClick={() => navigate('/dashboard')}>Contas</Button>
            <Button variant="ghost" className="text-primary">Projetos</Button>
            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">Sair</a>
          </nav>
        </header>

        <div className="max-w-7xl mx-auto">
          <nav className="flex items-center gap-2 text-sm text-muted-foreground mb-6">
            <Link to="/dashboard" className="hover:text-primary">Contas</Link>
            <span>/</span>
            <span className="text-foreground">Projetos</span>
          </nav>

          <div className="flex flex-col md:flex-row items-start md:items-center justify-between mb-8 gap-4">
            <div>
              <h1 className="text-3xl font-bold text-foreground mb-2">Projetos</h1>
              <p className="text-muted-foreground">Selecione um projeto para ver os seus ficheiros.</p>
            </div>
            <div className="flex items-center gap-4 w-full md:w-auto">
                <Button onClick={() => setIsModalOpen(true)}>Criar Projeto</Button>
                <div className="relative w-full md:w-80">
                  <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-muted-foreground w-4 h-4" />
                  <Input
                    id="project-search"
                    placeholder="Pesquisar projetos..."
                    className="pl-10 bg-card border-border"
                    value={searchTerm}
                    onChange={e => { setSearchTerm(e.target.value); setCurrentPage(1); }}
                  />
                </div>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
            {loading ? (
                Array.from({ length: 12 }).map((_, i) => <CardSkeleton key={i} />)
            ) : paginatedProjects.length > 0 ? paginatedProjects.map((project) => (
              <ElegantCard
                key={project.id}
                title={getProjectName(project)}
                buttonText="Abrir Projeto"
                onButtonClick={() => handleOpenProject(project)}
              >
                {!registro.has(normalizeAccId(project.id)) && (
                  <span title="Este projeto existe na Autodesk mas ainda não foi processado pelo robô do N&A." className="inline-block mb-2 text-[9px] font-black uppercase tracking-widest text-amber-500/80 border border-amber-500/30 rounded px-1.5 py-0.5">
                    Não registrado
                  </span>
                )}
              </ElegantCard>
            )) : (
                <p className="text-muted-foreground col-span-full text-center py-10">Nenhum projeto encontrado.</p>
            )}
          </div>

          {!loading && totalPages > 1 && (
            <div className="flex justify-center items-center mt-12 space-x-2">
                <Button onClick={() => setCurrentPage(p => Math.max(1, p - 1))} disabled={currentPage === 1} variant="ghost" size="sm">Anterior</Button>
                {getPageNumbers().map((page, index) =>
                    typeof page === 'number' ? (
                        <Button key={index} onClick={() => setCurrentPage(page)} variant={currentPage === page ? "default" : "outline"} size="sm">{page}</Button>
                    ) : (<span key={index} className="px-2 text-muted-foreground">...</span>)
                )}
                <Button onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))} disabled={currentPage === totalPages} variant="ghost" size="sm">Próxima</Button>
            </div>
          )}
        </div>
      </div>
    </AnimatedBackground>
  );
}
