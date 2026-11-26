import { test } from "node:test";
import assert from "node:assert/strict";
import { calcularSaldoPositivoPorDisciplina } from "../saldoPositivoEmissao";
import { criarCatalogo, classificarProgramacao, classificarVisaoGeral, type ProgramacaoResposta, type ProgramacaoLinha } from "../gestaoVistaJamileLogic";

const id = "b.00000000-0000-0000-0000-000000000001";
const cat = criarCatalogo([], [{id, name:"CHS001"}]);
const mk = (idRegistro:number, num:string, status:string, dataAtividade:string):ProgramacaoLinha => ({
  idRegistro, documento:num, tarefa:null, disciplinaWeekly:"Piping", siglaDoc3:"PI0",
  disciplinaNorma:"Tubulacao", disciplinaCanonica:null, fonteDisciplina:"WEEKLY",
  elaborador:null, verificador:null, aprovador:null,
  hhElaboracao:null, hhVerificacao:null, hhAprovacao:null,
  baseline:dataAtividade,reprogramado:null,dataAtividade,status,fase:null
});
const c1="CHS001-000-PI0-DE-001",c2="CHS001-000-PI0-DE-002",c3="CHS001-000-PI0-DE-003";
const source=(linhas:ProgramacaoLinha[]):ProgramacaoResposta=>({
  projectId:id, inicio:"2026-11-22",fim:"2026-11-28",
  ultimaSincronizacao:null,registrosProjeto:linhas.length,registrosSemData:0,
  datasComProgramacao:[],linhas
});
const scheduled = (codigo:string) => ({ chave:id+"::"+codigo,projectId:id,codigo,disciplina:classificarVisaoGeral(cat,classificarProgramacao(cat,id,mk(0,codigo,"NOT STARTED","2026-11-25"))),
  dias:[],atividades:1,situacao:"pendente" as const,motivo:null,atrasado:false,
  dataPrevista:"2026-11-25",fontePrevista:"ultima-atividade" as const });
test("status configured realizes two against one scheduled, allowing surplus", () => {
 const dados=[source([mk(1,c1,"CLIENT REVIEW","2026-11-24"), mk(2,c2,"CLIENT REVIEW","2026-12-01")])];
 const disc=calcularSaldoPositivoPorDisciplina(dados,[scheduled(c1)],{[id]:["CLIENT REVIEW"]},cat);
 assert.equal(disc.programados,1);
 assert.equal(disc.emitidos,2);
 assert.equal(disc.saldo,1);
 assert.equal(disc.acimaDaMeta,1);
 assert.equal(disc.porDisciplina.find(x=>x.emitidos===2)?.percentual,200);
});
test("duplicates, invalid status and AT do not inflate issuance", () => {
 const d=[source([mk(1,c1,"CLIENT REVIEW","2026-11-25"),
  mk(2,c1,"CLIENT REVIEW","2026-11-26"),mk(3,c2,"VERIFICATION","2026-11-26"),
  mk(4,"CHS001-000-PI0-AT-001","CLIENT REVIEW","2026-11-27")])];
 const r=calcularSaldoPositivoPorDisciplina(d,[scheduled(c1)],{[id]:["CLIENT REVIEW"]},cat);
 assert.equal(r.emitidos,1);
 assert.equal(r.saldo,0);
});
test("canceled documents never count even with allowed status", () => {
 const d=[source([mk(1,c1,"CLIENT REVIEW","2026-11-25")])];
 const r=calcularSaldoPositivoPorDisciplina(d,[scheduled(c1)],{[id]:["CLIENT REVIEW"]},cat,new Set([id+"::"+c1]));
 assert.equal(r.emitidos,0);
 assert.equal(r.saldo,-1);
});
test("any approved issuance status remains valid even when another activity is newer", () => {
 const d=[source([mk(1,c1,"CLIENT REVIEW","2026-11-25"),mk(2,c1,"VERIFICATION","2026-11-26")])];
 const r=calcularSaldoPositivoPorDisciplina(d,[scheduled(c1)],{[id]:["CLIENT REVIEW"]},cat);
 assert.equal(r.emitidos,1);
});
test("LDP APPROVED also qualifies when weekly status is different", () => {
 const dados=[source([mk(1,c1,"VERIFICATION","2026-11-25")])];
 const emissao=new Map([[id+"::"+c1,"2026-11-24"]]);
 const statusLdp=new Map([[id+"::"+c1,"APPROVED"]]);
 const r=calcularSaldoPositivoPorDisciplina(dados,[scheduled(c1)],{[id]:["APPROVED"]},cat,
   new Set(),{start:"2026-11-22",end:"2026-11-28"},emissao,statusLdp);
 assert.equal(r.emitidos,1);
 assert.equal(r.saldo,0);
});
