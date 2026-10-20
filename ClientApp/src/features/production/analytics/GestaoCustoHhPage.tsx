// Nova tela de análise por disciplina. Leitura canônica do Resumo + movimento datado do Timesheet.
// O corte de datas não muda retroativamente o consolidado gerencial nem data complementos históricos.
import {useEffect,useMemo,useState} from "react";
import {Search,CalendarDays,ChevronRight,Loader2,ArrowLeft,AlertTriangle} from "lucide-react";
import {usePmoReports,formatHH,formatCurrency,docKey} from "@/components/pmo/pmoData";
import {projectFacts,enrichDocuments,factKey,balance,type FactsReport} from "@/components/pmo/pmoFacts";
import type {PmoSelecionado} from "@/components/pmo/pmoMultiCore";
import ConsolidadoBanner from "@/components/pmo/ConsolidadoBanner";
import {ReportState} from "@/components/reports/ReportComponents";
import {cn} from "@/lib/utils";
import {buscarDisciplinas,buscarDocumentos,buscarPessoas,FILTROS_VAZIOS,horas,dataCurta,type Disciplina as DisciplinaTs,type PaginaDocumentos,type PaginaPessoas,type FiltrosLancamentos} from "../lancamentos/lancamentosApi";
import {DrilldownLancamentos,EstadoErro,Paginacao} from "../lancamentos/LancamentosShared";
import {disciplinasParaExibicao,pertenceAoGrupo} from "./disciplinasApresentacao";

const HH=(n:number|null|undefined)=>n==null?"—":formatHH(n);
const BRL=(n:number|null|undefined)=>n==null?"—":formatCurrency(n);
const td="px-3 py-3 text-xs";
const th="px-3 py-2 text-left text-[10px] font-black uppercase tracking-wide text-muted-foreground";
type Aba="documentos"|"periodo"|"pessoas";
export default function GestaoCustoHhPage({selection}:{selection:PmoSelecionado[]}){
  const projectId=selection[0]?.id;
  const {reports,falhas,loading,error,retry}=usePmoReports("gestao-vista",{hub:"",project:"",selection});
  const fr=reports as FactsReport[];
  const facts=useMemo(()=>projectFacts(fr),[fr]);
  const docs=useMemo(()=>enrichDocuments(fr),[fr]);
  const [disc,setDisc]=useState("");
  const [dataForm,setDataForm]=useState({from:"",to:""});
  const [periodo,setPeriodo]=useState({from:"",to:""});
  const [aba,setAba]=useState<Aba>("documentos");
  const [pageDocs,setPageDocs]=useState(1);
  const [pagePeople,setPagePeople]=useState(1);
  const [docsMov,setDocsMov]=useState<PaginaDocumentos>();
  const [people,setPeople]=useState<PaginaPessoas>();
  const [discTs,setDiscTs]=useState<DisciplinaTs[]>([]);
  const [busy,setBusy]=useState(false);
  const [fetchError,setFetchError]=useState<unknown>(null);
  const [detail,setDetail]=useState<{document:string;title:string}|null>(null);
  useEffect(()=>{setDisc("");setPeriodo({from:"",to:""});setDataForm({from:"",to:""});setDetail(null);},[projectId]);
  useEffect(()=>{setPageDocs(1);setPagePeople(1);setDetail(null);},[disc,periodo]);
  const disciplinas=useMemo(()=>disciplinasParaExibicao(fr),[fr]);
  const selected=disciplinas.find(d=>d.chave===disc);
  const eligible=docs.filter(d=>!d.isActivity&&(!disc||(selected&&pertenceAoGrupo(d.disciplinaCanonica,d.disciplina,selected)))).sort((a,b)=>(a.codigo??"").localeCompare(b.codigo??""));
  const emitidos=eligible.filter(d=>d.statusDocumento==="EMITIDO").length;
  const hist=fr.flatMap(r=>r.actual?.historicalMeasurement?.disciplines??[]).filter(d=>!disc||selected?.origens.includes(factKey(d.discipline)));
  const histHH=hist.reduce((s,d)=>s+(d.balance?.complementHours??0),0);
  const histCusto=hist.reduce((s,d)=>s+(d.balance?.complementCost??0),0);
  // Recorte financeiro DATADO do Timesheet (não inclui as medições históricas sem data de lançamento).
  // A disciplina vem prioritariamente do documento canônico e não do texto livre do lançamento.
  const corteFinanceiro=useMemo(()=>{
    const index=new Map(docs.map(d=>[docKey(d.projectId,d.codigo),d.disciplina] as const));
    let valor=0,comCusto=0,semCusto=0;
    for(const r of fr)for(const l of r.lancamentos){
      const day=l.dataLancamento?.slice(0,10);
      if(!day||(periodo.from&&day<periodo.from)||(periodo.to&&day>periodo.to))continue;
      const disciplina=index.get(docKey(l.projectId,l.documentoCodigo))??l.disciplina;
      if(disc&&!selected?.origens.includes(factKey(disciplina)))continue;
      if(l.custoCalculado==null)semCusto++;
      else {comCusto++;valor+=l.custoCalculado;}
    }
    return {valor:comCusto?valor:null,comCusto,semCusto};
  },[docs,fr,disc,periodo]);
  // Filtros da API recebem os rótulos ORIGINAIS de cada origem, nunca o nome visual.
  // Se duas origens forem equivalentes pela Norma, ambas são consultadas e somadas.
  const filters=useMemo<FiltrosLancamentos>(()=>({...FILTROS_VAZIOS,...periodo}),[periodo]);
  const sourceKeys=selected?.origens.filter(x=>x!=="__PMO_NULL__")??[];
  const sourceFingerprint=sourceKeys.join("|");
  useEffect(()=>{
    if(!projectId||!disc||!sourceFingerprint){setDiscTs([]);setDocsMov(undefined);setPeople(undefined);setFetchError(null);return;}
    let active=true;setBusy(true);setFetchError(null);
    const sources=sourceFingerprint.split("|");
    Promise.all(sources.map(async name=>{
      const ff={...filters,discipline:name};
      const [a,b,c]=await Promise.all([
        buscarDisciplinas(projectId,ff),buscarDocumentos(projectId,ff,pageDocs),
        buscarPessoas(projectId,ff,pagePeople)
      ]);
      return {a,b,c};
    })).then(res=>{
      if(!active)return;
      setDiscTs(res.flatMap(x=>x.a.items));
      // Evitar repetir o mesmo documento retornado por aliases distintos.
      const docs=new Map(res.flatMap(x=>x.b.items.map(d=>[d.documentCode,d] as const)));
      setDocsMov({
        ...res[0].b,items:[...docs.values()],total:res.reduce((s,x)=>s+x.b.total,0),
        totalPlannedHours:res.reduce((s,x)=>s+x.b.totalPlannedHours,0),
        totalWorkedHours:res.reduce((s,x)=>s+x.b.totalWorkedHours,0),
        workedHoursWithoutDocument:res.reduce((s,x)=>s+x.b.workedHoursWithoutDocument,0)
      });
      const people=new Map<string,PaginaPessoas["items"][number]>();
      for(const x of res)for(const person of x.c.items){
        const old=people.get(person.name);
        people.set(person.name,old?{...person,
          workedHours:old.workedHours+person.workedHours,
          entries:old.entries+person.entries,documents:old.documents+person.documents,
          plannedHours:old.plannedHours+person.plannedHours,
          difference:old.difference+person.difference}:person);
      }
      setPeople({...res[0].c,items:[...people.values()],
        total:people.size,
        totalPlannedHours:res.reduce((s,x)=>s+x.c.totalPlannedHours,0),
        totalWorkedHours:res.reduce((s,x)=>s+x.c.totalWorkedHours,0)});
    }).catch(e=>{if(active)setFetchError(e)})
      .finally(()=>{if(active)setBusy(false)});
    return()=>{active=false;};
  },[projectId,disc,sourceFingerprint,filters,pageDocs,pagePeople]);
  if(loading)return <ReportState title="Carregando Controle de HH e Custo…">Consultando o realizado gerencial e os documentos.</ReportState>;
  if(error)return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;
  if(!projectId)return <ReportState title="Selecione um projeto">Escolha um projeto para detalhar.</ReportState>;
  const c=facts.cards;
  const hp=selected?.HH_Previsto??c.HH_Previsto,hr=selected?.HH_Realizado??c.HH_Realizado;
  const cp=selected?.Custo_Previsto??c.Custo_Previsto,cr=selected?.Custo_Realizado??c.Custo_Realizado;
  const cutoffHh=discTs.length?discTs.reduce((sum,d)=>sum+d.workedHours,0):null;
  const pct=(a:number|null,b:number|null)=>a!=null&&b!=null&&b>0?HH(a/b*100)+"%":"—";
  const classHead=th+" text-right",classNum=td+" text-right tabular-nums";
  return <div className="space-y-5">
    <ConsolidadoBanner selection={selection} falhas={falhas}/>
    <section className="rounded-xl border border-border bg-card/60 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><h2 className="text-lg font-black">Gestão à Vista — Controle de Custo e HH</h2>
          <p className="mt-1 text-xs text-muted-foreground">Selecione uma disciplina para analisar horas, custos, documentos e pessoas que registraram atividades.</p></div>
        <span className="rounded-full border border-primary/40 px-3 py-1 text-[10px] font-black text-primary">ANÁLISE DETALHADA</span>
      </div>
      <form className="mt-4 flex flex-wrap items-end gap-3" onSubmit={e=>{e.preventDefault();if(dataForm.from&&dataForm.to&&dataForm.from>dataForm.to)return;setPeriodo(dataForm);}}>
        <label className="text-xs text-muted-foreground">De<input type="date" aria-label="Início do corte" className="mt-1 block h-9 rounded-md border border-border bg-background px-3" value={dataForm.from} onChange={e=>setDataForm(p=>({...p,from:e.target.value}))}/></label>
        <label className="text-xs text-muted-foreground">Até (data de corte)<input type="date" aria-label="Data de corte" className="mt-1 block h-9 rounded-md border border-border bg-background px-3" value={dataForm.to} onChange={e=>setDataForm(p=>({...p,to:e.target.value}))}/></label>
        <button type="submit" className="h-9 rounded-md bg-primary px-4 text-xs font-black text-primary-foreground">Aplicar corte</button>
        <button type="button" className="h-9 rounded-md border border-border px-3 text-xs hover:bg-accent" onClick={()=>{setPeriodo({from:"",to:""});setDataForm({from:"",to:""});}}>Limpar</button>
        {dataForm.from&&dataForm.to&&dataForm.from>dataForm.to&&<span role="alert" className="text-xs text-rose-500">Intervalo inválido.</span>}
      </form>
      <p className="mt-3 text-[11px] leading-relaxed text-muted-foreground"><b className="text-foreground">Importante:</b> HH e custos gerenciais são acumulados oficiais, incluindo complementos históricos. A data de corte filtra apenas os lançamentos datados do Timesheet nas abas Movimentação e Profissionais; o custo calculado no corte vem dos lançamentos datados, sem atribuir datas às medições históricas.</p>
    </section>
    <section aria-label="Disciplinas">
      <h3 className="mb-2 text-[10px] font-black uppercase tracking-[.2em] text-muted-foreground">Disciplinas — clique para analisar</h3>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
        <button aria-pressed={!disc} onClick={()=>setDisc("")} className={cn("rounded-xl border border-t-4 p-4 text-left hover:border-primary",!disc?"border-primary bg-primary/10":"border-border border-t-primary/30 bg-card/50")}>
          <b>Visão geral do projeto</b><p className="mt-3 text-xs text-muted-foreground">HH previstas <b className="text-foreground">{HH(c.HH_Previsto)}</b></p>
          <p className="mt-1 text-xs text-muted-foreground">HH realizadas <b className="text-foreground">{HH(c.HH_Realizado)}</b></p>
        </button>
        {disciplinas.map(d=><button key={d.chave} aria-pressed={disc===d.chave} onClick={()=>{setDisc(d.chave);setAba("documentos")}} className={cn("rounded-xl border border-t-4 p-4 text-left transition-colors hover:border-primary",disc===d.chave?"border-primary bg-primary/10":"border-border border-t-primary/40 bg-card/50")}>
          <div className="flex justify-between gap-2"><b className="truncate text-sm" title={d.diagnostico??d.Disciplina}>{d.Disciplina}</b><ChevronRight className="h-4 w-4 text-primary"/></div>
          {d.diagnostico&&<div className="mt-1 text-[10px] text-amber-500">{d.diagnostico}</div>}
          <div className="mt-3 grid grid-cols-2 gap-2 text-xs"><span>HH previstas<br/><b className="text-base text-foreground">{HH(d.HH_Previsto)}</b></span><span>HH realizadas<br/><b className="text-base text-foreground">{HH(d.HH_Realizado)}</b></span></div>
          <div className="mt-3 flex justify-between gap-2 border-t border-border/60 pt-2 text-[11px]"><span>Saldo <b className={cn((d.SaldoHH??0)<0&&"text-rose-500")}>{HH(d.SaldoHH)}</b></span><span>Consumo <b>{d.ConsumoHH==null?"—":HH(d.ConsumoHH)+"%"}</b></span></div>
        </button>)}
      </div>
    </section>
    <section className="space-y-3 rounded-xl border border-border bg-card/40 p-4" aria-label="Detalhamento da disciplina">
      <div className="flex flex-wrap justify-between gap-3"><h3 className="text-lg font-black">{selected?.Disciplina??"Consolidado do projeto"}</h3>{disc&&<button className="inline-flex items-center gap-1 text-xs text-primary hover:underline" onClick={()=>setDisc("")}><ArrowLeft className="h-4 w-4"/>Todas</button>}</div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Metrica label="HH previstas" value={HH(hp)}/>
        <Metrica label="HH realizadas" value={HH(hr)} sub="Gerencial, com medições históricas"/>
        <Metrica label="Saldo HH" value={HH(balance(hp,hr))} danger={(balance(hp,hr)??0)<0}/>
        <Metrica label="Consumo HH" value={pct(hr,hp)} danger={hr!=null&&hp!=null&&hr>hp}/>
        <Metrica label="Custo previsto" value={BRL(cp)}/>
        <Metrica label="Custo realizado" value={BRL(cr)}/>
        <Metrica label="Saldo de custo" value={BRL(balance(cp,cr))} danger={(balance(cp,cr)??0)<0}/>
        <Metrica label="Documentos emitidos" value={String(emitidos)} sub={String(eligible.length)+" documentos no recorte"}/>
      </div>
      {histHH>0&&<p className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-muted-foreground"><b className="text-amber-500">Medição histórica complementar:</b> {HH(histHH)} HH · {BRL(histCusto)} de custo, sem lançamento datado no Timesheet.</p>}
      <div role="tablist" aria-label="Visões de análise" className="flex flex-wrap gap-2">
        {([["documentos","Documentos"],["periodo","Movimentação no corte"],["pessoas","Pessoas que lançaram"]] as const).map(([key,label])=><button key={key} role="tab" aria-selected={aba===key} onClick={()=>setAba(key)} className={cn("rounded-md border px-3 py-2 text-xs font-bold",aba===key?"border-primary bg-primary/10 text-primary":"border-border hover:bg-accent")}>{label}</button>)}
      </div>
      {aba==="documentos"&&<div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full min-w-[1020px]"><thead className="border-b border-border bg-muted/30"><tr>
          <th className={th}>Documento</th><th className={th}>Situação</th><th className={classHead}>HH previstas</th><th className={classHead}>HH realizadas</th><th className={classHead}>Saldo HH</th><th className={classHead}>Custo previsto</th><th className={classHead}>Custo realizado</th><th className={classHead}>Saldo</th><th className={th}>Detalhes</th>
        </tr></thead><tbody>{eligible.map(d=><tr key={d.rowId} className="border-b border-border/60 hover:bg-accent/30">
          <td className={td}><b className="block max-w-[260px] truncate">{d.codigo??"SEM CÓDIGO"}</b><span className="block max-w-[260px] truncate text-[11px] text-muted-foreground" title={d.titulo??""}>{d.titulo}</span></td>
          <td className={td}><span className="text-[10px] font-bold">{d.statusDocumento.replaceAll("_"," ")}</span></td>
          <td className={classNum}>{HH(d.hhPrevisto)}</td><td className={classNum}>{HH(d.hhRealizado)}</td>
          <td className={cn(classNum,(d.saldoHh??0)<0&&"text-rose-500")}>{HH(d.saldoHh)}</td>
          <td className={classNum}>{BRL(d.custoPrevisto)}</td><td className={classNum}>{BRL(d.custoRealizado)}</td>
          <td className={cn(classNum,(d.saldoCusto??0)<0&&"text-rose-500")}>{BRL(d.saldoCusto)}</td>
          <td className={td}><button disabled={!d.codigo} onClick={()=>setDetail({document:d.codigo!,title:d.titulo??""})} className="inline-flex items-center gap-1 text-primary hover:underline disabled:opacity-30"><Search className="h-4 w-4"/>Lançamentos</button></td></tr>)}
          {eligible.length===0&&<tr><td colSpan={9} className="p-5 text-center text-xs text-muted-foreground">Nenhum documento na disciplina.</td></tr>}
        </tbody></table>
      </div>}
      {aba!=="documentos"&&(!disc?<p className="rounded-lg border border-border p-4 text-xs text-muted-foreground">Selecione uma disciplina para ver os profissionais e documentos movimentados.</p>:
        fetchError?<EstadoErro erro={fetchError}/>:
        <div className="space-y-3">
          <p className="text-xs text-muted-foreground"><CalendarDays className="mr-1 inline h-4 w-4"/>Timesheet de {periodo.from?dataCurta(periodo.from):"início"} até {periodo.to?dataCurta(periodo.to):"último lançamento"}. Complementos gerenciais sem data não são incluídos neste recorte.</p>
          {busy&&<p className="inline-flex items-center gap-2 text-xs text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/> Atualizando recorte…</p>}
          {aba==="periodo"?<><div className="grid gap-3 sm:grid-cols-2">
              <Metrica label="HH lançadas no corte" value={cutoffHh==null?"—":horas(cutoffHh)} sub="Timesheet corrigido / auditoria"/>
              <Metrica label="Custo calculado no corte" value={BRL(corteFinanceiro.valor)} sub={"Timesheet datado · "+corteFinanceiro.semCusto+" lançamento(s) sem custo"}/>
            </div>
            <div className="overflow-x-auto rounded-lg border border-border"><table className="w-full min-w-[630px]"><thead><tr><th className={th}>Documento</th><th className={classHead}>HH no corte</th><th className={classHead}>HH lançadas (total)</th><th className={classHead}>Pessoas</th><th className={th}>Detalhes</th></tr></thead>
            <tbody>{(docsMov?.items??[]).map(d=><tr key={d.documentCode} className="border-t border-border/60"><td className={td}>{d.documentCode}</td><td className={classNum}>{horas(d.workedHours)}</td><td className={classNum}>{horas(d.totalWorkedHours)}</td><td className={classNum}>{d.people}</td><td className={td}><button className="text-primary hover:underline" onClick={()=>setDetail({document:d.documentCode,title:d.document??""})}>Ver lançamentos</button></td></tr>)}</tbody></table>
            <Paginacao pagina={pageDocs} total={docsMov?.total??0} onPagina={setPageDocs} rotulo="documentos"/></div></>:
            <div className="overflow-x-auto rounded-lg border border-border"><table className="w-full min-w-[580px]"><thead><tr><th className={th}>Profissional</th><th className={classHead}>HH lançadas</th><th className={classHead}>Documentos</th><th className={classHead}>Lançamentos</th><th className={th}>Último lançamento</th></tr></thead>
            <tbody>{(people?.items??[]).map(p=><tr key={p.name} className="border-t border-border/60"><td className={td}>{p.name}</td><td className={classNum}>{horas(p.workedHours)}</td><td className={classNum}>{p.documents}</td><td className={classNum}>{p.entries}</td><td className={td}>{dataCurta(p.lastEntry)}</td></tr>)}</tbody></table><Paginacao pagina={pagePeople} total={people?.total??0} onPagina={setPagePeople} rotulo="profissionais"/></div>}
        </div>)}
    </section>
    {detail&&<DrilldownLancamentos projectId={projectId} filtros={{...FILTROS_VAZIOS,...periodo}} recorte={{document:detail.document}} titulo={detail.document} descricao={detail.title} onFechar={()=>setDetail(null)}/>}
    <p className="flex gap-2 text-[11px] text-muted-foreground"><AlertTriangle className="h-4 w-4"/>Fontes diferentes: total gerencial inclui medições confirmadas, filtro temporal se refere apenas ao Timesheet original. Somente leitura.</p>
  </div>;
}
function Metrica({label,value,sub,danger}:{label:string;value:string;sub?:string;danger?:boolean}){
  return <div className={cn("rounded-xl border border-t-4 bg-card/60 p-4",danger?"border-rose-500/40 border-t-rose-500":"border-border border-t-primary/60")}>
    <p className="text-[10px] font-black uppercase tracking-wide text-muted-foreground">{label}</p>
    <p className={cn("mt-1 text-xl font-black tabular-nums",danger&&"text-rose-500")}>{value}</p>
    {sub&&<p className="mt-1 text-[10px] text-muted-foreground">{sub}</p>}
  </div>
}
