import type { FactsReport } from "@/components/pmo/pmoFacts";
import type { PmoDocument } from "@/components/pmo/pmoData";
import { consumption, projectFacts } from "@/components/pmo/pmoFacts";
import { somaConhecida } from "@/components/pmo/pmoMultiCore";
import type { LdpProjeto } from "./resumoLogic";

export const FASES = [30,60,90] as const;
export type Fase = typeof FASES[number];
export type PhaseValues = Record<Fase,number|null>;
export const emptyPhases=():PhaseValues=>({30:null,60:null,90:null});
const nonNullSum=(a:(number|null|undefined)[])=>{const v=a.filter((x):x is number=>typeof x==="number"&&Number.isFinite(x));return v.length?v.reduce((s,n)=>s+n,0):null;};
export const pct=(a:number|null|undefined,b:number|null|undefined)=>typeof a==="number"&&typeof b==="number"&&b>0?a/b*100:null;
const normalize=(s:string)=>s.normalize("NFD").replace(/[̀-ͯ]/g,"").replace(/\s+/g," ").trim().toUpperCase();

/* ------------------------------------------------------------------------------------------------
 * FASES 30/60/90 (LDP Tipo 2) — somente o que o backend já entrega; nada é rateado.
 * ---------------------------------------------------------------------------------------------- */

/** Status por gate: somente as células que o backend da própria aba LDP fornece. */
export function faseContagem(projetos:readonly LdpProjeto[],row:string):PhaseValues{
 const out=emptyPhases();
 for(const gate of FASES){
   const values=projetos.filter(p=>p.tipoLdp===2).flatMap(p=>p.celulas
     .filter(c=>Number(c.column)===gate&&normalize(c.row)===normalize(row)).map(c=>c.value));
   out[gate]=nonNullSum(values);
 }
 return out;
}
export function faseTotalDocumentos(reports:readonly FactsReport[]):PhaseValues {
 const out=emptyPhases();
 for(const gate of FASES){
   const docs=reports.filter(r=>r.tipoLdp===2).flatMap(r=>r.documentos
     .filter(d=>!d.isActivity&&d.gates?.some(g=>g.percentual===gate&&g.ativo)));
   out[gate]=reports.some(r=>r.tipoLdp===2)?docs.length:null;
 }
 return out;
}
export function faseHhPrevisto(reports:readonly FactsReport[],filter?:(doc:PmoDocument)=>boolean):PhaseValues{
 const out=emptyPhases();
 for(const gate of FASES){
  const values=reports.filter(r=>r.tipoLdp===2).flatMap(r=>r.documentos.filter(d=>!d.isActivity&&(!filter||filter(d)))
   .flatMap(d=>d.gates?.filter(g=>g.percentual===gate&&g.ativo&&!g.cancelado).map(g=>g.esforcoHoras)??[]));
  out[gate]=nonNullSum(values);
 }
 return out;
}
/** Sem gate nos lançamentos originais do Timesheet: nunca ratear automaticamente. */
export function faseHhRealizado():PhaseValues{return emptyPhases();}
export function dataISO(v:string|null|undefined){return v?.slice(0,10)??null;}
const dataValida=(v:string|null|undefined)=>{const d=dataISO(v);return d&&d>="2000"?d:null;};

/** Prazo exclusivamente por gate ativo: mesma prioridade Replanned > Baseline da LDP.
 * Cancelado e Emitido saem do cálculo de pendência. Sem data válida não inventar atraso.
 */
export function fasePrazos(reports:readonly FactsReport[],cutoff:string){
 const atraso=emptyPhases(),noPrazo=emptyPhases();
 for(const g of FASES){
   const gates=reports.filter(r=>r.tipoLdp===2).flatMap(r=>r.documentos
     .filter(d=>!d.isActivity).flatMap(d=>d.gates?.filter(x=>x.percentual===g&&x.ativo&&!x.cancelado&&!x.emitido)??[]));
   const valid=gates.filter(x=>!!dataValida(x.reprogramada??x.baseline));
   atraso[g]=valid.length?valid.filter(x=>dataValida(x.reprogramada??x.baseline)!<cutoff).length:null;
   noPrazo[g]=valid.length?valid.filter(x=>dataValida(x.reprogramada??x.baseline)!>=cutoff).length:null;
 }
 return {atraso,noPrazo};
}

/* ------------------------------------------------------------------------------------------------
 * SEMANA — regra oficial do backend (PmoAvancoRules.SemanaVigente / ProductionDashboards.InicioSemana):
 * domingo a sábado.
 * ---------------------------------------------------------------------------------------------- */
export function addDias(date:string,dias:number){const d=new Date(date+"T12:00:00Z");d.setUTCDate(d.getUTCDate()+dias);return d.toISOString().slice(0,10);}
export function semanaDoCorte(date:string){
 const d=new Date(date+"T12:00:00Z");if(Number.isNaN(d.valueOf()))return {start:date,end:date};
 const start=addDias(date,-d.getUTCDay());
 return {start,end:addDias(start,6)};
}
export const diasDaSemana=(start:string)=>Array.from({length:7},(_,i)=>addDias(start,i));

/* ------------------------------------------------------------------------------------------------
 * DISCIPLINA — DOC3 → sigla → Norma de Coordenação do projeto → nome da Norma.
 * O backend resolve o nome (CoordinationDisciplineResolver) e devolve, por sigla, o canônico global
 * que agrupa o REALIZADO oficial. Aqui só se AGRUPA e ROTULA; nenhum valor é recalculado ou escondido.
 * ---------------------------------------------------------------------------------------------- */
export interface NormaSigla{sigla:string;disciplina:string;canonica:string|null}
export interface NormaDiagnostico{tipo:string;mensagem:string;disciplinas:string[];siglas:string[]}
export interface DisciplinasNorma{projectId:string;normaId:number|null;normaNome:string|null;siglas:NormaSigla[];diagnosticos:NormaDiagnostico[]}

export const SEM_DISCIPLINA="__SEM_DISCIPLINA__";
export const SEM_DISCIPLINA_ROTULO="SEM DISCIPLINA";
/** Mesmo rótulo devolvido pelo backend (CoordinationDisciplineResolver.SemDisciplinaLabel). */
const SEM_DISCIPLINA_BACKEND=["SEM DISCIPLINA","__PMO_NULL__",""];

export interface CatalogoDisciplinas{
 /**
  * Projetos da análise (>1 = consolidado). No consolidado, só a disciplina PADRÃO oficial (canônico que o backend resolve
  * pela Norma de cada projeto) une projetos; nome da Norma sem padrão ou disciplina fora da Norma fica separado POR
  * PROJETO — nunca se força equivalência por semelhança de texto entre Normas diferentes.
  */
 projetos:Map<string,string>;
 /** projectId → (nome da Norma normalizado → chave do grupo) */
 porNome:Map<string,Map<string,string>>;
 /** projectId → (canônico → chave do grupo) — liga o realizado oficial ao nome da Norma. */
 porCanonica:Map<string,Map<string,string>>;
 rotulos:Map<string,Set<string>>;
 normas:DisciplinasNorma[];
}
/** Código curto do projeto ("CHS001 - ETHANOL PLANT" → "CHS001") para rotular disciplinas separadas por projeto. */
const codigoDoProjeto=(nome:string|undefined,id:string)=>{const m=/^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]/i.exec(nome??"");return m?m[1].toUpperCase():(nome?.trim()||id);};
const consolidado=(cat:Pick<CatalogoDisciplinas,"projetos">)=>cat.projetos.size>1;
/** Chave sem disciplina padrão oficial: no consolidado ganha o projeto (escopo da Norma daquele projeto). */
const chaveLocal=(cat:Pick<CatalogoDisciplinas,"projetos">,prefixo:"N:"|"X:",projectId:string,nome:string)=>
 consolidado(cat)?`${prefixo}${projectId}|${normalize(nome)}`:prefixo+normalize(nome);
const rotuloLocal=(cat:Pick<CatalogoDisciplinas,"projetos">,projectId:string,nome:string)=>
 consolidado(cat)?`${nome.trim()} (${codigoDoProjeto(cat.projetos.get(projectId),projectId)})`:nome.trim();

/** projetos = projetos da análise (id + nome); com mais de um, o catálogo trabalha no modo consolidado. */
export function criarCatalogo(normas:readonly DisciplinasNorma[],projetos:readonly {id:string;name?:string}[]=[]):CatalogoDisciplinas{
 const base={projetos:new Map(projetos.map(p=>[p.id,p.name??p.id]))};
 const porNome=new Map<string,Map<string,string>>(),porCanonica=new Map<string,Map<string,string>>(),rotulos=new Map<string,Set<string>>();
 for(const n of normas){
  const nomes=new Map<string,string>(),canonicas=new Map<string,string>();
  for(const s of n.siglas){
   const k=s.canonica?"C:"+normalize(s.canonica):chaveLocal(base,"N:",n.projectId,s.disciplina);
   nomes.set(normalize(s.disciplina),k);
   if(s.canonica)canonicas.set(normalize(s.canonica),k);
   if(!rotulos.has(k))rotulos.set(k,new Set());
   rotulos.get(k)!.add(s.canonica?s.disciplina.trim():rotuloLocal(base,n.projectId,s.disciplina));
  }
  porNome.set(n.projectId,nomes);porCanonica.set(n.projectId,canonicas);
 }
 return {...base,porNome,porCanonica,rotulos,normas:[...normas]};
}
export interface Classificacao{chave:string;motivo:string|null}
/** Documento da LDP: o nome da Norma já vem resolvido do backend pelo DOC3 (`disciplinaNorma`). */
export function classificarDocumento(cat:CatalogoDisciplinas,doc:{projectId:string;disciplinaNorma?:string|null;disciplinaCanonica?:string|null;siglaDoc3?:string|null;fonteDisciplinaNorma?:string|null;codigo:string|null}):Classificacao{
 const nome=doc.disciplinaNorma?.trim();
 if(nome){
  // Catálogo da Norma indisponível para o projeto: usa o canônico OFICIAL que o backend resolveu para o mesmo DOC3
  // (o mesmo que classifica o realizado) — previsto e realizado caem na mesma linha, sem comparação textual.
  const canonica=doc.disciplinaCanonica?.trim();
  if(!cat.porNome.has(doc.projectId)&&canonica){
   const chave="X:"+normalize(canonica);
   if(!cat.rotulos.has(chave))cat.rotulos.set(chave,new Set([nome]));
   return {chave,motivo:null};
  }
  const chave=cat.porNome.get(doc.projectId)?.get(normalize(nome))??chaveLocal(cat,"N:",doc.projectId,nome);
  if(!cat.rotulos.has(chave))cat.rotulos.set(chave,new Set([rotuloLocal(cat,doc.projectId,nome)]));
  return {chave,motivo:null};
 }
 const sigla=doc.siglaDoc3?.trim();
 return {chave:SEM_DISCIPLINA,motivo:!doc.codigo?.trim()?"Documento sem código":!sigla?"Código sem DOC3":
  cat.normas.some(n=>n.projectId===doc.projectId&&n.normaId!==null)?`DOC3 '${sigla}' não cadastrado na Norma`:"Projeto sem Norma de Coordenação"};
}
/** Realizado oficial: o backend agrupa por canônico global da sigla. Volta ao nome da Norma do mesmo projeto. */
export function classificarRealizado(cat:CatalogoDisciplinas,projectId:string,disciplina:string|null):Classificacao{
 const nome=(disciplina??"").trim();
 if(SEM_DISCIPLINA_BACKEND.includes(normalize(nome)))return {chave:SEM_DISCIPLINA,motivo:"Lançamento sem DOC3 cadastrado na Norma"};
 const k=normalize(nome);
 const chave=cat.porCanonica.get(projectId)?.get(k)??cat.porNome.get(projectId)?.get(k);
 if(chave)return {chave,motivo:null};
 // Disciplina informada fora da Norma (AT manual, medição histórica): preservada com o nome da origem.
 const fora=chaveLocal(cat,"X:",projectId,nome);
 if(!cat.rotulos.has(fora))cat.rotulos.set(fora,new Set([rotuloLocal(cat,projectId,nome)]));
 return {chave:fora,motivo:"Disciplina informada fora da Norma (AT manual ou medição histórica)"};
}
export function rotulo(cat:CatalogoDisciplinas,chave:string){
 if(chave===SEM_DISCIPLINA)return SEM_DISCIPLINA_ROTULO;
 const nomes=[...(cat.rotulos.get(chave)??[])];
 return nomes.length?nomes.sort((a,b)=>a.localeCompare(b,"pt-BR")).join(" / "):chave.slice(2).replace(/^[^|]*\|/,"");
}

/* ------------------------------------------------------------------------------------------------
 * PERFORMANCE POR DISCIPLINA (tela 04) — previsto por documento da LDP, realizado oficial do backend.
 * Linhas somam exatamente os totais gerenciais; qualquer diferença vira linha explícita.
 * ---------------------------------------------------------------------------------------------- */
export interface LinhaPerformance{chave:string;disciplina:string;hhPrevisto:number|null;hhRealizado:number|null;
 custoPrevisto:number|null;custoRealizado:number|null;consumoHh:number|null;consumoCusto:number|null;documentos:number;
 diagnosticos:string[];reconciliacao?:boolean}
const custoRealizadoOficial=(r:FactsReport)=>r.hhRealizado?.custoOficial===true;
const TOLERANCIA=0.005;

export function performancePorDisciplina(reports:readonly FactsReport[],cat:CatalogoDisciplinas){
 const linhas=new Map<string,{hp:(number|null)[];hr:(number|null)[];cp:(number|null)[];cr:(number|null)[];docs:number;diag:Set<string>}>();
 const linha=(k:string)=>{if(!linhas.has(k))linhas.set(k,{hp:[],hr:[],cp:[],cr:[],docs:0,diag:new Set()});return linhas.get(k)!;};
 for(const r of reports){
  for(const d of r.documentos){
   const c=classificarDocumento(cat,d),l=linha(c.chave);
   l.hp.push(d.hhPrevisto);l.cp.push(d.custoPrevisto);if(!d.isActivity)l.docs++;
   if(c.motivo)l.diag.add(c.motivo);
  }
  const hh=r.hhRealizado?r.hhRealizado.porDisciplina.map(x=>({d:x.disciplina,hh:x.hh,custo:x.custo??null}))
   :r.actual?.byDiscipline.map(x=>({d:x.discipline,hh:x.values.hh,custo:x.values.knownCalculatedCostSubtotal}))??[];
  const custoLegado=!custoRealizadoOficial(r)&&r.hhRealizado?r.actual?.byDiscipline.map(x=>({d:x.discipline,custo:x.values.knownCalculatedCostSubtotal}))??[]:null;
  for(const x of hh){
   const c=classificarRealizado(cat,r.projectId,x.d),l=linha(c.chave);
   l.hr.push(x.hh);if(!custoLegado)l.cr.push(x.custo);
   if(c.motivo)l.diag.add(c.motivo);
  }
  for(const x of custoLegado??[]){const c=classificarRealizado(cat,r.projectId,x.d);linha(c.chave).cr.push(x.custo);}
 }
 const facts=projectFacts([...reports]);
 const rows:LinhaPerformance[]=[...linhas].map(([chave,l])=>{
  const hp=somaConhecida(l.hp),hr=somaConhecida(l.hr),cp=somaConhecida(l.cp),cr=somaConhecida(l.cr);
  return {chave,disciplina:rotulo(cat,chave),hhPrevisto:hp,hhRealizado:hr,custoPrevisto:cp,custoRealizado:cr,
   consumoHh:consumption(hr,hp),consumoCusto:consumption(cr,cp),documentos:l.docs,diagnosticos:[...l.diag]};
 }).sort((a,b)=>(a.chave===SEM_DISCIPLINA?1:0)-(b.chave===SEM_DISCIPLINA?1:0)||a.disciplina.localeCompare(b.disciplina,"pt-BR"));
 const soma=(f:(x:LinhaPerformance)=>number|null)=>somaConhecida(rows.map(f));
 const dif=(total:number|null,parcial:number|null)=>total!==null&&parcial!==null&&Math.abs(total-parcial)>TOLERANCIA?total-parcial:null;
 const ajuste={hp:dif(facts.cards.HH_Previsto,soma(x=>x.hhPrevisto)),hr:dif(facts.cards.HH_Realizado,soma(x=>x.hhRealizado)),
  cp:dif(facts.cards.Custo_Previsto,soma(x=>x.custoPrevisto)),cr:dif(facts.cards.Custo_Realizado,soma(x=>x.custoRealizado))};
 if(ajuste.hp!==null||ajuste.hr!==null||ajuste.cp!==null||ajuste.cr!==null)rows.push({chave:"__RECONCILIACAO__",
  disciplina:"DIFERENÇA DE RECONCILIAÇÃO",hhPrevisto:ajuste.hp,hhRealizado:ajuste.hr,custoPrevisto:ajuste.cp,custoRealizado:ajuste.cr,
  consumoHh:null,consumoCusto:null,documentos:0,reconciliacao:true,
  diagnosticos:["Diferença entre o total gerencial oficial e a soma por documento/disciplina; exibida para manter o total auditável."]});
 return {rows,cards:facts.cards};
}

/* ------------------------------------------------------------------------------------------------
 * PROGRAMAÇÃO DA SEMANA (tela 01).
 * Tipo 1: documento (sem AT, sem cancelado) com data-alvo oficial do backend (Reprogramada LDP > término
 * do Cronograma > Baseline LDP > baseline do Cronograma) dentro da semana.
 * Tipo 2: cada gate 30/60/90 ativo e não cancelado com data (Reprogramada > Baseline do gate) na semana.
 * Realizado = emitido; Em atraso = não emitido com data anterior à data de corte.
 * ---------------------------------------------------------------------------------------------- */
export interface EntregaSemana{projectId:string;projeto:string;codigo:string|null;gate:number|null;data:string;emitido:boolean;atrasado:boolean;chave:string}
export function entregasDaSemana(reports:readonly FactsReport[],cat:CatalogoDisciplinas,cutoff:string){
 const semana=semanaDoCorte(cutoff),out:EntregaSemana[]=[];
 const dentro=(d:string|null)=>!!d&&d>=semana.start&&d<=semana.end;
 for(const r of reports)for(const d of r.documentos){
  if(d.isActivity)continue;
  const {chave}=classificarDocumento(cat,d);
  if(r.tipoLdp===2){
   for(const g of d.gates??[]){
    const data=dataValida(g.reprogramada??g.baseline);
    if(!g.ativo||g.cancelado||!dentro(data))continue;
    const emitido=g.emitido===true;
    out.push({projectId:r.projectId,projeto:r.nomeProjeto,codigo:d.codigo,gate:g.percentual,data:data!,emitido,atrasado:!emitido&&data!<cutoff,chave});
   }
  }else{
   const data=dataValida(d.dataAlvo);
   if(d.cancelado||!dentro(data))continue;
   out.push({projectId:r.projectId,projeto:r.nomeProjeto,codigo:d.codigo,gate:null,data:data!,emitido:d.emitido,atrasado:!d.emitido&&data!<cutoff,chave});
  }
 }
 const previstos=out.length,realizados=out.filter(x=>x.emitido).length,atrasados=out.filter(x=>x.atrasado).length;
 const aderencia=pct(realizados,previstos);
 return {semana,entregas:out,previstos,realizados,atrasados,aderencia,desvio:aderencia===null?null:100-aderencia};
}
/** Próxima/anterior semana com entregas previstas — orienta o usuário quando a semana escolhida está vazia. */
export function semanasComEntregas(reports:readonly FactsReport[]){
 const datas=new Set<string>();
 for(const r of reports)for(const d of r.documentos){
  if(d.isActivity)continue;
  if(r.tipoLdp===2)(d.gates??[]).forEach(g=>{const x=dataValida(g.reprogramada??g.baseline);if(g.ativo&&!g.cancelado&&x)datas.add(semanaDoCorte(x).start);});
  else{const x=dataValida(d.dataAlvo);if(!d.cancelado&&x)datas.add(semanaDoCorte(x).start);}
 }
 return [...datas].sort();
}
export function agrupar<T>(itens:readonly T[],chave:(x:T)=>string){
 const m=new Map<string,T[]>();for(const x of itens){const k=chave(x);if(!m.has(k))m.set(k,[]);m.get(k)!.push(x);}return m;
}

/* ------------------------------------------------------------------------------------------------
 * PROGRAMAÇÃO DIÁRIA (tela 02) — dbo.tb_WeeklyEngineeringSchedule via /api/reports/gestao-vista-jamile/programacao.
 * HH previsto da linha = HH elaboração + HH verificação + HH aprovação (componentes conhecidos).
 * ---------------------------------------------------------------------------------------------- */
export interface ProgramacaoLinha{idRegistro:number;documento:string|null;tarefa:string|null;disciplinaWeekly:string|null;
 siglaDoc3:string|null;disciplinaNorma:string|null;disciplinaCanonica:string|null;fonteDisciplina:string;
 elaborador:string|null;verificador:string|null;aprovador:string|null;
 hhElaboracao:number|null;hhVerificacao:number|null;hhAprovacao:number|null;
 baseline:string|null;reprogramado:string|null;dataAtividade:string;status:string|null;fase:string|null}
export interface ProgramacaoResposta{projectId:string;inicio:string;fim:string;ultimaSincronizacao:string|null;registrosProjeto:number;
 registrosSemData:number;datasComProgramacao:{data:string;quantidade:number}[];linhas:ProgramacaoLinha[]}
export const hhLinha=(l:Pick<ProgramacaoLinha,"hhElaboracao"|"hhVerificacao"|"hhAprovacao">)=>somaConhecida([l.hhElaboracao,l.hhVerificacao,l.hhAprovacao]);
const NAO_RECURSO=new Set(["TBD","-","—","N/A","NA"]);
export function recursosDistintos(linhas:readonly ProgramacaoLinha[]){
 const s=new Set<string>();
 for(const l of linhas)for(const p of [l.elaborador,l.verificador,l.aprovador]){const k=normalize(p??"");if(k&&!NAO_RECURSO.has(k))s.add(k);}
 return s.size;
}
export function classificarProgramacao(cat:CatalogoDisciplinas,projectId:string,l:ProgramacaoLinha):Classificacao{
 return classificarDocumento(cat,{projectId,disciplinaNorma:l.disciplinaNorma,disciplinaCanonica:l.disciplinaCanonica,siglaDoc3:l.siglaDoc3,codigo:l.documento});
}

/* ----------------------------------------------------------------------------------------------
 * 02. PROGRAMAÇÃO SEMANAL — semana oficial domingo–sábado (semanaDoCorte). Tudo calculado sobre os registros reais de
 * dbo.tb_WeeklyEngineeringSchedule devolvidos pelo endpoint de programação, consultado com inicio=domingo e fim=sábado.
 * ---------------------------------------------------------------------------------------------- */
export const DIAS_SEMANA_CURTOS=["DOM","SEG","TER","QUA","QUI","SEX","SÁB"] as const;
/** "QUI" para 2026-10-15 (dia da semana em UTC ao meio-dia: sem deslocamento de fuso). */
export const diaDaSemanaCurto=(data:string)=>DIAS_SEMANA_CURTOS[new Date(data.slice(0,10)+"T12:00:00Z").getUTCDay()];

type LinhaDoProjeto=ProgramacaoLinha&{projectId:string};
const chaveTexto=(v:string|null)=>normalize(v??"");
/**
 * Atividade da programação = projeto + documento + tarefa + fase + data da atividade. Registros com a MESMA chave são
 * repetições do mesmo item (ex.: linha importada duas vezes) e contam uma vez (fica o menor Id_Registro). Tarefa, fase ou
 * dia diferentes são atividades distintas e são preservadas. Sem número de documento não há como provar repetição:
 * o registro é mantido.
 */
export function deduplicarProgramacao<T extends LinhaDoProjeto>(linhas:readonly T[]):{linhas:T[];repetidos:number}{
 const vistos=new Map<string,T>();
 const ordenadas=[...linhas].sort((a,b)=>a.idRegistro-b.idRegistro);
 const out:T[]=[];
 for(const l of ordenadas){
  const doc=chaveTexto(l.documento);
  if(!doc){out.push(l);continue;}
  const k=[l.projectId,doc,chaveTexto(l.tarefa),chaveTexto(l.fase),l.dataAtividade.slice(0,10)].join("|");
  if(vistos.has(k))continue;
  vistos.set(k,l);out.push(l);
 }
 return {linhas:out,repetidos:linhas.length-out.length};
}

/** Linhas da semana, opcionalmente só de um dia (filtro opcional; sem filtro = os sete dias). */
export const filtrarDia=<T extends {dataAtividade:string}>(linhas:readonly T[],dia:string|null)=>
 dia?linhas.filter(l=>l.dataAtividade.slice(0,10)===dia):[...linhas];

/** Quantidade de atividades por dia da semana (domingo → sábado), para o filtro e o agrupamento. */
export function atividadesPorDia(linhas:readonly {dataAtividade:string}[],inicioSemana:string){
 return diasDaSemana(inicioSemana).map(data=>({data,dia:diaDaSemanaCurto(data),quantidade:linhas.filter(l=>l.dataAtividade.slice(0,10)===data).length}));
}

/** Semana anterior/seguinte que TEM programação (datas com programação do projeto), para navegar a partir de uma semana vazia. */
export function semanasVizinhasComProgramacao(datas:readonly string[],semana:{start:string;end:string}){
 const ordenadas=[...new Set(datas.map(d=>d.slice(0,10)))].sort();
 const antes=[...ordenadas].reverse().find(d=>d<semana.start),depois=ordenadas.find(d=>d>semana.end);
 return {anterior:antes?semanaDoCorte(antes):null,proxima:depois?semanaDoCorte(depois):null};
}
