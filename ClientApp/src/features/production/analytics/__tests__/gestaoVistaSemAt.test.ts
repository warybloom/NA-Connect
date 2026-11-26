// 30/10/2026 — Gestão à Vista = documentos: Doc4=AT sai na ENTRADA da programação (01, 02, diária, TV) e dos agregados LDP.
import {test} from "node:test";
import assert from "node:assert/strict";
import {atividadeAt,semAtividadesAt,criarCatalogo,visaoGeralDaSemana,analiseDiariaAutomatica,faseTotalDocumentos,fasePrazos,
 type ProgramacaoLinha,type ProgramacaoResposta} from "../gestaoVistaJamileLogic";
import type {FactsReport} from "@/components/pmo/pmoFacts";
import {statusCounts} from "@/components/pmo/pmoFacts";

const pid="b.22222222-2222-2222-2222-222222222222",week={start:"2026-10-25",end:"2026-10-31"},hoje="2026-10-30";
let ix=0;
const linha=(codigo:string|null,data:string,tarefa="ELABORAÇÃO"):ProgramacaoLinha=>({idRegistro:++ix,documento:codigo,tarefa,disciplinaWeekly:"MECANICA",
 siglaDoc3:"ME0",disciplinaNorma:"MECANICA",disciplinaCanonica:null,fonteDisciplina:"DOC3",elaborador:"ANA",verificador:null,aprovador:null,
 hhElaboracao:2,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,dataAtividade:data,status:null,fase:null});
const resposta=(ls:ProgramacaoLinha[]):ProgramacaoResposta=>({projectId:pid,inicio:week.start,fim:week.end,ultimaSincronizacao:null,
 registrosProjeto:ls.length,registrosSemData:0,datasComProgramacao:[],linhas:ls,linhasSemData:[]});
const cat=()=>criarCatalogo([],[{id:pid,name:"BML011.016"}]);
const ldp=new Map([[pid,[{codigo:"BML011.016-000-ME0-DE-001",emitido:true,cancelado:false,dataEmissao:"2026-10-27"},
 {codigo:"BML011.016-000-ME0-DE-002",emitido:false,cancelado:false,dataEmissao:null}]]]);

test("Doc4=AT: exatamente o 4º segmento, trim/case-insensitive; título, outros segmentos e malformados não",()=>{
 assert.equal(atividadeAt("BML011.016-000-3D0-AT-001"),true);
 assert.equal(atividadeAt("BML011.016-000-SF0-AT-001"),true);
 assert.equal(atividadeAt("bml011.016-000-sf0- at -001"),true);
 assert.equal(atividadeAt("BML011.016-000-AT0-DE-001"),false);   // AT no DOC3 (disciplina), não no Doc4
 assert.equal(atividadeAt("BML011.016-000-ME0-DE-AT"),false);    // AT no Doc5
 assert.equal(atividadeAt("BML011.016-000-ME0-ATX-001"),false);  // nunca Contains
 assert.equal(atividadeAt("BML011.016-AT-001"),false);           // malformado: < 5 segmentos
 assert.equal(atividadeAt("ATIVIDADE AT"),false);
 assert.equal(atividadeAt(null),false);
});

test("semAtividadesAt retira AT de linhas e sem data; sem código e documentos permanecem",()=>{
 const r={...resposta([linha("BML011.016-000-3D0-AT-001",hoje),linha(null,hoje),linha("BML011.016-000-ME0-DE-001",hoje)]),
  linhasSemData:[{idRegistro:99,documento:"BML011.016-000-SF0-at-002",tarefa:null,disciplinaWeekly:null,siglaDoc3:null,disciplinaNorma:null,
   disciplinaCanonica:null,fonteDisciplina:"DOC3",elaborador:null,status:null,fase:null,linhaExcel:1}]};
 const f=semAtividadesAt(r);
 assert.deepEqual(f.linhas.map(l=>l.documento),[null,"BML011.016-000-ME0-DE-001"]);
 assert.equal(f.linhasSemData?.length,0);
 assert.equal(semAtividadesAt(resposta([])).linhasSemData?.length,0);
});

test("01 Visão Geral: incluir AT na entrada não altera nenhum indicador documental",()=>{
 const docs=[linha("BML011.016-000-ME0-DE-001","2026-10-27"),linha("BML011.016-000-ME0-DE-002","2026-10-28")];
 const ats=[linha("BML011.016-000-3D0-AT-001","2026-10-27"),linha("BML011.016-000-SF0-AT-001","2026-10-29")];
 const calc=(ls:ProgramacaoLinha[])=>visaoGeralDaSemana({dados:[semAtividadesAt(resposta(ls))],projetos:[{id:pid,name:"BML011.016"}],cat:cat(),semana:week,hoje,ldp});
 const a=calc(docs),b=calc([...docs,...ats]);
 for(const k of ["documentos","atividades","emitidos","semComprovacao","pendentes","atrasados","aderencia","desvio","aEmitir"] as const)
  assert.equal(b[k],a[k],k);
 assert.deepEqual(b.porDisciplina.map(x=>[x.nome,x.documentos,x.emitidos]),a.porDisciplina.map(x=>[x.nome,x.documentos,x.emitidos]));
 assert.equal(a.documentos,2);assert.equal(a.emitidos,1);
});

test("02/TV diária: AT não entra em previsto hoje, pendentes de ontem nem HH",()=>{
 const docs=[linha("BML011.016-000-ME0-DE-002","2026-10-29"),linha("BML011.016-000-ME0-DE-001",hoje)];
 const ats=[linha("BML011.016-000-3D0-AT-001","2026-10-29"),linha("BML011.016-000-3D0-AT-002",hoje)];
 const calc=(ls:ProgramacaoLinha[])=>analiseDiariaAutomatica({dados:[semAtividadesAt(resposta(ls))],cat:cat(),hoje,ldp}).totais;
 assert.deepEqual(calc([...docs,...ats]),calc(docs));
 assert.equal(calc(docs).pendentes,1);assert.equal(calc(docs).hhHoje,2);
});

test("Agregados LDP (fases 30/60/90 e status): AT marcado pelo backend não muda numerador nem denominador",()=>{
 const gate=(percentual:number,extra={})=>({percentual,ativo:true,cancelado:false,emitido:false,baseline:"2026-10-01",reprogramada:null,...extra});
 const doc={isActivity:false,statusDocumento:"ATRASADO",gates:[gate(30),gate(60,{emitido:true})]};
 const at={isActivity:true,statusDocumento:"ATIVIDADE",codigo:"BML011.016-000-3D0-AT-001",gates:[gate(30),gate(60)]};
 const rep=(documentos:unknown[])=>[{tipoLdp:2,documentos}] as unknown as FactsReport[];
 assert.deepEqual(faseTotalDocumentos(rep([doc,at])),faseTotalDocumentos(rep([doc])));
 assert.deepEqual(fasePrazos(rep([doc,at]),hoje),fasePrazos(rep([doc]),hoje));
 assert.deepEqual(statusCounts([doc,at]),statusCounts([doc]));
});
