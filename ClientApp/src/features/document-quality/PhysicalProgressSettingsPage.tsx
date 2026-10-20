// Qualidade → Configuração de Avanço Físico: percentual de avanço por Document_Status, POR PROJETO.
// "Carregar modelo inicial" e as sugestões só preenchem a tela; nada vale para a auditoria até "Salvar configuração".
import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import toast from "react-hot-toast";
import { AlertTriangle, Gauge, Info, Loader2, Plus, RefreshCw, RotateCcw, Save, Sparkles, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { cn } from "@/lib/utils";
import {
  applyInitialModel,
  documentStatusKey,
  getProgressSettings,
  isRowChanged,
  isRowPendingSave,
  parsePercent,
  rowsFromSettings,
  saveProgressSettings,
  validateRows,
  type ProgressRow,
  type ProgressSettings,
} from "./physicalProgressApi";

const situation = (row: ProgressRow): { label: string; className: string } => {
  if (row.saved) {
    if (isRowChanged(row)) return { label: "Alterado — não salvo", className: "border-amber-500/35 bg-amber-500/10 text-amber-700" };
    return row.ativo
      ? { label: "Configurado", className: "border-emerald-500/35 bg-emerald-500/10 text-emerald-700" }
      : { label: "Configurado · inativo", className: "border-border bg-muted text-muted-foreground" };
  }
  if (parsePercent(row.percentual) == null) {
    return { label: "Não configurado", className: "border-rose-500/35 bg-rose-500/10 text-rose-600" };
  }
  const origem = row.origin === "sugestao" ? "Sugestão" : row.origin === "modelo" ? "Modelo inicial" : "Preenchido";
  return { label: `${origem} — não salvo`, className: "border-sky-500/35 bg-sky-500/10 text-sky-700" };
};

export default function PhysicalProgressSettingsPage() {
  const { projetoAtual } = useProjetoAtual();
  const [searchParams] = useSearchParams();
  const highlightKey = documentStatusKey(searchParams.get("status"));
  const [settings, setSettings] = useState<ProgressSettings | null>(null);
  const [rows, setRows] = useState<ProgressRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [showErrors, setShowErrors] = useState(false);
  const [newStatus, setNewStatus] = useState("");
  const loadSeq = useRef(0);
  // Projeto atual sempre lido daqui após awaits: closures antigas não podem carregar/mostrar outro projeto.
  const currentProjectId = useRef(projetoAtual?.projectId);
  currentProjectId.current = projetoAtual?.projectId;

  const load = async (signal?: AbortSignal) => {
    if (!projetoAtual?.projectId) return;
    const projectId = projetoAtual.projectId;
    const seq = ++loadSeq.current;
    setLoading(true);
    setError(null);
    try {
      const data = await getProgressSettings(projectId, signal);
      if (signal?.aborted || seq !== loadSeq.current || currentProjectId.current !== projectId) return;
      setSettings(data);
      setRows(rowsFromSettings(data));
      setShowErrors(false);
      setSaveError(null);
    } catch (e) {
      if ((e instanceof DOMException && e.name === "AbortError") || seq !== loadSeq.current) return;
      setError(e instanceof Error ? e.message : "Falha ao carregar a configuração de avanço físico.");
    } finally {
      if (!signal?.aborted && seq === loadSeq.current) setLoading(false);
    }
  };

  // Trocar de projeto descarta a tela inteira: configuração de um projeto nunca é usada em outro.
  useEffect(() => {
    setSettings(null);
    setRows([]);
    setSaveError(null);
    setShowErrors(false);
    if (!projetoAtual?.projectId) return;
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projetoAtual?.projectId]);

  // Status vindo da auditoria que ainda não aparece na tabela: já deixa pronto para adicionar.
  useEffect(() => {
    if (!settings || !highlightKey) return;
    if (!rows.some((row) => documentStatusKey(row.documentStatus) === highlightKey)) setNewStatus(searchParams.get("status")?.trim() ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settings, highlightKey]);

  const validation = useMemo(() => validateRows(rows), [rows]);
  const pendingCount = useMemo(() => rows.filter(isRowPendingSave).length, [rows]);
  const unconfiguredCount = useMemo(() => rows.filter((row) => !row.saved && parsePercent(row.percentual) == null).length, [rows]);
  const configuredCount = useMemo(() => rows.filter((row) => row.saved).length, [rows]);

  const updateRow = (index: number, patch: Partial<ProgressRow>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  const removeRow = (index: number) => setRows((current) => current.filter((_, i) => i !== index));

  const addStatus = () => {
    const status = newStatus.replace(/\s+/g, " ").trim();
    if (!status) return;
    if (rows.some((row) => documentStatusKey(row.documentStatus) === documentStatusKey(status))) {
      toast.error(`"${status}" já está na tabela.`);
      return;
    }
    setRows((current) => [...current, { documentStatus: status, count: 0, saved: null, percentual: "", ativo: true, origin: "manual" }]);
    setNewStatus("");
  };

  const loadInitialModel = () => {
    const result = applyInitialModel(rows);
    setRows(result.rows);
    if (result.filled + result.added === 0) {
      toast("Nada a preencher: os status do modelo já estão configurados ou preenchidos.");
    } else {
      toast.success(`Modelo carregado na tela: ${result.filled} preenchido(s), ${result.added} adicionado(s). Revise e salve.`);
    }
  };

  const discard = () => {
    if (!settings) return;
    setRows(rowsFromSettings(settings));
    setShowErrors(false);
    setSaveError(null);
  };

  const save = async () => {
    if (!projetoAtual?.projectId) return;
    setShowErrors(true);
    if (validation.rowErrors.size > 0) return;
    const projectId = projetoAtual.projectId;
    setSaving(true);
    setSaveError(null);
    try {
      await saveProgressSettings(projectId, validation.items);
      toast.success("Configuração de avanço físico salva para este projeto.");
    } catch (e) {
      if (currentProjectId.current === projectId) setSaveError(e instanceof Error ? e.message : "Falha ao salvar a configuração de avanço físico.");
      setSaving(false);
      return;
    }
    setSaving(false);
    // O GET é a fonte da verdade depois de salvar (só se o usuário continua no mesmo projeto).
    if (currentProjectId.current === projectId) await load();
  };

  if (!projetoAtual) {
    return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;
  }

  const busy = loading || saving;

  return (
    <div className="space-y-5 animate-enter">
      <section className="overflow-hidden rounded-xl border border-border bg-card/60">
        <div className="flex flex-wrap items-center gap-4 p-5">
          <div className="flex h-11 w-11 items-center justify-center rounded-xl border border-primary/20 bg-primary/10">
            <Gauge className="h-6 w-6 text-primary" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-[10px] font-black uppercase tracking-[0.22em] text-muted-foreground">Qualidade</p>
            <h1 className="text-xl font-black">Configuração de Avanço Físico</h1>
            <p className="max-w-4xl text-sm text-muted-foreground">
              Defina quanto cada Document Status representa de avanço físico neste projeto. A Auditoria usa somente configurações salvas.
            </p>
          </div>
          <Button
            variant="outline"
            className="gap-2"
            onClick={() => { if (pendingCount === 0 || window.confirm("Descartar as alterações não salvas e recarregar?")) void load(); }}
            disabled={busy}
          >
            <RefreshCw className={cn("h-4 w-4", loading && "animate-spin")} />
            Recarregar
          </Button>
        </div>

        <div className="grid border-t border-border bg-muted/20 md:grid-cols-2">
          <div className="flex items-start gap-3 border-b border-border px-4 py-3 md:border-b-0 md:border-r">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-sky-600" />
            <div>
              <p className="text-xs font-bold">Configuração é específica deste projeto.</p>
              <p className="text-xs leading-relaxed text-muted-foreground">
                Projeto <b className="text-foreground">{projetoAtual.nome ?? projetoAtual.projectId}</b>. Valores de outros projetos não são usados.
                A fonte é o Document Status da LDP (não o Status In Flow).
              </p>
            </div>
          </div>
          <div className="flex items-start gap-3 px-4 py-3">
            <Sparkles className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
            <div>
              <p className="text-xs font-bold">Modelo inicial e sugestões só preenchem a tela</p>
              <p className="text-xs leading-relaxed text-muted-foreground">
                Nada é salvo automaticamente e linhas já configuradas não são sobrescritas. Revise os valores antes de salvar.
              </p>
            </div>
          </div>
        </div>
      </section>

      {error && <ReportState title={error} error>Nenhuma configuração foi alterada. Tente novamente.</ReportState>}

      {!settings && !error && loading && (
        <ReportState title="Carregando configuração">Lendo os Document Status do snapshot atual da LDP e a configuração salva.</ReportState>
      )}

      {settings && (
        <section className="space-y-3 rounded-xl border border-border bg-card/60 p-4">
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs text-muted-foreground">
            <span>
              {rows.length.toLocaleString("pt-BR")} status · {configuredCount.toLocaleString("pt-BR")} configurado(s) ·{" "}
              <span className={cn(unconfiguredCount > 0 && "font-semibold text-rose-600")}>{unconfiguredCount.toLocaleString("pt-BR")} não configurado(s)</span>
              {pendingCount > 0 && <span className="font-semibold text-amber-700"> · {pendingCount.toLocaleString("pt-BR")} alteração(ões) não salva(s)</span>}
            </span>
            {(settings.updatedAtUtc || settings.updatedBy) && (
              <span>
                Última atualização
                {settings.updatedAtUtc && <> em {new Date(settings.updatedAtUtc).toLocaleString("pt-BR")}</>}
                {settings.updatedBy && <> por {settings.updatedBy}</>}
              </span>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" className="gap-2" onClick={loadInitialModel} disabled={busy}>
              <Sparkles className="h-4 w-4" />
              Carregar modelo inicial
            </Button>
            <div className="flex min-w-[280px] flex-1 items-center gap-2">
              <input
                value={newStatus}
                onChange={(e) => setNewStatus(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") addStatus(); }}
                placeholder="Adicionar Document Status manualmente..."
                className="h-9 w-full max-w-sm rounded-md border border-input bg-background px-3 text-sm"
                disabled={busy}
              />
              <Button variant="outline" className="gap-2" onClick={addStatus} disabled={busy || !newStatus.trim()}>
                <Plus className="h-4 w-4" />
                Adicionar
              </Button>
            </div>
            <Button variant="ghost" className="gap-2" onClick={discard} disabled={busy || pendingCount === 0}>
              <RotateCcw className="h-4 w-4" />
              Descartar alterações
            </Button>
            <Button className="min-w-[180px] gap-2" onClick={() => void save()} disabled={busy || pendingCount === 0}>
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              Salvar configuração
            </Button>
          </div>

          {saveError && (
            <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{saveError}</p>
          )}
          {showErrors && validation.rowErrors.size > 0 && (
            <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              Corrija {validation.rowErrors.size} linha(s) destacada(s) antes de salvar.
            </p>
          )}

          {rows.length === 0 ? (
            <div className="flex min-h-36 flex-col items-center justify-center gap-2 rounded-lg border border-dashed text-sm text-muted-foreground">
              Nenhum Document Status encontrado no snapshot atual da LDP. Adicione manualmente ou carregue o modelo inicial.
            </div>
          ) : (
            <div className="overflow-auto rounded-lg border border-border">
              <table className="w-full min-w-[860px] text-sm">
                <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Document Status</th>
                    <th className="px-3 py-2 text-right">Documentos na LDP</th>
                    <th className="px-3 py-2">Percentual de avanço</th>
                    <th className="px-3 py-2">Ativo</th>
                    <th className="px-3 py-2">Situação</th>
                    <th className="px-3 py-2" />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, index) => {
                    const state = situation(row);
                    const rowError = showErrors ? validation.rowErrors.get(index) : undefined;
                    const highlighted = !!highlightKey && documentStatusKey(row.documentStatus) === highlightKey;
                    const removable = !row.saved && row.origin !== "observado";
                    return (
                      <tr
                        key={`${documentStatusKey(row.documentStatus)}:${index}`}
                        className={cn("border-t align-top", highlighted && "bg-primary/5", rowError && "bg-rose-500/5")}
                      >
                        <td className="px-3 py-2">
                          <p className="font-semibold">{row.documentStatus}</p>
                          {row.origin === "manual" && <p className="text-[11px] text-muted-foreground">Adicionado manualmente</p>}
                          {rowError && <p className="mt-0.5 text-[11px] font-semibold text-rose-600">{rowError}</p>}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">{row.count.toLocaleString("pt-BR")}</td>
                        <td className="px-3 py-2">
                          <div className="flex items-center gap-1.5">
                            <input
                              inputMode="decimal"
                              value={row.percentual}
                              onChange={(e) => updateRow(index, { percentual: e.target.value })}
                              placeholder="—"
                              aria-label={`Percentual de avanço de ${row.documentStatus}`}
                              className={cn(
                                "h-8 w-24 rounded-md border border-input bg-background px-2 text-right text-sm tabular-nums",
                                rowError && "border-rose-500",
                              )}
                              disabled={busy}
                            />
                            <span className="text-xs text-muted-foreground">%</span>
                          </div>
                        </td>
                        <td className="px-3 py-2">
                          <input
                            type="checkbox"
                            className="mt-2"
                            checked={row.ativo}
                            onChange={(e) => updateRow(index, { ativo: e.target.checked })}
                            aria-label={`${row.documentStatus} ativo`}
                            disabled={busy}
                          />
                        </td>
                        <td className="px-3 py-2">
                          <span className={cn("inline-flex rounded-full border px-2 py-0.5 text-[11px] font-bold", state.className)}>{state.label}</span>
                          {row.saved && (row.saved.updatedAtUtc || row.saved.updatedBy) && (
                            <p className="mt-1 text-[11px] text-muted-foreground">
                              {row.saved.updatedAtUtc && new Date(row.saved.updatedAtUtc).toLocaleString("pt-BR")}
                              {row.saved.updatedBy && <> · {row.saved.updatedBy}</>}
                            </p>
                          )}
                        </td>
                        <td className="px-3 py-2 text-right">
                          {removable && (
                            <Button size="sm" variant="ghost" onClick={() => removeRow(index)} disabled={busy} title="Remover linha não salva">
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          <p className="text-[11px] text-muted-foreground">
            Status já configurados não podem ficar sem percentual; para deixar de usar um status, desmarque "Ativo".
            Status sem percentual continuam "Não configurado" e aparecem na Auditoria como pendência de configuração.
          </p>
        </section>
      )}
    </div>
  );
}
