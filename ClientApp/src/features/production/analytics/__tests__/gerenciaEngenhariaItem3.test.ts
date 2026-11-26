import test from "node:test";
import assert from "node:assert/strict";
import { criarIndiceTitulosDocumentos, obterTituloDocumentoOficial } from "../item3/documentTitleIndex";

const report = (projectId: string, documentos: { codigo: string | null; titulo: string | null }[]) => ({
  projectId,
  documentos: documentos.map((d, i) => ({ ...d, rowId: String(i) })),
});

test("resolve o título oficial somente por projeto e número normalizado", () => {
  const indice = criarIndiceTitulosDocumentos([
    report("P1", [{ codigo: " chs-001 ", titulo: "Memória de cálculo" }]),
    report("P2", [{ codigo: "CHS-001", titulo: "Folha de dados" }]),
  ] as never);
  assert.equal(obterTituloDocumentoOficial(indice, "P1", "CHS-001"), "Memória de cálculo");
  assert.equal(obterTituloDocumentoOficial(indice, "P2", "chs-001"), "Folha de dados");
  assert.equal(obterTituloDocumentoOficial(indice, "P3", "CHS-001"), null);
});

test("não escolhe título quando a mesma chave oficial é ambígua", () => {
  const indice = criarIndiceTitulosDocumentos([
    report("P1", [
      { codigo: "DOC-1", titulo: "Título A" },
      { codigo: "DOC-1", titulo: "Título B" },
    ]),
  ] as never);
  assert.equal(obterTituloDocumentoOficial(indice, "P1", "DOC-1"), null);
});

test("ignora código ou título ausente sem criar dado artificial", () => {
  const indice = criarIndiceTitulosDocumentos([
    report("P1", [{ codigo: null, titulo: "Sem código" }, { codigo: "DOC-2", titulo: " " }]),
  ] as never);
  assert.equal(indice.size, 0);
});
