import { test } from "node:test";
import assert from "node:assert/strict";
import { aplicarValidacao, caminhoDaPasta, contagem, prontosDoLote, type ArquivoNaFila } from "../uploadNaPasta";
import type { Validacao } from "../uploadGovernadoApi";

const arquivo = (name: string) => ({ name, size: 10, type: "application/pdf" }) as unknown as File;
const v = (arquivoNome: string, status: Validacao["status"]): Validacao => ({
  arquivo: arquivoNome, status, mensagem: "msg técnica", documento: status === "PRONTO" ? "CHS001-PR-001" : null, campoIdentificador: null,
  idRegistroLdp: null, disciplina: null, revisao: null, folderId: null, pasta: null,
});

test("Lote misto: pré-valida todos; só os válidos sobem automaticamente e o fora da LDP não impede os demais", () => {
  const fila: ArquivoNaFila[] = ["a01.pdf", "a02.pdf", "a03.pdf", "a04.pdf"].map((n, i) => ({ chave: `k${i}`, arquivo: arquivo(n), estado: "VALIDANDO" }));
  const resultados = [v("a01.pdf", "PRONTO"), v("a02.pdf", "PRONTO"), v("a03.pdf", "FORA_DA_LDP"), v("a04.pdf", "PRONTO")];
  const r = aplicarValidacao(fila, ["k0", "k1", "k2", "k3"], resultados);
  assert.deepEqual(r.map((a) => a.estado), ["PRONTO", "PRONTO", "BLOQUEADO", "PRONTO"]);
  assert.deepEqual(prontosDoLote(fila, resultados).map((a) => a.arquivo.name), ["a01.pdf", "a02.pdf", "a04.pdf"]);
  assert.match(r[2].mensagem ?? "", /não existe na LDP atual deste projeto/);
  assert.deepEqual(contagem(r), { total: 4, validando: 0, prontos: 3, bloqueados: 1, enviando: 0, enviados: 0, erros: 0 });
});

test("Pasta fora das árvores protegidas continua permitida (regra atual preservada)", () => {
  const fila: ArquivoNaFila[] = [{ chave: "k", arquivo: arquivo("qualquer.pdf"), estado: "VALIDANDO" }];
  const r = aplicarValidacao(fila, ["k"], [v("qualquer.pdf", "PASTA_NAO_PROTEGIDA")]);
  assert.equal(r[0].estado, "PRONTO");
  assert.equal(prontosDoLote(fila, [v("qualquer.pdf", "PASTA_NAO_PROTEGIDA")]).length, 1);
});

test("Validação só altera os arquivos daquele lote", () => {
  const fila: ArquivoNaFila[] = [{ chave: "velho", arquivo: arquivo("x.pdf"), estado: "ENVIADO" }, { chave: "novo", arquivo: arquivo("y.pdf"), estado: "VALIDANDO" }];
  const r = aplicarValidacao(fila, ["novo"], [v("y.pdf", "NOME_INVALIDO")]);
  assert.deepEqual(r.map((a) => a.estado), ["ENVIADO", "BLOQUEADO"]);
});

test("Caminho da pasta pelo breadcrumb", () => {
  assert.equal(caminhoDaPasta([{ name: "Project Files" }, { name: "06 - ENGINEERING" }, { name: "06.01 - PROCESS" }]), "Project Files / 06 - ENGINEERING / 06.01 - PROCESS");
});

import { trilhaVisivel } from "../../../pages/cde/pastasDoProjeto";

test("Breadcrumb: começa em Project Files, sem duplicar e sem a raiz técnica; IDs preservados", () => {
  const acc = [{ id: "raiz", name: "b.123-abc" }, { id: "pf", name: "Project Files" }, { id: "06", name: "06 - ENGINEERING" }, { id: "0601", name: "06.01 - PROCESS" }];
  assert.deepEqual(trilhaVisivel(acc).map((p) => `${p.id}:${p.name}`), ["pf:Project Files", "06:06 - ENGINEERING", "0601:06.01 - PROCESS"]);
  assert.deepEqual(trilhaVisivel([{ id: "pf", name: "Project Files" }]).map((p) => p.name), ["Project Files"]);
  assert.deepEqual(trilhaVisivel([{ id: "raiz", name: "b.123-abc" }, { id: "06", name: "06 - ENGINEERING" }]).map((p) => `${p.id}:${p.name}`),
    ["raiz:Project Files", "06:06 - ENGINEERING"]);
  assert.deepEqual(trilhaVisivel([]), []);
});
