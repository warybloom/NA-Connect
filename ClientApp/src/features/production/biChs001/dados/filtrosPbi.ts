// Avaliador dos filtros do PBIR (filterConfig de página e de visual) sobre linhas de uma entidade.
// Suporta a gramática usada no relatório CHS001: In / Not / Comparison / And / Or sobre colunas, com literais texto,
// número, data e null. Comparação de texto SEM diferenciar maiúsculas (igual ao motor do Power BI). Blank = null ou "".
// Filtro sem condição ("filter" ausente — só o campo no painel) não filtra. Condição sobre MEDIDA ou sobre outra entidade
// não é avaliada aqui: é devolvida em `naoAvaliadas` para o chamador decidir (nunca ignorada em silêncio).
import type { Valor } from "./tipos";

export type Linha = Record<string, Valor>;

export interface FiltroPbi {
  name?: string;
  field?: any;
  type?: string;
  filter?: { From?: { Name: string; Entity: string }[]; Where?: { Condition: any }[] };
}

export interface ResultadoFiltro {
  /** Predicado sobre a linha (true = mantém). */
  aplicar: (l: Linha) => boolean;
  /** Condições que referenciam medida ou outra entidade (não avaliadas). */
  naoAvaliadas: { filtro: string; motivo: string }[];
}

export const ehBlank = (v: Valor | undefined) => v === null || v === undefined || v === "";

export function literalPbi(v: unknown): Valor {
  if (typeof v !== "string") return (v as Valor) ?? null;
  if (v === "null") return null;
  if (v.startsWith("'") && v.endsWith("'")) return v.slice(1, -1).replace(/''/g, "'");
  const dt = /^datetime'([^']*)'$/.exec(v);
  if (dt) return dt[1];
  const n = /^(-?\d+(?:\.\d+)?)[DLM]?$/.exec(v);
  if (n) return Number(n[1]);
  return v;
}

export function igualPbi(a: Valor | undefined, b: Valor): boolean {
  if (ehBlank(a) || ehBlank(b)) return ehBlank(a) && ehBlank(b);
  if (typeof a === "string" && typeof b === "string") return a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;
  // eslint-disable-next-line eqeqeq
  return a == b;
}

function comparar(a: Valor | undefined, b: Valor, tipo: number): boolean {
  if (tipo === 0) return igualPbi(a, b);
  if (ehBlank(a) || ehBlank(b)) return false;
  const [x, y] = typeof a === "number" || typeof b === "number" ? [Number(a), Number(b)] : [String(a), String(b)];
  switch (tipo) {
    case 1: return x > y;
    case 2: return x >= y;
    case 3: return x < y;
    case 4: return x <= y;
    default: return false;
  }
}

interface Contexto { aliases: Record<string, string>; entidade: string; naoAvaliadas: { filtro: string; motivo: string }[]; nome: string }

/** Coluna de uma expressão {Column:{Expression:{SourceRef:{Source|Entity}}, Property}} na entidade do contexto. */
function coluna(expr: any, ctx: Contexto): string | null | undefined {
  if (expr?.Column) {
    const ref = expr.Column.Expression?.SourceRef ?? {};
    const ent = ref.Entity ?? ctx.aliases[ref.Source];
    if (ent !== ctx.entidade) { ctx.naoAvaliadas.push({ filtro: ctx.nome, motivo: `coluna de outra entidade (${ent})` }); return null; }
    return expr.Column.Property as string;
  }
  if (expr?.Measure || expr?.Aggregation || expr?.Arithmetic) {
    ctx.naoAvaliadas.push({ filtro: ctx.nome, motivo: "condição sobre medida/agregação" });
    return null;
  }
  ctx.naoAvaliadas.push({ filtro: ctx.nome, motivo: "expressão não suportada" });
  return undefined;
}

type Pred = (l: Linha) => boolean;
const SEMPRE: Pred = () => true;
/**
 * Condição NÃO avaliável nesta entidade (outra entidade / medida): neutra — não restringe e NÃO é invertida por Not.
 * (Antes, Not(In sobre outra entidade) virava "sempre falso" e zerava o WBX nas páginas 3D, cujo filtro de página é
 * 'LDP (4)'[FORMAT] NOT IN {...}.)
 */
const NEUTRO = null;
type PredOuNeutro = Pred | typeof NEUTRO;

function condicao(c: any, ctx: Contexto): PredOuNeutro {
  if (!c) return SEMPRE;
  if (c.And) {
    const a = condicao(c.And.Left, ctx), b = condicao(c.And.Right, ctx);
    return a === NEUTRO ? b : b === NEUTRO ? a : (l) => a(l) && b(l);
  }
  if (c.Or) {
    const a = condicao(c.Or.Left, ctx), b = condicao(c.Or.Right, ctx);
    return a === NEUTRO || b === NEUTRO ? NEUTRO : (l) => a(l) || b(l);
  }
  if (c.Not) { const a = condicao(c.Not.Expression, ctx); return a === NEUTRO ? NEUTRO : (l) => !a(l); }
  if (c.In) {
    const cols = (c.In.Expressions as any[]).map((e) => coluna(e, ctx));
    if (cols.some((x) => !x)) return NEUTRO;
    const tuplas = (c.In.Values as any[][]).map((t) => t.map((x) => literalPbi(x?.Literal?.Value)));
    return (l) => tuplas.some((t) => t.every((v, i) => igualPbi(l[cols[i] as string], v)));
  }
  if (c.Comparison) {
    const col = coluna(c.Comparison.Left, ctx);
    if (!col) return NEUTRO;
    const v = literalPbi(c.Comparison.Right?.Literal?.Value);
    return (l) => comparar(l[col], v, c.Comparison.ComparisonKind);
  }
  ctx.naoAvaliadas.push({ filtro: ctx.nome, motivo: "condição não suportada" });
  return NEUTRO;
}

/** Combina (AND) todos os filtros de uma filterConfig para a entidade dada. */
export function compilarFiltros(config: unknown, entidade: string): ResultadoFiltro {
  const filtros = ((config as { filters?: FiltroPbi[] } | null | undefined)?.filters ?? []);
  const naoAvaliadas: ResultadoFiltro["naoAvaliadas"] = [];
  const preds: Pred[] = [];
  for (const f of filtros) {
    if (!f.filter?.Where?.length) continue; // campo no painel de filtros sem seleção = sem efeito
    const aliases = Object.fromEntries((f.filter.From ?? []).map((x) => [x.Name, x.Entity]));
    const ctx: Contexto = { aliases, entidade, naoAvaliadas, nome: f.name ?? "?" };
    for (const w of f.filter.Where) { const p = condicao(w.Condition, ctx); if (p !== NEUTRO) preds.push(p); }
  }
  return { aplicar: (l) => preds.every((p) => p(l)), naoAvaliadas };
}
