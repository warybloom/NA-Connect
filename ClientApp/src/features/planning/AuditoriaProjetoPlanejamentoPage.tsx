// Painel de Planejamento → Auditoria do Projeto: a mesma tela, no projeto atual (Project_ID_ACC escolhido na Home).
import { AuditoriaProjetoPage } from "@/features/production/analytics/ProductionAuditPage";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";

export default function AuditoriaProjetoPlanejamentoPage() {
  const { projetoAtual } = useProjetoAtual();
  return (
    <div className="space-y-3 animate-enter">
      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
        <span>Painel de Planejamento</span><span>›</span><span className="text-foreground">Auditoria do Projeto</span>
        {projetoAtual?.nome && <span className="normal-case tracking-normal">· {projetoAtual.nome}</span>}
      </div>
      <AuditoriaProjetoPage projectId={projetoAtual?.projectId} />
    </div>
  );
}
