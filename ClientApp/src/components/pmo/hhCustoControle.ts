import { code, docKey, day, addDays, rotuloDe, sumSkipNull, type PmoFilters, type PmoEntry } from './pmoData';
import { factKey, consumption, balance, projectFacts, ehAtividade, type FactsReport } from './pmoFacts';
import { financeiroCanonico } from './pmoFinanceiro';

/**
 * Controle de HH / Custo — UM cálculo para as três telas (original, Engenharia, Gestão).
 *  • Universo: Gestão = documentos cujo NA_Document_Number normalizado (code) está na classificação do planejador
 *    (/api/planning/management-documents, Project_ID_ACC + chave). Engenharia = TODO o resto da LDP atual (inclusive linhas
 *    sem código, que não podem ser classificadas). Partição exata por documento: nenhum documento entra nos dois.
 *  • Nos universos, lançamentos sem documento / fora da LDP não pertencem a nenhum dos dois: ficam fora e são EXPLICITADOS
 *    (pendências), nunca atribuídos a um documento. A tela original continua com os fatos oficiais do projeto inteiro.
 *  • Valores: sempre financeiroCanonico (mesma regra oficial de HH/custo, NULL ≠ 0); universos forçam a soma por documento.
 *  • Disciplina (30/10/2026): nomes unificados ANTES de agrupar/filtrar pelo resolvedor do backend (Norma do projeto > cadastro
 *    canônico/mapa PT > original). Lançamento de documento da LDP herda a disciplina DO DOCUMENTO; a disciplina do lançamento
 *    (atributo da pessoa) fica só como diagnóstico. Rótulo não reconhecido continua como está e é listado.
 *  • Responsável: Timesheet corrigido (De/Para) por pessoa × documento × dia (/api/production/hh-custo/responsaveis) + complemento
 *    de MEDIÇÃO HISTÓRICA CONFIRMADA do pacote (o mesmo já somado ao HH oficial do documento). Planejado NUNCA é repetido por pessoa.
 */
export type UniversoHhCusto = 'original' | 'engenharia' | 'gestao';

export interface ClassificacaoGestao { projectId: string; schemaAvailable: boolean; documentKeys: string[] }

export interface ResponsavelLinha { pessoa: string; documentoCodigo: string | null; disciplina: string | null; data: string | null; hh: number; lancamentos: number;
  documentoCodigoOriginal?: string | null; correcao?: string | null }
export type TipoResponsavel = 'PESSOA' | 'PACOTE' | 'PACOTE_SEM_EMPRESA' | 'AMBIGUO';
export interface ResponsavelInfo { pessoa: string; tipo: TipoResponsavel; empresa: string | null; pacotes: string[] }
export interface DisciplinaNome { origem: string; nome: string; canonico: string | null; fonte: string }
export interface ResponsaveisReport { projectId: string; crOs: string; fonte: string; linhas: ResponsavelLinha[]; responsaveis: ResponsavelInfo[];
  disciplinas?: DisciplinaNome[]; diagnosticosDisciplina?: string[] }

export interface FiltrosHhCusto extends PmoFilters { responsaveis: Set<string> }

/** Documento nunca casa com esta chave: filtro de responsável sem nenhum documento correspondente = conjunto vazio, não "sem filtro". */
export const NENHUM_DOCUMENTO = '__HH_CUSTO_NENHUM__';

export const TITULO_UNIVERSO: Record<UniversoHhCusto, string> = {
  original: 'Controle de HH / Custo',
  engenharia: 'Controle de HH / Custo — Engenharia',
  gestao: 'Controle de HH / Custo — Gestão',
};

/** Mesma chave do backend (HhCustoDisciplinaRules.Chave): sem acento, maiúsculas, espaços colapsados. */
export const chaveDisciplinaHh = (v: string | null | undefined) =>
  (v ?? '').normalize('NFD').replace(/\p{Mn}/gu, '').normalize('NFC').toUpperCase().split(/\s+/).filter(Boolean).join(' ');

/** Resolvedor do projeto: só o que o backend devolveu (cadastros). Sem entrada = rótulo original + registro em naoReconhecidos. */
export function criarResolvedorDisciplina(nomes: readonly DisciplinaNome[] | undefined) {
  const mapa = new Map((nomes ?? []).map(n => [chaveDisciplinaHh(n.origem), n.nome.trim()]));
  const naoReconhecidos = new Set<string>();
  const resolver = (v: string | null | undefined): string | null => {
    const limpo = (v ?? '').trim();
    if (!limpo) return null;
    const nome = mapa.get(chaveDisciplinaHh(limpo));
    if (nome) return nome;
    if (mapa.size) naoReconhecidos.add(limpo);
    return limpo;
  };
  return { resolver, naoReconhecidos };
}

const doc3 = (codigo: string | null) => code(codigo).split('-')[2] ?? '';

/**
 * Unifica as disciplinas de cada relatório ANTES de qualquer agrupamento, filtro ou cruzamento (documentos, lançamentos,
 * fatos de planejado/realizado por disciplina). Lançamento herda a disciplina do seu documento; sem documento na LDP, a sigla do
 * código (DOC3) comprovada por documentos da própria LDP; só então a do lançamento. `disciplinaLancamento` guarda o original.
 */
export function normalizarDisciplinas(reports: FactsReport[], responsaveis: ResponsaveisReport[]) {
  const naoReconhecidos = new Set<string>();
  const porProjeto = new Map<string, Map<string, string | null>>();
  const normalizados = reports.map(r => {
    const res = criarResolvedorDisciplina(responsaveis.find(x => x.projectId === r.projectId)?.disciplinas);
    const n = res.resolver;
    const documentos = r.documentos.map(d => ({ ...d, disciplinaCanonica: n(d.disciplinaCanonica ?? d.disciplina) }));
    const discDoc = new Map<string, string | null>();
    const porSigla = new Map<string, Set<string>>();
    for (const d of documentos) {
      const c = code(d.codigo);
      if (c && !discDoc.has(c)) discDoc.set(c, d.disciplinaCanonica ?? null);
      const s = d.siglaDoc3 ? code(d.siglaDoc3) : doc3(d.codigo);
      if (s && d.disciplinaCanonica) (porSigla.get(s) ?? porSigla.set(s, new Set()).get(s)!).add(d.disciplinaCanonica);
    }
    const daSigla = (codigo: string | null) => { const s = porSigla.get(doc3(codigo)); return s && s.size === 1 ? [...s][0] : null; };
    const disciplinaDoLancamento = (codigo: string | null, propria: string | null) => {
      const c = code(codigo);
      if (c && discDoc.has(c)) return discDoc.get(c) ?? n(propria);
      return (c ? daSigla(codigo) : null) ?? n(propria);
    };
    porProjeto.set(r.projectId, discDoc);
    const lancamentos = r.lancamentos.map(l => ({ ...l, disciplinaLancamento: l.disciplina, disciplina: disciplinaDoLancamento(l.documentoCodigo, l.disciplina) }));
    const out: FactsReport = { ...r, documentos, lancamentos: lancamentos as PmoEntry[],
      cronograma: r.cronograma.map(t => ({ ...t, disciplina: disciplinaDoLancamento(t.documentoCodigo, t.disciplina) })),
      planning: r.planning && { ...r.planning,
        plannedHhByDiscipline: r.planning.plannedHhByDiscipline.map(d => ({ ...d, discipline: n(d.discipline) })),
        plannedCostByDiscipline: r.planning.plannedCostByDiscipline.map(d => ({ ...d, discipline: n(d.discipline) })) },
      actual: r.actual && { ...r.actual, byDiscipline: r.actual.byDiscipline.map(d => ({ ...d, discipline: n(d.discipline) })) },
      hhRealizado: r.hhRealizado && { ...r.hhRealizado, porDisciplina: r.hhRealizado.porDisciplina.map(d => ({ ...d, disciplina: n(d.disciplina) })) } };
    res.naoReconhecidos.forEach(x => naoReconhecidos.add(x));
    return { out, n, disciplinaDoLancamento };
  });
  return { reports: normalizados.map(x => x.out), naoReconhecidos: [...naoReconhecidos].sort(),
    resolver: new Map(normalizados.map(x => [x.out.projectId, x] as const)) };
}

const emGestao = (keys: Set<string> | undefined, codigo: string | null) => { const c = code(codigo); return c !== '' && keys?.has(c) === true; };

/** Recorta cada relatório no universo pedido (documentos, lançamentos e cronograma pelo código do documento). */
export function recortarUniverso(reports: FactsReport[], universo: UniversoHhCusto, gestao: Map<string, Set<string>>): FactsReport[] {
  if (universo === 'original') return reports;
  return reports.map(r => {
    const keys = gestao.get(r.projectId);
    const documentos = r.documentos.filter(d => (universo === 'gestao') === emGestao(keys, d.codigo));
    const codigos = new Set(documentos.map(d => code(d.codigo)).filter(c => c !== ''));
    return { ...r, documentos,
      lancamentos: r.lancamentos.filter(l => codigos.has(code(l.documentoCodigo))),
      cronograma: r.cronograma.filter(t => codigos.has(code(t.documentoCodigo))) };
  });
}

/** Pendências honestas do universo: o que existe no projeto e não pode ser atribuído a Engenharia nem a Gestão. */
export function pendenciasUniverso(reports: FactsReport[], universo: UniversoHhCusto, gestao: Map<string, Set<string>>) {
  const rec = projectFacts(reports).reconciliacaoDocumentos;
  const docs = reports.flatMap(r => r.documentos);
  const codigosLdp = new Set(reports.flatMap(r => r.documentos.map(d => docKey(r.projectId, d.codigo))));
  const classificadosForaDaLdp = reports.reduce((n, r) => n + [...(gestao.get(r.projectId) ?? [])].filter(k => !codigosLdp.has(docKey(r.projectId, k))).length, 0);
  return {
    linhasSemCodigo: universo === 'gestao' ? 0 : docs.filter(d => code(d.codigo) === '').length,
    hhSemDocumento: rec.hhSemDocumento, hhForaDaLdp: rec.hhForaDaLdp,
    custoSemDocumento: rec.custoSemDocumento, custoForaDaLdp: rec.custoForaDaLdp,
    classificadosForaDaLdp,
  };
}

/** Desvio = 100 × (Previsto − Realizado) / Previsto. Positivo = abaixo do previsto; negativo = estouro; previsto 0/NULL = N/D. */
export const desvioPercentual = (previsto: number | null, realizado: number | null): number | null => {
  const c = consumption(realizado, previsto);
  return c === null ? null : 100 - c;
};
export const tomDesvio = (v: number | null): 'positivo' | 'negativo' | 'neutro' | 'nd' =>
  v === null ? 'nd' : Math.abs(v) < 0.005 ? 'neutro' : v > 0 ? 'positivo' : 'negativo';
export const formatPct = (v: number | null) => v === null ? 'N/D' : `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1, minimumFractionDigits: 1 }).format(v)}%`;

/** N/D nunca esconde o realizado: explica por que o desvio não existe (sem previsto aplicável / sem orçamento / sem realizado). */
export function motivoDesvioIndisponivel(previsto: number | null, realizado: number | null, tipo: 'hh' | 'custo', fmt: (v: number | null) => string) {
  if (desvioPercentual(previsto, realizado) !== null) return null;
  const real = tipo === 'hh' ? `${fmt(realizado ?? 0)} HH realizadas` : `${fmt(realizado ?? 0)} realizado`;
  if (previsto === null) return `${real} · ${tipo === 'hh' ? 'sem HH planejada' : 'sem orçamento'} — desvio % indisponível`;
  if (previsto === 0) return `${real} · ${tipo === 'hh' ? '0 HH planejadas' : 'orçamento zero'} — desvio % indisponível`;
  return `${tipo === 'hh' ? 'HH' : 'Custo'} realizado ausente — desvio % indisponível`;
}

const naSemana = (data: string | null, semanas: Set<string>) =>
  !semanas.size || (data !== null && [...semanas].some(s => day(data)! >= s && day(data)! <= addDays(s, 6)));

/** Rótulo do responsável: pacote com empresa comprovada exibe a EMPRESA do cadastro (costs.Package.Empresa); o resto, o nome. */
export function grupoResponsavel(info: ResponsavelInfo | undefined, pessoa: string) {
  if (info?.tipo === 'PACOTE' && info.empresa) return { chave: `EMPRESA:${info.empresa.toUpperCase()}`, rotulo: info.empresa, detalhe: `Pacote ${info.pacotes.join(', ')}`, pendencia: null };
  if (info?.tipo === 'PACOTE_SEM_EMPRESA') return { chave: `PESSOA:${pessoa}`, rotulo: pessoa, detalhe: `Pacote ${info.pacotes.join(', ')}`, pendencia: 'Empresa não identificada no cadastro do pacote' };
  if (info?.tipo === 'AMBIGUO') return { chave: `PESSOA:${pessoa}`, rotulo: pessoa, detalhe: `Pacotes ${info.pacotes.join(', ')}`, pendencia: 'Casa com pacotes de empresas diferentes' };
  return { chave: `PESSOA:${pessoa}`, rotulo: pessoa, detalhe: null, pendencia: null };
}

type OrigemLinha = 'TIMESHEET' | 'MEDICAO_HISTORICA';

/**
 * Complemento de MEDIÇÃO HISTÓRICA CONFIRMADA (costs.PackageMeasuredHistorical) por documento, já incluído pelo backend no HH
 * oficial do documento. Vira linha do profissional do pacote, SEM data (não é lançamento de Timesheet).
 */
export function linhasMedicaoHistorica(r: FactsReport): (ResponsavelLinha & { origem: OrigemLinha; pacote: string })[] {
  const hm = r.actual?.historicalMeasurement;
  if (!hm?.documents?.length) return [];
  const pacotes = new Map(hm.disciplines.map(d => [d.packageId, { profissional: d.professional, pacote: d.package }]));
  return hm.documents.filter(d => d.hours > 0 && pacotes.has(d.packageId)).map(d => ({
    pessoa: pacotes.get(d.packageId)!.profissional, pacote: pacotes.get(d.packageId)!.pacote, documentoCodigo: d.document,
    disciplina: d.indicatorDiscipline ?? d.normaDiscipline ?? d.discipline, data: null, hh: d.hours, lancamentos: 0, origem: 'MEDICAO_HISTORICA' as const }));
}

export function controleHhCusto(reportsBrutos: FactsReport[], universo: UniversoHhCusto, gestao: Map<string, Set<string>>,
  filtros: FiltrosHhCusto, responsaveis: ResponsaveisReport[]) {
  const norm = normalizarDisciplinas(reportsBrutos, responsaveis);
  const reportsProjeto = norm.reports;
  const reports = recortarUniverso(reportsProjeto, universo, gestao);
  const univDocs = new Set(reports.flatMap(r => r.documentos.map(d => docKey(r.projectId, d.codigo))));
  const discDoc = new Map(reports.flatMap(r => r.documentos.map(d => [docKey(r.projectId, d.codigo), factKey(d.disciplinaCanonica ?? d.disciplina)] as const)));
  const atividade = new Set(reports.flatMap(r => r.documentos.filter(ehAtividade).map(d => docKey(r.projectId, d.codigo))));
  const infos = new Map(responsaveis.flatMap(r => r.responsaveis.map(i => [`${r.projectId}|${i.pessoa}`, i] as const)));

  // Linhas de responsável do universo (sem documento só na tela original), com a disciplina do DOCUMENTO quando existe.
  const brutas = [
    ...responsaveis.flatMap(r => r.linhas.map(l => ({ ...l, projectId: r.projectId, origem: 'TIMESHEET' as OrigemLinha, pacote: null as string | null }))),
    ...reportsBrutos.flatMap(r => linhasMedicaoHistorica(r).map(l => ({ ...l, projectId: r.projectId }))),
  ];
  const linhas = brutas.map(l => {
    const chaveDoc = docKey(l.projectId, l.documentoCodigo);
    const temDoc = code(l.documentoCodigo) !== '' && univDocs.has(chaveDoc);
    const info = infos.get(`${l.projectId}|${l.pessoa}`);
    const res = norm.resolver.get(l.projectId);
    const disciplinaLancamento = res ? res.n(l.disciplina) : l.disciplina;
    const disciplinaChave = discDoc.get(chaveDoc) ?? factKey(res ? res.disciplinaDoLancamento(l.documentoCodigo, l.disciplina) : l.disciplina);
    return { ...l, chaveDoc, temDoc, disciplinaChave, disciplinaLancamento, grupo: grupoResponsavel(info, l.pessoa) };
  }).filter(l => universo === 'original' || l.temDoc);

  // Responsável → documentos em que lançou horas (na semana): vira filtro de documento por INTERSEÇÃO.
  let documentos = filtros.documentos;
  if (filtros.responsaveis.size) {
    const dosResponsaveis = new Set(linhas.filter(l => l.temDoc && filtros.responsaveis.has(l.grupo.chave) && naSemana(l.data, filtros.semanas)).map(l => l.chaveDoc));
    const inter = filtros.documentos.size ? [...filtros.documentos].filter(k => dosResponsaveis.has(k)) : [...dosResponsaveis];
    documentos = new Set(inter.length ? inter : [NENHUM_DOCUMENTO]);
  }
  const base = financeiroCanonico(reports, { semanas: filtros.semanas, disciplinas: filtros.disciplinas, documentos }, 'semanal',
    { forcarDocumental: universo !== 'original' });

  const { cards } = base;
  const indicadores = {
    hhPlanejado: cards.hh_previsto, hhRealizado: cards.hh_realizado, saldoHh: cards.hh_saldo,
    custoPlanejado: cards.custo_previsto, custoRealizado: cards.custo_realizado, saldoCusto: cards.custo_saldo,
    consumoHh: consumption(cards.hh_realizado, cards.hh_previsto), consumoCusto: consumption(cards.custo_realizado, cards.custo_previsto),
  };

  const documentosTabela = base.documentosTabela
    .slice().sort((a, b) => code(a.codigo).localeCompare(code(b.codigo)) || (a.projectId ?? '').localeCompare(b.projectId ?? ''))
    .map((d, i) => ({ ...d, item: i + 1, disciplinaRotulo: rotuloDe(factKey(d.disciplina)), atividade: atividade.has(d.rowId),
      SaldoHH: balance(d.HH_Previsto, d.HH_Realizado), SaldoCusto: balance(d.Custo_Previsto, d.Custo_Realizado) }));
  // Contagem documental: só AT (atividade de esforço) fica fora; linhas sem documento na LDP (lançamento fora da LDP) também.
  const contagem = { documentos: documentosTabela.filter(d => univDocs.has(d.rowId) && !d.atividade).length,
    atividades: documentosTabela.filter(d => d.atividade).length, foraDaLdp: documentosTabela.filter(d => !univDocs.has(d.rowId)).length };

  const disciplinasTabela = base.disciplinasTabela.map(d => ({ ...d,
    DesvioHH: desvioPercentual(d.HH_Previsto, d.HH_Realizado), DesvioCusto: desvioPercentual(d.Custo_Previsto, d.Custo_Realizado) }));

  // Painel Responsável: semana (data do lançamento), disciplina e documento — EXCETO o próprio filtro (para permitir alternar).
  const linhasPainel = linhas.filter(l => naSemana(l.data, filtros.semanas)
    && (!filtros.disciplinas.size || filtros.disciplinas.has(l.disciplinaChave))
    && (!filtros.documentos.size || filtros.documentos.has(l.chaveDoc)));
  // Medição histórica não tem data: com filtro de semana fica fora e é informada (nunca some em silêncio).
  const medicaoForaDaSemana = filtros.semanas.size ? sumSkipNull(linhas.filter(l => l.origem === 'MEDICAO_HISTORICA'
    && (!filtros.disciplinas.size || filtros.disciplinas.has(l.disciplinaChave))
    && (!filtros.documentos.size || filtros.documentos.has(l.chaveDoc))).map(l => l.hh)) : null;
  const grupos = new Map<string, { chave: string; rotulo: string; detalhe: string | null; pendencia: string | null; pessoas: Set<string>;
    hh: number; hhTimesheet: number; hhMedicao: number; docs: Set<string>; lancadoComo: Set<string>; corrigidos: Set<string> }>();
  for (const l of linhasPainel) {
    const g = grupos.get(l.grupo.chave) ?? { ...l.grupo, pessoas: new Set<string>(), hh: 0, hhTimesheet: 0, hhMedicao: 0, docs: new Set<string>(),
      lancadoComo: new Set<string>(), corrigidos: new Set<string>() };
    g.pessoas.add(l.pessoa); g.hh += l.hh;
    if (l.origem === 'MEDICAO_HISTORICA') g.hhMedicao += l.hh; else g.hhTimesheet += l.hh;
    if (l.temDoc && !atividade.has(l.chaveDoc)) g.docs.add(l.chaveDoc);
    // Disciplina do lançamento ≠ disciplina do documento: diagnóstico legível, nunca nova disciplina/documento.
    if (l.origem === 'TIMESHEET' && l.temDoc && l.disciplinaLancamento && factKey(l.disciplinaLancamento) !== l.disciplinaChave) g.lancadoComo.add(l.disciplinaLancamento);
    if (l.documentoCodigoOriginal) g.corrigidos.add(`${l.documentoCodigoOriginal} → ${l.documentoCodigo}`);
    grupos.set(l.grupo.chave, g);
  }
  const r2 = (v: number) => Math.round(v * 100) / 100;
  const responsaveisTabela = [...grupos.values()].map(g => ({ chave: g.chave, rotulo: g.rotulo, detalhe: g.detalhe, pendencia: g.pendencia,
    pessoas: [...g.pessoas].sort(), hh: r2(g.hh), hhTimesheet: r2(g.hhTimesheet), hhMedicao: r2(g.hhMedicao), documentos: g.docs.size,
    lancadoComo: [...g.lancadoComo].sort(), corrigidos: [...g.corrigidos].sort() }))
    .sort((a, b) => b.hh - a.hh || a.rotulo.localeCompare(b.rotulo));

  // Reconciliação pessoa × documento: Timesheet corrigido + medição histórica por pessoa × HH realizado oficial desses documentos.
  const hhPessoasDocs = sumSkipNull(linhasPainel.filter(l => l.temDoc).map(l => l.hh));
  const hhPessoasSemDoc = sumSkipNull(linhasPainel.filter(l => !l.temDoc).map(l => l.hh));
  const hhMedicaoHistorica = sumSkipNull(linhasPainel.filter(l => l.temDoc && l.origem === 'MEDICAO_HISTORICA').map(l => l.hh));
  const hhDocumentos = sumSkipNull(base.documentosTabela.map(d => d.HH_Realizado));
  const reconciliacaoResponsaveis = { hhPessoasDocs, hhPessoasSemDoc, hhMedicaoHistorica, medicaoForaDaSemana, hhDocumentos, diferenca: balance(hhDocumentos, hhPessoasDocs) };

  return { base, reports, indicadores, documentosTabela, contagem, disciplinasTabela, responsaveisTabela, reconciliacaoResponsaveis,
    disciplinasNaoReconhecidas: norm.naoReconhecidos,
    diagnosticosDisciplina: [...new Set(responsaveis.flatMap(r => r.diagnosticosDisciplina ?? []))],
    semanas: base.weeks, pendencias: universo === 'original' ? null : pendenciasUniverso(reportsProjeto, universo, gestao) };
}

/** Mapa projectId → chaves de Gestão (só projetos com persistência disponível). */
export const mapaGestao = (cs: ClassificacaoGestao[]) =>
  new Map(cs.filter(c => c.schemaAvailable).map(c => [c.projectId, new Set(c.documentKeys.map(k => code(k)))]));
