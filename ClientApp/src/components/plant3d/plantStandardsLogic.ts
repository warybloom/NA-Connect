// Lógica PURA do Catálogo de Padrões e dos pesos de linha (Plant 3D). Sem rede e sem React: testável em node.
// Regra de ouro: nada aqui inventa valor — padrão ausente/ não configurado fica "não avaliado", nunca "válido".

export type SourceRef = { table: string; column: string };
export type StandardCatalog = {
  id: string;
  name: string;
  kind: string;
  targetCategory: string;
  values: string[];
  pid: SourceRef;
  piping: SourceRef;
  enabled: boolean;
};
export type PlantStandards = { catalogs: StandardCatalog[]; tagLineColumn: string; updatedAtUtc: string | null };

export const TAG_LINE_PADRAO = "TagLine";

/** Limites aplicados no cliente (o servidor valida de novo). */
export const LIMITES_PADROES = {
  catalogos: 50,
  valoresPorCatalogo: 5000,
  tamanhoValor: 120,
  tamanhoNome: 80,
  tamanhoColuna: 128,
  arquivoBytes: 2 * 1024 * 1024,
  linhasPlanilha: 20000,
} as const;

/** Tipos de catálogo (kind). "custom" = livre. Slugs estáveis: o backend recebe exatamente estes textos. */
export const TIPOS_CATALOGO: ReadonlyArray<{ id: string; label: string }> = [
  { id: "equipment", label: "Tipo de equipamento" },
  { id: "valve", label: "Tipo de válvula" },
  { id: "instrument", label: "Tipo de instrumento" },
  { id: "line-service", label: "Serviço da linha" },
  { id: "line-spec", label: "Spec da linha" },
  { id: "line-insulation", label: "Isolamento da linha" },
  { id: "diameter", label: "Diâmetro" },
  { id: "sigla", label: "Siglas" },
  { id: "area", label: "Área" },
  { id: "subarea", label: "Subárea" },
  { id: "custom", label: "Personalizado" },
];

export const rotuloTipo = (kind: string) => TIPOS_CATALOGO.find(t => t.id === kind)?.label ?? kind;

const CONTROLE = /[\u0000-\u001F\u007F-\u009F\u200B-\u200F\u2028-\u202E\uFEFF]/g;

/** Texto de célula/entrada → valor de catálogo: sem controles/zero-width, espaços colapsados, truncado. */
export function limparValor(bruto: unknown): string {
  if (bruto === null || bruto === undefined) return "";
  const t = String(bruto).replace(CONTROLE, " ").replace(/\s+/g, " ").trim();
  return t.slice(0, LIMITES_PADROES.tamanhoValor);
}

export const chaveValor = (v: string) => v.normalize("NFC").toLocaleLowerCase("pt-BR");

/** Remove vazios e duplicatas (sem diferenciar maiúsculas). Mantém a primeira ocorrência e a ordem. */
export function unirValores(...listas: ReadonlyArray<ReadonlyArray<string>>): { valores: string[]; duplicados: number; vazios: number } {
  const vistos = new Set<string>();
  const valores: string[] = [];
  let duplicados = 0, vazios = 0;
  for (const lista of listas) {
    for (const bruto of lista) {
      const v = limparValor(bruto);
      if (!v) { vazios++; continue; }
      const k = chaveValor(v);
      if (vistos.has(k)) { duplicados++; continue; }
      vistos.add(k);
      valores.push(v);
    }
  }
  return { valores, duplicados, vazios };
}

export const semFonte = (c: Pick<StandardCatalog, "pid" | "piping">) =>
  !(c.pid.table && c.pid.column) && !(c.piping.table && c.piping.column);

export type Colunas = Record<string, string[]>;

/** Acha a coluna sem diferenciar maiúsculas/minúsculas (TagLine = TAGLINE = tagline). Nunca cria coluna. */
export function acharColuna(colunas: ReadonlyArray<string> | undefined, nome: string): string | null {
  const alvo = nome.trim().toLowerCase();
  if (!alvo || !colunas) return null;
  return colunas.find(c => c.toLowerCase() === alvo) ?? null;
}

/** Tabelas do schema que possuem a coluna da chave da linha (case-insensitive). */
export function tabelasComColuna(colunas: Colunas, nome: string): Array<{ tabela: string; coluna: string }> {
  return Object.entries(colunas)
    .map(([tabela, cols]) => ({ tabela, coluna: acharColuna(cols, nome) }))
    .filter((x): x is { tabela: string; coluna: string } => x.coluna !== null)
    .sort((a, b) => a.tabela.localeCompare(b.tabela, "pt-BR"));
}

function validarFonte(rotulo: string, f: SourceRef, colunas: Colunas, temSchema: boolean): string[] {
  const e: string[] = [];
  if (!!f.table !== !!f.column) e.push(`${rotulo}: escolha tabela e coluna (ou deixe as duas vazias).`);
  if (f.table && f.column && temSchema) {
    if (!colunas[f.table]) e.push(`${rotulo}: tabela "${f.table}" não existe no schema atual.`);
    else if (!colunas[f.table].includes(f.column)) e.push(`${rotulo}: coluna "${f.column}" não existe em ${f.table}.`);
  }
  return e;
}

export type ResultadoValidacao = { erros: string[]; avisos: string[] };

export function validarCatalogo(
  c: StandardCatalog, pid: Colunas, piping: Colunas, temSchemaPid: boolean, temSchemaPiping: boolean, categoriasValidas: ReadonlyArray<string>,
): ResultadoValidacao {
  const erros: string[] = [], avisos: string[] = [];
  if (!c.name.trim()) erros.push("Informe o nome do catálogo.");
  if (c.name.length > LIMITES_PADROES.tamanhoNome) erros.push(`Nome acima de ${LIMITES_PADROES.tamanhoNome} caracteres.`);
  if (!c.kind.trim()) erros.push("Escolha o tipo (kind).");
  if (!c.targetCategory.trim()) erros.push("Escolha a categoria alvo.");
  else if (categoriasValidas.length > 0 && !categoriasValidas.includes(c.targetCategory)) erros.push(`Categoria alvo "${c.targetCategory}" não existe no DE/PARA salvo.`);
  if (c.values.length > LIMITES_PADROES.valoresPorCatalogo) erros.push(`Mais de ${LIMITES_PADROES.valoresPorCatalogo} valores.`);
  if (c.values.some(v => !v.trim() || v.length > LIMITES_PADROES.tamanhoValor)) erros.push("Há valores vazios ou acima do tamanho máximo.");
  erros.push(...validarFonte("P&ID", c.pid, pid, temSchemaPid));
  erros.push(...validarFonte("3D (Piping)", c.piping, piping, temSchemaPiping));
  if (c.enabled && c.values.length === 0) avisos.push("Catálogo ativo sem valores: nada será validado (itens ficam “não avaliados”).");
  if (c.enabled && semFonte(c)) avisos.push("Sem tabela/coluna de origem: nada será validado (itens ficam “não avaliados”).");
  return { erros, avisos };
}

export function validarPadroes(
  s: Pick<PlantStandards, "catalogs" | "tagLineColumn">, pid: Colunas, piping: Colunas, categoriasValidas: ReadonlyArray<string>,
): { global: string[]; porCatalogo: ResultadoValidacao[] } {
  const global: string[] = [];
  if (!s.tagLineColumn.trim()) global.push("Informe o nome da coluna-chave da linha (padrão TagLine).");
  if (s.tagLineColumn.length > LIMITES_PADROES.tamanhoColuna) global.push("Nome da coluna-chave muito longo.");
  if (s.catalogs.length > LIMITES_PADROES.catalogos) global.push(`Máximo de ${LIMITES_PADROES.catalogos} catálogos.`);
  const ids = new Set<string>();
  s.catalogs.forEach(c => { if (ids.has(c.id)) global.push(`ID de catálogo duplicado: ${c.id}.`); ids.add(c.id); });
  const nomes = new Map<string, number>();
  s.catalogs.forEach(c => { const k = c.name.trim().toLowerCase(); if (k) nomes.set(k, (nomes.get(k) ?? 0) + 1); });
  nomes.forEach((n, k) => { if (n > 1) global.push(`Nome de catálogo duplicado: "${k}".`); });
  const temPid = Object.keys(pid).length > 0, temPiping = Object.keys(piping).length > 0;
  return { global, porCatalogo: s.catalogs.map(c => validarCatalogo(c, pid, piping, temPid, temPiping, categoriasValidas)) };
}

/** Forma enviada ao PUT: valores limpos e sem duplicatas, textos aparados. */
export function paraEnvioPadroes(s: Pick<PlantStandards, "catalogs" | "tagLineColumn">) {
  return {
    catalogs: s.catalogs.map(c => ({
      id: c.id,
      name: c.name.trim(),
      kind: c.kind.trim(),
      targetCategory: c.targetCategory.trim(),
      values: unirValores(c.values).valores,
      pid: { table: c.pid.table.trim(), column: c.pid.column.trim() },
      piping: { table: c.piping.table.trim(), column: c.piping.column.trim() },
      enabled: !!c.enabled,
    })),
    tagLineColumn: s.tagLineColumn.trim(),
  };
}

// ───────── resposta do servidor (defensiva) ─────────
const txt = (v: unknown) => (typeof v === "string" ? v : "");
const fonte = (v: unknown): SourceRef => {
  const o = (v ?? {}) as Record<string, unknown>;
  return { table: txt(o.table), column: txt(o.column) };
};

export function normalizarPadroes(raw: unknown, novoId: () => string): PlantStandards {
  const r = (raw ?? {}) as Record<string, unknown>;
  const lista = Array.isArray(r.catalogs) ? (r.catalogs as unknown[]) : [];
  return {
    catalogs: lista.map(item => {
      const c = (item ?? {}) as Record<string, unknown>;
      return {
        id: txt(c.id) || novoId(),
        name: txt(c.name),
        kind: txt(c.kind),
        targetCategory: txt(c.targetCategory),
        values: Array.isArray(c.values) ? (c.values as unknown[]).filter((x): x is string => typeof x === "string") : [],
        pid: fonte(c.pid),
        piping: fonte(c.piping),
        enabled: c.enabled !== false,
      };
    }),
    tagLineColumn: txt(r.tagLineColumn).trim() || TAG_LINE_PADRAO,
    updatedAtUtc: typeof r.updatedAtUtc === "string" ? r.updatedAtUtc : null,
  };
}

// ───────── qualidade (GET /standards/quality) ─────────
export type QualityStatus = "ready" | "no-baseline" | "no-mapping" | "no-standards" | "unlinked";
export type QualityIssue = { kind: string; count: number };
export type QualityCategory = { id: string; label: string; total: number; valid: number; invalid: number; unchecked: number; issues: QualityIssue[] };
export type StandardsQuality = { status: QualityStatus; categories: QualityCategory[]; updatedAtUtc: string | null };

const STATUS_QUALIDADE: readonly QualityStatus[] = ["ready", "no-baseline", "no-mapping", "no-standards", "unlinked"];
const nat = (v: unknown): number => (typeof v === "number" && Number.isFinite(v) && v >= 0 ? v : 0);

export function normalizarQualidade(raw: unknown): StandardsQuality {
  const r = (raw ?? {}) as Record<string, unknown>;
  const status = STATUS_QUALIDADE.find(s => s === r.status);
  if (!status) throw new Error("Resposta inesperada de /standards/quality (status ausente ou desconhecido).");
  const lista = Array.isArray(r.categories) ? (r.categories as unknown[]) : [];
  return {
    status,
    updatedAtUtc: typeof r.updatedAtUtc === "string" ? r.updatedAtUtc : null,
    categories: lista.map(item => {
      const c = (item ?? {}) as Record<string, unknown>;
      const id = txt(c.id);
      return {
        id, label: txt(c.label) || id,
        total: nat(c.total), valid: nat(c.valid), invalid: nat(c.invalid), unchecked: nat(c.unchecked),
        issues: (Array.isArray(c.issues) ? (c.issues as unknown[]) : [])
          .map(i => ({ kind: txt((i as Record<string, unknown>)?.kind), count: nat((i as Record<string, unknown>)?.count) }))
          .filter(i => i.kind && i.count > 0),
      };
    }),
  };
}

/** % de válidos entre os AVALIADOS (valid+invalid). Sem avaliados → null (nunca 0% nem 100% inventados). */
export function percentualValidos(c: Pick<QualityCategory, "valid" | "invalid">): number | null {
  const avaliados = c.valid + c.invalid;
  return avaliados > 0 ? (c.valid / avaliados) * 100 : null;
}

// ───────── pesos de linha ─────────
export type LineWeight = { key: string; label: string; percent: number };
export type LineWeights = { weights: LineWeight[]; updatedAtUtc: string | null };

export const PARCELAS_SUGERIDAS: ReadonlyArray<{ key: string; label: string }> = [
  { key: "modelagem", label: "Modelagem da linha" },
  { key: "valvulas", label: "Válvulas" },
  { key: "instrumentos", label: "Instrumentos" },
  { key: "tie-ins", label: "Tie-ins" },
  { key: "suportes", label: "Suportes" },
];

export const CHAVE_PESO = /^[a-z0-9][a-z0-9_-]{0,39}$/;
export const TOLERANCIA_SOMA = 0.005;

export const somaPesos = (w: ReadonlyArray<Pick<LineWeight, "percent">>) =>
  Math.round(w.reduce((n, x) => n + (Number.isFinite(x.percent) ? x.percent : 0), 0) * 1000) / 1000;

/** Slug estável a partir do rótulo (sem acentos). */
export function chaveDoRotulo(rotulo: string): string {
  return rotulo.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40);
}

export function validarPesos(w: ReadonlyArray<LineWeight>): string[] {
  const e: string[] = [];
  if (w.length === 0) return e; // sem pesos = inativo (permitido)
  if (w.length > 20) e.push("Máximo de 20 parcelas.");
  const chaves = new Set<string>();
  w.forEach((x, i) => {
    if (!CHAVE_PESO.test(x.key)) e.push(`Parcela ${i + 1}: identificador inválido (use letras minúsculas, números, - ou _).`);
    if (chaves.has(x.key)) e.push(`Identificador duplicado: "${x.key}".`);
    chaves.add(x.key);
    if (!x.label.trim()) e.push(`Parcela ${i + 1}: informe o nome.`);
    if (!Number.isFinite(x.percent) || x.percent < 0 || x.percent > 100) e.push(`Parcela ${i + 1}: percentual deve estar entre 0 e 100.`);
  });
  const soma = somaPesos(w);
  if (Math.abs(soma - 100) > TOLERANCIA_SOMA) e.push(`A soma dos pesos é ${soma.toLocaleString("pt-BR")}%; precisa ser exatamente 100%.`);
  return e;
}

export function normalizarPesos(raw: unknown): LineWeights {
  const r = (raw ?? {}) as Record<string, unknown>;
  const lista = Array.isArray(r.weights) ? (r.weights as unknown[]) : [];
  return {
    weights: lista.map(item => {
      const o = (item ?? {}) as Record<string, unknown>;
      const p = typeof o.percent === "number" ? o.percent : Number(o.percent);
      return { key: txt(o.key), label: txt(o.label) || txt(o.key), percent: Number.isFinite(p) ? p : 0 };
    }),
    updatedAtUtc: typeof r.updatedAtUtc === "string" ? r.updatedAtUtc : null,
  };
}
