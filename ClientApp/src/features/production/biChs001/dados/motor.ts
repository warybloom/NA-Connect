// Motor de consulta do BI reproduzido: dado um visual do PBIR e as linhas (já com nomes do BI) da sua entidade, aplica
// filtros de página → filtros do visual → segmentações e calcula o que o visual exibe (cartão, categorias, tabela, opções).
// Semântica do Power BI reproduzida:
//   • CountNonNull(coluna) por categoria; categoria cuja medida é BLANK não aparece;
//   • tabela (tableEx) só com colunas = combinações DISTINTAS das colunas, ordenadas pelo sort do visual ou pela 1ª coluna;
//   • ordenação padrão de gráficos = medida decrescente (ou o sortDefinition salvo no visual).
import { ler, type CampoPbi, type PaginaPbi, type VisualPbi } from "../pbi/modelo";
import { compilarFiltros, ehBlank, igualPbi, literalPbi, type Linha } from "./filtrosPbi";
import { chaveMedida, MEDIDAS, motivoMedidaSemFonte, type ContextoMedida, type Medida } from "./medidas";
import { ENTIDADES } from "./entidades";
import { datasCorteCalendario, indexarSerie, serial, type SerieTarefas } from "./curvas";
import type { DadosVisual, FormatoValor, PontoCategoria, Valor } from "./tipos";

/** Segmentações ativas: "Entidade|Coluna" → valores selecionados (texto exato da fonte). */
export type Segmentacoes = Record<string, Valor[]>;
export const chaveSegmentacao = (entidade: string, coluna: string) => `${entidade}|${coluna}`;

export interface FonteCarregada {
  estado: "carregando" | "ok" | "erro"; linhas?: Linha[]; erro?: string;
  /** Colunas do BI mapeadas mas sem nenhum valor na fonte atual (colunasVazias) — contam como SEM FONTE para as medidas. */
  vazias?: string[];
  /** A fonte ainda não existe no banco (resposta "indisponivel" do endpoint) — motivo exibido no PENDENTE. */
  indisponivel?: string;
  /** Versão do cronograma isolado (bi_chs001.Versao_Cronograma) de onde vieram as linhas; ausente = fonte legada. */
  versaoId?: number;
  /** Endpoint efetivamente usado (principal ou alternativo legado). */
  origem?: string;
}

/** Eixo gerado pelo motor (tabela de datas do PBIX, não vem do banco): dCalendario[Data Corte] = quintas-feiras. */
const EIXO_CALENDARIO = (c?: CampoPbi) => c?.entity === "dCalendario" && c.prop === "Data Corte";

/** Série AssignmentByDay indexada uma vez por fonte carregada. */
const seriesIndexadas = new WeakMap<Linha[], SerieTarefas>();
function serieDe(fontes: Record<string, FonteCarregada>): SerieTarefas | undefined {
  const ls = fontes.AssignmentByDay?.estado === "ok" ? fontes.AssignmentByDay.linhas : undefined;
  if (!ls) return undefined;
  let s = seriesIndexadas.get(ls);
  if (!s) { s = indexarSerie(ls); seriesIndexadas.set(ls, s); }
  return s;
}

/** Entidades que as medidas do visual exigem além da entidade-base (ex.: série AssignmentByDay das curvas). */
export function entidadesExigidas(v: VisualPbi): string[] {
  const s = new Set<string>();
  for (const c of [...campos(v), ...medidasDosFiltros(v).map((f) => f.campo)]) {
    const m = c.kind === "measure" ? MEDIDAS[chaveMedida(c.entity, c.prop)] : undefined;
    if (m?.requer?.serie) s.add("AssignmentByDay");
    if (m?.requer?.documentos3D) s.add("LDP (4)");
  }
  return [...s];
}

/** VALUES('LDP (4)'[N&A DOCUMENT NUMBER]) com KEEPFILTERS(FORMAT = "3D") sob os filtros da página e do visual. */
function documentos3D(v: VisualPbi, ctx: ContextoVisual): Set<string> | undefined {
  const ldp = ctx.fontes["LDP (4)"];
  if (ldp?.estado !== "ok" || !ldp.linhas) return undefined;
  const fp = compilarFiltros(ctx.pagina.filtros, "LDP (4)"), fv = compilarFiltros(v.filters, "LDP (4)");
  const s = new Set<string>();
  for (const l of ldp.linhas)
    if (fp.aplicar(l) && fv.aplicar(l) && igualPbi(l.FORMAT, "3D") && typeof l["N&A DOCUMENT NUMBER"] === "string")
      s.add(String(l["N&A DOCUMENT NUMBER"]).toUpperCase());
  return s;
}

/**
 * Requisitos de fonte de uma medida (Medida.requer): colunas de WBX com dado e a série AssignmentByDay.
 * undefined = atendidos; "carregando"; ou o motivo (campos faltantes + fonte necessária).
 */
function faltaRequisito(m: Medida | undefined, fontes: Record<string, FonteCarregada>): string | undefined {
  if (!m?.requer || !m.entidade) return undefined;
  const base = fontes[m.entidade];
  const motivos: string[] = [];
  if (base?.estado === "ok") {
    for (const c of m.requer.colunas) if (base.vazias?.includes(c))
      motivos.push(ENTIDADES[m.entidade]?.faltantes?.[c] ?? `${m.entidade}[${c}] sem valor na fonte atual`);
  }
  if (m.requer.serie) {
    const f = fontes.AssignmentByDay;
    if (!f || f.estado === "carregando") return "carregando";
    if (f.estado === "erro") motivos.push(`AssignmentByDay (trabalho real por dia do .mpp): ${f.erro ?? "falha ao carregar"}`);
    else if (f.indisponivel) motivos.push(`AssignmentByDay (trabalho real por dia do .mpp): ${f.indisponivel}`);
    else if (!f.linhas?.length) motivos.push("AssignmentByDay (trabalho real por dia do .mpp): nenhuma linha no snapshot atual");
  }
  if (m.requer.documentos3D) {
    const f = fontes["LDP (4)"];
    if (!f || f.estado === "carregando") return "carregando";
    if (f.estado === "erro") motivos.push(`LDP (4) (documentos FORMAT = 3D do TREATAS): ${f.erro ?? "falha ao carregar"}`);
  }
  return motivos.length ? [...new Set(motivos)].join("; ") : undefined;
}

/** Filtros do visual com condição "medida op literal" (ex.: KPI 60 | CURVA | Mostrar Período = 1). */
interface FiltroMedida { nome: string; campo: CampoPbi; tipo: number; valor: Valor }
function medidasDosFiltros(v: VisualPbi): FiltroMedida[] {
  const out: FiltroMedida[] = [];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  for (const f of ((v.filters as { filters?: any[] } | undefined)?.filters ?? [])) {
    for (const w of f.filter?.Where ?? []) {
      const cmp = w?.Condition?.Comparison;
      const med = cmp?.Left?.Measure;
      if (!med) continue;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const alias: Record<string, string> = Object.fromEntries((f.filter.From ?? []).map((x: any) => [x.Name, x.Entity]));
      const entity = med.Expression?.SourceRef?.Entity ?? alias[med.Expression?.SourceRef?.Source];
      out.push({ nome: f.name ?? "?", campo: { entity, prop: med.Property, kind: "measure" } as CampoPbi, tipo: cmp.ComparisonKind,
        valor: literalPbi(cmp.Right?.Literal?.Value) });
    }
  }
  return out;
}

function passaFiltroMedida(f: FiltroMedida, x: Valor): boolean {
  if (x === null) return false;
  const a = Number(x), b = Number(f.valor);
  switch (f.tipo) { case 0: return a === b; case 1: return a > b; case 2: return a >= b; case 3: return a < b; case 4: return a <= b; default: return false; }
}

const campos = (v: VisualPbi): CampoPbi[] => Object.values(v.fields ?? {}).flat();

/** Entidade-base do visual: a das colunas, ou a da medida (registro de medidas). */
export function entidadeDoVisual(v: VisualPbi): string | undefined {
  for (const c of campos(v)) {
    if (c.kind === "measure") { const m = MEDIDAS[chaveMedida(c.entity, c.prop)]; if (m?.entidade) return m.entidade; continue; }
    if (c.entity && ENTIDADES[c.entity]) return c.entity;
  }
  for (const c of campos(v)) if (c.entity) return c.entity;
  return undefined;
}

function agregar(linhas: Linha[], c: CampoPbi, mctx?: ContextoMedida): Valor {
  if (c.kind === "measure") {
    const m = MEDIDAS[chaveMedida(c.entity, c.prop)];
    return m ? m.calcular(linhas, mctx) : null;
  }
  const col = c.prop ?? "";
  const vals = linhas.map((l) => l[col]).filter((x) => !ehBlank(x));
  switch (c.kind === "aggregation" ? c.fn : -1) {
    case 5: return vals.length === 0 ? null : vals.length;                       // CountNonNull
    case 2: return vals.length === 0 ? null : new Set(vals.map(String)).size;     // DistinctCount
    case 0: return vals.length === 0 ? null : vals.reduce<number>((s, x) => s + Number(x), 0);
    case 3: return vals.length === 0 ? null : [...vals].sort(compararValores)[0];  // Min / First
    case 4: return vals.length === 0 ? null : [...vals].sort(compararValores).at(-1) ?? null;
    default: return vals[0] ?? null;
  }
}

export function compararValores(a: Valor, b: Valor): number {
  if (ehBlank(a) && ehBlank(b)) return 0;
  if (ehBlank(a)) return -1;
  if (ehBlank(b)) return 1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), "pt-BR", { numeric: true, sensitivity: "base" });
}

function formatoCampo(c: CampoPbi): FormatoValor {
  if (c.kind === "measure") return MEDIDAS[chaveMedida(c.entity, c.prop)]?.formato ?? "decimal";
  if (c.kind === "aggregation") return c.fn === 5 || c.fn === 2 ? "inteiro" : "texto";
  return "texto";
}

const ref = (c: CampoPbi) => c.queryRef ?? `${c.entity}.${c.prop}`;

/** Campos que o motor sabe calcular para esta entidade (coluna mapeada ou medida registrada). */
function campoSuportado(c: CampoPbi, entidade: string): boolean {
  if (EIXO_CALENDARIO(c)) return true;
  if (c.kind === "measure") { const m = MEDIDAS[chaveMedida(c.entity, c.prop)]; return !!m && (m.entidade === null || m.entidade === entidade); }
  const def = ENTIDADES[entidade];
  if (c.entity !== entidade || !c.prop || !def?.colunas[c.prop]) return false;
  // Coluna mapeada só para contagem: o valor da fonte não é o mesmo campo do BI.
  return !def.soContagem?.includes(c.prop) || (c.kind === "aggregation" && (c.fn === 5 || c.fn === 2));
}

/** Por que o campo não tem fonte (sem o nome do campo). */
function motivoSemNome(c: CampoPbi): string {
  if (c.kind === "measure") return motivoMedidaSemFonte(c.entity, c.prop) ?? "medida ainda não transcrita / sem fonte verificada";
  const faltante = c.entity ? ENTIDADES[c.entity]?.faltantes?.[c.prop ?? ""] : undefined;
  if (faltante) return faltante;
  if (c.entity && ENTIDADES[c.entity]?.soContagem?.includes(c.prop ?? "")) return "coluna mapeada só para contagem";
  return c.entity && ENTIDADES[c.entity] ? "coluna sem equivalente verificado" : `fonte N&A da entidade "${c.entity}" não mapeada`;
}

/** Texto do selo PENDENTE de um campo. */
export const motivoCampo = (c: CampoPbi) => `${c.entity}[${c.prop}]: ${motivoSemNome(c)}`;

/** Motivos agrupados (visual com muitas medidas sem fonte, ex.: curvas). */
export function resumirMotivos(lista: CampoPbi[]): string {
  const grupos = new Map<string, string[]>();
  for (const c of lista) { const k = motivoSemNome(c); grupos.set(k, [...(grupos.get(k) ?? []), `${c.entity}[${c.prop}]`]); }
  return [...grupos].map(([motivo, nomes]) => {
    const unicos = [...new Set(nomes)];
    return `${unicos.slice(0, 3).join(", ")}${unicos.length > 3 ? ` (+${unicos.length - 3})` : ""}: ${motivo}`;
  }).join(" · ");
}

/** Data salva na segmentação de dSemana da página (o PBIX abre com ela) → "yyyy-MM-dd"; null se não houver seleção única. */
export function dataCorteSalva(pagina: PaginaPbi): string | null {
  for (const v of pagina.visuais) {
    const c = v.fields?.Values?.[0];
    if (v.type !== "slicer" || c?.entity !== "dSemana" || c.prop !== "Data") continue;
    const geral = (v.o?.general ?? []).find((e) => e.p?.filter)?.p?.filter;
    const valores = geral?.filter?.Where?.[0]?.Condition?.In?.Values;
    if (!Array.isArray(valores) || valores.length !== 1) return null;
    const m = /^datetime'(\d{4}-\d{2}-\d{2})/.exec(String(valores[0]?.[0]?.Literal?.Value ?? ""));
    return m ? m[1] : null;
  }
  return null;
}

export interface ContextoVisual {
  pagina: PaginaPbi; fontes: Record<string, FonteCarregada>; segmentacoes: Segmentacoes;
  /** SELECTEDVALUE(dSemana[Data]); undefined = a data salva na segmentação da página (dataCorteSalva). */
  dataCorte?: string | null;
}

/** Linhas da entidade após filtros de página, do visual e segmentações (exceto a do próprio slicer, se `ignorar`). */
export function linhasFiltradas(v: VisualPbi, entidade: string, ctx: ContextoVisual, ignorar?: string):
  { linhas: Linha[]; naoAvaliadas: { filtro: string; motivo: string }[] } {
  const base = ctx.fontes[entidade]?.linhas ?? [];
  const fp = compilarFiltros(ctx.pagina.filtros, entidade);
  const fv = compilarFiltros(v.filters, entidade);
  const segs = Object.entries(ctx.segmentacoes).filter(([k, vals]) => k !== ignorar && vals.length > 0 && k.startsWith(`${entidade}|`));
  const linhas = base.filter((l) => fp.aplicar(l) && fv.aplicar(l)
    && segs.every(([k, vals]) => { const col = k.split("|")[1]; return vals.some((x) => String(x ?? "") === String(l[col] ?? "")); }));
  // Filtros de página sobre outras entidades não restringem esta (sem relacionamento no modelo para as entidades mapeadas);
  // condições do PRÓPRIO visual sobre medida precisam ser conhecidas pelo chamador.
  return { linhas, naoAvaliadas: fv.naoAvaliadas.filter((n) => n.motivo !== "coluna de outra entidade" && !n.motivo.startsWith("coluna de outra entidade")) };
}

const ehCartao = (v: VisualPbi) => v.type === "card" || v.type === "cardVisual";
const medidaSemFonte = (c: CampoPbi) => c.kind === "measure" && MEDIDAS[chaveMedida(c.entity, c.prop)]?.entidade === null;

export function dadosDoVisual(v: VisualPbi, ctx: ContextoVisual): DadosVisual {
  const todos = campos(v);
  const dataCorte = ctx.dataCorte !== undefined ? ctx.dataCorte : dataCorteSalva(ctx.pagina);
  const formatos: Record<string, FormatoValor> = Object.fromEntries(todos.map((c) => [ref(c), formatoCampo(c)]));

  // Cartão só com medidas que dependem do contexto da página (ex.: KPI[Activity Title] = SELECTEDVALUE(dSemana[Data])).
  if (ehCartao(v) && todos.length && todos.every(medidaSemFonte)) {
    const mctx: ContextoMedida = { selecionadas: [], dataCorte };
    return { estado: "ok", tipo: "valores", valores: Object.fromEntries(todos.map((c) => [ref(c), agregar([], c, mctx)])), formatos };
  }

  const entidade = entidadeDoVisual(v);
  if (!entidade || !ENTIDADES[entidade]) {
    const medidas = todos.filter((c) => c.kind === "measure");
    return { estado: "pendente", motivo: medidas.length ? resumirMotivos(medidas)
      : `Fonte N&A da entidade "${entidade ?? "?"}" ainda não mapeada com paridade verificada.` };
  }
  const faltando = todos.filter((c) => !campoSuportado(c, entidade));
  // Cartão de vários blocos: cada bloco é uma medida independente — os sem fonte ficam PENDENTE, os demais calculam.
  const parcial = ehCartao(v) && faltando.length > 0 && faltando.length < todos.length;
  if (faltando.length && !parcial) return { estado: "pendente", motivo: resumirMotivos(faltando) };
  const fonte = ctx.fontes[entidade];
  if (!fonte || fonte.estado === "carregando") return { estado: "carregando" };
  if (fonte.estado === "erro") return { estado: "erro", mensagem: fonte.erro ?? "Falha ao carregar a fonte." };
  const { linhas, naoAvaliadas } = linhasFiltradas(v, entidade, ctx);
  // Condições "medida op literal" do próprio visual são avaliadas por ponto no eixo de calendário (abaixo).
  const filtrosMedida = medidasDosFiltros(v);
  const restantes = naoAvaliadas.filter((n) => !(n.motivo === "condição sobre medida/agregação" && filtrosMedida.some((f) => f.nome === n.filtro)));
  if (restantes.length) return { estado: "pendente", motivo: `Filtro do visual não reproduzível: ${restantes.map((n) => n.motivo).join("; ")}` };

  // Requisitos de fonte das medidas (Início, Trabalho, série diária…): sem eles o campo fica PENDENTE, nunca BLANK falso.
  const medidasUsadas = [...todos.filter((c) => c.kind === "measure"), ...filtrosMedida.map((f) => f.campo)];
  const semFonte: CampoPbi[] = [];
  const motivosRequisito = new Map<string, string>();
  for (const c of medidasUsadas) {
    const r = faltaRequisito(MEDIDAS[chaveMedida(c.entity, c.prop)], ctx.fontes);
    if (r === "carregando") return { estado: "carregando" };
    if (r) { semFonte.push(c); motivosRequisito.set(ref(c), `${c.entity}[${c.prop}]: ${r}`); }
  }
  if (semFonte.length) {
    const medidasDoVisual = todos.filter((c) => c.kind === "measure");
    const cartaoParcial = ehCartao(v) && !filtrosMedida.length && semFonte.length < medidasDoVisual.length;
    if (!cartaoParcial) return { estado: "pendente", motivo: [...new Set(motivosRequisito.values())].join(" · ") };
  }

  // ALLSELECTED(entidade) = linhas do visual sem o agrupamento por categoria/linha.
  const mctx: ContextoMedida = { selecionadas: linhas, dataCorte, serie: serieDe(ctx.fontes),
    documentos3D: medidasUsadas.some((c) => MEDIDAS[chaveMedida(c.entity, c.prop)]?.requer?.documentos3D) ? documentos3D(v, ctx) : undefined };

  // Eixo dCalendario[Data Corte] (curvas S): uma categoria por semana; medidas avaliadas com SELECTEDVALUE = a quinta-feira.
  const categoriaEixo = v.fields?.Category?.[0];
  if (EIXO_CALENDARIO(categoriaEixo)) {
    const medidas = [...(v.fields?.Y ?? []), ...(v.fields?.Y2 ?? [])];
    const pontos: PontoCategoria[] = [];
    for (const data of datasCorteCalendario()) {
      const pctx: ContextoMedida = { ...mctx, eixo: data };
      if (!filtrosMedida.every((f) => passaFiltroMedida(f, agregar(linhas, f.campo, pctx)))) continue;
      const valores = Object.fromEntries(medidas.map((m) => [ref(m), agregar(linhas, m, pctx) as number | null]));
      if (Object.values(valores).every((x) => x === null)) continue;
      pontos.push({ categoria: data, valores });
    }
    if (v.sort?.[0]?.direction === "Descending") pontos.reverse();
    return { estado: "ok", tipo: "pontos", pontos, formatos: { ...formatos, [ref(categoriaEixo!)]: "data" } };
  }

  // Gráficos: Category × Y.
  const categoria = v.fields?.Category?.[0];
  if (categoria) {
    const medidas = [...(v.fields?.Y ?? []), ...(v.fields?.Y2 ?? [])];
    const grupos = new Map<string, { valor: Valor; linhas: Linha[] }>();
    for (const l of linhas) {
      const val = l[categoria.prop ?? ""] ?? null;
      const k = ehBlank(val) ? "\u0000" : String(val);
      const g = grupos.get(k) ?? { valor: ehBlank(val) ? null : val, linhas: [] };
      g.linhas.push(l);
      grupos.set(k, g);
    }
    const pontos: (PontoCategoria & { bruto: Valor })[] = [];
    for (const g of grupos.values()) {
      const valores = Object.fromEntries(medidas.map((m) => [ref(m), agregar(g.linhas, m, mctx) as number | null]));
      if (Object.values(valores).every((x) => x === null)) continue;
      pontos.push({ categoria: g.valor === null ? "(Blank)" : String(g.valor), bruto: g.valor, valores });
    }
    ordenarPontos(pontos, v, categoria, medidas);
    return { estado: "ok", tipo: "pontos", pontos: pontos.map(({ bruto: _b, ...p }) => p), formatos };
  }

  // Matriz com hierarquia de linhas (Rows, ex.: WBX Level 4–7): árvore de grupos; Values calculados no grupo (agregações) ou
  // com ALLSELECTED (medidas PRAZO repetem o total do gate em cada linha, como no Power BI). Expandir/recolher fica na tela.
  if (v.type === "pivotTable" && (v.fields?.Rows?.length ?? 0) > 0) {
    const rows = v.fields?.Rows ?? [];
    const vazias = rows.filter((c) => fonte.vazias?.includes(c.prop ?? ""));
    if (vazias.length) return { estado: "pendente", motivo: resumirMotivos(vazias) };
    return { estado: "ok", tipo: "matriz", nos: arvoreMatriz(v, linhas, rows, 0, [], mctx), niveis: rows.map(ref), formatos };
  }

  // Tabela: colunas → combinações distintas; medidas/agregações por combinação.
  if (v.type === "tableEx" || v.type === "pivotTable") {
    const valores = v.fields?.Values ?? [];
    const cols = valores.filter((c) => c.kind === "column");
    const meds = valores.filter((c) => c.kind !== "column");
    const grupos = new Map<string, Linha[]>();
    for (const l of linhas) {
      const k = JSON.stringify(cols.map((c) => l[c.prop ?? ""] ?? null));
      const g = grupos.get(k) ?? [];
      g.push(l);
      grupos.set(k, g);
    }
    const saida: Record<string, Valor>[] = [];
    for (const [k, ls] of grupos) {
      const chaves = JSON.parse(k) as Valor[];
      const linha: Record<string, Valor> = {};
      cols.forEach((c, i) => { linha[ref(c)] = chaves[i]; });
      meds.forEach((m) => { linha[ref(m)] = agregar(ls, m, mctx); });
      // Linha com todas as medidas BLANK some (Power BI) — só quando há medidas.
      if (meds.length && meds.every((m) => linha[ref(m)] === null)) continue;
      saida.push(linha);
    }
    ordenarLinhas(saida, v, valores);
    return { estado: "ok", tipo: "linhas", linhas: saida, formatos };
  }

  // Cartões: um valor por projeção (bloco sem fonte → pendentes, sem número).
  const suportados = todos.filter((c) => !faltando.includes(c) && !semFonte.includes(c));
  const valores = Object.fromEntries(suportados.map((c) => [ref(c), agregar(linhas, c, mctx)]));
  const listaPendentes = [...(parcial ? faltando.map((c) => [ref(c), motivoCampo(c)] as const) : []),
    ...semFonte.map((c) => [ref(c), motivosRequisito.get(ref(c)) ?? motivoCampo(c)] as const)];
  const pendentes = listaPendentes.length ? Object.fromEntries(listaPendentes) : undefined;
  return { estado: "ok", tipo: "valores", valores, formatos, ...(pendentes ? { pendentes } : {}) };
}

/** Comparação de TEXTO como o DAX (ordem do agrupamento/collation, sem leitura numérica: "1.5.1.10" < "1.5.1.2"). */
const compararTextoDax = (a: Valor, b: Valor) =>
  ehBlank(a) && ehBlank(b) ? 0 : ehBlank(a) ? -1 : ehBlank(b) ? 1
    : typeof a === "number" && typeof b === "number" ? a - b : String(a).localeCompare(String(b), "pt-BR", { sensitivity: "base" });

/** Valor de um campo da matriz no grupo: MIN/MAX de texto pelo critério do DAX; demais = agregar(). */
function valorMatriz(ls: Linha[], c: CampoPbi, mctx: ContextoMedida): Valor {
  if (c.kind === "aggregation" && (c.fn === 3 || c.fn === 4)) {
    const vals = ls.map((l) => l[c.prop ?? ""] ?? null).filter((x) => !ehBlank(x));
    if (vals.length === 0) return null;
    const ord = [...vals].sort(compararTextoDax);
    return c.fn === 3 ? ord[0] : ord[ord.length - 1];
  }
  return agregar(ls, c, mctx);
}

function arvoreMatriz(v: VisualPbi, linhas: Linha[], rows: CampoPbi[], nivel: number, caminho: Valor[], mctx: ContextoMedida): import("./tipos").NoMatriz[] {
  if (nivel >= rows.length) return [];
  const col = rows[nivel].prop ?? "";
  const grupos = new Map<string, { valor: Valor; linhas: Linha[] }>();
  for (const l of linhas) {
    const val = l[col] ?? null;
    const k = ehBlank(val) ? "\u0000" : String(val);
    const g = grupos.get(k) ?? { valor: ehBlank(val) ? null : val, linhas: [] };
    g.linhas.push(l);
    grupos.set(k, g);
  }
  const valoresCampos = v.fields?.Values ?? [];
  const nos = [...grupos.values()].map((g) => {
    const cam = [...caminho, g.valor];
    const valores = Object.fromEntries(valoresCampos.map((c) => [ref(c), valorMatriz(g.linhas, c, mctx)]));
    const filhosPossiveis = nivel + 1 < rows.length && g.linhas.some((l) => !ehBlank(l[rows[nivel + 1].prop ?? ""]));
    return { caminho: cam, nivel, valores, filhos: filhosPossiveis ? arvoreMatriz(v, g.linhas, rows, nivel + 1, cam, mctx) : [] };
  });
  // sortDefinition salvo (ex.: Min(WBX.EDT) crescente); sem ele, o próprio valor da linha.
  const s = v.sort?.[0];
  const alvo = s ? valoresCampos.find((c) => c.prop === s.prop && c.kind === s.kind) : undefined;
  const desc = s?.direction === "Descending";
  nos.sort((a, b) => {
    const r = alvo ? compararTextoDax(a.valores[ref(alvo)] ?? null, b.valores[ref(alvo)] ?? null) : compararTextoDax(a.caminho[nivel], b.caminho[nivel]);
    return desc ? -r : r;
  });
  return nos;
}

function ordenarPontos(pontos: (PontoCategoria & { bruto: Valor })[], v: VisualPbi, cat: CampoPbi, medidas: CampoPbi[]) {
  const s = v.sort?.[0];
  const desc = (s?.direction ?? "Descending") === "Descending";
  const porCategoria = s ? s.prop === cat.prop && s.kind === cat.kind : false;
  const medida = s && !porCategoria ? medidas.find((m) => m.prop === s.prop && m.kind === s.kind) ?? medidas[0] : medidas[0];
  pontos.sort((a, b) => {
    const r = porCategoria ? compararValores(a.bruto, b.bruto) : compararValores(a.valores[ref(medida)] ?? null, b.valores[ref(medida)] ?? null);
    return desc ? -r : r;
  });
}

function ordenarLinhas(linhas: Record<string, Valor>[], v: VisualPbi, valores: CampoPbi[]) {
  const s = v.sort?.[0];
  const alvo = s ? valores.find((c) => c.prop === s.prop && c.kind === s.kind) : valores[0];
  if (!alvo) return;
  const desc = s ? s.direction === "Descending" : false;
  linhas.sort((a, b) => { const r = compararValores(a[ref(alvo)] ?? null, b[ref(alvo)] ?? null); return desc ? -r : r; });
}

/** Opções de uma segmentação (valores distintos da coluna nas linhas filtradas pela página e pelas OUTRAS segmentações). */
export function opcoesSegmentacao(v: VisualPbi, ctx: ContextoVisual): { entidade: string; coluna: string; valores: Valor[] } | null {
  const c = v.fields?.Values?.[0];
  if (!c?.entity || !c.prop || !ENTIDADES[c.entity]?.colunas[c.prop]) return null;
  const { linhas } = linhasFiltradas(v, c.entity, { ...ctx }, chaveSegmentacao(c.entity, c.prop));
  const distintos = new Map<string, Valor>();
  for (const l of linhas) { const x = l[c.prop] ?? null; distintos.set(String(x ?? ""), x); }
  return { entidade: c.entity, coluna: c.prop, valores: [...distintos.values()].sort(compararValores) };
}

/**
 * Segmentações que NÃO são reproduzidas (exibem só o valor salvo no PBIX): intervalo de datas (modo Between) e a data de
 * corte dSemana — dSemana = quintas-feiras entre MIN(WBX[Início da Linha de Base]) e MAX(WBX[Término]); o início da LB
 * não existe no banco, então a lista de semanas não é derivável. A data salva É usada como SELECTEDVALUE (motor).
 */
export function segmentacaoNaoReproduzida(v: VisualPbi): string | undefined {
  if (v.type !== "slicer") return undefined;
  if (String(ler(v.o, "data", "mode", "Basic")) === "Between")
    return "Segmentação de intervalo de datas (Between) ainda não reproduzida; exibindo o intervalo salvo no PBIX.";
  const c = v.fields?.Values?.[0];
  if (c?.entity === "dSemana")
    return "Lista de semanas (dSemana) depende de WBX[Início da Linha de Base], sem valor na fonte atual; a data de corte salva no PBIX é aplicada às medidas.";
  return undefined;
}

export const ehSegmentacaoSemana = (v: VisualPbi) => v.type === "slicer" && v.fields?.Values?.[0]?.entity === "dSemana" && v.fields.Values[0].prop === "Data";
export const CHAVE_SEMANA = chaveSegmentacao("dSemana", "Data");

/**
 * dSemana (tabela calculada do PBIX, lida com pbixray em 15/10/2026):
 *   FILTER(DISTINCT(SELECTCOLUMNS(dCalendario, "Data", dCalendario[Início da Semana] + 4)), [Data] >= [Data Min Projeto] && [Data] <= [Data Max Projeto])
 *   Data Min Projeto = MINX(ALLSELECTED(WBX), WBX[Início da Linha de Base]); Data Max Projeto = MAXX(ALLSELECTED(WBX), WBX[Término]).
 * Quinta-feira à 00:00 comparada com DATA-HORA (uma quinta com início às 08:00 fica de fora). Só é derivável com o Início da
 * LB (cronograma isolado do BI); sem ele → undefined (a segmentação mostra o valor salvo).
 */
export function semanasDSemana(wbx: FonteCarregada | undefined): string[] | "carregando" | undefined {
  if (!wbx || wbx.estado === "carregando") return "carregando";
  if (wbx.estado !== "ok" || !wbx.linhas || wbx.vazias?.includes("Início da Linha de Base")) return undefined;
  let min: string | null = null, max: string | null = null;
  for (const l of wbx.linhas) {
    const i = l["Início da Linha de Base"], t = l["Término"];
    if (typeof i === "string" && (min === null || i < min)) min = i;
    if (typeof t === "string" && (max === null || t > max)) max = t;
  }
  if (!min || !max) return undefined;
  const sMin = serial(min), sMax = serial(max);
  if (sMin === null || sMax === null) return undefined;
  const out: string[] = [];
  let d = Math.ceil(sMin); // primeiro dia à 00:00 que é >= início (data-hora)
  d += (4 - new Date(d * 86_400_000).getUTCDay() + 7) % 7; // quinta-feira
  for (; d <= sMax; d += 7) out.push(new Date(d * 86_400_000).toISOString().slice(0, 10));
  return out;
}
