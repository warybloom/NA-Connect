import React, { useState, useEffect } from 'react';
import { FolderTree, Save, X, FileSpreadsheet, Clock } from 'lucide-react';
import { Button } from "@/components/ui/button";
import toast from 'react-hot-toast';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    projectId: string;
    projectName: string;
}

export const LDPConfigModal = ({ isOpen, onClose, projectId, projectName }: Props) => {
    const [folders, setFolders] = useState<any[]>([]);
    const [selectedLdpFolder, setSelectedLdpFolder] = useState("");
    const [selectedCronFolder, setSelectedCronFolder] = useState("");
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (isOpen) {
            fetchFolders("root");
        }
    }, [isOpen]);

    const fetchFolders = async (parentId: string) => {
        setLoading(true);
        try {
            const res = await fetch(`/api/admin/pastas/${projectId}?parentId=${parentId}`);
            const data = await res.json();
            setFolders(data.filter((f: any) => f.type === 'folders'));
        } catch (err) {
            toast.error("Erro ao carregar pastas do ACC");
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        if (!selectedLdpFolder) {
            toast.error("Selecione pelo menos a pasta da LDP");
            return;
        }

        try {
            const res = await fetch(`/api/admin/projeto/${projectId}/pastas`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    ldpFolderId: selectedLdpFolder,
                    cronogramaFolderId: selectedCronFolder
                })
            });

            if (res.ok) {
                toast.success("Configuração salva com sucesso!");
                onClose();
            } else {
                throw new Error("Falha ao salvar");
            }
        } catch (error) {
            toast.error("Erro ao salvar a configuração das pastas.");
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-background/80 backdrop-blur-sm animate-enter">
            <div className="bg-card border border-border rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
                <div className="p-4 border-b border-border flex justify-between items-center bg-muted/30">
                    <h2 className="text-lg font-black uppercase text-foreground flex items-center gap-2">
                        <FolderTree className="text-primary" /> Setup de Diretórios
                    </h2>
                    <button 
                        onClick={onClose} 
                        title="Fechar janela"
                        aria-label="Fechar janela"
                        className="text-muted-foreground hover:text-foreground transition-colors p-1"
                    >
                        <X size={20} />
                    </button>
                </div>

                <div className="p-6 space-y-6">
                    <div className="bg-primary/10 border border-primary/20 p-3 rounded-lg">
                        <p className="text-xs text-primary font-bold uppercase tracking-widest text-center">
                            {projectName}
                        </p>
                    </div>

                    <div className="space-y-2">
                        <label htmlFor="ldp-folder-select" className="text-xs font-bold text-muted-foreground uppercase flex items-center gap-2">
                            <FileSpreadsheet size={14} className="text-emerald-500" /> Pasta Monitorada (LDP) *
                        </label>
                        <select 
                            id="ldp-folder-select"
                            title="Selecione a pasta da LDP"
                            aria-label="Selecione a pasta da LDP"
                            value={selectedLdpFolder}
                            onChange={(e) => setSelectedLdpFolder(e.target.value)}
                            className="w-full bg-background border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50 transition-colors"
                        >
                            <option value="">Selecione a pasta que contém a LDP...</option>
                            {folders.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                        </select>
                    </div>

                    <div className="space-y-2">
                        <label htmlFor="cron-folder-select" className="text-xs font-bold text-muted-foreground uppercase flex items-center gap-2">
                            <Clock size={14} className="text-blue-500" /> Pasta Cronograma (Opcional)
                        </label>
                        <select 
                            id="cron-folder-select"
                            title="Selecione a pasta do Cronograma"
                            aria-label="Selecione a pasta do Cronograma"
                            value={selectedCronFolder}
                            onChange={(e) => setSelectedCronFolder(e.target.value)}
                            className="w-full bg-background border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50 transition-colors"
                        >
                            <option value="">Selecione a pasta do Cronograma (Baseline)...</option>
                            {folders.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                        </select>
                    </div>

                    <div className="pt-4 flex gap-3">
                        <Button 
                            onClick={onClose} 
                            aria-label="Cancelar"
                            variant="outline" 
                            className="flex-1 border-border text-foreground hover:bg-muted font-bold uppercase text-[10px] transition-colors"
                        >
                            Cancelar
                        </Button>
                        <Button 
                            onClick={handleSave} 
                            aria-label="Gravar Configuração"
                            className="flex-1 bg-primary hover:bg-primary/80 text-primary-foreground font-bold uppercase text-[10px] gap-2 transition-colors"
                        >
                            <Save size={14} /> Gravar Configuração
                        </Button>
                    </div>
                </div>
            </div>
        </div>
    );
};