// Inventário por categoria (Linhas, Equipamentos, Instrumentos, Válvulas): lista P&ID ou 3D já filtrada pelas regras
// incluir/excluir persistidas (GET /inventory), com desenho, motivos de exclusão e editor das regras (PUT /filters, admin).
// As mesmas regras persistidas alimentam /results, /measurement e as demais telas.
import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "react-hot-toast";
import { AlertTriangle, ChevronLeft, ChevronRight, Loader2, Lock, Plus, Save, Search, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { descreverErro, formatarUtc, isAbort } from "@/components/plant3d/plantApi";
import {
  FILTER_OPERATORS, FilterRule, InventorySide, MAX_PAGE, PlantInventory, fmtNum, getFilters, getInventory, newId, putFilters,
} from "@/components/plant3d/plantRulesApi";
import { AvisoStatus, BarraEmpilhada, CAT_FIXAS, Estado, Kpi, Legenda, PlantHeader, SemProjeto } from "./plantShared";

const PAGE_SIZES = [50, 100, 250, MAX_PAGE];
const INPUT = "w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm text-foreground outline-none focus:border-primary disabled:opacity-60";
const LADOS: Array<{ id: InventorySide; label: string }> = [{ id: "pid", label: "P&ID" }, { id: "piping", label: "3D (Piping)" }];

function validarRegra(r: FilterRule, n: number): string | null {
  const op = FILTER_OPERATORS.find(o => o.id === r.operator);
  if (!op) return `Regra ${n}: operador desconhecido.`;
  if (r.target === "attribute" && !r.field.trim()) return `Regra ${n}: informe o atributo.`;
  if (!op.semValor && !r.value.trim()) return `Regra ${n}: informe o valor.`;
  return null;
}

export default function PlantInventoryPage({ category }: { category: string }) {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const { can } = useProjectPermissions(projectIdAcc);
  const podeEditar = can("plant3d.database.edit");
  const titulo = CAT_FIXAS.find(c => c.id === category)?.label ?? category;

  const [side, setSide] = useState<InventorySide>("pid");
  const [limit, setLimit] = useState(100);
  const [offset, setOffset] = useState(0);
  const [inv, setInv] = useState<PlantInventory | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);

  const [todasRegras, setTodasRegras] = useState<Record<string, FilterRule[]>>({});
  const [filtrosOk, setFiltrosOk] = useState(false);
  const [filtrosErro, setFiltrosErro] = useState("");
  const [filtrosEm, setFiltrosEm] = useState<string | null>(null);
  const [draft, setDraft] = useState<FilterRule[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [saveErro, setSaveErro] = useState("");

  const [visao, setVisao] = useState<"todos" | "incluidos" | "excluidos">("todos");
  const [busca, setBusca] = useState("");

  // Projeto/categoria mudou: recomeça.
  useEffect(() => { setOffset(0); setInv(null); setBusca(""); setVisao("todos"); }, [projectIdAcc, category, side]);

  // Regras persistidas.
  useEffect(() => {
    setTodasRegras({}); setFiltrosOk(false); setFiltrosErro(""); setDraft([]); setSaveErro("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    (async () => {
      try {
        const f = await getFilters(projectIdAcc, abort.signal);
        if (abort.signal.aborted) return;
        setTodasRegras(f.filters); setFiltrosEm(f.updatedAtUtc ?? null); setFiltrosOk(true);
        setDraft((f.filters[category] ?? []).map(r => ({ ...r })));
      } catch (e) {
        if (!isAbort(e)) setFiltrosErro(descreverErro(e, "Filtros salvos") + " Salvar fica bloqueado para não sobrescrever regras existentes.");
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, category]);

  // Inventário (sempre reflete as regras persistidas; draft só vale após salvar).
  useEffect(() => {
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true); setError("");
    (async () => {
      try {
        const r = await getInventory(projectIdAcc, category, side, offset, limit, abort.signal);
        if (abort.signal.aborted) return;
        setInv(r);
        if (r.items.length === 0 && r.totalItems > 0 && offset > 0) setOffset(0);
      } catch (e) {
        if (isAbort(e)) return;
        setInv(null); setError(descreverErro(e, `Inventário de ${titulo}`));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, category, side, offset, limit, tick, titulo]);

  const salvas = todasRegras[category] ?? [];
  const sujo = filtrosOk && JSON.stringify(draft) !== JSON.stringify(salvas);
  const erros = useMemo(() => draft.map((r, i) => validarRegra(r, i + 1)).filter((x): x is string => !!x), [draft]);

  const patch = (id: string, p: Partial<FilterRule>) => setDraft(d => d.map(r => (r.id === id ? { ...r, ...p } : r)));
  const adicionar = (action: FilterRule["action"]) =>
    setDraft(d => [...d, { id: newId(), side, action, target: "attribute", field: "", operator: "eq", value: "", enabled: true }]);

  const salvar = async () => {
    if (!podeEditar || !filtrosOk || erros.length || salvando) return;
    setSalvando(true); setSaveErro("");
    try {
      const envio = { ...todasRegras, [category]: draft.map(r => ({ ...r, field: r.field.trim(), value: r.value.trim() })) };
      const r = await putFilters(projectIdAcc, envio);
      setTodasRegras(r.filters); setFiltrosEm(r.updatedAtUtc ?? null);
      setDraft((r.filters[category] ?? []).map(x => ({ ...x })));
      setOffset(0); setTick(t => t + 1);
      toast.success("Filtros salvos — valem para todas as telas do projeto.");
    } catch (e) {
      const msg = descreverErro(e, "Filtros não foram salvos");
      setSaveErro(msg); toast.error(msg, { duration: 7000 });
    } finally { setSalvando(false); }
  };

  const colunas = useMemo(() => {
    const ks = new Set<string>();
    (inv?.items ?? []).forEach(i => Object.keys(i.attributes ?? {}).forEach(k => ks.add(k)));
    return Array.from(ks).sort((a, b) => a.localeCompare(b, "pt-BR"));
  }, [inv]);
  const colunasVisiveis = colunas.slice(0, 6);

  const itens = useMemo(() => {
    const q = busca.trim().toLowerCase();
    return (inv?.items ?? []).filter(i => {
      if (visao === "incluidos" && i.excluded) return false;
      if (visao === "excluidos" && !i.excluded) return false;
      if (!q) return true;
      return [i.tag, i.table, i.drawing, i.rowId, ...Object.values(i.attributes ?? {})].some(v => v?.toLowerCase().includes(q));
    });
  }, [inv, busca, visao]);

  const totalPaginas = inv ? Math.max(1, Math.ceil(inv.totalItems / limit)) : 1;
  const recarregar = useCallback(() => setTick(t => t + 1), []);
  const pronto = inv?.status === "ready";

  return (
    <div className="space-y-6">
      <PlantHeader titulo={`Configuração · ${titulo}`} subtitulo="Inventário P&ID e 3D após os filtros incluir/excluir. As regras salvas aqui valem para todo o sistema."
        pj={pj} loading={loading} onRefresh={recarregar}
        extra={filtrosEm && <p className="text-xs text-muted-foreground">Regras salvas em {formatarUtc(filtrosEm) ?? "data não informada"}</p>} />
      <SemProjeto pj={pj} />

      {projectIdAcc && (
        <div className="flex rounded-lg border border-border bg-background/50 p-1 w-fit">
          {LADOS.map(l => (
            <button key={l.id} type="button" onClick={() => setSide(l.id)}
              className={`rounded-md px-4 py-1.5 text-sm font-bold transition-colors ${side === l.id ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>{l.label}</button>
          ))}
        </div>
      )}

      {loading && !inv && <div className="flex justify-center p-10"><Loader2 className="h-10 w-10 animate-spin text-primary" /></div>}
      {error && !loading && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não foi possível carregar o inventário">{error}<div className="mt-2"><Button variant="outline" size="sm" onClick={recarregar}>Tentar novamente</Button></div></Estado>}
      <AvisoStatus status={inv?.status} />

      {inv && pronto && (
        <>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <Kpi titulo="Total lido (bruto)" valor={fmtNum(inv.rawCount)} cor="border-l-slate-500" />
            <Kpi titulo="Incluídos" valor={fmtNum(inv.includedCount)} cor="border-l-emerald-500" />
            <Kpi titulo="Excluídos pelos filtros" valor={fmtNum(inv.excludedCount)} cor="border-l-orange-500" />
          </div>
          <div className="glass-card space-y-2 rounded-xl border border-border/50 p-4">
            <BarraEmpilhada partes={[{ valor: inv.includedCount, cor: "bg-emerald-500", rotulo: "Incluídos" }, { valor: inv.excludedCount, cor: "bg-orange-500", rotulo: "Excluídos" }]} />
            <div className="flex flex-wrap gap-x-6 gap-y-1"><Legenda rotulo="Incluídos" valor={fmtNum(inv.includedCount)} cor="bg-emerald-500" /><Legenda rotulo="Excluídos" valor={fmtNum(inv.excludedCount)} cor="bg-orange-500" /></div>
          </div>
        </>
      )}

      {projectIdAcc && (
        <section className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h3 className="font-bold">Condições incluir / excluir — {titulo}</h3>
              <p className="text-xs text-muted-foreground">
                Qualquer regra de exclusão que casar remove o item. Havendo inclusões ativas para o lado, o item precisa casar ao menos uma. Sem regras, todos entram. Comparação sem diferenciar maiúsculas.
              </p>
            </div>
            {podeEditar && filtrosOk && (
              <div className="flex gap-2">
                <Button variant="outline" size="sm" className="gap-1" onClick={() => adicionar("include")}><Plus className="h-4 w-4" /> Incluir</Button>
                <Button variant="outline" size="sm" className="gap-1" onClick={() => adicionar("exclude")}><Plus className="h-4 w-4" /> Excluir</Button>
              </div>
            )}
          </div>

          {!podeEditar && <Estado tom="blue" icon={<Lock className="h-5 w-5" />} titulo="Somente leitura">Salvar regras exige a permissão <code>plant3d.database.edit</code>.</Estado>}
          {filtrosErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Regras não carregadas">{filtrosErro}</Estado>}

          {filtrosOk && draft.length === 0 && <p className="rounded-lg border border-dashed border-border/50 py-4 text-center text-sm italic text-muted-foreground">Nenhuma regra: todos os itens desta categoria são incluídos.</p>}

          <datalist id="plant-atributos">{colunas.map(c => <option key={c} value={c} />)}</datalist>
          <div className="space-y-2">
            {draft.map((r, i) => {
              const op = FILTER_OPERATORS.find(o => o.id === r.operator);
              return (
                <div key={r.id} className={`grid grid-cols-2 items-end gap-2 rounded-lg border p-2 md:grid-cols-12 ${r.action === "include" ? "border-emerald-500/30 bg-emerald-500/5" : "border-orange-500/30 bg-orange-500/5"} ${r.enabled ? "" : "opacity-60"}`}>
                  <select aria-label={`Regra ${i + 1}: ação`} className={`${INPUT} md:col-span-1`} disabled={!podeEditar} value={r.action} onChange={e => patch(r.id, { action: e.target.value as FilterRule["action"] })}>
                    <option value="include">Incluir</option><option value="exclude">Excluir</option>
                  </select>
                  <select aria-label={`Regra ${i + 1}: lado`} className={`${INPUT} md:col-span-2`} disabled={!podeEditar} value={r.side} onChange={e => patch(r.id, { side: e.target.value as FilterRule["side"] })}>
                    <option value="pid">P&ID</option><option value="piping">3D</option><option value="both">Ambos</option>
                  </select>
                  <select aria-label={`Regra ${i + 1}: alvo`} className={`${INPUT} md:col-span-2`} disabled={!podeEditar} value={r.target} onChange={e => patch(r.id, { target: e.target.value as FilterRule["target"] })}>
                    <option value="attribute">Atributo</option><option value="drawing">Desenho</option><option value="table">Tabela (classe)</option>
                  </select>
                  <input aria-label={`Regra ${i + 1}: campo`} list="plant-atributos" className={`${INPUT} md:col-span-2`} disabled={!podeEditar || r.target === "table"}
                    placeholder={r.target === "attribute" ? "Atributo" : r.target === "drawing" ? "Campo do desenho (opcional)" : "—"} value={r.target === "table" ? "" : r.field} onChange={e => patch(r.id, { field: e.target.value })} />
                  <select aria-label={`Regra ${i + 1}: operador`} className={`${INPUT} md:col-span-2`} disabled={!podeEditar} value={r.operator} onChange={e => patch(r.id, { operator: e.target.value as FilterRule["operator"] })}>
                    {FILTER_OPERATORS.map(o => <option key={o.id} value={o.id}>{o.label}</option>)}
                  </select>
                  <input aria-label={`Regra ${i + 1}: valor`} className={`${INPUT} md:col-span-2`} disabled={!podeEditar || !!op?.semValor}
                    placeholder={op?.semValor ? "—" : op?.lista ? "valores separados por vírgula" : "Valor"} value={op?.semValor ? "" : r.value} onChange={e => patch(r.id, { value: e.target.value })} />
                  <div className="col-span-2 flex items-center justify-end gap-2 md:col-span-1">
                    <label className="flex items-center gap-1 text-xs text-muted-foreground"><input type="checkbox" disabled={!podeEditar} checked={r.enabled} onChange={e => patch(r.id, { enabled: e.target.checked })} />Ativa</label>
                    {podeEditar && <button type="button" aria-label={`Remover regra ${i + 1}`} className="rounded p-1 text-muted-foreground hover:text-destructive" onClick={() => setDraft(d => d.filter(x => x.id !== r.id))}><Trash2 className="h-4 w-4" /></button>}
                  </div>
                </div>
              );
            })}
          </div>

          {erros.length > 0 && <ul className="space-y-1 text-xs text-red-600 dark:text-red-400">{erros.map(m => <li key={m}>{m}</li>)}</ul>}
          {saveErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não salvo">{saveErro}</Estado>}
          {podeEditar && filtrosOk && (
            <div className="flex items-center justify-end gap-3">
              {sujo && <span className="text-xs font-semibold text-amber-700 dark:text-amber-400">Alterações não salvas (a lista abaixo ainda usa as regras salvas)</span>}
              <Button variant="outline" disabled={!sujo || salvando} onClick={() => setDraft(salvas.map(r => ({ ...r })))}>Descartar</Button>
              <Button className="gap-2 font-bold" disabled={!sujo || salvando || erros.length > 0} onClick={salvar}>
                {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} SALVAR FILTROS
              </Button>
            </div>
          )}
        </section>
      )}

      {inv && pronto && (
        <div className="glass-card flex flex-col overflow-hidden rounded-xl border border-border/50">
          <div className="flex flex-col justify-between gap-3 border-b border-border/50 bg-card/30 p-4 lg:flex-row lg:items-center">
            <h3 className="text-lg font-bold">{titulo} — {LADOS.find(l => l.id === side)?.label}</h3>
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex rounded-lg border border-border bg-background/50 p-1">
                {([["todos", "Todos"], ["incluidos", "Incluídos"], ["excluidos", "Excluídos"]] as const).map(([k, l]) => (
                  <button key={k} type="button" onClick={() => setVisao(k)}
                    className={`rounded-md px-3 py-1 text-xs font-bold ${visao === k ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>{l}</button>
                ))}
              </div>
              <div className="relative">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <input type="text" placeholder="Pesquisar TAG, desenho, atributo…" value={busca} onChange={e => setBusca(e.target.value)}
                  className="w-full rounded-lg border border-border bg-background py-2 pl-9 pr-3 text-sm focus:border-primary focus:outline-none sm:w-64" />
              </div>
            </div>
          </div>
          {(busca || visao !== "todos") && <p className="px-4 pt-2 text-xs text-muted-foreground">Busca e Incluídos/Excluídos filtram apenas os {inv.items.length} itens desta página (de {inv.totalItems}).</p>}

          <div className="custom-scrollbar max-h-[560px] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 z-10 border-b border-border/50 bg-background/95 text-[10px] uppercase text-muted-foreground backdrop-blur-md">
                <tr>
                  <th className="px-3 py-3 font-bold">TAG</th><th className="px-3 py-3 font-bold">Tabela</th><th className="px-3 py-3 font-bold">Desenho</th>
                  {colunasVisiveis.map(c => <th key={c} className="px-3 py-3 font-bold">{c}</th>)}
                  <th className="px-3 py-3 font-bold">Situação</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/30">
                {itens.map(i => (
                  <tr key={i.rowId + (i.table ?? "")} className={`align-top hover:bg-primary/5 ${i.excluded ? "opacity-70" : ""}`}>
                    <td className="px-3 py-2 font-mono text-xs font-bold">{i.tag?.trim() || <span className="font-sans font-normal italic text-muted-foreground">(sem TAG)</span>}</td>
                    <td className="px-3 py-2 text-xs text-muted-foreground">{i.table ?? "—"}</td>
                    <td className="max-w-[260px] break-all px-3 py-2 text-xs text-muted-foreground">{i.drawing ?? "—"}</td>
                    {colunasVisiveis.map(c => <td key={c} className="px-3 py-2 text-xs">{i.attributes?.[c] ?? <span className="text-muted-foreground">—</span>}</td>)}
                    <td className="px-3 py-2 text-xs">
                      {i.excluded
                        ? <div><span className="rounded-full border border-orange-500/30 bg-orange-500/10 px-2 py-0.5 font-bold text-orange-700 dark:text-orange-400">Excluído</span>{(i.reasons ?? []).map(m => <div key={m} className="mt-0.5 text-[11px] text-muted-foreground">{m}</div>)}</div>
                        : <span className="rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 font-bold text-emerald-700 dark:text-emerald-400">Incluído</span>}
                    </td>
                  </tr>
                ))}
                {itens.length === 0 && <tr><td colSpan={4 + colunasVisiveis.length} className="px-4 py-10 text-center text-sm text-muted-foreground">{inv.items.length === 0 ? "O backend não retornou itens para esta categoria/lado." : "Nenhum item desta página corresponde aos filtros."}</td></tr>}
              </tbody>
            </table>
          </div>
          {colunas.length > colunasVisiveis.length && <p className="px-4 py-1 text-[11px] text-muted-foreground">Mostrando {colunasVisiveis.length} de {colunas.length} atributos (os demais podem ser usados nas regras).</p>}

          <div className="flex flex-col items-center justify-between gap-3 border-t border-border/50 bg-card/30 px-4 py-3 text-xs text-muted-foreground sm:flex-row">
            <span>{inv.totalItems === 0 ? "0 itens" : `${inv.offset + 1}–${inv.offset + inv.items.length} de ${fmtNum(inv.totalItems)} itens`}</span>
            <div className="flex items-center gap-2">
              <label className="flex items-center gap-1">Por página
                <select value={limit} onChange={e => { setLimit(Number(e.target.value)); setOffset(0); }} className="rounded border border-border bg-background px-2 py-1 text-foreground">
                  {PAGE_SIZES.map(n => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
              <Button variant="outline" size="sm" disabled={offset <= 0 || loading} onClick={() => setOffset(Math.max(0, offset - limit))} aria-label="Página anterior"><ChevronLeft className="h-4 w-4" /></Button>
              <span className="font-semibold text-foreground">{Math.floor(offset / limit) + 1} / {totalPaginas}</span>
              <Button variant="outline" size="sm" disabled={offset + limit >= inv.totalItems || loading} onClick={() => setOffset(offset + limit)} aria-label="Próxima página"><ChevronRight className="h-4 w-4" /></Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
