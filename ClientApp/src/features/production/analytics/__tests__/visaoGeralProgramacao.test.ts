// 01. VISÃO GERAL (19/10/2026): mesma fonte semanal da 02 (programação por projeto), documentos distintos por projeto +
// código, todos os projetos selecionados, emissão conciliada com a LDP e nada de gates/fases.
import {test} from "node:test";
import {readFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import {criarCatalogo,visaoGeralDaSemana,ldpParaEmissao,type ProgramacaoLinha,type ProgramacaoResposta,type DocumentoLdpEmissao} from "../gestaoVistaJamileLogic";

const SEMANA={start:"2026-10-11",end:"2026-10-17"};
const pid=(i:number)=>`b.${String(i).padStart(8,"0")}-0000-0000-0000-000000000000`;
const PROJETOS=Array.from({length:16},(_,i)=>({id:pid(i+1),name:`PRJ${String(i+1).padStart(3,"0")} - PROJETO ${i+1}`}));
let seq=0;
const linha=(o:Partial<ProgramacaoLinha>&{documento:string|null;dataAtividade:string}):ProgramacaoLinha=>({idRegistro:++seq,tarefa:"ELABORAÇÃO",
 disciplinaWeekly:null,siglaDoc3:null,disciplinaNorma:"MECÂNICA",disciplinaCanonica:"MECHANICAL",fonteDisciplina:"DOC3",elaborador:"ANA",verificador:null,
 aprovador:null,hhElaboracao:4,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,status:null,fase:null,...o});
const resposta=(projectId:string,linhas:ProgramacaoLinha[]):ProgramacaoResposta=>({projectId,inicio:SEMANA.start,fim:SEMANA.end,ultimaSincronizacao:null,
 registrosProjeto:linhas.length,registrosSemData:0,datasComProgramacao:[...new Set(linhas.map(l=>l.dataAtividade.slice(0,10)))].map(data=>({data,quantidade:1})),linhas});
const ldpDoc=(codigo:string,o:Partial<DocumentoLdpEmissao>={}):DocumentoLdpEmissao=>({codigo,emitido:false,cancelado:false,dataEmissao:null,...o});
const calcular=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje="2026-10-25",projetos=PROJETOS)=>
 visaoGeralDaSemana({dados,projetos,cat:criarCatalogo([],projetos),semana:SEMANA,hoje,ldp:new Map(Object.entries(ldp))});

test("16 projetos selecionados: todos aparecem no gráfico por projeto, inclusive os sem atividade (zero)",()=>{
 const dados=PROJETOS.map((p,i)=>resposta(p.id,i%2===0?[linha({documento:`DOC-${i}`,dataAtividade:"2026-10-13T00:00:00"})]:[]));
 const ldp=Object.fromEntries(PROJETOS.map((p,i)=>[p.id,[ldpDoc(`DOC-${i}`)]]));
 const r=calcular(dados,ldp);
 assert.equal(r.porProjeto.length,16);
 assert.deepEqual(r.porProjeto.map(x=>x.projectId),PROJETOS.map(p=>p.id),"ordem da seleção");
 assert.equal(r.porProjeto.filter(x=>x.documentos===0).length,8);
 assert.equal(r.porProjeto.find(x=>x.projectId===pid(2))!.nome,"PRJ002 - PROJETO 2");
 assert.equal(r.documentos,8);
});

test("Por disciplina: TODAS as disciplinas programadas na semana em todos os projetos (não só duas)",()=>{
 const disc=[["CIVIL","CIVIL"],["ELÉTRICA","ELECTRICAL"],["MECÂNICA","MECHANICAL"],["TUBULAÇÃO","PIPING"],["PROCESSO","PROCESS"],["INSTRUMENTAÇÃO","INSTRUMENTATION"]];
 const dados=PROJETOS.slice(0,6).map((p,i)=>resposta(p.id,[linha({documento:`D-${i}`,disciplinaNorma:disc[i][0],disciplinaCanonica:disc[i][1],dataAtividade:"2026-10-12"})]));
 const r=calcular(dados,{},"2026-10-25",PROJETOS);
 assert.equal(r.porDisciplina.length,6);
 assert.deepEqual(r.porDisciplina.map(x=>x.nome).sort(),disc.map(d=>d[0]).sort());
 // Mesma disciplina canônica em projetos diferentes soma numa linha (consolidado).
 const dois=calcular([resposta(pid(1),[linha({documento:"A",dataAtividade:"2026-10-12"})]),resposta(pid(2),[linha({documento:"B",dataAtividade:"2026-10-12"})])],{});
 assert.equal(dois.porDisciplina.length,1);
 assert.equal(dois.porDisciplina[0].documentos,2);
});

test("Documento com várias atividades conta uma vez; repetição da importação é deduplicada; atividade sem código é item próprio",()=>{
 const l=[
  linha({documento:"CHS-001",tarefa:"ELABORAÇÃO",dataAtividade:"2026-10-12"}),
  linha({documento:"CHS-001",tarefa:"VERIFICAÇÃO",dataAtividade:"2026-10-14"}),
  linha({documento:" chs-001 ",tarefa:"APROVAÇÃO",dataAtividade:"2026-10-16"}),
  linha({documento:"CHS-001",tarefa:"APROVAÇÃO",dataAtividade:"2026-10-16"}),   // repetida (mesma chave da 02)
  linha({documento:null,tarefa:"REUNIÃO",dataAtividade:"2026-10-13"}),
  linha({documento:null,tarefa:"REUNIÃO",dataAtividade:"2026-10-13"}),
 ];
 const r=calcular([resposta(pid(1),l)],{[pid(1)]:[ldpDoc("CHS-001")]},"2026-10-25",PROJETOS.slice(0,1));
 assert.equal(r.repetidos,1);
 assert.equal(r.atividades,5);
 assert.equal(r.documentos,3,"CHS-001 uma vez + 2 atividades sem código");
 const doc=r.itens.find(x=>x.codigo?.trim()==="CHS-001")!;
 assert.deepEqual(doc.dias,["2026-10-12","2026-10-14","2026-10-16"]);
 assert.equal(doc.atividades,3);
 // Por dia: o documento aparece em cada dia em que tem atividade.
 assert.deepEqual(r.porDia.map(x=>x.documentos),[0,1,2,1,0,1,0]);
});

test("Mesmo código em projetos diferentes = documentos diferentes",()=>{
 const r=calcular([resposta(pid(1),[linha({documento:"X-1",dataAtividade:"2026-10-12"})]),resposta(pid(2),[linha({documento:"X-1",dataAtividade:"2026-10-12"})])],{});
 assert.equal(r.documentos,2);
});

test("Nunca mistura outra semana: atividade fora de domingo–sábado é descartada",()=>{
 const r=calcular([resposta(pid(1),[linha({documento:"A",dataAtividade:"2026-10-10"}),linha({documento:"B",dataAtividade:"2026-10-11"}),
  linha({documento:"C",dataAtividade:"2026-10-17T23:00:00"}),linha({documento:"D",dataAtividade:"2026-10-18"})])],{},"2026-10-25",PROJETOS.slice(0,1));
 assert.equal(r.documentos,2);
 assert.equal(r.foraDaSemana,2);
 assert.deepEqual(r.porDia.map(x=>x.data),["2026-10-11","2026-10-12","2026-10-13","2026-10-14","2026-10-15","2026-10-16","2026-10-17"]);
});

test("Emissão conciliada por projeto + código com a LDP; sem prova = sem comprovação e aderência indisponível",()=>{
 const l=["E1","E2","P1","P2","S1","S2","S3"].map(c=>linha({documento:c,dataAtividade:"2026-10-13"})).concat(linha({documento:null,dataAtividade:"2026-10-13"}));
 const ldp={[pid(1)]:[
  ldpDoc("E1",{emitido:true,dataEmissao:"2026-10-13T00:00:00"}),
  ldpDoc("E2",{emitido:true,dataEmissao:"2026-09-01"}),
  ldpDoc("P1"),
  ldpDoc("P2",{emitido:true,dataEmissao:"2026-10-20"}),           // emitido DEPOIS do sábado: não emitido na semana
  ldpDoc("S1",{emitido:true,dataEmissao:null}),                   // status aprovado sem data, semana encerrada
  ldpDoc("S2",{cancelado:true}),
 ]};
 const r=calcular([resposta(pid(1),l)],ldp,"2026-10-25",PROJETOS.slice(0,1));
 const sit=Object.fromEntries(r.itens.map(x=>[x.codigo??"(sem código)",x.situacao]));
 assert.deepEqual(sit,{E1:"emitido",E2:"emitido",P1:"pendente",P2:"pendente",S1:"sem-comprovacao",S2:"sem-comprovacao",S3:"sem-comprovacao","(sem código)":"sem-comprovacao"});
 assert.equal(r.emitidos,2);
 assert.equal(r.semComprovacao,4);
 assert.equal(r.aderencia,null);
 assert.equal(r.desvio,null);
 assert.deepEqual(r.motivos.map(m=>m.motivo).sort(),["Atividade sem código de documento","Código não encontrado na LDP do projeto","Documento cancelado na LDP",
  "Emitido na LDP sem data de emissão (não comprova a emissão até o fim da semana)"].sort());
 // Código de OUTRO projeto nunca concilia.
 const outro=calcular([resposta(pid(2),[linha({documento:"E1",dataAtividade:"2026-10-13"})])],ldp,"2026-10-25",PROJETOS.slice(0,2));
 assert.equal(outro.itens[0].situacao,"sem-comprovacao");
});

test("Aderência = emitidos / documentos quando todos estão comprovados; desvio = 100 − aderência",()=>{
 const l=["A","B","C","D"].map(c=>linha({documento:c,dataAtividade:"2026-10-12"}));
 const r=calcular([resposta(pid(1),l)],{[pid(1)]:[ldpDoc("A",{emitido:true,dataEmissao:"2026-10-12"}),ldpDoc("B"),ldpDoc("C"),ldpDoc("D")]},"2026-10-25",PROJETOS.slice(0,1));
 assert.equal(r.aderencia,25);
 assert.equal(r.desvio,75);
 assert.equal(r.atrasados,3,"semana encerrada: não emitidos com atividade até sábado estão atrasados");
});

test("Semana em curso: status atual comprova emissão sem data e data futura nunca é atraso",()=>{
 const l=[linha({documento:"A",dataAtividade:"2026-10-12"}),linha({documento:"B",dataAtividade:"2026-10-16"}),linha({documento:"C",dataAtividade:"2026-10-14"})];
 const r=calcular([resposta(pid(1),l)],{[pid(1)]:[ldpDoc("A"),ldpDoc("B"),ldpDoc("C",{emitido:true})]},"2026-10-14",PROJETOS.slice(0,1));
 assert.equal(r.itens.find(x=>x.codigo==="C")!.situacao,"emitido");
 assert.deepEqual(r.itens.filter(x=>x.atrasado).map(x=>x.codigo),["A"]);
 assert.equal(r.limiteAtraso,"2026-10-14");
});

test("Zero: sem programação → zeros, aderência indisponível e todos os projetos listados",()=>{
 const r=calcular(PROJETOS.map(p=>resposta(p.id,[])),{});
 assert.equal(r.documentos,0);
 assert.equal(r.atividades,0);
 assert.equal(r.aderencia,null);
 assert.equal(r.porProjeto.length,16);
 assert.ok(r.porProjeto.every(p=>p.documentos===0));
 assert.equal(r.porDisciplina.length,0);
});

test("ldpParaEmissao indexa a LDP por projeto",()=>{
 const m=ldpParaEmissao([{projectId:pid(1),documentos:[ldpDoc("A")]}]);
 assert.equal(m.get(pid(1))!.length,1);
 assert.equal(m.get(pid(2)),undefined);
});

test("Página: 01 usa a programação semanal da 02, bloqueia em falha de qualquer projeto e não usa gates/LDP como previsto",()=>{
 const src=readFileSync(path.resolve(process.cwd().endsWith("ClientApp")?process.cwd():path.join(process.cwd(),"ClientApp"),"src/features/production/analytics/GestaoVistaJamilePage.tsx"),"utf8");
 const overview=src.slice(src.indexOf("function Overview("),src.indexOf("/* ---------------------------------------------------------------- 02. PROGRAMAÇÃO"));
 assert.match(src,/useProgramacao\(selection,week\.start,week\.end,screen==="programacao"\|\|screen==="visao-geral"\|\|/);
 assert.match(src,/\.\.\.\(screen==="visao-geral"\?prog\.falhas:\[\]\)/);
 assert.match(src,/<Overview reports=\{fr\} dados=\{prog\.dados\} selection=\{selection\} semana=\{week\}/);
 assert.match(overview,/visaoGeralDaSemana\(/);
 assert.doesNotMatch(overview,/entregasDaSemana|semanasComEntregas|\.gates|FASES|PhaseSupplement|faseContagem/);
 assert.doesNotMatch(overview,/type="date"|DATA DE CORTE/);
});
