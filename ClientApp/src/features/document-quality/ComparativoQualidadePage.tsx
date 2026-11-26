import ResumoProducaoLabPage from "@/features/production/analytics/ResumoProducaoLabPage";
import SeletorProjetosAnalise from "@/features/project/SeletorProjetosAnalise";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { paraSelecao } from "@/features/project/projetoAnalise";

const keys = ["quality.comparison.view"];

export default function ComparativoQualidadePage() {
  const { projetoAtual, projetosAnalise } = useProjetoAtual();
  const selected = projetoAtual ? paraSelecao(projetosAnalise.length ? projetosAnalise : [projetoAtual]) : [];
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-xs font-bold uppercase tracking-widest text-muted-foreground">Qualidade · Conferência gerencial</p>
          <h1 className="text-xl font-black">Comparativo de HH e Custos</h1>
          <p className="text-xs text-muted-foreground">Compare as fontes e use Auditoria de Divergências para localizar o problema.</p>
        </div>
        {projetoAtual && <SeletorProjetosAnalise keys={keys} />}
      </div>
      {selected.length ? <ResumoProducaoLabPage selection={selected} /> : 
        <p className="rounded-xl border border-border p-4">Selecione um projeto na Home.</p>}
    </div>
  );
}