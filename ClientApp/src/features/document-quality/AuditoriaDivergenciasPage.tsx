import { Fragment, useEffect, useMemo, useState } from "react";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { formatCurrency, formatHH } from "@/components/pmo/pmoData";
import { AlertTriangle, ChevronDown, ChevronRight, Download, FileSearch, RefreshCw } from "lucide-react";
import { Link } from "react-router-dom";

interface DocRow {
  document: string; ldpHh: number; ldpCost: number; mirrorHh: number | null; mirrorCost: number | null;
  timesheetAllHh: number; timesheetAllCost: number; timesheetEngHh: number; timesheetEngCost: number;
  managementHh: number; managementCost: number; missingCostHh: number;
  differenceHh: number; differenceCost: number; entries: number; reason: string;
}
interface PersonRow {
  name: string; hours: number; knownCost: number; missingCostHh: number;
  mirrorHh: number | null; mirrorCost: number | null; management: boolean;
  entries: number; documents: number;
}
interface Launch {
  document: string; date: string | null; person: string; hh: number; cost: number | null;
  costStatus: string; management: boolean; description: string;
}
interface DayRow {
  date: string | null; person: string; rawDayHh: number; splitDocumentHh: number;
  differenceHh: number; management: boolean;
}
interface Audit {
  rawDayHh: number; rawDayEngineeringHh: number; rawDayManagementHh: number;
  unreconciledEngineeringHh: number; dailyReconciliation: DayRow[]; splitEntries: number;
  projectCode: string; ldpHh: number; ldpCost: number; mirrorHh: number | null; mirrorCost: number | null;
  timesheetAllHh: number; timesheetAllKnownCost: number;
  timesheetEngineeringHh: number; timesheetEngineeringKnownCost: number;
  managementHh: number; managementKnownCost: number; missingCostHh: number;
  differenceHh: number; differenceCost: number; mirrorError: string | null;
  documents: DocRow[]; people: PersonRow[]; entries: Launch[];
}
type Tab = "hh" | "cost" | "days" | "people" | "entries";
const n = (v: number, format: (value: number) => string) => `${v > 0 ? "+" : ""}${format(v)}`;
const clean = (v: string) => v.toLocaleLowerCase("pt-BR").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
const dateText = (v: string | null) => v ? new Date(v).toLocaleDateString("pt-BR", {timeZone:"UTC"}) : "—";
const hhDiff = (v: number) => Math.abs(v) > 0.005;
const costDiff = (v: number) => Math.abs(v) > 0.01;

export default function AuditoriaDivergenciasPage() {
  const { projetoAtual } = useProjetoAtual();
  const projectId = projetoAtual?.projectId;
  const [data, setData] = useState<Audit | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [tab, setTab] = useState<Tab>("hh");
  const [search, setSearch] = useState("");
  const [onlyDiff, setOnlyDiff] = useState(true);
  const [withManagement, setWithManagement] = useState(true);
  const [selectedDocument, setSelectedDocument] = useState<string | null>(null);

  useEffect(() => {
    if (!projectId) { setData(null); return; }
    const abort = new AbortController();
    setLoading(true); setError(""); setData(null); setSelectedDocument(null);
    fetch(`/api/production-audit/audit-differences?projectId=${encodeURIComponent(projectId)}`,
      { credentials: "include", signal: abort.signal })
      .then(async response => {
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body.message ?? "Não foi possível calcular as diferenças.");
        return body as Audit;
      })
      .then(value => { if (!abort.signal.aborted) setData(value); })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof Error ? e.message : "Erro na auditoria."); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [projectId, revision]);

  // Ao comparar a distribuição com a LDP, permitir incluir Gestão porque a LDP não traz exclusão por profissional.
  const allocatedHh = (d: DocRow) => withManagement ? d.timesheetAllHh : d.timesheetEngHh;
  const allocatedCost = (d: DocRow) => withManagement ? d.timesheetAllCost : d.timesheetEngCost;
  const hhDelta = (d: DocRow) => allocatedHh(d) - d.ldpHh;
  const costDelta = (d: DocRow) => allocatedCost(d) - d.ldpCost;
  const docs = useMemo(() => {
    const rows = (data?.documents ?? []).filter(d =>
      clean(d.document).includes(clean(search)) &&
      (!onlyDiff || (tab === "cost" ? costDiff(costDelta(d)) : hhDiff(hhDelta(d)))));
    rows.sort((a,b) => tab === "cost"
      ? Math.abs(costDelta(b)) - Math.abs(costDelta(a))
      : Math.abs(hhDelta(b)) - Math.abs(hhDelta(a)));
    return rows;
  }, [data,search,tab,onlyDiff,withManagement]);
  const people = useMemo(() => (data?.people ?? [])
    .filter(p => clean(p.name).includes(clean(search)) && (!onlyDiff || p.management || p.missingCostHh > 0 ||
      (p.mirrorCost !== null && costDiff(p.knownCost - p.mirrorCost))))
    .sort((a,b) => b.missingCostHh - a.missingCostHh || b.knownCost - a.knownCost),[data,search,onlyDiff]);
  const days = useMemo(() => (data?.dailyReconciliation ?? [])
    .filter(d => clean(d.person).includes(clean(search)) ||
      dateText(d.date).includes(search.trim()))
    .sort((a,b) => Math.abs(b.differenceHh) - Math.abs(a.differenceHh)),
    [data,search]);
  const entries = useMemo(() => (data?.entries ?? [])
    .filter(x => (clean(x.person).includes(clean(search)) || clean(x.document).includes(clean(search)) ||
      clean(x.description).includes(clean(search))) &&
      (!selectedDocument || x.document === selectedDocument) &&
      (!onlyDiff || x.management || x.cost === null ||
        (data?.documents.find(d => d.document === x.document && (hhDiff(hhDelta(d)) || costDiff(costDelta(d)))) !== undefined)))
    .sort((a,b) => (b.date || "").localeCompare(a.date || "")),[data,search,onlyDiff,selectedDocument,withManagement]);

  function downloadCsv() {
    if (!data) return;
    if (tab === "days") {
      const headerDays = ["Data","Profissional","HH Timesheet diário","HH distribuídas em documentos","Diferença (diário - distribuído)","Gestão"];
      const rowsDays = data.dailyReconciliation.map(d =>
        [dateText(d.date), d.person, d.rawDayHh, d.splitDocumentHh, d.differenceHh, d.management ? "Sim" : "Não"]);
      const csvDays = [headerDays,...rowsDays].map(row =>
        row.map(x => `"${String(x).replace(/"/g,'""')}"`).join(";")).join("\r\n");
      const url = URL.createObjectURL(new Blob(["\ufeff",csvDays],{type:"text/csv;charset=utf-8"}));
      const a=document.createElement("a");a.href=url;a.download=`conciliacao-diaria-${data.projectCode}.csv`;
      a.click();URL.revokeObjectURL(url);
      return;
    }
    const header = ["Número do Documento","HH LDP","HH Espelho","HH distribuídas total","HH Gestão distribuídas","HH Engenharia distribuídas","Diferença HH Engenharia - LDP","Custo LDP","Custo Espelho","Custo distribuído conhecido","Custo Gestão distribuído","Custo Engenharia distribuído","Diferença custo Engenharia - LDP","HH sem custo distribuídas","Sinais para investigar"];
    const rows = data.documents.map(x => [
      x.document,x.ldpHh,x.mirrorHh ?? "",x.timesheetAllHh,x.managementHh,x.timesheetEngHh,x.differenceHh,
      x.ldpCost,x.mirrorCost ?? "",x.timesheetAllCost,x.managementCost,x.timesheetEngCost,x.differenceCost,x.missingCostHh,x.reason,
    ]);
    const csv = [header,...rows].map(row => row.map(x => `"${String(x).replace(/"/g,'""')}"`).join(";")).join("\r\n");
    const url = URL.createObjectURL(new Blob(["\ufeff",csv],{type:"text/csv;charset=utf-8"}));
    const a=document.createElement("a"); a.href=url; a.download=`auditoria-diferencas-${data.projectCode}.csv`; a.click(); URL.revokeObjectURL(url);
  }

  return <div className="space-y-5">
    <header className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <p className="text-xs font-bold uppercase tracking-widest text-muted-foreground">Qualidade · Conferência por fonte</p>
        <h1 className="text-xl font-black">Auditoria de Divergências de HH e Custos</h1>
        <p className="mt-1 text-sm text-muted-foreground">Veja exatamente quais documentos apresentam diferença e abra os lançamentos para investigar a causa.</p>
        <p className="text-xs text-muted-foreground">Projeto: <strong>{projetoAtual?.nome ?? data?.projectCode ?? "Selecione um projeto"}</strong></p>
      </div>
      <div className="flex gap-2">
        <Link className="rounded-lg border border-border px-3 py-2 text-xs font-semibold hover:bg-accent"
          to="/qualidade/comparativo-hh-custos">Abrir comparativo</Link>
        <button type="button" disabled={loading || !projectId} onClick={() => setRevision(v => v+1)}
          className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-semibold hover:bg-accent disabled:opacity-50"><RefreshCw size={14}/> Atualizar</button>
        <button type="button" disabled={!data} onClick={downloadCsv}
          className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-semibold hover:bg-accent disabled:opacity-50"><Download size={14}/> Exportar CSV</button>
      </div>
    </header>
    {loading && <p className="rounded-xl border border-border p-5 text-sm">Conferindo LDP, espelho gerencial e lançamentos do Timesheet...</p>}
    {error && <p role="alert" className="rounded-xl border border-rose-500/30 p-5 text-sm text-rose-600"><AlertTriangle className="mr-2 inline" size={16}/>{error}</p>}
    {!projectId && <p className="rounded-xl border p-4 text-sm">Selecione um projeto na Home.</p>}
    {data && <>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Metric title="HH LDP" value={formatHH(data.ldpHh)} sub="VW_Ldp_Unificada: realizado total corrigido"/>
        <Metric title="HH distribuídas / Engenharia" value={formatHH(data.timesheetEngineeringHh)}
          sub={`Fonte por Número do Documento: ${formatHH(data.timesheetAllHh)} − Gestão: ${formatHH(data.managementHh)}`}/>
        <Metric title="Diferença documental HH" value={n(data.differenceHh,formatHH)}
          sub="HH distribuídas de Engenharia − LDP (LDP não exclui Gestão)" alert={hhDiff(data.differenceHh)}/>
        <Metric title="HH sem custo" value={formatHH(data.missingCostHh)} sub="Engenharia sem custo/hora reconhecido" alert={data.missingCostHh>0}/>
        <Metric title="Custo LDP" value={formatCurrency(data.ldpCost)} sub="Custo realizado corrigido da LDP"/>
        <Metric title="Custo distribuído / Engenharia" value={formatCurrency(data.timesheetEngineeringKnownCost)} sub="Soma de HH distribuídas × taxa vigente reconhecida"/>
        <Metric title="Diferença documental de custo" value={n(data.differenceCost,formatCurrency)} sub="Custo Engenharia distribuído − LDP; parcial se faltar taxa" alert={costDiff(data.differenceCost)}/>
        <Metric title="Gestão na distribuição" value={formatHH(data.managementHh)} sub={`${formatCurrency(data.managementKnownCost)} de custo conhecido`}/>
        <Metric title="HH diária / Engenharia" value={formatHH(data.rawDayEngineeringHh)} sub={`Dia bruto: ${formatHH(data.rawDayHh)} − Gestão: ${formatHH(data.rawDayManagementHh)}`}/>
        <Metric title="HH não conciliadas no rateio" value={n(data.unreconciledEngineeringHh,formatHH)} sub="HH diárias de Engenharia − soma distribuída por documento" alert={hhDiff(data.unreconciledEngineeringHh)}/>
        <Metric title="Diferença geral de HH" value={n(data.rawDayEngineeringHh-data.ldpHh,formatHH)} sub="HH diárias Engenharia − LDP; base diferente da análise documental"
          alert={hhDiff(data.rawDayEngineeringHh-data.ldpHh)}/>
      </div>

      <section className="rounded-xl border border-border bg-card/60 p-4 text-sm">
        <h2 className="mb-2 font-bold">Entenda de onde vem a diferença</h2>
        <div className="grid gap-3 md:grid-cols-3">
          <div><span className="text-xs text-muted-foreground">1. LDP (valor do documento)</span>
            <div className="text-base font-black">{formatHH(data.ldpHh)} · {formatCurrency(data.ldpCost)}</div></div>
          <div><span className="text-xs text-muted-foreground">2. Espelho gerencial (Resumo atual)</span>
            <div className="text-base font-black">{data.mirrorHh === null ? "Indisponível" : `${formatHH(data.mirrorHh)} · ${formatCurrency(data.mirrorCost ?? 0)}`}</div>
            {data.mirrorHh !== null && <p className="text-[11px] text-muted-foreground">Espelho − LDP: {n(data.mirrorHh-data.ldpHh,formatHH)} · {n((data.mirrorCost??0)-data.ldpCost,formatCurrency)}</p>}</div>
          <div><span className="text-xs text-muted-foreground">3. Timesheet distribuído por Número do Documento</span>
            <div className="text-base font-black">{formatHH(data.timesheetEngineeringHh)} · {formatCurrency(data.timesheetEngineeringKnownCost)}</div>
            <p className="text-[11px] text-muted-foreground">Distribuído incluindo Gestão: {formatHH(data.timesheetAllHh)} · {formatCurrency(data.timesheetAllKnownCost)}</p></div>
        </div>
        {data.mirrorError && <p className="mt-3 text-xs text-amber-600"><AlertTriangle className="mr-1 inline" size={13}/>{data.mirrorError}</p>}
        <p className="mt-3 text-xs text-muted-foreground">
          A conferência documental usa tbTimeSheetRealizadoDocumento: cada Número do Documento recebe suas próprias HH.
          A conferência diária usa tbTimeSheet e NÃO redistribui horas. A diferença entre as duas fontes é mostrada
          em "Conciliação por dia". A LDP não exclui Gestão automaticamente; use a opção de comparação abaixo para
          verificar tanto o total como somente Engenharia. Nenhum lançamento original é modificado.
        </p>
      </section>

      <section className="rounded-xl border border-border bg-card/60 p-4">
        <div className="mb-4 flex flex-wrap items-center gap-2">
          {([["hh","Diferenças de HH"],["cost","Diferenças de custo"],["days","Conciliação por dia"],["people","Profissionais"],["entries","Lançamentos"]] as const).map(([id,label]) =>
            <button key={id} type="button" onClick={() => {setTab(id);setSelectedDocument(null);}}
              className={`rounded-lg px-3 py-2 text-xs font-semibold ${tab===id ? "bg-primary text-primary-foreground" : "border border-border hover:bg-accent"}`}>{label}</button>)}
          <label className="ml-auto flex items-center gap-1.5 text-xs">
            <input type="checkbox" checked={onlyDiff} onChange={e => setOnlyDiff(e.target.checked)} /> Apenas itens para conferir
          </label>
          {(tab === "hh" || tab === "cost" || tab === "entries") && <label className="flex items-center gap-1.5 text-xs">
            <input type="checkbox" checked={withManagement} onChange={e => setWithManagement(e.target.checked)} />
            Incluir Gestão na comparação documental
          </label>}
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Filtrar documento ou pessoa"
            className="min-w-[200px] rounded-lg border border-border bg-background px-3 py-2 text-xs"/>
        </div>

        {(tab==="hh" || tab==="cost") && <>
          <p className="mb-3 text-xs text-muted-foreground">
            Ordenado pelo maior impacto de {tab==="hh" ? "HH" : "custo"}. LDP × distribuição por documento
            {withManagement ? " incluindo Gestão (mesmo escopo de origem)" : " apenas Engenharia (LDP não exclui Gestão)"}.
            Clique no Número do Documento para conferir cada lançamento distribuído.
          </p>
          <div className="overflow-x-auto"><table className="w-full min-w-[1020px] text-left text-xs">
            <thead><tr className="border-b text-muted-foreground">
              <th className="py-2 pr-3">Documento / motivo</th><th className="py-2 pr-2 text-right">HH LDP</th>
              <th className="py-2 pr-2 text-right">HH distribuídas</th><th className="py-2 pr-2 text-right">Δ HH</th>
              <th className="py-2 pr-2 text-right">Custo LDP</th><th className="py-2 pr-2 text-right">Custo distribuído</th>
              <th className="py-2 pr-2 text-right">Δ custo</th><th className="py-2 pr-2 text-right">Gestão (HH)</th>
              <th className="py-2">Abrir</th>
            </tr></thead>
            <tbody>{docs.map(d => <Fragment key={d.document}>
              <tr className="border-b border-border/60">
                <td className="py-3 pr-3"><strong className="font-semibold">{d.document}</strong>
                  <p className="mt-1 max-w-sm text-[10px] text-muted-foreground">{d.reason}</p></td>
                <td className="py-3 pr-2 text-right tabular-nums">{formatHH(d.ldpHh)}</td>
                <td className="py-3 pr-2 text-right tabular-nums" title={`Total distribuído incluindo Gestão: ${formatHH(d.timesheetAllHh)}`}>{formatHH(allocatedHh(d))}</td>
                <td className={`py-3 pr-2 text-right font-bold tabular-nums ${hhDiff(hhDelta(d)) ? "text-amber-600" : ""}`}>{n(hhDelta(d),formatHH)}</td>
                <td className="py-3 pr-2 text-right tabular-nums">{formatCurrency(d.ldpCost)}</td>
                <td className="py-3 pr-2 text-right tabular-nums">{formatCurrency(allocatedCost(d))}</td>
                <td className={`py-3 pr-2 text-right font-bold tabular-nums ${costDiff(costDelta(d)) ? "text-amber-600" : ""}`}>{n(costDelta(d),formatCurrency)}</td>
                <td className="py-3 pr-2 text-right tabular-nums">{formatHH(d.managementHh)}</td>
                <td className="py-3">
                  <button type="button" className="inline-flex items-center gap-1 font-semibold text-primary hover:underline"
                    onClick={() => setSelectedDocument(selectedDocument===d.document ? null : d.document)}>
                    {selectedDocument===d.document ? <ChevronDown size={14}/> : <ChevronRight size={14}/>} Lançamentos
                  </button>
                </td>
              </tr>
              {selectedDocument===d.document && <tr key={d.document+"-detail"} className="border-b bg-muted/20">
                <td colSpan={9} className="p-3">
                  <p className="mb-2 font-bold"><FileSearch size={14} className="mr-1 inline"/> {d.entries} lançamento(s) · HH sem custo: {formatHH(d.missingCostHh)} · Espelho: {d.mirrorHh===null?"indisponível":formatHH(d.mirrorHh)}</p>
                  <div className="max-h-80 overflow-auto">
                    <table className="w-full text-xs"><thead><tr className="border-b text-muted-foreground"><th>Data</th><th>Profissional</th><th className="text-right">HH</th><th className="text-right">Custo</th><th>Situação</th></tr></thead>
                      <tbody>{data.entries.filter(e => e.document===d.document).map((e,i) =>
                        <tr key={i} className="border-b border-border/40"><td className="py-1.5">{dateText(e.date)}</td><td>{e.person}</td>
                          <td className="text-right">{formatHH(e.hh)}</td><td className="text-right">{e.cost===null?"Sem custo":formatCurrency(e.cost)}</td>
                          <td>{e.management ? "Gestão (não computar)" : e.costStatus==="VIGENTE_NA_DATA" ? "Engenharia" : e.costStatus}</td></tr>)}</tbody>
                    </table>
                  </div>
                  <button type="button" onClick={() => {setTab("entries");setSearch(d.document);setSelectedDocument(null);setOnlyDiff(false);}}
                    className="mt-2 text-xs font-semibold text-primary hover:underline">Ver descrições completas dos lançamentos →</button>
                </td>
              </tr>}
            </Fragment>)}</tbody>
          </table></div>
          {!docs.length && <p className="py-5 text-sm text-muted-foreground">Nenhum documento atende aos filtros.</p>}
          <p className="mt-3 text-[11px] text-muted-foreground">Δ = HH distribuídas para o documento − LDP. O comparador inclui ou exclui Gestão conforme a opção acima; o custo é parcial quando faltam taxas.</p>
        </>}

        {tab==="days" && <>
          <p className="mb-3 text-xs text-muted-foreground">
            Conferência por data e profissional: Timesheet diário (tbTimeSheet) contra a soma das linhas
            por Número do Documento (tbTimeSheetRealizadoDocumento). São mostrados os dias que não fecham,
            sem atribuir o saldo automaticamente a qualquer documento.
          </p>
          <div className="mb-3 flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
            <span>HH diária de Engenharia: <b className="text-foreground">{formatHH(data.rawDayEngineeringHh)}</b></span>
            <span>HH distribuídas de Engenharia: <b className="text-foreground">{formatHH(data.timesheetEngineeringHh)}</b></span>
            <span>Diferença: <b className="text-foreground">{n(data.unreconciledEngineeringHh,formatHH)}</b></span>
            <span>{days.length} dia(s)/profissional(is) para conferir</span>
          </div>
          <div className="max-h-[65vh] overflow-auto"><table className="w-full min-w-[700px] text-left text-xs">
            <thead className="sticky top-0 bg-card"><tr className="border-b text-muted-foreground">
              <th className="py-2">Data</th><th>Profissional</th><th className="text-right">HH dia bruto</th>
              <th className="text-right">HH distribuídas</th><th className="text-right">Δ dia − documentos</th><th>Classificação</th>
            </tr></thead>
            <tbody>{days.map((d,i) => <tr key={i} className="border-b border-border/50">
              <td className="py-2">{dateText(d.date)}</td><td>{d.person}</td>
              <td className="text-right tabular-nums">{formatHH(d.rawDayHh)}</td>
              <td className="text-right tabular-nums">{formatHH(d.splitDocumentHh)}</td>
              <td className="text-right font-bold tabular-nums text-amber-600">{n(d.differenceHh,formatHH)}</td>
              <td>{d.management ? "Gestão (fora da Engenharia)" : "Engenharia"}</td>
            </tr>)}</tbody>
          </table></div>
          {!days.length && <p className="py-4 text-sm text-muted-foreground">Todos os dias conciliados. Sem diferença superior a 0,005 HH.</p>}
        </>}

        {tab==="people" && <div className="overflow-x-auto"><table className="w-full min-w-[760px] text-left text-xs">
          <thead><tr className="border-b text-muted-foreground"><th className="py-2">Profissional</th><th className="text-right">HH bruto</th><th className="text-right">Custo conhecido</th><th className="text-right">HH sem custo</th><th className="text-right">Custo espelho</th><th>Classificação</th><th>Documentos</th></tr></thead>
          <tbody>{people.map(p => <tr key={p.name} className="border-b border-border/50">
            <td className="py-2 font-semibold">{p.name}<p className="text-[10px] font-normal text-muted-foreground">{p.entries} lançamento(s)</p></td>
            <td className="text-right">{formatHH(p.hours)}</td><td className="text-right">{formatCurrency(p.knownCost)}</td>
            <td className="text-right">{formatHH(p.missingCostHh)}</td><td className="text-right">{p.mirrorCost===null?"—":formatCurrency(p.mirrorCost)}</td>
            <td>{p.management ? "Gestão · excluído" : "Engenharia"}</td><td>{p.documents}</td>
          </tr>)}</tbody>
        </table><Link className="mt-3 inline-block text-xs font-semibold text-primary hover:underline" to="/qualidade/classificacao-profissionais">Alterar classificação de profissionais →</Link></div>}

        {tab==="entries" && <div className="max-h-[65vh] overflow-auto"><table className="w-full min-w-[950px] text-left text-xs">
          <thead className="sticky top-0 bg-card"><tr className="border-b text-muted-foreground">
            <th className="py-2">Data</th><th>Documento</th><th>Profissional</th><th className="text-right">HH</th>
            <th className="text-right">Custo</th><th>Situação</th><th>Descrição original</th>
          </tr></thead>
          <tbody>{entries.map((e,i) => <tr key={i} className="border-b border-border/50 align-top">
            <td className="py-2">{dateText(e.date)}</td><td>{e.document}</td><td>{e.person}</td>
            <td className="text-right">{formatHH(e.hh)}</td><td className="text-right">{e.cost===null?"—":formatCurrency(e.cost)}</td>
            <td>{e.management ? "Gestão" : e.cost===null ? e.costStatus : "Engenharia"}</td>
            <td className="max-w-[340px] whitespace-normal">{e.description || "—"}</td>
          </tr>)}</tbody>
        </table>{!entries.length && <p className="p-4 text-sm text-muted-foreground">Nenhum lançamento corresponde ao filtro.</p>}</div>}
      </section>
    </>}
  </div>;
}

function Metric({title,value,sub,alert}:{title:string;value:string;sub:string;alert?:boolean}) {
  return <div className={`rounded-xl border border-border border-t-4 bg-card/60 p-4 ${alert ? "border-t-amber-500" : "border-t-sky-500"}`}>
    <p className="text-xs font-bold uppercase tracking-wide text-muted-foreground">{title}</p>
    <p className="mt-2 text-xl font-black tabular-nums">{value}</p>
    <p className="mt-1 text-[11px] text-muted-foreground">{sub}</p>
  </div>;
}