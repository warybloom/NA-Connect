import { readFileSync } from "node:fs";
import { test } from "node:test";
import assert from "node:assert/strict";
import { projectFacts, statusCounts, enrichDocuments, type FactsReport } from "@/components/pmo/pmoFacts";
import { calcularResumo, consolidarAvanco, documentalDaLdp, documentosDoStatus, ehStatusDocumento, rotaLdpFiltrada, rotuloTipoLdp, type LdpProjeto } from "../resumoLogic";
import { lerFiltrosDaUrl } from "../../dashboard/filtrosLogic";

type Status = "CANCELADO" | "EMITIDO" | "NO_PRAZO" | "ATRASADO" | "SEM_PRAZO";
const doc = (projectId: string, codigo: string, status: Status, hh: number, custo: number) => ({
  rowId: `${projectId}-${codigo}`, projectId, codigo, titulo: codigo, disciplina: "CIVIL", hhPrevisto: hh, custoPrevisto: custo, inicioSemana: null,
  dataBaseline: null, dataReprogramada: null, dataEmissao: null, statusInterno: null, statusCliente: null, situacaoEmissao: null,
  avancoFisico: null, classeFisica: "SEM_DADO", cancelado: status === "CANCELADO", emitido: status === "EMITIDO", statusPrazo: status,
  dataAlvo: null, fonteDataAlvo: "", correlacaoCronograma: "", statusDocumento: status, percentFisicoLdp: null, fonteAvancoFisico: "",
});
const valores = (hh: number, cost: number) => ({ hh, cost, knownCostSubtotal: cost, knownCalculatedCostSubtotal: cost, entries: 1, missingHh: 0, missingCost: 0, missingCalculatedCost: 0 });
const relatorio = (projectId: string, tipoLdp: number, docs: ReturnType<typeof doc>[], hhPrev: number, custoPrev: number, hhReal: number, custoReal: number): FactsReport => ({
  projectId, nomeProjeto: projectId, tipoLdp, dataReferencia: "2026-09-30", documentos: docs as never, lancamentos: [], weeklyAtividades: [], cronograma: [],
  planning: {
    plannedHhTotal: hhPrev, plannedHhByDiscipline: [{ discipline: "CIVIL", plannedHh: hhPrev }], disciplineTotal: hhPrev, undisciplinedHh: 0, reconciliationDifference: 0,
    source: "", coverage: "", diagnostics: [], plannedCostTotal: null, documentPlannedCost: custoPrev, knownDocumentCostSubtotal: custoPrev,
    plannedCostByDiscipline: [{ discipline: "CIVIL", documentCost: custoPrev, documents: docs.length, withCost: docs.length }], costSource: "", costCoverage: "", costDocuments: docs.length, documentsWithCost: docs.length,
  },
  actual: { total: valores(hhReal, custoReal), source: "", byDiscipline: [{ discipline: "CIVIL", values: valores(hhReal, custoReal) }], byDocument: [] },
});

// Projeto normal (Tipo 1) e faseado (Tipo 2): o status já vem do backend; o Resumo só conta.
const CHS = relatorio("CHS001", 2, [doc("CHS001", "A", "EMITIDO", 10, 100), doc("CHS001", "B", "ATRASADO", 20, 200), doc("CHS001", "C", "CANCELADO", 5, 50)], 35, 350, 30, 400);
const BML = relatorio("BML011", 1, [doc("BML011", "X", "NO_PRAZO", 8, 80), doc("BML011", "Y", "SEM_PRAZO", 2, 20)], 10, 100, 4, 40);

test("AT é atividade: não entra em total/status documental", () => {
  const documento = { ...doc("P", "DOC", "EMITIDO", 10, 100), isActivity: false };
  const atividade = { ...doc("P", "AT-001", "SEM_PRAZO", 40, 400), statusDocumento: "ATIVIDADE", isActivity: true };
  const c = statusCounts([documento, atividade]);
  assert.equal(c.total, 1);
  assert.equal(c.emitidos, 1);
  assert.equal(c.semPrazo, 0);
  assert.equal(c.soma, 1);
  assert.equal(c.diferenca, 0);
});

test("Resumo = exatamente os números da Gestão à Vista (mesmas funções, mesmo payload)", () => {
  for (const sel of [[CHS], [BML], [CHS, BML]]) {
    const r = calcularResumo(sel);
    const gv = projectFacts(sel).cards;
    const gvDocs = statusCounts(enrichDocuments(sel));
    assert.deepEqual(r.documentos, gvDocs);
    assert.equal(r.hhPrevisto, gv.HH_Previsto); assert.equal(r.hhRealizado, gv.HH_Realizado);
    assert.equal(r.custoPrevisto, gv.Custo_Previsto); assert.equal(r.custoRealizado, gv.Custo_Realizado);
    assert.equal(r.consumoHh, gv.ConsumoHH); assert.equal(r.consumoCusto, gv.ConsumoCusto);
  }
});

test("Multiprojeto agrega; LDP normal e faseada no mesmo painel", () => {
  const r = calcularResumo([CHS, BML]);
  assert.equal(r.documentos.total, 5);
  assert.deepEqual([r.documentos.emitidos, r.documentos.atrasados, r.documentos.cancelados, r.documentos.noPrazo], [1, 1, 1, 1]);
  assert.equal(r.hhPrevisto, 45); assert.equal(r.custoRealizado, 440);
  assert.deepEqual(r.tiposLdp, [1, 2]);
  assert.match(rotuloTipoLdp(2), /faseada/); assert.match(rotuloTipoLdp(1), /normal/);
});

test("Card clicável: drill-down lista EXATAMENTE os documentos contados no card", () => {
  const sel = [CHS, BML];
  const r = calcularResumo(sel);
  assert.equal(documentosDoStatus(sel, "ATRASADO").length, r.documentos.atrasados);
  assert.equal(documentosDoStatus(sel, "EMITIDO").length, r.documentos.emitidos);
  assert.deepEqual(documentosDoStatus(sel, "CANCELADO").map((d) => d.codigo), ["C"]);
  assert.equal(ehStatusDocumento("ATRASADO"), true); assert.equal(ehStatusDocumento("QUALQUER"), false); assert.equal(ehStatusDocumento(null), false);
});

// --- Resumo × aba LDP: os cards documentais SÃO o resultado da aba LDP (mesmos nomes e números) ---
const kpi = (key: string, label: string, value: number, dim: string | null = null, val: string | null = null) => ({ key, label, value, filterDimension: dim, filterValue: val });
const VAL: Record<string, [string, string]> = { emitidos: ["gate", "EMITIDO"], cancelados: ["gate", "CANCELADO"], aEmitir: ["gate", "A_EMITIR"], analiseCliente: ["gateStatus", "EM_ANALISE_CLIENTE"] };
const cel = (row: string, column: string, value: number) => ({ row, column, value,
  filterDimension: VAL[row][0] === "gate" ? `gate${column}` : `gate${column}Status`, filterValue: VAL[row][1] });
const LDP_T1: LdpProjeto = { projectId: "b.bml", hubId: "b.hub", nome: "BML011.016", tipoLdp: 1,
  kpis: [kpi("total", "Total", 105), kpi("emitidos", "Emitidos", 53, "gestao", "EMITIDO"), kpi("cancelados", "Cancelados", 28, "gestao", "CANCELADO"),
    kpi("revisaoCliente", "Revisão do cliente", 7, "status", "EM_ANALISE_CLIENTE"), kpi("aEmitir", "A emitir", 24, "gestao", "A_EMITIR")], celulas: [] };
const LDP_T2: LdpProjeto = { projectId: "b.chs", hubId: "b.hub", nome: "CHS001", tipoLdp: 2,
  kpis: [kpi("total", "Total Documentos", 437), kpi("emitidosGeral", "Emitidos Geral", 126, "gestao", "EMITIDO")],
  celulas: ["30", "60", "90"].flatMap((g, i) => [cel("emitidos", g, [176, 82, 0][i]), cel("cancelados", g, [9, 80, 6][i]),
    cel("analiseCliente", g, [0, 82, 0][i]), cel("aEmitir", g, [0, 186, 167][i])]) };
const card = (d: ReturnType<typeof documentalDaLdp>, key: string) => d.cards.find((c) => c.key === key)!;

test("Quatro cards com os nomes da aba LDP: Emitidos, Cancelados, Em Análise do Cliente, A Emitir", () => {
  assert.deepEqual(documentalDaLdp([LDP_T1]).cards.map((c) => c.titulo), ["Emitidos", "Cancelados", "Em Análise do Cliente", "A Emitir"]);
});

test("Tipo 1: só o número do KPI da aba LDP (sem 30/60/90) e o clique abre a aba LDP com o MESMO filtro", () => {
  const d = documentalDaLdp([LDP_T1]);
  assert.deepEqual(d.cards.map((c) => c.valor?.valor), [53, 28, 7, 24]);
  assert.ok(d.cards.every((c) => c.gates.length === 0));
  assert.equal(rotaLdpFiltrada(LDP_T1, card(d, "emitidos").valor!.filtro), "/controle-producao/ldp?hub=b.hub&projeto=b.bml&f.gestao=EMITIDO");
  assert.equal(rotaLdpFiltrada(LDP_T1, card(d, "analiseCliente").valor!.filtro), "/controle-producao/ldp?hub=b.hub&projeto=b.bml&f.status=EM_ANALISE_CLIENTE");
  assert.deepEqual([d.total.label, d.total.valor], ["Total", 105]);
});

test("Tipo 2: 30%/60%/90% = células da matriz da aba LDP; cada número abre a aba LDP com o MESMO filtro", () => {
  const d = documentalDaLdp([LDP_T2]);
  assert.ok(d.cards.every((c) => c.valor === null));                 // sem número geral extra no card
  assert.deepEqual(card(d, "aEmitir").gates.map((g) => [g.label, g.valor]), [["30%", 0], ["60%", 186], ["90%", 167]]);
  assert.deepEqual(card(d, "cancelados").gates.map((g) => g.valor), [9, 80, 6]);
  assert.deepEqual(card(d, "analiseCliente").gates.map((g) => g.valor), [0, 82, 0]);
  const a30 = card(d, "aEmitir").gates[0];
  assert.equal(rotaLdpFiltrada(LDP_T2, a30.filtro), "/controle-producao/ldp?hub=b.hub&projeto=b.chs&f.gate30=A_EMITIR");
  assert.equal(rotaLdpFiltrada(LDP_T2, card(d, "analiseCliente").gates[1].filtro), "/controle-producao/ldp?hub=b.hub&projeto=b.chs&f.gate60Status=EM_ANALISE_CLIENTE");
  // A aba LDP aberta por esse link aplica exatamente esse filtro (KPI/matriz/tabela do mesmo motor).
  assert.deepEqual(lerFiltrosDaUrl("?hub=b.hub&projeto=b.chs&f.gate30=A_EMITIR"), { gate30: ["A_EMITIR"] });
  assert.deepEqual(lerFiltrosDaUrl("?hub=x"), {});
  assert.deepEqual([d.total.label, d.total.valor], ["Total Documentos", 437]);
});

test("Multiprojeto: cada projeto resolvido pela sua regra e só depois somado; sem drill-down documental ambíguo", () => {
  const d = documentalDaLdp([LDP_T1, LDP_T2]);
  assert.equal(d.total.valor, 542);
  assert.equal(card(d, "emitidos").valor?.valor, 53);                  // número = projetos Tipo 1
  assert.equal(card(d, "emitidos").gates[0].valor, 176);               // 30/60/90 = projetos Tipo 2
  assert.equal(card(d, "emitidos").valor?.filtro, undefined); assert.equal(card(d, "emitidos").gates[0].filtro, undefined);
  assert.deepEqual(d.tipos, [1, 2]);
});

test("Avanço multiprojeto: Σ numeradores ÷ Σ denominadores dos projetos já calculados no backend", () => {
  const av = (tipo: number, pn: number, pd: number, fn: number, fd: number) => ({ tipoLdp: tipo, dataReferencia: "2026-09-30", inicioSemanaVigente: "2026-09-27", fimSemanaVigente: "2026-10-03",
    previsto: pd ? (pn / pd) * 100 : null, previstoNumerador: pn, previstoDenominador: pd, fisico: fd ? (fn / fd) * 100 : null, fisicoNumerador: fn, fisicoDenominador: fd,
    documentos: 1, documentosSemDadoFisico: 0, documentosSemDadoPrevisto: 0, regraPrevisto: "p", regraFisico: "f", ponderacao: "Planned_HH" });
  const r1 = { ...CHS, avanco: av(2, 77, 100, 46, 100) }, r2 = { ...BML, avanco: av(1, 100, 100, 62, 100) };
  const c = consolidarAvanco([r1, r2]);
  assert.equal(c.previsto, 88.5); assert.equal(c.fisico, 54);
  assert.equal(c.fimSemana, "2026-10-03");
  assert.equal(consolidarAvanco([CHS]).previsto, null);               // sem avanço do backend → null, nunca 0%
});

test("HH realizado = valor oficial do backend (Actual_HH_Total_Corrigido), também por disciplina e por documento", () => {
  const docs = CHS.documentos.map((d, i) => ({ ...d, hhRealizado: [8, 2, 1][i] ?? null }));
  const oficial = { ...CHS, documentos: docs, hhRealizado: { total: 11, porDisciplina: [{ disciplina: "civil", hh: 11 }], fonte: "VW" } };
  const r = calcularResumo([oficial]);
  assert.equal(r.hhRealizado, 11);                                     // não o Timesheet (30)
  assert.equal(r.saldoHh, 35 - 11);
  assert.equal(r.consumoHh, (11 / 35) * 100);
  const f = projectFacts([oficial]);
  assert.equal(f.disciplines.find((x) => x.chave === "CIVIL")?.HH_Realizado, 11);
  assert.equal(f.reconciliacaoRealizado.hhDiferenca, 0);
  assert.equal(f.reconciliacaoDocumentos.hhDiferenca, 0);
  assert.deepEqual(enrichDocuments([oficial]).map((d) => d.hhRealizado), [8, 2, 1]);
  assert.equal("HH_OS_Contratado" in f.cards, false);
});

test("Custo realizado = valor oficial do backend (Actual_Cost_Total_Corrigido): total, saldo, consumo, disciplina e documento", () => {
  const docs = CHS.documentos.map((d, i) => ({ ...d, hhRealizado: [8, 2, 1][i] ?? null, custoRealizado: [80, 20, 10][i] ?? null }));
  const oficial = { ...CHS, documentos: docs, hhRealizado: { total: 11, porDisciplina: [{ disciplina: "civil", hh: 11, custo: 110 }], fonte: "VW", custoTotal: 110, custoOficial: true } };
  const r = calcularResumo([oficial]);
  assert.equal(r.custoRealizado, 110);                                  // não a fonte anterior (400)
  assert.equal(r.saldoCusto, 350 - 110);
  assert.equal(r.consumoCusto, (110 / 350) * 100);
  const f = projectFacts([oficial]);
  assert.equal(f.disciplines.find((x) => x.chave === "CIVIL")?.Custo_Realizado, 110);
  assert.equal(f.reconciliacaoDocumentos.custoDiferenca, 0);
  assert.deepEqual(enrichDocuments([oficial]).map((d) => d.custoRealizado), [80, 20, 10]);
  // Sem a coluna no ambiente (custoOficial=false): custo segue a fonte anterior, HH continua oficial.
  const semCusto = { ...oficial, hhRealizado: { ...oficial.hhRealizado, custoOficial: false, custoTotal: null } };
  assert.equal(calcularResumo([semCusto]).custoRealizado, 400);
  assert.equal(calcularResumo([semCusto]).hhRealizado, 11);
});

test("Interface do Resumo: sem card HH OS, sem '(corrigido)', sem 'Por fase', sem 'Valor Total'; 30/60/90 em destaque", () => {
  const src = readFileSync("src/features/production/analytics/ResumoProducaoPage.tsx", "utf-8");
  // Títulos oficiais dos cards de avanço por fase são permitidos; nenhum outro "por fase".
  const visivel = src.split("\n").filter((l) => !l.trim().startsWith("//") && !l.trim().startsWith("*")).join("\n")
    .replace('"Avanço Previsto por Fase"', "").replace('"Avanço Físico por Fase"', "");
  for (const proibido of ["HH OS", "HH das OS", "(corrigido)", "Corrigido\"", "por fase", "Por fase", "POR FASE", "Valor Total", "VALOR TOTAL", "Total:", "Custo OS"])
    assert.ok(!visivel.includes(proibido), `texto proibido na interface: ${proibido}`);
  assert.match(visivel, /text-2xl font-black leading-tight text-foreground tabular-nums 2xl:text-3xl/); // número do gate em destaque (reduzido)
  assert.match(visivel, /text-base font-black tracking-wide text-muted-foreground md:text-lg/); // 30%/60%/90% maiores
  assert.match(visivel, /divide-x/);                                   // lado a lado, divisórias discretas
});

test("Avanço por fase multiprojeto: Σ numeradores ÷ Σ denominadores por fase (nunca média de percentuais)", () => {
  const fase = (gate: number, pn: number, pd: number, fn: number, fd: number) =>
    ({ gate, previsto: pd ? (pn / pd) * 100 : null, previstoNumerador: pn, previstoDenominador: pd, fisico: fd ? (fn / fd) * 100 : null, fisicoNumerador: fn, fisicoDenominador: fd });
  const av = (porFase: ReturnType<typeof fase>[], fn: number, fd: number) => ({ tipoLdp: 2, dataReferencia: "2026-09-30", inicioSemanaVigente: "2026-09-27", fimSemanaVigente: "2026-10-03",
    previsto: null, previstoNumerador: 0, previstoDenominador: 0, fisico: fd ? (fn / fd) * 100 : null, fisicoNumerador: fn, fisicoDenominador: fd,
    documentos: 1, documentosSemDadoFisico: 0, documentosSemDadoPrevisto: 0, regraPrevisto: "p", regraFisico: "f", ponderacao: "Esforço", porFase });
  // Projeto 1: fase 30 com esforço 30 a 80% (24/30); Projeto 2: fase 30 com esforço 60 a 50% (30/60) → 54/90 = 60% (não 65%).
  const p1 = { ...CHS, avanco: av([fase(30, 0, 0, 24, 30)], 24, 30) }, p2 = { ...BML, avanco: av([fase(30, 0, 0, 30, 60)], 30, 60) };
  const c = consolidarAvanco([p1, p2]);
  assert.equal(c.fisico, 60);
  assert.equal(c.porFase.find((f) => f.gate === 30)?.fisico, 60);
  assert.equal(c.porFase.find((f) => f.gate === 60)?.fisico, null);   // sem esforço na fase = sem valor, nunca 0%
  assert.deepEqual(consolidarAvanco([CHS]).porFase, []);              // sem Tipo 2: não há cards por fase
});
