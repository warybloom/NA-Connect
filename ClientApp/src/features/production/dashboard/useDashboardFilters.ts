import { useCallback, useMemo, useState } from "react";
import { alternarFiltro, removerFiltro, serializarFiltros, type DashboardFilters } from "./filtrosLogic";

/**
 * Filtros de cross-filter de UMA dashboard (estado local da tela — nunca global entre telas).
 * dimensão → valores selecionados (OR dentro da dimensão, AND entre dimensões — mesma regra do backend).
 * Serialização: f.<dimensão>=<valor> (repetível), lida por ProductionDashboardsController.
 */
export type { DashboardFilters };

export function useDashboardFilters(inicial: () => DashboardFilters = () => ({})) {
  const [filters, setFilters] = useState<DashboardFilters>(inicial);

  const toggle = useCallback((dimension: string, value: string) => setFilters(a => alternarFiltro(a, dimension, value)), []);
  const remove = useCallback((dimension: string, value: string) => setFilters(a => removerFiltro(a, dimension, value)), []);
  const clear = useCallback(() => setFilters({}), []);
  const replace = useCallback((novos: DashboardFilters) => setFilters(novos), []);
  const isSelected = useCallback((dimension: string, value: string) => (filters[dimension] ?? []).includes(value), [filters]);
  const queryEntries = useMemo(() => serializarFiltros(filters), [filters]);

  return { filters, toggle, remove, clear, replace, isSelected, queryEntries, hasFilters: Object.keys(filters).length > 0 };
}
