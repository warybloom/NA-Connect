// Menu lateral da Gestão à Vista (Jamile) dentro do player de TV: só navega entre slides DA apresentação, nunca para
// /controle-producao; telas fora da apresentação (ou sem permissão) ficam visíveis e sem função. Fora do player, rotas normais.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import GestaoVistaJamilePage from "../../production/analytics/GestaoVistaJamilePage";
import { NavegacaoTvProvider, destinoNaApresentacao, type NavegacaoTv } from "../tvNavegacao";
import { getTelaTv, TELAS_TV_SELECIONAVEIS } from "../tvScreens";
import { telasEmLote } from "../../settings/TelasTvConfigPage";

const CHS = "b.22222222-2222-2222-2222-222222222222", BML = "b.33333333-3333-3333-3333-333333333333";
const slide = (screenId: string, ...projectIds: string[]) => ({ screenId, projectIds: projectIds.length ? projectIds : [CHS] });

test("Destino: só telas incluídas; prefere o slide com os MESMOS projetos do slide atual; ordem preservada", () => {
  const slides = [slide("gestao-a-vista-visao-geral"), slide("ldp"), slide("gestao-a-vista-programacao", BML), slide("gestao-a-vista-programacao"), slide("gestao-a-vista-visao-geral", BML)];
  assert.deepEqual(destinoNaApresentacao(slides, 0, "gestao-a-vista-programacao"), { indice: 3, projectIds: [CHS] });   // mesmo projeto (CHS)
  assert.deepEqual(destinoNaApresentacao(slides, 4, "gestao-a-vista-programacao"), { indice: 2, projectIds: [BML] });   // mesmo projeto (BML)
  assert.deepEqual(destinoNaApresentacao(slides, 1, "gestao-a-vista-visao-geral"), { indice: 0, projectIds: [CHS] });
  // Consolidado: o 01 consolidado (CHS+BML) leva ao 02 consolidado com os MESMOS projetos, não ao 02 só do CHS.
  const cons = [slide("gestao-a-vista-programacao"), slide("gestao-a-vista-visao-geral", CHS, BML), slide("gestao-a-vista-programacao", CHS, BML)];
  assert.deepEqual(destinoNaApresentacao(cons, 1, "gestao-a-vista-programacao"), { indice: 2, projectIds: [CHS, BML] });
  assert.equal(destinoNaApresentacao(slides, 0, "gestao-a-vista-indicadores"), null);                                 // não incluída
  assert.equal(destinoNaApresentacao([], 0, "gestao-a-vista-visao-geral"), null);
});

const eff = (extra: Partial<EffectivePermissions>): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: [], anyProject: [], ...extra });

function render(screen: "visao-geral" | "indicadores", slides: { screenId: string; projectIds: string[] }[] | null, efetivas: EffectivePermissions, saltos: number[] = []) {
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  const nav: NavegacaoTv | null = slides && { destino: (id) => destinoNaApresentacao(slides, 0, id), irPara: (i) => { saltos.push(i); } };
  const pagina = createElement(GestaoVistaJamilePage, { selection: [{ id: CHS, hubId: "", name: "CHS001 - ETHANOL PLANT" }], screen });
  return renderToStaticMarkup(createElement(MemoryRouter, null,
    createElement(PermissionContext.Provider, { value: valor }, nav ? createElement(NavegacaoTvProvider, { valor: nav, children: pagina }) : pagina)));
}

const estados = (html: string) => Object.fromEntries(Array.from(html.matchAll(/data-tv-nav="([^"]+)" data-tv-nav-estado="([^"]+)"/g), (m) => [m[1], m[2]]));

test("Player, Indicadores NÃO incluído: Visão Geral/Programação habilitados; Documentação/Indicadores estáticos; nenhum link de rota", () => {
  const html = render("visao-geral", [slide("gestao-a-vista-visao-geral"), slide("gestao-a-vista-programacao")], eff({ isMasterAdmin: true }));
  assert.deepEqual(estados(html), {
    "gestao-a-vista-visao-geral": "habilitado", "gestao-a-vista-programacao": "habilitado",
    "gestao-a-vista-documentacao": "indisponivel", "gestao-a-vista-indicadores": "indisponivel",
  });
  assert.ok(!/href="\/controle-producao/.test(html), "o player nunca navega para /controle-producao");
  assert.match(html, /<span class="gv-nav-indisponivel" aria-disabled="true" title="Tela não incluída nesta apresentação"[^>]*data-tv-nav="gestao-a-vista-indicadores"/);
  assert.match(html, /<button type="button" class="gv-active" aria-current="page" data-tv-nav="gestao-a-vista-visao-geral"/);
});

test("Player, Indicadores incluído: habilitado com permissão gerencial; sem resposta de permissão do projeto, fica estático", () => {
  const slides = [slide("gestao-a-vista-visao-geral"), slide("gestao-a-vista-indicadores")];
  assert.equal(estados(render("visao-geral", slides, eff({ isMasterAdmin: true })))["gestao-a-vista-indicadores"], "habilitado");
  // Usuário comum (permissões aplicadas): até a permissão do projeto do slide ser confirmada, nada é habilitado.
  const comum = estados(render("visao-geral", slides, eff({ permissions: ["reports.gestao.view"], anyProject: ["reports.gestao.view"] })));
  assert.equal(comum["gestao-a-vista-indicadores"], "indisponivel");
  // Regra: habilita só com a tela na apresentação E a key da tela no projeto do slide; Indicadores = key gerencial.
  const fonte = readFileSync("src/features/tv/tvNavegacao.tsx", "utf8");
  assert.match(fonte, /const habilitado = !!destino && !perm\.loading && perm\.negados\.length === 0;/);
  assert.match(fonte, /usePermissaoProjetos\(destino\?\.projectIds \?\? \[\], chaves\)/);   // TODOS os projetos do slide
  assert.deepEqual(getTelaTv("gestao-a-vista-indicadores")?.chaves, ["production.managerial.hh-custo.view"]);
  const jamile = readFileSync("src/features/production/analytics/GestaoVistaJamilePage.tsx", "utf8");
  assert.match(jamile, /chaves=\{encontrarFolha\("gestao-a-vista\/"\+x\.slug\)\?\.permissionKeys\?\?\[\]\}/);
});

test("Fora do player: menu continua com as rotas normais do Controle de Produção", () => {
  const html = render("visao-geral", null, eff({ isMasterAdmin: true }));
  for (const s of ["visao-geral", "programacao", "documentacao", "indicadores"]) assert.match(html, new RegExp(`href="/controle-producao/gestao-a-vista/${s}"`));
  assert.ok(!/data-tv-nav=/.test(html));
});

test("Player: salto zera o tempo do slide e respeita os limites; provider envolve o conteúdo do slide", () => {
  const player = readFileSync("src/features/tv/TvPlayerPage.tsx", "utf8");
  assert.match(player, /irPara: \(i\) => \{ if \(i >= 0 && i < total\) \{ setIndice\(i\); setDecorrido\(0\); \} \}/);
  assert.match(player, /destinoNaApresentacao\(\(apresentacao\?\.slides \?\? \[\]\)\.map\(\(s\) => \(\{ screenId: s\.screenId, projectIds: projetosDoSlide\(s\)\.map/);
  assert.ok(player.indexOf("<NavegacaoTvProvider") < player.indexOf("<ConteudoTv") && player.indexOf("</NavegacaoTvProvider>") > player.indexOf("<ConteudoTv"));
});

test("Adicionar todas: telas financeiras (04. INDICADORES, custos) nunca entram em lote", () => {
  const lote = telasEmLote(TELAS_TV_SELECIONAVEIS).map((t) => t.id);
  for (const id of ["gestao-a-vista-indicadores", "gestao-a-vista-custo-disciplina", "controle-hh-custo", "analise-financeira"]) assert.ok(!lote.includes(id), id);
  for (const id of ["gestao-a-vista-visao-geral", "gestao-a-vista-programacao", "gestao-a-vista-documentacao", "gestao-a-vista-hh-disciplina", "ldp"]) assert.ok(lote.includes(id), id);
});
