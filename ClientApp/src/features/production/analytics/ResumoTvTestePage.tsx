/** Resumo TV experimental: não altera Resumo oficial, sem quantias monetárias ou conciliação histórica. */
import {useCallback,useEffect,useMemo,useRef,useState} from "react";
import {ArrowLeft,ArrowRight,Maximize2,Minimize2,Pause,Play} from "lucide-react";
import type {PmoSelecionado} from "@/components/pmo/pmoMultiCore";
import type {FactsReport} from "@/components/pmo/pmoFacts";
import {usePmoReports,formatHH} from "@/components/pmo/pmoData";
import {ReportState} from "@/components/reports/ReportComponents";
import {calcularResumo,consolidarAvanco,documentalDaLdp} from "./resumoLogic";
import {useLdpPorProjeto} from "./useLdpPorProjeto";
import {resumoDisciplinas,resumoFases,resumoPageSize,type PhaseField,type ResumoPhaseMetric} from "./resumoPhaseData";
import ResumoDisciplineCard,{ResumoPhaseStrip} from "./ResumoDisciplineCard";
import "./resumoTvTeste.css";
type Item={title:string;value:string;percentage?:number|null;color?:string;field?:PhaseField;phaseFormat?:(v:number|null)=>string};
const fmt=(n:number|null|undefined)=>typeof n==="number"&&Number.isFinite(n)?formatHH(n):"—";
const pct=(n:number|null|undefined)=>fmt(n)+(n===null||n===undefined?"":"%");
const ratio=(n:number|null|undefined,d:number|null|undefined)=>typeof n==="number"&&typeof d==="number"&&d>0?n/d*100:null;
export default function ResumoTvTestePage({selection}:{selection:PmoSelecionado[]}){
  const {reports,loading,error,retry}=usePmoReports("gestao-vista",{hub:"",project:"",selection});
  const ldp=useLdpPorProjeto(selection);
  const stage=useRef<HTMLDivElement>(null);
  const [fullscreen,setFullscreen]=useState(false),[auto,setAuto]=useState(true);
  const [page,setPage]=useState(0),[viewport,setViewport]=useState({width:window.innerWidth,height:window.innerHeight,available:window.innerHeight-166});
  useEffect(()=>{
    const resized=()=>setViewport({width:stage.current?.clientWidth??window.innerWidth,height:window.innerHeight,
      available:Math.max(0,window.innerHeight-(stage.current?.getBoundingClientRect().top??166)-12)});
    const change=()=>{setFullscreen(document.fullscreenElement===stage.current);resized()};
    window.addEventListener("resize",resized);document.addEventListener("fullscreenchange",change);
    resized();
    return()=>{window.removeEventListener("resize",resized);document.removeEventListener("fullscreenchange",change)};
  },[loading]);
  const fr=reports as FactsReport[];
  const disciplinas=useMemo(()=>resumoDisciplinas(fr),[fr]);
  const fases=useMemo(()=>resumoFases(fr),[fr]);
  const visibleCount=resumoPageSize(viewport.width,viewport.height,fr.some(r=>r.tipoLdp===2));
  const totalPages=Math.max(1,Math.ceil(disciplinas.length/visibleCount));
  useEffect(()=>setPage(p=>Math.min(p,totalPages-1)),[totalPages]);
  useEffect(()=>{
    if(!auto||totalPages<2)return;
    const timer=window.setInterval(()=>setPage(p=>(p+1)%totalPages),16000);
    return()=>window.clearInterval(timer);
  },[auto,totalPages]);
  const toggleFullscreen=useCallback(async()=>{
    try {if(document.fullscreenElement===stage.current)await document.exitFullscreen();
         else await stage.current?.requestFullscreen();}catch{ /* opção de navegador, não afeta dados */ }
  },[]);
  if(loading)return <ReportState title="Carregando Resumo TV…">Consultando dados gerenciais do projeto.</ReportState>;
  if(error)return <ReportState title={error} error><button onClick={retry} className="underline text-primary">Tentar novamente</button></ReportState>;
  const r=calcularResumo(fr),av=consolidarAvanco(fr),doc=documentalDaLdp(ldp.projetos);
  const count=(key:string)=>ldp.carregando?"…":ldp.erro?"—":String(doc.cards.find(c=>c.key===key)?.valor?.valor??(key==="emitidos"?r.documentos.emitidos:key==="cancelados"?r.documentos.cancelados:r.documentos.total-r.documentos.emitidos-r.documentos.cancelados));
  const total=ldp.carregando?"…":ldp.erro?"—":String(doc.total.valor??"—");
  const custoR=ratio(r.custoRealizado,r.custoPrevisto);
  const custoSaldo=typeof custoR==="number"?100-custoR:null;
  const docs:Item[]=[
    {title:"Total de documentos",value:total,color:"purple",field:"total"},
    {title:"Emitidos",value:count("emitidos"),color:"green",field:"emitidos"},
    {title:"Cancelados",value:count("cancelados"),color:"slate",field:"cancelados"},
    {title:"A emitir",value:count("aEmitir"),color:"amber",field:"aEmitir"},
    {title:"Atrasados",value:String(r.documentos.atrasados),color:"red",field:"atrasados"},
    {title:"No prazo",value:String(r.documentos.noPrazo),color:"blue",field:"noPrazo"}];
  const hours:Item[]=[
    {title:"Consumo HH",value:pct(r.consumoHh),percentage:r.consumoHh,color:"blue",field:"consumoHh",phaseFormat:pct},
    {title:"HH previsto",value:fmt(r.hhPrevisto)+" h",color:"blue",field:"hhPrevisto"},
    {title:"HH realizado",value:fmt(r.hhRealizado)+" h",color:"orange",field:"hhRealizado"},
    {title:"Saldo HH",value:fmt(r.saldoHh)+" h",color:r.saldoHh!=null&&r.saldoHh<0?"red":"green",field:"saldoHh"}];
  const costs:Item[]=[
    {title:"Consumo de custo",value:pct(custoR),percentage:custoR,color:"green",field:"consumoCusto",phaseFormat:pct},
    {title:"Custo previsto",value:r.custoPrevisto!=null&&r.custoPrevisto>0?"100%":"—",percentage:r.custoPrevisto!=null&&r.custoPrevisto>0?100:null,color:"slate",field:"custoPrevisto",phaseFormat:()=>"—"},
    {title:"Custo realizado",value:pct(custoR),percentage:custoR,color:"green",field:"custoRealizado",phaseFormat:()=>"—"},
    {title:"Saldo de custo",value:pct(custoSaldo),percentage:custoSaldo,color:(custoSaldo??0)<0?"red":"green"}];
  const advances:Item[]=[
    {title:"Avanço previsto",value:pct(av.previsto),percentage:av.previsto,color:"amber"},
    {title:"Avanço realizado",value:pct(av.fisico),percentage:av.fisico,color:"purple"}];
  const visible=disciplinas.slice(page*visibleCount,(page+1)*visibleCount);
  const title=selection.map(s=>s.name||s.id).join(" · ")||"Projeto não selecionado";
  return <div ref={stage} className="tv-stage" data-testid="resumo-tv-teste" style={{height:fullscreen?"100dvh":viewport.available,"--resumo-cols":viewport.width<1000?1:viewport.width<1600?2:3} as React.CSSProperties}>
    <header className="tv-header">
      <div className="tv-heading">
        <span>N&A CONNECT <i/> LABORATÓRIO DE VISUALIZAÇÃO</span>
        <h1>Resumo de Produção <em>TV</em></h1>
        <p title={title}>{title}</p>
      </div>
      <div className="tv-actions">
        <span>SEM VALORES MONETÁRIOS</span>
        <button onClick={toggleFullscreen} title="Exibir na TV em tela cheia">
          {fullscreen?<Minimize2 size={16}/>:<Maximize2 size={16}/>} {fullscreen?"Sair da TV":"Tela cheia"}
        </button>
      </div>
    </header>
    <section className="tv-documents" aria-label="Documentos">
      {docs.map((m,i)=><Tile key={i} item={m} fases={fases}/>)}
    </section>
    <section className="tv-group">
      <h2>Horas <small>CONTRATADO X EXECUTADO</small></h2>
      <div className="tv-cards four">{hours.map((m,i)=><Tile key={i} item={m} fases={fases}/>)}</div>
    </section>
    <section className="tv-group">
      <h2>Custo <small>EXCLUSIVAMENTE PORCENTAGENS</small></h2>
      <div className="tv-cards four">{costs.map((m,i)=><Tile key={i} item={m} fases={fases}/>)}</div>
    </section>
    <section className="tv-group">
      <h2>Avanço <small>PREVISTO X REALIZADO</small></h2>
      <div className="tv-cards two">{advances.map((m,i)=><Tile key={i} item={m}/>)}</div>
    </section>
    <section className="tv-charts" aria-label="HH e percentual de custos por disciplina">
      <div className="tv-chart-title">
        <h2>Disciplinas · indicadores por card</h2>
        <div className="tv-pagination">{totalPages>1&&<>
          <button aria-label="Página anterior de disciplinas" onClick={()=>setPage(p=>(p-1+totalPages)%totalPages)}><ArrowLeft size={16}/></button>
          <span>{page+1} / {totalPages}</span>
          <button aria-label="Página seguinte de disciplinas" onClick={()=>setPage(p=>(p+1)%totalPages)}><ArrowRight size={16}/></button>
          <button aria-label={auto?"Pausar rotação":"Ativar rotação"} onClick={()=>setAuto(p=>!p)}>{auto?<Pause size={16}/>:<Play size={16}/>}</button>
        </>}</div>
      </div>
      <div className="tv-discipline-cards">
        {visible.map(d=><ResumoDisciplineCard key={d.idExibicao} discipline={d} tv />)}
        {!visible.length&&<div className="tv-chart-empty">Nenhuma disciplina disponível.</div>}
      </div>
      <div className="tv-chart-footer">
        <span>HH por disciplina, documentos e consumo percentual de custo</span>
        <span>{disciplinas.length} disciplinas · Dados gerenciais oficiais · Exibição de custos apenas em %</span>
      </div>
    </section>
  </div>;
}
function Tile({item:m,fases=[]}:{item:Item;fases?:ResumoPhaseMetric[]}){
  return <div className={"tv-tile "+(m.color??"blue")} title={m.title+": "+m.value}>
    <small><i/>{m.title}</small><strong>{m.value}</strong>
    {m.field&&<ResumoPhaseStrip fases={fases} field={m.field} format={m.phaseFormat}/>}
    <div className="tv-tile-progress">
      {m.percentage!=null&&Number.isFinite(m.percentage)&&<span style={{width:Math.max(0,Math.min(100,m.percentage))+"%"}}/>}
    </div>
  </div>;
}
