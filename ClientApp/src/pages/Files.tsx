// Caminho: ClientApp/src/pages/Files.tsx
// VERSÃO FINAL COMPLETA - Inclui interface atualizada e passo de depuração

import { useEffect, useState, useCallback, useMemo } from "react";
import { useParams, useNavigate, useSearchParams, useLocation } from "react-router-dom";
import toast from 'react-hot-toast';
import { AnimatedBackground } from "@/components/ui/animated-background";
import { Logo } from "@/components/ui/logo";
import { ElegantTable, TableRowData } from "@/components/ui/elegant-table";
import { Button } from "@/components/ui/button";
import { VersionHistoryModal } from "@/components/VersionHistoryModal";
import { SyncResultModal } from "@/components/SyncResultModal";
import { CreatePlaceholdersModal } from "@/components/CreatePlaceholdersModal";
import { PlaceholderResultModal } from "@/components/PlaceholderResultModal";
import { MissingDocsDbModal } from "@/components/MissingDocsDbModal";
// 👇 IMPORT NOVO: Modal e Interface de Auditoria
import { AuditResultModal, AuditResult } from "@/components/AuditResultModal";

import { FilePlus2, SearchCheck } from "lucide-react"; // 👇 Adicionei SearchCheck
import { Breadcrumbs } from "@/components/ui/Breadcrumbs";

// --- Interfaces de Dados ---
interface ApiDataItem {
    id: string; type: 'folders' | 'items';
    attributes: { displayName: string; lastModifiedTime: string; lastModifiedUserName: string; };
    relationships?: { tip?: { data?: { id: string; } } }
}
interface ApiVersionItem {
    urn: string; id: string; type: 'versions';
    customAttributes?: { id: number; name: string; type: string; value: any }[];
    relationships?: { storage?: { data?: { id: string; } } }
}
interface Breadcrumb { id: string; name: string; }
interface SyncResult { totalLdpFiles: number; filesUpdated: number; filesNotFound: string[]; filesUpdatedSuccess: string[]; allLdpFiles: string[]; summaryMessage: string; }

// --- Interface PlaceholderResult ATUALIZADA ---
interface PlaceholderResult {
    totalRowsInLdp: number;
    rowsProcessed: number;
    placeholdersCreated: number;
    filesSkippedExisting: number;
    filesSkippedFiltered: number;
    createdFiles: string[];
    skippedExistingFiles: string[];
    skippedFilteredInfo: string[];
    errors: string[];
    summaryMessage: string;
}
// --- FIM Interface PlaceholderResult ---

export default function Files() {
    const navigate = useNavigate();
    const { projectId, folderId } = useParams<{ projectId: string; folderId: string }>();
    const [searchParams] = useSearchParams();
    const hubId = searchParams.get('hubId');
    const location = useLocation();
    // console.log("Location State:", location.state);
    const projectName = location.state?.projectName || "Projeto";

    // --- Estados do Componente ---
    const [content, setContent] = useState<TableRowData[]>([]);
    const [folderPath, setFolderPath] = useState<Breadcrumb[]>([]);
    const [loading, setLoading] = useState(true);
    const [isVersionModalOpen, setIsVersionModalOpen] = useState(false);
    const [selectedItem, setSelectedItem] = useState<{ id: string, name: string } | null>(null);
    
    // States Sync
    const [isSyncing, setIsSyncing] = useState(false);
    const [isSyncResultModalOpen, setIsSyncResultModalOpen] = useState(false);
    const [syncResult, setSyncResult] = useState<SyncResult | null>(null);
    
    // States Placeholders
    const [isPlaceholderModalOpen, setIsPlaceholderModalOpen] = useState(false);
    const [isPlaceholderResultModalOpen, setIsPlaceholderResultModalOpen] = useState(false);
    const [placeholderResult, setPlaceholderResult] = useState<PlaceholderResult | null>(null);
    
    // States DB Update
    const [isUpdatingFromDb, setIsUpdatingFromDb] = useState(false);
    const [missingDocsFromDb, setMissingDocsFromDb] = useState<string[]>([]);
    const [isMissingDocsModalOpen, setIsMissingDocsModalOpen] = useState(false);

    // 👇 NOVOS STATES PARA AUDITORIA
    const [isAuditing, setIsAuditing] = useState(false);
    const [auditResult, setAuditResult] = useState<AuditResult | null>(null);
    const [isAuditModalOpen, setIsAuditModalOpen] = useState(false);

    // --- Busca de Dados ---
    const fetchData = useCallback(async () => {
        if (!projectId || !folderId) return;
        setLoading(true);
        setFolderPath([]); 
        // console.log("fetchData called"); 
        try {
            const [contentResponse, pathResponse] = await Promise.all([
                fetch(`/DataManagement/Data/ConteudoPasta?projectId=${projectId}&folderId=${folderId}`),
                fetch(`/DataManagement/Data/GetFolderPath?projectId=${projectId}&folderId=${folderId}`)
            ]);
            
            if (contentResponse.status === 401 || pathResponse.status === 401) {
                navigate('/login');
                return;
            }

            if (contentResponse.ok) {
                const apiResponse = await contentResponse.json();
                const itemsAndFolders: ApiDataItem[] = apiResponse.data || [];
                const versions: ApiVersionItem[] = apiResponse.included || [];
                const formattedData: TableRowData[] = itemsAndFolders.map((item) => {
                    let storageUrn: string | undefined = undefined; let customAttributes: Record<string, any> = {};
                    if (item.type === 'items') {
                        const tipVersionId = item.relationships?.tip?.data?.id;
                        if (tipVersionId) {
                            const versionData = versions.find(v => v.urn === tipVersionId);
                            storageUrn = versionData?.relationships?.storage?.data?.id;
                            versionData?.customAttributes?.forEach(attr => { customAttributes[attr.name] = attr.value; });
                        }
                    }
                    return { id: item.id, type: item.type === 'folders' ? 'folder' : 'item', name: item.attributes.displayName, lastModified: `${new Date(item.attributes.lastModifiedTime).toLocaleString('pt-BR')} por ${item.attributes.lastModifiedUserName}`, storageUrn: storageUrn, customAttributes: customAttributes };
                });
                formattedData.sort((a, b) => { if (a.type === 'folder' && b.type !== 'folder') return -1; if (a.type !== 'folder' && b.type === 'folder') return 1; return a.name.localeCompare(b.name); });
                setContent(formattedData);
            } else {
                toast.error("Falha ao carregar conteúdo.");
            }

            if (pathResponse.ok) {
                const pathData = await pathResponse.json();
                setFolderPath(pathData);
            } else {
                setFolderPath([]);
            }

        } catch (error) {
            console.error("fetchData general error:", error);
            toast.error("Erro ao carregar dados.");
            setFolderPath([]);
        } finally {
            setLoading(false);
        }
    }, [projectId, folderId, navigate]);

    useEffect(() => {
        fetchData();
    }, [fetchData]);

    // --- Handlers ---

    const handlePlaceholdersCreated = (result: PlaceholderResult) => {
        setPlaceholderResult(result);
        setIsPlaceholderResultModalOpen(true);
    };

    const handleSyncClick = async () => {
        if (!hubId || !projectId) {
            toast.error("Não foi possível identificar o Hub ou o Projeto atual."); return;
        }
        setIsSyncing(true);
        const loadingToast = toast.loading("A iniciar sincronização...");
        try {
            // AQUI ESTÁ A CHAMADA PARA O SEU BACKEND
            const response = await fetch(`/DataManagement/Data/SyncFromProjectLdp?hubId=${hubId}&projectId=${projectId}`, { method: 'POST' });
            
            const result = await response.json();
            if (!response.ok) { throw new Error(result.details || result.message || "Erro desconhecido na sincronização."); }
            toast.success(result.summaryMessage, { id: loadingToast, duration: 6000 });
            setSyncResult(result);
            setIsSyncResultModalOpen(true);
            fetchData();
        } catch (error: any) {
            toast.error(`Erro na sincronização: ${error.message}`, { id: loadingToast });
        } finally {
            setIsSyncing(false);
        }
    };

    const handleUpdateFromDbClick = async () => {
        if (!hubId || !projectId) {
            toast.error("Não foi possível identificar o Hub ou o Projeto atual."); return;
        }
        setIsUpdatingFromDb(true);
        setMissingDocsFromDb([]);
        setIsMissingDocsModalOpen(false);
        const loadingToast = toast.loading("A ler banco de dados e atualizar LDP...");
        try {
            const response = await fetch(`/DataManagement/Data/UpdateLdpFromDatabase?hubId=${hubId}&projectId=${projectId}`, { method: 'POST' });
            const result = await response.json();

            if (!response.ok) {
                throw new Error(result.summaryMessage || result.details || result.message || "Erro desconhecido na atualização.");
            }

            toast.success(result.summaryMessage, { id: loadingToast, duration: 6000 });

            if (result.documentosNaoEncontradosNoDb && result.documentosNaoEncontradosNoDb.length > 0) {
                setMissingDocsFromDb(result.documentosNaoEncontradosNoDb);
                setIsMissingDocsModalOpen(true);
            }
            fetchData();
        } catch (error: any) {
            toast.error(`Erro na atualização: ${error.message}`, { id: loadingToast });
            setMissingDocsFromDb([]);
            setIsMissingDocsModalOpen(false);
        } finally {
            setIsUpdatingFromDb(false);
        }
    };

    const handleInstallWebhook = async () => {
        const ngrokUrl = prompt("Por favor, insira a sua URL base do ngrok (ex: https://abcd-1234.ngrok-free.app):");
        if (!ngrokUrl || !hubId || !projectId) {
            toast.error("A instalação do Webhook foi cancelada ou faltam dados (Hub/Projeto)."); return;
        }
        const loadingToast = toast.loading("A instalar o Webhook na Autodesk...");
        try {
            const response = await fetch(`/DataManagement/Data/InstallWebhookForLdp?hubId=${hubId}&projectId=${projectId}`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ NgrokUrl: ngrokUrl })
            });
            const result = await response.json();
            if (!response.ok) { throw new Error(result.details || result.message); }
            toast.success(result.message, { id: loadingToast, duration: 6000 });
        } catch (error: any) {
            toast.error(`Falha na instalação: ${error.message}`, { id: loadingToast });
        }
    };

    // 👇 NOVO HANDLER DE AUDITORIA
    const handleAuditClick = async () => {
        if (!hubId || !projectId) {
            toast.error("Hub ou Projeto não identificado.");
            return;
        }

        setIsAuditing(true);
        const loadingToast = toast.loading("A comparar arquivos ACC vs LDP...");

        try {
            const response = await fetch(`/DataManagement/Data/CheckAuditFilesAccVsLdp?hubId=${hubId}&projectId=${projectId}`, {
                method: 'POST'
            });
            
            const data = await response.json();

            if (!response.ok) {
                throw new Error(data.message || "Erro desconhecido na auditoria.");
            }

            // Define o resultado e abre o modal
            setAuditResult(data);
            setIsAuditModalOpen(true);
            
            // Mensagem simples no toast, o detalhe vai no modal
            if (data.count === 0) {
                toast.success("Auditoria Concluída: Tudo OK!", { id: loadingToast });
            } else {
                toast.error(`Atenção: ${data.count} arquivos não listados na LDP.`, { id: loadingToast });
            }

        } catch (error: any) {
            console.error(error);
            toast.error(`Erro na auditoria: ${error.message}`, { id: loadingToast });
        } finally {
            setIsAuditing(false);
        }
    };

    const handleVersionsClick = (itemId: string, itemName: string) => {
        setSelectedItem({ id: itemId, name: itemName });
        setIsVersionModalOpen(true);
    };

    const handleFolderClick = (newFolderId: string) => {
        navigate(
            `/projetos/${projectId}/conteudo/${newFolderId}?hubId=${hubId}`,
            { state: { projectName: projectName } }
        );
    };

    const handleDownloadClick = async (storageUrn: string) => {
        if (!storageUrn) {
            toast.error("URN de armazenamento não encontrado."); return;
        }
        const loadingToast = toast.loading("A obter link de download...");
        try {
            const response = await fetch(`/DataManagement/Data/DownloadArquivo?storageUrn=${storageUrn}`);
            if (response.ok) {
                const data = await response.json();
                toast.dismiss(loadingToast);
                window.open(data.downloadUrl, '_blank');
            } else {
                 const errorData = await response.text();
                throw new Error(errorData || "Falha ao obter o link de download.");
            }
        } catch(error: any) {
             toast.error(`Erro no download: ${error.message}`, { id: loadingToast });
        }
    };

    // --- Preparação para Renderização ---
    const displayPath = useMemo(() => {
        if (folderPath.length > 0) {
            const firstItem = folderPath[0];
            if (firstItem.name.includes('-') && firstItem.name.toLowerCase() !== 'project files') {
                const newPath = [...folderPath];
                newPath[0] = { ...firstItem, name: 'Project Files' };
                return newPath;
            }
        }
        return folderPath;
    }, [folderPath]);

    const currentFolderName = displayPath.length > 0 ? displayPath[displayPath.length - 1].name : (loading ? "A carregar..." : "Ficheiros");

    // --- JSX ---
    return (
        <AnimatedBackground>
            {/* ========================================================= */}
            {/* === Modais Renderizados Condicionalmente === */}
            {/* ========================================================= */}
            {isVersionModalOpen && selectedItem && projectId && (
                <VersionHistoryModal
                    projectId={projectId}
                    itemId={selectedItem.id}
                    itemName={selectedItem.name}
                    onClose={() => setIsVersionModalOpen(false)}
                />
            )}

            <SyncResultModal
                isOpen={isSyncResultModalOpen}
                onClose={() => setIsSyncResultModalOpen(false)}
                result={syncResult}
            />

            <CreatePlaceholdersModal
                isOpen={isPlaceholderModalOpen}
                onClose={() => setIsPlaceholderModalOpen(false)}
                onPlaceholdersCreated={handlePlaceholdersCreated}
                projectId={projectId}
                hubId={hubId ?? undefined}
            />

            <PlaceholderResultModal
                isOpen={isPlaceholderResultModalOpen}
                onClose={() => setIsPlaceholderResultModalOpen(false)}
                result={placeholderResult}
            />

            <MissingDocsDbModal
                isOpen={isMissingDocsModalOpen}
                onClose={() => setIsMissingDocsModalOpen(false)}
                documents={missingDocsFromDb}
            />

            {/* 👇 NOVO MODAL DE AUDITORIA */}
            <AuditResultModal 
                isOpen={isAuditModalOpen} 
                onClose={() => setIsAuditModalOpen(false)} 
                result={auditResult} 
            />
            {/* ========================================================= */}
            {/* === Fim dos Modais === */}
            {/* ========================================================= */}

            <div className="min-h-screen p-6">
                <header className="flex items-start justify-between mb-8">
                    <Logo />
                    <div className="flex-1 text-center px-4">
                        {projectName && (
                            <h2 className="text-lg font-semibold text-foreground truncate leading-tight">
                                {projectName}
                            </h2>
                        )}
                    </div>
                    <nav className="flex items-center gap-2">
                        
                        <Button  size="sm" className="" onClick={() => hubId && navigate(`/projects/${hubId}`)}>Voltar</Button>
                        <a href="/Authentication/OAuth/Logout" className="text-xs text-muted-foreground hover:text-primary px-2">Sair</a>
                    </nav>
                </header>

                <div className="w-full max-w-screen-2xl mx-auto px-6">
                    <Breadcrumbs
                        path={[
                            { id: 'dashboard', name: 'Contas' },
                            { id: hubId ? `projects/${hubId}` : 'projects', name: 'Projetos' },
                            ...displayPath
                        ]}
                        onPathClick={(itemId, index) => {
                            if (index === 0) navigate('/dashboard');
                            else if (index === 1 && hubId) navigate(`/projects/${hubId}`);
                            else if (index > 1 && projectId && hubId) navigate(`/projetos/${projectId}/conteudo/${itemId}?hubId=${hubId}`, { state: { projectName: projectName } });
                        }}
                        className="mb-6"
                    />

                    <div className="flex items-center justify-between mb-8">
                        <h1 className="text-3xl font-bold text-foreground">{currentFolderName}</h1>
                        <div className="flex gap-2 flex-wrap justify-end">
                            <Button onClick={() => setIsPlaceholderModalOpen(true)} variant="outline">
                                <FilePlus2 className="mr-2 h-4 w-4" /> Criar Placeholders
                            </Button>

                            {/* 👇 NOVO BOTÃO DE AUDITORIA */}
                            <Button 
                                onClick={handleAuditClick} 
                                disabled={isAuditing} 
                                variant="secondary" 
                                className="bg-yellow-600/20 text-yellow-500 hover:bg-yellow-600/30 border border-yellow-600/50"
                            >
                                <SearchCheck className="mr-2 h-4 w-4" /> 
                                {isAuditing ? "Auditando..." : "Auditar (ACC vs LDP)"}
                            </Button>
                            
                            <Button onClick={handleSyncClick} disabled={isSyncing}>
                                {isSyncing ? "Sincronizando..." : "Sincronizar LDP"}
                            </Button>
                            <Button onClick={handleUpdateFromDbClick} disabled={isUpdatingFromDb} variant="secondary">
                                {isUpdatingFromDb ? "Atualizando..." : "Atualizar LDP do Banco"}
                            </Button>
                            <Button onClick={handleInstallWebhook} variant="outline" size="icon" title="Instalar Webhook">
                                <span className="sr-only">Webhook</span>
                                ⚙️
                            </Button>
                        </div>
                    </div>

                    {loading ? (
                        <p className="text-muted-foreground text-center py-10">A carregar conteúdo...</p>
                    ) : (
                        <ElegantTable
                            data={content}
                            onFolderClick={handleFolderClick}
                            onDownloadClick={handleDownloadClick}
                            onVersionsClick={handleVersionsClick}
                        />
                    )}
                </div>
            </div>
        </AnimatedBackground>
    );
}