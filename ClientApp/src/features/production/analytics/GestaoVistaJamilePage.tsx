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
import {dataReferenciaAutomatica,dataReferenciaValida,janelaDiariaAutomatica,type LayoutDiario} from "@/features/tv/tvLogic";
import {useHojeBahia} from "@/features/tv/useHojeBahia";
import {useLdpPorProjeto} from "./useLdpPorProjeto";
import {
 FASES,addDias,agrupar,classificarDocumento,classificarProgramacao,criarCatalogo,faseContagem,
 faseHhPrevisto,faseTotalDocumentos,fasePrazos,hhLinha,performancePorDisciplina,recursosDistintos,rotulo,semanaDoCorte,
 deduplicarProgramacao,diaDaSemanaCurto,filtrarDia,atividadesPorDia,semanasVizinhasComProgramacao,
 visaoGeralDaSemana,ldpParaEmissao,type ContagemVisaoGeral,SEM_DISCIPLINA,type CatalogoDisciplinas,type DisciplinasNorma,type PhaseValues,type ProgramacaoLinha,
 type ProgramacaoResposta,resumoProgramacao,ultimaSemanaComProgramacao,janelasDoPeriodo,mesclarJanelas,
 analiseDiariaAutomatica,type AtividadeDiaria,type DisciplinaDiaria,geometriaMarcaMeta,gradeDosGauges,type MetaNoCorte,
 ROTULO_SITUACAO,type SituacaoDisciplina
} from "./gestaoVistaJamileLogic";
import "./gestaoVistaJamile.css";
import { NA_LOGO } from "@/features/theme/brandAssets";
import ProjetoFaseAgrupamento,{SeletorVisaoMultiprojeto,type VisaoMultiprojeto} from "./projetoFase/ProjetoFaseAgrupamento";
import {agruparDocumentacao,agruparEntregas,agruparIndicadores,agruparProgramacao} from "./projetoFase/projetoFaseLogic";
import {useRegrasSiglasAtivas} from "@/features/disciplinas/useRegrasSiglasAtivas";
import {criarNormalizador} from "@/features/disciplinas/normalizadorSiglas";
import {GerenciaEngenhariaItem3,type ItemSemanaEngenharia,type SecaoEngenharia} from "./item3/GerenciaEngenhariaItem3";
import {criarIndiceTitulosDocumentos,obterTituloDocumentoOficial} from "./item3/documentTitleIndex";

/** "programacao-diaria" = slide de TV (gestao-a-vista-programacao-diaria): UM projeto e UM dia salvos no slide; fora do menu. */
export type JamileScreen="visao-geral"|"programacao"|"documentacao"|"indicadores"|"programacao-diaria";
const SCREENS:{slug:JamileScreen;number:string;title:string}[]=[
 {slug:"visao-geral",number:"01",title:"VISÃO GERAL"},
 {slug:"programacao",number:"02",title:"PROGRAMAÇÃO"},
 {slug:"documentacao",number:"03",title:"DOCUMENTAÇÃO"},
 {slug:"indicadores",number:"04",title:"INDICADORES"}];
const SUBTITLE:Record<JamileScreen,string>={"visao-geral":"PAINEL À VISTA",programacao:"PROGRAMAÇÃO SEMANAL",documentacao:"FLUXO DE DOCUMENTOS",indicadores:"INDICADOR DE PERFORMANCE",
 "programacao-diaria":"PROGRAMAÇÃO DIÁRIA"};
const TODAY=()=>new Date().toLocaleDateString("en-CA",{timeZone:"America/Bahia"});
const ACC_ID=/^b\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const GAUGES_POR_PAGINA=10;
const BARRAS_SEM_COMPACTAR=6;

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
function Tile({title,value,gates,className="",dica,nota}:{title:string;value:string;gates?:PhaseValues;className?:string;
 /** Só na 01: tooltip e nota curta sob o valor (ex.: "A EMITIR" inclui documentos sem comprovação). */
 dica?:string;nota?:ReactNode}){
 return <div className={`gv-tile ${className}`} title={dica}><span className="gv-tile-title">{title}</span><strong className="gv-tile-value">{value}</strong>
  {nota&&<small className="gv-tile-nota">{nota}</small>}
  {gates ? null : null} {/* Gates suprimidos em todas as telas da Gestão à Vista. */}
 </div>;
}
function Section({title,children,cls="",tone="steel"}:{title:string;children:ReactNode;cls?:string;tone?:"steel"|"navy"}){
 return <section className={`gv-section gv-section-${tone} ${cls}`}><header>{title}</header><div className="gv-section-body">{children}</div></section>;
}
function Legend({items}:{items:[string,string][]}){
 return <div className="gv-legend">{items.map(([cls,label])=><span key={label}><i className={cls}/>{label}</span>)}</div>;
}
function Empty({children}:{children:ReactNode}){return <div className="gv-empty">{children}</div>;}

/** 01 — DOCUMENTOS POR PROJETO e POR DISCIPLINA: programados (azul-escuro) e emitidos (verde, embaixo) em duas barras na MESMA
 *  escala, com os dois números sempre visíveis (zero inclusive). Emitidos são subconjunto dos programados: as barras nunca se somam. */
function BarrasDocumentos({rows,grupo}:{rows:{key:string;name:string;planned:number;actual:number;title?:string}[];grupo:"projeto"|"disciplina"}){
 const max=Math.max(1,...rows.map(r=>Math.max(r.planned,r.actual)));
 const largura=(n:number)=>({width:`calc((100% - 3em) * ${Math.min(1,n/max)})`});
 // Mais de 6 linhas (ex.: 9 disciplinas na TV): barras e textos mais finos para caber sem rolagem; mesma escala e mesmas cores.
 return <div className={"gv-pbars"+(rows.length>BARRAS_SEM_COMPACTAR?" gv-pbars-compacto":"")} data-testid={`gv-vg-barras-${grupo}`}>{rows.map(r=>
  <div className="gv-pbar" key={r.key} title={r.title}>
   <label>{r.name}</label>
   <div className="gv-pbar-linhas">
    <div className="gv-pbar-linha gv-pbar-prog"><i style={largura(r.planned)}/><b data-testid={`gv-vg-${grupo}-programados`}>{r.planned}</b></div>
    <div className="gv-pbar-linha gv-pbar-emit"><i style={largura(r.actual)}/><b data-testid={`gv-vg-${grupo}-emitidos`}>{r.actual}</b></div>
   </div>
  </div>)}</div>;
}

/** Meta acumulada desenhada no velocímetro da 01 (marca vermelha radial com seta). pct null = sem meta a marcar.
 *  `texto` ("META HOJE: x de y (z%)") vai SÓ para o aria-label — nunca é exibido (a meta visível é só a seta vermelha). */
interface MetaGauge{pct:number|null;texto:string;dica:string}
/** Velocímetro semicircular: arco de 180°, 0–100%, fundo cinza, progresso azul, valor central. >100% é sinalizado sem distorcer o arco.
 *  Com `meta` (só 01): seta vermelha da meta na mesma escala e fração; nenhum texto de meta/desvio visível (ficam no tooltip/aria);
 *  embaixo, só a nota discreta "N s/ comprov.". Com `situacao` (só 01): cor do arco — adiantada verde, no prazo azul, atrasada
 *  vermelho, indeterminada cinza (a faixa de fundo ganha um tom da mesma cor para a situação aparecer também com 0%). */
export function Gauge({title,value,detail,fracao,meta,nota,situacao}:{title:string;value:number|null;detail?:string;fracao?:string;meta?:MetaGauge;nota?:string;
 situacao?:SituacaoDisciplina}){
 const v=typeof value==="number"&&Number.isFinite(value)?value:null;
 const fill=v===null?0:Math.max(0,Math.min(100,v));
 const excedido=v!==null&&v>100;
 const arc="M 20 100 A 80 80 0 0 1 180 100";
 const marca=meta&&meta.pct!==null?geometriaMarcaMeta(meta.pct):null;
 const aria=`${title}: ${percent(v)}${situacao?` · Situação: ${ROTULO_SITUACAO[situacao]}`:""}${meta?` · ${meta.texto}`:""}`;
 return <figure className={"gv-gauge"+(excedido?" gv-gauge-over":"")+(meta?" gv-gauge-com-meta":"")+(situacao?` gv-gauge-sit-${situacao}`:"")}
  title={detail} data-situacao={situacao}>
  <figcaption>{title}</figcaption>
  <svg viewBox={meta?"-11 -11 222 133":"0 0 200 122"} role="img" aria-label={aria} preserveAspectRatio={meta?"xMidYMax meet":"xMidYMid meet"}>
   <path d={arc} className="gv-gauge-track" pathLength={100}/>
   {fill>0&&<path d={arc} className="gv-gauge-fill" pathLength={100} strokeDasharray={`${fill} 100`}/>}
   {marca&&<g className="gv-gauge-alvo" data-testid="gv-gauge-alvo" data-pct={marca.pct}><title>{meta!.dica}</title>
    <line className="gv-gauge-alvo-halo" x1={marca.x1} y1={marca.y1} x2={marca.x2} y2={marca.y2}/>
    <line x1={marca.x1} y1={marca.y1} x2={marca.x2} y2={marca.y2}/><polygon points={marca.seta}/></g>}
   {/* Com meta (01): percentual menor e alguns px acima, longe do arco e da marca; fração menor logo abaixo. */}
   <text x="100" y={meta?87:92} textAnchor="middle" className="gv-gauge-value">{percent(v)}</text>
   {fracao&&<text x="100" y={meta?109:116} textAnchor="middle" className="gv-gauge-fracao" data-testid="gv-gauge-fracao">{fracao}</text>}
   {excedido&&<text x="100" y="62" textAnchor="middle" className="gv-gauge-over-label">ACIMA DE 100%</text>}
   <text x="20" y="118" textAnchor="middle" className="gv-gauge-end">0,0%</text>
   <text x="180" y="118" textAnchor="middle" className="gv-gauge-end">100,0%</text>
  </svg>
  {meta&&nota&&<div className="gv-gauge-meta" data-testid="gv-gauge-meta" title={meta.dica}><span className="gv-gauge-meta-nota">{nota}</span></div>}
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
/** Linha das barras: documentos distintos (previstos) × emitidos comprovados; atividades e sem comprovação no tooltip. */
const barraVisaoGeral=(key:string,name:string,c:ContagemVisaoGeral)=>({key,name,planned:c.documentos,actual:c.emitidos,
 title:`${name} · ${c.documentos} documento(s) distinto(s) · ${c.atividades} atividade(s) · ${c.emitidos} emitido(s)${c.semComprovacao?` · ${c.semComprovacao} sem comprovação de emissão`:""}`});
export function Overview({reports,dados,selection,semana,cat,carregando,onSemana}:{reports:FactsReport[];dados:ProgramacaoResposta[];selection:PmoSelecionado[];
 semana:{start:string;end:string};cat:CatalogoDisciplinas;carregando:boolean;onSemana:(d:string)=>void}){
 // HOJE (Bahia) se atualiza sozinho com a tela aberta (TV 24/7): meta e corte acompanham o dia; quem estava na semana corrente
 // passa para a nova semana na virada de sábado para domingo (semana escolhida à mão não é trocada).
 const hoje=useHojeBahia();
 const [hojeVisto,setHojeVisto]=useState(hoje);
 useEffect(()=>{
  if(hoje===hojeVisto)return;
  if(semana.start===semanaDoCorte(hojeVisto).start&&semanaDoCorte(hoje).start!==semana.start)onSemana(hoje);
  setHojeVisto(hoje);
 },[hoje,hojeVisto,semana.start,onSemana]);
 // MESMA fonte da 02 (programação semanal por projeto); a LDP só concilia a emissão por projeto + código.
 const vg=visaoGeralDaSemana({dados,projetos:selection,cat,semana,hoje,ldp:ldpParaEmissao(reports)});
 const maxDia=Math.max(1,...vg.porDia.map(x=>x.documentos));
 const porProjeto=vg.porProjeto.map(p=>barraVisaoGeral(p.projectId,p.documentos?p.nome:`${p.nome} — sem programação na semana`,p));
 const porDisc=vg.porDisciplina.map(d=>barraVisaoGeral(d.chave,d.nome,d));
 const [pagina,setPagina]=useState(0);
 const paginas=Math.max(1,Math.ceil(vg.porDisciplina.length/GAUGES_POR_PAGINA));
 const visiveis=vg.porDisciplina.slice(Math.min(pagina,paginas-1)*GAUGES_POR_PAGINA,(Math.min(pagina,paginas-1)+1)*GAUGES_POR_PAGINA);
 const gradeGauges=gradeDosGauges(visiveis.length);
 const vizinhas=semanasVizinhasComProgramacao(dados.flatMap(d=>d.datasComProgramacao.map(x=>x.data)),semana);
 const semProva=vg.semComprovacao>0;
 const vazio=!vg.documentos&&<Empty>{carregando?"Carregando a programação semanal…":`Nenhuma atividade programada entre ${br(semana.start)} e ${br(semana.end)} nos ${selection.length} projeto(s).`}
  {!carregando&&vizinhas.anterior&&<button onClick={()=>onSemana(vizinhas.anterior!.start)}>Semana anterior com programação: {br(vizinhas.anterior.start)} a {br(vizinhas.anterior.end)}</button>}
  {!carregando&&vizinhas.proxima&&<button onClick={()=>onSemana(vizinhas.proxima!.start)}>Próxima semana com programação: {br(vizinhas.proxima.start)} a {br(vizinhas.proxima.end)}</button>}</Empty>;
 const legenda:[string,string][]=[["gv-dot-navy","Documentos programados"],["gv-dot-teal","Emitidos (LDP)"]];
 // Meta acumulada: rótulo do corte (hoje / sábado / semana futura) e texto "x de y (z%)" — SÓ no aria-label e nos tooltips (a meta visível é a seta).
 const m=vg.meta;
 const rotuloMeta=m.estado==="corrente"?"META HOJE":m.estado==="concluida"?`META ATÉ ${diaDaSemanaCurto(semana.end)} ${br(semana.end).slice(0,5)}`:"META (SEMANA FUTURA)";
 const textoMeta=(x:MetaNoCorte)=>`${rotuloMeta}: ${x.meta} de ${x.documentos} (${percent(x.metaPct)})`;
 const corteTxt=m.corte?`corte ${diaDaSemanaCurto(m.corte)} ${br(m.corte)}`:"semana futura: sem meta antes do domingo";
 // Corte do backlog (desvio/atraso): semana em curso = ontem (hoje só entra depois da meia-noite da Bahia); concluída = sábado (foto do fechamento).
 const ateAtraso=m.corteAtraso?`${diaDaSemanaCurto(m.corteAtraso)} ${br(m.corteAtraso)}`:null;
 const dicaMeta=(nome:string,x:MetaNoCorte)=>`${nome} — ${corteTxt} · Planejado (meta acumulada): ${x.meta} de ${x.documentos} documento(s) com data prevista até o corte (${percent(x.metaPct)})`
  +` · Realizado: ${x.realizados} de ${x.documentos} emitido(s) até ${br(m.corteRealizado)}${x.realizadoPct===null?"":` (${percent(x.realizadoPct)})`}`
  +(m.corte?` · Aderência até o corte: ${x.aderidos} de ${x.meta} previsto(s) emitido(s)${x.indeterminadosMeta?` (${x.indeterminadosMeta} indeterminado(s))`:""}`:"")
  +(ateAtraso?` · Desvio (pendências de dias anteriores): ${x.desvio} de ${x.vencidos} documento(s) previsto(s) até ${ateAtraso} sem emissão comprovada até ${br(m.corteRealizado)}`
   :" · Desvio: nenhum dia anterior a avaliar")
  +(x.indeterminados?` · Indeterminado: ${x.indeterminados} vencido(s) sem comprovação de emissão (não contado como desvio)`:"")
  +(x.semComprovacao?` · ${x.semComprovacao} sem comprovação na disciplina: percentual realizado indisponível`:"");
 const notaGauge=(x:MetaNoCorte)=>x.semComprovacao?`${x.semComprovacao} s/ comprov.`:undefined;
 // Situação da disciplina (cor do arco): o tooltip diz a situação, o corte e as contagens que a sustentam.
 const corteSituacao=`${diaDaSemanaCurto(m.corteRealizado)} ${br(m.corteRealizado)}`;
 const motivoSituacao=(x:MetaNoCorte)=>x.situacao==="atrasada"?`${x.desvio} documento(s) previsto(s) até ${ateAtraso} sem emissão comprovada`
   +(x.antecipados?` (prioridade sobre ${x.antecipados} antecipado(s))`:"")
  :x.situacao==="indeterminada"?`Sem comprovação suficiente: ${x.indeterminados} documento(s) vencido(s) sem comprovação de emissão (não contado como atraso)`
  :x.situacao==="adiantada"?`${x.antecipados} documento(s) previsto(s) para depois de ${br(m.corteRealizado)} já emitido(s); nenhum vencido em aberto`
  :`nenhum documento vencido em aberto${ateAtraso?"":" (nenhum dia anterior a avaliar)"} e nenhum antecipado; previsto para hoje e não emitido continua no prazo`;
 const dicaSituacao=(x:MetaNoCorte)=>`Situação: ${ROTULO_SITUACAO[x.situacao]} — corte ${corteSituacao} (Bahia) · ${motivoSituacao(x)}`;
 const situacoesVistas=new Set(visiveis.map(x=>x.meta.situacao));
 // Cartões %ADERÊNCIA / %DESVIO: nota curta com a fração da coorte, ou o motivo do "—" (coorte vazia / indeterminado).
 const t=m.total;
 const notaKpi=(texto:string,alerta=false)=>alerta?texto:<span className="gv-tile-nota-neutra">{texto}</span>;
 const notaAderencia=!m.corte?notaKpi("semana futura"):!t.meta?notaKpi(`nenhum previsto até ${br(m.corte).slice(0,5)}`)
  :t.indeterminadosMeta?notaKpi(`indeterminado: ${t.indeterminadosMeta} s/ comprov.`,true):notaKpi(`${t.aderidos} de ${t.meta} previstos`);
 const notaDesvio=!m.corteAtraso?notaKpi("semana futura"):!t.vencidos?notaKpi(m.estado==="corrente"?"nenhum vencido antes de hoje":"nenhum previsto")
  :t.indeterminados?notaKpi(`indeterminado: ${t.indeterminados} s/ comprov.`,true):notaKpi(`${t.desvio} de ${t.vencidos} vencidos`);
 const foto=m.estado==="concluida"?` Semana concluída: foto do fechamento (${diaDaSemanaCurto(semana.end)} ${br(semana.end)}) — emissão depois do sábado não conta como pontual.`:"";
 const dicaAderencia=m.corte?`Documentos distintos com data prevista até ${br(m.corte)}${m.estado==="corrente"?" (hoje inclusive)":""} e emissão oficial comprovada pela LDP até ${br(m.corteRealizado)}`
  +` ÷ documentos previstos até ${br(m.corte)}. Emissão antecipada de documento previsto para depois não compensa documento vencido.${foto}`
  +(t.indeterminadosMeta?` ${t.indeterminadosMeta} documento(s) previsto(s) sem comprovação de emissão: percentual indeterminado.`:"")
  :"Semana futura: nenhum documento previsto até hoje.";
 const dicaDesvio=ateAtraso?`Backlog: documentos distintos com data prevista até ${ateAtraso}${m.estado==="corrente"?" (dias anteriores a hoje; hoje só entra depois da meia-noite, Bahia)":""}`
  +` que continuam sem emissão comprovada até ${br(m.corteRealizado)} ÷ documentos previstos até ${ateAtraso}. Cada documento conta uma vez; documento emitido sai do backlog.${foto}`
  +(t.indeterminados?` ${t.indeterminados} documento(s) vencido(s) sem comprovação de emissão: percentual indeterminado.`:"")
  :"Semana futura: nenhum documento vencido.";
 return <>
  <div className="gv-kpis gv-six gv-vg-kpis" data-testid="gv-vg-kpis">
   <Tile title="DOCUMENTOS PROGRAMADOS" value={carregando?"…":String(vg.documentos)}/>
   <Tile title="EMITIDOS" value={carregando?"…":String(vg.emitidos)}
    dica={`Documentos programados na semana com emissão comprovada pela LDP (mesmo projeto e código) até ${br(m.corteRealizado)}${m.estado==="concluida"?" (fechamento da semana)":" (hoje)"}, inclusive antecipados.`}/>
   <Tile title="EM ATRASO" value={carregando?"…":String(vg.atrasados)}
    dica={ateAtraso?`Mesmo backlog do %DESVIO: ${t.desvio} documento(s) com data prevista até ${ateAtraso} sem emissão comprovada até ${br(m.corteRealizado)}.`:"Semana futura: nada em atraso."}/>
   <Tile title="A EMITIR" value={carregando?"…":String(vg.aEmitir)}
    dica={`Saldo matemático = documentos programados − emitidos (nunca negativo).${semProva?` Inclui ${vg.semComprovacao} documento(s) sem comprovação de emissão na LDP — não são atraso nem emissão.`:""}`}
    nota={!carregando&&semProva?`inclui ${vg.semComprovacao} s/ comprov.`:undefined}/>
   <Tile title="%ADERÊNCIA À PROGRAMAÇÃO" value={carregando?"…":percent(vg.aderencia)} dica={dicaAderencia} nota={carregando?undefined:notaAderencia}/>
   <Tile title="%DESVIO DA PROGRAMAÇÃO" value={carregando?"…":percent(vg.desvio)} dica={dicaDesvio} nota={carregando?undefined:notaDesvio}/>
  </div>
  {!carregando&&vg.documentos>0&&<p className="gv-rule" data-testid="gv-vg-contagem">{vg.documentos} documento(s) distinto(s) (projeto + código) em {vg.atividades} atividade(s) programada(s)
   {vg.repetidos>0&&<>; {vg.repetidos} registro(s) repetido(s) da importação contado(s) uma vez</>}.
   {" "}Conciliação com a LDP até {br(m.corteRealizado)}: {vg.emitidos} emitido(s), {vg.pendentes} não emitido(s){semProva&&<>, <b>{vg.semComprovacao} sem comprovação</b> ({vg.motivos.map(m=>`${m.motivo}: ${m.quantidade}`).join("; ")})</>}.
   {(t.indeterminadosMeta>0||t.indeterminados>0)&&<> Aderência/desvio indeterminados (—): há documento sem comprovação de emissão na coorte avaliada.</>}</p>}
  <div className="gv-overview">
   <Section title="EVOLUÇÃO DIÁRIA DA PROGRAMAÇÃO" cls="gv-ov-evolution">
    <Legend items={[["gv-dot-sky","Documentos programados no dia"],["gv-dot-navy","Emitidos"]]}/>
    {vg.documentos?<div className="gv-columns">{vg.porDia.map(x=><div key={x.data} className="gv-col" title={`${br(x.data)} · ${x.documentos} documento(s) · ${x.atividades} atividade(s) · Emitidos ${x.emitidos}`}>
      <div className="gv-col-bars">
       <span className="gv-bar-sky" style={{height:`${x.documentos/maxDia*100}%`}}>{x.documentos>0&&<b>{x.documentos}</b>}</span>
       <span className="gv-bar-navy" style={{height:`${x.emitidos/maxDia*100}%`}}>{x.emitidos>0&&<b>{x.emitidos}</b>}</span>
      </div>
      <small>{DIA[new Date(x.data+"T12:00:00Z").getUTCDay()]} {x.data.slice(8)}/{x.data.slice(5,7)}</small>
     </div>)}</div>:vazio}
   </Section>
   <Section title="DOCUMENTOS POR PROJETO" cls="gv-ov-project">
    <Legend items={legenda}/>
    <BarrasDocumentos rows={porProjeto} grupo="projeto"/>
   </Section>
   <Section title="DOCUMENTOS POR DISCIPLINA" cls="gv-ov-discipline">
    <Legend items={legenda}/>
    {porDisc.length?<BarrasDocumentos rows={porDisc} grupo="disciplina"/>:<Empty>Sem programação na semana.</Empty>}
   </Section>
   <Section title="% EMISSÃO POR DISCIPLINA" cls="gv-ov-gauges">
    {/* Legenda centralizada: data do realizado + cores do arco (situação). Meta e desvio NÃO aparecem em texto: a meta é só a
        seta vermelha de cada gauge (valores exatos no tooltip/aria). Cinza só entra na legenda se algum gauge estiver indeterminado. */}
    {porDisc.length>0&&<div className="gv-legend gv-legend-meta" data-testid="gv-vg-legenda-meta" title={dicaMeta("Total da semana",m.total)}>
     <span data-testid="gv-vg-legenda-emitidos">EMITIDOS ATÉ {br(m.corteRealizado)}</span>
     <span className="gv-legend-situacoes" data-testid="gv-vg-legenda-situacoes">
      {(["adiantada","no-prazo","atrasada","indeterminada"] as const).filter(s=>s!=="indeterminada"||situacoesVistas.has(s)).map(s=>
       <span key={s}><i className={`gv-dot-sit gv-dot-sit-${s}`}/>{s==="indeterminada"?"SEM COMPROVAÇÃO":ROTULO_SITUACAO[s].toUpperCase()}</span>)}
     </span>
    </div>}
    {/* Grade pelo número de gauges da página: até 4 = 2 colunas; 5–10 = 3 colunas (gauges maiores que 2 × 5 na TV). Paginação de 10 mantida. */}
    {porDisc.length?<div className="gv-gauge-grid" style={{gridTemplateColumns:`repeat(${gradeGauges.colunas},minmax(0,1fr))`,
      gridTemplateRows:`repeat(${gradeGauges.linhas},minmax(0,1fr))`}}>{visiveis.map(x=><Gauge key={x.chave} title={x.nome}
      value={x.meta.realizadoPct}
      fracao={`${x.meta.realizados} de ${x.meta.documentos}`}
      meta={{pct:x.meta.metaPct,texto:textoMeta(x.meta),dica:dicaMeta(x.nome,x.meta)}} nota={notaGauge(x.meta)} situacao={x.meta.situacao}
      detail={`${dicaSituacao(x.meta)} · ${dicaMeta(x.nome,x.meta)}`}/>)}</div>:<Empty>Sem programação na semana.</Empty>}
    {paginas>1&&<div className="gv-pager"><button disabled={pagina<=0} onClick={()=>setPagina(p=>p-1)} aria-label="Página anterior"><ChevronLeft size={14}/></button>
     <span>{Math.min(pagina,paginas-1)+1}/{paginas}</span>
     <button disabled={pagina>=paginas-1} onClick={()=>setPagina(p=>p+1)} aria-label="Próxima página"><ChevronRight size={14}/></button></div>}
   </Section>
  </div>
  <p className="gv-rule">Fonte: programação semanal (mesma da 02. PROGRAMAÇÃO), semana domingo–sábado. Documento = projeto + código
   (várias atividades do mesmo documento contam uma vez; atividade sem código conta como item próprio), na data prevista de emissão (atividade de
   emissão na tarefa ou, sem ela, a última atividade do documento na semana). Emitido = emissão oficial da LDP do mesmo projeto e código (status
   aprovado ou data de emissão) até {br(m.corteRealizado)}{m.estado==="concluida"?" (fechamento da semana)":""}. Aderência = previstos até {m.corte?br(m.corte):"—"} emitidos ÷ previstos
   até {m.corte?br(m.corte):"—"}. Desvio e em atraso = mesmo backlog: previstos até {m.corteAtraso?br(m.corteAtraso):"—"}{m.estado==="corrente"?" (antes de hoje)":""} ainda não emitidos, cada documento
   uma vez. A emitir = programados − emitidos. Meta acumulada (marca vermelha): {m.corte?`${corteTxt} (Bahia)`:"semana futura: meta zero"}.
   Cor do arco: vermelho = documento vencido não emitido (prioridade); verde = documento futuro já emitido; azul = no prazo; cinza = sem comprovação suficiente.
   Sem comprovação = indeterminado (—), nunca atraso. Sem fases ou gates.</p>
 </>;
}

/* ---------------------------------------------------------------- 02. PROGRAMAÇÃO */
type ProgLinha=ProgramacaoLinha&{projectId:string};
const cobertura=(xs:readonly ProgLinha[],f:(l:ProgLinha)=>string|null)=>`${xs.filter(l=>(f(l)??"").trim()!=="").length}/${xs.length}`;
function Programming({reports,semana,dia,cat,dados,erro,carregando,onSemana,historico=false,diario=false,rotulos}:{reports:FactsReport[];semana:{start:string;end:string};dia:string|null;
 cat:CatalogoDisciplinas;dados:ProgramacaoResposta[];erro:string|null;carregando:boolean;onSemana:(data:string)=>void;
 /** Modo histórico: TODAS as atividades importadas com a data real de cada uma (semana = período disponível). */
 historico?:boolean;
 /** Programação Diária da TV: somente o dia do slide (dia obrigatório), sem agrupamento por dia nem navegação de semana. */
 diario?:boolean;
 /** Diária automática (Padrão, seção ONTEM): títulos próprios no MESMO layout; ausente = títulos da 02/diária de sempre. */
 rotulos?:{documentos:string;hh:string;titulo:string;vazio:string}}){
 const [agruparDia,setAgruparDia]=useState(true);
 const noPeriodo=(d:string|null|undefined)=>!!d&&d>=semana.start&&d<=semana.end&&(!dia||d===dia);
 // Semana inteira (domingo–sábado) sem repetições; o filtro de dia é opcional e só recorta a semana já carregada.
 const semanaDedup=deduplicarProgramacao(dados.flatMap(d=>d.linhas.map(l=>({...l,projectId:d.projectId}))));
 const linhas:ProgLinha[]=filtrarDia(semanaDedup.linhas,dia);
 // Fallback (fonte detalhada indisponível): datas da LDP, sem responsáveis — nunca inventados.
 const ldp=erro?enrichDocuments(reports).filter(d=>!d.isActivity&&!d.cancelado&&noPeriodo(d.dataAlvo?.slice(0,10))):[];
 // Item 3 (Codex): título OFICIAL do documento (LdpAtual.Document_Title) por projeto + código; a tarefa nunca é usada como título.
 const indiceTitulos=criarIndiceTitulosDocumentos(reports);
 const itens=(linhas.length?linhas.map(l=>({key:`${l.projectId}:${l.idRegistro}`,projectId:l.projectId,chave:classificarProgramacao(cat,l.projectId,l).chave,codigo:l.documento,
   elaborador:l.elaborador,verificador:l.verificador,aprovador:l.aprovador,hh:hhLinha(l),status:l.status,data:l.dataAtividade.slice(0,10),
   titulo:`${l.tarefa??""}${l.siglaDoc3?` · DOC3 ${l.siglaDoc3}`:""} · HH elab. ${fmt2(l.hhElaboracao)} + verif. ${fmt2(l.hhVerificacao)} + aprov. ${fmt2(l.hhAprovacao)}`,
   tituloDocumento:obterTituloDocumentoOficial(indiceTitulos,l.projectId,l.documento)}))
  :ldp.map(d=>({key:d.rowId,projectId:d.projectId,chave:classificarDocumento(cat,d).chave,codigo:d.codigo,elaborador:null,verificador:null,aprovador:null,hh:d.hhPrevisto,
   status:d.statusInterno,data:d.dataAlvo!.slice(0,10),titulo:d.titulo??"",tituloDocumento:obterTituloDocumentoOficial(indiceTitulos,d.projectId,d.codigo)})))
  // Ordem: dia → disciplina → documento (agrupamento por dia usa a mesma ordem).
  .sort((a,b)=>a.data.localeCompare(b.data)||rotulo(cat,a.chave).localeCompare(rotulo(cat,b.chave),"pt-BR")||(a.codigo??"").localeCompare(b.codigo??"","pt-BR"));
 // Um documento é único por projeto + código dentro da disciplina; atividades não duplicam a contagem.
 const documentosDistintos=(xs:typeof itens)=>new Set(xs.map(x=>x.projectId+"::"+(x.codigo?.trim().toUpperCase()||x.key))).size;
 const grupos=[...agrupar(itens,x=>x.chave)].map(([k,xs])=>({key:k,name:rotulo(cat,k),planned:documentosDistintos(xs),actual:0,
  hh:xs.some(x=>x.hh!==null)?xs.reduce((s,x)=>s+(x.hh??0),0):null})).sort((a,b)=>b.planned-a.planned||a.name.localeCompare(b.name,"pt-BR"));
 const totalDocumentos=documentosDistintos(itens);
 const totalHh=itens.some(x=>x.hh!==null)?itens.reduce((s,x)=>s+(x.hh??0),0):null;
 const itensSemHh=itens.filter(x=>x.hh===null).length;
 const vizinhas=semanasVizinhasComProgramacao(dados.flatMap(d=>d.datasComProgramacao.map(x=>x.data)),semana);
 const registros=dados.reduce((s,d)=>s+d.registrosProjeto,0);
 const periodo=dia?`${diaDaSemanaCurto(dia)} ${br(dia)}`:historico?"a programação importada":`a semana de ${br(semana.start)} a ${br(semana.end)}`;
 // Item 3 (Codex): Semana Completa no topo (largura total, com título do documento) e, abaixo, Entregáveis × HH por disciplina.
 return <div className="gv-program-item3" style={{display:"flex",flexDirection:"column",flex:1,minHeight:0,gap:"1em"}}>
  <div className="gv-kpis gv-three">
   <Tile title={rotulos?.documentos??(historico?"DOCUMENTOS IMPORTADOS COM DATA":diario?"DOCUMENTOS DO DIA":"DOCUMENTOS DA SEMANA")} value={String(totalDocumentos)}/>
   <Tile title={rotulos?.hh??(dia?"HH PLANEJADO DO DIA":"HH PLANEJADO DA SEMANA")} value={fmt2(totalHh)}/>
   <Tile title="RECURSOS" value={linhas.length?String(recursosDistintos(linhas)):"—"}/>
  </div>
  {itensSemHh>0&&<p className="gv-rule" role="status">HH planejado {totalHh===null?"não informado":"parcial"}: {itensSemHh} atividade(s) sem horas na programação semanal. O total considera apenas os HH disponíveis {diario?"do dia":"da semana"}.</p>}
  <GerenciaEngenhariaItem3
   itens={itens.map(x=>({key:x.key,data:x.data,dia:`${diaDaSemanaCurto(x.data)} ${brCurta(x.data).slice(0,5)}`,disciplina:rotulo(cat,x.chave),
    codigoDocumento:x.codigo,tituloDocumento:x.tituloDocumento,elaborador:x.elaborador,verificador:x.verificador,aprovador:x.aprovador,situacao:x.status,hhPrevisto:x.hh}))}
   grupos={grupos.map(x=>({key:x.key,nome:x.name,entregaveis:x.planned,hh:x.hh}))}
   agruparPorDia={diario?false:agruparDia}
   onAgruparPorDia={diario?undefined:setAgruparDia}
   descricaoPeriodo={diario&&dia?`${diaDaSemanaCurto(dia)} ${br(dia)}`:dia?`Filtro: ${diaDaSemanaCurto(dia)} ${br(dia)}`:historico?`Datas reais de ${br(semana.start)} a ${br(semana.end)} — não é a semana atual`:`${br(semana.start)} a ${br(semana.end)}`}
   titulo={rotulos?.titulo??(historico?"TODAS AS ATIVIDADES IMPORTADAS":diario?"DOCUMENTOS DO DIA":"DOCUMENTOS DA SEMANA")}
   carregando={carregando}
   mensagemVazia={rotulos?<Empty>{rotulos.vazio}</Empty>:<Empty>
    {registros?`Nenhuma atividade programada para ${periodo} (${registros} registro(s) na programação do projeto).`:"O projeto não possui programação semanal importada."}
    {!historico&&!diario&&vizinhas.anterior&&<button onClick={()=>onSemana(vizinhas.anterior!.start)}>Semana anterior com programação: {br(vizinhas.anterior.start)} a {br(vizinhas.anterior.end)}</button>}
    {!historico&&!diario&&vizinhas.proxima&&<button onClick={()=>onSemana(vizinhas.proxima!.start)}>Próxima semana com programação: {br(vizinhas.proxima.start)} a {br(vizinhas.proxima.end)}</button>}
   </Empty>}
   aviso={erro?<p className="gv-warning">Programação detalhada indisponível ({erro}); exibindo datas-alvo da LDP, sem responsáveis.</p>:undefined}
   rodape={!erro&&dados.length>0?<p className="gv-rule">Fonte: programação semanal (data da atividade = Reprogramada › Baseline), {diario&&dia?`somente as atividades de ${br(dia)} (sem agregação da semana)`:"semana domingo a sábado"}. HH previsto = elaboração + verificação + aprovação. TBD não conta como recurso.
    {semanaDedup.repetidos>0&&<> {semanaDedup.repetidos} registro(s) repetido(s) (mesmo documento, tarefa, fase e dia) contado(s) uma vez.</>}
    {linhas.length>0&&<> Preenchimento na planilha semanal: elaborador {cobertura(linhas,l=>l.elaborador)}, verificador {cobertura(linhas,l=>l.verificador)}, aprovador {cobertura(linhas,l=>l.aprovador)}, situação {cobertura(linhas,l=>l.status)}. "—" = campo vazio na fonte (Recurso / Weekly_Responsible_Verification / Weekly_Responsible_Approval / Weekly_Progress_Status).</>}
    {" "}Título do documento = LDP (Document_Title) do mesmo projeto e código; "—" = sem título na LDP.</p>:undefined}
  />
 </div>;
}

/* ---------------------------------------------------------------- PROGRAMAÇÃO DIÁRIA AUTOMÁTICA (TV) */
type AnaliseDiaria=ReturnType<typeof analiseDiariaAutomatica>;
const somaHhDisc=(xs:readonly (number|null)[])=>xs.some(x=>x!==null)?xs.reduce<number>((s,x)=>s+(x??0),0):null;
/**
 * Slide AUTO: HOJE e ONTEM pela data civil da Bahia (o slide nunca guarda data). Mesma fonte/deduplicação da 02; pendente de
 * ontem = não emissão COMPROVADA pela LDP; sem prova = SEM COMPROVAÇÃO, nunca pendente. As três exibições (TvSlide.layoutDiario)
 * seguem a hierarquia da 02 (ajuste visual 20/10): EM CIMA, em largura total, a tabela de documentos da 02 (GerenciaEngenhariaItem3)
 * em seções separadas — PREVISTO HOJE, PENDÊNCIAS DE ONTEM COMPROVADAS e, à parte, ONTEM SEM COMPROVAÇÃO; EMBAIXO, os resumos:
 *   • padrao     = gráficos DOCUMENTOS / HH POR DISCIPLINA da 02;
 *   • cards      = um card por disciplina (hoje e ontem) + os mesmos gráficos;
 *   • disciplina = só a disciplina do slide: documentos dela em cima, indicadores dela embaixo (sem gráfico de uma barra só e sem
 *                  repetir os mesmos números em cartões no topo).
 */
export function DiariaAutomatica({reports,dados,cat,hoje,carregando,layout,disciplina,disciplinaNome}:{reports:FactsReport[];dados:ProgramacaoResposta[];
 cat:CatalogoDisciplinas;hoje:string;carregando:boolean;layout:LayoutDiario;disciplina:string|null;disciplinaNome:string|null}){
 const a=analiseDiariaAutomatica({dados,cat,hoje,ldp:ldpParaEmissao(reports)});
 const indice=criarIndiceTitulosDocumentos(reports);
 const diaH=`${diaDaSemanaCurto(a.hoje)} ${br(a.hoje)}`,diaO=`${diaDaSemanaCurto(a.ontem)} ${br(a.ontem)}`;
 const v=(n:number|null)=>carregando?"…":fmt2(n);
 const n=(x:number)=>carregando?"…":String(x);
 const umaDisciplina=layout==="disciplina";
 const disciplinas=umaDisciplina?a.disciplinas.filter(d=>d.chave===disciplina):a.disciplinas;
 const soma=(f:(d:DisciplinaDiaria)=>number)=>disciplinas.reduce((s,d)=>s+f(d),0);
 const t=umaDisciplina?{documentosHoje:soma(d=>d.documentosHoje),hhHoje:somaHhDisc(disciplinas.map(d=>d.hhHoje)),
   pendentes:soma(d=>d.pendentes.length),hhPendente:somaHhDisc(disciplinas.map(d=>d.hhPendente)),semComprovacao:soma(d=>d.semComprovacao.length),
   emitidosOntem:soma(d=>d.emitidosOntem),regularizados:soma(d=>d.regularizados),
   documentosOntem:soma(d=>d.pendentes.length+d.semComprovacao.length+d.emitidosOntem+d.regularizados)}:a.totais;
 const nomeDisciplina=disciplina&&(cat.rotulos.has(disciplina)||disciplina===SEM_DISCIPLINA)?rotulo(cat,disciplina):disciplinaNome?.trim()||disciplina||"—";
 const foraDaNorma=umaDisciplina&&!!disciplina&&disciplina!==SEM_DISCIPLINA&&!cat.rotulos.has(disciplina);
 // Linhas da tabela da 02: uma por atividade (como na 02); título = LDP oficial do mesmo projeto e código, "—" se a LDP não tiver.
 const item=(l:AtividadeDiaria,nome:string,situacao:string|null):ItemSemanaEngenharia=>{const data=l.dataAtividade.slice(0,10);return {
  key:`${l.projectId}:${l.idRegistro}`,data,dia:`${diaDaSemanaCurto(data)} ${brCurta(data).slice(0,5)}`,disciplina:nome,codigoDocumento:l.documento,
  tituloDocumento:obterTituloDocumentoOficial(indice,l.projectId,l.documento),elaborador:l.elaborador,verificador:l.verificador,aprovador:l.aprovador,
  situacao,hhPrevisto:l.hh};};
 const itensHoje=disciplinas.flatMap(d=>d.hoje.map(l=>item(l,d.nome,l.status)));
 const itensPendentes=disciplinas.flatMap(d=>d.pendentes.flatMap(x=>x.atividades.map(l=>item(l,d.nome,`${l.status?.trim()||"—"} · LDP: NÃO EMITIDO`))));
 const itensSemProva=disciplinas.flatMap(d=>d.semComprovacao.flatMap(x=>x.atividades.map(l=>item(l,d.nome,`SEM COMPROVAÇÃO: ${x.motivo??"—"}`))));
 const secoes:SecaoEngenharia[]=[
  {key:"hoje",testId:"gv-diaria-secao-hoje",rotulo:`PREVISTO HOJE · ${diaH}`,itens:itensHoje,
   vazio:`Nada programado para hoje (${diaH})${umaDisciplina?` em ${nomeDisciplina}`:" neste projeto"}.`},
  {key:"ontem",testId:"gv-diaria-secao-ontem",rotulo:`PENDÊNCIAS DE ONTEM COMPROVADAS (NÃO EMITIDAS NA LDP) · ${diaO}`,itens:itensPendentes,
   vazio:`Nenhuma pendência comprovada de ontem (${diaO}): ${t.documentosOntem} documento(s) programado(s) ontem — ${t.emitidosOntem} emitido(s) até ontem, `
    +`${t.regularizados} emitido(s) depois, ${t.semComprovacao} sem comprovação.`},
  ...(itensSemProva.length?[{key:"sem-comprovacao",testId:"gv-diaria-sem-comprovacao",tom:"alerta" as const,
   rotulo:`ONTEM SEM COMPROVAÇÃO / INDETERMINADO — NÃO CONTADO COMO PENDÊNCIA · ${diaO}`,itens:itensSemProva}]:[])];
 const itens=[...itensHoje,...itensPendentes,...itensSemProva];
 // Gráficos: o que está na mesa hoje = previsto hoje + pendências comprovadas (sem comprovação não entra). Documento contado uma vez.
 const grupos=disciplinas.map(d=>{const xs=[...d.hoje,...d.pendentes.flatMap(x=>x.atividades)];
  return {key:d.chave,nome:d.nome,entregaveis:new Set(xs.map(l=>l.projectId+"::"+(l.documento?.trim().toUpperCase()||"#"+l.idRegistro))).size,hh:somaHhDisc(xs.map(l=>l.hh))};})
  .filter(g=>g.entregaveis>0).sort((x,y)=>y.entregaveis-x.entregaveis||x.nome.localeCompare(y.nome,"pt-BR"));
 const semHh=[...itensHoje,...itensPendentes].filter(x=>x.hhPrevisto===null).length;
 const vazio=!carregando&&!disciplinas.length;
 const resumo=vazio||carregando?undefined:layout==="cards"
  ?<section className="ge3-panel gv-diaria-resumo" data-testid="gv-diaria-resumo"><h3>RESUMO POR DISCIPLINA · HOJE E ONTEM</h3>
    <div className="gv-diaria-cards" data-testid="gv-diaria-cards">{disciplinas.map(d=><CartaoDisciplinaDiaria key={d.chave} d={d} diaH={diaH}/>)}</div></section>
  :umaDisciplina
  ?<section className="ge3-panel gv-diaria-resumo gv-diaria-resumo-uma" data-testid="gv-diaria-resumo"><h3>INDICADORES DE {nomeDisciplina}</h3>
    {disciplinas.map(d=><IndicadoresDisciplinaDiaria key={d.chave} d={d} diaH={diaH} diaO={diaO}/>)}</section>
  :undefined;
 return <div className={`gv-diaria-auto gv-diaria-modo-${layout}`} data-testid="gv-diaria-automatica" data-layout={layout}>
  {!umaDisciplina&&<div className="gv-kpis gv-five gv-diaria-kpis">
   <Tile title="DOCUMENTOS PREVISTOS HOJE" value={n(t.documentosHoje)}/>
   <Tile title="HH PREVISTO HOJE" value={v(t.hhHoje)}/>
   <Tile title="PENDENTES DE ONTEM (COMPROVADOS)" value={n(t.pendentes)}/>
   <Tile title="HH PENDENTE DE ONTEM" value={v(t.hhPendente)}/>
   <Tile title="ONTEM SEM COMPROVAÇÃO" value={n(t.semComprovacao)}/>
  </div>}
  <GerenciaEngenhariaItem3
   className={`gv-diaria-ge3${vazio&&umaDisciplina?" gv-diaria-ge3-sem-inferior":""}`}
   itens={vazio?[]:itens} secoes={vazio||carregando?undefined:secoes} grupos={grupos} agruparPorDia={false}
   titulo={umaDisciplina?`DOCUMENTOS DE ${nomeDisciplina}`:"DOCUMENTOS DO DIA"}
   descricaoPeriodo={`Hoje ${diaH} + pendências comprovadas de ontem ${diaO}`}
   carregando={carregando}
   mensagemVazia={<div className="gv-diaria-vazia" data-testid="gv-diaria-vazia"><Empty>{umaDisciplina
     ?<>Nenhuma atividade de {nomeDisciplina} programada para hoje ({diaH}) nem para ontem ({diaO}) neste projeto. Sem pendências de ontem a exibir.
      {foraDaNorma&&<> A disciplina deste slide não está na Norma de Coordenação atual do projeto — revise o slide em Configurações → Telas para TV.</>}</>
     :<>Nenhuma atividade programada para hoje ({diaH}) nem para ontem ({diaO}) neste projeto.{" "}Sem pendências de ontem a exibir.</>}</Empty></div>}
   aviso={!carregando&&semHh>0?<>HH {t.hhHoje===null&&t.hhPendente===null?"não informado":"parcial"}: {semHh} atividade(s) sem horas na programação semanal ("—"); os totais somam apenas os HH disponíveis.</>:undefined}
   resumo={resumo} graficos={!umaDisciplina} baseGraficos="hoje + pendências de ontem comprovadas"
   rodape={<RegraDiariaAutomatica a={a}/>}
  />
 </div>;
}
/** Card por disciplina (faixa inferior, exibição Cards): HOJE e PENDENTES DE ONTEM em documentos × HH; só disciplinas com algo hoje/ontem. */
function CartaoDisciplinaDiaria({d,diaH}:{d:DisciplinaDiaria;diaH:string}){
 return <article className="gv-diaria-card" data-testid="gv-diaria-card">
  <header title={d.nome}>{d.nome}</header>
  <div className="gv-diaria-card-grade">
   <span/><small>DOC.</small><small>HH</small>
   <span>HOJE · {diaH.slice(0,9)}</span><strong>{d.documentosHoje}</strong><strong>{fmt2(d.hhHoje)}</strong>
   <span>PENDENTES DE ONTEM</span><strong>{d.pendentes.length}</strong><strong>{fmt2(d.hhPendente)}</strong>
  </div>
  <p>RECURSOS HOJE {d.hoje.length?recursosDistintos(d.hoje):"—"} · SEM COMPROVAÇÃO {d.semComprovacao.length} · EMITIDOS ATÉ ONTEM {d.emitidosOntem}</p>
 </article>;
}
/** Uma tela por disciplina: indicadores DELA embaixo, em cartões da Gestão à Vista (HOJE | ONTEM). */
function IndicadoresDisciplinaDiaria({d,diaH,diaO}:{d:DisciplinaDiaria;diaH:string;diaO:string}){
 return <div className="gv-diaria-uma" data-testid="gv-diaria-indicadores-disciplina">
  <div><h4>HOJE · {diaH}</h4><div className="gv-kpis gv-three">
   <Tile title="DOCUMENTOS PREVISTOS" value={String(d.documentosHoje)}/><Tile title="HH PREVISTO" value={fmt2(d.hhHoje)}/>
   <Tile title="RECURSOS" value={d.hoje.length?String(recursosDistintos(d.hoje)):"—"}/></div></div>
  <div><h4>ONTEM · {diaO}</h4><div className="gv-kpis gv-four">
   <Tile title="PENDENTES (COMPROVADOS)" value={String(d.pendentes.length)}/><Tile title="HH PENDENTE" value={fmt2(d.hhPendente)}/>
   <Tile title="SEM COMPROVAÇÃO" value={String(d.semComprovacao.length)}/><Tile title="EMITIDOS ATÉ ONTEM" value={String(d.emitidosOntem)}/></div></div>
 </div>;
}
function RegraDiariaAutomatica({a}:{a:AnaliseDiaria}){
 const t=a.totais;
 return <p className="gv-rule">Atualização automática: HOJE = {br(a.hoje)} e ONTEM = {br(a.ontem)} pela data civil da Bahia; a tela muda sozinha na virada do dia.
  {" "}Fonte: programação semanal (data da atividade = Reprogramada › Baseline), somente ontem e hoje; mesma deduplicação da 02
  {a.repetidos>0&&<> ({a.repetidos} registro(s) repetido(s) contado(s) uma vez)</>}. HH = elaboração + verificação + aprovação.
  {" "}Pendente de ontem = documento programado ontem que a LDP oficial (mesmo projeto e código) mostra NÃO emitido; emitido até ontem ou depois
  não é pendência; sem prova (sem código, fora da LDP, cancelado, código repetido ou emitido sem data) fica em "sem comprovação".
  {t.documentosOntem>0&&<> Ontem, no projeto: {t.documentosOntem} documento(s) — {t.emitidosOntem} emitido(s), {t.pendentes} pendente(s), {t.regularizados} emitido(s) depois, {t.semComprovacao} sem comprovação.</>}
  {" "}Título do documento = LDP (Document_Title) do mesmo projeto e código; "—" = sem título na LDP.</p>;
}

/** Data/hora de um DateTime do SQL ("2026-10-19T10:28:21.437" → "19/10/2026 10:28"). */
const dataHora=(v:string|null)=>v?`${br(v.slice(0,10))}${v.length>=16?" "+v.slice(11,16):""}`:"—";
type ModoProgramacao="semana"|"todas";
/**
 * 02 — o que o banco tem além da semana consultada: importados / com data / sem data, período disponível e última
 * sincronização, por projeto (nunca somados num cartão da semana). Atalhos levam à semana REAL das atividades ou ao
 * modo histórico; nenhuma atividade é transposta para a semana atual.
 */
function ProgramacaoImportada({dados,selection,reports,cat,semana,modo,onSemana,onModo}:{dados:ProgramacaoResposta[];selection:PmoSelecionado[];
 reports:FactsReport[];cat:CatalogoDisciplinas;semana:{start:string;end:string};modo:ModoProgramacao;onSemana:(data:string)=>void;onModo:(m:ModoProgramacao)=>void}){
 if(!dados.length)return null;
 const resumo=resumoProgramacao(dados);
 const ultima=ultimaSemanaComProgramacao(dados.flatMap(d=>d.datasComProgramacao.map(x=>x.data)));
 const atual=semanaDoCorte(TODAY());
 const nome=(id:string)=>selection.find(p=>p.id===id)?.name??id;
 const multi=dados.length>1;
 const semDataLista=dados.flatMap(d=>(d.linhasSemData??[]).map(l=>({...l,projectId:d.projectId})));
 const listaDisponivel=dados.every(d=>Array.isArray(d.linhasSemData));
 const indiceTitulos=criarIndiceTitulosDocumentos(reports);
 const naUltima=!!ultima&&modo==="semana"&&semana.start===ultima.start;
 const naAtual=modo==="semana"&&semana.start===atual.start;
 return <section className="gv-prog-importada" data-testid="gv-programacao-importada" aria-label="Programação importada">
  <div className="gv-prog-importada-linha">
   <b>PROGRAMAÇÃO IMPORTADA</b>
   {resumo.projetos.map(p=><span key={p.projectId} data-testid="gv-prog-contadores">
    {multi&&<em>{nome(p.projectId)}: </em>}{p.importados} documento(s) importado(s); {p.comData} com data; {p.semData} sem data
    {" · "}Período disponível: {p.periodo?`${br(p.periodo.inicio)} a ${br(p.periodo.fim)}`:"—"}
    {" · "}Última sincronização: {dataHora(p.ultimaSincronizacao)}{p.arquivoOrigem&&<> · Arquivo: {p.arquivoOrigem}</>}
   </span>)}
  </div>
  <div className="gv-prog-importada-acoes">
   {ultima&&<button type="button" className="gv-link" disabled={naUltima} onClick={()=>onSemana(ultima.start)}>
    Ver última semana com atividades ({br(ultima.start)} a {br(ultima.end)})</button>}
   <button type="button" className="gv-link" disabled={modo==="todas"||resumo.importados===0} onClick={()=>onModo("todas")}>
    Ver todas as atividades importadas ({resumo.comData} com data{resumo.periodo?`, ${br(resumo.periodo.inicio)} a ${br(resumo.periodo.fim)}`:""})</button>
   {!naAtual&&<button type="button" className="gv-link" onClick={()=>onSemana(atual.start)}>Voltar à semana atual ({br(atual.start)} a {br(atual.end)})</button>}
  </div>
  {modo==="todas"&&<p className="gv-warning" role="status">Modo histórico: atividades listadas com a data real da planilha. Não é a programação da semana atual.</p>}
  {resumo.semData>0&&<details className="gv-sem-data" data-testid="gv-sem-data">
   <summary>Documentos sem data ({resumo.semData}) — sem Replanned nem Baseline na programação; não entram em semana alguma</summary>
   {listaDisponivel?<div className="gv-table-scroll"><table>
    <thead><tr>{[...(multi?["PROJETO"]:[]),"DISCIPLINA","CÓDIGO DO DOCUMENTO","TÍTULO DO DOCUMENTO","DESCRIÇÃO NA PROGRAMAÇÃO","FASE","SITUAÇÃO","LINHA EXCEL"].map(x=><th key={x}>{x}</th>)}</tr></thead>
    <tbody>{semDataLista.map(l=><tr key={`${l.projectId}:${l.idRegistro}`}>
     {multi&&<td>{nome(l.projectId)}</td>}
     <td>{rotulo(cat,classificarDocumento(cat,{projectId:l.projectId,disciplinaNorma:l.disciplinaNorma,disciplinaCanonica:l.disciplinaCanonica,siglaDoc3:l.siglaDoc3,codigo:l.documento},"PROGRAMACAO").chave)}</td>
     <td>{l.documento??"—"}</td><td>{obterTituloDocumentoOficial(indiceTitulos,l.projectId,l.documento)??"—"}</td>
     <td>{l.tarefa??"—"}</td><td>{l.fase??"—"}</td><td>{l.status??"—"}</td><td>{l.linhaExcel??"—"}</td></tr>)}</tbody>
   </table></div>:<p>A lista detalhada exige o backend publicado em 19/10/2026; a contagem acima vem do banco.</p>}
  </details>}
 </section>;
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
  {false&&type2&&nTipo2<reports.length&&<p className="gv-rule">Totais = todos os {reports.length} projetos. Fases 30/60/90 = somente os {nTipo2} projeto(s) LDP Tipo 2 da seleção (LDP Tipo 1 não tem gates).</p>}
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
  {false&&type2&&<div className="gv-extra-gates">
   <PhaseSupplement title="HH PREVISTO POR FASE" values={faseHhPrevisto(reports)}/>
   <PhaseSupplement title="DOCUMENTOS POR FASE" values={faseTotalDocumentos(reports)}/>
   <PhaseSupplement title="EMITIDOS POR FASE" values={faseContagem(ldp.projetos,"emitidos")}/>
   <PhaseSupplement title="CANCELADOS POR FASE" values={faseContagem(ldp.projetos,"cancelados")}/>
   <PhaseSupplement title="A EMITIR POR FASE" values={faseContagem(ldp.projetos,"aEmitir")}/>
   <PhaseSupplement title="ATRASADOS POR FASE" values={prazo.atraso}/>
   <PhaseSupplement title="NO PRAZO POR FASE" values={prazo.noPrazo}/>
  </div>}
  {false&&type2&&<p className="gv-rule">HH realizado não é exibido por fase: o Timesheet não identifica gate e nada é rateado artificialmente.</p>}
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
 // carregando começa em `ativo`: a 01/02 nunca exibem "sem programação" antes da primeira consulta.
 const [estado,setEstado]=useState<{dados:ProgramacaoResposta[];erro:string|null;falhas:FalhaProjeto[];carregando:boolean}>({dados:[],erro:null,falhas:[],carregando:ativo});
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

/**
 * Modo histórico: para cada projeto, todas as linhas com data no período disponível (datasComProgramacao da consulta
 * semanal), em janelas de até 56 dias (o endpoint aceita até 62). Projeto sem nenhuma data → nenhuma linha, resumo preservado.
 */
function useProgramacaoCompleta(selection:PmoSelecionado[],base:ProgramacaoResposta[],ativo:boolean){
 const chave=JSON.stringify(base.map(d=>{const p=resumoProgramacao([d]).projetos[0];return [d.projectId,p.periodo?.inicio??null,p.periodo?.fim??null,d.ultimaSincronizacao];}));
 const [estado,setEstado]=useState<{dados:ProgramacaoResposta[];falhas:FalhaProjeto[];carregando:boolean}>({dados:[],falhas:[],carregando:false});
 const [tentativa,setTentativa]=useState(0);
 useEffect(()=>{
  if(!ativo)return;
  const ctrl=new AbortController();
  setEstado(s=>({...s,carregando:true}));
  Promise.allSettled(base.map(d=>{
   const p=resumoProgramacao([d]).projetos[0].periodo;
   if(!p)return Promise.resolve({...d,linhas:[]});
   return Promise.all(janelasDoPeriodo(p.inicio,p.fim).map(j=>getJson<ProgramacaoResposta>("/api/reports/gestao-vista-jamile/programacao?"+
    new URLSearchParams({projectId:d.projectId,inicio:j.inicio,fim:j.fim}),ctrl.signal))).then(rs=>mesclarJanelas(rs)??{...d,linhas:[]});
  })).then(rs=>{if(ctrl.signal.aborted)return;
   const falhas=rs.flatMap((r,i):FalhaProjeto[]=>r.status==="rejected"?[{projectId:base[i].projectId,
    projectName:selection.find(p=>p.id===base[i].projectId)?.name??base[i].projectId,message:r.reason instanceof Error?r.reason.message:"Indisponível"}]:[]);
   setEstado({dados:rs.flatMap(r=>r.status==="fulfilled"?[r.value]:[]),falhas,carregando:false});});
  return()=>ctrl.abort();
  // chave resume base (projeto + período + sincronização); base muda de identidade a cada render.
  // eslint-disable-next-line react-hooks/exhaustive-deps
 },[chave,ativo,tentativa]);
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

type PropsGestaoVista={selection:PmoSelecionado[];screen:JamileScreen;
 /** Só "programacao-diaria": dia (YYYY-MM-DD) salvo no slide da TV, ou "AUTO" (hoje + pendências de ontem). Ausente/inválido = aviso. */
 dataReferencia?:string|null;
 /** Só no slide AUTO: exibição (ausente = padrão) e, em "disciplina", a chave/rótulo da disciplina salvos no slide. */
 layoutDiario?:string|null;disciplina?:string|null;disciplinaNome?:string|null};
export default function GestaoVistaJamilePage(props:PropsGestaoVista){
 return props.screen==="programacao-diaria"&&dataReferenciaAutomatica(props.dataReferencia)
  ?<DiariaAutomaticaNoDia {...props}/>:<GestaoVistaJamileConteudo {...props} hojeAuto={null}/>;
}
/** Slide AUTO: a data de hoje vira a key — na virada do dia a tela remonta e TODOS os dados (programação e LDP) são buscados de novo. */
function DiariaAutomaticaNoDia(props:PropsGestaoVista){
 const hoje=useHojeBahia();
 return <GestaoVistaJamileConteudo key={hoje} {...props} hojeAuto={hoje}/>;
}
function GestaoVistaJamileConteudo({selection,screen,dataReferencia=null,hojeAuto,layoutDiario=null,disciplina=null,disciplinaNome=null}:PropsGestaoVista&{
 /** Só no slide AUTO: hoje (Bahia), vindo de DiariaAutomaticaNoDia; null em qualquer outra tela. */
 hojeAuto:string|null}){
 const {reports,falhas,loading,error,retry}=usePmoReports("gestao-vista",{hub:"",project:"",selection});
 const ldp=useLdpPorProjeto(selection);
 const ids=useMemo(()=>selection.map(x=>x.id),[selection]);
 const normas=useNormas(ids);
 const [cutoff,setCutoff]=useState(TODAY);
 // Programação Diária (TV): o dia vem do slide; sem data válida nada é consultado (nunca cai em "hoje").
 const diaria=screen==="programacao-diaria";
 // Diária AUTOMÁTICA: janela [ontem, hoje] (cruza a semana no domingo numa consulta só); dia fixo continua como antes.
 const auto=diaria&&hojeAuto!==null?janelaDiariaAutomatica(hojeAuto):null;
 const diaFixo=diaria&&!auto&&dataReferenciaValida(dataReferencia)?dataReferencia:null;
 // Exibição do slide AUTO: desconhecida ou "disciplina" sem disciplina = aviso explícito (nunca outra exibição no lugar).
 const layoutAuto:LayoutDiario|null=!auto?null:(layoutDiario??"padrao")==="padrao"?"padrao":layoutDiario==="cards"?"cards"
  :layoutDiario==="disciplina"&&disciplina?.trim()?"disciplina":null;
 const valid=diaFixo??(/^\d{4}-\d{2}-\d{2}$/.test(cutoff)?cutoff:TODAY()),week=semanaDoCorte(valid);
 // 01 e 02: MESMA fonte — programação semanal oficial (domingo–sábado) de cada projeto; na 02, filtro opcional de um dia.
 // Diária: o MESMO endpoint semanal, filtrado exatamente no dia do slide.
 const prog=useProgramacao(selection,week.start,week.end,screen==="programacao"||screen==="visao-geral"||diaFixo!==null);
 // Diária AUTOMÁTICA: consulta PRÓPRIA (mesmo endpoint) na janela [ontem, hoje]; 01/02/dia fixo seguem com a consulta acima.
 const progAuto=useProgramacao(selection,auto?.inicio??week.start,auto?.fim??week.end,auto!==null);
 const [diaFiltro,setDiaFiltro]=useState<string|null>(null);
 // "todas" = modo histórico (todas as atividades importadas, data real); qualquer navegação de semana volta ao modo semana.
 const [,setModoProg]=useState<ModoProgramacao>("semana");
 // A Programação é exclusivamente semanal; o histórico técnico não altera a visualização operacional.
 const historico=false;
 const todas=useProgramacaoCompleta(selection,prog.dados,historico);
 const dadosProg=historico?todas.dados:prog.dados;
 const periodoHist=resumoProgramacao(prog.dados).periodo;
 const semanaProg=historico&&periodoHist?{start:periodoHist.inicio,end:periodoHist.fim}:week;
 const dia=!historico&&diaFiltro&&diaFiltro>=week.start&&diaFiltro<=week.end?diaFiltro:null;
 const irParaSemana=(data:string)=>{setCutoff(data);setDiaFiltro(null);setModoProg("semana");};
 // Consolidado: TODOS os projetos do slide participam; qualquer fonte que falhe em um deles bloqueia os números da tela.
 const consolidado=selection.length>1;
 // Multiprojeto: padrão = PROJETO → FASE → disciplina (nada misturado); "consolidado" = telas originais somadas, com o
 // Normalizador de Siglas unindo siglas diferentes de mesmo nome canônico PT-BR (só aqui; 1 projeto nunca normaliza).
 const [visaoMulti,setVisaoMulti]=useState<VisaoMultiprojeto>("consolidado");
 const porProjetoFase=false; // Gestão à Vista não oferece visão por projeto/fase em nenhuma tela.
 const siglas=useRegrasSiglasAtivas(consolidado);
 const normalizador=useMemo(()=>consolidado?criarNormalizador(siglas.regras):null,[consolidado,siglas.regras]);
 const cat=useMemo(()=>criarCatalogo(normas.normas,selection,normalizador),[normas.normas,selection,normalizador]);
 const name=selection.map(x=>x.name).join(" · ");
 const nomeDisciplinaSlide=disciplina&&(cat.rotulos.has(disciplina)||disciplina===SEM_DISCIPLINA)?rotulo(cat,disciplina):disciplinaNome?.trim()||disciplina||"";
 const falhasTela:FalhaProjeto[]=[
  ...falhas,
  ...(screen==="programacao"&&prog.falhas.length<selection.length?prog.falhas:[]),
  // 01: sem fallback — qualquer projeto sem programação carregada bloqueia os números (nunca total parcial como total).
  ...(screen==="visao-geral"?prog.falhas:[]),
  // Diária: um projeto, sem fallback para a LDP — falha mostra o erro e o botão de nova tentativa.
  ...(diaria?prog.falhas:[]),
  ...(auto?progAuto.falhas:[]),
  ...(historico?todas.falhas:[]),
  ...((screen==="documentacao"||screen==="indicadores")&&ldp.erro&&!ldp.carregando
   ?[{projectId:"__ldp__",projectName:"Aba LDP (fases 30/60/90)",message:ldp.erro}]:[]),
 ];
 // A Programação Semanal é exclusivamente a semana por disciplina: sem painel de auditoria da importação.
 // O detalhamento técnico continua no contrato de dados, sem ocupar a interface operacional.
 const exibirDiagnosticoImportacao=false;
 // No player de TV o menu só salta entre slides DA apresentação (nunca sai para /controle-producao); fora dele, rotas normais.
 const navTv=useNavegacaoTv();
 const importada=screen==="programacao"&&!prog.carregando&&!prog.erro
  ?<ProgramacaoImportada dados={prog.dados} selection={selection} reports={reports as FactsReport[]} cat={cat} semana={week} modo={historico?"todas":"semana"}
    onSemana={irParaSemana} onModo={setModoProg}/>:null;
 let content:ReactNode;
 if(auto&&!layoutAuto)content=<ReportState title="Programação Diária automática com exibição inválida" error>
  {layoutDiario==="disciplina"?"Este slide é \"uma tela por disciplina\", mas não tem a disciplina.":`Exibição "${layoutDiario}" desconhecida.`} Corrija em
  Configurações → Configuração de Telas para TV.</ReportState>;
 else if(diaria&&!diaFixo&&!auto)content=<ReportState title="Programação Diária sem data de referência" error>
  {dataReferencia?`A data "${dataReferencia}" deste slide não é uma data válida (AAAA-MM-DD).`:"Este slide não tem data de referência."} Configure-a em
  Configurações → Configuração de Telas para TV. A data de hoje nunca é usada no lugar.</ReportState>;
 else if(loading||normas.carregando||siglas.carregando)content=<ReportState title="Carregando Gestão à Vista…">Consultando os dados do projeto.</ReportState>;
 else if(error)content=<ReportState title={error} error><button className="gv-link" onClick={retry}>Tentar novamente</button></ReportState>;
 else if(falhasTela.length>0)content=<ConsolidacaoIncompleta falhas={falhasTela} total={selection.length} onRetry={()=>{retry();prog.retry();progAuto.retry();todas.retry();}}/>;
 else if(porProjetoFase){
  const fr=reports as FactsReport[];
  const titulo=`${SUBTITLE[screen]} — POR PROJETO E FASE`;
  content=<>
   {screen==="visao-geral"&&<ProjetoFaseAgrupamento idBase="pf-entregas" titulo={`ENTREGAS DA SEMANA ${br(week.start)} A ${br(week.end)} — POR PROJETO E FASE`} agrupamento={agruparEntregas(fr,cat,valid)}/>}
   {/* Programação Semanal nunca usa agrupamento por projeto/fase. */}
   {screen==="documentacao"&&<ProjetoFaseAgrupamento idBase="pf-documentos" titulo={titulo} agrupamento={agruparDocumentacao(fr,cat)}/>}
   {screen==="indicadores"&&<ProjetoFaseAgrupamento idBase="pf-indicadores" titulo={titulo} agrupamento={agruparIndicadores(fr,cat)}/>}
   <DiagnosticoNorma normas={normas.normas} erro={normas.erro}/>
  </>;
 }
 else{
  const fr=reports as FactsReport[];
  content=<>
   {screen==="visao-geral"&&<Overview reports={fr} dados={prog.dados} selection={selection} semana={week} cat={cat} carregando={prog.carregando} onSemana={setCutoff}/>}
   {screen==="programacao"&&exibirDiagnosticoImportacao&&importada}
   {screen==="programacao"&&<Programming reports={fr} semana={semanaProg} dia={dia} cat={cat} dados={dadosProg} erro={prog.erro}
    carregando={prog.carregando||(historico&&todas.carregando)} onSemana={irParaSemana} historico={historico}/>}
   {diaria&&diaFixo&&<Programming reports={fr} semana={week} dia={diaFixo} cat={cat} dados={prog.dados} erro={null}
    carregando={prog.carregando} onSemana={()=>undefined} diario/>}
   {auto&&layoutAuto&&<DiariaAutomatica reports={fr} dados={progAuto.dados} cat={cat} hoje={auto.hoje} carregando={progAuto.carregando}
    layout={layoutAuto} disciplina={disciplina} disciplinaNome={disciplinaNome}/>}
   {screen==="documentacao"&&<Documentation reports={fr} ldp={ldp} cat={cat}/>}
   {screen==="indicadores"&&<Indicators reports={fr} cutoff={valid} ldp={ldp} cat={cat}/>}
   {consolidado&&<p className="gv-rule" data-testid="gv-normalizador">{siglas.aviso??(normalizador?.ativo
    ?`Consolidado: disciplinas agregadas pelo nome canônico PT-BR do Normalizador de Siglas (${siglas.regras.length} regra(s) ativa(s)); sem regra, vale o nome original.`
    :"Consolidado: nenhuma regra ativa no Normalizador de Siglas — disciplinas pelo nome original.")}</p>}
   <DiagnosticoNorma normas={normas.normas} erro={normas.erro}/>
  </>;
 }
 // Só a 01 ganha sidebar mais estreita e, dentro do player da TV, a altura toda abaixo do cabeçalho do player (gestaoVistaJamile.css).
 return <div className={"gv-shell"+(screen==="visao-geral"?" gv-shell-vg"+(navTv?" gv-shell-vg-tv":""):"")} data-testid={"gestao-a-vista-"+screen}>
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
    <div className="gv-header-text"><b>GERÊNCIA DA ENGENHARIA</b><h1>{SUBTITLE[screen]}</h1><small title={consolidado?selection.map(x=>x.name).join("\n"):name} data-testid="gv-escopo">{layoutAuto==="disciplina"?`${name} · DISCIPLINA: ${nomeDisciplinaSlide}`:consolidado?(screen==="programacao"?`POR DISCIPLINA — ${selection.length} PROJETOS`:`${porProjetoFase?"POR PROJETO E FASE":"CONSOLIDADO"} — ${selection.length} PROJETOS: ${name}`):name}</small>
     {false&&consolidado&&<SeletorVisaoMultiprojeto valor={visaoMulti} onChange={setVisaoMulti}/>}</div>
    {screen==="programacao"&&<div className="gv-date gv-date-semana">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     <div><small>SEMANA</small>
      <span className="gv-date-input">
       <button type="button" aria-label="Semana anterior" title="Semana anterior" onClick={()=>irParaSemana(addDias(week.start,-7))}><ChevronLeft size={13}/></button>
       <b data-testid="gv-semana">{br(week.start)} A {br(week.end)}</b>
       <button type="button" aria-label="Próxima semana" title="Próxima semana" onClick={()=>irParaSemana(addDias(week.start,7))}><ChevronRight size={13}/></button>
      </span></div>
     <label><small>DIA (OPCIONAL)</small>
      <select aria-label="Filtrar um dia da semana" value={dia??""} disabled={historico} onChange={e=>setDiaFiltro(e.target.value||null)}>
       <option value="">SEMANA COMPLETA</option>
       {atividadesPorDia(deduplicarProgramacao(prog.dados.flatMap(d=>d.linhas.map(l=>({...l,projectId:d.projectId})))).linhas,week.start)
        .map(x=><option key={x.data} value={x.data}>{x.dia} {brCurta(x.data).slice(0,5)} ({x.quantidade})</option>)}
      </select></label>
     <label><small>IR PARA DATA</small>
      <span className="gv-date-input"><input type="date" value={valid} onChange={e=>e.target.value&&irParaSemana(e.target.value)}/></span></label>
    </div>}
    {diaria&&<div className="gv-date" aria-label="Dia de referência da programação diária">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     <div><small>{auto?"AUTOMÁTICO · HOJE + PENDÊNCIAS DE ONTEM":"DIA DE REFERÊNCIA"}</small>
      <span className="gv-date-input"><b data-testid="gv-dia-referencia">{auto?`HOJE ${diaDaSemanaCurto(auto.hoje)} ${br(auto.hoje)} · ONTEM ${diaDaSemanaCurto(auto.ontem)} ${br(auto.ontem)}`
       :diaFixo?`${diaDaSemanaCurto(diaFixo)} ${br(diaFixo)}`:"SEM DATA"}</b></span></div>
    </div>}
    {screen==="visao-geral"&&<div className="gv-date gv-date-semana" aria-label="Semana de referência da visão geral">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     <div><small>SEMANA DE REFERÊNCIA</small>
      <span className="gv-date-input">
       <button type="button" aria-label="Semana anterior" title="Semana anterior" onClick={()=>setCutoff(addDias(week.start,-7))}><ChevronLeft size={13}/></button>
       <b data-testid="gv-semana">{br(week.start)} A {br(week.end)}</b>
       <button type="button" aria-label="Próxima semana" title="Próxima semana" onClick={()=>setCutoff(addDias(week.start,7))}><ChevronRight size={13}/></button>
      </span>
     </div>
     <button type="button" className="gv-link" disabled={week.start===semanaDoCorte(TODAY()).start}
      onClick={()=>setCutoff(TODAY())}>Semana atual</button>
    </div>}
    {screen==="indicadores"&&<div className="gv-date">
     <CalendarDays className="gv-date-icon" aria-hidden/>
     <label><small>DATA DE CORTE</small>
      <span className="gv-date-input">
       <input type="date" value={valid} onChange={e=>setCutoff(e.target.value)}/>
      </span></label>
    </div>}
   </header>
   <main className={"gv-content gv-content-"+screen+(diaria?" gv-content-programacao":"")}>{content}</main>
  </div>
 </div>;
}
