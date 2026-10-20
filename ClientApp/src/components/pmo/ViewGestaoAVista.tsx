import { useEffect, useState } from "react";
import { CheckCircle2, AlertTriangle, Clock, CalendarDays, FileText, Target, Layers, Filter, X } from "lucide-react";
import PendenteBackend from "./PendenteBackend";
import ConsolidadoBanner from "./ConsolidadoBanner";
import HistoricalMeasurementPanel from "./HistoricalMeasurementPanel";
import { PmoProps, usePmoReports, docKey, rotuloDe, formatHH as formatHHOriginal, formatCurrency as formatCurrencyOriginal, formatData, toggleInSet, day, weekStart, addDays } from './pmoData';

import { projectFacts, FactsReport, factKey as chaveDe, finite, enrichDocuments, statusCounts, ehAtividade } from './pmoFacts';
const formatHH = (value: number | null) => finite(value) ? formatHHOriginal(value) : '\u2014';
const formatCurrency = (value: number | null) => finite(value) ? formatCurrencyOriginal(value) : '\u2014';

export default function ViewGestaoAVista(props: PmoProps) {
  const { reports, falhas, loading, error, retry, selectionKey } = usePmoReports('gestao-vista', props);
  const [filtroDisciplina, setFiltroDisciplina] = useState<Set<string>>(new Set());
  const [filtroDocumento, setFiltroDocumento] = useState<Set<string>>(new Set());
  const [filtroSemana, setFiltroSemana] = useState<Set<string>>(new Set());
  const [focoInicio, setFocoInicio] = useState('');
  useEffect(() => { setFiltroDisciplina(new Set()); setFiltroDocumento(new Set()); setFiltroSemana(new Set()); setFocoInicio(''); }, [selectionKey]);
  const base = {
    weeks: [...new Set(reports.flatMap(r => r.documentos.flatMap(d => d.inicioSemana ? [day(d.inicioSemana)!] : [])))].sort(),
    names: new Map(reports.map(r => [r.projectId, r.nomeProjeto])),
    semData: reports.flatMap(r => r.lancamentos).filter(t => !t.dataLancamento).length,
    semCusto: reports.flatMap(r => r.lancamentos).filter(t => !finite(t.custoCalculado)).length,
  };
  const factsReports = reports as FactsReport[];
  const facts = projectFacts(factsReports);
  const documentos = enrichDocuments(factsReports);
  const documentosFiltrados = documentos.filter(d =>
    (!filtroDisciplina.size || filtroDisciplina.has(chaveDe(d.disciplina))) &&
    (!filtroDocumento.size || filtroDocumento.has(d.rowId)) &&
    (!filtroSemana.size || (d.inicioSemana !== null && filtroSemana.has(day(d.inicioSemana)!))));
  const cardsHH = facts.cards;
  const budgets = facts.planning;
  const graficosDisciplina = facts.disciplines.filter(d => !filtroDisciplina.size || filtroDisciplina.has(d.chave));
  const currentWeek = reports[0] ? weekStart(day(reports[0].dataReferencia)!, 0) : '';
  const focoEfetivo = focoInicio || currentWeek;
  const weeklyFiltrado = reports.flatMap(r => r.weeklyAtividades).filter(w => {
    // Numero_NA_Atividade do Weekly NÃO é documental por definição (não tem relação comprovada com
    // NA_Document_Number da LDP) — nunca usar filtroDocumento (seleção de linhas da tabela de
    // documentos) para restringir atividades Weekly. Só Disciplina e Semana são dimensões seguras que
    // existem nos dois domínios.
    if (filtroDisciplina.size && !filtroDisciplina.has(chaveDe(w.disciplina))) return false;
    const date = day(w.dataReprogramada ?? w.dataBaseline);
    const windows = focoEfetivo === 'todas' ? [] : [focoEfetivo];
    return !windows.length || (date !== null && windows.some(s => date >= s && date <= addDays(s, 6)));
  });
  const { total: totalDocs, cancelados, emitidos, noPrazo, atrasados, semPrazo, soma: somaCategorias } = statusCounts(documentosFiltrados);
  const totalFiltrosAtivos = filtroDisciplina.size + filtroDocumento.size + filtroSemana.size;
  const limparFiltros = () => { setFiltroDisciplina(new Set()); setFiltroDocumento(new Set()); setFiltroSemana(new Set()); setFocoInicio(''); };
  if (loading) return <div className="p-12 text-slate-400">Carregando Gestão à Vista...</div>;
  if (error) return <div role="alert" className="p-12 text-rose-400">{error} <button onClick={retry}>Tentar novamente</button></div>;
  return (
    <div className="space-y-6 animate-enter">
      <ConsolidadoBanner selection={props.selection ?? []} falhas={falhas} />
      <HistoricalMeasurementPanel reports={factsReports} />
      <div className="rounded-lg border border-slate-700 bg-slate-900 p-3 text-xs text-slate-400 space-y-2">
        <p>Consumo de HH compara o HH realizado com o HH previsto da última carga da LDP; não representa avanço físico.</p>
        <p>Emitidos: Document Status APPROVED/APROVADO ou data de emissão preenchida, excluindo cancelados. Prazo: quando há data real de emissão, ela é comparada ao alvo; APPROVED sem data continua emitido sem inventar data. Baseline é preservada para desvio.</p>
        <label className="block text-slate-200">Semana dos documentos — LDP</label>
        <div className="flex gap-2 overflow-auto pb-1" aria-label="Semanas LDP">
          {base.weeks.map(s => <button key={s} onClick={() => setFiltroSemana(toggleInSet(filtroSemana, s))} aria-pressed={filtroSemana.has(s)}
            className={`shrink-0 rounded px-3 py-2 ${filtroSemana.has(s) ? 'bg-sky-600 text-white' : 'bg-slate-800'}`}>{formatData(s)}</button>)}
        </div>
        {!base.weeks.length && <p>Sem informação de início de semana na LDP.</p>}
      </div>


      {totalFiltrosAtivos > 0 && (
        <div className="flex flex-col gap-3 p-4 rounded-xl border border-slate-700 bg-slate-800/50 shadow">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="flex items-center gap-2 text-sm font-semibold text-slate-300">
              <Filter className="w-4 h-4 text-amber-400" /> Detalhando Cockpit:
            </div>
            {[...filtroDisciplina].map(v => (
              <div key={`d-${v}`} className="flex items-center gap-2 bg-emerald-900/50 border border-emerald-500/50 text-emerald-300 px-3 py-1 rounded-full text-xs font-medium">
                Disc: {rotuloDe(v)}
                <button onClick={() => setFiltroDisciplina(toggleInSet(filtroDisciplina, v))} title="Remover filtro" className="hover:text-foreground"><X className="w-3 h-3" /></button>
              </div>
            ))}
            {filtroDocumento.size > 0 && (
              <div className="flex items-center gap-2 bg-purple-900/50 border border-purple-500/50 text-purple-300 px-3 py-1 rounded-full text-xs font-medium">
                {filtroDocumento.size} documento(s)
                <button onClick={() => setFiltroDocumento(new Set())} title="Remover filtro" className="hover:text-foreground"><X className="w-3 h-3" /></button>
              </div>
            )}
            <button className="ml-auto text-[10px] uppercase font-bold text-rose-400 hover:text-rose-300 underline" onClick={limparFiltros}>Limpar Filtros</button>
          </div>
        </div>
      )}

      <div className="rounded-lg border border-slate-700 p-3 text-xs text-slate-400" aria-label="Reconciliação de HH do Cronograma">
        {reports.map((r, i) => <div key={r.projectId}>
          {r.nomeProjeto}: HH Previsto (LDP, última carga) {formatHH(budgets[i]?.plannedHhTotal ?? null)} = disciplinas {formatHH(budgets[i]?.disciplineTotal ?? null)}
          {' + geral '}{formatHH(budgets[i]?.undisciplinedHh ?? null)}; diferença {formatHH(budgets[i]?.reconciliationDifference ?? null)} HH.
          {' '}{budgets[i]?.diagnostics.join(' ') ?? 'Planejamento indisponível.'}
          <p>Fonte HH: {budgets[i]?.source ?? '\u2014'} / {budgets[i]?.coverage ?? '\u2014'}. Cobertura Planned_HH: {budgets[i]?.documentsWithHh ?? '\u2014'}/{budgets[i]?.hhDocuments ?? '\u2014'} documentos com HH informado; documentos na LDP atual: {reports[i].documentos.filter(d => !ehAtividade(d)).length}{reports[i].documentos.some(ehAtividade) ? ` (+ ${reports[i].documentos.filter(ehAtividade).length} atividades AT, só HH/custo)` : ''}.</p>
          <p>Custo previsto documental (LDP): {formatCurrency(budgets[i]?.documentPlannedCost ?? null)}.
            Subtotal documental conhecido: {formatCurrency(budgets[i]?.knownDocumentCostSubtotal ?? null)}. Cobertura: {budgets[i]?.documentsWithCost ?? '\u2014'}/{budgets[i]?.costDocuments ?? '\u2014'} documentos; orçamento total do projeto indisponível.</p>
          <p>Custo real Timesheet: {formatCurrency(factsReports[i].actual?.total.cost ?? null)}.
            Subtotal conhecido: {formatCurrency(factsReports[i].actual?.total.knownCostSubtotal ?? null)};
            lançamentos sem custo: {factsReports[i].actual?.total.missingCost ?? '\u2014'}.</p>
        </div>)}
        <p>Reconciliação HH Realizado: total {formatHH(facts.reconciliacaoRealizado.hhTotal)} = disciplinas {formatHH(facts.reconciliacaoRealizado.hhDisciplinas)}; diferença {formatHH(facts.reconciliacaoRealizado.hhDiferenca)} HH.</p>
        <p>Reconciliação Custo Realizado: total {formatCurrency(facts.reconciliacaoRealizado.custoTotal)} = disciplinas {formatCurrency(facts.reconciliacaoRealizado.custoDisciplinas)}; diferença {formatCurrency(facts.reconciliacaoRealizado.custoDiferenca)}.</p>
        <p>Reconciliação HH por documento: total Timesheet {formatHH(facts.reconciliacaoDocumentos.hhTotal)} = casado com LDP {formatHH(facts.reconciliacaoDocumentos.hhPorDocumento)} + fora da LDP {formatHH(facts.reconciliacaoDocumentos.hhForaDaLdp)} + sem documento {formatHH(facts.reconciliacaoDocumentos.hhSemDocumento)}; diferença {formatHH(facts.reconciliacaoDocumentos.hhDiferenca)} HH.</p>
        <p>Reconciliação Custo por documento: total Timesheet {formatCurrency(facts.reconciliacaoDocumentos.custoTotal)} = casado com LDP {formatCurrency(facts.reconciliacaoDocumentos.custoPorDocumento)} + fora da LDP {formatCurrency(facts.reconciliacaoDocumentos.custoForaDaLdp)} + sem documento {formatCurrency(facts.reconciliacaoDocumentos.custoSemDocumento)}; diferença {formatCurrency(facts.reconciliacaoDocumentos.custoDiferenca)}.</p>
      </div>

      {/* LINHA 1: CARDS DE DOCUMENTOS & MEDIDOR DE SAÚDE */}
      <div className="grid gap-6 lg:grid-cols-12">

        <div className="lg:col-span-12 grid grid-cols-2 md:grid-cols-5 gap-4" data-testid="kpis-gerais">
          <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-violet-500" title="Todos os documentos do projeto, sem exceção">
            <span className="text-xs font-bold text-violet-300 uppercase flex items-center gap-1 mb-2"><FileText className="w-3 h-3" /> Total Documentos</span>
            <span className="text-5xl font-black" data-testid="total-docs">{totalDocs}</span>
            <span className="text-[10px] text-slate-500 mt-2" data-testid="reconciliacao-docs">
              {cancelados} + {emitidos} + {noPrazo} + {atrasados} + {semPrazo} = {somaCategorias} · diferença {totalDocs - somaCategorias}
            </span>
          </div>
          <KpiGeral titulo="HH Previsto" valor={formatHH(cardsHH.HH_Previsto)} testId="kpi-hh-previsto" />
          <KpiGeral titulo="HH Realizado" valor={formatHH(cardsHH.HH_Realizado)} testId="kpi-hh-realizado" />
          <KpiGeral titulo="Custo Previsto" valor={formatCurrency(cardsHH.Custo_Previsto)} testId="kpi-custo-previsto" />
          <KpiGeral titulo="Custo Realizado" valor={formatCurrency(cardsHH.Custo_Realizado)} testId="kpi-custo-realizado" />
        </div>

        <div className="lg:col-span-12 grid grid-cols-2 md:grid-cols-5 gap-4" data-testid="cards-documentos">
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-slate-400" title="Documento com status canônico de cancelamento (Situation/Document_Status/Status_In_Flow = Canceled/Cancelado)">
            <span className="text-xs font-bold text-slate-300 uppercase flex items-center gap-1 mb-2"><X className="w-3 h-3" /> Cancelados</span>
            <span className="text-5xl font-black" data-testid="docs-cancelados">{cancelados}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-emerald-500" title="Documento não cancelado com Emission_Date preenchida na LDP">
            <span className="text-xs font-bold text-emerald-400 uppercase flex items-center gap-1 mb-2"><CheckCircle2 className="w-3 h-3" /> Emitidos</span>
            <span className="text-5xl font-black" data-testid="docs-emitidos">{emitidos}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-sky-500" title="Documento não cancelado e NÃO emitido com data alvo corrente/reprogramada >= hoje">
            <span className="text-xs font-bold text-sky-400 uppercase flex items-center gap-1 mb-2"><Target className="w-3 h-3" /> No Prazo</span>
            <span className="text-5xl font-black" data-testid="docs-no-prazo">{noPrazo}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-rose-500 relative overflow-hidden" title="Documento não cancelado e NÃO emitido com data alvo corrente/reprogramada < hoje">
            <span className="text-xs font-bold text-rose-400 uppercase flex items-center gap-1 mb-2"><AlertTriangle className="w-3 h-3" /> Atrasados</span>
            <span className="text-5xl font-black" data-testid="docs-atrasados">{atrasados}</span>
          </div>
          <div className="rounded-xl border bg-slate-800 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-amber-500" title="Documento não cancelado e NÃO emitido e sem data prevista válida">
            <span className="text-xs font-bold text-amber-400 uppercase flex items-center gap-1 mb-2"><Clock className="w-3 h-3" /> Sem Prazo</span>
            <span className="text-5xl font-black" data-testid="docs-sem-prazo">{semPrazo}</span>
          </div>
        </div>

        <div className="lg:col-span-6 grid grid-cols-2 gap-4">
          <GaugeConsumo titulo="Consumo HH" cor="sky" percentual={cardsHH.ConsumoHH} realizado={formatHH(cardsHH.HH_Realizado)} previsto={formatHH(cardsHH.HH_Previsto)} unidade="h" testId="consumo-hh" />
          <GaugeConsumo titulo="Consumo Custo" cor="emerald" percentual={cardsHH.ConsumoCusto} realizado={formatCurrency(cardsHH.Custo_Realizado)} previsto={formatCurrency(cardsHH.Custo_Previsto)} unidade="" testId="consumo-custo" />
        </div>

      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <ResumoMini label="Sem data (HH)" valor={String(base.semData)} />
        <ResumoMini label="Sem custo (HH)" valor={String(base.semCusto)} />
        <ResumoMini label="Emissões tardias" valor={String(documentosFiltrados.filter(d => d.emitido && d.statusPrazo === 'ATRASADO').length)} />
        <ResumoMini label="Sem físico" valor={String(documentosFiltrados.filter(d => d.classeFisica === 'SEM_DADO').length)} />
      </div>

      {/* LINHA 3: GRID DE DISCIPLINAS & FOCO SEMANAL (WEEKLY) */}
      <div className="grid gap-6 lg:grid-cols-12">

        <div className="lg:col-span-7 rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[460px]">
          <div className="p-4 border-b border-slate-700 flex items-center justify-between">
            <h3 className="font-semibold text-slate-100 flex items-center gap-2 text-sm">
              <Layers className="w-4 h-4 text-sky-400" /> Saúde Financeira e de HH por Disciplina <span className="text-xs text-slate-500 ml-2 font-normal">(Clique para filtrar)</span>
            </h3>
          </div>

          <div className="p-4 overflow-y-auto custom-scrollbar flex-1">
            {graficosDisciplina.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-slate-500 text-center">
                <PendenteBackend label="Nenhum documento para os filtros atuais" />
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {graficosDisciplina.map((disc) => {
                  const percentHH = disc.ConsumoHH;
                  const percentCusto = disc.ConsumoCusto;
                  const consumoHH = finite(percentHH) ? percentHH : null;
                  const consumoCusto = finite(percentCusto) ? percentCusto : null;
                  const corHH = percentHH !== null && percentHH > 105 ? 'bg-rose-500' : 'bg-sky-500';
                  const corTextoHH = percentHH !== null && percentHH > 105 ? 'text-rose-400' : 'text-sky-400';
                  const corCusto = percentCusto !== null && percentCusto > 105 ? 'bg-rose-500' : 'bg-emerald-500';
                  const corTextoCusto = percentCusto !== null && percentCusto > 105 ? 'text-rose-400' : 'text-emerald-400';
                  const isSelected = filtroDisciplina.has(disc.chave);
                  return (
                    <div
                      key={disc.chave}
                      onClick={() => setFiltroDisciplina(toggleInSet(filtroDisciplina, disc.chave))}
                      className={`bg-slate-900 border rounded-xl p-4 flex flex-col gap-3 cursor-pointer transition-all duration-200
                      ${isSelected ? 'border-sky-500 ring-1 ring-sky-500/50 bg-slate-800' : 'border-slate-700 hover:border-slate-500 hover:bg-slate-800'}`}
                    >
                      <h4 className="text-sm font-bold text-slate-100 border-b border-slate-700/50 pb-2 truncate" title={disc.Disciplina}>{disc.Disciplina}</h4>
                      <div className="flex flex-col gap-1">
                        <div className="flex justify-between items-end text-xs">
                          <span className="text-slate-400 font-medium">Esforço (HH)</span>
                          <span className={`font-bold ${corTextoHH}`}>{consumoHH === null ? '—' : `${formatHH(consumoHH)}%`}</span>
                        </div>
                        <div className="w-full bg-slate-800 rounded-full h-2">
                          <div className={`h-2 rounded-full ${percentHH === null ? 'bg-slate-700' : corHH}`} style={{ width: `${consumoHH === null ? 0 : Math.min(consumoHH, 100)}%` }}></div>
                        </div>
                        <div className="flex justify-between text-[10px] text-slate-500 mt-0.5">
                          <span>Real: {formatHH(disc.HH_Realizado)}</span>
                          <span>Prev: {formatHH(disc.HH_Previsto)}</span>
                          <span>Saldo: {formatHH(disc.SaldoHH)}</span>
                        </div>
                      </div>
                      <div className="flex flex-col gap-1">
                        <div className="flex justify-between items-end text-xs">
                          <span className="text-slate-400 font-medium">Custo (R$)</span>
                          <span className={`font-bold ${corTextoCusto}`}>{consumoCusto === null ? '—' : `${formatHH(consumoCusto)}%`}</span>
                        </div>
                        <div className="w-full bg-slate-800 rounded-full h-2">
                          <div className={`h-2 rounded-full ${percentCusto === null ? 'bg-slate-700' : corCusto}`} style={{ width: `${consumoCusto === null ? 0 : Math.min(consumoCusto, 100)}%` }}></div>
                        </div>
                        <div className="flex justify-between text-[10px] text-slate-500 mt-0.5">
                          <span>Real: {formatCurrency(disc.Custo_Realizado)} ({disc.DocsSemCusto} docs sem custo)</span>
                          <span>Previsto: {formatCurrency(disc.Custo_Previsto)} ({disc.DocsComCusto}/{disc.DocsCusto} docs)</span>
                          <span>Saldo: {formatCurrency(disc.SaldoCusto)}</span>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>

        <div className="lg:col-span-5 rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[460px]">
          <div className="p-4 border-b border-slate-700 flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-slate-100 text-sm flex items-center gap-2">
                <CalendarDays className="w-4 h-4 text-amber-400" /> Foco da Semana (Weekly)
              </h3>
            </div>
            <div className="bg-slate-900 border border-slate-700 rounded-lg p-3 flex items-center justify-between">
              <div className="flex flex-col">
                <span className="text-xs text-slate-400 uppercase font-bold">Atividades Weekly</span>
                <label className="text-xs text-slate-300">Início da semana Weekly
                  <input type="date" aria-label="Início da semana Weekly"
                    value={focoEfetivo === 'todas' ? '' : focoEfetivo} onChange={e => setFocoInicio(e.target.value)} className="block bg-slate-800 rounded p-1" />
                </label>
                <button className="text-xs text-sky-400 underline" onClick={() => { setFocoInicio('todas'); }}>Todas as atividades Weekly</button>
                <span className="text-[10px] text-slate-500">Compromissos da Programação Semanal</span>
              </div>
              <div className="text-2xl font-black text-amber-400">
                {weeklyFiltrado.length} <span className="text-sm font-medium text-slate-400">ativ.</span>
              </div>
            </div>
          </div>

          <div className="p-0 overflow-auto custom-scrollbar flex-1">
            {weeklyFiltrado.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-slate-500 p-6 text-center">
                <PendenteBackend label="Sem atividades Weekly para os filtros atuais" />
              </div>
            ) : (
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-900 sticky top-0 z-10 text-slate-400">
                  <tr>
                    <th className="px-4 py-2 font-medium">Atividade</th>
                    <th className="px-4 py-2 font-medium text-center">Baseline / Replan</th>
                    <th className="px-4 py-2 font-medium text-right">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-700/50">
                  {weeklyFiltrado.map((w) => (
                    <tr key={w.rowId} className="hover:bg-slate-700/50 transition-colors">
                      <td className="px-4 py-2">
                        <div className="font-medium truncate max-w-[200px] text-slate-200" title={w.nomeTarefa ?? undefined}>{w.nomeTarefa ?? '—'}</div>
                        <div className="text-[9px] text-slate-500">{w.numeroAtividade ?? '—'} · {w.disciplina ?? '—'}{reports.length > 1 && <span className="block text-amber-300">{base.names.get(w.projectId)}</span>}</div>
                      </td>
                      <td className="px-4 py-2 text-center whitespace-nowrap text-slate-300">
                        {formatData(w.dataBaseline)} / {formatData(w.dataReprogramada)}
                      </td>
                      <td className="px-4 py-2 text-right">
                        <span className="bg-slate-900 border border-slate-700 px-1.5 py-0.5 rounded text-sky-400 font-bold whitespace-nowrap">{w.statusProgresso ?? '—'}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

      </div>

      {/* LINHA 4: RAIO-X DE DOCUMENTOS (fonte da seleção por rowId) */}
      <div className="rounded-xl border border-slate-700 bg-slate-800/50 shadow flex flex-col min-w-0 h-[400px]">
        <div className="p-4 border-b border-slate-700 flex items-center justify-between">
          <h3 className="font-semibold text-slate-100 text-sm">Documentos</h3>
          <span className="text-xs text-slate-400">{documentosFiltrados.filter(d => !ehAtividade(d)).length} de {documentos.filter(d => !ehAtividade(d)).length} documentos{documentos.some(ehAtividade) ? ` · ${documentosFiltrados.filter(ehAtividade).length} atividades AT (HH/custo)` : ''} · clique na linha para filtrar</span>
        </div>
        <div className="p-0 overflow-auto custom-scrollbar flex-1">
          {documentosFiltrados.length === 0 ? (
            <div className="flex items-center justify-center h-full text-slate-500">Nenhum documento para os filtros atuais.</div>
          ) : (
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-900 sticky top-0 z-10 uppercase text-slate-400">
                <tr>
                  {reports.length > 1 && <th className="px-4 py-3 font-medium">Projeto</th>}
                  <th className="px-4 py-3 font-medium">Documento</th>
                  <th className="px-4 py-3 font-medium">Disciplina</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium text-right">HH Prev</th>
                  <th className="px-4 py-3 font-medium text-right">HH Real</th>
                  <th className="px-4 py-3 font-medium text-right">Saldo HH</th>
                  <th className="px-4 py-3 font-medium text-right">Consumo HH</th>
                  <th className="px-4 py-3 font-medium text-right">Custo Prev</th>
                  <th className="px-4 py-3 font-medium text-right">Custo Real</th>
                  <th className="px-4 py-3 font-medium text-right">Saldo Custo</th>
                  <th className="px-4 py-3 font-medium text-right">Consumo Custo</th>
                  <th className="px-4 py-3 font-medium">Avanço Físico</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-700/50">
                {documentosFiltrados.map((doc) => {
                  const selecionada = filtroDocumento.has(doc.rowId);
                  return (
                    <tr key={doc.rowId} onClick={() => setFiltroDocumento(toggleInSet(filtroDocumento, doc.rowId))}
                      className={`cursor-pointer transition-colors ${selecionada ? 'bg-sky-900/40 hover:bg-sky-900/50' : 'hover:bg-slate-700/30'}`}>
                      {reports.length > 1 && <td className="px-4 py-3 text-amber-300 font-bold text-[11px] whitespace-nowrap" data-testid="col-projeto">{base.names.get(doc.projectId)}</td>}
                      <td className="px-4 py-3"><div className="font-medium truncate max-w-[220px] text-slate-200" title={doc.titulo ?? undefined}>{doc.titulo ?? '—'}</div><div className="text-[10px] text-slate-500">{doc.codigo ?? '—'}{reports.length > 1 && <span className="block text-amber-300">{base.names.get(doc.projectId)}</span>}</div></td>
                      <td className="px-4 py-3 text-slate-300">{doc.disciplina ?? '—'}</td>
                      <td className="px-4 py-3 text-slate-300" title={`${doc.fonteDataAlvo}: ${formatData(doc.dataAlvo)}`}>{doc.situacaoEmissao ?? '—'}<span className="block text-[10px]" data-status={doc.statusDocumento}>{ROTULO_STATUS[doc.statusDocumento]}</span></td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatHH(doc.hhPrevisto)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatHH(doc.hhRealizado)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatHH(doc.saldoHh)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{finite(doc.consumoHh) ? `${formatHH(doc.consumoHh)}%` : '—'}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatCurrency(doc.custoPrevisto)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatCurrency(doc.custoRealizado)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{formatCurrency(doc.saldoCusto)}</td>
                      <td className="px-4 py-3 text-right text-slate-300">{finite(doc.consumoCusto) ? `${formatHH(doc.consumoCusto)}%` : '—'}</td>
                      <td className="px-4 py-3" title={doc.fonteAvancoFisico}>{doc.avancoFisico === null ? <span className="text-amber-400 italic">Sem dado</span> : <span className="text-sky-400 font-bold">{formatHH(doc.avancoFisico)}%</span>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}

function ResumoMini({ label, valor }: { label: string; valor: string }) {
  return (
    <div className="rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 flex flex-col">
      <span className="text-[9px] uppercase text-slate-500 font-bold truncate">{label}</span>
      <span className="text-lg font-bold text-slate-200">{valor}</span>
    </div>
  );
}

const ROTULO_STATUS = { EMITIDO: 'Emitido', NO_PRAZO: 'No prazo', ATRASADO: 'Atrasado', SEM_PRAZO: 'Sem prazo', CANCELADO: 'Cancelado', ATIVIDADE: 'Atividade' } as const;

function GaugeConsumo({ titulo, cor, percentual, realizado, previsto, unidade, testId }:
  { titulo: string; cor: 'sky' | 'emerald'; percentual: number | null; realizado: string; previsto: string; unidade: string; testId: string }) {
  const valido = finite(percentual);
  const traco = valido && percentual > 100 ? 'text-rose-500' : cor === 'sky' ? 'text-sky-500' : 'text-emerald-500';
  return (
    <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-4 flex flex-col items-center gap-2" data-testid={testId}>
      <span className="text-xs font-bold text-slate-400 uppercase flex items-center gap-1"><Clock className="w-3 h-3" /> {titulo}</span>
      <div className="relative w-24 h-24 flex items-center justify-center">
        <svg className="w-full h-full transform -rotate-90" viewBox="0 0 96 96">
          <circle cx="48" cy="48" r="36" stroke="currentColor" strokeWidth="8" fill="transparent" className="text-slate-800" />
          {valido && <circle cx="48" cy="48" r="36" stroke="currentColor" strokeWidth="8" fill="transparent" strokeDasharray="226.2"
            strokeDashoffset={226.2 - (226.2 * Math.min(percentual, 100)) / 100} className={traco} strokeLinecap="round" />}
        </svg>
        <span className="absolute text-lg font-black">{valido ? `${formatHHOriginal(percentual)}%` : '\u2014'}</span>
      </div>
      <span className="text-[11px] text-slate-300">Realizado: {realizado}{unidade && ` ${unidade}`}</span>
      <span className="text-[11px] text-slate-400">Previsto: {previsto}{unidade && ` ${unidade}`}</span>
    </div>
  );
}

function KpiGeral({ titulo, valor, testId }: { titulo: string; valor: string; testId: string }) {
  return (
    <div className="rounded-xl border bg-slate-900 text-slate-50 shadow p-5 flex flex-col justify-center border-t-4 border-t-slate-600" data-testid={testId}>
      <span className="text-xs font-bold text-slate-400 uppercase mb-2">{titulo}</span>
      <span className="text-2xl font-black">{valor}</span>
    </div>
  );
}
