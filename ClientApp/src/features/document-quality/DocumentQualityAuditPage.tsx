import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, Download, FileSearch, Folder, Loader2, Play, Search, Settings2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { ApiError } from "@/lib/api";
import { disciplinasDe, porDisciplina, porStatus, resumoDe, SEM_DISCIPLINA, TODAS } from "./auditFilters";
import { Link } from "react-router-dom";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import NavegadorPastas from "@/features/upload-governado/NavegadorPastas";
import { getConfiguration, getLatestRun, getRunStatus, reportUrl, saveConfiguration, startAudit, type DocumentQualityConfiguration, type DocumentResult, type RunResult, type RunStatus } from "./documentQualityApi";

const INTERVALO_STATUS_MS = 2000;
// Execução em andamento por projeto (sessionStorage): recarregar a tela retoma o acompanhamento. Só conveniência.
const chaveExecucao = (projectId: string) => `na.docQuality.execucao.${projectId}`;
const lerExecucao = (projectId: string) => { try { return sessionStorage.getItem(chaveExecucao(projectId)); } catch { return null; } };
const gravarExecucao = (projectId: string, id: string | null) => {
  try { if (id) sessionStorage.setItem(chaveExecucao(projectId), id); else sessionStorage.removeItem(chaveExecucao(projectId)); } catch { /* sem storage */ }
};

const statusClass: Record<string, string> = {
  "CONFORME": "bg-emerald-500/10 text-emerald-600 border-emerald-500/30",
  "DIVERGÊNCIA": "bg-amber-500/10 text-amber-600 border-amber-500/30",
  "FORA DA LDP": "bg-rose-500/10 text-rose-600 border-rose-500/30",
  "NÃO IDENTIFICADO": "bg-slate-500/10 text-slate-600 border-slate-500/30",
  "DUPLICADO": "bg-violet-500/10 text-violet-600 border-violet-500/30",
  "AMBÍGUO": "bg-fuchsia-500/10 text-fuchsia-600 border-fuchsia-500/30",
  "ERRO DE LEITURA": "bg-orange-500/10 text-orange-600 border-orange-500/30",
  "NÃO CALIBRADO": "bg-sky-500/10 text-sky-600 border-sky-500/30",
};

export default function DocumentQualityAuditPage() {
  const { projetoAtual } = useProjetoAtual();
  const [configuration, setConfiguration] = useState<DocumentQualityConfiguration | null>(null);
  const [result, setResult] = useState<RunResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [executionId, setExecutionId] = useState<string | null>(null);
  const [run, setRun] = useState<RunStatus | null>(null);
  const running = starting || !!executionId;
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState("TODOS");
  const [disciplina, setDisciplina] = useState(TODAS);
  const [selected, setSelected] = useState<DocumentResult | null>(null);
  const [escolhendoPasta, setEscolhendoPasta] = useState(false);
  const { can } = usePermissions();

  useEffect(() => {
    if (!projetoAtual) return;
    const abort = new AbortController();
    setLoading(true); setError(null); setResult(null);
    getConfiguration(projetoAtual.hubId, projetoAtual.projectId, abort.signal)
      .then(setConfiguration)
      .catch((cause) => { if (!abort.signal.aborted) setError(cause instanceof Error ? cause.message : "Falha ao carregar a configuração."); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    setRun(null);
    const emAndamento = lerExecucao(projetoAtual.projectId);
    setExecutionId(emAndamento);
    // Sem execução em andamento: abre na EXECUÇÃO MAIS RECENTE (as anteriores continuam guardadas no servidor).
    if (!emAndamento) getLatestRun(projetoAtual.hubId, projetoAtual.projectId, abort.signal)
      .then((latest) => { if (!abort.signal.aborted && latest) setResult((atual) => atual ?? latest); })
      .catch(() => { /* sem histórico: a tela continua vazia até a primeira auditoria */ });
    return () => abort.abort();
  }, [projetoAtual?.hubId, projetoAtual?.projectId]);

  // Acompanhamento: consulta o status periodicamente até COMPLETED/FAILED (nenhuma request fica aberta esperando os PDFs).
  useEffect(() => {
    if (!projetoAtual || !executionId) return;
    const { hubId, projectId } = projetoAtual;
    const abort = new AbortController();
    let timer: number | undefined;
    let falhasSeguidas = 0;
    const encerrar = () => { gravarExecucao(projectId, null); setExecutionId(null); };
    const consultar = async () => {
      try {
        const status = await getRunStatus(hubId, projectId, executionId, abort.signal);
        if (abort.signal.aborted) return;
        falhasSeguidas = 0;
        setRun(status);
        if (status.status === "COMPLETED") { setResult(status.result); encerrar(); return; }
        if (status.status === "FAILED") { setError(status.error ?? "A auditoria falhou."); encerrar(); return; }
      } catch (cause) {
        if (abort.signal.aborted) return;
        if (cause instanceof ApiError && cause.status === 404) {
          setError("A execução não foi encontrada no servidor (ele pode ter sido reiniciado). Inicie a auditoria novamente."); encerrar(); return;
        }
        if (++falhasSeguidas >= 5) { setError(cause instanceof Error ? cause.message : "Não foi possível consultar o andamento da auditoria."); encerrar(); return; }
      }
      timer = window.setTimeout(() => void consultar(), INTERVALO_STATUS_MS);
    };
    void consultar();
    return () => { abort.abort(); window.clearTimeout(timer); };
  }, [projetoAtual?.hubId, projetoAtual?.projectId, executionId]);

  // Disciplina (da LDP do documento identificado) + status. Os indicadores acompanham a disciplina escolhida.
  const disciplinas = useMemo(() => disciplinasDe(result?.documents ?? []), [result]);
  const daDisciplina = useMemo(() => porDisciplina(result?.documents ?? [], disciplina), [result, disciplina]);
  const documents = useMemo(() => porStatus(daDisciplina, filter), [daDisciplina, filter]);
  const resumo = useMemo(() => resumoDe(daDisciplina), [daDisciplina]);
  if (!projetoAtual) return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;
  if (loading) return <ReportState title="Carregando…">Lendo a configuração da auditoria documental.</ReportState>;

  const ready = Boolean(configuration?.packageId && configuration?.auditFolderId);
  // Pasta auditada: escolhida aqui (o template é a coluna "Template Auditoria" da Configuração do Projeto). Mantém o template.
  const salvarPasta = async (folderId: string, caminho: string) => {
    setEscolhendoPasta(false); setError(null);
    try { setConfiguration(await saveConfiguration(projetoAtual.hubId, projetoAtual.projectId, configuration?.packageId ?? null, folderId, caminho)); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Falha ao salvar a pasta de auditoria."); }
  };
  // POST responde na hora (202 + executionId); o processamento continua no servidor.
  const execute = async () => {
    setStarting(true); setError(null); setFilter("TODOS"); setDisciplina(TODAS); setResult(null); setRun(null);
    try {
      const r = await startAudit(projetoAtual.hubId, projetoAtual.projectId);
      gravarExecucao(projetoAtual.projectId, r.executionId);
      setExecutionId(r.executionId);
    }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Falha ao iniciar a auditoria."); }
    finally { setStarting(false); }
  };

  return (
    <div className="space-y-5 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <FileSearch className="h-7 w-7 text-primary" />
        <div className="min-w-0 flex-1">
          <h1 className="text-xl font-black">Auditoria de Qualidade de Documentos</h1>
          <p className="text-sm text-muted-foreground">Leitura somente de PDFs da pasta configurada e comparação com a LDP atual.</p>
        </div>
        <Button className="gap-2" disabled={!ready || running} onClick={() => void execute()}>
          {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}{running ? "Auditando…" : "Auditar agora"}
        </Button>
      </section>

      <section className="grid gap-3 md:grid-cols-3">
        <Info label="Projeto atual" value={configuration?.projectName ?? projetoAtual.nome ?? projetoAtual.projectId} icon={<Settings2 className="h-4 w-4" />} />
        <Info label="Pasta configurada" value={configuration?.auditFolderPath ?? "Não configurada"} icon={<Folder className="h-4 w-4" />}
          action={can("settings.projects.edit") && !running ? <button type="button" className="text-[10px] font-bold uppercase text-primary hover:underline" onClick={() => setEscolhendoPasta(true)}>Alterar</button> : undefined} />
        <Info label="Template configurado" value={configuration?.packageName ?? "Não configurado"} icon={<FileSearch className="h-4 w-4" />} />
      </section>

      {!ready && <p className="flex items-center gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-700">
        <AlertTriangle className="h-4 w-4" />
        {!configuration?.packageId
          ? <span>Escolha o template na coluna “Template Auditoria” de <Link className="underline" to="/planejamento/configuracao-do-projeto">Configuração do Projeto</Link>{!configuration?.auditFolderId && " e a pasta auditada acima"} antes de executar.</span>
          : <span>Escolha a pasta auditada (Pasta configurada → Alterar) antes de executar.</span>}
      </p>}
      {running && <Progresso run={run} />}
      {error && <p className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 p-4 text-sm text-destructive"><AlertTriangle className="h-4 w-4" />{error}</p>}

      {result && <>
        <section className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-6">
          <Metric label="Total" value={resumo.total} />
          <Metric label="Conformes" value={resumo.compliant} tone="text-emerald-600" />
          <Metric label="Divergências" value={resumo.divergent} tone="text-amber-600" />
          <Metric label="Fora da LDP" value={resumo.outsideLdp} tone="text-rose-600" />
          {/* Cards executivos: NÃO IDENTIFICADO saiu daqui (continua como status e filtro); NÃO CALIBRADO é categoria própria. */}
          <Metric label="Não calibrados" value={resumo.notCalibrated} tone="text-sky-600" frame="border-sky-500/30 bg-sky-500/10" />
          {/* Categoria própria (laranja do badge ERRO DE LEITURA — distinto do âmbar de DIVERGÊNCIA); não soma em nenhuma outra. */}
          <Metric label="Erro de leitura" value={resumo.readErrors} tone="text-orange-600" frame="border-orange-500/30 bg-orange-500/10" />
        </section>
        <p className="text-xs text-muted-foreground">
          Execução de {new Date(result.finishedAtUtc).toLocaleString("pt-BR")} · {result.folder} · template {result.packageName ?? result.packageId}
        </p>
        {!result.auditPersistenceSucceeded && <PersistenciaFalhou erro={result.auditPersistenceError} />}
        <section className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-2">
              {/* Disciplina: lista vinda dos próprios resultados (Discipline da LDP), sem lista fixa. */}
              <select aria-label="Disciplina" value={disciplina} onChange={(e) => setDisciplina(e.target.value)}
                className="h-9 rounded-md border border-border bg-background px-2 text-xs font-bold uppercase">
                <option value={TODAS}>Todas as disciplinas</option>
                {disciplinas.map((d) => <option key={d} value={d}>{d === SEM_DISCIPLINA ? "Sem disciplina (não identificado)" : d}</option>)}
              </select>
              {["TODOS", "CONFORME", "DIVERGÊNCIA", "FORA DA LDP", "NÃO IDENTIFICADO", "AMBÍGUO", "DUPLICADO", "ERRO DE LEITURA", "NÃO CALIBRADO"].map((status) =>
                <Button key={status} size="sm" variant={filter === status ? "default" : "outline"} onClick={() => setFilter(status)}>{status}</Button>)}
            </div>
            <a href={reportUrl(projetoAtual.hubId, projetoAtual.projectId, result.executionId)}><Button variant="outline" className="gap-2"><Download className="h-4 w-4" />Exportar CSV</Button></a>
          </div>
          <div className="overflow-x-auto rounded-xl border border-border bg-card/60">
            {/* Cabe no container do desktop (sem largura mínima exagerada): Arquivo/Documento compactos, Motivo fica com o restante,
                Detalhe é só um ícone. Em tela realmente pequena, rola na horizontal. */}
            <table className="w-full min-w-[720px] table-fixed text-sm">
              <colgroup><col className="w-[17%]" /><col className="w-[15%]" /><col className="w-[9%]" /><col className="w-[7%]" /><col className="w-[118px]" /><col /><col className="w-[40px]" /></colgroup>
              <thead className="bg-secondary text-left text-[10px] uppercase text-muted-foreground"><tr>
                <th className="px-2 py-2">Arquivo</th><th className="px-2 py-2">Documento</th><th className="px-2 py-2">Disciplina</th>
                <th className="px-2 py-2">Revisão</th><th className="px-2 py-2">Status</th><th className="px-2 py-2">Motivo</th><th className="px-1 py-2"><span className="sr-only">Detalhe</span></th>
              </tr></thead>
              <tbody>{documents.map((document) => <tr key={document.fileName} className="border-t border-border/70">
                {/* Nome/número do documento nunca quebra linha: uma linha só, truncado com o valor inteiro no title (o Detalhe mostra completo). */}
                <td className="whitespace-nowrap px-2 py-2 text-[11px] font-semibold"><span className="block truncate" title={document.fileName}>{document.fileName}</span></td>
                <td className="whitespace-nowrap px-2 py-2 text-[11px]"><span className="block truncate" title={document.document ?? undefined}>{document.document ?? "—"}</span></td>
                <td className="whitespace-nowrap px-2 py-2 text-[11px]"><span className="block truncate" title={document.discipline ?? undefined}>{document.discipline ?? "—"}</span></td>
                <td className="whitespace-nowrap px-2 py-2 text-[11px]"><span className="block truncate" title={document.revisionFound ?? undefined}>{document.revisionFound ?? "—"}</span></td>
                <td className="px-2 py-2"><Status value={document.status} /></td>
                <td className="px-2 py-2 text-[11px] text-muted-foreground"><span className="line-clamp-2" title={document.reason}>{document.reason}</span></td>
                <td className="px-1 py-2 text-right"><Button variant="ghost" size="icon" className="h-7 w-7" title="Detalhe" aria-label={`Detalhe de ${document.fileName}`} onClick={() => setSelected(document)}><Search className="h-3.5 w-3.5" /></Button></td>
              </tr>)}</tbody>
            </table>
            {documents.length === 0 && <p className="p-8 text-center text-sm text-muted-foreground">Nenhum documento neste filtro.</p>}
          </div>
        </section>
      </>}

      {escolhendoPasta && <NavegadorPastas hubId={projetoAtual.hubId} projectId={projetoAtual.projectId} titulo="Pasta auditada" acao="Auditar esta pasta"
        onCancelar={() => setEscolhendoPasta(false)} onEscolher={(folderId, caminho) => void salvarPasta(folderId, caminho)} />}

      {selected && <Dialog open onOpenChange={(open) => { if (!open) setSelected(null); }}><DialogContent className="max-w-3xl">
        <DialogHeader><DialogTitle>{selected.fileName}</DialogTitle></DialogHeader>
        <p className="flex items-center gap-2 text-sm"><Status value={selected.status} />{selected.reason}</p>
        <p className="text-xs text-muted-foreground">
          Documento: <strong className="text-foreground">{selected.document ?? "—"}</strong> · Identificado por: {selected.identifiedBy ?? "não identificado"}
          {selected.discipline && <> · Disciplina (LDP): {selected.discipline}</>}
        </p>
        <p className="text-[11px] text-muted-foreground">
          Format LDP: <strong className="text-foreground">{selected.formatLdp ?? "—"}</strong> · Perfil utilizado: <strong className="text-foreground">{selected.sheetType && selected.sheetType !== "—" ? selected.sheetType : "nenhum"}</strong>
          {selected.sheetSource && <> · Origem: {selected.sheetSource}</>}
        </p>
        <div className="max-h-[60vh] overflow-auto rounded-lg border border-border">
          <table className="w-full text-sm"><thead className="bg-secondary text-left text-xs uppercase text-muted-foreground"><tr>
            <th className="px-4 py-2">Campo</th><th className="px-4 py-2">LDP</th><th className="px-4 py-2" title="Valor bruto lido na região do template (diagnóstico)">PDF (valor bruto lido)</th><th className="px-4 py-2">Status</th>
          </tr></thead><tbody>{selected.fields.map((field) => <tr key={field.field} className="border-t border-border">
            <td className="px-4 py-2 font-bold">{field.label}</td><td className="px-4 py-2">{field.expected ?? "—"}</td><td className="whitespace-pre-line px-4 py-2 font-mono text-xs">{field.found ?? "—"}
              {/* Diagnóstico discreto: como os dois valores foram comparados (após normalização segura). */}
              {field.foundNormalized != null && field.foundNormalized !== field.found && <span className="mt-1 block text-[10px] text-muted-foreground">comparado como: {field.foundNormalized}</span>}
              {field.status !== "OK" && field.expectedNormalized && field.expectedNormalized !== field.expected && <span className="block text-[10px] text-muted-foreground">LDP comparada como: {field.expectedNormalized}</span>}
            </td>
            <td className="px-4 py-2"><span className={field.status === "OK" ? "text-emerald-600" : field.status === "DIVERGENTE" ? "text-amber-600" : field.status === "NÃO CALIBRADO" ? "text-sky-600" : "text-orange-600"} title={field.reason ?? undefined}>{field.status}</span></td>
          </tr>)}</tbody></table>
        </div>
      </DialogContent></Dialog>}
    </div>
  );
}

function Progresso({ run }: { run: RunStatus | null }) {
  const total = run?.total ?? null;
  const processados = run?.processed ?? 0;
  const pct = total ? Math.round((processados / total) * 100) : 0;
  const aguardando = !run || run.status === "QUEUED";
  return (
    <section role="status" aria-live="polite" className="space-y-3 rounded-xl border border-primary/30 bg-primary/5 p-5">
      <p className="flex items-center gap-2 text-xs font-black uppercase tracking-widest text-primary"><Loader2 className="h-4 w-4 animate-spin" />Auditoria em execução</p>
      {aguardando ? <p className="text-sm text-muted-foreground">Na fila do servidor — começa assim que a auditoria anterior terminar.</p>
        : total === null ? <p className="text-sm text-muted-foreground">Listando os PDFs da pasta no ACC…</p>
        : <>
          <p className="text-2xl font-black tabular-nums">{processados} / {total} <span className="text-sm font-bold text-muted-foreground">documentos</span></p>
          <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={total} aria-valuenow={processados}>
            <div className="h-full bg-primary transition-all" style={{ width: `${pct}%` }} />
          </div>
          <p className="truncate text-sm"><span className="text-muted-foreground">Documento atual: </span>
            <strong>{run?.currentFile ?? (processados >= total ? "gravando resultados e relatório…" : "—")}</strong></p>
        </>}
      <p className="text-xs text-muted-foreground">Você pode sair desta tela: a auditoria continua no servidor e o andamento é retomado ao voltar.</p>
    </section>
  );
}

function Info({ label, value, icon, action }: { label: string; value: string; icon: React.ReactNode; action?: React.ReactNode }) {
  return <div className="rounded-xl border border-border bg-card/60 p-4"><p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-widest text-muted-foreground">{icon}<span className="flex-1">{label}</span>{action}</p><p className="mt-2 truncate text-sm font-bold" title={value}>{value}</p></div>;
}
function Metric({ label, value, tone = "text-foreground", frame = "border-border bg-card/60" }: { label: string; value: number; tone?: string; frame?: string }) {
  return <div className={`rounded-xl border p-4 ${frame}`}><p className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">{label}</p><p className={`mt-1 text-3xl font-black ${tone}`}>{value}</p></div>;
}

function Status({ value }: { value: string }) {
  return <span className={`inline-flex whitespace-nowrap rounded-full border px-1.5 py-0.5 text-[9px] font-black uppercase ${statusClass[value] ?? statusClass["NÃO IDENTIFICADO"]}`}>{value}</span>;
}

/** Falha ao gravar no histórico central (OperationalAudit): resumo compacto; o erro técnico só sob demanda. */
function PersistenciaFalhou({ erro }: { erro: string | null }) {
  const [aberto, setAberto] = useState(false);
  return (
    <div className="text-xs text-amber-600">
      <span className="inline-flex items-center gap-1.5 rounded-md border border-amber-500/30 bg-amber-500/10 px-2 py-1 font-bold">
        <AlertTriangle className="h-3.5 w-3.5" />Histórico central não persistido
        <button type="button" className="underline underline-offset-2" onClick={() => setAberto((v) => !v)}>{aberto ? "Ocultar detalhes" : "Ver detalhes"}</button>
      </span>
      {aberto && <p className="mt-1 max-h-24 max-w-3xl overflow-auto break-words rounded-md bg-muted/60 p-2 font-mono text-[10px] text-muted-foreground">{erro ?? "Sem detalhe."}</p>}
    </div>
  );
}
