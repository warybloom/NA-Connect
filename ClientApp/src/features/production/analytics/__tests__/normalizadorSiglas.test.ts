import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import path from "node:path";
import {chave,criarNormalizador,resolver,type RegraSigla} from "@/features/disciplinas/normalizadorSiglas";
import {filtrarRegras,gruposPorNomeCanonico,mapeamentoCompleto,validarRegra} from "@/features/settings/normalizador-siglas/normalizadorSiglasLogic";
import {criarCatalogo,classificarDocumento,classificarRealizado,performancePorDisciplina,rotulo,type DisciplinasNorma} from "../gestaoVistaJamileLogic";

// FIXTURE EXCLUSIVA DE TESTE (siglas sintéticas com prefixo X/T). A mesma usada pelos testes C#: os dois lados resolvem igual.
const fixture=JSON.parse(readFileSync(path.resolve(process.cwd(),"../Tests/NA_Connect.DisciplineAlias.Tests/Fixtures/casos_resolucao.json"),"utf8")) as {
 regras:{id:number;sigla:string;disciplina:string;contextoTipo:"PROJETO"|"FONTE"|null;contextoValor:string|null;ativo:boolean}[];
 casos:{nome:string;candidatos:(string|null)[];rotulo:string;projetos:string[];fonte:string|null;esperado:{nome:string;origem:string;conflito:boolean}}[]};
const regra=(r:typeof fixture.regras[number]):RegraSigla=>({id:r.id,siglaOriginal:r.sigla,disciplinaCanonica:r.disciplina,contextoTipo:r.contextoTipo,
 contextoValor:r.contextoValor,ativo:r.ativo,origem:"MANUAL",importacaoId:null,versao:1,criadoEm:"",criadoPor:"teste",alteradoEm:"",alteradoPor:"teste"});
const regras=fixture.regras.map(regra);

for(const c of fixture.casos)test(`Paridade C#/TS — ${c.nome}`,()=>{
 const r=resolver(regras,c.candidatos,c.rotulo,{projetos:c.projetos,fonte:c.fonte});
 assert.deepEqual({nome:r.nome,origem:r.origem,conflito:r.conflito!==null},c.esperado);
});

test("Chave: espaços/caixa normalizados, Unicode preservado (NFC), acento não removido",()=>{
 assert.equal(chave("  elétrica \t e  instrumentação "),"ELÉTRICA E INSTRUMENTAÇÃO");
 assert.equal(chave("Elétrica"),chave("Elétrica"));
 assert.notEqual(chave("ELÉTRICA"),chave("ELETRICA"));
});

/* ---------------------------------------------------------------- integração no consolidado da Gerência */
const P1="b.00000000-0000-0000-0000-0000000000a1",P2="b.00000000-0000-0000-0000-0000000000a2";
const norma=(projectId:string,siglas:[string,string][]):DisciplinasNorma=>({projectId,normaId:1,normaNome:"Norma teste",diagnosticos:[],
 siglas:siglas.map(([sigla,disciplina])=>({sigla,disciplina,canonica:null}))});
const normas=[norma(P1,[["XA","Disciplina Norma Um"]]),norma(P2,[["XQ","Disciplina Norma Dois"]])];
const projetos=[{id:P1,name:"TST901 - Projeto Teste Um"},{id:P2,name:"TST902 - Projeto Teste Dois"}];
const regrasPainel:RegraSigla[]=[
 regra({id:1,sigla:"XA",disciplina:"Teste Canônica",contextoTipo:null,contextoValor:null,ativo:true}),
 regra({id:2,sigla:"XQ",disciplina:"Teste Canônica",contextoTipo:null,contextoValor:null,ativo:true})];
const doc=(projectId:string,sigla:string,disciplinaNorma:string,hh:number)=>({projectId,codigo:`D-${sigla}-${hh}`,siglaDoc3:sigla,disciplinaNorma,
 hhPrevisto:hh,custoPrevisto:hh*10,isActivity:false});

test("Consolidado: siglas diferentes com o mesmo nome PT-BR somam numa linha, sem dupla contagem",()=>{
 const cat=criarCatalogo(normas,projetos,criarNormalizador(regrasPainel));
 const a=classificarDocumento(cat,doc(P1,"XA","Disciplina Norma Um",10)),b=classificarDocumento(cat,doc(P2,"XQ","Disciplina Norma Dois",5));
 assert.equal(a.chave,b.chave);
 assert.equal(rotulo(cat,a.chave),"Teste Canônica");
 const reports=[{projectId:P1,nomeProjeto:projetos[0].name,tipoLdp:1,documentos:[doc(P1,"XA","Disciplina Norma Um",10)]},
  {projectId:P2,nomeProjeto:projetos[1].name,tipoLdp:1,documentos:[doc(P2,"XQ","Disciplina Norma Dois",5)]}];
 const {rows}=performancePorDisciplina(reports as never,cat);
 const linha=rows.filter(r=>!r.reconciliacao);
 assert.equal(linha.length,1);
 assert.equal(linha[0].hhPrevisto,15);
 assert.equal(linha[0].documentos,2);
});

test("Sem regras, com 1 projeto ou com conflito: classificação original intacta",()=>{
 const semRegras=criarCatalogo(normas,projetos,criarNormalizador([]));
 assert.notEqual(classificarDocumento(semRegras,doc(P1,"XA","Disciplina Norma Um",1)).chave,classificarDocumento(semRegras,doc(P2,"XQ","Disciplina Norma Dois",1)).chave);
 const umProjeto=criarCatalogo([normas[0]],[projetos[0]],criarNormalizador(regrasPainel));
 assert.equal(umProjeto.normalizador,null);
 assert.equal(rotulo(umProjeto,classificarDocumento(umProjeto,doc(P1,"XA","Disciplina Norma Um",1)).chave),"Disciplina Norma Um");
 const conflito=[regra({id:3,sigla:"XA",disciplina:"Teste Um",contextoTipo:"PROJETO",contextoValor:"TST901",ativo:true}),
  regra({id:4,sigla:"XA",disciplina:"Teste Dois",contextoTipo:"FONTE",contextoValor:"LDP",ativo:true})];
 const c=classificarDocumento(criarCatalogo(normas,projetos,criarNormalizador(conflito)),doc(P1,"XA","Disciplina Norma Um",1));
 assert.ok(!c.chave.startsWith("S:"));
 assert.match(c.motivo??"",/conflitantes/);
});

test("Realizado (TIMESHEET) usa a regra pelo nome da disciplina e respeita contexto de fonte",()=>{
 const r=[regra({id:5,sigla:"Disciplina Norma Um",disciplina:"Teste Canônica",contextoTipo:"FONTE",contextoValor:"TIMESHEET",ativo:true})];
 const cat=criarCatalogo(normas,projetos,criarNormalizador(r));
 assert.equal(rotulo(cat,classificarRealizado(cat,P1,"Disciplina Norma Um").chave),"Teste Canônica");
 // O mesmo nome vindo da LDP não usa a regra exclusiva do Timesheet.
 assert.equal(rotulo(cat,classificarDocumento(cat,doc(P1,"ZZ","Disciplina Norma Um",1)).chave).includes("Teste Canônica"),false);
});

/* ---------------------------------------------------------------- lógica da tela de Configurações */
test("Tela: busca, filtro, duplicidade, grupos canônicos e de/para",()=>{
 assert.deepEqual(filtrarRegras(regras,"  teste   beta",  "todas").map(r=>r.id),[3]);
 assert.deepEqual(filtrarRegras(regras,"","inativas").map(r=>r.id),[6]);
 assert.match(validarRegra({siglaOriginal:" xb ",disciplinaCanonica:"Outra",contextoTipo:null,contextoValor:null,ativo:true},regras,null).join(),/Já existe regra/);
 assert.deepEqual(validarRegra({siglaOriginal:"XB",disciplinaCanonica:"Outra",contextoTipo:"PROJETO",contextoValor:"TST902",ativo:true},regras,null),[]);
 assert.deepEqual(gruposPorNomeCanonico(regras).find(g=>g.nome==="Disciplina Teste Ãlfa")?.siglas,["XA","T 1"]);
 assert.equal(mapeamentoCompleto({sigla:"A",disciplina:"A"}),false);
 assert.equal(mapeamentoCompleto({sigla:"A",disciplina:"B",contextoValor:"C"}),false);
 assert.equal(mapeamentoCompleto({sigla:"A",disciplina:"B",contextoTipo:"C",contextoValor:"D"}),true);
});
