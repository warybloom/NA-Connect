// 01. VISÃO GERAL — adendos de 20/10/2026 em % EMISSÃO POR DISCIPLINA:
//  (1) nenhum texto "META HOJE: x de y (z%)" / "META ATÉ …" visível (legenda e sob os gauges) — só a seta vermelha, com a meta
//      exata no tooltip/aria-label;
//  (2) cor do arco por disciplina: ADIANTADA verde, NO PRAZO azul, ATRASADA vermelho (prioridade), INDETERMINADA cinza; a seta da
//      meta continua sempre vermelha (halo claro quando o arco é vermelho).
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import path from "node:path";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {classificarSituacaoDisciplina,criarCatalogo,semanaDoCorte,visaoGeralDaSemana,
 type DocumentoLdpEmissao,type ProgramacaoLinha,type ProgramacaoResposta} from "../gestaoVistaJamileLogic";
import {Gauge,Overview} from "../GestaoVistaJamilePage";
import {dataCivilBahia} from "@/features/tv/tvLogic";
import type {FactsReport} from "@/components/pmo/pmoFacts";

const fonte=(relativo:string)=>{
 for(const base of [process.cwd(),path.resolve(process.cwd(),"ClientApp")]){try{return readFileSync(path.resolve(base,relativo),"utf8");}catch{/* próximo */}}
 throw new Error(`Fonte não encontrada: ${relativo}`);
};
// Semana domingo 11/10 → sábado 17/10/2026.
const SEMANA={start:"2026-10-11",end:"2026-10-17"};
const SEG="2026-10-12",TER="2026-10-13",QUA="2026-10-14",QUI="2026-10-15",SEX="2026-10-16",SAB="2026-10-17";
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
const CIVIL={disciplinaNorma:"CIVIL",disciplinaCanonica:"CIVIL"};
const ELETRICA={disciplinaNorma:"ELÉTRICA",disciplinaCanonica:"ELECTRICAL"};
const vg=(linhas:ProgramacaoLinha[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje:string,semana=SEMANA)=>
 visaoGeralDaSemana({dados:[resposta(P1,linhas,semana)],projetos:PROJETOS,cat:criarCatalogo([],PROJETOS),semana,hoje,ldp:new Map(Object.entries(ldp))});
/** Situação de UMA disciplina (todas as linhas de MECÂNICA, salvo indicação). */
const sit=(linhas:ProgramacaoLinha[],ldp:Record<string,DocumentoLdpEmissao[]>,hoje:string,semana=SEMANA)=>{
 const r=vg(linhas,ldp,hoje,semana);assert.equal(r.porDisciplina.length,1);return r.porDisciplina[0].meta;
};

/* ------------------------------------------------------------------------------- helper puro */
test("classificarSituacaoDisciplina: atraso comprovado > vencido sem prova > antecipação > no prazo",()=>{
 const c=(desvio:number,indeterminados:number,antecipados:number)=>classificarSituacaoDisciplina({desvio,indeterminados,antecipados});
 assert.equal(c(0,0,0),"no-prazo");
 assert.equal(c(0,0,2),"adiantada");
 assert.equal(c(1,0,0),"atrasada");
 assert.equal(c(1,0,5),"atrasada","antecipação nunca compensa atraso");
 assert.equal(c(1,3,5),"atrasada","atraso comprovado vence a indeterminação");
 assert.equal(c(0,1,0),"indeterminada");
 assert.equal(c(0,1,4),"indeterminada","vencido sem prova pode esconder atraso: nunca verde");
});

/* ------------------------------------------------------------------------------- regras sobre a 01 */
test("ATRASADA: um vencido não emitido tem prioridade sobre documento futuro já emitido antes do prazo",()=>{
 const m=sit([linha("A",SEG),linha("B",SEX)],{[P1]:[ldpDoc("A"),emitido("B",TER)]},QUA);
 assert.deepEqual([m.situacao,m.desvio,m.antecipados],["atrasada",1,1]);
});

test("ADIANTADA: documento previsto para depois de hoje já comprovadamente emitido, sem vencido em aberto",()=>{
 const m=sit([linha("A",SEG),linha("B",SEX)],{[P1]:[emitido("A",SEG),emitido("B",TER)]},QUA);
 assert.deepEqual([m.situacao,m.desvio,m.antecipados],["adiantada",0,1]);
 // Emitido HOJE com prazo hoje não é antecipação.
 assert.equal(sit([linha("H",QUA)],{[P1]:[emitido("H",QUA)]},QUA).situacao,"no-prazo");
 // Emitido pelo status (sem data) e previsto para depois: comprovado emitido até hoje → adiantada.
 assert.equal(sit([linha("S",SEX)],{[P1]:[ldpDoc("S",{emitido:true})]},QUA).situacao,"adiantada");
});

test("NO PRAZO: previsto para HOJE e ainda pendente não é atraso até a meia-noite (Bahia); no dia seguinte vira ATRASADA",()=>{
 const l=[linha("H",QUA)],ldp={[P1]:[ldpDoc("H")]};
 assert.equal(sit(l,ldp,QUA).situacao,"no-prazo");
 assert.equal(sit(l,ldp,QUI).situacao,"atrasada");
 // Fuso: 02:59 UTC de quinta ainda é quarta na Bahia.
 assert.equal(sit(l,ldp,dataCivilBahia(new Date("2026-10-15T02:59:00Z"))).situacao,"no-prazo");
 assert.equal(sit(l,ldp,dataCivilBahia(new Date("2026-10-15T03:00:00Z"))).situacao,"atrasada");
});

test("Meta zero hoje (só documentos futuros, nada emitido) = NO PRAZO, nunca vermelho",()=>{
 const m=sit([linha("X",SEX),linha("Y",SAB)],{[P1]:[ldpDoc("X"),ldpDoc("Y")]},SEG);
 assert.deepEqual([m.meta,m.metaPct,m.situacao],[0,0,"no-prazo"]);
});

test("Regularização: documento vencido emitido depois do prazo sai do atraso no dia da emissão",()=>{
 const l=[linha("A",SEG)],ldp={[P1]:[emitido("A",QUA)]};
 assert.equal(sit(l,ldp,TER).situacao,"atrasada","TER: emitido só na QUA — ainda em aberto");
 assert.equal(sit(l,ldp,QUA).situacao,"no-prazo");
 assert.equal(sit(l,ldp,QUI).situacao,"no-prazo");
});

test("Sem comprovação: vencido sem prova = INDETERMINADA (cinza), nunca atraso nem verde; só em hoje/futuro não bloqueia",()=>{
 // LDP do projeto ausente, documento vencido.
 assert.equal(sit([linha("A",SEG)],{},QUA).situacao,"indeterminada");
 // Código fora da LDP vencido + antecipado comprovado: não vira verde.
 const m=sit([linha("FORA",SEG),linha("B",SEX)],{[P1]:[emitido("B",TER)]},QUA);
 assert.deepEqual([m.situacao,m.indeterminados,m.antecipados],["indeterminada",1,1]);
 // Atraso comprovado + vencido sem prova: ATRASADA (o atraso comprovado basta).
 assert.equal(sit([linha("A",SEG),linha("FORA",SEG)],{[P1]:[ldpDoc("A")]},QUA).situacao,"atrasada");
 // Sem prova só em documento de hoje ou futuro: não pode ser atraso → NO PRAZO.
 assert.equal(sit([linha("FORA-H",QUA),linha("FORA-F",SEX)],{[P1]:[]},QUA).situacao,"no-prazo");
 // Sem código de documento vencido: indeterminada.
 assert.equal(sit([linha(null,SEG)],{[P1]:[]},QUA).situacao,"indeterminada");
});

test("Semana concluída (foto do sábado): emissão depois do sábado continua atraso; sem antecipação na foto",()=>{
 const H="2026-10-25";
 assert.equal(sit([linha("TARDE",QUA)],{[P1]:[emitido("TARDE","2026-10-19")]},H).situacao,"atrasada");
 assert.equal(sit([linha("REG",QUA)],{[P1]:[emitido("REG",QUI)]},H).situacao,"no-prazo","regularizado antes do sábado");
 const m=sit([linha("CEDO",SEX)],{[P1]:[emitido("CEDO",TER)]},H);
 assert.deepEqual([m.situacao,m.antecipados],["no-prazo",0],"no fechamento tudo já venceu: não há futuro a antecipar");
 // Emitido sem data em semana passada: sem comprovação → indeterminada.
 assert.equal(sit([linha("SD",QUA)],{[P1]:[ldpDoc("SD",{emitido:true})]},H).situacao,"indeterminada");
});

test("Semana futura: sem antecipação = NO PRAZO; documento já emitido antes do prazo = ADIANTADA",()=>{
 const H="2026-10-06";
 assert.equal(sit([linha("F",QUA)],{[P1]:[ldpDoc("F")]},H).situacao,"no-prazo");
 assert.equal(sit([linha("F",QUA)],{[P1]:[emitido("F","2026-10-05")]},H).situacao,"adiantada");
 // Emissão com data posterior a hoje não comprova nada até hoje.
 assert.equal(sit([linha("F",QUA)],{[P1]:[emitido("F","2026-10-07")]},H).situacao,"no-prazo");
});

test("Cada disciplina tem a sua situação; antecipação de uma não afeta outra; total coerente",()=>{
 const l=[linha("M-1",SEG),linha("M-2",SEX),linha("C-1",SEG,CIVIL),linha("E-1",QUA,ELETRICA),linha("E-X",SEG,ELETRICA)];
 const r=vg(l,{[P1]:[emitido("M-1",SEG),emitido("M-2",TER),ldpDoc("C-1"),ldpDoc("E-1")]},QUA);
 const por=Object.fromEntries(r.porDisciplina.map(d=>[d.nome,d.meta.situacao]));
 assert.deepEqual(por,{MECÂNICA:"adiantada",CIVIL:"atrasada",ELÉTRICA:"indeterminada"});
 assert.equal(r.meta.total.situacao,"atrasada");
 assert.equal(r.meta.total.antecipados,r.porDisciplina.reduce((s,d)=>s+d.meta.antecipados,0));
});

/* ------------------------------------------------------------------------------- Gauge e 01 renderizados */
/** Texto VISÍVEL do HTML: sem <title> (tooltip SVG) e sem atributos (title/aria-label). */
const visivel=(html:string)=>html.replace(/<title>[\s\S]*?<\/title>/g,"").replace(/\s[a-z-]+="[^"]*"/g,"").replace(/<[^>]+>/g," ");

test("Gauge: classe/atributo da situação, seta vermelha sempre presente, meta só no aria-label; sem situação = visual anterior",()=>{
 for(const s of ["adiantada","no-prazo","atrasada","indeterminada"] as const){
  const g=renderToStaticMarkup(createElement(Gauge,{title:"CIVIL",value:40,fracao:"2 de 5",situacao:s,
   meta:{pct:60,texto:"META HOJE: 3 de 5 (60,0%)",dica:"Planejado 3 de 5"}}));
  assert.match(g,new RegExp(`class="gv-gauge gv-gauge-com-meta gv-gauge-sit-${s}"[^>]*data-situacao="${s}"`));
  assert.match(g,/data-testid="gv-gauge-alvo" data-pct="60"><title>Planejado 3 de 5<\/title><line class="gv-gauge-alvo-halo"[^>]*><\/line><line [^>]*><\/line><polygon points="[^"]+"><\/polygon><\/g>/);
  assert.match(g,/aria-label="CIVIL: 40,0% · Situação: [^·]+ · META HOJE: 3 de 5 \(60,0%\)"/);
  assert.doesNotMatch(visivel(g),/META|DESVIO/);
  assert.match(visivel(g),/40,0%[\s\S]*2 de 5/);
 }
 // 0% atrasada: sem arco de progresso, a faixa (track) carrega o tom vermelho via CSS — o markup tem a classe.
 const zero=renderToStaticMarkup(createElement(Gauge,{title:"X",value:0,situacao:"atrasada",meta:{pct:30,texto:"META HOJE: 1 de 3 (33,3%)",dica:""}}));
 assert.match(zero,/gv-gauge-sit-atrasada/);assert.doesNotMatch(zero,/gv-gauge-fill/);assert.match(zero,/gv-gauge-track/);
 const antigo=renderToStaticMarkup(createElement(Gauge,{title:"X",value:50,fracao:"1 de 2"}));
 assert.doesNotMatch(antigo,/gv-gauge-sit|data-situacao|Situação/);
 assert.match(antigo,/^<figure class="gv-gauge"><figcaption>X<\/figcaption><svg viewBox="0 0 200 122" role="img" aria-label="X: 50,0%"/);
});

const reportsDe=(ldp:Record<string,DocumentoLdpEmissao[]>)=>PROJETOS.map(p=>({projectId:p.id,documentos:ldp[p.id]??[]})) as unknown as FactsReport[];
const render=(dados:ProgramacaoResposta[],ldp:Record<string,DocumentoLdpEmissao[]>,semana=SEMANA)=>renderToStaticMarkup(createElement(Overview,{reports:reportsDe(ldp),dados,
 selection:PROJETOS.map(p=>({...p,hubId:""})) as never,semana,cat:criarCatalogo([],PROJETOS),carregando:false,onSemana:()=>undefined}));
const gauges=(html:string)=>[...html.matchAll(/<figure class="gv-gauge gv-gauge-com-meta gv-gauge-sit-([a-z-]+)" title="([^"]*)" data-situacao="[a-z-]+"><figcaption>([^<]+)</g)]
 .map(m=>({situacao:m[1],dica:m[2],nome:m[3]}));

test("01 renderizada (semana concluída 11–17/10): cor por disciplina, sem META HOJE/META ATÉ/DESVIO visíveis, seta em todos, legenda de cores",()=>{
 const l=[linha("M-1",SEG),linha("C-1",SEG,CIVIL),linha("C-2",QUA,CIVIL),linha("E-X",SEG,ELETRICA)];
 const ldp={[P1]:[emitido("M-1",SEG),ldpDoc("C-1"),emitido("C-2",QUA)],[P2]:[]};
 const html=render([resposta(P1,l),resposta(P2,[])],ldp);
 const g=gauges(html);
 assert.deepEqual(g.map(x=>[x.nome,x.situacao]),[["CIVIL","atrasada"],["ELÉTRICA","indeterminada"],["MECÂNICA","no-prazo"]]);
 assert.match(g[0].dica,/^Situação: Atrasada — corte SÁB 17\/10\/2026 \(Bahia\) · 1 documento\(s\) previsto\(s\) até SÁB 17\/10\/2026 sem emissão comprovada/);
 assert.match(g[1].dica,/^Situação: Indeterminada — corte SÁB 17\/10\/2026 \(Bahia\) · Sem comprovação suficiente: 1 documento\(s\) vencido\(s\)/);
 assert.match(g[2].dica,/^Situação: No prazo — /);
 // Meta exata segue no tooltip e no aria-label; nada de meta/desvio em texto visível.
 assert.match(g[0].dica,/Planejado \(meta acumulada\): 2 de 2/);
 assert.equal([...html.matchAll(/data-testid="gv-gauge-alvo"/g)].length,3,"seta vermelha em cada gauge");
 const quadro=visivel(html.slice(html.indexOf("<header>% EMISSÃO POR DISCIPLINA</header>"),html.indexOf('<p class="gv-rule">Fonte:')));
 assert.doesNotMatch(quadro,/META|DESVIO \d|Desvio \d/);
 assert.match(quadro,/EMITIDOS ATÉ 17\/10\/2026/);
 assert.doesNotMatch(html,/gv-marca-meta|gv-gauge-meta-alvo|gv-dot-gauge/);
 // Legenda mínima de cores, centralizada na faixa existente; cinza só aparece porque há gauge indeterminado.
 assert.match(html,/data-testid="gv-vg-legenda-situacoes"><span><i class="gv-dot-sit gv-dot-sit-adiantada"><\/i>ADIANTADA<\/span><span><i class="gv-dot-sit gv-dot-sit-no-prazo"><\/i>NO PRAZO<\/span><span><i class="gv-dot-sit gv-dot-sit-atrasada"><\/i>ATRASADA<\/span><span><i class="gv-dot-sit gv-dot-sit-indeterminada"><\/i>SEM COMPROVAÇÃO<\/span><\/span>/);
 const sem=render([resposta(P1,[linha("M-1",SEG)]),resposta(P2,[])],{[P1]:[emitido("M-1",SEG)],[P2]:[]});
 assert.doesNotMatch(sem,/gv-dot-sit-indeterminada/);
});

test("01 renderizada (semana corrente, hoje real da Bahia): documento previsto hoje e pendente = NO PRAZO (azul), não vermelho",()=>{
 const hoje=dataCivilBahia(new Date()),semana=semanaDoCorte(hoje);
 const html=render([resposta(P1,[linha("HOJE-1",hoje)],semana),resposta(P2,[],semana)],{[P1]:[ldpDoc("HOJE-1")],[P2]:[]},semana);
 assert.deepEqual(gauges(html).map(x=>x.situacao),["no-prazo"]);
 assert.doesNotMatch(visivel(html),/META HOJE/);
 assert.match(html,/aria-label="MECÂNICA: 0,0% · Situação: No prazo · META HOJE: 1 de 1 \(100,0%\)"/);
});

/* ------------------------------------------------------------------------------- CSS: cores, temas e contraste */
const lum=(hex:string)=>{const c=[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16)/255).map(v=>v<=.03928?v/12.92:((v+.055)/1.055)**2.4);
 return .2126*c[0]+.7152*c[1]+.0722*c[2];};
const contraste=(a:string,b:string)=>{const [x,y]=[lum(a),lum(b)].sort((p,q)=>q-p);return (x+.05)/(y+.05);};

test("CSS: arco por situação (claro e escuro), seta sempre --gv-alvo com halo claro no arco vermelho; contraste ≥ 3:1",()=>{
 const css=fonte("src/features/production/analytics/gestaoVistaJamile.css");
 const tokens=(seletor:string)=>{
  const blocos=[...css.matchAll(new RegExp(`(?:^|\\n)${seletor.replace(/\./g,"\\.")}\\{([^}]*)\\}`,"g"))].map(m=>m[1]).join(";");
  return Object.fromEntries([...blocos.matchAll(/--([a-z0-9-]+):(#[0-9a-f]{3}(?:[0-9a-f]{3})?\b|var\(--[a-z0-9-]+\))/g)]
   .map(m=>[m[1],/^#[0-9a-f]{3}$/.test(m[2])?"#"+[...m[2].slice(1)].map(c=>c+c).join(""):m[2]]));
 };
 const resolver=(t:Record<string,string>)=>Object.fromEntries(Object.entries(t).map(([k,v])=>[k,v.startsWith("var(")?t[v.slice(6,-1)]:v]));
 const claroBruto=tokens(".gv-shell"),claro=resolver(claroBruto),escuro=resolver({...claroBruto,...tokens(".dark .gv-shell")});
 for(const t of [claro,escuro]){
  for(const k of ["gv-sit-adiantada","gv-sit-atrasada","gv-sit-indeterminada","gv-gauge-fill"])
   assert.ok(contraste(t[k],t["gv-gauge-bg"])>=3,`${k} × fundo do gauge: ${contraste(t[k],t["gv-gauge-bg"]).toFixed(2)}`);
  assert.ok(contraste(t["gv-sit-indeterminada"],t["gv-gauge-track"])>=1.8,"cinza da situação distinguível da faixa cinza");
  for(const k of ["gv-sit-adiantada","gv-sit-atrasada"])assert.ok(contraste(t[k],t[`${k}-faixa`])>=2.5,`${k} × sua faixa`);
  assert.ok(contraste(t["gv-gauge-fill"],t["gv-sit-no-prazo-faixa"])>=2.5,"azul × faixa azul-clara");
  // Seta vermelha × halo claro e halo × arco vermelho: a seta se destaca mesmo sobre o arco vermelho.
  assert.ok(contraste(t["gv-alvo"],t["gv-alvo-halo"])>=3,"seta × halo");
  assert.ok(contraste(t["gv-sit-atrasada"],t["gv-alvo-halo"])>=3,"arco vermelho × halo");
 }
 assert.notEqual(claro["gv-sit-atrasada"],escuro["gv-sit-atrasada"]);
 assert.match(css,/\.gv-gauge-com-meta\.gv-gauge-sit-adiantada \.gv-gauge-fill\{stroke:var\(--gv-sit-adiantada\)\}/);
 assert.match(css,/\.gv-gauge-com-meta\.gv-gauge-sit-atrasada \.gv-gauge-fill\{stroke:var\(--gv-sit-atrasada\)\}/);
 assert.match(css,/\.gv-gauge-com-meta\.gv-gauge-sit-indeterminada \.gv-gauge-fill\{stroke:var\(--gv-sit-indeterminada\)\}/);
 assert.doesNotMatch(css,/gv-gauge-sit-no-prazo \.gv-gauge-fill/,"no prazo = azul padrão (--gv-gauge-fill)");
 assert.match(css,/\.gv-gauge-com-meta\.gv-gauge-sit-no-prazo \.gv-gauge-track\{stroke:var\(--gv-sit-no-prazo-faixa\)\}/);
 assert.doesNotMatch(css,/gv-gauge-sit-indeterminada \.gv-gauge-track/,"indeterminada: faixa cinza neutra");
 // A seta nunca muda de cor: só --gv-alvo pinta traço e ponta; a situação só mexe no halo/contorno.
 assert.match(css,/\.gv-gauge-alvo line\{stroke:var\(--gv-alvo\);/);
 assert.match(css,/\.gv-gauge-alvo polygon\{fill:var\(--gv-alvo\);/);
 for(const regra of css.match(/[^}]*gv-gauge-sit-[^{]*gv-gauge-alvo[^{]*\{[^}]*\}/g)??[])
  assert.match(regra,/\{stroke:var\(--gv-alvo-halo\);stroke-width:[\d.]+\}$/,regra);
 assert.match(css,/\.gv-gauge-com-meta\.gv-gauge-sit-atrasada \.gv-gauge-alvo line\.gv-gauge-alvo-halo\{stroke:var\(--gv-alvo-halo\)/);
 assert.doesNotMatch(css,/gv-marca-meta|gv-gauge-meta-alvo/,"CSS do texto da meta removido");
});
