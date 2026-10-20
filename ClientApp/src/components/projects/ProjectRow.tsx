// Linha de projeto ACC/BIM 360 (lista compacta da Home). Mesmos dados e ação do ProjectCard (que continua no CDE):
// ícone · nome · plataforma · status · ação. A linha inteira é clicável e acessível por teclado.
import { ChevronRight, Folder } from "lucide-react";

interface ProjectRowProps {
  name: string;
  /** "type" da extensão do projeto na ACC (identifica BIM 360 x ACC). */
  extensionType: string;
  /** Projeto existe na Autodesk mas ainda não foi processado pelo robô do N&A (sem registro em dbo.Projetos). */
  naoRegistrado: boolean;
  actionLabel: string;
  onClick: () => void;
}

export function ProjectRow({ name, extensionType, naoRegistrado, actionLabel, onClick }: ProjectRowProps) {
  const plataforma = extensionType.includes("bim360") ? "BIM 360" : "ACC";
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onClick(); } }}
      className="group grid cursor-pointer grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-4 border-b border-border/50 px-4 py-3 transition-colors last:border-b-0 hover:bg-primary/5 focus-visible:bg-primary/5 focus-visible:outline-none sm:grid-cols-[auto_minmax(0,1fr)_110px_150px_auto]"
    >
      <span className="flex h-9 w-9 items-center justify-center rounded-md bg-primary/10 text-primary">
        <Folder className="h-4 w-4" />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-semibold text-foreground">{name}</span>
        <span className="block text-[11px] text-muted-foreground sm:hidden">{plataforma}</span>
      </span>
      <span className="hidden text-[11px] font-black uppercase tracking-widest text-muted-foreground sm:block">{plataforma}</span>
      <span className="hidden sm:block">
        {naoRegistrado ? (
          <span title="Este projeto existe na Autodesk mas ainda não foi processado pelo robô do N&A."
            className="inline-block rounded border border-amber-500/30 px-1.5 py-0.5 text-[9px] font-black uppercase tracking-widest text-amber-500/80">
            Não registrado
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-emerald-500">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />Registrado no N&amp;A
          </span>
        )}
      </span>
      <span className="flex items-center gap-1 text-[10px] font-black uppercase tracking-widest text-muted-foreground group-hover:text-primary">
        <span className="hidden md:inline">{actionLabel}</span>
        <ChevronRight className="h-4 w-4" />
      </span>
    </div>
  );
}
