import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { encontrarFolha, folhasProducao, navProducao, rotaExternaRedirecionada } from "../productionRoutes";
import { ROUTE_PERMISSIONS, criarCan, permissaoDaRota, rotaPermitida } from "../../permissions/permissionLogic";
import { PERMISSOES_AUDITORIA_PROJETO, ROTA_ANTIGA_AUDITORIA_PROJETO, ROTA_AUDITORIA_PROJETO } from "../../planning/auditoriaProjeto";
import {
  CONCILIADO, FILTROS_AUDITORIA_INICIAIS, SEM_DISCIPLINA, filtrarAuditoria, numeroFiltro, opcoesAuditoria,
  type DocumentoAuditoria, type FiltrosAuditoria,
} from "../analytics/auditoriaFiltros";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};
const TOL = 0.005;
const doc = (documentCode: string, ldpActual: number, timesheetActual: number, status: string, discipline: string | null, reason = ""): DocumentoAuditoria =>
  ({ documentCode, discipline, ldpActual, timesheetActual, difference: Math.round((ldpActual - timesheetActual) * 100) / 100, status, reason });
const DOCS: DocumentoAuditoria[] = [
  doc("BML-ELE-001", 30, 20, "HH REALIZADA DIFERENTE", "Elétrica", "LDP=30,00; Timesheet=20,00"),
  doc("BML-MEC-002", 10, 20, "HH REALIZADA DIFERENTE", "Mecânica", "LDP=10,00; Timesheet=20,00"),
  doc("bml-ele-003", 15, 15, CONCILIADO, "Elétrica", "Valores compatíveis após normalização trim/upper."),
  doc("BML-CIV-004", 0, 8, "SOMENTE NO TIMESHEET", null, "Código não encontrado na LDP. Timesheet=8,00."),
  doc("BML-CIV-005", 12, 0, "SOMENTE NA LDP", "Civil", "Código sem lançamentos no Timesheet. LDP=12,00."),
];
const com = (p: Partial<FiltrosAuditoria>): FiltrosAuditoria => ({ ...FILTROS_AUDITORIA_INICIAIS, visao: "todos", ...p });
const codigos = (p: Partial<FiltrosAuditoria>) => filtrarAuditoria(DOCS, com(p), TOL).map((d) => d.documentCode);

test("E: documento — contém, sem diferenciar maiúsculas/acentos, com trim", () => {
  assert.deepEqual(codigos({ documento: "  ele-00 " }), ["BML-ELE-001", "bml-ele-003"]);
  assert.deepEqual(codigos({ documento: "BML-CIV-005" }), ["BML-CIV-005"]);
});

test("F: disciplina — seleção múltipla a partir dos dados (sem disciplina incluída)", () => {
  assert.deepEqual(opcoesAuditoria(DOCS).disciplinas, ["Civil", "Elétrica", "Mecânica", SEM_DISCIPLINA]);
  assert.deepEqual(codigos({ disciplinas: ["Mecânica", SEM_DISCIPLINA] }), ["BML-MEC-002", "BML-CIV-004"]);
});

test("G: HH Trabalhado LDP — faixa mín./máx. (aceita vírgula decimal)", () => {
  assert.deepEqual(codigos({ ldpMin: "12", ldpMax: "30" }), ["BML-ELE-001", "bml-ele-003", "BML-CIV-005"]);
  assert.equal(numeroFiltro("1.234,5"), 1234.5);
  assert.equal(numeroFiltro("abc"), null);
});

test("H: Horas Trabalhadas Timesheet — faixa mín./máx.", () => {
  assert.deepEqual(codigos({ tsMin: "15", tsMax: "20" }), ["BML-ELE-001", "BML-MEC-002", "bml-ele-003"]);
});

test("I: Diferença de Horas — faixa com sinal (LDP − TIMESHEET) e 'somente com diferença' pela tolerância", () => {
  assert.deepEqual(codigos({ difMax: "-0,01" }), ["BML-MEC-002", "BML-CIV-004"]);
  assert.deepEqual(codigos({ difMin: "10" }), ["BML-ELE-001", "BML-CIV-005"]);
  const quase = [doc("X", 10.004, 10, "HH REALIZADA DIFERENTE", "Civil")];
  assert.equal(filtrarAuditoria(quase, com({ somenteComDiferenca: true }), TOL).length, 0, "abaixo da tolerância do backend");
  assert.deepEqual(codigos({ somenteComDiferenca: true }), ["BML-ELE-001", "BML-MEC-002", "BML-CIV-004", "BML-CIV-005"]);
});

test("J: Situação — seleção múltipla a partir dos dados", () => {
  assert.deepEqual(opcoesAuditoria(DOCS).situacoes, [CONCILIADO, "HH REALIZADA DIFERENTE", "SOMENTE NA LDP", "SOMENTE NO TIMESHEET"]);
  assert.deepEqual(codigos({ situacoes: ["SOMENTE NA LDP", "SOMENTE NO TIMESHEET"] }), ["BML-CIV-004", "BML-CIV-005"]);
});

test("K: Motivo Provável — contém", () => {
  assert.deepEqual(codigos({ motivo: "nao encontrado" }), ["BML-CIV-004"]);
});

test("L: conciliados permanecem no dataset e podem ser exibidos (Todos / Somente divergências / Conciliados)", () => {
  assert.equal(filtrarAuditoria(DOCS, FILTROS_AUDITORIA_INICIAIS, TOL).some((d) => d.status === CONCILIADO), false, "padrão = divergências");
  assert.deepEqual(filtrarAuditoria(DOCS, com({ visao: "conciliados" }), TOL).map((d) => d.documentCode), ["bml-ele-003"]);
  assert.equal(filtrarAuditoria(DOCS, com({ visao: "todos" }), TOL).length, DOCS.length);
});

test("M: Auditoria do Projeto NÃO está mais na sidebar do Controle de Produção", () => {
  const labels = (itens: ReturnType<typeof navProducao>): string[] => itens.flatMap((i) => i.kind === "group" ? [i.label, ...i.children.map((c) => c.label)] : [i.label]);
  assert.equal(labels(navProducao()).includes("Auditoria do Projeto"), false);
  assert.equal(encontrarFolha("auditoria"), undefined);
  assert.equal(folhasProducao().some((f) => f.label === "Auditoria do Projeto"), false);
});

test("N: Auditoria do Projeto está no Painel de Planejamento, uma única vez, com as keys existentes", () => {
  const layout = fonte("src/features/sidebar/officialNavigation.ts");
  const inicio = layout.indexOf('label: "Painel de Planejamento"');
  const bloco = layout.slice(inicio, layout.indexOf("\n  },", inicio));
  assert.match(bloco, /label: "Auditoria do Projeto", icon: \w+, to: ROTA_AUDITORIA_PROJETO, permissionKey: PERMISSOES_AUDITORIA_PROJETO/);
  assert.equal(layout.split('label: "Auditoria do Projeto"').length - 1, 1, "sem item duplicado");
  assert.equal(ROTA_AUDITORIA_PROJETO, "/planejamento/auditoria-projeto");
  assert.deepEqual(permissaoDaRota(ROTA_AUDITORIA_PROJETO), PERMISSOES_AUDITORIA_PROJETO);
  assert.equal(ROUTE_PERMISSIONS.filter((r) => r.pattern === ROTA_AUDITORIA_PROJETO).length, 1);
  const eff = (permissions: string[]) => ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "x@na.com", projectIdAcc: null, permissions, anyProject: permissions });
  assert.equal(rotaPermitida(ROTA_AUDITORIA_PROJETO, "", criarCan(eff(["reports.gestao.view"]))), true);
  assert.equal(rotaPermitida(ROTA_AUDITORIA_PROJETO, "", criarCan(eff(["planning.coordination.view"]))), false);
  assert.match(fonte("src/App.tsx"), /<Route path=\{ROTA_AUDITORIA_PROJETO\} element=\{<AuditoriaProjetoPlanejamentoPage \/>\} \/>/);
});

test("O: rota antiga /controle-producao/auditoria redireciona para o Painel de Planejamento", () => {
  assert.equal(ROTA_ANTIGA_AUDITORIA_PROJETO.split("/").pop(), "auditoria");
  assert.equal(rotaExternaRedirecionada("auditoria"), ROTA_AUDITORIA_PROJETO);
  assert.equal(rotaExternaRedirecionada("resumo"), undefined);
  assert.match(fonte("src/features/production/ControleProducaoPage.tsx"), /const externa = rotaExternaRedirecionada\(slug\);\s*if \(externa\) return <Navigate to=\{externa\} replace \/>;/);
});
