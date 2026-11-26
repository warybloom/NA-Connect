// Aba "Direcionamentos de HH" de Qualidade > Auditoria: uma linha por ORIGEM com De/Para, Redistribuição, AT,
// sem direcionamento, conflitos e histórico. Abrir a aba ou trocar filtros só faz GET; mutações só pelo modal.
import { useEffect, useMemo, useRef, useState } from "react";
import {
  AlertTriangle,
  CircleSlash,
  History,
  Link2,
  Pencil,
  RefreshCw,
  Repeat,
  RotateCcw,
  Search,
  Split,
  Tag,
  Unplug,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  EMPTY_ROUTING_FILTERS,
  filterOrigins,
  getRoutingControl,
  groupByOrigin,
  isAbortError,
  METHOD_LABEL,
  operationsFor,
  RoutingControlError,
  ROUTING_UNAVAILABLE_TITLE,
  summarizeOrigins,
  type RoutingAlertKind,
  type RoutingControlItem,
  type RoutingControlResult,
  type RoutingFilters,
  type RoutingHistoryEntry,
  type RoutingMethod,
  type RoutingOperation,
  type RoutingOrigin,
  type RoutingStatusFilter,
  type RoutingTypeFilter,
} from "./routingControlApi";
import RoutingControlDialog, { formatCurrency, formatHours, type RoutingDialogTarget } from "./RoutingControlDialog";

interface Props {
  projectId: string;
  /** quality.audit.edit no projeto. Falso ou carregando → nenhum controle de mutação é renderizado. */
  canEdit: boolean;
  userLabel: string | null;
  /** Recarrega a auditoria principal depois de uma operação aplicada. */
  onApplied: () => Promise<void> | void;
  /** Reavaliação: depois do reset, abre o preview oficial de redistribuição para a origem. */
  onReevaluate: (sourceDocumentCode: string) => void;
}

export default function RoutingControlSection({ projectId, canEdit, userLabel, onApplied, onReevaluate }: Props) {
  const [data, setData] = useState<RoutingControlResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<RoutingControlError | Error | null>(null);
  const [filters, setFilters] = useState<RoutingFilters>(EMPTY_ROUTING_FILTERS);
  const [target, setTarget] = useState<RoutingDialogTarget | null>(null);
  const seq = useRef(0);
  const abort = useRef<AbortController | null>(null);

  const load = async () => {
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    const current = ++seq.current;
    setLoading(true);
    setError(null);
    try {
      const result = await getRoutingControl(projectId, controller.signal);
      // Resposta de projeto/carga anterior nunca sobrescreve a atual (troca de projeto incrementa seq).
      if (current !== seq.current) return;
      setData(result);
    } catch (e) {
      if (isAbortError(e) || current !== seq.current) return;
      setData(null);
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      if (current === seq.current) setLoading(false);
    }
  };

  useEffect(() => {
    setData(null);
    setTarget(null);
    setFilters(EMPTY_ROUTING_FILTERS);
    void load();
    return () => {
      abort.current?.abort();
      seq.current += 1;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId]);

  // Sem permissão de edição o modal nunca fica aberto (ex.: permissões recarregadas).
  useEffect(() => { if (!canEdit) setTarget(null); }, [canEdit]);

  const origins = useMemo(() => (data ? groupByOrigin(data) : []), [data]);
  const filtered = useMemo(() => filterOrigins(origins, filters), [origins, filters]);
  const totals = useMemo(() => summarizeOrigins(filtered), [filtered]);
  const disciplines = useMemo(
    () => [...new Set(origins.map((o) => o.discipline).filter((x): x is string => !!x))].sort((a, b) => a.localeCompare(b, "pt-BR")),
    [origins],
  );
  const professionals = useMemo(
    () => [...new Set(origins.flatMap((o) => o.professionals))].sort((a, b) => a.localeCompare(b, "pt-BR")),
    [origins],
  );
  const visibleKeys = useMemo(() => new Set(filtered.map((o) => o.key)), [filtered]);
  const visibleHistory = useMemo(
    () => (data?.history ?? [])
      .filter((h) => visibleKeys.has(h.sourceDocumentCode.trim().toLocaleUpperCase("pt-BR")))
      .sort((a, b) => (b.atUtc ?? "").localeCompare(a.atUtc ?? "")),
    [data, visibleKeys],
  );

  const set = <K extends keyof RoutingFilters>(key: K, value: RoutingFilters[K]) => setFilters((f) => ({ ...f, [key]: value }));
  const filtersActive = JSON.stringify(filters) !== JSON.stringify(EMPTY_ROUTING_FILTERS);

  const openAction = (origin: RoutingOrigin, item: RoutingControlItem, operation: RoutingOperation, reevaluate = false) => {
    if (!canEdit) return;
    setTarget({ origin, item, operation, reevaluate });
  };

  const handleApplied = async (applied: RoutingDialogTarget) => {
    setTarget(null);
    // Limpa e recarrega a partir do servidor: a tela nunca confia no estado local depois de gravar.
    setData(null);
    await Promise.all([load(), onApplied()]);
    if (applied.reevaluate) onReevaluate(applied.item.sourceDocumentCode);
  };

  const unavailable = error && (!(error instanceof RoutingControlError) || error.kind === "unavailable" || error.kind === "failed");

  return (
    <section aria-labelledby="routing-control-title" className="space-y-3 rounded-xl border border-border bg-card/60 p-4">
      <div className="flex flex-wrap items-center gap-3">
        <Split className="h-5 w-5 text-primary" aria-hidden />
        <div className="min-w-0 flex-1">
          <h2 id="routing-control-title" className="text-sm font-black">Direcionamentos de HH</h2>
          <p className="text-xs text-muted-foreground">
            <b className="text-foreground">De/Para</b> = vínculo específico da origem a um documento da LDP ·{" "}
            <b className="text-foreground">Redistribuição</b> = rateio por disciplina, não prova trabalho no documento.
            Os dois nunca se somam na mesma origem.
          </p>
        </div>
        {!canEdit && <span className="rounded-md border border-border px-2 py-1 text-[11px] font-semibold text-muted-foreground">Somente leitura</span>}
        <Button size="sm" variant="outline" className="gap-2" onClick={() => void load()} disabled={loading}>
          <RefreshCw className={cn("h-3.5 w-3.5", loading && "animate-spin")} />
          {loading ? "Carregando…" : "Atualizar"}
        </Button>
      </div>

      {error && (
        <div role="alert" className="flex flex-wrap items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span className="flex-1">
            <b>{unavailable ? ROUTING_UNAVAILABLE_TITLE : "Não foi possível carregar os direcionamentos"}</b> · {error.message}
            {" "}As demais funções da Auditoria continuam disponíveis.
          </span>
          <Button size="sm" variant="outline" onClick={() => void load()} disabled={loading}>Tentar novamente</Button>
        </div>
      )}

      {!data && !error && loading && <p className="text-xs text-muted-foreground">Consultando De/Para, redistribuições e atividades AT…</p>}

      {data && (
        <>
          <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filtros dos direcionamentos">
            <div className="relative min-w-[220px] flex-1">
              <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" aria-hidden />
              <input
                aria-label="Buscar código ou título"
                value={filters.search}
                onChange={(e) => set("search", e.target.value)}
                placeholder="Código ou título (origem ou destino)"
                className="h-9 w-full rounded-md border border-input bg-background pl-9 pr-3 text-sm"
              />
            </div>
            <FilterSelect label="Tipo" value={filters.type} onChange={(v) => set("type", v as RoutingTypeFilter)} options={[
              ["TODOS", "Todos os tipos"], ["DE_PARA", "De/Para"], ["REDISTRIBUICAO", "Redistribuição"], ["AT", "AT"],
              ["SEM_DIRECIONAMENTO", "Sem tratamento"], ["CONFLITANTE", "Conflitantes"], ["HISTORICO", "Com histórico/inativos"],
            ]} />
            <FilterSelect label="Status" value={filters.status} onChange={(v) => set("status", v as RoutingStatusFilter)} options={[
              ["TODOS", "Ativos e inativos"], ["ATIVO", "Com método ativo"], ["INATIVO", "Com registro inativo"],
            ]} />
            <FilterSelect label="Disciplina" value={filters.discipline} onChange={(v) => set("discipline", v)}
              options={[["", "Todas as disciplinas"], ...disciplines.map((d): [string, string] => [d, d])]} />
            <FilterSelect label="Profissional" value={filters.professional} onChange={(v) => set("professional", v)}
              options={[["", "Todos os profissionais"], ...professionals.map((p): [string, string] => [p, p])]} />
            {filtersActive && <Button size="sm" variant="ghost" onClick={() => setFilters(EMPTY_ROUTING_FILTERS)}>Limpar</Button>}
          </div>

          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span><b className="text-foreground">{totals.origins.toLocaleString("pt-BR")}</b> origem(ns) · {formatHours(totals.hours)} HH · {formatCurrency(totals.cost)} (cada origem contada uma vez)</span>
            <span>De/Para {totals.byMethod.DE_PARA} · Redistribuição {totals.byMethod.REDISTRIBUICAO} · AT {totals.byMethod.AT} · Sem tratamento {totals.byMethod.SEM_DIRECIONAMENTO}</span>
            {totals.conflicting > 0 && <span className="font-semibold text-rose-700 dark:text-rose-300">{totals.conflicting} conflitante(s)</span>}
            {data.generatedAtUtc && <span className="ml-auto">Atualizado em {new Date(data.generatedAtUtc).toLocaleString("pt-BR")}</span>}
          </div>

          <RoutingOriginsTable origins={filtered} canEdit={canEdit} onAction={openAction} />

          <RoutingHistoryPanel entries={visibleHistory} />
        </>
      )}

      {canEdit && (
        <RoutingControlDialog
          projectId={projectId}
          target={target}
          userLabel={userLabel}
          onClose={() => setTarget(null)}
          onApplied={handleApplied}
        />
      )}
    </section>
  );
}

function FilterSelect({ label, value, onChange, options }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<[string, string]>;
}) {
  return (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)}
      className="h-9 max-w-[220px] rounded-md border border-input bg-background px-2 text-sm">
      {options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
    </select>
  );
}

const METHOD_STYLE: Record<RoutingMethod, { className: string; icon: typeof Link2 }> = {
  DE_PARA: { className: "border-violet-500/40 bg-violet-500/10 text-violet-800 dark:text-violet-300", icon: Link2 },
  REDISTRIBUICAO: { className: "border-sky-500/40 bg-sky-500/10 text-sky-800 dark:text-sky-300", icon: Split },
  AT: { className: "border-emerald-500/40 bg-emerald-500/10 text-emerald-800 dark:text-emerald-300", icon: Tag },
  SEM_DIRECIONAMENTO: { className: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300", icon: CircleSlash },
};

const ALERT_STYLE: Record<RoutingAlertKind, string> = {
  AMBOS_ATIVOS: "border-rose-500/40 bg-rose-500/10 text-rose-800 dark:text-rose-300",
  CONFLITO: "border-rose-500/40 bg-rose-500/10 text-rose-800 dark:text-rose-300",
  DESTINO_CANCELADO: "border-rose-500/40 bg-rose-500/10 text-rose-800 dark:text-rose-300",
  DIVERGENCIA_DISCIPLINA: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  ORIGEM_ATIVA_NA_LDP: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  HH_ALTERADAS: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  HH_ORIGEM_DIVERGENTE: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  SEM_LDP: "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  AVISO: "border-border bg-muted/40 text-foreground",
};

const ACTION_META: Record<RoutingOperation, { label: string; icon: typeof Pencil }> = {
  EDIT_DE_PARA: { label: "Editar", icon: Pencil },
  RESET_DE_PARA: { label: "Resetar De/Para", icon: RotateCcw },
  RESET_REDISTRIBUICAO: { label: "Resetar", icon: RotateCcw },
};

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleString("pt-BR") : "—");

/** Tabela pura (sem contexto/efeitos): também usada nos testes de renderização. */
export function RoutingOriginsTable({ origins, canEdit, onAction }: {
  origins: RoutingOrigin[];
  canEdit: boolean;
  onAction: (origin: RoutingOrigin, item: RoutingControlItem, operation: RoutingOperation, reevaluate?: boolean) => void;
}) {
  if (!origins.length) {
    return (
      <div className="flex min-h-24 items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground">
        Nenhuma origem para os filtros atuais.
      </div>
    );
  }
  return (
    <div className="overflow-x-auto rounded-lg border border-border">
      <table className="w-full table-fixed text-xs">
        <caption className="sr-only">Direcionamentos de HH por origem</caption>
        <thead className="bg-muted/40 text-left text-muted-foreground">
          <tr>
            <th scope="col" className="w-[26%] px-3 py-2">Origem · disciplina · pessoas</th>
            <th scope="col" className="w-[11%] px-3 py-2 text-right">HH / custo origem</th>
            <th scope="col" className="px-3 py-2">Direcionamento · destinos · decisão</th>
            <th scope="col" className="w-[20%] px-3 py-2">Alertas</th>
          </tr>
        </thead>
        <tbody>
          {origins.map((origin) => (
            <tr key={origin.key} className={cn("border-t align-top", origin.bothActive && "bg-rose-500/[0.04]")}>
              <td className="px-3 py-2.5">
                <p className="break-all font-mono font-semibold">{origin.sourceDocumentCode}</p>
                {origin.sourceTitle && <p className="mt-0.5 break-words text-muted-foreground">{origin.sourceTitle}</p>}
                <p className="mt-0.5 font-semibold">{origin.discipline ?? <span className="text-amber-700 dark:text-amber-300">Disciplina não resolvida</span>}</p>
                {origin.professionals.length > 0 && (
                  <p className="mt-0.5 break-words text-muted-foreground" title={origin.professionals.join(", ")}>
                    {origin.professionals.slice(0, 3).join(", ")}{origin.professionals.length > 3 && ` +${origin.professionals.length - 3}`}
                  </p>
                )}
              </td>
              <td className="px-3 py-2.5 text-right tabular-nums">
                <p className="font-semibold">{formatHours(origin.sourceHours)} HH</p>
                <p className="text-muted-foreground">{formatCurrency(origin.sourceCost)}</p>
              </td>
              <td className="space-y-2 px-3 py-2.5">
                {origin.items.map((item, index) => (
                  <MethodBlock key={`${item.method}:${item.decisionId ?? index}`} origin={origin} item={item} canEdit={canEdit} onAction={onAction} />
                ))}
              </td>
              <td className="px-3 py-2.5">
                {origin.alerts.length === 0 ? <span className="text-muted-foreground">—</span> : (
                  <ul className="flex flex-wrap gap-1">
                    {origin.alerts.map((alert) => (
                      <li key={`${alert.kind}:${alert.text}`} className={cn("rounded-md border px-1.5 py-0.5 text-[11px] font-semibold leading-snug", ALERT_STYLE[alert.kind])}>
                        {alert.text}
                      </li>
                    ))}
                  </ul>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MethodBlock({ origin, item, canEdit, onAction }: {
  origin: RoutingOrigin;
  item: RoutingControlItem;
  canEdit: boolean;
  onAction: (origin: RoutingOrigin, item: RoutingControlItem, operation: RoutingOperation, reevaluate?: boolean) => void;
}) {
  const style = METHOD_STYLE[item.method];
  const Icon = style.icon;
  const operations = operationsFor(item, canEdit);
  return (
    <div className={cn("rounded-md border px-2.5 py-2", item.active ? "border-border bg-background/60" : "border-dashed border-border/70 opacity-75")}>
      <div className="flex flex-wrap items-center gap-1.5">
        <span className={cn("inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] font-bold", style.className)}>
          <Icon className="h-3 w-3" aria-hidden />{METHOD_LABEL[item.method]}
        </span>
        <span className={cn("rounded-full border px-1.5 py-0.5 text-[10px] font-bold uppercase",
          item.active ? "border-emerald-500/40 text-emerald-700 dark:text-emerald-300" : "border-border text-muted-foreground")}>
          {item.active ? "Ativo" : "Inativo"}
        </span>
        {item.conflict && <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-rose-700 dark:text-rose-300"><Unplug className="h-3 w-3" aria-hidden />Conflito</span>}
        {operations.length > 0 && (
          <span className="ml-auto flex flex-wrap gap-1">
            {operations.map((operation) => {
              const meta = ACTION_META[operation];
              const ActionIcon = meta.icon;
              return (
                <Button key={operation} size="sm" variant="outline" className="h-7 gap-1 px-2 text-[11px]"
                  aria-label={`${meta.label} · ${origin.sourceDocumentCode}`}
                  onClick={() => onAction(origin, item, operation)}>
                  <ActionIcon className="h-3 w-3" aria-hidden />{meta.label}
                </Button>
              );
            })}
            {item.method === "REDISTRIBUICAO" && (
              <Button size="sm" variant="outline" className="h-7 gap-1 px-2 text-[11px]"
                aria-label={`Reavaliar redistribuição · ${origin.sourceDocumentCode}`}
                onClick={() => onAction(origin, item, "RESET_REDISTRIBUICAO", true)}>
                <Repeat className="h-3 w-3" aria-hidden />Reavaliar
              </Button>
            )}
          </span>
        )}
      </div>

      {item.method === "AT" && item.destinations.length === 0 && (
        <p className="mt-1 text-muted-foreground">Sem destino documental · HH fica no Timesheet e na disciplina{item.discipline ? ` ${item.discipline}` : ""}.</p>
      )}
      {item.method === "SEM_DIRECIONAMENTO" && <p className="mt-1 text-muted-foreground">Sem De/Para, redistribuição ou AT para esta origem.</p>}

      {item.destinations.length > 0 && (
        <ul className="mt-1.5 space-y-0.5">
          {item.destinations.map((d) => (
            <li key={d.documentCode} className="grid grid-cols-[1fr_auto_auto] items-baseline gap-x-3">
              <span className="min-w-0 break-words">
                <span className="break-all font-mono">{d.documentCode}</span>
                {d.title && <span className="text-muted-foreground"> · {d.title}</span>}
              </span>
              <span className="tabular-nums">{formatHours(d.hours)} HH</span>
              <span className="tabular-nums text-muted-foreground">{formatCurrency(d.cost)}</span>
            </li>
          ))}
        </ul>
      )}
      {item.destinations.length === 0 && item.destinationDocumentCode && (
        <p className="mt-1">Destino <span className="break-all font-mono">{item.destinationDocumentCode}</span></p>
      )}

      {(item.decisionBy || item.decisionAtUtc || item.reason) && (
        <p className="mt-1.5 break-words text-[11px] text-muted-foreground">
          {item.decisionBy ?? "Responsável não informado"} · {formatDate(item.decisionAtUtc)}
          {item.reason && <> · “{item.reason}”</>}
        </p>
      )}
    </div>
  );
}

export function RoutingHistoryPanel({ entries }: { entries: RoutingHistoryEntry[] }) {
  return (
    <details className="rounded-lg border border-border bg-background/40">
      <summary className="flex cursor-pointer items-center gap-2 px-3 py-2 text-xs font-bold">
        <History className="h-4 w-4 text-muted-foreground" aria-hidden />
        Histórico de direcionamentos ({entries.length.toLocaleString("pt-BR")})
      </summary>
      {entries.length === 0 ? (
        <p className="border-t border-border px-3 py-2 text-xs text-muted-foreground">Nenhum registro de histórico para as origens visíveis.</p>
      ) : (
        <div className="max-h-[360px] overflow-auto border-t border-border">
          <table className="w-full table-fixed text-xs">
            <thead className="sticky top-0 bg-card text-left text-muted-foreground">
              <tr>
                <th scope="col" className="w-[14%] px-3 py-1.5">Quando</th>
                <th scope="col" className="w-[20%] px-3 py-1.5">Origem</th>
                <th scope="col" className="w-[12%] px-3 py-1.5">Método</th>
                <th scope="col" className="px-3 py-1.5">Antes → depois</th>
                <th scope="col" className="w-[14%] px-3 py-1.5">Quem</th>
                <th scope="col" className="w-[20%] px-3 py-1.5">Motivo</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry, index) => (
                <tr key={`${entry.sourceDocumentCode}:${entry.atUtc ?? ""}:${index}`} className="border-t align-top">
                  <td className="px-3 py-1.5">{formatDate(entry.atUtc)}</td>
                  <td className="break-all px-3 py-1.5 font-mono">{entry.sourceDocumentCode}</td>
                  <td className="px-3 py-1.5">{METHOD_LABEL[entry.method as RoutingMethod] ?? entry.method}</td>
                  <td className="break-words px-3 py-1.5">{entry.details ?? "—"}</td>
                  <td className="break-all px-3 py-1.5">{entry.by ?? "—"}</td>
                  <td className="break-words px-3 py-1.5">{entry.reason ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </details>
  );
}
