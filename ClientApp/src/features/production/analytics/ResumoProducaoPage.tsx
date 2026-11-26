// CONTROLE DE PRODUÇÃO → RESUMO (ajuste 30/09/2026). NENHUMA regra própria:
//   • Documentos (Emitidos / Cancelados / Em Análise do Cliente / A Emitir; Tipo 2 em 30/60/90) = RESULTADO da ABA LDP, mesmos nomes;
//   • Atrasados / No prazo = partição da Gestão à Vista (a aba LDP não tem prazo);
//   • HH realizado = valor oficial do backend (coluna pronta Actual_HH_Total_Corrigido); custo = fonte atual (inalterada);
//   • Avanço = backend (PmoAvancoRules), consolidado por Σ numerador/Σ denominador entre projetos.
// Cards clicáveis abrem a aba LDP com o MESMO filtro do número (card = lista) ou a lista por prazo da Gestão à Vista.
import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, CheckCircle2, Clock, Coins, FileText, Gauge, Hourglass, MessageSquare, Target, TrendingUp, Wallet, X, type LucideIcon } from "lucide-react";
import ConsolidadoBanner from "@/components/pmo/ConsolidadoBanner";
import ResumoDisciplineCard from "./ResumoDisciplineCard";
import { resumoDisciplinas, resumoFases, type PhaseField } from "./resumoPhaseData";
import { usePmoReports, formatCurrency, formatHH, formatData } from "@/components/pmo/pmoData";
import type { FactsReport } from "@/components/pmo/pmoFacts";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { rotaPermitida } from "@/features/permissions/permissionLogic";
import { cn } from "@/lib/utils";
import { rotaProducao } from "../productionRoutes";
import { calcularResumo, consolidarAvanco, consolidarAvancoCronograma, documentalDaLdp, resumoPorProjeto, rotaLdpFiltrada, rotuloTipoLdp, type CardDocumental, type NumeroLdp, type StatusDocumento } from "./resumoLogic";
import { consolidarDocumentoOficial, STATUS_OFICIAIS } from "./documentosConsolidadoUi";
import { useLdpPorProjeto } from "./useLdpPorProjeto";

const pct = (v: number | null) => (v === null || !Number.isFinite(v) ? "—" : `${formatHH(Math.round(v * 100) / 100)}%`);

/** Lista por PRAZO (regra da Gestão à Vista) — Atrasados / No prazo. */
export const rotaDocumentosPorStatus = (status: StatusDocumento) => `${rotaProducao("lista-documentos")}?status=${status}&analise=1`;

interface Detalhe { rotulo: string; valor: string; para?: string }

export default function ResumoProducaoPage({ selection, fonteAvanco = "documentos" }: { selection: PmoSelecionado[]; fonteAvanco?: "documentos" | "cronograma" }) {
  const { reports, falhas, loading, error, retry } = usePmoReports("gestao-vista", { hub: "", project: "", selection });
  const ldp = useLdpPorProjeto(selection);
  const { can } = usePermissions();
  const [semLdpIds,setSemLdpIds] = useState<Set<string>>(new Set());
  // A fonte oficial deste marcador é dbo.Projetos.Sem_LDP via /api/me/projects.
  // Nunca inferir "Sem LDP" por Tipo_LDP=1 ou pelo número de documentos.
  useEffect(()=>{
    const ctl=new AbortController();
    fetch("/api/me/projects",{credentials:"include",signal:ctl.signal})
      .then(async res=>res.ok ? res.json() as Promise<Array<{projectIdAcc:string;semLdp?:boolean}>> : [])
      .then(items=>{if(!ctl.signal.aborted&&Array.isArray(items))
        setSemLdpIds(new Set(items.filter(x=>x.semLdp).map(x=>x.projectIdAcc)));})
      .catch(()=>{});
    return ()=>ctl.abort();
  },[]);
  const somenteSemLdp=selection.length===1 && (semLdpIds.has(selection[0].id) || (reports as FactsReport[]).some(x=>x.semLdp));
  const destino = (rota: string) => (rotaPermitida(rota.split("?")[0], "", can) ? rota : undefined);

  if (loading) return <ReportState title="Carregando Resumo…">Consultando a Gestão à Vista e a aba LDP dos projetos selecionados.</ReportState>;
  if (error) return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;

  const fr = reports as FactsReport[];
  const r = calcularResumo(fr);
  const documentoOficial = consolidarDocumentoOficial(fr);
  const valorDocumental = (v: number | null) => v === null ? "—" : String(v);
  const fases = resumoFases(fr);
  const disciplinas = resumoDisciplinas(fr);
  const detalhesFase = (field: PhaseField, format = formatHH): Detalhe[] => fases.map(f => ({rotulo: `${f.gate}%`, valor: format(f[field])}));
  const av = consolidarAvanco(fr);
  const fisicoCronograma = consolidarAvancoCronograma(fr);
  const fisicoExibido = fonteAvanco === "cronograma" ? fisicoCronograma.fisico : av.fisico;
  const doc = documentalDaLdp(ldp.projetos);
  const docsProvisorios=somenteSemLdp && fr.length===1 ? fr[0].documentos.filter(x=>x.doc4?.toUpperCase()!=="AT").length : null;
  const unico = doc.unicoProjeto;
  const ldpLink = (n?: NumeroLdp) => (unico ? destino(rotaLdpFiltrada(unico, n?.filtro)) : undefined);
  const financeiro = destino(rotaProducao("analise-financeira/visao-geral")) ?? destino(rotaProducao("controle-hh-custo"));
  const curvaS = destino(rotaProducao("curva-s"));
  const prazo = (s: StatusDocumento) => destino(rotaDocumentosPorStatus(s));
  const numero = (n: NumeroLdp | null | undefined) => (ldp.carregando ? "…" : ldp.erro || !n || n.valor === null ? "—" : String(n.valor));
  const ICONE_DOC: Record<string, LucideIcon> = { emitidos: CheckCircle2, cancelados: X, analiseCliente: MessageSquare, aEmitir: Hourglass };
  const COR_DOC: Record<string, keyof typeof CORES> = { emitidos: "emerald", cancelados: "slate", analiseCliente: "sky", aEmitir: "amber" };
  const semanaTxt = av.inicioSemana && av.fimSemana ? `${formatData(av.inicioSemana)}–${formatData(av.fimSemana)}` : null;
  const emitidosDoc = doc.cards.find(c => c.key === "emitidos");
  const canceladosDoc = doc.cards.find(c => c.key === "cancelados");
  const aEmitirDoc = doc.cards.find(c => c.key === "aEmitir");


  return (
    <div className="space-y-5">
      <ConsolidadoBanner selection={selection} falhas={falhas} />
      {fonteAvanco === "cronograma" && <div className="rounded-lg border border-sky-500/30 bg-sky-500/5 px-4 py-3 text-sm" data-testid="resumo-fonte-cronograma">
        <strong>Resumo — Avanço por Cronograma.</strong> Esta tela replica o Resumo original, mantendo documentos, HH, custos e previsão sem alterações. O avanço físico realizado usa exclusivamente o trabalho ponderado das tarefas do cronograma. A medição por status permanece preservada no Resumo original.
        {fisicoCronograma.fisico !== null && <span className="mt-1 block text-xs text-muted-foreground">{fisicoCronograma.tarefas} tarefas consideradas; {fisicoCronograma.semMedicao} tarefas com esforço sem medição válida. Fonte: {fisicoCronograma.fonte}.</span>}
      </div>}
      <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
        {somenteSemLdp
          ? <span className="rounded-full border border-amber-500/40 bg-amber-500/10 px-2.5 py-0.5 font-bold text-amber-600">Sem LDP</span>
          : r.tiposLdp.map((t) => (
            <span key={t} className="rounded-full border border-border bg-card/60 px-2.5 py-0.5 font-bold">{rotuloTipoLdp(t)}</span>
          ))}
        {!somenteSemLdp && selection.some(x=>semLdpIds.has(x.id)) &&
          <span className="rounded-full border border-amber-500/40 bg-amber-500/10 px-2.5 py-0.5 font-bold text-amber-600">Sem LDP</span>}
        {ldp.erro && <span className="text-amber-500">{ldp.erro}</span>}
        {somenteSemLdp && <span>Inventário de códigos do Timesheet corrigido. Status de emissão e planejamento ainda não disponíveis.</span>}
        {!unico && selection.length > 1 && <span>Detalhamento documental clicável com um projeto por vez.</span>}
      </div>

      {/* Faixas de leitura executiva: documento 3+3, HH 4, custo 4, avanço 2 e gráficos 2. */}
      <Grupo titulo="Documentos · Situação" colunas={3}>
        <Card icone={FileText} titulo="Total" valor={docsProvisorios!==null?String(docsProvisorios):valorDocumental(documentoOficial.situacao.total)} cor="violet" />
        <Card icone={CheckCircle2} titulo="Emitidos" valor={valorDocumental(documentoOficial.situacao.emitidos)} cor="emerald" />
        <Card icone={X} titulo="Cancelados" valor={valorDocumental(documentoOficial.situacao.cancelados)} cor="slate" />
      </Grupo>
      <Grupo titulo="Documentos · Prazos e emissão" colunas={3}>
        <Card icone={Hourglass} titulo="A emitir" valor={valorDocumental(documentoOficial.situacao.aEmitir)} cor="amber" />
        <Card icone={AlertTriangle} titulo="Atrasados" valor={somenteSemLdp?"—":String(r.documentos.atrasados)} cor="rose" para={somenteSemLdp?undefined:prazo("ATRASADO")} detalhes={detalhesFase("atrasados")} />
        <Card icone={Target} titulo="No prazo" valor={somenteSemLdp?"—":String(r.documentos.noPrazo)} cor="sky" para={somenteSemLdp?undefined:prazo("NO_PRAZO")} detalhes={detalhesFase("noPrazo")} />
      </Grupo>
      <Grupo titulo="Documentos · Status" colunas={6}>
        {STATUS_OFICIAIS.map((status, index) => (
          <Card key={status.chave} icone={index === 0 ? CheckCircle2 : index === 1 ? X : index === 2 ? MessageSquare : FileText}
            titulo={status.titulo} valor={valorDocumental(documentoOficial.status[status.chave])}
            cor={index === 0 ? "emerald" : index === 1 ? "slate" : index === 2 ? "sky" : index === 3 ? "violet" : index === 4 ? "amber" : "rose"} />
        ))}
      </Grupo>
      <Grupo titulo="Horas" colunas={4}>
        <Card icone={Gauge} titulo="Consumo HH" valor={pct(r.consumoHh)} cor="sky" para={financeiro} barra={r.consumoHh} detalhes={detalhesFase("consumoHh",pct)} />
        <Card icone={Clock} titulo="HH previsto" valor={formatHH(r.hhPrevisto)} cor="sky" para={financeiro} detalhes={detalhesFase("hhPrevisto")} />
        <Card icone={Clock} titulo="HH realizado" valor={formatHH(r.hhRealizado)} cor="sky" para={financeiro} detalhes={detalhesFase("hhRealizado")} />
        <Card icone={Clock} titulo="Saldo HH" valor={formatHH(r.saldoHh)} cor="sky" para={financeiro} detalhes={detalhesFase("saldoHh")} />
      </Grupo>
      <Grupo titulo="Custo" colunas={4}>
        <Card icone={Gauge} titulo="Consumo de custo" valor={pct(r.consumoCusto)} cor="emerald" para={financeiro} barra={r.consumoCusto}
          detalhe="Custo realizado ÷ custo previsto" detalhes={detalhesFase("consumoCusto",pct)} />
        <Card icone={Wallet} titulo="Custo previsto" valor={formatCurrency(r.custoPrevisto)} cor="emerald" para={financeiro} detalhes={detalhesFase("custoPrevisto",formatCurrency)} />
        <Card icone={Coins} titulo="Custo realizado" valor={formatCurrency(r.custoRealizado)} cor="emerald" para={financeiro} detalhes={detalhesFase("custoRealizado",formatCurrency)} />
        <Card icone={Coins} titulo="Saldo de custo" valor={formatCurrency(r.saldoCusto)} cor="emerald" para={financeiro} />
      </Grupo>
      <Grupo titulo="Avanço" colunas={2}>
        <Card icone={TrendingUp} titulo="Avanço previsto" valor={pct(av.previsto)} cor="amber" para={curvaS} barra={av.previsto}
          detalhe={semanaTxt ? "Corte: fim da semana vigente (" + semanaTxt + ")" : undefined} />
        <Card icone={TrendingUp} titulo={fonteAvanco === "cronograma" ? "Avanço realizado — Cronograma" : "Avanço realizado"} valor={pct(fisicoExibido)} cor="violet" para={curvaS} barra={fisicoExibido}
          detalhe={fonteAvanco === "cronograma" ? fisicoCronograma.fonte : r.tiposLdp.includes(2) ? "Avanço físico: gates consolidados por documento" : "Avanço físico do projeto"} />
      </Grupo>
      <section aria-label="Indicadores por disciplina">
        <h2 className="mb-3 text-sm font-bold">Disciplinas · indicadores completos</h2>
        <div className="resumo-discipline-grid">{disciplinas.map(d=><ResumoDisciplineCard key={d.idExibicao} discipline={d} money={!!financeiro} />)}</div>
        {fases.length>0&&<p className="mt-2 text-xs text-muted-foreground">—: ausência de vínculo ao gate. HH previsto usa Work do cronograma. HH realizado e custos sem vínculo ao gate permanecem apenas no consolidado.</p>}
      </section>

      {av.porFase.length > 0 && (
        // Tipo 2: avanço de cada fase, ponderado pelo esforço (backend) — mesma linguagem visual dos cards documentais.
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {(["previsto", "fisico"] as const).map((campo) => (
            <CardDoc key={campo} icone={TrendingUp} cor={campo === "previsto" ? "amber" : "violet"} link={() => undefined}
              numero={(n) => pct(n?.valor ?? null)}
              card={{ key: `avanco-${campo}`, titulo: campo === "previsto" ? "Avanço Previsto por Fase" : "Avanço Físico por Fase", valor: null,
                gates: av.porFase.map((f) => ({ label: `${f.gate}%`, valor: f[campo] })) }} />
          ))}
        </div>
      )}
      {av.regras.length > 0 && (
        <details className="text-[11px] text-muted-foreground">
          <summary className="cursor-pointer">Como o avanço é calculado</summary>
          <ul className="mt-1 list-disc pl-5">{fonteAvanco === "cronograma" && <li>Avanço físico realizado: Σ(Trabalho × % físico por tarefa) / ΣTrabalho, excluindo tarefas de resumo. O avanço por status dos documentos permanece no Resumo original.</li>}{av.regras.map((t) => <li key={t}>{fonteAvanco === "cronograma" && t.includes("Físico:") ? `Regra documental preservada (não utilizada nesta tela): ${t}` : t}</li>)}</ul>
        </details>
      )}

      {fr.length > 1 && (
        <section className="rounded-xl border border-border bg-card/40">
          <h2 className="border-b border-border px-4 py-2.5 text-[10px] font-black uppercase tracking-widest text-muted-foreground">Por projeto (cada um com a sua regra)</h2>
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="text-left text-muted-foreground">
                <tr>{["Projeto", "LDP", "Atrasados", "HH prev.", "HH real.", "Consumo HH", "Custo prev.", "Custo real.", "Avanço prev.", "Avanço físico"].map((h) => <th key={h} className="px-3 py-2 font-semibold">{h}</th>)}</tr>
              </thead>
              <tbody>
                {resumoPorProjeto(fr).map((p) => {
                  const a = fr.find((x) => x.projectId === p.projectId)?.avanco;
                  return (
                    <tr key={p.projectId} className="border-t border-border">
                      <td className="px-3 py-2 font-bold">{p.nome}</td><td className="px-3 py-2">{semLdpIds.has(p.projectId)?"Sem LDP":`Tipo ${p.tipoLdp}`}</td>
                      <td className="px-3 py-2">{p.resumo.documentos.atrasados}</td>
                      <td className="px-3 py-2">{formatHH(p.resumo.hhPrevisto)}</td><td className="px-3 py-2">{formatHH(p.resumo.hhRealizado)}</td><td className="px-3 py-2">{pct(p.resumo.consumoHh)}</td>
                      <td className="px-3 py-2">{formatCurrency(p.resumo.custoPrevisto)}</td><td className="px-3 py-2">{formatCurrency(p.resumo.custoRealizado)}</td>
                      <td className="px-3 py-2">{pct(a?.previsto ?? null)}</td><td className="px-3 py-2">{pct(fonteAvanco === "cronograma" ? fr.find(x => x.projectId === p.projectId)?.avancoCronograma?.fisico ?? null : a?.fisico ?? null)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}

const Grupo = ({ titulo, children, colunas }: { titulo: string; children: ReactNode; colunas: 2 | 3 | 4 | 6 }) => (
  <section aria-label={titulo}>
    <h2 className="mb-2 text-[10px] font-black uppercase tracking-[0.25em] text-muted-foreground">{titulo}</h2>
    <div className={cn("grid grid-cols-1 gap-3 sm:grid-cols-2",
      colunas === 6 ? "lg:grid-cols-3 xl:grid-cols-6" : colunas === 3 ? "xl:grid-cols-3" : colunas === 4 ? "xl:grid-cols-4" : "xl:grid-cols-2")}>{children}</div>
  </section>
);

const CORES = {
  // Tema: tom 600 no claro (contraste em fundo branco), 500 no escuro (visual original).
  violet: "border-t-violet-500 text-violet-600 dark:text-violet-500", emerald: "border-t-emerald-500 text-emerald-600 dark:text-emerald-500", sky: "border-t-sky-500 text-sky-600 dark:text-sky-500",
  rose: "border-t-rose-500 text-rose-600 dark:text-rose-500", slate: "border-t-slate-400 text-slate-400", amber: "border-t-amber-500 text-amber-600 dark:text-amber-500",
} as const;

/**
 * Card documental (mesmo nome da aba LDP). Tipo 1: número único. Tipo 2: 30% / 60% / 90% em destaque, cada número com o
 * SEU link (mesmo filtro da célula da matriz da aba LDP). Sem rótulos extras.
 */
function CardDoc({ card, icone: Icone, cor, numero, link }: {
  card: CardDocumental; icone: LucideIcon; cor: keyof typeof CORES;
  numero: (n: NumeroLdp | null | undefined) => string; link: (n?: NumeroLdp) => string | undefined;
}) {
  const [borda, ...corTexto] = CORES[cor].split(" "); const texto = corTexto.join(" ");
  const valorPara = card.valor ? link(card.valor) : undefined;
  const valor = card.valor && <span className="mt-2 block truncate text-4xl font-black text-foreground tabular-nums">{numero(card.valor)}</span>;
  return (
    <div className={cn("flex flex-col rounded-xl border border-border border-t-4 bg-card/60 p-4 shadow-sm", borda)}>
      <span className={cn("flex items-center gap-1.5 text-[11px] font-black uppercase tracking-wider", texto)}><Icone className="h-3.5 w-3.5" />{card.titulo}</span>
      {card.valor && (valorPara
        ? <Link to={valorPara} title={`Abrir lista: ${card.titulo}`} className="rounded-md hover:text-primary">{valor}</Link>
        : valor)}
      {card.gates.length > 0 && (
        // Tipo 2: 30% / 60% / 90% lado a lado, divisórias discretas; o NÚMERO é o maior elemento do card.
        <div className="mt-4 grid grid-cols-3 divide-x divide-border/70" data-testid={`gates-${card.key}`}>
          {card.gates.map((g) => {
            const para = link(g);
            const corpo = (
              <>
                <span className="block text-base font-black tracking-wide text-muted-foreground md:text-lg">{g.label}</span>
                <span className="mt-1 block text-2xl font-black leading-tight text-foreground tabular-nums 2xl:text-3xl">{numero(g)}</span>
              </>
            );
            return para ? (
              <Link key={g.label} to={para} title={`Abrir lista: ${card.titulo} · ${g.label}`}
                className="px-1 py-1 text-center transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">{corpo}</Link>
            ) : <div key={g.label} className="px-1 py-1 text-center">{corpo}</div>;
          })}
        </div>
      )}
    </div>
  );
}

function Card({ icone: Icone, titulo, valor, detalhe, detalhes, cor, para, barra }:
  { icone: LucideIcon; titulo: string; valor: string; detalhe?: string; detalhes?: Detalhe[]; cor: keyof typeof CORES; para?: string; barra?: number | null }) {
  const [borda, ...corTexto] = CORES[cor].split(" "); const texto = corTexto.join(" ");
  const cabecalho = (
    <>
      <span className={cn("flex items-center gap-1.5 text-[11px] font-black uppercase tracking-wider", texto)}><Icone className="h-3.5 w-3.5" />{titulo}</span>
      <span className="mt-2 block truncate text-3xl font-black text-foreground" title={valor}>{valor}</span>
    </>
  );
  return (
    <div className={cn("flex flex-col rounded-xl border border-border border-t-4 bg-card/60 p-4 shadow-sm", borda)}>
      {para ? (
        <Link to={para} title={`Abrir lista: ${titulo}`} className="block rounded-md transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">{cabecalho}</Link>
      ) : <div>{cabecalho}</div>}
      {barra != null && Number.isFinite(barra) && (
        <span className="mt-2 block h-1.5 w-full rounded-full bg-muted">
          <span className={cn("block h-1.5 rounded-full", barra > 100 ? "bg-rose-500" : "bg-primary")} style={{ width: `${Math.min(100, Math.max(0, barra))}%` }} />
        </span>
      )}
      {detalhes && detalhes.length > 0 && (
        // Tipo 2: cada fase tem o SEU número e o SEU link (mesmo filtro da célula da matriz da aba LDP).
        <div className="mt-2 grid grid-cols-3 gap-1">
          {detalhes.map((d) => {
            const corpo = <><span className="block text-[9px] font-black uppercase text-muted-foreground">{d.rotulo}</span><span className="block text-sm font-black">{d.valor}</span></>;
            return d.para ? (
              <Link key={d.rotulo} to={d.para} title={`Abrir lista: ${titulo} · ${d.rotulo}`}
                className="rounded-md border border-border bg-background/40 px-2 py-1 text-center hover:border-primary/60 hover:text-primary">{corpo}</Link>
            ) : <div key={d.rotulo} className="rounded-md border border-border bg-background/40 px-2 py-1 text-center">{corpo}</div>;
          })}
        </div>
      )}
      {detalhe && <span className="mt-1.5 block text-[10px] leading-snug text-muted-foreground">{detalhe}</span>}
    </div>
  );
}
