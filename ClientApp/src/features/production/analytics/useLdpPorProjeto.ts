// Resultado da ABA LDP (/api/reports/production/dashboard/ldp, sem filtros) de cada projeto analisado — mesmos KPIs e
// matriz por fase que a aba mostra. O Resumo só lê; nenhuma contagem documental é refeita no frontend.
import { useEffect, useState } from "react";
import { ApiError, getJson } from "@/lib/api";
import { limitedMap } from "@/components/pmo/pmoData";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import type { DashboardResult } from "../dashboard/types";
import type { LdpProjeto } from "./resumoLogic";

export function useLdpPorProjeto(selection: PmoSelecionado[]) {
  const chave = JSON.stringify(selection.map((p) => [p.hubId, p.id, p.name]));
  const [estado, setEstado] = useState<{ projetos: LdpProjeto[]; erro: string | null; carregando: boolean }>({ projetos: [], erro: null, carregando: true });

  useEffect(() => {
    const lista: [string, string, string][] = JSON.parse(chave);
    if (lista.length === 0) { setEstado({ projetos: [], erro: null, carregando: false }); return; }
    const abort = new AbortController();
    setEstado({ projetos: [], erro: null, carregando: true });
    limitedMap(lista, async ([hubId, projectId, nome]) => {
      const q = new URLSearchParams({ hubId, projectId, page: "1", pageSize: "1" });
      const r = await getJson<DashboardResult>(`/api/reports/production/dashboard/ldp?${q}`, abort.signal);
      return {
        projectId, hubId, nome, tipoLdp: Number(r.meta?.tipoLdp ?? 1),
        kpis: r.kpis.map((k) => ({ key: k.key, label: k.label, value: k.value, filterDimension: k.filterDimension, filterValue: k.filterValue })),
        celulas: (r.matrices ?? []).flatMap((m) => m.cells.map((c) => ({ row: c.row, column: c.column, value: c.value, filterDimension: c.filterDimension, filterValue: c.filterValue }))),
      } satisfies LdpProjeto;
    })
      .then((projetos) => { if (!abort.signal.aborted) setEstado({ projetos, erro: null, carregando: false }); })
      .catch((e) => {
        if (abort.signal.aborted) return;
        // Total parcial seria enganoso: qualquer projeto sem resposta = sem números documentais.
        const msg = e instanceof ApiError && e.status === 403 ? "Sem permissão para a aba LDP de um dos projetos." : e instanceof Error ? e.message : "Não foi possível carregar a aba LDP.";
        setEstado({ projetos: [], erro: msg, carregando: false });
      });
    return () => abort.abort();
  }, [chave]);

  return estado;
}
