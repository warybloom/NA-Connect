import { test } from "node:test";
import assert from "node:assert/strict";
import { disciplinasDe, porDisciplina, porStatus, resumoDe, SEM_DISCIPLINA, TODAS } from "../auditFilters";
import type { DocumentResult } from "../documentQualityApi";

const doc = (fileName: string, discipline: string | null, status: string) => ({ fileName, discipline, status } as unknown as DocumentResult);
const DOCS = [doc("a", "PROCESS", "CONFORME"), doc("b", "PIPING", "DIVERGÊNCIA"), doc("c", "PROCESS", "DIVERGÊNCIA"), doc("d", null, "FORA DA LDP"), doc("e", "ELECTRICAL", "AMBÍGUO")];

test("Disciplinas vêm dos resultados (sem lista fixa) + 'sem disciplina' para não identificados", () => {
  assert.deepEqual(disciplinasDe(DOCS), ["ELECTRICAL", "PIPING", "PROCESS", SEM_DISCIPLINA]);
  assert.deepEqual(disciplinasDe(DOCS.slice(0, 3)), ["PIPING", "PROCESS"]);
});

test("Filtro por disciplina combina com o filtro de status", () => {
  assert.deepEqual(porDisciplina(DOCS, "PROCESS").map((d) => d.fileName), ["a", "c"]);
  assert.deepEqual(porDisciplina(DOCS, SEM_DISCIPLINA).map((d) => d.fileName), ["d"]);
  assert.equal(porDisciplina(DOCS, TODAS).length, 5);
  assert.deepEqual(porStatus(porDisciplina(DOCS, "PROCESS"), "DIVERGÊNCIA").map((d) => d.fileName), ["c"]);
});

test("Indicadores acompanham a disciplina filtrada", () => {
  assert.deepEqual(resumoDe(porDisciplina(DOCS, "PROCESS")), { total: 2, compliant: 1, divergent: 1, outsideLdp: 0, notIdentified: 0, notCalibrated: 0, readErrors: 0 });
  assert.deepEqual(resumoDe(DOCS), { total: 5, compliant: 1, divergent: 2, outsideLdp: 1, notIdentified: 1, notCalibrated: 0, readErrors: 0 });
});

test("Cards executivos: as 5 categorias fecham exatamente o total (sem somar uma na outra)", () => {
  const docs = ["CONFORME", "DIVERGÊNCIA", "DIVERGÊNCIA", "FORA DA LDP", "NÃO CALIBRADO", "ERRO DE LEITURA", "ERRO DE LEITURA"].map((st, i) => doc(String(i), "PROCESS", st));
  const r = resumoDe(docs);
  assert.deepEqual([r.compliant, r.divergent, r.outsideLdp, r.notCalibrated, r.readErrors], [1, 2, 1, 1, 2]);
  assert.equal(r.compliant + r.divergent + r.outsideLdp + r.notCalibrated + r.readErrors, r.total);
});
