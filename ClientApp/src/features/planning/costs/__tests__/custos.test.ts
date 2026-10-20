import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { PERMISSAO_CUSTOS, PERMISSOES_CUSTOS_POR_CAMINHO, podeExibirCustosDuranteHomologacao, REDIRECIONAMENTOS_CUSTOS, ROTAS_CUSTOS, TELAS_CUSTOS } from "../custosRoutes";
import { __definirFetch, custosApi, juntarSoftware, lerDocumentosColados, lerExtrasDocumento, lerExtrasImportacao, lerNumero, mesclarExtras, montarDocumento, semPesoConfigurado, separarSoftware } from "../costsApi";
import { criarCan, rotaPermitida } from "../../../permissions/permissionLogic";
import { contextualNavigation } from "../../../sidebar/officialNavigation";

/** Código sem comentários (as regras citam "Autodesk"/"hubId" nos comentários de propósito). */
const codigo = (t: string) => t.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};
const eff = (anyProject: string[]) => ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "x@na.com", projectIdAcc: null, permissions: anyProject, anyProject });

test("Custos do Projeto é GRUPO com Impostos, Profissionais, Softwares e Pacotes (rotas estáveis)", () => {
  assert.deepEqual(TELAS_CUSTOS.map((t) => [t.label, t.to]), [
    ["Impostos", "/planejamento/custos/impostos"],
    ["Profissionais", "/planejamento/custos/profissionais"],
    ["Softwares", "/planejamento/custos/softwares"],
    ["Pacotes", "/planejamento/custos/pacotes"],
  ]);
  const layout = fonte("src/features/sidebar/officialNavigation.ts");
  assert.match(layout, /label: "Custos do Projeto",[\s\S]{0,80}children: TELAS_CUSTOS\.map/);
  assert.ok(!layout.includes('to: "/planejamento/custos-do-projeto"'), "a página única antiga saiu do sidebar");
});

test("Rota antiga /planejamento/custos-do-projeto redireciona para Profissionais", () => {
  assert.equal(REDIRECIONAMENTOS_CUSTOS["/planejamento/custos-do-projeto"], ROTAS_CUSTOS.profissionais);
  const app = fonte("src/App.tsx");
  assert.match(app, /REDIRECIONAMENTOS_CUSTOS\)\.map\(\(\[de, para\]\) => <Route key=\{de\} path=\{de\} element=\{<Navigate to=\{para\} replace \/>\}/);
  assert.ok(!app.includes("ProjectCostsPage"));
});

test("Permissão: cada tela de Custos tem chave independente; a chave genérica não concede tudo", () => {
  assert.equal(PERMISSAO_CUSTOS, "planning.costs.analysis");
  const somentePlanner = criarCan(eff(["planning.planner.view"]), "any");
  const sem = criarCan(eff(["home.view"]), "any");
  const antigoGenerico = criarCan(eff(["planning.costs.analysis"]), "any");
  for (const rota of Object.values(ROTAS_CUSTOS)) {
    const chave = PERMISSOES_CUSTOS_POR_CAMINHO[rota];
    const autorizado = criarCan(eff([chave]), "any");
    assert.equal(rotaPermitida(rota, "", autorizado), true, rota);
    assert.equal(rotaPermitida(rota, "", sem), false, rota);
    assert.equal(rotaPermitida(rota, "", somentePlanner), false, rota);
    assert.equal(rotaPermitida(rota, "", antigoGenerico), false, rota);
  }
});

test("Homologação: Custos do Projeto só aparece para MasterAdmin", () => {
  assert.equal(podeExibirCustosDuranteHomologacao(true), true);
  assert.equal(podeExibirCustosDuranteHomologacao(false), false);
  assert.equal(podeExibirCustosDuranteHomologacao(undefined), false);
  const hasCosts = (master: boolean) => contextualNavigation(false, false, master)
    .find(n => n.label === "Painel de Planejamento")?.children?.some(n => n.label === "Custos do Projeto") ?? false;
  assert.equal(hasCosts(false), false);
  assert.equal(hasCosts(true), true);
});

test("API de custos: Project_ID_ACC na query, sem Autodesk/hubId; edição e listas novas", async () => {
  const chamadas: { url: string; method: string; body?: string }[] = [];
  __definirFetch(async (url, init) => { chamadas.push({ url, method: init?.method ?? "GET", body: init?.body as string }); return new Response("[]", { status: 200 }); });
  const P = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";
  await custosApi.atualizarProfissional(P, 7, { nome: "Ana", empresa: null, aliasTimesheet: "ana", hhBaseMensal: 160, ativo: true });
  await custosApi.atualizarItem(P, 7, 3, { descricao: "INSS", valor: 1200, ativo: true });
  await custosApi.aditivos(P, 9);
  await custosApi.rateio(P, 9);
  await custosApi.criarItem(P, 7, { categoria: "IMPOSTO", descricao: "FGTS", valor: 400 });
  assert.deepEqual(chamadas.map((c) => `${c.method} ${c.url.split("?")[0]}`), [
    "PUT /api/project-costs/professionals/7", "PUT /api/project-costs/professionals/7/items/3",
    "GET /api/project-costs/packages/9/addenda", "GET /api/project-costs/packages/9/allocation",
    "POST /api/project-costs/professionals/7/items",
  ]);
  assert.ok(chamadas.every((c) => c.url.includes(`projectId=${encodeURIComponent(P)}`) && !c.url.includes("hubId")));
  assert.deepEqual(JSON.parse(chamadas[4].body!), { categoria: "IMPOSTO", descricao: "FGTS", valor: 400 });
  for (const a of ["costsApi.ts", "CustosShared.tsx", "CustosProfissionaisPage.tsx", "CustosItensCategoriaPage.tsx", "CustosPacotesPage.tsx"])
    assert.ok(!/DataManagement|hubId|AccessToken|CustoCalculado/.test(codigo(fonte(`src/features/planning/costs/${a}`))), a);
});

test("Números pt-BR, documentos colados (AUTO/MANUAL) e software nome — licença", () => {
  assert.equal(lerNumero("1.234,50"), 1234.5);
  assert.equal(lerNumero("160"), 160);
  assert.equal(lerNumero(""), null);
  assert.equal(lerNumero("abc"), null);
  assert.deepEqual(lerDocumentosColados("DOC-1;40;150,00\nDOC-2\t40\nDOC-3\t\t\n\nDOC-4;;150\nDOC-5;abc"), {
    documentos: [
      { documentoCodigo: "DOC-1", disciplina: null, hhAlocada: 40, valorHora: 150, custoAlocado: null, modoHH: "MANUAL", modoCusto: "MANUAL" },
      { documentoCodigo: "DOC-2", disciplina: null, hhAlocada: 40, valorHora: null, custoAlocado: null, modoHH: "MANUAL", modoCusto: "AUTO" },
      { documentoCodigo: "DOC-3", disciplina: null, hhAlocada: null, valorHora: null, custoAlocado: null, modoHH: "AUTO", modoCusto: "AUTO" },
    ],
    erros: [
      "Linha 5: Valor/hora exige HH previsto (o custo é HH previsto × Valor/hora, calculado pelo backend).",
      'Linha 6: HH previsto inválido: "abc".',
    ],
  });
  assert.equal(juntarSoftware(" AutoCAD ", "Licença anual"), "AutoCAD — Licença anual");
  assert.deepEqual(separarSoftware("AutoCAD — Licença anual"), { nome: "AutoCAD", licenca: "Licença anual" });
  assert.deepEqual(separarSoftware("Revit"), { nome: "Revit", licenca: "" });
});

test("Pacotes: documento manual usa os campos do Excel, sem disciplina e sem custo calculado no React; edição via PUT", async () => {
  const r = montarDocumento({ codigo: " DOC-1 ", hh: "40", valorHora: "150,00" });
  assert.ok("doc" in r);
  assert.equal(r.doc.custoAlocado, null, "custo previsto = backend (HH × Valor/hora)");
  assert.equal(r.doc.disciplina, null);
  assert.deepEqual(montarDocumento({ codigo: "", hh: "", valorHora: "" }), { erro: "Informe o número do documento." });
  assert.deepEqual(montarDocumento({ codigo: "D", hh: "-1", valorHora: "" }), { erro: "HH previsto e Valor/hora não podem ser negativos." });
  // Custo MANUAL legado sem Valor/hora é preservado tal como gravado; Valor/hora informado prevalece.
  const legado = montarDocumento({ codigo: "D", hh: "10", valorHora: "" }, 2500);
  assert.ok("doc" in legado && legado.doc.custoAlocado === 2500 && legado.doc.modoCusto === "MANUAL");
  const recalculado = montarDocumento({ codigo: "D", hh: "10", valorHora: "100" }, 2500);
  assert.ok("doc" in recalculado && recalculado.doc.custoAlocado === null && recalculado.doc.valorHora === 100);

  const chamadas: { url: string; method: string; body?: string }[] = [];
  __definirFetch(async (url, init) => { chamadas.push({ url, method: init?.method ?? "GET", body: init?.body as string }); return new Response("true", { status: 200 }); });
  await custosApi.atualizarPacote("P", 9, { nomeReferencia: "EMPRESA X – Civil", empresa: null, aliasTimesheet: null, descricao: null, ativo: true });
  await custosApi.atualizarDocumento("P", 9, 4, r.doc);
  assert.deepEqual(chamadas.map((c) => `${c.method} ${c.url.split("?")[0]}`), ["PUT /api/project-costs/packages/9", "PUT /api/project-costs/packages/9/documents/4"]);
  assert.ok(!("hhBase" in JSON.parse(chamadas[0].body!)), "base contratual só muda por aditivo");

  const pagina = codigo(fonte("src/features/planning/costs/CustosPacotesPage.tsx"));
  assert.ok(!/aria-label="Disciplina"/.test(pagina), "formulário manual não tem campo Disciplina");
  assert.match(pagina, /Custo previsto \(estimativa\)/);
});

test("Histórico 09C mantém a composição em vigências e protege telas legadas de edição retroativa", () => {
  const history = fonte("src/features/planning/costs/CustosVigenciasProfissional.tsx");
  const taxes = fonte("src/features/planning/costs/CustosImpostosProjetoPage.tsx");
  const software = fonte("src/features/planning/costs/CustosItensCategoriaPage.tsx");
  assert.match(history, /Nova vigência/);
  assert.match(history, /AlertDialog/);
  assert.match(history, /calculationType/);
  assert.match(taxes, /Base/);
  assert.match(taxes, /Nova vigência/);
  assert.match(software, /Softwares e equipamentos são itens vinculados a uma vigência/);
  assert.match(software, /!config\.software/);
});

test("Cópia de configuração oferece preview e commit em lote, sem acoplar Autodesk", () => {
  const source = fonte("src/features/planning/costs/ProjectCostCopyPanel.tsx");
  assert.match(source, /previaCopiaLote/);
  assert.match(source, /confirmarCopiaLote/);
  assert.match(source, /nenhuma gravação foi feita/);
  assert.match(codigo(source), /projetosElegiveisCustos/);
  assert.doesNotMatch(codigo(source), /DataManagement|hubId|AccessToken|Autodesk/);
});

test("Custos por documento informa pendências de HH sem match/vigência e limita planejado ao pacote", () => {
  const source = fonte("src/features/planning/costs/CustosPacotesPage.tsx");
  assert.match(source, /Custo real por documento/);
  assert.match(source, /uncostedHours/);
  assert.match(source, /timesheetStatus/);
  assert.match(source, /somente de pacotes/);
});

test("Pacotes: enriquecimento por documento é lido do backend (PT/EN), sem cálculo financeiro no React", () => {
  // Contrato real do backend: `disciplina` já é a canônica quando o documento vem enriquecido (doc3/documentStatus presentes).
  const doc = lerExtrasDocumento({ id: 1, documentoCodigo: "D-1", disciplina: "CIVIL", documentStatus: "Elaborando", doc3: "CIV", statusPercentual: 40, hhOriginal: 10, hhRedistribuida: 2, custoLiberado: 400, diagnostico: "Norma ausente" });
  const rateio = lerExtrasDocumento({ documentId: 1, hhFinal: 12, custoFinal: 1200 });
  const rec = lerExtrasDocumento({ timesheetHours: 15, recognizedHours: 12, excessHours: 3, ambiguousMatch: true, warnings: [{ message: "Norma ausente" }] });
  const e = mesclarExtras(doc, rateio, rec);
  assert.deepEqual([e.documentStatus, e.doc3, e.disciplinaCanonica, e.statusPercent], ["Elaborando", "CIV", "CIVIL", 40]);
  assert.deepEqual([e.hhBase, e.hhEfetiva, e.hhTimesheet, e.hhContabilizada, e.hhExcedente], [10, 12, 15, 12, 3]);
  assert.deepEqual([e.custoEfetivo, e.custoLiberado, e.custoBase, e.hhRedistribuida], [1200, 400, null, 2]);
  assert.equal(lerExtrasDocumento({ disciplina: "digitada" }).disciplinaCanonica, null, "disciplina sem enriquecimento não é oficial");
  assert.equal(e.matchAmbiguo, true);
  assert.deepEqual(e.diagnosticos, ["Norma ausente"]);
  assert.equal(semPesoConfigurado(e), false);
  assert.equal(semPesoConfigurado(lerExtrasDocumento({ documentStatus: "Novo status", statusPercentual: null })), true);
  assert.equal(semPesoConfigurado(lerExtrasDocumento({ documentStatus: "Aprovado", statusPercent: 100, statusConfigured: false })), true);
  assert.equal(lerExtrasDocumento({ documentStatus: "CANCELADO" }).cancelado, true);
  assert.equal(lerExtrasDocumento({ documentStatus: "Aprovado", cancelado: true }).cancelado, true);
  assert.deepEqual(lerExtrasImportacao({ hhPrevistaDocumento: 40, valorHora: 150, custoPrevistoDocumento: 6000 }), { empresa: null, alias: null, hhPrevista: 40, valorHora: 150, custoDocumento: 6000, aviso: null });
  const source = fonte("src/features/planning/costs/CustosPacotesPage.tsx");
  for (const t of ["SEM PESO CONFIGURADO", "MATCH AMBÍGUO – revisar alias", "HH/custo redistribuídos", "Texto usado para localizar o profissional no Timesheet (contém)", "O Timesheet não aumenta o contrato"])
    assert.ok(source.includes(t), t);
});
