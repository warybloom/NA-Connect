import React, { useState, useEffect } from 'react';
import { Settings2, Database, RefreshCw, Save, Cable, Box, GitMerge, Gauge, ListChecks } from 'lucide-react';
import { Button } from "@/components/ui/button";
import toast from 'react-hot-toast';

type CategoriaTag = 'equipamentos' | 'linhas' | 'valvulas' | 'instrumentos' | 'tie_ins';

export default function PlantMappingPanel({ projectId }: { projectId: string }) {
    const [activeTab, setActiveTab] = useState<CategoriaTag>('equipamentos');
    const [pidColumns, setPidColumns] = useState<string[]>([]);
    const [p3dColumns, setP3dColumns] = useState<string[]>([]);
    const [mapping, setMapping] = useState<any>({
        equipamentos: { pid_col: '', p3d_col: '' },
        linhas: { pid_col: '', p3d_col: '' },
        valvulas: { pid_col: '', p3d_col: '' },
        instrumentos: { pid_col: '', p3d_col: '' },
        tie_ins: { pid_col: '', p3d_col: '' },
    });

    useEffect(() => {
        const fetchColumns = async () => {
            try {
                const res = await fetch(`/api/plant/colunas-disponiveis/${projectId}`);
                if (res.ok) {
                    const data = await res.json();
                    setPidColumns(data.pidColumns);
                    setP3dColumns(data.p3dColumns);
                }
            } catch (error) {
                console.error("Erro ao carregar colunas", error);
            }
        };
        fetchColumns();
    }, [projectId]);

    const handleSave = async () => {
        const tid = toast.loading("Salvando mapeamento...");
        try {
            const res = await fetch(`/api/plant/salvar-mapeamento/${projectId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(mapping)
            });
            if (res.ok) toast.success("Configuração salva!", { id: tid });
        } catch { toast.error("Erro ao salvar", { id: tid }); }
    };

    return (
        <div className="glass-card p-8 bg-slate-900/40 border border-white/5 rounded-[2rem]">
            <div className="flex justify-between mb-6">
                <h2 className="text-xl font-black uppercase italic text-white">Mapeamento de Atributos</h2>
                <Button onClick={handleSave} className="bg-blue-500 text-white text-[10px] font-black uppercase">Gravar Regras</Button>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                 {/* Selects de De/Para aqui... */}
                 <p className="text-slate-400 text-xs italic">Configure os campos de tagueamento para cada categoria.</p>
            </div>
        </div>
    );
}