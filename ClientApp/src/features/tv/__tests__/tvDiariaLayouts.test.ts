// Programação Diária AUTOMÁTICA — exibições por slide (20/10/2026): Padrão (layout da 02 em Hoje/Pendências de ontem),
// Cards por disciplina e Uma tela por disciplina. Persistência por slide (layoutDiario/disciplina) compatível com o JSON
// anterior, duplicidade só para mesmo projeto + mesma exibição + mesma disciplina, limite 50 sem parcial, disciplinas do
// editor com a MESMA chave da tela (Norma/programação, nunca inventadas) e renderização distinta de cada exibição.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import { ConteudoTv, TELAS_TV_DATA_OBRIGATORIA, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO, TELAS_TV_SELECIONAVEIS, TV_PROGRAMACAO_DIARIA, TV_VISAO_GERAL } from "../tvScreens";
import {
  DATA_REFERENCIA_AUTO, LAYOUTS_DIARIOS, TV_LIMITES, chaveDiariaAutomatica, gerarTelasDiariasAutomaticas, layoutDoSlide, rotuloLayoutDiario, slideComProjetos,
  slideDiarioAutomatico, validarApresentacoes, type TvPresentation, type TvSlide, type TvSlideProjeto,
} from "../tvLogic";
import { itensParaAdicionar } from "../../settings/TelasTvConfigPage";
import { getTelaTv } from "../tvScreens";
import { DiariaAutomatica } from "../../production/analytics/GestaoVistaJamilePage";
import {
  analiseDiariaAutomatica, criarCatalogo, opcoesDisciplinaDiaria, type DisciplinasNorma, type ProgramacaoLinha, type ProgramacaoResposta,
} from "../../production/analytics/gestaoVistaJamileLogic";
import type { FactsReport } from "@/components/pmo/pmoFacts";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};
const P = (n: number, nome: string): TvSlideProjeto => ({ hubId: "", projectId: `b.${String(n).repeat(8)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(12)}`, projectName: nome });
const CHS = P(2, "CHS001 - ETHANOL PLANT"), BML = P(3, "BML011.016 - COOLING TOWER");
const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
const validar = (lista: TvPresentation[]) => validarApresentacoes(lista, permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_DATA_OBRIGATORIA);
const tv = (slides: TvSlide[], id = "tv-eng"): TvPresentation => ({ id, name: id.toUpperCase(), secondsPerScreen: 30, slides });
const MEC = { chave: "C:MECHANICAL", nome: "MECÂNICA" }, CIV = { chave: "C:CIVIL", nome: "CIVIL" };
const padrao = (p: TvSlideProjeto) => slideDiarioAutomatico(TV_PROGRAMACAO_DIARIA, p);
const cards = (p: TvSlideProjeto) => slideDiarioAutomatico(TV_PROGRAMACAO_DIARIA, p, "cards");
const disc = (p: TvSlideProjeto, d = MEC) => slideDiarioAutomatico(TV_PROGRAMACAO_DIARIA, p, "disciplina", d);
const gerar = (slides: TvSlide[], projetos: TvSlideProjeto[], o: Partial<Parameters<typeof gerarTelasDiariasAutomaticas>[0]> = {}) =>
  gerarTelasDiariasAutomaticas({ slides, projetos, telaDiaria: TV_PROGRAMACAO_DIARIA, telaVisaoGeral: TV_VISAO_GERAL,
    podeDiaria: () => true, podeVisaoGeral: () => true, visaoGeralDisponivel: true, ...o });

/* ------------------------------------------------------------------------------------------- persistência e validação */
test("Persistência por slide: padrão = formato anterior (sem campos novos); cards/disciplina gravam a exibição; data AUTO e fixa intactas", () => {
  assert.deepEqual(LAYOUTS_DIARIOS.map((l) => [l.id, l.rotulo]), [["padrao", "Padrão"], ["cards", "Cards por disciplina"], ["disciplina", "Uma tela por disciplina"]]);
  assert.deepEqual(padrao(CHS), slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], DATA_REFERENCIA_AUTO), "padrão idêntico ao slide AUTO anterior");
  assert.ok(!("layoutDiario" in padrao(CHS)) && !("disciplina" in padrao(CHS)));
  assert.deepEqual([cards(CHS).layoutDiario, cards(CHS).dataReferencia, cards(CHS).disciplina], ["cards", "AUTO", undefined]);
  assert.deepEqual([disc(CHS).layoutDiario, disc(CHS).disciplina, disc(CHS).disciplinaNome, disc(CHS).dataReferencia], ["disciplina", "C:MECHANICAL", "MECÂNICA", "AUTO"]);
  assert.equal(layoutDoSlide({}), "padrao", "JSON antigo (sem campo) = padrão");
  assert.equal(rotuloLayoutDiario(disc(CHS)), "Disciplina: MECÂNICA");
  assert.equal(validar([tv([padrao(CHS), cards(CHS), disc(CHS), disc(CHS, CIV), slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "2026-10-15")])]), null);
});

test("Validação: exibição desconhecida e 'disciplina' sem disciplina recusadas; duplicata só com mesmo projeto + exibição + disciplina", () => {
  assert.match(validar([tv([{ ...padrao(CHS), layoutDiario: "Cards" as never }])])!, /exibição inválida/);
  assert.match(validar([tv([{ ...disc(CHS), disciplina: "  " }])])!, /está sem disciplina/);
  // Mesma exibição + mesmo projeto (+ mesma disciplina) = duplicata; "padrao" explícito = ausente.
  assert.match(validar([tv([padrao(CHS), { ...padrao(CHS), layoutDiario: "padrao" }])])!, /automática repetida para CHS001/);
  assert.match(validar([tv([cards(CHS), cards(CHS)])])!, /repetida para CHS001 - ETHANOL PLANT \(Cards por disciplina\)/);
  assert.match(validar([tv([disc(CHS), disc(CHS)])])!, /repetida para .* \(Disciplina: MECÂNICA\)/);
  // Distintos: outra disciplina, outra exibição, outro projeto, outra apresentação.
  assert.equal(validar([tv([disc(CHS), disc(CHS, CIV), disc(BML), cards(CHS), padrao(CHS)]), tv([disc(CHS)], "tv-sala")]), null);
  assert.notEqual(chaveDiariaAutomatica(disc(CHS)), chaveDiariaAutomatica(disc(CHS, CIV)));
  assert.equal(chaveDiariaAutomatica(padrao(CHS)), chaveDiariaAutomatica({ ...padrao(CHS), layoutDiario: "padrao" }));
  // Editor manual (data fixa): exibição não entra; itens AUTO idênticos nunca são re-adicionados.
  assert.deepEqual(itensParaAdicionar(getTelaTv(TV_PROGRAMACAO_DIARIA)!, [CHS], [padrao(CHS)], "consolidado", DATA_REFERENCIA_AUTO), []);
});

/* ------------------------------------------------------------------------------------------- botão por exibição */
test("Botão / Cards: um slide 'cards' por projeto; padrão já existente do mesmo projeto NÃO impede (exibições distintas)", () => {
  const r = gerar([padrao(CHS)], [CHS, BML], { layout: "cards" });
  assert.equal(r.bloqueio, null);
  assert.deepEqual(r.novosSlides.map((s) => [s.projectId, s.layoutDiario]), [[CHS.projectId, "cards"], [BML.projectId, "cards"]]);
  assert.match(r.mensagens.join(" "), /2 tela\(s\) diária\(s\) automática\(s\) \(Cards por disciplina\) adicionada\(s\)/);
  assert.equal(validar([tv(r.slides!)]), null);
  const de_novo = gerar(r.slides!, [CHS, BML], { layout: "cards" });
  assert.equal(de_novo.slides, null);
  assert.match(de_novo.mensagens[0], /Nada a adicionar/);
});

test("Botão / Uma tela por disciplina: um slide por disciplina escolhida; repetição só da mesma disciplina é ignorada; sem disciplina nada muda", () => {
  const r = gerar([], [CHS], { layout: "disciplina", disciplinas: [MEC, CIV, MEC] });
  assert.deepEqual(r.novosSlides.map((s) => [s.projectId, s.layoutDiario, s.disciplina, s.disciplinaNome]),
    [[CHS.projectId, "disciplina", "C:MECHANICAL", "MECÂNICA"], [CHS.projectId, "disciplina", "C:CIVIL", "CIVIL"]]);
  assert.equal(r.slides![0].screenId, TV_VISAO_GERAL, "01 no início como no padrão");
  assert.match(r.mensagens.join(" "), /CHS001 · MECÂNICA, CHS001 · CIVIL/);
  const segunda = gerar(r.slides!, [CHS], { layout: "disciplina", disciplinas: [MEC, { chave: "N:ELETRICA", nome: "ELÉTRICA" }] });
  assert.deepEqual(segunda.novosSlides.map((s) => s.disciplina), ["N:ELETRICA"]);
  assert.match(segunda.mensagens.join(" "), /1 já existia\(m\).*CHS001 · MECÂNICA/);
  const nenhuma = gerar([], [CHS], { layout: "disciplina", disciplinas: [] });
  assert.equal(nenhuma.slides, null);
  assert.match(nenhuma.bloqueio!, /escolha ao menos uma disciplina/);
  // Projeto sem permissão continua bloqueado antes de tudo.
  assert.match(gerar([], [CHS], { layout: "disciplina", disciplinas: [MEC], podeDiaria: () => false }).bloqueio!, /Sem permissão/);
});

test("Limite de 50 vale por slide de disciplina: acima do limite nada é adicionado (nunca parcial)", () => {
  const base = Array.from({ length: 47 }, (_, i) => slideComProjetos("ldp", [{ ...CHS, projectId: `b.${String(i).padStart(8, "0")}-0000-0000-0000-000000000000` }]));
  const cabe = gerar(base, [CHS], { layout: "disciplina", disciplinas: [MEC, CIV], visaoGeralDisponivel: false });   // 47 + 2 = 49
  assert.equal(cabe.slides!.length, 49);
  const tres = [MEC, CIV, { chave: "N:ELETRICA", nome: "ELÉTRICA" }, { chave: "N:PROCESSO", nome: "PROCESSO" }];
  const estoura = gerar(base, [CHS], { layout: "disciplina", disciplinas: tres });                                       // 47 + 1 + 4 = 52
  assert.equal(estoura.slides, null);
  assert.equal(estoura.totalResultante, 52);
  assert.match(estoura.bloqueio!, new RegExp(`Limite de ${TV_LIMITES.maxTelas} telas por TV: seriam 52 \\(47 atuais \\+ 4 diária\\(s\\) \\+ 01 Visão Geral\\)`));
});

/* ------------------------------------------------------------------------------------------- disciplinas do editor */
let seq = 0;
const linha = (o: Partial<ProgramacaoLinha> & { documento: string | null; dataAtividade: string }): ProgramacaoLinha => ({ idRegistro: ++seq, tarefa: "ELABORAÇÃO",
  disciplinaWeekly: null, siglaDoc3: "ME", disciplinaNorma: "MECÂNICA", disciplinaCanonica: "MECHANICAL", fonteDisciplina: "DOC3", elaborador: "ANA", verificador: "BIA",
  aprovador: null, hhElaboracao: 4, hhVerificacao: 2, hhAprovacao: null, baseline: null, reprogramado: null, status: "EM ANDAMENTO", fase: null, ...o });
const resposta = (projectId: string, linhas: ProgramacaoLinha[]): ProgramacaoResposta => ({ projectId, inicio: "2026-10-19", fim: "2026-10-20", ultimaSincronizacao: null,
  registrosProjeto: linhas.length, registrosSemData: 0, datasComProgramacao: [], linhas });
const PRJ = CHS.projectId;
const norma: DisciplinasNorma = { projectId: PRJ, normaId: 7, normaNome: "NORMA CHS", diagnosticos: [], siglas: [
  { sigla: "ME", disciplina: "MECÂNICA", canonica: "MECHANICAL" }, { sigla: "CV", disciplina: "CIVIL", canonica: "CIVIL" },
  { sigla: "EL", disciplina: "ELÉTRICA", canonica: null }] };
const dados = () => [resposta(PRJ, [
  linha({ documento: "ME-1", dataAtividade: "2026-10-20" }),
  linha({ documento: "ME-2", dataAtividade: "2026-10-19" }),
  linha({ documento: "CV-1", siglaDoc3: "CV", disciplinaNorma: "CIVIL", disciplinaCanonica: "CIVIL", dataAtividade: "2026-10-19" }),
  linha({ documento: "ME-9", dataAtividade: "2026-10-12" }),                                   // fora de ontem/hoje: não descobre nada
])];

test("Disciplinas do editor: Norma + descobertas na programação de ontem/hoje, com a MESMA chave da tela; nada inventado", () => {
  const opcoes = opcoesDisciplinaDiaria({ norma, dados: dados()[0], projeto: { id: PRJ, name: CHS.projectName! }, hoje: "2026-10-20" });
  assert.deepEqual(opcoes.map((o) => [o.nome, o.naNorma, o.atividadesHoje, o.atividadesOntem]),
    [["CIVIL", true, 0, 1], ["MECÂNICA", true, 1, 1], ["ELÉTRICA", true, 0, 0]], "com programação primeiro; só-Norma depois");
  // A chave oferecida é exatamente a que a tela diária calcula (catálogo de 1 projeto com a mesma Norma).
  const cat = criarCatalogo([norma], [{ id: PRJ, name: CHS.projectName! }]);
  const a = analiseDiariaAutomatica({ dados: dados(), cat, hoje: "2026-10-20", ldp: new Map() });
  for (const d of a.disciplinas) assert.ok(opcoes.some((o) => o.chave === d.chave && o.nome === d.nome), d.nome);
  // Sem Norma e sem programação: nenhuma disciplina (nunca siglas inventadas).
  assert.deepEqual(opcoesDisciplinaDiaria({ norma: { ...norma, normaId: null, siglas: [] }, dados: null, projeto: { id: PRJ }, hoje: "2026-10-20" }), []);
  // Hook do editor: sem Norma não oferece nada (a chave poderia divergir da TV) e declara o motivo.
  const hook = fonte("src/features/tv/useDisciplinasDiariaTv.ts");
  assert.match(hook, /if \(norma\.status === "rejected"\)/);
  assert.match(hook, /\/api\/reports\/gestao-vista-jamile\/disciplinas\?/);
  assert.match(hook, /janelaDiariaAutomatica\(hoje\)/);
});

/* ------------------------------------------------------------------------------------------- tela: três exibições distintas */
const reports = [{ projectId: PRJ, documentos: [
  { codigo: "ME-1", titulo: "BOMBA P-101", emitido: false, cancelado: false, dataEmissao: null, isActivity: false },
  { codigo: "ME-2", titulo: "TANQUE T-01", emitido: false, cancelado: false, dataEmissao: null, isActivity: false },
  { codigo: "CV-1", titulo: "FUNDAÇÃO", emitido: true, cancelado: false, dataEmissao: "2026-10-19", isActivity: false }] }] as unknown as FactsReport[];
const render = (layout: "padrao" | "cards" | "disciplina", disciplina: string | null = null, disciplinaNome: string | null = null) => {
  const cat = criarCatalogo([norma], [{ id: PRJ, name: CHS.projectName! }]);
  return renderToStaticMarkup(createElement(MemoryRouter, null, createElement(DiariaAutomatica,
    { reports, dados: dados(), cat, hoje: "2026-10-20", carregando: false, layout, disciplina, disciplinaNome })));
};
const conta = (html: string, s: string) => html.split(s).length - 1;

test("Padrão: o MESMO layout da 02 (Programming/GerenciaEngenhariaItem3) em HOJE e PENDÊNCIAS DE ONTEM; pendente só com não emissão comprovada", () => {
  const html = render("padrao");
  assert.match(html, /data-layout="padrao"/);
  assert.equal(conta(html, 'class="ge3-layout"'), 2, "duas seções com o layout da 02");
  assert.equal(conta(html, "gv-kpis gv-three"), 2, "cartões da 02 em cada seção");
  assert.match(html, /HOJE · TER 20\/10\/2026/);
  assert.match(html, /PENDÊNCIAS DE ONTEM · SEG 19\/10\/2026/);
  assert.match(html, /PENDENTES DE ONTEM \(COMPROVADOS\)/);
  // Hoje: ME-1; ontem pendente: só ME-2 (CV-1 foi emitido em 19/10 → não é pendência).
  const [hoje, ontem] = html.split("PENDÊNCIAS DE ONTEM · ");
  assert.ok(hoje.includes("ME-1") && !hoje.includes("ME-2"));
  assert.ok(ontem.includes("ME-2") && !ontem.includes("CV-1"));
  assert.ok(!html.includes("gv-diaria-card"), "padrão não usa os cards por disciplina");
});

test("Cards por disciplina: cartões de indicadores + blocos por disciplina num slide; nenhuma disciplina vazia", () => {
  const html = render("cards");
  assert.match(html, /data-layout="cards"/);
  // Classe da raiz nunca colide com a grade de cartões (.gv-diaria-cards) — regressão visual de 20/10.
  assert.match(html, /class="gv-diaria-auto gv-diaria-modo-cards"/);
  assert.equal(conta(html, 'class="gv-diaria-cards"'), 1);
  assert.equal(conta(html, 'data-testid="gv-diaria-card"'), 2, "MECÂNICA e CIVIL (ELÉTRICA sem nada hoje/ontem não vira card)");
  assert.equal(conta(html, 'data-testid="gv-diaria-disciplina"'), 2);
  assert.ok(!html.includes("ELÉTRICA"));
  assert.ok(!html.includes('class="ge3-layout"'));
  assert.match(html, /PENDENTES DE ONTEM<\/dt><dd>1<\/dd>/);
});

test("Uma tela por disciplina: só a disciplina do slide; sem atividade = aviso explícito (nunca card vazio); fora da Norma avisa", () => {
  const html = render("disciplina", "C:MECHANICAL", "MECÂNICA");
  assert.match(html, /data-layout="disciplina"/);
  assert.equal(conta(html, 'data-testid="gv-diaria-disciplina"'), 1);
  assert.ok(html.includes("ME-1") && html.includes("ME-2") && !html.includes("CV-1"));
  assert.ok(!html.includes("gv-diaria-card"));
  const vazia = render("disciplina", "N:ELETRICA", "ELÉTRICA");
  assert.match(vazia, /Nenhuma atividade de ELÉTRICA programada para hoje/);
  assert.ok(!vazia.includes('data-testid="gv-diaria-disciplina"'));
  assert.ok(!vazia.includes("não está na Norma"), "ELÉTRICA está na Norma");
  assert.match(render("disciplina", "N:QUIMICA", "QUÍMICA"), /não está na Norma de Coordenação atual do projeto/);
});

/* ------------------------------------------------------------------------------------------- player / contratos */
const eff = (extra: Partial<EffectivePermissions>): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: [], anyProject: [], ...extra });
const renderSlide = (layoutDiario: string | null, disciplina: string | null = null, disciplinaNome: string | null = null) => {
  const efetivas = eff({ isMasterAdmin: true });
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  return renderToStaticMarkup(createElement(MemoryRouter, null, createElement(PermissionContext.Provider, { value: valor },
    createElement(ConteudoTv, { screenId: TV_PROGRAMACAO_DIARIA, selection: [{ id: CHS.projectId, hubId: "", name: CHS.projectName! }],
      dataReferencia: "AUTO", layoutDiario, disciplina, disciplinaNome }))));
};

test("Player: exibição chega à tela; por disciplina mostra projeto e disciplina no cabeçalho; exibição inválida = aviso", () => {
  assert.match(renderSlide("disciplina", "C:MECHANICAL", "MECÂNICA"), /data-testid="gv-escopo">CHS001 - ETHANOL PLANT · DISCIPLINA: MECÂNICA</);
  assert.match(renderSlide(null), /data-testid="gv-escopo">CHS001 - ETHANOL PLANT</);
  assert.match(renderSlide("mosaico"), /Programação Diária automática com exibição inválida/);
  assert.match(renderSlide("disciplina"), /não tem a disciplina/);
  const player = fonte("src/features/tv/TvPlayerPage.tsx");
  assert.match(player, /layoutDiario=\{def\?\.exigeDataReferencia \? slide\.layoutDiario \?\? null : null\}/);
  assert.match(player, /rotuloLayoutDiario\(slide\)/);
});

test("Contratos: backend guarda/valida a exibição; editor só oferece Cards/Disciplina com servidor atualizado; Portal inalterado", () => {
  const regras = fonte("../Services/Tv/TvConfiguration.cs");
  assert.match(regras, /JsonIgnoreCondition\.WhenWritingNull\)\] public string\? LayoutDiario \{ get; init; \}/);
  assert.match(regras, /JsonIgnoreCondition\.WhenWritingNull\)\] public string\? Disciplina \{ get; init; \}/);
  assert.match(regras, /automaticas\.Add\(t\.ScreenId \+ "\|" \+ t\.ProjectId \+ "\|" \+ layout \+ "\|" \+ disciplina\)/);
  const dia = fonte("../Services/Tv/TvDiaAutomatico.cs");
  assert.match(dia, /LayoutPadrao = "padrao", LayoutCards = "cards", LayoutDisciplina = "disciplina"/);
  assert.match(fonte("../Controllers/TvConfigurationController.cs"), /layoutsDiarios = true/);
  const editor = fonte("src/features/settings/TelasTvConfigPage.tsx");
  assert.match(editor, /const layoutEfetivo: LayoutDiario = layoutsDiarios \? layoutAuto : "padrao";/);
  assert.match(editor, /data-testid="tv-diarias-exibicao"/);
  assert.match(editor, /disabled=\{l\.id !== "padrao" && !layoutsDiarios\}/);
  assert.match(editor, /Gerar telas diárias automáticas/);
  // Troca de projeto na exibição por disciplina: só pré-marca com as opções DO projeto escolhido (sem corrida).
  assert.match(editor, /disciplinasProjeto\.projeto !== projetoDisciplina\.projectId\) return;/);
  assert.match(fonte("src/features/tv/useDisciplinasDiariaTv.ts"), /estado\.projeto === null \|\| estado\.projeto === id \? estado/);
  const pagina = fonte("src/features/production/analytics/GestaoVistaJamilePage.tsx");
  assert.match(pagina, /<Programming reports=\{reports\} semana=\{semana\} dia=\{a\.hoje\}/, "Padrão reutiliza o componente da 02");
  assert.match(pagina, /const progAuto=useProgramacao\(selection,auto\?\.inicio\?\?week\.start,auto\?\.fim\?\?week\.end,auto!==null\);/, "mesma janela/consulta");
  // Portal: a exibição não abre recurso nem período novo (mesma regra do slide AUTO).
  assert.ok(!/LayoutDiario/.test(fonte("../Services/Tv/Portal/TvPortalResources.cs")));
});
