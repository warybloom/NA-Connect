import React from 'react';
import { 
    CheckCircle2, 
    AlertCircle, 
    XCircle, 
    Search,
    Filter,
    ArrowRightLeft
} from 'lucide-react';
import { cn } from "@/lib/utils";

// Interface baseada no Plant3D_Ativos_Model do C#
interface AuditResultItem {
    ativoID: number;
    tag: string;
    categoria: string;
    desenho_PID: string;
    desenho_3D: string;
    spec_PID: string;
    spec_3D: string;
    status_Cruzamento: string;
}

export default function AuditResultsTable({ data }: { data: AuditResultItem[] }) {
    
    // Função para definir o estilo do badge de status
    const getStatusBadge = (status: string) => {
        const base = "px-3 py-1 rounded-full text-[9px] font-black uppercase tracking-widest border ";
        switch (status) {
            case 'OK':
                return <span className={cn(base, "bg-emerald-500/10 text-emerald-500 border-emerald-500/20")}>CONFORME</span>;
            case 'DIVERGENTE_SPEC':
                return <span className={cn(base, "bg-amber-500/10 text-amber-500 border-amber-500/20")}>ERRO DE SPEC</span>;
            case 'FALTANDO_NO_3D':
                return <span className={cn(base, "bg-red-500/10 text-red-500 border-red-500/20")}>AUSENTE NO 3D</span>;
            case 'SOBRA_NO_3D':
                return <span className={cn(base, "bg-purple-500/10 text-purple-500 border-purple-500/20")}>SOBRA NO 3D</span>;
            default:
                return <span className={cn(base, "bg-slate-500/10 text-slate-500 border-slate-500/20")}>{status}</span>;
        }
    };

    return (
        <div className="space-y-6 animate-in fade-in slide-in-from-bottom-4 duration-700">
            {/* Cards de Resumo Rápido */}
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                <div className="glass-card p-6 bg-emerald-500/5 border border-emerald-500/10 rounded-2xl">
                    <p className="text-[10px] font-black text-emerald-500 uppercase tracking-widest">Itens OK</p>
                    <h4 className="text-2xl font-black text-white italic tracking-tighter mt-1">
                        {data.filter(i => i.status_Cruzamento === 'OK').length}
                    </h4>
                </div>
                <div className="glass-card p-6 bg-amber-500/5 border border-amber-500/10 rounded-2xl">
                    <p className="text-[10px] font-black text-amber-500 uppercase tracking-widest">Divergências</p>
                    <h4 className="text-2xl font-black text-white italic tracking-tighter mt-1">
                        {data.filter(i => i.status_Cruzamento === 'DIVERGENTE_SPEC').length}
                    </h4>
                </div>
                <div className="glass-card p-6 bg-red-500/5 border border-red-500/10 rounded-2xl">
                    <p className="text-[10px] font-black text-red-500 uppercase tracking-widest">Ausentes</p>
                    <h4 className="text-2xl font-black text-white italic tracking-tighter mt-1">
                        {data.filter(i => i.status_Cruzamento === 'FALTANDO_NO_3D').length}
                    </h4>
                </div>
                <div className="glass-card p-6 bg-purple-500/5 border border-purple-500/10 rounded-2xl">
                    <p className="text-[10px] font-black text-purple-500 uppercase tracking-widest">Sobras 3D</p>
                    <h4 className="text-2xl font-black text-white italic tracking-tighter mt-1">
                        {data.filter(i => i.status_Cruzamento === 'SOBRA_NO_3D').length}
                    </h4>
                </div>
            </div>

            {/* Tabela de Detalhes */}
            <div className="glass-card overflow-hidden bg-slate-900/40 border border-white/5 rounded-[2rem] shadow-2xl">
                <table className="w-full text-left border-collapse">
                    <thead>
                        <tr className="bg-white/5 text-[10px] uppercase font-black tracking-widest text-slate-400 border-b border-white/5">
                            <th className="px-6 py-5">Categoria / Tag</th>
                            <th className="px-6 py-5">Referência P&ID</th>
                            <th className="px-6 py-5 text-center"><ArrowRightLeft size={14} className="mx-auto opacity-40"/></th>
                            <th className="px-6 py-5">Referência 3D</th>
                            <th className="px-6 py-5">Status Auditoria</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-white/5 bg-black/20">
                        {data.map((item) => (
                            <tr key={item.ativoID} className="group hover:bg-white/5 transition-colors">
                                <td className="px-6 py-4">
                                    <div className="flex flex-col">
                                        <span className="text-[9px] font-black text-orange-500 uppercase mb-1">{item.categoria}</span>
                                        <span className="text-sm font-bold text-white tracking-tight">{item.tag}</span>
                                    </div>
                                </td>
                                <td className="px-6 py-4">
                                    <div className="flex flex-col opacity-60">
                                        <span className="text-[10px] text-slate-400 truncate max-w-[150px]">{item.desenho_PID || '---'}</span>
                                        <span className="text-[9px] font-mono text-slate-500 uppercase mt-1">{item.spec_PID}</span>
                                    </div>
                                </td>
                                <td className="px-6 py-4 text-center">
                                    <div className="w-8 h-8 rounded-full bg-white/5 border border-white/10 flex items-center justify-center mx-auto">
                                        <Search size={12} className="text-slate-600 group-hover:text-orange-500 transition-colors" />
                                    </div>
                                </td>
                                <td className="px-6 py-4">
                                    <div className="flex flex-col">
                                        <span className="text-[10px] text-slate-300 font-bold truncate max-w-[150px]">{item.desenho_3D || '---'}</span>
                                        <span className="text-[9px] font-mono text-orange-500/70 uppercase mt-1 font-bold">{item.spec_3D}</span>
                                    </div>
                                </td>
                                <td className="px-6 py-4">
                                    {getStatusBadge(item.status_Cruzamento)}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}