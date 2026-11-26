import {test} from "node:test";
import assert from "node:assert/strict";
import {criarCatalogo,rotulo} from "../gestaoVistaJamileLogic";
import {criarNormalizador,type RegraSigla} from "@/features/disciplinas/normalizadorSiglas";
import {readFileSync} from "node:fs";
import path from "node:path";
import {analisarDocumentacao,completarFluxos,resolverEtapa,uniaoEtapas,type DocFluxo} from "../doc03/documentacaoFluxoLogic";
import type {EffectiveProjectFlow,EffectiveStage,ReviewFlowBinding} from "../doc03/reviewFlowsApi";
import {mover,paraDadosFluxo,paraDadosVinculo,situacaoDoProjeto,validarFluxo,validarVinculo} from "@/features/planning/fluxos-revisao/fluxosRevisaoLogic";
import {PERMISSOES_FLUXOS_REVISAO,ROTA_FLUXOS_REVISAO,ROTA_FLUXOS_REVISAO_ANTIGA} from "@/features/planning/fluxos-revisao/fluxosRevisaoRotas";
import {ROUTE_PERMISSIONS} from "@/features/permissions/permissionLogic";

const NORMA=(projectId:string)=>({projectId,normaId:5,normaNome:"BML011 - BR",diagnosticos:[],siglas:[
 {sigla:"EL",disciplina:"ELETRICA",canonica:"ELECTRICAL"},{sigla:"TU",disciplina:"TUBULAÇÃO",canonica:"PIPING"}]});
const etapa=(name:string,order:number,extra:Partial<EffectiveStage>={}):EffectiveStage=>({id:name,name,order,applicable:true,statusInFlowValues:[name],...extra});
const fluxo=(projectId:string,stages:EffectiveStage[]):EffectiveProjectFlow=>({projectId,flowId:"F-"+projectId,flowName:"Fluxo "+projectId,source:"project",stages});
let n=0;
const doc=(projectId:string,codigo:string|null,status:string|null,extra:Partial<DocFluxo>={}):DocFluxo=>({rowId:"r"+(++n),projectId,codigo,statusInterno:status,
 emitido:false,cancelado:false,isActivity:false,disciplina:null,disciplinaNorma:"ELETRICA",siglaDoc3:"EL",...extra});

const RAIZ=process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp");
const fonte=(rel:string)=>readFileSync(path.resolve(RAIZ,rel),"utf8");

test("03: API indisponível = nenhuma etapa presumida (só DISCIPLINA e TOTAL); a tela não tem fluxo de referência",()=>{
 const cat=criarCatalogo([NORMA("P1")]);
 const a=analisarDocumentacao({cat,fluxos:[],docs:[doc("P1","A-1","Verificação")]});
 assert.deepEqual(a.colunas,[]);
 assert.equal(a.total.total,0);
 assert.equal(a.emFluxo,0);
 const tela=fonte("src/features/production/analytics/doc03/Documentacao03.tsx");
 const logica=fonte("src/features/production/analytics/doc03/documentacaoFluxoLogic.ts");
 assert.ok(!/REFER[ÊE]NCIA/i.test(tela+logica),"sem fluxo de referência");
 assert.match(tela,/setEstado\(\{fluxos:\[\],erro:msg,carregando:false\}\)/);
 assert.match(tela,/PENDENTE DE CONFIGURAÇÃO/);
});

test("03: união multiprojeto ordenada, estável e sem nomes duplicados (acento/caixa)",()=>{
 const a=fluxo("P1",[etapa("Verificação",1),etapa("Aprovação",2),etapa("Qualidade",3)]);
 const b=fluxo("P2",[etapa("VERIFICACAO",1),etapa("Autorização",2),etapa("Aprovação",3),etapa("Cliente",9)]);
 assert.deepEqual(uniaoEtapas([a,b]).map(c=>c.nome),["Verificação","Autorização","Aprovação","Qualidade","Cliente"]);
 // Mesma entrada = mesma saída (estável).
 assert.deepEqual(uniaoEtapas([a,b]),uniaoEtapas([a,b]));
});

test("03: Status_In_Flow desconhecido NÃO vira Verificação; alias e prefixo 'Em' resolvem",()=>{
 const f=fluxo("P1",[etapa("VERIFICAÇÃO",1,{statusInFlowValues:["Checking","Em Verificação"]}),etapa("APROVAÇÃO",2)]);
 assert.equal(resolverEtapa(f,"Em Verificação"),"VERIFICACAO");
 assert.equal(resolverEtapa(f,"checking"),"VERIFICACAO");
 assert.equal(resolverEtapa(f,"Elaboração"),null);
 assert.equal(resolverEtapa(f,"Verificação técnica pendente"),null);
 assert.equal(resolverEtapa(f,"VERIFICAÇÃO"),null); // nome oficial isolado não é alias
 assert.equal(resolverEtapa(f,null),null);
 // Etapa não aplicável ao projeto não recebe documento.
 assert.equal(resolverEtapa(fluxo("P1",[etapa("APROVAÇÃO",1,{applicable:false})]),"Aprovação"),null);
});

test("03: tabela por disciplina com N/A (não aplicável) × 0 (aplicável sem documentos) e revisões contadas uma vez",()=>{
 const cat=criarCatalogo([NORMA("P1"),NORMA("P2")]);
 const fluxos=[fluxo("P1",[etapa("VERIFICAÇÃO",1),etapa("APROVAÇÃO",2)]),fluxo("P2",[etapa("VERIFICAÇÃO",1),etapa("QUALIDADE",2)])];
 const docs=[
  doc("P1","A-1","Verificação"),
  doc("P1","A-1","Verificação"),                       // revisão repetida do mesmo documento
  doc("P1","A-2","Elaboração"),                        // fora do fluxo
  doc("P1","A-3","Aprovação",{emitido:true}),          // emitido: fora das etapas
  doc("P1","A-4","Aprovação",{cancelado:true}),        // cancelado: fora de tudo
  doc("P1","A-5","Aprovação",{isActivity:true}),       // AT
  doc("P2","B-1","Qualidade",{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU"}),
 ];
 const a=analisarDocumentacao({docs,fluxos,cat});
 assert.deepEqual(a.colunas.map(c=>c.nome),["VERIFICAÇÃO","APROVAÇÃO","QUALIDADE"]);
 assert.equal(a.total.total,2);           // só A-1 e B-1 estão em etapas aplicáveis
 assert.equal(a.emFluxo,2);               // A-1 (Verificação), B-1 (Qualidade)
 assert.equal(a.repetidos,1);             // linha extra de A-1
 const el=a.linhas.find(l=>l.nome==="ELETRICA")!;
 const tu=a.linhas.find(l=>l.nome==="TUBULAÇÃO")!;
 assert.ok(el&&tu,a.linhas.map(l=>l.nome).join());
 assert.deepEqual(el.etapas,{VERIFICACAO:1,APROVACAO:0,QUALIDADE:null});
 assert.deepEqual(tu.etapas,{VERIFICACAO:0,APROVACAO:null,QUALIDADE:1});
 assert.deepEqual(a.total.etapas,{VERIFICACAO:1,APROVACAO:0,QUALIDADE:1});
 assert.equal(el.total,1); assert.equal(tu.total,1);
 assert.equal(a.total.total,a.linhas.reduce((n,l)=>n+l.total,0));
 assert.equal(a.total.total,a.emFluxo);
 assert.deepEqual(a.foraDoFluxo,[{status:"Elaboração",quantidade:1}]);
 assert.equal(a.cancelados,1);
 assert.equal(rotulo(cat,el.chave),"ELETRICA");
});

test("03: revisão em fluxo prevalece sobre revisão anterior emitida do mesmo documento",()=>{
 const cat=criarCatalogo([NORMA("P1")]);
 const a=analisarDocumentacao({cat,fluxos:[fluxo("P1",[etapa("APROVAÇÃO",1)])],docs:[doc("P1","C-1","Aprovação",{emitido:true}),doc("P1","c-1 ","Aprovação")]});
 assert.equal(a.total.total,1);
 assert.equal(a.emFluxo,1);
});

test("03: projeto ausente na resposta da API = sem fluxo (todas as etapas N/A para ele), nada inventado",()=>{
 const cat=criarCatalogo([NORMA("P1"),NORMA("P2")]);
 const fluxos=completarFluxos(["P1","P2"],[fluxo("P1",[etapa("VERIFICAÇÃO",1)])]);
 assert.equal(fluxos[1].source,"none");
 const a=analisarDocumentacao({cat,fluxos,docs:[doc("P2","D-1","Verificação",{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU"})]});
 assert.deepEqual(a.projetosSemFluxo,["P2"]);
 assert.equal(a.emFluxo,0);
 assert.equal(a.linhas[0].total,0);
 assert.deepEqual(a.linhas[0].etapas,{VERIFICACAO:null});
 assert.deepEqual(a.total.etapas,{VERIFICACAO:0});
});

test("Configuração de fluxos: ordem = posição, valores deduplicados, nomes e valores ambíguos rejeitados",()=>{
 const d=paraDadosFluxo(" Padrão ",true,[{name:"Verificação",valores:"Em Verificação; Verification;Verification"},{name:"Aprovação",valores:""}]);
 assert.deepEqual(d.stages.map(s=>[s.name,s.order,s.statusInFlowValues]),[["Verificação",1,["Em Verificação","Verification"]],["Aprovação",2,[]]]);
 assert.deepEqual(validarFluxo(d),[]);
 // Valor igual ao próprio nome da etapa não é ambíguo.
 assert.deepEqual(validarFluxo(paraDadosFluxo("F",true,[{name:"Qualidade",valores:"QUALIDADE"}])),[]);
 assert.equal(validarFluxo(paraDadosFluxo("F",true,[{name:"Aprovação",valores:""},{name:"APROVACAO",valores:""}])).length,1);
 assert.equal(validarFluxo(paraDadosFluxo("F",true,[{name:"A",valores:"X"},{name:"B",valores:"x"}])).length,1);
 assert.ok(validarFluxo(paraDadosFluxo("",true,[])).length>=2);
 assert.deepEqual(mover(["a","b","c"],0,1),["b","a","c"]);
 assert.deepEqual(mover(["a","b"],0,-1),["a","b"]);
});

const A="b.11111111-1111-1111-1111-111111111111",B="b.22222222-2222-2222-2222-222222222222",C="b.33333333-3333-3333-3333-333333333333";
const vinc=(id:string,scope:"project"|"group",x:{projectId?:string;groupName?:string;projectIds?:string[];flowId?:string}):ReviewFlowBinding=>
 ({id,flowId:x.flowId??"F1",scope,projectId:x.projectId??null,groupName:x.groupName??null,projectIds:x.projectIds??[],version:1});

test("Vínculos: projeto, grupo de projetos e unicidade (um direto por projeto, projeto em um grupo só, nome de grupo único)",()=>{
 assert.equal(validarVinculo(paraDadosVinculo({flowId:"1",scope:"project",projectId:"P1",groupName:null,projectIds:[]})).length,1);
 assert.deepEqual(validarVinculo(paraDadosVinculo({flowId:"1",scope:"project",projectId:A,groupName:"x",projectIds:[B]})),[]);
 // Corpo limpo por escopo.
 assert.deepEqual(paraDadosVinculo({flowId:"1",scope:"group",projectId:A,groupName:" Petrobras ",projectIds:[A,B,A]}),
  {flowId:"1",scope:"group",projectId:null,groupName:"Petrobras",projectIds:[A,B]});
 assert.deepEqual(validarVinculo(paraDadosVinculo({flowId:"1",scope:"group",projectId:null,groupName:"Petrobras",projectIds:[A]})),[]);
 assert.ok(validarVinculo(paraDadosVinculo({flowId:"1",scope:"group",projectId:null,groupName:"Petrobras",projectIds:[]})).some(e=>/ao menos um projeto/.test(e)));
 const existentes=[vinc("1","project",{projectId:A}),vinc("2","group",{groupName:"Petrobras",projectIds:[A,B]})];
 assert.ok(validarVinculo({flowId:"1",scope:"project",projectId:A,groupName:null,projectIds:[]},existentes).some(e=>/vínculo direto/.test(e)));
 assert.deepEqual(validarVinculo({flowId:"2",scope:"project",projectId:A,groupName:null,projectIds:[]},existentes,"1"),[]);   // edição do próprio
 assert.ok(validarVinculo({flowId:"1",scope:"group",projectId:null,groupName:"PETROBRÁS",projectIds:[C]},existentes).some(e=>/grupo com esse nome/.test(e)));
 assert.ok(validarVinculo({flowId:"1",scope:"group",projectId:null,groupName:"Vale",projectIds:[B,C]},existentes,undefined,id=>id===B?"BML011":id)
  .includes('BML011 já pertence ao grupo "Petrobras".'));
 // Projeto com vínculo direto pode estar num grupo (o direto prevalece).
 assert.deepEqual(validarVinculo({flowId:"1",scope:"group",projectId:null,groupName:"Vale",projectIds:[C]},[...existentes,vinc("3","project",{projectId:C})]),[]);
});

test("Fluxo efetivo: vínculo do projeto › grupo › PENDENTE DE CONFIGURAÇÃO (nada inventado)",()=>{
 const vs=[vinc("2","group",{groupName:"Petrobras",projectIds:[A,B],flowId:"G"}),vinc("1","project",{projectId:A,flowId:"P"})];
 const sa=situacaoDoProjeto(A,vs),sb=situacaoDoProjeto(B,vs),sc=situacaoDoProjeto(C,vs);
 assert.equal(sa.tipo,"projeto");assert.equal(sa.tipo==="projeto"&&sa.binding.flowId,"P");
 assert.equal(sb.tipo,"grupo");assert.equal(sb.tipo==="grupo"&&sb.binding.groupName,"Petrobras");
 assert.equal(sc.tipo,"pendente");
});

test("Fluxos de Revisão fica no Painel de Planejamento, com permissões do Planejamento; rota antiga só redireciona",()=>{
 assert.equal(ROTA_FLUXOS_REVISAO,"/planejamento/fluxos-de-revisao");
 assert.deepEqual(PERMISSOES_FLUXOS_REVISAO,{ver:"planning.review-flows.view",editar:"planning.coordination.edit"});
 const regra=(p:string)=>ROUTE_PERMISSIONS.find(r=>r.pattern===p)?.keys;
 assert.deepEqual(regra(ROTA_FLUXOS_REVISAO),["planning.review-flows.view"]);
 assert.deepEqual(regra(ROTA_FLUXOS_REVISAO_ANTIGA),["planning.review-flows.view"]);
 const catalogo=JSON.parse(fonte("src/features/sidebar/catalog.json")) as {id:string;parentId:string|null;permissionKeys:string[]}[];
 const item=catalogo.find(n=>n.id==="page:"+ROTA_FLUXOS_REVISAO)!;
 assert.equal(item.parentId,"group:Painel de Planejamento");
 assert.deepEqual(item.permissionKeys,["planning.review-flows.view"]);
 assert.ok(!catalogo.some(n=>n.id.includes("fluxos-revisao")&&n.id!==item.id),"nenhum item em Configurações");
 assert.match(fonte("src/App.tsx"),/<Route path=\{ROTA_FLUXOS_REVISAO_ANTIGA\} element=\{<Navigate to=\{ROTA_FLUXOS_REVISAO\} replace \/>\} \/>/);
 const pagina=fonte("src/features/planning/fluxos-revisao/FluxosRevisaoPage.tsx");
 assert.match(pagina,/const podeEditar = can\(PERMISSOES_FLUXOS_REVISAO\.editar\)/);
 assert.ok(!/settings\.projects\.edit/.test(pagina),"sem permissão administrativa de Configurações");
});

test("03: aliases bilíngues explícitos resolvem por projeto e exibem sempre o nome oficial",()=>{
 const a=fluxo("P1",[etapa("QUALIDADE",1,{statusInFlowValues:["QUALITY","Qualidade"]}),
   etapa("VERIFICADOR",2,{statusInFlowValues:["CHECKER","Verificador"]})]);
 const b=fluxo("P2",[etapa("REVISÃO",1,{statusInFlowValues:["CHECKER"]})]);
 assert.equal(resolverEtapa(a," QUALITY "),"QUALIDADE");
 assert.equal(resolverEtapa(a,"Qualidade"),"QUALIDADE");
 assert.equal(resolverEtapa(a,"checker"),"VERIFICADOR");
 assert.equal(resolverEtapa(a,"Verificador"),"VERIFICADOR");
 assert.equal(resolverEtapa(b,"CHECKER"),"REVISAO");
 assert.equal(resolverEtapa(a,"VERIFICATION"),null);
 assert.deepEqual(uniaoEtapas([a,b]).map(c=>c.nome),["QUALIDADE","VERIFICADOR","REVISÃO"]);
 const cat=criarCatalogo([NORMA("P1"),NORMA("P2")]);
 const result=analisarDocumentacao({cat,fluxos:[a,b],docs:[doc("P1","A1","QUALITY"),doc("P2","B1","CHECKER",{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU"})]});
 assert.equal(result.linhas.find(l=>l.nome==="ELETRICA")!.etapas.REVISAO,null);
 assert.equal(result.linhas.find(l=>l.nome==="TUBULAÇÃO")!.etapas.QUALIDADE,null);
 assert.equal(result.emFluxo,2);
});

test("Configuração: aliases duplicados ou ambíguos no mesmo fluxo são rejeitados",()=>{
 assert.ok(validarFluxo(paraDadosFluxo("F",true,[{name:"QUALIDADE",valores:"QUALITY; quality"}])).length);
 assert.ok(validarFluxo(paraDadosFluxo("F",true,[{name:"QUALIDADE",valores:"QUALITY"},{name:"VERIFICADOR",valores:" quality "}])).length);
 assert.deepEqual(validarFluxo(paraDadosFluxo("F",true,[{name:"QUALIDADE",valores:"QUALITY; Qualidade"},{name:"VERIFICADOR",valores:"CHECKER; Verificador"}])),[]);
});

test("03: DOCUMENTOS EM FLUXO soma só etapas aplicáveis, sem vazios, desconhecidos, AT ou revisões duplicadas",()=>{
 const cat=criarCatalogo([NORMA("P1"),NORMA("P2")]);
 const f1=fluxo("P1",[etapa("VERIFICADOR",1,{statusInFlowValues:["CHECKER","Verificador"]}),etapa("QUALIDADE",2,{statusInFlowValues:["QUALITY","Qualidade"]})]);
 const f2=fluxo("P2",[etapa("APROVAÇÃO",1,{statusInFlowValues:["APPROVAL"]})]);
 const docs=[
  doc("P1","A","CHECKER"),doc("P1","A","Verificador"),
  doc("P1","B","QUALITY"),doc("P1","C",null),doc("P1","D","UNKNOWN"),
  doc("P1","E","QUALITY",{isActivity:true}),doc("P1","F","QUALITY",{emitido:true}),
  doc("P2","G","APPROVAL",{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU"}),
  doc("P2","H","CHECKER",{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU"}),
 ];
 const a=analisarDocumentacao({docs,fluxos:[f1,f2],cat});
 const el=a.linhas.find(l=>l.nome==="ELETRICA")!,tu=a.linhas.find(l=>l.nome==="TUBULAÇÃO")!;
 assert.equal(el.total,2); assert.equal(tu.total,1);
 assert.deepEqual(el.etapas,{VERIFICADOR:1,QUALIDADE:1,APROVACAO:null});
 assert.deepEqual(tu.etapas,{VERIFICADOR:null,QUALIDADE:null,APROVACAO:1});
 assert.equal(a.total.total,3); assert.equal(a.emFluxo,3);
 assert.equal(a.total.total,a.linhas.reduce((n,l)=>n+l.total,0));
 for(const l of [...a.linhas,a.total]) assert.equal(l.total,Object.values(l.etapas).reduce<number>((n,v)=>n+(v??0),0));
 assert.equal(a.repetidos,1);
 const tela=fonte("src/features/production/analytics/doc03/Documentacao03.tsx");
 assert.match(tela,/<th>DOCUMENTOS EM FLUXO<\/th>/);
 assert.ok(!tela.includes("TOTAL DE DOCUMENTOS"));
});

test("03: BML011.021 — as 39 linhas '----' (regra oficial do backend) ficam fora de total, SEM DISCIPLINA e indicadores",()=>{
 const cat=criarCatalogo([NORMA("BML")]);
 const fl=fluxo("BML",[etapa("Elaboração",1),etapa("Verificação",2)]);
 const reais=[
  ...Array.from({length:3},(_,i)=>doc("BML",`BML011.021-204-EL${i}-DR-001`,"Elaboração")),
  ...Array.from({length:3},(_,i)=>doc("BML",`BML011.021-204-EL${i}-CS-001`,"Verificação")),
  doc("BML","BML011.021-204-TU0-SC-001",null,{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU0",emitido:true}),
  doc("BML","BML011.021-204-TU0-LD-001",null,{disciplinaNorma:"TUBULAÇÃO",siglaDoc3:"TU0",emitido:true})];
 const at=Array.from({length:4},(_,i)=>doc("BML",`BML011.021-000-EL0-AT-00${i}`,null,{isActivity:true}));
 const vazias=Array.from({length:39},()=>doc("BML","----",null,{disciplinaNorma:null,siglaDoc3:null,semConteudoDocumental:true}));
 const a=analisarDocumentacao({cat,fluxos:[fl],docs:[...reais,...at,...vazias]});
 assert.equal(a.naoDocumentais,39);
 assert.equal(a.repetidos,0,"as 39 não viram 'revisões' de um documento '----'");
 assert.equal(a.emFluxo,6);
 assert.equal(a.emitidos,2);
 assert.equal(a.semStatus,0);
 assert.ok(!a.linhas.some(l=>rotulo(cat,l.chave)==="SEM DISCIPLINA"),"sem linha SEM DISCIPLINA");
 // Documento real sem número (sem a marca do backend) continua auditável em SEM DISCIPLINA.
 const b=analisarDocumentacao({cat,fluxos:[fl],docs:[...reais,doc("BML","----","Elaboração",{disciplinaNorma:null,siglaDoc3:null})]});
 assert.equal(b.naoDocumentais,0);
 assert.ok(b.linhas.some(l=>l.nome==="SEM DISCIPLINA"&&l.total===1));
});

test("03: DOC3 fora da Norma do projeto resolve pelo Normalizador (VA5→HVAC, TU4→TUBULAÇÃO); sem regra fica SEM DISCIPLINA auditável",()=>{
 const regra=(id:number,siglaOriginal:string,disciplinaCanonica:string):RegraSigla=>({id,siglaOriginal,disciplinaCanonica,contextoTipo:null,contextoValor:null,
  ativo:true,origem:"IMPORTACAO",importacaoId:1,versao:1,criadoEm:"",criadoPor:"",alteradoEm:"",alteradoPor:""});
 const projetos=[{id:"E17",name:"BML011.017 - ELETROCENTER"},{id:"T03",name:"TIM003 - AS BULT"}];
 const cat=criarCatalogo([NORMA("E17"),NORMA("T03")],projetos,criarNormalizador([regra(283,"VA5","HVAC"),regra(342,"TU4","TUBULAÇÃO")]));
 const fl=[fluxo("E17",[etapa("Elaboração",1)]),fluxo("T03",[etapa("Elaboração",1)])];
 const docs=[doc("E17","BML011.017-211-VA5-MC-001",null,{disciplinaNorma:null,siglaDoc3:"VA5",disciplinaCanonica:"HVAC",emitido:true}),
  doc("T03","TIM001.01-000-TU4-DE-001",null,{disciplinaNorma:null,siglaDoc3:"TU4",disciplinaCanonica:"PIPING"}),
  doc("T03","TIM001.01-000-ZZ1-DE-001",null,{disciplinaNorma:null,siglaDoc3:"ZZ1"})];
 const nomes=analisarDocumentacao({cat,fluxos:fl,docs}).linhas.map(l=>l.nome);
 assert.ok(nomes.includes("HVAC"));
 assert.ok(nomes.includes("TUBULAÇÃO"));
 assert.ok(nomes.includes("SEM DISCIPLINA"),"DOC3 sem Norma nem regra continua pendência real");
});
