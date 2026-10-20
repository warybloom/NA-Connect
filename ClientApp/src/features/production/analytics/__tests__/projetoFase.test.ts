import {test} from "node:test";
import assert from "node:assert/strict";
import {criarCatalogo,type DisciplinasNorma,type ProgramacaoResposta} from "../gestaoVistaJamileLogic";
import {agruparDocumentacao,agruparEntregas,agruparIndicadores,agruparProgramacao,ordemFase,SEM_FASE,SEM_FASE_ROTULO} from "../projetoFase/projetoFaseLogic";
import {criarNormalizador,type RegraSigla} from "@/features/disciplinas/normalizadorSiglas";

/* Dados sintéticos de teste: dois projetos (um Tipo 2 com gates, um Tipo 1) com a MESMA sigla e nomes de disciplina diferentes. */
const P1="b.00000000-0000-0000-0000-0000000000b1",P2="b.00000000-0000-0000-0000-0000000000b2";
const projetos=[{id:P1,name:"TST901 - Projeto Fases"},{id:P2,name:"TST902 - Projeto Sem Fases"}];
const norma=(projectId:string,disciplina:string):DisciplinasNorma=>({projectId,normaId:1,normaNome:"N",diagnosticos:[],siglas:[{sigla:"XA",disciplina,canonica:null}]});
const normas=[norma(P1,"Disciplina Um"),norma(P2,"Disciplina Dois")];
const regras:RegraSigla[]=[{id:1,siglaOriginal:"XA",disciplinaCanonica:"Teste Canônica",contextoTipo:null,contextoValor:null,ativo:true,origem:"MANUAL",
 importacaoId:null,versao:1,criadoEm:"",criadoPor:"t",alteradoEm:"",alteradoPor:"t"}];
// Catálogo CONSOLIDADO com normalizador: o agrupamento por projeto deve IGNORAR a normalização (rótulos originais).
const cat=criarCatalogo(normas,projetos,criarNormalizador(regras));

const gate=(percentual:number,o:Partial<{ativo:boolean;cancelado:boolean;emitido:boolean;esforcoHoras:number|null;documentStatus:string|null;baseline:string|null}>={})=>
 ({percentual,ativo:true,cancelado:false,emitido:false,esforcoHoras:null,documentStatus:null,baseline:null,reprogramada:null,...o});
const doc=(projectId:string,codigo:string,o:Record<string,unknown>={})=>({projectId,codigo,siglaDoc3:"XA",disciplinaNorma:projectId===P1?"Disciplina Um":"Disciplina Dois",
 isActivity:false,cancelado:false,emitido:false,statusInterno:null,hhPrevisto:null,custoPrevisto:null,dataAlvo:null,gates:[],...o});
const reports=[
 {projectId:P1,nomeProjeto:projetos[0].name,tipoLdp:2,documentos:[
  doc(P1,"D1",{hhPrevisto:100,custoPrevisto:1000,gates:[gate(30,{emitido:true,esforcoHoras:40,documentStatus:"APPROVED",baseline:"2026-10-19"}),gate(60,{esforcoHoras:50,documentStatus:"ELABORAÇÃO",baseline:"2026-10-20"})]}),
  doc(P1,"D2",{hhPrevisto:20,custoPrevisto:200,statusInterno:"VERIFICAÇÃO",gates:[]})],
  hhRealizado:{total:70,custoOficial:false,custoTotal:null,porDisciplina:[{disciplina:"Disciplina Um",hh:70,custo:null}]}},
 {projectId:P2,nomeProjeto:projetos[1].name,tipoLdp:1,documentos:[
  doc(P2,"E1",{hhPrevisto:30,custoPrevisto:300,emitido:true,dataAlvo:"2026-10-21"})],
  hhRealizado:{total:12,custoOficial:false,custoTotal:null,porDisciplina:[{disciplina:"Disciplina Dois",hh:12,custo:null}]}},
] as never[];

test("Ordem das fases: gates numéricos, textuais, Sem fase por último",()=>{
 assert.deepEqual(["FASE 90%","BASIC",SEM_FASE,"FASE 30%","DETAIL"].sort(ordemFase),["FASE 30%","FASE 90%","BASIC","DETAIL",SEM_FASE]);
});

test("Documentação: projeto → fase (gate real) → disciplina original; total do projeto conta cada documento uma vez",()=>{
 const a=agruparDocumentacao(reports,cat);
 assert.deepEqual(a.projetos.map(p=>p.codigo),["TST901","TST902"]);
 const p1=a.projetos[0];
 assert.deepEqual(p1.fases.map(f=>f.fase),["FASE 30%","FASE 60%",SEM_FASE_ROTULO]);
 assert.equal(p1.fases[0].totais.emitidos,1);              // situação DO GATE 30
 assert.equal(p1.fases[1].totais.emFluxo,1);                // gate 60 com Document_Status
 assert.equal(p1.fases[2].totais.emFluxo,1);                // D2 sem gate → Sem fase definida
 assert.equal(p1.totais.total,2);                           // D1 em 2 fases, mas 1 documento
 assert.ok(p1.notas.some(n=>/uma vez/.test(n)));
 // Disciplina ORIGINAL do projeto (sem normalização e sem sufixo de projeto).
 assert.equal(p1.fases[0].disciplinas[0].disciplina,"Disciplina Um");
 const p2=a.projetos[1];
 assert.deepEqual(p2.fases.map(f=>f.chave),[SEM_FASE]);
 assert.equal(p2.fases[0].disciplinas[0].disciplina,"Disciplina Dois");
 assert.equal(a.consolidado.total,3);
});

test("Indicadores: HH por gate, realizado em Sem fase, total oficial e diferença explícita (nunca rateio)",()=>{
 const a=agruparIndicadores(reports,cat);
 const p1=a.projetos.find(p=>p.codigo==="TST901")!;
 assert.equal(p1.fases.find(f=>f.fase==="FASE 30%")!.totais.hhPrevisto,40);
 assert.equal(p1.fases.find(f=>f.fase==="FASE 60%")!.totais.hhPrevisto,50);
 const sem=p1.fases.find(f=>f.semFase)!;
 assert.equal(sem.totais.hhPrevisto,20);
 assert.equal(sem.totais.hhRealizado,70);
 assert.equal(p1.fases.find(f=>f.fase==="FASE 30%")!.totais.hhRealizado,null);
 // Totais do projeto vêm dos cards oficiais; o que não cabe em fase aparece como diferença explícita.
 if(p1.totais.hhPrevisto!==null&&p1.totais.hhPrevisto!==110)assert.ok(p1.naoAtribuido);
 const p2=a.projetos.find(p=>p.codigo==="TST902")!;
 assert.equal(p2.fases.length,1);
 assert.equal(p2.fases[0].totais.hhRealizado,12);
 // %HH recalculado pela razão, nunca média de percentuais.
 assert.equal(p2.fases[0].totais.consumoHh,40);
});

test("Entregas da semana: gate como fase, Tipo 1 em Sem fase, projetos não se misturam",()=>{
 const a=agruparEntregas(reports,cat,"2026-10-22");
 const p1=a.projetos.find(p=>p.codigo==="TST901")!,p2=a.projetos.find(p=>p.codigo==="TST902")!;
 assert.deepEqual(p1.fases.map(f=>[f.fase,f.totais.previstos,f.totais.emitidos,f.totais.atrasados]),[["FASE 30%",1,1,0],["FASE 60%",1,0,1]]);
 assert.deepEqual(p2.fases.map(f=>[f.fase,f.totais.previstos,f.totais.emitidos]),[[SEM_FASE_ROTULO,1,1]]);
 assert.equal(a.consolidado.previstos,3);
 assert.equal(Math.round(a.consolidado.aderencia!),67);
});

test("Programação: fase = Weekly_Phase real; vazio → Sem fase; folha com atividades identificadas",()=>{
 const linha=(idRegistro:number,fase:string|null,data:string,documento="DOC-1")=>({idRegistro,documento,tarefa:"Tarefa teste",disciplinaWeekly:null,siglaDoc3:"XA",
  disciplinaNorma:"Disciplina Um",disciplinaCanonica:null,fonteDisciplina:"t",elaborador:"A",verificador:null,aprovador:null,hhElaboracao:2,hhVerificacao:1,hhAprovacao:null,
  baseline:null,reprogramado:null,dataAtividade:data,status:null,fase});
 const dados:ProgramacaoResposta[]=[{projectId:P1,inicio:"2026-10-18",fim:"2026-10-24",ultimaSincronizacao:null,registrosProjeto:3,registrosSemData:0,datasComProgramacao:[],
  linhas:[linha(1," basic  ","2026-10-19"),linha(2,null,"2026-10-20","DOC-2"),linha(3," basic  ","2026-10-19")]}];
 const a=agruparProgramacao(projetos,dados,cat,null);
 assert.equal(a.projetos.length,1); // P2 sem programação não aparece com zeros inventados
 const p=a.projetos[0];
 assert.deepEqual(p.fases.map(f=>[f.fase,f.totais.atividades,f.totais.hh]),[["basic",1,3],[SEM_FASE_ROTULO,1,3]]); // linha 3 repetida (dedup)
 assert.equal(p.fases[0].detalhes[0].documento,"DOC-1");
 assert.equal(agruparProgramacao(projetos,dados,cat,"2026-10-20").projetos[0].fases.length,1);
});

test("Componente: renderiza hierarquia identificada, botões acessíveis e Sem fase definida",async()=>{
 const React=await import("react");
 const {renderToStaticMarkup}=await import("react-dom/server");
 const {default:ProjetoFaseAgrupamento}=await import("../projetoFase/ProjetoFaseAgrupamento");
 const html=renderToStaticMarkup(React.createElement(ProjetoFaseAgrupamento,{titulo:"DOCUMENTAÇÃO — POR PROJETO E FASE",idBase:"t",agrupamento:agruparDocumentacao(reports,cat)}));
 assert.match(html,/aria-expanded="true"/);
 assert.match(html,/TST901/);
 assert.match(html,/TST902/);
 assert.match(html,/FASE 30%/);
 assert.match(html,/Sem fase definida/);
 assert.match(html,/LDP TIPO 2/);
 assert.doesNotMatch(html,/Teste Canônica/); // por projeto: disciplina original, nunca o nome normalizado
});
