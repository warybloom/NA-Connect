import { test } from "node:test";
import assert from "node:assert/strict";
import { rotuloItem } from "../admin/BulkPreviewView";

const item = (p: Partial<Parameters<typeof rotuloItem>[0]>) =>
  ({ status: "ALTERAR", vinculo: null, perfisAdicionados: [], perfisRemovidos: [], ...p }) as Parameters<typeof rotuloItem>[0];

test("Lote: prévia classifica Adicionar (cria vínculo), Reativar, Adicionar perfil, Já possui e Ignorar", () => {
  assert.equal(rotuloItem(item({ vinculo: "CRIAR", perfisAdicionados: ["adm"] })).rotulo, "Adicionar");
  assert.equal(rotuloItem(item({ vinculo: "REATIVAR" })).rotulo, "Reativar");
  assert.equal(rotuloItem(item({ perfisAdicionados: ["adm"] })).rotulo, "Adicionar perfil");
  assert.equal(rotuloItem(item({ perfisAdicionados: ["adm"], perfisRemovidos: ["leitura"] })).rotulo, "Alterar");
  assert.equal(rotuloItem(item({ status: "JA_POSSUI" })).rotulo, "Já possui");
  assert.equal(rotuloItem(item({ status: "IGNORAR" })).rotulo, "Ignorar");
});
