// Modal único da Central de Direcionamentos: Editar De/Para, Resetar De/Para, Resetar/Reavaliar Redistribuição.
// Nada é gravado ao abrir: o usuário preenche a justificativa, pede a simulação (POST /preview, sem gravação), revisa o
// antes/depois e só então confirma (POST /apply). Qualquer mudança no pedido invalida a simulação.
import { useEffect, useMemo, useRef, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, ArrowRight, Loader2, RotateCcw, ShieldAlert, Split } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  applyRoutingOperation,
  canApply,
  checkPreview,
  draftFingerprint,
  getLdpDocumentOptions,
  isAbortError,
  METHOD_LABEL,
  MIN_REASON_LENGTH,
  operationEffectText,
  OPERATION_LABEL,
  previewRoutingOperation,
  RoutingControlError,
  routingKey,
  validateDraft,
  type RoutingControlItem,
  type RoutingDestination,
  type RoutingOperation,
  type RoutingOperationRequest,
  type RoutingOrigin,
  type RoutingPreview,
} from "./routingControlApi";

export const formatHours = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value ?? 0);
export const formatCurrency = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(value ?? 0);
const signedHours = (value: number) => `${value > 0.004 ? "+" : ""}${formatHours(value)}`;
const signedCurrency = (value: number) => `${value > 0.004 ? "+" : ""}${formatCurrency(value)}`;

export interface RoutingDialogTarget {
  origin: RoutingOrigin;
  item: RoutingControlItem;
  operation: RoutingOperation;
  /** "Reavaliar redistribuição": reset controlado e, depois, o preview oficial de redistribuição (confirmação separada). */
  reevaluate: boolean;
}

interface Props {
  projectId: string;
  target: RoutingDialogTarget | null;
  /** Identidade exibida ao usuário; o autor gravado é sempre o do servidor (sessão). */
  userLabel: string | null;
  onClose: () => void;
  onApplied: (target: RoutingDialogTarget) => void | Promise<void>;
}

export default function RoutingControlDialog({ projectId, target, userLabel, onClose, onApplied }: Props) {
  const [reason, setReason] = useState("");
  const [destination, setDestination] = useState("");
  const [destinationFilter, setDestinationFilter] = useState("");
  const [ldpDocs, setLdpDocs] = useState<string[] | null>(null);
  const [ldpError, setLdpError] = useState<string | null>(null);
  const [preview, setPreview] = useState<RoutingPreview | null>(null);
  const [previewFingerprint, setPreviewFingerprint] = useState<string | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [secondStep, setSecondStep] = useState(false);
  const previewAbort = useRef<AbortController | null>(null);
  const previewSeq = useRef(0);

  const operation = target?.operation ?? null;
  const isEdit = operation === "EDIT_DE_PARA";
  const isReset = operation === "RESET_DE_PARA" || operation === "RESET_REDISTRIBUICAO";

  // Novo alvo → estado limpo; simulação em andamento de outro alvo é descartada.
  useEffect(() => {
    previewAbort.current?.abort();
    previewSeq.current += 1;
    setReason("");
    setDestination("");
    setDestinationFilter("");
    setPreview(null);
    setPreviewFingerprint(null);
    setPreviewing(false);
    setApplying(false);
    setError(null);
    setAcknowledged(false);
    setSecondStep(false);
    setLdpDocs(null);
    setLdpError(null);
    if (!target || target.operation !== "EDIT_DE_PARA") return;
    // Só leitura: lista de documentos existentes na LDP para o seletor.
    const controller = new AbortController();
    getLdpDocumentOptions(projectId, controller.signal).then(
      (docs) => { if (!controller.signal.aborted) setLdpDocs(docs); },
      (e) => {
        if (controller.signal.aborted || isAbortError(e)) return;
        setLdpError(e instanceof Error ? e.message : "Falha ao carregar os documentos da LDP.");
      },
    );
    return () => controller.abort();
  }, [target, projectId]);

  useEffect(() => () => previewAbort.current?.abort(), []);

  const ldpSet = useMemo(() => (ldpDocs ? new Set(ldpDocs.map(routingKey)) : null), [ldpDocs]);
  const destinationOptions = useMemo(() => {
    if (!ldpDocs || !target) return [];
    const q = destinationFilter.trim().toLocaleUpperCase("pt-BR");
    const source = routingKey(target.item.sourceDocumentCode);
    return ldpDocs.filter((code) => routingKey(code) !== source && (!q || code.toLocaleUpperCase("pt-BR").includes(q))).slice(0, 300);
  }, [ldpDocs, destinationFilter, target]);

  if (!target || !operation) return <Dialog open={false} />;

  const request: RoutingOperationRequest = {
    projectId,
    sourceDocumentCode: target.item.sourceDocumentCode,
    operation,
    destinationDocumentCode: isEdit ? destination : undefined,
    reason,
    version: target.item.version ?? "",
  };
  const fingerprint = draftFingerprint(request);
  const draftError = validateDraft({ operation, destinationDocumentCode: destination, reason }, target.item, isEdit ? ldpSet : null);
  const previewIsCurrent = !!preview && previewFingerprint === fingerprint;
  const check = preview ? checkPreview(preview) : null;
  const serverBlock = preview && (!preview.canConfirm || preview.blockingReason)
    ? preview.blockingReason ?? "O servidor não permite confirmar esta operação."
    : null;
  const ready = canApply(preview, previewFingerprint, fingerprint, acknowledged) && !applying && !previewing;
  const locked = applying;

  const runPreview = async () => {
    if (draftError) { setError(draftError); return; }
    previewAbort.current?.abort();
    const controller = new AbortController();
    previewAbort.current = controller;
    const seq = ++previewSeq.current;
    setPreviewing(true);
    setError(null);
    setPreview(null);
    setPreviewFingerprint(null);
    setAcknowledged(false);
    setSecondStep(false);
    try {
      const result = await previewRoutingOperation(request, controller.signal);
      if (seq !== previewSeq.current) return;
      setPreview(result);
      setPreviewFingerprint(fingerprint);
    } catch (e) {
      if (isAbortError(e) || seq !== previewSeq.current) return;
      setError(describeError(e));
    } finally {
      if (seq === previewSeq.current) setPreviewing(false);
    }
  };

  const apply = async () => {
    if (!ready || !preview?.version) return;
    if (isReset && !secondStep) { setSecondStep(true); return; }
    setApplying(true);
    setError(null);
    try {
      // Mesmo corpo simulado; a versão é a devolvida pela simulação (o que o usuário viu).
      const result = await applyRoutingOperation({ ...request, version: preview.version });
      if (!result.success) {
        setPreview(null);
        setPreviewFingerprint(null);
        setSecondStep(false);
        setAcknowledged(false);
        setError(result.message ?? "O servidor não aplicou a operação. Nada foi alterado; simule novamente.");
        return;
      }
      toast.success(result.message ?? `${OPERATION_LABEL[operation]} aplicado em ${target.item.sourceDocumentCode}.`);
      await onApplied(target);
    } catch (e) {
      setPreview(null);
      setPreviewFingerprint(null);
      setSecondStep(false);
      setAcknowledged(false);
      setError(describeError(e));
    } finally {
      setApplying(false);
    }
  };

  const title = target.reevaluate ? "Reavaliar redistribuição" : OPERATION_LABEL[operation];
  const disabledMethod = operation === "RESET_REDISTRIBUICAO" ? "REDISTRIBUICAO" : "DE_PARA";

  return (
    <Dialog open onOpenChange={(open) => { if (!open && !locked) onClose(); }}>
      <DialogContent
        className="flex max-h-[90dvh] max-w-[1040px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (locked) e.preventDefault(); }}
        onEscapeKeyDown={(e) => { if (locked) e.preventDefault(); }}
      >
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            {isEdit ? <Split className="h-5 w-5 text-primary" /> : <RotateCcw className="h-5 w-5 text-amber-600" />}
            {title}
          </DialogTitle>
          <DialogDescription>
            Origem <strong className="break-all font-mono text-foreground">{target.origin.sourceDocumentCode}</strong>
            {target.origin.sourceTitle && <> · {target.origin.sourceTitle}</>}
            {target.origin.discipline && <> · {target.origin.discipline}</>}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 py-5 custom-scrollbar">
          <div className="grid gap-3 text-xs sm:grid-cols-4">
            <Info label="HH de origem (Timesheet)" value={`${formatHours(target.origin.sourceHours)} HH`} />
            <Info label="Custo de origem" value={formatCurrency(target.origin.sourceCost)} />
            <Info label="Método afetado" value={METHOD_LABEL[target.item.method]} />
            <Info label="Destino atual" value={target.item.destinationDocumentCode ?? (target.item.destinations.length ? `${target.item.destinations.length} documento(s)` : "—")} mono />
          </div>

          <p className={cn(
            "flex items-start gap-2 rounded-md border px-3 py-2 text-xs",
            isEdit ? "border-primary/30 bg-primary/5" : "border-amber-500/40 bg-amber-500/10 text-amber-800 dark:text-amber-300",
          )}>
            <ShieldAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <span>
              {operationEffectText(operation, target.origin)}
              {isReset && <> Será desativado: <b>{METHOD_LABEL[disabledMethod]}</b>. Nenhum registro é apagado.</>}
              {target.reevaluate && <> Depois do reset, a tela abre a simulação oficial de redistribuição — ela só é gravada se você confirmar lá, sem edição manual de pesos.</>}
            </span>
          </p>

          {target.origin.bothActive && (
            <p role="alert" className="flex items-start gap-2 rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-700 dark:text-rose-300">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              Esta origem tem De/Para e Redistribuição ativos. Os dois não se somam; desative apenas o método incorreto.
            </p>
          )}

          {isEdit && (
            <fieldset className="space-y-2" disabled={applying}>
              <legend className="text-xs font-bold">Novo documento de destino (LDP)</legend>
              {ldpError ? (
                <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-700 dark:text-rose-300">
                  Não foi possível carregar os documentos da LDP ({ldpError}). A edição fica bloqueada.
                </p>
              ) : !ldpDocs ? (
                <p className="flex items-center gap-2 text-xs text-muted-foreground"><Loader2 className="h-3.5 w-3.5 animate-spin" />Carregando documentos da LDP…</p>
              ) : (
                <div className="grid gap-2 sm:grid-cols-[1fr_2fr]">
                  <input
                    aria-label="Filtrar documentos da LDP"
                    value={destinationFilter}
                    onChange={(e) => setDestinationFilter(e.target.value)}
                    placeholder="Filtrar número do documento"
                    className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                  />
                  <select
                    aria-label="Documento de destino"
                    value={destination}
                    onChange={(e) => setDestination(e.target.value)}
                    className="h-9 rounded-md border border-input bg-background px-3 font-mono text-xs"
                  >
                    <option value="">Selecione o documento existente na LDP…</option>
                    {destinationOptions.map((code) => <option key={code} value={code}>{code}</option>)}
                  </select>
                </div>
              )}
            </fieldset>
          )}

          <label className="block space-y-1 text-xs">
            <span className="font-bold">Justificativa <span className="text-rose-600">*</span></span>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={applying}
              rows={3}
              maxLength={1000}
              aria-required="true"
              placeholder="Por que esta mudança é necessária? Cite a evidência (ex.: confirmação do responsável, documento correto)."
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
            />
            <span className="text-muted-foreground">
              Mínimo de {MIN_REASON_LENGTH} caracteres · registrado no histórico com o usuário da sessão
              {userLabel ? <> (<b className="text-foreground">{userLabel}</b>)</> : null} e a data do servidor.
            </span>
          </label>

          <div className="flex flex-wrap items-center gap-2">
            <Button type="button" variant="outline" className="gap-2" onClick={() => void runPreview()} disabled={!!draftError || previewing || applying}>
              {previewing && <Loader2 className="h-4 w-4 animate-spin" />}
              {preview && !previewIsCurrent ? "Simular novamente" : "Simular efeito"}
            </Button>
            {draftError && <span className="text-xs text-muted-foreground">{draftError}</span>}
            {preview && !previewIsCurrent && (
              <span className="text-xs font-semibold text-amber-700 dark:text-amber-300">O pedido mudou depois da simulação — simule novamente.</span>
            )}
          </div>

          {error && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-700 dark:text-rose-300">{error}</p>}

          {preview && check && (
            <div className="space-y-3" aria-live="polite">
              <div className="grid gap-3 lg:grid-cols-2">
                <SideTable title="Antes" side={preview.before} residualHours={check.beforeResidualHours} residualCost={check.beforeResidualCost} />
                <SideTable title="Depois" side={preview.after} residualHours={check.afterResidualHours} residualCost={check.afterResidualCost} />
              </div>
              <div className="grid gap-3 text-xs sm:grid-cols-2">
                <Info label="Diferença total de HH (servidor)" value={`${signedHours(preview.totalHoursDelta)} HH`} warn={Math.abs(preview.totalHoursDelta) > 0.005} />
                <Info label="Diferença total de custo (servidor)" value={signedCurrency(preview.totalCostDelta)} warn={Math.abs(preview.totalCostDelta) > 0.005} />
              </div>
              {(serverBlock || check.localBlock) && (
                <p role="alert" className="flex items-start gap-2 rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-xs text-rose-700 dark:text-rose-300">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  <span><b>Confirmação bloqueada.</b> {serverBlock ?? check.localBlock}</span>
                </p>
              )}
              {!serverBlock && !check.localBlock && previewIsCurrent && (
                <label className="flex cursor-pointer items-start gap-2 rounded-md border border-border bg-muted/20 px-3 py-2 text-xs">
                  <input type="checkbox" className="mt-0.5" checked={acknowledged} onChange={(e) => { setAcknowledged(e.target.checked); setSecondStep(false); }} disabled={applying} />
                  <span>
                    Revisei o antes/depois de <b className="font-mono">{target.origin.sourceDocumentCode}</b>
                    {isEdit && destination && <> com novo destino <b className="font-mono">{destination}</b></>}.
                    {isReset && <> Entendo que <b>{METHOD_LABEL[disabledMethod]}</b> será desativado (sem apagar registros).</>}
                  </span>
                </label>
              )}
              {secondStep && (
                <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/50 bg-amber-500/15 px-3 py-2 text-xs font-semibold text-amber-800 dark:text-amber-300">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  Segunda confirmação: desativar {METHOD_LABEL[disabledMethod]} de {target.origin.sourceDocumentCode}? Clique novamente para confirmar ou em Cancelar.
                </p>
              )}
            </div>
          )}
        </div>

        <DialogFooter className="gap-2 border-t border-border px-6 py-3">
          <Button variant="outline" onClick={onClose} disabled={locked}>Cancelar</Button>
          <Button
            onClick={() => void apply()}
            disabled={!ready}
            variant={isReset ? "destructive" : "default"}
            className="min-w-[220px] gap-2"
          >
            {applying && <Loader2 className="h-4 w-4 animate-spin" />}
            {isEdit
              ? <>Confirmar novo destino <ArrowRight className="h-4 w-4" /></>
              : secondStep ? `Sim, desativar ${operation === "RESET_DE_PARA" ? "De/Para" : "redistribuição"}` : `${title}…`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function describeError(e: unknown): string {
  if (e instanceof RoutingControlError) {
    if (e.kind === "conflict") return `${e.message} Os dados mudaram desde o carregamento. Feche, atualize a central e simule de novo.`;
    if (e.kind === "forbidden") return `${e.message} Seu acesso a este projeto não permite alterar direcionamentos.`;
    return e.message;
  }
  return e instanceof Error ? e.message : "Falha inesperada.";
}

function SideTable({ title, side, residualHours, residualCost }: {
  title: string;
  side: { sourceHours: number; sourceCost: number; destinations: RoutingDestination[] };
  residualHours: number;
  residualCost: number;
}) {
  const hasResidual = Math.abs(residualHours) > 0.005 || Math.abs(residualCost) > 0.005;
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <p className="border-b border-border bg-muted/40 px-3 py-1.5 text-xs font-black uppercase tracking-widest text-muted-foreground">{title}</p>
      <table className="w-full table-fixed text-xs">
        <thead className="text-left text-muted-foreground">
          <tr>
            <th className="w-[55%] px-3 py-1.5">Destino</th>
            <th className="px-3 py-1.5 text-right">HH</th>
            <th className="px-3 py-1.5 text-right">Custo</th>
          </tr>
        </thead>
        <tbody>
          {side.destinations.length === 0 ? (
            <tr className="border-t"><td colSpan={3} className="px-3 py-2 text-muted-foreground">Sem destino documental.</td></tr>
          ) : side.destinations.map((row) => (
            <tr key={row.documentCode} className="border-t align-top">
              <td className="px-3 py-1.5">
                <p className="break-all font-mono">{row.documentCode}</p>
                {row.title && <p className="text-muted-foreground">{row.title}</p>}
              </td>
              <td className="px-3 py-1.5 text-right tabular-nums">{formatHours(row.hours)}</td>
              <td className="px-3 py-1.5 text-right tabular-nums">{formatCurrency(row.cost)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot className="border-t bg-muted/20">
          <tr>
            <td className="px-3 py-1.5 font-semibold">Origem</td>
            <td className="px-3 py-1.5 text-right font-semibold tabular-nums">{formatHours(side.sourceHours)}</td>
            <td className="px-3 py-1.5 text-right font-semibold tabular-nums">{formatCurrency(side.sourceCost)}</td>
          </tr>
          <tr className={cn("border-t", hasResidual ? "text-amber-700 dark:text-amber-300" : "text-muted-foreground")}>
            <td className="px-3 py-1.5">Resíduo sem destino documental</td>
            <td className="px-3 py-1.5 text-right tabular-nums">{formatHours(residualHours)}</td>
            <td className="px-3 py-1.5 text-right tabular-nums">{formatCurrency(residualCost)}</td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

function Info({ label, value, mono = false, warn = false }: { label: string; value: string; mono?: boolean; warn?: boolean }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 px-3 py-2">
      <p className="text-muted-foreground">{label}</p>
      <b className={cn("mt-0.5 block break-all text-sm tabular-nums", mono && "font-mono text-xs", warn && "text-amber-700 dark:text-amber-300")}>{value}</b>
    </div>
  );
}
