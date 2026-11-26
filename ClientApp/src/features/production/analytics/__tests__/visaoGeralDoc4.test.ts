import {test} from "node:test";
import assert from "node:assert/strict";
import {criarCatalogo,visaoGeralDaSemana,doc4Na,documentoVisaoGeral,type ProgramacaoLinha,type ProgramacaoResposta} from "../gestaoVistaJamileLogic";
const pid="b.11111111-1111-1111-1111-111111111111",week={start:"2026-10-18",end:"2026-10-24"};
let ix=0;const linha=(codigo:string|null,nome:string):ProgramacaoLinha=>({idRegistro:++ix,documento:codigo,tarefa:"ELABORAÇÃO",disciplinaWeekly:nome,siglaDoc3:"SD1",disciplinaNorma:nome,disciplinaCanonica:null,fonteDisciplina:"DOC3",elaborador:"ANA",verificador:null,aprovador:null,hhElaboracao:1,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,dataAtividade:"2026-10-19",status:null,fase:null});
const calcular=(ls:ProgramacaoLinha[])=>visaoGeralDaSemana({dados:[{projectId:pid,inicio:week.start,fim:week.end,ultimaSincronizacao:null,registrosProjeto:ls.length,registrosSemData:0,datasComProgramacao:[],linhas:ls} as ProgramacaoResposta],projetos:[{id:pid,name:"CHS001"}],cat:criarCatalogo([],[{id:pid,name:"CHS001"}]),semana:week,hoje:"2026-10-20",ldp:new Map([[pid,[]]])});
test("DOC4 no quarto segmento, AT excluida, DS preservado, sufixo AT nao confunde",()=>{
 assert.equal(doc4Na("CHS001-000-PI2-AT-003"),"AT");assert.equal(doc4Na("CHS001-000-PI2-DS-003"),"DS");
 assert.equal(documentoVisaoGeral("chs001-000-pi2-at-003"),false);
 assert.equal(documentoVisaoGeral("CHS001-000-PI2-DS-AT"),true);
 assert.equal(documentoVisaoGeral(null),false);
});
test("02 atividades AT e item sem codigo nao entram na base da 01",()=>{
 const a="CHS001-000-PI2-AT-003",b="CHS001-000-PI2-DS-003";
 const x=calcular([linha(a,"SISTEMA DIGITAIS"),linha(a,"SISTEMAS DIGITAIS"),linha(null,"SISTEMAS DIGITAIS"),linha(b,"SISTEMAS DIGITAIS")]);
 assert.equal(x.documentos,1);assert.equal(x.porDisciplina.length,1);assert.equal(x.porDisciplina[0].nome,"SISTEMAS DIGITAIS");
 assert.equal(x.porDisciplina[0].meta.documentos,1);assert.equal(x.itens.some(i=>i.codigo===a),false);
});
test("Duas variantes digitadas viram um unico grafico correto",()=>{
 const x=calcular([linha("CHS001-000-PI2-DS-001","SISTEMA DIGITAIS"),linha("CHS001-000-PI2-DS-002","SISTEMAS DIGITAIS")]);
 assert.equal(x.porDisciplina.length,1);assert.equal(x.porDisciplina[0].nome,"SISTEMAS DIGITAIS");
 assert.equal(x.porDisciplina[0].documentos,2);
});
test("Apenas AT: nao ha gauge, nem falsa sem comprovacao",()=>{
 const x=calcular([linha("CHS001-000-PI2-AT-003","SISTEMA DIGITAIS"),linha(null,"SISTEMAS DIGITAIS")]);
 assert.equal(x.documentos,0);assert.equal(x.semComprovacao,0);assert.equal(x.porDisciplina.length,0);
});
