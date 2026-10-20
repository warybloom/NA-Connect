// 01. VISÃO GERAL — metas e layout (20/10/2026): 6 cartões (A EMITIR novo), barras por projeto com programados × emitidos visíveis,
// meta acumulada por disciplina (marca vermelha no gauge) derivada das datas da programação, desvio no corte sem dupla contagem,
// corte pela data civil da Bahia e layout mais largo só na 01 (02 e demais telas intactas).
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import path from "node:path";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {
 corteDaMeta,criarCatalogo,ehAtividadeDeEmissao,geometriaMarcaMeta,gradeDosGauges,metaAcumuladaDaSemana,serieMetaAcumulada,visaoGeralDaSemana,
 type DocumentoLdpEmissao,type ProgramacaoLinha,type ProgramacaoResposta,
} from "../gestaoVistaJamileLogic";
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
const resposta=(projectId:string,linhas:ProgramacaoLinha[]):ProgramacaoResposta=>({projectId,inicio:SEMANA.start,fim:SEMANA.end,ultimaSincronizacao:null,
 registrosProjeto:linhas.length,registrosSemData:0,datasComProgramacao:[...new Set(linhas.map(l=>l.dataAtividade.slice(0,10)))].map(data=>({data,quantidade:1})),linhas});
const ldpDoc=(codigo:string,o:Partial<DocumentoLdpEmissao>={}):DocumentoLdpEmissao=>({codigo,emitido:false,cancelado:false,dataEmissao:null,...o});
const vg=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje:string)=>
 visaoGeralDaSemana({dados,projetos:PROJETOS,cat:criarCatalogo([],PROJETOS),semana:SEMANA,hoje,ldp:new Map(Object.entries(ldp))});
const metaMec=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje:string)=>vg(dados,ldp,hoje).porDisciplina[0].meta;

/* ------------------------------------------------------------------------------- meta acumulada */
// 10 documentos de MECÂNICA com a última atividade em SEG(1) TER(2) QUI(4) SEX(3): meta 10/30/30/70/100 sai das datas, sem constante.
const DEZ=[[SEG,1],[TER,2],[QUI,4],[SEX,3]].flatMap(([d,n],i)=>Array.from({length:n as number},(_,j)=>linha(`MEC-${i}${j}`,d as string)));
test("Exemplo 10/30/30/70/100 derivado das datas previstas (seg→sex), cumulativo e sem hardcode",()=>{
 const serie=serieMetaAcumulada(DEZ.map(l=>l.dataAtividade),SEMANA);
 assert.deepEqual(serie.map(x=>x.metaPct),[0,10,30,30,70,100,100]);
 const porDia=[SEG,TER,QUA,QUI,SEX].map(hoje=>metaMec([resposta(P1,DEZ)],{},hoje));
 assert.deepEqual(porDia.map(m=>m.metaPct),[10,30,30,70,100]);
 assert.deepEqual(porDia.map(m=>m.meta),[1,3,3,7,10]);
 assert.ok(porDia.every(m=>m.documentos===10));
 // Nunca diminui ao longo da semana.
 const todos=[DOM,SEG,TER,QUA,QUI,SEX,SAB].map(h=>metaMec([resposta(P1,DEZ)],{},h).meta);
 assert.ok(todos.every((v,i)=>i===0||v>=todos[i-1]),todos.join(","));
});

test("Documento com atividades em 2 dias conta UMA vez, na última atividade (sem atividade de emissão identificável)",()=>{
 const l=[linha("DOC-A",SEG,{tarefa:"ELABORAÇÃO"}),linha("DOC-A",QUI,{tarefa:"VERIFICAÇÃO"}),linha("DOC-B",TER)];
 const r=vg([resposta(P1,l)],{},QUA);
 const a=r.itens.find(x=>x.codigo==="DOC-A")!;
 assert.equal(a.dataPrevista,QUI);assert.equal(a.fontePrevista,"ultima-atividade");
 assert.deepEqual([r.porDisciplina[0].meta.documentos,r.porDisciplina[0].meta.meta],[2,1],"QUA: só DOC-B (TER) está na meta");
 assert.deepEqual([metaMec([resposta(P1,l)],{},QUI).meta,metaMec([resposta(P1,l)],{},SAB).meta],[2,2],"DOC-A entra uma vez na QUI");
});

test("Atividade de EMISSÃO inequívoca define a data prevista; título com a palavra 'emissão' não",()=>{
 assert.ok(["EMISSÃO","Emissão do documento","EMISSAO PARA APROVACAO","Issuance","EMISSÃO REV.0"].every(ehAtividadeDeEmissao));
 assert.ok(!["SISTEMA DE EMISSÃO DE GASES","PRÉ-EMISSÃO","ELABORAÇÃO","LDP - EMISSÕES ATMOSFÉRICAS",null,""].some(ehAtividadeDeEmissao));
 const l=[linha("DOC-E",TER,{tarefa:"EMISSÃO"}),linha("DOC-E",QUI,{tarefa:"ATENDIMENTO A COMENTÁRIOS"}),
  linha("DOC-G",SEG,{tarefa:"SISTEMA DE EMISSÃO DE GASES"}),linha("DOC-G",SEX,{tarefa:"SISTEMA DE EMISSÃO DE GASES"})];
 const r=vg([resposta(P1,l)],{},QUA);
 const e=r.itens.find(x=>x.codigo==="DOC-E")!,g=r.itens.find(x=>x.codigo==="DOC-G")!;
 assert.deepEqual([e.dataPrevista,e.fontePrevista],[TER,"emissao"]);
 assert.deepEqual([g.dataPrevista,g.fontePrevista],[SEX,"ultima-atividade"]);
});

test("Corte: semana futura sem meta e sem desvio; semana em curso = hoje; semana concluída = sábado",()=>{
 assert.deepEqual(corteDaMeta(SEMANA,"2026-10-05"),{estado:"futura",corte:null,corteRealizado:"2026-10-05",corteAtraso:null});
 assert.deepEqual(corteDaMeta(SEMANA,QUA),{estado:"corrente",corte:QUA,corteRealizado:QUA,corteAtraso:TER});
 assert.deepEqual(corteDaMeta(SEMANA,"2026-10-20"),{estado:"concluida",corte:SAB,corteRealizado:SAB,corteAtraso:SAB});
 const futura=metaMec([resposta(P1,DEZ)],{},"2026-10-05");
 assert.deepEqual([futura.meta,futura.metaPct,futura.desvio,futura.indeterminados],[0,0,0,0]);
 const passada=metaMec([resposta(P1,DEZ)],{},"2026-10-28");
 assert.deepEqual([passada.meta,passada.metaPct],[10,100]);
});

test("Domingo e sábado: domingo é o 1º dia do corte (documento de domingo já é meta); sábado fecha a semana",()=>{
 const l=[linha("D-DOM",DOM),linha("D-SAB",SAB)];
 assert.deepEqual([metaMec([resposta(P1,l)],{},DOM).meta,metaMec([resposta(P1,l)],{},SEX).meta,metaMec([resposta(P1,l)],{},SAB).meta],[1,1,2]);
 assert.equal(corteDaMeta(SEMANA,DOM).estado,"corrente");
 assert.equal(corteDaMeta(SEMANA,SAB).estado,"corrente");
});

test("Timezone: o dia vem da data civil da Bahia (UTC−3), não do UTC da máquina",()=>{
 const sabadoNoite=dataCivilBahia(new Date("2026-10-18T02:59:00Z"));   // 17/10 23:59 na Bahia
 const domingoZero=dataCivilBahia(new Date("2026-10-18T03:00:00Z"));   // 18/10 00:00 na Bahia
 assert.equal(sabadoNoite,SAB);assert.equal(domingoZero,"2026-10-18");
 assert.deepEqual(corteDaMeta(SEMANA,sabadoNoite),{estado:"corrente",corte:SAB,corteRealizado:SAB,corteAtraso:SEX});
 assert.equal(corteDaMeta(SEMANA,domingoZero).estado,"concluida");
});

/* ------------------------------------------------------------------------------- realizado e desvio */
test("Emissão antes/depois da data prevista: antes = realizado; depois mas até o corte = realizado e não é desvio; depois do corte = desvio (só dias anteriores)",()=>{
 const l=[linha("ANTES",TER),linha("DEPOIS",SEG),linha("TARDE",TER)];
 const ldp={[P1]:[ldpDoc("ANTES",{emitido:true,dataEmissao:SEG}),ldpDoc("DEPOIS",{emitido:true,dataEmissao:QUA}),ldpDoc("TARDE",{emitido:true,dataEmissao:"2026-10-19"})]};
 const qui=metaMec([resposta(P1,l)],ldp,QUI);
 assert.deepEqual([qui.meta,qui.realizados,qui.desvio],[3,2,1],"QUI: só TARDE é desvio");
 const ter=metaMec([resposta(P1,l)],ldp,TER);
 assert.deepEqual([ter.meta,ter.realizados,ter.desvio],[3,1,1],"TER: só DEPOIS (prevista SEG, emitida QUA) é backlog; TARDE é de hoje e ainda não é desvio");
 // Semana concluída (corte sábado): TARDE emitido em 19/10 continua desvio da semana; o realizado do gauge é até sábado.
 const fim=metaMec([resposta(P1,l)],ldp,"2026-10-25");
 assert.deepEqual([fim.meta,fim.realizados,fim.desvio,fim.realizadoPct],[3,2,1,2/3*100]);
});

test("Backlog de dia anterior SEM dupla contagem: pendência de segunda conta 1 na terça, quarta…; hoje só entra no dia seguinte",()=>{
 const l=[linha("SEG-1",SEG),linha("QUA-1",QUA),linha("SEX-1",SEX)];
 const ldp={[P1]:[ldpDoc("SEG-1"),ldpDoc("QUA-1"),ldpDoc("SEX-1")]};
 assert.deepEqual([SEG,TER,QUA,QUI,SEX,SAB].map(h=>metaMec([resposta(P1,l)],ldp,h).desvio),[0,1,1,2,2,3]);
 // Regularizado na quinta: sai do backlog a partir da quinta (não fica "preso" ao dia anterior).
 const reg={[P1]:[ldpDoc("SEG-1",{emitido:true,dataEmissao:QUI}),ldpDoc("QUA-1"),ldpDoc("SEX-1")]};
 assert.deepEqual([QUA,QUI,SEX,SAB].map(h=>metaMec([resposta(P1,l)],reg,h).desvio),[1,1,1,2]);
});

test("Sem comprovação: nunca é desvio (fica indeterminado) e deixa o percentual realizado indisponível",()=>{
 const l=[linha("FORA-LDP",SEG),linha(null,SEG),linha("OK",SEG),linha("PEND",SEG)];
 const ldp={[P1]:[ldpDoc("OK",{emitido:true,dataEmissao:SEG}),ldpDoc("PEND")]};
 const m=metaMec([resposta(P1,l)],ldp,QUA);
 assert.deepEqual([m.documentos,m.meta,m.realizados,m.desvio,m.indeterminados,m.semComprovacao,m.realizadoPct,m.metaPct],[4,4,1,1,2,2,null,100]);
 // Emitido SEM data: comprova no corte de hoje (semana em curso); em corte passado vira sem comprovação.
 const semData={[P1]:[ldpDoc("X",{emitido:true})]};
 assert.equal(metaMec([resposta(P1,[linha("X",SEG)])],semData,QUA).realizados,1);
 const passado=metaMec([resposta(P1,[linha("X",SEG)])],semData,"2026-10-25");
 assert.deepEqual([passado.realizados,passado.indeterminados,passado.desvio],[0,1,0]);
});

test("Dois projetos com o mesmo código: documentos distintos, cada um conciliado com a LDP do SEU projeto",()=>{
 const dados=[resposta(P1,[linha("X-1",SEG)]),resposta(P2,[linha("X-1",SEG)])];
 const ldp={[P1]:[ldpDoc("X-1",{emitido:true,dataEmissao:SEG})],[P2]:[ldpDoc("X-1")]};
 const r=vg(dados,ldp,QUA);
 assert.equal(r.porDisciplina.length,1);
 const m=r.porDisciplina[0].meta;
 assert.deepEqual([m.documentos,m.meta,m.realizados,m.desvio],[2,2,1,1]);
 assert.deepEqual([r.meta.total.documentos,r.meta.total.desvio],[2,1]);
});

test("Total = soma das disciplinas (cada documento em uma só disciplina); cartões = coortes do total",()=>{
 const l=[...DEZ,linha("CIV-1",SEG,{disciplinaNorma:"CIVIL",disciplinaCanonica:"CIVIL"}),linha("CIV-2",SEX,{disciplinaNorma:"CIVIL",disciplinaCanonica:"CIVIL"})];
 const ldp={[P1]:l.map(x=>ldpDoc(x.documento!,x.documento==="CIV-1"?{emitido:true,dataEmissao:SEG}:{}))};
 const r=vg([resposta(P1,l)],ldp,QUA);
 const soma=(k:"documentos"|"meta"|"desvio"|"realizados")=>r.porDisciplina.reduce((s,d)=>s+d.meta[k],0);
 assert.deepEqual([r.meta.total.documentos,r.meta.total.meta,r.meta.total.desvio,r.meta.total.realizados],[soma("documentos"),soma("meta"),soma("desvio"),soma("realizados")]);
 assert.deepEqual([r.meta.total.documentos,r.meta.total.meta,r.meta.total.desvio],[12,4,3]);
 // Cartões (20/10): aderência = previstos até hoje emitidos (1 de 4); desvio = backlog até ontem (3 de 4); A EMITIR = saldo nunca negativo.
 assert.equal(r.aderencia,25);assert.equal(r.desvio,75);assert.equal(r.atrasados,r.meta.total.desvio);
 assert.equal(r.aEmitir,11);
 assert.equal(vg([resposta(P1,[])],{},QUA).aEmitir,0);
});

test("Grade dos gauges: até 4 em 2 colunas; 5–10 (7–10 disciplinas na TV) em 3 colunas; paginação de 10 preservada",()=>{
 assert.deepEqual([1,2,4,5,6,7,9,10].map(n=>{const g=gradeDosGauges(n);return `${g.colunas}x${g.linhas}`;}),["2x2","2x2","2x2","3x2","3x2","3x3","3x3","3x4"]);
 assert.match(fonte("src/features/production/analytics/GestaoVistaJamilePage.tsx"),/const GAUGES_POR_PAGINA=10;/);
});

/* ------------------------------------------------------------------------------- SVG */
test("Marca da meta no arco de 180°: 0% à esquerda, 50% no topo, 100% à direita; fora de 0–100 é limitado",()=>{
 const g0=geometriaMarcaMeta(0),g50=geometriaMarcaMeta(50),g100=geometriaMarcaMeta(100);
 assert.deepEqual([g0.x1,g0.y1,g0.x2,g0.y2],[40,100,4,100]);
 assert.deepEqual([g50.x1,g50.y1,g50.x2,g50.y2],[100,40,100,4]);
 assert.deepEqual([g100.x1,g100.y1,g100.x2,g100.y2],[160,100,196,100]);
 // Seta por fora do arco, com a ponta no traço (r=96) — dentro do viewBox -11 -11 222 133.
 assert.equal(g50.seta,"100,4 105.5,-8 94.5,-8");
 for(const g of [g0,g50,g100])for(const [x,y] of g.seta.split(" ").map(p=>p.split(",").map(Number)))assert.ok(x>=-11&&x<=211&&y>=-11&&y<=122,g.seta);
 assert.equal(geometriaMarcaMeta(-20).pct,0);assert.equal(geometriaMarcaMeta(140).pct,100);assert.equal(geometriaMarcaMeta(Number.NaN).pct,0);
});

test("Gauge da 01: marca vermelha com tooltip e fração; meta textual SÓ no aria-label (adendo 20/10); sem meta (pct null) não inventa marca; gauge sem meta = markup original",()=>{
 const com=renderToStaticMarkup(createElement(Gauge,{title:"MECÂNICA",value:30,fracao:"3 de 10",meta:{pct:70,texto:"META HOJE: 7 de 10 (70,0%)",dica:"Meta hoje"}}));
 assert.match(com,/data-testid="gv-gauge-alvo" data-pct="70"/);
 assert.match(com,/<title>Meta hoje<\/title>/);
 assert.match(com,/class="gv-gauge-fracao"[^>]*>3 de 10</);
 assert.doesNotMatch(com,/gv-gauge-meta-alvo|>META HOJE/);
 assert.match(com,/aria-label="MECÂNICA: 30,0% · META HOJE: 7 de 10 \(70,0%\)"/);
 assert.match(com,/viewBox="-11 -11 222 133"/);
 const semMeta=renderToStaticMarkup(createElement(Gauge,{title:"X",value:null,meta:{pct:null,texto:"META HOJE: 0 de 0 (—)",dica:""}}));
 assert.doesNotMatch(semMeta,/gv-gauge-alvo/);
 const original=renderToStaticMarkup(createElement(Gauge,{title:"X",value:50,fracao:"1 de 2"}));
 assert.match(original,/viewBox="0 0 200 122"/);
 assert.doesNotMatch(original,/gv-gauge-alvo|gv-gauge-meta|gv-gauge-com-meta/);
});

/* ------------------------------------------------------------------------------- tela 01 renderizada */
test("01 renderizada: 6 cartões na ordem, A EMITIR, DOCUMENTOS POR PROJETO sem contagem, números de programados e emitidos visíveis (zero inclusive)",()=>{
 const dados=[resposta(P1,DEZ),resposta(P2,[])];
 const reports=[{projectId:P1,documentos:DEZ.map(x=>ldpDoc(x.documento!,x.documento==="MEC-00"?{emitido:true,dataEmissao:SEG}:{}))},{projectId:P2,documentos:[]}] as unknown as FactsReport[];
 const html=renderToStaticMarkup(createElement(Overview,{reports,dados,selection:PROJETOS.map(p=>({...p,hubId:""})) as never,semana:SEMANA,
  cat:criarCatalogo([],PROJETOS),carregando:false,onSemana:()=>undefined}));
 const titulos=[...html.matchAll(/class="gv-tile-title">([^<]+)</g)].map(m=>m[1]);
 assert.deepEqual(titulos,["DOCUMENTOS PROGRAMADOS","EMITIDOS","EM ATRASO","A EMITIR","%ADERÊNCIA À PROGRAMAÇÃO","%DESVIO DA PROGRAMAÇÃO"]);
 assert.match(html,/A EMITIR<\/span><strong class="gv-tile-value">9</);
 assert.match(html,/<header>DOCUMENTOS POR PROJETO<\/header>/);
 const prog=[...html.matchAll(/data-testid="gv-vg-projeto-programados">(\d+)</g)].map(m=>m[1]);
 const emit=[...html.matchAll(/data-testid="gv-vg-projeto-emitidos">(\d+)</g)].map(m=>m[1]);
 assert.deepEqual([prog,emit],[["10","0"],["1","0"]]);
 // Semana 11–17/10 já concluída (hoje real ≥ 20/10/2026): meta no sábado = 100%, marca desenhada; o texto da meta só no aria-label.
 assert.match(html,/data-testid="gv-gauge-alvo" data-pct="100"/);
 assert.match(html,/aria-label="[^"]*META ATÉ SÁB 17\/10: 10 de 10 \(100,0%\)"/);
 assert.doesNotMatch(html.replace(/<title>[\s\S]*?<\/title>/g,"").replace(/\s[a-z-]+="[^"]*"/g,""),/META (HOJE|ATÉ)/);
 assert.match(html,/class="gv-gauge-fracao"[^>]*>1 de 10</);
 // DESVIO N não aparece mais sob os gauges (fica no tooltip); legenda "EMITIDOS ATÉ" do corte.
 assert.doesNotMatch(html,/DESVIO \d/);
 assert.match(html,/data-testid="gv-vg-legenda-emitidos">EMITIDOS ATÉ 17\/10\/2026</);
});

/* ------------------------------------------------------------------------------- escopo (02 e demais telas intactas) */
test("Escopo: classes de layout só na 01; CSS novo só com seletores da 01; 02 continua com o markup e as classes de antes",()=>{
 const src=fonte("src/features/production/analytics/GestaoVistaJamilePage.tsx");
 assert.match(src,/className=\{"gv-shell"\+\(screen==="visao-geral"\?" gv-shell-vg"\+\(navTv\?" gv-shell-vg-tv":""\):""\)\}/);
 const prog=src.slice(src.indexOf("function Programming("),src.indexOf("/* ---------------------------------------------------------------- PROGRAMAÇÃO DIÁRIA AUTOMÁTICA"));
 assert.doesNotMatch(prog,/gv-vg|gv-pbar|meta=|BarrasProjeto|gv-shell-vg/);
 assert.match(prog,/<div className="gv-kpis gv-three">/);
 const css=fonte("src/features/production/analytics/gestaoVistaJamile.css");
 const bloco=css.slice(css.indexOf("/* ------------------------------------------------------------------ 01. VISÃO GERAL — metas e layout"));
 const seletores=bloco.replace(/\/\*[\s\S]*?\*\//g,"").replace(/@media[^{]*\{/g,"").match(/[^{}]+(?=\{)/g)!.map(s=>s.trim()).filter(Boolean);
 assert.ok(seletores.length>20);
 const permitido=/gv-shell-vg|gv-vg-|gv-pbar|gv-gauge-com-meta|gv-gauge-alvo|gv-gauge-meta|gv-tile-nota|gv-legend-meta|^\.gv-shell$|^\.dark \.gv-shell$/;
 for(const s of seletores)for(const parte of s.split(/,(?![^(]*\))/))assert.match(parte.trim(),permitido,`seletor fora do escopo da 01: ${parte}`);
 // Cores das barras por projeto: programados = azul-escuro (navy), emitidos = verde (teal); meta = único vermelho novo.
 assert.match(bloco,/\.gv-pbar-prog i\{background:var\(--gv-navy\)\}/);
 assert.match(bloco,/\.gv-pbar-emit i\{background:var\(--gv-teal\)\}/);
 assert.match(bloco,/\.gv-gauge-alvo line\{stroke:var\(--gv-alvo\)/);
 // Os tokens --gv-alvo só são definidos aqui (claro e escuro).
 assert.match(bloco,/\.gv-shell\{--gv-alvo:#[0-9a-f]{6};--gv-alvo-texto:#[0-9a-f]{6}\}/);
 assert.match(bloco,/\.dark \.gv-shell\{--gv-alvo:#[0-9a-f]{6};--gv-alvo-texto:#[0-9a-f]{6}\}/);
});
