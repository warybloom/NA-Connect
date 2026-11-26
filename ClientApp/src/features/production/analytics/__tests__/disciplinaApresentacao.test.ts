import {test} from "node:test";
import assert from "node:assert/strict";
import {disciplinasParaExibicao,pertenceAoGrupo} from "../disciplinasApresentacao";
import {projectFacts,type FactsReport} from "@/components/pmo/pmoFacts";
const values=(hh:number,cost:number)=>({hh,cost,knownCostSubtotal:cost,knownCalculatedCostSubtotal:cost,entries:1,missingHh:0,missingCost:0,missingCalculatedCost:0});
function report(projectId:string,alias:string,canon:string|null,hh:number,cost:number):FactsReport{
 const codigo=projectId+"-212-TU3-DE-001";
 return {
  projectId,nomeProjeto:projectId,tipoLdp:1,dataReferencia:"2026-10-14",
  documentos:[{rowId:projectId+"-1",projectId,codigo,titulo:"Teste",disciplina:alias,disciplinaNorma:canon,fonteDisciplinaNorma:canon?"NORMA_PREFIXO":"SEM_DISCIPLINA",
    hhPrevisto:hh,custoPrevisto:cost,inicioSemana:null,dataBaseline:null,dataReprogramada:null,
    dataEmissao:null,statusInterno:null,statusCliente:null,situacaoEmissao:null,
    avancoFisico:null,classeFisica:"",cancelado:false,emitido:false,statusPrazo:"",
    dataAlvo:null,fonteDataAlvo:"",correlacaoCronograma:"",statusDocumento:"SEM_PRAZO",
    percentFisicoLdp:null,fonteAvancoFisico:""}],
  lancamentos:[],weeklyAtividades:[],cronograma:[],
  planning:{plannedHhTotal:hh,plannedHhByDiscipline:[{discipline:alias,plannedHh:hh}],disciplineTotal:hh,
    undisciplinedHh:0,reconciliationDifference:0,source:"",coverage:"",diagnostics:[],
    plannedCostTotal:cost,documentPlannedCost:cost,knownDocumentCostSubtotal:cost,
    plannedCostByDiscipline:[{discipline:alias,documentCost:cost,documents:1,withCost:1}],
    costSource:"",costCoverage:"",costDocuments:1,documentsWithCost:1},
  actual:{total:values(hh/2,cost/2),source:"",byDiscipline:[{discipline:alias,values:values(hh/2,cost/2)}],byDocument:[]}
 };
}
test("DOC3 + Norma: alias PIPING/TUBULAÇÃO vira um nome oficial, sem alterar totais",()=>{
 const r=[report("P1","PIPING","Tubulação",40,400),report("P2","TUBULAÇÃO","Tubulação",60,600)];
 const grupos=disciplinasParaExibicao(r);
 assert.equal(grupos.length,1);
 assert.equal(grupos[0].Disciplina,"Tubulação");
 assert.deepEqual(grupos[0].origens,["PIPING","TUBULAÇÃO"]);
 assert.equal(grupos[0].HH_Previsto,projectFacts(r).cards.HH_Previsto);
 assert.equal(grupos[0].HH_Realizado,projectFacts(r).cards.HH_Realizado);
 assert.equal(grupos[0].Custo_Previsto,projectFacts(r).cards.Custo_Previsto);
 assert.equal(grupos[0].Custo_Realizado,projectFacts(r).cards.Custo_Realizado);
 assert.equal(pertenceAoGrupo(null,"PIPING",grupos[0]),true);
});
test("DOC3 sem correspondência: nenhum nome inventado e nenhum HH escondido",()=>{
 const [g]=disciplinasParaExibicao([report("P1","PIPING",null,10,100)]);
 assert.equal(g.Disciplina,"SEM MAPEAMENTO");
 assert.equal(g.oficial,false);
 assert.equal(g.HH_Previsto,10);
 assert.equal(g.Custo_Realizado,50);
});
test("Mesma grafia bruta apontando para duas disciplinas na Norma: manter como ambígua",()=>{
 const groups=disciplinasParaExibicao([report("P1","ME","Mecânica",10,100),report("P2","ME","Instrumentação",20,200)]);
 assert.equal(groups.length,1);
 assert.equal(groups[0].Disciplina,"SEM MAPEAMENTO");
 assert.match(groups[0].diagnostico??"",/mais de uma disciplina/);
 assert.equal(groups[0].HH_Previsto,30);
});
test("Regra 2 caracteres do DOC3: texto bruto da LDP não contamina o grupo quando o canônico DOC3 existe",()=>{
 // CIM-3D-001: LDP diz '3D MODELING', mas o DOC3 'CIM' -> 'CI' -> CIVIL (canônico já resolvido no backend).
 const r=report("P1","CIVIL","CIVIL",106.88,1000);
 r.documentos.push({...r.documentos[0],rowId:"P1-2",codigo:"P1-000-CIM-3D-001",disciplina:"3D MODELING",disciplinaCanonica:"CIVIL",disciplinaNorma:"CIVIL"});
 r.documentos[0].disciplinaCanonica="CIVIL";
 const [g,...resto]=disciplinasParaExibicao([r]);
 assert.equal(resto.length,0);
 assert.equal(g.Disciplina,"CIVIL");
 assert.equal(g.oficial,true);
 assert.equal(g.HH_Previsto,106.88);
});
