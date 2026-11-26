import { Unplug } from "lucide-react";

/**
 * Selo discreto "Dado ainda não conectado" (PENDENTE_BACKEND) — usado pelas views do módulo
 * Reports/Gestão à Vista portado do ForgeSync enquanto o backend N&A real daquele indicador não existe.
 * Nunca remove o card/gráfico/tab: o layout e os componentes do ForgeSync continuam completos, só o
 * valor numérico é substituído por esta indicação (nunca um número inventado).
 */
export default function PendenteBackend({ label = "Dado ainda não conectado" }: { label?: string }) {
  return (
    <span
      title="PENDENTE_BACKEND — este indicador ainda não tem fonte real no N&A Connect"
      className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[10px] font-black uppercase tracking-wide bg-amber-500/10 border border-amber-500/30 text-amber-400 whitespace-nowrap"
    >
      <Unplug className="w-3 h-3" /> {label}
    </span>
  );
}
