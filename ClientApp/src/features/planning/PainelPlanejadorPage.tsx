// Painel de Planejamento → Painel do Planejador (30/09/2026, ajuste): SÓ o painel do planejador.
// A listagem/configuração de projetos saiu daqui e virou item próprio do sidebar ("Configuração do Projeto",
// /planejamento/configuracao-do-projeto). Nenhum bloco de configuração LDP é exibido nesta tela — cada etapa da LDP
// é uma página/rota individual no sidebar.
import { PlannerDashboardPage } from "./planner/PlannerDashboardPage";

export default function PainelPlanejadorPage() {
  return <PlannerDashboardPage />;
}
