// BI CHS001 — fonte ISOLADA (bi_chs001 / /api/bi-chs001/schedule/*): alias auditável do gate 30%, carga com versão única,
// medidas 3D do 30% (TREATAS) × oráculo Python independente, matriz hierárquica Level 4–7 e lista de semanas (dSemana).
// Fixtures = cache do PBIX (só teste). Rodar: node _bi_chs001_replica/tools/testar.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { compilarFiltros, type Linha } from "../dados/filtrosPbi";
import { dadosDoVisual, semanasDSemana, type FonteCarregada } from "../dados/motor";
import { converterLinhas, prepararCronograma } from "../dados/entidades";
import { avisosDeFonte, infoFonte } from "../dados/avisos";
import { carregar } from "../dados/useDadosBi";
import { MEDIDAS } from "../dados/medidas";
import { noAbertoInicial } from "../pbi/VisuaisDados";
import type { NoMatriz } from "../dados/tipos";
import type { PaginaPbi } from "../pbi/modelo";

const clientApp = (() => {
  for (const b of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { readdirSync(path.join(b, "src/features/production/biChs001/generated")); return b; } catch { /* próximo */ }
  }
  throw new Error("ClientApp não encontrado");
})();
const gerado = (arq: string) => JSON.parse(readFileSync(path.join(clientApp, "src/features/production/biChs001/generated", arq), "utf8"));
const pagina = (arq: string) => gerado(`${arq}.json`) as PaginaPbi;
const fixture = (arq: string) => JSON.parse(readFileSync(path.join(clientApp, "..", "_bi_chs001_replica/fixtures_pbix", arq), "utf8"));
const ok = (linhas: Linha[]): FonteCarregada => ({ estado: "ok", linhas });
const FONTES = { "LDP (4)": ok(fixture("ldp4.json")), WBX: ok(fixture("wbx.json")), AssignmentByDay: ok(fixture("abd.json")) };
const contarNaPagina = (arq: string, linhas: Linha[]) => linhas.filter((l) => compilarFiltros(pagina(arq).filtros, "WBX").aplicar(l)).length;

// ------------------------------------------------------------------------------------------ alias do gate 30%

test("Gate 30%: valor bruto 'Emgineering - 30%' SEM alias → 0 / 543 / 386 (contamina 60/90 pela exclusão); COM o alias do BI → 196 / 347 / 190", () => {
  const wbx = fixture("wbx.json") as Linha[];
  const bruto = wbx.map((l) => (l["Level 4"] === "Engineering - 30%" ? { ...l, "Level 4": "Emgineering - 30%" } : l));
  assert.deepEqual([contarNaPagina("p03", bruto), contarNaPagina("p04", bruto), contarNaPagina("p05", bruto)], [0, 543, 386]);
  // O cronograma isolado entrega o rótulo do BI (bi_chs001.VW_Tarefa.Nome_BI) — mesmas contagens do PBIX.
  assert.deepEqual([contarNaPagina("p03", wbx), contarNaPagina("p04", wbx), contarNaPagina("p05", wbx)], [196, 347, 190]);
});

const ARVORE_BI = [ // linhas como /api/bi-chs001/schedule/tarefas devolve (TASK_NAME = rótulo do BI, TASK_NAME_Original = .mpp)
  { ID_Cronograma: 1, UID_Tarefa: 0, UID_Tarefa_Pai: null, TASK_NAME: "CHS001", TASK_NAME_Original: "CHS001", Eh_Tarefa_Resumo: true },
  { ID_Cronograma: 2, UID_Tarefa: 1, UID_Tarefa_Pai: 0, TASK_NAME: "CHS - ETHANOL PLANT", TASK_NAME_Original: "CHS - ETHANOL PLANT", Eh_Tarefa_Resumo: true },
  { ID_Cronograma: 3, UID_Tarefa: 3917, UID_Tarefa_Pai: 1, TASK_NAME: "Engineering - 30%", TASK_NAME_Original: "Emgineering - 30%", Alias_Id: 1, Eh_Tarefa_Resumo: true },
  { ID_Cronograma: 4, UID_Tarefa: 10, UID_Tarefa_Pai: 3917, TASK_NAME: "PFD", TASK_NAME_Original: "PFD", Eh_Tarefa_Resumo: false,
    Inicio: "2026-07-01T08:00:00", Termino: "2026-07-10T17:00:00", Inicio_Linha_Base: "2026-07-01T08:00:00", Termino_Linha_Base: "2026-07-08T17:00:00",
    Trabalho_Horas: 8, Trabalho_Linha_Base_Horas: 8, Trabalho_Real_Horas: 4 },
  { ID_Cronograma: 5, UID_Tarefa: 3948, UID_Tarefa_Pai: 1, TASK_NAME: "Engineering - 60%", TASK_NAME_Original: "Engineering - 60%", Eh_Tarefa_Resumo: true },
  { ID_Cronograma: 6, UID_Tarefa: 11, UID_Tarefa_Pai: 3948, TASK_NAME: "PDC", TASK_NAME_Original: "PDC", Eh_Tarefa_Resumo: false },
];

test("Alias auditável: Level 4 usa o rótulo do BI, 'Level 4 Original' preserva o valor do .mpp e a tela informa a correspondência", () => {
  const folhas = prepararCronograma(ARVORE_BI);
  assert.deepEqual(folhas.map((f) => [f.TASK_NAME, f.Level_4, f.Level_4_Original]),
    [["PFD", "Engineering - 30%", "Emgineering - 30%"], ["PDC", "Engineering - 60%", "Engineering - 60%"]]);
  const linhas = converterLinhas("WBX", ARVORE_BI);
  const fontes = { WBX: { estado: "ok" as const, linhas, versaoId: 1 } };
  assert.deepEqual(avisosDeFonte(pagina("p03"), fontes), []); // nenhum valor desconhecido do PBIX
  const info = infoFonte(fontes);
  assert.match(info[0], /BI CHS001 isolado \(bi_chs001\), versão 1/);
  assert.match(info[1], /"Emgineering - 30%" \(valor do \.mpp, preservado\) → "Engineering - 30%" em 1 tarefas/);
  // Prazo do 30% passa a ter valor (antes: BLANK com o valor bruto).
  const pg = pagina("p03");
  const d = dadosDoVisual(pg.visuais.find((v) => v.id.startsWith("6d9c8b46"))!, { pagina: pg, fontes, segmentacoes: {} });
  assert.ok(d.estado === "ok" && d.tipo === "valores");
  assert.deepEqual(Object.values(d.valores).map((x) => String(x).slice(0, 10)), ["2026-07-10", "2026-07-08"]);
});

test("Alias NÃO é inventado no frontend: outra grafia/legado continua literal e com aviso", () => {
  const legado = converterLinhas("WBX", ARVORE_BI.map(({ TASK_NAME_Original, ...r }) => ({ ...r, TASK_NAME: TASK_NAME_Original })));
  const fontes = { WBX: ok(legado) };
  assert.equal(legado[0]["Level 4"], "Emgineering - 30%");
  assert.match(avisosDeFonte(pagina("p03"), fontes)[0], /"Emgineering - 30%" \(1 tarefas\).*NÃO entram/);
  assert.match(infoFonte(fontes)[0], /fonte legada/);
});

// ------------------------------------------------------------------------------------------ carga com versão única

function simularFetch(rotas: Record<string, unknown>, chamadas: string[]) {
  globalThis.fetch = (async (url: string) => {
    chamadas.push(String(url));
    const chave = Object.keys(rotas).find((k) => String(url).startsWith(k));
    if (!chave) return new Response("<!doctype html>", { status: 404 });
    return new Response(JSON.stringify(rotas[chave]), { status: 200, headers: { "Content-Type": "application/json" } });
  }) as typeof fetch;
}

test("Carga: tarefas do BI isolado com versaoId → série pedida com ?versao= da MESMA versão", async () => {
  const chamadas: string[] = [];
  simularFetch({
    "/api/bi-chs001/schedule/tarefas": { linhas: ARVORE_BI, versaoId: 7 },
    "/api/bi-chs001/schedule/trabalho-diario": { linhas: [{ UID_Tarefa: 10, Data: "2026-07-06", Trabalho_Real_Horas: 4 }], versaoId: 7 },
  }, chamadas);
  const serie = await carregar("AssignmentByDay", "proj-versao");
  assert.equal(serie.estado, "ok");
  assert.ok(chamadas.some((c) => c.startsWith("/api/bi-chs001/schedule/trabalho-diario?projectId=proj-versao&versao=7")), chamadas.join(" | "));
  assert.deepEqual(serie.linhas, [{ TaskUID: "10", TimeByDay: "2026-07-05", "Trabalho Real": 4 }]);
  const wbx = await carregar("WBX", "proj-versao");
  assert.equal(wbx.versaoId, 7);
  assert.equal(wbx.origem, "/api/bi-chs001/schedule/tarefas");
});

test("Carga: BI isolado indisponível (ou rota ainda não publicada) → tarefas do legado; série NÃO é misturada (PENDENTE)", async () => {
  for (const [proj, rotaBi] of [["proj-indisp", { linhas: [], indisponivel: "Nenhuma versão" }], ["proj-404", undefined]] as const) {
    const chamadas: string[] = [];
    simularFetch({
      ...(rotaBi ? { "/api/bi-chs001/schedule/tarefas": rotaBi } : {}),
      "/api/bi-chs001/cronograma": { linhas: [{ ID_Cronograma: 1, UID_Tarefa: "5", EDT_WBS: "1.6", TASK_NAME: "Engineering - 60%", Eh_Tarefa_Resumo: false }] },
    }, chamadas);
    const wbx = await carregar("WBX", proj);
    assert.equal(wbx.estado, "ok", proj);
    assert.equal(wbx.origem, "/api/bi-chs001/cronograma", proj);
    assert.equal(wbx.versaoId, undefined, proj);
    const serie = await carregar("AssignmentByDay", proj);
    assert.match(serie.indisponivel ?? "", /só existe no cronograma isolado/, proj);
    assert.ok(!chamadas.some((c) => c.includes("trabalho-diario")), proj);
  }
});

// ------------------------------------------------------------------------------------------ 3D 30%

test("Filtro de página sobre OUTRA entidade (LDP FORMAT NOT IN) é neutro para WBX — não é invertido pelo Not", () => {
  for (const p of ["p08", "p09", "p10"]) {
    const f = compilarFiltros(pagina(p).filtros, "WBX");
    assert.equal(f.aplicar({ "Level 4": "Engineering - 30%" }), true, p);
    assert.ok(f.naoAvaliadas.some((n) => /outra entidade/.test(n.motivo)), p);
    // Para a própria LDP o filtro continua valendo: FORMAT A sai, 3D fica.
    const l = compilarFiltros(pagina(p).filtros, "LDP (4)");
    assert.equal(l.aplicar({ FORMAT: "A" }), false, p);
    assert.equal(l.aplicar({ FORMAT: "3D" }), true, p);
  }
});

test("LDP 3D 30%: KPI 30 | 3D | COLUNA/CURVA | % Realizado × oráculo Python independente (TREATAS de FORMAT = 3D)", () => {
  const esp = fixture("esperado_3d30.json") as { pontos: Record<string, number | string | null>[]; tarefas3D: number };
  assert.ok(esp.tarefas3D > 0);
  const pg = pagina("p08");
  const combo = pg.visuais.find((v) => v.id.startsWith("59602fe3"))!;
  const d = dadosDoVisual(combo, { pagina: pg, fontes: FONTES, segmentacoes: {} });
  assert.ok(d.estado === "ok" && d.tipo === "pontos", JSON.stringify(d).slice(0, 300));
  assert.deepEqual(d.pontos.map((p) => p.categoria), esp.pontos.map((p) => p.categoria));
  d.pontos.forEach((p, i) => {
    for (const k of ["3D | COLUNA | % Realizado", "3D | CURVA | % Realizado"]) {
      const motor = p.valores[`KPI.KPI 30 | ${k}`], oraculo = esp.pontos[i][k];
      if (oraculo === null) assert.equal(motor, null, `${p.categoria} ${k}`);
      else assert.ok(typeof motor === "number" && Math.abs(motor - (oraculo as number)) < 1e-9, `${p.categoria} ${k}: motor ${motor} × oráculo ${oraculo}`);
    }
  });
});

test("LDP 3D 30%: sem a LDP carregada a medida 3D aguarda; sem série fica PENDENTE com o motivo (nunca zero)", () => {
  const pg = pagina("p08");
  const combo = pg.visuais.find((v) => v.id.startsWith("59602fe3"))!;
  assert.equal(dadosDoVisual(combo, { pagina: pg, fontes: { WBX: FONTES.WBX, AssignmentByDay: FONTES.AssignmentByDay }, segmentacoes: {} }).estado, "carregando");
  const d = dadosDoVisual(combo, { pagina: pg, fontes: { ...FONTES, AssignmentByDay: { estado: "ok", linhas: [], indisponivel: "sem versão" } }, segmentacoes: {} });
  assert.equal(d.estado, "pendente");
  assert.ok(MEDIDAS["KPI.KPI 30 | 3D | CURVA | % Realizado"].requer?.documentos3D);
});

// ------------------------------------------------------------------------------------------ matriz

test("Matriz Resumo 30%: Level 4 → 5 → 6 → 7 pelo pai, Min(EDT) como no DAX, PRAZO (ALLSELECTED) repetido, expansão salva do PBIR", () => {
  const pg = pagina("p03");
  const v = pg.visuais.find((x) => x.type === "pivotTable")!;
  const d = dadosDoVisual(v, { pagina: pg, fontes: FONTES, segmentacoes: {} });
  assert.ok(d.estado === "ok" && d.tipo === "matriz", JSON.stringify(d).slice(0, 300));
  assert.deepEqual(d.nos.map((n) => n.caminho[0]), ["Engineering - 30%"]); // filtro da página
  const raiz = d.nos[0];
  const wbx = (fixture("wbx.json") as Linha[]).filter((l) => l["Level 4"] === "Engineering - 30%");
  assert.equal(raiz.filhos.length, new Set(wbx.map((l) => l["Level 5"])).size);
  // PRAZO | Término Baseline com ALLSELECTED: igual ao cartão (11/08) em todas as linhas.
  const tb = "KPI.KPI 30 | PRAZO | Término Baseline";
  assert.equal(String(raiz.valores[tb]).slice(0, 10), "2026-08-11");
  assert.ok(raiz.filhos.every((f) => f.valores[tb] === raiz.valores[tb]));
  // Min(EDT): ordem de texto do DAX (não numérica) e filhos ordenados por ela (sortDefinition crescente).
  const edt = "Min(WBX.EDT)";
  const minTexto = (ls: Linha[]) => ls.map((l) => String(l.EDT)).sort((a, b) => a.localeCompare(b, "pt-BR", { sensitivity: "base" }))[0];
  assert.equal(raiz.valores[edt], minTexto(wbx));
  const edts = raiz.filhos.map((f) => String(f.valores[edt]));
  assert.deepEqual(edts, [...edts].sort((a, b) => a.localeCompare(b, "pt-BR", { sensitivity: "base" })));
  // Expansão salva: "Engineering - 30%" aberto (isToggled), níveis abaixo recolhidos por padrão.
  const exp = gerado("expansao_matrizes.json")[v.id];
  assert.equal(noAbertoInicial(exp, raiz), true);
  assert.equal(noAbertoInicial(exp, raiz.filhos[0] as NoMatriz), false);
  // Folhas de nível 7 existem na árvore (expandir até o fim).
  const profundidade = (n: NoMatriz): number => (n.filhos.length ? 1 + Math.max(...n.filhos.map(profundidade)) : 0);
  assert.equal(profundidade(raiz), 3);
});

// ------------------------------------------------------------------------------------------ dSemana

test("dSemana: quintas-feiras de MIN(Início da LB) a MAX(Término); 08/10/2026 presente; outra semana muda o título e o prazo", () => {
  const semanas = semanasDSemana(FONTES.WBX);
  assert.ok(Array.isArray(semanas));
  const lista = semanas as string[];
  assert.ok(lista.includes("2026-10-08"));
  assert.ok(lista.every((s) => new Date(`${s}T00:00:00Z`).getUTCDay() === 4));
  // Tabela dSemana do cache do PBIX (pbixray, 15/10/2026): 23 quintas-feiras, 04/06/2026 a 05/11/2026.
  assert.equal(lista.length, 23);
  assert.equal(lista[0], "2026-06-04");
  assert.equal(lista.at(-1), "2026-11-05");
  const pg = pagina("p03");
  const titulo = pg.visuais.find((v) => v.id.startsWith("9f5e7fa9"))!;
  const d = dadosDoVisual(titulo, { pagina: pg, fontes: FONTES, segmentacoes: {}, dataCorte: "2026-10-01" });
  assert.ok(d.estado === "ok" && d.tipo === "valores");
  assert.equal(Object.values(d.valores)[0], "Week - 25/09 to 01/10");
  // Sem Início da LB (fonte legada) a lista não é derivável.
  assert.equal(semanasDSemana({ estado: "ok", linhas: [], vazias: ["Início da Linha de Base"] }), undefined);
});
