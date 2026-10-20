// Cópia de configuração de custos do projeto atual para outros projetos (prévia obrigatória antes de gravar).
// Destinos vêm de projetosElegiveisCustos: apenas projetos ativos ou sincronizados ao menos uma vez.
import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Loader2, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { custosApi, type CopyBatchInput, type CopyPreview, type ProjetoElegivelCusto } from "./costsApi";
import { CampoTexto, SelosProjeto, mensagemDe } from "./CustosShared";

type Selecao = { professionals: boolean; professionalComposition: boolean; projectTaxes: boolean; packages: boolean; expenses: boolean };

const OPCOES: readonly (readonly [keyof Selecao, string])[] = [
  ["professionals", "Profissionais"],
  ["professionalComposition", "Composição e vigências"],
  ["projectTaxes", "Impostos do projeto"],
  ["packages", "Pacotes"],
  ["expenses", "Despesas"],
];

export default function ProjectCostCopyPanel({ sourceProjectId }: { sourceProjectId: string }) {
  const [open, setOpen] = useState(false);
  const [projects, setProjects] = useState<ProjetoElegivelCusto[] | null>(null);
  const [destinations, setDestinations] = useState<string[]>([]);
  const [search, setSearch] = useState("");
  const [previews, setPreviews] = useState<CopyPreview[]>([]);
  const [skipConflicts, setSkipConflicts] = useState(false);
  const [busy, setBusy] = useState(false);
  const [selection, setSelection] = useState<Selecao>({
    professionals: true, professionalComposition: true, projectTaxes: true, packages: true, expenses: true,
  });

  useEffect(() => {
    if (!open) return;
    let vivo = true;
    setProjects(null);
    custosApi.projetosElegiveisCustos()
      .then((rows) => { if (vivo) setProjects(rows.filter((p) => p.projectIdAcc !== sourceProjectId)); })
      .catch((e) => { if (vivo) { setProjects([]); toast.error(mensagemDe(e)); } });
    return () => { vivo = false; };
  }, [open, sourceProjectId]);

  // Mudar de projeto de origem invalida destinos e prévia.
  useEffect(() => { setDestinations([]); setPreviews([]); }, [sourceProjectId]);

  const filtered = useMemo(() => {
    const q = search.trim().toLocaleLowerCase("pt-BR");
    return (projects ?? []).filter((p) => !q || p.name.toLocaleLowerCase("pt-BR").includes(q) || p.projectIdAcc.toLowerCase().includes(q));
  }, [projects, search]);

  const nomeProjeto = (id: string) => projects?.find((p) => p.projectIdAcc === id)?.name ?? id;
  const algoSelecionado = selection.professionals || selection.projectTaxes || selection.packages || selection.expenses;
  const temConflitos = previews.some((p) => p.conflicts.length > 0);

  const input = (destinationProjectIds: string[]): CopyBatchInput => ({
    ...selection,
    sourceProjectId,
    destinationProjectIds,
    professionalComposition: selection.professionals && selection.professionalComposition,
    skipConflicts,
  });

  const alternarDestino = (id: string, checked: boolean) => {
    setDestinations((d) => (checked ? [...d, id] : d.filter((x) => x !== id)));
    setPreviews([]);
  };

  const preview = async () => {
    if (!destinations.length) { toast.error("Selecione ao menos um destino."); return; }
    setBusy(true);
    try {
      const result = await custosApi.previaCopiaLote(input(destinations));
      setPreviews(result.destinations);
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(false);
    }
  };

  const commit = async () => {
    setBusy(true);
    try {
      await custosApi.confirmarCopiaLote(input(previews.map((p) => p.destinationProjectId)));
      toast.success(`Configuração copiada atomicamente para ${previews.length} projeto(s). O destino mantém dados independentes.`);
      setPreviews([]);
      setDestinations([]);
    } catch (e) {
      toast.error(`Cópia cancelada integralmente; nenhum destino foi confirmado. ${mensagemDe(e)}`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="rounded-xl border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-semibold">Copiar configurações entre projetos</h3>
          <p className="text-xs text-muted-foreground">A cópia cria configuração independente; não copia realizados, reconhecimentos ou excedentes.</p>
        </div>
        <Button variant="outline" aria-expanded={open} disabled={busy} onClick={() => { setOpen((x) => !x); setPreviews([]); }}>
          {open ? "Fechar" : "Preparar cópia"}
        </Button>
      </div>

      {open && (
        <div className="mt-3 space-y-3">
          <fieldset className="grid gap-2 text-xs sm:grid-cols-2">
            <legend className="mb-2 font-semibold">O que copiar</legend>
            {OPCOES.map(([key, label]) => (
              <label key={key} className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={selection[key]}
                  disabled={key === "professionalComposition" && !selection.professionals}
                  onChange={(e) => { setSelection((s) => ({ ...s, [key]: e.target.checked })); setPreviews([]); }}
                />
                {label}
              </label>
            ))}
          </fieldset>

          <fieldset className="space-y-2">
            <legend className="text-xs font-semibold">Destinos ({destinations.length} selecionado(s))</legend>
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <CampoTexto className="pl-8" placeholder="Buscar projeto…" aria-label="Buscar projeto destino da cópia" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <div className="max-h-64 overflow-y-auto rounded-lg border">
              {projects === null ? (
                <p className="flex items-center gap-2 p-3 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando projetos…</p>
              ) : filtered.length === 0 ? (
                <p className="p-3 text-sm text-muted-foreground">Nenhum projeto elegível{search.trim() ? " para a busca" : ""}.</p>
              ) : filtered.map((p) => (
                <label key={p.projectIdAcc} className="flex cursor-pointer items-center gap-3 border-b px-3 py-2 text-sm last:border-b-0 hover:bg-accent/30">
                  <input type="checkbox" checked={destinations.includes(p.projectIdAcc)} onChange={(e) => alternarDestino(p.projectIdAcc, e.target.checked)} />
                  <span className="min-w-0 flex-1">
                    <b className="block truncate">{p.name}</b>
                    <span className="text-[10px] text-muted-foreground">{p.projectIdAcc}</span>
                  </span>
                  <SelosProjeto projeto={p} />
                </label>
              ))}
            </div>
          </fieldset>

          <label className="flex items-center gap-2 text-xs">
            <input type="checkbox" checked={skipConflicts} onChange={(e) => { setSkipConflicts(e.target.checked); setPreviews([]); }} />
            Ignorar por categoria quando o destino já tiver configurações (sem sobrescrever)
          </label>

          <Button disabled={busy || !algoSelecionado || destinations.length === 0} onClick={preview} className="gap-1.5">
            {busy && previews.length === 0 && <Loader2 className="h-4 w-4 animate-spin" />}Gerar prévia
          </Button>

          {previews.length > 0 && (
            <div className="space-y-2 rounded border p-3" role="status">
              <h4 className="font-medium">Prévia — nenhuma gravação foi feita</h4>
              {previews.map((p) => (
                <div key={p.destinationProjectId} className="text-sm">
                  <b>{nomeProjeto(p.destinationProjectId)}</b>: criar {p.professionalsToCreate} profissionais, {p.taxDefinitionsToCreate} impostos,
                  {" "}{p.packagesToCreate} pacotes, {p.expensesToCreate} despesas. Conflitos: {p.conflicts.length ? p.conflicts.join(", ") : "nenhum"}.
                </div>
              ))}
              <Button disabled={busy || (!skipConflicts && temConflitos)} onClick={commit} className="gap-1.5">
                {busy && <Loader2 className="h-4 w-4 animate-spin" />}Confirmar cópia ({previews.length})
              </Button>
              {!skipConflicts && temConflitos && (
                <p className="text-xs text-amber-600">A cópia está bloqueada até resolver os conflitos ou marcar “Ignorar por categoria” e gerar a prévia novamente.</p>
              )}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
