import { useEffect, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { ApiError, getJson } from "@/lib/api";
import { limitedMap } from "./pmoData";
import { planSlice, serverPagesFor, type PmoSelecionado } from "./pmoMultiCore";

export interface PagedReport<T> { totalRows: number; filteredRows: number; lastSync: string | null; page: number; pageSize: number; rows: T[] }
export type MergedRow<T> = T & { __projectId: string; __projectName: string };
export interface FalhaProjeto { projectId: string; projectName: string; message: string }

/**
 * Lista CONSOLIDADA de N projetos sobre endpoints paginados por projeto (mesmo padrão da Gestão à Vista: o front busca por projeto e agrega).
 * A página global é a concatenação, na ordem da seleção, das páginas de cada projeto; só as páginas do servidor necessárias são buscadas.
 * Projeto que falha (sem acesso, sem dado) NÃO derruba a tela: entra em `falhas` e os demais continuam aparecendo. `error` só quando TODOS falham.
 * Nenhuma linha é deduplicada entre projetos: a identidade é (projeto, linha).
 */
export function useMergedPages<T>(opts: { endpoint: string; projects: PmoSelecionado[]; params: Record<string, string>; page: number; pageSize: number }) {
  const { endpoint, projects, params, page, pageSize } = opts;
  const [state, setState] = useState<{ rows: MergedRow<T>[]; filteredRows: number; totalRows: number; lastSync: string | null; falhas: FalhaProjeto[] }>(
    { rows: [], filteredRows: 0, totalRows: 0, lastSync: null, falhas: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  const cache = useRef(new Map<string, PagedReport<T>>());
  const projectsKey = JSON.stringify(projects.map(p => [p.hubId, p.id]));
  const paramsKey = JSON.stringify(params);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined);
    const fetchPage = async (p: PmoSelecionado, n: number) => {
      const key = `${endpoint}|${paramsKey}|${p.id}|${pageSize}|${n}`;
      const hit = cache.current.get(key); if (hit && attempt === 0) return hit;
      const q = new URLSearchParams({ hubId: p.hubId, projectId: p.id, ...params, page: String(n), pageSize: String(pageSize) });
      const r = await getJson<PagedReport<T>>(`${endpoint}?${q}`, abort.signal);
      cache.current.set(key, r); return r;
    };
    (async () => {
      const falhas: FalhaProjeto[] = [];
      const primeiras = await limitedMap(projects, async p => {
        try { return await fetchPage(p, 1); }
        catch (e) { if (abort.signal.aborted) throw e; falhas.push({ projectId: p.id, projectName: p.name, message: e instanceof ApiError ? e.message : 'Falha ao consultar o projeto.' }); return null; }
      });
      if (abort.signal.aborted) return;
      if (falhas.length === projects.length) { setError(falhas[0]?.message ?? 'Não foi possível carregar os dados. Tente novamente.'); return; }
      const counts = primeiras.map(r => r?.filteredRows ?? 0);
      const rows: MergedRow<T>[] = [];
      for (const part of planSlice(counts, page, pageSize)) {
        const proj = projects[part.project];
        const pages = await Promise.all(serverPagesFor(part.from, part.to, pageSize).map(n => n === 1 ? Promise.resolve(primeiras[part.project]!) : fetchPage(proj, n)));
        const base = (serverPagesFor(part.from, part.to, pageSize)[0] - 1) * pageSize;
        pages.flatMap(r => r.rows).slice(part.from - base, part.to - base)
          .forEach(row => rows.push({ ...row, __projectId: proj.id, __projectName: proj.name }));
      }
      const datas = primeiras.map(r => r?.lastSync).filter((d): d is string => !!d).sort();
      setState({ rows, filteredRows: counts.reduce((a, b) => a + b, 0), totalRows: primeiras.reduce((a, r) => a + (r?.totalRows ?? 0), 0),
        lastSync: datas.length ? datas[datas.length - 1] : null, falhas });
    })().catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar os dados. Tente novamente.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [endpoint, projectsKey, paramsKey, page, pageSize, attempt]);

  return { ...state, loading, error, totalPages: Math.max(1, Math.ceil(state.filteredRows / pageSize)), retry: () => setAttempt(a => a + 1) };
}

/** Coluna "Projeto" (só no consolidado) prefixada às colunas da aba. */
export function comColunaProjeto<T>(columns: ColumnDef<MergedRow<T>, any>[], multi: boolean): ColumnDef<MergedRow<T>, any>[] {
  if (!multi) return columns;
  return [{ id: '__projeto', accessorKey: '__projectName', header: 'Projeto',
    cell: ({ getValue }) => getValue() as string } as ColumnDef<MergedRow<T>, any>, ...columns];
}
