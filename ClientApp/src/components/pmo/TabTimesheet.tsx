import { useEffect, useState } from "react";
import { BarChart3, TrendingUp, AlertTriangle, FileSearch, CalendarClock, LayoutDashboard, Search, ChevronDown, Activity,
  CalendarDays, Table2, FileSpreadsheet, FileWarning, PauseCircle, ShieldAlert, Wrench, Building2 } from "lucide-react";
import { AccItem, AccList, ApiError, getJson } from "@/lib/api";
import PmoProjectSelector, { usePmoProjectSelection } from "./PmoProjectSelector";
import type { PmoProject } from "./pmoData";
import ViewGestaoAVista from "./ViewGestaoAVista";
import ViewTimesheet from "./ViewTimesheet";
import ViewProducao from "./ViewProducao";
import ViewCurvaS from "./ViewCurvaS";
import ViewAvanco4D from "./ViewAvanco4D";
import ViewOfensores from "./ViewOfensores";
import WeeklyDetailsSection from "./WeeklyDetailsSection";
import ScheduleSection from "./ScheduleSection";
import LdpDetailsSection from "./LdpDetailsSection";
import ScopeDeviationsSection from "./ScopeDeviationsSection";
import HoldsSection from "./HoldsSection";
import CapActionSection from "./CapActionSection";
import QualityReportSection from "@/components/reports/QualityReportSection";

/**
 * Porte literal do hub ForgeSync Clientapp/src/components/TabTimesheet.tsx ("Painel Gerencial (PMO)",
 * montado em App.tsx na rota /relatorios) — mesma barra superior, mesma navegação interna por abas, as
 * mesmas 6 views executivas. Acrescenta um segundo grupo "CONTROLE OPERACIONAL" (conceitual, decidido
 * nesta sessão) com as tabelas operacionais já prontas no N&A: Programação Semanal, Cronograma, LDP,
 * Desvios de Escopo, HOLD, NCR, CAP Interno, CAP Externo — mesmo estilo de aba do ForgeSync, apenas
 * agrupado em duas linhas em vez de uma lista única desorganizada.
 *
 * Única adaptação de arquitetura (não visual): o seletor de projeto do ForgeSync é um único combobox de
 * "Código do Projeto" (com opção agregada "TODOS/Portfólio") alimentado por uma tabela própria do
 * ForgeSync. O backend N&A exige hub+project ACC por chamada via IProjectReportAccess e nunca autoriza
 * uma consulta agregada sem esse gate por projeto — por isso aqui são dois selects (Conta/Projeto, mesmo
 * padrão já usado pelo restante de Relatórios), estilizados no mesmo chrome escuro/ícone de busca/chevron
 * do combobox original. A opção "Portfólio (TODOS)" não é portada nesta etapa (ver PROGRESSO.md).
 *
 * NCR reaproveita QualityReportSection.tsx (endpoint /api/reports/quality já existente, intacto) — ainda
 * no visual claro legado, não no tema escuro PMO; harmonização visual registrada como pendência.
 */
export default function TabTimesheet() {
  const [abaAtiva, setAbaAtiva] = useState("gestao");

  const [hubs, setHubs] = useState<AccItem[]>([]);
  const [projects, setProjects] = useState<AccItem[]>([]);
  const [hub, setHub] = useState("");
  const [selectedProjects, setSelectedProjects] = usePmoProjectSelection(projects);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  // Regra de negócio: 1 projeto selecionado = aquele projeto; N projetos = CONSOLIDADO de todos. TODAS as abas recebem "selection" (todos os
  // selecionados). "project" (o primeiro) existe só como compatibilidade de PmoProps e nunca decide o que uma aba mostra.
  const project = selectedProjects.size > 0 ? [...selectedProjects][0] : "";
  const selection: PmoProject[] = [...selectedProjects].map(id => ({
    id, hubId: hub, name: projects.find(p => p.id === id)?.attributes.name ?? id,
  }));

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(undefined);
    async function load() {
      if (!hub) {
        const data = await getJson<AccList>('/DataManagement/Data/Hubs', abort.signal);
        if (!abort.signal.aborted) setHubs(data.data ?? []);
      } else {
        const data = await getJson<AccList>(`/DataManagement/Data/Projetos?hubId=${encodeURIComponent(hub)}`, abort.signal);
        if (!abort.signal.aborted) setProjects(data.data ?? []);
      }
    }
    load().catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : 'Não foi possível carregar contas/projetos.'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [hub]);

  const tabClass = (active: boolean, activeClass = "bg-primary text-primary-foreground") =>
    `flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${active ? activeClass : "text-slate-400 hover:text-foreground hover:bg-slate-800"}`;

  return (
    <div className="w-full h-full flex flex-col gap-6 animate-enter">

      {/* BARRA GLOBAL SUPERIOR COM O FILTRO DE PROJETOS */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-slate-700 pb-4">

        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-amber-500/10 border border-amber-500/30 text-amber-500 rounded-lg shadow-[0_0_15px_rgba(245,158,11,0.15)]">
            <Activity className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-xl font-black text-slate-100 uppercase tracking-tight">Painel Gerencial (PMO)</h1>
            <p className="text-xs text-slate-400 font-medium">Controle integrado de Escopo, Cronograma e Custos</p>
          </div>
        </div>

        <div className="flex items-center gap-2 flex-wrap w-full md:w-auto">
          <div className="flex items-center gap-2 bg-slate-800/80 border border-slate-600 rounded-lg p-1 pr-3 w-full md:w-auto shadow-sm">
            <div className="bg-slate-700 p-1.5 rounded-md text-slate-300"><Search className="w-4 h-4" /></div>
            <select value={hub} onChange={(e) => { setHub(e.target.value); setProjects([]); setSelectedProjects(new Set()); }}
              title="Selecionar Conta" aria-label="Conta"
              className="bg-transparent text-sm font-semibold text-slate-100 outline-none cursor-pointer border-none focus:ring-0 w-full md:w-48 appearance-none">
              <option value="" className="bg-slate-800 text-slate-300">Selecione uma conta</option>
              {hubs.map(h => (<option key={h.id} value={h.id} className="bg-slate-800 font-medium text-slate-300">{h.attributes.name}</option>))}
            </select>
            <ChevronDown className="w-4 h-4 text-slate-400 pointer-events-none" />
          </div>

          <PmoProjectSelector projects={projects} selected={selectedProjects} onChange={setSelectedProjects} />
        </div>

      </div>

      {/* NAVEGAÇÃO — GRUPO EXECUTIVO (6 abas ForgeSync originais) */}
      <div className="space-y-2">
        <p className="text-[10px] font-black uppercase tracking-widest text-slate-500 px-1">Executivo</p>
        <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
          <button onClick={() => setAbaAtiva("gestao")} className={tabClass(abaAtiva === "gestao", "bg-amber-500 text-white shadow-[0_0_15px_rgba(245,158,11,0.4)]")}>
            <LayoutDashboard className="w-4 h-4" /> Gestão à Vista
          </button>
          <button onClick={() => setAbaAtiva("timesheet")} className={tabClass(abaAtiva === "timesheet")}>
            <BarChart3 className="w-4 h-4" /> Financeiro / Horas
          </button>
          <button onClick={() => setAbaAtiva("producao")} className={tabClass(abaAtiva === "producao")}>
            <FileSearch className="w-4 h-4" /> Mapa de Calor (Status)
          </button>
          <button onClick={() => setAbaAtiva("curvas")} className={tabClass(abaAtiva === "curvas")}>
            <TrendingUp className="w-4 h-4" /> Curva S (EVM)
          </button>
          <button onClick={() => setAbaAtiva("avanco4d")} className={tabClass(abaAtiva === "avanco4d", "bg-emerald-500 text-white")}>
            <CalendarClock className="w-4 h-4" /> Avanço 4D
          </button>
          <button onClick={() => setAbaAtiva("desvios")} className={tabClass(abaAtiva === "desvios")}>
            <AlertTriangle className="w-4 h-4" /> Análise de Desvios
          </button>
        </div>
      </div>

      {/* NAVEGAÇÃO — GRUPO CONTROLE OPERACIONAL (tabelas operacionais já prontas no N&A) */}
      <div className="space-y-2">
        <p className="text-[10px] font-black uppercase tracking-widest text-slate-500 px-1">Controle Operacional</p>
        <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
          <button onClick={() => setAbaAtiva("weekly")} className={tabClass(abaAtiva === "weekly", "bg-sky-500 text-white")}>
            <CalendarDays className="w-4 h-4" /> Programação Semanal
          </button>
          <button onClick={() => setAbaAtiva("cronograma")} className={tabClass(abaAtiva === "cronograma", "bg-sky-500 text-white")}>
            <Table2 className="w-4 h-4" /> Cronograma
          </button>
          <button onClick={() => setAbaAtiva("ldp")} className={tabClass(abaAtiva === "ldp", "bg-sky-500 text-white")}>
            <FileSpreadsheet className="w-4 h-4" /> LDP
          </button>
          <button onClick={() => setAbaAtiva("ncr")} className={tabClass(abaAtiva === "ncr", "bg-emerald-600 text-white")}>
            <ShieldAlert className="w-4 h-4" /> NCR
          </button>
          <button onClick={() => setAbaAtiva("op-desvios")} className={tabClass(abaAtiva === "op-desvios", "bg-amber-600 text-white")}>
            <FileWarning className="w-4 h-4" /> Desvios de Escopo
          </button>
          <button onClick={() => setAbaAtiva("hold")} className={tabClass(abaAtiva === "hold", "bg-sky-600 text-white")}>
            <PauseCircle className="w-4 h-4" /> HOLD
          </button>
          <button onClick={() => setAbaAtiva("cap-interno")} className={tabClass(abaAtiva === "cap-interno", "bg-indigo-600 text-white")}>
            <Wrench className="w-4 h-4" /> CAP Interno
          </button>
          <button onClick={() => setAbaAtiva("cap-externo")} className={tabClass(abaAtiva === "cap-externo", "bg-indigo-600 text-white")}>
            <Building2 className="w-4 h-4" /> CAP Externo
          </button>
        </div>
      </div>

      {/* RENDERIZAÇÃO DINÂMICA DA TELA */}
      <div className="flex-1">
        {error ? (
          <div className="flex flex-col items-center justify-center h-64 gap-3 text-rose-400"><p>{error}</p></div>
        ) : !hub || selection.length === 0 ? (
          <div className="flex items-center justify-center h-64 text-slate-500 text-center px-6">
            {loading ? "Carregando contas/projetos..." : "Selecione uma conta e um projeto para abrir o Painel Gerencial."}
          </div>
        ) : (
          <>
            {abaAtiva === "gestao" && <ViewGestaoAVista hub={hub} project={project} selection={selection} />}
            {abaAtiva === "timesheet" && <ViewTimesheet hub={hub} project={project} selection={selection} />}
            {abaAtiva === "producao" && <ViewProducao selection={selection} />}
            {abaAtiva === "curvas" && <ViewCurvaS selection={selection} />}
            {abaAtiva === "avanco4d" && <ViewAvanco4D selection={selection} />}
            {abaAtiva === "desvios" && <ViewOfensores selection={selection} />}
            {abaAtiva === "weekly" && <WeeklyDetailsSection selection={selection} />}
            {abaAtiva === "cronograma" && <ScheduleSection selection={selection} />}
            {abaAtiva === "ldp" && <LdpDetailsSection selection={selection} />}
            {abaAtiva === "ncr" && <QualityReportSection selection={selection} />}
            {abaAtiva === "op-desvios" && <ScopeDeviationsSection selection={selection} />}
            {abaAtiva === "hold" && <HoldsSection selection={selection} />}
            {abaAtiva === "cap-interno" && <CapActionSection selection={selection} endpoint="/api/reports/cap/internal" title="CAP Interno" />}
            {abaAtiva === "cap-externo" && <CapActionSection selection={selection} endpoint="/api/reports/cap/external" title="CAP Externo" />}
          </>
        )}
      </div>

    </div>
  );
}
