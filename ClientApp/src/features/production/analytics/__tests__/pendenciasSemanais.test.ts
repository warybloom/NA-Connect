import {test} from "node:test";
import assert from "node:assert/strict";
import {calcularPendenciasSemanais} from "../pendenciasSemanais";
import type {ProgramacaoResposta,ProgramacaoLinha} from "../gestaoVistaJamileLogic";
const projectId="b.fcdb7ebd-1b23-440d-a04b-90391098522e";
const week={start:"2026-11-22",end:"2026-11-28"};
const doc=(n:number)=>"CHS001-000-PI0-DE-"+String(n).padStart(3,"0");
const line=(n:number,status:string,planned="2026-11-23"):ProgramacaoLinha=>({
 idRegistro:n,documento:doc(n),tarefa:"EMISSAO",disciplinaWeekly:"Tubulação",
 siglaDoc3:"PI0",disciplinaNorma:"Tubulação",disciplinaCanonica:null,fonteDisciplina:"WEEKLY",
 elaborador:null,verificador:null,aprovador:null,hhElaboracao:null,hhVerificacao:null,hhAprovacao:null,
 baseline:planned,reprogramado:null,dataAtividade:planned,status,fase:null
});
const source=(rows:ProgramacaoLinha[]):ProgramacaoResposta=>({
 projectId,inicio:week.start,fim:week.end,ultimaSincronizacao:null,registrosProjeto:rows.length,
 registrosSemData:0,datasComProgramacao:[],linhas:rows
});
const scheduled=(n:number,date="2026-11-23")=>({projectId,codigo:doc(n),disciplina:"PIPING",dataPrevista:date});
const valid={[projectId]:["APPROVED","APROVADO","CLIENT REVIEW"]};
const opts=(p:Partial<Parameters<typeof calcularPendenciasSemanais>[0]>={})=>({
 itens:[scheduled(1),scheduled(2),scheduled(3),scheduled(4),scheduled(5,"2026-11-28")],
 semana:week,hoje:"2026-11-26",programacao:[source([
  line(1,"CANCELED"),line(2,"NOT STARTED"),line(3,"ELABORATION"),line(4,"CLIENT REVIEW"),line(5,"ISSUANCE FLOW","2026-11-28")
 ])],
 documentosLdp:[],statusValidos:valid,...p
});
test("cancelled is neither late nor to issue; pending statuses remain charged",()=>{
 const result=calcularPendenciasSemanais(opts());
 assert.equal(result.programados,5);
 assert.equal(result.cancelados,1);
 assert.equal(result.aEmitir,3);
 assert.equal(result.atrasados,2);
 assert.equal(result.vencidos,3);
 assert.equal(result.emitidosProgramados,1);
 assert.equal(Math.round(result.desvio!*10)/10,66.7);
});
test("LDP cancelled also excluded and weekly other statuses remain pending",()=>{
 const r=calcularPendenciasSemanais(opts({documentosLdp:[{
  projectId,codigo:doc(2),cancelado:true
 }]}));
 assert.equal(r.cancelados,2);
 assert.equal(r.aEmitir,2);
 assert.equal(r.atrasados,1);
});
test("documents from other weeks never inflate weekly to-issue or overdue",()=>{
 const r=calcularPendenciasSemanais(opts({
  itens:[scheduled(1),scheduled(2,"2026-12-02"),scheduled(3,"2026-11-16")],
  programacao:[source([line(1,"NOT STARTED"),line(2,"NOT STARTED","2026-12-02"),line(3,"NOT STARTED","2026-11-16")])]
 }));
 assert.equal(r.programados,1);
 assert.equal(r.aEmitir,1);
 assert.equal(r.atrasados,1);
});
test("once the week ends, all remaining unissued become overdue",()=>{
 const r=calcularPendenciasSemanais(opts({hoje:"2026-11-30"}));
 assert.equal(r.aEmitir,3);
 assert.equal(r.atrasados,3);
});
test("valid LDP status and date is emitted, without extra status in weekly",()=>{
 const r=calcularPendenciasSemanais(opts({documentosLdp:[{
  projectId,codigo:doc(2),cancelado:false,statusCliente:"APPROVED",dataEmissao:"2026-11-25"
 }]}));
 assert.equal(r.aEmitir,2);
 assert.equal(r.atrasados,1);
});
test("emitidos early from other weeks cannot offset weekly late documents",()=>{
 const r=calcularPendenciasSemanais(opts({
  itens:[scheduled(2)],
  programacao:[source([line(2,"NOT STARTED"),line(9,"CLIENT REVIEW","2026-12-03")])],
  documentosLdp:[{projectId,codigo:doc(9),cancelado:false,statusCliente:"CLIENT REVIEW",dataEmissao:"2026-11-25"}]
 }));
 assert.equal(r.aEmitir,1);
 assert.equal(r.atrasados,1);
});
