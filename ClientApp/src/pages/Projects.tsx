// Caminho: ClientApp/src/pages/Projects.tsx

import { useState, useEffect, useMemo, useCallback } from "react";
import { UploadDocumentModal } from "@/components/UploadDocumentoModal";
import React from "react";
import { useNavigate, useParams, Link } from "react-router-dom";
import toast from "react-hot-toast";
import { Search, Upload } from "lucide-react";

// Componentes visuais existentes
import { AnimatedBackground } from "@/components/ui/animated-background";
import { Logo } from "@/components/ui/logo";
import { ElegantCard } from "@/components/ui/elegant-card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { CreateProjectModal } from "@/components/CreateProjectModal";
import { ModeToggle } from "@/components/alternar-modo";
import { useGlobalFileDrop } from "@/components/hooks/useGlobalFileDrop";

// Componentes da Sidebar e Select do Shadcn
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/Select";
import { useCurrentUser } from "@/components/hooks/use-current-user";


// Interface para os Projetos
interface Project {
  id: string;
  attributes: { name: string };
  relationships?: {
    rootFolder?: {
      data?: {
        id: string;
      };
    };
  };
}

// Interface para os Hubs (Contas)
interface Hub {
  id: string;
  attributes: {
    name: string;
    region?: string;
  };
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
  const { user, loading: loadingUser } = useCurrentUser();

  // Estados dos Projetos e Interface
  const [allProjects, setAllProjects] = useState<Project[]>([]);
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);
  const PROJECTS_PER_PAGE = 12;
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedProjectForUpload, setSelectedProjectForUpload] = useState<Project | null>(null);
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false);

  // Estados para os Hubs (Contas)
  const [hubs, setHubs] = useState<Hub[]>([]);
  const [loadingHubs, setLoadingHubs] = useState(false);

  const handleOpenUploadModal = (project: Project) => {
    setSelectedProjectForUpload(project);
    setIsUploadModalOpen(true);
  }

  // 1. Procurar a lista de Hubs utilizando a mesma API do Hubs.tsx
// 1. Procurar a lista de Hubs utilizando a mesma API do Hubs.tsx
  useEffect(() => {
    const fetchHubs = async () => {
      setLoadingHubs(true);
      try {
        const response = await fetch("/DataManagement/Data/Hubs");
        if (response.ok) {
          const data = await response.json();
          const hubsList: Hub[] = data.data || [];
          setHubs(hubsList);

          // 🔴 Se não veio hubId na URL, redireciona para o primeiro hub disponível
          if (!hubId && hubsList.length > 0) {
            navigate(`/projects/${hubsList[0].id}`, { replace: true });
          }
        }
      } catch (error) {
        console.error("Erro ao carregar os hubs no Select:", error);
      } finally {
        setLoadingHubs(false);
      }
    };

    fetchHubs();
  }, [hubId, navigate]);

  // 2. Procurar os Projetos do Hub atual
  const fetchProjects = useCallback(async () => {
    if (!hubId) return;
    setLoading(true);

    try {
      const response = await fetch(`/DataManagement/Data/Projects?hubId=${hubId}`);

      if (response.ok) {
        const data = await response.json();
        setAllProjects(data.data || []);
      } else if (response.status === 401) {
        navigate("/login");
      }
    } catch (error) {
      console.error("Erro ao carregar projetos:", error);
    } finally {
      setLoading(false);
    }
  }, [hubId, navigate]);

  useEffect(() => {
    fetchProjects();
  }, [fetchProjects]);


  // Navegação direta forçando a atualização do ID da conta
  const handleSelectHub = (newHubId: string) => {
    if (newHubId && newHubId !== hubId) {
      // 1. Substitui o hubId antigo pelo novo na URL atual
      const currentPath = window.location.pathname;
      const newPath = currentPath.replace(hubId!, newHubId);

      // 2. Se o ID estiver na rota /contas/ID/projetos
      if (currentPath.includes("/contas/")) {
        navigate(`/contas/${encodeURIComponent(newHubId)}/projetos`);
      } else {
        // Redirecionamento direto fallback
        window.location.href = newPath;
      }
    }
  };

  // Filtro de Projetos por termo de pesquisa
  const filteredProjects = useMemo(() => {
    return allProjects.filter((p) =>
      p.attributes?.name?.toLowerCase().includes(searchTerm.toLowerCase())
    );
  }, [allProjects, searchTerm]);

  const [isRoboLoading, setIsRoboLoading] = useState(false);

const handleTriggerRobot = async () => {
  setIsRoboLoading(true);
  const tid = toast.loading("🤖 Iniciando robô...");

  try {
    const res = await fetch(`/DataManagement/IniciarRobo`, {
      method: "POST",
    });

    if (res.status === 202) {
      toast.success("✅ Robô iniciado! Rodando em background.", { id: tid });
    } else if (res.status === 409) {
      toast.error("⚠️ O robô já está em execução.", { id: tid });
    } else {
      const data = await res.json().catch(() => null);
      toast.error(data?.message ?? "❌ Falha ao iniciar o Robô", { id: tid });
    }
  } catch (error) {
    toast.error("Erro crítico de conexão", { id: tid });
  } finally {
    setIsRoboLoading(false);
  }
};

  // Cálculo da Paginação
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
      if (startPage > 1) pageNumbers.push(1, "...");
      for (let i = startPage; i <= endPage; i++) pageNumbers.push(i);
      if (endPage < totalPages) pageNumbers.push("...", totalPages);
    }
    return pageNumbers;
  };

  const handleOpenProject = async (project: Project) => {
    const loadingToast = toast.loading(`A abrir ${project.attributes.name}...`);

    try {
      const rootFolderResponse = await fetch(
        `/DataManagement/Data/GetProjectRootFolder?hubId=${hubId}&projectId=${project.id}`
      );

      if (!rootFolderResponse.ok) {
        const errorData = await rootFolderResponse.json();
        throw new Error(
          errorData.message || 'Não foi possível encontrar a pasta "Project Files".'
        );
      }

      const { rootFolderId } = await rootFolderResponse.json();

      if (!rootFolderId) {
        throw new Error('O ID da pasta "Project Files" não foi retornado pela API.');
      }

      navigate(
        `/projetos/${project.id}/conteudo/${rootFolderId}?hubId=${hubId}`,
        {
          state: {
            projectName: project.attributes.name,
          },
        }
      );

      toast.dismiss(loadingToast);
    } catch (error: any) {
      console.error("Erro ao abrir projeto:", error);
      toast.error(`Erro ao abrir projeto: ${error.message}`, { id: loadingToast });
    }
  };

  return (
    <SidebarProvider>
      {/* 1. Menu Lateral */}
      <AppSidebar />

      {/* 2. Conteúdo da Página */}
      <SidebarInset>
        <AnimatedBackground>
          <CreateProjectModal
            isOpen={isModalOpen}
            onClose={() => setIsModalOpen(false)}
            onProjectCreated={fetchProjects}
            hubId={hubId}
          />

         
       
            <div className="min-h-screen p-8">
            {/* Header */}
            <header className="flex items-center justify-between  ">
              <div className="flex items-center gap-2">
                <SidebarTrigger className="-ml-7"/>

                <ModeToggle/>
                <a href="/Authentication/OAuth/Logout" className=" text-muted-foreground hover:text-primary">
                </a>
                
              </div>

            </header>

            <div className="max-w-7xl mx-auto">
             

              <div className="flex flex-col md:flex-row items-start md:items-center justify-between mb-8 gap-4">
                <div>
   {loadingUser ? (
     <span className="text-xs text-muted-foreground">Carregando...</span>
   ) : user ? (
     <>
       <span className="text-3xl truncate font-semibold">Olá, {user.name}</span>
       
     </>
   ) : null}
                  

                  {/* SELECT PREENCHIDO COM OS HUBS */}
                  <Select value={hubId || ""} onValueChange={handleSelectHub}>
                    <SelectTrigger className="w-[240px]">
                      <SelectValue
                        placeholder={
                          loadingHubs ? "A carregar contas..." : "Selecione uma conta"
                        }
                      />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        {hubs.map((hub) => (
                          <SelectItem key={hub.id} value={hub.id}>
                            {hub.attributes.name}
                          </SelectItem>
                        ))}
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </div>

                <div className="flex items-center gap-4 w-full md:w-auto mb-10">
                  <Button onClick={handleTriggerRobot} disabled={isRoboLoading}>
  {isRoboLoading ? "Iniciando..." : "Iniciar Robô"}
</Button>
                  <Button onClick={() => setIsModalOpen(true)}>Criar Projeto</Button>
                  <div className="relative w-full md:w-80">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-muted-foreground w-4 h-4" />
                    <Input
                      id="project-search"
                      placeholder="Pesquisar projetos..."
                      className="pl-10 bg-card border-border"
                      value={searchTerm}
                      onChange={(e) => {
                        setSearchTerm(e.target.value);
                        setCurrentPage(1);
                      }}
                    />
                  </div>
                </div>
              </div>

              {/* Grid de Cards de Projetos */}
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
                {loading ? (
                  Array.from({ length: 12 }).map((_, i) => <CardSkeleton key={i} />)
                ) : paginatedProjects.length > 0 ? (
                  paginatedProjects.map((project) => (
                    <ElegantCard
                      key={project.id}
                      title={project.attributes.name}
                      buttonText="Abrir Projeto"
                      onButtonClick={() => handleOpenProject(project)}
                    />
                  ))
                ) : (
                  <p className="text-muted-foreground col-span-full text-center py-10">
                    Nenhum projeto encontrado.
                  </p>
                )}
              </div>

              {/* Paginação */}
              {!loading && totalPages > 1 && (
                <div className="flex justify-center items-center mt-12 space-x-2">
                  <Button
                    onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                    disabled={currentPage === 1}
                    variant="ghost"
                    size="sm"
                  >
                    Anterior
                  </Button>
                  {getPageNumbers().map((page, index) =>
                    typeof page === "number" ? (
                      <Button
                        key={index}
                        onClick={() => setCurrentPage(page)}
                        variant={currentPage === page ? "default" : "outline"}
                        size="sm"
                      >
                        {page}
                      </Button>
                    ) : (
                      <span key={index} className="px-2 text-muted-foreground">
                        ...
                      </span>
                    )
                  )}
                  <Button
                    onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                    disabled={currentPage === totalPages}
                    variant="ghost"
                    size="sm"
                  >
                    Próxima
                  </Button>
                </div>
              )}
            </div>
          </div>
        </AnimatedBackground>
      </SidebarInset>
    </SidebarProvider>
  );
}