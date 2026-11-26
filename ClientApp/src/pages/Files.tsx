import { useEffect, useState, useCallback, useMemo } from "react";
import { useParams, useNavigate, useSearchParams, useLocation } from "react-router-dom";
import toast from 'react-hot-toast';
import { ElegantTable, type TableRowData } from "@/components/ui/elegant-table";
import { VersionHistoryModal } from "@/components/VersionHistoryModal";
import { FolderOpen } from "lucide-react";
import { Breadcrumbs } from "@/components/ui/Breadcrumbs";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import EnvioNaPasta from "@/features/upload-governado/EnvioNaPasta";
import { caminhoDaPasta } from "@/features/upload-governado/uploadNaPasta";
import { useProtecaoPasta } from "@/features/upload-governado/useProtecaoPasta";
import { trilhaVisivel } from "@/pages/cde/pastasDoProjeto";

// Tipos e Interfaces
type AttributeValue = string | number | boolean | null | undefined;
interface LocationState { projectName?: string; hubId?: string; }
interface ApiDataItem {
  id: string; type: 'folders' | 'items';
  attributes: { displayName: string; lastModifiedTime: string; lastModifiedUserName: string; };
  relationships?: { tip?: { data?: { id: string; } } };
}
interface ApiVersionItem {
  urn: string; id: string; type: 'versions';
  attributes: { versionNumber: number };
  customAttributes?: { id: number; name: string; type: string; value: AttributeValue }[];
  relationships?: { storage?: { data?: { id: string; } } };
}
interface Breadcrumb { id: string; name: string; }
export default function Files() {
  const navigate = useNavigate();
  const location = useLocation();
  const { projectId, folderId } = useParams<{ projectId: string; folderId: string }>();
  const [searchParams] = useSearchParams();

  // --- RECUPERAÇÃO ROBUSTA DE IDs ---
  const state = location.state as LocationState | undefined;
  // Tenta pegar o hubId da URL, do state ou da Query String
  const rawHubId = useParams<{ hubId: string }>().hubId || searchParams.get('hubId') || state?.hubId;
  const { projetoAtual } = useProjetoAtual();
  const projectName = state?.projectName || projetoAtual?.nome || "Projeto";

  // Garante que o HubId sempre tenha o prefixo "b." se necessário (ajuste conforme seu padrão)
  const actualHubId = rawHubId?.startsWith('b.') ? rawHubId : (rawHubId ? `b.${rawHubId}` : undefined);

  // ✅ CORREÇÃO ERRO 2: Garante que o ProjectID tenha "b." para bater com o Banco de Dados
  const dbProjectId = projectId?.startsWith('b.') ? projectId : `b.${projectId}`;

  // Envio direto na pasta: só com cde.docs.upload NO projeto (o backend exige a mesma key).
  const podeEnviar = useProjectPermissions(dbProjectId).can("cde.docs.upload");

  const [content, setContent] = useState<TableRowData[]>([]);
  const [folderPath, setFolderPath] = useState<Breadcrumb[]>([]);
  const [loading, setLoading] = useState(true);

  // Estados dos Modais
  const [versionModal, setVersionModal] = useState({ isOpen: false, itemId: "", itemName: "" });
  // --- BUSCA DE DADOS ---
  const fetchData = useCallback(async (signal?: AbortSignal) => {
    if (!actualHubId || !projectId || !folderId) {
        console.warn("IDs incompletos:", { actualHubId, projectId, folderId });
        return;
    }

    setLoading(true);

    try {
      const safeProject = encodeURIComponent(projectId);
      const safeFolder = encodeURIComponent(folderId);

      const [contentRes, pathRes] = await Promise.all([
        fetch(`/DataManagement/Data/ConteudoPasta?projectId=${safeProject}&folderId=${safeFolder}`, { credentials: 'include', signal }),
        fetch(`/DataManagement/Data/GetFolderPath?projectId=${safeProject}&folderId=${safeFolder}`, { credentials: 'include', signal })
      ]);

      if (contentRes.status === 401) { navigate('/login'); return; }

      if (contentRes.ok) {
        const apiResponse = await contentRes.json();
        const itemsAndFolders: ApiDataItem[] = apiResponse.data || [];
        const versions: ApiVersionItem[] = apiResponse.included || [];

        const formattedData: TableRowData[] = itemsAndFolders.map((item) => {
          let storageUrn: string | undefined = undefined;
          let vNumber = 1; // Default visual se não achar a versão
          const customAttrs: Record<string, AttributeValue> = {};

          if (item.type === 'items') {
            const tipVersionId = item.relationships?.tip?.data?.id;

            if (tipVersionId) {
              // Tenta casar a versão de várias formas para evitar o erro "V1"
              const versionData = versions.find(v =>
                  v.id === tipVersionId ||
                  v.urn === tipVersionId ||
                  decodeURIComponent(v.id) === decodeURIComponent(tipVersionId)
              );

              if (versionData) {
                  storageUrn = versionData.relationships?.storage?.data?.id;
                  vNumber = versionData.attributes?.versionNumber || 1;
                  versionData.customAttributes?.forEach(attr => { customAttrs[attr.name] = attr.value; });
              }
            }
          }

          return {
            id: item.id,
            name: item.attributes.displayName,
            type: item.type === 'folders' ? 'folder' : 'item',
            // elegant-table.tsx do N&A (não transplantado — tem coluna dinâmica por atributo
            // customizado do ACC, feature que o ForgeSync original não tinha) exige "lastModified"
            // como string combinada, em vez de updatedAt/author separados.
            lastModified: `${new Date(item.attributes.lastModifiedTime).toLocaleString('pt-BR')} por ${item.attributes.lastModifiedUserName}`,
            version: vNumber,
            updatedAt: new Date(item.attributes.lastModifiedTime).toLocaleDateString('pt-BR'),
            author: item.attributes.lastModifiedUserName,
            // ✅ CORREÇÃO ERRO 1: Se não achou URN, passamos o ID do Item.
            // O handleDownloadClick vai saber buscar a versão correta.
            storageUrn: storageUrn || `ITEM:${item.id}`,
            customAttributes: customAttrs
          };
        });

        formattedData.sort((a, b) => {
          if (a.type === 'folder' && b.type !== 'folder') return -1;
          if (a.type !== 'folder' && b.type === 'folder') return 1;
          return a.name.localeCompare(b.name);
        });

        setContent(formattedData);
      }

      if (pathRes.ok) {
        const pathData = await pathRes.json();
        setFolderPath(pathData);
      }

    } catch (error: any) {
      if (error.name !== 'AbortError') console.error("Erro fetch:", error);
    } finally {
      setLoading(false);
    }
  }, [actualHubId, projectId, folderId, navigate]);

  useEffect(() => {
    const controller = new AbortController();
    fetchData(controller.signal);
    return () => controller.abort();
  }, [fetchData]);

  // --- ✅ CORREÇÃO ERRO 1: DOWNLOAD INTELIGENTE ---
  const handleDownloadClick = async (identifier: string) => {
    const lt = toast.loading("Preparando download...");

    try {
        let finalStorageUrn = identifier;

        // Se o identificador for um ID de Item (porque a URN falhou no carregamento)
        if (identifier.startsWith("ITEM:")) {
            const itemId = identifier.replace("ITEM:", "");
            toast.loading("Buscando versão mais recente...", { id: lt });

            // Busca as versões desse arquivo específico
            const versionsRes = await fetch(`/DataManagement/Data/GetItemVersions?projectId=${projectId}&itemId=${itemId}`, { credentials: 'include' });

            if (!versionsRes.ok) throw new Error("Não foi possível localizar o arquivo.");

            const versionsData = await versionsRes.json();
            // Pega a primeira versão (a mais recente)
            const latestVersion = versionsData.data?.[0];

            if (latestVersion && latestVersion.relationships?.storage?.data?.id) {
                finalStorageUrn = latestVersion.relationships.storage.data.id;
            } else {
                throw new Error("Arquivo sem binário para download.");
            }
        }

        // Faz o download usando a URN encontrada
        const res = await fetch(`/DataManagement/Data/DownloadArquivo?storageUrn=${encodeURIComponent(finalStorageUrn)}`, { credentials: 'include' });

        if (!res.ok) throw new Error("Falha ao gerar link.");
        const data = await res.json();

        if (data.downloadUrl) {
            window.open(data.downloadUrl, '_blank');
            toast.dismiss(lt);
        } else {
            throw new Error("Link vazio.");
        }
    } catch (e: any) {
        console.error(e);
        toast.error(e.message || "Erro no download.", { id: lt });
    }
  };

  const handleFolderClick = (newFolderId: string) => {
    navigate(
      `/projetos/${projectId}/conteudo/${newFolderId}?hubId=${actualHubId}`,
      { state: { projectName, hubId: actualHubId } }
    );
  };

  // Ferramentas antigas (Auditar, Sync LDP, SQL Server, Placeholders, Webhook) saíram da INTERFACE desta tela, focada em
  // pastas/documentos/download/versões/upload. Os endpoints do DataController e os modais em components/ continuam existindo.

  // Breadcrumb VISUAL: começa em "Project Files" (sem "Contas"/"Projetos", sem duplicar a raiz). IDs reais preservados.
  const displayPath = useMemo(() => trilhaVisivel(folderPath), [folderPath]);
  const protecao = useProtecaoPasta(actualHubId, dbProjectId, folderId);

  const currentFolderName = displayPath.length > 0 ? displayPath[displayPath.length - 1].name : (loading ? "Carregando..." : "Arquivos");

  return (
    // Só o shell do N&A Connect (header/sidebar globais): sem o cabeçalho antigo (logo, Contas, Projetos, Sair).
    <div className="space-y-4 animate-enter">
      <VersionHistoryModal isOpen={versionModal.isOpen} projectId={projectId ?? ""} itemId={versionModal.itemId} itemName={versionModal.itemName} onClose={() => setVersionModal(prev => ({ ...prev, isOpen: false }))} />
          <div className="min-w-0 space-y-1">
              {/* Nome COMPLETO do projeto (sem reticências; quebra de linha controlada se precisar). */}
              <p className="break-words text-sm font-black uppercase tracking-tight text-primary">{projectName}</p>
              <Breadcrumbs
                path={displayPath}
                onPathClick={(itemId) => {
                  if (projectId && actualHubId) navigate(`/projetos/${projectId}/conteudo/${itemId}?hubId=${actualHubId}`, { state: { projectName, hubId: actualHubId } });
                }}
              />
              <div className="flex flex-wrap items-center gap-2">
                 <FolderOpen className="h-6 w-6 flex-shrink-0 text-primary" />
                 <h1 className="min-w-0 break-words text-2xl font-black uppercase italic tracking-tighter text-foreground">{currentFolderName}</h1>
              </div>
          </div>

          {/* Envio governado pela LDP DENTRO da pasta aberta (arrastar/soltar ou "Enviar arquivos"); key = pasta → fila zera ao trocar de pasta. */}
          <EnvioNaPasta key={folderId} hubId={actualHubId ?? ""} projectId={dbProjectId} nomeProjeto={projectName} folderId={folderId ?? ""}
            caminho={caminhoDaPasta(displayPath)} podeEnviar={podeEnviar && !!actualHubId && !!folderId} protegida={protecao}
            onEnviados={() => void fetchData()}>
          {loading ? <div className="py-20 text-center animate-pulse text-[10px] font-black uppercase text-primary tracking-widest italic">Conectando...</div> :
            <ElegantTable
                data={content}
                onFolderClick={handleFolderClick}
                onDownloadClick={handleDownloadClick} // ✅ Agora trata IDs vazios
                onVersionsClick={(id, name) => setVersionModal({ isOpen: true, itemId: id, itemName: name })}
            />
          }
          </EnvioNaPasta>
    </div>
  );
}
