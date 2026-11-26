// Medidas de CURVA / ACC / BASE SEM / COLUNA dos Resumos 30/60/90 — transcrição 1:1 do DAX do PBIX (MAPA_MEDIDAS_DAX.md e
// DataModel lido com pbixray em 15/10/2026). Atenção: as famílias NÃO são iguais entre os gates:
//   • 60%: planejado por HORAS ÚTEIS (8–12h e 13–17h, seg–sex, exceto 01/05/2026), corte às 17h do dia do eixo;
//   • 30%/90%: avanço LINEAR por dia corrido (DATEDIFF em dias) e datas de avaliação no domingo seguinte.
// Fontes: WBX[Início], [Início da Linha de Base], [Trabalho], [Trab Linha de Base], [Trabalho Real] e a série
// AssignmentByDay[TaskUID, TimeByDay, Trabalho Real] (relacionada a WBX[NodeKey]). Sem essas fontes a medida NÃO é calculada:
// o motor exibe PENDENTE com o campo faltante (Medida.requer) — nunca aproximação.
import { igualPbi, type Linha } from "./filtrosPbi";
import type { ContextoMedida, Medida } from "./medidas";
import type { Valor } from "./tipos";

const DIA = 86_400_000;
/** "yyyy-MM-dd[THH:mm[:ss]]" → nº de dias (com fração de hora) desde 1970-01-01, sem fuso. */
export function serial(v: Valor | undefined): number | null {
  if (typeof v !== "string") return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec(v);
  if (!m) return null;
  return Date.UTC(+m[1], +m[2] - 1, +m[3], +(m[4] ?? 0), +(m[5] ?? 0), +(m[6] ?? 0)) / DIA;
}
export const isoDeSerial = (s: number) => new Date(Math.round(s * DIA)).toISOString().slice(0, 19);
const dataDeSerial = (s: number) => isoDeSerial(s).slice(0, 10);
/** dCalendario[Início da Semana]: domingo da semana (Power Query Date.StartOfWeek com domingo). */
export const inicioSemana = (s: number) => { const d = Math.floor(s); return d - ((new Date(d * DIA).getUTCDay()) % 7); };
const datediff = (a: number, b: number) => Math.floor(b) - Math.floor(a);
const num = (v: Valor | undefined): number | null => (typeof v === "number" && Number.isFinite(v) ? v : null);

// ------------------------------------------------------------------------------------------- aritmética de BLANK do DAX

/** SUM/SUMX de valores possivelmente BLANK: nenhum valor = BLANK. */
function soma(vals: (number | null)[]): number | null {
  let s = 0, algum = false;
  for (const v of vals) if (v !== null) { s += v; algum = true; }
  return algum ? s : null;
}
/** a − b com BLANK: BLANK − BLANK = BLANK; BLANK conta 0 se o outro existe. */
const sub = (a: number | null, b: number | null) => (a === null && b === null ? null : (a ?? 0) - (b ?? 0));
/** DIVIDE(a, b, alternativo). */
const divide = (a: number | null, b: number | null, alt: number | null = null) => (b === null || b === 0 ? alt : a === null ? null : a / b);
/** "x = 0" do DAX: BLANK = 0 é verdadeiro. */
const zero = (x: number | null) => x === null || x === 0;
const minimo1 = (x: number | null) => (x === null ? null : Math.min(1, x));
/** MIN(1, x) do DAX com dois escalares: BLANK conta como 0 (MIN(1, BLANK()) = 0). Usado nas medidas 3D. */
const minimo1Dax = (x: number | null) => Math.min(1, x ?? 0);

// ------------------------------------------------------------------------------------------- série AssignmentByDay

/** AssignmentByDay indexado por WBX[NodeKey]: [TimeByDay (serial), Trabalho Real]. */
export type SerieTarefas = Map<string, [number, number][]>;

export function indexarSerie(linhas: Linha[]): SerieTarefas {
  const m: SerieTarefas = new Map();
  for (const l of linhas) {
    const t = serial(l.TimeByDay), v = num(l["Trabalho Real"]), k = l.TaskUID;
    if (t === null || v === null || k === null || k === undefined) continue;
    const arr = m.get(String(k)) ?? [];
    arr.push([t, v]);
    m.set(String(k), arr);
  }
  return m;
}

/** CALCULATE(SUM(AssignmentByDay[Trabalho Real]), <WBX do contexto>, FILTER(ALL(TimeByDay), cond)). */
function real(gl: Linha[], serie: SerieTarefas | undefined, cond: (t: number) => boolean): number | null {
  if (!serie) return null;
  const vals: number[] = [];
  for (const l of gl) for (const [t, v] of serie.get(String(l.NodeKey ?? "")) ?? []) if (cond(t)) vals.push(v);
  return soma(vals);
}

// ------------------------------------------------------------------------------------------- contexto comum

const gateDe = (n: number) => `Engineering - ${n}%`;
/** FILTER(ALLSELECTED('WBX'), Level 4 = gate) / KEEPFILTERS(Level 4 = gate) — linhas do visual sem agrupamento. */
const doGate = (n: number, ls: Linha[], ctx?: ContextoMedida) => (ctx?.selecionadas ?? ls).filter((l) => igualPbi(l["Level 4"], gateDe(n)));
const somaCol = (gl: Linha[], col: string) => soma(gl.map((l) => num(l[col])));
const corte = (ctx?: ContextoMedida) => serial(ctx?.dataCorte ?? null);
/** SELECTEDVALUE(dCalendario[Data Corte]) no eixo do gráfico (quinta-feira) ou, nos cartões, o TREATAS da medida. */
const eixo = (ctx?: ContextoMedida) => serial(ctx?.eixo ?? null);

// ------------------------------------------------------------------------------------------- 60%: horas úteis

const FERIADO_60 = serial("2026-05-01")!;

function fracaoUtil(ini: number, fim: number, c: number): number | null {
  if (c >= fim) return 1;
  if (c <= ini) return 0;
  let dur = 0, dec = 0;
  for (let d = Math.floor(ini); d <= Math.floor(fim); d++) {
    const dow = new Date(d * DIA).getUTCDay();
    if (dow === 0 || dow === 6 || d === FERIADO_60) continue;
    for (const [a, b] of [[8, 12], [13, 17]]) {
      const ja = d + a / 24, jb = d + b / 24;
      dur += Math.max(0, Math.min(fim, jb) - Math.max(ini, ja));
      dec += Math.max(0, Math.min(c, jb) - Math.max(ini, ja));
    }
  }
  return dur === 0 ? null : dec / dur;
}

/** KPI 60 | CURVA | % Baseline (ci/cf = Início/Término da Linha de Base) ou % Reprogramado (Início/Término). */
function curva60(gl: Linha[], dataEixo: number | null, ci: string, cf: string): number | null {
  if (dataEixo === null) return null;
  const c = Math.floor(dataEixo) + 17 / 24;
  const at = gl.filter((l) => (l["É Resumo"] as unknown) !== true);
  if (at.length === 0) return null;
  const invalidas = at.some((l) => {
    const p = num(l.Trabalho) ?? 0, i = serial(l[ci]), f = serial(l[cf]);
    return p < 0 || (p > 0 && (i === null || f === null || f < i));
  });
  if (invalidas) return null;
  const total = somaCol(at, "Trabalho");
  let previsto = 0;
  for (const l of at) {
    const p = num(l.Trabalho), i = serial(l[ci]), f = serial(l[cf]);
    if (p === null || p <= 0 || i === null || f === null || f < i) continue;
    const fr = fracaoUtil(i, f, c);
    if (fr !== null) previsto += p * fr;
  }
  return divide(previsto, total, 1);
}

// ------------------------------------------------------------------------------------------- 30/90%: linear por dia

function avanco(i: number | null, f: number | null, data: number): number {
  if (i === null || f === null) return 0;
  if (data < i) return 0;
  if (data >= f) return 1;
  const dur = datediff(i, f);
  if (dur <= 0) return 1;
  return datediff(i, data) / dur;
}
const planejado = (gl: Linha[], ci: string, cf: string, data: number) =>
  soma(gl.map((l) => { const p = num(l.Trabalho); return p === null ? null : p * avanco(serial(l[ci]), serial(l[cf]), data); }));

// ------------------------------------------------------------------------------------------- registro

const W = "WBX";
const REQ_PLAN = { colunas: ["Início", "Término", "Trabalho"] };
const REQ_BASE = { colunas: ["Início da Linha de Base", "Término da Linha de Base", "Trabalho"] };
const REQ_REAL = { colunas: ["Trab Linha de Base", "Trabalho Real"], serie: true };
const REQ_ACC_REAL = { colunas: ["Trab Linha de Base", "Trabalho Real"] };
const unir = (...rs: { colunas: string[]; serie?: boolean }[]) =>
  ({ colunas: [...new Set(rs.flatMap((r) => r.colunas))], serie: rs.some((r) => r.serie) });

function familia(n: 30 | 60 | 90): Record<string, Medida> {
  const k = `KPI.KPI ${n} |`;
  const g = (ls: Linha[], ctx?: ContextoMedida) => doGate(n, ls, ctx);
  const LB = (gl: Linha[]) => somaCol(gl, "Trab Linha de Base");

  const accRealizado = (ls: Linha[], ctx?: ContextoMedida) => {
    const gl = g(ls, ctx), lb = LB(gl);
    return zero(lb) ? null : minimo1(divide(somaCol(gl, "Trabalho Real"), lb, 0));
  };

  // Planejado: 60 = curva por horas úteis avaliada na data de corte; 30/90 = linear até o domingo seguinte.
  const accPlanejado = (ls: Linha[], ctx?: ContextoMedida) => {
    const dc = corte(ctx);
    if (dc === null) return null;
    const gl = g(ls, ctx);
    if (n === 60) return curva60(gl, dc, "Início", "Término");
    const total = somaCol(gl, "Trabalho");
    return zero(total) ? null : divide(planejado(gl, "Início", "Término", inicioSemana(dc) + 7), total, 0);
  };

  const baseSemPlanejado = (ls: Linha[], ctx?: ContextoMedida) => {
    const dc = corte(ctx);
    if (dc === null) return null;
    const gl = g(ls, ctx);
    if (n === 60) {
      const a = curva60(gl, dc, "Início", "Término"), b = curva60(gl, dc - 7, "Início", "Término");
      return a === null || b === null ? null : a - b;
    }
    const total = somaCol(gl, "Trabalho"), is = inicioSemana(dc);
    return zero(total) ? null : divide(sub(planejado(gl, "Início", "Término", is + 7), planejado(gl, "Início", "Término", is)), total, 0);
  };

  const baseSemRealizado = (ls: Linha[], ctx?: ContextoMedida) => {
    const dc = corte(ctx);
    const gl = g(ls, ctx), lb = LB(gl);
    if (dc === null || zero(lb)) return null;
    const is = inicioSemana(dc);
    const ate = n === 60 ? real(gl, ctx?.serie, (t) => t < Math.floor(dc) + 1) : real(gl, ctx?.serie, (t) => t <= dc);
    const semana = sub(ate, real(gl, ctx?.serie, (t) => t < is));
    if (n === 90 && zero(semana)) return null;
    return divide(semana, lb, 0);
  };

  const desvio = (p: number | null, r: number | null, exigePlanejado = false) =>
    p === null || r === null || (exigePlanejado && p === 0) ? null : r - p;

  // ---------------------------------------------------------------- gráfico (eixo dCalendario[Data Corte])
  const curvaPlanejada = (ci: string, cf: string) => (ls: Linha[], ctx?: ContextoMedida) => {
    const e = eixo(ctx);
    if (e === null) return null;
    const gl = g(ls, ctx);
    if (n === 60) return curva60(gl, e, ci, cf);
    const dataEixo = inicioSemana(e) + 7;
    const vals = gl.map((l) => serial(l[cf])).filter((x): x is number => x !== null);
    if (vals.length === 0) return null;
    const dataMax = Math.max(...vals);
    if (dataEixo >= dataMax) return 1;
    return divide(planejado(gl, ci, cf, Math.min(dataEixo, dataMax)), somaCol(gl, "Trabalho"), 0);
  };

  const colunaPlanejada = (ci: string, cf: string) => (ls: Linha[], ctx?: ContextoMedida) => {
    const e = eixo(ctx);
    if (e === null) return null;
    const gl = g(ls, ctx);
    if (n === 60) {
      const a = curva60(gl, e, ci, cf), b = curva60(gl, e - 7, ci, cf);
      return a === null || b === null ? null : a - b;
    }
    // KPI 30/90 | COLUNA | % Baseline usa WBX[Início]/[Término] (literal do DAX, não a linha de base).
    const is = inicioSemana(e), total = somaCol(gl, "Trabalho");
    return zero(total) ? null : divide(sub(planejado(gl, "Início", "Término", is + 7), planejado(gl, "Início", "Término", is)), total, 0);
  };

  const colunaRealizado = (ls: Linha[], ctx?: ContextoMedida) => {
    const e = eixo(ctx), dc = corte(ctx);
    if (e === null) return null;
    const gl = g(ls, ctx), lb = LB(gl), is = inicioSemana(e);
    if (dc === null || is > dc || zero(lb)) return null;
    const limite = Math.min(is + 6, dc);
    const r = n === 60 ? real(gl, ctx?.serie, (t) => t >= is && t < Math.floor(limite) + 1) : real(gl, ctx?.serie, (t) => t >= is && t <= limite);
    return divide(r, lb, 0);
  };

  const curvaRealizado = (ls: Linha[], ctx?: ContextoMedida) => {
    const e = eixo(ctx), dc = corte(ctx);
    const gl = g(ls, ctx), lb = LB(gl);
    if (n === 60) {
      if (e === null || dc === null || e > dc || zero(lb)) return null;
      const acum = e === dc ? somaCol(gl, "Trabalho Real") : real(gl, ctx?.serie, (t) => t < Math.floor(e) + 1);
      return minimo1(divide(acum, lb, 0));
    }
    if (dc === null || zero(lb) || e === null) return null;
    const dataEixo = inicioSemana(e) + 6;
    if (dataEixo < dc) return minimo1(divide(real(gl, ctx?.serie, (t) => t <= Math.min(dataEixo, dc)), lb, 0));
    if (dataEixo >= dc && dataEixo - 6 <= dc) return accRealizado(ls, ctx);
    return null;
  };

  const mostrarPeriodo = (ls: Linha[], ctx?: ContextoMedida) => {
    const e = eixo(ctx);
    if (e === null) return null;
    const gl = g(ls, ctx), s = inicioSemana(e);
    const ext = (col: string, maior: boolean) => {
      const v = gl.map((l) => serial(l[col])).filter((x): x is number => x !== null);
      return v.length ? (maior ? Math.max(...v) : Math.min(...v)) : null;
    };
    const ib = ext("Início da Linha de Base", false), ir = ext("Início", false);
    const tb = ext("Término da Linha de Base", true), tr = ext("Término", true);
    // MIN/MAX de dois escalares do DAX: BLANK conta como 0.
    const dMin = Math.min(ib ?? 0, ir ?? 0), dMax = Math.max(tb ?? 0, tr ?? 0);
    return s + 6 >= dMin && s <= dMax ? 1 : 0;
  };

  const m = (formato: Medida["formato"], calcular: Medida["calcular"], dax: string, requer?: Medida["requer"]): Medida =>
    ({ entidade: W, formato, calcular, dax, requer });
  const daxRef = `ver MAPA_MEDIDAS_DAX.md — KPI ${n}`;
  const medidas: Record<string, Medida> = {
    [`${k} ACC | % Realizado`]: m("percentual", accRealizado, `MIN(1, DIVIDE([KPI ${n} | BASE | Trabalho Real], [KPI ${n} | BASE | Trabalho Linha Base], 0))`, REQ_ACC_REAL),
    [`${k} ACC | % Planejado`]: m("percentual", accPlanejado, daxRef, REQ_PLAN),
    [`${k} ACC | % Desvio`]: m("percentual", (ls, ctx) => desvio(accPlanejado(ls, ctx), accRealizado(ls, ctx)), "Realizado − Planejado", unir(REQ_PLAN, REQ_ACC_REAL)),
    [`${k} BASE SEM | % Planejado`]: m("percentual", baseSemPlanejado, daxRef, REQ_PLAN),
    [`${k} BASE SEM | % Realizado`]: m("percentual", baseSemRealizado, daxRef, REQ_REAL),
    [`${k} BASE SEM | % Desvio`]: m("percentual", (ls, ctx) => desvio(baseSemPlanejado(ls, ctx), baseSemRealizado(ls, ctx), n === 90),
      "Realizado − Planejado", unir(REQ_PLAN, REQ_REAL)),
    [`${k} CURVA | % Baseline`]: m("percentual", curvaPlanejada("Início da Linha de Base", "Término da Linha de Base"), daxRef, REQ_BASE),
    [`${k} CURVA | % Reprogramado`]: m("percentual", curvaPlanejada("Início", "Término"), daxRef, REQ_PLAN),
    [`${k} CURVA | % Realizado`]: m("percentual", curvaRealizado, daxRef, REQ_REAL),
    [`${k} COLUNA | % Baseline`]: m("percentual", n === 60 ? colunaPlanejada("Início da Linha de Base", "Término da Linha de Base") : colunaPlanejada("Início", "Término"),
      daxRef, n === 60 ? REQ_BASE : REQ_PLAN),
    [`${k} COLUNA | % Realizado`]: m("percentual", colunaRealizado, daxRef, REQ_REAL),
    [`${k} CURVA | Mostrar Período`]: m("inteiro", mostrarPeriodo, daxRef, { colunas: ["Início", "Início da Linha de Base"] }),
  };
  if (n === 60) medidas[`${k} COLUNA | % Reprogramado`] = m("percentual", colunaPlanejada("Início", "Término"), daxRef, REQ_PLAN);
  if (n === 90) {
    // FORMAT(valor, "0.00%") no modelo pt-BR; BLANK → "".
    const pct = (v: number | null) => (v === null ? "" : `${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v * 100)}%`);
    medidas[`${k} CARD | Weekly Complete`] = m("texto", (ls, ctx) => pct(baseSemRealizado(ls, ctx)), `IF(ISBLANK(v), "", FORMAT(v, "0.00%"))`, REQ_REAL);
    medidas[`${k} CARD | Weekly Deviation`] = m("texto", (ls, ctx) => pct(desvio(baseSemPlanejado(ls, ctx), baseSemRealizado(ls, ctx), true)),
      `IF(ISBLANK(v), "", FORMAT(v, "0.00%;-0.00%;0.00%"))`, unir(REQ_PLAN, REQ_REAL));
  }
  return medidas;
}

/** PRAZO que depende de Início: Início Baseline / Início Reprogramado (MIN em ALLSELECTED) e Duração Baseline. */
function prazoInicio(n: 30 | 60 | 90): Record<string, Medida> {
  const k = `KPI.KPI ${n} | PRAZO |`;
  const minimo = (gl: Linha[], col: string) => {
    let melhor: number | null = null;
    for (const l of gl) { const s = serial(l[col]); if (s !== null && (melhor === null || s < melhor)) melhor = s; }
    return melhor;
  };
  const maximo = (gl: Linha[], col: string) => {
    let melhor: number | null = null;
    for (const l of gl) { const s = serial(l[col]); if (s !== null && (melhor === null || s > melhor)) melhor = s; }
    return melhor;
  };
  const ib = (ls: Linha[], ctx?: ContextoMedida) => minimo(doGate(n, ls, ctx), "Início da Linha de Base");
  return {
    [`${k} Início Baseline`]: { entidade: W, formato: "data", requer: { colunas: ["Início da Linha de Base"] },
      calcular: (ls, ctx) => { const v = ib(ls, ctx); return v === null ? null : isoDeSerial(v); },
      dax: `CALCULATE(MIN('WBX'[Início da Linha de Base]), FILTER(ALLSELECTED('WBX'), 'WBX'[Level 4] = "${gateDe(n)}"))` },
    [`${k} Início Reprogramado`]: { entidade: W, formato: "data", requer: { colunas: ["Início"] },
      calcular: (ls, ctx) => { const v = minimo(doGate(n, ls, ctx), "Início"); return v === null ? null : isoDeSerial(v); },
      dax: `CALCULATE(MIN('WBX'[Início]), FILTER(ALLSELECTED('WBX'), 'WBX'[Level 4] = "${gateDe(n)}"))` },
    [`${k} Duração Baseline`]: { entidade: W, formato: "inteiro", requer: { colunas: ["Início da Linha de Base"] },
      calcular: (ls, ctx) => {
        const i = ib(ls, ctx), t = maximo(doGate(n, ls, ctx), "Término da Linha de Base");
        return i === null || t === null ? null : datediff(i, t);
      },
      dax: `IF(ISBLANK(InicioBaseline) || ISBLANK(TerminoBaseline), BLANK(), DATEDIFF(InicioBaseline, TerminoBaseline, DAY))` },
  };
}

/**
 * KPI 30 | 3D | COLUNA/CURVA | % Realizado (página LDP - 3D - 30%). Mesmo DAX do realizado, restrito por
 * TREATAS(VALUES('LDP (4)'[N&A DOCUMENT NUMBER]) com KEEPFILTERS(FORMAT = "3D"), 'WBX'[NA DOCUMENT NUMBER]) — os documentos
 * vêm de ContextoMedida.documentos3D (LDP já com filtros da página). As páginas 3D 60%/90% usam as medidas gerais KPI 60/90
 * (literal do PBIR), por isso só o 30% tem família 3D.
 */
function familia3D30(): Record<string, Medida> {
  const k = "KPI.KPI 30 | 3D |";
  const base = (ls: Linha[], ctx?: ContextoMedida) => {
    const docs = ctx?.documentos3D;
    const gl = doGate(30, ls, ctx).filter((l) => (l["É Resumo"] as unknown) !== true
      && docs !== undefined && typeof l["NA DOCUMENT NUMBER"] === "string" && docs.has(String(l["NA DOCUMENT NUMBER"]).toUpperCase()));
    return { gl, lb: somaCol(gl, "Trab Linha de Base"), total: somaCol(gl, "Trabalho Real") };
  };
  const guarda = (ctx: ContextoMedida | undefined, lb: number | null) => {
    const e = eixo(ctx), dc = corte(ctx);
    return e === null || dc === null || e > dc || zero(lb) ? null : { e, dc };
  };
  const coluna = (ls: Linha[], ctx?: ContextoMedida) => {
    const { gl, lb, total } = base(ls, ctx);
    const g = guarda(ctx, lb);
    if (!g) return null;
    const inicio = g.e - 6;
    const semana = g.e === g.dc
      ? Math.max(0, (total ?? 0) - (real(gl, ctx?.serie, (t) => t < inicio) ?? 0))
      : real(gl, ctx?.serie, (t) => t >= inicio && t < Math.floor(g.e) + 1);
    return divide(semana, lb, 0);
  };
  const curva = (ls: Linha[], ctx?: ContextoMedida) => {
    const { gl, lb, total } = base(ls, ctx);
    const g = guarda(ctx, lb);
    if (!g) return null;
    const acum = g.e === g.dc ? total : real(gl, ctx?.serie, (t) => t < Math.floor(g.e) + 1);
    return minimo1Dax(divide(acum, lb, 0));
  };
  const req = { colunas: ["Trab Linha de Base", "Trabalho Real", "NA DOCUMENT NUMBER"], serie: true, documentos3D: true };
  const dax = "ver MAPA_MEDIDAS_DAX.md — KPI 30 | 3D | (COLUNA|CURVA) | % Realizado (TREATAS de LDP FORMAT = 3D)";
  return {
    [`${k} COLUNA | % Realizado`]: { entidade: W, formato: "percentual", calcular: coluna, dax, requer: req },
    [`${k} CURVA | % Realizado`]: { entidade: W, formato: "percentual", calcular: curva, dax, requer: req },
  };
}

export const MEDIDAS_CURVAS: Record<string, Medida> = {
  ...familia(30), ...familia(60), ...familia(90), ...prazoInicio(30), ...prazoInicio(60), ...prazoInicio(90), ...familia3D30(),
};

/** dCalendario[Data Corte] (quintas-feiras) de 02/01/2025 a 30/12/2027 — mesma faixa da consulta Power Query do PBIX. */
export function datasCorteCalendario(): string[] {
  const out: string[] = [];
  for (let s = serial("2025-01-02")!, fim = serial("2027-12-30")!; s <= fim; s += 7) out.push(dataDeSerial(s));
  return out;
}
