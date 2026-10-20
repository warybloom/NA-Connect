import {test} from "node:test";
import assert from "node:assert/strict";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {resumoFases,resumoDisciplinas,resumoPageSize,type ResumoReport,type ResumoPhaseMetric} from "../resumoPhaseData";
import ResumoDisciplineCard from "../ResumoDisciplineCard";
import {calcularResumo} from "../resumoLogic";

const phase=(gate:number,hh:number|null=10):ResumoPhaseMetric=>({gate,total:1,emitidos:0,cancelados:0,aEmitir:1,atrasados:0,noPrazo:0,semPrazo:1,
  hhPrevisto:hh,hhRealizado:null,saldoHh:null,consumoHh:null,custoPrevisto:null,custoRealizado:null,consumoCusto:null,semHhPrevisto:hh===null?1:0});
const report=(id:string,tipo=2):ResumoReport=>({projectId:id,nomeProjeto:id,tipoLdp:tipo,dataReferencia:"2026-10-14",lancamentos:[],weeklyAtividades:[],cronograma:[],
  documentos:[{rowId:id+":1",projectId:id,codigo:"NA-01-CV-DE-001",disciplina:"CIVIL",disciplinaNorma:"Civil",siglaDoc3:"CV",fonteDisciplinaNorma:"NORMA_EXATA",statusDocumento:"SEM_PRAZO"} as never],
  planning:{plannedHhTotal:30,plannedHhByDiscipline:[{discipline:"CIVIL",plannedHh:30}],plannedCostByDiscipline:[{discipline:"CIVIL",documentCost:100,documents:1,withCost:1}],documentPlannedCost:100} as never,
  hhRealizado:{total:45,porDisciplina:[{disciplina:"Civil",hh:45,custo:150}],fonte:"TIMESHEET_CORRIGIDO + HISTORICO_CONFIRMADO",custoOficial:true,custoTotal:150},
  resumoFases:{fases:[phase(30),phase(60),phase(90)],documentos:[{rowId:id+":1",codigo:"NA-01-CV-DE-001",disciplina:"CIVIL",disciplinaNorma:"Civil",fases:[phase(30),phase(60),phase(90)]}],pendencias:[]}});

test("Tipo 1 does not show artificial gates",()=>assert.deepEqual(resumoFases([report("P",1)]),[]));
test("Missing backend projection shows dashes, never a reconstruction from document status",()=>{
  const r=report("P");delete r.resumoFases;
  assert.ok(resumoFases([r]).every(f=>f.total===null&&f.hhPrevisto===null));
});
test("Phase consolidation preserves incomplete coverage and never allocates actual hours",()=>{
  const a=report("A"),b=report("B");b.resumoFases!.fases[0].hhPrevisto=null;
  const fs=resumoFases([a,b]);assert.equal(fs[0].hhPrevisto,null);assert.equal(fs[1].hhPrevisto,20);
  assert.ok(fs.every(f=>f.hhRealizado===null&&f.saldoHh===null&&f.consumoHh===null&&f.custoRealizado===null));
});
test("Discipline cards reconcile official totals including overspend and historical source",()=>{
  const r=report("P"),before=calcularResumo([r]);
  const ds=resumoDisciplinas([r]);assert.equal(ds.length,1);assert.equal(ds[0].Disciplina,"Civil");
  assert.equal(ds[0].HH_Realizado,45);assert.equal(ds[0].SaldoHH,-15);assert.equal(ds[0].ConsumoHH,150);
  assert.equal(ds[0].documentos.total,1);assert.equal(ds[0].documentos.aEmitir,1);
  assert.deepEqual(calcularResumo([r]),before);assert.equal(ds[0].Custo_Realizado,before.custoRealizado);
});
test("Norma mapping remains isolated by project",()=>{
  const a=report("A"),b=report("B");b.documentos[0].disciplinaNorma="Estruturas";b.hhRealizado!.porDisciplina=[{disciplina:"Estruturas",hh:45,custo:150}];
  const names=resumoDisciplinas([a,b]).map(d=>d.Disciplina);assert.ok(names.includes("Civil"));assert.ok(names.includes("Estruturas"));
});
test("TV card includes all mandatory metrics and never exposes money",()=>{
  const [d]=resumoDisciplinas([report("P")]);
  const html=renderToStaticMarkup(createElement(ResumoDisciplineCard,{discipline:d,tv:true}));
  for(const text of ["HH previsto","HH realizado","Saldo HH","Consumo HH","Total documentos","Emitidos","Cancelados","A emitir","Atrasados","No prazo","Custo previsto","Custo realizado","Consumo custo","30%","60%","90%","150%"])
    assert.ok(html.includes(text),text);
  assert.ok(!html.includes("R$")&&!html.includes("BRL"));
});
test("TV paginates for width and height rather than reducing the font",()=>{
  assert.equal(resumoPageSize(800,1080,true),1);assert.equal(resumoPageSize(1280,720,true),2);
  assert.equal(resumoPageSize(1920,1080,true),3);assert.equal(resumoPageSize(3840,2160,true),3);
});
