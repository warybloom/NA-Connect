import { Link } from "react-router-dom";
import { Button } from "../components/ui/button"; 
import { ThemeLogo } from "@/features/theme/ThemeLogo";   

/**
 * Página de Erro 404 - Forges Sync Connect.
 * ✅ PENTE FINO: Lógica de redirecionamento para '/dashboard' preservada.
 *
 */
export default function NotFound() {
  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-background text-foreground text-center p-4 relative overflow-hidden">
      
      {/* Elemento Visual de Fundo para Identidade Visual */}
      <div className="absolute inset-0 geo-pattern opacity-20" />
      
      <div className="relative z-10 flex flex-col items-center animate-enter">
        <div className="mb-12 opacity-50 grayscale hover:grayscale-0 transition-all duration-500">
          <ThemeLogo />
        </div>

        <h1 className="text-8xl font-black text-primary mb-2 tracking-tighter italic opacity-20 selection:bg-transparent">
          404
        </h1>
        
        <div className="space-y-2 mb-8">
          <h2 className="text-2xl font-black uppercase tracking-tight italic">
            Página Não Encontrada
          </h2>
          <p className="text-sm text-muted-foreground font-medium max-w-xs mx-auto">
            Lamentamos, mas a página que você procura não existe ou foi movida para outro diretório.
          </p>
        </div>

        <Button asChild className="font-black text-[10px] tracking-[0.2em] uppercase px-8 shadow-lg shadow-primary/20">
          {/* ✅ NAVEGAÇÃO: Mantida a lógica original de retorno */}
          <Link to="/dashboard">Voltar para o Início</Link>
        </Button>
      </div>

      <div className="absolute bottom-8 text-[9px] text-muted-foreground/30 font-black uppercase tracking-[0.5em]">
        Forges Sync Connect • Engenharia de Software
      </div>
    </div>
  );
}