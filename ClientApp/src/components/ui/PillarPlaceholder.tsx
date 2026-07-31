/**
 * PillarPlaceholder.tsx
 * Card elegante exibido nos pilares ainda não implementados (Fase C+).
 */

import { type LucideIcon, Construction, Layers3 } from "lucide-react";
import { cn } from "@/lib/utils";

interface PillarPlaceholderProps {
  pillar:      string;
  module:      string;
  description: string;
  Icon?:       LucideIcon;
  phase?:      string;
  className?:  string;
}

export function PillarPlaceholder({
  pillar,
  module,
  description,
  Icon = Layers3,
  phase = "Fase C",
  className,
}: PillarPlaceholderProps) {
  return (
    <div className={cn("flex flex-col items-center justify-center min-h-[55vh] text-center select-none", className)}>
      {/* Anel decorativo */}
      <div className="relative mb-8">
        <div className="absolute inset-0 rounded-full bg-accent/10 blur-2xl scale-150" />
        <div className="relative flex h-20 w-20 items-center justify-center rounded-2xl border border-accent/20 bg-accent/5 shadow-lg shadow-accent/5">
          <Icon className="h-9 w-9 text-accent/60" />
        </div>
        {/* Badge de fase */}
        <div className="absolute -top-2 -right-2 flex items-center gap-1 rounded-full border border-accent/30 bg-card px-2 py-0.5 shadow">
          <Construction className="h-3 w-3 text-accent/70" />
          <span className="text-[9px] font-black uppercase tracking-widest text-accent/70">{phase}</span>
        </div>
      </div>

      {/* Textos */}
      <p className="text-[10px] font-black uppercase tracking-[0.3em] text-accent/50 mb-1">
        {pillar}
      </p>
      <h2 className="text-xl font-black tracking-tight text-foreground/90 uppercase italic mb-3">
        {module}
      </h2>
      <p className="max-w-sm text-sm text-muted-foreground leading-relaxed">
        {description}
      </p>

      {/* Linha decorativa */}
      <div className="mt-8 flex items-center gap-3 opacity-30">
        <div className="h-px w-12 bg-accent" />
        <div className="h-1.5 w-1.5 rounded-full bg-accent" />
        <div className="h-px w-12 bg-accent" />
      </div>
    </div>
  );
}
