export { WeeklyPlanningPage } from './weekly/WeeklyPlanningPage';
export { PlannerDashboardPage } from './planner/PlannerDashboardPage';
export { CoordinationStandardsPage } from './coordination/CoordinationStandardsPage';
// Mounted under the application's existing ProtectedRoute and Layout.
export const planningRoutePaths = {
  weekly: '/planejamento/programacao-semanal', // redireciona para weeklyCurrent
  weeklyCurrent: '/planejamento/programacao-semanal/vigente',
  weeklyPrevious: '/planejamento/programacao-semanal/semana-anterior',
  weeklyHistogram: '/planejamento/programacao-semanal/histograma',
  planner: '/planejamento/painel-do-planejador',
  coordination: '/planejamento/normas-de-coordenacao',
} as const;
