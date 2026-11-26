import test from 'node:test';
import assert from 'node:assert/strict';
import { controleHhCusto, recortarUniverso, desvioPercentual, tomDesvio, formatPct, grupoResponsavel, mapaGestao, NENHUM_DOCUMENTO,
  motivoDesvioIndisponivel, criarResolvedorDisciplina, chaveDisciplinaHh,
  type FiltrosHhCusto, type ResponsaveisReport } from '../hhCustoControle';
import type { FactsReport } from '../pmoFacts';
import { docKey, type PmoDocument, type PmoEntry } from '../pmoData';
import { PRODUCTION_NAV, folhasProducao } from '@/features/production/productionRoutes';
import { alternarTodosFiltrados, chavesDoLote, contadores, filtrarCandidatos, mensagemResultado, FILTROS_INICIAIS,
  type DocumentoCandidato } from '@/features/planning/documentosGestao/documentosGestaoModel';

const P = 'b.00000000-0000-0000-0000-000000000001';
const doc = (codigo: string | null, disciplina: string, hhPrevisto: number | null, hhRealizado: number | null, custoPrevisto: number | null, custoRealizado: number | null,
  inicioSemana = '2026-10-04'): PmoDocument => ({
  rowId: `ldp-${codigo ?? 'sem'}-${disciplina}`, projectId: P, codigo, titulo: `Título ${codigo}`, disciplina, disciplinaCanonica: disciplina,
  hhPrevisto, custoPrevisto, hhRealizado, custoRealizado, inicioSemana, dataBaseline: null, dataReprogramada: null, dataEmissao: null,
  statusInterno: null, statusCliente: null, situacaoEmissao: null, avancoFisico: null, classeFisica: 'SEM_DADO', cancelado: false, emitido: false,
  statusPrazo: 'SEM_DADO', dataAlvo: null, fonteDataAlvo: 'SEM_DADO', correlacaoCronograma: 'SEM_MATCH', statusDocumento: 'SEM_PRAZO',
  percentFisicoLdp: null, fonteAvancoFisico: 'SEM_DADO' });
const lanc = (codigo: string | null, hh: number): PmoEntry => ({ rowId: `ts-${codigo}-${hh}`, projectId: P, documentoCodigo: codigo, documento: null,
  disciplina: null, dataLancamento: '2026-10-05', hhRealizado: hh, custoRealizado: null, custoCalculado: hh * 10 });

function report(): FactsReport {
  return { projectId: P, nomeProjeto: 'PROJ', tipoLdp: 2, dataReferencia: '2026-10-29', weeklyAtividades: [], cronograma: [],
    documentos: [
      doc('GES-001', 'GESTÃO', 100, 80, 1000, 800),
      doc('ENG-001', 'ELÉTRICA', 200, 240, 2000, null),
      doc('ENG-002', 'PROCESSO', 0, 10, null, 50),
      doc(null, 'PROCESSO', 50, 5, 500, 40),
    ],
    lancamentos: [lanc('GES-001', 80), lanc('ENG-001', 240), lanc(null, 7), lanc('FORA-999', 3)],
    hhRealizado: { total: 342, porDisciplina: [], fonte: 'teste', custoOficial: true, custoTotal: 890 },
  } as FactsReport;
}
const vazio = (): FiltrosHhCusto => ({ semanas: new Set(), disciplinas: new Set(), documentos: new Set(), responsaveis: new Set() });
const gestao = mapaGestao([{ projectId: P, schemaAvailable: true, documentKeys: [' ges-001 '] }]);
const resp = (): ResponsaveisReport[] => [{ projectId: P, crOs: 'X', fonte: 't', responsaveis: [
  { pessoa: 'EVERTON', tipo: 'PACOTE', empresa: 'ETS ENGENHARIA E PROJETOS', pacotes: ['ETS001'] },
  { pessoa: 'MARIA', tipo: 'PESSOA', empresa: null, pacotes: [] },
  { pessoa: 'JOSE', tipo: 'PACOTE_SEM_EMPRESA', empresa: null, pacotes: ['P2'] }],
  linhas: [
    { pessoa: 'EVERTON', documentoCodigo: 'ENG-001', disciplina: null, data: '2026-10-05', hh: 200, lancamentos: 3 },
    { pessoa: 'MARIA', documentoCodigo: 'ENG-001', disciplina: null, data: '2026-10-20', hh: 40, lancamentos: 1 },
    { pessoa: 'MARIA', documentoCodigo: 'GES-001', disciplina: null, data: '2026-10-05', hh: 80, lancamentos: 2 },
    { pessoa: 'JOSE', documentoCodigo: 'ENG-002', disciplina: null, data: '2026-10-06', hh: 10, lancamentos: 1 },
    { pessoa: 'MARIA', documentoCodigo: null, disciplina: 'PROCESSO', data: '2026-10-05', hh: 7, lancamentos: 1 }] }];

test('Universos: partição exata por documento (Gestão só os marcados; Engenharia o resto, inclusive sem código); sem dupla contagem', () => {
  const r = [report()];
  const eng = recortarUniverso(r, 'engenharia', gestao)[0], ges = recortarUniverso(r, 'gestao', gestao)[0];
  assert.deepEqual(ges.documentos.map(d => d.codigo), ['GES-001']);
  assert.deepEqual(eng.documentos.map(d => d.codigo), ['ENG-001', 'ENG-002', null]);
  // Lançamentos sem documento / fora da LDP não entram em nenhum universo.
  assert.deepEqual(eng.lancamentos.map(l => l.documentoCodigo), ['ENG-001']);
  assert.deepEqual(ges.lancamentos.map(l => l.documentoCodigo), ['GES-001']);
  const e = controleHhCusto(r, 'engenharia', gestao, vazio(), []).indicadores;
  const g = controleHhCusto(r, 'gestao', gestao, vazio(), []).indicadores;
  assert.equal(g.hhPlanejado, 100); assert.equal(g.hhRealizado, 80);
  assert.equal(e.hhPlanejado, 250); assert.equal(e.hhRealizado, 255);
  assert.equal(e.hhPlanejado! + g.hhPlanejado!, 350, 'Σ universos = Σ documentos da LDP');
  // Original preserva os fatos oficiais do projeto (Timesheet completo), não a soma por documento.
  assert.equal(controleHhCusto(r, 'original', gestao, vazio(), []).indicadores.hhRealizado, 342);
});

test('Sem classificação (persistência pendente): Gestão vazia e Engenharia = todos — nunca inventa vínculo', () => {
  const nada = mapaGestao([{ projectId: P, schemaAvailable: false, documentKeys: ['GES-001'] }]);
  assert.equal(recortarUniverso([report()], 'gestao', nada)[0].documentos.length, 0);
  assert.equal(recortarUniverso([report()], 'engenharia', nada)[0].documentos.length, 4);
});

test('% consumido sem cap e N/D com planejado zero/NULL; saldo previsto − realizado; custo NULL não vira zero', () => {
  const r = controleHhCusto([report()], 'engenharia', gestao, vazio(), []);
  const eng1 = r.documentosTabela.find(d => d.codigo === 'ENG-001')!;
  assert.equal(eng1.SaldoHH, -40);
  assert.equal(eng1.Custo_Realizado, null); assert.equal(eng1.SaldoCusto, null);
  const so2 = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), documentos: new Set([eng1.rowId]) }, []).indicadores;
  assert.equal(so2.consumoHh, 120, 'sem limite de 100%');
  const zero = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), documentos: new Set(r.documentosTabela.filter(d => d.codigo === 'ENG-002').map(d => d.rowId)) }, []).indicadores;
  assert.equal(zero.consumoHh, null); assert.equal(formatPct(zero.consumoHh), 'N/D');
  assert.equal(zero.custoPlanejado, null); assert.equal(zero.consumoCusto, null);
  assert.ok(!Number.isNaN(zero.saldoHh));
});

test('Desvio = 100×(Prev−Real)/Prev: positivo verde, negativo vermelho, zero neutro, denominador 0/NULL = N/D', () => {
  assert.equal(desvioPercentual(100, 80), 20); assert.equal(tomDesvio(20), 'positivo');
  assert.equal(desvioPercentual(200, 240), -20); assert.equal(tomDesvio(-20), 'negativo');
  assert.equal(desvioPercentual(100, 100), 0); assert.equal(tomDesvio(0), 'neutro');
  assert.equal(desvioPercentual(0, 10), null); assert.equal(desvioPercentual(null, 10), null); assert.equal(tomDesvio(null), 'nd');
  assert.equal(formatPct(-20), '-20,0%');
  const disc = controleHhCusto([report()], 'engenharia', gestao, vazio(), []).disciplinasTabela;
  assert.equal(disc.find(d => d.chave === 'ELÉTRICA')!.DesvioHH, -20);
});

test('Cross-filter: responsável → documentos; documento → responsáveis; disciplina → ambos; semana pela data do lançamento', () => {
  const ets = grupoResponsavel(resp()[0].responsaveis[0], 'EVERTON').chave;
  const porResp = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), responsaveis: new Set([ets]) }, resp());
  assert.deepEqual(porResp.documentosTabela.map(d => d.codigo), ['ENG-001']);
  const engDoc = porResp.documentosTabela[0].rowId;
  const porDoc = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), documentos: new Set([engDoc]) }, resp());
  assert.deepEqual(porDoc.responsaveisTabela.map(x => x.rotulo).sort(), ['ETS ENGENHARIA E PROJETOS', 'MARIA']);
  const porDisc = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), disciplinas: new Set(['PROCESSO']) }, resp());
  assert.deepEqual(porDisc.responsaveisTabela.map(x => x.rotulo), ['JOSE']);
  assert.ok(porDisc.documentosTabela.every(d => d.disciplinaRotulo === 'PROCESSO'));
  const semana = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), semanas: new Set(['2026-10-18']) }, resp());
  assert.deepEqual(semana.responsaveisTabela.map(x => x.rotulo), ['MARIA']);
  // Interseção: responsável sem documento no filtro → conjunto vazio (nunca "sem filtro").
  const nenhum = controleHhCusto([report()], 'engenharia', gestao, { ...vazio(), responsaveis: new Set([ets]), documentos: new Set(['x']) }, resp());
  assert.equal(nenhum.documentosTabela.length, 0);
  assert.ok(NENHUM_DOCUMENTO.length > 0);
});

test('Responsável: Gestão não recebe linhas de Engenharia; sem documento só na tela original; pacote exibe empresa do cadastro', () => {
  const g = controleHhCusto([report()], 'gestao', gestao, vazio(), resp());
  assert.deepEqual(g.responsaveisTabela.map(x => [x.rotulo, x.hh]), [['MARIA', 80]]);
  const o = controleHhCusto([report()], 'original', gestao, vazio(), resp());
  assert.equal(o.reconciliacaoResponsaveis.hhPessoasSemDoc, 7);
  const ets = o.responsaveisTabela.find(x => x.chave.startsWith('EMPRESA:'))!;
  assert.equal(ets.rotulo, 'ETS ENGENHARIA E PROJETOS'); assert.deepEqual(ets.pessoas, ['EVERTON']);
  const jose = grupoResponsavel(resp()[0].responsaveis[2], 'JOSE');
  assert.equal(jose.rotulo, 'JOSE'); assert.match(jose.pendencia!, /Empresa não identificada/);
  assert.equal(grupoResponsavel(undefined, 'ALGUEM').pendencia, null);
});

test('Planejado nunca duplica por responsável: 2 pessoas no mesmo documento mantêm o HH planejado do documento; reconciliação explícita', () => {
  const r = controleHhCusto([report()], 'engenharia', gestao, vazio(), resp());
  const eng1 = r.documentosTabela.find(d => d.codigo === 'ENG-001')!;
  assert.equal(eng1.HH_Previsto, 200);
  assert.equal(r.indicadores.hhPlanejado, 250);
  assert.equal(r.reconciliacaoResponsaveis.hhPessoasDocs, 250);
  assert.equal(r.reconciliacaoResponsaveis.hhDocumentos, 255);
  assert.equal(r.reconciliacaoResponsaveis.diferenca, 5);
  assert.equal(r.pendencias!.linhasSemCodigo, 1);
});

test('Rotas: Engenharia e Gestão usam o MESMO renderer (hh-custo) e a MESMA permissão gerencial da tela original', () => {
  const folhas = folhasProducao();
  const original = folhas.find(f => f.slug === 'controle-hh-custo')!;
  for (const [slug, universo] of [['controle-hh-custo-engenharia', 'engenharia'], ['controle-hh-custo-gestao', 'gestao']] as const) {
    const f = folhas.find(x => x.slug === slug)!;
    assert.deepEqual(f.page, { tipo: 'hh-custo', universo });
    assert.deepEqual(f.permissionKeys, original.permissionKeys);
    assert.equal(f.containsCostData, true);
  }
  assert.deepEqual(original.page, { tipo: 'hh-custo' });
  assert.ok(PRODUCTION_NAV.length > 0);
});

const cand = (codigo: string, gestao: boolean, titulo = 'Relatório', disciplina = 'Elétrica'): DocumentoCandidato =>
  ({ documentKey: codigo.toUpperCase(), codigo, titulo, disciplina, status: 'EM ELABORAÇÃO', situacao: null, atividade: false, linhas: 1, gestao });

test('Documentos de Gestão (Planejador): busca sem acento, seleção de todos os filtrados, lote só com chaves atuais e resultado honesto', () => {
  const docs = [cand('A-1', false, 'Memorial de cálculo'), cand('A-2', true, 'Plano de gestão', 'Gestão'), cand('A-3', false, 'Calculo X')];
  assert.deepEqual(filtrarCandidatos(docs, { ...FILTROS_INICIAIS, busca: 'calculo' }).map(d => d.codigo), ['A-1', 'A-3']);
  assert.deepEqual(filtrarCandidatos(docs, { ...FILTROS_INICIAIS, classe: 'gestao' }).map(d => d.codigo), ['A-2']);
  assert.deepEqual(filtrarCandidatos(docs, { ...FILTROS_INICIAIS, disciplina: 'gestao' }).map(d => d.codigo), ['A-2']);
  const filtrados = filtrarCandidatos(docs, { ...FILTROS_INICIAIS, busca: 'calculo' });
  let sel = alternarTodosFiltrados(new Set(['A-2']), filtrados);
  assert.deepEqual([...sel].sort(), ['A-1', 'A-2', 'A-3']);
  sel = alternarTodosFiltrados(sel, filtrados);
  assert.deepEqual([...sel], ['A-2']);
  assert.deepEqual(chavesDoLote(docs, new Set(['A-1', 'OUTRO-PROJETO'])), ['A-1']);
  const c = contadores(docs, new Set(['A-1', 'A-2']));
  assert.deepEqual([c.total, c.gestao, c.engenharia, c.selecionados, c.selecionadosParaGestao, c.selecionadosParaEngenharia], [3, 1, 2, 2, 1, 1]);
  assert.equal(mensagemResultado({ target: 'GESTAO', solicitados: 3, alterados: 2, jaEstavam: 1, foraDaLdp: [], invalidos: 0 }),
    '2 enviado(s) para Documentos de Gestão · 1 já estava(m) nesse grupo.');
});

// ───────────── Reabertura 02 (30/10/2026): casos reais reproduzidos com dados sintéticos de mesma forma ─────────────
const Q = 'b.00000000-0000-0000-0000-000000000002';
// Mesma forma que o backend devolve (HhCustoDisciplinaRules.Construir): origem em chave sem acento → nome PT do projeto.
const NOMES = [
  ['PROCESS', 'PROCESSO', 'PROCESS', 'NORMA'], ['PROCESSO', 'PROCESSO', 'PROCESS', 'NORMA'], ['PROCESSOS', 'PROCESSO', 'PROCESS', 'NORMA'],
  ['PLANNING AND CONTROL', 'PLANEJAMENTO E CONTROLE', 'PLANNING AND CONTROL', 'MAPA_PT'], ['PLANEJAMENTO E CONTROLE', 'PLANEJAMENTO E CONTROLE', 'PLANNING AND CONTROL', 'MAPA_PT'],
  ['DIGITAL SYSTEM', 'SISTEMAS DIGITAIS', 'DIGITAL SYSTEMS', 'NORMA'], ['DIGITAL SYSTEMS', 'SISTEMAS DIGITAIS', 'DIGITAL SYSTEMS', 'NORMA'],
  ['SISTEMAS DIGITAIS', 'SISTEMAS DIGITAIS', 'DIGITAL SYSTEMS', 'NORMA'], ['SISTEMAS', 'SISTEMAS DIGITAIS', 'DIGITAL SYSTEMS', 'NORMA'],
  ['DOC CONTROL', 'DOC CONTROL', 'DOC CONTROL', 'NORMA'], ['CIVIL', 'CIVIL', 'CIVIL', 'NORMA'],
].map(([origem, nome, canonico, fonte]) => ({ origem, nome, canonico, fonte }));
const docQ = (codigo: string, disciplina: string, hhPrevisto: number | null, hhRealizado: number | null, extra: Partial<PmoDocument> = {}): PmoDocument =>
  ({ ...doc(codigo, disciplina, hhPrevisto, hhRealizado, hhPrevisto === null ? null : hhPrevisto * 100, hhRealizado === null ? null : hhRealizado * 100),
    projectId: Q, rowId: `q-${codigo}`, ...extra });
const lancQ = (codigo: string | null, hh: number, disciplina: string | null): PmoEntry =>
  ({ ...lanc(codigo, hh), rowId: `q-${codigo}-${hh}-${disciplina}`, projectId: Q, disciplina, custoCalculado: hh * 100 });

function reportQ(): FactsReport {
  return { projectId: Q, nomeProjeto: 'BML', tipoLdp: 1, dataReferencia: '2026-10-29', weeklyAtividades: [], cronograma: [],
    documentos: [
      docQ('X-212-CI4-DE-001', 'CIVIL', 16, 27), docQ('X-212-CI4-DE-002', 'CIVIL', 20, 10),
      docQ('X-212-DC1-LD-001', 'DOC CONTROL', 80, 8), docQ('X-212-PC2-DE-001', 'PROCESS', 30, 16),
      docQ('X-212-3DM-3D-001', 'SISTEMAS DIGITAIS', 12, 0), docQ('X-212-3DM-3D-002', 'SISTEMAS', 12, 38),
      docQ('X-000-PL0-AT-001', 'PLANNING AND CONTROL', 19, 11, { docTypeCode: 'AT', isActivity: true, statusDocumento: 'ATIVIDADE' }),
    ],
    lancamentos: [lancQ('X-212-CI4-DE-001', 25, 'PROCESS'), lancQ('X-212-CI4-DE-001', 2, 'DOC CONTROL'), lancQ('X-212-CI4-DE-002', 10, 'DOC CONTROL'),
      lancQ('X-212-DC1-LD-001', 8, 'DOC CONTROL'), lancQ('X-212-PC2-DE-001', 16, 'PROCESS'), lancQ('X-212-3DM-3D-002', 38, 'DIGITAL SYSTEM'),
      lancQ('X-000-PL0-AT-001', 11, 'PLANNING AND CONTROL')],
    planning: { plannedHhTotal: 189, plannedHhByDiscipline: [{ discipline: 'CIVIL', plannedHh: 36 }, { discipline: 'DOC CONTROL', plannedHh: 80 },
      { discipline: 'PROCESS', plannedHh: 30 }, { discipline: 'SISTEMAS DIGITAIS', plannedHh: 24 }, { discipline: 'Planning and Control', plannedHh: 19 }],
      plannedCostByDiscipline: [], disciplineTotal: null, undisciplinedHh: null, reconciliationDifference: null, source: '', coverage: '', diagnostics: [],
      plannedCostTotal: null, documentPlannedCost: null, knownDocumentCostSubtotal: null, costSource: '', costCoverage: '', costDocuments: 0, documentsWithCost: 0 },
    hhRealizado: { total: 110, porDisciplina: [{ disciplina: 'CIVIL', hh: 37 }, { disciplina: 'DOC CONTROL', hh: 8 }, { disciplina: 'PROCESSO', hh: 16 },
      { disciplina: 'Sistemas Digitais', hh: 38 }, { disciplina: 'PLANEJAMENTO E CONTROLE', hh: 11 }], fonte: 't', custoOficial: true, custoTotal: 11000 },
    actual: { total: { hh: 110, cost: null, knownCostSubtotal: null, knownCalculatedCostSubtotal: 11000, entries: 7, missingHh: 0, missingCost: 0, missingCalculatedCost: 0 },
      source: 't', byDiscipline: [], byDocument: [],
      historicalMeasurement: { projectId: Q, complementHours: 490, complementCost: 0, excessHours: 0, excessCost: 0, diagnostics: [],
        disciplines: [{ packageId: 2, confirmationId: 1, package: 'ETS001', professional: 'Everton Eustaquio', discipline: 'CIVIL', confirmedHours: 710, confirmedCost: 0,
          timesheetHours: 25, timesheetCost: null, confirmedAtUtc: '2026-10-13', confirmedBy: 'x', origin: 'x',
          balance: { recognizedHours: 25, recognizedCost: 0, complementHours: 490, complementCost: 0, excessHours: 0, excessCost: 0, diagnostics: [] } }],
        documents: [{ packageId: 2, confirmationId: 1, discipline: 'CIVIL', document: 'X-212-CI4-DE-001', hours: 245, cost: 0, indicatorDiscipline: 'CIVIL' },
          { packageId: 2, confirmationId: 1, discipline: 'CIVIL', document: 'X-212-CI4-DE-002', hours: 245, cost: 0, indicatorDiscipline: 'CIVIL' }] } },
  } as unknown as FactsReport;
}
const respQ = (): ResponsaveisReport[] => [{ projectId: Q, crOs: 'X', fonte: 't', disciplinas: NOMES, responsaveis: [
  { pessoa: 'Everton Eustaquio', tipo: 'PACOTE', empresa: 'ETS', pacotes: ['ETS001'] },
  { pessoa: 'Elisane Barros', tipo: 'PESSOA', empresa: null, pacotes: [] },
  { pessoa: 'Christian Santana', tipo: 'PESSOA', empresa: null, pacotes: [] }],
  linhas: [
    { pessoa: 'Everton Eustaquio', documentoCodigo: 'X-212-CI4-DE-001', disciplina: 'PROCESS', data: '2026-06-12', hh: 25, lancamentos: 3 },
    { pessoa: 'Elisane Barros', documentoCodigo: 'X-212-CI4-DE-001', disciplina: 'DOC CONTROL', data: '2026-06-11', hh: 2, lancamentos: 4 },
    { pessoa: 'Elisane Barros', documentoCodigo: 'X-212-CI4-DE-002', disciplina: 'DOC CONTROL', data: '2026-06-11', hh: 10, lancamentos: 2 },
    { pessoa: 'Elisane Barros', documentoCodigo: 'X-212-DC1-LD-001', disciplina: 'DOC CONTROL', data: '2026-06-11', hh: 8, lancamentos: 2 },
    { pessoa: 'Christian Santana', documentoCodigo: 'X-212-3DM-3D-002', documentoCodigoOriginal: 'X-000-3D0-AT-001', correcao: 'DE_PARA',
      disciplina: 'DIGITAL SYSTEM', data: '2026-06-05', hh: 38, lancamentos: 14 },
    { pessoa: 'Christian Santana', documentoCodigo: 'X-000-PL0-AT-001', disciplina: 'DIGITAL SYSTEM', data: '2026-06-05', hh: 11, lancamentos: 1 }] }];
const semGestao = new Map<string, Set<string>>();

test('ETS: empresa do cadastro soma Timesheet + medição histórica confirmada (componentes explícitos); semana não esconde a medição', () => {
  const r = controleHhCusto([reportQ()], 'original', semGestao, vazio(), respQ());
  const ets = r.responsaveisTabela.find(x => x.chave === 'EMPRESA:ETS')!;
  assert.equal(ets.rotulo, 'ETS');
  assert.deepEqual([ets.hh, ets.hhTimesheet, ets.hhMedicao], [515, 25, 490]);
  assert.equal(r.reconciliacaoResponsaveis.hhMedicaoHistorica, 490);
  const semana = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), semanas: new Set(['2026-06-07']) }, respQ());
  assert.equal(semana.responsaveisTabela.find(x => x.chave === 'EMPRESA:ETS')!.hh, 25);
  assert.equal(semana.reconciliacaoResponsaveis.medicaoForaDaSemana, 490, 'medição sem data informada fora do filtro de semana');
});

test('Sistemas Digitais: 2 documentos reais (SISTEMAS/SISTEMAS DIGITAIS unificados), AT fora da contagem, De/Para leva a pessoa ao documento', () => {
  const r = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), disciplinas: new Set(['SISTEMAS DIGITAIS']) }, respQ());
  assert.deepEqual(r.documentosTabela.map(d => d.codigo), ['X-212-3DM-3D-001', 'X-212-3DM-3D-002']);
  assert.equal(r.contagem.documentos, 2); assert.equal(r.contagem.atividades, 0);
  const chr = r.responsaveisTabela.find(x => x.rotulo === 'Christian Santana')!;
  assert.deepEqual([chr.hh, chr.documentos], [38, 1]);
  assert.deepEqual(chr.corrigidos, ['X-000-3D0-AT-001 → X-212-3DM-3D-002']);
  const tudo = controleHhCusto([reportQ()], 'original', semGestao, vazio(), respQ());
  assert.equal(tudo.contagem.atividades, 1, 'AT contada à parte, nunca como documento');
  assert.equal(tudo.disciplinasTabela.filter(d => d.chave === 'SISTEMAS DIGITAIS').length, 1);
});

test('Clique em DOC CONTROL (qualquer dos dois gráficos de desvio): só documentos DOC CONTROL e só quem lançou neles; toggle restaura', () => {
  const base = controleHhCusto([reportQ()], 'original', semGestao, vazio(), respQ());
  const chave = base.disciplinasTabela.find(d => d.Disciplina === 'DOC CONTROL')!.chave; // mesma chave nos dois painéis (HH e custo)
  const f = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), disciplinas: new Set([chave]) }, respQ());
  assert.deepEqual(f.documentosTabela.map(d => d.codigo), ['X-212-DC1-LD-001'], 'lançamentos DOC CONTROL em documentos CIVIL não trazem os CIVIL');
  assert.deepEqual(f.disciplinasTabela.map(d => d.Disciplina), ['DOC CONTROL']);
  assert.deepEqual(f.responsaveisTabela.map(x => [x.rotulo, x.hh]), [['Elisane Barros', 8]]);
  // Cadeia disciplina → documento → pessoa.
  const porDoc = controleHhCusto([reportQ()], 'original', semGestao,
    { ...vazio(), disciplinas: new Set([chave]), documentos: new Set([f.documentosTabela[0].rowId]) }, respQ());
  assert.deepEqual(porDoc.responsaveisTabela.map(x => x.rotulo), ['Elisane Barros']);
  // Desmarcar = estado inicial.
  assert.equal(controleHhCusto([reportQ()], 'original', semGestao, vazio(), respQ()).documentosTabela.length, base.documentosTabela.length);
});

test('CI4-DE-001: documento mostra SÓ a sua disciplina; HH por responsável no documento; disciplina do lançamento é diagnóstico', () => {
  const ci4 = docKey(Q, 'X-212-CI4-DE-001');
  const r = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), documentos: new Set([ci4]) }, respQ());
  assert.deepEqual(r.disciplinasTabela.map(d => d.Disciplina), ['CIVIL']);
  assert.deepEqual(r.documentosTabela.map(d => d.disciplinaRotulo), ['CIVIL']);
  const linhas = Object.fromEntries(r.responsaveisTabela.map(x => [x.rotulo, x]));
  assert.equal(linhas['ETS'].hhTimesheet, 25); assert.deepEqual(linhas['ETS'].lancadoComo, ['PROCESSO']);
  assert.equal(linhas['Elisane Barros'].hh, 2); assert.deepEqual(linhas['Elisane Barros'].lancadoComo, ['DOC CONTROL']);
  // Desvio do documento existe (previsto válido): 100×(16−27)/16.
  assert.equal(Math.round(r.disciplinasTabela[0].DesvioHH! * 100) / 100, -68.75);
});

test('Elisane: filtro por pessoa não esconde HH; N/D só sem denominador e sempre com explicação visível', () => {
  const r = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), responsaveis: new Set(['PESSOA:Elisane Barros']) }, respQ());
  assert.equal(r.responsaveisTabela.find(x => x.rotulo === 'Elisane Barros')!.hh, 20);
  assert.ok(r.disciplinasTabela.every(d => d.DesvioHH !== null), 'documentos com previsto válido nunca caem em N/D');
  assert.equal(motivoDesvioIndisponivel(0, 2, 'hh', v => String(v)), '2 HH realizadas · 0 HH planejadas — desvio % indisponível');
  assert.equal(motivoDesvioIndisponivel(null, 2, 'custo', v => `R$ ${v}`), 'R$ 2 realizado · sem orçamento — desvio % indisponível');
  assert.equal(motivoDesvioIndisponivel(16, 27, 'hh', v => String(v)), null);
});

test('Normalização PT antes de agrupar: PROCESS/PROCESSO, PLANNING AND CONTROL/Planejamento e Controle, Systems; acento/caixa; não reconhecido rastreado', () => {
  const { resolver, naoReconhecidos } = criarResolvedorDisciplina(NOMES);
  assert.equal(resolver('process'), 'PROCESSO'); assert.equal(resolver(' Processo '), 'PROCESSO');
  assert.equal(resolver('Planning and Control'), 'PLANEJAMENTO E CONTROLE'); assert.equal(resolver('planejamento e controle'), 'PLANEJAMENTO E CONTROLE');
  assert.equal(resolver('Sistemas Digitais'), 'SISTEMAS DIGITAIS'); assert.equal(resolver('DIGITAL SYSTEM'), 'SISTEMAS DIGITAIS');
  assert.equal(chaveDisciplinaHh('Tubulação  Industrial'), 'TUBULACAO INDUSTRIAL');
  assert.equal(resolver('PLANING AND CONTROL'), 'PLANING AND CONTROL', 'sem cadastro: mantém e registra, nunca adivinha');
  assert.deepEqual([...naoReconhecidos], ['PLANING AND CONTROL']);
  const r = controleHhCusto([reportQ()], 'original', semGestao, vazio(), respQ());
  const nomes = r.disciplinasTabela.map(d => d.Disciplina);
  assert.ok(!nomes.includes('PROCESS') && nomes.includes('PROCESSO'));
  assert.equal(nomes.filter(n => n === 'PLANEJAMENTO E CONTROLE').length, 1);
  assert.equal(r.disciplinasTabela.find(d => d.Disciplina === 'SISTEMAS DIGITAIS')!.HH_Previsto, 24);
  // Seleção pelo nome unificado filtra as duas grafias de origem.
  const p = controleHhCusto([reportQ()], 'original', semGestao, { ...vazio(), disciplinas: new Set(['PROCESSO']) }, respQ());
  assert.deepEqual(p.documentosTabela.map(d => d.codigo), ['X-212-PC2-DE-001']);
});
