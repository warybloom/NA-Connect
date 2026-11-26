
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  confirmedRedistributionCodes,
  isConfirmedDecision,
  isRedistributionPending,
  redistributionKey,
  summarizeDestinations,
} from "../informationQualityApi";

const finding = (rule: string, documentCode: string | null) => ({ rule, documentCode });
const confirmed = new Set([redistributionKey(" doc-002 ")]);

test("Simular sÃ³ aparece para TIMESHEET_SEM_LDP e DOCUMENTO_RETIRADO_COM_HH pendentes", () => {
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", "DOC-001"), confirmed), true);
  assert.equal(isRedistributionPending(finding("DOCUMENTO_RETIRADO_COM_HH", "DOC-001"), confirmed), true);
  assert.equal(isRedistributionPending(finding("LDP_SEM_TIMESHEET", "DOC-001"), confirmed), false);
  assert.equal(isRedistributionPending(finding("SEM_INCONSISTENCIAS", null), confirmed), false);
});

test("OcorrÃªncia jÃ¡ redistribuÃ­da (ou sem nÃºmero de documento) nÃ£o oferece Simular", () => {
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", "DOC-002"), confirmed), false);
  assert.equal(isRedistributionPending(finding("DOCUMENTO_RETIRADO_COM_HH", "doc-002"), confirmed), false);
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", null), confirmed), false);
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", "  "), confirmed), false);
});

test("SÃ³ status CONFIRMADO conta como tratado", () => {
  assert.equal(isConfirmedDecision({ status: "CONFIRMADO" }), true);
  assert.equal(isConfirmedDecision({ status: " confirmado " }), true);
  assert.equal(isConfirmedDecision({ status: "PENDENTE" }), false);
  assert.equal(isConfirmedDecision({ status: "CANCELADO" }), false);
  assert.equal(isConfirmedDecision({ status: null }), false);

  const codes = confirmedRedistributionCodes([], [
    { status: "CONFIRMADO", sourceDocumentCode: "DOC-010" },
    { status: "PENDENTE", sourceDocumentCode: "DOC-011" },
  ]);
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", "DOC-010"), codes), false);
  assert.equal(isRedistributionPending(finding("TIMESHEET_SEM_LDP", "DOC-011"), codes), true);
});

test("REDISTRIBUICAO_CONFIRMADA da auditoria impede Simular mesmo sem histÃ³rico", () => {
  const codes = confirmedRedistributionCodes([finding("REDISTRIBUICAO_CONFIRMADA", "doc-020")], []);
  assert.equal(isRedistributionPending(finding("DOCUMENTO_RETIRADO_COM_HH", "DOC-020"), codes), false);
  assert.equal(isRedistributionPending(finding("REDISTRIBUICAO_CONFIRMADA", "DOC-020"), codes), false);
});

const dest = (documentCode: string, weight: number, hours: number, cost: number) =>
  ({ documentCode, title: null, plannedHours: weight * 100, weight, hours, cost });

test("Totais e diferenÃ§a destinos âˆ’ origem sempre calculados", () => {
  const exact = summarizeDestinations([dest("A", 0.5, 5, 50), dest("B", 0.5, 5, 50)], 10, 100);
  assert.equal(exact.hasDifference, false);
  assert.equal(exact.hoursDifference, 0);
  assert.equal(exact.costDifference, 0);
  assert.deepEqual(exact.inconsistentWeightCodes, []);

  const rounded = summarizeDestinations([dest("A", 1 / 3, 3.33, 33.33), dest("B", 1 / 3, 3.33, 33.33), dest("C", 1 / 3, 3.33, 33.33)], 10, 100);
  assert.equal(rounded.hasDifference, true);
  assert.ok(Math.abs(rounded.hoursDifference + 0.01) < 1e-9);
  assert.ok(Math.abs(rounded.costDifference + 0.01) < 1e-9);
  assert.deepEqual(rounded.inconsistentWeightCodes, []);
});

test("HH e custo com pesos diferentes sÃ£o sinalizados", () => {
  const totals = summarizeDestinations([dest("A", 0.5, 5, 70), dest("B", 0.5, 5, 30)], 10, 100);
  assert.deepEqual(totals.inconsistentWeightCodes, ["A", "B"]);
  assert.equal(totals.hasDifference, false);
});

