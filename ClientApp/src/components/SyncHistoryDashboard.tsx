import React from 'react';
import { Activity, Terminal, CheckCircle } from 'lucide-react';

export default function SyncHistoryDashboard() {
    return (
        <div className="space-y-6 animate-enter">
            {/* Card Live Sync */}
            <div className=" p-8 bg-slate-500/40 border border-green-500/20 rounded-[2rem]">
                <div className="flex items-center gap-3 text-blue-500 mb-4">
                    <Activity className="animate-pulse" />
                    <span className="text-[10px] font-black uppercase hover:text-black">Execução Atual: N&A CONNECT ENGINE</span>
                </div>
                <div className="h-2 bg-white/5 rounded-full overflow-hidden">
                    <div className="h-full bg-blue-500 w-3/4 shadow-[0_0_15px_#ea580c]" />
                </div>
                <div className="mt-4 bg-black/50 p-4 rounded-lg font-mono text-[10px] text-emerald-500">
                    <p>{`> [LOG] Extraindo dados do Piping.dcf...`}</p>
                    <p>{`> [LOG] Processando 1248 itens... 75%`}</p>
                </div>
            </div>
        </div>
    );
}