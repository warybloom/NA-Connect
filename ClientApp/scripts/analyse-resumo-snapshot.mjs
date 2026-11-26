import {build} from 'esbuild';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.resolve(root,'../_build_verify/resumo');
await build({stdin:{resolveDir:root,sourcefile:'resumo-evidence.ts',contents:`
import {readFileSync,writeFileSync} from 'node:fs';
import {resumoDisciplinas} from './src/features/production/analytics/resumoPhaseData';
import {calcularResumo} from './src/features/production/analytics/resumoLogic';
import {projectFacts} from './src/components/pmo/pmoFacts';
const reports=JSON.parse(readFileSync(${JSON.stringify(path.join(output,'reports.json'))},'utf8'));
const evidence=reports.map(r=>{
 const before=calcularResumo([r]),snapshot=JSON.stringify(r),ds=resumoDisciplinas([r]);
 if(snapshot!==JSON.stringify(r))throw Error('Official payload was mutated');
 const known=field=>ds.reduce((n,d)=>n+(d[field]??0),0);
 const baselineDisciplines=projectFacts([r]).disciplines;
 const baselineKnown=field=>baselineDisciplines.reduce((n,d)=>n+(d[field]??0),0);
 const presentationDifferences=Object.fromEntries(['HH_Previsto','HH_Realizado','Custo_Previsto','Custo_Realizado'].map(field=>[field,known(field)-baselineKnown(field)]));
 if(Object.values(presentationDifferences).some(v=>Math.abs(v)>1e-6))throw Error('Presentation changed official discipline values');
 const hhDifference=known('HH_Realizado')-(before.hhRealizado??0),costDifference=known('Custo_Realizado')-(before.custoRealizado??0);
 if(Math.abs(hhDifference)>1e-6||Math.abs(costDifference)>1e-6)throw Error('Consolidated actual values not reconciled');
 return {project:r.nomeProjeto,tipoLdp:r.tipoLdp,consolidated:before,disciplines:ds.length,actualHhDifference:hhDifference,actualCostDifference:costDifference,presentationDifferences,
  phaseCoverage:r.resumoFases.fases,unresolvedDocuments:r.documentos.filter(d=>!d.disciplinaNorma).map(d=>({project:r.nomeProjeto,document:d.codigo,doc3:d.siglaDoc3,norma:d.normaCoordenacaoId,cause:d.diagnosticoDisciplinaNorma})),
  historicHours:r.actual.historicalMeasurement?.complementHours??0,disciplinesWithOverspend:ds.filter(d=>(d.SaldoHH??0)<0).map(d=>({name:d.Disciplina,balance:d.SaldoHH})),
  phasesWithCompleteWork:ds.flatMap(d=>d.fases.filter(f=>f.total>0&&f.hhPrevisto!==null).map(f=>({discipline:d.Disciplina,gate:f.gate,hh:f.hhPrevisto}))) };
});
writeFileSync(${JSON.stringify(path.join(output,'evidence.json'))},JSON.stringify(evidence,null,2));
console.log(JSON.stringify(evidence.map(e=>({project:e.project,type:e.tipoLdp,disciplines:e.disciplines,hhDifference:e.actualHhDifference,costDifference:e.actualCostDifference,unresolved:e.unresolvedDocuments.length,completeWorkPhases:e.phasesWithCompleteWork.length,historic:e.historicHours,overspend:e.disciplinesWithOverspend.length})),null,2));
`},bundle:true,platform:'node',format:'esm',outfile:path.join(output,'evidence.mjs'),alias:{'@':path.join(root,'src')},logLevel:'error',
 banner:{js:"import {createRequire as __createRequire} from 'node:module'; const require=__createRequire(import.meta.url);"}});
await import(pathToFileURL(path.join(output,'evidence.mjs')).href);
