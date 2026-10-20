import { test } from "node:test";
import assert from "node:assert/strict";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { DashboardConteudo } from "../ProductionDashboard";
import { filtroDaCelula } from "../DashboardMatrixCard";
import type { DashboardMatrix, DashboardResult } from "../types";

const noop = () => {};

const base = (over: Partial<DashboardResult>): DashboardResult => ({
  id: "ldp", title: "LDP", source: "LDP", totalRows: 3, filteredRows: 3, lastSync: null, dimensions: [], activeFilters: [],
  kpis: [], charts: [], notes: [], defaultFilters: null,
  table: { columns: [], rows: [], total: 0, page: 1, pageSize: 12, rowFilterDimension: "documento" },
  ...over,
});

const render = (data: DashboardResult) =>
  renderToStaticMarkup(createElement(DashboardConteudo, { data, page: 1, onPage: noop, onToggle: noop, onRemove: noop, onClear: noop }));

const tipo1 = base({
  meta: { tipoLdp: 1 },
  kpis: [{ key: "total", label: "Total", detail: "", value: 3, format: "numero", filterDimension: null, filterValue: null, selected: false }],
  table: {
    columns: [{ key: "na", label: "N&A Document Number", type: "texto" }, { key: "status", label: "Document Status (atual)", type: "texto" }],
    rows: [{ na: "A", status: "APPROVED", __rowKey: "A", __selected: false }], total: 1, page: 1, pageSize: 12, rowFilterDimension: "documento",
  },
});

const matriz: DashboardMatrix = {
  key: "statusPorFase", title: "Status por fase",
  rows: [{ key: "emitidos", label: "Emitidos" }, { key: "cancelados", label: "Cancelados" }],
  columns: [{ key: "30", label: "30%" }, { key: "60", label: "60%" }, { key: "90", label: "90%" }],
  cells: ["30", "60", "90"].flatMap((g) => [
    { row: "emitidos", column: g, value: g === "30" ? 2 : 1, filterDimension: `gate${g}`, filterValue: "EMITIDO", selected: false },
    { row: "cancelados", column: g, value: g === "60" ? 1 : 0, filterDimension: `gate${g}`, filterValue: "CANCELADO", selected: false },
  ]),
};

const tipo2 = base({
  meta: { tipoLdp: 2 },
  matrices: [matriz],
  table: {
    columns: [
      { key: "na", label: "N&A Document Number", type: "texto" },
      { key: "gate30", label: "30%", type: "gate" }, { key: "gate60", label: "60%", type: "gate" }, { key: "gate90", label: "90%", type: "gate" },
    ],
    rows: [
      {
        na: "B", __rowKey: "B", __selected: false,
        gate30: { rotulo: "Emitido · Aprovado", ativo: true, classe: "EMITIDO", documentStatus: "APPROVED", situation: "ISSUED" },
        gate60: { rotulo: "Cancelado", ativo: true, classe: "CANCELADO", documentStatus: "CANCELLED", situation: "CANCELLED" },
        gate90: { rotulo: "N/A", ativo: false, classe: "NA", documentStatus: null, situation: null },
      },
    ],
    total: 1, page: 1, pageSize: 12, rowFilterDimension: "documento",
  },
});

test("LDP Tipo 1: matriz de gates NÃO está no DOM e a tabela não tem 30/60/90", () => {
  const html = render(tipo1);
  assert.ok(!html.includes("data-matrix"));
  assert.ok(!html.includes("Status por fase"));
  assert.ok(!/>(30|60|90)%</.test(html));
  assert.ok(html.includes("N&amp;A Document Number"));
});

test("LDP Tipo 2: matriz aparece com colunas 30/60/90 e células clicáveis com o filtro correto", () => {
  const html = render(tipo2);
  assert.ok(html.includes('data-matrix="statusPorFase"'));
  for (const g of ["30%", "60%", "90%"]) assert.ok(html.includes(`>${g}<`), g);
  assert.ok(html.includes('data-filter="gate60=EMITIDO"'));
  assert.ok(html.includes('data-filter="gate30=CANCELADO"'));
  const c = matriz.cells.find((x) => x.row === "emitidos" && x.column === "60")!;
  assert.deepEqual(filtroDaCelula(c), ["gate60", "EMITIDO"]);
});

test("LDP Tipo 2: tabela continua 1 linha por documento, com N/A no gate inativo e detalhe do gate no tooltip", () => {
  const html = render(tipo2);
  assert.equal((html.match(/<tr[^>]*title="Filtrar a tela por este documento"/g) ?? []).length, 1);
  assert.ok(html.includes(">N/A<"));
  assert.ok(html.includes("Document Status: CANCELLED · Situation: CANCELLED"));
  assert.ok(html.includes(">Emitido · Aprovado<"));
});

test("Renderer genérico: dashboard sem matrizes (demais telas) continua sem bloco de matriz", () => {
  const html = render(base({ id: "hold", title: "HOLD", matrices: undefined }));
  assert.ok(!html.includes("data-matrix"));
});
