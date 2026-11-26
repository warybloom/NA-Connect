// Card de projeto ACC/BIM 360 — extraído de Dashboard.tsx (mesmo visual) para ser reutilizado pelo CDE.
import { Folder } from "lucide-react";

interface ProjectCardProps {
  name: string;
  /** "type" da extensão do projeto na ACC (identifica BIM 360 x ACC). */
  extensionType: string;
  /** Projeto existe na Autodesk mas ainda não foi processado pelo robô do N&A (sem registro em dbo.Projetos). */
  naoRegistrado: boolean;
  actionLabel: string;
  onClick: () => void;
}

export function ProjectCard({ name, extensionType, naoRegistrado, actionLabel, onClick }: ProjectCardProps) {
  return (
    <div
      onClick={onClick}
      className="group cursor-pointer glass-card p-6 rounded-xl border border-border/50 hover:border-primary/50 transition-all duration-300"
    >
      <div className="w-12 h-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4 group-hover:scale-110 transition-transform">
        <Folder className="w-6 h-6 text-primary" />
      </div>
      <h3 className="font-bold text-foreground line-clamp-2 min-h-[2.5rem]">{name}</h3>
      {naoRegistrado && (
        <span title="Este projeto existe na Autodesk mas ainda não foi processado pelo robô do N&A." className="inline-block mb-2 text-[9px] font-black uppercase tracking-widest text-amber-500/80 border border-amber-500/30 rounded px-1.5 py-0.5">
          Não registrado
        </span>
      )}
      <div className="mt-4 flex items-center justify-between text-[10px] text-muted-foreground uppercase tracking-widest font-black">
        <span className="opacity-60">{extensionType.includes("bim360") ? "BIM 360" : "ACC"}</span>
        <span className="opacity-0 group-hover:opacity-100 transition-opacity text-primary italic font-black text-[9px]">{actionLabel}</span>
      </div>
    </div>
  );
}
