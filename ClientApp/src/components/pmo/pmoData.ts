import { useEffect, useState } from 'react';
import { getJson } from '@/lib/api';

export interface PmoProject { id: string; hubId: string; name: string }
export interface PmoProps { hub: string; project: string; selection?: PmoProject[] }
export interface PmoDocument {
  rowId: string; projectId: string; codigo: string | null; titulo: string | null; disciplina: string | null;
  disciplinaPtPadrao?: string | null; disciplinaEnPadrao?: string | null; disciplinaCanonica?: string | null;
  /** Apresentação: nome da Norma de Coordenação do projeto resolvido pelo DOC3 no backend. */
  disciplinaNorma?: string | null; siglaDoc3?: string | null; fonteDisciplinaNorma?: string | null;
  /** Gates oficiais LDP Tipo 2; HH por fase só quando esforço do cronograma é conhecido. */
  gates?: { percentual:number; ativo:boolean; cancelado:boolean; emitido?:boolean; documentStatus?:string|null; situation?:string|null; esforcoHoras?:number|null; baseline?:string|null; reprogramada?:string|null; planejadoFisico?:number|null; fisicoConcluido?:number|null }[];
  hhPrevisto: number | null; custoPrevisto: number | null; inicioSemana: string | null;
  dataBaseline: string | null; dataReprogramada: string | null; dataEmissao: string | null;
  statusInterno: string | null; statusCliente: string | null; situacaoEmissao: string | null;
  avancoFisico: number | null; classeFisica: string; cancelado: boolean; emitido: boolean;
  statusPrazo: string; dataAlvo: string | null; fonteDataAlvo: string; correlacaoCronograma: string;
  statusDocumento: 'CANCELADO' | 'EMITIDO' | 'NO_PRAZO' | 'ATRASADO' | 'SEM_PRAZO' | 'ATIVIDADE';
  docTypeCode?: string | null; doc4?: string | null; isActivity?: boolean; isDocument?: boolean;
  percentFisicoLdp: number | null; fonteAvancoFisico: string;
  /** HH realizado oficial do documento (Actual_HH_Total_Corrigido), quando o backend envia. */
  hhRealizado?: number | null;
  /** Custo realizado oficial do documento (Actual_Cost_Total_Corrigido), quando o backend envia. */
  custoRealizado?: number | null;
}
export interface PmoEntry {
  rowId: string; projectId: string; documentoCodigo: string | null; documento: string | null;
  disciplina: string | null; dataLancamento: string | null; hhRealizado: number | null; custoRealizado: number | null;
  custoCalculado?: number | null;
}
export interface PmoWeekly {
  rowId: string; projectId: string; numeroAtividade: string | null; disciplina: string | null;
  nomeTarefa: string | null; dataBaseline: string | null; dataReprogramada: string | null; statusProgresso: string | null;
}
export interface PmoTask {
  rowId: string; projectId: string; documentoCodigo: string | null; disciplina: string | null;
  dataFimBaseline: string | null; dataFimAtual: string | null; desvioDias: number | null; resumo: boolean;
  trabalho: number | null;
}
export interface PmoReport {
  projectId: string; nomeProjeto: string; tipoLdp: number; dataReferencia: string;
  documentos: PmoDocument[]; lancamentos: PmoEntry[]; weeklyAtividades: PmoWeekly[]; cronograma: PmoTask[];
}
export const SEM_VALOR = '__PMO_NULL__';
export const chaveDe = (v: string | null) => v ?? SEM_VALOR;
export const rotuloDe = (v: string) => v === SEM_VALOR ? 'Não informado' : v;
export const code = (v: string | null) => (v ?? '').trim().toUpperCase();
export const docKey = (pid: string, codigo: string | null) => JSON.stringify([pid, code(codigo)]);
export const day = (v: string | null) => v?.slice(0, 10) ?? null;
export const addDays = (v: string, n: number) => {
  const d = new Date(v.slice(0, 10) + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10);
};
export const weekStart = (v: string, weekday: number) => {
  const d = new Date(v.slice(0, 10) + 'T12:00:00Z'); return addDays(v, -((d.getUTCDay() - weekday + 7) % 7));
};
export const formatData = (v: string | null) => v ? v.slice(0, 10).split('-').reverse().join('/') : '—';
export const somaOuNull = (values: (number | null)[]): number | null =>
  !values.length || values.some(v => v === null) ? null : values.reduce<number>((a, v) => a + v!, 0);
/** Mesma semântica de SQL SUM(): ignora NULLs individuais; só retorna null se NENHUM valor for conhecido. */
export const sumSkipNull = (values: (number | null)[]): number | null => {
  const known = values.filter((v): v is number => v !== null);
  return known.length ? known.reduce((a, v) => a + v, 0) : null;
};
export const formatHH = (v: number | null) => v === null ? '—' : new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(v);
export const formatCurrency = (v: number | null) => v === null ? '—' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v);
export function toggleInSet<T>(set: Set<T>, value: T) { const n = new Set(set); if (n.has(value)) n.delete(value); else n.add(value); return n; }
export async function limitedMap<T, U>(items: T[], action: (item: T) => Promise<U>, limit = 3): Promise<U[]> {
  const results: U[] = new Array(items.length); let index = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (index < items.length) { const i = index++; results[i] = await action(items[i]); }
  })); return results;
}
export interface PmoFalha { projectId: string; projectName: string; message: string }
export function usePmoReports(endpoint: string, props: PmoProps) {
  const [reports, setReports] = useState<PmoReport[]>([]);
  const [falhas, setFalhas] = useState<PmoFalha[]>([]);
  const [loading, setLoading] = useState(true); const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  const selecionados = props.selection ?? [{ id: props.project, hubId: props.hub, name: props.project }];
  const key = JSON.stringify(selecionados.map(p => [p.hubId, p.id, p.name]));
  useEffect(() => {
    const abort = new AbortController(); setLoading(true); setError(undefined); setReports([]); setFalhas([]);
    const projects: string[][] = JSON.parse(key);
    // Consolidado: um projeto sem dado (sem acesso, falha pontual) NÃO derruba a tela — vira "falha" listada; erro só quando TODOS falham.
    const fs: PmoFalha[] = [];
    limitedMap(projects, async ([hubId, projectId, name]) => {
      try { return await getJson<PmoReport>(`/api/reports/${endpoint}?${new URLSearchParams({ hubId, projectId })}`, abort.signal); }
      catch (e) { if (abort.signal.aborted) throw e; fs.push({ projectId, projectName: name, message: e instanceof Error ? e.message : 'Falha ao consultar o projeto.' }); return null; }
    })
      .then(r => {
        if (abort.signal.aborted) return;
        const ok = r.filter((x): x is PmoReport => x !== null);
        setFalhas(fs);
        if (ok.length === 0) setError(fs[0]?.message ?? 'Falha ao consultar os projetos.'); else setReports(ok);
      })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof Error ? e.message : 'Falha ao consultar os projetos.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [endpoint, key, attempt]);
  return { reports, falhas, loading, error, retry: () => setAttempt(a => a + 1), selectionKey: key };
}
export interface PmoFilters { semanas: Set<string>; disciplinas: Set<string>; documentos: Set<string> }
export type Periodo = 'diario' | 'semanal' | 'mensal';

/** Mantém orçamento documental e lançamentos separados. Um orçamento nunca se repete por lançamento. */
export function financeiro(reports: PmoReport[], filters: PmoFilters, periodo: Periodo) {
  const allDocs = reports.flatMap(r => r.documentos);
  const allEntries = reports.flatMap(r => r.lancamentos);
  const names = new Map(reports.map(r => [r.projectId, r.nomeProjeto]));
  const lookup = new Map(allDocs.map(d => [docKey(d.projectId, d.codigo), d]));
  // Disciplina canônica é a referência oficial em análise individual e consolidada.
  const disciplinaDoc = (d: PmoDocument) => d.disciplinaCanonica ?? d.disciplina;
  const disciplinaEntry = (l: PmoEntry) => {
    const doc = lookup.get(docKey(l.projectId, l.documentoCodigo));
    return doc ? disciplinaDoc(doc) : l.disciplina;
  };
  const dimension = (pid: string, cod: string | null, disc: string | null) =>
    (!filters.documentos.size || filters.documentos.has(docKey(pid, cod))) &&
    (!filters.disciplinas.size || filters.disciplinas.has(chaveDe(disc)));
  const weeks = [...new Set(allDocs.flatMap(d => d.inicioSemana ? [day(d.inicioSemana)!] : []))].sort();
  const docs = allDocs.filter(d => dimension(d.projectId, d.codigo, disciplinaDoc(d)) &&
    (!filters.semanas.size || (d.inicioSemana !== null && filters.semanas.has(day(d.inicioSemana)!))));
  const entries = allEntries.filter(l => dimension(l.projectId, l.documentoCodigo, disciplinaEntry(l)) &&
    (!filters.semanas.size || (l.dataLancamento !== null && [...filters.semanas].some(s => day(l.dataLancamento)! >= s && day(l.dataLancamento)! <= addDays(s, 6)))));
  const values = (ds: PmoDocument[], ls: PmoEntry[]) => ({
    HH_Previsto: somaOuNull(ds.map(d => d.hhPrevisto)), HH_Realizado: somaOuNull(ls.map(l => l.hhRealizado)),
    Custo_Previsto: somaOuNull(ds.map(d => d.custoPrevisto)), Custo_Realizado: somaOuNull(ls.map(l => l.custoRealizado)),
  });
  const grouped = new Map<string, { ds: PmoDocument[]; ls: PmoEntry[] }>();
  const group = (key: string) => { if (!grouped.has(key)) grouped.set(key, { ds: [], ls: [] }); return grouped.get(key)!; };
  docs.forEach(d => group(docKey(d.projectId, d.codigo)).ds.push(d));
  entries.forEach(l => group(docKey(l.projectId, l.documentoCodigo)).ls.push(l));
  const documentosTabela = [...grouped.entries()].map(([rowId, g]) => {
    const pid = g.ds[0]?.projectId ?? g.ls[0].projectId;
    const codigo = g.ds[0]?.codigo ?? g.ls[0]?.documentoCodigo ?? null;
    const source = lookup.get(docKey(pid, codigo));
    return { rowId, projectId: pid, nomeProjeto: names.get(pid), codigo,
      documento: source?.titulo ?? g.ls[0]?.documento ?? null,
      disciplina: source ? disciplinaDoc(source) : (g.ls[0]?.disciplina ?? null), ...values(g.ds, g.ls) };
  });
  const disciplines = [...new Set([...docs.map(d => chaveDe(disciplinaDoc(d))), ...entries.map(l => chaveDe(disciplinaEntry(l)))])].sort();
  const disciplinasTabela = disciplines.map(chave => ({ chave, Disciplina: rotuloDe(chave),
    ...values(docs.filter(d => chaveDe(disciplinaDoc(d)) === chave), entries.filter(l => chaveDe(disciplinaEntry(l)) === chave)) }));
  const totals = values(docs, entries);
  const difference = (a: number | null, b: number | null) => a === null || b === null ? null : a - b;
  const tasks = reports.flatMap(r => r.cronograma).filter(t => !t.resumo && t.desvioDias !== null &&
    dimension(t.projectId, t.documentoCodigo, (() => {
      const doc = lookup.get(docKey(t.projectId, t.documentoCodigo));
      return doc ? disciplinaDoc(doc) : t.disciplina;
    })()) &&
    (!filters.semanas.size || docs.some(d => docKey(d.projectId, d.codigo) === docKey(t.projectId, t.documentoCodigo))));
  const cards = { hh_previsto: totals.HH_Previsto, hh_realizado: totals.HH_Realizado,
    custo_previsto: totals.Custo_Previsto, custo_realizado: totals.Custo_Realizado,
    hh_saldo: difference(totals.HH_Previsto, totals.HH_Realizado), custo_saldo: difference(totals.Custo_Previsto, totals.Custo_Realizado),
    desvio_dias: tasks.length ? Math.max(...tasks.map(t => t.desvioDias!)) : null,
    tarefasComDesvio: tasks.filter(t => t.desvioDias! > 0).length };
  const anchors = new Map(reports.map(r => { const date = r.documentos.find(d => d.inicioSemana)?.inicioSemana;
    return [r.projectId, date ? new Date(day(date)! + 'T12:00:00Z').getUTCDay() : 0]; }));
  const buckets = new Map<string, { ds: PmoDocument[]; ls: PmoEntry[] }>();
  const bucket = (date: string) => { if (!buckets.has(date)) buckets.set(date, { ds: [], ls: [] }); return buckets.get(date)!; };
  if (periodo !== 'diario') docs.forEach(d => { if (d.inicioSemana) {
    const s = day(d.inicioSemana)!; bucket(periodo === 'mensal' ? s.slice(0, 7) + '-01' : s).ds.push(d);
  } });
  entries.forEach(l => { if (l.dataLancamento) {
    const date = day(l.dataLancamento)!;
    const s = weekStart(date, anchors.get(l.projectId) ?? 0);
    const b = periodo === 'diario' ? date : periodo === 'semanal' ? s : s.slice(0, 7) + '-01';
    bucket(b).ls.push(l);
  } });
  const grafico = [...buckets.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([Data, g]) => ({
    Data, DataFormatada: formatData(Data), ...values(g.ds, g.ls),
  }));
  return { docs, entries, weeks, names, lookup, cards, documentosTabela, disciplinasTabela, grafico,
    semData: allEntries.filter(l => !l.dataLancamento).length, semCusto: entries.filter(l => l.custoRealizado === null).length,
    custoConhecido: somaOuNull(entries.flatMap(l => l.custoRealizado === null ? [] : [l.custoRealizado])) };
}
