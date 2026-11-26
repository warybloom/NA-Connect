import { test } from "node:test";
import assert from "node:assert/strict";
import { alternarFiltro, removerFiltro, serializarFiltros } from "../filtrosLogic";

test("clicar adiciona e clicar de novo remove o filtro", () => {
  const um = alternarFiltro({}, "disciplina", "MECANICA");
  assert.deepEqual(um, { disciplina: ["MECANICA"] });
  assert.deepEqual(alternarFiltro(um, "disciplina", "MECANICA"), {});
});

test("disciplina + status combinam (AND entre dimensões)", () => {
  const f = alternarFiltro(alternarFiltro({}, "disciplina", "MECANICA"), "status", "EMITIDO");
  assert.deepEqual(f, { disciplina: ["MECANICA"], status: ["EMITIDO"] });
  assert.deepEqual(serializarFiltros(f), [["f.disciplina", "MECANICA"], ["f.status", "EMITIDO"]]);
});

test("remover um filtro individual mantém os outros", () => {
  const f = { disciplina: ["MECANICA", "CIVIL"], status: ["EMITIDO"] };
  assert.deepEqual(removerFiltro(f, "disciplina", "CIVIL"), { disciplina: ["MECANICA"], status: ["EMITIDO"] });
  assert.deepEqual(removerFiltro({ status: ["EMITIDO"] }, "status", "EMITIDO"), {});
});

test("serialização estável independe da ordem de clique", () => {
  const a = alternarFiltro(alternarFiltro({}, "status", "B"), "disciplina", "A");
  const b = alternarFiltro(alternarFiltro({}, "disciplina", "A"), "status", "B");
  assert.deepEqual(serializarFiltros(a), serializarFiltros(b));
});

test("não altera o objeto original (estado imutável)", () => {
  const original = { disciplina: ["MECANICA"] };
  alternarFiltro(original, "disciplina", "CIVIL");
  assert.deepEqual(original, { disciplina: ["MECANICA"] });
});
