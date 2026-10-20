import { test } from "node:test";
import assert from "node:assert/strict";
import { problemaArquivo, resumoAplicacao, rotuloAplicar } from "../logosEmLote";

test("Arquivo inválido: formato e tamanho", () => {
  assert.equal(problemaArquivo({ name: "a.png", type: "image/png", size: 10 }), null);
  assert.ok(problemaArquivo({ name: "a.gif", type: "image/gif", size: 10 }));
  assert.ok(problemaArquivo({ name: "a.png", type: "image/png", size: 2_000_000 }));
  assert.ok(problemaArquivo({ name: "a.png", type: "image/png", size: 0 }));
});

test("Resumo: uma logo para todos os selecionados, separando os que terão a logo substituída", () => {
  const comLogo = new Set(["p3", "p5", "p9"]);
  const sel = ["p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8", "p9"];
  assert.deepEqual(resumoAplicacao(sel, (id) => comLogo.has(id)), { total: 9, semLogo: 6, substituir: 3 });
  assert.deepEqual(resumoAplicacao([], () => true), { total: 0, semLogo: 0, substituir: 0 });
});

test("Rótulo do botão reflete a quantidade de projetos, não de arquivos", () => {
  assert.equal(rotuloAplicar(9), "Aplicar logo aos 9 projetos");
  assert.equal(rotuloAplicar(1), "Aplicar logo ao projeto");
});
