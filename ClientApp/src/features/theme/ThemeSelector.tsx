import { useRef, type KeyboardEvent } from "react";
import { Monitor, Moon, Sun, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { OPCOES_TEMA, type PreferenciaTema } from "./themeLogic";
import { useTemaApp } from "./ThemeProvider";

const ICONES: Record<PreferenciaTema, LucideIcon> = { light: Sun, dark: Moon, system: Monitor };

/**
 * Seletor global de tema (Claro / Escuro / Automático). Grupo de rádio acessível: Tab entra no item marcado,
 * setas mudam a opção, Enter/Espaço confirmam. `comRotulo` mostra o texto ao lado dos ícones.
 */
export function ThemeSelector({ className, comRotulo = false }: { className?: string; comRotulo?: boolean }) {
  const { preferencia, tema, definir } = useTemaApp();
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const indiceAtual = Math.max(0, OPCOES_TEMA.findIndex((o) => o.valor === preferencia));

  const aoTeclar = (e: KeyboardEvent<HTMLDivElement>) => {
    const passo = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
    if (!passo) return;
    e.preventDefault();
    const proximo = (indiceAtual + passo + OPCOES_TEMA.length) % OPCOES_TEMA.length;
    definir(OPCOES_TEMA[proximo].valor);
    refs.current[proximo]?.focus();
  };

  return (
    <div
      role="radiogroup"
      aria-label="Tema da interface"
      onKeyDown={aoTeclar}
      className={cn("inline-flex items-center gap-0.5 rounded-lg border border-border bg-muted/60 p-0.5", className)}
    >
      {OPCOES_TEMA.map((opcao, i) => {
        const Icone = ICONES[opcao.valor];
        const marcado = opcao.valor === preferencia;
        const dica = opcao.valor === "system"
          ? `${opcao.rotulo} — ${opcao.descricao} (agora: ${tema === "dark" ? "escuro" : "claro"})`
          : `Tema ${opcao.rotulo.toLowerCase()}`;
        return (
          <button
            key={opcao.valor}
            ref={(el) => { refs.current[i] = el; }}
            type="button"
            role="radio"
            aria-checked={marcado}
            aria-label={opcao.rotulo}
            title={dica}
            tabIndex={marcado ? 0 : -1}
            onClick={() => definir(opcao.valor)}
            className={cn(
              "flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-semibold transition-colors",
              marcado ? "bg-card text-primary shadow-sm ring-1 ring-border" : "text-muted-foreground hover:text-foreground"
            )}
          >
            <Icone className="h-3.5 w-3.5" aria-hidden="true" />
            {comRotulo && <span>{opcao.rotulo}</span>}
          </button>
        );
      })}
    </div>
  );
}
