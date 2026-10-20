import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, Clock3, Loader2, Package, Search, UserRound } from "lucide-react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ReportState } from "@/components/reports/ReportComponents";
import { getDisciplineStandards, type DisciplineStandard } from "@/features/document-quality/documentQualityApi";

type Row = {
  rowKey: string;
  date: string | null;
  name: string | null;
  week: string | null;
  hours: number;
  afe: string | null;
  description: string | null;
  project: string;
  function: string | null;
  discipline: string | null;
  hourlyCost: number | null;
  cost: number | null;
  costSource: "VIGENTE_NA_DATA" | null;
  costStatus?: string | null;
  assignedDiscipline: string | null;
  isPackage: boolean;
  treatmentStatus: "PENDENTE" | "AT" | string;
};

type Overview = {
  projectCode: string;
  totalBiHours: number;
  hoursWithDocumentCode: number;
  hoursWithoutDocumentCode: number;
  costWithoutDocumentCode: number;
  rowsWithoutCost: number;
  totalPositiveRows: number;
  rowsWithDocumentCode: number;
  rowsWithoutDocumentCode: number;
  scopeAmbiguous: boolean;
  scopeMessage: string | null;
  items: Row[];
};

type RoutingConfig = {
  projectCode: string;
  biProjectCode: string;
  routingMode: "PROJECT" | "PROJECT_AFE";
  afeCode: string | null;
  suggestedAfe: string | null;
  isConfigured: boolean;
  requiresConfiguration: boolean;
  candidates: { afe: string; rows: number; hours: number }[];
};

type TreatmentResult = {
  updated: number;
  treatmentType: string;
  hours: number;
  cost: number;
  rowsWithoutCost: number;
};

const hh = (value: number) =>
  new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);

const money = (value: number) =>
  new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(value);

// Motivo explícito quando o lançamento não tem custo/hora no cadastro de profissionais (nunca custo zero silencioso).
const COST_PENDING: Record<string, string> = {
  SEM_NOME: "Lançamento sem profissional",
  SEM_CADASTRO: "Profissional fora do cadastro",
  PROFISSIONAL_AMBIGUO: "Nome casa com mais de um profissional",
  SEM_DATA: "Lançamento sem data",
  SEM_VIGENCIA: "Sem custo/hora vigente na data",
  VIGENCIA_SOBREPOSTA: "Vigências sobrepostas no cadastro",
  CUSTO_INVALIDO: "Custo/hora inválido no cadastro",
};
const costPending = (row: Row) => COST_PENDING[row.costStatus ?? ""] ?? "Sem custo/hora vigente";

const date = (value: string | null) =>
  value ? new Date(value).toLocaleDateString("pt-BR", { timeZone: "UTC" }) : "—";

async function load(projectId: string, signal?: AbortSignal): Promise<Overview> {
  const response = await fetch(
    `/api/production-audit/timesheet-unassigned?projectId=${encodeURIComponent(projectId)}`,
    { credentials: "include", signal },
  );
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? "Não foi possível carregar as HH sem documento.");
  return body;
}

async function loadRouting(projectId: string, signal?: AbortSignal): Promise<RoutingConfig> {
  const response = await fetch(
    `/api/production-audit/timesheet-unassigned/routing?projectId=${encodeURIComponent(projectId)}`,
    { credentials: "include", signal },
  );
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? "Não foi possível carregar o roteamento do Timesheet.");
  return body;
}

async function saveRouting(
  projectId: string,
  body: { routingMode: "PROJECT" | "PROJECT_AFE"; afeCode?: string | null },
): Promise<RoutingConfig> {
  const response = await fetch(
    `/api/production-audit/timesheet-unassigned/routing?projectId=${encodeURIComponent(projectId)}`,
    {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(body),
    },
  );
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message ?? "Não foi possível salvar o roteamento.");
  return payload;
}

async function saveTreatment(
  projectId: string,
  body: { rowKeys: string[]; discipline?: string | null; isPackage?: boolean; treatmentType?: "PENDENTE" | "AT" },
): Promise<TreatmentResult> {
  const response = await fetch(
    `/api/production-audit/timesheet-unassigned/treatment?projectId=${encodeURIComponent(projectId)}`,
    {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(body),
    },
  );
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message ?? "Não foi possível salvar o tratamento.");
  return payload;
}

export default function TimesheetUnassignedAuditPage({ projectId, canEdit=true }: { projectId?: string; canEdit?: boolean }) {
  const [data, setData] = useState<Overview>();
  const [routing, setRouting] = useState<RoutingConfig>();
  const [routingMode, setRoutingMode] = useState<"PROJECT" | "PROJECT_AFE">("PROJECT");
  const [routingAfe, setRoutingAfe] = useState("");
  const [routingSaving, setRoutingSaving] = useState(false);
  const [disciplines, setDisciplines] = useState<DisciplineStandard[]>([]);
  const [error, setError] = useState<string>();
  const [search, setSearch] = useState("");
  const [person, setPerson] = useState("TODOS");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [savingKey, setSavingKey] = useState<string | null>(null);
  const [batchDiscipline, setBatchDiscipline] = useState("");
  const [batchPackageMode, setBatchPackageMode] = useState<"MANTER" | "SIM" | "NAO">("MANTER");
  const [previewOpen, setPreviewOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);

  const reload = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) return;
    const result = await load(projectId, signal);
    setData(result);
    setSelected((current) => {
      const valid = new Set(result.items.map((x) => x.rowKey));
      return new Set([...current].filter((x) => valid.has(x)));
    });
  }, [projectId]);

  useEffect(() => {
    setData(undefined);
    setRouting(undefined);
    setError(undefined);
    setSelected(new Set());
    if (!projectId) return;

    const controller = new AbortController();
    Promise.all([
      reload(controller.signal),
      loadRouting(projectId, controller.signal).then((x) => {
        setRouting(x);
        setRoutingMode(x.routingMode);
        setRoutingAfe(x.afeCode ?? x.suggestedAfe ?? "");
      }),
      getDisciplineStandards(controller.signal).then((x) => setDisciplines(x.filter((d) => d.active))),
    ]).catch((e) => {
      if (e instanceof DOMException && e.name === "AbortError") return;
      setError(e instanceof Error ? e.message : "Falha ao carregar.");
    });
    return () => controller.abort();
  }, [projectId, reload]);

  const people = useMemo(
    () => [...new Set((data?.items ?? []).map((x) => x.name?.trim()).filter((x): x is string => !!x))]
      .sort((a, b) => a.localeCompare(b, "pt-BR")),
    [data],
  );

  const rows = useMemo(() => {
    const q = search.trim().toLocaleLowerCase("pt-BR");
    return (data?.items ?? []).filter((row) => {
      if (person !== "TODOS" && row.name !== person) return false;
      if (!q) return true;
      return [
        row.name,
        row.week,
        row.afe,
        row.description,
        row.function,
        row.discipline,
        row.assignedDiscipline,
      ].some((value) => value?.toLocaleLowerCase("pt-BR").includes(q));
    });
  }, [data, search, person]);

  const selectedRows = useMemo(
    () => (data?.items ?? []).filter((row) => selected.has(row.rowKey)),
    [data, selected],
  );

  const filteredHours = rows.reduce((sum, row) => sum + row.hours, 0);
  const filteredCost = rows.reduce((sum, row) => sum + (row.cost ?? 0), 0);
  const selectedHours = selectedRows.reduce((sum, row) => sum + row.hours, 0);
  const selectedCost = selectedRows.reduce((sum, row) => sum + (row.cost ?? 0), 0);
  const selectedWithoutCost = selectedRows.filter((row) => row.cost == null).length;

  const disciplineOptions = useMemo(() => {
    const catalog = disciplines.map((x) => x.standardName);
    const used = (data?.items ?? [])
      .flatMap((x) => [x.assignedDiscipline, x.discipline])
      .filter((x): x is string => !!x?.trim());
    return [...new Set([...catalog, ...used])].sort((a, b) => a.localeCompare(b, "pt-BR"));
  }, [data, disciplines]);

  const allVisibleSelected = rows.length > 0 && rows.every((row) => selected.has(row.rowKey));

  const toggleVisible = (checked: boolean) => {
    setSelected((current) => {
      const next = new Set(current);
      for (const row of rows) checked ? next.add(row.rowKey) : next.delete(row.rowKey);
      return next;
    });
  };

  const patchRow = async (row: Row, patch: { discipline?: string | null; isPackage?: boolean }) => {
    if (!projectId) return;
    setSavingKey(row.rowKey);
    try {
      await saveTreatment(projectId, { rowKeys: [row.rowKey], ...patch });
      setData((current) => current ? {
        ...current,
        items: current.items.map((item) => item.rowKey === row.rowKey
          ? {
              ...item,
              assignedDiscipline: patch.discipline === undefined ? item.assignedDiscipline : patch.discipline,
              isPackage: patch.isPackage === undefined ? item.isPackage : patch.isPackage,
            }
          : item),
      } : current);
      toast.success("Tratamento atualizado.");
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao salvar.");
    } finally {
      setSavingKey(null);
    }
  };

  const applyDisciplineToSelected = async (discipline: string) => {
    setBatchDiscipline(discipline);
    if (!projectId || !selectedRows.length || !discipline) return;

    setConfirming(true);
    try {
      const result = await saveTreatment(projectId, {
        rowKeys: selectedRows.map((x) => x.rowKey),
        discipline,
      });
      setData((current) => current ? {
        ...current,
        items: current.items.map((item) =>
          selected.has(item.rowKey) ? { ...item, assignedDiscipline: discipline } : item,
        ),
      } : current);
      setBatchDiscipline("");
      toast.success(`Disciplina aplicada a ${result.updated} lançamento(s).`);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao aplicar disciplina.");
    } finally {
      setConfirming(false);
    }
  };

  const saveBatchMetadata = async () => {
    if (!projectId || !selectedRows.length) return;
    const body: { rowKeys: string[]; discipline?: string | null; isPackage?: boolean } = {
      rowKeys: selectedRows.map((x) => x.rowKey),
    };
    if (batchDiscipline) body.discipline = batchDiscipline;
    if (batchPackageMode === "SIM") body.isPackage = true;
    if (batchPackageMode === "NAO") body.isPackage = false;

    if (body.discipline === undefined && body.isPackage === undefined) {
      toast.error("Escolha uma disciplina ou uma ação de pacote para aplicar ao lote.");
      return;
    }

    setConfirming(true);
    try {
      const result = await saveTreatment(projectId, body);
      toast.success(`${result.updated} lançamento(s) atualizado(s).`);
      await reload();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao salvar lote.");
    } finally {
      setConfirming(false);
    }
  };

  const effectiveDiscipline = (row: Row) => batchDiscipline || row.assignedDiscipline || null;
  const previewGroups = useMemo(() => {
    const groups = new Map<string, { discipline: string; rows: number; hours: number; cost: number; withoutCost: number; packages: number }>();
    for (const row of selectedRows) {
      const discipline = effectiveDiscipline(row) ?? "SEM DISCIPLINA";
      const current = groups.get(discipline) ?? { discipline, rows: 0, hours: 0, cost: 0, withoutCost: 0, packages: 0 };
      current.rows++;
      current.hours += row.hours;
      current.cost += row.cost ?? 0;
      current.withoutCost += row.cost == null ? 1 : 0;
      const packageValue = batchPackageMode === "SIM" ? true : batchPackageMode === "NAO" ? false : row.isPackage;
      current.packages += packageValue ? 1 : 0;
      groups.set(discipline, current);
    }
    return [...groups.values()].sort((a, b) => a.discipline.localeCompare(b.discipline, "pt-BR"));
  }, [selectedRows, batchDiscipline, batchPackageMode]);

  const blockedWithoutDiscipline = selectedRows.filter((row) => !effectiveDiscipline(row)).length;

  const confirmAsAt = async () => {
    if (!projectId || !selectedRows.length || blockedWithoutDiscipline > 0) return;
    setConfirming(true);
    try {
      const body: { rowKeys: string[]; discipline?: string; isPackage?: boolean; treatmentType: "AT" } = {
        rowKeys: selectedRows.map((x) => x.rowKey),
        treatmentType: "AT",
      };
      if (batchDiscipline) body.discipline = batchDiscipline;
      if (batchPackageMode === "SIM") body.isPackage = true;
      if (batchPackageMode === "NAO") body.isPackage = false;

      const result = await saveTreatment(projectId, body);
      toast.success(`${result.updated} lançamento(s) confirmado(s) como AT · ${hh(result.hours)} HH.`);
      setPreviewOpen(false);
      setSelected(new Set());
      await reload();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao confirmar AT.");
    } finally {
      setConfirming(false);
    }
  };

  const saveRoutingConfig = async () => {
    if (!projectId || !routing) return;
    if (routingMode === "PROJECT_AFE" && !routingAfe.trim()) {
      toast.error("Selecione ou informe a AFE correspondente à CR-OS.");
      return;
    }

    setRoutingSaving(true);
    try {
      const result = await saveRouting(projectId, {
        routingMode,
        afeCode: routingMode === "PROJECT_AFE" ? routingAfe.trim() : null,
      });
      setRouting(result);
      setRoutingMode(result.routingMode);
      setRoutingAfe(result.afeCode ?? result.suggestedAfe ?? "");
      toast.success("Roteamento do Timesheet salvo.");
      await reload();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao salvar o roteamento.");
    } finally {
      setRoutingSaving(false);
    }
  };

  if (!projectId) return <ReportState title="Nenhum projeto selecionado">Selecione um projeto na tela Início.</ReportState>;
  if (error) return <ReportState title="Erro ao carregar HH sem documento" error>{error}</ReportState>;
  if (!data || !routing) return <ReportState title="Carregando HH sem documento">Consultando a tbTimeSheet bruta do BI.</ReportState>;
  // Leitura realmente restrita: botões e controles de alteração sequer são montados.
  if (!canEdit) return (
    <section className="space-y-3 rounded-xl border border-border bg-card p-4">
      <div>
        <h3 className="font-bold">HH sem Número do Documento</h3>
        <p className="text-xs text-muted-foreground">Consulta somente leitura · {hh(data.hoursWithoutDocumentCode)} HH · {money(data.costWithoutDocumentCode)}</p>
      </div>
      <div className="max-h-[60vh] overflow-auto">
        <table className="w-full min-w-[850px] text-left text-xs">
          <thead><tr className="border-b text-muted-foreground"><th className="p-2">Data</th><th className="p-2">Profissional</th><th className="p-2">HH</th><th className="p-2">Custo</th><th className="p-2">AFE</th><th className="p-2">Disciplina</th><th className="p-2">Classificação</th></tr></thead>
          <tbody>{data.items.map(row => <tr key={row.rowKey} className="border-b border-border/50">
            <td className="p-2">{date(row.date)}</td><td className="p-2">{row.name??"—"}</td>
            <td className="p-2">{hh(row.hours)}</td><td className="p-2">{row.cost==null?costPending(row):money(row.cost)}</td>
            <td className="p-2">{row.afe??"—"}</td><td className="p-2">{row.assignedDiscipline??row.discipline??"—"}</td>
            <td className="p-2">{row.treatmentStatus}</td>
          </tr>)}</tbody>
        </table>
      </div>
    </section>
  );

  const routingPanel = (
    <RoutingPanel
      routing={routing}
      mode={routingMode}
      afe={routingAfe}
      saving={routingSaving}
      onMode={setRoutingMode}
      onAfe={setRoutingAfe}
      onSave={() => void saveRoutingConfig()}
    />
  );

  if (data.scopeAmbiguous) {
    return (
      <div className="space-y-4">
        {routingPanel}
        <div className="rounded-xl border border-amber-500/40 bg-amber-500/5 p-5">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
            <div>
              <h3 className="font-bold">Roteamento obrigatório antes de carregar as HH</h3>
              <p className="mt-1 text-sm text-muted-foreground">{data.scopeMessage}</p>
              <p className="mt-2 text-xs text-muted-foreground">
                Nenhuma HH foi atribuída por aproximação. Salve a AFE correta acima e a tela será recarregada.
              </p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {routingPanel}
      <div className="grid gap-3 md:grid-cols-5">
        <Summary label="HH total no BI" value={`${hh(data.totalBiHours)} HH`} />
        <Summary label="HH sem documento" value={`${hh(data.hoursWithoutDocumentCode)} HH`} emphasis />
        <Summary
          label="Custo conhecido sem documento"
          value={money(data.costWithoutDocumentCode)}
          note={data.rowsWithoutCost ? `${data.rowsWithoutCost} lançamento(s) sem custo vigente` : undefined}
        />
        <Summary label="HH filtradas" value={`${hh(filteredHours)} HH`} note={`${rows.length} lançamento(s)`} />
        <Summary label="Selecionado" value={`${hh(selectedHours)} HH`} note={`${money(selectedCost)} · ${selectedRows.length} linha(s)`} />
      </div>

      <section className="rounded-xl border bg-card">
        <div className="flex flex-wrap items-end gap-3 border-b border-border p-4">
          <div className="mr-auto">
            <h3 className="font-bold">HH lançadas sem número do documento</h3>
            <p className="text-xs text-muted-foreground">
              Fonte: db_na_BI.dbo.tbTimeSheet · custo = HH × CUSTO/HORA do cadastro de profissionais vigente na data do lançamento.
            </p>
          </div>

          <label className="space-y-1 text-xs font-semibold">
            <span className="text-muted-foreground">Profissional</span>
            <select
              className="block h-9 min-w-[230px] rounded-md border border-border bg-background px-2 text-sm"
              value={person}
              onChange={(e) => {
                setPerson(e.target.value);
                setSelected(new Set());
                setBatchDiscipline("");
                setBatchPackageMode("MANTER");
              }}
            >
              <option value="TODOS">Todos os profissionais ({people.length})</option>
              {people.map((name) => <option key={name} value={name}>{name}</option>)}
            </select>
          </label>

          <div className="relative w-full max-w-sm">
            <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
            <input
              className="h-9 w-full rounded-md border border-border bg-background pl-9 pr-3 text-sm"
              placeholder="Buscar descrição, função, AFE ou disciplina..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3 border-b border-border bg-muted/20 px-4 py-3">
          <label className="flex cursor-pointer items-center gap-2 text-xs font-semibold">
            <input type="checkbox" checked={allVisibleSelected} onChange={(e) => toggleVisible(e.target.checked)} />
            Selecionar lançamentos visíveis ({rows.length})
          </label>

          <span className="text-xs text-muted-foreground">
            {selectedRows.length} selecionado(s) · {hh(selectedHours)} HH · {money(selectedCost)}
            {selectedWithoutCost > 0 ? ` · ${selectedWithoutCost} sem custo` : ""}
          </span>

          <div className="ml-auto flex flex-wrap items-end gap-2">
            <label className="space-y-1 text-[10px] font-bold uppercase tracking-wide text-muted-foreground">
              <span>Disciplina de todos os selecionados</span>
              <select
                className="block h-8 min-w-[220px] rounded-md border border-border bg-background px-2 text-xs normal-case font-normal tracking-normal text-foreground"
                value={batchDiscipline}
                disabled={!selectedRows.length || confirming}
                onChange={(e) => void applyDisciplineToSelected(e.target.value)}
                title="Define a mesma disciplina para todos os lançamentos selecionados"
              >
                <option value="">Escolher disciplina...</option>
                {disciplineOptions.map((x) => <option key={x} value={x}>{x}</option>)}
              </select>
            </label>

            <label className="space-y-1 text-[10px] font-bold uppercase tracking-wide text-muted-foreground">
              <span>Pacote dos selecionados</span>
              <select
                className="block h-8 rounded-md border border-border bg-background px-2 text-xs normal-case font-normal tracking-normal text-foreground"
                value={batchPackageMode}
                disabled={!selectedRows.length || confirming}
                onChange={(e) => setBatchPackageMode(e.target.value as "MANTER" | "SIM" | "NAO")}
              >
                <option value="MANTER">Manter atual</option>
                <option value="SIM">Marcar todos como pacote</option>
                <option value="NAO">Desmarcar pacote de todos</option>
              </select>
            </label>

            <Button
              size="sm"
              variant="outline"
              disabled={!selectedRows.length || confirming || batchPackageMode === "MANTER"}
              onClick={() => void saveBatchMetadata()}
            >
              {confirming && <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />}
              Aplicar pacote ({selectedRows.length})
            </Button>

            <Button
              size="sm"
              disabled={!selectedRows.length || confirming}
              onClick={() => setPreviewOpen(true)}
            >
              Simular como AT ({selectedRows.length})
            </Button>
          </div>
        </div>

        <div className="max-h-[62vh] overflow-auto">
          <table className="w-full min-w-[1450px] text-sm">
            <thead className="sticky top-0 z-10 bg-muted/95 text-left text-xs text-muted-foreground backdrop-blur">
              <tr>
                <th className="w-10 px-3 py-2">Sel.</th>
                <th className="px-3 py-2">Data</th>
                <th className="px-3 py-2">Profissional</th>
                <th className="px-3 py-2">HH</th>
                <th className="px-3 py-2">Custo</th>
                <th className="px-3 py-2">AFE</th>
                <th className="px-3 py-2">Função</th>
                <th className="px-3 py-2">Disciplina origem</th>
                <th className="px-3 py-2">Disciplina destino</th>
                <th className="px-3 py-2">Pacote</th>
                <th className="px-3 py-2">Descrição lançada</th>
                <th className="px-3 py-2">Classificação</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.rowKey} className="border-t align-top hover:bg-muted/20">
                  <td className="px-3 py-3 text-center">
                    <input
                      type="checkbox"
                      checked={selected.has(row.rowKey)}
                      onChange={(e) => setSelected((current) => {
                        const next = new Set(current);
                        e.target.checked ? next.add(row.rowKey) : next.delete(row.rowKey);
                        return next;
                      })}
                    />
                  </td>
                  <td className="whitespace-nowrap px-3 py-3">{date(row.date)}</td>
                  <td className="px-3 py-3">
                    <span className="flex items-center gap-1.5">
                      <UserRound className="h-3.5 w-3.5 text-muted-foreground" />
                      {row.name ?? "SEM NOME"}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-3 py-3 font-semibold tabular-nums">
                    <span className="flex items-center gap-1.5">
                      <Clock3 className="h-3.5 w-3.5 text-muted-foreground" />
                      {hh(row.hours)}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-3 py-3 tabular-nums">
                    {row.cost == null ? (
                      <span className="text-amber-600" title={row.costStatus ?? undefined}>{costPending(row)}</span>
                    ) : (
                      <>
                        <b>{money(row.cost)}</b>
                        <span className="block text-[10px] text-muted-foreground">
                          {money(row.hourlyCost ?? 0)}/h
                        </span>
                      </>
                    )}
                  </td>
                  <td className="px-3 py-3">{row.afe ?? "—"}</td>
                  <td className="px-3 py-3">{row.function ?? "—"}</td>
                  <td className="px-3 py-3">{row.discipline ?? "—"}</td>
                  <td className="px-3 py-3">
                    <select
                      className="h-8 min-w-[180px] rounded-md border border-border bg-background px-2 text-xs"
                      value={row.assignedDiscipline ?? ""}
                      disabled={savingKey === row.rowKey}
                      onChange={(e) => void patchRow(row, { discipline: e.target.value })}
                    >
                      <option value="">Definir disciplina...</option>
                      {disciplineOptions.map((x) => <option key={x} value={x}>{x}</option>)}
                    </select>
                  </td>
                  <td className="px-3 py-3">
                    <label className="inline-flex cursor-pointer items-center gap-2 text-xs font-semibold">
                      <input
                        type="checkbox"
                        checked={row.isPackage}
                        disabled={savingKey === row.rowKey}
                        onChange={(e) => void patchRow(row, { isPackage: e.target.checked })}
                      />
                      {row.isPackage ? <span className="inline-flex items-center gap-1 text-violet-600"><Package className="h-3.5 w-3.5" />Sim</span> : "Não"}
                    </label>
                  </td>
                  <td className="max-w-[430px] whitespace-pre-wrap px-3 py-3 text-xs">{row.description || "—"}</td>
                  <td className="px-3 py-3">
                    {row.treatmentStatus === "AT" ? (
                      <span className="inline-flex items-center gap-1 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-[10px] font-bold uppercase tracking-wide text-emerald-700">
                        <CheckCircle2 className="h-3 w-3" />AT
                      </span>
                    ) : (
                      <span className="inline-flex rounded-full border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-[10px] font-bold uppercase tracking-wide text-amber-700">
                        Pendente
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {!rows.length && (
            <p className="py-8 text-center text-sm text-muted-foreground">
              {data.items.length ? "Nenhum lançamento corresponde aos filtros." : "Nenhuma HH sem documento foi encontrada."}
            </p>
          )}
        </div>

        <div className="flex flex-wrap justify-between gap-2 border-t border-border px-4 py-3 text-xs text-muted-foreground">
          <span>{rows.length.toLocaleString("pt-BR")} de {data.items.length.toLocaleString("pt-BR")} lançamento(s)</span>
          <span>
            Reconciliação: {hh(data.hoursWithDocumentCode)} HH com documento + {hh(data.hoursWithoutDocumentCode)} HH sem documento = {hh(data.totalBiHours)} HH
          </span>
        </div>
      </section>

      <Dialog open={previewOpen} onOpenChange={(open) => { if (!confirming) setPreviewOpen(open); }}>
        <DialogContent className="flex max-h-[90vh] max-w-[900px] flex-col overflow-hidden p-0">
          <DialogHeader className="border-b border-border px-6 py-4 text-left">
            <DialogTitle>Simulação de AT — HH sem documento</DialogTitle>
            <DialogDescription>
              As HH e o custo permanecem no projeto e serão classificados pela disciplina, sem criar número do documento.
            </DialogDescription>
          </DialogHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 py-5">
            <div className="grid gap-3 sm:grid-cols-4">
              <Summary label="Lançamentos" value={selectedRows.length.toLocaleString("pt-BR")} />
              <Summary label="HH" value={`${hh(selectedHours)} HH`} />
              <Summary label="Custo conhecido" value={money(selectedCost)} />
              <Summary label="Sem custo/hora vigente" value={selectedWithoutCost.toLocaleString("pt-BR")} />
            </div>

            {selectedWithoutCost > 0 && (
              <p className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                <span>
                  {selectedWithoutCost} lançamento(s) sem custo calculado — as HH entram no AT, o custo fica pendente até o cadastro ser corrigido:{" "}
                  {[...selectedRows.filter((row) => row.cost == null).reduce((acc, row) => {
                    const key = costPending(row);
                    return acc.set(key, (acc.get(key) ?? 0) + 1);
                  }, new Map<string, number>())].map(([reason, count]) => `${reason} (${count})`).join(" · ")}
                </span>
              </p>
            )}

            {blockedWithoutDiscipline > 0 && (
              <p className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                {blockedWithoutDiscipline} lançamento(s) ainda estão sem disciplina destino. Defina individualmente ou escolha uma disciplina para o lote antes de confirmar.
              </p>
            )}

            <div className="overflow-hidden rounded-xl border border-border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50 text-left text-xs text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Disciplina destino</th>
                    <th className="px-3 py-2">Lançamentos</th>
                    <th className="px-3 py-2">HH</th>
                    <th className="px-3 py-2">Custo conhecido</th>
                    <th className="px-3 py-2">Pacote</th>
                  </tr>
                </thead>
                <tbody>
                  {previewGroups.map((group) => (
                    <tr key={group.discipline} className="border-t">
                      <td className="px-3 py-2 font-semibold">{group.discipline}</td>
                      <td className="px-3 py-2">{group.rows}</td>
                      <td className="px-3 py-2 tabular-nums">{hh(group.hours)} HH</td>
                      <td className="px-3 py-2 tabular-nums">
                        {money(group.cost)}
                        {group.withoutCost > 0 && <span className="ml-1 text-[10px] text-amber-600">+ {group.withoutCost} sem custo</span>}
                      </td>
                      <td className="px-3 py-2">{group.packages} lançamento(s)</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p className="text-xs text-muted-foreground">
              A marcação “Pacote” é somente uma classificação nesta etapa. Ela não altera reconhecimento financeiro nem regra de pacote ainda.
            </p>
          </div>

          <DialogFooter className="gap-2 border-t border-border px-6 py-3">
            <Button variant="outline" onClick={() => setPreviewOpen(false)} disabled={confirming}>Cancelar</Button>
            <Button
              onClick={() => void confirmAsAt()}
              disabled={!selectedRows.length || blockedWithoutDiscipline > 0 || confirming}
              className="min-w-[190px]"
            >
              {confirming && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Confirmar como AT
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function RoutingPanel({
  routing,
  mode,
  afe,
  saving,
  onMode,
  onAfe,
  onSave,
}: {
  routing: RoutingConfig;
  mode: "PROJECT" | "PROJECT_AFE";
  afe: string;
  saving: boolean;
  onMode: (value: "PROJECT" | "PROJECT_AFE") => void;
  onAfe: (value: string) => void;
  onSave: () => void;
}) {
  const projectOnlyAllowed = routing.projectCode === routing.biProjectCode;
  const currentConfigured = routing.isConfigured
    ? mode === routing.routingMode && (mode === "PROJECT" || afe.trim().toUpperCase() === (routing.afeCode ?? "").trim().toUpperCase())
    : false;

  return (
    <section className="rounded-xl border border-border bg-card">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-4 py-3">
        <div>
          <h3 className="font-bold">Roteamento do Timesheet</h3>
          <p className="text-xs text-muted-foreground">
            Define como as HH realmente sem número do documento são atribuídas à CR-OS atual.
          </p>
        </div>
        <span className={`rounded-full border px-2.5 py-1 text-[10px] font-black uppercase tracking-wide ${
          routing.requiresConfiguration
            ? "border-amber-500/40 bg-amber-500/10 text-amber-700"
            : routing.isConfigured
              ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-700"
              : "border-sky-500/40 bg-sky-500/10 text-sky-700"
        }`}>
          {routing.requiresConfiguration ? "Configuração obrigatória" : routing.isConfigured ? "Configurado" : "Projeto direto"}
        </span>
      </div>

      <div className="grid gap-4 p-4 lg:grid-cols-[1fr_1fr_1.4fr_auto] lg:items-end">
        <div>
          <p className="text-[10px] font-bold uppercase tracking-wide text-muted-foreground">CR-OS N&A</p>
          <p className="mt-1 font-semibold">{routing.projectCode}</p>
        </div>
        <div>
          <p className="text-[10px] font-bold uppercase tracking-wide text-muted-foreground">Projeto no BI</p>
          <p className="mt-1 font-semibold">{routing.biProjectCode}</p>
        </div>

        <div className="grid gap-2 sm:grid-cols-2">
          <label className="space-y-1 text-xs font-semibold">
            <span className="text-muted-foreground">Modo</span>
            <select
              className="block h-9 w-full rounded-md border border-border bg-background px-2 text-sm"
              value={mode}
              disabled={saving}
              onChange={(e) => onMode(e.target.value as "PROJECT" | "PROJECT_AFE")}
            >
              <option value="PROJECT" disabled={!projectOnlyAllowed}>Somente Projeto</option>
              <option value="PROJECT_AFE">Projeto + AFE</option>
            </select>
          </label>

          <label className="space-y-1 text-xs font-semibold">
            <span className="text-muted-foreground">AFE / CR-OS no BI</span>
            <input
              className="block h-9 w-full rounded-md border border-border bg-background px-2 text-sm disabled:opacity-50"
              list="timesheet-routing-afe-options"
              value={afe}
              disabled={saving || mode !== "PROJECT_AFE"}
              placeholder={routing.suggestedAfe ?? "Ex.: BML011.13"}
              onChange={(e) => onAfe(e.target.value)}
            />
            <datalist id="timesheet-routing-afe-options">
              {routing.candidates.map((candidate) => (
                <option key={candidate.afe} value={candidate.afe}>
                  {candidate.rows} lançamento(s) · {hh(candidate.hours)} HH sem documento
                </option>
              ))}
            </datalist>
          </label>
        </div>

        <Button onClick={onSave} disabled={saving || currentConfigured || (mode === "PROJECT_AFE" && !afe.trim())}>
          {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          Salvar roteamento
        </Button>
      </div>

      <div className="border-t border-border bg-muted/20 px-4 py-3 text-xs text-muted-foreground">
        <b className="text-foreground">Trava:</b> se a descrição possuir um Número do documento válido, o documento é a autoridade e a AFE é ignorada para distribuição.
        {mode === "PROJECT_AFE" && routing.candidates.length > 0 && (
          <span className="ml-2">
            AFEs encontradas nas linhas sem documento: {routing.candidates.map((x) => `${x.afe} (${hh(x.hours)} HH)`).join(" · ")}
          </span>
        )}
      </div>
    </section>
  );
}

function Summary({ label, value, note, emphasis = false }: { label: string; value: string; note?: string; emphasis?: boolean }) {
  return (
    <div className={`rounded-xl border bg-card p-4 ${emphasis ? "border-amber-500/40" : ""}`}>
      <p className="text-xs text-muted-foreground">{label}</p>
      <b className={`mt-1 block text-xl tabular-nums ${emphasis ? "text-amber-600" : ""}`}>{value}</b>
      {note && <p className="mt-1 text-[10px] text-muted-foreground">{note}</p>}
    </div>
  );
}
