import { FileText, Hammer, Construction } from "lucide-react"; 
import { Button } from "@/components/ui/button"; 
import { useNavigate } from "react-router-dom";

/**
 * Página de Documentos Corporativos.
 * ✅ PENTE FINO: Mantida a lógica de "Página em Construção" do original.
 *
 */
export default function Documents() {
  const navigate = useNavigate();

  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] animate-enter">
      
      {/* Container Principal com efeito Glass-Card Forges Sync Connect */}
      <div className="glass-card p-12 rounded-2xl border border-border/50 max-w-lg w-full text-center flex flex-col items-center gap-6 shadow-2xl">
        
        {/* Composição Visual de Engenharia */}
        <div className="relative">
          <div className="absolute inset-0 bg-primary/20 blur-3xl rounded-full animate-pulse" />
          <div className="relative w-24 h-24 rounded-2xl bg-primary/10 flex items-center justify-center border border-primary/20 shadow-inner">
            <Construction className="w-12 h-12 text-primary opacity-50 absolute -top-2 -left-2 rotate-[-15deg]" />
            <Hammer className="w-10 h-10 text-primary relative z-10" />
          </div>
          <div className="absolute -bottom-2 -right-2 w-10 h-10 text-muted-foreground bg-card rounded-lg p-2 border border-border/50 shadow-lg">
            <FileText className="w-full h-full" />
          </div>
        </div>

        <div className="space-y-2">
          <h1 className="text-2xl font-black text-foreground tracking-tight uppercase italic">
            Módulo de Documentação
          </h1>
          {/* ✅ MANTIDO: O status de "Em construção" da versão funcional */}
          <p className="text-sm text-muted-foreground leading-relaxed font-medium">
            O hub de documentos corporativos está sendo integrado às APIs da Autodesk e ao banco de dados N&A.
            Em breve, o controle de normas técnicas e manuais de API estará disponível.
          </p>
        </div>

        {/* Linha de Progresso Visual */}
        <div className="w-full bg-muted/20 h-1.5 rounded-full overflow-hidden border border-border/30">
          <div className="bg-primary h-full w-3/4 shadow-[0_0_15px_hsl(var(--primary))] animate-pulse" />
        </div>

        <div className="flex flex-col gap-3 w-full">
          <Button 
            variant="outline" 
            className="w-full font-black text-[10px] tracking-[0.2em] uppercase border-primary/20 hover:bg-primary/10 transition-all"
            onClick={() => navigate("/dashboard")}
          >
            VOLTAR AO DASHBOARD
          </Button>
        </div>
      </div>

      {/* Footer Forges Sync Connect • Data: 2026 */}
      <div className="mt-8 flex items-center gap-2 opacity-30">
        <div className="h-px w-8 bg-muted-foreground" />
        <p className="text-[10px] text-muted-foreground uppercase tracking-[0.5em] font-black">
          Forges Sync Connect • Engenharia & Automação 2026
        </p>
        <div className="h-px w-8 bg-muted-foreground" />
      </div>
    </div>
  );
}