import test from "node:test";
import assert from "node:assert/strict";
import { linhasAExportar, neutralizarFormula, nomeSeguro } from "../plantReportsSafe.ts";
import { atingimentoPorFase, faseEmAndamento, larguraBarra, type FaseMeta } from "../plantPhaseBi.ts";

// ---------- injeção de fórmula ----------
test("neutralizarFormula prefixa apóstrofo em =, +, -, @, tab, CR, |, % (também após espaços)", () => {
  for (const v of ["=1+1", "+SUM(A1)", "-2+3", "@cmd", "\t=1", "\r=1", "|calc", "%x", "  =HYPERLINK(\"http://x\")"]) {
    assert.equal(neutralizarFormula(v), "'" + v, v);
  }
});
test("neutralizarFormula preserva texto comum, vazio e nulo", () => {
  assert.equal(neutralizarFormula("P-1001-A"), "P-1001-A");
  assert.equal(neutralizarFormula("10\"-CS-150"), "10\"-CS-150");
  assert.equal(neutralizarFormula(""), "");
  assert.equal(neutralizarFormula(null), "");
  assert.equal(neutralizarFormula(undefined), "");
  assert.equal(neutralizarFormula(42), "42");
});
test("nomeSeguro remove separadores de caminho e símbolos", () => {
  assert.equal(nomeSeguro("../../etc/passwd"), "etc_passwd");
  assert.equal(nomeSeguro("tie-ins"), "tie-ins");
});

// ---------- limites ----------
test("linhasAExportar respeita o teto e o total, nunca negativo/NaN", () => {
  assert.equal(linhasAExportar(250_000, 20_000), 20_000);
  assert.equal(linhasAExportar(150, 20_000), 150);
  assert.equal(linhasAExportar(-5, 20_000), 0);
  assert.equal(linhasAExportar(Number.NaN, 20_000), 0);
});

// ---------- gates por fase: NUNCA somar fases ----------
const fases: FaseMeta[] = [
  { id: "f90", name: "90", sortOrder: 3, expected: { line: 900 } },
  { id: "f30", name: "30", sortOrder: 1, expected: { line: 300, valve: 50 } },
  { id: "f60", name: "60", sortOrder: 2, expected: { line: 600 } },
];
test("atingimentoPorFase: ordena por sortOrder e divide o MESMO realizado pela meta de cada fase (sem somar)", () => {
  const l = atingimentoPorFase(fases, "line", { pid: 450, piping: 300 });
  assert.deepEqual(l.map(x => x.phaseId), ["f30", "f60", "f90"]);
  assert.deepEqual(l.map(x => x.meta), [300, 600, 900]);
  assert.deepEqual(l.map(x => x.pipingPct), [100, 50, (300 / 900) * 100]);
  assert.equal(l[1].pidPct, 75);
});
test("atingimentoPorFase: meta ausente ou zero e realizado ausente viram null, nunca 0%", () => {
  const v = atingimentoPorFase(fases, "valve", { pid: 10, piping: null });
  assert.equal(v[0].meta, 50); assert.equal(v[0].pidPct, 20); assert.equal(v[0].pipingPct, null);
  assert.equal(v[1].meta, null); assert.equal(v[1].pidPct, null);
  assert.equal(atingimentoPorFase(fases, "line", null)[0].pidPct, null);
  const zero = atingimentoPorFase([{ id: "z", name: "z", sortOrder: 0, expected: { line: 0 } }], "line", { pid: 5, piping: 5 });
  assert.equal(zero[0].pipingPct, null);
});
test("larguraBarra limita a 0–100 e trata null", () => {
  assert.equal(larguraBarra(null), 0); assert.equal(larguraBarra(180), 100); assert.equal(larguraBarra(-3), 0); assert.equal(larguraBarra(42.5), 42.5);
});
test("faseEmAndamento: primeira fase com 3D < 100%; todas atingidas → última; sem dados → null", () => {
  const l = atingimentoPorFase(fases, "line", { pid: 0, piping: 300 });
  assert.equal(faseEmAndamento(l), "f60");
  assert.equal(faseEmAndamento(atingimentoPorFase(fases, "line", { pid: 1, piping: 1000 })), "f90");
  assert.equal(faseEmAndamento(atingimentoPorFase(fases, "line", null)), null);
});
