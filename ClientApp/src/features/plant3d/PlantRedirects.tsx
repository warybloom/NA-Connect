// Redirecionamentos das rotas antigas do Plant 3D (favoritos/histórico não quebram).
import { Navigate, useParams } from "react-router-dom";
import { rotaFase } from "./plantRoutes";

/** /plant3d/resultados/geral/fase/:id → primeira tela (Quantitativa) da mesma fase. */
export function GeralFaseRedirect() {
  const { id = "" } = useParams();
  return <Navigate to={rotaFase(id, "quantitativa")} replace />;
}
