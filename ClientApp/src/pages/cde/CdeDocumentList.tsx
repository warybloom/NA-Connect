// CONTROLE DE PRODUÇÃO / LISTA DE DOCUMENTOS — visão da LDP ESPELHO do projeto (Tipo 1 ou Tipo 2), lida do banco via
// GET /api/reports/cde/document-list. As COLUNAS vêm do backend (mesma intenção de campos do gerador de espelho de
// cada tipo) — este componente não conhece nomes de colunas.
// Grade (30/09/2026) com @tanstack/react-table (já instalado): largura total, rolagem horizontal/vertical com
// cabeçalho fixo, redimensionar (arrastar a borda), reordenar (arrastar o cabeçalho), ordenar (servidor), mostrar/
// ocultar colunas e itens por página. Tudo isso (ordem, larguras, colunas visíveis, ordenação e itens por página) é
// PREFERÊNCIA DO USUÁRIO POR PROJETO, gravada pela camada central (features/preferences) — ao voltar, a grade está
// exatamente como ele deixou.
// Pesquisa, disciplina, ordenação e paginação continuam no SERVIDOR (nenhum filtro "só da página" enganoso).
import { useEffect, useMemo, useRef, useState } from "react";
import {
  getCoreRowModel, useReactTable,
  type ColumnDef, type ColumnOrderState, type ColumnSizingState, type VisibilityState,
} from "@tanstack/react-table";
import { ArrowDown, ArrowUp, ArrowUpDown, Columns3, GripVertical, RotateCcw, Search, X } from "lucide-react";
import { ApiError, getJson } from "@/lib/api";
import { LoadingState, ReportState } from "@/components/reports/ReportComponents";
import {
  DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn } from "@/lib/utils";
import { useUserPreference } from "@/features/preferences/useUserPreference";
import { ehObjeto } from "@/features/preferences/userPreferences";

interface Coluna { key: string; label: string; type: "texto" | "data" | "numero" | "marcador"; sortable: boolean }
interface ListaDocumentos {
  tipoLdp: number; columns: Coluna[]; totalRows: number; filteredRows: number; lastSync: string | null;
  page: number; pageSize: number; rows: Record<string, unknown>[];
}
type Linha = Record<string, unknown>;

const TAMANHOS_PAGINA = [25, 50, 100] as const; // backend aceita até 100
type Ordenacao = { key: string; dir: "asc" | "desc" } | null;
/** Preferência visual da grade (usuário × projeto × "lista-documentos" × "grid"). */
export interface PreferenciasGrade { order: ColumnOrderState; sizing: ColumnSizingState; visibility: VisibilityState; pageSize: number; sort: Ordenacao }
export const GRADE_PADRAO: PreferenciasGrade = { order: [], sizing: {}, visibility: {}, pageSize: 50, sort: null };

/** Preferência salva → válida (campos corrompidos/antigos voltam ao padrão, nunca quebram a tela). */
export function sanearGrade(p: Partial<PreferenciasGrade> | null | undefined): PreferenciasGrade {
  const obj = (v: unknown) => (v && typeof v === "object" && !Array.isArray(v) ? v : {});
  return {
    order: Array.isArray(p?.order) ? p!.order.filter((x) => typeof x === "string") : [],
    sizing: obj(p?.sizing) as ColumnSizingState,
    visibility: obj(p?.visibility) as VisibilityState,
    pageSize: TAMANHOS_PAGINA.includes(p?.pageSize as (typeof TAMANHOS_PAGINA)[number]) ? p!.pageSize! : 50,
    sort: p?.sort && typeof p.sort.key === "string" && (p.sort.dir === "asc" || p.sort.dir === "desc") ? p.sort : null,
  };
}

function formatar(valor: unknown, tipo: Coluna["type"]) {
  if (valor === null || valor === undefined || valor === "") return "—";
  if (tipo === "data" && typeof valor === "string") return new Date(valor).toLocaleDateString("pt-BR", { timeZone: "UTC" });
  return String(valor);
}

const larguraPadrao = (c: Coluna) => (c.type === "texto" ? 220 : c.type === "data" ? 120 : 110);

export default function CdeDocumentList({ hubId, projectId, projectName }: { hubId: string; projectId: string; projectName: string }) {
  const [busca, setBusca] = useState("");
  const [buscaAplicada, setBuscaAplicada] = useState("");
  const [disciplina, setDisciplina] = useState("");
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);
  const [dados, setDados] = useState<ListaDocumentos>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  const [gradeSalva, setGrade, { pronto }] = useUserPreference<PreferenciasGrade>("lista-documentos", "grid", GRADE_PADRAO,
    { projeto: projectId, validar: ehObjeto<PreferenciasGrade>() });
  const grade = useMemo(() => sanearGrade(gradeSalva), [gradeSalva]);
  const { order: columnOrder, sizing: columnSizing, visibility: columnVisibility, pageSize, sort } = grade;
  const atualizar = <K extends keyof PreferenciasGrade>(campo: K, v: PreferenciasGrade[K] | ((a: PreferenciasGrade[K]) => PreferenciasGrade[K])) =>
    setGrade((g) => { const atual = sanearGrade(g); return { ...atual, [campo]: typeof v === "function" ? (v as (a: PreferenciasGrade[K]) => PreferenciasGrade[K])(atual[campo]) : v }; });
  const setColumnOrder = (v: ColumnOrderState | ((a: ColumnOrderState) => ColumnOrderState)) => atualizar("order", v);
  const setColumnSizing = (v: ColumnSizingState | ((a: ColumnSizingState) => ColumnSizingState)) => atualizar("sizing", v);
  const setColumnVisibility = (v: VisibilityState | ((a: VisibilityState) => VisibilityState)) => atualizar("visibility", v);
  const setPageSize = (n: number) => atualizar("pageSize", n);
  const setSort = (v: Ordenacao | ((a: Ordenacao) => Ordenacao)) => atualizar("sort", v);
  const arrastando = useRef<string | null>(null);

  // Busca aplicada com pequeno atraso para não consultar a cada tecla.
  useEffect(() => {
    const t = setTimeout(() => { setBuscaAplicada(busca.trim()); setPage(1); }, 350);
    return () => clearTimeout(t);
  }, [busca]);

  useEffect(() => {
    if (!pronto) return; // espera as preferências do usuário (ordenação/itens por página) para não consultar duas vezes
    const abort = new AbortController();
    setLoading(true); setError(undefined);
    const q = new URLSearchParams({ hubId, projectId, search: buscaAplicada, disciplina: disciplina.trim(),
      page: String(page), pageSize: String(pageSize), sort: sort?.key ?? "", dir: sort?.dir ?? "" });
    getJson<ListaDocumentos>(`/api/reports/cde/document-list?${q}`, abort.signal)
      .then(d => { if (!abort.signal.aborted) setDados(d); })
      .catch(e => { if (!abort.signal.aborted) { setDados(undefined); setError(e instanceof ApiError ? e.message : "Não foi possível carregar a lista de documentos."); } })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [hubId, projectId, buscaAplicada, disciplina, sort?.key, sort?.dir, page, pageSize, attempt, pronto]);

  const colunasApi = dados?.columns;
  const columns = useMemo<ColumnDef<Linha>[]>(() => (colunasApi ?? []).map(c => ({
    id: c.key,
    header: c.label,
    accessorFn: (row: Linha) => row[c.key],
    size: larguraPadrao(c),
    minSize: 60,
    maxSize: 800,
    enableSorting: c.sortable,
    meta: c,
  })), [colunasApi]);

  const table = useReactTable({
    data: dados?.rows ?? [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    manualSorting: true,
    manualPagination: true,
    columnResizeMode: "onChange",
    state: { columnOrder, columnSizing, columnVisibility },
    onColumnOrderChange: setColumnOrder,
    onColumnSizingChange: setColumnSizing,
    onColumnVisibilityChange: setColumnVisibility,
  });

  const alternarOrdem = (c: Coluna) => {
    if (!c.sortable) return;
    setPage(1);
    setSort(s => (s?.key !== c.key ? { key: c.key, dir: "asc" } : s.dir === "asc" ? { key: c.key, dir: "desc" } : null));
  };

  const soltarEm = (destino: string) => {
    const origem = arrastando.current;
    arrastando.current = null;
    if (!origem || origem === destino) return;
    const atual = table.getAllLeafColumns().map(c => c.id);
    const ordem = columnOrder.length ? [...columnOrder.filter(id => atual.includes(id)), ...atual.filter(id => !columnOrder.includes(id))] : atual;
    const de = ordem.indexOf(origem), para = ordem.indexOf(destino);
    if (de < 0 || para < 0) return;
    ordem.splice(para, 0, ordem.splice(de, 1)[0]);
    setColumnOrder(ordem);
  };

  const restaurar = () => setGrade({ ...GRADE_PADRAO, pageSize, sort });

  const redimensionando = !!table.getState().columnSizingInfo.isResizingColumn;
  const totalPages = dados ? Math.max(1, Math.ceil(dados.filteredRows / pageSize)) : 1;
  const visiveis = table.getVisibleLeafColumns().length;
  const campo = "h-8 rounded-md border border-border bg-background px-2.5 text-xs text-foreground";

  return (
    <section aria-label="Lista de Documentos" className="flex w-full min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="mr-auto min-w-0">
          <h2 className="text-sm font-black uppercase tracking-[0.2em]">Lista de Documentos</h2>
          <p className="truncate text-[11px] text-muted-foreground">
            {projectName}{dados ? ` · Espelho Tipo ${dados.tipoLdp}` : ""}
            {dados?.lastSync ? ` · Última sincronização: ${new Date(dados.lastSync).toLocaleString("pt-BR")}` : ""}
          </p>
        </div>
        <span className="relative">
          <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input aria-label="Pesquisar (N&A, cliente, título)" placeholder="Pesquisar N&A, cliente, título" value={busca}
            onChange={e => setBusca(e.target.value)} className={cn(campo, "w-60 pl-8")} />
        </span>
        <input aria-label="Disciplina (texto exato)" placeholder="Disciplina (exata)" value={disciplina}
          onChange={e => { setDisciplina(e.target.value); setPage(1); }} className={cn(campo, "w-36")} />
        {(busca || disciplina) && (
          <button type="button" title="Limpar filtros" onClick={() => { setBusca(""); setDisciplina(""); setPage(1); }}
            className="flex h-8 items-center gap-1 rounded-md px-2 text-xs text-muted-foreground hover:bg-accent hover:text-foreground">
            <X className="h-3.5 w-3.5" />Limpar
          </button>
        )}
        {dados && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button type="button" className="flex h-8 items-center gap-1.5 rounded-md border border-border px-2.5 text-xs font-bold hover:bg-accent">
                <Columns3 className="h-3.5 w-3.5" />Colunas ({visiveis}/{dados.columns.length})
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="max-h-[60vh] w-64 overflow-y-auto">
              <DropdownMenuLabel className="text-[10px] uppercase tracking-widest">Mostrar colunas</DropdownMenuLabel>
              {table.getAllLeafColumns().map(col => (
                <DropdownMenuCheckboxItem key={col.id} checked={col.getIsVisible()} onSelect={e => e.preventDefault()}
                  disabled={col.getIsVisible() && visiveis <= 1} onCheckedChange={v => col.toggleVisibility(!!v)} className="text-xs">
                  {String(col.columnDef.header)}
                </DropdownMenuCheckboxItem>
              ))}
              <DropdownMenuSeparator />
              <DropdownMenuItem onSelect={restaurar} className="gap-2 text-xs"><RotateCcw className="h-3.5 w-3.5" />Restaurar colunas padrão</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>

      {loading && !dados ? <LoadingState /> : error ? (
        <ReportState title={error} error><button className="text-primary underline" onClick={() => setAttempt(a => a + 1)}>Tentar novamente</button></ReportState>
      ) : dados && (
        <>
          <div className={cn("w-full max-h-[calc(100vh-15rem)] min-h-[240px] overflow-auto rounded-xl border border-border bg-card/60 custom-scrollbar", loading && "opacity-60")}>
            <table className="text-left text-xs" style={{ width: table.getTotalSize(), minWidth: "100%", tableLayout: "fixed" }}>
              <thead className="sticky top-0 z-10 bg-secondary text-muted-foreground">
                {table.getHeaderGroups().map(hg => (
                  <tr key={hg.id}>
                    {hg.headers.map(h => {
                      const c = h.column.columnDef.meta as Coluna;
                      const ativa = sort?.key === c.key;
                      const Icone = !c.sortable ? null : !ativa ? ArrowUpDown : sort!.dir === "asc" ? ArrowUp : ArrowDown;
                      return (
                        <th key={h.id} scope="col" style={{ width: h.getSize() }}
                          draggable={!redimensionando}
                          onDragStart={e => { if (redimensionando) { e.preventDefault(); return; } arrastando.current = h.column.id; }}
                          onDragOver={e => e.preventDefault()} onDrop={() => soltarEm(h.column.id)}
                          className="group relative select-none border-b border-border px-2 py-2 font-semibold">
                          <div className="flex min-w-0 items-center gap-1">
                            <GripVertical className="h-3 w-3 flex-shrink-0 cursor-grab opacity-0 group-hover:opacity-50" aria-hidden />
                            {c.sortable ? (
                              <button type="button" onClick={() => alternarOrdem(c)} title={`Ordenar por ${c.label}`}
                                className="flex min-w-0 items-center gap-1 hover:text-foreground">
                                <span className="truncate">{c.label}</span>
                                {Icone && <Icone className={cn("h-3 w-3 flex-shrink-0", ativa ? "text-primary" : "opacity-50")} />}
                              </button>
                            ) : <span className="truncate">{c.label}</span>}
                          </div>
                          <span role="separator" aria-orientation="vertical" title="Arraste para redimensionar (duplo clique restaura)"
                            onMouseDown={h.getResizeHandler()} onTouchStart={h.getResizeHandler()} onDoubleClick={() => h.column.resetSize()}
                            onClick={e => e.stopPropagation()}
                            className={cn("absolute right-0 top-0 h-full w-1.5 cursor-col-resize touch-none bg-transparent hover:bg-primary/50",
                              h.column.getIsResizing() && "bg-primary")} />
                        </th>
                      );
                    })}
                  </tr>
                ))}
              </thead>
              <tbody>
                {table.getRowModel().rows.length === 0 ? (
                  <tr><td colSpan={visiveis} className="px-3 py-8 text-center text-muted-foreground">Nenhum documento encontrado.</td></tr>
                ) : table.getRowModel().rows.map(row => (
                  <tr key={row.id} className="border-t border-border hover:bg-secondary/40">
                    {row.getVisibleCells().map(cell => {
                      const c = cell.column.columnDef.meta as Coluna;
                      const texto = formatar(cell.getValue(), c.type);
                      return (
                        <td key={cell.id} style={{ width: cell.column.getSize() }} title={texto}
                          className={cn("truncate px-2 py-1.5", (c.type === "numero" || c.type === "marcador") && "text-center tabular-nums")}>
                          {texto}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
            <p className="text-muted-foreground">{dados.filteredRows} de {dados.totalRows} documentos</p>
            <div className="flex items-center gap-2">
              <label className="flex items-center gap-1.5 text-muted-foreground">Por página
                <select value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }} className={cn(campo, "w-auto")}>
                  {TAMANHOS_PAGINA.map(n => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
              <button className="rounded-md border border-border px-2.5 py-1 disabled:opacity-40" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Anterior</button>
              <span className="text-muted-foreground" aria-live="polite">Página {page} de {totalPages}</span>
              <button className="rounded-md border border-border px-2.5 py-1 disabled:opacity-40" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Próxima</button>
            </div>
          </div>
        </>
      )}
    </section>
  );
}
