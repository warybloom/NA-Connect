// Plant 3D › Relatórios › Linhas | Equipamentos | Instrumentos | Válvulas | Tie-ins | Materiais | Suportes (F6).
// Uma única página serve as 7 rotas (o relatório vem da prop `kind` ou da URL). Dados paginados no servidor (≤ 500 linhas por vez),
// filtros globais do projeto sempre aplicados pelo backend, campos/filtros do usuário persistidos no servidor (migration 005).
// Materiais/Suportes (ou qualquer relatório) sem origem confiável mostram "não mapeado" — nunca dados inventados.
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { ChevronLeft, ChevronRight, FileSpreadsheet, FileText, Loader2, Settings2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { descreverErro, isAbort } from "@/components/plant3d/plantApi";
import { fmtNum } from "@/components/plant3d/plantRulesApi";
import { Estado, PlantHeader, SemProjeto } from "./plantShared";
import { PLANT_ROTAS } from "./plantRoutes";
import {
  MAX_PAGINA, REPORT_KINDS, SITUACOES, XLSX_MAX_LINHAS, exportarCsv, exportarXlsx, getReportCatalog, getReportData, getReportPrefs,
  putReportPrefs, reportKindDaRota, type ReportCatalog, type ReportData, type ReportDescriptor, type ReportKind,
} from "./plantReports";

const MAX_CAMPOS = 80;
const INPUT = "rounded-lg border border-border bg-background px-3 py-2 text-sm text-foreground outline-none focus:ring-2 focus:ring-primary";

function useDebounced<T>(valor: T, ms: number): T {
  const [v, setV] = useState(valor);
  useEffect(() => { const t = window.setTimeout(() => setV(valor), ms); return () => window.clearTimeout(t); }, [valor, ms]);
  return v;
}

export default function PlantReportsCenterPage({ kind: kindProp }: { kind?: ReportKind }) {
  const { pathname } = useLocation();
  const kind = kindProp ?? reportKindDaRota(pathname);
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const rotulo = REPORT_KINDS.find(r => r.kind === kind)?.label ?? kind;

  const [catalog, setCatalog] = useState<ReportCatalog | null>(null);
  const [catErro, setCatErro] = useState("");
  const [catLoading, setCatLoading] = useState(false);
  const [tick, setTick] = useState(0);

  const [campos, setCampos] = useState<string[]>([]);
  const [situacao, setSituacao] = useState("");
  const [busca, setBusca] = useState("");
  const buscaDeb = useDebounced(busca, 400);
  const [limit, setLimit] = useState(100);
  const [offset, setOffset] = useState(0);
  const [prefsChave, setPrefsChave] = useState("");
  const [prefsAviso, setPrefsAviso] = useState("");
  const prefsSalvaveis = useRef(false);
  const ultimoJson = useRef("");

  const [dados, setDados] = useState<ReportData | null>(null);
  const [loading, setLoading] = useState(false);
  const [erro, setErro] = useState("");
  const [exportando, setExportando] = useState<"" | "csv" | "xlsx">("");
  const [msgExport, setMsgExport] = useState<{ tom: "ok" | "erro"; texto: string } | null>(null);
  const [filtroCampo, setFiltroCampo] = useState("");

  const desc: ReportDescriptor | null = useMemo(() => catalog?.reports.find(r => r.kind === kind) ?? null, [catalog, kind]);
  const chave = `${projectIdAcc}|${kind}`;
  const pronto = prefsChave === chave;

  // 1) catálogo (campos universais DE/PARA disponíveis por relatório)
  useEffect(() => {
    setCatalog(null); setCatErro(""); setDados(null);
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setCatLoading(true);
    (async () => {
      try { const c = await getReportCatalog(projectIdAcc, abort.signal); if (!abort.signal.aborted) setCatalog(c); }
      catch (e) { if (!isAbort(e)) setCatErro(descreverErro(e, "Catálogo de relatórios")); }
      finally { if (!abort.signal.aborted) setCatLoading(false); }
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);

  // 2) preferências do usuário (servidor). Falha nunca bloqueia o relatório: usa os padrões e avisa.
  useEffect(() => {
    setPrefsChave(""); setPrefsAviso(""); prefsSalvaveis.current = false; setOffset(0); setDados(null);
    if (!projectIdAcc || !desc) return;
    const abort = new AbortController();
    const validos = new Set(desc.fields.map(f => f.id));
    (async () => {
      let escolhidos = desc.defaultFields; let cls = ""; let q = "";
      try {
        const { prefs } = await getReportPrefs(projectIdAcc, kind, abort.signal);
        if (prefs) {
          const f = prefs.fields.filter(id => validos.has(id));
          if (f.length > 0) escolhidos = f;
          cls = prefs.classification ?? ""; q = prefs.search ?? "";
        }
        prefsSalvaveis.current = true;
      } catch (e) {
        if (isAbort(e)) return;
        setPrefsAviso(descreverErro(e, "Preferências do relatório") + " Suas escolhas valem só nesta sessão.");
      }
      if (abort.signal.aborted) return;
      ultimoJson.current = JSON.stringify([escolhidos, cls, q.trim()]);
      setCampos(escolhidos); setSituacao(cls); setBusca(q); setPrefsChave(chave);
    })();
    return () => abort.abort();
  }, [projectIdAcc, kind, desc, chave]);

  // 3) autosave das preferências (debounce); só depois do carregamento inicial
  const prefsJson = JSON.stringify([campos, situacao, buscaDeb.trim()]);
  useEffect(() => {
    if (!pronto || !prefsSalvaveis.current || !projectIdAcc || prefsJson === ultimoJson.current) return; // nada mudou: não grava só por abrir
    const t = window.setTimeout(() => {
      putReportPrefs(projectIdAcc, kind, { fields: campos, classification: situacao || null, search: buscaDeb.trim() || null })
        .then(() => { ultimoJson.current = prefsJson; setPrefsAviso(""); })
        .catch(e => setPrefsAviso(descreverErro(e, "Salvar preferências") + " Suas escolhas valem só nesta sessão."));
    }, 1000);
    return () => window.clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefsJson, pronto]);

  // 4) dados paginados
  const camposChave = campos.join(",");
  useEffect(() => {
    setErro("");
    if (!projectIdAcc || !desc || !pronto || desc.mode === "unmapped" || campos.length === 0) { setDados(null); return; }
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const r = await getReportData(projectIdAcc, kind, { fields: campos, classification: situacao || null, search: buscaDeb }, offset, limit, abort.signal);
        if (abort.signal.aborted) return;
        if (r.rows.length === 0 && r.total > 0 && offset > 0) { setOffset(0); return; }
        setDados(r);
      } catch (e) { if (!isAbort(e)) setErro(descreverErro(e, "Relatório")); }
      finally { if (!abort.signal.aborted) setLoading(false); }
    })();
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectIdAcc, kind, desc, pronto, camposChave, situacao, buscaDeb, offset, limit, tick]);

  const grupos = useMemo(() => {
    const termo = filtroCampo.trim().toLowerCase();
    const mapa = new Map<string, ReportDescriptor["fields"]>();
    for (const f of desc?.fields ?? []) {
      if (termo && !f.label.toLowerCase().includes(termo)) continue;
      mapa.set(f.group, [...(mapa.get(f.group) ?? []), f]);
    }
    return [...mapa.entries()];
  }, [desc, filtroCampo]);

  const alternar = useCallback((id: string) => {
    setCampos(atual => atual.includes(id) ? atual.filter(x => x !== id) : atual.length >= MAX_CAMPOS ? atual : [...atual, id]);
  }, []);

  const consulta = { fields: campos, classification: situacao || null, search: buscaDeb };
  async function exportar(tipo: "csv" | "xlsx") {
    if (!projectIdAcc || campos.length === 0) return;
    setExportando(tipo); setMsgExport(null);
    try {
      if (tipo === "csv") {
        const r = await exportarCsv(projectIdAcc, kind, consulta);
        setMsgExport({ tom: "ok", texto: r.truncado ? `CSV gerado com o limite de ${fmtNum(catalog?.maxExportRows ?? 250000)} linhas; refine os filtros para obter o restante.` : `CSV gerado (${fmtNum(r.total)} linhas, filtros globais aplicados).` });
      } else {
        const r = await exportarXlsx(projectIdAcc, kind, consulta);
        setMsgExport({ tom: "ok", texto: r.truncado ? `XLSX limitado a ${fmtNum(r.linhas)} de ${fmtNum(r.total)} linhas; use CSV para o conjunto completo ou refine os filtros.` : `XLSX gerado (${fmtNum(r.linhas)} linhas, filtros globais aplicados).` });
      }
    } catch (e) { setMsgExport({ tom: "erro", texto: descreverErro(e, "Exportação") }); }
    finally { setExportando(""); }
  }

  const total = dados?.total ?? 0;
  const paginaFim = dados ? dados.offset + dados.rows.length : 0;

  return (
    <div className="space-y-6">
      <PlantHeader titulo={`Plant 3D · Relatório de ${rotulo}`}
        subtitulo="Extração universal por campos do DE/PARA, com os filtros globais do projeto. Exporte para Excel (XLSX) ou CSV."
        pj={pj} loading={catLoading || loading} onRefresh={() => setTick(t => t + 1)}
        extra={
          <div className="flex flex-wrap gap-1.5 pt-1">
            {REPORT_KINDS.map(r => (
              <Link key={r.kind} to={r.rota} className={`rounded-full border px-3 py-1 text-xs font-bold ${r.kind === kind ? "border-primary bg-primary/10 text-primary" : "border-border text-muted-foreground hover:text-foreground"}`}>{r.label}</Link>
            ))}
            <Link to={PLANT_ROTAS.relatorios.configuracao} className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-xs font-bold text-muted-foreground hover:text-foreground"><Settings2 className="h-3 w-3" /> Configuração</Link>
          </div>
        } />
      <SemProjeto pj={pj} />
      {catErro && <Estado tom="red" titulo="Relatórios indisponíveis">{catErro}</Estado>}
      {catalog && catalog.status !== "ready" && (
        <Estado tom="amber" titulo={catalog.status === "unlinked" ? "Projeto sem vínculo com o Dashboard BIM" : "Aguardando versão-base do Dashboard BIM"}>
          Sem histórico P&ID/3D disponível, nenhum relatório pode ser gerado (nada é estimado).
        </Estado>
      )}

      {catalog?.status === "ready" && !desc && !catLoading && (
        <Estado tom="red" titulo="Relatório não oferecido pelo servidor">O catálogo do backend não contém “{kind}”. Verifique a versão do backend implantada.</Estado>
      )}

      {desc?.mode === "unmapped" && (
        <Estado tom="amber" titulo={`${desc.label}: não mapeado`}>
          {desc.reason ?? "Sem origem de dados configurada."}{" "}
          <Link className="font-bold text-primary underline" to={PLANT_ROTAS.relatorios.configuracao}>Configuração de Relatórios</Link>
        </Estado>
      )}

      {desc && desc.mode !== "unmapped" && (
        <>
          {prefsAviso && <Estado tom="amber" titulo="Preferências não persistidas">{prefsAviso}</Estado>}
          <section className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
            <div className="flex flex-wrap items-end gap-3">
              {desc.mode === "mapped" && (
                <label className="flex flex-col gap-1 text-xs font-bold text-muted-foreground">Situação P&ID × 3D
                  <select className={INPUT} value={situacao} onChange={e => { setSituacao(e.target.value); setOffset(0); }}>
                    <option value="">Todas</option>
                    {SITUACOES.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}
                  </select>
                </label>
              )}
              <label className="flex min-w-[220px] flex-1 flex-col gap-1 text-xs font-bold text-muted-foreground">Busca nos campos selecionados
                <input className={INPUT} value={busca} maxLength={100} placeholder="TAG, desenho, valor…" onChange={e => { setBusca(e.target.value); setOffset(0); }} />
              </label>
              <div className="ml-auto flex flex-wrap gap-2">
                <Button variant="outline" className="gap-2 font-bold" disabled={!!exportando || campos.length === 0 || total === 0} onClick={() => exportar("csv")}>
                  {exportando === "csv" ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileText className="h-4 w-4" />} CSV (servidor)
                </Button>
                <Button className="gap-2 font-bold" disabled={!!exportando || campos.length === 0 || total === 0} onClick={() => exportar("xlsx")}
                  title={`XLSX montado no navegador, até ${XLSX_MAX_LINHAS.toLocaleString("pt-BR")} linhas`}>
                  {exportando === "xlsx" ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileSpreadsheet className="h-4 w-4" />} Excel (XLSX)
                </Button>
              </div>
            </div>
            {msgExport && <p role="status" className={`text-sm font-semibold ${msgExport.tom === "erro" ? "text-red-600" : "text-muted-foreground"}`}>{msgExport.texto}</p>}
            {dados && (
              <p className="text-xs text-muted-foreground">
                {dados.filterRules > 0
                  ? <>Filtros globais do projeto aplicados: <b className="text-foreground">{dados.filterRules}</b> regra(s) ativa(s). A exportação usa exatamente os mesmos filtros.</>
                  : <>Nenhuma regra de filtro global ativa para este relatório. Qualquer regra salva é aplicada automaticamente.</>}
                {" "}Excel gera até {XLSX_MAX_LINHAS.toLocaleString("pt-BR")} linhas; CSV até {fmtNum(catalog?.maxExportRows ?? 250000)}.
              </p>
            )}

            <details className="rounded-lg border border-border/50 bg-card/30 p-3">
              <summary className="cursor-pointer text-sm font-bold">Campos do relatório ({campos.length} selecionados de {desc.fields.length})</summary>
              <div className="mt-3 space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  <input className={`${INPUT} min-w-[200px]`} placeholder="Filtrar campos…" value={filtroCampo} onChange={e => setFiltroCampo(e.target.value)} />
                  <Button variant="outline" size="sm" onClick={() => setCampos(desc.defaultFields)}>Padrão</Button>
                  <Button variant="outline" size="sm" onClick={() => setCampos(desc.fields.slice(0, MAX_CAMPOS).map(f => f.id))}>Todos (até {MAX_CAMPOS})</Button>
                  <Button variant="outline" size="sm" onClick={() => setCampos([])}>Limpar</Button>
                </div>
                {grupos.map(([grupo, lista]) => (
                  <fieldset key={grupo} className="space-y-1">
                    <legend className="text-[11px] font-bold uppercase text-muted-foreground">{grupo}</legend>
                    <div className="grid gap-1 sm:grid-cols-2 lg:grid-cols-3">
                      {lista.map(f => (
                        <label key={f.id} className="flex items-center gap-2 text-sm">
                          <input type="checkbox" checked={campos.includes(f.id)} onChange={() => alternar(f.id)}
                            disabled={!campos.includes(f.id) && campos.length >= MAX_CAMPOS} />
                          <span className="break-all">{f.label}</span>
                        </label>
                      ))}
                    </div>
                  </fieldset>
                ))}
                {campos.length >= MAX_CAMPOS && <p className="text-xs text-amber-600">Limite de {MAX_CAMPOS} campos por relatório atingido.</p>}
              </div>
            </details>
          </section>

          {erro && <Estado tom="red" titulo="Falha ao carregar o relatório">{erro}</Estado>}
          {campos.length === 0 && pronto && <Estado tom="neutral" titulo="Nenhum campo selecionado">Escolha ao menos um campo em “Campos do relatório”.</Estado>}

          {dados && dados.columns.length > 0 && (
            <section className="glass-card overflow-hidden rounded-xl border border-border/50">
              <div className="max-h-[65vh] overflow-auto">
                <table className="w-full text-left text-sm">
                  <thead className="sticky top-0 z-10 border-b border-border/50 bg-background/95 text-[10px] uppercase text-muted-foreground backdrop-blur-md">
                    <tr>{dados.columns.map(c => <th key={c.id} className="whitespace-nowrap px-3 py-3 font-bold">{c.label}</th>)}</tr>
                  </thead>
                  <tbody className="divide-y divide-border/30">
                    {dados.rows.map((r, i) => (
                      <tr key={dados.offset + i} className="align-top hover:bg-primary/5">
                        {dados.columns.map((c, j) => <td key={c.id} className="max-w-[280px] break-all px-3 py-2 text-xs">{r[j] || <span className="text-muted-foreground">—</span>}</td>)}
                      </tr>
                    ))}
                    {dados.rows.length === 0 && <tr><td colSpan={dados.columns.length} className="px-4 py-10 text-center text-sm text-muted-foreground">Nenhuma linha para os filtros atuais.</td></tr>}
                  </tbody>
                </table>
              </div>
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border/50 px-4 py-3 text-xs text-muted-foreground">
                <span>{total === 0 ? "0 linhas" : `${fmtNum(dados.offset + 1)}–${fmtNum(paginaFim)} de ${fmtNum(total)} linhas`}</span>
                <div className="flex items-center gap-2">
                  <label className="flex items-center gap-1">Por página
                    <select value={limit} onChange={e => { setLimit(Number(e.target.value)); setOffset(0); }} className="rounded border border-border bg-background px-2 py-1 text-foreground">
                      {[50, 100, 200, MAX_PAGINA].map(n => <option key={n} value={n}>{n}</option>)}
                    </select>
                  </label>
                  <Button variant="outline" size="sm" disabled={offset <= 0 || loading} onClick={() => setOffset(Math.max(0, offset - limit))} aria-label="Página anterior"><ChevronLeft className="h-4 w-4" /></Button>
                  <Button variant="outline" size="sm" disabled={paginaFim >= total || loading} onClick={() => setOffset(offset + limit)} aria-label="Próxima página"><ChevronRight className="h-4 w-4" /></Button>
                </div>
              </div>
            </section>
          )}
        </>
      )}
    </div>
  );
}
