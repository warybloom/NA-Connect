// =============================================================================================
// RESUMO do Controle de Produção — lógica PURA. NÃO existe cálculo próprio aqui:
//   • documentos/HH/custo/consumo = projectFacts + enrichDocuments + statusCounts (MESMAS funções da Gestão à Vista,
//     sobre o MESMO payload /api/reports/gestao-vista) → os totais batem por construção;
//   • o status de cada documento (cancelado/emitido/atrasado/no prazo/sem prazo) vem PRONTO do backend
//     (PmoExecutiveData), que já trata LDP Tipo 1 (normal) e Tipo 2 (faseada/gates) — a tela não reinterpreta o tipo;
//   • HH realizado = HH realizado oficial do backend (coluna pronta Actual_HH_Total_Corrigido da VW_Ldp_Unificada);
//   • avanço previsto/físico e documentos: ver blocos abaixo (backend PmoAvancoRules / resultado da aba LDP).
// =============================================================================================
import { enrichDocuments, projectFacts, statusCounts, type EnrichedDocument, type FactsReport } from "@/components/pmo/pmoFacts";

export type StatusDocumento = Exclude<EnrichedDocument["statusDocumento"], "ATIVIDADE">;
export const STATUS_DOCUMENTO: readonly StatusDocumento[] = ["CANCELADO", "EMITIDO", "NO_PRAZO", "ATRASADO", "SEM_PRAZO"];
export const ROTULO_STATUS: Record<StatusDocumento, string> = {
  CANCELADO: "Cancelados", EMITIDO: "Emitidos", NO_PRAZO: "No prazo", ATRASADO: "Atrasados", SEM_PRAZO: "Sem prazo",
};
export const ehStatusDocumento = (v: string | null): v is StatusDocumento => !!v && (STATUS_DOCUMENTO as readonly string[]).includes(v);

export interface ResumoProducao {
  documentos: ReturnType<typeof statusCounts>;
  hhPrevisto: number | null; hhRealizado: number | null; consumoHh: number | null; saldoHh: number | null;
  custoPrevisto: number | null; custoRealizado: number | null; consumoCusto: number | null; saldoCusto: number | null;
  /** Tipos de LDP dos projetos analisados (1 = normal, 2 = faseada). */
  tiposLdp: number[];
}

/** Métricas do Resumo = exatamente os números dos cards da Gestão à Vista para os mesmos projetos. */
export function calcularResumo(reports: readonly FactsReport[]): ResumoProducao {
  const lista = [...reports];
  const f = projectFacts(lista);
  const docs = enrichDocuments(lista);
  return {
    documentos: statusCounts(docs),
    hhPrevisto: f.cards.HH_Previsto, hhRealizado: f.cards.HH_Realizado, consumoHh: f.cards.ConsumoHH, saldoHh: f.cards.SaldoHH,
    custoPrevisto: f.cards.Custo_Previsto, custoRealizado: f.cards.Custo_Realizado, consumoCusto: f.cards.ConsumoCusto, saldoCusto: f.cards.SaldoCusto,
    tiposLdp: [...new Set(lista.map((r) => r.tipoLdp))].sort(),
  };
}

export const rotuloTipoLdp = (tipo: number) => (tipo === 2 ? "LDP faseada (Tipo 2 · gates)" : tipo === 1 ? "LDP normal (Tipo 1)" : `LDP Tipo ${tipo}`);

/** Documentos de um status (drill-down dos cards) — mesma lista que a Gestão à Vista conta. */
export const documentosDoStatus = (reports: readonly FactsReport[], status: StatusDocumento) =>
  enrichDocuments([...reports]).filter((d) => d.statusDocumento === status);

// ------------------------------------------------------------------------------------------------
// AVANÇO (30/09/2026): calculado no BACKEND por projeto (PmoAvancoRules — Tipo 1 = Curva S inalterada no físico;
// Tipo 2 = gates consolidados por documento; previsto com corte no fim da semana vigente). Aqui só se CONSOLIDA vários
// projetos: Σ numeradores ÷ Σ denominadores (cada projeto já resolvido pela sua regra — nunca mistura linhas).
// ------------------------------------------------------------------------------------------------
export interface AvancoFaseConsolidado { gate: number; previsto: number | null; fisico: number | null }
export interface AvancoConsolidado {
  previsto: number | null; fisico: number | null; inicioSemana: string | null; fimSemana: string | null; regras: string[];
  /** Tipo 2: por fase (30/60/90), Σ numeradores ÷ Σ denominadores dos projetos (nunca média de percentuais). Vazio sem Tipo 2. */
  porFase: AvancoFaseConsolidado[];
}

export function consolidarAvanco(reports: readonly FactsReport[]): AvancoConsolidado {
  const av = reports.map((r) => r.avanco).filter((a): a is NonNullable<FactsReport["avanco"]> => !!a);
  const pct = (num: number, den: number) => (den > 0 ? Math.round((num / den) * 10000) / 100 : null);
  const soma = (f: (a: (typeof av)[number]) => number) => av.reduce((t, a) => t + f(a), 0);
  return {
    previsto: av.length ? pct(soma((a) => a.previstoNumerador), soma((a) => a.previstoDenominador)) : null,
    fisico: av.length ? pct(soma((a) => a.fisicoNumerador), soma((a) => a.fisicoDenominador)) : null,
    inicioSemana: av[0]?.inicioSemanaVigente ?? null, fimSemana: av[0]?.fimSemanaVigente ?? null,
    regras: [...new Set(av.flatMap((a) => [a.regraPrevisto, a.regraFisico, `Ponderação: ${a.ponderacao}`]))],
    porFase: av.some((a) => a.porFase && a.porFase.length > 0)
      ? [30, 60, 90].map((gate) => {
          const fs = av.flatMap((a) => (a.porFase ?? []).filter((f) => f.gate === gate));
          const s = (k: "previstoNumerador" | "previstoDenominador" | "fisicoNumerador" | "fisicoDenominador") => fs.reduce((t, f) => t + f[k], 0);
          return { gate, previsto: pct(s("previstoNumerador"), s("previstoDenominador")), fisico: pct(s("fisicoNumerador"), s("fisicoDenominador")) };
        })
      : [],
  };
}

// ------------------------------------------------------------------------------------------------
// DOCUMENTOS = ABA LDP (30/09/2026): os cards documentais do Resumo usam o RESULTADO da própria aba LDP
// (/api/reports/production/dashboard/ldp — KPIs e matriz "Status por fase"), com os MESMOS nomes. Cada projeto é
// resolvido pela sua definição (Tipo 1 ou Tipo 2) e só depois somado. Filtro de cada número = filtro do drill-down.
// ------------------------------------------------------------------------------------------------
export interface LdpKpi { key: string; label: string; value: number | null; filterDimension: string | null; filterValue: string | null }
export interface LdpCelula { row: string; column: string; value: number; filterDimension: string; filterValue: string }
export interface LdpProjeto { projectId: string; hubId: string; nome: string; tipoLdp: number; kpis: LdpKpi[]; celulas: LdpCelula[] }

export interface NumeroLdp { label: string; valor: number | null; filtro?: { dimensao: string; valor: string } }
/** Card documental: valor (projetos Tipo 1, KPI da aba LDP) e/ou gates 30/60/90 (projetos Tipo 2, matriz da aba LDP). */
export interface CardDocumental { key: string; titulo: string; valor: NumeroLdp | null; gates: NumeroLdp[] }
export interface DocumentalLdp { total: NumeroLdp; cards: CardDocumental[]; tipos: number[]; unicoProjeto: LdpProjeto | null }

export const GATES_LDP = ["30", "60", "90"] as const;

/**
 * Os quatro cards com os MESMOS nomes da aba LDP (linhas da matriz "Status por fase"): cada um aponta para o KPI do
 * Tipo 1 e para a linha da matriz do Tipo 2 que a aba LDP usa para a mesma classificação.
 */
export const CARDS_DOCUMENTAIS = [
  { key: "emitidos", titulo: "Emitidos", kpiTipo1: "emitidos", linhaTipo2: "emitidos" },
  { key: "cancelados", titulo: "Cancelados", kpiTipo1: "cancelados", linhaTipo2: "cancelados" },
  { key: "analiseCliente", titulo: "Em Análise do Cliente", kpiTipo1: "revisaoCliente", linhaTipo2: "analiseCliente" },
  { key: "aEmitir", titulo: "A Emitir", kpiTipo1: "aEmitir", linhaTipo2: "aEmitir" },
] as const;

export function documentalDaLdp(projetos: readonly LdpProjeto[]): DocumentalLdp {
  const unico = projetos.length === 1 ? projetos[0] : null;
  const somar = (vs: (number | null | undefined)[]) => { const k = vs.filter((v): v is number => typeof v === "number"); return k.length ? k.reduce((a, b) => a + b, 0) : null; };
  const t1 = projetos.filter((p) => p.tipoLdp !== 2);
  const t2 = projetos.filter((p) => p.tipoLdp === 2);
  const kpi = (p: LdpProjeto, key: string) => p.kpis.find((k) => k.key === key);
  const filtroKpi = (key: string) => {
    const k = unico && unico.tipoLdp !== 2 ? kpi(unico, key) : undefined;
    return k?.filterDimension && k.filterValue ? { dimensao: k.filterDimension, valor: k.filterValue } : undefined;
  };
  const gate = (linha: string, g: string): NumeroLdp => {
    const cs = t2.flatMap((p) => p.celulas.filter((c) => c.column === g && c.row === linha));
    return { label: `${g}%`, valor: somar(cs.map((c) => c.value)), filtro: unico && cs[0] ? { dimensao: cs[0].filterDimension, valor: cs[0].filterValue } : undefined };
  };
  const totalKpi = unico ? kpi(unico, "total") : undefined;
  return {
    total: { label: totalKpi?.label ?? "Total Documentos", valor: somar(projetos.map((p) => kpi(p, "total")?.value)) },
    cards: CARDS_DOCUMENTAIS.map((c) => ({
      key: c.key, titulo: c.titulo,
      valor: t1.length ? { label: c.titulo, valor: somar(t1.map((p) => kpi(p, c.kpiTipo1)?.value)), filtro: filtroKpi(c.kpiTipo1) } : null,
      gates: t2.length ? GATES_LDP.map((g) => gate(c.linhaTipo2, g)) : [],
    })),
    tipos: [...new Set(projetos.map((p) => p.tipoLdp))].sort(), unicoProjeto: unico,
  };
}

/** Rota da aba LDP já filtrada (mesmo filtro que gerou o número) — drill-down do card. */
export const rotaLdpFiltrada = (p: { hubId: string; projectId: string }, filtro?: { dimensao: string; valor: string }) => {
  const q = new URLSearchParams({ hub: p.hubId, projeto: p.projectId });
  if (filtro) q.set(`f.${filtro.dimensao}`, filtro.valor);
  return `/controle-producao/ldp?${q}`;
};

/** Por projeto (análise multiprojeto): cada projeto com seus próprios cards. */
export const resumoPorProjeto = (reports: readonly FactsReport[]) =>
  reports.map((r) => ({ projectId: r.projectId, nome: r.nomeProjeto, tipoLdp: r.tipoLdp, resumo: calcularResumo([r]) }));
