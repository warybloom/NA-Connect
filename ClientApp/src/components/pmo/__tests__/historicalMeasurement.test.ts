import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { projectFacts, type FactsReport } from "../pmoFacts";
import { calcularResumo } from "@/features/production/analytics/resumoLogic";
import HistoricalMeasurementPanel from "../HistoricalMeasurementPanel";

const values=(hh:number,cost:number)=>({hh,cost:null,knownCostSubtotal:null,knownCalculatedCostSubtotal:cost,entries:23,missingHh:0,missingCost:23,missingCalculatedCost:0});
const report={
  projectId:"BML011.016",nomeProjeto:"COOLING TOWER",tipoLdp:1,dataReferencia:"2026-10-13",
  documentos:[],lancamentos:[],weeklyAtividades:[],cronograma:[],
  planning:{plannedHhTotal:1043.84,documentPlannedCost:110269.29,plannedHhByDiscipline:[],plannedCostByDiscipline:[]},
  actual:{total:values(1213.22,109130.09),timesheetOriginal:values(723.22,60130.09),source:"snapshot + historical",
    byDiscipline:[{discipline:"CIVIL",values:values(1213.22,109130.09)}],byDocument:[],
    historicalMeasurement:{projectId:"BML011.016",complementHours:490,complementCost:49000,excessHours:0,excessCost:0,diagnostics:[],
      disciplines:[{packageId:2,confirmationId:1,package:"ETS001",professional:"Everton Eustaquio",discipline:"CIVIL",
        confirmedHours:190,confirmedCost:19000,timesheetHours:47,timesheetCost:4700,confirmedAtUtc:"2026-10-13",confirmedBy:"Planejamento",origin:"Confirmação explícita",
        balance:{recognizedHours:47,recognizedCost:4700,complementHours:143,complementCost:14300,excessHours:0,excessCost:0,diagnostics:[]}}]}},
  hhRealizado:{total:1213.22,custoTotal:109130.09,custoOficial:true,fonte:"snapshot + historical",porDisciplina:[{disciplina:"CIVIL",hh:1213.22,custo:109130.09}]}
} as unknown as FactsReport;

test("Resumo e Análise Financeira consomem o consolidado sem somar novamente a confirmação",()=>{
  const facts=projectFacts([report]);const summary=calcularResumo([report]);
  assert.equal(facts.cards.HH_Realizado,1213.22);assert.equal(summary.hhRealizado,1213.22);
  assert.equal(summary.custoRealizado,109130.09);assert.equal(summary.hhPrevisto,1043.84);
  assert.equal(summary.custoPrevisto,110269.29);assert.equal(facts.disciplines[0].HH_Realizado,1213.22);
});
test("Painel identifica origem, complemento e evidência sem criar lançamento",()=>{
  const html=renderToStaticMarkup(React.createElement(HistoricalMeasurementPanel,{reports:[report]}));
  for(const label of ["Timesheet original","Complemento histórico","Realizado gerencial","ETS001","CIVIL","Everton Eustaquio","Confirmação explícita"])assert.ok(html.includes(label),label);
  assert.equal(report.lancamentos.length,0);
});
test("Pacote sem confirmação não exibe painel histórico",()=>{
  const other={...report,actual:{...report.actual!,historicalMeasurement:undefined}};
  assert.equal(renderToStaticMarkup(React.createElement(HistoricalMeasurementPanel,{reports:[other]})),"");
});
