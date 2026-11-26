// Contrato de GET /api/reports/production/dashboard/{id} (DashboardEngine.Avaliar no backend).
export interface DashboardOption { key: string; label: string }
export interface DashboardActiveFilter { dimension: string; dimensionLabel: string; values: DashboardOption[] }
export interface DashboardKpi {
  key: string; label: string; detail: string; value: number | null; format: string;
  filterDimension: string | null; filterValue: string | null; selected: boolean;
}
export interface DashboardPoint { key: string; label: string; value: number; selected: boolean }
export interface DashboardChart {
  key: string; title: string; kind: "bar" | "column" | "donut"; dimension: string; measureLabel: string; measureFormat: string;
  points: DashboardPoint[];
}
export interface DashboardTable {
  columns: { key: string; label: string; type: string }[];
  rows: Record<string, unknown>[];
  total: number; page: number; pageSize: number;
  /** Dimensão aplicada ao clicar numa linha; cada linha traz __rowKey e __selected. */
  rowFilterDimension: string | null;
}
/** Célula da matriz compacta: clique = filtro filterDimension=filterValue (mesmo critério da contagem). */
export interface DashboardMatrixCell { row: string; column: string; value: number; filterDimension: string; filterValue: string; selected: boolean }
export interface DashboardMatrix { key: string; title: string; rows: DashboardOption[]; columns: DashboardOption[]; cells: DashboardMatrixCell[] }
/** Célula de gate da LDP Tipo 2 (coluna type "gate"). */
export interface GateCelula { rotulo: string; ativo: boolean; classe: string; documentStatus: string | null; situation: string | null }
export interface DashboardResult {
  id: string; title: string; source: string; totalRows: number; filteredRows: number; lastSync: string | null;
  dimensions: DashboardOption[]; activeFilters: DashboardActiveFilter[];
  kpis: DashboardKpi[]; charts: DashboardChart[]; table: DashboardTable; notes: string[];
  /** Filtro inicial sugerido pelo backend (ex.: semana atual) — aplicado só na primeira carga da tela. */
  defaultFilters: Record<string, string[]> | null;
  /** Matrizes compactas opcionais (hoje só LDP Tipo 2: status por fase 30/60/90). Ausente = nada a montar. */
  matrices?: DashboardMatrix[] | null;
  /** Metadados do dataset (ex.: { tipoLdp: 1 | 2 }). */
  meta?: Record<string, unknown> | null;
}
