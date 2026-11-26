// Lógica pura dos filtros de cross-filter (usada por useDashboardFilters e testada em __tests__).
export type DashboardFilters = Record<string, string[]>;

export function alternarFiltro(atual: DashboardFilters, dimensao: string, valor: string): DashboardFilters {
  const valores = atual[dimensao] ?? [];
  return comValores(atual, dimensao, valores.includes(valor) ? valores.filter(v => v !== valor) : [...valores, valor]);
}

export function removerFiltro(atual: DashboardFilters, dimensao: string, valor: string): DashboardFilters {
  return comValores(atual, dimensao, (atual[dimensao] ?? []).filter(v => v !== valor));
}

function comValores(atual: DashboardFilters, dimensao: string, valores: string[]): DashboardFilters {
  const copia = { ...atual };
  if (valores.length === 0) delete copia[dimensao]; else copia[dimensao] = valores;
  return copia;
}

/** Pares f.&lt;dimensão&gt;=valor em ordem estável (ordem de clique não muda a consulta nem o cache). */
export function serializarFiltros(filtros: DashboardFilters): [string, string][] {
  return Object.keys(filtros).sort().flatMap(d => [...filtros[d]].sort().map(v => [`f.${d}`, v] as [string, string]));
}

/**
 * Filtros iniciais vindos da URL (f.<dimensão>=valor, mesmo formato da API) — usados pelo drill-down do Resumo:
 * o card abre esta dashboard já com a MESMA dimensão/valor que gerou o número (card = lista, por construção).
 */
export function lerFiltrosDaUrl(search: string): DashboardFilters {
  const out: DashboardFilters = {};
  new URLSearchParams(search).forEach((valor, chave) => {
    if (!chave.startsWith("f.") || chave.length <= 2 || !valor) return;
    const dim = chave.slice(2);
    if (!(out[dim] ?? []).includes(valor)) out[dim] = [...(out[dim] ?? []), valor];
  });
  return out;
}
