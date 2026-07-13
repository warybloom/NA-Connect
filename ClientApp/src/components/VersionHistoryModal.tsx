// Caminho: ClientApp/src/components/VersionHistoryModal.tsx
import { useEffect, useState } from "react";
import { Button } from "./ui/button";
import { Download } from "lucide-react";

interface Version {
  id: string;
  attributes: {
    versionNumber: number;
    displayName: string;
    lastModifiedTime: string;
    lastModifiedUserName: string;
  };
  relationships: {
    storage?: { data?: { id: string } };
  };
}

interface VersionHistoryModalProps {
  projectId: string;
  itemId: string;
  itemName: string;
  onClose: () => void;
}

export const VersionHistoryModal = ({ projectId, itemId, itemName, onClose }: VersionHistoryModalProps) => {
  const [versions, setVersions] = useState<Version[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchVersions = async () => {
      const response = await fetch(`/DataManagement/Data/GetItemVersions?projectId=${projectId}&itemId=${itemId}`);
      if (response.ok) {
        const data = await response.json();
        setVersions((data.data || []).reverse()); // Mostra a mais nova primeiro
      }
      setLoading(false);
    };
    fetchVersions();
  }, [projectId, itemId]);

  const handleDownload = async (storageUrn?: string) => {
      if (!storageUrn) { alert("URN de armazenamento não encontrado."); return; }
      const response = await fetch(`/DataManagement/Data/DownloadArquivo?storageUrn=${storageUrn}`);
      if (response.ok) {
          const data = await response.json();
          window.open(data.downloadUrl, '_blank');
      } else {
          alert("Falha ao obter o link de download.");
      }
  };

  return (
    <div className="fixed inset-0 bg-black/60 z-50 flex items-center justify-center" onClick={onClose}>
      <div className="bg-card border border-border rounded-lg shadow-xl w-full max-w-2xl p-6" onClick={e => e.stopPropagation()}>
        <h2 className="text-xl font-bold text-foreground mb-1">Histórico de Versões</h2>
        <p className="text-muted-foreground mb-4 truncate">Ficheiro: {itemName}</p>
        <div className="max-h-[60vh] overflow-y-auto pr-2">
          {loading ? <p>A carregar versões...</p> : (
            <ul className="space-y-2">
              {versions.map(v => (
                <li key={v.id} className="flex justify-between items-center bg-muted/50 p-3 rounded-md">
                  <div>
                    <p className="font-semibold text-foreground">v{v.attributes.versionNumber} - {v.attributes.displayName}</p>
                    <p className="text-sm text-muted-foreground">
                      Modificado por {v.attributes.lastModifiedUserName} em {new Date(v.attributes.lastModifiedTime).toLocaleString('pt-BR')}
                    </p>
                  </div>
                  <Button size="sm" variant="ghost" onClick={() => handleDownload(v.relationships?.storage?.data?.id)}>
                    <Download className="w-4 h-4 mr-2" /> Download
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
};