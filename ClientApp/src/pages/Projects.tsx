// Caminho: ClientApp/src/pages/Projects.tsx (VERSÃO COMPLETA E CORRIGIDA)

import { useState, useEffect, useMemo, useCallback } from "react";
import { AnimatedBackground } from "@/components/ui/animated-background";
import { Logo } from "@/components/ui/logo";
import { ElegantCard } from "@/components/ui/elegant-card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Search } from "lucide-react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { CreateProjectModal } from "@/components/CreateProjectModal";
import toast from 'react-hot-toast';

interface Project {
    id: string;
    attributes: { name: string; };
    relationships?: { 
        rootFolder?: { 
            data?: { 
                id: string; 
            } 
        } 
    }
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
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);
  const PROJECTS_PER_PAGE = 12;
  const [isModalOpen, setIsModalOpen] = useState(false);

  const fetchProjects = useCallback(async () => {
    if (!hubId) return;
    setLoading(true);
    
    const response = await fetch(`/DataManagement/Data/Projetos?hubId=${hubId}`);
    
    if (response.ok) {
        const data = await response.json();
        setAllProjects(data.data || []);
    } else if (response.status === 401) {
        navigate("/login");
    }
    setLoading(false);
  }, [hubId, navigate]);

  useEffect(() => {
    fetchProjects();
  }, [fetchProjects]);

  const filteredProjects = useMemo(() => {
    return allProjects.filter(p => p.attributes?.name?.toLowerCase().includes(searchTerm.toLowerCase()));
  }, [allProjects, searchTerm]);

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
  
  // Caminho: ClientApp/src/pages/Projects.tsx
// SUBSTITUA A FUNÇÃO handleOpenProject POR ESTA VERSÃO COMPLETA E CORRIGIDA:

  const handleOpenProject = async (project: Project) => {
      // 1. Mostrar feedback visual
      const loadingToast = toast.loading(`A abrir ${project.attributes.name}...`);

      try {
          // 2. Chamar o backend para buscar a pasta "Project Files"
          const rootFolderResponse = await fetch(
              `/DataManagement/Data/GetProjectRootFolder?hubId=${hubId}&projectId=${project.id}`
          );

          if (!rootFolderResponse.ok) {
              const errorData = await rootFolderResponse.json();
              throw new Error(errorData.message || 'Não foi possível encontrar a pasta "Project Files".');
          }

          const { rootFolderId } = await rootFolderResponse.json();

          if (!rootFolderId) {
              throw new Error('O ID da pasta "Project Files" não foi retornado pela API.');
          }

          // 3. Navegar para a página de ficheiros, **incluindo o state**
          navigate(
              `/projetos/${project.id}/conteudo/${rootFolderId}?hubId=${hubId}`,
              { // <-- OBJETO DE OPÇÕES DA NAVEGAÇÃO
                state: { // <-- PASSA O NOME DO PROJETO AQUI
                  projectName: project.attributes.name // Pega o nome do objeto 'project'
                }
              }
          );

          toast.dismiss(loadingToast); // Remove o toast de carregamento

      } catch (error: any) {
          console.error("Erro ao abrir projeto:", error);
          toast.error(`Erro ao abrir projeto: ${error.message}`, { id: loadingToast });
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
          <Logo />
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
                title={project.attributes.name}
                buttonText="Abrir Projeto"
                onButtonClick={() => handleOpenProject(project)}
              />
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