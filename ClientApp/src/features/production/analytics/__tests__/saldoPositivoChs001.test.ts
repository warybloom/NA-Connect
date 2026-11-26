import { test } from "node:test";
import assert from "node:assert/strict";
import { calcularSaldoPositivoPorDisciplina } from "../saldoPositivoEmissao";
import { criarCatalogo, classificarVisaoGeral, classificarProgramacao, type ProgramacaoResposta, type ProgramacaoLinha } from "../gestaoVistaJamileLogic";

const id="b.00000000-0000-0000-0000-000000000001";
const cat=criarCatalogo([],[{id,name:"CHS001"}]);
const doc=(i:number)=>"CHS001-000-PI0-DE-"+String(i).padStart(3,"0");
const mk=(i:number):ProgramacaoLinha=>({
 idRegistro:i,documento:doc(i),tarefa:"EMISSAO",disciplinaWeekly:"Piping",
 siglaDoc3:"PI0",disciplinaNorma:"Tubulacao",disciplinaCanonica:null,fonteDisciplina:"WEEKLY",
 elaborador:null,verificador:null,aprovador:null,hhElaboracao:null,hhVerificacao:null,
 hhAprovacao:null,baseline:i<=75?"2026-11-25":"2026-12-02",reprogramado:null,
 dataAtividade:i<=75?"2026-11-25":"2026-12-02",status:"CLIENT REVIEW",fase:null
});
const lines=Array.from({length:94},(_,i)=>mk(i+1));
const src:ProgramacaoResposta={
 projectId:id,inicio:"2026-11-22",fim:"2026-12-05",ultimaSincronizacao:null,
 registrosProjeto:94,registrosSemData:0,datasComProgramacao:[],linhas:lines
};
const disciplina=classificarVisaoGeral(cat,classificarProgramacao(cat,id,mk(1)));
const weekDocs=lines.slice(0,75).map(l=>({
 chave:id+"::"+l.documento,projectId:id,codigo:l.documento,disciplina
}));
const statuses={[id]:["APPROVED","APROVADO","CLIENT REVIEW"]};
const dates=new Map(lines.map(l=>[id+"::"+l.documento!,"2026-11-24"]));
test("CHS001: 75 planned and 94 issued in this week yield +19 and >100% per discipline",()=>{
 const s=calcularSaldoPositivoPorDisciplina([src],weekDocs,statuses,cat,new Set(),
  {start:"2026-11-22",end:"2026-11-28"},dates);
 assert.equal(s.programados,75);assert.equal(s.emitidos,94);assert.equal(s.saldo,19);
 assert.equal(s.acimaDaMeta,19);
 const d=s.porDisciplina.find(d=>d.disciplina===disciplina);
 assert.equal(d?.emitidos,94);assert.equal(d?.programados,75);
 assert.ok((d?.percentual??0)>100);
});
test("same emission dates are NOT credited in the following week",()=>{
 const s=calcularSaldoPositivoPorDisciplina([src],[],statuses,cat,new Set(),
  {start:"2026-11-29",end:"2026-12-05"},dates);
 assert.equal(s.emitidos,0);assert.equal(s.saldo,0);
});
test("status alone without emission date does not create cross-week positive credit",()=>{
 const d=new Map(dates);d.delete(id+"::"+doc(94));
 const s=calcularSaldoPositivoPorDisciplina([src],weekDocs,statuses,cat,new Set(),
  {start:"2026-11-22",end:"2026-11-28"},d);
 assert.equal(s.emitidos,93);assert.equal(s.saldo,18);
});
