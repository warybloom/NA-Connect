// Duas telas Plant 3D independentes: uma rota por entrada do sidebar.
// URLs antigas ?tab= são redirecionadas para os endereços canônicos.
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import TabDashboard from "@/components/plant3d/TabDashboard";
import TabSettings from "@/components/plant3d/TabSettings";

export function Plant3DResultsPage() {
  const navigate = useNavigate();
  return <div className="min-h-full"><TabDashboard onOpenSettings={() => navigate("/plant3d/configuracao")} /></div>;
}

export function Plant3DMappingPage() {
  return <div className="min-h-full"><TabSettings /></div>;
}

export default function Plant3DDashboard() {
  const { search } = useLocation();
  const tab = new URLSearchParams(search).get("tab");
  return <Navigate to={tab === "mapping" || tab === "tag-rules" || tab === "database"
    ? "/plant3d/configuracao" : "/plant3d/resultados"} replace />;
}
