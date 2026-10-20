// BACKEND_PENDENTE: auditoria/mapeamento Plant3D (api/plant/*) nao existe no N&A. UI transplantada literalmente do ForgeSync. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
import DashboardHeader from "@/components/plant3d/DashboardHeader";
import MainTabs from "@/components/plant3d/MainTabs";

const Plant3DDashboard = () => {
  return (
    <div className="min-h-screen bg-background">
      <DashboardHeader />
      <div className="py-6">
        <MainTabs />
      </div>
    </div>
  );
};

export default Plant3DDashboard;