// Gestão à Vista Tipo 2 na TV (19/10/2026): as 10 páginas prioritárias do BI CHS001 (antigo grupo "BI — CHS001 (Original)")
// como telas da Configuração de Telas para TV — catálogo próprio (fora de PRODUCTION_NAV), um projeto por item, EXCLUSIVAS
// da CHS001 (bloqueio ao adicionar, ao trocar projeto e ao salvar), mesma key de cada página e player com a página do BI
// carregada sob demanda, sem sair do player pelos links internos.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import { BI_CHS001_GRUPO, BI_CHS001_PAGINAS, PRIORIDADE_10, rotaBiChs001 } from "../../production/biChs001/biChs001Paginas";
import {
  ConteudoTv, NOME_PROJETO_GESTAO_VISTA_TIPO2, PROJETO_GESTAO_VISTA_TIPO2, TELAS_TV, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO,
  TELAS_TV_SELECIONAVEIS, TV_MAX_IDADE_DADOS_BI_MS, getTelaTv, idTelaTvTipo2, navegacaoBiNaTv,
} from "../tvScreens";
import { slideComProjetos, validarApresentacoes, type TvPresentation, type TvSlide } from "../tvLogic";
import type { NavegacaoTv } from "../tvNavegacao";
import {
  avisoProjetoExclusivo, estadosDasTelas, itensParaAdicionar, ofereceModos, projetosForaDaTela, telasEmLote,
} from "../../settings/TelasTvConfigPage";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};

const CHS001 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e";
const IDS = ["gestao-vista-tipo2-resumo-30", "gestao-vista-tipo2-resumo-60", "gestao-vista-tipo2-resumo-90",
  "gestao-vista-tipo2-ldp-3d-30", "gestao-vista-tipo2-ldp-3d-60", "gestao-vista-tipo2-ldp-3d-90",
  "gestao-vista-tipo2-ldp-engineering-30", "gestao-vista-tipo2-ldp-engineering-60", "gestao-vista-tipo2-ldp-engineering-90",
  "gestao-vista-tipo2-programacao"];
const PROJ_CHS = { hubId: "", projectId: CHS001, projectName: "CHS001 - ETHANOL PLANT" };
const PROJ_BML = { hubId: "", projectId: "b.33333333-3333-3333-3333-333333333333", projectName: "BML011.016 - COOLING TOWER" };
const tv = (...slides: TvSlide[]): TvPresentation[] => [{ id: "tv", name: "TV", secondsPerScreen: 30, slides }];
const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);

test("Exatamente 10 telas Tipo 2: ids estáveis, rótulo do grupo, categoria própria, um projeto, key da página, só CHS001", () => {
  assert.equal(BI_CHS001_GRUPO, "Gestão à Vista Tipo 2");
  assert.equal(PROJETO_GESTAO_VISTA_TIPO2, CHS001);
  const tipo2 = TELAS_TV.filter((t) => t.categoria === "Gestão à Vista Tipo 2");
  assert.deepEqual(tipo2.map((t) => t.id), IDS);
  for (const t of tipo2) {
    const slug = t.id.slice("gestao-vista-tipo2-".length);
    const pagina = BI_CHS001_PAGINAS.find((p) => p.slug === slug)!;
    assert.ok(pagina && !pagina.emDesenvolvimento && (PRIORIDADE_10 as readonly string[]).includes(slug), t.id);
    assert.equal(t.id, idTelaTvTipo2(slug));
    assert.equal(t.label, `Gestão à Vista Tipo 2 — ${pagina.nome}`);
    assert.equal(t.tvSelectable, true);
    assert.equal(t.multiProjeto, false);
    assert.equal(t.containsCostData, false);
    assert.deepEqual([...t.chaves], ["reports.gestao.view"]);
    assert.deepEqual([...t.chaves], [...pagina.permissionKeys]);                       // permissão idêntica à da página
    assert.deepEqual(t.page, { tipo: "gestao-vista-tipo2", slug });
    assert.equal(t.projetoExclusivo, CHS001);
    assert.ok(TELAS_TV_PROJETO_UNICO.includes(t.id));
    assert.equal(ofereceModos(t), false);
  }
  // As 13 páginas não prioritárias do BI NUNCA entram na TV (nem como indisponíveis).
  for (const p of BI_CHS001_PAGINAS.filter((x) => x.emDesenvolvimento)) {
    assert.equal(getTelaTv(idTelaTvTipo2(p.slug)), undefined, p.slug);
    assert.ok(!TELAS_TV.some((t) => t.slug === `bi-chs001/${p.slug}`), p.slug);
  }
  assert.equal(Object.keys(TELAS_TV_PROJETO_EXCLUSIVO).length, 10);
  // Slugs e rotas antigas do BI preservados.
  assert.equal(rotaBiChs001("resumo-30"), "/controle-producao/bi-chs001/resumo-30");
  assert.equal(rotaBiChs001("ldp-engineering-90"), "/controle-producao/bi-chs001/ldp-engineering-90");
});

test("Seleção: CHS001 adiciona 1 item por tela; qualquer outro projeto selecionado bloqueia com mensagem clara", () => {
  for (const id of IDS) {
    const tela = getTelaTv(id)!;
    assert.deepEqual(projetosForaDaTela(tela, [PROJ_CHS]), []);
    const itens = itensParaAdicionar(tela, [PROJ_CHS], []);
    assert.deepEqual(itens.map((s) => [s.screenId, s.projectId, s.projetos ?? null]), [[id, CHS001, null]]);
    assert.deepEqual(projetosForaDaTela(tela, [PROJ_CHS, PROJ_BML]), [PROJ_BML]);
    assert.deepEqual(projetosForaDaTela(tela, [PROJ_BML]), [PROJ_BML]);
    const msg = avisoProjetoExclusivo(tela, [PROJ_BML]);
    assert.match(msg, /é exclusiva do projeto CHS001 - ETHANOL PLANT/);
    assert.match(msg, /BML011\.016 - COOLING TOWER/);
  }
  // Telas comuns não têm restrição de projeto.
  assert.deepEqual(projetosForaDaTela(getTelaTv("ldp")!, [PROJ_BML]), []);
  // "Adicionar todas" nunca inclui as telas Tipo 2 (só individualmente, com a CHS001).
  const lote = telasEmLote(TELAS_TV_SELECIONAVEIS).map((t) => t.id);
  assert.ok(IDS.every((id) => !lote.includes(id)));
  assert.ok(lote.includes("ldp") && lote.includes("gestao-a-vista-visao-geral"));
});

test("Salvar: validação (espelho do backend) aceita só a CHS001 nas telas Tipo 2 e recusa outro projeto", () => {
  for (const id of IDS) {
    assert.equal(validarApresentacoes(tv(slideComProjetos(id, [PROJ_CHS])), permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO), null, id);
    assert.match(validarApresentacoes(tv(slideComProjetos(id, [PROJ_BML])), permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO) ?? "",
      /é exclusiva do projeto CHS001/, id);
    // Mesmo com CHS001 como principal, combinar com outro projeto é recusado.
    assert.ok(validarApresentacoes(tv(slideComProjetos(id, [PROJ_CHS, PROJ_BML])), permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO));
    // Id em maiúsculas não é a CHS001 (comparação exata, como no backend).
    const upper = { ...PROJ_CHS, projectId: CHS001.toUpperCase().replace(/^B\./, "b.") };
    assert.match(validarApresentacoes(tv(slideComProjetos(id, [upper])), permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO) ?? "", /exclusiva/);
  }
  // Telas comuns continuam aceitando qualquer projeto.
  assert.equal(validarApresentacoes(tv(slideComProjetos("ldp", [PROJ_BML])), permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO), null);
  // O editor usa a regra ao salvar (erroValidacao) e ao adicionar/trocar projeto.
  const config = fonte("src/features/settings/TelasTvConfigPage.tsx");
  assert.match(config, /validarApresentacoes\(rascunho, telasPermitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO\)/);
  assert.match(config, /const fora = projetosForaDaTela\(telaDef, projetosSelecionados\);\s*if \(fora\.length > 0\) \{ setAviso\(avisoProjetoExclusivo\(telaDef, fora\)\); return; \}/);
  assert.match(config, /disabled=\{projetosSelecionados\.length === 0 \|\| foraDaTela\.length > 0/);
});

const eff = (keys: string[], extra: Partial<EffectivePermissions> = {}): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: keys, anyProject: keys, ...extra });

test("Autorização: oferecidas só com reports.gestao.view, autorizadas pelo servidor e com backend atualizado", () => {
  const todos = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
  const estado = (ops: ReturnType<typeof estadosDasTelas>, id: string) => ops.find((o) => o.tela.id === id)?.estado;
  const gestao = estadosDasTelas(TELAS_TV, todos, null, criarCan(eff(["reports.gestao.view"]), "any"));
  for (const id of IDS) assert.equal(estado(gestao, id), "ok", id);
  const operacional = estadosDasTelas(TELAS_TV, todos, null, criarCan(eff(["production.operational.ldp.view"]), "any"));
  for (const id of IDS) assert.equal(estado(operacional, id), "sem-permissao", id);
  // Servidor restringe (telasAutorizadas sem as Tipo 2) → sem permissão, mesmo com a key local.
  const restrito = estadosDasTelas(TELAS_TV, todos, todos.filter((id) => !IDS.includes(id)), criarCan(eff(["reports.gestao.view"]), "any"));
  for (const id of IDS) assert.equal(estado(restrito, id), "sem-permissao", id);
  // Backend antigo (31 telas, sem as Tipo 2) → indisponível por backend desatualizado; nunca liberadas.
  const antigo = estadosDasTelas(TELAS_TV, todos.filter((id) => !IDS.includes(id)), null, criarCan(eff([], { isMasterAdmin: true }), "any"));
  for (const id of IDS) assert.equal(estado(antigo, id), "servidor", id);
  // Sem permissões carregadas: nada.
  assert.ok(estadosDasTelas(TELAS_TV, todos, todos, criarCan(null, "any")).every((o) => o.estado !== "ok"));
});

const renderTv = (screenId: string, keys: string[] | "master", projeto: { id: string; name: string }) => {
  const efetivas = keys === "master" ? eff([], { isMasterAdmin: true }) : eff(keys);
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  return renderToStaticMarkup(createElement(MemoryRouter, null,
    createElement(PermissionContext.Provider, { value: valor },
      createElement(ConteudoTv, { screenId, selection: [{ id: projeto.id, hubId: "", name: projeto.name }] }))));
};

test("Player: CHS001 monta a página do BI sob demanda; outro projeto é recusado; permissão conferida antes", () => {
  for (const id of IDS) {
    const html = renderTv(id, "master", { id: CHS001, name: "CHS001 - ETHANOL PLANT" });
    assert.match(html, /Carregando página do BI/, id);                                    // fallback do Suspense (React.lazy)
    assert.ok(!/projeto não permitido|Tela não encontrada|indisponível|Sem permissão/.test(html), id);
    const outro = renderTv(id, "master", { id: PROJ_BML.projectId, name: PROJ_BML.projectName });
    assert.match(outro, /projeto não permitido/, id);
    assert.match(outro, new RegExp(`exclusiva do projeto ${NOME_PROJETO_GESTAO_VISTA_TIPO2}`), id);
    assert.ok(!/Carregando página do BI/.test(outro), id);                                // nenhuma página/dado montado
  }
  // Usuário comum: espera a permissão do projeto do item antes de montar a página (nenhum endpoint chamado).
  assert.match(renderTv(IDS[0], ["reports.gestao.view"], { id: CHS001, name: "CHS001" }), /Verificando permissões/);
});

test("Player: BiChs001Page só por import dinâmico e conteúdo exportado do próprio módulo (sem URL/seletor)", () => {
  const telas = fonte("src/features/tv/tvScreens.tsx");
  assert.ok(!/^import [^;]*biChs001\/BiChs001Page/m.test(telas), "BiChs001Page não pode ser importado estaticamente");
  assert.match(telas, /lazy\(\(\) =>\s*import\("@\/features\/production\/biChs001\/BiChs001Page"\)\.then\(\(m\) => \(\{ default: m\.ConteudoPaginaBiChs001 \}\)\)\)/);
  assert.match(telas, /<Suspense fallback=\{<ReportState title="Carregando página do BI…">/);
  const bi = fonte("src/features/production/biChs001/BiChs001Page.tsx");
  const conteudo = bi.slice(bi.indexOf("export function ConteudoPaginaBiChs001"), bi.indexOf("export default function BiChs001Page"));
  assert.ok(conteudo.length > 0);
  assert.match(conteudo, /<PaginaBi /);
  assert.ok(!/useSearchParams|useProjetoAtual|definirProjeto|ProductionProjectSelector|navigate\(/.test(conteudo), "conteúdo da TV não usa URL/seletor");
  assert.equal(TV_MAX_IDADE_DADOS_BI_MS, 300_000);
});

test("Links internos do BI na TV: só saltam para slides Tipo 2 da apresentação; nunca saem do player", () => {
  const saltos: number[] = [];
  const nav: NavegacaoTv = {
    destino: (screenId) => (screenId === idTelaTvTipo2("resumo-60") ? { indice: 3, projectIds: [CHS001] } : null),
    irPara: (i) => saltos.push(i),
  };
  const n = navegacaoBiNaTv(nav);
  assert.equal(n.disponivel("resumo-60"), true);
  assert.equal(n.disponivel("resumo-90"), false);                                         // não incluída na apresentação
  assert.equal(n.disponivel("capa"), false);                                              // página fora da TV
  n.ir("resumo-90");
  n.ir("resumo-60");
  assert.deepEqual(saltos, [3]);
  const fora = navegacaoBiNaTv(null);                                                      // sem player: tudo desabilitado
  assert.equal(fora.disponivel("resumo-60"), false);
  fora.ir("resumo-60");
  assert.deepEqual(saltos, [3]);
  // PaginaBi: com navegação externa, o clique NÃO usa o router (nunca /controle-producao dentro da TV).
  const bi = fonte("src/features/production/biChs001/BiChs001Page.tsx");
  assert.match(bi, /if \(navegacao\) \{ if \(navegacao\.disponivel\(alvo\.slug\)\) navegacao\.ir\(alvo\.slug\); \}\s*else navigate\(/);
  assert.match(bi, /navegacao=\{navegacao \?\? SEM_NAVEGACAO\}/);
});
