// 01, 02 e demais telas com UNICO projeto mostram somente o projeto no titulo da TV.
// Na diaria AUTO em modo disciplina, projeto + disciplina; slides MULTIPROJETO ficam com label original.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { TV_PROGRAMACAO_DIARIA, getTelaTv, TELAS_TV } from "../tvScreens";
import { DATA_REFERENCIA_AUTO, slideComProjetos, tituloCentralTv, type TvSlide, type TvSlideProjeto } from "../tvLogic";
import { TituloTelaTv } from "../TvPlayerPage";

const P = (id: string, name: string | null): TvSlideProjeto => ({hubId:"",projectId:id,projectName:name});
const A=P("b.A","CHS001 - ETHANOL PLANT"), B=P("b.B","BML011.016 - PROJETO PILOTO");
const title=(s:TvSlide)=>tituloCentralTv(s,getTelaTv(s.screenId)?.label,TV_PROGRAMACAO_DIARIA);
const html=(s:TvSlide)=>renderToStaticMarkup(createElement(TituloTelaTv,{titulo:title(s)}));
const texto=(s:TvSlide)=>html(s).replace(/<[^>]+>/g,"");

test("Todas as telas com apenas um projeto exibem somente o nome real dele, com destaque",()=>{
  const telas=TELAS_TV.filter(t=>t.id!==TV_PROGRAMACAO_DIARIA);
  assert.ok(telas.length>10);
  for(const t of telas){
    const result=title(slideComProjetos(t.id,[A]));
    assert.deepEqual(result,{texto:A.projectName,destaque:true},t.id);
    assert.equal(texto(slideComProjetos(t.id,[A])),A.projectName,t.id);
  }
  assert.equal(title(slideComProjetos(TV_PROGRAMACAO_DIARIA,[A],DATA_REFERENCIA_AUTO)).texto,A.projectName);
  assert.equal(title({...slideComProjetos(TV_PROGRAMACAO_DIARIA,[A],DATA_REFERENCIA_AUTO),layoutDiario:"cards"}).texto,A.projectName);
  assert.equal(title(slideComProjetos(TV_PROGRAMACAO_DIARIA,[A],"2026-10-20")).texto,A.projectName);
});
test("Uma tela por disciplina no AUTO mostra projeto e disciplina real; nos demais formatos nao inventa disciplina",()=>{
  const base=slideComProjetos(TV_PROGRAMACAO_DIARIA,[A],DATA_REFERENCIA_AUTO);
  const single={...base,layoutDiario:"disciplina" as const,disciplina:"PIP",disciplinaNome:"TUBULAÇÃO"};
  assert.deepEqual(title(single),{texto:"CHS001 - ETHANOL PLANT — TUBULAÇÃO",destaque:true,partes:{projeto:"CHS001 - ETHANOL PLANT",disciplina:"TUBULAÇÃO"}});
  assert.equal(texto(single),"CHS001 - ETHANOL PLANT — TUBULAÇÃO");
  assert.equal(title({...single,disciplinaNome:""}).texto,"CHS001 - ETHANOL PLANT — PIP");
  assert.equal(title({...single,disciplina:null}).texto,A.projectName);
  assert.equal(title({...single,layoutDiario:"cards"}).texto,A.projectName);
  assert.equal(title({...single,dataReferencia:"2026-10-19"}).texto,A.projectName);
});
test("Multprojeto preserva o titulo original em qualquer tela, inclusive diaria, nunca nome de projeto principal",()=>{
 for(const id of ["gestao-a-vista-visao-geral","gestao-a-vista-programacao",TV_PROGRAMACAO_DIARIA]){
   const s=slideComProjetos(id,[A,B],DATA_REFERENCIA_AUTO);
   const rotulo=getTelaTv(id)?.label;
   assert.deepEqual(title({...s,layoutDiario:"disciplina",disciplina:"PIP",disciplinaNome:"PIPING"}),{texto:rotulo,destaque:false});
   assert.equal(texto(s),rotulo);
   assert.match(html(s),/truncate text-center text-xl font-bold/);
 }
});
test("Nome de projeto ausente recua para projectId; titulo amplo, sem corte horizontal",()=>{
 const s=slideComProjetos("gestao-a-vista-visao-geral",[P("b.SEM_NOME",null)]);
 assert.equal(title(s).texto,"b.SEM_NOME");
 const rendered=html(s);
 assert.ok(rendered.includes("text-2xl")&&rendered.includes("font-black")&&rendered.includes("line-clamp-2"));
 assert.ok(!rendered.includes(" truncate "));
});
