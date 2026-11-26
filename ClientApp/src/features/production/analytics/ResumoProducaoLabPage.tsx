import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link, Navigate } from "react-router-dom";
import {
  AlertTriangle, CheckCircle2, Clock, FileText, Gauge, Hourglass,
  MessageSquare, Target, TrendingUp, Wallet, X, type LucideIcon,
} from "lucide-react";
import ConsolidadoBanner from "@/components/pmo/ConsolidadoBanner";
import { usePmoReports, formatCurrency, formatHH, formatData } from "@/components/pmo/pmoData";
import type { FactsReport } from "@/components/pmo/pmoFacts";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { rotaPermitida } from "@/features/permissions/permissionLogic";
import { cn } from "@/lib/utils";
import { rotaProducao } from "../productionRoutes";
import {
  calcularResumo, consolidarAvanco, documentalDaLdp, rotaLdpFiltrada,
  rotuloTipoLdp, type CardDocumental, type NumeroLdp, type StatusDocumento,
} from "./resumoLogic";
import { useLdpPorProjeto } from "./useLdpPorProjeto";

interface ExperimentalSummary {
  projectCode: string;
  totalHours: number;
  documentHours: number;
  unassignedHours: number;
  confirmedAtHours: number;
  pendingUnassignedHours: number;
  packageMarkedHours: number;
  knownCost: number;
  documentCost: number;
  unassignedCost: number;
  missingCostHours: number;
  missingCostRows: number;
  scopeAmbiguous: boolean;
  scopeMessage: string | null;
  excludedHours: number;
  excludedKnownCost: number;
  excludedRows: number;
  allocatedEngineeringHours: number | null;
  allocatedEngineeringKnownCost: number | null;
  allocatedTotalHours: number | null;
  allocatedExcludedHours: number | null;
  allocatedExcludedKnownCost: number | null;
  allocatedMissingCostHours: number | null;
  allocatedRows: number;
}

const pct = (v: number | null) => (v === null || !Number.isFinite(v) ? "—" : `${formatHH(Math.round(v * 100) / 100)}%`);
const delta = (v: number | null, formatter: (n: number) => string) =>
  v === null || !Number.isFinite(v) ? "—" : `${v >= 0 ? "+" : ""}${formatter(v)}`;

export const rotaDocumentosPorStatusLab = (status: StatusDocumento) =>
  `${rotaProducao("lista-documentos")}?status=${status}&analise=1`;

export default function ResumoProducaoLabPage({ selection }: { selection: PmoSelecionado[] }) {
  const { reports, falhas, loading, error, retry } = usePmoReports("gestao-vista", { hub: "", project: "", selection });
  const ldp = useLdpPorProjeto(selection);
  const { can } = usePermissions();

  const [access, setAccess] = useState<boolean | null>(null);
  const [lab, setLab] = useState<ExperimentalSummary[]>([]);
  const [labLoading, setLabLoading] = useState(false);
  const [labError, setLabError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    fetch("/api/production-audit/experimental-summary/access", { credentials: "include" })
      .then(async (r) => {
        if (!r.ok) return { allowed: false };
        return (await r.json()) as { allowed: boolean };
      })
      .then((x) => { if (active) setAccess(x.allowed); })
      .catch(() => { if (active) setAccess(false); });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (access !== true || selection.length === 0) {
      setLab([]);
      setLabError(null);
      return;
    }

    const controller = new AbortController();
    setLabLoading(true);
    setLabError(null);

    Promise.all(selection.map(async (item) => {
      const response = await fetch(
        `/api/production-audit/experimental-summary?projectId=${encodeURIComponent(item.id)}`,
        { credentials: "include", signal: controller.signal },
      );
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message ?? `Falha no Comparativo de HH e Custos de ${item.name}.`);
      return body as ExperimentalSummary;
    }))
      .then(setLab)
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setLabError(e instanceof Error ? e.message : "Não foi possível carregar o Comparativo de HH e Custos.");
      })
      .finally(() => setLabLoading(false));

    return () => controller.abort();
  }, [access, selection]);

  const exp = useMemo(() => ({
    totalHours: lab.reduce((s, x) => s + x.totalHours, 0),
    documentHours: lab.reduce((s, x) => s + x.documentHours, 0),
    unassignedHours: lab.reduce((s, x) => s + x.unassignedHours, 0),
    confirmedAtHours: lab.reduce((s, x) => s + x.confirmedAtHours, 0),
    pendingUnassignedHours: lab.reduce((s, x) => s + x.pendingUnassignedHours, 0),
    packageMarkedHours: lab.reduce((s, x) => s + x.packageMarkedHours, 0),
    knownCost: lab.reduce((s, x) => s + x.knownCost, 0),
    documentCost: lab.reduce((s, x) => s + x.documentCost, 0),
    unassignedCost: lab.reduce((s, x) => s + x.unassignedCost, 0),
    missingCostHours: lab.reduce((s, x) => s + x.missingCostHours, 0),
    missingCostRows: lab.reduce((s, x) => s + x.missingCostRows, 0),
    excludedHours: lab.reduce((s, x) => s + (x.excludedHours ?? 0), 0),
    excludedKnownCost: lab.reduce((s, x) => s + (x.excludedKnownCost ?? 0), 0),
    allocatedEngineeringHours: lab.reduce((s, x) => s + (x.allocatedEngineeringHours ?? 0), 0),
    allocatedEngineeringKnownCost: lab.reduce((s, x) => s + (x.allocatedEngineeringKnownCost ?? 0), 0),
    allocatedTotalHours: lab.reduce((s, x) => s + (x.allocatedTotalHours ?? 0), 0),
    allocatedExcludedHours: lab.reduce((s, x) => s + (x.allocatedExcludedHours ?? 0), 0),
    allocatedExcludedKnownCost: lab.reduce((s, x) => s + (x.allocatedExcludedKnownCost ?? 0), 0),
    allocatedMissingCostHours: lab.reduce((s, x) => s + (x.allocatedMissingCostHours ?? 0), 0),
    allocatedRows: lab.reduce((s, x) => s + (x.allocatedRows ?? 0), 0),
  }), [lab]);

  const ambiguos = lab.filter((x) => x.scopeAmbiguous);

  if (access === false) return <Navigate to="/controle-producao/resumo" replace />;
  if (access === null) return <ReportState title="Validando acesso ao Comparativo de HH e Custos…">Aguarde.</ReportState>;
  if (loading || labLoading) return <ReportState title="Carregando Comparativo de HH e Custos…">Comparando o Resumo atual com as fontes experimentais de HH e custo.</ReportState>;
  if (error) return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;
  if (labError) return <ReportState title="Comparativo de HH e Custos indisponível" error>{labError}</ReportState>;

  const fr = reports as FactsReport[];
  const atual = calcularResumo(fr);
  const av = consolidarAvanco(fr);
  const doc = documentalDaLdp(ldp.projetos);
  const unico = doc.unicoProjeto;
  const destino = (rota: string) => (rotaPermitida(rota.split("?")[0], "", can) ? rota : undefined);
  const ldpLink = (n?: NumeroLdp) => (unico ? destino(rotaLdpFiltrada(unico, n?.filtro)) : undefined);
  const financeiro = destino(rotaProducao("analise-financeira/visao-geral")) ?? destino(rotaProducao("controle-hh-custo"));
  const curvaS = destino(rotaProducao("curva-s"));
  const prazo = (s: StatusDocumento) => destino(rotaDocumentosPorStatusLab(s));
  const numero = (n: NumeroLdp | null | undefined) => (ldp.carregando ? "…" : ldp.erro || !n || n.valor === null ? "—" : String(n.valor));

  const hhPrevisto = atual.hhPrevisto;
  const custoPrevisto = atual.custoPrevisto;
  // Mesma fonte de HH e custo por Número do Documento da Auditoria de Divergências.
  // A leitura diária continua separada, para detectar HH ainda não distribuídas.
  const hhRealizadoLab = exp.allocatedEngineeringHours;
  const custoRealizadoLab = exp.allocatedEngineeringKnownCost;
  const diferencaDiarioDistribuido = exp.totalHours - exp.allocatedEngineeringHours;
  const saldoHhLab = hhPrevisto === null ? null : hhPrevisto - hhRealizadoLab;
  const saldoCustoLab = custoPrevisto === null ? null : custoPrevisto - custoRealizadoLab;
  const consumoHhLab = hhPrevisto !== null && hhPrevisto > 0 ? (hhRealizadoLab * 100) / hhPrevisto : null;
  const consumoCustoLab = custoPrevisto !== null && custoPrevisto > 0 ? (custoRealizadoLab * 100) / custoPrevisto : null;
  const diffHh = atual.hhRealizado === null ? null : hhRealizadoLab - atual.hhRealizado;
  const diffSaldoHh = atual.saldoHh === null || saldoHhLab === null ? null : saldoHhLab - atual.saldoHh;
  const diffCusto = atual.custoRealizado === null ? null : custoRealizadoLab - atual.custoRealizado;
  const diffSaldoCusto = atual.saldoCusto === null || saldoCustoLab === null ? null : saldoCustoLab - atual.saldoCusto;

  const ICONE_DOC: Record<string, LucideIcon> = { emitidos: CheckCircle2, cancelados: X, analiseCliente: MessageSquare, aEmitir: Hourglass };
  const COR_DOC: Record<string, keyof typeof CORES> = { emitidos: "emerald", cancelados: "slate", analiseCliente: "sky", aEmitir: "amber" };
  const semanaTxt = av.inicioSemana && av.fimSemana ? `${formatData(av.inicioSemana)}–${formatData(av.fimSemana)}` : null;

  return (
    <div className="space-y-5">
      <div className="rounded-xl border border-violet-500/40 bg-violet-500/5 px-4 py-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="rounded-full border border-violet-500/40 bg-violet-500/10 px-2.5 py-1 text-[10px] font-black uppercase tracking-widest text-violet-500">
            Acesso restrito
          </span>
          <b className="text-sm">Comparativo de dados — não altera o Resumo de produção</b>
        </div>
        <p className="mt-1 text-xs text-muted-foreground">
          HH e custo realizados de Engenharia utilizam o Timesheet distribuído por Número do Documento
          (tbTimeSheetRealizadoDocumento), excluindo Gestão. Os valores do Timesheet diário permanecem
          disponíveis apenas para conciliação e tratamento de HH sem documento.
        </p>
      </div>

      <ConsolidadoBanner selection={selection} falhas={falhas} />

      {ambiguos.map((x) => (
        <div key={x.projectCode} className="rounded-lg border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700">
          <AlertTriangle className="mr-1.5 inline h-3.5 w-3.5" />{x.scopeMessage}
        </div>
      ))}

      {/* Uma fileira por assunto. Para realizado e saldo, LDP e Timesheet ficam lado a lado. */}
      <Grupo titulo="Horas trabalhadas" colunas={3}>
        <Card icone={Clock} titulo="HH previsto" valor={hhPrevisto === null ? "—" : formatHH(hhPrevisto)} cor="sky" para={financeiro} />
        <Comparacao titulo="HH realizado" atual={atual.hhRealizado === null ? "—" : formatHH(atual.hhRealizado)}
          lab={formatHH(hhRealizadoLab)} diferenca={delta(diffHh, formatHH)}
          nota={`Por documento: ${formatHH(exp.allocatedTotalHours)} HH no total · Gestão excluída: ${formatHH(exp.allocatedExcludedHours)} HH. Diário Engenharia: ${formatHH(exp.totalHours)} HH (diferença entre fontes: ${delta(diferencaDiarioDistribuido, formatHH)})`} />
        <Comparacao titulo="Saldo HH" atual={atual.saldoHh === null ? "—" : formatHH(atual.saldoHh)}
          lab={saldoHhLab === null ? "—" : formatHH(saldoHhLab)} diferenca={delta(diffSaldoHh, formatHH)} />
      </Grupo>

      <Grupo titulo="Custos do projeto" colunas={3}>
        <Card icone={Wallet} titulo="Custo previsto" valor={custoPrevisto === null ? "—" : formatCurrency(custoPrevisto)} cor="emerald" para={financeiro} />
        <Comparacao titulo="Custo realizado"
          atual={atual.custoRealizado === null ? "—" : formatCurrency(atual.custoRealizado)}
          lab={formatCurrency(custoRealizadoLab)} diferenca={delta(diffCusto, formatCurrency)}
          nota={`${exp.allocatedMissingCostHours > 0 ? `Parcial: ${formatHH(exp.allocatedMissingCostHours)} HH distribuídas sem custo profissional` : "Custo reconhecido por documento e vigência"} · Gestão distribuída excluída: ${formatCurrency(exp.allocatedExcludedKnownCost)} · Custo diário Engenharia: ${formatCurrency(exp.knownCost)}`} />
        <Comparacao titulo="Saldo de custo"
          atual={atual.saldoCusto === null ? "—" : formatCurrency(atual.saldoCusto)}
          lab={saldoCustoLab === null ? "—" : formatCurrency(saldoCustoLab)}
          diferenca={delta(diffSaldoCusto, formatCurrency)} />
      </Grupo>

      <Grupo titulo="Consumo de HH" colunas={2}>
        <Card icone={Gauge} titulo="Consumo HH Resumo atual" valor={pct(atual.consumoHh)} cor="sky" barra={atual.consumoHh}
          detalhe="HH realizado no espelho gerencial ÷ HH previsto" />
        <Card icone={Gauge} titulo="Consumo HH Timesheet por documento" valor={pct(consumoHhLab)} cor="violet" barra={consumoHhLab}
          detalhe="HH distribuídas de Engenharia ÷ HH previsto" />
      </Grupo>

      <Grupo titulo="Consumo de custo" colunas={2}>
        <Card icone={Gauge} titulo="Consumo custo Resumo atual" valor={pct(atual.consumoCusto)} cor="sky" barra={atual.consumoCusto}
          detalhe="Custo realizado no espelho gerencial ÷ custo previsto" />
        <Card icone={Gauge} titulo="Consumo custo Timesheet por documento" valor={pct(consumoCustoLab)} cor="violet" barra={consumoCustoLab}
          detalhe={exp.allocatedMissingCostHours > 0
            ? `Custo parcial: ${formatHH(exp.allocatedMissingCostHours)} HH distribuídas sem taxa`
            : "Custo das HH distribuídas de Engenharia ÷ custo previsto"} />
      </Grupo>

      <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
        {atual.tiposLdp.map((t) => (
          <span key={t} className="rounded-full border border-border bg-card/60 px-2.5 py-0.5 font-bold">{rotuloTipoLdp(t)}</span>
        ))}
        {ldp.erro && <span className="text-amber-500">{ldp.erro}</span>}
        {!unico && selection.length > 1 && <span>Detalhamento documental clicável com um projeto por vez.</span>}
      </div>

      <Grupo titulo="Documentos">
        <Card icone={FileText} titulo={doc.total.label} valor={numero(doc.total)} cor="violet" para={ldpLink(doc.total)} />
        {doc.cards.map((c) => (
          <CardDoc key={c.key} card={c} icone={ICONE_DOC[c.key] ?? FileText} cor={COR_DOC[c.key] ?? "violet"} numero={numero} link={ldpLink} />
        ))}
        <Card icone={AlertTriangle} titulo="Atrasados" valor={String(atual.documentos.atrasados)} cor="rose" para={prazo("ATRASADO")} />
        <Card icone={Target} titulo="No prazo" valor={String(atual.documentos.noPrazo)} cor="sky" para={prazo("NO_PRAZO")} />
      </Grupo>

      <Grupo titulo="Tratamento das HH sem documento">
        <Card icone={Clock} titulo="HH com documento" valor={formatHH(exp.documentHours)} cor="sky"
          detalhe="Timesheet diário (não é o rateio documental)" />
        <Card icone={Clock} titulo="HH sem documento" valor={formatHH(exp.unassignedHours)} cor="amber"
          detalhe="Timesheet diário; não incluídas automaticamente no total distribuído por documento" />
        <Card icone={CheckCircle2} titulo="AT confirmadas" valor={formatHH(exp.confirmedAtHours)} cor="emerald"
          detalhe={`Pendentes: ${formatHH(exp.pendingUnassignedHours)} HH`} />
        <Card icone={FileText} titulo="Marcadas como pacote" valor={formatHH(exp.packageMarkedHours)} cor="violet"
          detalhe="Somente classificação; regra financeira de pacote ainda não aplicada" />
      </Grupo>

      <Grupo titulo="Avanço do projeto" colunas={2}>
        <Card icone={TrendingUp} titulo="Avanço previsto" valor={pct(av.previsto)} cor="amber" para={curvaS} barra={av.previsto}
          detalhe={semanaTxt ? `Corte: fim da semana vigente (${semanaTxt})` : undefined} />
        <Card icone={TrendingUp} titulo="Avanço físico" valor={pct(av.fisico)} cor="violet" para={curvaS} barra={av.fisico}
          detalhe={atual.tiposLdp.includes(2) ? "Tipo 2: gates consolidados por documento" : undefined} />
      </Grupo>
    </div>
  );
}

function Comparacao({ titulo, atual, lab, diferenca, nota }: {
  titulo: string;
  atual: string;
  lab: string;
  diferenca: string;
  nota?: string;
}) {
  return (
    <div className="rounded-xl border border-border border-t-4 border-t-violet-500 bg-card/60 p-4 shadow-sm">
      <p className="text-[11px] font-black uppercase tracking-wider text-violet-500">{titulo}</p>
      <div className="mt-3 grid grid-cols-2 divide-x divide-border/70">
        <div className="min-w-0 pr-2">
          <span className="block text-[10px] text-muted-foreground">Resumo atual (espelho)</span>
          <b className="mt-1 block break-words text-lg font-black tabular-nums text-foreground">{atual}</b>
        </div>
        <div className="min-w-0 pl-3">
          <span className="block text-[10px] text-violet-500">Timesheet documental · Engenharia</span>
          <b className="mt-1 block break-words text-lg font-black tabular-nums text-foreground">{lab}</b>
        </div>
      </div>
      <p className="mt-3 text-[10px] text-muted-foreground">Diferença Timesheet − espelho: {diferenca}</p>
      {nota && <p className="mt-1 text-[10px] text-muted-foreground">{nota}</p>}
    </div>
  );
}

const Grupo = ({ titulo, children, colunas }: { titulo: string; children: ReactNode; colunas?: 2 | 3 }) => (
  <section aria-label={titulo}>
    <h2 className="mb-2 text-[10px] font-black uppercase tracking-[0.25em] text-muted-foreground">{titulo}</h2>
    <div className={cn("grid grid-cols-1 gap-3",
      colunas === 3 ? "lg:grid-cols-3" :
        colunas === 2 ? "md:grid-cols-2" :
          "sm:grid-cols-2 xl:grid-cols-4 2xl:grid-cols-5"
    )}>{children}</div>
  </section>
);

const CORES = {
  violet: "border-t-violet-500 text-violet-500",
  emerald: "border-t-emerald-500 text-emerald-500",
  sky: "border-t-sky-500 text-sky-500",
  rose: "border-t-rose-500 text-rose-500",
  slate: "border-t-slate-400 text-slate-400",
  amber: "border-t-amber-500 text-amber-500",
} as const;

function CardDoc({ card, icone: Icone, cor, numero, link }: {
  card: CardDocumental;
  icone: LucideIcon;
  cor: keyof typeof CORES;
  numero: (n: NumeroLdp | null | undefined) => string;
  link: (n?: NumeroLdp) => string | undefined;
}) {
  const [borda, texto] = CORES[cor].split(" ");
  const valorPara = card.valor ? link(card.valor) : undefined;
  const valor = card.valor && <span className="mt-2 block truncate text-4xl font-black text-foreground tabular-nums">{numero(card.valor)}</span>;
  return (
    <div className={cn("flex flex-col rounded-xl border border-border border-t-4 bg-card/60 p-4 shadow-sm", borda)}>
      <span className={cn("flex items-center gap-1.5 text-[11px] font-black uppercase tracking-wider", texto)}>
        <Icone className="h-3.5 w-3.5" />{card.titulo}
      </span>
      {card.valor && (valorPara ? <Link to={valorPara} className="rounded-md hover:text-primary">{valor}</Link> : valor)}
      {card.gates.length > 0 && (
        <div className="mt-4 grid grid-cols-3 divide-x divide-border/70">
          {card.gates.map((g) => {
            const para = link(g);
            const corpo = <><span className="block text-base font-black text-muted-foreground">{g.label}</span><span className="mt-1 block text-2xl font-black text-foreground">{numero(g)}</span></>;
            return para ? <Link key={g.label} to={para} className="px-1 py-1 text-center hover:text-primary">{corpo}</Link>
              : <div key={g.label} className="px-1 py-1 text-center">{corpo}</div>;
          })}
        </div>
      )}
    </div>
  );
}

function Card({ icone: Icone, titulo, valor, detalhe, cor, para, barra }: {
  icone: LucideIcon;
  titulo: string;
  valor: string;
  detalhe?: string;
  cor: keyof typeof CORES;
  para?: string;
  barra?: number | null;
}) {
  const [borda, texto] = CORES[cor].split(" ");
  const cabecalho = (
    <>
      <span className={cn("flex items-center gap-1.5 text-[11px] font-black uppercase tracking-wider", texto)}>
        <Icone className="h-3.5 w-3.5" />{titulo}
      </span>
      <span className="mt-2 block truncate text-3xl font-black text-foreground" title={valor}>{valor}</span>
    </>
  );

  return (
    <div className={cn("flex flex-col rounded-xl border border-border border-t-4 bg-card/60 p-4 shadow-sm", borda)}>
      {para ? <Link to={para} className="block rounded-md hover:text-primary">{cabecalho}</Link> : <div>{cabecalho}</div>}
      {barra != null && Number.isFinite(barra) && (
        <span className="mt-2 block h-1.5 w-full rounded-full bg-muted">
          <span className={cn("block h-1.5 rounded-full", barra > 100 ? "bg-rose-500" : "bg-primary")}
            style={{ width: `${Math.min(100, Math.max(0, barra))}%` }} />
        </span>
      )}
      {detalhe && <span className="mt-1.5 block text-[10px] leading-snug text-muted-foreground">{detalhe}</span>}
    </div>
  );
}
