// Caminho: ClientApp/src/components/VersionHistoryModal.tsx
// VERSÃO FINAL PREMIUM - LIMPEZA DE WHITESPACE, ACESSIBILIDADE E ABORT CONTROLLER

import { useEffect, useState } from "react";
import { Download, History, X, Loader2 } from "lucide-react";
import toast from 'react-hot-toast';
import { Button } from "@/components/ui/button";

interface Version {
  id: string;
  attributes: {
    versionNumber: number;
    displayName: string;
    lastModifiedTime: string;
    lastModifiedUserName: string;
  };
  relationships: {
    storage?: {
      data?: {
        id: string;
      };
    };
  };
}

interface VersionHistoryModalProps {
  isOpen: boolean;
  projectId: string;
  itemId: string;
  itemName: string;
  onClose: () => void;
}

export const VersionHistoryModal = ({
  isOpen,
  projectId,
  itemId,
  itemName,
  onClose
}: VersionHistoryModalProps) => {
  const [versions, setVersions] = useState<Version[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Só executa se o modal estiver aberto e tivermos os IDs necessários
    if (!isOpen || !itemId || !projectId) return;

    const controller = new AbortController();
    const { signal } = controller;

    const fetchVersions = async () => {
      setLoading(true);
      try {
        const response = await fetch(
          `/DataManagement/Data/GetItemVersions?projectId=${projectId}&itemId=${itemId}`,
          { 
            credentials: 'include',
            signal 
          }
        );

        if (response.status === 401) {
          window.location.href = '/login';
          return;
        }

        if (response.ok) {
          const data = await response.json();
          // Lógica original: Inverte para mostrar a versão mais recente no topo
          setVersions((data.data || []).reverse());
        } else {
          throw new Error("Falha na resposta do servidor");
        }
      } catch (error: unknown) {
        if (error instanceof Error && error.name === 'AbortError') {
          return; // Ignora erros de cancelamento intencional
        }
        console.error("Fetch versions error:", error);
        toast.error("Erro ao carregar histórico de versões.");
      } finally {
        setLoading(false);
      }
    };

    fetchVersions();

    // Cleanup: Cancela a requisição se o modal for fechado antes do término
    return () => controller.abort();
  }, [isOpen, projectId, itemId]);

  const handleDownload = async (storageUrn?: string) => {
    if (!storageUrn) {
      toast.error("URN de armazenamento não encontrada.");
      return;
    }

    const lt = toast.loading("Gerando link de download...");
    try {
      const response = await fetch(
        `/DataManagement/Data/DownloadArquivo?storageUrn=${encodeURIComponent(storageUrn)}`,
        { credentials: 'include' }
      );

      if (response.ok) {
        const data = await response.json();
        if (data.downloadUrl) {
          window.open(data.downloadUrl, '_blank');
          toast.dismiss(lt);
        } else {
          throw new Error("URL não fornecida");
        }
      } else {
        throw new Error("Erro na requisição");
      }
    } catch (error: unknown) {
      console.error("Download error:", error);
      toast.error("Falha ao gerar link de download.", { id: lt });
    }
  };

  if (!isOpen) return null;

  return (
    <div 
      className="fixed inset-0 bg-background/40 backdrop-blur-md z-50 flex items-center justify-center p-4 animate-in fade-in duration-200"
      onClick={onClose}
    >
      <div 
        className="bg-card glass-card border border-border/50 rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden flex flex-col animate-in zoom-in-95 duration-300"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header do Modal */}
        <div className="p-6 border-b border-border/50 flex items-center justify-between bg-primary/5">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-primary/10 border border-primary/20 shadow-sm">
              <History className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h2 className="text-lg font-black text-foreground uppercase tracking-tight italic">Histórico de Versões</h2>
              <p className="text-[10px] text-muted-foreground font-bold uppercase tracking-widest truncate max-w-[350px]">
                {itemName}
              </p>
            </div>
          </div>
          <button 
            onClick={onClose} 
            className="p-2 hover:bg-muted rounded-full transition-colors group"
            title="Fechar Histórico"
            aria-label="Fechar histórico de versões"
          >
            <X className="w-5 h-5 text-muted-foreground group-hover:text-primary transition-colors" />
          </button>
        </div>

        {/* Lista de Versões */}
        <div className="p-6 overflow-y-auto max-h-[50vh] space-y-3 custom-scrollbar">
          {loading ? (
            <div className="py-12 text-center flex flex-col items-center gap-3">
              <Loader2 className="w-6 h-6 text-primary animate-spin" />
              <p className="text-[10px] font-black uppercase tracking-widest text-primary italic">A ler versões da Autodesk...</p>
            </div>
          ) : versions.length === 0 ? (
            <div className="py-12 text-center text-muted-foreground text-sm font-bold uppercase italic border-2 border-dashed border-border/40 rounded-xl">
              Nenhuma versão localizada.
            </div>
          ) : (
            versions.map((v) => (
              <div 
                key={v.id} 
                className="flex justify-between items-center bg-muted/20 hover:bg-muted/40 p-4 rounded-lg border border-transparent hover:border-border/50 transition-all group"
              >
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="px-1.5 py-0.5 rounded bg-primary text-[10px] text-primary-foreground font-black shadow-sm shadow-primary/20">
                      V{v.attributes.versionNumber}
                    </span>
                    <p className="text-sm font-bold text-foreground truncate max-w-[300px]">
                      {v.attributes.displayName}
                    </p>
                  </div>
                  <p className="text-[11px] text-muted-foreground mt-1 font-medium italic">
                    {v.attributes.lastModifiedUserName} • {new Date(v.attributes.lastModifiedTime).toLocaleString('pt-BR')}
                  </p>
                </div>
                <Button 
                  size="sm" 
                  variant="outline" 
                  className="font-black text-[10px] gap-2 border-primary/20 hover:border-primary shrink-0 transition-all"
                  onClick={() => handleDownload(v.relationships?.storage?.data?.id)}
                  title={`Baixar Versão ${v.attributes.versionNumber}`}
                >
                  <Download className="w-3.5 h-3.5" /> BAIXAR
                </Button>
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        <div className="p-4 bg-muted/10 border-t border-border/50 flex justify-end">
          <Button 
            variant="ghost" 
            onClick={onClose} 
            className="font-black text-[10px] uppercase italic tracking-widest"
            title="Fechar Modal"
          >
            FECHAR
          </Button>
        </div>
      </div>
    </div>
  );
};