import { test } from "node:test";
import assert from "node:assert/strict";
import { priorizarProjetoAtual } from "../priorizarProjetoAtual";

const P = [{ id: "b.aaa", n: "AAA" }, { id: "b.chs", n: "CHS001" }, { id: "b.zzz", n: "ZZZ" }];

test("Projeto atual vem primeiro; demais continuam na ordem original", () => {
  assert.deepEqual(priorizarProjetoAtual(P, "b.chs", (p) => p.id).map((p) => p.n), ["CHS001", "AAA", "ZZZ"]);
  assert.deepEqual(priorizarProjetoAtual(P, "chs", (p) => p.id).map((p) => p.n), ["CHS001", "AAA", "ZZZ"]); // sem prefixo b.
});

test("Sem projeto atual (ou fora da lista/filtro): ordem inalterada", () => {
  assert.deepEqual(priorizarProjetoAtual(P, null, (p) => p.id).map((p) => p.n), ["AAA", "CHS001", "ZZZ"]);
  assert.deepEqual(priorizarProjetoAtual(P, "b.outro", (p) => p.id).map((p) => p.n), ["AAA", "CHS001", "ZZZ"]);
});
