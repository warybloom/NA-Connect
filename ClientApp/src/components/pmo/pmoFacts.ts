import { somaConhecida } from './pmoMultiCore';
import { PmoReport, PmoDocument, PmoEntry, SEM_VALOR, sumSkipNull, docKey, day, addDays, weekStart, formatData } from './pmoData';

export interface PlanningFacts {
  plannedHhTotal: number | null;
  plannedHhByDiscipline: { discipline: string | null; plannedHh: number | null }[];
  disciplineTotal: number | null; undisciplinedHh: number | null; reconciliationDifference: number | null;
  source: string; coverage: string; diagnostics: string[];
  plannedCostTotal: number | null; documentPlannedCost: number | null;
  knownDocumentCostSubtotal: number | null;
  plannedCostByDiscipline: { discipline: string | null; documentCost: number | null; documents: number; withCost: number }[];
  costSource: string; costCoverage: string; costDocuments: number; documentsWithCost: number;
  hhDocuments?: number; documentsWithHh?: number;
}
interface ActualValues { hh: number | null; cost: number | null; knownCostSubtotal: number | null;
  knownCalculatedCostSubtotal: number | null; entries: number; missingHh: number; missingCost: number; missingCalculatedCost: number }
export interface HistoricalMeasurementFacts {
  projectId: string; complementHours: number; complementCost: number; excessHours: number; excessCost: number; diagnostics: string[];
  disciplines: { packageId: number; confirmationId: number; package: string; professional: string; discipline: string;
    confirmedHours: number; confirmedCost: number; timesheetHours: number; timesheetCost: number | null;
    confirmedAtUtc: string; confirmedBy: string; origin: string;
    balance: { recognizedHours: number; recognizedCost: number; complementHours: number; complementCost: number;
      excessHours: number; excessCost: number; diagnostics: string[] } }[];
}
export interface ActualFacts {
  total: ActualValues; source: string;
  timesheetOriginal?: ActualValues;
  historicalMeasurement?: HistoricalMeasurementFacts;
  byDiscipline: { discipline: string | null; values: ActualValues }[];
  byDocument: { document: string; values: ActualValues }[];
  semDocumento?: ActualValues;
  /** Lançamentos com documento fora da LDP atual. Diagnóstico: HH/custo CONTINUAM no realizado do projeto. */
  foraDaLdp?: ActualValues | null;
  /** Desde 06/10/2026 o total é sempre o Timesheet completo; este campo descreve apenas o diagnóstico aplicado. */
  escopoHh?: string;
}
/**
 * HH REALIZADO oficial do projeto (backend): Σ da coluna pronta Actual_HH_Total_Corrigido da VW_Ldp_Unificada, total e por
 * disciplina do documento. Quando presente, é A fonte de HH realizado/saldo/consumo de todas as telas (nada é reconstruído aqui).
 */
export interface HhRealizadoOficial {
  total: number | null; porDisciplina: { disciplina: string | null; hh: number | null; custo?: number | null }[]; fonte: string;
  /** CUSTO REALIZADO oficial (Σ Actual_Cost_Total_Corrigido) — só quando custoOficial = true (coluna existente na view do ambiente). */
  custoTotal?: number | null; custoOficial?: boolean;
}
/** Avanço previsto/físico do projeto (backend PmoAvancoRules) — numerador/denominador para consolidar vários projetos. */
export interface AvancoProjeto {
  tipoLdp: number; dataReferencia: string; inicioSemanaVigente: string; fimSemanaVigente: string;
  previsto: number | null; previstoNumerador: number; previstoDenominador: number;
  fisico: number | null; fisicoNumerador: number; fisicoDenominador: number;
  documentos: number; documentosSemDadoFisico: number; documentosSemDadoPrevisto: number;
  regraPrevisto: string; regraFisico: string; ponderacao: string;
  /** Tipo 2: avanço de cada fase (30/60/90) ponderado pelo esforço (Work) — numerador/denominador por projeto. */
  porFase?: { gate: number; previsto: number | null; previstoNumerador: number; previstoDenominador: number;
    fisico: number | null; fisicoNumerador: number; fisicoDenominador: number }[];
  fasesSemEsforco?: number;
}
export type FactsReport = PmoReport & { planning?: PlanningFacts; actual?: ActualFacts; hhRealizado?: HhRealizadoOficial; avanco?: AvancoProjeto };
/** HH realizado do projeto: oficial (Actual_HH_Total_Corrigido) quando o backend o envia; senão o legado do Timesheet. */
const hhDoProjeto = (r: FactsReport) => (r.hhRealizado ? r.hhRealizado.total : r.actual?.total.hh);
/** Custo realizado do projeto: oficial (Actual_Cost_Total_Corrigido) quando disponível; senão a fonte anterior. */
const custoOficial = (r: FactsReport) => r.hhRealizado?.custoOficial === true;
const custoDoProjeto = (r: FactsReport) => (custoOficial(r) ? r.hhRealizado!.custoTotal : r.actual?.total.knownCalculatedCostSubtotal);
export const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
export const factKey = (v: string | null) => v?.trim().toUpperCase() || SEM_VALOR;
export const sumFacts = (values: unknown[]): number | null => {
  if (!values.length || values.some(v => !finite(v))) return null;
  const sum = (values as number[]).reduce((a, v) => a + v, 0);
  return finite(sum) ? sum : null;
};
export const consumption = (actual: number | null, planned: number | null) => {
  const value = finite(actual) && finite(planned) && planned > 0 ? actual / planned * 100 : null;
  return finite(value) ? value : null;
};
export const balance = (planned: number | null, actual: number | null) => {
  const value = finite(planned) && finite(actual) ? planned - actual : null;
  return finite(value) ? value : null;
};

/** Presentation aggregation only. Source selection, hierarchy and coverage live in the canonical providers. */
export function projectFacts(reports: FactsReport[]) {
  const planning = reports.map(r => r.planning);
  // Consolidação multi-projeto: soma SQL dos valores CONHECIDOS de cada projeto selecionado (um projeto sem valor informado não anula os demais).
  const hh = somaConhecida(reports.map(hhDoProjeto));
  const planned = somaConhecida(planning.map(p => p?.plannedHhTotal));
  // Custo Previsto Total = mesma fonte já exibida por projeto no painel de reconciliação (documentPlannedCost,
  // LDP documental — PlannedCostTotal nunca é atribuído pelo backend hoje, ver ProjectPlanningFacts.cs).
  // Custo Realizado Total = mesmo campo canônico (knownCalculatedCostSubtotal) já usado abaixo por disciplina.
  const custoPrevistoTotal = somaConhecida(planning.map(p => p?.documentPlannedCost));
  const custoRealizadoTotal = somaConhecida(reports.map(custoDoProjeto));
  const keys = new Set(reports.flatMap(r => [
    ...(r.planning?.plannedHhByDiscipline.map(d => factKey(d.discipline)) ?? []),
    ...(r.planning?.plannedCostByDiscipline.map(d => factKey(d.discipline)) ?? []),
    ...(r.actual?.byDiscipline.map(d => factKey(d.discipline)) ?? []),
    ...(r.hhRealizado?.porDisciplina.map(d => factKey(d.disciplina)) ?? [])]));
  const disciplines = [...keys].sort().map(chave => {
    const ps = planning.flatMap(p => p?.plannedHhByDiscipline.filter(d => factKey(d.discipline) === chave) ?? []);
    const costs = planning.flatMap(p => p?.plannedCostByDiscipline.filter(d => factKey(d.discipline) === chave) ?? []);
    const actuals = reports.flatMap(r => r.actual?.byDiscipline.filter(d => factKey(d.discipline) === chave) ?? []);
    // Grupo vazio (nenhuma linha do backend caiu nesta disciplina) é zero CONFIRMADO, nunca "desconhecido"
    // — sumFacts([]) retorna null porque seu uso normal é "algum valor individual é desconhecido", o que
    // não é o caso aqui: o backend já teria colocado a tarefa em SEM DISCIPLINA se não soubesse classificá-
    // la. Sem essa distinção, qualquer disciplina só com Custo (LDP)/Realizado (Timesheet) e nenhuma tarefa
    // de cronograma aparecia com HH Previsto em branco — e a mesma lacuna zerava a reconciliação inteira.
    const hp = planning.some(p => !p) || !planning.some(p => finite(p!.plannedHhTotal)) ? null : (ps.length === 0 ? 0 : somaConhecida(ps.map(d => d.plannedHh)));
    // HH realizado por disciplina: coluna oficial por disciplina do documento (backend); legado = Timesheet por Doc3.
    const hr = reports.some(r => !r.actual && !r.hhRealizado) ? null : somaConhecida(reports.map(r => {
      if (r.hhRealizado) { const ds = r.hhRealizado.porDisciplina.filter(d => factKey(d.disciplina) === chave); return ds.length === 0 ? 0 : somaConhecida(ds.map(d => d.hh)); }
      const as = r.actual?.byDiscipline.filter(d => factKey(d.discipline) === chave) ?? [];
      return as.length === 0 ? 0 : somaConhecida(as.map(d => d.values.hh));
    }));
    const custoPrevisto = costs.length === 0 ? 0 : somaConhecida(costs.map(d => d.documentCost));
    // Custo real operacional atual = CustoCalculado (CustoTimesheetInformado não existe na tabela).
    // Custo realizado por disciplina: coluna oficial por disciplina do documento (backend) quando disponível.
    const custoRealizado = somaConhecida(reports.map(r => {
      if (custoOficial(r)) { const ds = r.hhRealizado!.porDisciplina.filter(d => factKey(d.disciplina) === chave); return ds.length === 0 ? 0 : somaConhecida(ds.map(d => d.custo ?? null)); }
      const as = r.actual?.byDiscipline.filter(d => factKey(d.discipline) === chave) ?? [];
      return as.length === 0 ? 0 : somaConhecida(as.map(d => d.values.knownCalculatedCostSubtotal));
    })) ?? (actuals.length === 0 ? 0 : null);
    const docsComCusto = costs.reduce((n, d) => n + d.withCost, 0);
    const docsCusto = costs.reduce((n, d) => n + d.documents, 0);
    return { chave, Disciplina: chave === SEM_VALOR ? 'GERAL / SEM DISCIPLINA' : chave,
      HH_Previsto: hp, HH_Realizado: hr, SaldoHH: balance(hp, hr), ConsumoHH: consumption(hr, hp),
      Custo_Previsto: custoPrevisto, Custo_Realizado: custoRealizado, SaldoCusto: balance(custoPrevisto, custoRealizado),
      ConsumoCusto: consumption(custoRealizado, custoPrevisto),
      // "Docs sem custo" = documentos da disciplina sem Custo Previsto (LDP) válido — nunca custo real/Timesheet ausente.
      DocsSemCusto: docsCusto - docsComCusto,
      DocsComCusto: docsComCusto, DocsCusto: docsCusto };
  });
  // Reconciliação obrigatória: soma das disciplinas (inclui o bucket GERAL/SEM DISCIPLINA, que já entra em
  // `disciplines` via SEM_VALOR) precisa bater com o total. Diferença nunca escondida — exposta ao card.
  // sumSkipNull (não sumFacts) aqui: uma disciplina com lançamentos reais mas nenhum CustoCalculado
  // preenchido (ex.: SAFETY) já contribui 0 para custoRealizadoTotal (que é a mesma soma "conhecida",
  // Sum(CustoCalculado válido) — nunca soma tudo); a reconciliação precisa comparar as duas somas com a
  // MESMA semântica, não travar em null por causa de uma disciplina sem preço, já visível no card dela.
  const hhDisciplinasSum = sumSkipNull(disciplines.map(d => d.HH_Realizado));
  const custoDisciplinasSum = sumSkipNull(disciplines.map(d => d.Custo_Realizado));
  // Reconciliação "por documento": SUM(HH/Custo dos documentos identificados) + SEM DOCUMENTO precisa bater
  // com o Total Realizado — mesma partição de linhas de actual.byDiscipline, só que particionada por
  // documento em vez de disciplina. SEM DOCUMENTO nunca é descartado do total (ver PmoActualFactsProvider.cs).
  // Total do projeto = Timesheet completo. A parcela por documento abaixo é somente o que casa com a LDP atual;
  // ForaDaLdp e SemDocumento fecham a reconciliação sem esconder horas/custo reais.
  const hhPorDocumento = somaConhecida(reports.flatMap(r => r.hhRealizado ? r.documentos.map(d => d.hhRealizado ?? null) : r.actual?.byDocument.map(d => d.values.hh) ?? []));
  const hhForaDaLdp = somaConhecida(reports.map(r => r.actual?.foraDaLdp?.hh));
  const hhSemDocumento = somaConhecida(reports.map(r => r.actual?.semDocumento?.hh));
  const custoForaDaLdp = somaConhecida(reports.map(r => r.actual?.foraDaLdp?.knownCalculatedCostSubtotal));
  const custoPorDocumento = somaConhecida(reports.flatMap(r => custoOficial(r) ? r.documentos.map(d => d.custoRealizado ?? null) : r.actual?.byDocument.map(d => d.values.knownCalculatedCostSubtotal) ?? []));
  const custoSemDocumento = somaConhecida(reports.map(r => r.actual?.semDocumento?.knownCalculatedCostSubtotal));
  return { planning, disciplines,
    cards: { HH_Previsto: planned, HH_Realizado: hh, SaldoHH: balance(planned, hh), ConsumoHH: consumption(hh, planned),
      Custo_Previsto: custoPrevistoTotal, Custo_Realizado: custoRealizadoTotal,
      SaldoCusto: balance(custoPrevistoTotal, custoRealizadoTotal), ConsumoCusto: consumption(custoRealizadoTotal, custoPrevistoTotal) },
    reconciliacaoRealizado: {
      hhTotal: hh, hhDisciplinas: hhDisciplinasSum, hhDiferenca: balance(hh, hhDisciplinasSum),
      custoTotal: custoRealizadoTotal, custoDisciplinas: custoDisciplinasSum, custoDiferenca: balance(custoRealizadoTotal, custoDisciplinasSum),
    },
    reconciliacaoDocumentos: {
      hhTotal: hh, hhPorDocumento, hhSemDocumento, hhForaDaLdp,
      hhDiferenca: balance(hh, somaConhecida([hhPorDocumento, hhSemDocumento, hhForaDaLdp])),
      custoTotal: custoRealizadoTotal, custoPorDocumento, custoSemDocumento, custoForaDaLdp,
      custoDiferenca: balance(custoRealizadoTotal, somaConhecida([custoPorDocumento, custoSemDocumento, custoForaDaLdp])),
    },
  };
}

export type EnrichedDocument = PmoDocument & { hhRealizado: number | null; custoRealizado: number | null;
  saldoHh: number | null; consumoHh: number | null; saldoCusto: number | null; consumoCusto: number | null };

/**
 * Documentos com os valores canônicos por documento (mesma derivação da Gestão à Vista): HH previsto e custo previsto OFICIAIS são o Planned_HH e o
 * Planned_Cost da própria linha da última carga da LDP (null = ausente, nunca zero); HH e CustoCalculado realizados vêm do Timesheet atual (actual.byDocument).
 * Código repetido na LDP deixa o REALIZADO null (o Timesheet é por código e seria atribuído duas vezes); o previsto de cada linha é próprio e permanece.
 */
export function enrichDocuments(reports: FactsReport[]): EnrichedDocument[] {
  const realized = new Map(reports.flatMap(r => r.actual?.byDocument.map(d => [docKey(r.projectId, d.document), d.values] as const) ?? []));
  const multiplicity = new Map<string, number>();
  reports.flatMap(r => r.documentos).forEach(d => { const k = docKey(d.projectId, d.codigo); multiplicity.set(k, (multiplicity.get(k) ?? 0) + 1); });
  const oficial = new Set(reports.filter(r => r.hhRealizado).map(r => r.projectId));
  const oficialCusto = new Set(reports.filter(custoOficial).map(r => r.projectId));
  return reports.flatMap(r => r.documentos).map(d => {
    const k = docKey(d.projectId, d.codigo); const unico = multiplicity.get(k) === 1;
    // HH realizado oficial = a coluna do PRÓPRIO documento (Actual_HH_Total_Corrigido); legado = Timesheet por código.
    const hhRealizado = oficial.has(d.projectId) ? (d.hhRealizado ?? null) : (unico ? realized.get(k)?.hh : null) ?? null;
    // Custo realizado oficial = a coluna do PRÓPRIO documento (Actual_Cost_Total_Corrigido); senão a fonte anterior.
    const custoRealizado = oficialCusto.has(d.projectId) ? (d.custoRealizado ?? null) : (unico ? realized.get(k)?.knownCalculatedCostSubtotal : null) ?? null;
    return { ...d, disciplina: d.disciplinaCanonica ?? d.disciplina, hhRealizado, custoRealizado,
      saldoHh: balance(d.hhPrevisto, hhRealizado), consumoHh: consumption(hhRealizado, d.hhPrevisto),
      saldoCusto: balance(d.custoPrevisto, custoRealizado), consumoCusto: consumption(custoRealizado, d.custoPrevisto) };
  });
}

/** AT (Doc_Type_Code = 'AT') = atividade de esforço/custo, não documento. Classificação vem do backend (LdpItemRules). */
export const ehAtividade = (d: { statusDocumento: string; isActivity?: boolean }) => d.isActivity === true || d.statusDocumento === 'ATIVIDADE';

/** Partição canônica mutuamente exclusiva: cancelado > emitido > atrasado > no prazo > sem prazo. */
export function statusCounts(docs: { statusDocumento: string; isActivity?: boolean }[]) {
  // Cards documentais nunca contam AT. A atividade permanece nas somas de HH/custo e nas tabelas econômicas.
  const documentais = docs.filter(d => !ehAtividade(d));
  const n = (s: string) => documentais.filter(d => d.statusDocumento === s).length;
  const c = { total: documentais.length, cancelados: n('CANCELADO'), emitidos: n('EMITIDO'), noPrazo: n('NO_PRAZO'), atrasados: n('ATRASADO'), semPrazo: n('SEM_PRAZO') };
  const soma = c.cancelados + c.emitidos + c.noPrazo + c.atrasados + c.semPrazo;
  return { ...c, soma, diferenca: c.total - soma };
}
