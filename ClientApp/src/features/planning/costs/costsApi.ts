// Custos do Projeto — cliente de /api/project-costs/* (fonte oficial N&A: schema costs, por Project_ID_ACC).
// Custo oficial vem das tabelas costs.* e das regras do backend (CustoMensal/CustoHora, rateio AUTO/MANUAL,
// reconhecimento). Timesheet.CustoCalculado NUNCA é usado como custo.

const BASE = "/api/project-costs";

export interface Profissional {
  id: number; projectIdAcc: string; funcao: string | null; disciplina: string | null; nome: string; empresa: string | null;
  aliasTimesheet: string | null; hhBaseMensal: number; remuneracao: number; encargos: number; softwares: number; computadores: number;
  custoMensal: number; custoHora: number; ativo: boolean;
}
export interface ItemCusto { id: number; professionalId: number; categoria: string; descricao: string; valor: number; ativo: boolean }
export interface Pacote {
  id: number; projectIdAcc: string; empresa: string | null; nomeReferencia: string; aliasTimesheet: string | null; descricao: string | null;
  hhBase: number; hhAddenda: number; hhEfetiva: number; custoBase: number; custoAddenda: number; custoEfetivo: number; documents: number; ativo: boolean;
  /** Ausente em backends anteriores = DOCUMENTO. */
  modeloCadastro?: ModeloPacote; disciplinas?: number;
}
// ------------------------------------------------------------------------------- Pacote Por Disciplina ----
export type ModeloPacote = "DOCUMENTO" | "DISCIPLINA";
export type Abrangencia = "TODOS" | "SELECIONADOS";
export const ehPorDisciplina = (p: Pick<Pacote, "modeloCadastro">) => p.modeloCadastro === "DISCIPLINA";
export interface SuporteModelo { disciplineModelInstalled: boolean; message: string | null }
export interface DocumentoLdp { codigo: string; disciplina: string; titulo: string | null; status: string | null; cancelado: boolean; plannedHH: number | null; plannedCost: number | null; statusPercentual: number | null }
export interface DisciplinaLdp { disciplina: string; documentos: number; ativos: number; cancelados: number; hhLdpAtivos: number; custoLdpAtivos: number; itens: DocumentoLdp[] }
export interface CatalogoLdp { disciplinas: DisciplinaLdp[]; documentosSemDisciplina: number; diagnostico: string | null }
export interface DisciplinaInput { disciplina: string; hhPrevisto: number; custoPrevisto: number; abrangencia: Abrangencia; documentos: string[] | null; justificativa: string | null }
export interface PacoteDisciplinaInput { empresa: string | null; nomeReferencia: string; aliasTimesheet: string | null; descricao: string | null; disciplinas: DisciplinaInput[] }
export interface PlanoDisciplina {
  disciplina: string; abrangencia: Abrangencia; hhPrevisto: number; custoPrevisto: number; hhLdp: number; custoLdp: number; divergenciaHH: number; divergenciaCusto: number;
  documentosDisciplina: number; documentosElegiveis: number; justificativa: string | null; erros: string[]; avisos: string[]; valido: boolean;
  documentos: { documentoCodigo: string; titulo: string | null; documentStatus: string | null; hhPrevisto: number; custoPrevisto: number; hhLdp: number | null; custoLdp: number | null }[];
  excluidos: { documentoCodigo: string; motivo: string }[];
}
export interface PreviaPacoteDisciplina { disciplinas: PlanoDisciplina[]; hhTotal: number; custoTotal: number; hhLdpTotal: number; custoLdpTotal: number; valido: boolean; erros: string[] }
export interface DisciplinaCadastrada { id: number; disciplina: string; abrangencia: Abrangencia; hhPrevisto: number; custoPrevisto: number; hhPrevistoLdp: number | null; custoPrevistoLdp: number | null; documentosLdp: number | null; justificativa: string | null; documentos: string[]; atualizadoEm: string }
export interface ApropriacaoDocumento {
  disciplina: string; documentoCodigo: string; documentStatus: string | null; statusPercentual: number | null;
  hhPrevisto: number; hhDireto: number; hhRateado: number; hhBruto: number; hhApropriado: number; hhExcedente: number; hhSaldo: number;
  custoPrevisto: number; custoBruto: number; custoApropriado: number; custoExcedente: number; custoLiberado: number;
}
export interface PendenciaDisciplina { tipo: string; disciplina: string | null; documentoCodigo: string | null; horas: number; lancamentos: number; motivo: string }
export interface AlertaExcedente { projectIdAcc: string; packageId: number; pacote: string; profissional: string | null; disciplina: string; documentoCodigo: string | null; hhPrevisto: number; hhBruto: number; hhApropriado: number; hhExcedente: number; situacao: string }
export interface TotaisDisciplina { hhPrevisto: number; hhBruto: number; hhApropriado: number; hhExcedente: number; hhSaldo: number; custoPrevisto: number; custoBruto: number; custoApropriado: number; custoExcedente: number; custoLiberado: number; hhPendente: number }
export interface DetalhePacoteDisciplina { pacote: Pacote; cadastro: DisciplinaCadastrada[]; rateioAtual: PreviaPacoteDisciplina; apropriacao: ApropriacaoDocumento[]; pendencias: PendenciaDisciplina[]; excedentes: AlertaExcedente[]; totais: TotaisDisciplina; historicalMeasurement?: import('@/components/pmo/pmoFacts').HistoricalMeasurementFacts; hhGerencial?: number; custoGerencial?: number }
export interface LinhaImportacaoDisciplina { row: number; pacote: string; profissional: string | null; empresa: string | null; disciplina: string; hhPrevisto: number | null; custoPrevisto: number | null; abrangencia: Abrangencia; documentos: string[]; justificativa: string | null; erro: string | null }
export interface PreviaImportacaoDisciplina { rows: LinhaImportacaoDisciplina[]; pacotes: { pacote: string; profissional: string | null; empresa: string | null; previa: PreviaPacoteDisciplina; erros: string[] }[]; valido: boolean; invalidas: number }
export type Modo = "AUTO" | "MANUAL";
export interface DocumentoPacote { id: number; packageId: number; documentoCodigo: string; disciplina: string | null; hhAlocada: number; custoAlocado: number; modoHH: Modo; modoCusto: Modo; ativo: boolean }
export interface Aditivo { id: number; packageId: number; data: string; descricao: string; hhAdicional: number; custoAdicional: number; ativo: boolean }
export interface RateioDocumento { documentId: number; documentoCodigo: string; disciplina: string | null; hhFinal: number; modoHH: Modo; custoFinal: number; modoCusto: Modo }
export interface Rateio { packageId: number; hhEfetiva: number; custoEfetivo: number; documentos: RateioDocumento[]; erro: string | null }
export interface Reconhecimento {
  packageId: number; professionalId: number | null; documentoCodigo: string; disciplina: string | null; timesheetHours: number; contractedHours: number;
  recognizedHours: number; excessHours: number; theoreticalCost: number; recognizedCost: number; excessCost: number; ambiguousMatch: boolean;
}
export interface Resumo {
  professionals: number; expenses: number; packages: number; costPredicted: number; recognized: number; excess: number;
  plannedHours: number; workedHours: number; recognizedHours: number; excessHours: number; timesheetStatus?: string | null;
}
export interface LinhaImportacao { row: number; valid: number; project: string | null; package: string | null; document: string | null; error: string | null }
export interface PreviaImportacao { rows: LinhaImportacao[]; valid: number; invalid: number; warnings: number }
export interface ResultadoImportacao { packagesCreated: number; documentsCreated: number }
export interface PeriodoProfissional { id:number; professionalId:number; start:string; end:string|null; monthlyHours:number; remuneration:number; manualRate:number|null; monthlyCost:number; hourlyRate:number; manualRateUsed:boolean; active:boolean; items: {id:number;category:string;description:string;calculationType:string;amount:number;percentage:number|null;calculationBase:string;active:boolean}[] }
export interface ItemVigenciaInput { category:string; description:string; calculationType:"FIXO"|"PERCENTUAL"; amount:number; percentage:number|null; calculationBase:"REMUNERACAO" }
/** Corpo de criação/edição de vigência. Valores financeiros são SEMPRE number (o backend usa decimal). */
export interface VigenciaProfissionalInput {
  start:string; end:string|null; monthlyHours:number; remuneration:number; manualRate:number|null;
  confirmClosePrevious:boolean; copyPreviousComposition:boolean; excludePreviousItemIds:number[]; items:ItemVigenciaInput[];
}
export interface PreviaVigencia { openPeriodId:number|null; closeOpenPeriodOn:string|null; confirmationRequired:boolean; isHistoricalBackfill:boolean }
export interface ImportacaoProfissionalLinha {
  row:number; valid:boolean; action:string; funcao:string|null; disciplina:string|null; nome:string; remuneracao:number; encargos:number;
  softwares:number; computadores:number; totalInformado:number; totalCalculado:number; custoHora:number|null; vigenciaInicio:string;
  vigenciaFim:string|null; hhBaseMensal:number; warning:string|null; error:string|null;
}
export interface PreviaImportacaoProfissionais {
  rows:ImportacaoProfissionalLinha[]; valid:number; invalid:number; warnings:number; professionalsToCreate:number; periodsToCreate:number; periodsToUpdate:number;
}
export interface ResultadoImportacaoProfissionais { professionalsCreated:number; professionalsUpdated:number; periodsCreated:number; periodsUpdated:number }
export interface ProjetoElegivelCusto { projectIdAcc:string; name:string; isActive:boolean; hasSync:boolean }
export interface AtribuicaoProfissionaisInput { professionalIds:number[]; destinationProjectIds:string[] }
export interface AtribuicaoDestino { projectIdAcc:string; name:string; toCreate:number; alreadyExists:number }
export interface PreviaAtribuicaoProfissionais { destinations:AtribuicaoDestino[] }
export interface ResultadoAtribuicaoProfissionais { professionalsCreated:number; destinations:AtribuicaoDestino[] }
export interface ProjectTax { id:number; projectIdAcc:string; name:string; type:string; active:boolean; periods:{id:number;start:string;end:string|null;calculationType:string;percentage:number|null;fixedValue:number|null;calculationBase:string;active:boolean}[] }
export interface CopyPreview { destinationProjectId:string; professionalsToCreate:number; taxDefinitionsToCreate:number; packagesToCreate:number; expensesToCreate:number; conflicts:string[] }
export interface CopyInput { sourceProjectId:string; destinationProjectId:string; professionals:boolean; professionalComposition:boolean; projectTaxes:boolean; packages:boolean; expenses:boolean; skipConflicts:boolean }
export interface CopyBatchInput { sourceProjectId:string; destinationProjectIds:string[]; professionals:boolean; professionalComposition:boolean; projectTaxes:boolean; packages:boolean; expenses:boolean; skipConflicts:boolean }
export interface DocumentActual { documentCode:string; workedHours:number; internalActualCost:number; packageRecognizedCost:number; actualCost:number; packagePlannedCost:number; packageRemaining:number; uncostedHours:number; ambiguousRows:number; plannedIsPackageOnly:boolean }

/**
 * Enriquecimento por documento de pacote (status LDP, peso do Document_Status, disciplina canônica da Norma, valores base/efetivos e
 * reconhecimento). O backend está evoluindo o contrato: os campos são lidos de forma tolerante (nomes PT/EN) de documents, allocation
 * e recognition. O React NUNCA calcula valores financeiros — campo ausente = null ("—" na tela).
 */
export interface ExtrasDocumento {
  documentStatus: string | null; doc3: string | null; disciplinaCanonica: string | null;
  /** Peso configurado do Document_Status, 0–100 (mesma escala do Avanço físico). */
  statusPercent: number | null;
  /** false = status sem peso configurado (custo liberado R$ 0). null = backend não informou. */
  statusConfigurado: boolean | null;
  cancelado: boolean;
  hhBase: number | null; hhEfetiva: number | null; hhTimesheet: number | null; hhContabilizada: number | null; hhExcedente: number | null;
  custoBase: number | null; custoEfetivo: number | null; custoLiberado: number | null; valorHora: number | null;
  /** Parcelas que compõem o efetivo (redistribuição de cancelados e aditivos), quando o backend informa. */
  hhRedistribuida: number | null; hhAdicional: number | null; custoRedistribuido: number | null; custoAdicional: number | null;
  matchAmbiguo: boolean; diagnosticos: string[];
}
/** Linha da prévia de importação: modelo Pacote/Profissional | Número do documento | HH previsto | Valor/hora (custo calculado pelo backend). */
export interface ExtrasImportacao { empresa: string | null; alias: string | null; hhPrevista: number | null; valorHora: number | null; custoDocumento: number | null; aviso: string | null }

type Bruto = Record<string, unknown>;
const comoBruto = (o: unknown): Bruto => (o && typeof o === "object" ? (o as Bruto) : {});
const numDe = (o: Bruto, ...k: string[]) => { for (const c of k) { const v = o[c]; if (typeof v === "number" && Number.isFinite(v)) return v; } return null; };
const txtDe = (o: Bruto, ...k: string[]) => { for (const c of k) { const v = o[c]; if (typeof v === "string" && v.trim()) return v.trim(); } return null; };
const boolDe = (o: Bruto, ...k: string[]) => { for (const c of k) { const v = o[c]; if (typeof v === "boolean") return v; } return null; };
function listaDe(o: Bruto, ...k: string[]): string[] {
  const r: string[] = [];
  for (const c of k) {
    const v = o[c];
    for (const item of Array.isArray(v) ? v : v == null ? [] : [v]) {
      if (typeof item === "string" && item.trim()) r.push(item.trim());
      else if (item && typeof item === "object") { const t = txtDe(item as Bruto, "message", "mensagem", "description", "code"); if (t) r.push(t); }
    }
  }
  return r;
}

/** Rótulo do backend (CoordinationDisciplineResolver) quando Doc3 + Norma não resolvem um Nome padrão único. */
export const SEM_DISCIPLINA = "SEM DISCIPLINA";

/**
 * Corpo de documento do pacote (CostDocumentInput). Mesmos campos essenciais do Excel: Número do documento, HH previsto e Valor/hora.
 * Disciplina não é enviada (oficial = Doc3 + Norma). O custo NÃO é calculado aqui: com Valor/hora o backend calcula HH × Valor/hora.
 */
export interface DocumentoInput { documentoCodigo: string; disciplina: null; hhAlocada: number | null; custoAlocado: number | null; valorHora: number | null; modoHH: Modo; modoCusto: Modo }
export interface CamposDocumento { codigo: string; hh: string; valorHora: string }

/**
 * Valida os campos digitados e monta o corpo. HH vazio = AUTO; Valor/hora vazio = custo AUTO.
 * `custoLegado`: custo MANUAL já gravado sem Valor/hora (cadastro/planilha antigos) — preservado como está se o Valor/hora ficar vazio.
 */
export function montarDocumento(c: CamposDocumento, custoLegado: number | null = null): { doc: DocumentoInput } | { erro: string } {
  const codigo = c.codigo.trim();
  if (!codigo) return { erro: "Informe o número do documento." };
  const hh = lerNumero(c.hh); const valorHora = lerNumero(c.valorHora);
  if (c.hh.trim() && hh === null) return { erro: `HH previsto inválido: "${c.hh.trim()}".` };
  if (c.valorHora.trim() && valorHora === null) return { erro: `Valor/hora inválido: "${c.valorHora.trim()}".` };
  if ((hh ?? 0) < 0 || (valorHora ?? 0) < 0) return { erro: "HH previsto e Valor/hora não podem ser negativos." };
  if (valorHora !== null && hh === null) return { erro: "Valor/hora exige HH previsto (o custo é HH previsto × Valor/hora, calculado pelo backend)." };
  const custoManual = valorHora === null && custoLegado !== null;
  return { doc: {
    documentoCodigo: codigo, disciplina: null, hhAlocada: hh, valorHora,
    custoAlocado: custoManual ? custoLegado : null,
    modoHH: hh === null ? "AUTO" : "MANUAL", modoCusto: valorHora !== null || custoManual ? "MANUAL" : "AUTO",
  } };
}

export function lerExtrasDocumento(fonte: unknown): ExtrasDocumento {
  const o = comoBruto(fonte);
  const documentStatus = txtDe(o, "documentStatus", "document_Status", "statusDocumento", "ldpStatus");
  // No contrato enriquecido o backend devolve em `disciplina` a disciplina canônica (Doc3 + Norma + catálogo). Sem enriquecimento,
  // `disciplina` é o texto digitado e NÃO é exibido como oficial.
  const enriquecido = "doc3" in o || "documentStatus" in o;
  return {
    documentStatus,
    doc3: txtDe(o, "doc3", "Doc3", "disciplineCode", "siglaDisciplina"),
    disciplinaCanonica: txtDe(o, "canonicalDiscipline", "disciplinaCanonica", "disciplineStandardName", "disciplinaPadrao") ?? (enriquecido ? txtDe(o, "disciplina") : null),
    statusPercent: numDe(o, "statusPercentual", "statusPercent", "percentualStatus", "configuredPercent", "statusWeight"),
    statusConfigurado: boolDe(o, "statusConfigured", "statusWeightConfigured", "pesoConfigurado", "isStatusConfigured"),
    cancelado: boolDe(o, "isCancelled", "cancelled", "cancelado") === true || (documentStatus ?? "").toUpperCase().startsWith("CANCELAD"),
    hhBase: numDe(o, "hhOriginal", "hhBaseOriginal", "baseHours", "originalHours", "hhPrevistaOriginal"),
    hhEfetiva: numDe(o, "hhEfetiva", "effectiveHours", "hhFinal"),
    hhTimesheet: numDe(o, "timesheetHours", "hhTimesheet"),
    hhContabilizada: numDe(o, "countedHours", "hhContabilizada", "recognizedHours", "hhReconhecida"),
    hhExcedente: numDe(o, "excessHours", "hhExcedente"),
    custoBase: numDe(o, "custoOriginal", "custoBaseOriginal", "baseCost", "originalCost"),
    custoEfetivo: numDe(o, "custoEfetivo", "effectiveCost", "custoFinal"),
    custoLiberado: numDe(o, "releasedCost", "custoLiberado", "statusReleasedCost", "custoLiberadoStatus"),
    valorHora: numDe(o, "valorHora", "hourlyRate", "rate"),
    hhRedistribuida: numDe(o, "hhRedistribuida", "redistributedHours"), hhAdicional: numDe(o, "hhAdicional", "addendumHours"),
    custoRedistribuido: numDe(o, "custoRedistribuido", "redistributedCost"), custoAdicional: numDe(o, "custoAdicional", "addendumCost"),
    matchAmbiguo: boolDe(o, "ambiguousMatch", "ambiguous", "matchAmbiguo") === true,
    diagnosticos: listaDe(o, "diagnostico", "diagnostics", "diagnosticos", "warnings", "avisos"),
  };
}

/** Junta as fontes na ordem de prioridade: primeiro valor informado vence; flags somam; diagnósticos sem repetição. */
export function mesclarExtras(...fontes: ExtrasDocumento[]): ExtrasDocumento {
  const r = lerExtrasDocumento({});
  for (const f of fontes) {
    for (const k of Object.keys(r) as (keyof ExtrasDocumento)[]) {
      if (k === "cancelado" || k === "matchAmbiguo") r[k] = r[k] || f[k];
      else if (k === "diagnosticos") r.diagnosticos = [...new Set([...r.diagnosticos, ...f.diagnosticos])];
      else if (r[k] === null && f[k] !== null) (r as unknown as Bruto)[k] = f[k];
    }
  }
  return r;
}

/** Sem peso: backend marcou explicitamente, ou há status e nenhum percentual configurado. */
export const semPesoConfigurado = (e: ExtrasDocumento) => e.statusConfigurado === false || (e.statusConfigurado === null && e.documentStatus !== null && e.statusPercent === null);

export function lerExtrasImportacao(fonte: unknown): ExtrasImportacao {
  const o = comoBruto(fonte);
  return {
    empresa: txtDe(o, "company", "empresa"),
    alias: txtDe(o, "alias", "aliasTimesheet"),
    hhPrevista: numDe(o, "hhPrevistaDocumento", "plannedHours", "hhPrevista", "hhPrevisto", "documentHours"),
    valorHora: numDe(o, "hourlyRate", "valorHora", "rate"),
    custoDocumento: numDe(o, "custoPrevistoDocumento", "calculatedCost", "documentCost", "custoDocumento"),
    aviso: txtDe(o, "warning", "aviso"),
  };
}

/** Categorias de ProfessionalCostItem. Impostos e Softwares têm tela própria; as demais compõem o custo no Profissional. */
export const CATEGORIA = { imposto: "IMPOSTO", software: "SOFTWARE", salario: "SALARIO", beneficio: "BENEFICIO", outro: "OUTRO" } as const;
export const ROTULO_CATEGORIA: Record<string, string> = { IMPOSTO: "Imposto / encargo", SOFTWARE: "Software", SALARIO: "Salário", BENEFICIO: "Benefício", OUTRO: "Outro" };
export const CATEGORIAS_COMPOSICAO = [CATEGORIA.salario, CATEGORIA.beneficio, CATEGORIA.outro] as const;

export type Entidade = "professional" | "item" | "package" | "document" | "addendum";

export class CustosError extends Error { constructor(message: string, public status: number) { super(message); } }

type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;
let fetchImpl: FetchLike = (i, init) => fetch(i, init);
/** Só para testes. */
export const __definirFetch = (f: FetchLike) => { fetchImpl = f; };

const qs = (projectId: string) => `projectId=${encodeURIComponent(projectId)}`;

async function chamar<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetchImpl(url, { credentials: "include", ...init, headers: init?.body && !(init.body instanceof FormData) ? { "Content-Type": "application/json", ...(init.headers ?? {}) } : init?.headers });
  if (!r.ok) {
    const corpo = (await r.json().catch(() => ({}))) as { message?: string };
    throw new CustosError(corpo.message || (r.status === 403 ? "Você não tem permissão para Custos do Projeto." : "Não foi possível concluir a operação."), r.status);
  }
  const texto = await r.text();
  return (texto ? JSON.parse(texto) : undefined) as T;
}
const enviar = <T,>(url: string, method: "POST" | "PUT", body: unknown) => chamar<T>(url, { method, body: JSON.stringify(body) });


const CACHE_LDP_MS = 120_000;
const cacheDisciplinas = new Map<string, { data?: CatalogoLdp; until?: number; loading?: Promise<CatalogoLdp> }>();
/** Consulta única por projeto, pré-carregada na página e reaproveitada ao reabrir o cadastro. */
export function carregarCatalogoLdp(projectId: string): Promise<CatalogoLdp> {
  const cached = cacheDisciplinas.get(projectId);
  if (cached?.data && (cached.until ?? 0) > Date.now()) return Promise.resolve(cached.data);
  if (cached?.loading) return cached.loading;
  const loading = chamar<CatalogoLdp>(`${BASE}/packages/ldp-disciplines?${qs(projectId)}`);
  cacheDisciplinas.set(projectId, { loading });
  loading.then((data) => {
    cacheDisciplinas.set(projectId, { data, until: Date.now() + CACHE_LDP_MS });
    if (cacheDisciplinas.size > 8) cacheDisciplinas.delete(cacheDisciplinas.keys().next().value!);
  }).catch(() => {
    if (cacheDisciplinas.get(projectId)?.loading === loading) cacheDisciplinas.delete(projectId);
  });
  return loading;
}
export function preCarregarCatalogoLdp(projectId: string): void {
  void carregarCatalogoLdp(projectId).catch(() => { /* erro exibido ao abrir o formulário */ });
}
export const custosApi = {
  resumo: (p: string) => chamar<Resumo>(`${BASE}/summary?${qs(p)}`),

  profissionais: (p: string) => chamar<Profissional[]>(`${BASE}/professionals?${qs(p)}`),
  criarProfissional: (p: string, x: { nome: string; empresa: string | null; aliasTimesheet: string | null; hhBaseMensal: number; funcao?: string | null; disciplina?: string | null }) =>
    enviar<{ id: number }>(`${BASE}/professionals?${qs(p)}`, "POST", x),
  atualizarProfissional: (p: string, id: number, x: { nome: string; empresa: string | null; aliasTimesheet: string | null; hhBaseMensal: number; ativo: boolean; funcao?: string | null; disciplina?: string | null }) =>
    enviar<boolean>(`${BASE}/professionals/${id}?${qs(p)}`, "PUT", x),

  itens: (p: string, profissionalId: number) => chamar<ItemCusto[]>(`${BASE}/professionals/${profissionalId}/items?${qs(p)}`),
  criarItem: (p: string, profissionalId: number, x: { categoria: string; descricao: string; valor: number }) =>
    enviar<{ id: number }>(`${BASE}/professionals/${profissionalId}/items?${qs(p)}`, "POST", x),
  atualizarItem: (p: string, profissionalId: number, itemId: number, x: { descricao: string; valor: number; ativo: boolean }) =>
    enviar<boolean>(`${BASE}/professionals/${profissionalId}/items/${itemId}?${qs(p)}`, "PUT", x),

  pacotes: (p: string) => chamar<Pacote[]>(`${BASE}/packages?${qs(p)}`),
  criarPacote: (p: string, x: { empresa: string | null; nomeReferencia: string; aliasTimesheet: string | null; descricao: string | null; hhBase: number; custoBase: number }) =>
    enviar<{ id: number }>(`${BASE}/packages?${qs(p)}`, "POST", x),
  /** HH/custo base não entram: o contrato só muda por aditivo. */
  atualizarPacote: (p: string, id: number, x: { empresa: string | null; nomeReferencia: string; aliasTimesheet: string | null; descricao: string | null; ativo: boolean }) =>
    enviar<boolean>(`${BASE}/packages/${id}?${qs(p)}`, "PUT", x),
  documentos: (p: string, pacoteId: number) => chamar<DocumentoPacote[]>(`${BASE}/packages/${pacoteId}/documents?${qs(p)}`),
  adicionarDocumentos: (p: string, pacoteId: number, docs: DocumentoInput[]) =>
    enviar<{ id: number }>(`${BASE}/packages/${pacoteId}/documents/batch?${qs(p)}`, "POST", docs),
  atualizarDocumento: (p: string, pacoteId: number, documentoId: number, doc: DocumentoInput) =>
    enviar<boolean>(`${BASE}/packages/${pacoteId}/documents/${documentoId}?${qs(p)}`, "PUT", doc),
  aditivos: (p: string, pacoteId: number) => chamar<Aditivo[]>(`${BASE}/packages/${pacoteId}/addenda?${qs(p)}`),
  adicionarAditivo: (p: string, pacoteId: number, x: { data: string; descricao: string; hhAdicional: number; custoAdicional: number }) =>
    enviar<{ id: number }>(`${BASE}/packages/${pacoteId}/addenda?${qs(p)}`, "POST", x),
  rateio: (p: string, pacoteId: number) => chamar<Rateio>(`${BASE}/packages/${pacoteId}/allocation?${qs(p)}`),
  reconhecimento: (p: string) => chamar<Reconhecimento[]>(`${BASE}/recognition?${qs(p)}`),

  desativar: (p: string, entidade: Entidade, id: number) => chamar<boolean>(`${BASE}/${entidade}/${id}/deactivate?${qs(p)}`, { method: "POST" }),

  baixarModeloPacotes: async (p: string) => {
    const r = await fetchImpl(`${BASE}/import/template?${qs(p)}`, { credentials: "include" });
    if (!r.ok) {
      const erro = await r.json().catch(() => ({})) as { message?: string };
      throw new CustosError(erro.message || "Não foi possível baixar o modelo de pacotes.", r.status);
    }
    return r.blob();
  },
  suporteModelo: (p: string) => chamar<SuporteModelo>(`${BASE}/packages/model-support?${qs(p)}`),
  disciplinasLdp: carregarCatalogoLdp,
  simularPacoteDisciplina: (p: string, x: PacoteDisciplinaInput) => enviar<PreviaPacoteDisciplina>(`${BASE}/packages/discipline/preview?${qs(p)}`, "POST", x),
  criarPacoteDisciplina: (p: string, x: PacoteDisciplinaInput) => enviar<{ id: number }>(`${BASE}/packages/discipline?${qs(p)}`, "POST", x),
  atualizarPacoteDisciplina: (p: string, id: number, x: PacoteDisciplinaInput) => enviar<boolean>(`${BASE}/packages/${id}/discipline?${qs(p)}`, "PUT", x),
  detalhePacoteDisciplina: (p: string, id: number) => chamar<DetalhePacoteDisciplina>(`${BASE}/packages/${id}/disciplines?${qs(p)}`),
  baixarModeloDisciplina: async (p: string) => {
    const r = await fetchImpl(`${BASE}/import/discipline/template?${qs(p)}`, { credentials: "include" });
    if (!r.ok) { const erro = await r.json().catch(() => ({})) as { message?: string }; throw new CustosError(erro.message || "Não foi possível baixar o modelo Por Disciplina.", r.status); }
    return r.blob();
  },
  previaImportacaoDisciplina: (p: string, arquivo: File) => { const f = new FormData(); f.append("file", arquivo); return chamar<PreviaImportacaoDisciplina>(`${BASE}/import/discipline/preview?${qs(p)}`, { method: "POST", body: f }); },
  confirmarImportacaoDisciplina: (p: string, arquivo: File) => { const f = new FormData(); f.append("file", arquivo); return chamar<ResultadoImportacao>(`${BASE}/import/discipline/commit?${qs(p)}`, { method: "POST", body: f }); },
  previaImportacao: (p: string, arquivo: File) => { const f = new FormData(); f.append("file", arquivo); return chamar<PreviaImportacao>(`${BASE}/import/preview?${qs(p)}`, { method: "POST", body: f }); },
  confirmarImportacao: (p: string, arquivo: File) => { const f = new FormData(); f.append("file", arquivo); return chamar<ResultadoImportacao>(`${BASE}/import/commit?${qs(p)}`, { method: "POST", body: f }); },
  vigencias: (p:string,id:number)=>chamar<PeriodoProfissional[]>(`${BASE}/history/professionals/${id}/periods?${qs(p)}`),
  previaVigencia: (p:string,id:number,start:string,end:string|null)=>enviar<PreviaVigencia>(`${BASE}/history/professionals/${id}/periods/preview?${qs(p)}`,"POST",{start,end}),
  criarVigencia: (p:string,id:number,x:VigenciaProfissionalInput)=>enviar<{id:number}>(`${BASE}/history/professionals/${id}/periods?${qs(p)}`,"POST",x),
  atualizarVigencia: (p:string,id:number,periodId:number,x:VigenciaProfissionalInput)=>enviar<boolean>(`${BASE}/history/professionals/${id}/periods/${periodId}?${qs(p)}`,"PUT",x),
  desativarVigencia: (p:string,id:number,periodId:number)=>chamar<boolean>(`${BASE}/history/professionals/${id}/periods/${periodId}/deactivate?${qs(p)}`,{method:"POST"}),
  baixarModeloProfissionais: async (p:string) => {
    const response=await fetchImpl(`${BASE}/history/professionals/import/template?${qs(p)}`,{credentials:"include"});
    if(!response.ok){const body=await response.json().catch(()=>({})) as {message?:string};throw new CustosError(body.message??"Não foi possível baixar o modelo.",response.status);}
    return response.blob();
  },
  previaImportacaoProfissionais: (p:string,arquivo:File)=>{const f=new FormData();f.append("file",arquivo);return chamar<PreviaImportacaoProfissionais>(`${BASE}/history/professionals/import/preview?${qs(p)}`,{method:"POST",body:f});},
  confirmarImportacaoProfissionais: (p:string,arquivo:File)=>{const f=new FormData();f.append("file",arquivo);return chamar<ResultadoImportacaoProfissionais>(`${BASE}/history/professionals/import/commit?${qs(p)}`,{method:"POST",body:f});},
  projetosElegiveisCustos: ()=>chamar<ProjetoElegivelCusto[]>(`${BASE}/history/eligible-projects`),
  previaAtribuicaoProfissionais: (p:string,x:AtribuicaoProfissionaisInput)=>enviar<PreviaAtribuicaoProfissionais>(`${BASE}/history/professionals/assign/preview?${qs(p)}`,"POST",x),
  confirmarAtribuicaoProfissionais: (p:string,x:AtribuicaoProfissionaisInput)=>enviar<ResultadoAtribuicaoProfissionais>(`${BASE}/history/professionals/assign/commit?${qs(p)}`,"POST",x),
  impostosProjeto: (p:string)=>chamar<ProjectTax[]>(`${BASE}/history/taxes?${qs(p)}`),
  desativarImpostoProjeto: (p:string,id:number)=>chamar<boolean>(`${BASE}/history/taxes/${id}/deactivate?${qs(p)}`,{method:"POST"}),
  criarImpostoProjeto: (p:string,x:unknown)=>enviar<{id:number}>(`${BASE}/history/taxes?${qs(p)}`,"POST",x),
  previaVigenciaImpostoProjeto: (p:string,id:number,start:string)=>chamar<{openPeriodId:number|null;closeOpenPeriodOn:string|null;confirmationRequired:boolean}>(`${BASE}/history/taxes/${id}/periods/preview?${qs(p)}`,{method:"POST",body:JSON.stringify({start})}),
  novaVigenciaImpostoProjeto: (p:string,id:number,x:unknown)=>enviar<{id:number}>(`${BASE}/history/taxes/${id}/periods?${qs(p)}`,"POST",x),
  custoRealDocumentos: (p:string)=>chamar<DocumentActual[]>(`${BASE}/history/documents/actuals?${qs(p)}`),
  previaCopia: (x:CopyInput)=>chamar<CopyPreview>(`${BASE}/history/copy/preview`,{method:"POST",body:JSON.stringify(x)}),
  confirmarCopia: (x:CopyInput)=>enviar<unknown>(`${BASE}/history/copy/commit`,"POST",x),
  previaCopiaLote: (x:CopyBatchInput)=>chamar<{destinations:CopyPreview[]}>(`${BASE}/history/copy/batch/preview`,{method:"POST",body:JSON.stringify(x)}),
  confirmarCopiaLote: (x:CopyBatchInput)=>enviar<unknown>(`${BASE}/history/copy/batch/commit`,"POST",x),
};

// ------------------------------------------------------------------------------------------------ formatação ----
const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const num2 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
export const dinheiro = (v: number | null | undefined) => brl.format(v ?? 0);
export const horasFmt = (v: number | null | undefined) => num2.format(v ?? 0);
export const dataFmt = (iso: string | null | undefined) => (iso ? new Date(iso.length === 10 ? `${iso}T00:00:00` : iso).toLocaleDateString("pt-BR") : "—");

/** "0,00" / "1.234,5" / "1234.5" → número (aceita vírgula decimal pt-BR). Inválido ou vazio → null. */
export function lerNumero(texto: string): number | null {
  const t = texto.trim();
  if (!t) return null;
  const normal = t.includes(",") ? t.replace(/\./g, "").replace(",", ".") : t;
  const n = Number(normal);
  return Number.isFinite(n) ? n : null;
}

/** Valor monetário digitado em pt-BR → number. Aceita "R$ 5.000,00", "5.000" (milhar) e "5000.5". Inválido ou vazio → null. */
export function lerValor(texto: string): number | null {
  const t = texto.replace(/R\$/gi, "").replace(/\s/g, "");
  if (!t) return null;
  if (!t.includes(",") && /^-?\d{1,3}(\.\d{3})+$/.test(t)) return lerNumero(t.replace(/\./g, ""));
  return lerNumero(t);
}

/** Software guarda "Nome — licença" em Descricao (sem tabela nova). */
export const SEPARADOR_SOFTWARE = " — ";
export const juntarSoftware = (nome: string, licenca: string) => (licenca.trim() ? `${nome.trim()}${SEPARADOR_SOFTWARE}${licenca.trim()}` : nome.trim());
export function separarSoftware(descricao: string): { nome: string; licenca: string } {
  const i = descricao.indexOf(SEPARADOR_SOFTWARE);
  return i < 0 ? { nome: descricao, licenca: "" } : { nome: descricao.slice(0, i), licenca: descricao.slice(i + SEPARADOR_SOFTWARE.length) };
}

/** Linhas coladas ("NÚMERO;HH PREVISTO;VALOR/HORA" ou colunas do Excel por tab) → documentos + erros por linha. */
export function lerDocumentosColados(texto: string): { documentos: DocumentoInput[]; erros: string[] } {
  const documentos: DocumentoInput[] = []; const erros: string[] = [];
  texto.split(/\r?\n/).forEach((bruta, i) => {
    if (!bruta.trim()) return;
    const [codigo = "", hh = "", valorHora = ""] = bruta.split(/\t|;/).map((c) => c.trim());
    const r = montarDocumento({ codigo, hh, valorHora });
    if ("erro" in r) erros.push(`Linha ${i + 1}: ${r.erro}`); else documentos.push(r.doc);
  });
  return { documentos, erros };
}
