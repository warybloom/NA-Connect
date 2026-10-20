import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  CircleAlert,
  Gauge,
  History,
  Info,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Split,
  UserCheck,
  Wrench,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { cn } from "@/lib/utils";
import {
  confirmedRedistributionCodes,
  getInformationQualityAudit,
  getRedistributions,
  isConfirmedDecision,
  isRedistributionPending,
  redistributionActionFor,
  redistributionKey,
  REDISTRIBUTABLE_RULES,
  REDISTRIBUTION_CONFIRMED_RULE,
  type InformationQualityAuditResult,
  type InformationQualityFinding,
  type InformationQualitySeverity,
  type RedistributionDecision,
} from "./informationQualityApi";
import RedistributionDialog, { DestinationsTable, formatCurrency, formatHours } from "./RedistributionDialog";
import RedistributionBatchDialog from "./RedistributionBatchDialog";
import {
  findingDocumentStatus,
  formatPercent,
  getProgressDecisions,
  isConfirmedProgress,
  isProgressSimulationPending,
  progressExcludedCodes,
  progressSettingsLink,
  PROGRESS_MISSING_RULE,
  PROGRESS_UNCONFIGURED_RULE,
  type ProgressDecision,
} from "./physicalProgressApi";
import PhysicalProgressDialog from "./PhysicalProgressDialog";
import PhysicalProgressBatchDialog from "./PhysicalProgressBatchDialog";

const number = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value ?? 0);

const signed = (value: number) => `${value > 0 ? "+" : ""}${number(value)}`;

const severityLabel: Record<InformationQualitySeverity, string> = {
  CRITICO: "Crítico",
  ATENCAO: "Atenção",
  INFORMATIVO: "Informativo",
};

const severityClass: Record<InformationQualitySeverity, string> = {
  CRITICO: "border-rose-500/35 bg-rose-500/10 text-rose-600",
  ATENCAO: "border-amber-500/35 bg-amber-500/10 text-amber-600",
  INFORMATIVO: "border-sky-500/35 bg-sky-500/10 text-sky-600",
};

type DecisionFilter = "TODOS" | "DECISAO" | "TECNICO";

export default function InformationQualityAuditPage() {
  const { projetoAtual } = useProjetoAtual();
  const [result, setResult] = useState<InformationQualityAuditResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [severity, setSeverity] = useState<InformationQualitySeverity | "TODOS">("TODOS");
  const [category, setCategory] = useState("TODOS");
  const [decisionFilter, setDecisionFilter] = useState<DecisionFilter>("TODOS");
  const [search, setSearch] = useState("");
  const [redistributions, setRedistributions] = useState<RedistributionDecision[]>([]);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [simulating, setSimulating] = useState<InformationQualityFinding | null>(null);
  const [selectedRedistributions, setSelectedRedistributions] = useState<Set<string>>(new Set());
  const [batchOpen, setBatchOpen] = useState(false);
  const [selectedProgress, setSelectedProgress] = useState<Set<string>>(new Set());
  const [progressBatchOpen, setProgressBatchOpen] = useState(false);
  const [progressDecisions, setProgressDecisions] = useState<ProgressDecision[]>([]);
  const [progressHistoryError, setProgressHistoryError] = useState<string | null>(null);
  const [simulatingProgress, setSimulatingProgress] = useState<InformationQualityFinding | null>(null);
  const loadSeq = useRef(0);

  // Sempre recarrega auditoria E históricos (redistribuições + avanços validados) juntos; respostas de cargas anteriores são descartadas.
  const load = async (signal?: AbortSignal) => {
    if (!projetoAtual?.projectId) return;
    const projectId = projetoAtual.projectId;
    const seq = ++loadSeq.current;
    const stale = () => !!signal?.aborted || seq !== loadSeq.current;
    setLoading(true);
    setError(null);
    // Se o histórico falhar, a auditoria continua visível, mas nenhuma confirmação é permitida às cegas.
    const history = getRedistributions(projectId, signal).then(
      (items) => {
        if (stale()) return;
        setRedistributions(items);
        setHistoryError(null);
      },
      (e) => {
        if ((e instanceof DOMException && e.name === "AbortError") || stale()) return;
        setRedistributions([]);
        setHistoryError(e instanceof Error ? e.message : "Falha ao carregar o histórico de redistribuições.");
      },
    );
    // Mesma regra para avanços validados: sem o histórico, nenhuma validação de avanço físico às cegas.
    const progressHistory = getProgressDecisions(projectId, signal).then(
      (items) => {
        if (stale()) return;
        setProgressDecisions(items);
        setProgressHistoryError(null);
      },
      (e) => {
        if ((e instanceof DOMException && e.name === "AbortError") || stale()) return;
        setProgressDecisions([]);
        setProgressHistoryError(e instanceof Error ? e.message : "Falha ao carregar os avanços físicos validados.");
      },
    );
    try {
      const audit = await getInformationQualityAudit(projectId, signal);
      // Renderiza só com os históricos em mãos, para "Simular" não piscar em ocorrências já tratadas.
      await Promise.all([history, progressHistory]);
      if (!stale()) setResult(audit);
    } catch (e) {
      if ((e instanceof DOMException && e.name === "AbortError") || stale()) return;
      setError(e instanceof Error ? e.message : "Falha ao executar a auditoria.");
    } finally {
      await Promise.all([history, progressHistory]);
      if (!stale()) setLoading(false);
    }
  };

  useEffect(() => {
    setSimulating(null);
    setBatchOpen(false);
    setSelectedRedistributions(new Set());
    setProgressBatchOpen(false);
    setSelectedProgress(new Set());
    setSimulatingProgress(null);
    setRedistributions([]);
    setHistoryError(null);
    setProgressDecisions([]);
    setProgressHistoryError(null);
    if (!projetoAtual?.projectId) {
      setResult(null);
      return;
    }
    const controller = new AbortController();
    setResult(null);
    void load(controller.signal);
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projetoAtual?.projectId]);

  const categories = useMemo(
    () => [...new Set((result?.findings ?? []).map((x) => x.category))].sort((a, b) => a.localeCompare(b, "pt-BR")),
    [result],
  );

  // Só status CONFIRMADO conta como tratado.
  const confirmedDecisions = useMemo(
    () => redistributions
      .filter(isConfirmedDecision)
      .sort((a, b) => (b.confirmedAtUtc ?? "").localeCompare(a.confirmedAtUtc ?? "")),
    [redistributions],
  );
  const unconfirmedDecisionCount = redistributions.length - confirmedDecisions.length;

  const redistributedByCode = useMemo(
    () => new Map(confirmedDecisions.map((item) => [redistributionKey(item.sourceDocumentCode), item])),
    [confirmedDecisions],
  );

  const confirmedCodes = useMemo(
    () => confirmedRedistributionCodes(result?.findings ?? [], confirmedDecisions),
    [result, confirmedDecisions],
  );

  const pendingRedistributionCount = useMemo(
    () => (result?.findings ?? []).filter((item) => isRedistributionPending(item, confirmedCodes)).length,
    [result, confirmedCodes],
  );

  const confirmBlockedReason = historyError
    ? "O histórico de redistribuições não pôde ser carregado; sem ele não é possível garantir que este número de documento ainda não foi redistribuído. Atualize a auditoria e tente novamente."
    : loading
      ? "Auditoria e histórico estão sendo recarregados. Aguarde para confirmar."
      : null;

  const handleConfirmed = () => load();

  // Avanço físico: só CONFIRMADO conta como validado; cancelados/retirados/redistribuídos nunca recebem a ação.
  const confirmedProgress = useMemo(
    () => progressDecisions
      .filter(isConfirmedProgress)
      .sort((a, b) => (b.confirmedAtUtc ?? "").localeCompare(a.confirmedAtUtc ?? "")),
    [progressDecisions],
  );
  const unconfirmedProgressCount = progressDecisions.length - confirmedProgress.length;

  const validatedProgressByCode = useMemo(
    () => new Map(confirmedProgress.map((item) => [redistributionKey(item.documentCode), item])),
    [confirmedProgress],
  );

  const progressExcluded = useMemo(
    () => progressExcludedCodes(result?.findings ?? [], confirmedCodes),
    [result, confirmedCodes],
  );

  const pendingProgressCount = useMemo(
    () => (result?.findings ?? []).filter((item) => isProgressSimulationPending(item, validatedProgressByCode, progressExcluded)).length,
    [result, validatedProgressByCode, progressExcluded],
  );

  const progressConfirmBlockedReason = progressHistoryError
    ? "Os avanços físicos já validados não puderam ser carregados; sem eles não é possível garantir que este documento ainda não foi validado. Atualize a auditoria e tente novamente."
    : historyError
      ? "O histórico de redistribuições não pôde ser carregado; sem ele não é possível garantir que este documento não foi redistribuído. Atualize a auditoria e tente novamente."
      : loading
        ? "Auditoria e históricos estão sendo recarregados. Aguarde para confirmar."
        : null;

  const decisionCount = useMemo(
    () => (result?.findings ?? []).filter((item) => item.decisionRequired).length,
    [result],
  );

  const technicalCount = useMemo(
    () => (result?.findings ?? []).filter((item) => !item.decisionRequired && item.rule !== "SEM_INCONSISTENCIAS").length,
    [result],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLocaleUpperCase("pt-BR");
    return (result?.findings ?? []).filter((item) => {
      if (severity !== "TODOS" && item.severity !== severity) return false;
      if (category !== "TODOS" && item.category !== category) return false;
      if (decisionFilter === "DECISAO" && !item.decisionRequired) return false;
      if (decisionFilter === "TECNICO" && item.decisionRequired) return false;
      if (!q) return true;
      return [
        item.title,
        item.description,
        item.documentCode,
        item.discipline,
        item.rule,
        item.category,
        ...item.details.flatMap((x) => [x.label, x.value]),
      ].some((value) => (value ?? "").toLocaleUpperCase("pt-BR").includes(q));
    });
  }, [result, severity, category, decisionFilter, search]);

  const visibleRedistributable = useMemo(
    () => filtered.filter((item) => isRedistributionPending(item, confirmedCodes)),
    [filtered, confirmedCodes],
  );

  const selectedRedistributionFindings = useMemo(
    () => (result?.findings ?? []).filter((item) => {
      const key = redistributionKey(item.documentCode);
      return !!key && selectedRedistributions.has(key) && isRedistributionPending(item, confirmedCodes);
    }),
    [result, selectedRedistributions, confirmedCodes],
  );

  const visibleProgress = useMemo(
    () => filtered.filter((item) => isProgressSimulationPending(item, validatedProgressByCode, progressExcluded)),
    [filtered, validatedProgressByCode, progressExcluded],
  );

  const selectedProgressFindings = useMemo(
    () => (result?.findings ?? []).filter((item) => {
      const key = redistributionKey(item.documentCode);
      return !!key
        && selectedProgress.has(key)
        && isProgressSimulationPending(item, validatedProgressByCode, progressExcluded);
    }),
    [result, selectedProgress, validatedProgressByCode, progressExcluded],
  );

  const toggleRedistributionSelection = (finding: InformationQualityFinding, checked: boolean) => {
    const key = redistributionKey(finding.documentCode);
    if (!key) return;
    setSelectedRedistributions((current) => {
      const next = new Set(current);
      if (checked) next.add(key); else next.delete(key);
      return next;
    });
  };

  const toggleVisibleRedistributions = (checked: boolean) => {
    setSelectedRedistributions((current) => {
      const next = new Set(current);
      for (const finding of visibleRedistributable) {
        const key = redistributionKey(finding.documentCode);
        if (!key) continue;
        if (checked) next.add(key); else next.delete(key);
      }
      return next;
    });
  };

  const toggleProgressSelection = (finding: InformationQualityFinding, checked: boolean) => {
    const key = redistributionKey(finding.documentCode);
    if (!key) return;
    setSelectedProgress((current) => {
      const next = new Set(current);
      if (checked) next.add(key); else next.delete(key);
      return next;
    });
  };

  const toggleVisibleProgress = (checked: boolean) => {
    setSelectedProgress((current) => {
      const next = new Set(current);
      for (const finding of visibleProgress) {
        const key = redistributionKey(finding.documentCode);
        if (!key) continue;
        if (checked) next.add(key); else next.delete(key);
      }
      return next;
    });
  };

  if (!projetoAtual) {
    return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;
  }

  return (
    <div className="space-y-5 animate-enter">
      <section className="overflow-hidden rounded-xl border border-border bg-card/60">
        <div className="flex flex-wrap items-center gap-4 p-5">
          <div className="flex h-11 w-11 items-center justify-center rounded-xl border border-primary/20 bg-primary/10">
            <ShieldCheck className="h-6 w-6 text-primary" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-[10px] font-black uppercase tracking-[0.22em] text-muted-foreground">Qualidade</p>
            <h1 className="text-xl font-black">Auditoria</h1>
            <p className="max-w-4xl text-sm text-muted-foreground">
              Auditoria visual da qualidade da informação entre LDP, Timesheet e cronograma.
              O N&A Connect identifica a inconsistência e apresenta evidências; a decisão documental continua sendo do responsável.
            </p>
          </div>
          <Button className="gap-2" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={cn("h-4 w-4", loading && "animate-spin")} />
            {loading ? "Auditando..." : "Atualizar auditoria"}
          </Button>
        </div>

        <div className="grid border-t border-border bg-muted/20 md:grid-cols-2">
          <div className="flex items-start gap-3 border-b border-border px-4 py-3 md:border-b-0 md:border-r">
            <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
            <div>
              <p className="text-xs font-bold">Sem correção automática</p>
              <p className="text-xs leading-relaxed text-muted-foreground">
                Esta tela não altera número do documento, título, data, status, LDP, Timesheet, cronograma ou De/Para.
                A redistribuição de HH e o avanço físico gerencial só são registrados depois de simulados e confirmados por você.
              </p>
            </div>
          </div>
          <div className="flex items-start gap-3 px-4 py-3">
            <UserCheck className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
            <div>
              <p className="text-xs font-bold">Decisão humana</p>
              <p className="text-xs leading-relaxed text-muted-foreground">
                Quando houver ambiguidade documental, o sistema mostra o contexto e aguarda sua decisão.
              </p>
            </div>
          </div>
        </div>
      </section>

      {error && <ReportState title={error} error>A fonte permaneceu inalterada. Tente novamente ou confira a disponibilidade do banco.</ReportState>}

      {!result && !error && loading && (
        <ReportState title="Executando auditoria">Comparando LDP, Timesheet e cronograma do projeto atual.</ReportState>
      )}

      {result && (
        <>
          <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
            <MetricCard title="Críticos" value={result.summary.critical} icon={<CircleAlert className="h-4 w-4" />} tone="critical" />
            <MetricCard title="Atenções" value={result.summary.warning} icon={<AlertTriangle className="h-4 w-4" />} tone="warning" />
            <MetricCard title="Informativos" value={result.summary.info} icon={<Info className="h-4 w-4" />} />
            <MetricCard title="Sua decisão" value={decisionCount} icon={<UserCheck className="h-4 w-4" />} tone={decisionCount > 0 ? "warning" : undefined} />
            <MetricCard title="HH LDP" value={number(result.summary.ldpActualHours)} suffix="HH" />
            <MetricCard title="HH Timesheet" value={number(result.summary.timesheetActualHours)} suffix="HH" />
          </section>

          {(decisionCount > 0 || technicalCount > 0) && (
            <section className="grid gap-3 lg:grid-cols-2">
              <div className={cn(
                "rounded-xl border p-4",
                decisionCount > 0 ? "border-amber-500/30 bg-amber-500/5" : "border-border bg-card/60",
              )}>
                <div className="flex items-start gap-3">
                  <UserCheck className="mt-0.5 h-5 w-5 text-amber-600" />
                  <div>
                    <p className="text-sm font-bold">Decisões do responsável</p>
                    <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
                      <b className="text-foreground">{decisionCount}</b> ocorrência(s) precisam de análise humana.
                      Nenhuma delas será corrigida automaticamente.
                    </p>
                  </div>
                </div>
              </div>
              <div className="rounded-xl border border-border bg-card/60 p-4">
                <div className="flex items-start gap-3">
                  <Wrench className="mt-0.5 h-5 w-5 text-sky-600" />
                  <div>
                    <p className="text-sm font-bold">Alertas técnicos</p>
                    <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
                      <b className="text-foreground">{technicalCount}</b> ocorrência(s) são de integridade, sincronização ou cobertura.
                      A auditoria informa o problema sem alterar a fonte.
                    </p>
                  </div>
                </div>
              </div>
            </section>
          )}

          <section className="rounded-xl border border-border bg-card/60 p-4">
            <div className="grid gap-3 text-xs sm:grid-cols-2 lg:grid-cols-4">
              <SummaryItem label="Linhas LDP" value={result.summary.ldpRows.toLocaleString("pt-BR")} />
              <SummaryItem label="Documentos LDP" value={result.summary.ldpDocuments.toLocaleString("pt-BR")} />
              <SummaryItem label="Documentos Timesheet" value={result.summary.timesheetDocuments.toLocaleString("pt-BR")} />
              <SummaryItem
                label="Diferença HH · LDP − Timesheet"
                value={`${signed(result.summary.differenceHours)} HH`}
                emphasize={Math.abs(result.summary.differenceHours) > 0.005}
              />
            </div>
          </section>

          <section className="space-y-3 rounded-xl border border-border bg-card/60 p-4">
            <div className="flex flex-wrap items-center gap-3">
              <div className="relative min-w-[260px] flex-1">
                <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Buscar número do documento, regra, disciplina, fonte ou descrição..."
                  className="h-9 w-full rounded-md border border-input bg-background pl-9 pr-3 text-sm"
                />
              </div>

              <select
                className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                value={severity}
                onChange={(e) => setSeverity(e.target.value as InformationQualitySeverity | "TODOS")}
              >
                <option value="TODOS">Todas as severidades</option>
                <option value="CRITICO">Críticos</option>
                <option value="ATENCAO">Atenções</option>
                <option value="INFORMATIVO">Informativos</option>
              </select>

              <select
                className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                value={category}
                onChange={(e) => setCategory(e.target.value)}
              >
                <option value="TODOS">Todas as categorias</option>
                {categories.map((item) => <option key={item} value={item}>{item}</option>)}
              </select>

              <select
                className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                value={decisionFilter}
                onChange={(e) => setDecisionFilter(e.target.value as DecisionFilter)}
              >
                <option value="TODOS">Todas as decisões</option>
                <option value="DECISAO">Decisão necessária</option>
                <option value="TECNICO">Sem decisão documental</option>
              </select>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
              <span>
                {filtered.length.toLocaleString("pt-BR")} de {result.findings.length.toLocaleString("pt-BR")} ocorrência(s)
                {pendingRedistributionCount > 0 && <> · {pendingRedistributionCount.toLocaleString("pt-BR")} com redistribuição de HH pendente</>}
                {pendingProgressCount > 0 && <> · {pendingProgressCount.toLocaleString("pt-BR")} com avanço físico a validar</>}
              </span>
              <span>Atualizado em {new Date(result.generatedAtUtc).toLocaleString("pt-BR")}</span>
            </div>

            {(visibleRedistributable.length > 0 || visibleProgress.length > 0) && (
              <div className="flex flex-wrap items-center gap-3 rounded-lg border border-primary/20 bg-primary/5 px-3 py-2">
                {visibleRedistributable.length > 0 && (
                  <label className="flex cursor-pointer items-center gap-2 text-xs font-semibold">
                    <input
                      type="checkbox"
                      checked={visibleRedistributable.every((item) => selectedRedistributions.has(redistributionKey(item.documentCode)))}
                      onChange={(e) => toggleVisibleRedistributions(e.target.checked)}
                    />
                    Redistribuições visíveis ({visibleRedistributable.length})
                  </label>
                )}
                {visibleProgress.length > 0 && (
                  <label className="flex cursor-pointer items-center gap-2 text-xs font-semibold">
                    <input
                      type="checkbox"
                      checked={visibleProgress.every((item) => selectedProgress.has(redistributionKey(item.documentCode)))}
                      onChange={(e) => toggleVisibleProgress(e.target.checked)}
                    />
                    Avanços visíveis ({visibleProgress.length})
                  </label>
                )}

                <div className="ml-auto flex flex-wrap items-center gap-2">
                  {visibleRedistributable.length > 0 && (
                    <Button
                      size="sm"
                      className="gap-2"
                      disabled={selectedRedistributionFindings.length === 0 || !!historyError || loading}
                      onClick={() => setBatchOpen(true)}
                    >
                      <Split className="h-3.5 w-3.5" />
                      Simular redistribuições ({selectedRedistributionFindings.length})
                    </Button>
                  )}
                  {visibleProgress.length > 0 && (
                    <Button
                      size="sm"
                      variant="outline"
                      className="gap-2"
                      disabled={selectedProgressFindings.length === 0 || !!progressHistoryError || !!historyError || loading}
                      onClick={() => setProgressBatchOpen(true)}
                    >
                      <Gauge className="h-3.5 w-3.5" />
                      Simular avanços ({selectedProgressFindings.length})
                    </Button>
                  )}
                </div>
              </div>
            )}

            {historyError && (
              <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                Não foi possível consultar redistribuições já confirmadas ({historyError}). A simulação fica apenas para leitura
                e a confirmação está bloqueada até o histórico ser recarregado.
              </p>
            )}
            {progressHistoryError && (
              <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                Não foi possível consultar avanços físicos já validados ({progressHistoryError}). A simulação fica apenas para leitura
                e a validação está bloqueada até o histórico ser recarregado.
              </p>
            )}

            <AuditTable
              rows={filtered}
              confirmedCodes={confirmedCodes}
              selectedRedistributions={selectedRedistributions}
              onToggleRedistribution={toggleRedistributionSelection}
              selectedProgress={selectedProgress}
              onToggleProgress={toggleProgressSelection}
              redistributedByCode={redistributedByCode}
              historyUnavailable={!!historyError}
              onSimulate={setSimulating}
              validatedProgressByCode={validatedProgressByCode}
              progressExcluded={progressExcluded}
              progressHistoryUnavailable={!!progressHistoryError || !!historyError}
              onSimulateProgress={setSimulatingProgress}
            />
          </section>

          <ValidatedProgressSection
            decisions={confirmedProgress}
            unconfirmedCount={unconfirmedProgressCount}
            historyError={progressHistoryError}
            loading={loading}
            onRetry={() => void load()}
          />

          <ConfirmedRedistributionsSection
            decisions={confirmedDecisions}
            unconfirmedCount={unconfirmedDecisionCount}
            historyError={historyError}
            loading={loading}
            onRetry={() => void load()}
          />
        </>
      )}

      {projetoAtual.projectId && (
        <RedistributionDialog
          projectId={projetoAtual.projectId}
          finding={simulating}
          confirmedDecision={simulating ? redistributedByCode.get(redistributionKey(simulating.documentCode)) : undefined}
          confirmBlockedReason={confirmBlockedReason}
          onClose={() => setSimulating(null)}
          onConfirmed={handleConfirmed}
        />
      )}

      {projetoAtual.projectId && batchOpen && (
        <RedistributionBatchDialog
          projectId={projetoAtual.projectId}
          findings={selectedRedistributionFindings}
          confirmBlockedReason={confirmBlockedReason}
          onClose={() => setBatchOpen(false)}
          onConfirmed={handleConfirmed}
        />
      )}

      {projetoAtual.projectId && progressBatchOpen && (
        <PhysicalProgressBatchDialog
          projectId={projetoAtual.projectId}
          findings={selectedProgressFindings}
          confirmBlockedReason={progressConfirmBlockedReason}
          onClose={() => setProgressBatchOpen(false)}
          onConfirmed={handleConfirmed}
        />
      )}

      {projetoAtual.projectId && (
        <PhysicalProgressDialog
          projectId={projetoAtual.projectId}
          finding={simulatingProgress}
          confirmedDecision={simulatingProgress ? validatedProgressByCode.get(redistributionKey(simulatingProgress.documentCode)) : undefined}
          confirmBlockedReason={progressConfirmBlockedReason}
          onClose={() => setSimulatingProgress(null)}
          onConfirmed={handleConfirmed}
        />
      )}
    </div>
  );
}

function MetricCard({
  title,
  value,
  suffix,
  icon,
  tone,
}: {
  title: string;
  value: string | number;
  suffix?: string;
  icon?: React.ReactNode;
  tone?: "critical" | "warning";
}) {
  return (
    <div className={cn(
      "rounded-xl border bg-card p-4",
      tone === "critical" && "border-rose-500/35",
      tone === "warning" && "border-amber-500/35",
    )}>
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">{title}</p>
        <span className="text-muted-foreground">{icon}</span>
      </div>
      <div className="mt-1 flex items-baseline gap-1">
        <b className="text-2xl tabular-nums">{value}</b>
        {suffix && <span className="text-xs font-semibold text-muted-foreground">{suffix}</span>}
      </div>
    </div>
  );
}

function SummaryItem({ label, value, emphasize = false }: { label: string; value: string; emphasize?: boolean }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 px-3 py-2">
      <p className="text-muted-foreground">{label}</p>
      <b className={cn("mt-0.5 block text-sm tabular-nums", emphasize && "text-amber-600")}>{value}</b>
    </div>
  );
}

function ConfirmedRedistributionsSection({
  decisions,
  unconfirmedCount,
  historyError,
  loading,
  onRetry,
}: {
  decisions: RedistributionDecision[];
  unconfirmedCount: number;
  historyError: string | null;
  loading: boolean;
  onRetry: () => void;
}) {
  const [open, setOpen] = useState(true);
  const totalHours = decisions.reduce((sum, item) => sum + item.sourceHours, 0);
  const totalCost = decisions.reduce((sum, item) => sum + item.sourceCost, 0);

  return (
    <section className="rounded-xl border border-border bg-card/60">
      <button
        type="button"
        className="flex w-full flex-wrap items-center gap-3 px-4 py-3 text-left"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <History className="h-5 w-5 text-emerald-600" />
        <div className="min-w-0 flex-1">
          <p className="text-sm font-bold">Tratamentos de HH confirmados</p>
          <p className="text-xs text-muted-foreground">
            {historyError
              ? "Histórico indisponível"
              : <>{decisions.length.toLocaleString("pt-BR")} decisão(ões) · {formatHours(totalHours)} HH · {formatCurrency(totalCost)}</>}
          </p>
        </div>
        <ChevronDown className={cn("h-4 w-4 text-muted-foreground transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <div className="space-y-2 border-t border-border p-4">
          {historyError ? (
            <div className="flex flex-wrap items-center gap-3 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
              <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
              <span className="flex-1">Não foi possível carregar o histórico ({historyError}).</span>
              <Button size="sm" variant="outline" onClick={onRetry} disabled={loading}>Tentar novamente</Button>
            </div>
          ) : decisions.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhuma redistribuição confirmada para este projeto.</p>
          ) : (
            decisions.map((decision) => (
              <details key={decision.id} className="rounded-lg border border-border/70 bg-background/50">
                <summary className="grid cursor-pointer gap-x-4 gap-y-1 px-3 py-2 text-xs sm:grid-cols-[minmax(180px,1.4fr)_1fr_1fr_0.7fr_0.8fr_0.6fr]">
                  <span>
                    <b className="break-all font-mono">{decision.sourceDocumentCode}</b>
                    {decision.discipline && <span className="block text-muted-foreground">{decision.discipline}</span>}
                  </span>
                  <span>
                    <span className="block text-muted-foreground">Data</span>
                    {decision.confirmedAtUtc ? new Date(decision.confirmedAtUtc).toLocaleString("pt-BR") : "—"}
                  </span>
                  <span>
                    <span className="block text-muted-foreground">Usuário</span>
                    <span className="break-all">{decision.confirmedBy ?? "—"}</span>
                  </span>
                  <span className="tabular-nums">
                    <span className="block text-muted-foreground">HH</span>
                    {formatHours(decision.sourceHours)}
                  </span>
                  <span className="tabular-nums">
                    <span className="block text-muted-foreground">Custo</span>
                    {formatCurrency(decision.sourceCost)}
                  </span>
                  <span>
                    <span className="block text-muted-foreground">{decision.items.length === 0 ? "Tratamento" : "Destinos"}</span>
                    {decision.items.length === 0 ? "AT" : decision.items.length.toLocaleString("pt-BR")}
                  </span>
                </summary>
                <div className="border-t border-border/70 p-3">
                  {decision.items.length === 0 ? (
                    <p className="text-xs text-muted-foreground">
                      Atividade AT sem documento de destino. HH e custo permanecem no projeto e compõem a disciplina <b className="text-foreground">{decision.discipline ?? "—"}</b>.
                    </p>
                  ) : (
                    <DestinationsTable rows={decision.items} sourceHours={decision.sourceHours} sourceCost={decision.sourceCost} />
                  )}
                </div>
              </details>
            ))
          )}
          {!historyError && unconfirmedCount > 0 && (
            <p className="text-[11px] text-muted-foreground">
              {unconfirmedCount.toLocaleString("pt-BR")} registro(s) do histórico com status diferente de CONFIRMADO não contam como tratados.
            </p>
          )}
        </div>
      )}
    </section>
  );
}

function ValidatedProgressSection({
  decisions,
  unconfirmedCount,
  historyError,
  loading,
  onRetry,
}: {
  decisions: ProgressDecision[];
  unconfirmedCount: number;
  historyError: string | null;
  loading: boolean;
  onRetry: () => void;
}) {
  const [open, setOpen] = useState(true);

  return (
    <section className="rounded-xl border border-border bg-card/60">
      <button
        type="button"
        className="flex w-full flex-wrap items-center gap-3 px-4 py-3 text-left"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <Gauge className="h-5 w-5 text-emerald-600" />
        <div className="min-w-0 flex-1">
          <p className="text-sm font-bold">Avanços físicos validados</p>
          <p className="text-xs text-muted-foreground">
            {historyError
              ? "Histórico indisponível"
              : <>{decisions.length.toLocaleString("pt-BR")} documento(s) · camada gerencial, a LDP não é alterada</>}
          </p>
        </div>
        <ChevronDown className={cn("h-4 w-4 text-muted-foreground transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <div className="space-y-2 border-t border-border p-4">
          {historyError ? (
            <div className="flex flex-wrap items-center gap-3 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
              <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
              <span className="flex-1">Não foi possível carregar os avanços validados ({historyError}).</span>
              <Button size="sm" variant="outline" onClick={onRetry} disabled={loading}>Tentar novamente</Button>
            </div>
          ) : decisions.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhum avanço físico validado para este projeto.</p>
          ) : (
            <div className="overflow-auto rounded-lg border border-border">
              <table className="w-full min-w-[760px] text-sm">
                <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Documento</th>
                    <th className="px-3 py-2">Document Status</th>
                    <th className="px-3 py-2 text-right">Original (LDP)</th>
                    <th className="px-3 py-2 text-right">Validado</th>
                    <th className="px-3 py-2">Usuário</th>
                    <th className="px-3 py-2">Data</th>
                  </tr>
                </thead>
                <tbody>
                  {decisions.map((decision) => (
                    <tr key={decision.id} className="border-t align-top">
                      <td className="max-w-[300px] px-3 py-2"><span className="break-all font-mono text-xs">{decision.documentCode}</span></td>
                      <td className="px-3 py-2 text-xs">{decision.documentStatus ?? "—"}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{formatPercent(decision.originalPercent)}</td>
                      <td className="px-3 py-2 text-right font-semibold tabular-nums">{formatPercent(decision.validatedPercent)}</td>
                      <td className="px-3 py-2 text-xs"><span className="break-all">{decision.confirmedBy ?? "—"}</span></td>
                      <td className="px-3 py-2 text-xs">{decision.confirmedAtUtc ? new Date(decision.confirmedAtUtc).toLocaleString("pt-BR") : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {!historyError && unconfirmedCount > 0 && (
            <p className="text-[11px] text-muted-foreground">
              {unconfirmedCount.toLocaleString("pt-BR")} registro(s) com status diferente de CONFIRMADO não contam como validados.
            </p>
          )}
        </div>
      )}
    </section>
  );
}

function AuditTable({
  rows,
  confirmedCodes,
  selectedRedistributions,
  onToggleRedistribution,
  selectedProgress,
  onToggleProgress,
  redistributedByCode,
  historyUnavailable,
  onSimulate,
  validatedProgressByCode,
  progressExcluded,
  progressHistoryUnavailable,
  onSimulateProgress,
}: {
  rows: InformationQualityFinding[];
  confirmedCodes: ReadonlySet<string>;
  selectedRedistributions: ReadonlySet<string>;
  onToggleRedistribution: (finding: InformationQualityFinding, checked: boolean) => void;
  selectedProgress: ReadonlySet<string>;
  onToggleProgress: (finding: InformationQualityFinding, checked: boolean) => void;
  redistributedByCode: ReadonlyMap<string, RedistributionDecision>;
  historyUnavailable: boolean;
  onSimulate: (finding: InformationQualityFinding) => void;
  validatedProgressByCode: ReadonlyMap<string, ProgressDecision>;
  progressExcluded: ReadonlySet<string>;
  progressHistoryUnavailable: boolean;
  onSimulateProgress: (finding: InformationQualityFinding) => void;
}) {
  if (!rows.length) {
    return (
      <div className="flex min-h-36 flex-col items-center justify-center gap-2 rounded-lg border border-dashed">
        <CheckCircle2 className="h-7 w-7 text-emerald-600" />
        <p className="text-sm font-semibold">Nenhuma ocorrência para os filtros atuais.</p>
      </div>
    );
  }

  return (
    <div className="overflow-auto rounded-lg border border-border">
      <table className="w-full min-w-[1300px] text-sm">
        <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
          <tr>
            <th className="w-10 px-3 py-2 text-center">Sel.</th>
            <th className="px-3 py-2">Severidade</th>
            <th className="px-3 py-2">Fonte / regra</th>
            <th className="px-3 py-2">Ocorrência</th>
            <th className="px-3 py-2">Documento</th>
            <th className="px-3 py-2">HH LDP</th>
            <th className="px-3 py-2">HH Timesheet</th>
            <th className="px-3 py-2">Responsável</th>
            <th className="px-3 py-2">Contexto</th>
            <th className="px-3 py-2">Ação</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((item) => {
            const redistributionSelectable = isRedistributionPending(item, confirmedCodes);
            const progressSelectable = isProgressSimulationPending(item, validatedProgressByCode, progressExcluded);
            const key = redistributionKey(item.documentCode);
            const selectable = redistributionSelectable || progressSelectable;
            const checked = redistributionSelectable
              ? selectedRedistributions.has(key)
              : progressSelectable
                ? selectedProgress.has(key)
                : false;
            return (
            <tr key={item.id} className="border-t align-top hover:bg-muted/20">
              <td className="px-3 py-3 text-center">
                {selectable ? (
                  <input
                    type="checkbox"
                    aria-label={`Selecionar ${item.documentCode ?? "item"}`}
                    checked={checked}
                    onChange={(e) => {
                      if (redistributionSelectable) onToggleRedistribution(item, e.target.checked);
                      else if (progressSelectable) onToggleProgress(item, e.target.checked);
                    }}
                  />
                ) : <span className="text-muted-foreground">—</span>}
              </td>
              <td className="px-3 py-3">
                <span className={cn("inline-flex rounded-full border px-2 py-0.5 text-[11px] font-bold", severityClass[item.severity])}>
                  {severityLabel[item.severity]}
                </span>
              </td>
              <td className="px-3 py-3">
                <span className="inline-flex rounded-md bg-muted px-2 py-1 text-[11px] font-bold">{item.category}</span>
                <p className="mt-1 max-w-[220px] break-all font-mono text-[10px] text-muted-foreground">{item.rule}</p>
              </td>
              <td className="max-w-[380px] px-3 py-3">
                <p className="font-semibold">{item.title}</p>
                <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{item.description}</p>
              </td>
              <td className="max-w-[280px] px-3 py-3">
                <p className="break-all font-mono text-xs">{item.documentCode ?? "—"}</p>
                {item.discipline && <p className="mt-1 text-xs text-muted-foreground">{item.discipline}</p>}
              </td>
              <td className="px-3 py-3 tabular-nums">{item.ldpHours == null ? "—" : number(item.ldpHours)}</td>
              <td className="px-3 py-3 tabular-nums">{item.timesheetHours == null ? "—" : number(item.timesheetHours)}</td>
              <td className="px-3 py-3">
                {item.decisionRequired ? (
                  <div className="inline-flex items-center gap-1.5 rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-xs font-semibold text-amber-700">
                    <UserCheck className="h-3.5 w-3.5" />
                    Sua decisão
                  </div>
                ) : (
                  <div className="inline-flex items-center gap-1.5 rounded-md border border-sky-500/20 bg-sky-500/5 px-2 py-1 text-xs text-sky-700">
                    <Wrench className="h-3.5 w-3.5" />
                    Alerta técnico
                  </div>
                )}
              </td>
              <td className="px-3 py-3">
                {item.details.length > 0 ? (
                  <details>
                    <summary className="cursor-pointer text-xs font-semibold text-primary">Ver evidências</summary>
                    <div className="mt-2 min-w-[300px] space-y-1.5 rounded-md border border-border/60 bg-muted/30 p-2.5 text-xs">
                      {item.details.map((detail, index) => (
                        <div key={`${item.id}:${index}`} className="grid grid-cols-[110px_1fr] gap-2">
                          <b className="text-foreground">{detail.label}</b>
                          <span className="break-words text-muted-foreground">{detail.value}</span>
                        </div>
                      ))}
                    </div>
                  </details>
                ) : <span className="text-xs text-muted-foreground">Sem contexto adicional</span>}
              </td>
              <td className="px-3 py-3">
                {item.rule === PROGRESS_MISSING_RULE || item.rule === PROGRESS_UNCONFIGURED_RULE ? (
                  <ProgressAction
                    finding={item}
                    validatedByCode={validatedProgressByCode}
                    excludedCodes={progressExcluded}
                    historyUnavailable={progressHistoryUnavailable}
                    onSimulate={onSimulateProgress}
                  />
                ) : (
                  <RedistributionAction
                    finding={item}
                    confirmedCodes={confirmedCodes}
                    redistributedByCode={redistributedByCode}
                    historyUnavailable={historyUnavailable}
                    onSimulate={onSimulate}
                  />
                )}
              </td>
            </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function RedistributionAction({
  finding,
  confirmedCodes,
  redistributedByCode,
  historyUnavailable,
  onSimulate,
}: {
  finding: InformationQualityFinding;
  confirmedCodes: ReadonlySet<string>;
  redistributedByCode: ReadonlyMap<string, RedistributionDecision>;
  historyUnavailable: boolean;
  onSimulate: (finding: InformationQualityFinding) => void;
}) {
  const kind = redistributionActionFor(finding, confirmedCodes);
  if (kind === "SIMULAR") {
    return (
      <Button
        size="sm"
        variant="outline"
        className="gap-1.5"
        onClick={() => onSimulate(finding)}
        title={historyUnavailable ? "Histórico indisponível: simulação só para leitura, confirmação bloqueada." : undefined}
      >
        <Split className="h-3.5 w-3.5" />
        {historyUnavailable ? "Simular (só leitura)" : "Simular"}
      </Button>
    );
  }

  if (kind === "SEM_CODIGO") {
    return <span className="text-xs text-amber-700" title="A ocorrência não informa o número do documento; trate na origem (Timesheet/LDP).">Sem número do documento</span>;
  }
  if (kind === "NENHUMA") return <span className="text-xs text-muted-foreground">—</span>;
  const key = redistributionKey(finding.documentCode);

  const decision = redistributedByCode.get(key);
  return (
    <div
      className="inline-flex items-center gap-1.5 rounded-md border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-xs font-semibold text-emerald-700"
      title={decision
        ? decision.items.length === 0
          ? `Atividade sem documento · HH/custo em ${decision.discipline ?? "disciplina definida"}`
          : `${decision.items.length} documento(s) de destino · veja "Redistribuições confirmadas"`
        : "Confirmada no servidor"}
    >
      <CheckCircle2 className="h-3.5 w-3.5" />
      {decision?.items.length === 0 ? "Atividade AT" : "Redistribuído"}
      {decision?.confirmedAtUtc && <span className="font-normal">· {new Date(decision.confirmedAtUtc).toLocaleDateString("pt-BR")}</span>}
    </div>
  );
}

function ProgressAction({
  finding,
  validatedByCode,
  excludedCodes,
  historyUnavailable,
  onSimulate,
}: {
  finding: InformationQualityFinding;
  validatedByCode: ReadonlyMap<string, ProgressDecision>;
  excludedCodes: ReadonlySet<string>;
  historyUnavailable: boolean;
  onSimulate: (finding: InformationQualityFinding) => void;
}) {
  // Status sem configuração salva: o caminho é configurar o projeto, nunca confirmar daqui.
  if (finding.rule === PROGRESS_UNCONFIGURED_RULE) {
    return (
      <Button asChild size="sm" variant="outline" className="gap-1.5">
        <Link to={progressSettingsLink(findingDocumentStatus(finding))}>
          <Settings2 className="h-3.5 w-3.5" />
          Configurar avanço físico
        </Link>
      </Button>
    );
  }

  if (isProgressSimulationPending(finding, validatedByCode, excludedCodes)) {
    return (
      <Button
        size="sm"
        variant="outline"
        className="gap-1.5"
        onClick={() => onSimulate(finding)}
        title={historyUnavailable ? "Histórico indisponível: simulação só para leitura, validação bloqueada." : undefined}
      >
        <Gauge className="h-3.5 w-3.5" />
        {historyUnavailable ? "Simular avanço físico (só leitura)" : "Simular avanço físico"}
      </Button>
    );
  }

  const decision = validatedByCode.get(redistributionKey(finding.documentCode));
  if (!decision) return <span className="text-xs text-muted-foreground">—</span>;
  return (
    <div
      className="inline-flex items-center gap-1.5 rounded-md border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-xs font-semibold text-emerald-700"
      title='Veja "Avanços físicos validados"'
    >
      <CheckCircle2 className="h-3.5 w-3.5" />
      Avanço validado · {formatPercent(decision.validatedPercent)}
    </div>
  );
}
