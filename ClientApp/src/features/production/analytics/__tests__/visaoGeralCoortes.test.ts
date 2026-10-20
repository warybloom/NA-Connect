// 01. VISÃO GERAL — correções após teste (20/10/2026): %ADERÊNCIA acumulada até hoje por coorte documental, %DESVIO = backlog de
// dias anteriores (sem duplicar, hoje fora até a meia-noite da Bahia), EM ATRASO = mesmo backlog, EMITIDOS até a data corrente,
// gauges com percentual/fração menores e sem "DESVIO N", legenda "EMITIDOS ATÉ" centralizada e DOCUMENTOS POR DISCIPLINA com as
// mesmas barras de DOCUMENTOS POR PROJETO.
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import path from "node:path";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {criarCatalogo,semanaDoCorte,visaoGeralDaSemana,type DocumentoLdpEmissao,type ProgramacaoLinha,type ProgramacaoResposta} from "../gestaoVistaJamileLogic";
import {Gauge,Overview} from "../GestaoVistaJamilePage";
import {dataCivilBahia} from "@/features/tv/tvLogic";
import type {FactsReport} from "@/components/pmo/pmoFacts";

const fonte=(relativo:string)=>{
 for(const base of [process.cwd(),path.resolve(process.cwd(),"ClientApp")]){try{return readFileSync(path.resolve(base,relativo),"utf8");}catch{/* próximo */}}
 throw new Error(`Fonte não encontrada: ${relativo}`);
};
// Semana domingo 11/10 → sábado 17/10/2026.
const SEMANA={start:"2026-10-11",end:"2026-10-17"};
const DOM="2026-10-11",SEG="2026-10-12",TER="2026-10-13",QUA="2026-10-14",QUI="2026-10-15",SEX="2026-10-16",SAB="2026-10-17";
const P1="b.11111111-1111-1111-1111-111111111111",P2="b.22222222-2222-2222-2222-222222222222";
const PROJETOS=[{id:P1,name:"CHS001 - ETHANOL PLANT"},{id:P2,name:"BML011 - COOLING TOWER"}];
let seq=0;
const linha=(documento:string|null,dataAtividade:string,o:Partial<ProgramacaoLinha>={}):ProgramacaoLinha=>({idRegistro:++seq,documento,tarefa:"ELABORAÇÃO",
 disciplinaWeekly:null,siglaDoc3:null,disciplinaNorma:"MECÂNICA",disciplinaCanonica:"MECHANICAL",fonteDisciplina:"DOC3",elaborador:"ANA",verificador:null,
 aprovador:null,hhElaboracao:4,hhVerificacao:null,hhAprovacao:null,baseline:null,reprogramado:null,dataAtividade,status:null,fase:null,...o});
const resposta=(projectId:string,linhas:ProgramacaoLinha[],semana=SEMANA):ProgramacaoResposta=>({projectId,inicio:semana.start,fim:semana.end,ultimaSincronizacao:null,
 registrosProjeto:linhas.length,registrosSemData:0,datasComProgramacao:[...new Set(linhas.map(l=>l.dataAtividade.slice(0,10)))].map(data=>({data,quantidade:1})),linhas});
const ldpDoc=(codigo:string,o:Partial<DocumentoLdpEmissao>={}):DocumentoLdpEmissao=>({codigo,emitido:false,cancelado:false,dataEmissao:null,...o});
const emitido=(codigo:string,dataEmissao:string)=>ldpDoc(codigo,{emitido:true,dataEmissao});
const vg=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje:string,semana=SEMANA)=>
 visaoGeralDaSemana({dados,projetos:PROJETOS,cat:criarCatalogo([],PROJETOS),semana,hoje,ldp:new Map(Object.entries(ldp))});
const kpis=(r:ReturnType<typeof vg>)=>({aderencia:r.aderencia,desvio:r.desvio,atrasados:r.atrasados,emitidos:r.emitidos});
const pct=(a:number,b:number)=>a/b*100;
const CIVIL={disciplinaNorma:"CIVIL",disciplinaCanonica:"CIVIL"};

// 10 documentos de MECÂNICA com a data prevista em SEG(1) TER(2) QUI(4) SEX(3) → meta 10/30/30/70/100 (outro arquivo cobre a série).
// MEC-00 (SEG) e MEC-10 (TER) emitidos no próprio dia; os demais pendentes na LDP.
const DEZ=[[SEG,1],[TER,2],[QUI,4],[SEX,3]].flatMap(([d,n],i)=>Array.from({length:n as number},(_,j)=>linha(`MEC-${i}${j}`,d as string)));
const LDP_DEZ={[P1]:DEZ.map(l=>l.documento==="MEC-00"?emitido("MEC-00",SEG):l.documento==="MEC-10"?emitido("MEC-10",TER):ldpDoc(l.documento!))};

test("10 documentos: aderência acumulada até hoje e desvio só de dias anteriores, dia a dia (sem constante)",()=>{
 const dia=(hoje:string)=>{const r=vg([resposta(P1,DEZ)],LDP_DEZ,hoje);return [r.meta.total.meta,r.meta.total.metaPct,kpis(r)];};
 // SEG: 1 previsto até hoje (emitido) → 100%; nenhum vencido antes de hoje → desvio —; hoje não vira atraso.
 assert.deepEqual(dia(SEG),[1,10,{aderencia:100,desvio:null,atrasados:0,emitidos:1}]);
 // TER: 3 previstos até hoje, 2 emitidos; o vencido (SEG) foi emitido → desvio 0%; MEC-11 (hoje) ainda não é atraso.
 assert.deepEqual(dia(TER),[3,30,{aderencia:pct(2,3),desvio:0,atrasados:0,emitidos:2}]);
 // QUA: MEC-11 (TER) vira backlog: 1 de 3 vencidos.
 assert.deepEqual(dia(QUA),[3,30,{aderencia:pct(2,3),desvio:pct(1,3),atrasados:1,emitidos:2}]);
 // QUI: a pendência de terça continua 1 (não soma 1+1); os 4 de QUI entram na aderência (meta de hoje), não no desvio.
 assert.deepEqual(dia(QUI),[7,70,{aderencia:pct(2,7),desvio:pct(1,3),atrasados:1,emitidos:2}]);
 // SEX: vencidos até QUI = 7, pendentes 5.
 assert.deepEqual(dia(SEX),[10,100,{aderencia:pct(2,10),desvio:pct(5,7),atrasados:5,emitidos:2}]);
});

test("Regularização: documento emitido depois da data prevista sai do backlog no dia da emissão",()=>{
 const reg={[P1]:LDP_DEZ[P1].map(d=>d.codigo==="MEC-11"?emitido("MEC-11",QUI):d)};
 const qua=vg([resposta(P1,DEZ)],reg,QUA),qui=vg([resposta(P1,DEZ)],reg,QUI);
 assert.deepEqual([qua.desvio,qua.atrasados],[pct(1,3),1],"QUA: ainda pendente");
 assert.deepEqual([qui.desvio,qui.atrasados,qui.meta.total.aderidos],[0,0,3],"QUI: regularizado, conta como emitido até hoje");
});

test("Emissão antecipada de documento futuro NÃO compensa documento vencido (aderência e desvio)",()=>{
 const l=[linha("VENCIDO",SEG),linha("FUTURO",SEX)];
 const r=vg([resposta(P1,l)],{[P1]:[ldpDoc("VENCIDO"),emitido("FUTURO",SEG)]},QUA);
 assert.deepEqual(kpis(r),{aderencia:0,desvio:100,atrasados:1,emitidos:1},"EMITIDOS conta o antecipado; aderência/desvio não");
 assert.equal(r.meta.total.realizados,1,"gauge (realizado da semana) = cartão EMITIDOS");
});

test("Semana em curso: EMITIDOS é até HOJE (nunca o sábado futuro); emitido sem data comprova pelo status atual",()=>{
 const l=[linha("A",SEG),linha("B",TER),linha("C",QUA)];
 const r=vg([resposta(P1,l)],{[P1]:[emitido("A",SEG),ldpDoc("B",{emitido:true}),emitido("C","2026-10-16")]},QUA);
 assert.equal(r.meta.corteRealizado,QUA);
 // C com data de emissão futura (sexta) não é emitido hoje → pendente, mas é meta de hoje, não atraso.
 assert.deepEqual(r.itens.map(x=>[x.codigo,x.situacao,x.atrasado]),[["A","emitido",false],["B","emitido",false],["C","pendente",false]]);
 assert.deepEqual(kpis(r),{aderencia:pct(2,3),desvio:0,atrasados:0,emitidos:2});
});

test("Sem data de emissão em semana concluída, atraso desconhecido e LDP ausente → indeterminado (—), nunca atraso",()=>{
 // Semana concluída: emitido SEM data não comprova a emissão até o sábado.
 const semData=vg([resposta(P1,[linha("A",SEG),linha("B",SEG)])],{[P1]:[ldpDoc("A",{emitido:true}),ldpDoc("B")]},"2026-10-25");
 assert.deepEqual(kpis(semData),{aderencia:null,desvio:null,atrasados:1,emitidos:0});
 assert.deepEqual([semData.meta.total.indeterminados,semData.meta.total.indeterminadosMeta],[1,1]);
 // Atraso desconhecido: vencido sem código / fora da LDP → indeterminado; o comprovado pendente segue no atraso.
 const desconhecido=vg([resposta(P1,[linha(null,SEG),linha("FORA",SEG),linha("PEND",SEG)])],{[P1]:[ldpDoc("PEND")]},QUA);
 assert.deepEqual(kpis(desconhecido),{aderencia:null,desvio:null,atrasados:1,emitidos:0});
 assert.equal(desconhecido.meta.total.indeterminados,2);
 // LDP do projeto ausente: tudo sem comprovação.
 const semLdp=vg([resposta(P1,[linha("A",SEG),linha("B",TER)])],{},QUA);
 assert.deepEqual(kpis(semLdp),{aderencia:null,desvio:null,atrasados:0,emitidos:0});
 // Sem comprovação só em documento FUTURO não torna a coorte de hoje indeterminada.
 const fut=vg([resposta(P1,[linha("A",SEG),linha("FORA",SEX)])],{[P1]:[emitido("A",SEG)]},QUA);
 assert.deepEqual(kpis(fut),{aderencia:100,desvio:0,atrasados:0,emitidos:1});
});

test("Meta zero hoje: documentos só previstos para dias futuros → aderência e desvio —, marca da meta em 0 (sem meta artificial)",()=>{
 const l=[linha("X",SEX),linha("Y",SEX,CIVIL)];
 const r=vg([resposta(P1,l)],{[P1]:[ldpDoc("X"),ldpDoc("Y")]},SEG);
 assert.deepEqual(kpis(r),{aderencia:null,desvio:null,atrasados:0,emitidos:0});
 assert.ok(r.porDisciplina.every(d=>d.meta.meta===0&&d.meta.metaPct===0));
 const g=renderToStaticMarkup(createElement(Gauge,{title:"CIVIL",value:0,fracao:"0 de 1",meta:{pct:0,texto:"META HOJE: 0 de 1 (0,0%)",dica:""}}));
 assert.match(g,/data-testid="gv-gauge-alvo" data-pct="0"/);
});

test("Dois projetos com o mesmo código: coortes separadas por projeto",()=>{
 const r=vg([resposta(P1,[linha("X-1",SEG)]),resposta(P2,[linha("X-1",SEG)])],{[P1]:[emitido("X-1",SEG)],[P2]:[ldpDoc("X-1")]},QUA);
 assert.deepEqual(kpis(r),{aderencia:50,desvio:50,atrasados:1,emitidos:1});
 assert.deepEqual(r.porProjeto.map(p=>[p.documentos,p.emitidos]),[[1,1],[1,0]]);
});

test("Mesmo documento em dois dias: conta UMA vez, na data prevista (última atividade), nunca duas no backlog",()=>{
 const l=[linha("D",SEG),linha("D",QUI,{tarefa:"VERIFICAÇÃO"}),linha("E",SEG)];
 const ldp={[P1]:[ldpDoc("D"),ldpDoc("E")]};
 const qua=vg([resposta(P1,l)],ldp,QUA),sex=vg([resposta(P1,l)],ldp,SEX);
 assert.deepEqual([qua.meta.total.meta,qua.meta.total.vencidos,qua.atrasados],[1,1,1],"QUA: D (previsto QUI) fora das coortes");
 assert.deepEqual([sex.meta.total.meta,sex.meta.total.vencidos,sex.atrasados,sex.desvio],[2,2,2,100]);
 // Tarefa de EMISSÃO inequívoca define a data prevista mesmo com atividade posterior.
 const em=vg([resposta(P1,[linha("F",SEG,{tarefa:"EMISSÃO"}),linha("F",QUI,{tarefa:"ATENDIMENTO A COMENTÁRIOS"})])],{[P1]:[ldpDoc("F")]},TER);
 assert.deepEqual([em.itens[0].dataPrevista,em.atrasados],[SEG,1]);
});

test("Domingo e sábado: domingo sem vencidos (desvio —); sábado avalia o backlog até sexta",()=>{
 const l=[linha("D-DOM",DOM),linha("D-SEX",SEX),linha("D-SAB",SAB)];
 const ldp={[P1]:[ldpDoc("D-DOM"),ldpDoc("D-SEX"),ldpDoc("D-SAB")]};
 const dom=vg([resposta(P1,l)],ldp,DOM),sab=vg([resposta(P1,l)],ldp,SAB);
 assert.deepEqual(kpis(dom),{aderencia:0,desvio:null,atrasados:0,emitidos:0});
 assert.deepEqual([sab.meta.total.vencidos,sab.atrasados,sab.meta.total.meta],[2,2,3],"D-SAB é meta de hoje, não atraso");
});

test("Semana histórica (concluída): foto do sábado — emissão depois do sábado não vira pontual; futura: tudo —",()=>{
 const l=[linha("NO-PRAZO",QUA),linha("TARDE",QUA),linha("NUNCA",SEX)];
 const ldp={[P1]:[emitido("NO-PRAZO",QUI),emitido("TARDE","2026-10-19"),ldpDoc("NUNCA")]};
 const hist=vg([resposta(P1,l)],ldp,"2026-10-25");
 assert.deepEqual([hist.meta.estado,hist.meta.corte,hist.meta.corteRealizado,hist.meta.corteAtraso],["concluida",SAB,SAB,SAB]);
 assert.deepEqual(kpis(hist),{aderencia:pct(1,3),desvio:pct(2,3),atrasados:2,emitidos:1});
 // Semana futura: nada vencido nem devido; antecipado aparece só em EMITIDOS.
 const futura=vg([resposta(P1,l)],{[P1]:[emitido("NO-PRAZO","2026-10-05"),ldpDoc("TARDE"),ldpDoc("NUNCA")]},"2026-10-06");
 assert.deepEqual(kpis(futura),{aderencia:null,desvio:null,atrasados:0,emitidos:1});
});

test("Fuso Bahia: pendência de terça só vira atraso à 00:00 de quarta na Bahia (03:00 UTC), não à 00:00 UTC",()=>{
 const l=[linha("T",TER)],ldp={[P1]:[ldpDoc("T")]};
 const antes=dataCivilBahia(new Date("2026-10-14T02:59:00Z")),depois=dataCivilBahia(new Date("2026-10-14T03:00:00Z"));
 assert.deepEqual([antes,depois],[TER,QUA]);
 assert.deepEqual([vg([resposta(P1,l)],ldp,antes).atrasados,vg([resposta(P1,l)],ldp,antes).desvio],[0,null]);
 assert.deepEqual([vg([resposta(P1,l)],ldp,depois).atrasados,vg([resposta(P1,l)],ldp,depois).desvio],[1,100]);
});

test("Consistência: EM ATRASO = backlog do %DESVIO; somas por disciplina = total; barras = gauges = cartões",()=>{
 const l=[...DEZ,linha("CIV-1",SEG,CIVIL),linha("CIV-2",TER,CIVIL),linha("CIV-3",SEX,CIVIL)];
 const ldp={[P1]:[...LDP_DEZ[P1],emitido("CIV-1",TER),ldpDoc("CIV-2"),emitido("CIV-3",SEG)]};
 for(const hoje of [DOM,SEG,TER,QUA,QUI,SEX,SAB,"2026-10-25"]){
  const r=vg([resposta(P1,l)],ldp,hoje),t=r.meta.total;
  assert.equal(r.atrasados,t.desvio,hoje);
  assert.equal(r.itens.filter(x=>x.atrasado).length,t.desvio,hoje);
  for(const k of ["meta","aderidos","vencidos","desvio","realizados","documentos"] as const)
   assert.equal(r.porDisciplina.reduce((s,d)=>s+d.meta[k],0),t[k],`${hoje} ${k}`);
  assert.ok(r.porDisciplina.every(d=>d.emitidos===d.meta.realizados&&d.documentos===d.meta.documentos),"barras × gauges");
  assert.equal(r.emitidos,t.realizados,"cartão EMITIDOS × gauge");
  assert.equal(r.porProjeto.reduce((s,p)=>s+p.emitidos,0),r.emitidos);
  assert.equal(r.aderencia,t.meta?pct(t.aderidos,t.meta):null,hoje);
  assert.equal(r.desvio,t.vencidos?pct(t.desvio,t.vencidos):null,hoje);
 }
});

/* ------------------------------------------------------------------------------- tela 01 renderizada */
const reportsDe=(ldp:Record<string,DocumentoLdpEmissao[]>)=>PROJETOS.map(p=>({projectId:p.id,documentos:ldp[p.id]??[]})) as unknown as FactsReport[];
const render=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,semana=SEMANA)=>renderToStaticMarkup(createElement(Overview,{reports:reportsDe(ldp),dados,
 selection:PROJETOS.map(p=>({...p,hubId:""})) as never,semana,cat:criarCatalogo([],PROJETOS),carregando:false,onSemana:()=>undefined}));
const tile=(html:string,titulo:string)=>html.match(new RegExp(`${titulo}</span><strong class="gv-tile-value">([^<]+)</strong>(?:<small class="gv-tile-nota">(.*?)</small>)?`))!;

test("01 renderizada (semana concluída): cartões pelas coortes, sem DESVIO N, legenda EMITIDOS ATÉ, barras por disciplina iguais às por projeto",()=>{
 const l=[...DEZ,linha("CIV-1",SEG,CIVIL)];
 const ldp={[P1]:[...LDP_DEZ[P1],ldpDoc("CIV-1")],[P2]:[]};
 const html=render([resposta(P1,l),resposta(P2,[])],ldp);
 // 11 previstos até sábado: 2 emitidos → 18,2%; backlog 9 de 11 → 81,8%; EM ATRASO 9 (mesmo backlog).
 assert.equal(tile(html,"%ADERÊNCIA À PROGRAMAÇÃO")[1],"18,2%");
 assert.match(tile(html,"%ADERÊNCIA À PROGRAMAÇÃO")[2],/2 de 11 previstos/);
 assert.equal(tile(html,"%DESVIO DA PROGRAMAÇÃO")[1],"81,8%");
 assert.match(tile(html,"%DESVIO DA PROGRAMAÇÃO")[2],/9 de 11 vencidos/);
 assert.equal(tile(html,"EM ATRASO")[1],"9");
 assert.equal(tile(html,"EMITIDOS")[1],"2");
 assert.doesNotMatch(html,/DESVIO \d|gv-gauge-meta-nota/);
 assert.match(html,/data-testid="gv-vg-legenda-emitidos">EMITIDOS ATÉ 17\/10\/2026</);
 // DOCUMENTOS POR DISCIPLINA: mesmo componente (gv-pbar) com programados (azul) e emitidos (verde), ambos visíveis, zero incluído.
 assert.match(html,/data-testid="gv-vg-barras-disciplina"/);
 assert.doesNotMatch(html,/gv-hbar|gv-htrack/);
 const disc=html.slice(html.indexOf('data-testid="gv-vg-barras-disciplina"'));
 const prog=[...disc.matchAll(/data-testid="gv-vg-disciplina-programados">(\d+)</g)].map(m=>m[1]);
 const emit=[...disc.matchAll(/data-testid="gv-vg-disciplina-emitidos">(\d+)</g)].map(m=>m[1]);
 assert.deepEqual([prog,emit],[["10","1"],["2","0"]]);
 assert.match(disc,/gv-pbar-linha gv-pbar-prog[\s\S]*?gv-pbar-linha gv-pbar-emit/,"programados em cima, emitidos embaixo");
 // Gauge: fração "2 de 10" e valor no SVG mais alto (y=87), fração em y=109.
 assert.match(html,/<text x="100" y="87" text-anchor="middle" class="gv-gauge-value">20,0%<\/text><text x="100" y="109" text-anchor="middle" class="gv-gauge-fracao" data-testid="gv-gauge-fracao">2 de 10</);
});

test("01 renderizada (semana corrente, hoje real da Bahia): documento de hoje não é atraso; EMITIDOS ATÉ = hoje",()=>{
 const hoje=dataCivilBahia(new Date()),semana=semanaDoCorte(hoje);
 const html=render([resposta(P1,[linha("HOJE-1",hoje)],semana),resposta(P2,[],semana)],{[P1]:[ldpDoc("HOJE-1")],[P2]:[]},semana);
 const br=(v:string)=>v.split("-").reverse().join("/");
 assert.match(html,new RegExp(`EMITIDOS ATÉ ${br(hoje).replace(/\//g,"\\/")}<`));
 assert.equal(tile(html,"EM ATRASO")[1],"0");
 assert.equal(tile(html,"%ADERÊNCIA À PROGRAMAÇÃO")[1],"0,0%");
 assert.equal(tile(html,"%DESVIO DA PROGRAMAÇÃO")[1],"—");
 assert.match(tile(html,"%DESVIO DA PROGRAMAÇÃO")[2],/nenhum vencido antes de hoje/);
});

/* ------------------------------------------------------------------------------- CSS e escopo */
test("CSS da 01: percentual central 17px e fração 13px nos gauges com meta; legenda centralizada; demais gauges intactos",()=>{
 const css=fonte("src/features/production/analytics/gestaoVistaJamile.css");
 assert.match(css,/\.gv-gauge-com-meta \.gv-gauge-value\{font-size:17px\}/);
 assert.match(css,/\.gv-gauge-com-meta \.gv-gauge-fracao\{font-size:13px;/);
 assert.match(css,/\.gv-legend-meta\{[^}]*justify-content:center[^}]*text-align:center/);
 assert.match(css,/\.gv-gauge-value\{fill:var\(--gv-ink\);font-size:21px;/,"gauge sem meta (outras telas) inalterado");
 const original=renderToStaticMarkup(createElement(Gauge,{title:"X",value:50,fracao:"1 de 2"}));
 assert.match(original,/y="92"[^>]*class="gv-gauge-value"/);
 assert.match(original,/y="116"[^>]*class="gv-gauge-fracao"/);
});

test("Barras por disciplina com mais de 6 linhas: modo compacto (mesmo componente/cores), sem rolagem na TV; até 6 = layout de projeto",()=>{
 const D=["CIVIL","ELÉTRICA","MECÂNICA","TUBULAÇÃO","PROCESSO","INSTRUMENTAÇÃO","AUTOMAÇÃO"];
 const l=D.map((d,i)=>linha(`D-${i}`,SEG,{disciplinaNorma:d,disciplinaCanonica:d}));
 const ldp={[P1]:l.map(x=>ldpDoc(x.documento!)),[P2]:[]};
 const sete=render([resposta(P1,l),resposta(P2,[])],ldp);
 assert.match(sete,/class="gv-pbars gv-pbars-compacto" data-testid="gv-vg-barras-disciplina"/);
 assert.match(sete,/class="gv-pbars" data-testid="gv-vg-barras-projeto"/);
 assert.equal([...sete.matchAll(/data-testid="gv-vg-disciplina-emitidos">0</g)].length,7,"zero visível em todas");
 const seis=render([resposta(P1,l.slice(0,6)),resposta(P2,[])],ldp);
 assert.match(seis,/class="gv-pbars" data-testid="gv-vg-barras-disciplina"/);
 assert.match(fonte("src/features/production/analytics/gestaoVistaJamile.css"),/\.gv-pbars-compacto \.gv-pbar-linha\{height:\.8em\}/);
});
