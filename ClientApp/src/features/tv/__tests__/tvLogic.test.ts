import { test } from "node:test";
import assert from "node:assert/strict";
import { escolherApresentacao, gerarIdApresentacao, mesmasApresentacoes, mover, proximoIndice, resumoTv, tvAtiva, validarApresentacoes, type TvPresentation } from "../tvLogic";

const HUB = "b.11111111-1111-1111-1111-111111111111";
const PROJ = "b.22222222-2222-2222-2222-222222222222";
const TELAS = ["ldp", "hold", "rnc"];
const ap = (p: Partial<TvPresentation> = {}): TvPresentation => ({
  id: "sala", name: "Sala", secondsPerScreen: 30,
  slides: [{ screenId: "ldp", hubId: HUB, projectId: PROJ, projectName: "CHS001" }], ...p,
});

test("TV: configuração válida passa; mesmas regras do backend recusam o inválido", () => {
  assert.equal(validarApresentacoes([ap()], TELAS), null);
  assert.equal(validarApresentacoes([], TELAS), null);
  assert.match(validarApresentacoes([ap({ name: "  " })], TELAS)!, /Nome da Tela/);
  assert.match(validarApresentacoes([ap({ secondsPerScreen: 5 })], TELAS)!, /entre 10 e 600/);
  assert.match(validarApresentacoes([ap({ secondsPerScreen: 601 })], TELAS)!, /entre 10 e 600/);
  assert.match(validarApresentacoes([ap({ slides: [] })], TELAS)!, /ao menos uma tela/);
  assert.match(validarApresentacoes([ap({ slides: [{ screenId: "controle-hh-custo", hubId: HUB, projectId: PROJ, projectName: null }] })], TELAS)!, /não pode ser exibida/);
  assert.match(validarApresentacoes([ap({ slides: [{ screenId: "ldp", hubId: HUB, projectId: "", projectName: null }] })], TELAS)!, /projeto válido/);
  assert.match(validarApresentacoes([ap(), ap()], TELAS)!, /duplicada/);
  assert.match(validarApresentacoes([ap({ id: "ID Inválido" })], TELAS)!, /Identificador/);
});

test("TV: id gerado do nome, sem acento, único", () => {
  assert.equal(gerarIdApresentacao("Sala de Reunião 2", []), "sala-de-reuniao-2");
  assert.equal(gerarIdApresentacao("Sala", ["sala", "sala-2"]), "sala-3");
  assert.equal(gerarIdApresentacao("!!!", []), "apresentacao");
});

test("TV: reordenar, detectar alteração, rodízio e apresentação escolhida", () => {
  assert.deepEqual(mover(["a", "b", "c"], 0, 2), ["b", "c", "a"]);
  assert.deepEqual(mover(["a", "b"], 0, 5), ["a", "b"]);
  assert.equal(mesmasApresentacoes([ap()], [ap()]), true);
  assert.equal(mesmasApresentacoes([ap()], [ap({ secondsPerScreen: 45 })]), false);
  assert.equal(proximoIndice(2, 3), 0);
  assert.equal(proximoIndice(0, 3, -1), 2);
  assert.equal(proximoIndice(0, 0), 0);
  const lista = [ap({ id: "a" }), ap({ id: "b" })];
  assert.equal(escolherApresentacao(lista, "b")?.id, "b");
  assert.equal(escolherApresentacao(lista, "x")?.id, "a");
  assert.equal(escolherApresentacao([], null), undefined);
});

test("TV: listagem identifica nome, projeto (ou MULTIPROJETO), conteúdo e status; arquivo antigo = ativa", () => {
  const rot = (id: string) => ({ ldp: "LDP", hold: "HOLD", rnc: "RNC" } as Record<string, string>)[id] ?? id;
  const eng = ap({ name: "TV SALA DE ENGENHARIA" });
  assert.deepEqual(resumoTv(eng, rot), { projeto: "CHS001", conteudo: "LDP", status: "ATIVA" });
  const dir = ap({ name: "TV DIRETORIA", ativa: false, slides: [
    { screenId: "ldp", hubId: HUB, projectId: PROJ, projectName: "CHS001" },
    { screenId: "hold", hubId: HUB, projectId: "b.33333333-3333-3333-3333-333333333333", projectName: "BML011" }] });
  assert.deepEqual(resumoTv(dir, rot), { projeto: "MULTIPROJETO", conteudo: "LDP · HOLD", status: "INATIVA" });
  assert.equal(tvAtiva({}), true);                          // sem o campo (arquivo antigo) = ativa
  assert.notEqual(eng.name, dir.name);
});

test("TV sem Autodesk: hubId vazio é aceito, Project_ID_ACC continua obrigatório; hub antigo inválido é recusado", () => {
  assert.equal(validarApresentacoes([ap({ slides: [{ screenId: "ldp", hubId: "", projectId: PROJ, projectName: "BML011" }] })], TELAS), null);
  assert.match(validarApresentacoes([ap({ slides: [{ screenId: "ldp", hubId: "", projectId: "", projectName: null }] })], TELAS)!, /projeto válido/);
  assert.match(validarApresentacoes([ap({ slides: [{ screenId: "ldp", hubId: "nao-e-hub", projectId: PROJ, projectName: null }] })], TELAS)!, /projeto válido/);
  assert.equal(validarApresentacoes([ap()], TELAS), null); // configuração antiga com hub continua válida
});
