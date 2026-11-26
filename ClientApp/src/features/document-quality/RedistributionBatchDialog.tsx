import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, CheckCircle2, Loader2, Split } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  confirmRedistributionBatch,
  getActivityDisciplineOptions,
  getRedistributionBatchPreview,
  saveActivityDisciplineMapping,
  type InformationQualityFinding,
  type RedistributionBatchConfirmItem,
  type RedistributionBatchPreviewItem,
} from "./informationQualityApi";
import { DestinationsTable, formatCurrency, formatHours } from "./RedistributionDialog";

interface Props {
  projectId: string;
  findings: InformationQualityFinding[];
  confirmBlockedReason: string | null;
  onClose: () => void;
  onConfirmed: () => Promise<void>;
}

export default function RedistributionBatchDialog({
  projectId,
  findings,
  confirmBlockedReason,
  onClose,
  onConfirmed,
}: Props) {
  const documentCodes = useMemo(
    () => [...new Set(findings.map((x) => x.documentCode?.trim()).filter((x): x is string => !!x))],
    [findings],
  );
  const [items, setItems] = useState<RedistributionBatchPreviewItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<RedistributionBatchConfirmItem[] | null>(null);
  const [disciplineOptions, setDisciplineOptions] = useState<string[]>([]);
  const [disciplineByActivity, setDisciplineByActivity] = useState<Record<string, string>>({});
  const [mappingSaving, setMappingSaving] = useState<string | null>(null);

  const refreshPreviews = async (signal?: AbortSignal) => {
    if (!documentCodes.length) return;
    setLoading(true);
    setError(null);
    try {
      const response = await getRedistributionBatchPreview(projectId, documentCodes, signal);
      setItems(response);
      setAcknowledged(false);
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") return;
      setError(e instanceof Error ? e.message : "Falha ao simular os documentos selecionados.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  };

  useEffect(() => {
    setItems([]);
    setResults(null);
    setAcknowledged(false);
    setError(null);
    setDisciplineByActivity({});
    if (!documentCodes.length) return;
    const controller = new AbortController();
    void refreshPreviews(controller.signal);
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, documentCodes.join("|")]);

  useEffect(() => {
    if (!items.some((x) => !!x.preview?.sourceActivityCode)) return;
    if (disciplineOptions.length) return;
    const controller = new AbortController();
    getActivityDisciplineOptions(projectId, controller.signal)
      .then(setDisciplineOptions)
      .catch(() => {});
    return () => controller.abort();
  }, [projectId, items, disciplineOptions.length]);

  const validItems = items.filter((x) => !!x.preview?.canConfirm && !x.error);
  const blockedItems = items.filter((x) => !x.preview?.canConfirm || !!x.error);
  const validCodes = validItems.map((x) => x.documentCode);
  const locked = loading || confirming || reloading || !!mappingSaving;

  const saveAt = async (item: RedistributionBatchPreviewItem) => {
    const activity = item.preview?.sourceActivityCode;
    if (!activity) return;
    const discipline = disciplineByActivity[activity] ?? item.preview?.discipline ?? "";
    if (!discipline) return;
    setMappingSaving(activity);
    setError(null);
    try {
      await saveActivityDisciplineMapping(projectId, activity, discipline);
      toast.success(`Atividade ${activity} classificada como AT em ${discipline}.`);
      await refreshPreviews();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao salvar a classificação AT.");
    } finally {
      setMappingSaving(null);
    }
  };

  const confirmAll = async () => {
    if (!validCodes.length || !acknowledged || confirmBlockedReason) return;
    setConfirming(true);
    setError(null);
    try {
      const response = await confirmRedistributionBatch(projectId, validCodes);
      setResults(response);
      const ok = response.filter((x) => x.success).length;
      const failed = response.length - ok;
      if (failed === 0) toast.success(`${ok} tratamento(s) confirmado(s) em lote.`);
      else toast.error(`${ok} confirmado(s) e ${failed} falha(s). Revise o resultado.`);
      setReloading(true);
      await onConfirmed();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao confirmar os tratamentos em lote.");
    } finally {
      setReloading(false);
      setConfirming(false);
    }
  };

  return (
    <Dialog open={findings.length > 0} onOpenChange={(open) => { if (!open && !locked) onClose(); }}>
      <DialogContent className="flex max-h-[92dvh] max-w-[1100px] flex-col gap-0 overflow-hidden p-0">
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Split className="h-5 w-5 text-primary" />
            Simulações individuais · confirmação em lote
          </DialogTitle>
          <DialogDescription>
            {documentCodes.length} documento(s) selecionado(s). Cada documento é simulado separadamente; somente os válidos entram na confirmação em lote.
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 py-5">
          {error && (
            <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-600">{error}</p>
          )}
          {confirmBlockedReason && (
            <p role="alert" className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
              <b>Confirmação bloqueada:</b> {confirmBlockedReason}
            </p>
          )}
          {loading && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              Calculando {documentCodes.length} simulação(ões) individualmente…
            </div>
          )}

          {!loading && items.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-3">
              <div className="rounded-lg border border-border p-3 text-xs">
                <span className="text-muted-foreground">Selecionados</span>
                <b className="mt-1 block text-lg">{items.length}</b>
              </div>
              <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 p-3 text-xs">
                <span className="text-muted-foreground">Prontos para confirmar</span>
                <b className="mt-1 block text-lg text-emerald-700">{validItems.length}</b>
              </div>
              <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs">
                <span className="text-muted-foreground">Bloqueados / exigem decisão</span>
                <b className="mt-1 block text-lg text-amber-700">{blockedItems.length}</b>
              </div>
            </div>
          )}

          {items.map((item) => {
            const preview = item.preview;
            const activity = preview?.sourceActivityCode ?? null;
            const canMapAt = !!activity && (!!preview?.activityMappingRequired || preview?.disciplineResolution === "AT_MANUAL");
            const manualAt = preview?.disciplineResolution === "AT_MANUAL";
            const selectedDiscipline = activity
              ? disciplineByActivity[activity] ?? preview?.discipline ?? ""
              : "";

            return (
              <section key={item.documentCode} className="rounded-xl border border-border bg-card/60">
                <div className="flex flex-wrap items-start gap-3 border-b border-border px-4 py-3">
                  <div className="min-w-0 flex-1">
                    <p className="break-all font-mono text-xs font-bold">{item.documentCode}</p>
                    {preview?.discipline && <p className="mt-1 text-xs text-muted-foreground">{preview.discipline}</p>}
                  </div>
                  {item.error ? (
                    <span className="rounded-full border border-rose-500/30 bg-rose-500/10 px-2 py-1 text-[11px] font-bold text-rose-600">Erro</span>
                  ) : preview?.canConfirm ? (
                    <span className="rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-[11px] font-bold text-emerald-700">Pronto</span>
                  ) : (
                    <span className="rounded-full border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-[11px] font-bold text-amber-700">Bloqueado</span>
                  )}
                </div>

                <div className="space-y-3 p-4">
                  {item.error ? (
                    <p className="text-xs text-rose-600">{item.error}</p>
                  ) : preview && (
                    <>
                      <div className="grid gap-2 text-xs sm:grid-cols-4">
                        <div><span className="block text-muted-foreground">HH origem</span><b>{formatHours(preview.sourceHours)} HH</b></div>
                        <div><span className="block text-muted-foreground">Custo origem</span><b>{formatCurrency(preview.sourceCost)}</b></div>
                        <div><span className="block text-muted-foreground">Disciplina</span><b>{preview.discipline ?? "—"}</b></div>
                        <div><span className="block text-muted-foreground">Tratamento</span><b>{manualAt ? "AT sem documento" : `${preview.destinations.length} destino(s)`}</b></div>
                      </div>

                      {!preview.canConfirm && preview.blockingReason && (
                        <p className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700">
                          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                          {preview.blockingReason}
                        </p>
                      )}

                      {canMapAt && (
                        <div className="grid gap-2 rounded-md border border-sky-500/30 bg-sky-500/5 p-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
                          <div className="text-xs">
                            <span className="block text-muted-foreground">Atividade</span>
                            <b className="font-mono">{activity}</b> · AT
                          </div>
                          <label className="space-y-1 text-xs">
                            <span className="font-semibold">Disciplina responsável</span>
                            <select
                              className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
                              value={selectedDiscipline}
                              onChange={(e) => activity && setDisciplineByActivity((prev) => ({ ...prev, [activity]: e.target.value }))}
                              disabled={mappingSaving === activity}
                            >
                              <option value="">Selecione a disciplina</option>
                              {disciplineOptions.map((discipline) => <option key={discipline} value={discipline}>{discipline}</option>)}
                            </select>
                          </label>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            onClick={() => void saveAt(item)}
                            disabled={!selectedDiscipline || mappingSaving === activity}
                          >
                            {mappingSaving === activity && <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />}
                            {manualAt ? "Alterar disciplina" : "Salvar AT"}
                          </Button>
                        </div>
                      )}

                      {manualAt ? (
                        <p className="rounded-md border border-emerald-500/30 bg-emerald-500/5 px-3 py-2 text-xs text-muted-foreground">
                          <b className="text-foreground">AT sem documento.</b> HH e custo permanecem no projeto e serão atribuídos integralmente à disciplina {preview.discipline ?? "—"}.
                        </p>
                      ) : preview.destinations.length > 0 ? (
                        <DestinationsTable rows={preview.destinations} sourceHours={preview.sourceHours} sourceCost={preview.sourceCost} />
                      ) : null}
                    </>
                  )}
                </div>
              </section>
            );
          })}

          {!loading && validItems.length > 0 && !results && (
            <label className="flex cursor-pointer items-start gap-2 rounded-md border border-border bg-muted/20 px-3 py-3 text-xs">
              <input
                type="checkbox"
                className="mt-0.5"
                checked={acknowledged}
                onChange={(e) => setAcknowledged(e.target.checked)}
                disabled={locked}
              />
              <span>
                Revisei as <b>{validItems.length} simulações válidas</b> acima e quero confirmá-las de uma vez.
                Itens bloqueados não serão confirmados.
              </span>
            </label>
          )}

          {results && (
            <div className="space-y-2 rounded-lg border border-border p-3">
              <p className="text-sm font-bold">Resultado da confirmação em lote</p>
              {results.map((item) => (
                <div key={item.documentCode} className="flex items-start gap-2 text-xs">
                  {item.success ? <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" /> : <AlertTriangle className="h-4 w-4 shrink-0 text-rose-600" />}
                  <span className="break-all font-mono">{item.documentCode}</span>
                  <span className={item.success ? "text-emerald-700" : "text-rose-600"}>{item.success ? "Confirmado" : item.error ?? "Falhou"}</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <DialogFooter className="gap-2 border-t border-border px-6 py-3">
          <Button variant="outline" onClick={onClose} disabled={locked}>{results ? "Fechar" : "Cancelar"}</Button>
          {!results && (
            <Button
              onClick={() => void confirmAll()}
              disabled={!validItems.length || !acknowledged || !!confirmBlockedReason || locked}
              className="min-w-[220px] gap-2"
            >
              {confirming && <Loader2 className="h-4 w-4 animate-spin" />}
              Confirmar {validItems.length} válido(s)
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
