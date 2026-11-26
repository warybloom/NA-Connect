import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { pesquisarProjetos, projetosAtivos, projetosDoSlide, resumoProjetosSlide, resumoSelecao, slideComProjetos, validarApresentacoes, type TvPresentation } from "../tvLogic";
import { TELAS_TV, chavesTelaTv, getTelaTv } from "../tvScreens";
import { projetosPermitidos } from "../../project/projetoAnalise";

const HUB = "b.11111111-1111-1111-1111-111111111111";
const P = (n: number, nome: string) => ({ hubId: HUB, projectId: `b.${String(n).repeat(8)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(12)}`, projectName: nome });
const CHS = P(2, "CHS001 - ETHANOL PLANT"), BML = P(3, "BML011.016 - COOLING TOWER"), BBF = P(4, "BBF002 - ETANOL"), ENR = P(5, "ENR004 - SUBESTAÇÃO"), X = P(6, "XYZ009 - OUTRO");
const TELAS = TELAS_TV.map((t) => t.id);
const tv = (slides: TvPresentation["slides"]): TvPresentation[] => [{ id: "tv", name: "TV DIRETORIA", secondsPerScreen: 30, slides }];

test("Catálogo: Resumo, Gestão à Vista, Curva S, Análise Financeira e os dashboards; todos com permissão da rota", () => {
  for (const id of ["resumo", "gestao-a-vista", "curva-s", "analise-financeira", "ldp"]) {
    assert.ok(getTelaTv(id), id);
    assert.ok(chavesTelaTv(id).length > 0, `keys de ${id}`);
  }
});

test("1 projeto + Resumo; 3 projetos + Resumo; 5 projetos + Curva S; 3 + Gestão à Vista; vários + Análise Financeira", () => {
  const slides = [
    slideComProjetos("resumo", [CHS]),
    slideComProjetos("resumo", [CHS, BBF, BML]),
    slideComProjetos("curva-s", [CHS, BBF, BML, ENR, X]),
    slideComProjetos("gestao-a-vista", [CHS, BBF, BML]),
    slideComProjetos("analise-financeira", [CHS, BML, BBF, ENR, X]),
    slideComProjetos("ldp", [CHS, BML]),                       // dashboards também aceitam vários projetos
  ];
  assert.equal(validarApresentacoes(tv(slides), TELAS), null);
  assert.deepEqual(slides.map((s) => projetosDoSlide(s).length), [1, 3, 5, 3, 5, 2]);
  assert.deepEqual(projetosDoSlide(slides[1]).map((p) => p.projectId), [CHS.projectId, BBF.projectId, BML.projectId]);
});

test("Mesma seleção em várias telas; depois outro conjunto — cada item guarda o seu (sem vazar)", () => {
  const sel1 = [CHS, BBF, BML], sel2 = [CHS];
  const slides = [slideComProjetos("resumo", sel1), slideComProjetos("curva-s", sel1), slideComProjetos("analise-financeira", sel1), slideComProjetos("curva-s", sel2)];
  assert.deepEqual(slides.map((s) => projetosDoSlide(s).length), [3, 3, 3, 1]);
  const player = readFileSync("src/features/tv/TvPlayerPage.tsx", "utf-8");
  assert.match(player, /selection=\{projetosDoSlide\(slide\)\.map/);
  assert.match(player, /key=\{`\$\{indice\}-\$\{slide\.screenId\}-\$\{projetosDoSlide\(slide\)/);
  assert.ok(!/useProjetoAtual/.test(player));                          // nunca o contexto do navegador
});

test("Salvar/reabrir e configuração antiga (só projectId) continuam funcionando", () => {
  const salvo = JSON.parse(JSON.stringify(tv([slideComProjetos("resumo", [CHS, BBF, BML])])));
  assert.equal(projetosDoSlide(salvo[0].slides[0]).length, 3);
  const antigo = { screenId: "ldp", hubId: HUB, projectId: CHS.projectId, projectName: CHS.projectName };
  assert.deepEqual(projetosDoSlide(antigo).map((p) => p.projectId), [CHS.projectId]);
  assert.equal(validarApresentacoes(tv([antigo]), TELAS), null);
});

test("Seletor: só projetos ATIVOS; pesquisa por código e por nome (sem acento/maiúscula)", () => {
  const lista = [CHS, BML, BBF, ENR, X].map((p) => ({ id: p.projectId, nome: p.projectName }));
  const ativos = projetosAtivos(lista, (id) => (id === X.projectId ? false : true));
  assert.ok(!ativos.some((p) => p.id === X.projectId));                // inativo não aparece
  assert.deepEqual(pesquisarProjetos(ativos, "chs").map((p) => p.nome), ["CHS001 - ETHANOL PLANT"]);
  assert.deepEqual(pesquisarProjetos(ativos, "etanol").map((p) => p.nome), ["BBF002 - ETANOL"]);
  assert.deepEqual(pesquisarProjetos(ativos, "subestacao").map((p) => p.nome), ["ENR004 - SUBESTAÇÃO"]);
  assert.equal(pesquisarProjetos(ativos, "").length, 4);
  assert.equal(projetosAtivos(lista, () => undefined).length, 0);      // não registrado no N&A = não aparece
});

test("Projeto sem permissão não aparece (mesmo mecanismo do seletor Analisar)", () => {
  const cands = [{ id: CHS.projectId }, { id: BML.projectId }];
  assert.deepEqual(projetosPermitidos(cands, new Map([[CHS.projectId, true], [BML.projectId, false]])).map((c) => c.id), [CHS.projectId]);
});

test("Sequência compacta: códigos, '+N projetos' e só a quantidade quando são muitos", () => {
  assert.equal(resumoProjetosSlide(slideComProjetos("curva-s", [CHS])).texto, "CHS001");
  assert.equal(resumoProjetosSlide(slideComProjetos("resumo", [CHS, BML, BBF, ENR, X])).texto, "CHS001 + BML011.016 + 3 projetos");
  const muitos = Array.from({ length: 15 }, (_, i) => P((i % 9) + 1, `PRJ${String(i).padStart(3, "0")} - X`))
    .map((p, i) => ({ ...p, projectId: `b.${String(i).padStart(8, "0")}-0000-0000-0000-000000000000` }));
  assert.equal(resumoProjetosSlide(slideComProjetos("analise-financeira", muitos)).texto, "15 projetos");
  assert.equal(resumoSelecao([{ id: "1", nome: "CHS001 - A" }, ...Array.from({ length: 14 }, (_, i) => ({ id: String(i + 2), nome: "X" }))]), "CHS001 + 14 projetos");
});

test("Configuração: fluxo PROJETOS → TELA → ADICIONAR com multiselect pesquisável (sem lista aberta de checkboxes)", () => {
  const src = readFileSync("src/features/settings/TelasTvConfigPage.tsx", "utf-8");
  assert.ok(src.indexOf("<SeletorProjetosTv") < src.indexOf('aria-label="Tela"'));
  assert.match(src, /Adicionar todas as telas/);
  assert.ok(!/Todas as telas deste projeto/.test(src));
  assert.ok(!/function ListaProjetos/.test(src));
});
