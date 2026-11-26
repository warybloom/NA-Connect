// Entidades do modelo do PBIX → fonte oficial N&A Connect (somente leitura), com as colunas renomeadas para os NOMES DO BI.
// Correspondência conferida em 15/10/2026 contra o cache do PBIX (pbixray) e o banco (contagens por coluna em
// _bi_chs001_replica/STATUS_CLAUDE.md). Coluna do BI sem equivalente fica de fora do mapa — o visual que precisar dela
// recebe PENDENTE DE PARIDADE (nunca valor inventado).
import type { Linha } from "./filtrosPbi";
import type { Valor } from "./tipos";

export interface FonteEntidade {
  /** Endpoint (GET, ?projectId=) que devolve { linhas: Record<string, unknown>[] }. */
  endpoint: string;
  /**
   * Endpoint legado usado SÓ quando o principal responde "indisponivel" (BI isolado ainda sem versão gravada). Mesmas colunas;
   * o que o legado não tem fica nulo → PENDENTE com o motivo.
   */
  endpointAlternativo?: string;
  /** A série desta entidade deve vir da MESMA versão do cronograma que WBX (parâmetro ?versao= da carga de WBX). */
  mesmaVersaoDe?: string;
  /** Coluna do BI → coluna da fonte (ou função). */
  colunas: Record<string, string | ((r: Record<string, unknown>) => Valor)>;
  descricao: string;
  /** Transformação da lista inteira antes do mapeamento de colunas (ex.: hierarquia derivada da ordem das tarefas). */
  preparar?: (brutas: Record<string, unknown>[]) => Record<string, unknown>[];
  /** Colunas mapeadas SÓ para contagem (CountNonNull/DistinctCount): o valor da fonte não é o mesmo campo do BI. */
  soContagem?: string[];
  /** Colunas do BI sem fonte verificada → motivo exibido no selo PENDENTE (documenta a fonte necessária). */
  faltantes?: Record<string, string>;
}

const NAO_PERSISTIDO = "sem valor na fonte atual (o cronograma isolado do BI — bi_chs001 — ainda não tem versão gravada e o legado não guarda esse campo)";

/** Valor da chave do JSON de campos customizados do MS Project (Campos_Customizados_JSON) — texto exato, "" = vazio. */
const iso = (v: unknown): Valor => (v === null || v === undefined || v === "" ? null : String(v));

/**
 * WBX do PBIX = GeraWBS(Task) filtrado em IsLeaf: cada tarefa FOLHA com o nome dos ancestrais em "Level n". Level 2 = tarefa
 * de resumo do projeto (EDT "0"), Level 3 = EDT de 1 segmento ("1"), Level 4 = EDT de 2 segmentos ("1.5"…).
 * Fonte principal (cronograma isolado do BI, /api/bi-chs001/schedule/tarefas): traz UID_Tarefa_Pai → hierarquia pelo PAI,
 * como o GeraWBS do PBIX (conferido em 15/10/2026 na versão 47 do .mpp: 196 / 347 / 190 = cache, com o alias do 30%).
 * Fonte alternativa (legado, sem o pai): o ancestral vem da ORDEM do cronograma (ID_Cronograma = ordem das tarefas no .mpp) —
 * a última tarefa de resumo de cada profundidade antes da folha. Não usar o prefixo da EDT da folha: no CHS001 há folhas
 * do 90% com código "1.6.x" (EDT digitada), que o Power BI classifica pelo pai real.
 */
export function prepararCronograma(brutas: Record<string, unknown>[]): Record<string, unknown>[] {
  const ordenadas = [...brutas].sort((a, b) => Number(a.ID_Cronograma ?? 0) - Number(b.ID_Cronograma ?? 0));
  // Com o pai da tarefa persistido (UID_Tarefa_Pai — proposta de migração), a hierarquia vem do pai, como o GeraWBS do PBIX.
  if (ordenadas.some((r) => r.UID_Tarefa_Pai !== null && r.UID_Tarefa_Pai !== undefined && r.UID_Tarefa_Pai !== "")) return hierarquiaPorPai(ordenadas);
  const ancestrais: Record<number, string> = {};
  const folhas: Record<string, unknown>[] = [];
  for (const r of ordenadas) {
    const edt = String(r.EDT_WBS ?? "").trim();
    const nivel = edt === "0" ? 2 : edt ? edt.split(".").length + 2 : 0;
    if (r.Eh_Tarefa_Resumo === true || r.Eh_Tarefa_Resumo === 1) {
      if (!nivel) continue;
      ancestrais[nivel] = String(r.TASK_NAME ?? "");
      for (const k of Object.keys(ancestrais)) if (Number(k) > nivel) delete ancestrais[Number(k)];
      continue;
    }
    // Folha rasa (nível ≤ 4) é ela mesma o "Level n" do seu nível; acima dele, os ancestrais vigentes.
    const nome = (k: number) => (nivel === k ? String(r.TASK_NAME ?? "") : nivel > k ? ancestrais[k] ?? null : null);
    folhas.push({ ...r, Level_3: nome(3), Level_4: nome(4), Level_4_Original: nome(4) });
  }
  return folhas;
}

/**
 * GeraWBS(Task, "TaskUID", "TaskParentUID", "Nome"): caminho raiz → folha pelos pais. No cache do PBIX: Level 2 = resumo do
 * projeto (nível de estrutura 0), Level k = ancestral de nível k − 2, a folha ocupa o próprio nível e os seguintes ficam vazios.
 * Ciclo ou pai ausente → a folha fica sem Level (nunca nome inventado).
 */
function hierarquiaPorPai(ordenadas: Record<string, unknown>[]): Record<string, unknown>[] {
  const porUid = new Map(ordenadas.map((r) => [String(r.UID_Tarefa), r]));
  const folhas: Record<string, unknown>[] = [];
  for (const r of ordenadas) {
    if (r.Eh_Tarefa_Resumo === true || r.Eh_Tarefa_Resumo === 1) continue;
    const caminho: string[] = [];
    const original: string[] = [];
    const vistos = new Set<string>();
    let atual: Record<string, unknown> | undefined = r;
    let valido = true;
    while (atual) {
      const uid = String(atual.UID_Tarefa);
      if (vistos.has(uid)) { valido = false; break; }
      vistos.add(uid);
      caminho.unshift(String(atual.TASK_NAME ?? ""));
      // Cronograma isolado do BI: TASK_NAME = rótulo do BI (alias bi_chs001.Alias_Rotulo), TASK_NAME_Original = valor do .mpp.
      original.unshift(String(atual.TASK_NAME_Original ?? atual.TASK_NAME ?? ""));
      const pai = atual.UID_Tarefa_Pai;
      if (pai === null || pai === undefined || pai === "") break;
      atual = porUid.get(String(pai));
      if (!atual) { valido = false; break; }
    }
    const nivel = (k: number) => (valido ? caminho[k - 2] ?? null : null);
    folhas.push({ ...r, Level_3: nivel(3), Level_4: nivel(4), Level_5: nivel(5), Level_6: nivel(6), Level_7: nivel(7),
      Level_4_Original: valido ? original[2] ?? null : null });
  }
  return folhas;
}

/** Série diária do .mpp → grão do AssignmentByDay do PBIX (exportação Access do Project: um registro por semana, no DOMINGO). */
export function prepararSerieDiaria(brutas: Record<string, unknown>[]): Record<string, unknown>[] {
  const semanas = new Map<string, Record<string, unknown>>();
  for (const r of brutas) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(r.Data ?? ""));
    const v = Number(r.Trabalho_Real_Horas);
    if (!m || r.Trabalho_Real_Horas === null || !Number.isFinite(v)) continue;
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    d.setUTCDate(d.getUTCDate() - d.getUTCDay());
    const domingo = d.toISOString().slice(0, 10);
    const k = `${r.UID_Tarefa}|${domingo}`;
    const atual = semanas.get(k) ?? { TaskUID: String(r.UID_Tarefa), TimeByDay: domingo, Trabalho_Real: 0 };
    atual.Trabalho_Real = Number(atual.Trabalho_Real) + v;
    semanas.set(k, atual);
  }
  return [...semanas.values()];
}

const numero = (v: unknown): Valor => (v === null || v === undefined || v === "" || !Number.isFinite(Number(v)) ? null : Number(v));

const txt = (v: unknown): Valor => (v === null || v === undefined ? null : typeof v === "number" ? v : String(v));

/** ITEM é numérico no PBIX (coluna inteira da planilha); a fonte N&A guarda texto. Texto não numérico fica como veio. */
const numeroOuTexto = (v: unknown): Valor => {
  if (v === null || v === undefined || v === "") return null;
  const s = String(v).trim();
  return /^-?\d+$/.test(s) ? Number(s) : s;
};

/** Data ISO da fonte → texto dd/MM/yyyy (as datas da planilha de programação são texto no PBIX). */
const dataTexto = (v: unknown): Valor => {
  if (v === null || v === undefined || v === "") return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(v));
  return m ? `${m[3]}/${m[2]}/${m[1]}` : String(v);
};

export const ENTIDADES: Record<string, FonteEntidade> = {
  "LDP (4)": {
    endpoint: "/api/bi-chs001/ldp",
    descricao: "LDP atual do projeto (PmoLdpSnapshot — todas as linhas, inclusive AT) + gates 30/60/90 (VW_Ldp_Documento_Gates_Ultima_Versao)",
    colunas: {
      ITEM: (r) => numeroOuTexto(r.Item), DISCIPLINE: "Discipline", "N&A DOCUMENT NUMBER": "NA_Document_Number", "CLIENT DOCUMENT NUMBER": "Client_Document_Number",
      "DOCUMENT TITLE": "Document_Title", FORMAT: "Format", "REV.": "Revision", "STATUS IN THE FLOW": "Status_In_Flow",
      "DOCUMENT STATUS": "Document_Status", SITUATION: "Situation",
      "Fase 30%": "Fase_30", "DOCUMENT STATUS 30%": "Document_Status_30", "SITUATION 30%": "Situation_30",
      "Fase 60%": "Fase_60", "DOCUMENT STATUS 60%": "Document_Status_60", "SITUATION 60%": "Situation_60",
      "Fase 90%": "Fase_90", "DOCUMENT STATUS 90%": "Document_Status_90", "SITUATION 90%": "Situation_90",
    },
  },
  "Programação Semanal": {
    endpoint: "/api/bi-chs001/programacao",
    descricao: "Programação semanal importada (tb_WeeklyEngineeringSchedule, última importação do projeto)",
    colunas: {
      ITEM: (r) => numeroOuTexto(r.Weekly_Item),
      // No PBIX a fase é TEXTO "0.6" (cache do modelo); a fonte N&A guarda "0,6" → mesmo texto do BI.
      PHASE: (r) => (r.Weekly_Phase === null || r.Weekly_Phase === undefined || r.Weekly_Phase === "" ? null : String(r.Weekly_Phase).replace(",", ".")),
      DISCIPLINE: "Disciplina", "N&A DOCUMENT NUMBER": "Numero_NA_Atividade", "DOCUMENT TITLE": "Nome_Tarefa",
      "DOCUMENT STATUS": "Weekly_Progress_Status",
      "BASELINE DATE": (r) => dataTexto(r.Weekly_Baseline_Date), "REPLANNED DATE": (r) => dataTexto(r.Weekly_Replanned_Date),
    },
  },
  WBX: {
    endpoint: "/api/bi-chs001/schedule/tarefas",
    endpointAlternativo: "/api/bi-chs001/cronograma",
    descricao: "Cronograma isolado do BI (bi_chs001, cópia do .mpp oficial lida com MPXJ) — tarefas folha; alternativa: legado VW_Cronograma_Tipo2_Ultima_Versao",
    preparar: prepararCronograma,
    // Conferido em 15/10/2026 (.mpp v47, status 08/10/2026) contra o cache do PBIX: 755/755 folhas casadas por GUID; Início,
    // Término, Início/Término da LB idênticos em 755/755; Trab LB 737/755 exatos; Level 4 = 196/347/190 (alias do 30%).
    colunas: {
      "Level 3": (r) => iso(r.Level_3), "Level 4": (r) => iso(r.Level_4),
      // Valor BRUTO do .mpp no Level 4 (auditoria do alias; não é coluna do PBIX).
      "Level 4 Original": (r) => iso(r.Level_4_Original),
      // MS Project "ID" não é persistido; o UID identifica a mesma tarefa — usado SÓ em CountNonNull (soContagem).
      TaskID: (r) => iso(r.UID_Tarefa), EDT: "EDT_WBS",
      Status1: "Status1", Disciplina: "Disciplina", Responsabilidade: "Responsabilidade", Pacote: "Pacote",
      "NA DOCUMENT NUMBER": "NA_Document_Number_Campo",
      "Término da Linha de Base": (r) => iso(r.Termino_Linha_Base), "Término": (r) => iso(r.Termino),
      // Work da tarefa (horas). Diferença conhecida: marcos = 1 min no PBIX (exportação Access) e 0 no .mpp.
      Trabalho: (r) => numero(r.Trabalho_Horas),
      // Campos nativos do .mpp gravados pelo BI isolado; no legado são nulos (→ PENDENTE com o motivo).
      "Início": (r) => iso(r.Inicio), "Início da Linha de Base": (r) => iso(r.Inicio_Linha_Base),
      "Trab Linha de Base": (r) => numero(r.Trabalho_Linha_Base_Horas), "Trabalho Real": (r) => numero(r.Trabalho_Real_Horas),
      "Level 5": (r) => iso(r.Level_5), "Level 6": (r) => iso(r.Level_6), "Level 7": (r) => iso(r.Level_7),
      // Chave da relação AssignmentByDay[TaskUID] → WBX[NodeKey].
      NodeKey: (r) => iso(r.UID_Tarefa),
    },
    soContagem: ["TaskID"],
    // Coluna mapeada mas SEM DADO na fonte atual (todas as linhas nulas) → este motivo aparece no PENDENTE.
    faltantes: {
      "Início": `WBX[Início] ${NAO_PERSISTIDO}`,
      "Início da Linha de Base": `WBX[Início da Linha de Base] ${NAO_PERSISTIDO}`,
      "Trabalho": `WBX[Trabalho] ${NAO_PERSISTIDO}`,
      "Trab Linha de Base": `WBX[Trab Linha de Base] (BaselineWork do .mpp) ${NAO_PERSISTIDO}`,
      "Trabalho Real": `WBX[Trabalho Real] (ActualWork do .mpp) ${NAO_PERSISTIDO}`,
      "Level 5": `hierarquia Level 5–7 depende do pai da tarefa (UID_Tarefa_Pai): ${NAO_PERSISTIDO}`,
      "Level 6": `hierarquia Level 5–7 depende do pai da tarefa (UID_Tarefa_Pai): ${NAO_PERSISTIDO}`,
      "Level 7": `hierarquia Level 5–7 depende do pai da tarefa (UID_Tarefa_Pai): ${NAO_PERSISTIDO}`,
    },
  },
  AssignmentByDay: {
    endpoint: "/api/bi-chs001/schedule/trabalho-diario",
    // Sem alternativa: a série só existe no cronograma isolado, e SEMPRE da mesma versão das tarefas (WBX).
    mesmaVersaoDe: "WBX",
    descricao: "Trabalho real por tarefa × dia (bi_chs001.Trabalho_Tarefa_Dia, timephased do .mpp), agrupado por semana (domingo) como no PBIX",
    preparar: prepararSerieDiaria,
    colunas: { TaskUID: "TaskUID", TimeByDay: "TimeByDay", "Trabalho Real": (r) => numero(r.Trabalho_Real) },
  },
};

export function converterLinhas(entidade: string, brutasOriginais: Record<string, unknown>[]): Linha[] {
  const def = ENTIDADES[entidade];
  if (!def) return [];
  const brutas = def.preparar ? def.preparar(brutasOriginais) : brutasOriginais;
  return brutas.map((r) => {
    const l: Linha = {};
    for (const [bi, src] of Object.entries(def.colunas)) l[bi] = typeof src === "function" ? src(r) : txt(r[src]);
    return l;
  });
}

/** Colunas do BI mapeadas cuja fonte atual não traz NENHUM valor (todas nulas) — tratadas como sem fonte. */
export function colunasVazias(entidade: string, linhas: Linha[]): string[] {
  const def = ENTIDADES[entidade];
  if (!def) return [];
  return Object.keys(def.colunas).filter((c) => linhas.every((l) => l[c] === null || l[c] === undefined || l[c] === ""));
}

export const colunaMapeada = (entidade: string, coluna: string) => !!ENTIDADES[entidade]?.colunas[coluna];
