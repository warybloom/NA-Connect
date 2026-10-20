import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import path from "node:path";
import {
 criarCatalogo,deduplicarProgramacao,semanaDoCorte,semanasVizinhasComProgramacao,resumoProgramacao,ultimaSemanaComProgramacao,
 janelasDoPeriodo,mesclarJanelas,addDias,type ProgramacaoLinha,type ProgramacaoResposta,type ProgramacaoSemData
} from "../gestaoVistaJamileLogic";
import {agruparProgramacao} from "../projetoFase/projetoFaseLogic";

/*
 * Fixture = distribuição REAL de dbo.tb_WeeklyEngineeringSchedule do CHS001 (SQL read-only em 19/10/2026 11h45):
 * 104 registros DADO (CHS001-000-PM0-RT-001-10.xlsx, sincronizado 2026-10-19 10:28:21.437), 100 com
 * COALESCE(Replanned, Baseline) entre 13/08 e 22/09/2026, 4 sem data. Semana 13–19/09 = 59; 20–26/09 = 4; 18–24/10 = 0.
 */
const CHS="b.fcdb7ebd-1b23-440d-a04b-90391098522e";
const DISTRIBUICAO:[string,number][]=[["2026-08-13",1],["2026-08-14",1],["2026-08-18",3],["2026-08-25",12],["2026-08-28",1],["2026-09-01",2],
 ["2026-09-03",1],["2026-09-07",2],["2026-09-08",8],["2026-09-09",5],["2026-09-11",1],["2026-09-15",56],["2026-09-17",3],["2026-09-21",1],["2026-09-22",3]];
const SYNC="2026-10-19T10:28:21.437";
const ARQUIVO="CHS001-000-PM0-RT-001-10.xlsx";

const linha=(id:number,data:string,documento:string,o:Partial<ProgramacaoLinha>={}):ProgramacaoLinha=>({idRegistro:id,documento,
 tarefa:`Título longo do documento ${id} — Mechanical Datasheet - ET-4801 - Reboiler com descrição extensa`,disciplinaWeekly:"MECHANICAL",
 siglaDoc3:"ME7",disciplinaNorma:null,disciplinaCanonica:null,fonteDisciplina:"",elaborador:null,verificador:null,aprovador:null,
 hhElaboracao:null,hhVerificacao:null,hhAprovacao:null,baseline:data+"T00:00:00",reprogramado:null,dataAtividade:data+"T00:00:00",
 status:"VERIFICATION",fase:"0,6",...o});
const BANCO_CHS:ProgramacaoLinha[]=(()=>{let id=1000;return DISTRIBUICAO.flatMap(([d,n])=>Array.from({length:n},()=>{id++;return linha(id,d,`CHS001-000-ME7-DS-${id}`);}));})();
const SEM_DATA_CHS:ProgramacaoSemData[]=[["CHS001-000-PR0-TR-001","CAPEX REPORT",108],["CHS001-000-ME0-AT-003","TECHNICAL EQUALIZATION",109],
 ["CHS001-000-IA0-AT-003","TECHNICAL EQUALIZATION",110],["CHS001-000-EL0-AT-003","TECHNICAL EQUALIZATION",111]]
 .map(([documento,tarefa,linhaExcel],i)=>({idRegistro:2000+i,documento:documento as string,tarefa:tarefa as string,disciplinaWeekly:null,siglaDoc3:null,
  disciplinaNorma:null,disciplinaCanonica:null,fonteDisciplina:"",elaborador:null,status:null,fase:"GERAL",linhaExcel:linhaExcel as number}));

/** Simula GestaoVistaJamileRepository.ProgramacaoAsync: resumo do projeto inteiro + linhas com data em [inicio, fim]. */
function api(projectId:string,banco:readonly ProgramacaoLinha[],semData:readonly ProgramacaoSemData[],inicio:string,fim:string,sync:string|null=SYNC,arquivo:string|null=ARQUIVO):ProgramacaoResposta{
 const dias=new Map<string,number>();
 for(const l of banco){const d=l.dataAtividade.slice(0,10);dias.set(d,(dias.get(d)??0)+1);}
 return {projectId,inicio,fim,ultimaSincronizacao:banco.length+semData.length?sync:null,registrosProjeto:banco.length+semData.length,registrosSemData:semData.length,
  datasComProgramacao:[...dias].sort().map(([data,quantidade])=>({data:data+"T00:00:00",quantidade})),
  linhas:banco.filter(l=>{const d=l.dataAtividade.slice(0,10);return d>=inicio&&d<=fim;}),arquivoOrigem:arquivo,linhasSemData:[...semData]};
}
const semana=(data:string,projectId=CHS,banco=BANCO_CHS,semData=SEM_DATA_CHS)=>{const s=semanaDoCorte(data);return api(projectId,banco,semData,s.start,s.end);};

test("CHS001 real: 104 importados, 100 com data, 4 sem data; período 13/08–22/09; sincronização e arquivo preservados",()=>{
 const r=resumoProgramacao([semana("2026-10-19")]);
 assert.deepEqual([r.importados,r.comData,r.semData],[104,100,4]);
 assert.deepEqual(r.periodo,{inicio:"2026-08-13",fim:"2026-09-22"});
 assert.equal(r.ultimaSincronizacao,SYNC);
 assert.equal(r.projetos[0].arquivoOrigem,ARQUIVO);
});

test("CHS001 real: semana 13–19/09 tem 59 atividades (sem repetição); 20–26/09 tem 4; semana atual 18–24/10 tem ZERO",()=>{
 const setembro=semana("2026-09-15");
 assert.equal(setembro.linhas.length,59);
 assert.equal(deduplicarProgramacao(setembro.linhas.map(l=>({...l,projectId:CHS}))).linhas.length,59);
 assert.equal(semana("2026-09-22").linhas.length,4);
 const atual=semana("2026-10-19");
 assert.deepEqual(semanaDoCorte("2026-10-19"),{start:"2026-10-18",end:"2026-10-24"});
 assert.equal(atual.linhas.length,0);                                    // nada é transposto para outubro
 assert.ok(atual.linhas.every(l=>l.dataAtividade<"2026-10-01"));
});

test("Semana atual vazia com histórico: atalho para a última semana REAL com atividades e para a semana anterior",()=>{
 const atual=semana("2026-10-19");
 const datas=atual.datasComProgramacao.map(x=>x.data);
 assert.deepEqual(ultimaSemanaComProgramacao(datas),{start:"2026-09-20",end:"2026-09-26"});
 assert.deepEqual(semanasVizinhasComProgramacao(datas,semanaDoCorte("2026-10-19")).anterior,{start:"2026-09-20",end:"2026-09-26"});
 assert.equal(semanasVizinhasComProgramacao(datas,semanaDoCorte("2026-10-19")).proxima,null);
});

test("Modo histórico: janelas ≤ 62 dias contíguas, união por Id_Registro devolve as 100 datadas com a data real",()=>{
 assert.deepEqual(janelasDoPeriodo("2026-08-13","2026-09-22"),[{inicio:"2026-08-13",fim:"2026-09-22"}]);
 const ano=janelasDoPeriodo("2026-01-01","2026-12-31");
 assert.equal(ano[0].inicio,"2026-01-01");assert.equal(ano[ano.length-1].fim,"2026-12-31");
 for(let i=0;i<ano.length;i++){
  const dias=(Date.parse(ano[i].fim)-Date.parse(ano[i].inicio))/86400000+1;
  assert.ok(dias>=1&&dias<=62,`janela ${i} com ${dias} dias`);
  if(i>0)assert.equal(ano[i].inicio,addDias(ano[i-1].fim,1));        // sem lacuna nem sobreposição
 }
 // Duas janelas que se sobrepõem de propósito: nenhuma linha duplicada.
 const a=api(CHS,BANCO_CHS,SEM_DATA_CHS,"2026-08-01","2026-09-15"),b=api(CHS,BANCO_CHS,SEM_DATA_CHS,"2026-09-15","2026-10-31");
 const m=mesclarJanelas([a,b])!;
 assert.equal(m.linhas.length,100);
 assert.equal(new Set(m.linhas.map(l=>l.idRegistro)).size,100);
 assert.deepEqual([m.inicio,m.fim],["2026-08-01","2026-10-31"]);
 assert.equal(m.linhas[0].dataAtividade.slice(0,10),"2026-08-13");     // ordenado pela data real
 assert.equal(m.linhasSemData?.length,4);                              // sem data permanecem à parte, nunca datados
 assert.ok(m.linhas.every(l=>l.dataAtividade.slice(0,10)<="2026-09-22"));
 assert.equal(mesclarJanelas([]),null);
});

test("Projeto com 0 dados ou 100% sem data: contadores honestos, sem período, sem semana inventada",()=>{
 const vazio=api("b.00000000-0000-0000-0000-0000000000e1",[],[],"2026-10-18","2026-10-24");
 const soSemData=api("b.00000000-0000-0000-0000-0000000000e2",[],SEM_DATA_CHS,"2026-10-18","2026-10-24");
 const r=resumoProgramacao([vazio,soSemData]);
 assert.deepEqual(r.projetos.map(p=>[p.importados,p.comData,p.semData,p.periodo]),[[0,0,0,null],[4,0,4,null]]);
 assert.equal(r.periodo,null);
 assert.equal(r.projetos[0].ultimaSincronizacao,null);
 assert.equal(ultimaSemanaComProgramacao([]),null);
 assert.equal(mesclarJanelas([{...soSemData,linhas:[]}])!.linhas.length,0);
});

test("Multiprojeto: histórico por projeto, contadores separados, nenhum registro trocado de projeto",()=>{
 const P2="b.00000000-0000-0000-0000-0000000000b2";
 const banco2=[linha(1,"2026-10-20","BML011-000-EL2-DR-001",{hhElaboracao:4}),linha(2,"2026-10-21","BML011-000-EL2-DR-002",{hhElaboracao:2})];
 const sel=[{id:CHS,name:"CHS001 - ETHANOL PLANT"},{id:P2,name:"BML011 - COOLING TOWER"}];
 const cat=criarCatalogo([],sel);
 // Semana atual: só o P2 tem atividades; CHS001 aparece vazio ("—" = null), nunca com zero inventado nem com setembro.
 const atual=[semana("2026-10-19"),semana("2026-10-19",P2,banco2,[])];
 const ga=agruparProgramacao(sel,atual,cat,null);
 assert.deepEqual(ga.projetos.map(p=>[p.projectId,p.totais.atividades]).sort(),[[P2,2],[CHS,null]].sort());
 // Histórico: cada projeto com as SUAS linhas (mesmo Id_Registro em projetos distintos não se mistura).
 const hist=[mesclarJanelas([api(CHS,BANCO_CHS,SEM_DATA_CHS,"2026-08-13","2026-09-22")])!,mesclarJanelas([api(P2,banco2,[],"2026-10-20","2026-10-21")])!];
 const gh=agruparProgramacao(sel,hist,cat,null);
 assert.deepEqual(gh.projetos.map(p=>[p.projectId,p.totais.atividades]).sort(),[[CHS,100],[P2,2]].sort());
 assert.equal(gh.projetos.find(p=>p.projectId===CHS)!.totais.hh,null);                           // HH vazio na fonte continua vazio (nunca 0 fictício)
 const r=resumoProgramacao(hist);
 assert.deepEqual(r.projetos.map(p=>[p.importados,p.comData,p.semData]),[[104,100,4],[2,2,0]]);
 assert.deepEqual(r.periodo,{inicio:"2026-08-13",fim:"2026-10-21"});
});

test("Item 3: título longo e HH em branco exibidos honestamente; título padrão SEMANA COMPLETA preservado",async()=>{
 const React=await import("react");
 const {renderToStaticMarkup}=await import("react-dom/server");
 const {GerenciaEngenhariaItem3}=await import("../item3/GerenciaEngenhariaItem3");
 const longo="Technical Specification - Existing Beer Column Internals Revamp and Capacity Evaluation for Ethanol Plant Expansion";
 const item={key:"1",data:"2026-09-15",dia:"TER 15/09",disciplina:"MECHANICAL",codigoDocumento:"CHS001-000-ME6-TS-019",tituloDocumento:longo,
  elaborador:null,verificador:null,aprovador:null,situacao:"VERIFICATION",hhPrevisto:null};
 const props={itens:[item],grupos:[{key:"M",nome:"MECHANICAL",entregaveis:1,hh:0}],agruparPorDia:true,onAgruparPorDia:()=>{},descricaoPeriodo:"13/09/2026 a 19/09/2026"};
 const semanaHtml=renderToStaticMarkup(React.createElement(GerenciaEngenhariaItem3,props));
 assert.match(semanaHtml,/SEMANA COMPLETA/);
 assert.ok(semanaHtml.includes(longo));                                 // título inteiro, sem corte
 assert.match(semanaHtml,/<td>—<\/td><\/tr>/);                           // HH vazio = "—", não "0"
 const hist=renderToStaticMarkup(React.createElement(GerenciaEngenhariaItem3,{...props,titulo:"TODAS AS ATIVIDADES IMPORTADAS"}));
 assert.match(hist,/TODAS AS ATIVIDADES IMPORTADAS/);
 assert.doesNotMatch(hist,/SEMANA COMPLETA/);
});

test("Tela 02: atalhos e indicadores visíveis, modo histórico rotulado e consulta semanal preservada",()=>{
 const raiz=process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp");
 const pagina=readFileSync(path.resolve(raiz,"src/features/production/analytics/GestaoVistaJamilePage.tsx"),"utf8");
 for(const texto of ["Ver última semana com atividades","Ver todas as atividades importadas","Documentos sem data","Última sincronização",
  "Período disponível","documento(s) importado(s)","com data;","sem data","Não é a programação da semana atual","ATIVIDADES IMPORTADAS COM DATA"])
  assert.ok(pagina.includes(texto),texto);
 assert.match(pagina,/useProgramacao\(selection,week\.start,week\.end,screen==="programacao"\|\|/);   // semana continua sendo a consulta padrão
 assert.match(pagina,/setModoProg\("semana"\)/);                                                   // navegar de semana sai do histórico
 assert.match(pagina,/\{screen==="programacao"&&importada\}[\s\S]*<ProjetoFaseAgrupamento idBase="pf-programacao"/); // PROJETO > FASE também recebe o painel
 assert.match(pagina,/agruparProgramacao\(selection,dadosProg,cat,dia\)/);
 const repo=readFileSync(path.resolve(raiz,"../Services/Reports/GestaoVistaJamileRepository.cs"),"utf8");
 assert.match(repo,/COALESCE\(Weekly_Replanned_Date, Weekly_Baseline_Date\) IS NULL\s+ORDER BY Linha_Excel/);
 assert.match(repo,/MAX\(Arquivo_Origem\) AS ArquivoOrigem/);
});
