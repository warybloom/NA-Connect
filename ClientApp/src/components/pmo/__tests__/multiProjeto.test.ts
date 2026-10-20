import test from 'node:test';
import assert from 'node:assert/strict';
import { planSlice, serverPagesFor, consolidarCurvaS, matrizStatusDisciplina, mergeGruposQualidade, somaConhecida, rotuloConsolidado,
  type CurvaSReport, type CurvaSPonto } from '../pmoMultiCore';
import { projectFacts, enrichDocuments, statusCounts, type FactsReport } from '../pmoFacts';

// ---------- paginação consolidada ----------
test('planSlice concatena os projetos na ordem e cobre exatamente a página global', () => {
  assert.deepEqual(planSlice([3, 0, 60], 1, 25), [{ project: 0, from: 0, to: 3 }, { project: 2, from: 0, to: 22 }]);
  assert.deepEqual(planSlice([3, 0, 60], 2, 25), [{ project: 2, from: 22, to: 47 }]);
  assert.deepEqual(planSlice([3, 0, 60], 3, 25), [{ project: 2, from: 47, to: 60 }]);
  assert.deepEqual(planSlice([3, 0, 60], 4, 25), []);
  const total = [1, 2, 3].flatMap((_, i) => planSlice([40, 7, 53], i + 1, 50)).reduce((a, x) => a + (x.to - x.from), 0);
  assert.equal(total, 100 - 0); // 3 páginas de 50 cobrem as 100 linhas, sem repetir nem perder
});
test('serverPagesFor devolve as páginas de servidor que cobrem o intervalo local', () => {
  assert.deepEqual(serverPagesFor(0, 22, 25), [1]);
  assert.deepEqual(serverPagesFor(22, 47, 25), [1, 2]);
  assert.deepEqual(serverPagesFor(47, 60, 25), [2, 3]);
});

// ---------- soma SQL ----------
test('somaConhecida soma valores conhecidos e só devolve null quando nenhum é conhecido', () => {
  assert.equal(somaConhecida([null, 2, undefined, 3]), 5);
  assert.equal(somaConhecida([0, null]), 0);
  assert.equal(somaConhecida([null, undefined]), null);
  assert.equal(somaConhecida([]), null);
});
test('rótulo consolidado só aparece com 2+ projetos', () => {
  assert.equal(rotuloConsolidado(1), null);
  assert.equal(rotuloConsolidado(3), 'CONSOLIDADO — 3 PROJETOS');
});

// ---------- Curva S ----------
const ponto = (data: string, ph: number, rh: number, pc: number, rc: number, pd: number, ed: number): CurvaSPonto =>
  ({ data, previstoHh: ph, realizadoHh: rh, previstoCusto: pc, realizadoCusto: rc, previstoDocumentos: pd, emitidosDocumentos: ed });
const cobertura = (o: Partial<CurvaSReport['cobertura']> = {}): CurvaSReport['cobertura'] => ({ totalDocumentos: 0, cancelados: 0, ambiguos: 0, semBaseline: 0, semHhPrevisto: 0,
  semCustoPrevisto: 0, hhPrevistoTotalProjeto: null, hhCurvaPrevisto: 0, hhCurvaRealizado: 0, hhCancelados: 0, hhSemBaseline: 0, hhAtividades: 0, hhSemDocumento: null, custoCurvaPrevisto: 0,
  custoCurvaRealizado: 0, ...o });
const curva = (id: string, pontos: CurvaSPonto[], cob: Partial<CurvaSReport['cobertura']> = {}): CurvaSReport =>
  ({ projectId: id, dataReferencia: '2026-09-19T00:00:00', pontos, cobertura: cobertura(cob), fontePrevisto: 'p', fonteRealizado: 'r' });

test('Curva S consolidada soma os acumulados na linha do tempo comum (não faz média)', () => {
  const a = curva('A', [ponto('2026-09-01', 100, 0, 10, 0, 1, 0), ponto('2026-09-10', 150, 100, 15, 10, 2, 1)]);
  const b = curva('B', [ponto('2026-09-01', 200, 0, 20, 0, 2, 0), ponto('2026-09-05', 250, 50, 25, 5, 3, 1)]);
  const c = consolidarCurvaS([a, b]);
  assert.deepEqual(c.pontos.map(p => p.data), ['2026-09-01', '2026-09-05', '2026-09-10']);
  assert.equal(c.pontos[0].previstoHh, 300);                    // 100 + 200 na mesma data
  assert.equal(c.pontos[1].previstoHh, 100 + 250);              // A segue em 100 (último ≤ D) e B sobe para 250
  assert.equal(c.pontos[2].previstoHh, 150 + 250);
  assert.equal(c.pontos[2].realizadoHh, 100 + 50);
  assert.equal(c.pontos[2].emitidosDocumentos, 2);
  assert.equal(c.pontos[2].previstoDocumentos, 5);
});
test('Curva S consolidada: ponto final = soma dos finais, monotônica e sem NaN/Infinity', () => {
  const a = curva('A', [ponto('2026-01-01', 10, 0, 1, 0, 1, 0), ponto('2026-03-01', 30, 20, 3, 2, 2, 1)]);
  const b = curva('B', [ponto('2026-02-01', 5, 5, 1, 1, 1, 1)]);
  const c = consolidarCurvaS([a, b]);
  const fim = c.pontos[c.pontos.length - 1];
  assert.equal(fim.previstoHh, 35); assert.equal(fim.realizadoHh, 25); assert.equal(fim.previstoCusto, 4);
  for (let i = 1; i < c.pontos.length; i++) {
    for (const k of ['previstoHh', 'realizadoHh', 'previstoCusto', 'realizadoCusto', 'previstoDocumentos', 'emitidosDocumentos'] as const) assert.ok(c.pontos[i][k] >= c.pontos[i - 1][k], k);
  }
  assert.ok(c.pontos.every(p => Object.values(p).every(v => typeof v === 'string' || Number.isFinite(v))));
  assert.deepEqual(c.pontos.map(p => p.data), [...c.pontos.map(p => p.data)].sort());
});
test('Curva S consolidada soma a cobertura e mantém total nulo só se nenhum projeto souber', () => {
  const a = curva('A', [], { totalDocumentos: 403, cancelados: 36, hhPrevistoTotalProjeto: 2297.4, hhSemDocumento: 0 });
  const b = curva('B', [], { totalDocumentos: 499, cancelados: 116, hhPrevistoTotalProjeto: null, hhSemDocumento: null });
  const c = consolidarCurvaS([a, b]);
  assert.equal(c.cobertura.totalDocumentos, 902); assert.equal(c.cobertura.cancelados, 152);
  assert.equal(c.cobertura.hhPrevistoTotalProjeto, 2297.4);
  assert.equal(consolidarCurvaS([curva('X', [], {}), curva('Y', [], {})]).cobertura.hhPrevistoTotalProjeto, null);
});

// ---------- Mapa de Calor ----------
test('matriz Disciplina × Status consolida projetos, normaliza a disciplina e reconcilia com o total', () => {
  const docs = [
    { projectId: 'A', projectName: 'CHS', disciplina: 'Electrical', statusDocumento: 'ATRASADO' },
    { projectId: 'A', projectName: 'CHS', disciplina: ' ELECTRICAL ', statusDocumento: 'CANCELADO' },
    { projectId: 'B', projectName: 'BML', disciplina: 'electrical', statusDocumento: 'ATRASADO' },
    { projectId: 'B', projectName: 'BML', disciplina: null, statusDocumento: 'SEM_PRAZO' },
  ];
  const m = matrizStatusDisciplina(docs, '__SEM__');
  assert.equal(m.soma, docs.length);
  const el = m.rows.find(r => r.chave === 'ELECTRICAL')!;
  assert.equal(el.c.ATRASADO, 2); assert.equal(el.c.CANCELADO, 1); assert.equal(el.total, 3);
  assert.equal(el.porProjeto.get('CHS')!.ATRASADO, 1); assert.equal(el.porProjeto.get('BML')!.ATRASADO, 1);
  assert.ok(m.rows.some(r => r.chave === '__SEM__'));
  assert.equal(Object.values(m.colunas).reduce((a, b) => a + b, 0), docs.length);
});

// ---------- NCR ----------
test('grupos de NCR de vários projetos somam por (status, disciplina)', () => {
  const g = mergeGruposQualidade([[{ status: 'Aberta', discipline: 'PIPING', count: 3 }], [{ status: 'Aberta', discipline: 'PIPING', count: 4 }, { status: 'Fechada', discipline: null, count: 1 }]]);
  assert.equal(g.length, 2); assert.equal(g.find(x => x.status === 'Aberta')!.count, 7);
});

// ---------- fatos canônicos multi-projeto ----------
const doc = (projectId: string, codigo: string, over: Record<string, unknown> = {}) => ({ rowId: `${projectId}:ldp:${codigo}`, projectId, codigo, titulo: null, disciplina: 'ELECTRICAL',
  hhPrevisto: 10, custoPrevisto: 100, inicioSemana: null, dataBaseline: null, dataReprogramada: null, dataEmissao: null, statusInterno: null, statusCliente: null,
  situacaoEmissao: null, avancoFisico: null, classeFisica: 'SEM_DADO', cancelado: false, emitido: false, statusPrazo: 'SEM_DADO', dataAlvo: null, fonteDataAlvo: '',
  correlacaoCronograma: '', statusDocumento: 'SEM_PRAZO', percentFisicoLdp: null, fonteAvancoFisico: '', ...over });
const values = (hh: number | null, custo: number | null) => ({ hh, cost: null, knownCostSubtotal: null, knownCalculatedCostSubtotal: custo, entries: 1, missingHh: 0, missingCost: 0, missingCalculatedCost: 0 });
function rel(id: string, nome: string, o: { plannedHh: number | null; cost: number | null; realHh: number | null; realCusto: number | null; disc: { d: string; hh: number | null }[]; docs: ReturnType<typeof doc>[]; actualDocs?: { document: string; hh: number }[] }): FactsReport {
  return { projectId: id, nomeProjeto: nome, tipoLdp: 1, dataReferencia: '2026-09-19', documentos: o.docs, lancamentos: [], weeklyAtividades: [], cronograma: [],
    planning: { plannedHhTotal: o.plannedHh, plannedHhByDiscipline: o.disc.map(x => ({ discipline: x.d, plannedHh: x.hh })), disciplineTotal: o.plannedHh, undisciplinedHh: 0,
      reconciliationDifference: 0, source: 'LDP_PLANNED_HH_ULTIMA_CARGA', coverage: 'RECONCILIADO', diagnostics: [], plannedCostTotal: null, documentPlannedCost: o.cost,
      knownDocumentCostSubtotal: o.cost, plannedCostByDiscipline: o.disc.map(x => ({ discipline: x.d, documentCost: o.cost, documents: 1, withCost: 1 })),
      costSource: '', costCoverage: '', costDocuments: o.docs.length, documentsWithCost: o.docs.length },
    actual: { total: values(o.realHh, o.realCusto), source: '', byDiscipline: o.disc.map(x => ({ discipline: x.d, values: values(o.realHh, o.realCusto) })),
      byDocument: (o.actualDocs ?? []).map(a => ({ document: a.document, values: values(a.hh, null) })) },
  } as unknown as FactsReport;
}
const CHS = rel('CHS', 'CHS001', { plannedHh: 2297.4, cost: 1295604.58, realHh: 5539.46, realCusto: 151575.68, disc: [{ d: 'ELECTRICAL', hh: 500 }, { d: 'PIPING', hh: 1797.4 }],
  docs: [doc('CHS', '000-PR-001'), doc('CHS', '000-PR-002')], actualDocs: [{ document: '000-PR-001', hh: 7 }] });
const BML = rel('BML', 'BML011.013', { plannedHh: 6401.96, cost: 716347.63, realHh: 7088.06, realCusto: 491803.46, disc: [{ d: 'ELECTRICAL', hh: 700 }, { d: 'CIVIL', hh: 5701.96 }],
  docs: [doc('BML', '000-PR-001'), doc('BML', '000-PR-002', { statusDocumento: 'EMITIDO' }), doc('BML', '000-PR-003', { statusDocumento: 'CANCELADO' })], actualDocs: [{ document: '000-PR-001', hh: 3 }] });

test('consolidado = soma dos projetos: HH/custo previstos e realizados, e documentos', () => {
  const f = projectFacts([CHS, BML]).cards;
  assert.ok(Math.abs((f.HH_Previsto as number) - (2297.4 + 6401.96)) < 1e-9);
  assert.ok(Math.abs((f.HH_Realizado as number) - (5539.46 + 7088.06)) < 1e-9);
  assert.ok(Math.abs((f.Custo_Previsto as number) - (1295604.58 + 716347.63)) < 1e-6);
  assert.ok(Math.abs((f.Custo_Realizado as number) - (151575.68 + 491803.46)) < 1e-6);
  const docs = enrichDocuments([CHS, BML]);
  assert.equal(docs.length, 5);
});
test('percentual consolidado é razão das somas, nunca média de percentuais', () => {
  const f = projectFacts([CHS, BML]).cards;
  const esperado = (5539.46 + 7088.06) / (2297.4 + 6401.96) * 100;
  assert.ok(Math.abs((f.ConsumoHH as number) - esperado) < 1e-9);
  const mediaSimples = (5539.46 / 2297.4 * 100 + 7088.06 / 6401.96 * 100) / 2;
  assert.ok(Math.abs((f.ConsumoHH as number) - mediaSimples) > 1);
  const custo = (151575.68 + 491803.46) / (1295604.58 + 716347.63) * 100;
  assert.ok(Math.abs((f.ConsumoCusto as number) - custo) < 1e-9);
});
test('disciplinas iguais consolidam entre projetos e cada disciplina soma ao total', () => {
  const d = projectFacts([CHS, BML]).disciplines;
  const el = d.find(x => x.chave === 'ELECTRICAL')!;
  assert.equal(el.HH_Previsto, 500 + 700);
  assert.ok(d.some(x => x.chave === 'PIPING') && d.some(x => x.chave === 'CIVIL'));
  assert.ok(Math.abs(d.reduce((a, x) => a + (x.HH_Previsto ?? 0), 0) - (2297.4 + 6401.96)) < 1e-9);
});
test('projeto sem HH informado ou sem timesheet não anula o consolidado dos demais', () => {
  const vazio = rel('V', 'VAZIO', { plannedHh: null, cost: null, realHh: null, realCusto: null, disc: [], docs: [] });
  const f = projectFacts([CHS, vazio]).cards;
  assert.equal(f.HH_Previsto, 2297.4); assert.equal(f.HH_Realizado, 5539.46);
  assert.equal(projectFacts([vazio]).cards.HH_Previsto, null);   // sozinho continua "desconhecido"
});
test('mesmo código documental em projetos diferentes são documentos diferentes (sem dedupe nem mistura de realizado)', () => {
  const docs = enrichDocuments([CHS, BML]).filter(d => d.codigo === '000-PR-001');
  assert.equal(docs.length, 2);
  assert.notEqual(docs[0].rowId, docs[1].rowId);
  assert.deepEqual(docs.map(d => d.hhRealizado).sort(), [3, 7]);   // cada um com o realizado do PRÓPRIO projeto
});
test('status dos documentos consolidados: total = soma das 5 categorias', () => {
  const s = statusCounts(enrichDocuments([CHS, BML]));
  assert.equal(s.total, 5); assert.equal(s.soma, 5); assert.equal(s.diferenca, 0);
  assert.equal(s.cancelados, 1); assert.equal(s.emitidos, 1); assert.equal(s.semPrazo, 3);
});
