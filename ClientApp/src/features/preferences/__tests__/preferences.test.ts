import { test } from "node:test";
import assert from "node:assert/strict";
import { ehObjeto, ehUmDe, gravarPreferencia, lerPreferencia, montarChave, removerPreferencia, type ArmazenamentoPreferencias } from "../userPreferences";
import { colunasHome, itensPorPaginaHome } from "../../../pages/homeLayout";
import { GRADE_PADRAO, sanearGrade } from "../../../pages/cde/CdeDocumentList";

const memoria = (): ArmazenamentoPreferencias & { dados: Map<string, string> } => {
  const dados = new Map<string, string>();
  return { dados, getItem: (k) => dados.get(k) ?? null, setItem: (k, v) => { dados.set(k, v); }, removeItem: (k) => { dados.delete(k); } };
};

test("Chave organizada: usuário / projeto (ou global) / tela / configuração", () => {
  assert.equal(montarChave({ usuario: "Ana@NA.com", tela: "home", config: "viewMode" }), "na:pref:v1/ana@na.com/global/home/viewmode");
  assert.equal(montarChave({ usuario: "ana@na.com", projeto: "b.123", tela: "lista-documentos", config: "grid" }), "na:pref:v1/ana@na.com/b.123/lista-documentos/grid");
  assert.equal(montarChave({ usuario: null, tela: "home", config: "viewMode" }), "na:pref:v1/anonimo/global/home/viewmode");
  // "/" no valor nunca cria níveis extras de chave
  assert.equal(montarChave({ usuario: "a/b", tela: "x/y", config: "c" }).split("/").length, 5);
});

test("Gravar → fechar → abrir: preferência volta igual; outro usuário/projeto não enxerga", () => {
  const s = memoria();
  const chave = { usuario: "ana@na.com", projeto: "b.1", tela: "lista-documentos", config: "grid" };
  gravarPreferencia(chave, { order: ["titulo", "codigo"], sizing: { titulo: 340 } }, s);
  assert.deepEqual(lerPreferencia(chave, null, undefined, s), { order: ["titulo", "codigo"], sizing: { titulo: 340 } });
  assert.equal(lerPreferencia({ ...chave, usuario: "bia@na.com" }, "padrao", undefined, s), "padrao");
  assert.equal(lerPreferencia({ ...chave, projeto: "b.2" }, "padrao", undefined, s), "padrao");
  removerPreferencia(chave, s);
  assert.equal(lerPreferencia(chave, "padrao", undefined, s), "padrao");
});

test("Preferência corrompida/inválida volta ao padrão; armazenamento indisponível não quebra", () => {
  const s = memoria();
  const chave = { usuario: "ana@na.com", tela: "home", config: "viewMode" };
  s.dados.set(montarChave(chave), "{quebrado");
  assert.equal(lerPreferencia(chave, "cards", ehUmDe(["cards", "lista"] as const), s), "cards");
  gravarPreferencia(chave, "tabela", s);
  assert.equal(lerPreferencia(chave, "cards", ehUmDe(["cards", "lista"] as const), s), "cards");
  gravarPreferencia(chave, "lista", s);
  assert.equal(lerPreferencia(chave, "cards", ehUmDe(["cards", "lista"] as const), s), "lista");
  const quebrado: ArmazenamentoPreferencias = { getItem: () => { throw new Error("x"); }, setItem: () => { throw new Error("x"); }, removeItem: () => {} };
  assert.equal(lerPreferencia(chave, "cards", undefined, quebrado), "cards");
  assert.doesNotThrow(() => gravarPreferencia(chave, "lista", quebrado));
  assert.equal(ehObjeto()([1]), false);
});

test("Home: 6 colunas × 5 fileiras em tela larga; menos colunas em telas menores; lista compacta", () => {
  assert.deepEqual([1920, 1536, 1366, 1100, 800, 400].map(colunasHome), [6, 6, 5, 4, 3, 2]);
  assert.equal(itensPorPaginaHome("cards", colunasHome(1920)), 30);
  assert.equal(itensPorPaginaHome("cards", colunasHome(1100)), 20);
  assert.equal(itensPorPaginaHome("lista", 6), 50);
});

test("Lista de Documentos: grade salva (ordem, largura, visibilidade, ordenação, itens) volta saneada", () => {
  const salva = { order: ["titulo", "codigo"], sizing: { titulo: 400 }, visibility: { hh: false }, pageSize: 100, sort: { key: "titulo", dir: "desc" as const } };
  assert.deepEqual(sanearGrade(JSON.parse(JSON.stringify(salva))), salva);
  assert.deepEqual(sanearGrade(null), GRADE_PADRAO);
  assert.deepEqual(sanearGrade({ order: "x" as never, pageSize: 999, sort: { key: 1 as never, dir: "asc" } }), GRADE_PADRAO);
});
