// Resultados Plant 3D (P&ID × 3D). Dados reais de GET /api/plant-integration/projects/{id}/results e /status (CONTRATO_02).
// Sem mocks: sem vínculo / sem base / sem mapeamento / erro de API aparecem como estado explícito, nunca como sucesso.
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { motion } from "framer-motion";
import {
  RefreshCw, Loader2, AlertTriangle, Box, GitBranch, CheckCircle2, Search, LayoutList, ChevronLeft, ChevronRight,
  Link2, Settings2, Clock, Tags as TagsIcon, ChevronDown, ChevronUp, Hourglass,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePlantProjects } from "./usePlantProjects";
import {
  PlantResults, PlantStatus, ResultItem, ResultsCategory, descreverErro, formatarUtc, isAbort, plantRequest, projetoPath,
} from "./plantApi";

const POLL_MS = 15_000;
const PAGE_SIZES = [50, 100, 250, 500];

type ClassInfo = { label: string; tone: "ok" | "warn" | "bad" | "extra" | "tagless" | "neutral" };
const TONE_CLASS: Record<ClassInfo["tone"], string> = {
  ok: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
  warn: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400",
  bad: "border-red-500/30 bg-red-500/10 text-red-700 dark:text-red-400",
  extra: "border-orange-500/30 bg-orange-500/10 text-orange-700 dark:text-orange-400",
  tagless: "border-slate-500/30 bg-slate-500/10 text-slate-700 dark:text-slate-300",
  neutral: "border-border bg-muted/40 text-muted-foreground",
};

function classificar(valor: string): ClassInfo {
  const k = (valor ?? "").toLowerCase().replace(/[\s_-]/g, "");
  if (k === "matched" || k === "ok") return { label: "Pareada", tone: "ok" };
  if (k === "missing3d") return { label: "Falta no 3D", tone: "bad" };
  if (k === "extra3d") return { label: "Sobra no 3D", tone: "extra" };
  if (k === "divergent") return { label: "Divergência de atributo", tone: "warn" };
  if (k === "taglesspid") return { label: "Sem TAG (P&ID)", tone: "tagless" };
  if (k === "tagless3d") return { label: "Sem TAG (3D)", tone: "tagless" };
  if (k.includes("duplic")) return { label: valor, tone: "warn" };
  return { label: valor || "—", tone: "neutral" };
}
const semTag = (i: ResultItem) => !i.tag?.trim() || (i.classification ?? "").toLowerCase().replace(/[\s_-]/g, "").startsWith("tagless");

function assinatura(st: PlantStatus): string {
  if (st.revisionToken) return st.revisionToken;
  const v = (x?: { completedVersions?: number; latestCompletedAtUtc?: string | null }) => [x?.completedVersions ?? null, x?.latestCompletedAtUtc ?? null];
  return JSON.stringify([v(st.pnId), v(st.piping), v(st.pnIdDelta), v(st.pipingDelta), st.filtersUpdatedAtUtc ?? null]);
}

function textoDetalhes(d: unknown): string[] {
  if (d == null || d === "") return [];
  if (typeof d === "string") return [d];
  const um = (x: unknown): string => (typeof x === "string" || typeof x === "number" || typeof x === "boolean" ? String(x) : JSON.stringify(x));
  if (Array.isArray(d)) return d.map(um);
  if (typeof d === "object") return Object.entries(d as Record<string, unknown>).map(([k, v]) => `${k}: ${um(v)}`);
  return [String(d)];
}

type Props = { onOpenSettings?: () => void };

export default function TabDashboard({ onOpenSettings }: Props) {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;

  const [results, setResults] = useState<PlantResults | null>(null);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState("");
  const [pollError, setPollError] = useState("");
  const [statusInfo, setStatusInfo] = useState<PlantStatus | null>(null);
  const [lastCheck, setLastCheck] = useState<Date | null>(null);

  const [category, setCategory] = useState("");
  const [limit, setLimit] = useState(100);
  const [offset, setOffset] = useState(0);
  const [busca, setBusca] = useState("");
  const [filtroClasse, setFiltroClasse] = useState("");
  const [visao, setVisao] = useState<"todos" | "comTag" | "semTag">("todos");
  const [aberto, setAberto] = useState<string | null>(null);

  const resultsRef = useRef<PlantResults | null>(null);
  resultsRef.current = results;
  const abortRef = useRef<AbortController | null>(null);
  const handledSigRef = useRef<string | null>(null);

  const fetchResults = useCallback(async (silent: boolean) => {
    if (!projectIdAcc) return;
    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;
    if (silent) setRefreshing(true); else setLoading(true);
    try {
      const qs = new URLSearchParams({ limit: String(limit), offset: String(offset) });
      if (category) qs.set("category", category);
      const r = await plantRequest<PlantResults>(`${projetoPath(projectIdAcc)}/results?${qs}`, { signal: ctrl.signal });
      if (!r || typeof r !== "object" || !r.status) throw new Error("Resposta inesperada de /results (campo status ausente).");
      if (r.status === "ready" && !r.totals) throw new Error("Resposta inesperada de /results (totals ausente).");
      setResults({ ...r, items: r.items ?? [], categories: r.categories ?? [], totalItems: r.totalItems ?? 0 });
      setError("");
      if ((r.items?.length ?? 0) === 0 && (r.totalItems ?? 0) > 0 && offset > 0) setOffset(0);
    } catch (e) {
      if (isAbort(e)) return;
      const msg = descreverErro(e, "Resultados");
      if (silent) setPollError("Atualização automática falhou: " + msg); else { setError(msg); setResults(null); }
    } finally {
      if (abortRef.current === ctrl) { setLoading(false); setRefreshing(false); }
    }
  }, [projectIdAcc, limit, offset, category]);

  const fetchRef = useRef(fetchResults);
  fetchRef.current = fetchResults;

  // Mudou projeto: limpa tudo.
  useEffect(() => {
    abortRef.current?.abort();
    setResults(null); setError(""); setPollError(""); setStatusInfo(null); setLastCheck(null);
    setCategory(""); setOffset(0); setBusca(""); setFiltroClasse(""); setVisao("todos"); setAberto(null);
    handledSigRef.current = null;
  }, [projectIdAcc]);

  // Carga (projeto, página, categoria).
  useEffect(() => {
    if (!projectIdAcc) return;
    void fetchResults(false);
    return () => abortRef.current?.abort();
  }, [fetchResults, projectIdAcc]);

  // Polling leve: só /status; /results só quando a revisão muda.
  const status = results?.status;
  useEffect(() => {
    if (!projectIdAcc || !status || status === "unlinked") return;
    let parado = false;
    const poll = async () => {
      if (document.hidden) return;
      try {
        const st = await plantRequest<PlantStatus>(`${projetoPath(projectIdAcc)}/status`);
        if (parado) return;
        setStatusInfo(st); setLastCheck(new Date()); setPollError("");
        const sig = assinatura(st);
        const atual = resultsRef.current;
        const mudou = st.revisionToken && atual?.revisionToken
          ? st.revisionToken !== atual.revisionToken && sig !== handledSigRef.current
          : handledSigRef.current !== null && sig !== handledSigRef.current;
        handledSigRef.current = sig;
        if (mudou) void fetchRef.current(true);
      } catch (e) {
        if (!parado && !isAbort(e)) setPollError(descreverErro(e, "Verificação de atualização"));
      }
    };
    void poll();
    const id = window.setInterval(poll, POLL_MS);
    return () => { parado = true; window.clearInterval(id); };
  }, [projectIdAcc, status]);

  const forcar = () => { void fetchResults(false); };
  const trocarCategoria = (id: string) => { setCategory(id); setOffset(0); };

  const itensFiltrados = useMemo(() => {
    const q = busca.trim().toLowerCase();
    return (results?.items ?? []).filter(i => {
      if (visao === "comTag" && semTag(i)) return false;
      if (visao === "semTag" && !semTag(i)) return false;
      if (filtroClasse && i.classification !== filtroClasse) return false;
      if (!q) return true;
      return [i.tag, i.category, i.pidTable, i.pipingTable, i.pidDrawing, i.pipingDrawing].some(v => v?.toLowerCase().includes(q));
    });
  }, [results, busca, filtroClasse, visao]);

  const classesNaPagina = useMemo(() => Array.from(new Set((results?.items ?? []).map(i => i.classification))).sort(), [results]);
  const dataBase = formatarUtc(results?.updatedAtUtc);
  const totalPaginas = results ? Math.max(1, Math.ceil(results.totalItems / limit)) : 1;
  const paginaAtual = Math.floor(offset / limit) + 1;
  const pageIncompleta = !!results && results.totalItems > results.items.length;

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      <div className="glass-card flex flex-col justify-between gap-4 rounded-xl border border-border/50 p-6 md:flex-row md:items-center">
        <div className="flex-1 space-y-2">
          <h2 className="text-2xl font-bold text-primary">Resultados da Auditoria P&ID × 3D</h2>
          {!pj.temProjetoAtual ? (
            <div className="flex flex-wrap items-center gap-3">
              <label htmlFor="dashboardProject" className="flex items-center gap-2 text-sm font-medium text-muted-foreground"><LayoutList className="h-4 w-4" /> Selecionar Projeto:</label>
              <select id="dashboardProject" value={pj.escolhido} onChange={e => pj.setEscolhido(e.target.value)}
                className="min-w-[260px] rounded-lg border border-border bg-background px-3 py-2 text-sm font-bold text-foreground outline-none focus:ring-2 focus:ring-primary">
                <option value="">-- Escolha um Projeto --</option>
                {pj.projects.map(p => <option key={p.projectIdAcc} value={p.projectIdAcc}>{p.name || p.projectIdAcc}{p.dashboardProjectId ? "" : " — não vinculado"}</option>)}
              </select>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">Projeto atual: <b className="text-foreground">{pj.nome || projectIdAcc}</b></p>
          )}
          {results?.status === "ready" && (
            <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span className="flex items-center gap-1"><Clock className="h-3.5 w-3.5" /> Base atualizada em: <b className="text-foreground">{dataBase ?? "data não informada pelo backend"}</b></span>
              {lastCheck && <span>Verificado às {lastCheck.toLocaleTimeString("pt-BR")} (a cada {POLL_MS / 1000}s)</span>}
              {refreshing && <span className="flex items-center gap-1"><Loader2 className="h-3 w-3 animate-spin" /> atualizando…</span>}
            </p>
          )}
        </div>
        <Button onClick={forcar} disabled={loading || !projectIdAcc} className="gap-2 font-bold" title="Buscar os resultados agora">
          {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
          ATUALIZAR DADOS
        </Button>
      </div>

      {pollError && results && <Estado tom="amber" icon={<AlertTriangle className="h-5 w-5" />} titulo="Atualização automática indisponível">{pollError} Os dados exibidos podem estar desatualizados.</Estado>}
      {pj.error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Projetos não carregados">{pj.error}</Estado>}

      {!projectIdAcc && !pj.error && (
        <Estado tom="neutral" icon={<Box className="h-8 w-8" />} titulo="Nenhum projeto selecionado">
          {pj.loaded && pj.projects.length === 0 ? "Seu usuário não tem projetos habilitados para o Plant 3D." : "Escolha um projeto acima (ou defina o projeto atual na Home) para ver os resultados."}
        </Estado>
      )}

      {loading && !results && <div className="flex justify-center p-12"><Loader2 className="h-12 w-12 animate-spin text-primary" /></div>}

      {error && !loading && (
        <Estado tom="red" icon={<AlertTriangle className="h-6 w-6" />} titulo="Não foi possível carregar os resultados">
          {error}
          <div className="mt-3"><Button variant="outline" size="sm" onClick={forcar}>Tentar novamente</Button></div>
        </Estado>
      )}

      {results?.status === "unlinked" && (
        <Estado tom="amber" icon={<Link2 className="h-6 w-6" />} titulo="Projeto sem vínculo com o Dashboard BIM">
          Nenhum resultado é exibido enquanto o projeto N&A não estiver associado a um projeto do Dashboard BIM. A associação é feita na aba de configuração.
          {onOpenSettings && <div className="mt-3"><Button variant="outline" size="sm" onClick={onOpenSettings} className="gap-2"><Settings2 className="h-4 w-4" /> Abrir Configuração DE/PARA e TAGs</Button></div>}
        </Estado>
      )}

      {results?.status === "no-baseline" && (
        <Estado tom="amber" icon={<Hourglass className="h-6 w-6" />} titulo="Aguardando versão-base">
          O Dashboard BIM ainda não tem uma versão completa de P&ID e/ou 3D para este projeto. Os resultados aparecem automaticamente quando o DBSYNC criar a base (esta tela verifica a cada {POLL_MS / 1000}s).
          {statusInfo && (
            <p className="mt-2 font-mono text-xs">
              P&ID: {statusInfo.pnId?.completedVersions ?? 0} versão(ões) completa(s) · 3D: {statusInfo.piping?.completedVersions ?? 0}
              {statusInfo.message ? ` — ${statusInfo.message}` : ""}
            </p>
          )}
        </Estado>
      )}

      {results?.status === "no-mapping" && (
        <Estado tom="amber" icon={<Settings2 className="h-6 w-6" />} titulo="Sem mapeamento DE/PARA salvo">
          Há base no Dashboard BIM, mas este projeto ainda não tem configuração de tabelas/TAGs salva. Configure e salve o DE/PARA para gerar os resultados.
          {onOpenSettings && <div className="mt-3"><Button variant="outline" size="sm" onClick={onOpenSettings} className="gap-2"><Settings2 className="h-4 w-4" /> Abrir Configuração DE/PARA e TAGs</Button></div>}
        </Estado>
      )}

      {results?.status === "ready" && (
        <>
          <CardsAnalises t={results.totals} />
          <CategoriasTabela categorias={results.categories} ativa={category} onSelect={trocarCategoria} />

          <div className="glass-card flex flex-col overflow-hidden rounded-xl border border-border/50">
            <div className="space-y-3 border-b border-border/50 bg-card/30 p-4">
              <div className="flex flex-col justify-between gap-3 lg:flex-row lg:items-center">
                <h3 className="flex items-center gap-2 text-lg font-bold"><TagsIcon className="h-5 w-5 text-primary" /> Detalhamento por TAG</h3>
                <div className="flex flex-wrap items-center gap-2">
                  <div className="flex rounded-lg border border-border bg-background/50 p-1">
                    {([["todos", "Todos"], ["comTag", "Com TAG"], ["semTag", "Sem TAG"]] as const).map(([k, l]) => (
                      <button key={k} type="button" onClick={() => setVisao(k)}
                        className={`rounded-md px-3 py-1 text-xs font-bold transition-colors ${visao === k ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>{l}</button>
                    ))}
                  </div>
                  <select aria-label="Filtrar por classificação" value={filtroClasse} onChange={e => setFiltroClasse(e.target.value)}
                    className="rounded-lg border border-border bg-background px-3 py-2 text-xs font-semibold text-foreground">
                    <option value="">Todas as classificações</option>
                    {classesNaPagina.map(c => <option key={c} value={c}>{classificar(c).label}</option>)}
                  </select>
                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <input type="text" placeholder="Pesquisar TAG, tabela, desenho…" value={busca} onChange={e => setBusca(e.target.value)}
                      className="w-full rounded-lg border border-border bg-background py-2 pl-9 pr-3 text-sm text-foreground focus:border-primary focus:outline-none sm:w-64" />
                  </div>
                </div>
              </div>
              {(pageIncompleta || busca || filtroClasse || visao !== "todos") && (
                <p className="text-xs text-muted-foreground">
                  Busca, classificação e Com/Sem TAG filtram apenas os {results.items.length} itens desta página (de {results.totalItems}). A categoria filtra no servidor.
                </p>
              )}
            </div>

            <div className="custom-scrollbar max-h-[560px] overflow-auto">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 z-10 border-b border-border/50 bg-background/95 text-[10px] uppercase text-muted-foreground backdrop-blur-md">
                  <tr>
                    <th className="px-4 py-3 font-bold tracking-wider">TAG</th>
                    <th className="px-4 py-3 font-bold tracking-wider">Categoria</th>
                    <th className="px-4 py-3 font-bold tracking-wider">Classificação</th>
                    <th className="px-4 py-3 font-bold tracking-wider text-emerald-700 dark:text-emerald-400">P&ID</th>
                    <th className="px-4 py-3 font-bold tracking-wider text-blue-700 dark:text-blue-400">3D</th>
                    <th className="px-4 py-3 font-bold tracking-wider">Detalhes</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/30">
                  {itensFiltrados.map(item => {
                    const cls = classificar(item.classification);
                    const det = textoDetalhes(item.details);
                    const expandido = aberto === item.id;
                    return (
                      <tr key={item.id} className="align-top transition-colors hover:bg-primary/5">
                        <td className="px-4 py-3 font-mono text-xs font-bold text-foreground">{item.tag?.trim() || <span className="font-sans font-normal italic text-muted-foreground">(sem TAG)</span>}</td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">{results.categories.find(c => c.id === item.category)?.label ?? item.category}</td>
                        <td className="px-4 py-3"><span className={`inline-flex whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-bold ${TONE_CLASS[cls.tone]}`}>{cls.label}</span></td>
                        <td className="px-4 py-3 text-xs"><Origem tabela={item.pidTable} desenho={item.pidDrawing} rowId={item.pidRowId} /></td>
                        <td className="px-4 py-3 text-xs"><Origem tabela={item.pipingTable} desenho={item.pipingDrawing} rowId={item.pipingRowId} /></td>
                        <td className="max-w-[360px] px-4 py-3 text-xs">
                          {det.length === 0 ? <span className="text-muted-foreground">—</span> : (
                            <div>
                              <button type="button" onClick={() => setAberto(expandido ? null : item.id)} className="flex items-center gap-1 font-semibold text-primary hover:underline">
                                {det.length} detalhe{det.length > 1 ? "s" : ""} {expandido ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
                              </button>
                              {expandido && <ul className="mt-1 space-y-0.5 break-words font-mono text-[11px] text-foreground/80">{det.map((d, i) => <li key={i}>{d}</li>)}</ul>}
                            </div>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                  {itensFiltrados.length === 0 && (
                    <tr><td colSpan={6} className="px-4 py-10 text-center text-sm text-muted-foreground">
                      {results.items.length === 0
                        ? "O backend não retornou itens para este filtro de categoria."
                        : visao === "semTag"
                          ? `Nenhum item sem TAG nesta página. Contagens do projeto — P&ID: ${results.totals.taglessPid} · 3D: ${results.totals.tagless3d}.`
                          : "Nenhum item corresponde aos filtros desta página."}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="flex flex-col items-center justify-between gap-3 border-t border-border/50 bg-card/30 px-4 py-3 text-xs text-muted-foreground sm:flex-row">
              <span>
                {results.totalItems === 0 ? "0 itens" : `${results.offset + 1}–${results.offset + results.items.length} de ${results.totalItems} itens`}
                {category && <> · categoria: <b className="text-foreground">{results.categories.find(c => c.id === category)?.label ?? category}</b></>}
              </span>
              <div className="flex items-center gap-2">
                <label className="flex items-center gap-1">Por página
                  <select value={limit} onChange={e => { setLimit(Number(e.target.value)); setOffset(0); }} className="rounded border border-border bg-background px-2 py-1 text-foreground">
                    {PAGE_SIZES.map(n => <option key={n} value={n}>{n}</option>)}
                  </select>
                </label>
                <Button variant="outline" size="sm" disabled={offset <= 0 || loading} onClick={() => setOffset(Math.max(0, offset - limit))} aria-label="Página anterior"><ChevronLeft className="h-4 w-4" /></Button>
                <span className="font-semibold text-foreground">{paginaAtual} / {totalPaginas}</span>
                <Button variant="outline" size="sm" disabled={offset + limit >= results.totalItems || loading} onClick={() => setOffset(offset + limit)} aria-label="Próxima página"><ChevronRight className="h-4 w-4" /></Button>
              </div>
            </div>
          </div>
        </>
      )}
    </motion.div>
  );
}

function Origem({ tabela, desenho, rowId }: { tabela?: string | null; desenho?: string | null; rowId?: string | null }) {
  if (!tabela && !desenho && !rowId) return <span className="text-muted-foreground">—</span>;
  return (
    <div className="space-y-0.5">
      {tabela && <div className="font-semibold text-foreground">{tabela}</div>}
      {desenho && <div className="break-all text-muted-foreground">{desenho}</div>}
      {rowId && <div className="font-mono text-[10px] text-muted-foreground">#{rowId}</div>}
    </div>
  );
}

function Barra({ partes }: { partes: Array<{ valor: number; cor: string; rotulo: string }> }) {
  const total = partes.reduce((n, p) => n + p.valor, 0);
  if (total === 0) return <div className="h-2 rounded-full bg-secondary" />;
  return (
    <div className="flex h-2 overflow-hidden rounded-full bg-secondary" role="img" aria-label={partes.map(p => `${p.rotulo}: ${p.valor}`).join(", ")}>
      {partes.filter(p => p.valor > 0).map(p => <div key={p.rotulo} className={p.cor} style={{ width: `${(p.valor / total) * 100}%` }} title={`${p.rotulo}: ${p.valor}`} />)}
    </div>
  );
}

function Linha({ rotulo, valor, cor }: { rotulo: string; valor: number; cor: string }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="flex items-center gap-2 text-muted-foreground"><span className={`h-2.5 w-2.5 rounded-full ${cor}`} />{rotulo}</span>
      <b className="tabular-nums text-foreground">{valor.toLocaleString("pt-BR")}</b>
    </div>
  );
}

function CardsAnalises({ t }: { t: PlantResults["totals"] }) {
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
      <div className="glass-card space-y-3 rounded-xl border-l-4 border-l-emerald-500 p-5">
        <div className="flex items-center justify-between"><h3 className="text-sm font-bold">1 · Cobertura por TAG (P&ID × 3D)</h3><GitBranch className="h-5 w-5 text-emerald-500/60" /></div>
        <Barra partes={[{ valor: t.matched, cor: "bg-emerald-500", rotulo: "Pareadas" }, { valor: t.divergent, cor: "bg-amber-500", rotulo: "Divergentes" }, { valor: t.missing3d, cor: "bg-red-500", rotulo: "Faltam no 3D" }, { valor: t.extra3d, cor: "bg-orange-500", rotulo: "Sobram no 3D" }]} />
        <div className="space-y-1.5">
          <Linha rotulo="Pareadas" valor={t.matched} cor="bg-emerald-500" />
          <Linha rotulo="Faltam no 3D" valor={t.missing3d} cor="bg-red-500" />
          <Linha rotulo="Sobram no 3D (não existem no P&ID)" valor={t.extra3d} cor="bg-orange-500" />
        </div>
        <p className="border-t border-border/40 pt-2 text-xs text-muted-foreground">Linhas lidas — P&ID: <b className="text-foreground">{t.pid.toLocaleString("pt-BR")}</b> · 3D: <b className="text-foreground">{t.piping.toLocaleString("pt-BR")}</b></p>
      </div>

      <div className="glass-card space-y-3 rounded-xl border-l-4 border-l-amber-500 p-5">
        <div className="flex items-center justify-between"><h3 className="text-sm font-bold">2 · Divergência de atributos</h3><AlertTriangle className="h-5 w-5 text-amber-500/60" /></div>
        <p className="text-4xl font-black tabular-nums text-amber-600 dark:text-amber-400">{t.divergent.toLocaleString("pt-BR")}</p>
        <p className="text-xs text-muted-foreground">TAGs que existem nos dois lados mas têm atributo configurado diferente. Abra “Detalhes” na tabela para ver o que diverge.</p>
        <div className="flex items-center gap-2 border-t border-border/40 pt-2 text-xs text-muted-foreground"><CheckCircle2 className="h-4 w-4 text-emerald-500" /> Pareadas sem divergência: <b className="text-foreground">{t.matched.toLocaleString("pt-BR")}</b></div>
      </div>

      <div className="glass-card space-y-3 rounded-xl border-l-4 border-l-slate-500 p-5">
        <div className="flex items-center justify-between"><h3 className="text-sm font-bold">3 · Itens sem TAG</h3><TagsIcon className="h-5 w-5 text-slate-500/60" /></div>
        <div className="space-y-1.5">
          <Linha rotulo="Sem TAG no P&ID" valor={t.taglessPid} cor="bg-emerald-500" />
          <Linha rotulo="Sem TAG no 3D" valor={t.tagless3d} cor="bg-blue-500" />
        </div>
        <p className="border-t border-border/40 pt-2 text-xs text-muted-foreground">Contagens independentes por lado: não se assume correspondência entre linhas sem TAG do P&ID e do 3D.</p>
      </div>
    </div>
  );
}

function CategoriasTabela({ categorias, ativa, onSelect }: { categorias: ResultsCategory[]; ativa: string; onSelect: (id: string) => void }) {
  if (categorias.length === 0) return null;
  const cab = "px-3 py-2 text-right font-bold";
  return (
    <div className="glass-card overflow-hidden rounded-xl border border-border/50">
      <div className="flex items-center justify-between border-b border-border/50 bg-card/30 px-4 py-3">
        <h3 className="font-bold">Por categoria</h3>
        {ativa && <button type="button" onClick={() => onSelect("")} className="text-xs font-semibold text-primary hover:underline">Limpar filtro</button>}
      </div>
      <div className="custom-scrollbar overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-background/60 text-[10px] uppercase text-muted-foreground">
            <tr>
              <th className="px-3 py-2 text-left font-bold">Categoria</th>
              <th className={cab}>P&ID</th><th className={cab}>3D</th><th className={cab}>Pareadas</th><th className={cab}>Falta 3D</th>
              <th className={cab}>Sobra 3D</th><th className={cab}>Divergentes</th><th className={cab}>Sem TAG P&ID</th><th className={cab}>Sem TAG 3D</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border/30 tabular-nums">
            {categorias.map(c => (
              <tr key={c.id} onClick={() => onSelect(ativa === c.id ? "" : c.id)} className={`cursor-pointer transition-colors hover:bg-primary/5 ${ativa === c.id ? "bg-primary/10" : ""}`}>
                <td className="px-3 py-2 font-semibold">{c.label}</td>
                {[c.pidCount, c.pipingCount, c.matched, c.missing3d, c.extra3d, c.divergent, c.taglessPid, c.tagless3d].map((v, i) => (
                  <td key={i} className="px-3 py-2 text-right text-foreground/90">{v.toLocaleString("pt-BR")}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

const ESTADO_TOM = {
  amber: "border-amber-500/30 bg-amber-500/5 text-amber-700 dark:text-amber-400",
  red: "border-red-500/30 bg-red-500/5 text-red-700 dark:text-red-400",
  neutral: "border-border/50 bg-card/30 text-muted-foreground",
} as const;

function Estado({ tom, icon, titulo, children }: { tom: keyof typeof ESTADO_TOM; icon: React.ReactNode; titulo: string; children: React.ReactNode }) {
  return (
    <div className={`glass-card flex items-start gap-4 rounded-xl border p-5 ${ESTADO_TOM[tom]}`}>
      <div className="mt-0.5 shrink-0">{icon}</div>
      <div className="space-y-1">
        <h3 className="font-bold text-foreground">{titulo}</h3>
        <div className="text-sm text-muted-foreground">{children}</div>
      </div>
    </div>
  );
}
