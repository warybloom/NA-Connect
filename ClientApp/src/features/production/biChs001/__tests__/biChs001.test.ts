// BI — CHS001 (Original): contratos do registro/navegação, fidelidade estrutural do layout extraído e semântica do motor
// (filtros PBIR + medidas DAX). Rodar: node _bi_chs001_replica/tools/testar.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { BI_CHS001_GRUPO, BI_CHS001_PAGINAS, PRIORIDADE_10, ROTAS_BI_CHS001, paginaBiPorId, paginaBiPorSlugProducao, rotaBiChs001 } from "../biChs001Paginas";
import { folhasProducao, PRODUCTION_NAV } from "../../productionRoutes";
import { NAVEGACAO } from "../../../sidebar/officialNavigation";
import { criarCan, rotaPermitida, type EffectivePermissions } from "../../../permissions/permissionLogic";
import { compilarFiltros, type Linha } from "../dados/filtrosPbi";
import { MEDIDAS } from "../dados/medidas";
import { converterLinhas } from "../dados/entidades";
import { dadosDoVisual } from "../dados/motor";
import type { PaginaPbi } from "../pbi/modelo";

const raiz = (() => {
  for (const b of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { readdirSync(path.join(b, "src/features/production/biChs001/generated")); return b; } catch { /* próximo */ }
  }
  throw new Error("ClientApp não encontrado");
})();
const gerado = (arq: string) => JSON.parse(readFileSync(path.join(raiz, "src/features/production/biChs001/generated", arq), "utf8"));
const eff = (p: Partial<EffectivePermissions>): EffectivePermissions => ({
  enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "u@na.com", projectIdAcc: null, permissions: [], anyProject: [], ...p,
});

const NOMES_PBIX = ["Capa", "Resumo Geral", "Resumo 30%", "Resumo 60%", "Resumo 90%", "Progress to Date", "Planned Activities",
  "LDP - 3D - 30%", "LDP - 3D - 60%", "LDP - 3D - 90%", "3D - MODEL", "LDP - Engineering - 30%", "LDP - Engineering - 60%",
  "LDP - Engineering - 90%", "CAP externo", "CAP Interno", "Programação", "Controle de Desvio", "Controle de Holds",
  "Não Conformidades", "Controle de Hora e Custo", "Controle de Hora e Custo Gestão", "Controle de Qualidade"];

test("23 páginas com nomes e ordem EXATOS do pages.json do PBIX; ids e arquivos batem com o layout extraído", () => {
  assert.deepEqual(BI_CHS001_PAGINAS.map((p) => p.nome), NOMES_PBIX);
  const indice = gerado("indice.json");
  assert.deepEqual(indice.paginas.map((p: { nome: string }) => p.nome), NOMES_PBIX);
  BI_CHS001_PAGINAS.forEach((p, i) => {
    assert.equal(p.ordem, i + 1);
    assert.equal(p.pageId, indice.paginas[i].id, p.nome);
    assert.equal(p.arquivo, indice.paginas[i].arquivo, p.nome);
    assert.equal(paginaBiPorId(p.pageId), p);
    assert.equal(paginaBiPorSlugProducao(`bi-chs001/${p.slug}`), p);
  });
  assert.equal(new Set(BI_CHS001_PAGINAS.map((p) => p.slug)).size, 23);
  assert.equal(paginaBiPorSlugProducao("ldp"), undefined);
});

test("Grupo prioritário (PRIORIDADE_10_TELAS.md): 10 páginas na ordem do arquivo; as outras 13 marcadas em desenvolvimento", () => {
  assert.deepEqual([...PRIORIDADE_10], ["resumo-30", "resumo-60", "resumo-90", "ldp-engineering-30", "ldp-engineering-60", "ldp-engineering-90",
    "ldp-3d-30", "ldp-3d-60", "ldp-3d-90", "programacao"]);
  assert.equal(BI_CHS001_PAGINAS.filter((p) => !p.emDesenvolvimento).length, 10);
  assert.equal(BI_CHS001_PAGINAS.filter((p) => p.emDesenvolvimento).length, 13);
});

test("Isolamento: o BI não entra em PRODUCTION_NAV; na TV só as 10 prioritárias, em catálogo próprio (Gestão à Vista Tipo 2)", () => {
  assert.ok(folhasProducao().every((f) => !f.slug.startsWith("bi-chs001")));
  assert.ok(PRODUCTION_NAV.every((i) => i.label !== BI_CHS001_GRUPO));
  assert.equal(BI_CHS001_GRUPO, "Gestão à Vista Tipo 2");
  // 19/10/2026: o catálogo da TV lê este registro só para as páginas de PRIORIDADE_10 (detalhes em tvGestaoVistaTipo2.test.ts)
  // e carrega BiChs001Page apenas por import() dinâmico.
  const tv = readFileSync(path.join(raiz, "src/features/tv/tvScreens.tsx"), "utf8");
  assert.match(tv, /\.filter\(\(p\) => !p\.emDesenvolvimento && \(PRIORIDADE_10 as readonly string\[\]\)\.includes\(p\.slug\)\)/);
  assert.ok(!/^import [^;]*BiChs001Page/m.test(tv));
});

test("Sidebar: grupo próprio dentro de Controle de Produção com as 23 folhas e as keys de cada página", () => {
  const cp = NAVEGACAO.find((n) => n.label === "Controle de Produção");
  const grupo = cp?.children?.find((n) => n.label === BI_CHS001_GRUPO);
  assert.ok(grupo?.children);
  assert.deepEqual(grupo.children.map((n) => n.label), NOMES_PBIX);
  grupo.children.forEach((n, i) => {
    assert.equal(n.to, rotaBiChs001(BI_CHS001_PAGINAS[i].slug));
    assert.deepEqual(n.permissionKey, BI_CHS001_PAGINAS[i].permissionKeys);
  });
  // As telas existentes continuam antes do grupo novo, na mesma ordem.
  assert.equal(cp!.children!.at(-1)?.label, BI_CHS001_GRUPO);
});

test("Guard de URL: páginas do BI exigem a key da página (custo só com key gerencial de HH/custo)", () => {
  const gestao = criarCan(eff({ anyProject: ["reports.gestao.view"] }), "any");
  const operacional = criarCan(eff({ anyProject: ["production.operational.ldp.view"] }), "any");
  const custo = criarCan(eff({ anyProject: ["production.managerial.hh-custo.view"] }), "any");
  assert.equal(rotaPermitida("/controle-producao/bi-chs001/ldp-engineering-30", "", gestao), true);
  assert.equal(rotaPermitida("/controle-producao/bi-chs001/ldp-engineering-30", "", operacional), false);
  assert.equal(rotaPermitida("/controle-producao/bi-chs001/controle-de-hora-e-custo", "", gestao), false);
  assert.equal(rotaPermitida("/controle-producao/bi-chs001/controle-de-hora-e-custo", "", custo), true);
  assert.equal(ROTAS_BI_CHS001.length, 23);
  for (const p of BI_CHS001_PAGINAS.filter((x) => x.containsCostData)) assert.deepEqual(p.permissionKeys, ["production.managerial.hh-custo.view"]);
});

test("Layout extraído: canvas 2048×1123 em todas as páginas; logos CHS/Whitefox marcadas LOGO_NA (omitidas no canvas; única logo = sidebar)", () => {
  for (const p of BI_CHS001_PAGINAS) {
    const pg = gerado(p.arquivo) as PaginaPbi;
    assert.equal(pg.largura, 2048, p.nome);
    assert.equal(pg.altura, 1123, p.nome);
    const texto = JSON.stringify(pg);
    assert.ok(!/Imagem2_CHS|Imagem_WFX/.test(texto), `logo original em ${p.nome}`);
    assert.equal((texto.match(/"recurso":"LOGO_NA"/g) ?? []).length, 2, `duas logos de cliente marcadas em ${p.nome}`);
  }
});

test("Filtros PBIR: Not In com null, In sem diferenciar maiúsculas, comparação e condição sobre medida sinalizada", () => {
  const notNull3D = { filters: [{ name: "f", filter: { From: [{ Name: "l", Entity: "LDP (4)" }], Where: [{ Condition: { Not: { Expression: { In: {
    Expressions: [{ Column: { Expression: { SourceRef: { Source: "l" } }, Property: "FORMAT" } }], Values: [[{ Literal: { Value: "null" } }], [{ Literal: { Value: "'3D'" } }]] } } } } }] } }] };
  const f = compilarFiltros(notNull3D, "LDP (4)");
  assert.equal(f.aplicar({ FORMAT: "A" }), true);
  assert.equal(f.aplicar({ FORMAT: "3d" }), false);
  assert.equal(f.aplicar({ FORMAT: null }), false);
  assert.equal(f.aplicar({ FORMAT: "" }), false);
  const medida = { filters: [{ name: "m", filter: { From: [{ Name: "k", Entity: "KPI" }], Where: [{ Condition: { Comparison: { ComparisonKind: 0,
    Left: { Measure: { Expression: { SourceRef: { Source: "k" } }, Property: "X" } }, Right: { Literal: { Value: "1L" } } } } }] } }] };
  assert.equal(compilarFiltros(medida, "LDP (4)").naoAvaliadas.length, 1);
  const semCondicao = { filters: [{ name: "s", field: { Measure: {} }, type: "Advanced" }] };
  assert.equal(compilarFiltros(semCondicao, "LDP (4)").naoAvaliadas.length, 0);
});

test("Medidas DAX transcritas: CANCELED 30 compara com 'Canceled' (não casa com 'CANCELLED'); vazio = BLANK", () => {
  const ls: Linha[] = [{ "SITUATION 30%": "CANCELLED", "DOCUMENT STATUS 30%": "CANCELLED" }, { "SITUATION 30%": "ISSUED", "DOCUMENT STATUS 30%": "APPROVED" },
    { "SITUATION 60%": "CANCELED", "DOCUMENT STATUS 60%": "CLIENT REVIEW" }];
  assert.equal(MEDIDAS["dFase Engenharia 30%.CANCELED 30"].calcular(ls), null);
  assert.equal(MEDIDAS["dFase Engenharia 30%.ISSUED 30"].calcular(ls), 1);
  assert.equal(MEDIDAS["dFase Engenharia 60%.CANCELED 60"].calcular(ls), 1);
  assert.equal(MEDIDAS["dFase Engenharia 60%.CLIENT REVIEW 60"].calcular(ls), 1);
  assert.equal(MEDIDAS["dFase Engenharia 30%.Documents 30"].calcular(ls), 2);
});

test("Motor sobre a página LDP - Engineering - 30%: filtros de página (FORMAT ≠ null/3D e Fase 30% ≠ null) e cartões", () => {
  const pagina = gerado("p12.json") as PaginaPbi;
  const brutas = [
    { Item: "1", Discipline: "PROCESS", Format: "A", Fase_30: "P", Document_Status_30: "APPROVED", Situation_30: "ISSUED" },
    { Item: "2", Discipline: "PROCESS", Format: "D", Fase_30: "FA", Document_Status_30: "CANCELLED", Situation_30: "CANCELLED" },
    { Item: "3", Discipline: "CIVIL", Format: "3D", Fase_30: "P", Document_Status_30: "APPROVED", Situation_30: "ISSUED" },
    { Item: "4", Discipline: "CIVIL", Format: null, Fase_30: "P", Document_Status_30: "APPROVED", Situation_30: "ISSUED" },
    { Item: "5", Discipline: "MECHANICAL", Format: "A", Fase_30: null, Document_Status_30: null, Situation_30: null },
  ];
  const ctx = { pagina, fontes: { "LDP (4)": { estado: "ok" as const, linhas: converterLinhas("LDP (4)", brutas) } }, segmentacoes: {} };
  const visual = (prefixo: string) => pagina.visuais.find((v) => v.id.startsWith(prefixo))!;
  const total = dadosDoVisual(visual("c76cb8f4"), ctx);
  assert.ok(total.estado === "ok" && total.tipo === "valores");
  assert.deepEqual(Object.values(total.valores), [2]);
  const issued = dadosDoVisual(visual("3ed78afe"), ctx);
  assert.ok(issued.estado === "ok" && issued.tipo === "valores");
  assert.deepEqual(Object.values(issued.valores), [1]);
  const porDisciplina = dadosDoVisual(visual("46c74ecd"), ctx);
  assert.ok(porDisciplina.estado === "ok" && porDisciplina.tipo === "pontos");
  assert.deepEqual(porDisciplina.pontos.map((p) => [p.categoria, Object.values(p.valores)[0]]), [["PROCESS", 2]]);
  const tabela = dadosDoVisual(visual("95a23f2f"), ctx);
  assert.ok(tabela.estado === "ok" && tabela.tipo === "linhas");
  assert.deepEqual(tabela.linhas.map((l) => l["LDP (4).ITEM"]), [1, 2]);
});

test("Medida cuja coluna de origem está vazia na fonte atual fica PENDENTE com o campo faltante (nunca número)", () => {
  const pagina = gerado("p04.json") as PaginaPbi;
  const card = pagina.visuais.find((v) => v.id.startsWith("68d2224d"))!; // KPI 60 | ACC | % Planejado
  const linhas = [{ "Level 4": "Engineering - 60%", Término: "2026-10-19T17:00:00", Trabalho: 8, Início: null }];
  const d = dadosDoVisual(card, { pagina, fontes: { WBX: { estado: "ok", linhas, vazias: ["Início"] } }, segmentacoes: {} });
  assert.equal(d.estado, "pendente");
  assert.match(d.estado === "pendente" ? d.motivo : "", /WBX\[Início\]/);
  // Fonte ainda não carregada → "carregando" (o hook carrega WBX), não número.
  assert.equal(dadosDoVisual(card, { pagina, fontes: {}, segmentacoes: {} }).estado, "carregando");
});
