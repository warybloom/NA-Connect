// =============================================================================================
// REGISTRY ÚNICO das telas (dashboards) do Controle de Produção. A navegação (sidebar/rotas, por variante
// Gerencial/Operacional) está em productionRoutes.ts e aponta para estes ids. A futura
// Gestão à Vista (playlist/TV) devem descobrir as telas SÓ por aqui — nunca duplicar esta lista.
// Toda tela recebe o escopo de projeto de fora: `selection: PmoSelecionado[]` (hoje sempre 1 projeto).
// =============================================================================================
import type { ComponentType } from "react";
import {
  AlertOctagon, BarChart3, CalendarCheck, CalendarRange, ClipboardCheck, ClipboardList, FileStack, FileWarning,
  GitPullRequest, PauseCircle, ShieldAlert, Users, type LucideIcon,
} from "lucide-react";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { createDashboardScreen } from "./dashboard/createDashboardScreen";

export type ProductionScreenId =
  | "ldp" | "nivelamento-programacao" | "controle-programacao" | "desvio-qualidade" | "cap-interno" | "cap-externo"
  | "change-order" | "controle-documental" | "hold" | "desvio-escopo" | "rnc" | "programacao";

export interface ProductionScreenProps { selection: PmoSelecionado[] }

export interface ProductionScreenDefinition {
  /** ID estável — usado na URL (?tela=) e, no futuro, na playlist da Gestão à Vista. Nunca renomear. */
  id: ProductionScreenId;
  label: string;
  /** Rótulo curto dos cards de navegação (o completo aparece ao passar o mouse). */
  shortLabel: string;
  icon: LucideIcon;
  component: ComponentType<ProductionScreenProps>;
  /** true SOMENTE se a tela implementada exibe valores financeiros (base para o filtro de TV). */
  containsCostData: boolean;
  /** Pode ser escolhida futuramente para a apresentação da Gestão à Vista. */
  tvSelectable: boolean;
  /** true SOMENTE onde a implementação realmente consolida vários projetos (useMergedPages). */
  supportsPortfolio: boolean;
  /** Fonte/endpoint — referência técnica, nunca exibida como dado. */
  source: string;
}

// Fase 3: cada tela é um DASHBOARD INTERATIVO (KPIs + gráficos + cross-filter + tabela resumida) servido por
// GET /api/reports/production/dashboard/{id} (backend: Services/Reports/Dashboards/ProductionDashboards.cs).
// containsCostData = false em todas: nenhum dashboard exibe valor financeiro (a antiga grade LdpDetailsSection
// com custo saiu da tela LDP). supportsPortfolio = true (30/09/2026): vários projetos = mesma definição sobre a união das
// linhas dos projetos no backend (ProductionDashboardsController, parâmetro "projetos").
const DASH = "GET /api/reports/production/dashboard/";

export const PRODUCTION_SCREENS: readonly ProductionScreenDefinition[] = [
  { id: "ldp", label: "LDP", shortLabel: "LDP", icon: FileStack, component: createDashboardScreen("ldp"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}ldp` },
  { id: "nivelamento-programacao", label: "Nivelamento Programação", shortLabel: "Nivelamento", icon: Users, component: createDashboardScreen("nivelamento-programacao"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}nivelamento-programacao` },
  { id: "controle-programacao", label: "Controle Programação", shortLabel: "Ctrl. Programação", icon: CalendarCheck, component: createDashboardScreen("controle-programacao"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}controle-programacao` },
  { id: "desvio-qualidade", label: "Desvio de Qualidade", shortLabel: "Desv. Qualidade", icon: ShieldAlert, component: createDashboardScreen("desvio-qualidade"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}desvio-qualidade` },
  { id: "cap-interno", label: "CAP Interno", shortLabel: "CAP Interno", icon: ClipboardList, component: createDashboardScreen("cap-interno"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}cap-interno` },
  { id: "cap-externo", label: "CAP Externo", shortLabel: "CAP Externo", icon: ClipboardCheck, component: createDashboardScreen("cap-externo"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}cap-externo` },
  { id: "change-order", label: "Change Order", shortLabel: "Change Order", icon: GitPullRequest, component: createDashboardScreen("change-order"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}change-order` },
  { id: "controle-documental", label: "Controle Documental", shortLabel: "Ctrl. Documental", icon: BarChart3, component: createDashboardScreen("controle-documental"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}controle-documental` },
  { id: "hold", label: "HOLD", shortLabel: "HOLD", icon: PauseCircle, component: createDashboardScreen("hold"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}hold` },
  { id: "desvio-escopo", label: "Desvio de Escopo", shortLabel: "Desv. Escopo", icon: FileWarning, component: createDashboardScreen("desvio-escopo"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}desvio-escopo` },
  { id: "rnc", label: "RNC", shortLabel: "RNC", icon: AlertOctagon, component: createDashboardScreen("rnc"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}rnc` },
  { id: "programacao", label: "Programação", shortLabel: "Programação", icon: CalendarRange, component: createDashboardScreen("programacao"),
    containsCostData: false, tvSelectable: true, supportsPortfolio: true, source: `${DASH}programacao` },
];

export const PRODUCTION_SCREEN_IDS = PRODUCTION_SCREENS.map(s => s.id);

export const getProductionScreen = (id: string | null | undefined): ProductionScreenDefinition | undefined =>
  PRODUCTION_SCREENS.find(s => s.id === id);
