// Simulação → confirmação do avanço físico gerencial de um documento sem avanço na LDP, a partir do percentual
// SALVO para o Document_Status no projeto. Não altera a LDP: cria uma camada gerencial validada pelo responsável.
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import toast from "react-hot-toast";
import { AlertTriangle, CheckCircle2, Gauge, Loader2, Settings2 } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import type { InformationQualityFinding } from "./informationQualityApi";
import {
  confirmProgress,
  formatPercent,
  getProgressPreview,
  progressBlockedReason,
  progressSettingsLink,
  type ProgressDecision,
  type ProgressPreview,
} from "./physicalProgressApi";

interface Props {
  projectId: string;
  finding: InformationQualityFinding | null;
  /** Decisão CONFIRMADO do histórico recarregado para este documento (fonte da verdade após confirmar). */
  confirmedDecision?: ProgressDecision;
  /** Quando preenchido, a simulação pode ser vista, mas a confirmação fica bloqueada (ex.: histórico indisponível). */
  confirmBlockedReason: string | null;
  onClose: () => void;
  /** Recarrega auditoria + decisões; a tela não confia só no estado local. */
  onConfirmed: () => Promise<void>;
}

export default function PhysicalProgressDialog({ projectId, finding, confirmedDecision, confirmBlockedReason, onClose, onConfirmed }: Props) {
  const documentCode = finding?.documentCode ?? null;
  const [preview, setPreview] = useState<ProgressPreview | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setPreview(null);
    setConfirmed(false);
    setReloading(false);
    setAcknowledged(false);
    setError(null);
    setConfirming(false);
    setLoading(false);
    if (!documentCode) return;

    const controller = new AbortController();
    setLoading(true);
    getProgressPreview(projectId, documentCode, controller.signal)
      .then(setPreview)
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setError(e instanceof Error ? e.message : "Falha ao simular o avanço físico.");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [projectId, documentCode]);

  const serverBlocked = preview ? progressBlockedReason(preview) : null;
  const blockedReason = !preview ? null : serverBlocked ?? confirmBlockedReason;
  const missingConfiguration = !!preview && preview.configuredPercent == null;

  const confirm = async () => {
    if (!preview || !documentCode || blockedReason || !acknowledged || preview.configuredPercent == null || !preview.documentStatus) return;
    setConfirming(true);
    setError(null);
    try {
      await confirmProgress(projectId, documentCode, preview.documentStatus, preview.configuredPercent);
      setConfirmed(true);
      toast.success(`Avanço físico de ${documentCode} validado.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao confirmar o avanço físico.");
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

  const canConfirm = !!preview && !blockedReason && acknowledged && !confirmed && !loading && !confirming;
  const locked = confirming || reloading;

  return (
    <Dialog open={!!finding} onOpenChange={(v) => { if (!v && !locked) onClose(); }}>
      <DialogContent
        className="flex max-h-[90dvh] max-w-[760px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (locked) e.preventDefault(); }}
      >
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Gauge className="h-5 w-5 text-primary" />
            {confirmed ? "Avanço físico validado" : "Simular avanço físico"}
          </DialogTitle>
          <DialogDescription>
            Documento <strong className="break-all font-mono text-foreground">{documentCode}</strong>
            {finding?.title && <> · {finding.title}</>}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 py-5 custom-scrollbar">
          {error && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{error}</p>}

          {loading && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />Consultando o Document Status e a configuração do projeto…
            </div>
          )}

          {confirmed && (
            <>
              {reloading && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" />Recarregando auditoria e avanços validados…
                </div>
              )}
              {confirmedDecision ? (
                <p className="flex items-start gap-2 rounded-md border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-700">
                  <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  Avanço gerencial de {formatPercent(confirmedDecision.validatedPercent)} validado
                  {confirmedDecision.confirmedAtUtc && <> em {new Date(confirmedDecision.confirmedAtUtc).toLocaleString("pt-BR")}</>}
                  {confirmedDecision.confirmedBy && <> por {confirmedDecision.confirmedBy}</>}. A LDP não foi alterada.
                </p>
              ) : !reloading && (
                <p className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  O servidor aceitou a validação, mas ela ainda não aparece em "Avanços físicos validados" como CONFIRMADO.
                  Atualize a auditoria para conferir antes de qualquer nova ação.
                </p>
              )}
            </>
          )}

          {!confirmed && preview && (
            <>
              <div className="grid gap-3 text-xs sm:grid-cols-2">
                <Info label="Documento" value={preview.documentCode} mono />
                <Info label="Document Status" value={preview.documentStatus ?? "—"} />
                <Info label="Avanço atual (LDP)" value={formatPercent(preview.originalPercent)} />
                <Info label="Percentual configurado do projeto" value={formatPercent(preview.configuredPercent)} />
              </div>
              <div className="rounded-lg border border-primary/30 bg-primary/5 px-4 py-3">
                <p className="text-xs text-muted-foreground">Avanço gerencial proposto</p>
                <b className="text-2xl tabular-nums">{formatPercent(preview.configuredPercent)}</b>
              </div>

              <p className="flex items-start gap-2 rounded-md border border-sky-500/30 bg-sky-500/5 px-3 py-2 text-xs text-muted-foreground">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-sky-600" />
                Esta ação não altera a LDP. Ela cria uma camada gerencial de avanço físico validada por você, usando o percentual
                salvo para este Document Status na configuração do projeto.
              </p>

              {blockedReason ? (
                <div role="alert" className="space-y-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
                  <p className="flex items-start gap-2">
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                    <span><b>Confirmação bloqueada:</b> {blockedReason}</span>
                  </p>
                  {missingConfiguration && (
                    <Button asChild size="sm" variant="outline" className="gap-1.5">
                      <Link to={progressSettingsLink(preview.documentStatus)}>
                        <Settings2 className="h-3.5 w-3.5" />Configurar avanço físico
                      </Link>
                    </Button>
                  )}
                </div>
              ) : (
                <label className="flex cursor-pointer items-start gap-2 rounded-md border border-border bg-muted/20 px-3 py-2 text-xs">
                  <input
                    type="checkbox"
                    className="mt-0.5"
                    checked={acknowledged}
                    onChange={(e) => setAcknowledged(e.target.checked)}
                    disabled={confirming}
                  />
                  <span>Valido <b>{formatPercent(preview.configuredPercent)}</b> para este documento.</span>
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
                Validar avanço físico
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function Info({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 px-3 py-2">
      <p className="text-muted-foreground">{label}</p>
      <b className={mono ? "mt-0.5 block break-all font-mono text-sm" : "mt-0.5 block text-sm tabular-nums"}>{value}</b>
    </div>
  );
}
