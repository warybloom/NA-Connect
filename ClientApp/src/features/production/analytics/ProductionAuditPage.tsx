// Auditoria do Projeto — LDP × Timesheet. Mesma tela aprovada (cards + composição da diferença); agora no Painel de
// Planejamento, com HH LDP = Actual_HH (valor real), Diferença = LDP − TIMESHEET e filtros por coluna.
import { useEffect, useMemo, useState } from "react";
import { ChevronDown, X } from "lucide-react";
import { ReportState } from "@/components/reports/ReportComponents";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";
import {
  FILTROS_AUDITORIA_INICIAIS, disciplinaDe, filtrarAuditoria, filtrosAtivos, opcoesAuditoria,
  type DocumentoAuditoria, type FiltrosAuditoria, type VisaoAuditoria,
} from "./auditoriaFiltros";

type Overview = {
  crOs: string; sources: { name: string; hours: number; updatedAt?: string }[]; documents: DocumentoAuditoria[]; difference: number;
  tolerance: number; ldpHoursWithoutDocument?: number; timesheetHoursWithoutDocument?: number;
};
const number = (v?: number) => new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(v ?? 0);
const comSinal = (v: number) => `${v > 0 ? "+" : ""}${number(v)}`;
const api = async <T,>(url: string): Promise<T> => { const r=await fetch(url,{credentials:"include"}); if(!r.ok){const b=await r.json().catch(()=>({})); const e=Object.assign(new Error(b.message||"Não foi possível carregar os dados."),{status:r.status,code:b.code}); throw e;} return r.json(); };
function query(projectId:string, filters:Record<string,string|number|undefined>={}) { const q=new URLSearchParams({projectId}); Object.entries(filters).forEach(([k,v])=>{if(v!==undefined&&v!=="")q.set(k,String(v));});return q.toString(); }
function ErrorState({error}:{error:unknown}) { const e=error as {code?:string,message?:string}; if(e?.code==="crOsNotConfigured"||e?.code==="crOsAmbiguous") return <ReportState title="CR/OS não disponível" error>{e.message}</ReportState>; return <ReportState title="Erro ao carregar" error>{e?.message||"Tente novamente."}</ReportState>; }

const VISOES: { valor: VisaoAuditoria; rotulo: string }[] = [
  { valor: "divergencias", rotulo: "Somente divergências" }, { valor: "todos", rotulo: "Todos" }, { valor: "conciliados", rotulo: "Conciliados" },
];
const campo = "h-8 w-full min-w-0 rounded-md border border-border bg-background px-2 text-xs font-normal normal-case text-foreground placeholder:text-muted-foreground";

export function AuditoriaProjetoPage({ projectId }: { projectId?: string }) {
  const [data,setData]=useState<Overview>(); const [error,setError]=useState<unknown>();
  const [f, setF] = useState<FiltrosAuditoria>(FILTROS_AUDITORIA_INICIAIS);
  useEffect(()=>{if(projectId){setData(undefined);setError(undefined);api<Overview>(`/api/production-audit/reconciliation?${query(projectId)}`).then(setData).catch(setError);}},[projectId]);
  const opcoes = useMemo(() => opcoesAuditoria(data?.documents ?? []), [data]);
  const linhas = useMemo(() => (data ? filtrarAuditoria(data.documents, f, data.tolerance) : []), [data, f]);
  const set = <K extends keyof FiltrosAuditoria>(k: K, v: FiltrosAuditoria[K]) => setF((a) => ({ ...a, [k]: v }));

  if(!projectId)return <ReportState title="Nenhum projeto selecionado">Selecione um projeto na tela Início.</ReportState>;
  if(error)return <ErrorState error={error}/>;
  if(!data)return <ReportState title="Carregando auditoria">Consultando fontes internas.</ReportState>;
  const ativos = filtrosAtivos(f);
  const semDoc = (data.ldpHoursWithoutDocument ?? 0) + (data.timesheetHoursWithoutDocument ?? 0);

  return <div className="space-y-4">
    <div className="grid gap-3 md:grid-cols-3">
      {data.sources.map(s=><div className="rounded-xl border bg-card p-4" key={s.name}><p className="text-xs text-muted-foreground">{s.name}</p><b className="text-xl">{number(s.hours)} HH</b></div>)}
      <div className="rounded-xl border border-amber-500/40 bg-card p-4">
        <p className="text-xs text-muted-foreground">Diferença de Horas</p>
        <b className="text-2xl tabular-nums">{comSinal(data.difference)} HH</b>
        <p className="mt-0.5 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">LDP - TIMESHEET</p>
      </div>
    </div>
    {semDoc > 0 && <p className="text-xs text-amber-500">Fora dos totais e da tabela (sem código de documento): LDP {number(data.ldpHoursWithoutDocument)} HH · Timesheet {number(data.timesheetHoursWithoutDocument)} HH.</p>}

    <section className="rounded-xl border bg-card p-4 overflow-auto">
      <div className="flex flex-wrap items-center gap-3">
        <h3 className="font-bold">Composição da diferença — CR/OS {data.crOs}</h3>
        <div className="flex rounded-lg border border-border p-0.5" role="group" aria-label="Visão">
          {VISOES.map((v) => (
            <button key={v.valor} type="button" onClick={() => set("visao", v.valor)} aria-pressed={f.visao === v.valor}
              className={cn("rounded-md px-2.5 py-1 text-xs font-semibold", f.visao === v.valor ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
              {v.rotulo}
            </button>
          ))}
        </div>
        <span className="ml-auto text-xs text-muted-foreground">{linhas.length.toLocaleString("pt-BR")} de {data.documents.length.toLocaleString("pt-BR")} documentos</span>
        {ativos > 0 && (
          <button type="button" onClick={() => setF({ ...FILTROS_AUDITORIA_INICIAIS, visao: f.visao })} className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
            <X className="h-3.5 w-3.5" />Limpar filtros ({ativos})
          </button>
        )}
      </div>
      <table className="mt-3 w-full text-sm">
        <thead>
          <tr className="text-left text-muted-foreground"><th>Documento</th><th>Disciplina</th><th>HH Trabalhado LDP</th><th>Horas Trabalhadas Timesheet</th><th>Diferença de Horas</th><th>Situação</th><th>Motivo Provável</th></tr>
          <tr className="align-top" aria-label="Filtros por coluna">
            <th className="py-2 pr-2"><input aria-label="Filtrar documento" className={campo} placeholder="Contém…" value={f.documento} onChange={(e) => set("documento", e.target.value)} /></th>
            <th className="py-2 pr-2"><MultiSelecao rotulo="Disciplina" opcoes={opcoes.disciplinas} valor={f.disciplinas} onChange={(v) => set("disciplinas", v)} /></th>
            <th className="py-2 pr-2"><Faixa rotulo="HH Trabalhado LDP" min={f.ldpMin} max={f.ldpMax} onMin={(v) => set("ldpMin", v)} onMax={(v) => set("ldpMax", v)} /></th>
            <th className="py-2 pr-2"><Faixa rotulo="Horas Trabalhadas Timesheet" min={f.tsMin} max={f.tsMax} onMin={(v) => set("tsMin", v)} onMax={(v) => set("tsMax", v)} /></th>
            <th className="py-2 pr-2">
              <Faixa rotulo="Diferença de Horas" min={f.difMin} max={f.difMax} onMin={(v) => set("difMin", v)} onMax={(v) => set("difMax", v)} />
              <label className="mt-1 flex items-center gap-1 text-[11px] font-normal normal-case" title={`|Diferença| maior que a tolerância de conciliação (${number(data.tolerance)} HH)`}>
                <input type="checkbox" checked={f.somenteComDiferenca} onChange={(e) => set("somenteComDiferenca", e.target.checked)} />Somente com diferença
              </label>
            </th>
            <th className="py-2 pr-2"><MultiSelecao rotulo="Situação" opcoes={opcoes.situacoes} valor={f.situacoes} onChange={(v) => set("situacoes", v)} /></th>
            <th className="py-2"><input aria-label="Filtrar motivo provável" className={campo} placeholder="Contém…" value={f.motivo} onChange={(e) => set("motivo", e.target.value)} /></th>
          </tr>
        </thead>
        <tbody>{linhas.map(x=><tr className="border-t" key={x.documentCode}><td>{x.documentCode}</td><td>{disciplinaDe(x)}</td><td>{number(x.ldpActual)}</td><td>{number(x.timesheetActual)}</td><td className={cn("tabular-nums", Math.abs(x.difference) > data.tolerance && "font-semibold")}>{comSinal(x.difference)}</td><td>{x.status}</td><td>{x.reason}</td></tr>)}</tbody>
      </table>
      {!linhas.length&&<p className="py-6 text-center text-muted-foreground">{data.documents.length === 0 ? "Nenhum documento nas fontes." : f.visao === "divergencias" && ativos === 0 ? "Fontes conciliadas." : "Nenhum documento para os filtros."}</p>}
    </section>
  </div>;
}

function Faixa({ rotulo, min, max, onMin, onMax }: { rotulo: string; min: string; max: string; onMin: (v: string) => void; onMax: (v: string) => void }) {
  return (
    <div className="flex gap-1">
      <input aria-label={`${rotulo} mínimo`} className={campo} inputMode="decimal" placeholder="mín." value={min} onChange={(e) => onMin(e.target.value)} />
      <input aria-label={`${rotulo} máximo`} className={campo} inputMode="decimal" placeholder="máx." value={max} onChange={(e) => onMax(e.target.value)} />
    </div>
  );
}

/** Seleção múltipla com busca (opções vindas do resultado). Nada marcado = todas. */
function MultiSelecao({ rotulo, opcoes, valor, onChange }: { rotulo: string; opcoes: string[]; valor: string[]; onChange: (v: string[]) => void }) {
  const [busca, setBusca] = useState("");
  const visiveis = opcoes.filter((o) => o.toLowerCase().includes(busca.trim().toLowerCase()));
  const alternar = (o: string) => onChange(valor.includes(o) ? valor.filter((x) => x !== o) : [...valor, o]);
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button type="button" aria-label={`Filtrar ${rotulo.toLowerCase()}`} className={cn(campo, "flex items-center justify-between gap-1 text-left")}>
          <span className="truncate">{valor.length === 0 ? "Todas" : valor.length === 1 ? valor[0] : `${valor.length} selecionadas`}</span>
          <ChevronDown className="h-3.5 w-3.5 shrink-0 opacity-60" />
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-72 p-2">
        <input aria-label={`Buscar ${rotulo.toLowerCase()}`} className={cn(campo, "mb-2")} placeholder="Buscar…" value={busca} onChange={(e) => setBusca(e.target.value)} />
        <div className="max-h-60 space-y-0.5 overflow-y-auto">
          {visiveis.length === 0 && <p className="px-1 py-2 text-xs text-muted-foreground">Nenhuma opção.</p>}
          {visiveis.map((o) => (
            <label key={o} className="flex cursor-pointer items-center gap-2 rounded px-1.5 py-1 text-xs hover:bg-accent">
              <input type="checkbox" checked={valor.includes(o)} onChange={() => alternar(o)} />{o}
            </label>
          ))}
        </div>
        {valor.length > 0 && <button type="button" onClick={() => onChange([])} className="mt-2 text-xs font-semibold text-primary hover:underline">Limpar seleção</button>}
      </PopoverContent>
    </Popover>
  );
}
