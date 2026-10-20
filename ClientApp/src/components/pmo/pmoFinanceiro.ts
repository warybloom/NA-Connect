import { PmoFilters, Periodo, PmoEntry, docKey, day, addDays, weekStart, formatData, rotuloDe, sumSkipNull, SEM_VALOR } from './pmoData';
import { FactsReport, projectFacts, enrichDocuments, statusCounts, factKey, balance } from './pmoFacts';

/**
 * Financeiro / Horas sobre a MESMA fonte canônica da Gestão à Vista (payload de /api/reports/gestao-vista).
 * Sem filtros de documento/semana, cards e tabela de disciplinas são exatamente projectFacts (idênticos à Gestão).
 * Com filtros de documento/semana, os mesmos valores por documento (enrichDocuments) e os lançamentos atuais do
 * Timesheet (HH e CustoCalculado) são recombinados — nunca uma segunda regra de HH/custo.
 */
export function financeiroCanonico(reports: FactsReport[], filters: PmoFilters, periodo: Periodo) {
  const facts = projectFacts(reports);
  const allDocs = enrichDocuments(reports);
  const allEntries = reports.flatMap(r => r.lancamentos) as PmoEntry[];
  const names = new Map(reports.map(r => [r.projectId, r.nomeProjeto]));
  const lookup = new Map(allDocs.map(d => [docKey(d.projectId, d.codigo), d]));
  // Mesma precedência do backend: disciplina do próprio lançamento; senão a do documento na LDP.
  const entryDiscipline = (l: PmoEntry) => {
    const own = factKey(l.disciplina);
    return own !== SEM_VALOR ? own : factKey(lookup.get(docKey(l.projectId, l.documentoCodigo))?.disciplina ?? null);
  };
  const discOk = (chave: string) => !filters.disciplinas.size || filters.disciplinas.has(chave);
  const docOk = (pid: string, cod: string | null) => !filters.documentos.size || filters.documentos.has(docKey(pid, cod));
  const weekActive = filters.semanas.size > 0;
  const hasDocOrWeek = filters.documentos.size > 0 || weekActive;
  const weeks = [...new Set(allDocs.flatMap(d => d.inicioSemana ? [day(d.inicioSemana)!] : []))].sort();
  const docs = allDocs.filter(d => docOk(d.projectId, d.codigo) && discOk(factKey(d.disciplina)) &&
    (!weekActive || (d.inicioSemana !== null && filters.semanas.has(day(d.inicioSemana)!))));
  const entries = allEntries.filter(l => docOk(l.projectId, l.documentoCodigo) && discOk(entryDiscipline(l)) &&
    (!weekActive || (l.dataLancamento !== null && [...filters.semanas].some(s => day(l.dataLancamento)! >= s && day(l.dataLancamento)! <= addDays(s, 6)))));
  // HH REALIZADO oficial (Actual_HH_Total_Corrigido) é por DOCUMENTO: com a fonte oficial, tabelas/totais filtrados somam
  // o valor oficial dos documentos; o Timesheet (por data de lançamento) só alimenta o gráfico no tempo (porData=true).
  const oficial = reports.some(r => r.hhRealizado);
  const oficialCusto = reports.some(r => r.hhRealizado?.custoOficial === true);
  const values = (ds: { hhPrevisto: number | null; custoPrevisto: number | null; hhRealizado?: number | null; custoRealizado?: number | null }[], es: PmoEntry[], porData = false) => ({
    HH_Previsto: sumSkipNull(ds.map(d => d.hhPrevisto)),
    HH_Realizado: oficial && !porData ? sumSkipNull(ds.map(d => d.hhRealizado ?? null)) : sumSkipNull(es.map(l => l.hhRealizado)),
    Custo_Previsto: sumSkipNull(ds.map(d => d.custoPrevisto)),
    Custo_Realizado: oficialCusto && !porData ? sumSkipNull(ds.map(d => d.custoRealizado ?? null)) : sumSkipNull(es.map(l => l.custoCalculado ?? null)),
  });

  // Cards: sem filtro de documento/semana => canônico puro (facts); só disciplina => soma das linhas canônicas.
  let totals: { HH_Previsto: number | null; HH_Realizado: number | null; Custo_Previsto: number | null; Custo_Realizado: number | null };
  if (!hasDocOrWeek && !filters.disciplinas.size) totals = facts.cards;
  else if (!hasDocOrWeek) {
    const rows = facts.disciplines.filter(d => filters.disciplinas.has(d.chave));
    totals = { HH_Previsto: sumSkipNull(rows.map(r => r.HH_Previsto)), HH_Realizado: sumSkipNull(rows.map(r => r.HH_Realizado)),
      Custo_Previsto: sumSkipNull(rows.map(r => r.Custo_Previsto)), Custo_Realizado: sumSkipNull(rows.map(r => r.Custo_Realizado)) };
  } else totals = values(docs, entries);

  const grouped = new Map<string, { ds: typeof docs; ls: PmoEntry[] }>();
  const group = (key: string) => { if (!grouped.has(key)) grouped.set(key, { ds: [], ls: [] }); return grouped.get(key)!; };
  docs.forEach(d => group(docKey(d.projectId, d.codigo)).ds.push(d));
  entries.forEach(l => group(docKey(l.projectId, l.documentoCodigo)).ls.push(l));
  const documentosTabela = [...grouped.entries()].map(([rowId, g]) => {
    const pid = g.ds[0]?.projectId ?? g.ls[0].projectId;
    const codigo = g.ds[0]?.codigo ?? g.ls[0]?.documentoCodigo ?? null;
    const source = lookup.get(docKey(pid, codigo));
    const v = g.ds.length === 1 && !weekActive
      ? { HH_Previsto: g.ds[0].hhPrevisto, HH_Realizado: g.ds[0].hhRealizado, Custo_Previsto: g.ds[0].custoPrevisto, Custo_Realizado: g.ds[0].custoRealizado }
      : values(g.ds, g.ls);
    return { rowId, projectId: pid, nomeProjeto: names.get(pid), codigo,
      documento: source?.titulo ?? g.ls[0]?.documento ?? null,
      disciplina: source?.disciplina ?? g.ls[0]?.disciplina ?? null,
      statusDocumento: source?.statusDocumento ?? null, ...v };
  });

  let disciplinasTabela: { chave: string; Disciplina: string; HH_Previsto: number | null; HH_Realizado: number | null; Custo_Previsto: number | null; Custo_Realizado: number | null }[];
  if (!hasDocOrWeek) {
    disciplinasTabela = facts.disciplines.filter(d => discOk(d.chave)).map(d => ({ chave: d.chave, Disciplina: d.Disciplina,
      HH_Previsto: d.HH_Previsto, HH_Realizado: d.HH_Realizado, Custo_Previsto: d.Custo_Previsto, Custo_Realizado: d.Custo_Realizado }));
  } else {
    const chaves = [...new Set([...docs.map(d => factKey(d.disciplina)), ...entries.map(entryDiscipline)])].sort();
    disciplinasTabela = chaves.map(chave => ({ chave, Disciplina: rotuloDe(chave),
      ...values(docs.filter(d => factKey(d.disciplina) === chave), entries.filter(l => entryDiscipline(l) === chave)) }));
  }

  const difference = balance;
  const tasks = reports.flatMap(r => r.cronograma).filter(t => !t.resumo && t.desvioDias !== null &&
    docOk(t.projectId, t.documentoCodigo) && discOk(factKey(lookup.get(docKey(t.projectId, t.documentoCodigo))?.disciplina ?? t.disciplina)) &&
    (!weekActive || docs.some(d => docKey(d.projectId, d.codigo) === docKey(t.projectId, t.documentoCodigo))));
  const cards = { hh_previsto: totals.HH_Previsto, hh_realizado: totals.HH_Realizado,
    custo_previsto: totals.Custo_Previsto, custo_realizado: totals.Custo_Realizado,
    hh_saldo: difference(totals.HH_Previsto, totals.HH_Realizado), custo_saldo: difference(totals.Custo_Previsto, totals.Custo_Realizado),
    desvio_dias: tasks.length ? Math.max(...tasks.map(t => t.desvioDias!)) : null,
    tarefasComDesvio: tasks.filter(t => t.desvioDias! > 0).length };

  const anchors = new Map(reports.map(r => { const date = r.documentos.find(d => d.inicioSemana)?.inicioSemana;
    return [r.projectId, date ? new Date(day(date)! + 'T12:00:00Z').getUTCDay() : 0]; }));
  const buckets = new Map<string, { ds: typeof docs; ls: PmoEntry[] }>();
  const bucket = (date: string) => { if (!buckets.has(date)) buckets.set(date, { ds: [], ls: [] }); return buckets.get(date)!; };
  if (periodo !== 'diario') docs.forEach(d => { if (d.inicioSemana) {
    const s = day(d.inicioSemana)!; bucket(periodo === 'mensal' ? s.slice(0, 7) + '-01' : s).ds.push(d);
  } });
  entries.forEach(l => { if (l.dataLancamento) {
    const date = day(l.dataLancamento)!;
    const s = weekStart(date, anchors.get(l.projectId) ?? 0);
    bucket(periodo === 'diario' ? date : periodo === 'semanal' ? s : s.slice(0, 7) + '-01').ls.push(l);
  } });
  const grafico = [...buckets.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([Data, g]) => ({
    Data, DataFormatada: formatData(Data), ...values(g.ds, g.ls, true),
  }));

  return { docs, entries, weeks, names, lookup, cards, documentosTabela, disciplinasTabela, grafico, facts,
    statusDocumentos: statusCounts(docs),
    semData: allEntries.filter(l => !l.dataLancamento).length,
    semCusto: entries.filter(l => (l.custoCalculado ?? null) === null).length,
    custoConhecido: sumSkipNull(entries.map(l => l.custoCalculado ?? null)) };
}
