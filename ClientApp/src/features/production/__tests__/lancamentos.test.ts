import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { encontrarFolha, navProducao, rotaProducao, slugRedirecionado, ROTAS_PRODUCAO } from "../productionRoutes";
import { ROUTE_PERMISSIONS, criarCan, rotaPermitida } from "../../permissions/permissionLogic";
import { chaveDisciplina, disciplinasDaNorma, ehNaoMapeado, mesclarDisciplinas, origemDe } from "../lancamentos/disciplinasNorma";
import { FILTROS_VAZIOS, buscarDisciplinas, buscarDocumentos, buscarLancamentos, buscarPessoas, montarQuery } from "../lancamentos/lancamentosApi";

/** Código sem comentários (as regras citam "Autodesk"/"hubId" nos comentários de propósito). */
const codigo = (t: string) => t.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};
const PROJ = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";
const eff = (anyProject: string[]) => ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "x@na.com", projectIdAcc: null, permissions: anyProject, anyProject });

test("Lançamentos é GRUPO com Pessoas, Disciplinas e Documentos (rotas estáveis)", () => {
  const grupo = navProducao().find((i) => i.kind === "group" && i.label === "Lançamentos");
  assert.ok(grupo && grupo.kind === "group");
  assert.deepEqual(grupo.children.map((c) => [c.label, rotaProducao(c.slug)]), [
    ["Pessoas", "/controle-producao/lancamentos/pessoas"],
    ["Disciplinas", "/controle-producao/lancamentos/disciplinas"],
    ["Documentos", "/controle-producao/lancamentos/documentos"],
  ]);
  for (const c of grupo.children) assert.deepEqual(c.permissionKeys, ["production.managerial.hh-custo.view", "reports.gestao.view"]);
  assert.equal(encontrarFolha("lancamentos"), undefined, "a folha única antiga deixou de existir");
});

test("/controle-producao/lancamentos redireciona para Pessoas; rota global /lancamentos (Desvios/Holds/NC) intacta", () => {
  assert.equal(slugRedirecionado("lancamentos"), "lancamentos/pessoas");
  assert.equal(slugRedirecionado("lancamentos/pessoas"), undefined);
  assert.ok(ROTAS_PRODUCAO.some((r) => r.rota === "/controle-producao/lancamentos/documentos"));
  const legado = ROUTE_PERMISSIONS.find((r) => r.pattern === "/lancamentos");
  assert.deepEqual(legado?.keys, ["reports.lancamentos.view"]);
  const pagina = fonte("src/features/production/ControleProducaoPage.tsx");
  assert.match(pagina, /slugRedirecionado\(slug\)/);
});

test("Rotas de Lançamentos: liberadas pelas keys de produção, negadas sem elas", () => {
  const comKey = criarCan(eff(["reports.gestao.view"]), "any");
  const sem = criarCan(eff(["home.view"]), "any");
  for (const v of ["pessoas", "disciplinas", "documentos"]) {
    assert.equal(rotaPermitida(`/controle-producao/lancamentos/${v}`, "", comKey), true);
    assert.equal(rotaPermitida(`/controle-producao/lancamentos/${v}`, "", sem), false);
  }
});

test("Disciplinas da Norma: normalizadas, sem duplicadas por acento/caixa/espaço, nunca lista fixa", () => {
  const norma = { mapeamentos: [
    { normaMapeamentoId: 1, sigla: "ME", disciplina: "Mecânica" },
    { normaMapeamentoId: 2, sigla: "MC", disciplina: " MECANICA " },
    { normaMapeamentoId: 3, sigla: "CI", disciplina: "Civil" },
    { normaMapeamentoId: 4, sigla: "EL", disciplina: "Elétrica" },
  ] };
  assert.deepEqual(disciplinasDaNorma(norma), ["Civil", "Elétrica", "Mecânica"]);
  assert.deepEqual(disciplinasDaNorma(null), []);
  assert.equal(chaveDisciplina("  Tubulação  "), "TUBULACAO");
  const api = fonte("src/features/production/lancamentos/LancamentosShared.tsx");
  assert.match(api, /obterNormaDoProjeto\(projectId\)/);
});

test("Disciplinas do Timesheet fora da Norma aparecem; NÃO MAPEADO fica separado e nunca escondido", () => {
  const ops = mesclarDisciplinas(["Civil", "Mecânica"], ["MECÂNICA", "PROCESSOS", "NÃO MAPEADO", "NÃO MAPEADO - AJUSTAR"]);
  assert.deepEqual(ops.map((o) => [o.nome, o.origem, o.noTimesheet]), [
    ["Civil", "norma", false], ["Mecânica", "norma", true],
    ["PROCESSOS", "timesheet", true],
    ["NÃO MAPEADO", "nao-mapeado", true], ["NÃO MAPEADO - AJUSTAR", "nao-mapeado", true],
  ]);
  assert.equal(origemDe("mecanica", ["Mecânica"]), "norma");
  assert.equal(origemDe("PROCESSOS", ["Mecânica"]), "timesheet");
  assert.equal(ehNaoMapeado(""), true);
});

test("Query: projectId sempre; nunca hubId; vazios não são enviados; drilldown com exact=true", async () => {
  const urls: string[] = [];
  const f = async (u: string) => { urls.push(u); return new Response(JSON.stringify({ items: [], total: 0, crOs: "X" }), { status: 200 }); };
  const filtros = { ...FILTROS_VAZIOS, from: "2026-08-01", person: " Sulivan ", status: "ESTOURADO" };
  await buscarPessoas(PROJ, filtros, 2, f);
  await buscarDisciplinas(PROJ, filtros, f);
  await buscarDocumentos(PROJ, filtros, 1, f);
  await buscarLancamentos(PROJ, filtros, { document: "BML-1" }, 1, f);
  assert.match(urls[0], /^\/api\/production-audit\/people\?projectId=b\./);
  assert.ok(urls.every((u) => !u.includes("hubId")));
  assert.ok(urls.every((u) => !u.includes("DataManagement")));
  assert.ok(!urls[0].includes("status="), "pessoas não recebe status");
  assert.ok(!urls[1].includes("status="), "disciplinas não recebe status");
  assert.match(urls[2], /status=ESTOURADO/);
  assert.match(urls[0], /person=Sulivan&/);
  assert.match(urls[3], /\/entries\?.*document=BML-1.*exact=true/);
  assert.equal(new URLSearchParams(montarQuery(PROJ, { discipline: "", document: "" })).toString(), `projectId=${encodeURIComponent(PROJ)}`);
});

test("Telas de Lançamentos não usam Autodesk/hubId", () => {
  for (const a of ["LancamentosPessoasPage.tsx", "LancamentosDisciplinasPage.tsx", "LancamentosDocumentosPage.tsx", "LancamentosShared.tsx", "lancamentosApi.ts"]) {
    const t = codigo(fonte(`src/features/production/lancamentos/${a}`));
    assert.ok(!/DataManagement|hubId|AccessToken|autodesk/i.test(t), a);
  }
});
