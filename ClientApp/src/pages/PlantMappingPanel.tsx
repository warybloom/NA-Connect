// BACKEND_PENDENTE: auditoria/mapeamento Plant3D (api/plant/*) nao existe no N&A. UI transplantada literalmente do ForgeSync. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
import React, { useState, useEffect } from 'react';
import { Settings2, Save, Box, Cable, GitMerge, Gauge, ListChecks, RefreshCw } from 'lucide-react';
import { Button } from "@/components/ui/button";
import toast from 'react-hot-toast';

type CategoriaTag = 'equipamentos' | 'linhas' | 'valvulas' | 'instrumentos' | 'tie_ins';

interface Mapeamento {
    pid_col: string;
    p3d_col: string;
}

export default function PlantMappingPanel({ projectId }: { projectId?: string }) {
    const [activeTab, setActiveTab] = useState<CategoriaTag>('equipamentos');
    const [pidColumns, setPidColumns] = useState<string[]>([]);
    const [p3dColumns, setP3dColumns] = useState<string[]>([]);
    const [isLoadingCols, setIsLoadingCols] = useState(false);
    const [isSaving, setIsSaving] = useState(false);

    const [mapping, setMapping] = useState<Record<CategoriaTag, Mapeamento>>({
        equipamentos: { pid_col: '', p3d_col: '' },
        linhas: { pid_col: '', p3d_col: '' },
        valvulas: { pid_col: '', p3d_col: '' },
        instrumentos: { pid_col: '', p3d_col: '' },
        tie_ins: { pid_col: '', p3d_col: '' },
    });

    const abas = [
        { id: 'equipamentos', label: 'Equipamentos', icon: <Box size={16} /> },
        { id: 'linhas', label: 'Linhas (Groups)', icon: <Cable size={16} /> },
        { id: 'valvulas', label: 'Válvulas', icon: <GitMerge size={16} /> },
        { id: 'instrumentos', label: 'Instrumentos', icon: <Gauge size={16} /> },
        { id: 'tie_ins', label: 'Tie-Ins', icon: <ListChecks size={16} /> },
    ] as const;

    useEffect(() => {
        const fetchColumns = async () => {
            if (!projectId) return;
            setIsLoadingCols(true);
            try {
                const res = await fetch(`/api/plant/colunas-disponiveis/${projectId}`);
                if (res.ok) {
                    const data = await res.json();
                    setPidColumns(data.pidColumns || []);
                    setP3dColumns(data.p3dColumns || []);
                }
            } catch (error) {
                console.error("Erro ao carregar colunas", error);
            } finally {
                setIsLoadingCols(false);
            }
        };
        fetchColumns();
    }, [projectId]);

    const handleMappingChange = (type: 'pid_col' | 'p3d_col', value: string) => {
        setMapping(prev => ({
            ...prev,
            [activeTab]: { ...prev[activeTab], [type]: value }
        }));
    };

    const handleSave = async () => {
        setIsSaving(true);
        const tid = toast.loading("Salvando mapeamento...");
        try {
            const res = await fetch(`/api/plant/salvar-mapeamento/${projectId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(mapping)
            });
            if (res.ok) {
                toast.success("Configuração salva com sucesso!", { id: tid });
            } else throw new Error();
        } catch {
            toast.error("Erro ao salvar configuração.", { id: tid });
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <div className="glass-card p-8 bg-slate-900/40 border border-border/60 rounded-[2rem] shadow-2xl animate-in fade-in duration-500">
            <div className="flex items-center justify-between mb-8 pb-6 border-b border-border/60">
                <div className="flex items-center gap-4">
                    <div className="p-3 bg-orange-500/10 rounded-xl border border-orange-500/20">
                        <Settings2 className="text-orange-500 w-6 h-6" />
                    </div>
                    <div>
                        <h2 className="text-2xl font-black uppercase italic text-foreground tracking-tighter">Dicionário de Atributos</h2>
                        <p className="text-[10px] text-slate-400 font-bold uppercase tracking-[0.2em] mt-1">Configuração De/Para por Projeto</p>
                    </div>
                </div>
                <Button onClick={handleSave} disabled={isSaving} className="bg-orange-600 hover:bg-orange-500 text-white font-black text-[10px] uppercase tracking-widest gap-2">
                    {isSaving ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                    Gravar Regras
                </Button>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-4 gap-8">
                <div className="space-y-2 col-span-1">
                    {abas.map((aba) => (
                        <button
                            key={aba.id}
                            onClick={() => setActiveTab(aba.id)}
                            className={`w-full flex items-center gap-3 p-4 rounded-xl text-xs font-bold uppercase transition-all ${
                                activeTab === aba.id 
                                ? 'bg-orange-500/10 text-orange-500 border border-orange-500/30' 
                                : 'text-slate-400 hover:bg-foreground/5 hover:text-foreground border border-transparent'
                            }`}
                        >
                            {aba.icon}
                            {aba.label}
                        </button>
                    ))}
                </div>

                <div className="col-span-3">
                    <div className="bg-black/30 p-8 rounded-2xl border border-border/60 group hover:border-orange-500/30 transition-all duration-500">
                        <h3 className="text-lg font-black uppercase italic text-foreground mb-6">
                            Regra para: <span className="text-orange-500">{abas.find(a => a.id === activeTab)?.label}</span>
                        </h3>

                        <div className="grid grid-cols-1 md:grid-cols-2 gap-8 relative z-10">
                            <div className="space-y-3">
                                <label htmlFor="pid-select" className="text-[10px] font-black text-slate-400 uppercase tracking-widest flex items-center justify-between">
                                    Propriedade no P&ID
                                    {isLoadingCols && <RefreshCw className="w-3 h-3 animate-spin text-orange-500" />}
                                </label>
                                <select 
                                    id="pid-select"
                                    title="Selecione a coluna correspondente no P&ID"
                                    className="w-full bg-slate-950 border border-border rounded-xl px-4 py-4 text-xs font-bold text-foreground outline-none focus:border-orange-500/50 appearance-none hover:bg-slate-900 transition-colors"
                                    value={mapping[activeTab].pid_col}
                                    onChange={(e) => handleMappingChange('pid_col', e.target.value)}
                                >
                                    <option value="">-- SELECIONE A COLUNA --</option>
                                    {pidColumns.map(col => <option key={col} value={col}>{col}</option>)}
                                </select>
                            </div>

                            <div className="space-y-3">
                                <label htmlFor="p3d-select" className="text-[10px] font-black text-slate-400 uppercase tracking-widest flex items-center justify-between">
                                    Propriedade no 3D
                                    {isLoadingCols && <RefreshCw className="w-3 h-3 animate-spin text-orange-500" />}
                                </label>
                                <select 
                                    id="p3d-select"
                                    title="Selecione a coluna correspondente no Plant 3D"
                                    className="w-full bg-slate-950 border border-border rounded-xl px-4 py-4 text-xs font-bold text-foreground outline-none focus:border-orange-500/50 appearance-none hover:bg-slate-900 transition-colors"
                                    value={mapping[activeTab].p3d_col}
                                    onChange={(e) => handleMappingChange('p3d_col', e.target.value)}
                                >
                                    <option value="">-- SELECIONE A COLUNA --</option>
                                    {p3dColumns.map(col => <option key={col} value={col}>{col}</option>)}
                                </select>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}