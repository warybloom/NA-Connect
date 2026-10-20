import { test } from "node:test";
import assert from "node:assert/strict";
import { __definirFetch, custosApi, ehPorDisciplina } from "../costsApi";
import { assinaturaEntrada, linhasDoCadastro, montarPacoteDisciplina, novaLinhaDisciplina } from "../pacoteDisciplinaForm";

const cab = { nome: "Pacote Everton", alias: "Everton", empresa: "", descricao: "" };

test("formulário Por Disciplina monta o contrato do backend sem calcular rateio", () => {
  const linha = { ...novaLinhaDisciplina("Processo"), hh: "1.000", custo: "R$ 150.000,00", justificativa: " Proposta revisada " };
  const r = montarPacoteDisciplina(cab, [linha]);
  assert.ok("input" in r);
  assert.deepEqual(r.input, {
    nomeReferencia: "Pacote Everton", aliasTimesheet: "Everton", empresa: null, descricao: null,
    disciplinas: [{ disciplina: "Processo", hhPrevisto: 1000, custoPrevisto: 150000, abrangencia: "TODOS", documentos: null, justificativa: "Proposta revisada" }],
  });
});

test("abrangência SELECIONADOS exige documentos e só envia a lista nesse modo", () => {
  const sem = montarPacoteDisciplina(cab, [{ ...novaLinhaDisciplina("Processo"), hh: "10", abrangencia: "SELECIONADOS" }]);
  assert.ok("erros" in sem);
  const com = montarPacoteDisciplina(cab, [{ ...novaLinhaDisciplina("Processo"), hh: "10", abrangencia: "SELECIONADOS", documentos: ["A", "B"] }]);
  assert.ok("input" in com);
  assert.deepEqual(com.input.disciplinas[0].documentos, ["A", "B"]);
});

test("erros de digitação ficam no formulário", () => {
  const r = montarPacoteDisciplina({ ...cab, nome: " " }, [{ ...novaLinhaDisciplina(""), hh: "abc" }]);
  assert.ok("erros" in r);
  assert.equal(r.erros.length, 3);
});

test("qualquer alteração invalida a simulação anterior", () => {
  const a = montarPacoteDisciplina(cab, [{ ...novaLinhaDisciplina("Processo"), hh: "1000" }]);
  const b = montarPacoteDisciplina(cab, [{ ...novaLinhaDisciplina("Processo"), hh: "1001" }]);
  assert.ok("input" in a && "input" in b);
  assert.notEqual(assinaturaEntrada(a.input), assinaturaEntrada(b.input));
});

test("edição parte do cadastro salvo", () => {
  const linhas = linhasDoCadastro([{ id: 1, disciplina: "Processo", abrangencia: "SELECIONADOS", hhPrevisto: 1000.5, custoPrevisto: 0, hhPrevistoLdp: 950, custoPrevistoLdp: null, documentosLdp: 10, justificativa: null, documentos: ["X"], atualizadoEm: "" }]);
  assert.equal(linhas[0].hh, "1000,5");
  assert.deepEqual(linhas[0].documentos, ["X"]);
});

test("pacotes antigos sem modeloCadastro continuam Por Documento", () => {
  assert.equal(ehPorDisciplina({}), false);
  assert.equal(ehPorDisciplina({ modeloCadastro: "DISCIPLINA" }), true);
});

test("rotas Por Disciplina usam /api/project-costs/packages e /import/discipline", async () => {
  const chamadas: string[] = [];
  __definirFetch(async (url, init) => { chamadas.push(`${init?.method ?? "GET"} ${url.split("?")[0]}`); return new Response("{}", { status: 200 }); });
  const p = "b.11111111-1111-1111-1111-111111111111";
  const input = { nomeReferencia: "X", aliasTimesheet: null, empresa: null, descricao: null, disciplinas: [] };
  await custosApi.suporteModelo(p); await custosApi.disciplinasLdp(p); await custosApi.simularPacoteDisciplina(p, input);
  await custosApi.criarPacoteDisciplina(p, input); await custosApi.atualizarPacoteDisciplina(p, 9, input); await custosApi.detalhePacoteDisciplina(p, 9);
  assert.deepEqual(chamadas, [
    "GET /api/project-costs/packages/model-support", "GET /api/project-costs/packages/ldp-disciplines", "POST /api/project-costs/packages/discipline/preview",
    "POST /api/project-costs/packages/discipline", "PUT /api/project-costs/packages/9/discipline", "GET /api/project-costs/packages/9/disciplines",
  ]);
});
