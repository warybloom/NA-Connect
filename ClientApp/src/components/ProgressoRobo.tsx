import { Loader2, CheckCircle2, AlertTriangle } from "lucide-react";

interface ProgressoRoboProps {
    executando: boolean;
    porcentagem: number; 
    etapaAtualTexto?: string;
}

export function ProgressoRobo({
    executando,
    porcentagem,
    etapaAtualTexto = "Aguardando Início"
}: ProgressoRoboProps) {
    const valorProgresso = Math.min(Math.max(porcentagem,0),100);
    
    
return (
    <div className="w-full bg-card/40 border border-border/50 rounded-xl p-4 backdrop-blur-sm space-y-3">
      {/* Cabeçalho do Progresso */}
      <div className="flex items-center justify-between text-xs">
        <div className="flex items-center gap-2">
          {executando ? (
            <Loader2 className="w-4 h-4 animate-spin text-indigo-400" />
          ) : valorProgresso === 100 ? (
            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
          ) : (
            <AlertTriangle className="w-4 h-4 text-muted-foreground" />
          )}
          <span className="font-bold text-foreground">
            {executando ? "Executando Robô..." : valorProgresso === 100 ? "Concluído" : "Ocioso"}
          </span>
          <span className="text-muted-foreground font-mono text-[11px]">
            — {etapaAtualTexto}
          </span>
        </div>
        <span className="font-mono font-black text-indigo-400 text-sm">
          {valorProgresso.toFixed(0)}%
        </span>
      </div>

      {/* Trilho da Barra */}
      <div className="w-full bg-slate-800/60 h-2.5 rounded-full overflow-hidden p-0.5 border border-slate-700/50">
        {/* Preenchimento Dinâmico com Efeito Neon */}
        <div 
          className={`h-full rounded-full transition-all duration-500 ease-out ${
            valorProgresso === 100 
              ? "bg-emerald-500 shadow-[0_0_12px_rgba(16,185,129,0.5)]" 
              : "bg-indigo-500 shadow-[0_0_12px_rgba(99,102,241,0.5)]"
          }`}
          style={{ width: `${valorProgresso}%` }}
        />
      </div>
    </div>
  );
}


