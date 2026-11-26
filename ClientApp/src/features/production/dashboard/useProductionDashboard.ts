import { useEffect, useState } from "react";
import { ApiError, getJson } from "@/lib/api";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import type { DashboardResult } from "./types";

/**
 * Carrega UMA dashboard para selection[0] com os filtros ativos. Cada mudança (filtro/página) aborta a
 * requisição anterior — nunca aplica resposta atrasada. O dado anterior do MESMO projeto fica visível
 * (esmaecido) só enquanto a nova resposta chega; troca de projeto remonta a tela (ProductionScreen usa
 * `key` por projeto), então nunca aparece número de outro projeto.
 */
export function useProductionDashboard(dashboardId: string, selection: PmoSelecionado[], filtros: [string, string][], page: number, pageSize = 15) {
  const projeto = selection[0];
  const [data, setData] = useState<DashboardResult>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  const chaveFiltros = JSON.stringify(filtros);
  const chaveSelecao = selection.map((p) => p.id).join("|");

  useEffect(() => {
    if (!projeto) return;
    const abort = new AbortController();
    setLoading(true); setError(undefined);
    // Multiprojeto (TV): o 1º é o principal; os demais vão em "projetos" e o backend avalia a mesma definição sobre a união.
    const extras = selection.slice(1).map((p): [string, string] => ["projetos", p.id]);
    const q = new URLSearchParams([["hubId", projeto.hubId], ["projectId", projeto.id], ["page", String(page)], ["pageSize", String(pageSize)], ...extras, ...filtros]);
    getJson<DashboardResult>(`/api/reports/production/dashboard/${encodeURIComponent(dashboardId)}?${q}`, abort.signal)
      .then(r => { if (!abort.signal.aborted) setData(r); })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : "Não foi possível carregar o dashboard."); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dashboardId, projeto?.hubId, chaveSelecao, chaveFiltros, page, pageSize, attempt]);

  return { data, loading, error, retry: () => setAttempt(a => a + 1) };
}
