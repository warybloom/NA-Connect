
// SimulaÃ§Ã£o â†’ confirmaÃ§Ã£o da redistribuiÃ§Ã£o de HH de um nÃºmero de documento fora da LDP (ou retirado) para os
// documentos da mesma disciplina. Nada Ã© gravado atÃ© o responsÃ¡vel marcar a revisÃ£o e clicar em "Confirmar redistribuiÃ§Ã£o".
import { useEffect, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, CheckCircle2, Loader2, Split } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  confirmRedistribution,
  getActivityDisciplineOptions,
  getRedistributionPreview,
  saveActivityDisciplineMapping,
  summarizeDestinations,
  type InformationQualityFinding,
  type RedistributionDecision,
  type RedistributionDestination,
  type RedistributionPreview,
} from "./informationQualityApi";

export const formatHours = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value ?? 0);

export const formatCurrency = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(value ?? 0);

const percent = (value?: number | null) =>
  new Intl.NumberFormat("pt-BR", { style: "percent", maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value ?? 0);

const signedHours = (value: number) => `${value > 0 ? "+" : ""}${formatHours(value)}`;
const signedCurrency = (value: number) => `${value > 0 ? "+" : ""}${formatCurrency(value)}`;

interface Props {
  projectId: string;
  finding: InformationQualityFinding | null;
  /** DecisÃ£o CONFIRMADO do histÃ³rico recarregado para este nÃºmero de documento (fonte da verdade apÃ³s confirmar). */
  confirmedDecision?: RedistributionDecision;
  /** Quando preenchido, a simulaÃ§Ã£o pode ser vista, mas a confirmaÃ§Ã£o fica bloqueada (ex.: histÃ³rico indisponÃ­vel). */
  confirmBlockedReason: string | null;
  onClose: () => void;
  /** Recarrega auditoria + histÃ³rico; a tela nÃ£o confia sÃ³ no estado local. */
  onConfirmed: () => Promise<void>;
}

export default function RedistributionDialog({ projectId, finding, confirmedDecision, confirmBlockedReason, onClose, onConfirmed }: Props) {
  const documentCode = finding?.documentCode ?? null;
  const [preview, setPreview] = useState<RedistributionPreview | null>(null);
  const [responseDecision, setResponseDecision] = useState<RedistributionDecision | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [classificationOpen, setClassificationOpen] = useState(false);
  const [disciplineOptions, setDisciplineOptions] = useState<string[]>([]);
  const [selectedDiscipline, setSelectedDiscipline] = useState("");
  const [classificationLoading, setClassificationLoading] = useState(false);
  const [mappingSaving, setMappingSaving] = useState(false);
  const [mappingError, setMappingError] = useState<string | null>(null);

  useEffect(() => {
    setPreview(null);
    setResponseDecision(null);
    setConfirmed(false);
    setReloading(false);
    setAcknowledged(false);
    setError(null);
    setConfirming(false);
    setLoading(false);
    setClassificationOpen(false);
    setDisciplineOptions([]);
    setSelectedDiscipline("");
    setClassificationLoading(false);
    setMappingSaving(false);
    setMappingError(null);
    if (!documentCode) return;

    const controller = new AbortController();
    setLoading(true);
    getRedistributionPreview(projectId, documentCode, controller.signal)
      .then(setPreview)
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setError(e instanceof Error ? e.message : "Falha ao simular a redistribuiÃ§Ã£o.");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [projectId, documentCode]);

  const totals = preview ? summarizeDestinations(preview.destinations, preview.sourceHours, preview.sourceCost) : null;
  const weightBlocked = !!totals && totals.inconsistentWeightCodes.length > 0;
  const isManualActivity = preview?.disciplineResolution === "AT_MANUAL";
  const canClassifyActivity = !!preview?.sourceActivityCode && (!!preview.activityMappingRequired || isManualActivity);

  const blockedReason = !preview
    ? null
    : !preview.canConfirm
      ? preview.blockingReason ?? "O servidor nÃ£o permite confirmar este tratamento."
      : isManualActivity
        ? confirmBlockedReason
        : preview.destinations.length === 0
          ? "Nenhum documento de destino disponÃ­vel na LDP para esta disciplina."
          : confirmBlockedReason
            ?? (weightBlocked
              ? `HH e custo nÃ£o seguem o mesmo peso em ${totals!.inconsistentWeightCodes.length} destino(s). Revise antes de confirmar.`
              : null);

  const openClassification = async () => {
    if (!preview?.sourceActivityCode) return;
    setClassificationOpen(true);
    setMappingError(null);
    if (isManualActivity && preview.discipline) setSelectedDiscipline(preview.discipline);
    if (disciplineOptions.length > 0) return;
    setClassificationLoading(true);
    try {
      const options = await getActivityDisciplineOptions(projectId);
      setDisciplineOptions(options);
    } catch (e) {
      setMappingError(e instanceof Error ? e.message : "Falha ao carregar as disciplinas disponÃ­veis.");
    } finally {
      setClassificationLoading(false);
    }
  };

  const saveClassification = async () => {
    if (!documentCode || !preview?.sourceActivityCode || !selectedDiscipline) return;
    setMappingSaving(true);
    setMappingError(null);
    try {
      await saveActivityDisciplineMapping(projectId, preview.sourceActivityCode, selectedDiscipline);
      const refreshed = await getRedistributionPreview(projectId, documentCode);
      setPreview(refreshed);
      setAcknowledged(false);
      setClassificationOpen(!refreshed.canConfirm);
      toast.success(`Atividade ${preview.sourceActivityCode} classificada como AT em ${selectedDiscipline}.`);
    } catch (e) {
      setMappingError(e instanceof Error ? e.message : "Falha ao salvar a classificaÃ§Ã£o AT.");
    } finally {
      setMappingSaving(false);
    }
  };

  const confirm = async () => {
    if (!documentCode || blockedReason || !acknowledged) return;
    setConfirming(true);
    setError(null);
    try {
      const decision = await confirmRedistribution(projectId, documentCode);
      setResponseDecision(decision);
      setConfirmed(true);
      toast.success(isManualActivity
        ? `Atividade ${preview?.sourceActivityCode ?? documentCode} confirmada em ${preview?.discipline ?? "disciplina selecionada"}.`
        : `RedistribuiÃ§Ã£o de ${documentCode} confirmada.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao confirmar o tratamento.");
      setConfirming(false);
      return;
    }
    setConfirming(false);
    setReloading(true);
    try {
      await onConfirmed();
    } finally {
      setReloading(false);
    }
  };

  const shownDecision = confirmedDecision ?? responseDecision;
  const canConfirm = !!preview && !blockedReason && acknowledged && !confirmed && !loading && !confirming;
  const locked = confirming || reloading || mappingSaving;

  return (
    <Dialog open={!!finding} onOpenChange={(v) => { if (!v && !locked) onClose(); }}>
      <DialogContent
        className="flex max-h-[90dvh] max-w-[960px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (locked) e.preventDefault(); }}
      >
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Split className="h-5 w-5 text-primary" />
            {confirmed
              ? (isManualActivity ? "Atividade AT confirmada" : "RedistribuiÃ§Ã£o confirmada")
              : (isManualActivity ? "Classificar HH como atividade AT" : "Simular redistribuiÃ§Ã£o de HH")}
          </DialogTitle>
          <DialogDescription>
            NÃºmero do documento de origem <strong className="break-all font-mono text-foreground">{documentCode}</strong>
            {finding?.title && <> Â· {finding.title}</>}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 py-5 custom-scrollbar">
          {error && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{error}</p>}

          {loading && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />Calculando a distribuiÃ§Ã£o proporcional ao HH planejadoâ€¦
            </div>
          )}

          {confirmed && (
            <>
              {reloading && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" />Recarregando auditoria e histÃ³ricoâ€¦
                </div>
              )}
              {shownDecision ? (
                <DecisionView decision={shownDecision} />
              ) : !reloading && (
                <p className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  O servidor aceitou a confirmaÃ§Ã£o, mas a decisÃ£o ainda nÃ£o aparece no histÃ³rico como CONFIRMADO.
                  Atualize a auditoria para conferir antes de qualquer nova aÃ§Ã£o.
                </p>
              )}
            </>
          )}

          {!confirmed && preview && totals && (
            <>
              <div className="grid gap-3 text-xs sm:grid-cols-2 lg:grid-cols-4">
                <Info label="Disciplina" value={preview.discipline ?? "â€”"} />
                <Info label="HH de origem" value={`${formatHours(preview.sourceHours)} HH`} />
                <Info label="Custo de origem" value={formatCurrency(preview.sourceCost)} />
                <Info
                  label={isManualActivity ? "Destino gerencial" : "HH planejado dos destinos"}
                  value={isManualActivity ? "Disciplina (sem documento)" : `${formatHours(preview.totalDestinationPlannedHours)} HH`}
                />
              </div>

              {blockedReason ? (
                <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  <span><b>ConfirmaÃ§Ã£o bloqueada:</b> {blockedReason}</span>
                </p>
              ) : isManualActivity ? (
                <p className="flex items-start gap-2 rounded-md border border-emerald-500/30 bg-emerald-500/5 px-3 py-2 text-xs text-muted-foreground">
                  <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-600" />
                  <span>
                    <b className="text-foreground">Atividade sem documento.</b> Ao confirmar, os{" "}
                    <b>{formatHours(preview.sourceHours)} HH</b> e <b>{formatCurrency(preview.sourceCost)}</b> permanecem
                    integralmente no projeto e passam a compor a disciplina <b>{preview.discipline}</b>. Nenhum documento
                    de destino serÃ¡ criado ou usado.
                  </span>
                </p>
              ) : (
                <p className="flex items-start gap-2 rounded-md border border-sky-500/30 bg-sky-500/5 px-3 py-2 text-xs text-muted-foreground">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-sky-600" />
                  SimulaÃ§Ã£o apenas. HH e custo de origem serÃ£o distribuÃ­dos entre os documentos abaixo com o mesmo peso
                  (proporcional ao HH planejado), somente quando vocÃª confirmar.
                </p>
              )}

              {canClassifyActivity && (
                <div className="space-y-3 rounded-lg border border-sky-500/30 bg-sky-500/5 p-3">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="space-y-1 text-xs">
                      <p className="font-bold text-foreground">
                        {isManualActivity ? "ClassificaÃ§Ã£o manual da atividade" : "Este lanÃ§amento pode representar uma atividade"}
                      </p>
                      <p>
                        Atividade: <b className="font-mono">{preview.sourceActivityCode}</b> Â· Tipo: <b>AT</b>
                      </p>
                      <p className="text-muted-foreground">
                        {isManualActivity
                          ? <>Disciplina manual atual: <b className="text-foreground">{preview.discipline ?? "â€”"}</b>. Esta classificaÃ§Ã£o vale para esta atividade dentro deste projeto.</>
                          : <>ResoluÃ§Ã£o automÃ¡tica atual: <b className="text-foreground">{preview.discipline ?? "nÃ£o resolvida"}</b>. VocÃª pode substituir pela disciplina responsÃ¡vel da atividade neste projeto.</>}
                      </p>
                    </div>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void openClassification()}
                      disabled={classificationLoading || mappingSaving}
                    >
                      {isManualActivity ? "Alterar disciplina" : "Tratar como atividade AT"}
                    </Button>
                  </div>

                  {classificationOpen && (
                    <div className="grid gap-3 rounded-md border border-border bg-background/70 p-3 sm:grid-cols-[1fr_auto] sm:items-end">
                      <label className="space-y-1 text-xs">
                        <span className="font-semibold">Disciplina responsÃ¡vel</span>
                        <select
                          className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
                          value={selectedDiscipline}
                          onChange={(e) => setSelectedDiscipline(e.target.value)}
                          disabled={classificationLoading || mappingSaving}
                        >
                          <option value="">{classificationLoading ? "Carregando disciplinas..." : "Selecione a disciplina"}</option>
                          {disciplineOptions.map((discipline) => (
                            <option key={discipline} value={discipline}>{discipline}</option>
                          ))}
                        </select>
                      </label>
                      <Button
                        type="button"
                        onClick={() => void saveClassification()}
                        disabled={!selectedDiscipline || classificationLoading || mappingSaving}
                        className="gap-2"
                      >
                        {mappingSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                        Salvar classificaÃ§Ã£o
                      </Button>
                      {mappingError && (
                        <p role="alert" className="text-xs text-rose-500 sm:col-span-2">{mappingError}</p>
                      )}
                      <p className="text-[11px] text-muted-foreground sm:col-span-2">
                        Depois de salvar, a simulaÃ§Ã£o Ã© recalculada automaticamente. A classificaÃ§Ã£o Ã© reutilizada pelos outros lanÃ§amentos da mesma atividade neste projeto.
                      </p>
                    </div>
                  )}
                </div>
              )}

              {!isManualActivity && (
                <DestinationsTable rows={preview.destinations} sourceHours={preview.sourceHours} sourceCost={preview.sourceCost} />
              )}

              {!blockedReason && (
                <label className="flex cursor-pointer items-start gap-2 rounded-md border border-border bg-muted/20 px-3 py-2 text-xs">
                  <input
                    type="checkbox"
                    className="mt-0.5"
                    checked={acknowledged}
                    onChange={(e) => setAcknowledged(e.target.checked)}
                    disabled={confirming}
                  />
                  <span>
                    {isManualActivity ? (
                      <>
                        Confirmo que este lanÃ§amento Ã© uma <b>atividade AT sem documento</b> e que{" "}
                        <b>{formatHours(preview.sourceHours)} HH</b> e <b>{formatCurrency(preview.sourceCost)}</b> devem
                        permanecer no projeto, atribuÃ­dos integralmente Ã  disciplina <b>{preview.discipline}</b>.
                      </>
                    ) : (
                      <>
                        Revisei a simulaÃ§Ã£o e confirmo a redistribuiÃ§Ã£o de <b>{formatHours(preview.sourceHours)} HH</b> e{" "}
                        <b>{formatCurrency(preview.sourceCost)}</b> de <b className="font-mono">{documentCode}</b> para{" "}
                        {preview.destinations.length} documento(s)
                        {totals.hasDifference && <>, ciente da diferenÃ§a de {signedHours(totals.hoursDifference)} HH / {signedCurrency(totals.costDifference)}</>}.
                      </>
                    )}
                  </span>
                </label>
              )}
            </>
          )}
        </div>

        <DialogFooter className="gap-2 border-t border-border px-6 py-3">
          {confirmed ? (
            <Button onClick={onClose} disabled={reloading}>Fechar</Button>
          ) : (
            <>
              <Button variant="outline" onClick={onClose} disabled={confirming}>Cancelar</Button>
              <Button onClick={() => void confirm()} disabled={!canConfirm} className="min-w-[200px] gap-2">
                {confirming && <Loader2 className="h-4 w-4 animate-spin" />}
                {isManualActivity ? "Confirmar atividade AT" : "Confirmar redistribuiÃ§Ã£o"}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function DecisionView({ decision }: { decision: RedistributionDecision }) {
  const activityAt = decision.items.length === 0;
  return (
    <>
      <p className="flex items-start gap-2 rounded-md border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-700">
        <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        {activityAt ? "Atividade AT confirmada" : `DecisÃ£o ${decision.status ?? "registrada"}`}
        {decision.confirmedAtUtc && <> em {new Date(decision.confirmedAtUtc).toLocaleString("pt-BR")}</>}
        {decision.confirmedBy && <> por {decision.confirmedBy}</>}.
      </p>
      <div className="grid gap-3 text-xs sm:grid-cols-3">
        <Info label="Disciplina" value={decision.discipline ?? "â€”"} />
        <Info label={activityAt ? "HH da atividade" : "HH redistribuÃ­do"} value={`${formatHours(decision.sourceHours)} HH`} />
        <Info label={activityAt ? "Custo da atividade" : "Custo redistribuÃ­do"} value={formatCurrency(decision.sourceCost)} />
      </div>
      {activityAt ? (
        <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 px-3 py-3 text-sm text-muted-foreground">
          Sem documento de destino. HH e custo permanecem integralmente no projeto e compÃµem a disciplina <b className="text-foreground">{decision.discipline ?? "â€”"}</b>.
        </div>
      ) : (
        <DestinationsTable rows={decision.items} sourceHours={decision.sourceHours} sourceCost={decision.sourceCost} />
      )}
    </>
  );
}

export function DestinationsTable({ rows, sourceHours, sourceCost }: { rows: RedistributionDestination[]; sourceHours: number; sourceCost: number }) {
  if (!rows.length) {
    return (
      <div className="flex min-h-24 items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground">
        Nenhum documento de destino encontrado na LDP para esta disciplina.
      </div>
    );
  }

  const totals = summarizeDestinations(rows, sourceHours, sourceCost);
  const inconsistent = new Set(totals.inconsistentWeightCodes);

  return (
    <div className="space-y-2">
      <div className="overflow-auto rounded-lg border border-border">
        <table className="w-full min-w-[760px] text-sm">
          <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
            <tr>
              <th className="px-3 py-2">Documento destino</th>
              <th className="px-3 py-2 text-right">HH planejado</th>
              <th className="px-3 py-2 text-right">Peso</th>
              <th className="px-3 py-2 text-right">HH recebido</th>
              <th className="px-3 py-2 text-right">Custo recebido</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.documentCode} className={cn("border-t align-top", inconsistent.has(row.documentCode) && "bg-amber-500/10")}>
                <td className="max-w-[360px] px-3 py-2">
                  <p className="break-all font-mono text-xs">{row.documentCode}</p>
                  {row.title && <p className="mt-0.5 text-xs text-muted-foreground">{row.title}</p>}
                  {inconsistent.has(row.documentCode) && (
                    <p className="mt-0.5 text-[11px] font-semibold text-amber-700">HH e custo fora do peso informado</p>
                  )}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">{formatHours(row.plannedHours)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{percent(row.weight)}</td>
                <td className="px-3 py-2 text-right font-semibold tabular-nums">{formatHours(row.hours)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(row.cost)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t bg-muted/20 text-xs font-bold">
            <tr>
              <td className="px-3 py-2">Total destinos Â· {rows.length} documento(s)</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatHours(totals.plannedHours)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{percent(totals.weight)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatHours(totals.hours)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(totals.cost)}</td>
            </tr>
            <tr className="border-t">
              <td className="px-3 py-2 font-normal text-muted-foreground">Origem</td>
              <td className="px-3 py-2" />
              <td className="px-3 py-2 text-right tabular-nums">{percent(1)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatHours(sourceHours)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(sourceCost)}</td>
            </tr>
            <tr className={cn("border-t", totals.hasDifference ? "text-amber-700" : "text-muted-foreground")}>
              <td className="px-3 py-2">DiferenÃ§a Â· destinos âˆ’ origem</td>
              <td className="px-3 py-2" />
              <td className="px-3 py-2 text-right tabular-nums">{percent(totals.weight - 1)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{signedHours(totals.hoursDifference)}</td>
              <td className="px-3 py-2 text-right tabular-nums">{signedCurrency(totals.costDifference)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      {totals.hasDifference && (
        <p className="text-[11px] text-amber-700">
          A soma dos destinos difere da origem ({formatHours(sourceHours)} HH Â· {formatCurrency(sourceCost)}). Confira a diferenÃ§a acima antes de prosseguir.
        </p>
      )}
    </div>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 px-3 py-2">
      <p className="text-muted-foreground">{label}</p>
      <b className="mt-0.5 block text-sm tabular-nums">{value}</b>
    </div>
  );
}

