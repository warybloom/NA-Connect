// Medidas DAX do PBIX transcritas 1:1 (fonte: DataModel do CHS001-000-PM0-RT-002-01.pbix, lido com pbixray em 15/10/2026).
// Cada medida recebe as linhas JÁ filtradas pelo contexto do visual (filtros de página + visual + segmentações) e, no
// contexto, as linhas de ALLSELECTED (as mesmas, sem o agrupamento do próprio visual) e a data de corte (dSemana).
// Retorno null = BLANK do DAX (o cartão exibe "(Blank)" como no Power BI).
import { ehBlank, igualPbi, type Linha } from "./filtrosPbi";
import type { FormatoValor, Valor } from "./tipos";
import { MEDIDAS_CURVAS } from "./curvas";

export interface ContextoMedida {
  /** ALLSELECTED(entidade): linhas do visual inteiro (página + visual + segmentações), sem o agrupamento de linha/categoria. */
  selecionadas: Linha[];
  /** KPI | CORTE | Data Corte = SELECTEDVALUE('dSemana'[Data]) — "yyyy-MM-dd" ou null. */
  dataCorte: string | null;
  /** SELECTEDVALUE(dCalendario[Data Corte]) do ponto do eixo (curvas) — "yyyy-MM-dd". */
  eixo?: string | null;
  /** AssignmentByDay indexado por WBX[NodeKey] (curvas.ts). */
  serie?: import("./curvas").SerieTarefas;
  /** VALUES('LDP (4)'[N&A DOCUMENT NUMBER]) com FORMAT = "3D" no contexto da página (maiúsculas) — TREATAS das medidas 3D. */
  documentos3D?: Set<string>;
}

export interface Medida {
  /** Entidade-base cujas linhas a medida percorre; null = medida que só depende do contexto (sem fonte de dados). */
  entidade: string | null;
  formato: FormatoValor;
  calcular: (linhas: Linha[], ctx?: ContextoMedida) => Valor;
  /** DAX original (documentação/auditoria). */
  dax: string;
  /** Fontes obrigatórias: colunas de WBX com dado e/ou a série AssignmentByDay. Faltando → PENDENTE (nunca BLANK falso). */
  requer?: { colunas: string[]; serie?: boolean; documentos3D?: boolean };
}

/** COUNTROWS de tabela vazia = BLANK. */
const contar = (linhas: Linha[], pred: (l: Linha) => boolean): number | null => {
  const n = linhas.filter(pred).length;
  return n === 0 ? null : n;
};

function fase(n: 30 | 60 | 90): Record<string, Medida> {
  const ds = `DOCUMENT STATUS ${n}%`, sit = `SITUATION ${n}%`, tab = `dFase Engenharia ${n}%`;
  const m = (dax: string, pred: (l: Linha) => boolean): Medida => ({ entidade: "LDP (4)", formato: "inteiro", dax, calcular: (ls) => contar(ls, pred) });
  return {
    [`${tab}.Documents ${n}`]: m(`CALCULATE(COUNTROWS('LDP (4)'), NOT ISBLANK('LDP (4)'[${ds}]))`, (l) => !ehBlank(l[ds])),
    [`${tab}.ISSUED ${n}`]: m(`CALCULATE(COUNTROWS('LDP (4)'), 'LDP (4)'[${sit}] = "Issued")`, (l) => igualPbi(l[sit], "Issued")),
    // DAX compara texto sem diferenciar maiúsculas, mas "Canceled" ≠ "CANCELLED" (grafia diferente) — exatamente como no BI.
    [`${tab}.CANCELED ${n}`]: m(`CALCULATE(COUNTROWS('LDP (4)'), 'LDP (4)'[${sit}] = "Canceled")`, (l) => igualPbi(l[sit], "Canceled")),
    [`${tab}.CLIENT REVIEW ${n}`]: m(`CALCULATE(COUNTROWS('LDP (4)'), 'LDP (4)'[${ds}] = "CLIENT REVIEW")`, (l) => igualPbi(l[ds], "CLIENT REVIEW")),
    [`${tab}.ADRESSING ${n}`]: m(`CALCULATE(COUNTROWS('LDP (4)'), 'LDP (4)'[${ds}] = "ADRESSING COMMENTS")`, (l) => igualPbi(l[ds], "ADRESSING COMMENTS")),
  };
}

// ------------------------------------------------------------------------------------------- datas (DAX)

/** Parte de data de um datetime ISO ("yyyy-MM-dd[THH:mm:ss]") → nº do dia (DATEDIFF(..., DAY) conta fronteiras de dia). */
function dia(v: Valor | undefined): number | null {
  if (typeof v !== "string") return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(v);
  return m ? Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])) / 86_400_000 : null;
}

const datediff = (a: Valor, b: Valor): number | null => {
  const x = dia(a), y = dia(b);
  return x === null || y === null ? null : y - x;
};

/** MAX/MIN de uma coluna datetime (texto ISO da fonte) — linhas sem data não entram; nenhuma = BLANK. */
function extremo(linhas: Linha[], coluna: string, maior: boolean): Valor {
  let melhor: string | null = null;
  for (const l of linhas) {
    const v = l[coluna];
    if (typeof v !== "string" || dia(v) === null) continue;
    if (melhor === null || (maior ? v > melhor : v < melhor)) melhor = v;
  }
  return melhor;
}

function ddMM(serial: number): string {
  const d = new Date(serial * 86_400_000);
  return `${String(d.getUTCDate()).padStart(2, "0")}/${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
}

// ------------------------------------------------------------------------------------------- KPI | PRAZO

function prazo(n: 30 | 60 | 90): Record<string, Medida> {
  const gate = `Engineering - ${n}%`;
  // CALCULATE(MAX(...), FILTER(ALLSELECTED('WBX'), 'WBX'[Level 4] = "Engineering - n%")) — ALLSELECTED: ignora o agrupamento
  // do visual (cada linha de uma matriz mostra o mesmo valor) e mantém página/segmentações/filtros do visual.
  const doGate = (ctx: ContextoMedida | undefined, linhas: Linha[]) =>
    (ctx?.selecionadas ?? linhas).filter((l) => igualPbi(l["Level 4"], gate));
  const terminoBaseline = (ls: Linha[], ctx?: ContextoMedida) => extremo(doGate(ctx, ls), "Término da Linha de Base", true);
  const terminoReprogramado = (ls: Linha[], ctx?: ContextoMedida) => extremo(doGate(ctx, ls), "Término", true);
  const k = `KPI.KPI ${n} | PRAZO |`;
  return {
    [`${k} Término Baseline`]: {
      entidade: "WBX", formato: "data", calcular: terminoBaseline,
      dax: `CALCULATE(MAX('WBX'[Término da Linha de Base]), FILTER(ALLSELECTED('WBX'), 'WBX'[Level 4] = "${gate}"))`,
    },
    [`${k} Término Reprogramado`]: {
      entidade: "WBX", formato: "data", calcular: terminoReprogramado,
      dax: `CALCULATE(MAX('WBX'[Término]), FILTER(ALLSELECTED('WBX'), 'WBX'[Level 4] = "${gate}"))`,
    },
    [`${k} Desvio`]: {
      entidade: "WBX", formato: "inteiro",
      calcular: (ls, ctx) => {
        const tb = terminoBaseline(ls, ctx), tr = terminoReprogramado(ls, ctx);
        return tb === null || tr === null ? null : datediff(tb, tr);
      },
      dax: `IF(ISBLANK(TerminoBaseline) || ISBLANK(TerminoReprogramado), BLANK(), DATEDIFF(TerminoBaseline, TerminoReprogramado, DAY))`,
    },
    [`${k} Dias Restantes`]: {
      entidade: "WBX", formato: "inteiro",
      calcular: (ls, ctx) => {
        const corte = ctx?.dataCorte ?? null, tr = terminoReprogramado(ls, ctx);
        if (corte === null || tr === null) return null;
        const d = datediff(corte, tr);
        return d === null ? null : Math.max(0, d);
      },
      dax: `IF(ISBLANK(DataCorte) || ISBLANK(TerminoReprogramado), BLANK(), MAX(0, DATEDIFF(DataCorte, TerminoReprogramado, DAY)))`,
    },
  };
}

export const MEDIDAS: Record<string, Medida> = {
  ...fase(30), ...fase(60), ...fase(90),
  ...prazo(30), ...prazo(60), ...prazo(90),
  ...MEDIDAS_CURVAS,
  "KPI.Activity Title": {
    entidade: null, formato: "texto",
    calcular: (_ls, ctx) => {
      const sel = dia(ctx?.dataCorte ?? null);
      return sel === null ? "Weekly Progress" : `Week - ${ddMM(sel - 6)} to ${ddMM(sel)}`;
    },
    dax: `VAR DataSelecionada = SELECTEDVALUE('dSemana'[Data]) RETURN IF(ISBLANK(DataSelecionada), "Weekly Progress",
      "Week - " & FORMAT(DataSelecionada - 6, "dd/MM") & " to " & FORMAT(DataSelecionada, "dd/MM"))`,
  },
};

export const chaveMedida = (entidade: string | undefined, prop: string | undefined) => `${entidade}.${prop}`;

// ---------------------------------------------------------------------------- medidas SEM fonte (motivo exibido)

const SEM_ASSIGNMENT_BY_DAY = "usa AssignmentByDay (trabalho real/LB por dia do .mpp) — não existe fonte equivalente no banco";
const SEM_INICIO = "usa WBX[Início]/[Início da Linha de Base] — não persistidos no cronograma do banco";

/** Medida do PBIX que NÃO pode ser reproduzida com as fontes atuais → motivo, sem o nome (nunca aproximação). */
export function motivoMedidaSemFonte(entidade: string | undefined, prop: string | undefined): string | undefined {
  if (entidade !== "KPI" && entidade !== "k" || !prop) return undefined;
  if (/\| PRAZO \| (Início Baseline|Início Reprogramado|Duração Baseline)$/.test(prop)) return SEM_INICIO;
  if (/\| ACC \| % Planejado$/.test(prop)) return `${SEM_INICIO}; e WBX[Trabalho] é nulo`;
  if (/\| CURVA \| Mostrar Período$/.test(prop)) return SEM_INICIO;
  if (/\| (ACC|BASE SEM|CURVA|COLUNA|CARD) \|/.test(prop) || /\| 3D \|/.test(prop)) return SEM_ASSIGNMENT_BY_DAY;
  return undefined;
}
