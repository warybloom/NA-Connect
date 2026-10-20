// BACKEND_PENDENTE: escrita de configuracao de LDP (api/admin/projeto/configurar-ldp) nao existe no N&A. UI transplantada literalmente. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
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

    // Busca as pastas iniciais do projeto via sua API existente
    useEffect(() => {
        if (isOpen) {
            fetchFolders("root"); // Começa da raiz ou pasta pai configurada
        }
    }, [isOpen]);

    const fetchFolders = async (parentId: string) => {
        setLoading(true);
        try {
            // Chamada para o seu endpoint que usa o AutodeskApiService.cs
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
        const payload = {
            projetoId: projectId,
            folderIdLdp: selectedLdpFolder,
            folderIdCronograma: selectedCronFolder,
            nome: `Config_${projectName}`,
            mappingJson: "{}" // Aqui entra o mapeamento de colunas depois
        };

        const res = await fetch('/api/admin/projeto/configurar-ldp', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            toast.success("Projeto configurado!");
            onClose();
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
            <div className="glass-card w-full max-w-2xl border border-border shadow-2xl overflow-hidden rounded-3xl bg-slate-900">
                <div className="p-8 border-b border-border/60 flex justify-between items-center bg-gradient-to-r from-primary/10 to-transparent">
                    <div>
                        <h2 className="text-xl font-black text-foreground uppercase tracking-tighter">Configurar Ecossistema LDP</h2>
                        <p className="text-xs text-slate-400 font-bold uppercase">{projectName}</p>
                    </div>
                    <Button variant="ghost" onClick={onClose} className="text-slate-400 hover:text-foreground">
                        <X size={20} />
                    </Button>
                </div>

                <div className="p-8 space-y-8">
                    {/* SELEÇÃO DA PASTA LDP */}
<div className="space-y-3">
    <label 
        htmlFor="ldp-folder-select" 
        className="text-[10px] font-black text-primary uppercase tracking-widest flex items-center gap-2"
    >
        <FileSpreadsheet size={14} /> Pasta da LDP (Excel)
    </label>
    <select 
        id="ldp-folder-select" // Vincula ao htmlFor do label
        title="Selecione a pasta da LDP" // Resolve o erro de title attribute
        value={selectedLdpFolder}
        onChange={(e) => setSelectedLdpFolder(e.target.value)}
        className="w-full bg-slate-800 border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50"
    >
        <option value="">Selecione a pasta onde o robô buscará a LDP...</option>
        {folders.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
    </select>
</div>

                    {/* SELEÇÃO DA PASTA CRONOGRAMA */}
                    <div className="space-y-3">
                        <label 
                            htmlFor="cron-folder-select"
                            className="text-[10px] font-black text-primary uppercase tracking-widest flex items-center gap-2"
                        >
                            <Clock size={14} /> Pasta do Cronograma
                        </label>
                        <select 
                            id="cron-folder-select" // Vincula ao htmlFor do label
                            title="Selecione a pasta do Cronograma" // Resolve o erro de title attribute
                            value={selectedCronFolder}
                            onChange={(e) => setSelectedCronFolder(e.target.value)}
                            className="w-full bg-slate-800 border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50"
                        >
                            <option value="">Selecione a pasta do Cronograma (Baseline)...</option>
                            {folders.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                        </select>
                    </div>

                    

                    <div className="pt-4 flex gap-3">
                        <Button onClick={onClose} variant="outline" className="flex-1 border-border text-foreground hover:bg-foreground/5 font-bold uppercase text-[10px]">
                            Cancelar
                        </Button>
                        <Button onClick={handleSave} className="flex-1 bg-primary hover:bg-primary/80 text-white font-bold uppercase text-[10px] gap-2">
                            <Save size={14} /> Gravar Configuração
                        </Button>
                    </div>
                </div>
            </div>
        </div>
    );
};