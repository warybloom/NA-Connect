import { useCallback, useEffect, useMemo, useState } from "react";
import { useProductionSelectionInterna } from "@/features/production/ProductionProjectSelector";
import { usePermissions } from "@/features/permissions/PermissionProvider";

interface EmissionConfiguration {
  projectId: string;
  revision: number;
  statuses: string[];
  availableStatuses: string[];
  configured: boolean;
  updatedBy?: string | null;
  updatedAt?: string | null;
}
const norm = (s: string) => s.trim().toUpperCase();
export default function EmissionStatusesPage() {
  const { can } = usePermissions();
  const selection = useProductionSelectionInterna({});
  const projects = useMemo(() => selection.projects.map(p => ({ id: p.id, name: p.attributes.name })), [selection.projects]);
  const [projectId, setProjectId] = useState("");
  const [data, setData] = useState<EmissionConfiguration | null>(null);
  const [statuses, setStatuses] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  useEffect(() => {
    if (!projects.some(p => p.id === projectId)) setProjectId(projects[0]?.id ?? "");
  }, [projectId, projects]);
  const fetchConfig = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) return;
    setLoading(true); setError(""); setSuccess("");
    try {
      const response = await fetch("/api/planning/emission-statuses?" + new URLSearchParams({ projectId, catalog: "true" }), { credentials: "same-origin", signal });
      const json = await response.json();
      if (!response.ok) throw new Error(json.message || "Falha ao carregar configuração.");
      if (!signal?.aborted) {
        setData(json as EmissionConfiguration);
        setStatuses((json.statuses as string[]).map(norm));
      }
    } catch (e) {
      if (!signal?.aborted) setError(e instanceof Error ? e.message : "Falha inesperada.");
    } finally { if (!signal?.aborted) setLoading(false); }
  }, [projectId]);
  useEffect(() => {
    const abort = new AbortController();
    setData(null); setStatuses([]);
    void fetchConfig(abort.signal);
    return () => abort.abort();
  }, [fetchConfig]);
  const canEdit = can("planning.emission-statuses.edit") || can("planning.planner.management-documents.edit");
  const selected = new Set(statuses);
  const toggle = (value: string) => {
    const current = norm(value);
    setStatuses(old => old.includes(current) ? old.filter(x => x !== current) : [...old, current]);
    setSuccess("");
  };
  const save = async () => {
    if (!data || !projectId || !canEdit) return;
    setSaving(true); setError(""); setSuccess("");
    try {
      const response = await fetch("/api/planning/emission-statuses?" + new URLSearchParams({ projectId, catalog: "true" }), {
        method: "PUT", credentials: "same-origin",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ revision: data.revision, statuses })
      });
      const json = await response.json();
      if (!response.ok) throw new Error(json.message || "Falha ao salvar.");
      setData({ ...data, ...json, configured: true });
      setSuccess("Status válidos para emissão salvos para este projeto.");
    } catch (e) { setError(e instanceof Error ? e.message : "Falha ao salvar."); }
    finally { setSaving(false); }
  };
  return <section className="mx-auto max-w-4xl space-y-5 p-6 text-foreground">
    <header><h1 className="text-2xl font-bold">Status Válidos para Emissão</h1>
      <p className="text-sm text-muted-foreground">Defina os status da LDP ou da programação que comprovam emissão na 01. VISÃO GERAL. A configuração de avanço físico permanece independente.</p>
    </header>
    <label className="block space-y-2"><span className="font-medium">Projeto</span>
      <select value={projectId} onChange={e => setProjectId(e.target.value)} className="w-full rounded border bg-background p-3">
        {projects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
      </select>
    </label>
    {loading && <p role="status">Carregando status reais da LDP...</p>}
    {error && <p className="rounded border border-red-600 p-3 text-red-600" role="alert">{error} <button onClick={() => void fetchConfig()} className="underline">Recarregar</button></p>}
    {data && !loading && <>
      <p className="text-sm text-muted-foreground">{data.configured ? "Configuração salva" : "Configuração inicial ainda não salva"} — revisão {data.revision}</p>
      <div className="space-y-3 rounded-lg border p-4">
        {data.availableStatuses.length === 0 ? <p>Nenhum status encontrado na LDP ou na programação deste projeto.</p>
          : [...new Set([...data.availableStatuses, ...statuses])].sort().map(name => <label key={name} className="flex items-center justify-between border-b py-2">
            <span className="font-medium">{name} {!data.availableStatuses.includes(name) && <small className="text-amber-600">(não encontrado na fonte atual — desmarque para remover)</small>}</span>
            <input type="checkbox" disabled={!canEdit || saving || ["CANCELADO", "CANCELED"].includes(norm(name))}
              checked={selected.has(norm(name))} onChange={() => toggle(name)} aria-label={name + " comprova emissão"} />
          </label>)}
      </div>
      {success && <p role="status" className="text-green-600">{success}</p>}
      <button type="button" onClick={() => void save()} disabled={!canEdit || saving}
        className="rounded bg-primary px-5 py-2 text-primary-foreground disabled:opacity-50">
        {saving ? "Salvando..." : "Salvar status de emissão"}
      </button>
      {!canEdit && <p className="text-sm text-muted-foreground">Você tem permissão de consulta, mas não de edição.</p>}
    </>}
  </section>;
}
