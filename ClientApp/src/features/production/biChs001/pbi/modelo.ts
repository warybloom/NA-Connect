// Tipos do layout normalizado (generated/pNN.json) e leitura de propriedades de formatação do Power BI.
// Ordem de resolução de uma propriedade: objeto do visual (entrada geral → entrada {id:"default"}) → padrão do tema
// CY24SU10 / padrão do Power BI para o tipo de visual. Fontes do Power BI vêm em pt; o canvas é em px (1pt = 4/3 px).

export type Props = Record<string, any>;
export interface Entrada { p: Props; s?: Record<string, any> }
export type Objetos = Record<string, Entrada[]>;

export interface CampoPbi {
  entity?: string; prop?: string; kind: string; fn?: number;
  queryRef?: string; nativeQueryRef?: string; displayName?: string; active?: boolean;
}

export interface VisualPbi {
  id: string; type: string; x: number; y: number; w: number; h: number; z: number;
  hidden?: boolean; parent?: string;
  o?: Objetos; c?: Objetos;
  fields?: Record<string, CampoPbi[]>;
  sort?: (CampoPbi & { direction?: string })[];
  filters?: unknown;
}

export interface PaginaPbi {
  ordem: number; id: string; nome: string; slug: string; largura: number; altura: number; displayOption: string;
  objetos: Objetos; filtros: unknown; interacoes: unknown[]; visuais: VisualPbi[];
}

/** Paleta do tema CY24SU10 (dataColors) — ordem usada quando o visual não define cor por ponto/série. */
export const CORES_DADOS = ["#118DFF", "#12239E", "#E66C37", "#6B007B", "#E044A7", "#744EC2", "#D9B300", "#D64550", "#197278", "#1AAB40",
  "#15C6F4", "#4092FF", "#FFA058", "#BE5DC9", "#F472D0", "#B5A1FF", "#C4A200", "#FF8080", "#00DBBC", "#5BD667"];

export const FONTE_PADRAO = "'Segoe UI', wf_segoe-ui_normal, helvetica, arial, sans-serif";
export const FONTE_TITULO = "'DIN', 'Segoe UI', wf_segoe-ui_normal, helvetica, arial, sans-serif";
export const COR_TEXTO = "#252423";
export const COR_TEXTO_SECUNDARIO = "#605E5C";

export const pt = (n: number) => (n * 4) / 3;

/** Fonte do Power BI → CSS. "wf_standard-font" é a fonte padrão do serviço (Segoe UI). */
export function fonte(f: unknown, padrao = FONTE_PADRAO): string {
  if (typeof f !== "string" || !f.trim()) return padrao;
  return f.replace(/wf_standard-font/g, "'Segoe UI'").replace(/^DIN\b/, "'DIN', 'Segoe UI'");
}

const geral = (e: Entrada) => !e.s || e.s.id === "default";

/** Primeira ocorrência da propriedade em entradas gerais (sem seletor ou seletor default). */
export function ler<T = any>(objs: Objetos | undefined, objeto: string, prop: string, padrao?: T): T {
  const lista = objs?.[objeto];
  if (lista) {
    for (const e of lista) if (!e.s && e.p[prop] !== undefined) return e.p[prop] as T;
    for (const e of lista) if (geral(e) && e.p[prop] !== undefined) return e.p[prop] as T;
  }
  return padrao as T;
}

/** Propriedade específica de uma série (seletor metadata = queryRef da projeção). */
export function lerSerie<T = any>(objs: Objetos | undefined, objeto: string, prop: string, queryRef: string | undefined): T | undefined {
  if (!queryRef) return undefined;
  for (const e of objs?.[objeto] ?? []) if (e.s?.metadata === queryRef && e.p[prop] !== undefined) return e.p[prop] as T;
  return undefined;
}

/**
 * Propriedade por valor de categoria: seletor data[].scopeId.Comparison (Left = coluna, Right = literal). Compara pelo
 * nome da coluna e pelo valor literal (o Power BI grava o literal entre aspas simples).
 */
export function lerCategoria<T = any>(objs: Objetos | undefined, objeto: string, prop: string, coluna: string | undefined, valor: string,
  entidade?: string): T | undefined {
  for (const e of objs?.[objeto] ?? []) {
    const cmp = e.s?.data?.[0]?.scopeId?.Comparison;
    if (!cmp || e.p[prop] === undefined) continue;
    // Seletores de outras entidades (formatação copiada de outro relatório) não se aplicam a este visual.
    if (entidade && cmp.Left?.Column?.Expression?.SourceRef?.Entity !== entidade) continue;
    const col = cmp.Left?.Column?.Property;
    const lit = cmp.Right?.Literal?.Value;
    if (typeof lit !== "string") continue;
    const v = lit.startsWith("'") && lit.endsWith("'") ? lit.slice(1, -1).replace(/''/g, "'") : lit.replace(/[DLM]$/, "");
    if ((!coluna || col === coluna) && v === valor) return e.p[prop] as T;
  }
  return undefined;
}

/** Cor com transparência (0..100, como no Power BI). */
export function rgba(cor: unknown, transparencia = 0): string | undefined {
  if (typeof cor !== "string" || !/^#?[0-9a-f]{6}$/i.test(cor.replace("#", "").slice(0, 6))) return typeof cor === "string" ? cor : undefined;
  const h = cor.replace("#", "");
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
  const a = Math.max(0, Math.min(1, 1 - (Number(transparencia) || 0) / 100));
  return a >= 1 ? `#${h}` : `rgba(${r},${g},${b},${a})`;
}

const TIPOS_GRAFICO = new Set(["clusteredBarChart", "clusteredColumnChart", "donutChart", "pieChart", "lineClusteredColumnComboChart",
  "lineStackedColumnComboChart"]);
export const ehGrafico = (tipo: string) => TIPOS_GRAFICO.has(tipo);

/** Fundo do contêiner ligado por padrão? (tema CY24SU10: '*' liga; shape/image/actionButton desligam). */
export function fundoPadrao(tipo: string): boolean {
  return !["shape", "image", "actionButton", "basicShape", "group"].includes(tipo);
}

/** Título do contêiner ligado por padrão? (Power BI: gráficos, sim; cartões, tabelas, segmentações, formas, não.) */
export function tituloPadrao(tipo: string): boolean {
  return ehGrafico(tipo);
}

/** Nome exibido de um campo: displayName → nome da coluna/medida (com agregação "Count of"). */
export function nomeCampo(c: CampoPbi): string {
  if (c.displayName) return c.displayName.trim();
  const base = c.prop ?? c.queryRef ?? "";
  if (c.kind === "aggregation") {
    // QueryAggregateFunction do Power BI: 0 Sum, 1 Avg, 2 DistinctCount, 3 Min, 4 Max, 5 CountNonNull, 6 Median.
    const fn = { 0: "Sum of", 1: "Average of", 2: "Count of", 3: "First", 4: "Last", 5: "Count of", 6: "Median of" }[c.fn ?? -1];
    return fn ? `${fn} ${base}` : base;
  }
  return base;
}

/** Título automático do Power BI: "<medidas> by <categoria>". */
export function tituloAutomatico(v: VisualPbi): string {
  const cat = v.fields?.Category?.[0];
  const ys = [...(v.fields?.Y ?? []), ...(v.fields?.Y2 ?? [])].map(nomeCampo);
  if (!ys.length) return "";
  const medidas = ys.length > 1 ? `${ys.slice(0, -1).join(", ")} and ${ys[ys.length - 1]}` : ys[0];
  return cat ? `${medidas} by ${nomeCampo(cat)}` : medidas;
}

/** Texto (pode ter \n) a partir dos parágrafos de um textbox. */
export interface Paragrafo {
  textRuns: { value: string; textStyle?: Record<string, string>; url?: string }[];
  horizontalTextAlignment?: string;
  listType?: string;
  indentLevel?: number;
}
