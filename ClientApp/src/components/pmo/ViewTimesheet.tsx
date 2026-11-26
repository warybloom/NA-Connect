import { useEffect, useMemo, useState, type ReactNode } from "react";
import { AlertTriangle, CalendarDays, ChevronDown, FileText, Layers, UserRound, X } from "lucide-react";
import PendenteBackend from "./PendenteBackend";
import ConsolidadoBanner from './ConsolidadoBanner';
import type { FactsReport } from './pmoFacts';
import { PmoProps, usePmoReports, formatHH, formatCurrency, formatData, toggleInSet, rotuloDe } from './pmoData';
import { controleHhCusto, mapaGestao, formatPct, tomDesvio, motivoDesvioIndisponivel, TITULO_UNIVERSO, type UniversoHhCusto } from './hhCustoControle';
import { useHhCustoFontes } from './useHhCustoFontes';

/**
 * Controle de HH / Custo — renderer ÚNICO das três telas (original, Engenharia, Gestão). O universo só recorta os documentos
 * (classificação do Painel do Planejador); valores, regras e fontes são os mesmos (hhCustoControle → financeiroCanonico).
 * Layout para 1920×1080 sem rolagem da página: barra de filtros, cards, tabela central (rolagem interna) e quatro painéis.
 */
type Props = PmoProps & { universo?: UniversoHhCusto };

const TOM_DESVIO = { positivo: 'text-emerald-500', negativo: 'text-rose-500', neutro: 'text-sky-500', nd: 'text-muted-foreground' } as const;
const BARRA_DESVIO = { positivo: 'bg-emerald-500', negativo: 'bg-rose-500', neutro: 'bg-sky-500', nd: 'bg-transparent' } as const;
const tomSaldo = (v: number | null) => v === null ? 'text-muted-foreground' : v < 0 ? 'text-rose-500' : 'text-emerald-500';
const pctConsumo = (v: number | null) => v === null ? 'N/D' : formatPct(v);
const tomConsumo = (v: number | null) => v === null ? 'text-muted-foreground' : v > 100 ? 'text-rose-500' : 'text-sky-500';

function Card({ rotulo, valor, tom = 'text-foreground', titulo }: { rotulo: string; valor: string; tom?: string; titulo?: string }) {
  return (
    <div className="min-w-0 px-3 py-1.5" title={titulo}>
      <div className="truncate text-[10px] font-bold uppercase tracking-wide text-muted-foreground">{rotulo}</div>
      <div className={`truncate text-lg font-bold leading-tight tabular-nums ${tom}`}>{valor}</div>
    </div>
  );
}

function Grupo({ titulo, children, cols }: { titulo: string; children: ReactNode; cols: number }) {
  return (
    <section className="min-w-0 rounded-lg border border-border bg-card/60 shadow-sm">
      <h3 className="border-b border-border px-3 py-1 text-[10px] font-black uppercase tracking-widest text-sky-500">{titulo}</h3>
      <div className="grid divide-x divide-border" style={{ gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))` }}>{children}</div>
    </section>
  );
}

function Painel({ titulo, icone, children, extra }: { titulo: string; icone: ReactNode; children: ReactNode; extra?: ReactNode }) {
  return (
    <section className="flex min-h-0 min-w-0 flex-col rounded-lg border border-border bg-card/60 shadow-sm">
      <h3 className="flex items-center gap-1.5 border-b border-border px-3 py-1.5 text-[11px] font-semibold text-foreground">{icone}{titulo}{extra && <span className="ml-auto text-[10px] font-normal text-muted-foreground">{extra}</span>}</h3>
      <div className="min-h-0 flex-1 overflow-auto custom-scrollbar">{children}</div>
    </section>
  );
}

function DesvioPainel({ titulo, linhas, filtro, onToggle }: { titulo: string; linhas: { chave: string; Disciplina: string; valor: number | null; motivo: string | null }[];
  filtro: Set<string>; onToggle: (chave: string) => void }) {
  const escala = Math.max(1, ...linhas.map(l => Math.abs(l.valor ?? 0)));
  return (
    <Painel titulo={titulo} icone={<AlertTriangle className="h-3.5 w-3.5 text-amber-500" />} extra="100×(Prev−Real)/Prev">
      {linhas.length === 0 ? <div className="p-3"><PendenteBackend label="Sem disciplinas no filtro" /></div> : (
        <ul className="divide-y divide-border/50 text-[11px]">
          {linhas.map(l => {
            const tom = tomDesvio(l.valor); const largura = l.valor === null ? 0 : Math.min(50, Math.abs(l.valor) / escala * 50);
            return (
              <li key={l.chave}>
                <button type="button" onClick={() => onToggle(l.chave)} aria-pressed={filtro.has(l.chave)} title={l.motivo ?? undefined}
                  aria-label={`${l.Disciplina}: ${l.motivo ?? formatPct(l.valor)}`}
                  className={`grid w-full grid-cols-[minmax(0,7rem)_1fr_3.6rem] items-center gap-x-2 px-2 py-1 text-left hover:bg-accent/40 ${filtro.has(l.chave) ? 'bg-sky-500/10' : ''}`}>
                  <span className="truncate" title={l.Disciplina}>{l.Disciplina}</span>
                  <span className="relative h-2.5 rounded bg-muted/40" aria-hidden>
                    <span className="absolute inset-y-0 left-1/2 w-px bg-border" />
                    <span className={`absolute inset-y-0 rounded ${BARRA_DESVIO[tom]}`}
                      style={l.valor !== null && l.valor < 0 ? { right: '50%', width: `${largura}%` } : { left: '50%', width: `${largura}%` }} />
                  </span>
                  <span className={`text-right font-bold tabular-nums ${TOM_DESVIO[tom]}`}>{formatPct(l.valor)}</span>
                  {l.motivo && <span className="col-span-3 truncate text-[10px] text-muted-foreground">{l.motivo}</span>}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Painel>
  );
}

export default function ViewTimesheet(props: Props) {
  const universo = props.universo ?? 'original';
  const { reports, falhas, loading, error, retry, selectionKey } = usePmoReports('gestao-vista', props);
  const selecao = useMemo(() => props.selection ?? [{ id: props.project, hubId: props.hub, name: props.project }], [props.selection, props.project, props.hub]);
  const fontes = useHhCustoFontes(selecao, universo);
  const [filtroSemana, setFiltroSemana] = useState<Set<string>>(new Set());
  const [filtroDisciplina, setFiltroDisciplina] = useState<Set<string>>(new Set());
  const [filtroDocumento, setFiltroDocumento] = useState<Set<string>>(new Set());
  const [filtroResponsavel, setFiltroResponsavel] = useState<Set<string>>(new Set());
  const limparFiltros = () => { setFiltroSemana(new Set()); setFiltroDisciplina(new Set()); setFiltroDocumento(new Set()); setFiltroResponsavel(new Set()); };
  useEffect(limparFiltros, [selectionKey]);

  const gestao = useMemo(() => mapaGestao(fontes.classificacoes), [fontes.classificacoes]);
  const r = useMemo(() => controleHhCusto(reports as FactsReport[], universo, gestao,
    { semanas: filtroSemana, disciplinas: filtroDisciplina, documentos: filtroDocumento, responsaveis: filtroResponsavel }, fontes.responsaveis),
  [reports, universo, gestao, filtroSemana, filtroDisciplina, filtroDocumento, filtroResponsavel, fontes.responsaveis]);

  if (loading || fontes.carregandoClassificacao) return <div className="p-12 text-muted-foreground">Carregando {TITULO_UNIVERSO[universo]}...</div>;
  if (error) return <div role="alert" className="p-12 text-rose-400">{error} <button onClick={retry}>Tentar novamente</button></div>;
  if (universo !== 'original' && fontes.falhasClassificacao.length > 0)
    return <div role="alert" className="p-12 text-rose-400">Classificação de Documentos de Gestão indisponível — {fontes.falhasClassificacao.map(f => `${f.projectName}: ${f.message}`).join('; ')}. Os universos Engenharia/Gestão não são exibidos sem ela.</div>;

  const semPersistencia = universo !== 'original' ? fontes.classificacoes.filter(c => !c.schemaAvailable) : [];
  const { indicadores: k, documentosTabela, disciplinasTabela, responsaveisTabela, reconciliacaoResponsaveis: rr, pendencias } = r;
  const multi = reports.length > 1;
  const totalFiltros = filtroSemana.size + filtroDisciplina.size + filtroDocumento.size + filtroResponsavel.size;
  const docLabel = (rowId: string) => documentosTabela.find(d => d.rowId === rowId)?.codigo ?? 'Documento';
  const respLabel = (chave: string) => responsaveisTabela.find(x => x.chave === chave)?.rotulo ?? chave.replace(/^(EMPRESA|PESSOA):/, '');
  const chip = (key: string, texto: string, onRemove: () => void, cor: string) => (
    <span key={key} className={`inline-flex max-w-[14rem] items-center gap-1 rounded-full border px-2 py-0.5 text-[10px] font-medium ${cor}`}>
      <span className="truncate" title={texto}>{texto}</span>
      <button type="button" onClick={onRemove} title="Remover filtro" className="shrink-0 hover:text-foreground"><X className="h-3 w-3" /></button>
    </span>
  );
  const num = 'px-2 py-1 text-right tabular-nums whitespace-nowrap';

  return (
    <div className="flex flex-col gap-2 xl:h-[calc(100dvh-9.5rem)] xl:min-h-[620px]" data-testid="controle-hh-custo" data-universo={universo}>
      <ConsolidadoBanner selection={selecao} falhas={falhas} />
      {semPersistencia.length > 0 && (
        <p role="status" className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-[11px] text-amber-600 dark:text-amber-300">
          Persistência dos Documentos de Gestão pendente (migração não aplicada) em {semPersistencia.map(c => selecao.find(p => p.id === c.projectId)?.name ?? c.projectId).join(', ')}:
          {universo === 'gestao' ? ' nenhum documento classificado — Gestão vazia.' : ' nenhum documento classificado — Engenharia exibe todos os documentos elegíveis.'}
        </p>
      )}

      {/* Barra única de filtros: escopo + semanas (múltiplas) + filtros ativos + limpar. */}
      <div className="flex min-h-9 flex-wrap items-center gap-2 rounded-lg border border-border bg-card/60 px-3 py-1" data-testid="barra-filtros">
        <span className="text-[11px] font-black uppercase tracking-widest text-sky-500">{universo === 'original' ? 'Todos os documentos' : universo === 'gestao' ? 'Documentos de Gestão' : 'Engenharia'}</span>
        <span className="text-[11px] text-muted-foreground" title="Documentos reais da LDP no filtro; atividades (AT) e lançamentos fora da LDP contados à parte">
          {r.contagem.documentos.toLocaleString('pt-BR')} documentos{r.contagem.atividades > 0 && <> + {r.contagem.atividades} atividade(s) AT</>}{r.contagem.foraDaLdp > 0 && <> + {r.contagem.foraDaLdp} fora da LDP</>} · {totalFiltros} filtro(s)</span>
        <details className="relative">
          <summary className="flex cursor-pointer list-none items-center gap-1 rounded-md border border-border px-2 py-0.5 text-[11px] hover:bg-accent/40">
            <CalendarDays className="h-3.5 w-3.5 text-sky-500" /> Semanas LDP {filtroSemana.size > 0 && <b>({filtroSemana.size})</b>}<ChevronDown className="h-3 w-3" />
          </summary>
          <div className="absolute left-0 z-20 mt-1 max-h-64 w-48 overflow-auto rounded-md border border-border bg-popover p-1 shadow-lg custom-scrollbar">
            {r.semanas.length === 0 ? <p className="p-2 text-[11px] text-muted-foreground">Sem semanas LDP.</p> : r.semanas.map(s => (
              <label key={s} className="flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-[11px] hover:bg-accent/40">
                <input type="checkbox" checked={filtroSemana.has(s)} onChange={() => setFiltroSemana(toggleInSet(filtroSemana, s))} /> {formatData(s)}
              </label>
            ))}
          </div>
        </details>
        <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1">
          {[...filtroSemana].map(v => chip(`s-${v}`, `Semana ${formatData(v)}`, () => setFiltroSemana(toggleInSet(filtroSemana, v)), 'border-sky-500/50 text-sky-600 dark:text-sky-300'))}
          {[...filtroDisciplina].map(v => chip(`d-${v}`, `Disc: ${rotuloDe(v)}`, () => setFiltroDisciplina(toggleInSet(filtroDisciplina, v)), 'border-emerald-500/50 text-emerald-600 dark:text-emerald-300'))}
          {[...filtroDocumento].map(v => chip(`doc-${v}`, `Doc: ${docLabel(v)}`, () => setFiltroDocumento(toggleInSet(filtroDocumento, v)), 'border-purple-500/50 text-purple-600 dark:text-purple-300'))}
          {[...filtroResponsavel].map(v => chip(`r-${v}`, `Resp: ${respLabel(v)}`, () => setFiltroResponsavel(toggleInSet(filtroResponsavel, v)), 'border-amber-500/50 text-amber-600 dark:text-amber-300'))}
        </div>
        {totalFiltros > 0 && <button type="button" onClick={limparFiltros} className="text-[10px] font-bold uppercase text-rose-500 underline hover:text-rose-400">Limpar tudo</button>}
      </div>

      {/* Cards: três grupos na mesma fileira. */}
      <div className="grid gap-2 lg:grid-cols-[3fr_3fr_2fr]" data-testid="cards-hh-custo">
        <Grupo titulo="Análise de Horas" cols={3}>
          <Card rotulo="HH Planejado" valor={formatHH(k.hhPlanejado)} titulo="Σ Planned_HH da última carga da LDP (documentos do escopo)" />
          <Card rotulo="HH Realizado" valor={formatHH(k.hhRealizado)} tom="text-sky-500" titulo="HH realizado oficial (Actual_HH_Total_Corrigido)" />
          <Card rotulo="Saldo HH" valor={formatHH(k.saldoHh)} tom={tomSaldo(k.saldoHh)} titulo="Planejado − Realizado" />
        </Grupo>
        <Grupo titulo="Análise de Custo" cols={3}>
          <Card rotulo="Custo Planejado" valor={formatCurrency(k.custoPlanejado)} titulo="Σ Planned_Cost da última carga da LDP" />
          <Card rotulo="Custo Realizado" valor={formatCurrency(k.custoRealizado)} tom="text-emerald-500" titulo="Custo realizado oficial" />
          <Card rotulo="Saldo Custo" valor={formatCurrency(k.saldoCusto)} tom={tomSaldo(k.saldoCusto)} titulo="Planejado − Realizado; custo ausente mantém saldo indisponível" />
        </Grupo>
        <Grupo titulo="% Consumido" cols={2}>
          <Card rotulo="% HH Consumido" valor={pctConsumo(k.consumoHh)} tom={tomConsumo(k.consumoHh)} titulo="100 × Realizado / Planejado (sem limite de 100%; planejado zero/ausente = N/D)" />
          <Card rotulo="% Custo Consumido" valor={pctConsumo(k.consumoCusto)} tom={tomConsumo(k.consumoCusto)} titulo="100 × Realizado / Planejado (sem limite de 100%; planejado zero/ausente = N/D)" />
        </Grupo>
      </div>

      {/* Detalhamento por Documento: largura total, rolagem vertical interna. */}
      <section className="flex min-h-[260px] flex-1 flex-col rounded-lg border border-border bg-card/60 shadow-sm xl:min-h-0">
        <h3 className="flex items-center gap-1.5 border-b border-border px-3 py-1.5 text-[11px] font-semibold"><FileText className="h-3.5 w-3.5 text-sky-500" /> Detalhamento por Documento <span className="ml-auto text-[10px] font-normal text-muted-foreground">Clique na linha para filtrar</span></h3>
        <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden custom-scrollbar">
          {documentosTabela.length === 0 ? <div className="flex h-full items-center justify-center p-4"><PendenteBackend label="Sem documentos para os filtros atuais" /></div> : (
            <table className="w-full table-fixed text-[11px]" data-testid="tabela-documentos">
              <colgroup><col className="w-12" /><col className="w-[11%]" /><col /><col className="w-[8%]" /><col className="w-[8%]" /><col className="w-[8%]" /><col className="w-[10%]" /><col className="w-[10%]" /><col className="w-[10%]" /></colgroup>
              <thead className="sticky top-0 z-10 bg-card text-[10px] uppercase text-muted-foreground">
                <tr className="border-b border-border">
                  <th className="px-2 py-1 text-left">Item</th><th className="px-2 py-1 text-left">Disciplina</th><th className="px-2 py-1 text-left">Título do Documento</th>
                  <th className="px-2 py-1 text-right">HH Planejado</th><th className="px-2 py-1 text-right">HH Realizado</th><th className="px-2 py-1 text-right">Saldo HH</th>
                  <th className="px-2 py-1 text-right">Custo Planejado</th><th className="px-2 py-1 text-right">Custo Realizado</th><th className="px-2 py-1 text-right">Saldo de Custo</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {documentosTabela.map(d => (
                  <tr key={d.rowId} onClick={() => setFiltroDocumento(toggleInSet(filtroDocumento, d.rowId))}
                    className={`cursor-pointer ${filtroDocumento.has(d.rowId) ? 'bg-purple-500/10' : 'hover:bg-accent/40'}`}>
                    <td className="px-2 py-1 tabular-nums text-muted-foreground">{d.item}</td>
                    <td className="truncate px-2 py-1" title={d.disciplinaRotulo}>{d.disciplinaRotulo}</td>
                    <td className="px-2 py-1">
                      <div className="truncate font-medium" title={`${d.codigo ?? 'Sem código'} — ${d.documento ?? 'Sem título'}${multi ? ` · ${d.nomeProjeto}` : ''}`}>
                        <span className="text-muted-foreground">{d.codigo ?? 'Sem código'}</span> · {d.documento ?? '—'}{multi && <span className="text-amber-500"> · {d.nomeProjeto}</span>}
                      </div>
                    </td>
                    <td className={num}>{formatHH(d.HH_Previsto)}</td>
                    <td className={`${num} font-bold text-sky-500`}>{d.HH_Realizado === null ? <span className="font-normal italic text-amber-500">Sem dado</span> : formatHH(d.HH_Realizado)}</td>
                    <td className={`${num} ${tomSaldo(d.SaldoHH)}`}>{formatHH(d.SaldoHH)}</td>
                    <td className={num}>{formatCurrency(d.Custo_Previsto)}</td>
                    <td className={`${num} font-bold text-emerald-500`}>{d.Custo_Realizado === null ? <span className="font-normal italic text-amber-500">Sem dado</span> : formatCurrency(d.Custo_Realizado)}</td>
                    <td className={`${num} ${tomSaldo(d.SaldoCusto)}`}>{formatCurrency(d.SaldoCusto)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      {/* Painéis inferiores. */}
      <div className="grid gap-2 md:grid-cols-2 xl:h-[210px] xl:shrink-0 xl:grid-cols-[1.35fr_1fr_1fr_1fr]">
        <Painel titulo="HH e Custo por Disciplina" icone={<Layers className="h-3.5 w-3.5 text-sky-500" />} extra="Clique para filtrar">
          {disciplinasTabela.length === 0 ? <div className="p-3"><PendenteBackend label="Sem lançamentos para os filtros atuais" /></div> : (
            <table className="w-full table-fixed text-[11px]" data-testid="tabela-disciplinas">
              <thead className="sticky top-0 bg-card text-[10px] uppercase text-muted-foreground">
                <tr><th className="px-2 py-1 text-left">Disciplina</th><th className="w-[17%] px-1 py-1 text-right">HH Prev</th><th className="w-[17%] px-1 py-1 text-right">HH Real</th><th className="w-[21%] px-1 py-1 text-right">Custo Prev</th><th className="w-[21%] px-1 py-1 text-right">Custo Real</th></tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {disciplinasTabela.map(d => (
                  <tr key={d.chave} onClick={() => setFiltroDisciplina(toggleInSet(filtroDisciplina, d.chave))}
                    className={`cursor-pointer ${filtroDisciplina.has(d.chave) ? 'bg-sky-500/10' : 'hover:bg-accent/40'}`}>
                    <td className="truncate px-2 py-1 font-medium" title={d.Disciplina}>{d.Disciplina}</td>
                    <td className="px-1 py-1 text-right tabular-nums">{formatHH(d.HH_Previsto)}</td>
                    <td className="px-1 py-1 text-right font-bold tabular-nums text-sky-500">{formatHH(d.HH_Realizado)}</td>
                    <td className="truncate px-1 py-1 text-right tabular-nums" title={formatCurrency(d.Custo_Previsto)}>{formatCurrency(d.Custo_Previsto)}</td>
                    <td className="truncate px-1 py-1 text-right font-bold tabular-nums text-emerald-500" title={formatCurrency(d.Custo_Realizado)}>{formatCurrency(d.Custo_Realizado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Painel>

        <Painel titulo="Responsável" icone={<UserRound className="h-3.5 w-3.5 text-amber-500" />} extra="HH realizado">
          {fontes.carregandoResponsaveis ? <p className="p-3 text-[11px] text-muted-foreground">Carregando responsáveis…</p> : (
            <>
              {fontes.falhasResponsaveis.length > 0 && <p className="px-2 py-1 text-[10px] text-amber-500">{fontes.falhasResponsaveis.map(f => `${f.projectName}: ${f.message}`).join('; ')}</p>}
              {responsaveisTabela.length === 0 ? <div className="p-3"><PendenteBackend label="Sem lançamentos por responsável no filtro" /></div> : (
                <ul className="divide-y divide-border/50 text-[11px]" data-testid="lista-responsaveis">
                  {responsaveisTabela.map(x => {
                    const composicao = x.hhMedicao > 0 ? `${formatHH(x.hhTimesheet)} h Timesheet + ${formatHH(x.hhMedicao)} h medição histórica confirmada` : null;
                    const detalhes = [x.detalhe, x.pessoas.length > 1 || x.chave.startsWith('EMPRESA:') ? `Lançado por: ${x.pessoas.join(', ')}` : null, composicao,
                      x.lancadoComo.length ? `Disciplina do lançamento: ${x.lancadoComo.join(', ')} (o documento mantém a sua disciplina)` : null,
                      x.corrigidos.length ? `De/Para: ${x.corrigidos.join('; ')}` : null, x.pendencia].filter(Boolean).join(' · ');
                    return (
                      <li key={x.chave}>
                        <button type="button" onClick={() => setFiltroResponsavel(toggleInSet(filtroResponsavel, x.chave))} aria-pressed={filtroResponsavel.has(x.chave)}
                          aria-label={`Filtrar por ${x.rotulo}: ${formatHH(x.hh)} HH realizado`} title={detalhes}
                          className={`block w-full border-l-2 px-2 py-1 text-left hover:bg-accent/40 ${filtroResponsavel.has(x.chave) ? 'border-amber-500 bg-amber-500/10' : 'border-transparent'}`}>
                          <span className="flex min-w-0 items-center gap-1 font-medium leading-tight">
                            <span className="truncate">{x.rotulo}</span>{x.pendencia && <AlertTriangle className="h-3 w-3 shrink-0 text-amber-500" aria-label={x.pendencia} />}
                          </span>
                          <span className="flex min-w-0 items-baseline gap-1.5 leading-tight">
                            <span className="font-bold tabular-nums text-sky-500">{formatHH(x.hh)} h</span>
                            <span className="truncate text-[10px] text-muted-foreground">
                              {x.documentos} doc{composicao && <> · {formatHH(x.hhTimesheet)} TS + {formatHH(x.hhMedicao)} medição</>}{x.lancadoComo.length > 0 && <> · lançado como {x.lancadoComo.join(', ')}</>}
                            </span>
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </>
          )}
        </Painel>

        <DesvioPainel titulo="% Desvio de Horas por Disciplina" filtro={filtroDisciplina} onToggle={c => setFiltroDisciplina(toggleInSet(filtroDisciplina, c))}
          linhas={disciplinasTabela.map(d => ({ chave: d.chave, Disciplina: d.Disciplina, valor: d.DesvioHH,
            motivo: motivoDesvioIndisponivel(d.HH_Previsto, d.HH_Realizado, 'hh', formatHH) }))} />
        <DesvioPainel titulo="% Desvio de Custo por Disciplina" filtro={filtroDisciplina} onToggle={c => setFiltroDisciplina(toggleInSet(filtroDisciplina, c))}
          linhas={disciplinasTabela.map(d => ({ chave: d.chave, Disciplina: d.Disciplina, valor: d.DesvioCusto,
            motivo: motivoDesvioIndisponivel(d.Custo_Previsto, d.Custo_Realizado, 'custo', formatCurrency) }))} />
      </div>

      {/* Reconciliação em uma linha (fonte e diferenças explicitadas, nunca escondidas). */}
      <p className="shrink-0 truncate text-[10px] text-muted-foreground" data-testid="reconciliacao-hh-custo"
        title={`Responsáveis vêm do Timesheet corrigido com De/Para (gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial, mesma fonte do HH oficial por documento) por data de lançamento, mais o complemento de medição histórica confirmada dos pacotes (sem data). O HH realizado dos documentos é o oficial (Actual_HH_Total_Corrigido + complemento). HH contratado de OS e lançamentos fora da LDP explicam diferenças restantes. A semana filtra documentos pela semana LDP e responsáveis pela data do lançamento.${r.diagnosticosDisciplina.length ? ' Disciplinas: ' + r.diagnosticosDisciplina.join(' ') : ''}`}>
        Responsáveis (Timesheet corrigido{rr.hhMedicaoHistorica ? ' + medição histórica' : ''}): {formatHH(rr.hhPessoasDocs)} h em documentos{rr.hhMedicaoHistorica ? <> (dos quais {formatHH(rr.hhMedicaoHistorica)} h medição histórica confirmada)</> : null}{rr.hhPessoasSemDoc !== null && <> + {formatHH(rr.hhPessoasSemDoc)} h sem documento/fora da LDP</>} · documentos (oficial): {formatHH(rr.hhDocumentos)} h · diferença {formatHH(rr.diferenca)} h
        {rr.medicaoForaDaSemana ? <> · {formatHH(rr.medicaoForaDaSemana)} h de medição histórica sem data fora do filtro de semana</> : null}
        {r.disciplinasNaoReconhecidas.length > 0 && <> · Disciplina(s) sem cadastro (mantidas como estão): {r.disciplinasNaoReconhecidas.join(', ')}</>}
        {pendencias && <> · Fora de Engenharia/Gestão: {formatHH(pendencias.hhSemDocumento)} h sem documento, {formatHH(pendencias.hhForaDaLdp)} h fora da LDP{pendencias.linhasSemCodigo > 0 && <>, {pendencias.linhasSemCodigo} linha(s) LDP sem código (em Engenharia, não classificáveis)</>}{pendencias.classificadosForaDaLdp > 0 && <>, {pendencias.classificadosForaDaLdp} classificado(s) fora da LDP atual</>}</>}
        {r.base.semCusto > 0 && <> · {r.base.semCusto} lançamento(s) sem custo calculado</>}
      </p>
    </div>
  );
}
