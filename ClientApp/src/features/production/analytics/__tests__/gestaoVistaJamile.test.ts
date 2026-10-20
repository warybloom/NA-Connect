import {test} from "node:test";
import {readFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import {criarCatalogo,classificarDocumento,classificarRealizado,rotulo,performancePorDisciplina,entregasDaSemana,recursosDistintos,hhLinha,SEM_DISCIPLINA,FASES,faseContagem,faseHhPrevisto,faseHhRealizado,fasePrazos,faseTotalDocumentos,semanaDoCorte,deduplicarProgramacao,filtrarDia,atividadesPorDia,diaDaSemanaCurto,semanasVizinhasComProgramacao} from "../gestaoVistaJamileLogic";
import {encontrarFolha,navProducao} from "../../productionRoutes";
test("Quatro telas visuais correspondem às quatro páginas do PDF e fazem parte de GESTÃO À VISTA",()=>{
 const group=navProducao().find(x=>x.kind==="group"&&x.label==="GESTÃO À VISTA");
 assert.ok(group&&group.kind==="group");
 assert.deepEqual(group.children.slice(0,4).map(x=>x.label),["01. VISÃO GERAL","02. PROGRAMAÇÃO","03. DOCUMENTAÇÃO","04. INDICADORES"]);
 assert.ok(encontrarFolha("gestao-a-vista/hh-disciplina"));
 assert.ok(encontrarFolha("gestao-a-vista/custo-disciplina"));
});
test("Gates oficiais contados sem multiplicar documentos e sem inventar HH realizado",()=>{
 const report={tipoLdp:2,documentos:[
  {isActivity:false,gates:[{percentual:30,ativo:true,cancelado:false,esforcoHoras:20},{percentual:60,ativo:true,cancelado:false,esforcoHoras:30}]},
  {isActivity:false,gates:[{percentual:30,ativo:true,cancelado:false,esforcoHoras:50},{percentual:90,ativo:false,cancelado:false,esforcoHoras:10}]},
  {isActivity:true,gates:[{percentual:30,ativo:true,cancelado:false,esforcoHoras:80}]}
 ]};
 const phases=faseTotalDocumentos([report] as never);
 assert.deepEqual(phases,{30:2,60:1,90:0});
 const planned=faseHhPrevisto([report] as never);
 assert.deepEqual(planned,{30:70,60:30,90:null});
 assert.deepEqual(faseHhRealizado(),{30:null,60:null,90:null});
});
test("Matriz LDP é fonte da contagem oficial emitida/cancelada por fase",()=>{
 const p={tipoLdp:2,celulas:[
  {row:"emitidos",column:"30",value:7},{row:"emitidos",column:"60",value:5},
  {row:"cancelados",column:"30",value:2}
 ]};
 assert.deepEqual(faseContagem([p] as never,"emitidos"),{30:7,60:5,90:null});
 assert.deepEqual(faseContagem([p] as never,"cancelados"),{30:2,60:null,90:null});
});
test("Semana segue a regra oficial do backend: domingo a sábado",()=>{
 assert.deepEqual(semanaDoCorte("2026-10-14"),{start:"2026-10-11",end:"2026-10-17"});
 assert.deepEqual(semanaDoCorte("2026-09-27"),{start:"2026-09-27",end:"2026-10-03"});
});

test("Atraso e no prazo respeitam datas próprias do gate e ignoram cancelado/aprovado",()=>{
 const report={tipoLdp:2,documentos:[{isActivity:false,gates:[
  {percentual:30,ativo:true,cancelado:false,emitido:false,reprogramada:"2026-10-10"},
  {percentual:60,ativo:true,cancelado:false,emitido:false,baseline:"2026-10-20"},
  {percentual:90,ativo:true,cancelado:false,emitido:false},
 ]},{isActivity:false,gates:[
  {percentual:30,ativo:true,cancelado:true,emitido:false,reprogramada:"2026-10-10"},
  {percentual:60,ativo:true,cancelado:false,emitido:true,reprogramada:"2026-10-10"},
 ]}]};
 assert.deepEqual(fasePrazos([report] as never,"2026-10-14"),{
  atraso:{30:1,60:0,90:null},noPrazo:{30:0,60:1,90:null}
 });
});

const NORMA_BR={projectId:"P1",normaId:5,normaNome:"BML011 - BR",diagnosticos:[],siglas:[
 {sigla:"EL",disciplina:"ELETRICA",canonica:"ELECTRICAL"},{sigla:"EL2",disciplina:"ELETRICA",canonica:"ELECTRICAL"},
 {sigla:"TU",disciplina:"TUBULAÇÃO",canonica:"PIPING"},{sigla:"TU1",disciplina:"PIPING",canonica:"PIPING"},
 {sigla:"PM",disciplina:"GERENTE DE PROJETO",canonica:null}]};
test("Disciplina exibida = nome da Norma do projeto, inclusive para o realizado agrupado pelo canônico",()=>{
 const cat=criarCatalogo([NORMA_BR]);
 const doc=classificarDocumento(cat,{projectId:"P1",disciplinaNorma:"ELETRICA",siglaDoc3:"EL2",codigo:"X-0-EL2-DR-1"});
 assert.equal(rotulo(cat,doc.chave),"ELETRICA");
 assert.equal(classificarRealizado(cat,"P1","ELECTRICAL").chave,doc.chave);
 // Mesma disciplina padrão com dois nomes na Norma: um único grupo, ambos os nomes visíveis (cadastro a corrigir).
 assert.equal(rotulo(cat,classificarRealizado(cat,"P1","PIPING").chave),"PIPING / TUBULAÇÃO");
 // Nome da Norma sem padrão global continua com o nome da Norma.
 assert.equal(rotulo(cat,classificarDocumento(cat,{projectId:"P1",disciplinaNorma:"GERENTE DE PROJETO",codigo:"X-0-PM-RT-1"}).chave),"GERENTE DE PROJETO");
 const sem=classificarDocumento(cat,{projectId:"P1",disciplinaNorma:null,siglaDoc3:"ZZ",codigo:"X-0-ZZ-1"});
 assert.equal(sem.chave,SEM_DISCIPLINA);assert.match(sem.motivo!,/ZZ/);
 assert.equal(classificarRealizado(cat,"P1","SEM DISCIPLINA").chave,SEM_DISCIPLINA);
});
test("Performance por disciplina reconcilia com os totais gerenciais sem esconder valores",()=>{
 const cat=criarCatalogo([NORMA_BR]);
 const report={projectId:"P1",nomeProjeto:"P1",tipoLdp:1,documentos:[
  {projectId:"P1",codigo:"X-0-EL2-DR-1",disciplinaNorma:"ELETRICA",siglaDoc3:"EL2",hhPrevisto:10,custoPrevisto:100,isActivity:false},
  {projectId:"P1",codigo:"X-0-ZZ-DR-1",disciplinaNorma:null,siglaDoc3:"ZZ",hhPrevisto:5,custoPrevisto:50,isActivity:false}],
  planning:{plannedHhTotal:20,plannedHhByDiscipline:[],plannedCostByDiscipline:[],documentPlannedCost:150},
  hhRealizado:{total:9,custoTotal:90,custoOficial:true,fonte:"t",porDisciplina:[{disciplina:"ELECTRICAL",hh:6,custo:60},{disciplina:"SEM DISCIPLINA",hh:3,custo:30}]}};
 const {rows}=performancePorDisciplina([report] as never,cat);
 const el=rows.find(r=>r.disciplina==="ELETRICA")!;
 assert.deepEqual([el.hhPrevisto,el.hhRealizado,el.custoPrevisto,el.custoRealizado],[10,6,100,60]);
 const sem=rows.find(r=>r.chave===SEM_DISCIPLINA)!;
 assert.deepEqual([sem.hhPrevisto,sem.hhRealizado],[5,3]);
 // Planejado oficial = 20 e documentos somam 15: a diferença aparece explícita, nunca escondida.
 const rec=rows.find(r=>r.reconciliacao)!;
 assert.equal(rec.hhPrevisto,5);assert.equal(rec.hhRealizado,null);
});
test("Entregas da semana: Tipo 2 conta cada gate com data própria; atraso relativo à data de corte",()=>{
 const cat=criarCatalogo([NORMA_BR]);
 const report={projectId:"P1",nomeProjeto:"P1",tipoLdp:2,documentos:[{projectId:"P1",codigo:"X-0-EL2-DR-1",disciplinaNorma:"ELETRICA",isActivity:false,gates:[
  {percentual:30,ativo:true,cancelado:false,emitido:true,reprogramada:"2026-10-12"},
  {percentual:60,ativo:true,cancelado:false,emitido:false,baseline:"2026-10-13"},
  {percentual:90,ativo:true,cancelado:false,emitido:false,reprogramada:"2026-10-16"},
  ]},{projectId:"P1",codigo:"X-0-EL2-DR-2",disciplinaNorma:"ELETRICA",isActivity:false,gates:[{percentual:30,ativo:true,cancelado:true,reprogramada:"2026-10-12"}]}]};
 const r=entregasDaSemana([report] as never,cat,"2026-10-14");
 assert.deepEqual([r.previstos,r.realizados,r.atrasados],[3,1,1]);
 assert.ok(Math.abs(r.aderencia!-100/3)<1e-9);assert.ok(Math.abs(r.desvio!-200/3)<1e-9);
});
test("Programação: HH = elaboração + verificação + aprovação; TBD não é recurso",()=>{
 assert.equal(hhLinha({hhElaboracao:4,hhVerificacao:.8,hhAprovacao:null}),4.8);
 assert.equal(recursosDistintos([{elaborador:"ANTONIO NETO",verificador:"ANDRÉ DIAS",aprovador:null},{elaborador:"André Dias",verificador:"TBD",aprovador:null}] as never),2);
});
test("Catálogo da Norma indisponível: previsto e realizado da mesma disciplina oficial ficam numa ÚNICA linha (canônico do backend)",()=>{
 const cat=criarCatalogo([]); // endpoint de disciplinas falhou
 const doc=classificarDocumento(cat,{projectId:"P9",disciplinaNorma:"PROCESSO",disciplinaCanonica:"PROCESS",siglaDoc3:"PC4",codigo:"P9-000-PC4-LI-001"});
 assert.equal(classificarRealizado(cat,"P9","PROCESS").chave,doc.chave);
 // sem canônico oficial não há junção por semelhança de texto
 const outro=classificarDocumento(cat,{projectId:"P9",disciplinaNorma:"PROCESSO",disciplinaCanonica:null,siglaDoc3:"PC4",codigo:"x"});
 assert.notEqual(classificarRealizado(cat,"P9","PROCESS").chave,outro.chave);
});
test("Tela 01: emissão por disciplina = emitidos ÷ previstos da semana; gate CLIENT REVIEW não é emitido; fração visível",()=>{
 const cat=criarCatalogo([]);
 const report={projectId:"P1",nomeProjeto:"P1",tipoLdp:2,documentos:[
  {isActivity:false,codigo:"A",disciplinaNorma:"PROCESSO",disciplinaCanonica:"PROCESS",gates:[{percentual:30,ativo:true,cancelado:false,emitido:true,baseline:"2026-08-10"}]},
  {isActivity:false,codigo:"B",disciplinaNorma:"PROCESSO",disciplinaCanonica:"PROCESS",gates:[{percentual:60,ativo:true,cancelado:false,emitido:false,documentStatus:"CLIENT REVIEW",baseline:"2026-08-11"}]},
 ]};
 const r=entregasDaSemana([report] as never,cat,"2026-08-12");
 assert.equal(r.previstos,2);assert.equal(r.realizados,1);assert.equal(r.aderencia,50);
 const fonte=readFileSync(path.resolve(process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp"),"src/features/production/analytics/GestaoVistaJamilePage.tsx"),"utf8");
 assert.ok(fonte.includes("fracao={`${x.actual} de ${x.planned}`}"));
});
test("Tela 03: setas em grade de 4 colunas iguais, limitada à largura, com texto escalado pelo contêiner",()=>{
 const css=readFileSync(path.resolve(process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp"),"src/features/production/analytics/gestaoVistaJamile.css"),"utf8");
 assert.match(css,/\.gv-flow\{[^}]*grid-template-columns:repeat\(4,minmax\(0,1fr\)\)[^}]*width:min\(100%,72em\)[^}]*container-type:inline-size/);
 assert.match(css,/\.gv-chevron\{[^}]*aspect-ratio:3\.1\/1/);
 assert.match(css,/\.gv-chevron b\{font-size:min\(1\.1em,2\.3cqi\)/);
 assert.doesNotMatch(css,/\.gv-chevron\{[^}]*flex:0 1 15em/);
});

const linhaProg=(idRegistro:number,dataAtividade:string,documento:string|null,extra:Record<string,unknown>={})=>({idRegistro,documento,tarefa:"ELABORAÇÃO",fase:"IFC",dataAtividade,
 disciplinaWeekly:null,siglaDoc3:null,disciplinaNorma:null,disciplinaCanonica:null,fonteDisciplina:"",elaborador:null,verificador:null,aprovador:null,
 hhElaboracao:1,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,status:null,projectId:"P1",...extra});
test("02 Programação semanal: semana domingo–sábado, uma atividade por documento/tarefa/fase/dia, repetições contadas uma vez",()=>{
 assert.deepEqual(semanaDoCorte("2026-10-15"),{start:"2026-10-11",end:"2026-10-17"});
 assert.deepEqual(semanaDoCorte("2026-10-11"),{start:"2026-10-11",end:"2026-10-17"});   // domingo abre a semana
 assert.deepEqual(semanaDoCorte("2026-10-17"),{start:"2026-10-11",end:"2026-10-17"});   // sábado fecha
 const linhas=[
  linhaProg(10,"2026-10-12T00:00:00","CHS-001"),
  linhaProg(11,"2026-10-12T00:00:00","CHS-001"),                          // mesma chave → repetição
  linhaProg(12,"2026-10-14T00:00:00","CHS-001"),                          // outro dia → atividade distinta
  linhaProg(13,"2026-10-12T00:00:00","CHS-001",{tarefa:"VERIFICAÇÃO"}),    // outra tarefa → distinta
  linhaProg(14,"2026-10-12T00:00:00","CHS-001",{projectId:"P2"}),          // outro projeto → distinta
  linhaProg(15,"2026-10-16T00:00:00",null),linhaProg(16,"2026-10-16T00:00:00",null), // sem documento: nunca descarta
 ];
 const r=deduplicarProgramacao(linhas);
 assert.equal(r.repetidos,1);
 assert.deepEqual(r.linhas.map(l=>l.idRegistro),[10,12,13,14,15,16]);
 assert.equal(r.linhas.reduce((s,l)=>s+(hhLinha(l)??0),0),6);             // HH semanal = soma dos registros distintos
 // Filtro opcional por dia: recorta a semana sem perdê-la.
 assert.deepEqual(filtrarDia(r.linhas,"2026-10-12").map(l=>l.idRegistro),[10,13,14]);
 assert.equal(filtrarDia(r.linhas,null).length,6);
 assert.deepEqual(atividadesPorDia(r.linhas,"2026-10-11").map(x=>`${x.dia} ${x.quantidade}`),["DOM 0","SEG 3","TER 0","QUA 1","QUI 0","SEX 2","SÁB 0"]);
 assert.equal(diaDaSemanaCurto("2026-10-16"),"SEX");
});
test("02 Programação semanal: semana vazia oferece a semana anterior/seguinte que têm programação",()=>{
 const datas=["2026-10-05T00:00:00","2026-10-07T00:00:00","2026-10-16T00:00:00"];
 assert.deepEqual(semanasVizinhasComProgramacao(datas,{start:"2026-09-27",end:"2026-10-03"}),{anterior:null,proxima:{start:"2026-10-04",end:"2026-10-10"}});
 assert.deepEqual(semanasVizinhasComProgramacao(datas,{start:"2026-10-18",end:"2026-10-24"}),{anterior:{start:"2026-10-11",end:"2026-10-17"},proxima:null});
 const pagina=readFileSync(path.resolve(process.cwd(),"src/features/production/analytics/GestaoVistaJamilePage.tsx"),"utf8");
 assert.match(pagina,/programacao:"PROGRAMAÇÃO SEMANAL"/);
 assert.match(pagina,/useProgramacao\(selection,week\.start,week\.end,screen==="programacao"\)/);   // uma consulta por projeto para a semana inteira
 // Item 3 (19/10/2026): a tabela "Semana Completa" é o componente isolado do Codex, montado pelo contêiner.
 assert.match(pagina,/<GerenciaEngenhariaItem3/);
 const item3=readFileSync(path.resolve(process.cwd(),"src/features/production/analytics/item3/GerenciaEngenhariaItem3.tsx"),"utf8");
 assert.match(item3,/"DIA", "DISCIPLINA", "CÓDIGO DO DOCUMENTO", "TÍTULO DO DOCUMENTO",\s*"ELABORADOR", "VERIFICADOR", "APROVADOR", "SITUAÇÃO", "HH PREVISTO"/);
});

/* ------------------------------------------------------------------ CONSOLIDADO (vários projetos no mesmo slide) */
const PROJ=[{id:"P1",name:"CHS001 - ETHANOL PLANT"},{id:"P2",name:"BML011 - COOLING TOWER"}];
const NORMA_P2={projectId:"P2",normaId:7,normaNome:"OUTRA NORMA",diagnosticos:[],siglas:[
 {sigla:"EL",disciplina:"ELÉTRICA",canonica:"ELECTRICAL"},{sigla:"PM",disciplina:"GERENTE DE PROJETO",canonica:null}]};
const relatorio=(projectId:string,nomeProjeto:string,tipoLdp:number,hp:number,hr:number,cp:number,cr:number,documentos:unknown[])=>({projectId,nomeProjeto,tipoLdp,documentos,
 planning:{plannedHhTotal:hp,plannedHhByDiscipline:[],plannedCostByDiscipline:[],documentPlannedCost:cp},
 hhRealizado:{total:hr,custoTotal:cr,custoOficial:true,fonte:"t",porDisciplina:[{disciplina:"ELECTRICAL",hh:hr,custo:cr}]}});
test("Consolidado: só a disciplina PADRÃO oficial une projetos; nome sem padrão fica separado por projeto (Norma de cada um)",()=>{
 const cat=criarCatalogo([NORMA_BR,NORMA_P2],PROJ);
 const el1=classificarDocumento(cat,{projectId:"P1",disciplinaNorma:"ELETRICA",codigo:"CHS001-EL-1"});
 const el2=classificarDocumento(cat,{projectId:"P2",disciplinaNorma:"ELÉTRICA",codigo:"BML011-EL-1"});
 assert.equal(el1.chave,el2.chave);                                     // ELECTRICAL nas duas Normas → um grupo
 assert.equal(rotulo(cat,el1.chave),"ELETRICA / ELÉTRICA");              // nomes de cada Norma preservados
 const pm1=classificarDocumento(cat,{projectId:"P1",disciplinaNorma:"GERENTE DE PROJETO",codigo:"CHS001-PM-1"});
 const pm2=classificarDocumento(cat,{projectId:"P2",disciplinaNorma:"GERENTE DE PROJETO",codigo:"BML011-PM-1"});
 assert.notEqual(pm1.chave,pm2.chave);                                  // mesmo texto, Normas diferentes, sem padrão: NÃO une
 assert.deepEqual([rotulo(cat,pm1.chave),rotulo(cat,pm2.chave)],["GERENTE DE PROJETO (CHS001)","GERENTE DE PROJETO (BML011)"]);
 const fora1=classificarRealizado(cat,"P1","AT MANUAL"),fora2=classificarRealizado(cat,"P2","AT MANUAL");
 assert.notEqual(fora1.chave,fora2.chave);
 // Um projeto só: comportamento e rótulos de sempre.
 const unico=criarCatalogo([NORMA_BR],[PROJ[0]]);
 assert.equal(rotulo(unico,classificarDocumento(unico,{projectId:"P1",disciplinaNorma:"GERENTE DE PROJETO",codigo:"x"}).chave),"GERENTE DE PROJETO");
});
test("Consolidado 01: aderência pelos TOTAIS (Tipo 2 por gate + Tipo 1 por documento), nunca média dos percentuais",()=>{
 const cat=criarCatalogo([NORMA_BR,NORMA_P2],PROJ);
 const t2=relatorio("P1","CHS001 - ETHANOL PLANT",2,0,0,0,0,[{projectId:"P1",codigo:"CHS001-EL-1",disciplinaNorma:"ELETRICA",isActivity:false,gates:[
  {percentual:30,ativo:true,cancelado:false,emitido:true,reprogramada:"2026-10-12"},
  {percentual:60,ativo:true,cancelado:false,emitido:false,baseline:"2026-10-13"},
  {percentual:90,ativo:true,cancelado:false,emitido:false,reprogramada:"2026-10-16"}]}]);
 const t1=relatorio("P2","BML011 - COOLING TOWER",1,0,0,0,0,[{projectId:"P2",codigo:"BML011-EL-1",disciplinaNorma:"ELÉTRICA",isActivity:false,cancelado:false,emitido:true,dataAlvo:"2026-10-15"}]);
 const r=entregasDaSemana([t2,t1] as never,cat,"2026-10-14");
 assert.deepEqual([r.previstos,r.realizados],[4,2]);
 assert.equal(r.aderencia,50);                                          // 2 ÷ 4 (a média de 33,3% e 100% daria 66,7%)
 assert.deepEqual(new Set(r.entregas.map(e=>e.projectId)),new Set(["P1","P2"]));   // vínculo com o projeto de origem
 // Documentação: fases 30/60/90 só dos projetos Tipo 2 da seleção; Tipo 1 entra nos totais, não nos gates.
 assert.deepEqual(faseTotalDocumentos([t2,t1] as never),{30:1,60:1,90:1});
});
test("Consolidado 04: HH/custo somados por projeto e %consumo pelos totais (ponderado)",()=>{
 const cat=criarCatalogo([NORMA_BR,NORMA_P2],PROJ);
 const p1=relatorio("P1","CHS001",1,10,5,100,40,[{projectId:"P1",codigo:"CHS001-EL-1",disciplinaNorma:"ELETRICA",hhPrevisto:10,custoPrevisto:100,isActivity:false}]);
 const p2=relatorio("P2","BML011",1,90,90,900,900,[{projectId:"P2",codigo:"BML011-EL-1",disciplinaNorma:"ELÉTRICA",hhPrevisto:90,custoPrevisto:900,isActivity:false}]);
 const {rows,cards}=performancePorDisciplina([p1,p2] as never,cat);
 const el=rows.find(x=>!x.reconciliacao&&x.chave!==SEM_DISCIPLINA)!;
 assert.deepEqual([el.hhPrevisto,el.hhRealizado,el.custoPrevisto,el.custoRealizado,el.documentos],[100,95,1000,940,2]);
 assert.equal(cards.HH_Previsto,100);assert.equal(cards.HH_Realizado,95);
 assert.ok(Math.abs(cards.ConsumoHH!-95)<1e-9);                         // 95 ÷ 100 (média simples de 50% e 100% daria 75%)
 assert.ok(Math.abs(cards.ConsumoCusto!-94)<1e-9);                      // 940 ÷ 1000
 assert.ok(!rows.some(x=>x.reconciliacao));                             // nada duplicado: linhas = totais oficiais
});
test("Consolidado 02: programação de projetos diferentes soma sem perder a origem; mesmo documento em projetos distintos não é repetição",()=>{
 const l=(projectId:string,id:number,doc:string,dia:string,hh:number)=>({idRegistro:id,documento:doc,tarefa:"ELAB",fase:"IFC",dataAtividade:dia,projectId,
  disciplinaWeekly:null,siglaDoc3:null,disciplinaNorma:null,disciplinaCanonica:null,fonteDisciplina:"",elaborador:"ANA",verificador:null,aprovador:null,
  hhElaboracao:hh,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,status:null});
 const r=deduplicarProgramacao([l("P1",1,"DOC-1","2026-10-12",4),l("P2",1,"DOC-1","2026-10-12",6),l("P2",2,"DOC-2","2026-10-15",2)]);
 assert.equal(r.repetidos,0);
 assert.equal(r.linhas.reduce((s,x)=>s+(hhLinha(x)??0),0),12);
 assert.deepEqual(r.linhas.map(x=>x.projectId).sort(),["P1","P2","P2"]);
});
test("Consolidado: falha em QUALQUER projeto bloqueia os números (sem total parcial) e lista os projetos afetados",()=>{
 const raiz=process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp");
 const pagina=readFileSync(path.resolve(raiz,"src/features/production/analytics/GestaoVistaJamilePage.tsx"),"utf8");
 assert.match(pagina,/const \{reports,falhas,loading,error,retry\}=usePmoReports/);
 assert.match(pagina,/else if\(falhasTela\.length>0\)content=<ConsolidacaoIncompleta/);
 // Catálogo com TODOS os projetos da seleção (o 3º argumento opcional é o Normalizador de Siglas do consolidado).
 assert.match(pagina,/criarCatalogo\(normas\.normas,selection(,normalizador)?\)/);
 assert.match(pagina,/useProgramacao\(selection,week\.start,week\.end/);
 const disc=readFileSync(path.resolve(raiz,"src/features/production/analytics/GestaoVistaDisciplinasPage.tsx"),"utf8");
 assert.match(disc,/if\(falhas\.length>0\)return <ReportState title=\{`Consolidação incompleta/);
 assert.ok(!/selection\[0\]/.test(pagina)&&!/selection\[0\]/.test(disc),"nenhuma tela usa só o primeiro projeto");
});
