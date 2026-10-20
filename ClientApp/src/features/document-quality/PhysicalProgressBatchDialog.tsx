import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, CheckCircle2, Gauge, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import type { InformationQualityFinding } from "./informationQualityApi";
import {
  confirmProgress,
  formatPercent,
  getProgressPreview,
  progressBlockedReason,
  type ProgressPreview,
} from "./physicalProgressApi";

interface BatchProgressItem {
  documentCode: string;
  preview: ProgressPreview | null;
  error: string | null;
}

interface BatchProgressResult {
  documentCode: string;
  success: boolean;
  error: string | null;
}

interface Props {
  projectId: string;
  findings: InformationQualityFinding[];
  confirmBlockedReason: string | null;
  onClose: () => void;
  onConfirmed: () => Promise<void>;
}

export default function PhysicalProgressBatchDialog({
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
  const [items, setItems] = useState<BatchProgressItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<BatchProgressResult[] | null>(null);

  useEffect(() => {
    setItems([]);
    setResults(null);
    setAcknowledged(false);
    setError(null);
    if (!documentCodes.length) return;

    const controller = new AbortController();
    setLoading(true);

    Promise.all(documentCodes.map(async (documentCode): Promise<BatchProgressItem> => {
      try {
        const preview = await getProgressPreview(projectId, documentCode, controller.signal);
        return { documentCode, preview, error: null };
      } catch (e) {
        if (e instanceof DOMException && e.name === "AbortError") throw e;
        return { documentCode, preview: null, error: e instanceof Error ? e.message : "Falha ao simular avanço físico." };
      }
    }))
      .then((response) => setItems(response))
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setError(e instanceof Error ? e.message : "Falha ao simular os documentos selecionados.");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });

    return () => controller.abort();
  }, [projectId, documentCodes.join("|")]);

  const evaluated = items.map((item) => ({
    ...item,
    blockedReason: item.preview ? progressBlockedReason(item.preview) : item.error ?? "Simulação indisponível.",
  }));
  const validItems = evaluated.filter((item) => !!item.preview && !item.blockedReason);
  const blockedItems = evaluated.filter((item) => !item.preview || !!item.blockedReason);
  const locked = loading || confirming || reloading;

  const confirmAll = async () => {
    if (!validItems.length || !acknowledged || confirmBlockedReason) return;
    setConfirming(true);
    setError(null);

    const response: BatchProgressResult[] = [];
    for (const item of validItems) {
      const preview = item.preview!;
      try {
        if (!preview.documentStatus || preview.configuredPercent == null) {
          response.push({ documentCode: item.documentCode, success: false, error: "Preview incompleto." });
          continue;
        }
        await confirmProgress(projectId, item.documentCode, preview.documentStatus, preview.configuredPercent);
        response.push({ documentCode: item.documentCode, success: true, error: null });
      } catch (e) {
        response.push({
          documentCode: item.documentCode,
          success: false,
          error: e instanceof Error ? e.message : "Falha ao validar avanço físico.",
        });
      }
    }

    setResults(response);
    const ok = response.filter((x) => x.success).length;
    const failed = response.length - ok;
    if (failed === 0) toast.success(`${ok} avanço(s) físico(s) validado(s) em lote.`);
    else toast.error(`${ok} validado(s) e ${failed} falha(s). Revise o resultado.`);

    setConfirming(false);
    setReloading(true);
    try {
      await onConfirmed();
    } finally {
      setReloading(false);
    }
  };

  return (
    <Dialog open={findings.length > 0} onOpenChange={(open) => { if (!open && !locked) onClose(); }}>
      <DialogContent className="flex max-h-[92dvh] max-w-[980px] flex-col gap-0 overflow-hidden p-0">
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Gauge className="h-5 w-5 text-primary" />
            Simular avanços físicos selecionados
          </DialogTitle>
          <DialogDescription>
            {documentCodes.length} documento(s) selecionado(s). Cada Document Status é simulado individualmente; os válidos podem ser aprovados em uma única ação.
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
              Consultando Document Status e configuração de {documentCodes.length} documento(s)…
            </div>
          )}

          {!loading && items.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-3">
              <div className="rounded-lg border border-border p-3 text-xs">
                <span className="text-muted-foreground">Selecionados</span>
                <b className="mt-1 block text-lg">{items.length}</b>
              </div>
              <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 p-3 text-xs">
                <span className="text-muted-foreground">Prontos para validar</span>
                <b className="mt-1 block text-lg text-emerald-700">{validItems.length}</b>
              </div>
              <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs">
                <span className="text-muted-foreground">Bloqueados</span>
                <b className="mt-1 block text-lg text-amber-700">{blockedItems.length}</b>
              </div>
            </div>
          )}

          {evaluated.map((item) => (
            <section key={item.documentCode} className="rounded-xl border border-border bg-card/60">
              <div className="flex flex-wrap items-start gap-3 border-b border-border px-4 py-3">
                <div className="min-w-0 flex-1">
                  <p className="break-all font-mono text-xs font-bold">{item.documentCode}</p>
                  {item.preview?.documentStatus && <p className="mt-1 text-xs text-muted-foreground">{item.preview.documentStatus}</p>}
                </div>
                {item.error || item.blockedReason ? (
                  <span className="rounded-full border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-[11px] font-bold text-amber-700">Bloqueado</span>
                ) : (
                  <span className="rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-[11px] font-bold text-emerald-700">Pronto</span>
                )}
              </div>
              <div className="space-y-3 p-4">
                {item.error ? (
                  <p className="text-xs text-rose-600">{item.error}</p>
                ) : item.preview && (
                  <>
                    <div className="grid gap-2 text-xs sm:grid-cols-4">
                      <div><span className="block text-muted-foreground">Document Status</span><b>{item.preview.documentStatus ?? "—"}</b></div>
                      <div><span className="block text-muted-foreground">Avanço atual LDP</span><b>{formatPercent(item.preview.originalPercent)}</b></div>
                      <div><span className="block text-muted-foreground">Peso configurado</span><b>{formatPercent(item.preview.configuredPercent)}</b></div>
                      <div><span className="block text-muted-foreground">Avanço proposto</span><b className="text-primary">{formatPercent(item.preview.configuredPercent)}</b></div>
                    </div>
                    {item.blockedReason && (
                      <p className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700">
                        <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                        {item.blockedReason}
                      </p>
                    )}
                  </>
                )}
              </div>
            </section>
          ))}

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
                Revisei as <b>{validItems.length} simulações válidas</b> e quero validar os avanços físicos de uma vez.
                Documentos bloqueados não serão confirmados.
              </span>
            </label>
          )}

          {results && (
            <div className="space-y-2 rounded-lg border border-border p-3">
              <p className="text-sm font-bold">Resultado da validação em lote</p>
              {results.map((item) => (
                <div key={item.documentCode} className="flex items-start gap-2 text-xs">
                  {item.success ? <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" /> : <AlertTriangle className="h-4 w-4 shrink-0 text-rose-600" />}
                  <span className="break-all font-mono">{item.documentCode}</span>
                  <span className={item.success ? "text-emerald-700" : "text-rose-600"}>
                    {item.success ? "Validado" : item.error ?? "Falhou"}
                  </span>
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
              Validar {validItems.length} avanço(s)
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
