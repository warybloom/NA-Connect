import { test } from "node:test";
import assert from "node:assert/strict";
import { analiseAoTrocarPrincipal, normalizarAnalise, paraSelecao, projetosPermitidos } from "../projetoAnalise";
import { separarCodigo, textoFallbackLogo, urlLogoProjeto } from "../ProjectIdentity";

const H = "b.hub";
const CHS = { hubId: H, projectId: "b.chs", nome: "CHS001 - ETHANOL" };
const BML = { hubId: H, projectId: "b.bml", nome: "BML011.016 - PLANTA" };
const BYD = { hubId: H, projectId: "b.byd", nome: "BYD001 - FÁBRICA" };

test("Padrão: análise = [principal]; seleção vazia nunca fica vazia; sem duplicados", () => {
  assert.deepEqual(normalizarAnalise(CHS, []), [CHS]);
  assert.deepEqual(normalizarAnalise(CHS, [CHS, BML, BML]), [CHS, BML]);
  assert.deepEqual(normalizarAnalise(null, [BML]), []);
});

test("Entrar em NOVO projeto pela Home: vira principal e a análise antiga é descartada", () => {
  assert.deepEqual(analiseAoTrocarPrincipal(CHS, BYD, [CHS, BML]), [BYD]);
  assert.deepEqual(analiseAoTrocarPrincipal(null, CHS, []), [CHS]);
  // mesmo projeto (só completou o nome): mantém a análise
  assert.deepEqual(analiseAoTrocarPrincipal(CHS, { ...CHS, nome: "CHS001 - NOVO" }, [CHS, BML]).map((p) => p.projectId), ["b.chs", "b.bml"]);
});

test("Multiprojeto: telas analíticas recebem todos; seletor só oferece projetos com permissão", () => {
  assert.deepEqual(paraSelecao([CHS, BML]), [{ id: "b.chs", hubId: H, name: "CHS001 - ETHANOL" }, { id: "b.bml", hubId: H, name: "BML011.016 - PLANTA" }]);
  const cands = [{ id: "b.chs" }, { id: "b.bml" }, { id: "b.byd" }];
  assert.deepEqual(projetosPermitidos(cands, new Map([["b.chs", true], ["b.bml", true], ["b.byd", false]])).map((c) => c.id), ["b.chs", "b.bml"]);
  assert.deepEqual(projetosPermitidos(cands, new Map([["b.chs", true]])).map((c) => c.id), ["b.chs"]); // sem resposta = fora
  assert.equal(projetosPermitidos(cands, null).length, 3);                                              // safe mode
});

test("Logo do Projeto: uma URL oficial versionada; fallback com o código", () => {
  assert.equal(urlLogoProjeto("b.chs", 3), "/api/project-identity/b.chs/logo?v=3");
  assert.equal(textoFallbackLogo("BML011.016 - PLANTA"), "BML011.016");
  assert.equal(textoFallbackLogo("Projeto sem código"), "PRO");
  assert.deepEqual(separarCodigo("CHS001 - ETHANOL PLANT"), { codigo: "CHS001", titulo: "ETHANOL PLANT" });
});
