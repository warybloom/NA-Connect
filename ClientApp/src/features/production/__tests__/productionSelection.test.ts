import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { ME_PROJECTS_ENDPOINT, montarSelecao, projetoInicial, projetosInternos, type MeProject } from "../productionSelectionLogic";

const BML: MeProject = { projetoId: 7, projectIdAcc: "b.bml011", nomeProjeto: "BML011 - Projeto", tipoLdp: 2, isAtivo: true };
const OUTRO: MeProject = { projetoId: 8, projectIdAcc: "b.outro", nomeProjeto: "Outro", tipoLdp: 1, isAtivo: true };

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};

test("A: projeto atual (Project_ID_ACC) + hubId vazio → reconhecido e selecionado", () => {
  const lista = projetosInternos([BML, OUTRO]);
  const id = projetoInicial(lista, "b.bml011");
  assert.equal(id, "b.bml011");
  assert.deepEqual(montarSelecao(lista, id, ""), [{ id: "b.bml011", hubId: "", name: "BML011 - Projeto" }]);
});

test("B: usuário N&A sem Autodesk — seletor mostra os projetos internos (/api/me/projects)", () => {
  const lista = projetosInternos([OUTRO, BML, { ...BML, projectIdAcc: "b.inativo", isAtivo: false }]);
  assert.deepEqual(lista.map((p) => [p.id, p.attributes.name]), [["b.bml011", "BML011 - Projeto"], ["b.outro", "Outro"]]);
  assert.equal(ME_PROJECTS_ENDPOINT, "/api/me/projects");
});

test("B2: um único projeto autorizado → já selecionado; projeto sem acesso nunca fica selecionado", () => {
  assert.equal(projetoInicial(projetosInternos([BML]), null), "b.bml011");
  assert.equal(projetoInicial(projetosInternos([BML, OUTRO]), "b.sem-acesso"), "");
  assert.equal(projetoInicial(projetosInternos([BML, OUTRO]), null), "");
});

test("C: lista do Controle de Produção não usa Hubs/Projetos Autodesk", () => {
  const seletor = fonte("src/features/production/ProductionProjectSelector.tsx");
  const inicio = seletor.indexOf("export function useProductionSelectionInterna");
  const fim = seletor.indexOf("type Selecao", inicio);
  const hook = seletor.slice(inicio, fim);
  assert.ok(inicio > 0 && fim > inicio);
  assert.match(hook, /ME_PROJECTS_ENDPOINT/);
  assert.ok(!hook.includes("/DataManagement/Data/Projetos"), "a lista não vem da Autodesk");
  const pagina = fonte("src/features/production/ControleProducaoPage.tsx");
  assert.match(pagina, /useProductionSelectionInterna\(/);
  assert.ok(!/useProductionSelection\(/.test(pagina));
});

test("D: hubId vazio não esvazia a seleção (relatórios SQL usam Project_ID_ACC)", () => {
  const sel = montarSelecao(projetosInternos([BML]), "b.bml011", "");
  assert.equal(sel.length, 1);
  assert.equal(sel[0].id, "b.bml011");
});

test("E: sem a permission key da tela no projeto → Acesso negado (sem bypass; backend continua validando)", () => {
  const pagina = fonte("src/features/production/ControleProducaoPage.tsx");
  const gate = pagina.indexOf("!perm.can(folha.permissionKeys)");
  const primeiraTela = pagina.indexOf("<ConteudoPaginaProducao"); // resolvedor único das telas (ProductionPageContent.tsx)
  assert.ok(gate > 0 && primeiraTela > gate, "a checagem de permissão vem antes de qualquer tela com dados");
  assert.match(pagina, /useProjectPermissions\(s\.projectId \|\| null\)/);
});

test("D: Controle de Produção não chama /DataManagement/Data/Hubs (nem Projetos) — seletor e análise multiprojeto", () => {
  const seletor = fonte("src/features/production/ProductionProjectSelector.tsx");
  const inicio = seletor.indexOf("export function useProductionSelectionInterna");
  const hook = seletor.slice(inicio, seletor.indexOf("type Selecao", inicio));
  assert.ok(!hook.includes("/DataManagement/"), "hook interno sem Autodesk");
  const analise = fonte("src/features/project/SeletorProjetosAnalise.tsx");
  assert.ok(!analise.includes("/DataManagement/"), "candidatos da análise vêm de /api/me/projects");
  assert.match(analise, /ME_PROJECTS_ENDPOINT/);
  for (const arquivo of ["src/features/production/ControleProducaoPage.tsx", "src/components/pmo/ViewCurvaS.tsx", "src/components/pmo/ViewTimesheet.tsx", "src/pages/cde/CdeDocumentList.tsx"])
    assert.ok(!fonte(arquivo).includes("/DataManagement/Data/Hubs"), arquivo);
});

test("C: projeto atual com hubId vazio gera seleção válida (projectId = Project_ID_ACC, hubId vazio permitido)", () => {
  const [p] = montarSelecao(projetosInternos([BML]), projetoInicial(projetosInternos([BML]), "b.bml011"), "");
  assert.equal(p.id, "b.bml011");
  assert.equal(p.hubId, "");
  const q = new URLSearchParams({ hubId: p.hubId, projectId: p.id });
  assert.equal(q.get("projectId"), "b.bml011");
});

test("TV e Identidade Visual: projetos de /api/me/projects, sem Hubs/Projetos Autodesk", () => {
  for (const arquivo of ["src/features/settings/TelasTvConfigPage.tsx", "src/features/project/IdentidadeVisualPage.tsx"]) {
    const texto = fonte(arquivo);
    assert.match(texto, /useProductionSelectionInterna\(/, arquivo);
    assert.ok(!/useProductionSelection\(/.test(texto), arquivo);
    assert.ok(!texto.includes("/DataManagement/"), arquivo);
  }
});

test("H: projeto fora dos UserProjects nunca é selecionado (nem vindo do projeto atual/URL)", () => {
  assert.equal(projetoInicial(projetosInternos([OUTRO]), "b.bml011"), "b.outro"); // único autorizado
  assert.equal(projetoInicial(projetosInternos([]), "b.bml011"), "");
  assert.deepEqual(montarSelecao(projetosInternos([]), "b.bml011", ""), []);
});

test("TV (bug 1): lista vem da fonte administrativa interna /api/tv-config/projects, sem Autodesk, AccessToken nem registro /api/admin/projetos", () => {
  const pagina = fonte("src/features/settings/TelasTvConfigPage.tsx");
  assert.match(pagina, /useProductionSelectionInterna\([^)]*\}, TV_PROJECTS_ENDPOINT\)/);
  assert.ok(!pagina.includes("/DataManagement/"));
  const permitidos = fonte("src/features/tv/useProjetosPermitidosTv.ts");
  assert.ok(!permitidos.includes("getProjetosRegistrados"), "não depende de /api/admin/projetos (exige AccessToken)");
  assert.ok(!permitidos.includes("/DataManagement/"));
  const logica = fonte("src/features/production/productionSelectionLogic.ts");
  assert.match(logica, /TV_PROJECTS_ENDPOINT = "\/api\/tv-config\/projects"/);
});
