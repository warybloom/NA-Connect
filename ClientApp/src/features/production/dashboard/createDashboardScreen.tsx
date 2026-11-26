import type { ComponentType } from "react";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import ProductionDashboard from "./ProductionDashboard";

/**
 * Tela do registry = dashboard interativo `id` (mesmo id do backend ProductionDashboards.Todos).
 * Vários projetos (TV): a seleção inteira vai ao backend, que avalia a MESMA definição sobre a união das linhas dos projetos.
 */
export function createDashboardScreen(id: string): ComponentType<{ selection: PmoSelecionado[] }> {
  const Tela = ({ selection }: { selection: PmoSelecionado[] }) => <ProductionDashboard dashboardId={id} selection={selection} />;
  Tela.displayName = `ProductionDashboard(${id})`;
  return Tela;
}
