import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import TimesheetUnassignedAuditPage from "@/features/planning/TimesheetUnassignedAuditPage";
import OutsideLdpQualityPage from "./OutsideLdpQualityPage";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";

export default function TimesheetUnassignedQualityPage() {
  const { projetoAtual } = useProjetoAtual();
  const { loading, can } = useProjectPermissions(projetoAtual?.projectId);
  if (loading) return null;

  return (
    <div className="space-y-3 animate-enter">
      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
        <span>Qualidade</span><span>›</span><span className="text-foreground">HH sem documento</span>
        {projetoAtual?.nome && <span className="normal-case tracking-normal">· {projetoAtual.nome}</span>}
      </div>

      {can("quality.outside-ldp.view") && <OutsideLdpQualityPage projectId={projetoAtual?.projectId} canEdit={can("quality.outside-ldp.edit")} />}
      {can("quality.unassigned.view") && <TimesheetUnassignedAuditPage projectId={projetoAtual?.projectId} canEdit={can("quality.unassigned.edit")} />}
    </div>
  );
}
