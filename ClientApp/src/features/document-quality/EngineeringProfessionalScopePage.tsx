import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { formatCurrency, formatHH } from "@/components/pmo/pmoData";
import { AlertTriangle, CheckCircle2, RotateCcw, Search, UserPlus, Users } from "lucide-react";

interface ProfessionalRow {
  name: string;
  hours: number;
  missingCostHours: number;
  knownCost: number;
  entries: number;
  costStatus: string;
  excluded: boolean;
}
interface Overview {
  projectCode: string;
  items: ProfessionalRow[];
  hours: number;
  missingCostHours: number;
  excludedHours: number;
  excludedKnownCost: number;
}

const api = "/api/production-audit/engineering-professionals";
const labelStatus: Record<string, string> = {
  SEM_CADASTRO: "Sem cadastro de custo",
  SEM_VIGENCIA: "Sem vigência de custo",
  PROFISSIONAL_AMBIGUO: "Cadastro ambíguo",
  SEM_DATA: "Sem data do lançamento",
  CUSTO_INVALIDO: "Custo inválido",
  VIGENCIA_SOBREPOSTA: "Vigências sobrepostas",
  SEM_NOME: "Sem nome",
  VIGENTE_NA_DATA: "Custo reconhecido",
  SEM_LANCAMENTOS: "Adicionado manualmente",
};

export default function EngineeringProfessionalScopePage() {
  const { projetoAtual } = useProjetoAtual();
  const projectId = projetoAtual?.projectId;
  const { can } = useProjectPermissions(projectId);
  const canEdit = can("quality.professionals.edit");
  const [data, setData] = useState<Overview | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [selectedPerson, setSelectedPerson] = useState("");
  const [newExcluded, setNewExcluded] = useState(true);
  const [filter, setFilter] = useState<"missing" | "all" | "excluded">("missing");
  const [search, setSearch] = useState("");

  const load = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) return;
    setLoading(true);
    try {
      const response = await fetch(`${api}?projectId=${encodeURIComponent(projectId)}`,
        { credentials: "include", signal });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Não foi possível consultar os profissionais.");
      if (!signal?.aborted) { setData(body as Overview); setError(null); }
    } catch (e) {
      if (!signal?.aborted) setError(e instanceof Error ? e.message : "Erro ao consultar os profissionais.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    setData(null); setError(null); setMessage("");
    const abort = new AbortController();
    void load(abort.signal);
    return () => abort.abort();
  }, [load]);

  const save = async (personName: string, excluded: boolean) => {
    if (!projectId || busy || !canEdit) return;
    setBusy(personName); setError(null); setMessage("");
    try {
      const response = await fetch(`${api}?projectId=${encodeURIComponent(projectId)}`, {
        method: "PUT", credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: personName, excludeEngineering: excluded }),
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || "Não foi possível salvar a classificação.");
      setMessage(`${personName}: ${excluded ? "Gestão — fora da engenharia" : "Engenharia — incluído"}.`);
      setName("");
      setSelectedPerson("");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao salvar.");
    } finally { setBusy(null); }
  };

  const add = (e: FormEvent) => {
    e.preventDefault();
    const chosen = selectedPerson === "__manual" ? name.trim() : selectedPerson.trim();
    if (chosen) void save(chosen, newExcluded);
  };

  // Sugestões geradas dos profissionais que realmente têm HH no projeto.
  const suggestedPeople = useMemo(() => (data?.items ?? [])
    .filter(p => p.entries > 0)
    .sort((a, b) => a.name.localeCompare(b.name, "pt-BR")), [data]);

  const rows = useMemo(() => (data?.items ?? []).filter((p) => {
    if (filter === "missing" && (p.missingCostHours <= 0 || p.excluded)) return false;
    if (filter === "excluded" && !p.excluded) return false;
    return p.name.toLocaleLowerCase("pt-BR").includes(search.trim().toLocaleLowerCase("pt-BR"));
  }), [data, filter, search]);
  const missingEngineeringHours = (data?.items ?? []).filter(p => !p.excluded)
    .reduce((sum, p) => sum + p.missingCostHours, 0);

  return (
    <div className="space-y-5">
      <header>
        <p className="text-xs font-bold uppercase tracking-widest text-muted-foreground">Qualidade · Timesheet</p>
        <h1 className="mt-1 text-xl font-black">Classificação de profissionais — Engenharia e Gestão</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Decida quais lançamentos entram nos indicadores de engenharia. Pessoas classificadas como Gestão
          ficam fora das somas de HH e custo do Comparativo de HH e Custos, sem alterar o Timesheet ou a LDP.
        </p>
        <p className="mt-2 text-xs text-muted-foreground">
          Projeto: <strong className="text-foreground">{projetoAtual?.nome || data?.projectCode || "Nenhum selecionado"}</strong>
        </p>
      </header>

      {!projectId && <p className="rounded-xl border border-amber-500/30 p-4 text-sm">Selecione um projeto na Home para classificar os profissionais.</p>}
      {projectId && <>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="grid flex-1 gap-3 sm:grid-cols-3">
            <Metric title="HH sem custo (Engenharia)" value={formatHH(missingEngineeringHours)} />
            <Metric title="HH retiradas da Engenharia" value={formatHH(data?.excludedHours ?? 0)} />
            <Metric title="Custo retirado da Engenharia" value={formatCurrency(data?.excludedKnownCost ?? 0)} />
          </div>
          <button type="button" onClick={() => void load()} disabled={loading || !!busy}
            className="inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-bold hover:bg-accent disabled:opacity-50">
            <RotateCcw size={14} /> Atualizar
          </button>
        </div>

        {canEdit && <form onSubmit={add} className="rounded-xl border border-border bg-card/60 p-4">
          <div className="mb-3 flex items-center gap-2 text-sm font-bold"><UserPlus size={16} /> Classificar profissional</div>
          <div className="flex flex-wrap items-center gap-2">
            <select aria-label="Selecione um profissional" value={selectedPerson}
              onChange={e => { setSelectedPerson(e.target.value); setName(""); }}
              className="min-w-[240px] flex-1 rounded-lg border border-border bg-background px-3 py-2 text-sm">
              <option value="">Selecione um profissional do Timesheet...</option>
              <optgroup label="Com HH sem custo">
                {suggestedPeople.filter(p => p.missingCostHours > 0).map(p =>
                  <option key={p.name} value={p.name}>{p.name} · {formatHH(p.missingCostHours)} HH sem custo</option>)}
              </optgroup>
              <optgroup label="Demais profissionais com HH">
                {suggestedPeople.filter(p => p.missingCostHours <= 0).map(p =>
                  <option key={p.name} value={p.name}>{p.name}</option>)}
              </optgroup>
              <option value="__manual">Outro nome (digitar manualmente)...</option>
            </select>
            {selectedPerson === "__manual" && <input aria-label="Nome do profissional"
              value={name} onChange={e => setName(e.target.value)} maxLength={250}
              placeholder="Nome exatamente como aparece no Timesheet"
              className="min-w-[220px] flex-1 rounded-lg border border-border bg-background px-3 py-2 text-sm" />}
            <select aria-label="Classificação" value={newExcluded ? "gestao" : "engenharia"}
              onChange={e => setNewExcluded(e.target.value === "gestao")}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm">
              <option value="gestao">Gestão — não computar</option>
              <option value="engenharia">Engenharia — computar</option>
            </select>
            <button type="submit" disabled={!selectedPerson || (selectedPerson === "__manual" && !name.trim()) || !!busy}
              className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-primary-foreground disabled:opacity-50">
              Salvar classificação
            </button>
          </div>
          <p className="mt-2 text-xs text-muted-foreground">As sugestões vêm das HH lançadas no projeto. Também é possível informar outros nomes.</p>
        </form>}

        {error && <p role="alert" className="rounded-lg border border-red-500/30 px-3 py-2 text-sm text-red-600"><AlertTriangle className="mr-2 inline" size={15} />{error}</p>}
        {message && <p role="status" className="rounded-lg border border-emerald-500/30 px-3 py-2 text-sm"><CheckCircle2 size={15} className="mr-2 inline text-emerald-500" />{message}</p>}

        <section className="rounded-xl border border-border bg-card/60 p-4">
          <div className="mb-4 flex flex-wrap items-center gap-3">
            <Users size={17} />
            <h2 className="font-bold">Profissionais e lançamentos</h2>
            <select aria-label="Filtro de profissionais" value={filter} onChange={e => setFilter(e.target.value as typeof filter)}
              className="ml-auto rounded-lg border border-border bg-background px-3 py-2 text-xs">
              <option value="missing">HH sem custo — Engenharia</option>
              <option value="all">Todos os profissionais</option>
              <option value="excluded">Classificados como Gestão</option>
            </select>
            <div className="relative">
              <Search size={14} className="absolute left-2 top-2.5 text-muted-foreground" />
              <input value={search} onChange={e => setSearch(e.target.value)}
                placeholder="Buscar nome" className="w-44 rounded-lg border border-border bg-background py-2 pl-8 pr-2 text-xs" />
            </div>
          </div>
          {loading && <p className="text-sm text-muted-foreground">Carregando lançamentos...</p>}
          {!loading && rows.length === 0 && <p className="text-sm text-muted-foreground">Nenhum profissional encontrado para o filtro.</p>}
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-left text-sm">
              <thead><tr className="border-b text-xs text-muted-foreground">
                <th className="py-3 pr-3">Profissional</th><th className="py-3 pr-3 text-right">HH lançadas</th>
                <th className="py-3 pr-3 text-right">HH sem custo</th><th className="py-3 pr-3 text-right">Custo conhecido</th>
                <th className="py-3 pr-3">Situação do custo</th><th className="py-3">Classificação</th>
              </tr></thead>
              <tbody>{rows.map(p => <tr key={p.name} className="border-b border-border/60">
                <td className="py-3 pr-3 font-semibold">{p.name}<span className="block text-[11px] font-normal text-muted-foreground">{p.entries} lançamento(s)</span></td>
                <td className="py-3 pr-3 text-right tabular-nums">{formatHH(p.hours)}</td>
                <td className="py-3 pr-3 text-right tabular-nums">{formatHH(p.missingCostHours)}</td>
                <td className="py-3 pr-3 text-right tabular-nums">{formatCurrency(p.knownCost)}</td>
                <td className="py-3 pr-3 text-xs">{labelStatus[p.costStatus] || p.costStatus}</td>
                <td className="py-3">
                  {canEdit && <button type="button" disabled={!!busy}
                    title={p.excluded ? "Incluir HH e custo na engenharia" : "Excluir HH e custo da engenharia"}
                    onClick={() => void save(p.name, !p.excluded)}
                    className={`rounded-lg border px-3 py-2 text-xs font-bold disabled:opacity-50 ${p.excluded
                      ? "border-amber-500/40 bg-amber-500/10 text-amber-600"
                      : "border-emerald-500/40 bg-emerald-500/10 text-emerald-600"}`}>
                    {busy === p.name ? "Salvando..." : p.excluded ? "Gestão · incluir na engenharia" : "Engenharia · mover para Gestão"}
                  </button>}
                  {!canEdit && <span className="text-xs">{p.excluded ? "Gestão" : "Engenharia"}</span>}
                </td>
              </tr>)}</tbody>
            </table>
          </div>
          <p className="mt-3 text-xs text-muted-foreground">
            Regras por projeto. Nenhum profissional é excluído automaticamente por não possuir custo.
            Pessoas adicionadas manualmente sem lançamento não alteram totais até que surjam HH.
          </p>
        </section>
      </>}
    </div>
  );
}

function Metric({ title, value }: { title: string; value: string }) {
  return <div className="rounded-xl border border-border bg-card/60 p-3">
    <p className="text-[11px] uppercase tracking-wide text-muted-foreground">{title}</p>
    <p className="mt-1 text-lg font-black tabular-nums">{value}</p>
  </div>;
}
