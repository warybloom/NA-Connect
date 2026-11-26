import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, CheckCircle2, FileScan, Folder, FolderOpen, Loader2, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import NavegadorPastas from "@/features/upload-governado/NavegadorPastas";
import { getConfiguration, listPackages, saveConfiguration, type DocumentQualityConfiguration, type OcrPackage } from "./documentQualityApi";

export default function AuditProjectConfiguration() {
  const { projetoAtual } = useProjetoAtual();
  const { can } = usePermissions();
  const canEdit = can("settings.projects.edit");
  const [packages, setPackages] = useState<OcrPackage[]>([]);
  const [configuration, setConfiguration] = useState<DocumentQualityConfiguration | null>(null);
  const [packageId, setPackageId] = useState<number | null>(null);
  const [folder, setFolder] = useState<{ id: string; path: string } | null>(null);
  const [choosingFolder, setChoosingFolder] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    if (!projetoAtual) return;
    const abort = new AbortController();
    setLoading(true); setMessage(null);
    Promise.all([listPackages(abort.signal), getConfiguration(projetoAtual.hubId, projetoAtual.projectId, abort.signal)])
      .then(([available, config]) => {
        setPackages(available); setConfiguration(config); setPackageId(config.packageId);
        setFolder(config.auditFolderId ? { id: config.auditFolderId, path: config.auditFolderPath ?? config.auditFolderId } : null);
      })
      .catch((error) => { if (!abort.signal.aborted) setMessage({ ok: false, text: error instanceof Error ? error.message : "Falha ao carregar." }); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [projetoAtual?.hubId, projetoAtual?.projectId]);

  if (!projetoAtual) return null;
  const changed = configuration != null && (packageId !== configuration.packageId || folder?.id !== configuration.auditFolderId || folder?.path !== configuration.auditFolderPath);
  const save = async () => {
    setSaving(true); setMessage(null);
    try {
      const config = await saveConfiguration(projetoAtual.hubId, projetoAtual.projectId, packageId, folder?.id ?? null, folder?.path ?? null);
      setConfiguration(config); setMessage({ ok: true, text: "Configuração da auditoria salva." });
    } catch (error) { setMessage({ ok: false, text: error instanceof Error ? error.message : "Falha ao salvar." }); }
    finally { setSaving(false); }
  };

  return (
    <section className="space-y-4 rounded-xl border border-border bg-card/60 p-5">
      <div className="flex flex-wrap items-start gap-4">
        <FileScan className="mt-1 h-6 w-6 text-primary" />
        <div className="min-w-0 flex-1">
          <h2 className="font-black">Auditoria de Qualidade de Documentos</h2>
          <p className="text-xs text-muted-foreground">{projetoAtual.nome ?? projetoAtual.projectId} — template e pasta ACC usados na auditoria manual.</p>
        </div>
        <Link to="/configuracoes/templates/auditoria-documental"><Button variant="outline" size="sm">Configurar templates</Button></Link>
        {canEdit && <Button size="sm" className="gap-2" disabled={!changed || saving || loading} onClick={() => void save()}>
          {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}Salvar
        </Button>}
      </div>
      {loading ? <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando configuração…</p> : (
        <div className="grid gap-4 md:grid-cols-2">
          <label className="space-y-1 text-xs font-bold uppercase tracking-wide">
            Template de Auditoria Documental
            <select className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm font-normal normal-case" value={packageId ?? ""}
              disabled={!canEdit} onChange={(event) => setPackageId(event.target.value ? Number(event.target.value) : null)}>
              <option value="">Selecione um template</option>
              {packages.map((item) => <option key={item.id} value={item.id}>{item.nome}</option>)}
            </select>
          </label>
          <div className="space-y-1 text-xs font-bold uppercase tracking-wide">
            Pasta de Auditoria
            <div className="flex min-h-10 items-center gap-2 rounded-md border border-input bg-background px-3 normal-case">
              {folder ? <><Folder className="h-4 w-4 shrink-0 text-primary" /><span className="min-w-0 flex-1 truncate text-sm font-normal" title={folder.path}>{folder.path}</span></>
                : <span className="flex-1 text-sm font-normal text-muted-foreground">Nenhuma pasta selecionada</span>}
              {canEdit && <Button variant="ghost" size="sm" className="gap-1" onClick={() => setChoosingFolder(true)}><FolderOpen className="h-4 w-4" />Escolher</Button>}
            </div>
          </div>
        </div>
      )}
      {message && <p className={`flex items-center gap-2 text-sm ${message.ok ? "text-emerald-600" : "text-destructive"}`}>
        {message.ok ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}{message.text}
      </p>}
      {choosingFolder && <NavegadorPastas hubId={projetoAtual.hubId} projectId={projetoAtual.projectId} titulo="Selecionar Pasta de Auditoria" acao="Usar esta pasta"
        onCancelar={() => setChoosingFolder(false)} onEscolher={(id, path) => { setFolder({ id, path }); setChoosingFolder(false); }} />}
    </section>
  );
}
