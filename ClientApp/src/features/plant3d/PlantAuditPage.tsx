// Auditoria P&ID × 3D: reutiliza a tela de resultados existente (TabDashboard).
import TabDashboard from "@/components/plant3d/TabDashboard";

export default function PlantAuditPage({ onOpenSettings }: { onOpenSettings?: () => void }) {
  return <TabDashboard onOpenSettings={onOpenSettings} />;
}
