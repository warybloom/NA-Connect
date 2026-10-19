/* Quatro telas do PDF "Painel Gestão À Vista" (Jamile): estrutura visual fiel, dados oficiais N&A.
   Disciplina = DOC3 → sigla → Norma de Coordenação do projeto → nome da Norma (resolvido no backend). */
import {useEffect,useMemo,useState,type ReactNode} from "react";
import {Link} from "react-router-dom";
import {CalendarDays,ChevronLeft,ChevronRight,TriangleAlert} from "lucide-react";
import type {PmoSelecionado} from "@/components/pmo/pmoMultiCore";
import {formatCurrency,formatHH,usePmoReports} from "@/components/pmo/pmoData";
import {getJson} from "@/lib/api";
import {enrichDocuments,type FactsReport} from "@/components/pmo/pmoFacts";
import {ReportState} from "@/components/reports/ReportComponents";
import {encontrarFolha,rotaProducao} from "../productionRoutes";
import {ItemMenuTv,useNavegacaoTv} from "@/features/tv/tvNavegacao";
import {useLdpPorProjeto} from "./useLdpPorProjeto";
import {
 FASES,addDias,agrupar,classificarDocumento,classificarProgramacao,criarCatalogo,diasDaSemana,entregasDaSemana,faseContagem,
 faseHhPrevisto,faseTotalDocumentos,fasePrazos,hhLinha,performancePorDisciplina,recursosDistintos,rotulo,semanaDoCorte,
 deduplicarProgramacao,diaDaSemanaCurto,filtrarDia,atividadesPorDia,semanasVizinhasComProgramacao,
 semanasComEntregas,SEM_DISCIPLINA,type CatalogoDisciplinas,type DisciplinasNorma,type PhaseValues,type ProgramacaoLinha,
 type ProgramacaoResposta
} from "./gestaoVistaJamileLogic";
import "./gestaoVistaJamile.css";
import { NA_LOGO } from "@/features/theme/brandAssets";
import ProjetoFaseAgrupamento,{SeletorVisaoMultiprojeto,type VisaoMultiprojeto} from "./projetoFase/ProjetoFaseAgrupamento";
import {agruparDocumentacao,agruparEntregas,agruparIndicadores,agruparProgramacao} from "./projetoFase/projetoFaseLogic";
import {useRegrasSiglasAtivas} from "@/features/disciplinas/useRegrasSiglasAtivas";
import {criarNormalizador} from "@/features/disciplinas/normalizadorSiglas";
import {GerenciaEngenhariaItem3} from "./item3/GerenciaEngenhariaItem3";
import {criarIndiceTitulosDocumentos,obterTituloDocumentoOficial} from "./item3/documentTitleIndex";

export type JamileScreen="visao-geral"|"programacao"|"documentacao"|"indicadores";
const SCREENS:{slug:JamileScreen;number:string;title:string}[]=[
 {slug:"visao-geral",number:"01",title:"VISÃO GERAL"},
 {slug:"programacao",number:"02",title:"PROGRAMAÇÃO"},
 {slug:"documentacao",number:"03",title:"DOCUMENTAÇÃO"},
 {slug:"indicadores",number:"04",title:"INDICADORES"}];
const SUBTITLE:Record<JamileScreen,string>={"visao-geral":"PAINEL À VISTA",programacao:"PROGRAMAÇÃO SEMANAL",documentacao:"FLUXO DE DOCUMENTOS",indicadores:"INDICADOR DE PERFORMANCE"};
const TODAY=()=>new Date().toLocaleDateString("en-CA",{timeZone:"America/Bahia"});
const ACC_ID=/^b\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const GAUGES_POR_PAGINA=10;

const fmt=(n:number|null|undefined)=>typeof n==="number"?formatHH(n):"—";
const fmt2=(n:number|null|undefined)=>typeof n==="number"?new Intl.NumberFormat("pt-BR",{minimumFractionDigits:2,maximumFractionDigits:2}).format(n):"—";
const percent=(n:number|null|undefined)=>typeof n==="number"?new Intl.NumberFormat("pt-BR",{minimumFractionDigits:1,maximumFractionDigits:1}).format(n)+"%":"—";
const percent2=(n:number|null|undefined)=>typeof n==="number"?new Intl.NumberFormat("pt-BR",{minimumFractionDigits:2,maximumFractionDigits:2}).format(n)+"%":"—";
/** Igual ao cartão do Power BI: "13,85 Mil". */
const mil=(n:number|null|undefined)=>typeof n!=="number"?"—":Math.abs(n)>=1000?new Intl.NumberFormat("pt-BR",{minimumFractionDigits:2,maximumFractionDigits:2}).format(n/1000)+" Mil":fmt2(n);
const br=(v:string)=>v.split("-").reverse().join("/");
const brCurta=(v:string)=>{const [y,m,d]=v.split("-");return `${d}/${m}/${y.slice(2)}`;};
const DIA=["DOM","SEG","TER","QUA","QUI","SEX","SÁB"];

/* ---------------------------------------------------------------- componentes visuais */
function Tile({title,value,gates,className=""}:{title:string;value:string;gates?:PhaseValues;className?:string}){
 return <div className={`gv-tile ${className}`}><span className="gv-tile-title">{title}</span><strong className="gv-tile-value">{value}</strong>
  {gates&&<div className="gv-gates">{FASES.map(g=><div key={g}><small>{g}%</small><b>{fmt(gates[g])}</b></div>)}</div>}
 </div>;
}
function Section({title,children,cls="",tone="steel"}:{title:string;children:ReactNode;cls?:string;tone?:"steel"|"navy"}){
 return <section className={`gv-section gv-section-${tone} ${cls}`}><header>{title}</header><div className="gv-section-body">{children}</div></section>;
}
function Legend({items}:{items:[string,string][]}){
 return <div className="gv-legend">{items.map(([cls,label])=><span key={label}><i className={cls}/>{label}</span>)}</div>;
}
function Empty({children}:{children:ReactNode}){return <div className="gv-empty">{children}</div>;}

/** Barras horizontais (previsto + realizado sobreposto), como os visuais de barra do PDF. */
function HBars({rows,max,realizado=true}:{rows:{key:string;name:string;planned:number;actual:number;title?:string}[];max:number;realizado?:boolean}){
 return <div className="gv-hbars">{rows.map(r=>
  <div className="gv-hbar" key={r.key} title={r.title??`${r.name} · Previstos ${r.planned}${realizado?` · Emitidos ${r.actual}`:""}`}>
   <label>{r.name}</label>
   <div className="gv-htrack">
    <i style={{width:`${max>0?Math.min(100,r.planned/max*100):0}%`}}/>
    {realizado&&<em style={{width:`${max>0?Math.min(100,r.actual/max*100):0}%`}}/>}
   </div>
   <strong>{r.planned}</strong>
  </div>)}</div>;
}

/** Velocímetro semicircular: arco de 180°, 0–100%, fundo cinza, progresso azul, valor central. >100% é sinalizado sem distorcer o arco. */
export function Gauge({title,value,detail,fracao}:{title:string;value:number|null;detail?:string;fracao?:string}){
 const v=typeof value==="number"&&Number.isFinite(value)?value:null;
 const fill=v===null?0:Math.max(0,Math.min(100,v));
 const excedido=v!==null&&v>100;
 const arc="M 20 100 A 80 80 0 0 1 180 100";
 return <figure className={"gv-gauge"+(excedido?" gv-gauge-over":"")} title={detail}>
  <figcaption>{title}</figcaption>
  <svg viewBox="0 0 200 122" role="img" aria-label={`${title}: ${percent(v)}`} preserveAspectRatio="xMidYMid meet">
   <path d={arc} className="gv-gauge-track" pathLength={100}/>
   {fill>0&&<path d={arc} className="gv-gauge-fill" pathLength={100} strokeDasharray={`${fill} 100`}/>}
   <text x="100" y="92" textAnchor="middle" className="gv-gauge-value">{percent(v)}</text>
   {fracao&&<text x="100" y="116" textAnchor="middle" className="gv-gauge-fracao">{fracao}</text>}
   {excedido&&<text x="100" y="62" textAnchor="middle" className="gv-gauge-over-label">ACIMA DE 100%</text>}
   <text x="20" y="118" textAnchor="middle" className="gv-gauge-end">0,0%</text>
   <text x="180" y="118" textAnchor="middle" className="gv-gauge-end">100,0%</text>
  </svg>
 </figure>;
}
function PhaseSupplement({title,values}:{title:string;values:PhaseValues}){
 return <div className="gv-fase-box"><strong>{title}</strong><div className="gv-gates">{FASES.map(g=><div key={g}><small>{g}%</small><b>{fmt(values[g])}</b></div>)}</div></div>;
}
function DiagnosticoNorma({normas,erro}:{normas:DisciplinasNorma[];erro:string|null}){
 const itens=normas.flatMap(n=>n.diagnosticos.map(d=>({...d,norma:n.normaNome??"—"})));
 const semNorma=normas.filter(n=>n.normaId===null).length;
 if(!erro&&!itens.length&&!semNorma)return null;
 return <details className="gv-diagnostico">
  <summary><TriangleAlert size={14}/>{erro?"Catálogo da Norma indisponível":`${itens.length+semNorma} diagnóstico(s) do cadastro da Norma de Coordenação`}</summary>
  {erro&&<p>{erro} — os nomes exibidos usam somente o nome da Norma resolvido por documento; o realizado pode aparecer pelo nome padrão até o backend ser reiniciado.</p>}
  {semNorma>0&&<p>{semNorma} projeto(s) selecionado(s) sem Norma de Coordenação vinculada.</p>}
  <ul>{itens.map((d,i)=><li key={i}><b>{d.norma}</b>: {d.mensagem} <small>Siglas: {d.siglas.join(", ")}</small></li>)}</ul>
  <p className="gv-diagnostico-nota">Nenhum cadastro foi alterado. Corrigir a Norma requer aprovação.</p>
 </details>;
}

/* ---------------------------------------------------------------- 01. VISÃO GERAL */
function Overview({reports,cutoff,cat,onCutoff}:{reports:FactsReport[];cutoff:string;cat:CatalogoDisciplinas;onCutoff:(d:string)=>void}){
 const prog=entregasDaSemana(reports,cat,cutoff);
 const dias=diasDaSemana(prog.semana.start).map(d=>({d,p:prog.entregas.filter(x=>x.data===d).length,e:prog.entregas.filter(x=>x.data===d&&x.emitido).length}));
 const maxDia=Math.max(1,...dias.map(x=>x.p));
 const porProjeto=[...agrupar(prog.entregas,x=>x.projectId)].map(([k,xs])=>({key:k,name:xs[0].projeto,planned:xs.length,actual:xs.filter(x=>x.emitido).length}));
 const porDisc=[...agrupar(prog.entregas,x=>x.chave)].map(([k,xs])=>({key:k,name:rotulo(cat,k),planned:xs.length,actual:xs.filter(x=>x.emitido).length}))
  .sort((a,b)=>b.planned-a.planned||a.name.localeCompare(b.name,"pt-BR"));
 const [pagina,setPagina]=useState(0);
 const paginas=Math.max(1,Math.ceil(porDisc.length/GAUGES_POR_PAGINA));
 const visiveis=porDisc.slice(Math.min(pagina,paginas-1)*GAUGES_POR_PAGINA,(Math.min(pagina,paginas-1)+1)*GAUGES_POR_PAGINA);
 const semanas=prog.previstos?[]:semanasComEntregas(reports);
 const proxima=semanas.find(s=>s>prog.semana.start),anterior=[...semanas].reverse().find(s=>s<prog.semana.start);
 const vazio=!prog.previstos&&<Empty>Nenhuma entrega prevista entre {br(prog.semana.start)} e {br(prog.semana.end)}.
  {anterior&&<button onClick={()=>onCutoff(anterior)}>Semana anterior com programação: {br(anterior)}</button>}
  {proxima&&<button onClick={()=>onCutoff(proxima)}>Próxima semana com programação: {br(proxima)}</button>}</Empty>;
 return <>
  <div className="gv-kpis gv-five">
   <Tile title="ENTREGÁVEIS PREVISTOS" value={String(prog.previstos)}/>
   <Tile title="REALIZADOS" value={prog.realizados?String(prog.realizados):"--"}/>
   <Tile title="EM ATRASO" value={String(prog.atrasados)}/>
   <Tile title="%ADERÊNCIA À PROGRAMAÇÃO" value={percent(prog.aderencia)}/>
   <Tile title="%DESVIO DA PROGRAMAÇÃO" value={percent(prog.desvio)}/>
  </div>
  <div className="gv-overview">
   <Section title="EVOLUÇÃO DIÁRIA DA PROGRAMAÇÃO" cls="gv-ov-evolution">
    <Legend items={[["gv-dot-sky","Previstos"],["gv-dot-navy","Emitidos"]]}/>
    {prog.previstos?<div className="gv-columns">{dias.map(x=><div key={x.d} className="gv-col" title={`${br(x.d)} · Previstos ${x.p} · Emitidos ${x.e}`}>
      <div className="gv-col-bars">
       <span className="gv-bar-sky" style={{height:`${x.p/maxDia*100}%`}}>{x.p>0&&<b>{x.p}</b>}</span>
       <span className="gv-bar-navy" style={{height:`${x.e/maxDia*100}%`}}>{x.e>0&&<b>{x.e}</b>}</span>
      </div>
      <small>{DIA[new Date(x.d+"T12:00:00Z").getUTCDay()]} {x.d.slice(8)}/{x.d.slice(5,7)}</small>
     </div>)}</div>:vazio}
   </Section>
   <Section title="ATIVIDADES/DOCUMENTOS POR PROJETO" cls="gv-ov-project">
    <Legend items={[["gv-dot-navy","Previstos"],["gv-dot-teal","Emitidos"]]}/>
    {porProjeto.length?<HBars rows={porProjeto} max={Math.max(1,...porProjeto.map(x=>x.planned))}/>:<Empty>Sem entregas na semana.</Empty>}
   </Section>
   <Section title="ATIVIDADES/DOCUMENTOS POR DISCIPLINA" cls="gv-ov-discipline">
    <Legend items={[["gv-dot-navy","Previstos"],["gv-dot-teal","Emitidos"]]}/>
    {porDisc.length?<HBars rows={porDisc} max={Math.max(1,...porDisc.map(x=>x.planned))}/>:<Empty>Sem entregas na semana.</Empty>}
   </Section>
   <Section title="% EMISSÃO POR DISCIPLINA" cls="gv-ov-gauges">
    {porDisc.length?<div className="gv-gauge-grid">{visiveis.map(x=><Gauge key={x.key} title={x.name} value={x.planned?x.actual/x.planned*100:null}
      fracao={`${x.actual} de ${x.planned}`}
      detail={`${x.name}: ${x.actual} emitido(s) de ${x.planned} previsto(s) na semana (gate/documento com status APPROVED/APROVADO)`}/>)}</div>:<Empty>Sem entregas previstas na semana.</Empty>}
    {paginas>1&&<div className="gv-pager"><button disabled={pagina<=0} onClick={()=>setPagina(p=>p-1)} aria-label="Página anterior"><ChevronLeft size={14}/></button>
     <span>{Math.min(pagina,paginas-1)+1}/{paginas}</span>
     <button disabled={pagina>=paginas-1} onClick={()=>setPagina(p=>p+1)} aria-label="Próxima página"><ChevronRight size={14}/></button></div>}
   </Section>
  </div>
  <p className="gv-rule">Semana oficial domingo–sábado. Tipo 1: data-alvo do documento (Reprogramada LDP › término do cronograma › Baseline). Tipo 2: cada gate 30/60/90 ativo com Reprogramada › Baseline do gate. Em atraso = não emitido com data anterior à data de corte. % Emissão por disciplina = emitidos ÷ previstos da semana (mesma regra do PDF e do Resumo 30/60/90: emitido = status APPROVED/APROVADO; CLIENT REVIEW ainda não é emissão).</p>
 </>;
}

/* ---------------------------------------------------------------- 02. PROGRAMAÇÃO */
type ProgLinha=ProgramacaoLinha&{projectId:string};
const cobertura=(xs:readonly ProgLinha[],f:(l:ProgLinha)=>string|null)=>`${xs.filter(l=>(f(l)??"").trim()!=="").length}/${xs.length}`;
function Programming({reports,semana,dia,cat,dados,erro,carregando,onSemana}:{reports:FactsReport[];semana:{start:string;end:string};dia:string|null;
 cat:CatalogoDisciplinas;dados:ProgramacaoResposta[];erro:string|null;carregando:boolean;onSemana:(data:string)=>void}){
 const [agruparDia,setAgruparDia]=useState(true);
 const noPeriodo=(d:string|null|undefined)=>!!d&&d>=semana.start&&d<=semana.end&&(!dia||d===dia);
 // Semana inteira (domingo–sábado) sem repetições; o filtro de dia é opcional e só recorta a semana já carregada.
 const semanaDedup=deduplicarProgramacao(dados.flatMap(d=>d.linhas.map(l=>({...l,projectId:d.projectId}))));
 const linhas:ProgLinha[]=filtrarDia(semanaDedup.linhas,dia);
 // Fallback (fonte detalhada indisponível): datas da LDP, sem responsáveis — nunca inventados.
 const ldp=erro?enrichDocuments(reports).filter(d=>!d.isActivity&&!d.cancelado&&noPeriodo(d.dataAlvo?.slice(0,10))):[];
 // Item 3 (Codex): título OFICIAL do documento (LdpAtual.Document_Title) por projeto + código; a tarefa nunca é usada como título.
 const indiceTitulos=criarIndiceTitulosDocumentos(reports);
 const itens=(linhas.length?linhas.map(l=>({key:`${l.projectId}:${l.idRegistro}`,chave:classificarProgramacao(cat,l.projectId,l).chave,codigo:l.documento,
   elaborador:l.elaborador,verificador:l.verificador,aprovador:l.aprovador,hh:hhLinha(l),status:l.status,data:l.dataAtividade.slice(0,10),
   titulo:`${l.tarefa??""}${l.siglaDoc3?` · DOC3 ${l.siglaDoc3}`:""} · HH elab. ${fmt2(l.hhElaboracao)} + verif. ${fmt2(l.hhVerificacao)} + aprov. ${fmt2(l.hhAprovacao)}`,
   tituloDocumento:obterTituloDocumentoOficial(indiceTitulos,l.projectId,l.documento)}))
  :ldp.map(d=>({key:d.rowId,chave:classificarDocumento(cat,d).chave,codigo:d.codigo,elaborador:null,verificador:null,aprovador:null,hh:d.hhPrevisto,
   status:d.statusInterno,data:d.dataAlvo!.slice(0,10),titulo:d.titulo??"",tituloDocumento:obterTituloDocumentoOficial(indiceTitulos,d.projectId,d.codigo)})))
  // Ordem: dia → disciplina → documento (agrupamento por dia usa a mesma ordem).
  .sort((a,b)=>a.data.localeCompare(b.data)||rotulo(cat,a.chave).localeCompare(rotulo(cat,b.chave),"pt-BR")||(a.codigo??"").localeCompare(b.codigo??"","pt-BR"));
 const grupos=[...agrupar(itens,x=>x.chave)].map(([k,xs])=>({key:k,name:rotulo(cat,k),planned:xs.length,actual:0,
  hh:xs.reduce((s,x)=>s+(x.hh??0),0)})).sort((a,b)=>b.planned-a.planned||a.name.localeCompare(b.name,"pt-BR"));
 const totalHh=itens.some(x=>x.hh!==null)?itens.reduce((s,x)=>s+(x.hh??0),0):null;
 const vizinhas=semanasVizinhasComProgramacao(dados.flatMap(d=>d.datasComProgramacao.map(x=>x.data)),semana);
 const registros=dados.reduce((s,d)=>s+d.registrosProjeto,0);
 const periodo=dia?`${diaDaSemanaCurto(dia)} ${br(dia)}`:`a semana de ${br(semana.start)} a ${br(semana.end)}`;
 // Item 3 (Codex): Semana Completa no topo (largura total, com título do documento) e, abaixo, Entregáveis × HH por disciplina.
 return <div className="gv-program-item3" style={{display:"flex",flexDirection:"column",flex:1,minHeight:0,gap:"1em"}}>
  <div className="gv-kpis gv-three">
   <Tile title="ENTREGÁVEIS" value={String(itens.length)}/>
   <Tile title="HH PLANEJADO" value={fmt2(totalHh)}/>
   <Tile title="RECURSOS" value={linhas.length?String(recursosDistintos(linhas)):"—"}/>
  </div>
  <GerenciaEngenhariaItem3
   itens={itens.map(x=>({key:x.key,data:x.data,dia:`${diaDaSemanaCurto(x.data)} ${brCurta(x.data).slice(0,5)}`,disciplina:rotulo(cat,x.chave),
    codigoDocumento:x.codigo,tituloDocumento:x.tituloDocumento,elaborador:x.elaborador,verificador:x.verificador,aprovador:x.aprovador,situacao:x.status,hhPrevisto:x.hh}))}
   grupos={grupos.map(x=>({key:x.key,nome:x.name,entregaveis:x.planned,hh:x.hh}))}
   agruparPorDia={agruparDia}
   onAgruparPorDia={setAgruparDia}
   descricaoPeriodo={dia?`Filtro: ${diaDaSemanaCurto(dia)} ${br(dia)}`:`${br(semana.start)} a ${br(semana.end)}`}
   carregando={carregando}
   mensagemVazia={<Empty>
    {registros?`Nenhuma atividade programada para ${periodo} (${registros} registro(s) na programação do projeto).`:"O projeto não possui programação semanal importada."}
    {vizinhas.anterior&&<button onClick={()=>onSemana(vizinhas.anterior!.start)}>Semana anterior com programação: {br(vizinhas.anterior.start)} a {br(vizinhas.anterior.end)}</button>}
    {vizinhas.proxima&&<button onClick={()=>onSemana(vizinhas.proxima!.start)}>Próxima semana com programação: {br(vizinhas.proxima.start)} a {br(vizinhas.proxima.end)}</button>}
   </Empty>}
   aviso={erro?<p className="gv-warning">Programação detalhada indisponível ({erro}); exibindo datas-alvo da LDP, sem responsáveis.</p>:undefined}
   rodape={!erro&&dados.length>0?<p className="gv-rule">Fonte: programação semanal (data da atividade = Reprogramada › Baseline), semana domingo a sábado. HH previsto = elaboração + verificação + aprovação. TBD não conta como recurso.
    {semanaDedup.repetidos>0&&<> {semanaDedup.repetidos} registro(s) repetido(s) (mesmo documento, tarefa, fase e dia) contado(s) uma vez.</>}
    {linhas.length>0&&<> Preenchimento na planilha semanal: elaborador {cobertura(linhas,l=>l.elaborador)}, verificador {cobertura(linhas,l=>l.verificador)}, aprovador {cobertura(linhas,l=>l.aprovador)}, situação {cobertura(linhas,l=>l.status)}. "—" = campo vazio na fonte (Recurso / Weekly_Responsible_Verification / Weekly_Responsible_Approval / Weekly_Progress_Status).</>}
    {" "}Título do documento = LDP (Document_Title) do mesmo projeto e código; "—" = sem título na LDP.</p>:undefined}
  />
 </div>;
}

/* ---------------------------------------------------------------- 03. DOCUMENTAÇÃO */
function Documentation({reports,ldp,cat}:{reports:FactsReport[];ldp:ReturnType<typeof useLdpPorProjeto>;cat:CatalogoDisciplinas}){
 const docs=enrichDocuments(reports).filter(x=>!x.isActivity);
 const emitted=docs.filter(x=>x.emitido).length,cancelled=docs.filter(x=>x.cancelado).length;
 const inFlow=docs.filter(x=>!x.emitido&&!x.cancelado&&x.statusInterno).length;
 const noStart=docs.filter(x=>!x.statusInterno&&!x.emitido&&!x.cancelado).length;
 const stage=(needle:string)=>docs.filter(d=>!d.emitido&&!d.cancelado&&(d.statusInterno??"").normalize("NFD").replace(/[̀-ͯ]/g,"").toUpperCase().includes(needle)).length;
 const type2=reports.some(r=>r.tipoLdp===2);
 const nTipo2=reports.filter(r=>r.tipoLdp===2).length;
 const grupos=[...agrupar(docs,d=>classificarDocumento(cat,d).chave)].map(([k,d])=>({k,nome:rotulo(cat,k),d}))
  .sort((a,b)=>(a.k===SEM_DISCIPLINA?1:0)-(b.k===SEM_DISCIPLINA?1:0)||a.nome.localeCompare(b.nome,"pt-BR"));
 const flow:[string,number][]=[["ELABORAÇÃO",stage("ELABOR")],["VERIFICAÇÃO",stage("VERIF")],["QUALIDADE",stage("QUALID")],["APROVADO",emitted]];
 return <>
  <div className="gv-kpis gv-six">
   <Tile title="TOTAL DE DOCUMENTOS" value={String(docs.length)} gates={type2?faseTotalDocumentos(reports):undefined}/>
   <Tile title="NÃO INICIADOS" value={String(noStart)}/>
   <Tile title="EM FLUXO" value={String(inFlow)}/>
   <Tile title="EMITIDOS" value={String(emitted)} gates={type2?faseContagem(ldp.projetos,"emitidos"):undefined}/>
   <Tile title="ATEND. COMENTÁRIOS" value="--"/>
   <Tile title="CANCELADOS" value={String(cancelled)} gates={type2?faseContagem(ldp.projetos,"cancelados"):undefined}/>
  </div>
  <div className="gv-flow" role="list">
   {flow.map(([key,val],i)=><div key={key} role="listitem" className={`gv-chevron gv-chevron-${i}`}><b>{key}</b><strong>{val}</strong></div>)}
  </div>
  <Section title="STATUS DOS DOCUMENTOS POR DISCIPLINA" cls="gv-document-table" tone="navy">
   <div className="gv-table-scroll">
    <table><thead><tr>{["DISCIPLINA","TOTAL DE DOCUMENTOS","EMITIDO","NÃO INICIADO","EM FLUXO","CANCELADO"].map(x=><th key={x}>{x}</th>)}</tr></thead>
    <tbody>{grupos.map(g=><tr key={g.k}><td>{g.nome}</td><td>{g.d.length}</td><td>{g.d.filter(x=>x.emitido).length}</td>
     <td>{g.d.filter(x=>!x.statusInterno&&!x.emitido&&!x.cancelado).length}</td>
     <td>{g.d.filter(x=>!x.emitido&&!x.cancelado&&x.statusInterno).length}</td><td>{g.d.filter(x=>x.cancelado).length}</td></tr>)}</tbody>
    <tfoot><tr><td>TOTAL</td><td>{docs.length}</td><td>{emitted}</td><td>{noStart}</td><td>{inFlow}</td><td>{cancelled}</td></tr></tfoot>
    </table>
   </div>
  </Section>
  {type2&&nTipo2<reports.length&&<p className="gv-rule">Totais = todos os {reports.length} projetos. Fases 30/60/90 = somente os {nTipo2} projeto(s) LDP Tipo 2 da seleção (LDP Tipo 1 não tem gates).</p>}
 </>;
}

/* ---------------------------------------------------------------- 04. INDICADORES */
function Indicators({reports,ldp,cutoff,cat}:{reports:FactsReport[];ldp:ReturnType<typeof useLdpPorProjeto>;cutoff:string;cat:CatalogoDisciplinas}){
 const {rows,cards}=performancePorDisciplina(reports,cat),type2=reports.some(r=>r.tipoLdp===2);
 const prazo=fasePrazos(reports,cutoff);
 return <>
  <div className="gv-kpis gv-four gv-indicator-kpis">
   <Tile title="HH PLANEJADO" className="gv-planned-hh" value={mil(cards.HH_Previsto)} gates={type2?faseHhPrevisto(reports):undefined}/>
   <Tile title="HH REALIZADO" value={mil(cards.HH_Realizado)}/>
   <Tile title="%HH CONSUMIDO" value={percent2(cards.ConsumoHH)}/>
   <Tile title="%CUSTO CONSUMIDO" value={percent2(cards.ConsumoCusto)}/>
  </div>
  <Section title="PERFORMANCE POR DISCIPLINA" cls="gv-performance-table" tone="navy">
   <div className="gv-table-scroll">
    <table><thead><tr>{["DISCIPLINA","HH PLANEJADO","HH REALIZADO","%HH","CUSTO PLANEJADO","CUSTO REALIZADO","%CUSTO"].map(x=><th key={x}>{x}</th>)}</tr></thead>
    <tbody>{rows.map(x=><tr key={x.chave} className={x.reconciliacao?"gv-row-reconciliacao":x.chave===SEM_DISCIPLINA?"gv-row-sem":""}
     title={x.diagnosticos.join(" · ")||undefined}>
     <td>{x.disciplina}{x.diagnosticos.length>0&&<TriangleAlert size={12} className="gv-inline-alert" aria-label={x.diagnosticos.join(" · ")}/>}</td>
     <td>{fmt2(x.hhPrevisto)}</td><td>{fmt2(x.hhRealizado)}</td><td>{percent2(x.consumoHh)}</td>
     <td>{formatCurrency(x.custoPrevisto)}</td><td>{formatCurrency(x.custoRealizado)}</td><td>{percent2(x.consumoCusto)}</td></tr>)}</tbody>
    <tfoot><tr><td>TOTAL</td><td>{fmt2(cards.HH_Previsto)}</td><td>{fmt2(cards.HH_Realizado)}</td><td>{percent2(cards.ConsumoHH)}</td>
     <td>{formatCurrency(cards.Custo_Previsto)}</td><td>{formatCurrency(cards.Custo_Realizado)}</td><td>{percent2(cards.ConsumoCusto)}</td></tr></tfoot>
    </table>
   </div>
  </Section>
  {type2&&<div className="gv-extra-gates">
   <PhaseSupplement title="HH PREVISTO POR FASE" values={faseHhPrevisto(reports)}/>
   <PhaseSupplement title="DOCUMENTOS POR FASE" values={faseTotalDocumentos(reports)}/>
   <PhaseSupplement title="EMITIDOS POR FASE" values={faseContagem(ldp.projetos,"emitidos")}/>
   <PhaseSupplement title="CANCELADOS POR FASE" values={faseContagem(ldp.projetos,"cancelados")}/>
   <PhaseSupplement title="A EMITIR POR FASE" values={faseContagem(ldp.projetos,"aEmitir")}/>
   <PhaseSupplement title="ATRASADOS POR FASE" values={prazo.atraso}/>
   <PhaseSupplement title="NO PRAZO POR FASE" values={prazo.noPrazo}/>
  </div>}
  {type2&&<p className="gv-rule">HH realizado não é exibido por fase: o Timesheet não identifica gate e nada é rateado artificialmente.</p>}
 </>;
}

/* ---------------------------------------------------------------- dados auxiliares */
function useNormas(ids:string[]){
 const chave=ids.join("|");
 const [estado,setEstado]=useState<{normas:DisciplinasNorma[];erro:string|null;carregando:boolean}>({normas:[],erro:null,carregando:true});
 useEffect(()=>{
  const lista=chave?chave.split("|").filter(id=>ACC_ID.test(id)):[];
  const ctrl=new AbortController();
  setEstado(s=>({...s,carregando:true}));
  Promise.allSettled(lista.map(id=>getJson<DisciplinasNorma>("/api/reports/gestao-vista-jamile/disciplinas?"+new URLSearchParams({projectId:id}),ctrl.signal)))
   .then(rs=>{if(ctrl.signal.aborted)return;
    const ok=rs.flatMap(r=>r.status==="fulfilled"?[r.value]:[]);
    const falha=rs.find((r):r is PromiseRejectedResult=>r.status==="rejected");
    setEstado({normas:ok,erro:falha?(falha.reason instanceof Error?falha.reason.message:"Indisponível"):null,carregando:false});});
  return()=>ctrl.abort();
 },[chave]);
 return estado;
}
interface FalhaProjeto{projectId:string;projectName:string;message:string}
function useProgramacao(selection:PmoSelecionado[],inicio:string,fim:string,ativo:boolean){
 const chave=JSON.stringify(selection.map(p=>[p.id,p.name]));
 const [estado,setEstado]=useState<{dados:ProgramacaoResposta[];erro:string|null;falhas:FalhaProjeto[];carregando:boolean}>({dados:[],erro:null,falhas:[],carregando:false});
 const [tentativa,setTentativa]=useState(0);
 useEffect(()=>{
  if(!ativo)return;
  const lista=(JSON.parse(chave) as [string,string][]).filter(([id])=>ACC_ID.test(id));
  const ctrl=new AbortController();
  setEstado(s=>({...s,carregando:true}));
  Promise.allSettled(lista.map(([id])=>getJson<ProgramacaoResposta>("/api/reports/gestao-vista-jamile/programacao?"+new URLSearchParams({projectId:id,inicio,fim}),ctrl.signal)))
   .then(rs=>{if(ctrl.signal.aborted)return;
    const ok=rs.flatMap(r=>r.status==="fulfilled"?[r.value]:[]);
    // Cada projeto que falhou fica identificado: no consolidado, total parcial nunca é exibido como total.
    const falhas=rs.flatMap((r,i):FalhaProjeto[]=>r.status==="rejected"?[{projectId:lista[i][0],projectName:lista[i][1]||lista[i][0],
     message:r.reason instanceof Error?r.reason.message:"Indisponível"}]:[]);
    setEstado({dados:ok,erro:falhas.length&&!ok.length?falhas[0].message:null,falhas,carregando:false});});
  return()=>ctrl.abort();
 },[chave,inicio,fim,ativo,tentativa]);
 return {...estado,retry:()=>setTentativa(t=>t+1)};
}

/** Consolidado incompleto: nenhum número é exibido (seria um total parcial apresentado como total). */
function ConsolidacaoIncompleta({falhas,total,onRetry}:{falhas:FalhaProjeto[];total:number;onRetry:()=>void}){
 return <div className="gv-incompleto" role="alert">
  <TriangleAlert size={20}/>
  <b>Consolidação incompleta: {falhas.length} de {total} projeto(s) sem dados</b>
  <p>Os números não são exibidos para não apresentar um total parcial como se fosse o consolidado dos {total} projetos.</p>
  <ul>{falhas.map(f=><li key={f.projectId}><b>{f.projectName}</b> — {f.message}</li>)}</ul>
  <button type="button" className="gv-link" onClick={onRetry}>Tentar novamente</button>
 </div>;
}

export default function GestaoVistaJamilePage({selection,screen}:{selection:PmoSelecionado[];screen:JamileScreen}){
 const {reports,falhas,loading,error,retry}=usePmoReports("gestao-vista",{hub:"",project:"",selection});
 const ldp=useLdpPorProjeto(selection);
 const ids=useMemo(()=>selection.map(x=>x.id),[selection]);
 const normas=useNormas(ids);
 const [cutoff,setCutoff]=useState(TODAY);
 const valid=/^\d{4}-\d{2}-\d{2}$/.test(cutoff)?cutoff:TODAY(),week=semanaDoCorte(valid);
 // 02: semana oficial (domingo–sábado) da data escolhida; filtro opcional de um dia dentro dela.
 const prog=useProgramacao(selection,week.start,week.end,screen==="programacao");
 const [diaFiltro,setDiaFiltro]=useState<string|null>(null);
 const dia=diaFiltro&&diaFiltro>=week.start&&diaFiltro<=week.end?diaFiltro:null;
 const irParaSemana=(data:string)=>{setCutoff(data);setDiaFiltro(null);};
 // Consolidado: TODOS os projetos do slide participam; qualquer fonte que falhe em um deles bloqueia os números da tela.
 const consolidado=selection.length>1;
 // Multiprojeto: padrão = PROJETO → FASE → disciplina (nada misturado); "consolidado" = telas originais somadas, com o
 // Normalizador de Siglas unindo siglas diferentes de mesmo nome canônico PT-BR (só aqui; 1 projeto nunca normaliza).
 const [visaoMulti,setVisaoMulti]=useState<VisaoMultiprojeto>("projeto-fase");
 const porProjetoFase=consolidado&&visaoMulti==="projeto-fase";
 const siglas=useRegrasSiglasAtivas(consolidado);
 const normalizador=useMemo(()=>consolidado?criarNormalizador(siglas.regras):null,[consolidado,siglas.regras]);
 const cat=useMemo(()=>criarCatalogo(normas.normas,selection,normalizador),[normas.normas,selection,normalizador]);
 const name=selection.map(x=>x.name).join(" · ");
 const falhasTela:FalhaProjeto[]=[
  ...falhas,
  ...(screen==="programacao"&&prog.falhas.length<selection.length?prog.falhas:[]),
  ...((screen==="documentacao"||screen==="indicadores")&&ldp.erro&&!ldp.carregando
   ?[{projectId:"__ldp__",projectName:"Aba LDP (fases 30/60/90)",message:ldp.erro}]:[]),
 ];
 // No player de TV o menu só salta entre slides DA apresentação (nunca sai para /controle-producao); fora dele, rotas normais.
 const navTv=useNavegacaoTv();
 let content:ReactNode;
 if(loading||normas.carregando||siglas.carregando)content=<ReportState title="Carregando Gestão à Vista…">Consultando os dados do projeto.</ReportState>;
 else if(error)content=<ReportState title={error} error><button className="gv-link" onClick={retry}>Tentar novamente</button></ReportState>;
 else if(falhasTela.length>0)content=<ConsolidacaoIncompleta falhas={falhasTela} total={selection.length} onRetry={()=>{retry();prog.retry();}}/>;
 else if(porProjetoFase){
  const fr=reports as FactsReport[];
  const titulo=`${SUBTITLE[screen]} — POR PROJETO E FASE`;
  content=<>
   {screen==="visao-geral"&&<ProjetoFaseAgrupamento idBase="pf-entregas" titulo={`ENTREGAS DA SEMANA ${br(week.start)} A ${br(week.end)} — POR PROJETO E FASE`} agrupamento={agruparEntregas(fr,cat,valid)}/>}
   {screen==="programacao"&&(prog.carregando?<ReportState title="Consultando a programação…">Programação semanal dos projetos selecionados.</ReportState>
    :prog.erro?<ReportState title={`Programação detalhada indisponível (${prog.erro})`} error><button className="gv-link" onClick={prog.retry}>Tentar novamente</button></ReportState>
    :<ProjetoFaseAgrupamento idBase="pf-programacao" detalheFase="programacao" titulo={`${titulo} · ${dia?`${diaDaSemanaCurto(dia)} ${br(dia)}`:`SEMANA ${br(week.start)} A ${br(week.end)}`}`}
      agrupamento={agruparProgramacao(selection,prog.dados,cat,dia)}/>)}
   {screen==="documentacao"&&<ProjetoFaseAgrupamento idBase="pf-documentos" titulo={titulo} agrupamento={agruparDocumentacao(fr,cat)}/>}
   {screen==="indicadores"&&<ProjetoFaseAgrupamento idBase="pf-indicadores" titulo={titulo} agrupamento={agruparIndicadores(fr,cat)}/>}
   <DiagnosticoNorma normas={normas.normas} erro={normas.erro}/>
  </>;
 }
 else{
  const fr=reports as FactsReport[];
  content=<>
   {screen==="visao-geral"&&<Overview reports={fr} cutoff={valid} cat={cat} onCutoff={setCutoff}/>}
   {screen==="programacao"&&<Programming reports={fr} semana={week} dia={dia} cat={cat} dados={prog.dados} erro={prog.erro} carregando={prog.carregando} onSemana={irParaSemana}/>}
   {screen==="documentacao"&&<Documentation reports={fr} ldp={ldp} cat={cat}/>}
   {screen==="indicadores"&&<Indicators reports={fr} cutoff={valid} ldp={ldp} cat={cat}/>}
   {consolidado&&<p className="gv-rule" data-testid="gv-normalizador">{siglas.aviso??(normalizador?.ativo
    ?`Consolidado: disciplinas agregadas pelo nome canônico PT-BR do Normalizador de Siglas (${siglas.regras.length} regra(s) ativa(s)); sem regra, vale o nome original.`
    :"Consolidado: nenhuma regra ativa no Normalizador de Siglas — disciplinas pelo nome original.")}</p>}
   <DiagnosticoNorma normas={normas.normas} erro={normas.erro}/>
  </>;
 }
 return <div className="gv-shell" data-testid={"gestao-a-vista-"+screen}>
  <aside className="gv-sidebar">
   <div className="gv-brand"><img src={NA_LOGO.escuro.src} alt={NA_LOGO.alt}/></div>
   <nav aria-label="Gestão à Vista">{SCREENS.map(x=>navTv
     ?<ItemMenuTv key={x.slug} navegacao={navTv} screenId={"gestao-a-vista-"+x.slug} chaves={encontrarFolha("gestao-a-vista/"+x.slug)?.permissionKeys??[]}
       ativo={x.slug===screen} className={x.slug===screen?"gv-active":undefined} classeIndisponivel="gv-nav-indisponivel">{x.number}. {x.title}</ItemMenuTv>
     :<Link key={x.slug} className={x.slug===screen?"gv-active":""} aria-current={x.slug===screen?"page":undefined}
     to={rotaProducao("gestao-a-vista/"+x.slug)}>{x.number}. {x.title}</Link>)}</nav>
  </aside>
  <div className="gv-main">
   <header className="gv-page-header">
    <div className="gv-header-text"><b>GERÊNCIA DA ENGENHARIA</b><h1>{SUBTITLE[screen]}</h1><small title={consolidado?selection.map(x=>x.name).join("\n"):name} data-testid="gv-escopo">{consolidado?`${porProjetoFase?"POR PROJETO E FASE":"CONSOLIDADO"} — ${selection.length} PROJETOS: ${name}`:name}</small>
     {consolidado&&<SeletorVisaoMultiprojeto valor={visaoMulti} onChange={setVisaoMulti}/>}</div>
    {screen==="programacao"&&<div className="gv-date gv-date-semana">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     <div><small>SEMANA</small>
      <span className="gv-date-input">
       <button type="button" aria-label="Semana anterior" title="Semana anterior" onClick={()=>irParaSemana(addDias(week.start,-7))}><ChevronLeft size={13}/></button>
       <b data-testid="gv-semana">{br(week.start)} A {br(week.end)}</b>
       <button type="button" aria-label="Próxima semana" title="Próxima semana" onClick={()=>irParaSemana(addDias(week.start,7))}><ChevronRight size={13}/></button>
      </span></div>
     <label><small>DIA (OPCIONAL)</small>
      <select aria-label="Filtrar um dia da semana" value={dia??""} onChange={e=>setDiaFiltro(e.target.value||null)}>
       <option value="">SEMANA COMPLETA</option>
       {atividadesPorDia(deduplicarProgramacao(prog.dados.flatMap(d=>d.linhas.map(l=>({...l,projectId:d.projectId})))).linhas,week.start)
        .map(x=><option key={x.data} value={x.data}>{x.dia} {brCurta(x.data).slice(0,5)} ({x.quantidade})</option>)}
      </select></label>
     <label><small>IR PARA DATA</small>
      <span className="gv-date-input"><input type="date" value={valid} onChange={e=>e.target.value&&irParaSemana(e.target.value)}/></span></label>
    </div>}
    {screen!=="documentacao"&&screen!=="programacao"&&<div className="gv-date">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     {screen==="visao-geral"&&<div><small>SEMANA</small><b>{brCurta(week.start)} A {brCurta(week.end)}</b></div>}
     <label><small>DATA DE CORTE</small>
      <span className="gv-date-input">
       <input type="date" value={valid} onChange={e=>setCutoff(e.target.value)}/>
      </span></label>
    </div>}
   </header>
   <main className={"gv-content gv-content-"+screen}>{content}</main>
  </div>
 </div>;
}
