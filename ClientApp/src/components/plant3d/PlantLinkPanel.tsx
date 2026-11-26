// Associação persistente Projeto N&A (Project_ID_ACC) → Projeto Dashboard BIM, por IDs. Nome só serve de sugestão.
import { useEffect, useState } from "react";
import { Link2, Loader2, AlertTriangle, RefreshCw } from "lucide-react";
import { toast } from "react-hot-toast";
import { Button } from "@/components/ui/button";
import {
  DashboardCatalogProject, PlantProject, descreverErro, isAbort, plantRequest, projetoPath,
} from "./plantApi";

type Props = {
  project: { projectIdAcc: string; name: string };
  projects: PlantProject[];
  canViewCatalog: boolean;
  canEdit: boolean;
  onLinked: () => void;
};

export default function PlantLinkPanel({ project, projects, canViewCatalog, canEdit, onLinked }: Props) {
  const [catalog, setCatalog] = useState<DashboardCatalogProject[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [selected, setSelected] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [saving, setSaving] = useState(false);

  const loadCatalog = async (signal?: AbortSignal) => {
    setLoading(true);
    setError("");
    try {
      const result = await plantRequest<{ projects: DashboardCatalogProject[] }>("/catalog", { signal });
      setCatalog(result.projects ?? []);
    } catch (e) {
      if (!isAbort(e)) setError(descreverErro(e, "Catálogo do Dashboard BIM"));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setSelected("");
    setConfirmed(false);
    if (!canViewCatalog) return;
    const abort = new AbortController();
    void loadCatalog(abort.signal);
    return () => abort.abort();
  }, [project.projectIdAcc, canViewCatalog]);

  const donoDoVinculo = (dashboardProjectId: string) =>
    projects.find(p => p.dashboardProjectId?.toLowerCase() === dashboardProjectId.toLowerCase());
  const sugerido = (c: DashboardCatalogProject) =>
    !!c.projectCode && project.name.toLowerCase().includes(c.projectCode.toLowerCase());
  const escolhido = catalog.find(c => c.dashboardProjectId === selected);

  const associar = async () => {
    if (!escolhido || !confirmed) return;
    setSaving(true);
    try {
      await plantRequest(projetoPath(project.projectIdAcc) + "/link", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ dashboardProjectId: escolhido.dashboardProjectId }),
      });
      toast.success("Projeto associado ao Dashboard BIM.");
      onLinked();
    } catch (e) {
      toast.error(descreverErro(e, "Associação não realizada"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="glass-card rounded-xl border border-amber-500/30 bg-amber-500/5 p-6 space-y-4">
      <div className="flex items-start gap-3">
        <Link2 className="mt-0.5 h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" />
        <div className="space-y-1">
          <h3 className="font-bold text-foreground">Projeto não vinculado ao Dashboard BIM</h3>
          <p className="text-sm text-muted-foreground">
            <b className="text-foreground">{project.name || project.projectIdAcc}</b> ainda não tem projeto Dashboard associado.
            Sem o vínculo não há resultados nem configuração. O vínculo é feito por ID persistente — o nome é apenas uma sugestão.
          </p>
          <p className="font-mono text-[11px] text-muted-foreground">N&A: {project.projectIdAcc}</p>
        </div>
      </div>

      {!canViewCatalog && (
        <p className="text-sm text-muted-foreground">
          Seu perfil não tem permissão para consultar o catálogo do Dashboard BIM (<code>plant3d.database.view</code>). Peça a um administrador para vincular o projeto.
        </p>
      )}

      {canViewCatalog && (
        <>
          {error && (
            <div className="flex items-center gap-2 rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-600 dark:text-red-400">
              <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
            </div>
          )}

          <div className="flex flex-col gap-3 md:flex-row md:items-end">
            <div className="flex-1 space-y-1">
              <label htmlFor="dashboardCatalog" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Projeto do Dashboard BIM</label>
              <select
                id="dashboardCatalog"
                value={selected}
                onChange={e => { setSelected(e.target.value); setConfirmed(false); }}
                disabled={loading || catalog.length === 0}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm font-semibold text-foreground outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="">{loading ? "Carregando catálogo…" : catalog.length ? "-- Escolha o projeto do Dashboard --" : "Catálogo vazio"}</option>
                {catalog.map(c => {
                  const dono = donoDoVinculo(c.dashboardProjectId);
                  return (
                    <option key={c.dashboardProjectId} value={c.dashboardProjectId} disabled={!!dono}>
                      {c.projectCode}{sugerido(c) ? " (sugerido pelo nome)" : ""}{dono ? ` — já vinculado a ${dono.name || dono.projectIdAcc}` : ""}
                    </option>
                  );
                })}
              </select>
            </div>
            <Button variant="outline" onClick={() => loadCatalog()} disabled={loading} title="Recarregar catálogo" aria-label="Recarregar catálogo">
              <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            </Button>
          </div>

          {escolhido && (
            <div className="space-y-3 rounded-lg border border-border bg-background/60 p-4 text-sm">
              <p className="font-semibold text-foreground">Confira os IDs antes de associar:</p>
              <dl className="grid grid-cols-1 gap-x-4 gap-y-1 font-mono text-[11px] sm:grid-cols-[auto_1fr]">
                <dt className="text-muted-foreground">N&A (Project_ID_ACC)</dt><dd className="break-all text-foreground">{project.projectIdAcc}</dd>
                <dt className="text-muted-foreground">Dashboard (ProjectId)</dt><dd className="break-all text-foreground">{escolhido.dashboardProjectId}</dd>
                <dt className="text-muted-foreground">Código / origem</dt><dd className="break-all text-foreground">{escolhido.projectCode} · {escolhido.sourceProvider ?? "—"}</dd>
                <dt className="text-muted-foreground">ID externo (Plant)</dt><dd className="break-all text-foreground">{escolhido.externalProjectId ?? "—"}</dd>
              </dl>
              {!sugerido(escolhido) && (
                <p className="flex items-center gap-2 text-amber-700 dark:text-amber-400">
                  <AlertTriangle className="h-4 w-4 shrink-0" /> O código do Dashboard não aparece no nome do projeto N&A. Confirme que não é a cópia errada.
                </p>
              )}
              <label className="flex cursor-pointer items-start gap-2 text-foreground">
                <input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} disabled={!canEdit} className="mt-1" />
                <span>Confirmo que estes dois IDs representam o mesmo projeto e quero associá-los.</span>
              </label>
              {!canEdit && <p className="text-xs text-muted-foreground">Associar exige a permissão <code>plant3d.database.edit</code>.</p>}
              <Button onClick={associar} disabled={!confirmed || !canEdit || saving} className="gap-2 font-bold">
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />}
                ASSOCIAR PROJETOS
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
