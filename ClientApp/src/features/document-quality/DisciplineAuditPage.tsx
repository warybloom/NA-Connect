import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, Languages, Pencil, Plus, RefreshCw, Search, Shuffle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import {
  assignDisciplineAlias,
  createDisciplineStandard,
  getDisciplineAudit,
  getDisciplineStandards,
  updateDisciplineStandard,
  type DisciplineAuditResult,
  type DisciplineAuditRow,
  type DisciplineStandard,
} from "./documentQualityApi";

type Scope = "project" | "all";
type StandardDraft = { standardName: string; portugueseName: string; englishName: string };
/** create: nova disciplina global (opcionalmente já direcionando um nome da Norma). edit: renomear Nome padrão/PT/EN. */
type StandardModal =
  | { mode: "create"; raw: string | null; draft: StandardDraft }
  | { mode: "edit"; standardId: number; originalName: string; draft: StandardDraft };

export default function DisciplineAuditPage() {
  const { projetoAtual } = useProjetoAtual();
  const [scope, setScope] = useState<Scope>("all");
  const [data, setData] = useState<DisciplineAuditResult | null>(null);
  const [standards, setStandards] = useState<DisciplineStandard[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [status, setStatus] = useState("TODOS");
  const [projetoFiltro, setProjetoFiltro] = useState("TODOS");
  const [busca, setBusca] = useState("");
  const [selectedStandard, setSelectedStandard] = useState<Record<string, number>>({});
  const [redirecting, setRedirecting] = useState<Record<string, boolean>>({});
  const [modal, setModal] = useState<StandardModal | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    if (scope === "project" && !projetoAtual?.projectId) return;
    setLoading(true);
    setError(null);
    try {
      const [audit, catalog] = await Promise.all([
        getDisciplineAudit(projetoAtual?.projectId ?? "", scope, signal),
        getDisciplineStandards(signal),
      ]);
      setData(audit);
      setStandards(catalog.filter((x) => x.active).sort((a, b) => a.standardName.localeCompare(b.standardName, "pt-BR")));
      setSelectedStandard({});
      setRedirecting({});
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") return;
      setError(e instanceof Error ? e.message : "Falha ao auditar disciplinas.");
    } finally {
      setLoading(false);
    }
  }, [projetoAtual?.projectId, scope]);

  useEffect(() => {
    if (scope === "project" && !projetoAtual?.projectId) return;
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, projetoAtual?.projectId]);

  const projetos = useMemo(() =>
    [...new Set((data?.rows ?? []).flatMap((x) => x.projects))]
      .sort((a, b) => a.localeCompare(b, "pt-BR")),
  [data]);

  const rows = useMemo(() => {
    const q = busca.trim().toUpperCase();
    return (data?.rows ?? []).filter((x) => {
      if (status !== "TODOS" && x.status !== status) return false;
      if (projetoFiltro !== "TODOS" && !x.projects.includes(projetoFiltro)) return false;
      if (!q) return true;
      return [
        x.rawDiscipline,
        x.standardName,
        x.standardPt,
        x.standardEn,
        ...x.projects,
      ].some((v) => (v ?? "").toUpperCase().includes(q));
    });
  }, [data, status, projetoFiltro, busca]);

  // Quantos nomes de Norma (na auditoria atual) estão direcionados a cada padrão — usado no aviso de edição.
  const namesPerStandard = useMemo(() => {
    const m = new Map<number, number>();
    for (const r of data?.rows ?? [])
      if (r.disciplineStandardId) m.set(r.disciplineStandardId, (m.get(r.disciplineStandardId) ?? 0) + 1);
    return m;
  }, [data]);

  if (!projetoAtual && scope === "project")
    return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início ou use "Todos os projetos".</ReportState>;

  const direcionar = async (row: DisciplineAuditRow) => {
    const raw = row.rawDiscipline?.trim();
    const standardId = raw ? selectedStandard[raw] : undefined;
    if (!raw || !standardId) return;

    setSaving(raw);
    setError(null);
    setMessage(null);
    try {
      await assignDisciplineAlias(raw, standardId);
      const alvo = standards.find((s) => s.disciplineStandardId === standardId)?.standardName ?? "";
      setMessage("\"" + raw + "\" direcionado para " + alvo + ".");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao direcionar a disciplina.");
    } finally {
      setSaving(null);
    }
  };

  const abrirCriacao = (raw: string | null) =>
    setModal({ mode: "create", raw, draft: { standardName: raw?.trim().toUpperCase() ?? "", portugueseName: "", englishName: "" } });

  const abrirEdicao = (row: DisciplineAuditRow) => {
    if (!row.disciplineStandardId) return;
    const s = standards.find((x) => x.disciplineStandardId === row.disciplineStandardId);
    setModal({
      mode: "edit",
      standardId: row.disciplineStandardId,
      originalName: s?.standardName ?? row.standardName ?? "",
      draft: {
        standardName: s?.standardName ?? row.standardName ?? "",
        portugueseName: s?.portugueseName ?? row.standardPt ?? "",
        englishName: s?.englishName ?? row.standardEn ?? "",
      },
    });
  };

  return (
    <div className="space-y-5 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <Languages className="h-7 w-7 text-primary" />
        <div className="min-w-0 flex-1">
          <p className="text-[10px] font-black uppercase tracking-[0.22em] text-muted-foreground">Qualidade</p>
          <h1 className="text-xl font-black">Nomes das Disciplinas</h1>
          <p className="text-sm text-muted-foreground">
            Os nomes abaixo vêm das Normas de Coordenação. As siglas continuam sendo gerenciadas dentro de cada Norma. Nome padrão é a chave
            global; PT/EN são exibição.
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            Direcione cada nome encontrado para a disciplina global correta (ex.: PROCESS e PROCESSO → Nome padrão PROCESS, PT PROCESSO, EN PROCESS).
            A disciplina digitada na LDP não é fonte desta tela.
          </p>
        </div>

        <select
          className="h-9 rounded-md border border-input bg-background px-3 text-sm"
          value={scope}
          onChange={(e) => {
            setScope(e.target.value as Scope);
            setProjetoFiltro("TODOS");
          }}
        >
          <option value="all">Todos os projetos</option>
          <option value="project">Somente projeto atual</option>
        </select>

        <Button variant="outline" className="gap-2" onClick={() => void load()} disabled={loading}>
          <RefreshCw className={"h-4 w-4 " + (loading ? "animate-spin" : "")} />
          Atualizar
        </Button>
      </section>

      {error && <ReportState title={error} error>Revise a operação e tente novamente.</ReportState>}

      {loading && !data && (
        <ReportState title="Auditando disciplinas">
          {scope === "all" ? "Lendo os nomes usados nas Normas de Coordenação de todos os projetos ativos." : "Lendo os nomes usados na Norma de Coordenação do projeto atual."}
        </ReportState>
      )}

      {data && (
        <>
          <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
            <Card label="Projetos analisados" value={data.projectsAnalyzed} />
            <Card label="Siglas / mapeamentos" value={data.documents} />
            <Card label="Nomes únicos nas Normas" value={data.rawNames} />
            <Card label="Padronizados" value={data.standardizedNames} />
            <Card label="Não mapeados" value={data.unmappedNames} danger={data.unmappedNames > 0} />
          </section>

          <section className="rounded-xl border border-border bg-card/60 p-4 space-y-3">
            <div className="flex flex-wrap gap-3">
              <label className="relative min-w-[320px] flex-1">
                <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <input
                  value={busca}
                  onChange={(e) => setBusca(e.target.value)}
                  placeholder="Nome usado na Norma, Nome padrão, PT/EN ou projeto..."
                  className="h-9 w-full rounded-md border border-input bg-background pl-9 pr-3 text-sm"
                />
              </label>

              <select className="h-9 rounded-md border border-input bg-background px-3 text-sm" value={status} onChange={(e) => setStatus(e.target.value)}>
                <option value="TODOS">Todas as situações</option>
                <option value="PADRONIZADO">Padronizados</option>
                <option value="NAO_MAPEADO">Não mapeados</option>
              </select>

              {scope === "all" && (
                <select className="h-9 max-w-[320px] rounded-md border border-input bg-background px-3 text-sm" value={projetoFiltro} onChange={(e) => setProjetoFiltro(e.target.value)}>
                  <option value="TODOS">Todos os projetos</option>
                  {projetos.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              )}

              <Button size="sm" variant="ghost" className="h-9 gap-1 text-xs" onClick={() => abrirCriacao(null)}>
                <Plus className="h-3.5 w-3.5" />Nova disciplina global
              </Button>
            </div>

            {message && <div className="rounded-md border border-emerald-500/30 bg-emerald-500/5 px-3 py-2 text-sm">{message}</div>}

            <div className="overflow-auto rounded-lg border border-border">
              <table className="w-full min-w-[1450px] text-sm">
                <thead className="bg-muted/50 text-left text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Nome usado na Norma</th>
                    <th className="px-3 py-2">Nome padrão global</th>
                    <th className="px-3 py-2">Português</th>
                    <th className="px-3 py-2">Inglês</th>
                    <th className="px-3 py-2 text-right">Siglas / mapeamentos</th>
                    <th className="px-3 py-2">Projetos</th>
                    <th className="px-3 py-2">Situação</th>
                    <th className="px-3 py-2">Ações</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, i) => {
                    const raw = row.rawDiscipline ?? "";
                    const mapped = row.status === "PADRONIZADO" && !!row.disciplineStandardId;
                    const showPicker = !mapped || redirecting[raw];
                    const selected = selectedStandard[raw] ?? 0;
                    return (
                      <tr key={raw + "|" + i} className="border-t border-border/60 align-top">
                        <td className="px-3 py-2 font-semibold">{raw || "SEM DISCIPLINA"}</td>
                        <td className="px-3 py-2 font-semibold">{row.standardName ?? "—"}</td>
                        <td className="px-3 py-2">{row.standardPt ?? "—"}</td>
                        <td className="px-3 py-2">{row.standardEn ?? "—"}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{row.documents.toLocaleString("pt-BR")}</td>
                        <td className="max-w-[340px] px-3 py-2 text-xs text-muted-foreground">
                          <div className="font-semibold text-foreground">{row.projectsCount} projeto(s)</div>
                          <div title={row.projects.join(" · ")} className="max-h-16 overflow-hidden">{row.projects.join(" · ")}</div>
                        </td>
                        <td className="px-3 py-2">
                          {mapped ? <Badge ok text="PADRONIZADO" /> : <Badge text="NÃO MAPEADO" />}
                        </td>
                        <td className="px-3 py-2">
                          {!raw ? "—" : (
                            <div className="flex min-w-[380px] flex-col gap-1.5">
                              {mapped && !redirecting[raw] && (
                                <div className="flex gap-2">
                                  <Button size="sm" variant="outline" className="gap-1" onClick={() => abrirEdicao(row)}>
                                    <Pencil className="h-3.5 w-3.5" />Editar nome/idiomas
                                  </Button>
                                  <Button size="sm" variant="outline" className="gap-1" onClick={() => setRedirecting((r) => ({ ...r, [raw]: true }))}>
                                    <Shuffle className="h-3.5 w-3.5" />Redirecionar
                                  </Button>
                                </div>
                              )}
                              {showPicker && (
                                <div className="flex gap-2">
                                  <select
                                    className="h-8 min-w-[220px] flex-1 rounded-md border border-input bg-background px-2 text-xs"
                                    value={selected}
                                    onChange={(e) => setSelectedStandard((s) => ({ ...s, [raw]: Number(e.target.value) }))}
                                  >
                                    <option value={0}>{mapped ? "Escolha outro padrão..." : "Selecione o padrão..."}</option>
                                    {standards
                                      .filter((s) => s.disciplineStandardId !== row.disciplineStandardId)
                                      .map((s) => (
                                        <option key={s.disciplineStandardId} value={s.disciplineStandardId}>{s.standardName}</option>
                                      ))}
                                  </select>
                                  <Button size="sm" disabled={!selected || saving === raw} onClick={() => void direcionar(row)}>
                                    {saving === raw ? "Salvando..." : "Direcionar"}
                                  </Button>
                                  {mapped && (
                                    <Button size="sm" variant="ghost" disabled={saving === raw} onClick={() => setRedirecting((r) => ({ ...r, [raw]: false }))}>
                                      Cancelar
                                    </Button>
                                  )}
                                </div>
                              )}
                              {!mapped && (
                                <button
                                  type="button"
                                  className="self-start text-xs font-semibold text-primary hover:underline"
                                  onClick={() => abrirCriacao(raw)}
                                >
                                  + Criar disciplina global a partir deste nome
                                </button>
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                  {rows.length === 0 && (
                    <tr><td colSpan={8} className="px-3 py-10 text-center text-muted-foreground">Nenhuma disciplina para os filtros atuais.</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <p className="text-xs text-muted-foreground">
              Cada linha é um nome único encontrado nas Normas de Coordenação. Ao direcioná-lo para uma disciplina global, a equivalência passa a valer
              para todos os projetos.
            </p>
          </section>
        </>
      )}

      <StandardDialog
        modal={modal}
        affectedNames={modal?.mode === "edit" ? namesPerStandard.get(modal.standardId) ?? 0 : 0}
        onChange={(draft) => setModal((m) => (m ? { ...m, draft } : m))}
        onClose={() => setModal(null)}
        onSaved={async (msg) => {
          setModal(null);
          setError(null);
          setMessage(msg);
          await load();
        }}
      />
    </div>
  );
}

function StandardDialog({ modal, affectedNames, onChange, onClose, onSaved }: {
  modal: StandardModal | null;
  affectedNames: number;
  onChange: (draft: StandardDraft) => void;
  onClose: () => void;
  onSaved: (message: string) => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { setError(null); }, [modal?.mode, modal?.mode === "edit" ? modal.standardId : modal?.raw]);

  const save = async () => {
    if (!modal) return;
    const d = modal.draft;
    const standardName = d.standardName.trim();
    if (!standardName) return;
    const body = { standardName, portugueseName: d.portugueseName.trim() || null, englishName: d.englishName.trim() || null };

    setBusy(true);
    setError(null);
    try {
      if (modal.mode === "edit") {
        await updateDisciplineStandard(modal.standardId, body);
        await onSaved("\"" + standardName + "\" atualizado. Todos os nomes de Norma direcionados a ele passam a usar o novo Nome padrão/PT/EN.");
        return;
      }
      const { disciplineStandardId } = await createDisciplineStandard(body);
      if (modal.raw) {
        try {
          await assignDisciplineAlias(modal.raw, disciplineStandardId);
        } catch (e) {
          await onSaved("Disciplina global \"" + standardName + "\" criada, mas o direcionamento de \"" + modal.raw + "\" falhou: "
            + (e instanceof Error ? e.message : "erro desconhecido") + ". Direcione-o pela tabela.");
          return;
        }
        await onSaved("Disciplina global \"" + standardName + "\" criada e \"" + modal.raw + "\" direcionado para ela.");
      } else {
        await onSaved("Disciplina global \"" + standardName + "\" criada.");
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao salvar a disciplina global.");
    } finally {
      setBusy(false);
    }
  };

  const field = (key: keyof StandardDraft, label: string, placeholder: string) => (
    <label className="block space-y-1">
      <span className="text-xs font-semibold text-muted-foreground">{label}</span>
      <input
        className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
        placeholder={placeholder}
        value={modal?.draft[key] ?? ""}
        onChange={(e) => modal && onChange({ ...modal.draft, [key]: e.target.value })}
      />
    </label>
  );

  return (
    <Dialog open={!!modal} onOpenChange={(open) => { if (!open && !busy) onClose(); }}>
      <DialogContent className="sm:max-w-[480px]">
        {modal && (
          <>
            <DialogHeader>
              <DialogTitle>
                {modal.mode === "edit" ? "Editar nome/idiomas" : modal.raw ? "Criar disciplina global a partir de \"" + modal.raw + "\"" : "Nova disciplina global"}
              </DialogTitle>
              <DialogDescription>
                {modal.mode === "edit"
                  ? "Alterar \"" + modal.originalName + "\" vale para todos os projetos e para " + affectedNames + " nome(s) de Norma direcionados a este padrão."
                  : modal.raw
                    ? "A nova disciplina global será criada e \"" + modal.raw + "\" será direcionado para ela."
                    : "Depois de criada, direcione os nomes das Normas para ela pela tabela."}
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-3">
              {field("standardName", "Nome padrão (chave global)", "Ex.: PROCESS")}
              {field("portugueseName", "Exibição PT", "Ex.: PROCESSO")}
              {field("englishName", "Exibição EN", "Ex.: PROCESS")}
              {error && <div className="rounded-md border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-600">{error}</div>}
            </div>

            <DialogFooter>
              <Button variant="outline" disabled={busy} onClick={onClose}>Cancelar</Button>
              <Button disabled={busy || !modal.draft.standardName.trim()} onClick={() => void save()}>
                {busy ? "Salvando..." : modal.mode === "edit" ? "Salvar" : "Criar"}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

function Card({ label, value, danger = false }: { label: string; value: number; danger?: boolean }) {
  return (
    <div className={"rounded-xl border p-4 " + (danger ? "border-rose-500/40 bg-rose-500/5" : "border-border bg-card/60")}>
      <div className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">{label}</div>
      <div className={"mt-1 text-3xl font-black " + (danger ? "text-rose-500" : "")}>{value.toLocaleString("pt-BR")}</div>
    </div>
  );
}

function Badge({ ok = false, text }: { ok?: boolean; text: string }) {
  return ok
    ? <span className="inline-flex items-center gap-1 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-xs font-bold text-emerald-600"><CheckCircle2 className="h-3.5 w-3.5" />{text}</span>
    : <span className="inline-flex items-center gap-1 rounded-full border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-xs font-bold text-rose-600"><AlertTriangle className="h-3.5 w-3.5" />{text}</span>;
}
