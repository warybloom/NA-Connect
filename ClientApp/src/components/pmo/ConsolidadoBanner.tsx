import type { FalhaProjeto } from "./useMergedPages";
import { rotuloConsolidado, type PmoSelecionado } from "./pmoMultiCore";

/**
 * Faixa "CONSOLIDADO — N PROJETOS": deixa explícito que a tela soma todos os projetos selecionados (nunca só o primeiro/último) e lista, sem
 * quebrar a tela, os projetos que não devolveram dados naquele domínio. Com 1 projeto não mostra nada.
 */
export default function ConsolidadoBanner({ selection, falhas = [] }: { selection: PmoSelecionado[]; falhas?: FalhaProjeto[] }) {
  const rotulo = rotuloConsolidado(selection.length);
  if (!rotulo && falhas.length === 0) return null;
  return (
    <span className="block text-[11px] mt-1" data-testid="banner-consolidado">
      {rotulo && <span className="font-black text-amber-400">{rotulo}</span>}
      {rotulo && <span className="text-slate-400"> · {selection.map(p => p.name).join(' · ')}</span>}
      {falhas.length > 0 && <span className="block text-amber-300">Sem dados de: {falhas.map(f => `${f.projectName} (${f.message})`).join('; ')}</span>}
    </span>
  );
}
