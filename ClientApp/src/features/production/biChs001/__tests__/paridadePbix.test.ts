// Paridade NUMÉRICA com o Power BI: o motor do frontend roda sobre o cache de dados do PBIX (fixtures isoladas em
// _bi_chs001_replica/fixtures_pbix — nunca usadas pelo app) e cada visual de dados das 10 páginas prioritárias é comparado
// com o valor esperado. Esperados: cálculo independente em pandas sobre o mesmo cache + tabela de controle da auditoria Codex
// (AUDITORIA_CODEX_PRIORITARIAS.md). Rodar: node _bi_chs001_replica/tools/testar.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import type { Linha } from "../dados/filtrosPbi";
import { dadosDoVisual, dataCorteSalva, segmentacaoNaoReproduzida, type FonteCarregada } from "../dados/motor";
import { colunasVazias, converterLinhas, prepararCronograma, prepararSerieDiaria } from "../dados/entidades";
import { avisosDeFonte } from "../dados/avisos";
import type { DadosVisual, Valor } from "../dados/tipos";
import type { PaginaPbi } from "../pbi/modelo";

const clientApp = (() => {
  for (const b of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { readdirSync(path.join(b, "src/features/production/biChs001/generated")); return b; } catch { /* próximo */ }
  }
  throw new Error("ClientApp não encontrado");
})();
const pagina = (arq: string) => JSON.parse(readFileSync(path.join(clientApp, "src/features/production/biChs001/generated", `${arq}.json`), "utf8")) as PaginaPbi;
const fixture = (arq: string) => JSON.parse(readFileSync(path.join(clientApp, "..", "_bi_chs001_replica/fixtures_pbix", arq), "utf8")) as Linha[];

const ok = (linhas: Linha[]): FonteCarregada => ({ estado: "ok", linhas });
/** Oráculo independente (tools/oraculo_curvas.py, Python sobre o mesmo cache) — NÃO é captura do Power BI. */
const ESPERADO = JSON.parse(readFileSync(path.join(clientApp, "..", "_bi_chs001_replica/fixtures_pbix/esperado_curvas.json"), "utf8")) as
  Record<number, Record<string, number | string | null> & { pontos: Record<string, number | string | null>[] }>;
const FONTES_PBIX = { "LDP (4)": ok(fixture("ldp4.json")), "Programação Semanal": ok(fixture("programacao.json")), WBX: ok(fixture("wbx.json")),
  AssignmentByDay: ok(fixture("abd.json")) };

function calcular(pg: PaginaPbi, prefixo: string): DadosVisual {
  const v = pg.visuais.find((x) => x.id.startsWith(prefixo));
  assert.ok(v, `visual ${prefixo} em ${pg.nome}`);
  return dadosDoVisual(v, { pagina: pg, fontes: FONTES_PBIX, segmentacoes: {} });
}
const valores = (d: DadosVisual): Valor[] => {
  assert.ok(d.estado === "ok" && d.tipo === "valores", JSON.stringify(d).slice(0, 300));
  return Object.values(d.valores);
};
const valor = (pg: PaginaPbi, prefixo: string) => valores(calcular(pg, prefixo))[0];
const pontos = (d: DadosVisual) => {
  assert.ok(d.estado === "ok" && d.tipo === "pontos", JSON.stringify(d).slice(0, 300));
  return Object.fromEntries(d.pontos.map((p) => [p.categoria, Object.values(p.valores)[0]]));
};
const pendente = (d: DadosVisual) => assert.equal(d.estado, "pendente", JSON.stringify(d).slice(0, 300));

// ----------------------------------------------------------------------------------------------------- LDP

const LDP: [string, string, Record<string, number | null>][] = [
  // página, [total, issued, canceled, client review, adressing] — IDs dos cardVisual do PBIR
  ["p12", "Eng 30", { c76cb8f4: 181, "3ed78afe": 172, "9aa32598": null, "1c6e40b1": null, "2d7506eb": null }],
  ["p13", "Eng 60", { "609669a7": 357, c370079f: 129, "9b1eefcc": 75, "67a267ff": 129, e807f67b: null }],
  ["p14", "Eng 90", { e5ed0bbe: 166, b8a28b51: null, "14ff715b": null, "6aa4cb19": null, f4021156: null }],
  ["p08", "3D 30", { fc0e8e96: 4, e84b2324: 4, c48415c7: null, de52f632: null, "6308f22b": null }],
  ["p09", "3D 60", { "4c907805": 7, c8ef976d: null, fcb7f8a4: 1, e2df5cc8: null, a889a460: null }],
  ["p10", "3D 90", { a2dbaffe: 7, "291e9db4": null, "947d01e9": null, f476e3eb: null, c2ba931a: null }],
];

for (const [arq, nome, esperado] of LDP) {
  test(`PBIX × motor — LDP ${nome}: Total e os quatro cartões do gate (null = (Blank) do DAX)`, () => {
    const pg = pagina(arq);
    for (const [id, v] of Object.entries(esperado)) assert.equal(valor(pg, id), v, `${nome} ${id}`);
  });
}

test("Eng 30 — CANCELED (cartão) ≠ CANCELLED (gráficos): semânticas diferentes, nenhuma grafia normalizada", () => {
  const pg = pagina("p12");
  // Cartão 9aa32598 = dFase Engenharia 30%[CANCELED 30] = COUNTROWS(SITUATION 30% = "Canceled") → BLANK (Codex: "0").
  // LDP (4)[Cancelled] está só no painel de filtros do cartão, SEM condição → não filtra.
  assert.equal(valor(pg, "9aa32598"), null);
  // Os 9 "CANCELLED" são categorias de OUTRAS colunas: DOCUMENT STATUS 30% (STATUS) e SITUATION 30% (SITUATION).
  assert.deepEqual(pontos(calcular(pg, "c1d38717")), { APPROVED: 172, CANCELLED: 9 });
  assert.deepEqual(pontos(calcular(pg, "96cec642")), { ISSUED: 172, CANCELLED: 9 });
});

test("3D 90 — dois '7' diferentes: Total = 6 modelos 3D + 1 AT (filtro de página); tabela = 7 modelos 3D (filtro do visual)", () => {
  const pg = pagina("p10");
  // Página: FORMAT NOT IN {A, B, D, null} — a linha AT (CHS001-000-3D1-RE-001, Fase 90% = FD) entra no Total.
  const comFase = fixture("ldp4.json").filter((l) => !["A", "B", "D", null].includes(l.FORMAT as string) && l["Fase 90%"] !== null);
  assert.deepEqual(comFase.map((l) => l.FORMAT).sort(), ["3D", "3D", "3D", "3D", "3D", "3D", "AT"]);
  assert.equal(valor(pg, "a2dbaffe"), 7); // forçar FORMAT = 3D daria 6
  // Tabela: filtro do VISUAL FORMAT In ['3D'] e sem filtro de Fase → inclui CHS001-000-MEM-3D-001 (Fase 90% em branco).
  const t = calcular(pg, "51e521cc");
  assert.ok(t.estado === "ok" && t.tipo === "linhas");
  assert.equal(t.linhas.length, 7);
  assert.ok(t.linhas.every((l) => l["LDP (4).FORMAT"] === "3D"));
  assert.equal(t.linhas.filter((l) => l["LDP (4).Fase 90%"] === null).length, 1);
});

test("Tabelas LDP: KPI[Adh] é só ícone condicional órfão (série não projetada, medida ausente do modelo) → tabela calcula", () => {
  for (const [arq, id, n] of [["p12", "95a23f2f", 181], ["p13", "2b075837", 357], ["p14", "8e1b7156", 166], ["p08", "ff5bdd78", 4], ["p09", "c14d0b31", 7]] as const) {
    const t = calcular(pagina(arq), id);
    assert.ok(t.estado === "ok" && t.tipo === "linhas", `${arq} ${id}`);
    assert.equal(t.linhas.length, n, `${arq} ${id}`);
  }
});

// ------------------------------------------------------------------------------------------------ Programação

test("Programação — 148 linhas no cache; cada visual usa o SEU subconjunto (108 / 26 / 28 / 5 / gráfico e tabela 103)", () => {
  const pg = pagina("p17");
  const todas = fixture("programacao.json");
  assert.equal(todas.length, 148);
  const porStatus = todas.reduce<Record<string, number>>((a, l) => ({ ...a, [String(l["DOCUMENT STATUS"])]: (a[String(l["DOCUMENT STATUS"])] ?? 0) + 1 }), {});
  assert.deepEqual(porStatus, { "NOT STARTED": 28, ELABORATION: 26, VERIFICATION: 49, AUTORIZATION: 40, "ISSUANCE FLOW": 5 });
  assert.equal(valor(pg, "d2c0e0e9"), 108); // ACTIVITIES: filtro do cartão = ELABORATION, ISSUANCE FLOW, NOT STARTED, VERIFICATION
  assert.equal(valor(pg, "c9849695"), 26);
  assert.equal(valor(pg, "bfc7392a"), 28);
  assert.equal(valor(pg, "fdc15852"), 5);
  const grafico = pontos(calcular(pg, "bfc533e4")); // filtro: ELABORATION, NOT STARTED, VERIFICATION
  assert.equal(Object.values(grafico).reduce<number>((s, x) => s + Number(x), 0), 103);
  const tabela = calcular(pg, "4f0d9cb4");
  assert.ok(tabela.estado === "ok" && tabela.tipo === "linhas");
  assert.equal(tabela.linhas.length, 103);
});

test("Programação — slicer WBX[Término] (Between) não é reproduzido e não carrega WBX (sem relação WBX→Programação no modelo)", () => {
  const slicer = pagina("p17").visuais.find((v) => v.id.startsWith("ac7de2b1"))!;
  assert.match(segmentacaoNaoReproduzida(slicer) ?? "", /Between/);
});

// ------------------------------------------------------------------------------------------------ Resumos

const RESUMOS = [
  { arq: "p03", gate: 30, datas: "6d9c8b46", desvio: "8d19da71", dias: "bb4e1966", titulo: "9f5e7fa9", rosca: "4a4c60d0",
    tr: "2026-08-24", tb: "2026-08-11", d: 13, rest: 0, status: { Concluded: 190, Canceled: 6 } },
  { arq: "p04", gate: 60, datas: "1cb81bc0", desvio: "acd6bef2", dias: "c859d8df", titulo: "ae8c65ff", rosca: "b2955cba",
    tr: "2026-10-19", tb: "2026-09-28", d: 21, rest: 11, status: { Overdue: 152, Concluded: 113, Canceled: 82 } },
  { arq: "p05", gate: 90, datas: "bc7e47d1", desvio: "9a23de1b", dias: "819db0fa", titulo: "b3c33a1f", rosca: "be151583",
    tr: "2026-11-10", tb: "2026-10-19", d: 22, rest: 33, status: { "On Time": 174, Canceled: 16 } },
] as const;

for (const r of RESUMOS) {
  test(`PBIX × motor — Resumo ${r.gate}%: Término LB/Tendência, Desvio, Dias Restantes, Activity Title e rosca de status`, () => {
    const pg = pagina(r.arq);
    assert.equal(dataCorteSalva(pg), "2026-10-08"); // segmentação dSemana salva no PBIR
    const [trend, base] = valores(calcular(pg, r.datas)).map((x) => String(x).slice(0, 10));
    assert.equal(trend, r.tr);
    assert.equal(base, r.tb);
    assert.equal(valor(pg, r.desvio), r.d);
    const dias = calcular(pg, r.dias);
    assert.ok(dias.estado === "ok" && dias.tipo === "valores");
    // Duração Baseline (Início da LB do cache) + Dias Restantes; nenhum bloco pendente.
    assert.deepEqual(Object.values(dias.valores), [ESPERADO[r.gate]["PRAZO | Duração Baseline"], r.rest]);
    assert.equal(dias.pendentes, undefined);
    assert.equal(valor(pg, r.titulo), "Week - 02/10 to 08/10");
    assert.deepEqual(pontos(calcular(pg, r.rosca)), r.status);
  });

  test(`Resumo ${r.gate}%: cartões ACC / BASE SEM e curva S × oráculo independente (cache do PBIX, corte 08/10/2026)`, () => {
    const pg = pagina(r.arq);
    const esp = ESPERADO[r.gate];
    const perto = (a: unknown, b: unknown, msg: string) => {
      if (b === null) return assert.equal(a, null, msg);
      if (typeof b === "string") return assert.equal(a, b, msg);
      assert.ok(typeof a === "number" && Math.abs(a - (b as number)) < 1e-9, `${msg}: motor ${a} × oráculo ${b}`);
    };
    for (const v of pg.visuais.filter((x) => x.type === "card")) {
      const d = dadosDoVisual(v, { pagina: pg, fontes: FONTES_PBIX, segmentacoes: {} });
      assert.ok(d.estado === "ok" && d.tipo === "valores", `${v.id}: ${JSON.stringify(d).slice(0, 200)}`);
      const [chave, valorMotor] = Object.entries(d.valores)[0];
      const medida = chave.replace(/^KPI\.KPI \d+ \| /, "");
      if (medida === "CARD | Weekly Complete") { perto(valorMotor, fmtPct(esp["BASE SEM | % Realizado"] as number | null), chave); continue; }
      if (medida === "CARD | Weekly Deviation") { perto(valorMotor, fmtPct(esp["BASE SEM | % Desvio"] as number | null), chave); continue; }
      perto(valorMotor, esp[medida], chave);
    }
    const combo = pg.visuais.find((x) => x.type === "lineClusteredColumnComboChart")!;
    const d = dadosDoVisual(combo, { pagina: pg, fontes: FONTES_PBIX, segmentacoes: {} });
    assert.ok(d.estado === "ok" && d.tipo === "pontos", JSON.stringify(d).slice(0, 300));
    assert.deepEqual(d.pontos.map((p) => p.categoria), esp.pontos.map((p) => p.categoria), "semanas com Mostrar Período = 1");
    d.pontos.forEach((p, i) => {
      for (const [k, x] of Object.entries(p.valores)) perto(x, esp.pontos[i][k.replace(/^KPI\.KPI \d+ \| /, "")], `${p.categoria} ${k}`);
    });
  });

  test(`Resumo ${r.gate}%: sem a série diária (banco atual), curvas e cartões de realizado ficam PENDENTE com o motivo`, () => {
    const pg = pagina(r.arq);
    const fontes = { ...FONTES_PBIX, AssignmentByDay: { estado: "ok" as const, linhas: [], indisponivel: "tabela diária ainda não existe" } };
    const combo = pg.visuais.find((x) => x.type === "lineClusteredColumnComboChart")!;
    const d = dadosDoVisual(combo, { pagina: pg, fontes, segmentacoes: {} });
    pendente(d);
    assert.match(d.estado === "pendente" ? d.motivo : "", /tabela diária ainda não existe/);
    // A matriz (EDT, PRAZO, Status) não usa a série: continua calculada.
    const matriz = dadosDoVisual(pg.visuais.find((x) => x.type === "pivotTable")!, { pagina: pg, fontes, segmentacoes: {} });
    assert.ok(matriz.estado === "ok" && matriz.tipo === "matriz", JSON.stringify(matriz).slice(0, 200));
  });
}

const fmtPct = (v: number | null) => (v === null ? "" : `${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v * 100)}%`);

test("Resumo 60%: filtro de página é EXCLUSÃO (NOT IN de 7 valores) — preservado, inclusive para valores não listados", () => {
  const pg = pagina("p04");
  const linha = (l4: string): Linha => ({ "Level 4": l4, Status1: "Overdue", TaskID: "1", "Término": "2026-10-01T00:00:00", "Término da Linha de Base": "2026-10-01T00:00:00" });
  const fontes = { WBX: ok([linha("Engineering - 60%"), linha("Engineering - 30%"), linha("Outro Nível")]) };
  const rosca = dadosDoVisual(pg.visuais.find((v) => v.id.startsWith("b2955cba"))!, { pagina: pg, fontes, segmentacoes: {} });
  assert.deepEqual(pontos(rosca), { Overdue: 2 }); // "Engineering - 30%" excluído; "Outro Nível" entra (não está na lista)
});

test("Segmentações do Resumo: Disciplina filtra rosca e ALLSELECTED dos cartões de prazo", () => {
  const pg = pagina("p05");
  const ctx = { pagina: pg, fontes: FONTES_PBIX, segmentacoes: { "WBX|Disciplina": ["Process"] } };
  const rosca = pontos(dadosDoVisual(pg.visuais.find((v) => v.id.startsWith("be151583"))!, ctx));
  const esperado = fixture("wbx.json").filter((l) => l["Level 4"] === "Engineering - 90%" && l.Disciplina === "Process")
    .reduce<Record<string, number>>((a, l) => ({ ...a, [String(l.Status1)]: (a[String(l.Status1)] ?? 0) + 1 }), {});
  assert.deepEqual(rosca, esperado);
  const datas = dadosDoVisual(pg.visuais.find((v) => v.id.startsWith("bc7e47d1"))!, ctx);
  const maxTermino = fixture("wbx.json").filter((l) => l["Level 4"] === "Engineering - 90%" && l.Disciplina === "Process")
    .map((l) => String(l["Término"])).sort().at(-1);
  assert.equal(valores(datas)[0], maxTermino);
  assert.match(segmentacaoNaoReproduzida(pg.visuais.find((v) => v.id.startsWith("2a90aabc"))!) ?? "", /dSemana/);
});

// -------------------------------------------------------------------------------- fonte N&A (cronograma)

test("Cronograma do banco → WBX: hierarquia pela ORDEM das tarefas (não pelo prefixo da EDT digitada)", () => {
  const brutas = [
    { ID_Cronograma: 1, EDT_WBS: "0", TASK_NAME: "Projeto", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 2, EDT_WBS: "1", TASK_NAME: "CHS - ETHANOL PLANT", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 3, EDT_WBS: "1.6", TASK_NAME: "Engineering - 60%", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 4, EDT_WBS: "1.6.1", TASK_NAME: "Process", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 5, EDT_WBS: "1.6.1.1", UID_Tarefa: "10", TASK_NAME: "PDC 60", Eh_Tarefa_Resumo: false, Status1: "Overdue", Termino: "2026-10-01T12:00:00" },
    { ID_Cronograma: 6, EDT_WBS: "1.7", TASK_NAME: "Engineering - 90%", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 7, EDT_WBS: "1.6.1.1.1", UID_Tarefa: "11", TASK_NAME: "PDC 90", Eh_Tarefa_Resumo: false, Status1: "On Time", Termino: "2026-11-01T12:00:00" },
    { ID_Cronograma: 8, EDT_WBS: "1.8", UID_Tarefa: "12", TASK_NAME: "Marco solto", Eh_Tarefa_Resumo: false },
  ];
  const folhas = prepararCronograma([...brutas].reverse()); // ordem vem de ID_Cronograma, não da ordem recebida
  assert.deepEqual(folhas.map((f) => [f.TASK_NAME, f.Level_3, f.Level_4]), [
    ["PDC 60", "CHS - ETHANOL PLANT", "Engineering - 60%"],
    ["PDC 90", "CHS - ETHANOL PLANT", "Engineering - 90%"], // EDT "1.6.x", mas o pai é o 90%
    ["Marco solto", "CHS - ETHANOL PLANT", "Marco solto"],
  ]);
  const linhas = converterLinhas("WBX", brutas);
  assert.equal(linhas.length, 3);
  assert.deepEqual(linhas[1], { "Level 3": "CHS - ETHANOL PLANT", "Level 4": "Engineering - 90%", "Level 4 Original": "Engineering - 90%", TaskID: "11",
    EDT: "1.6.1.1.1", Status1: "On Time",
    Disciplina: null, Responsabilidade: null, Pacote: null, "NA DOCUMENT NUMBER": null, "Término da Linha de Base": null, "Término": "2026-11-01T12:00:00",
    // Fonte legada: Início/LB/real/pai não existem → nulos (as medidas os tratam como SEM FONTE).
    Trabalho: null, "Início": null, "Início da Linha de Base": null, "Trab Linha de Base": null, "Trabalho Real": null,
    "Level 5": null, "Level 6": null, "Level 7": null, NodeKey: "11" });
  assert.deepEqual(colunasVazias("WBX", linhas).sort(), ["Disciplina", "Início", "Início da Linha de Base", "Level 5", "Level 6", "Level 7",
    "NA DOCUMENT NUMBER", "Pacote", "Responsabilidade", "Trab Linha de Base", "Trabalho", "Trabalho Real", "Término da Linha de Base"]);
});

test("Com UID_Tarefa_Pai persistido, Level 3–7 vêm do PAI (GeraWBS); série diária agrupada no domingo (grão do AssignmentByDay)", () => {
  const brutas = [
    { ID_Cronograma: 1, UID_Tarefa: "0", UID_Tarefa_Pai: null, TASK_NAME: "Projeto", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 2, UID_Tarefa: "1", UID_Tarefa_Pai: "0", TASK_NAME: "CHS - ETHANOL PLANT", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 3, UID_Tarefa: "2", UID_Tarefa_Pai: "1", TASK_NAME: "Engineering - 60%", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 4, UID_Tarefa: "3", UID_Tarefa_Pai: "2", TASK_NAME: "Process", Eh_Tarefa_Resumo: true },
    { ID_Cronograma: 5, UID_Tarefa: "4", UID_Tarefa_Pai: "3", TASK_NAME: "PFD", Eh_Tarefa_Resumo: false, EDT_WBS: "1.7.9" },
    { ID_Cronograma: 6, UID_Tarefa: "5", UID_Tarefa_Pai: "99", TASK_NAME: "Órfã", Eh_Tarefa_Resumo: false },
  ];
  const f = prepararCronograma(brutas);
  assert.deepEqual(f.map((x) => [x.TASK_NAME, x.Level_3, x.Level_4, x.Level_5, x.Level_6, x.Level_7]), [
    ["PFD", "CHS - ETHANOL PLANT", "Engineering - 60%", "Process", "PFD", null], // EDT digitada "1.7.9" não manda
    ["Órfã", null, null, null, null, null], // pai ausente → sem Level (nunca nome inventado)
  ]);
  const semanas = prepararSerieDiaria([
    { UID_Tarefa: "4", Data: "2026-10-05T00:00:00", Trabalho_Real_Horas: 8 }, // segunda
    { UID_Tarefa: "4", Data: "2026-10-10T00:00:00", Trabalho_Real_Horas: 2 }, // sábado (mesma semana)
    { UID_Tarefa: "4", Data: "2026-10-11T00:00:00", Trabalho_Real_Horas: 1 }, // domingo seguinte
    { UID_Tarefa: "4", Data: "2026-10-12T00:00:00", Trabalho_Real_Horas: null },
  ]);
  assert.deepEqual(semanas, [{ TaskUID: "4", TimeByDay: "2026-10-04", Trabalho_Real: 10 }, { TaskUID: "4", TimeByDay: "2026-10-11", Trabalho_Real: 1 }]);
});

test("FONTE LEGADA (sem alias): 'Emgineering - 30%' literal — aviso; Resumo 30% não o recebe; 60%/90% o recebem pela exclusão", () => {
  const fontes = { WBX: ok([{ "Level 4": "Emgineering - 30%" }, { "Level 4": "Emgineering - 30%" }, { "Level 4": "Engineering - 60%" }]) };
  const [a30] = avisosDeFonte(pagina("p03"), fontes);
  const [a60] = avisosDeFonte(pagina("p04"), fontes);
  assert.match(a30, /"Emgineering - 30%" \(2 tarefas\).*NÃO entram/);
  assert.match(a60, /ENTRAM nesta página/);
  assert.deepEqual(avisosDeFonte(pagina("p04"), { WBX: ok([{ "Level 4": "Engineering - 60%" }]) }), []);
  // O frontend não normaliza nada por conta própria: sem o alias do BI isolado, o cartão de prazo do 30% fica BLANK.
  const pg = pagina("p03");
  const d = dadosDoVisual(pg.visuais.find((v) => v.id.startsWith("6d9c8b46"))!, { pagina: pg, fontes: { WBX: ok([{ "Level 4": "Emgineering - 30%", "Término": "2026-08-24T17:00:00" }]) }, segmentacoes: {} });
  assert.deepEqual(valores(d), [null, null]);
});

test("TaskID do banco (UID) só vale para contagem; como coluna exibida fica PENDENTE", () => {
  const pg = pagina("p03");
  const rosca = pg.visuais.find((v) => v.id.startsWith("4a4c60d0"))!;
  const comoColuna = { ...rosca, fields: { Category: rosca.fields!.Category, Y: [{ ...rosca.fields!.Y[0], kind: "column", fn: undefined }] } };
  pendente(dadosDoVisual(comoColuna, { pagina: pg, fontes: FONTES_PBIX, segmentacoes: {} }));
});
