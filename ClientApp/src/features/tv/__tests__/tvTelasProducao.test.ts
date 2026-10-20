// Telas do Controle de Produção na TV: catálogo completo derivado de productionRoutes.ts, contrato com o backend
// (Services/Tv/TvTelas.cs), seletor sem fallback silencioso, um projeto por item nas telas que não consolidam e o player
// renderizando a tela REAL de cada item (sem cair em outro dashboard).
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import { folhasProducao, PRODUCTION_NAV } from "../../production/productionRoutes";
import { ConteudoTv, PROJETO_GESTAO_VISTA_TIPO2, TELAS_TV, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO, TELAS_TV_SELECIONAVEIS, getTelaTv, idTelaTv, telasPorCategoria } from "../tvScreens";
import { slideComProjetos, validarApresentacoes, type TvPresentation } from "../tvLogic";
import { estadosDasTelas, itensParaAdicionar, ofereceModos } from "../../settings/TelasTvConfigPage";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};

const HUB = "b.11111111-1111-1111-1111-111111111111";
const P = (n: number, nome: string) => ({ hubId: HUB, projectId: `b.${String(n).repeat(8)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(12)}`, projectName: nome });
const CHS = P(2, "CHS001 - ETHANOL PLANT"), BML = P(3, "BML011.016 - COOLING TOWER");

const JAMILE = ["gestao-a-vista-visao-geral", "gestao-a-vista-programacao", "gestao-a-vista-documentacao", "gestao-a-vista-indicadores",
  "gestao-a-vista-hh-disciplina", "gestao-a-vista-custo-disciplina"];
/** As 16 telas do catálogo antigo — apresentações salvas referenciam esses ids. */
const ANTIGAS = ["ldp", "nivelamento-programacao", "controle-programacao", "desvio-qualidade", "cap-interno", "cap-externo", "change-order",
  "controle-documental", "hold", "desvio-escopo", "rnc", "programacao", "curva-s", "resumo", "analise-financeira", "gestao-a-vista"];
/** Gestão à Vista Tipo 2 (19/10/2026): as 10 páginas prioritárias do BI CHS001 — catálogo próprio, exclusivas da CHS001. */
const TIPO2 = ["gestao-vista-tipo2-resumo-30", "gestao-vista-tipo2-resumo-60", "gestao-vista-tipo2-resumo-90",
  "gestao-vista-tipo2-ldp-3d-30", "gestao-vista-tipo2-ldp-3d-60", "gestao-vista-tipo2-ldp-3d-90",
  "gestao-vista-tipo2-ldp-engineering-30", "gestao-vista-tipo2-ldp-engineering-60", "gestao-vista-tipo2-ldp-engineering-90",
  "gestao-vista-tipo2-programacao"];

test("Catálogo = TODAS as folhas do Controle de Produção (inclusive fora do menu) + 10 Gestão à Vista Tipo 2 + painel PMO antigo; ids únicos", () => {
  const folhas = folhasProducao();
  assert.equal(TELAS_TV.length, folhas.length + TIPO2.length + 1);
  assert.deepEqual(TELAS_TV.filter((t) => t.categoria === "Gestão à Vista Tipo 2").map((t) => t.id), TIPO2);
  for (const f of folhas) {
    const t = getTelaTv(idTelaTv(f));
    assert.ok(t, `folha sem tela de TV: ${f.slug}`);
    assert.deepEqual(t.chaves, f.permissionKeys, `keys de ${f.slug} = as da rota`);
    assert.equal(t.containsCostData, f.containsCostData, `custo de ${f.slug}`);
    assert.equal(t.slug, f.slug);
    assert.equal(t.tvSelectable, f.page.tipo !== "em-desenvolvimento", `selecionável ${f.slug}`);
  }
  assert.equal(new Set(TELAS_TV.map((t) => t.id)).size, TELAS_TV.length, "ids repetidos");
  for (const id of ANTIGAS) assert.ok(getTelaTv(id)?.tvSelectable, `id antigo preservado: ${id}`);
  // Folha nova no PRODUCTION_NAV entra sozinha (nada de lista paralela): o catálogo é calculado do registro.
  assert.ok(PRODUCTION_NAV.length > 0 && !/const EXTRAS/.test(fonte("src/features/tv/tvScreens.tsx")));
});

test("Telas pedidas: principais, produção, lançamentos, análise financeira e as SEIS da Gestão à Vista separadas", () => {
  const esperadas: Record<string, string> = {
    resumo: "Resumo", "resumo-tv-teste": "Resumo TV — Testes", "gestao-hh-custo": "Gestão à Vista — HH e Custo", "lista-documentos": "Lista de Documentos",
    "curva-s": "Curva S", "controle-hh-custo": "Controle de HH / Custo",
    ldp: "LDP", programacao: "Programação Semanal", "nivelamento-programacao": "Nivelamento da Programação", "controle-programacao": "Controle da Programação",
    "cap-externo": "CAP Externo", "cap-interno": "CAP Interno", "desvio-escopo": "Desvio de Escopo", "desvio-qualidade": "Desvio de Qualidade",
    "change-order": "Change Order", hold: "HOLD's", rnc: "RNC", "controle-documental": "Controle Documental",
    "lancamentos-pessoas": "Lançamentos — Pessoas", "lancamentos-disciplinas": "Lançamentos — Disciplinas", "lancamentos-documentos": "Lançamentos — Documentos",
    "analise-financeira": "Análise Financeira — Visão Geral", "analise-financeira-projeto": "Análise Financeira — Projeto", "analise-financeira-documento": "Análise Financeira — Documento",
    "gestao-a-vista-visao-geral": "Gestão à Vista — 01. VISÃO GERAL", "gestao-a-vista-programacao": "Gestão à Vista — 02. PROGRAMAÇÃO",
    "gestao-a-vista-documentacao": "Gestão à Vista — 03. DOCUMENTAÇÃO", "gestao-a-vista-indicadores": "Gestão à Vista — 04. INDICADORES",
    "gestao-a-vista-hh-disciplina": "Gestão à Vista — HH por Disciplina", "gestao-a-vista-custo-disciplina": "Gestão à Vista — Custo por Disciplina",
  };
  for (const [id, label] of Object.entries(esperadas)) {
    assert.equal(getTelaTv(id)?.label, label, id);
    assert.equal(getTelaTv(id)?.tvSelectable, true, id);
  }
  // Jamile: cada uma aponta para a SUA tela (screen/mode), na categoria Gestão à Vista.
  assert.deepEqual(JAMILE.map((id) => getTelaTv(id)?.page), [
    { tipo: "jamile", screen: "visao-geral" }, { tipo: "jamile", screen: "programacao" }, { tipo: "jamile", screen: "documentacao" },
    { tipo: "jamile", screen: "indicadores" }, { tipo: "jamile-disciplina", mode: "hh" }, { tipo: "jamile-disciplina", mode: "custo" }]);
  assert.ok(JAMILE.every((id) => getTelaTv(id)?.categoria === "Gestão à Vista"));
  // 31 do Controle de Produção + painel PMO (antes de 19/10/2026) + 10 Gestão à Vista Tipo 2.
  assert.equal(TELAS_TV_SELECIONAVEIS.length, 41);
  assert.equal(TELAS_TV_SELECIONAVEIS.filter((t) => !TIPO2.includes(t.id)).length, 31);
});

test("Em desenvolvimento: no catálogo como indisponível, nunca selecionável nem aceito na validação", () => {
  const emDev = TELAS_TV.filter((t) => !t.tvSelectable).map((t) => t.id).sort();
  assert.deepEqual(emDev, ["analise-financeira-pessoa", "boletim-medicao", "indicadores-producao"]);
  const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
  assert.ok(emDev.every((id) => !permitidas.includes(id)));
  const tv: TvPresentation[] = [{ id: "tv", name: "TV", secondsPerScreen: 30, slides: [slideComProjetos("boletim-medicao", [CHS])] }];
  assert.match(validarApresentacoes(tv, permitidas) ?? "", /não pode ser exibida/);
});

test("Contrato frontend × backend: TvTelas.cs tem exatamente os ids, multiprojeto e keys das telas selecionáveis", () => {
  const cs = fonte("../Services/Tv/TvTelas.cs");
  const backend = new Map<string, { multi: boolean; keys: string[] }>();
  for (const m of cs.matchAll(/\bT\("([a-z0-9-]+)",\s*(Multi|Unico)((?:,\s*"[^"]+")+)\)/g))
    backend.set(m[1], { multi: m[2] === "Multi", keys: Array.from(m[3].matchAll(/"([^"]+)"/g), (k) => k[1]) });
  assert.equal(backend.size, TELAS_TV_SELECIONAVEIS.length, "quantidade de telas no backend");
  for (const t of TELAS_TV_SELECIONAVEIS) {
    const b = backend.get(t.id);
    assert.ok(b, `tela só no frontend: ${t.id}`);
    assert.equal(b.multi, t.multiProjeto, `multiprojeto de ${t.id}`);
    assert.deepEqual(b.keys, [...t.chaves], `keys de ${t.id}`);
  }
  for (const id of backend.keys()) assert.ok(getTelaTv(id)?.tvSelectable, `tela só no backend: ${id}`);
  // Projeto exclusivo: exatamente as 10 telas Tipo 2 no backend (with { ProjetoExclusivo = ProjetoChs001 }) e no frontend.
  const chs = /public const string ProjetoChs001 = "([^"]+)";/.exec(cs)?.[1];
  assert.equal(chs, PROJETO_GESTAO_VISTA_TIPO2);
  const exclusivasBackend = Array.from(cs.matchAll(/\bT\("([a-z0-9-]+)",\s*Unico,\s*"reports\.gestao\.view"\) with \{ ProjetoExclusivo = ProjetoChs001 \}/g), (m) => m[1]);
  assert.deepEqual(exclusivasBackend, TIPO2);
  assert.deepEqual(Object.keys(TELAS_TV_PROJETO_EXCLUSIVO), TIPO2);
  assert.ok(Object.values(TELAS_TV_PROJETO_EXCLUSIVO).every((p) => p === PROJETO_GESTAO_VISTA_TIPO2));
  assert.equal((cs.match(/ProjetoExclusivo = /g) ?? []).length, TIPO2.length, "nenhuma outra tela exclusiva no backend");
});

test("Um projeto por item só nas telas que não consolidam (Lançamentos, Lista de Documentos, HH/Custo)", () => {
  for (const id of ["lancamentos-pessoas", "lista-documentos", "gestao-hh-custo", "controle-hh-custo"]) assert.ok(TELAS_TV_PROJETO_UNICO.includes(id), id);
  for (const id of [...JAMILE, "resumo", "curva-s", "ldp", "analise-financeira", "resumo-tv-teste", "gestao-a-vista"]) assert.ok(!TELAS_TV_PROJETO_UNICO.includes(id), id);
  const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
  const tv = (s: ReturnType<typeof slideComProjetos>): TvPresentation[] => [{ id: "tv", name: "TV", secondsPerScreen: 30, slides: [s] }];
  assert.match(validarApresentacoes(tv(slideComProjetos("lancamentos-pessoas", [CHS, BML])), permitidas, TELAS_TV_PROJETO_UNICO) ?? "", /um projeto por vez/);
  assert.equal(validarApresentacoes(tv(slideComProjetos("resumo", [CHS, BML])), permitidas, TELAS_TV_PROJETO_UNICO), null);
  const unica = getTelaTv("lancamentos-pessoas")!;
  assert.equal(itensParaAdicionar(unica, [CHS, BML], [], "consolidado").length, 2);   // nunca combina, mesmo pedindo consolidado
});

test("Gestão à Vista multiprojeto: 1 tela consolidada OU 1 tela por projeto; 1 projeto continua igual", () => {
  const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
  const tv = (s: ReturnType<typeof slideComProjetos>[]): TvPresentation[] => [{ id: "tv", name: "TV", secondsPerScreen: 30, slides: s }];
  const dezessete = Array.from({ length: 17 }, (_, i) => ({ hubId: "", projectId: `b.${String(i).padStart(8, "0")}-0000-0000-0000-000000000000`, projectName: `PRJ${String(i).padStart(3, "0")} - X` }));
  for (const id of JAMILE) {
    const tela = getTelaTv(id)!;
    assert.equal(tela.multiProjeto, true, id);
    assert.equal(ofereceModos(tela), true, id);
    const consolidado = itensParaAdicionar(tela, dezessete, [], "consolidado");
    assert.equal(consolidado.length, 1, id);
    assert.equal(consolidado[0].projetos?.length, 17, id);
    assert.equal(consolidado[0].projectId, dezessete[0].projectId);                   // principal = primeiro
    assert.equal(itensParaAdicionar(tela, dezessete, [], "por-projeto").length, 17, id);
    assert.equal(validarApresentacoes(tv(consolidado), permitidas, TELAS_TV_PROJETO_UNICO), null, id);
    const um = itensParaAdicionar(tela, [CHS], []);
    assert.deepEqual(um.map((s) => [s.projectId, s.projetos ?? null]), [[CHS.projectId, null]]);   // formato antigo (1 projeto)
    assert.equal(itensParaAdicionar(tela, dezessete, consolidado, "consolidado").length, 0);         // não duplica
  }
  assert.equal(ofereceModos(getTelaTv("resumo")!), false);                              // demais telas: sem a escolha
});

const eff = (keys: string[], extra: Partial<EffectivePermissions> = {}): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: keys, anyProject: keys, ...extra });

test("Seletor: erro/lista vazia do servidor NÃO libera telas; custo só com a key gerencial; Resumo TV só com a key da rota", () => {
  const ids = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
  const tudo = criarCan(eff([], { isMasterAdmin: true }), "any");
  // Servidor sem lista (falha/versão antiga): nada selecionável.
  assert.equal(estadosDasTelas(TELAS_TV, [], null, tudo).filter((o) => o.estado === "ok").length, 0);
  // Servidor antigo (16 telas): as novas aparecem como indisponíveis por backend desatualizado.
  const antigo = estadosDasTelas(TELAS_TV, ANTIGAS, null, tudo);
  assert.equal(antigo.filter((o) => o.estado === "ok").length, 16);
  assert.equal(antigo.find((o) => o.tela.id === "gestao-a-vista-visao-geral")?.estado, "servidor");
  // Sem permissão local (efetivas não carregadas) nada é liberado, mesmo com o servidor autorizando.
  assert.equal(estadosDasTelas(TELAS_TV, ids, ids, criarCan(null, "any")).filter((o) => o.estado === "ok").length, 0);
  // Perfil operacional: dashboards sim; telas com custo/HH gerencial e Resumo TV — Testes, não.
  const operacional = estadosDasTelas(TELAS_TV, ids, null, criarCan(eff(["production.operational.ldp.view", "reports.gestao.view"]), "any"));
  const estado = (id: string) => operacional.find((o) => o.tela.id === id)?.estado;
  assert.equal(estado("ldp"), "ok");
  assert.equal(estado("gestao-a-vista-visao-geral"), "ok");
  for (const id of ["gestao-a-vista-indicadores", "gestao-a-vista-custo-disciplina", "gestao-hh-custo", "controle-hh-custo", "analise-financeira", "resumo-tv-teste"])
    assert.equal(estado(id), "sem-permissao", id);
  assert.ok(TELAS_TV.filter((t) => t.containsCostData && t.id !== "resumo" && t.id !== "gestao-a-vista")
    .every((t) => !t.chaves.some((k) => k.startsWith("production.operational."))), "tela com custo liberada por key operacional");
  // Lista do servidor restringe mesmo quando a permissão local liberaria.
  assert.equal(estadosDasTelas(TELAS_TV, ids, ["ldp"], tudo).filter((o) => o.estado === "ok").length, 1);
  assert.equal(estadosDasTelas(TELAS_TV, ids, null, tudo).find((o) => o.tela.id === "boletim-medicao")?.estado, "em-desenvolvimento");
  // Agrupado por categoria, na ordem do Controle de Produção.
  assert.deepEqual(telasPorCategoria(TELAS_TV_SELECIONAVEIS).map((g) => g.categoria),
    ["Principais", "Produção", "Lançamentos", "Análise Financeira", "Gestão à Vista", "Gestão à Vista Tipo 2", "Outras"]);
});

/** Projeto do item no player: CHS001 REAL para as telas Tipo 2 (exclusivas), CHS de teste para as demais. */
const projetoPadrao = (screenId: string) => TIPO2.includes(screenId)
  ? { id: PROJETO_GESTAO_VISTA_TIPO2, hubId: "", name: "CHS001 - ETHANOL PLANT" } : { id: CHS.projectId, hubId: "", name: CHS.projectName };

const renderTv = (screenId: string, keys: string[] | "master", selection = [projetoPadrao(screenId)]) => {
  const efetivas = keys === "master" ? eff([], { isMasterAdmin: true }) : eff(keys);
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  // Resumo TV — Testes lê a viewport (window.innerWidth/innerHeight) já na renderização: viewport 1920×1080 provisória.
  const g = globalThis as { window?: unknown };
  const tinha = "window" in g;
  if (!tinha) g.window = { innerWidth: 1920, innerHeight: 1080, location: { search: "", pathname: "/gestao-a-vista", hash: "" } };
  try {
    return renderToStaticMarkup(createElement(MemoryRouter, null,
      createElement(PermissionContext.Provider, { value: valor },
        createElement(ConteudoTv, { screenId, selection }))));
  } finally {
    if (!tinha) delete g.window;
  }
};

test("Player: cada tela selecionável monta a SUA tela (sem fallback para outro dashboard)", () => {
  for (const t of TELAS_TV_SELECIONAVEIS) {
    const html = renderTv(t.id, "master");
    assert.ok(!/Tela não encontrada|indisponível|Não foi possível exibir|Sem permissão/.test(html), `${t.id}: ${html.slice(0, 200)}`);
  }
  // Jamile: o shell aprovado da tela pedida (data-testid gestao-a-vista-<screen>).
  for (const screen of ["visao-geral", "programacao", "documentacao", "indicadores"])
    assert.match(renderTv(`gestao-a-vista-${screen}`, "master"), new RegExp(`data-testid="gestao-a-vista-${screen}"`));
  // Gestão à Vista Tipo 2: página do BI carregada sob demanda (Suspense) — no servidor só o fallback, sem erro.
  for (const id of TIPO2) assert.match(renderTv(id, "master"), /Carregando página do BI/, id);
  assert.match(renderTv("nao-existe", "master"), /Tela não encontrada/);
  assert.match(renderTv("boletim-medicao", "master"), /indisponível/);
  // Permissões aplicadas (usuário comum): a tela só monta DEPOIS da resposta de permissões do projeto do item —
  // enquanto isso nenhum componente de dados é montado (nenhum endpoint chamado).
  const aguardando = renderTv("gestao-a-vista-custo-disciplina", ["reports.gestao.view"]);
  assert.match(aguardando, /Verificando permissões/);
  assert.ok(!/gv-shell/.test(aguardando));
  // Negado: mesmo texto e mesma regra da rota (can(tela.chaves) no projeto do item).
  // Permissão conferida em TODOS os projetos do item (não só no primeiro).
  assert.match(fonte("src/features/tv/tvScreens.tsx"), /usePermissaoProjetos\(projetos\.map\(\(p\) => p\.id\), tela\.chaves\)/);
  assert.match(fonte("src/features/tv/tvScreens.tsx"), /if \(perm\.negados\.length > 0\)\s*return <ReportState title=\{`Sem permissão/);
});

test("Player usa o resolvedor único e os projetos SALVOS; Controle de Produção usa o mesmo resolvedor", () => {
  const telas = fonte("src/features/tv/tvScreens.tsx");
  assert.match(telas, /<ConteudoPaginaProducao page=/);
  assert.ok(!/useProjetoAtual/.test(telas));
  assert.match(fonte("src/features/production/ControleProducaoPage.tsx"), /<ConteudoPaginaProducao page=\{folha\.page\} selection=\{selecao\}/);
  const config = fonte("src/features/settings/TelasTvConfigPage.tsx");
  assert.ok(!/telasPermitidas\.length === 0 \|\|/.test(config), "lista vazia não pode liberar o filtro");
  assert.match(config, /<optgroup/);
});
