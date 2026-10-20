// Peças compartilhadas pelas telas Lançamentos → Pessoas / Disciplinas / Documentos.
import { useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { ChevronLeft, ChevronRight, Filter, Loader2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ReportState } from "@/components/reports/ReportComponents";
import { cn } from "@/lib/utils";
import { obterNormaDoProjeto } from "@/features/planning/coordination/coordinationApi";
import { disciplinasDaNorma, mesclarDisciplinas, type OpcaoDisciplina } from "./disciplinasNorma";
import {
  FILTROS_VAZIOS, LancamentosError, PAGE_SIZE, STATUS_DOCUMENTO, buscarDisciplinas, buscarLancamentos, dataCurta, horas,
  type FiltrosLancamentos, type PaginaLancamentos,
} from "./lancamentosApi";

// ------------------------------------------------------------------------------------- Norma de Coordenação ----
export type EstadoNorma = { estado: "carregando" | "ok" | "sem-norma" | "indisponivel"; disciplinas: string[]; nomeNorma?: string };

/** Disciplinas da Norma vinculada ao projeto. Falha/sem permissão não bloqueia a tela: só não há sugestões da Norma. */
export function useDisciplinasDaNorma(projectId: string | undefined): EstadoNorma {
  const [r, setR] = useState<EstadoNorma>({ estado: "carregando", disciplinas: [] });
  useEffect(() => {
    if (!projectId) return;
    let vivo = true;
    setR({ estado: "carregando", disciplinas: [] });
    obterNormaDoProjeto(projectId)
      .then((x) => { if (vivo) setR(x?.norma ? { estado: "ok", disciplinas: disciplinasDaNorma(x.norma), nomeNorma: x.norma.nome } : { estado: "sem-norma", disciplinas: [] }); })
      .catch(() => { if (vivo) setR({ estado: "indisponivel", disciplinas: [] }); });
    return () => { vivo = false; };
  }, [projectId]);
  return r;
}

/** Disciplinas que aparecem no Timesheet do projeto (sem filtros) — para sugestões nas telas de Pessoas/Documentos. */
export function useDisciplinasDoTimesheet(projectId: string | undefined): string[] {
  const [lista, setLista] = useState<string[]>([]);
  useEffect(() => {
    if (!projectId) return;
    let vivo = true;
    buscarDisciplinas(projectId, FILTROS_VAZIOS).then((r) => { if (vivo) setLista(r.items.map((d) => d.discipline)); }).catch(() => { if (vivo) setLista([]); });
    return () => { vivo = false; };
  }, [projectId]);
  return lista;
}

export function AvisoNorma({ norma }: { norma: EstadoNorma }) {
  if (norma.estado === "ok")
    return <p className="text-xs text-muted-foreground">Disciplinas sugeridas da Norma de Coordenação <strong className="text-foreground">{norma.nomeNorma}</strong>.</p>;
  if (norma.estado === "sem-norma")
    return <p className="text-xs text-amber-500">Nenhuma Norma de Coordenação vinculada a este projeto — as sugestões usam apenas as disciplinas do Timesheet.</p>;
  if (norma.estado === "indisponivel")
    return <p className="text-xs text-amber-500">Norma de Coordenação indisponível para você — as sugestões usam apenas as disciplinas do Timesheet.</p>;
  return null;
}

// ------------------------------------------------------------------------------------------------- filtros ----
interface FiltrosProps {
  valor: FiltrosLancamentos;
  onAplicar: (f: FiltrosLancamentos) => void;
  disciplinas: OpcaoDisciplina[];
  /** Campos exibidos (status só em Documentos). */
  campos?: (keyof FiltrosLancamentos)[];
  carregando?: boolean;
}

const ROTULO: Record<keyof FiltrosLancamentos, string> = {
  from: "De", to: "Até", person: "Pessoa", discipline: "Disciplina", document: "Documento", status: "Situação",
};

export function FiltrosBar({ valor, onAplicar, disciplinas, campos = ["from", "to", "person", "discipline", "document"], carregando }: FiltrosProps) {
  const [rascunho, setRascunho] = useState(valor);
  const id = useId();
  useEffect(() => setRascunho(valor), [valor]);
  const set = (k: keyof FiltrosLancamentos, v: string) => setRascunho((r) => ({ ...r, [k]: v }));
  const aplicar = (e: FormEvent) => { e.preventDefault(); onAplicar(rascunho); };
  const ativos = campos.filter((c) => valor[c]).length;

  return (
    <form onSubmit={aplicar} className="rounded-xl border border-border bg-card/60 p-3" aria-label="Filtros">
      <div className="flex flex-wrap items-end gap-3">
        {campos.map((c) => (
          <label key={c} className="flex min-w-[150px] flex-1 flex-col gap-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            {ROTULO[c]}
            {c === "from" || c === "to" ? (
              <Input type="date" value={rascunho[c]} onChange={(e) => set(c, e.target.value)} className="h-9" />
            ) : c === "status" ? (
              <select value={rascunho.status} onChange={(e) => set("status", e.target.value)}
                className="h-9 rounded-md border border-border bg-background px-2 text-sm font-normal normal-case tracking-normal text-foreground">
                <option value="">Todas</option>
                {STATUS_DOCUMENTO.map((s) => <option key={s} value={s}>{rotuloStatus(s)}</option>)}
              </select>
            ) : (
              <Input value={rascunho[c]} onChange={(e) => set(c, e.target.value)} className="h-9 normal-case"
                placeholder={c === "person" ? "Nome" : c === "document" ? "Código ou título" : "Disciplina"}
                list={c === "discipline" ? `${id}-disc` : undefined} />
            )}
          </label>
        ))}
        <div className="flex gap-2">
          <Button type="submit" className="h-9 gap-1.5" disabled={carregando}>
            {carregando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Filter className="h-4 w-4" />}Filtrar
          </Button>
          {ativos > 0 && (
            <Button type="button" variant="ghost" className="h-9 gap-1" onClick={() => onAplicar({ ...FILTROS_VAZIOS })}>
              <X className="h-4 w-4" />Limpar ({ativos})
            </Button>
          )}
        </div>
      </div>
      <datalist id={`${id}-disc`}>{disciplinas.map((d) => <option key={d.chave} value={d.nome} />)}</datalist>
    </form>
  );
}

/** Chips de disciplina (Norma + Timesheet fora da Norma + NÃO MAPEADO). Clique filtra; clique de novo limpa. */
export function ChipsDisciplina({ opcoes, selecionada, onSelecionar }: { opcoes: OpcaoDisciplina[]; selecionada: string; onSelecionar: (nome: string) => void }) {
  if (opcoes.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5" aria-label="Disciplinas">
      {opcoes.map((o) => {
        const ativo = selecionada && o.nome.toLowerCase() === selecionada.toLowerCase();
        return (
          <button key={o.chave} type="button" onClick={() => onSelecionar(ativo ? "" : o.nome)}
            title={o.origem === "norma" ? (o.noTimesheet ? "Disciplina da Norma" : "Disciplina da Norma sem lançamentos") : o.origem === "timesheet" ? "Encontrada no Timesheet, fora da Norma" : "Sem disciplina mapeada"}
            className={cn("rounded-full border px-2.5 py-1 text-xs font-medium transition-colors",
              ativo ? "border-primary bg-primary text-primary-foreground"
                : o.origem === "norma" ? (o.noTimesheet ? "border-border bg-background hover:bg-accent" : "border-dashed border-border text-muted-foreground hover:bg-accent")
                : o.origem === "timesheet" ? "border-amber-500/50 bg-amber-500/10 text-amber-600 hover:bg-amber-500/20 dark:text-amber-400"
                : "border-rose-500/40 bg-rose-500/10 text-rose-500 hover:bg-rose-500/20")}>
            {o.nome}
          </button>
        );
      })}
    </div>
  );
}

export const opcoesDisciplina = (norma: EstadoNorma, doTimesheet: string[]) => mesclarDisciplinas(norma.disciplinas, doTimesheet);

// ------------------------------------------------------------------------------------------ estados / KPIs ----
export function EstadoErro({ erro }: { erro: unknown }) {
  const e = erro as LancamentosError;
  if (e?.code === "crOsNotConfigured" || e?.code === "crOsAmbiguous")
    return <ReportState title="CR/OS não disponível" error>{e.message} Ajuste o cadastro do projeto para ligar o Timesheet.</ReportState>;
  if (e?.status === 403) return <ReportState title="Sem permissão" error>{e.message}</ReportState>;
  return <ReportState title="Não foi possível carregar" error>{e?.message || "Tente novamente."}</ReportState>;
}

export function Indicador({ rotulo, valor, detalhe, tom }: { rotulo: string; valor: ReactNode; detalhe?: string; tom?: "alerta" | "ok" }) {
  return (
    <div className={cn("rounded-xl border bg-card/70 p-4", tom === "alerta" ? "border-rose-500/40" : "border-border")}>
      <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
      <p className={cn("mt-1 text-2xl font-semibold tabular-nums", tom === "alerta" && "text-rose-500", tom === "ok" && "text-emerald-500")}>{valor}</p>
      {detalhe && <p className="mt-1 text-xs text-muted-foreground">{detalhe}</p>}
    </div>
  );
}

export function Paginacao({ pagina, total, onPagina, rotulo }: { pagina: number; total: number; onPagina: (p: number) => void; rotulo: string }) {
  const paginas = Math.max(1, Math.ceil(total / PAGE_SIZE));
  return (
    <div className="flex items-center justify-between border-t border-border px-4 py-2 text-xs text-muted-foreground">
      <span>{total.toLocaleString("pt-BR")} {rotulo}</span>
      <span className="flex items-center gap-2">
        <Button variant="ghost" size="sm" className="h-7 px-2" disabled={pagina <= 1} onClick={() => onPagina(pagina - 1)} aria-label="Página anterior"><ChevronLeft className="h-4 w-4" /></Button>
        Página {pagina} de {paginas}
        <Button variant="ghost" size="sm" className="h-7 px-2" disabled={pagina >= paginas} onClick={() => onPagina(pagina + 1)} aria-label="Próxima página"><ChevronRight className="h-4 w-4" /></Button>
      </span>
    </div>
  );
}

export const rotuloStatus = (s: string) =>
  s === "ESTOURADO" ? "Estourado" : s === "DENTRO DO PLANEJADO" ? "Dentro do planejado" : s === "SEM HH PLANEJADA" ? "Sem HH planejada" : s;

export function SeloStatus({ status }: { status: string }) {
  return (
    <span className={cn("inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold",
      status === "ESTOURADO" ? "bg-rose-500/15 text-rose-500" : status === "DENTRO DO PLANEJADO" ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400" : "bg-muted text-muted-foreground")}>
      {rotuloStatus(status)}
    </span>
  );
}

/** Diferença realizado − planejado: positiva (acima do plano) em vermelho. */
export const Diferenca = ({ v }: { v: number }) => <span className={cn("tabular-nums", v > 0.005 ? "text-rose-500" : v < -0.005 ? "text-emerald-600 dark:text-emerald-400" : "")}>{v > 0 ? "+" : ""}{horas(v)}</span>;

export const th = "px-3 py-2 text-left text-[11px] font-semibold uppercase tracking-wide text-muted-foreground";
export const thNum = cn(th, "text-right");
export const td = "px-3 py-2";
export const tdNum = "px-3 py-2 text-right tabular-nums";

// ------------------------------------------------------------------------------------------------ drilldown ----
interface DrilldownProps {
  projectId: string;
  filtros: FiltrosLancamentos;
  recorte: Partial<Pick<FiltrosLancamentos, "person" | "discipline" | "document">> | null;
  titulo: string;
  descricao?: string;
  onFechar: () => void;
}

/** Lançamentos individuais do recorte escolhido (igualdade exata) respeitando os filtros da tela. */
export function DrilldownLancamentos({ projectId, filtros, recorte, titulo, descricao, onFechar }: DrilldownProps) {
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PaginaLancamentos>();
  const [erro, setErro] = useState<unknown>();
  const chave = JSON.stringify(recorte);

  useEffect(() => { setPagina(1); }, [chave]);
  useEffect(() => {
    if (!recorte) return;
    let vivo = true;
    setDados(undefined); setErro(undefined);
    buscarLancamentos(projectId, filtros, recorte, pagina).then((d) => { if (vivo) setDados(d); }).catch((e) => { if (vivo) setErro(e); });
    return () => { vivo = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, chave, pagina, JSON.stringify(filtros)]);

  return (
    <Sheet open={recorte !== null} onOpenChange={(v) => { if (!v) onFechar(); }}>
      <SheetContent side="right" className="flex w-full flex-col gap-0 p-0 sm:max-w-3xl">
        <SheetHeader className="border-b border-border px-6 py-4 text-left">
          <SheetTitle>{titulo}</SheetTitle>
          <SheetDescription>{descricao ?? "Lançamentos individuais do Timesheet no filtro atual."}</SheetDescription>
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-auto">
          {erro ? <div className="p-6"><EstadoErro erro={erro} /></div>
            : !dados ? <p className="flex items-center gap-2 p-6 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando lançamentos…</p>
            : dados.items.length === 0 ? <div className="p-6"><ReportState title="Nenhum lançamento">Nada encontrado para este recorte e filtro.</ReportState></div>
            : (
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-card"><tr className="border-b border-border">
                  <th className={th}>Data</th><th className={th}>Pessoa</th><th className={th}>Documento</th><th className={th}>Disciplina</th>
                  <th className={thNum}>HH realizada</th><th className={th}>Descrição</th>
                </tr></thead>
                <tbody>
                  {dados.items.map((l, i) => (
                    <tr key={i} className="border-b border-border/50 align-top">
                      <td className={cn(td, "whitespace-nowrap")}>{dataCurta(l.date)}</td>
                      <td className={td}>{l.name ?? "—"}</td>
                      <td className={td}><div className="font-medium">{l.documentCode ?? "—"}</div>{l.document && <div className="text-xs text-muted-foreground">{l.document}</div>}</td>
                      <td className={td}>{l.discipline ?? "—"}</td>
                      <td className={tdNum}>{horas(l.workedHours)}</td>
                      <td className={cn(td, "max-w-[280px] text-xs text-muted-foreground")}>{l.description ?? "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
        </div>
        {dados && <Paginacao pagina={pagina} total={dados.total} onPagina={setPagina} rotulo="lançamentos" />}
      </SheetContent>
    </Sheet>
  );
}
