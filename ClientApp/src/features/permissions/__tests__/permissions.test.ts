import { test } from "node:test";
import assert from "node:assert/strict";
import { criarCan, decisaoDaRota, ehEffectivePermissions, filtrarPorPermissao, permissaoDaRota, rotaDeEntradaDoProjeto, rotaPermitida, ROTAS_ENTRADA_PROJETO, type EffectivePermissions } from "../permissionLogic";
import { encontrarFolha, folhasProducao, navProducao, rotaLegada, ROTAS_PRODUCAO, semVarianteLegada, TODAS_KEYS_PRODUCAO } from "../../production/productionRoutes";
import { TEMPLATE_TYPES } from "../../settings/templateTypes";
import { alternar, contagem, diferenca, estadoDoNo, filtrarArvore, montarArvore, type CatalogResource } from "../admin/permissionTreeLogic";

const eff = (p: Partial<EffectivePermissions>): EffectivePermissions => ({
  enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "u@na.com", projectIdAcc: null, permissions: [], anyProject: [], ...p,
});

test("CASO C — sem resposta válida do backend: não revelar funcionalidades", () => {
  const can = criarCan(null);
  assert.equal(can("home.view"), false);
  assert.equal(can("permissions.manage"), false);
  assert.equal(rotaPermitida("/controle-producao", "", can), false);
  assert.equal(rotaPermitida("/rota-inventada", "", can), false);
  // O Vite sem proxy devolvia o index.html: isso NÃO é uma resposta de permissões.
  assert.equal(ehEffectivePermissions("<!doctype html>"), false);
  assert.equal(ehEffectivePermissions(null), false);
  assert.equal(ehEffectivePermissions({ message: "erro" }), false);
  assert.equal(ehEffectivePermissions(eff({})), true);
});

test("CASO A — Enforce=false, usuário sem NENHUMA key: tudo liberado (menu, rotas, abas, botões)", () => {
  const e = eff({ enforced: false, permissions: [], anyProject: [] });
  for (const escopo of ["any", "project"] as const) {
    const can = criarCan(e, escopo);
    assert.equal(can("robot.run"), true);
    assert.equal(can("production.rnc.view"), true);
    assert.equal(rotaPermitida("/configuracoes/permissoes", "", can), true);
    assert.equal(rotaPermitida("/plant3d", "?tab=rules", can), true);
  }
  assert.equal(decisaoDaRota(false, "/controle-producao", "", criarCan(e, "any")), "permitido");
});

test("CASO B (frontend) — Enforce=false com schema ausente: liberado", () => {
  const can = criarCan(eff({ enforced: false, schemaInstalled: false }), "any");
  assert.equal(rotaPermitida("/cde", "", can), true);
});

test("CASO D — Enforce=true, MasterAdmin configurado: acesso total mesmo sem keys", () => {
  const can = criarCan(eff({ enforced: true, isMasterAdmin: true, permissions: [], anyProject: [] }), "any");
  assert.equal(can("permissions.manage"), true);
  assert.equal(rotaPermitida("/configuracoes/permissoes", "", can), true);
  assert.equal(rotaPermitida("/rota-inventada", "", can), true);
});

test("CASO E — Enforce=true, usuário comum sem permissão: negado", () => {
  const can = criarCan(eff({ enforced: true, permissions: [], anyProject: [] }), "any");
  assert.equal(can("home.view"), false);
  assert.equal(decisaoDaRota(false, "/controle-producao", "", can), "negado");
  assert.equal(rotaPermitida("/rota-inventada", "", can), false);
});

test("CASO F — carregando NUNCA é negado", () => {
  const negaTudo = criarCan(eff({ enforced: true }), "any");
  assert.equal(decisaoDaRota(true, "/controle-producao", "", negaTudo), "carregando");
  assert.equal(decisaoDaRota(true, "/configuracoes/permissoes", "", negaTudo), "carregando");
});

test("enforced=false preserva o comportamento atual; MasterAdmin vê tudo", () => {
  assert.equal(criarCan(eff({ enforced: false }))("robot.run"), true);
  assert.equal(criarCan(eff({ isMasterAdmin: true }))("permissions.manage"), true);
});

test("can usa SOMENTE o conjunto resolvido pelo backend (sem recalcular regras)", () => {
  const e = eff({ permissions: ["production.view"], anyProject: ["production.view", "cde.view"] });
  assert.equal(criarCan(e, "project")("cde.view"), false);
  assert.equal(criarCan(e, "any")("cde.view"), true);
  assert.equal(criarCan(e)(["robot.view", "production.view"]), true); // qualquer uma
});

test("sidebar: folha sem permissão some; grupo some se nenhum filho sobrar", () => {
  type N = { label: string; permissionKey?: string; children?: N[] };
  const nav: N[] = [
    { label: "Início", permissionKey: "home.view" },
    { label: "Config", children: [{ label: "Robô", permissionKey: "robot.view" }, { label: "Perm", permissionKey: "permissions.manage" }] },
    { label: "Relatórios", children: [{ label: "Plant", permissionKey: "plant3d.results.view" }] },
    { label: "Sem key" },
  ];
  const r = filtrarPorPermissao(nav, criarCan(eff({ anyProject: ["robot.view"] }), "any"));
  assert.deepEqual(r.map((n) => n.label), ["Config"]);
  assert.deepEqual(r[0].children!.map((n) => n.label), ["Robô"]);
});

test("URL direta: rota sem permissão bloqueada; rota não mapeada bloqueada; aba do Plant 3D pela key da aba", () => {
  const can = criarCan(eff({ anyProject: ["production.managerial.ldp.view", "plant3d.results.view"] }), "any");
  assert.equal(rotaPermitida("/controle-producao", "", can), true);
  assert.equal(rotaPermitida("/configuracoes/permissoes", "", can), false);
  assert.equal(rotaPermitida("/rota-inventada", "", can), false); // enforced: rota não mapeada bloqueada
  assert.equal(permissaoDaRota("/rota-inventada"), undefined);
  assert.equal(rotaPermitida("/plant3d", "?tab=dashboard", can), true);
  assert.equal(rotaPermitida("/plant3d", "?tab=rules", can), false);
  assert.equal(rotaPermitida("/cde/b.1/b.2", "", can), false);
  assert.equal(rotaPermitida("/", "", can), true); // só redireciona
});

const cat: CatalogResource[] = [
  { key: "cde.view", parentKey: null, label: "CDE", type: "MODULE", scope: "PROJECT", route: "/cde", sortOrder: 1 },
  { key: "cde.docs.view", parentKey: "cde.view", label: "Docs", type: "TAB", scope: "PROJECT", route: null, sortOrder: 2 },
  { key: "cde.docs.download", parentKey: "cde.docs.view", label: "Baixar", type: "ACTION", scope: "PROJECT", route: null, sortOrder: 3 },
  { key: "cde.curva-s.view", parentKey: "cde.view", label: "Curva S", type: "TAB", scope: "PROJECT", route: null, sortOrder: 4 },
];

test("árvore: marcar o pai marca todos os descendentes; filho continua configurável", () => {
  const [raiz] = montarArvore(cat);
  let sel = alternar(new Set(), raiz, true);
  assert.deepEqual([...sel].sort(), ["cde.curva-s.view", "cde.docs.download", "cde.docs.view", "cde.view"]);
  assert.equal(estadoDoNo(sel, raiz), "todos");
  sel = alternar(sel, raiz.children[0].children[0], false); // desmarca só "Baixar"
  assert.equal(sel.has("cde.docs.download"), false);
  assert.equal(sel.has("cde.docs.view"), true);
  assert.equal(estadoDoNo(sel, raiz), "alguns");
  assert.equal(estadoDoNo(alternar(sel, raiz, false), raiz), "nenhum");
});

test("contador do grupo: marcadas / total inclui o próprio nó e todos os descendentes", () => {
  const [raiz] = montarArvore(cat);
  assert.deepEqual(contagem(new Set(), raiz), { marcadas: 0, total: 4 });
  assert.deepEqual(contagem(new Set(["cde.view", "cde.docs.download"]), raiz), { marcadas: 2, total: 4 });
  assert.deepEqual(contagem(new Set(["cde.docs.download"]), raiz.children[0]), { marcadas: 1, total: 2 });
});

test("busca filtra a árvore sem perder a seleção; diferença aponta o que falta salvar", () => {
  const sel = new Set(["cde.view", "cde.curva-s.view"]);
  const filtrada = filtrarArvore(montarArvore(cat), "baixar");
  assert.equal(filtrada.length, 1);
  assert.deepEqual(filtrada[0].children.map((c) => c.resource.key), ["cde.docs.view"]);
  assert.ok(sel.has("cde.curva-s.view")); // filtrar não mexe na seleção
  assert.deepEqual(diferenca(["cde.view", "cde.docs.view"], sel), { adicionadas: ["cde.curva-s.view"], removidas: ["cde.docs.view"] });
  assert.deepEqual(diferenca(["cde.view"], new Set(["cde.view"])), { adicionadas: [], removidas: [] });
});

test("Controle de Produção SEM Gerencial/Operacional: rota plana por tela, liberada pela key gerencial OU operacional", () => {
  const can = criarCan(eff({ anyProject: ["production.managerial.ldp.view", "production.operational.rnc.view"] }), "any");
  assert.equal(rotaPermitida("/controle-producao/ldp", "", can), true);
  assert.equal(rotaPermitida("/controle-producao/rnc", "", can), true);
  assert.equal(rotaPermitida("/controle-producao/hold", "", can), false);
  assert.deepEqual(permissaoDaRota("/controle-producao/analise-financeira/pessoa"), ["production.financial.person.view"]);
  assert.deepEqual(permissaoDaRota("/controle-producao/ldp"), ["production.managerial.ldp.view", "production.operational.ldp.view"]);
  // "*" genérico: tela inexistente cai no padrão (a página mostra "não encontrada"), sem liberar nada novo.
  assert.equal(rotaPermitida("/controle-producao/nao-existe", "", can), true);
  assert.equal(encontrarFolha("nao-existe"), undefined);
  assert.equal(new Set(ROTAS_PRODUCAO.map((r) => r.rota)).size, ROTAS_PRODUCAO.length); // rotas únicas
  assert.ok(ROTAS_PRODUCAO.every((r) => !/\/(gerencial|operacional)\//.test(r.rota)));
});

test("Rotas antigas com variante e ?tela= redirecionam para a rota plana", () => {
  assert.equal(semVarianteLegada("/controle-producao/gerencial/ldp"), "/controle-producao/ldp");
  assert.equal(semVarianteLegada("/controle-producao/operacional/analise-financeira/pessoa"), "/controle-producao/analise-financeira/pessoa");
  assert.equal(semVarianteLegada("/controle-producao/gerencial"), "/controle-producao");
  assert.equal(semVarianteLegada("/controle-producao/ldp"), undefined);
  assert.equal(rotaLegada("hold"), "/controle-producao/hold");
  assert.equal(rotaLegada("inexistente"), undefined);
});

test("Telas com valor monetário absoluto só abrem com key gerencial/financeira (nunca com key operacional)", () => {
  for (const f of folhasProducao().filter((l) => l.containsCostData)) {
    assert.ok(f.permissionKeys.every((k) => !k.startsWith("production.operational.")), f.slug);
  }
  const op = criarCan(eff({ anyProject: TODAS_KEYS_PRODUCAO.filter((k) => k.startsWith("production.operational.")) }), "any");
  assert.equal(rotaPermitida("/controle-producao/controle-hh-custo", "", op), false);
  assert.equal(rotaPermitida("/controle-producao/analise-financeira/projeto", "", op), false);
  assert.equal(folhasProducao().filter((f) => f.page.tipo === "dashboard").length, 12);
});

test("Lista de Documentos é tela do Controle de Produção (key da Lista de Documentos)", () => {
  const folha = encontrarFolha("lista-documentos");
  assert.equal(folha?.page.tipo, "lista-documentos");
  assert.deepEqual(folha?.permissionKeys, ["cde.document-list.view"]);
  assert.equal(navProducao().find((x) => x.kind === "leaf" && x.slug === "lista-documentos")?.label, "Lista de Documentos");
  const can = criarCan(eff({ anyProject: ["cde.document-list.view"] }), "any");
  assert.equal(rotaPermitida("/controle-producao/lista-documentos", "", can), true);
  assert.equal(rotaPermitida("/cde", "", can), false); // CDE → Documentos saiu do menu e segue com a key própria
});

test("Templates: um tipo por rota, cada um com sua key", () => {
  assert.equal(TEMPLATE_TYPES.length, 9);
  const can = criarCan(eff({ anyProject: ["settings.templates.rnc.edit"] }), "any");
  assert.equal(rotaPermitida("/configuracoes/templates/rnc", "", can), true);
  assert.equal(rotaPermitida("/configuracoes/templates/controle-de-custos", "", can), false);
});

test("Indicadores de Produção é UMA folha; os 3 dashboards antigos saem do menu mas continuam roteáveis", () => {
  const nav = navProducao();
  const ind = nav.filter((i) => i.label === "Indicadores de Produção");
  assert.equal(ind.length, 1);
  assert.equal(ind[0].kind, "leaf");
  const noMenu = nav.flatMap((i) => (i.kind === "leaf" ? [i.slug] : i.children.map((c) => c.slug)));
  for (const slug of ["nivelamento-programacao", "controle-programacao", "controle-documental"]) {
    assert.ok(!noMenu.includes(slug), `${slug} não deveria estar no sidebar`);
    assert.ok(encontrarFolha(slug), `${slug} deveria continuar roteável`);
  }
  const can = criarCan(eff({ anyProject: ["production.operational.indicadores.view", "production.managerial.nivelamento.view"] }), "any");
  assert.equal(rotaPermitida("/controle-producao/indicadores-producao", "", can), true);
  assert.equal(rotaPermitida("/controle-producao/nivelamento-programacao", "", can), true);
});

test("Ordem do Controle de Produção (lista única)", () => {
  assert.deepEqual(navProducao().map((i) => i.label), ["Resumo", "Gestão à Vista — HH e Custo", "Lista de Documentos", "Curva S", "LDP", "Programação Semanal", "CAP Externo", "CAP Interno",
    "Desvio de Escopo", "Change Order", "HOLD's", "Boletim de Medição", "Qualidade", "Controle de HH / Custo", "Lançamentos",
    "Análise Financeira", "GESTÃO À VISTA", "Indicadores de Produção", "Resumo TV — Testes"]);
});

test("Painel do Planejador = só planejador; Configuração do Projeto (e /painel antigo) = settings.projects.view", () => {
  const planejador = criarCan(eff({ anyProject: ["planning.planner.view"] }), "any");
  const projetos = criarCan(eff({ anyProject: ["settings.projects.view"] }), "any");
  assert.equal(rotaPermitida("/planejamento/painel-do-planejador", "", planejador), true);
  assert.equal(rotaPermitida("/planejamento/painel-do-planejador", "", projetos), false);
  assert.equal(rotaPermitida("/planejamento/configuracao-do-projeto", "", projetos), true);
  assert.equal(rotaPermitida("/planejamento/configuracao-do-projeto", "", planejador), false);
  assert.equal(rotaPermitida("/painel", "", projetos), true);
});

test("Resumo e Análise Financeira: telas com custo, nunca liberadas por key operacional", () => {
  const resumo = encontrarFolha("resumo");
  assert.equal(resumo?.page.tipo, "resumo");
  assert.equal(resumo?.analitica, true);
  assert.equal(resumo?.containsCostData, true);
  const op = criarCan(eff({ anyProject: TODAS_KEYS_PRODUCAO.filter((k) => k.startsWith("production.operational.")) }), "any");
  assert.equal(rotaPermitida("/controle-producao/resumo", "", op), false);
  assert.equal(rotaPermitida("/controle-producao/resumo", "", criarCan(eff({ anyProject: ["reports.gestao.view"] }), "any")), true);
  assert.equal(encontrarFolha("analise-financeira/visao-geral")?.page.tipo, "analise-financeira");
  assert.equal(encontrarFolha("analise-financeira/documento")?.page.tipo, "analise-financeira");
  assert.equal(encontrarFolha("analise-financeira/pessoa")?.page.tipo, "em-desenvolvimento"); // sem fonte de pessoa
  assert.equal(rotaPermitida("/controle-producao/analise-financeira/visao-geral", "", criarCan(eff({ anyProject: ["production.financial.project.view"] }), "any")), true);
});

test("Isolamento: permissões em outro projeto não abrem telas no projeto selecionado", () => {
  const limitado = eff({
    projectIdAcc: "b.c3e20d9d-779e-423c-9381-abec38cf11cb",
    permissions: ["home.view"],
    anyProject: ["home.view", "production.view", "production.managerial.ldp.view", "quality.audit.view"],
  });
  const canAqui = criarCan(limitado, "project");
  const canQualquer = criarCan(limitado, "any");
  assert.equal(rotaDeEntradaDoProjeto(canAqui), undefined);
  assert.equal(rotaPermitida("/controle-producao/resumo", "", canAqui), false);
  assert.equal(rotaPermitida("/qualidade/auditoria", "", canAqui), false);
  assert.equal(rotaPermitida("/dashboard", "", canAqui), true);
  assert.equal(rotaDeEntradaDoProjeto(canQualquer), "/controle-producao");
});

test("Home → ambiente do projeto: entra na primeira área permitida (nunca volta para a Home)", () => {
  assert.equal(rotaDeEntradaDoProjeto(criarCan(eff({ anyProject: ["home.view", "production.operational.ldp.view"] }), "any")), "/controle-producao");
  assert.equal(rotaDeEntradaDoProjeto(criarCan(eff({ anyProject: ["home.view", "planning.coordination.view"] }), "any")), "/planejamento/normas-de-coordenacao");
  assert.ok(!ROTAS_ENTRADA_PROJETO.includes("/dashboard"));
});

test("TV: player liberado para quem assiste (tv.view) ou configura; configuração só para quem configura", () => {
  const assiste = criarCan(eff({ anyProject: ["tv.view"] }), "any");
  const configura = criarCan(eff({ anyProject: ["settings.tv.configure"] }), "any");
  assert.equal(rotaPermitida("/gestao-a-vista", "", assiste), true);
  assert.equal(rotaPermitida("/gestao-a-vista", "", configura), true);
  assert.equal(rotaPermitida("/configuracoes/telas-tv", "", assiste), false);
  assert.equal(rotaPermitida("/configuracoes/telas-tv", "", configura), true);
  assert.equal(rotaPermitida("/extracoes-engenharia", "", criarCan(eff({ anyProject: ["reports.view"] }), "any")), true);
});

test("Qualidade: HH, documentos fora da LDP, comparativo e divergências independentes", () => {
  const onlyHH = criarCan(eff({ anyProject: ["quality.unassigned.view"] }), "any");
  assert.equal(rotaPermitida("/qualidade/hh-sem-documento", "", onlyHH), true);
  assert.equal(rotaPermitida("/qualidade/comparativo-hh-custos", "", onlyHH), false);
  assert.equal(rotaPermitida("/qualidade/auditoria-divergencias", "", onlyHH), false);
  const onlyOutside = criarCan(eff({ anyProject: ["quality.outside-ldp.view"] }), "any");
  assert.equal(rotaPermitida("/qualidade/hh-sem-documento", "", onlyOutside), true);
  const onlyCompare = criarCan(eff({ anyProject: ["quality.comparison.view"] }), "any");
  assert.equal(rotaPermitida("/qualidade/auditoria-divergencias", "", onlyCompare), false);
});

test("Custos: autorizar impostos não abre profissionais, softwares ou pacotes", () => {
  const taxes = criarCan(eff({ anyProject: ["planning.costs.taxes.view"] }), "any");
  assert.equal(rotaPermitida("/planejamento/custos/impostos", "", taxes), true);
  assert.equal(rotaPermitida("/planejamento/custos/profissionais", "", taxes), false);
  assert.equal(rotaPermitida("/planejamento/custos/softwares", "", taxes), false);
  assert.equal(rotaPermitida("/planejamento/custos/pacotes", "", taxes), false);
});

test("Envio governado pela LDP e Identidade Visual: rotas protegidas pelas keys existentes", () => {
  const upload = criarCan(eff({ anyProject: ["cde.docs.upload"] }), "any");
  const config = criarCan(eff({ anyProject: ["settings.projects.view"] }), "any");
  assert.equal(rotaPermitida("/cde/envio-documentos", "", upload), true);
  assert.equal(rotaPermitida("/cde/envio-documentos", "", config), false);
  assert.equal(rotaPermitida("/planejamento/pastas-documentos", "", config), false);
  assert.equal(rotaPermitida("/planejamento/pastas-documentos", "", upload), false);
  assert.equal(rotaPermitida("/planejamento/pastas-documentos", "", criarCan(eff({ anyProject: ["quality.folders.view"] }), "any")), true);
  assert.equal(rotaPermitida("/configuracoes/identidade-visual", "", config), true);
  assert.equal(rotaPermitida("/configuracoes/identidade-visual", "", upload), false);
});

test("CDE → Documentos (Project Files do projeto atual) exige cde.docs.view", () => {
  const docs = criarCan(eff({ anyProject: ["cde.docs.view"] }), "any");
  const semDocs = criarCan(eff({ anyProject: ["planning.coordination.view"] }), "any");
  assert.equal(rotaPermitida("/cde/documentos", "", docs), true);
  assert.equal(rotaPermitida("/cde/documentos", "", semDocs), false);
  assert.equal(rotaPermitida("/projects/b.h/b.p/urn:pasta", "", docs), true);
  // Sem cde.docs.view a Home continua usando a primeira área permitida.
  assert.equal(rotaDeEntradaDoProjeto(semDocs), "/planejamento/normas-de-coordenacao");
});

test("Resumo TV no sidebar: rota administrativa, acesso direto e ausência de permissão operacional", () => {
  const folha = encontrarFolha("resumo-tv-teste");
  assert.ok(folha && !folha.foraDoMenu);
  assert.deepEqual(folha.permissionKeys, ["permissions.manage"]);
  assert.ok(navProducao().some(x => x.kind === "leaf" && x.slug === "resumo-tv-teste"));
  const admin = criarCan(eff({anyProject:["permissions.manage"]}),"any");
  const consulta = criarCan(eff({anyProject:["reports.gestao.view"]}),"any");
  assert.equal(rotaPermitida("/controle-producao/resumo-tv-teste","",admin),true);
  assert.equal(rotaPermitida("/controle-producao/resumo-tv-teste","",consulta),false);
});
