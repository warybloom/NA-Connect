// Acompanhamento do robô — lógica PURA (sem React) do monitor global, do botão INICIAR ROBÔ e do polling central.
// Fonte única: GET /api/worker/status (estado em memória do RobotRunGate). O plano de etapas e o percentual vêm do backend
// (RobotStageCatalog + registros reais do Worker); aqui nada é estimado por tempo.

export type EstadoRobo = "IDLE" | "RUNNING" | "COMPLETED" | "ERROR";
export type EstadoEtapa = "PENDING" | "RUNNING" | "COMPLETED" | "ERROR" | "SKIPPED";

export interface EtapaRobo { id: string; name: string; state: EstadoEtapa; startedAt?: string | null; finishedAt?: string | null; message?: string | null }
export interface ProjetoFilaRobo { projectId: string; name: string; state: string; message?: string | null }
export interface EventoRobo { at: string; project: string; stage: string; status: string; message?: string | null }

export interface StatusRobo {
  state: EstadoRobo; manual: boolean; projectId: string | null; projectName: string | null;
  startedAt: string | null; finishedAt: string | null; message: string;
  robotId?: string | null; queuePosition?: number; queueTotal?: number; currentStage?: string | null; nextProject?: string | null;
  projects?: ProjetoFilaRobo[]; stages?: EtapaRobo[]; currentStageId?: string | null;
  stagePosition?: number; stageTotal?: number;
  overallCompletedStages?: number; overallTotalStages?: number; overallFailedStages?: number; overallSkippedStages?: number;
  progressPercent?: number | null; recentEntries?: EventoRobo[];
}

export const PERMISSAO_EXECUTAR_ROBO = "robot.run";
/** Quem acompanha o robô: robot.view — o mesmo [RequirePermission] do GET /api/worker/status (sem a key o monitor não consulta). */
export const PERMISSOES_ACOMPANHAR_ROBO: readonly string[] = ["robot.view"];
export const POLLING_EM_EXECUCAO_MS = 3000;
/** Ocioso/finalizado: consulta espaçada só para perceber uma execução iniciada em outro lugar (fila automática, outra sessão). */
export const POLLING_OCIOSO_MS = 30000;
/** Depois de finalizar, o monitor continua disponível até ser fechado ou por este tempo. */
export const MONITOR_POS_FIM_MS = 30 * 60 * 1000;

export const intervaloPolling = (s: StatusRobo | undefined): number => (s?.state === "RUNNING" ? POLLING_EM_EXECUCAO_MS : POLLING_OCIOSO_MS);

/** Percentual do backend (etapas processadas / etapas planejadas), limitado a 0–100. null = execução sem plano (sem barra). */
export function percentualRobo(s: StatusRobo | undefined): number | null {
  if (!s || s.progressPercent === null || s.progressPercent === undefined || !Number.isFinite(s.progressPercent)) return null;
  return Math.min(100, Math.max(0, Math.round(s.progressPercent)));
}

/** Identifica a execução (para "fechar" o monitor só desta execução). */
export const chaveExecucao = (s: StatusRobo | undefined): string => (s?.startedAt ? `${s.startedAt}` : "");

export function deveExibirMonitor(s: StatusRobo | undefined, chaveFechada: string | null, agora: number): boolean {
  if (!s || s.state === "IDLE") return false;
  if (s.state === "RUNNING") return true;
  if (chaveFechada && chaveFechada === chaveExecucao(s)) return false;
  const fim = s.finishedAt ? Date.parse(s.finishedAt) : NaN;
  return Number.isNaN(fim) || agora - fim < MONITOR_POS_FIM_MS;
}

export const etapaAtual = (s: StatusRobo | undefined): EtapaRobo | undefined =>
  s?.stages?.find((e) => e.state === "RUNNING") ?? (s?.currentStageId ? s.stages?.find((e) => e.id === s.currentStageId) : undefined);
export const rotuloEtapa = (e: EtapaRobo) => `${e.id} - ${e.name}`;
/** Últimas etapas concluídas do projeto atual (mais recente por último). */
export const etapasConcluidas = (s: StatusRobo | undefined, limite = 4): EtapaRobo[] =>
  (s?.stages ?? []).filter((e) => e.state === "COMPLETED").slice(-limite);
export const proximaEtapa = (s: StatusRobo | undefined): EtapaRobo | undefined => (s?.stages ?? []).find((e) => e.state === "PENDING");

/** "CHS001 • 38% • ETAPA 06 - CUSTO" */
export function rotuloMinimizado(s: StatusRobo): string {
  const partes = [s.projectName ?? s.projectId ?? "Robô"];
  const pct = percentualRobo(s);
  if (pct !== null) partes.push(`${pct}%`);
  if (s.state === "RUNNING") {
    const e = etapaAtual(s);
    if (e) partes.push(`ETAPA ${rotuloEtapa(e)}`);
    else if (s.currentStage) partes.push(s.currentStage);
  } else partes.push(s.state === "COMPLETED" ? "CONCLUÍDO" : "ERRO");
  return partes.join(" • ");
}

export interface ErroRobo { projeto: string | null; etapa: string | null; mensagem: string }
/** Erro a destacar: etapa com ERROR no projeto atual, senão projeto da fila com ERROR, senão a mensagem final do backend. */
export function erroRobo(s: StatusRobo | undefined): ErroRobo | null {
  if (!s) return null;
  const etapa = (s.stages ?? []).find((e) => e.state === "ERROR");
  if (etapa) return { projeto: s.projectName, etapa: rotuloEtapa(etapa), mensagem: etapa.message ?? s.message };
  const projeto = (s.projects ?? []).find((p) => p.state === "ERROR");
  if (projeto) return { projeto: projeto.name, etapa: null, mensagem: projeto.message ?? s.message };
  return s.state === "ERROR" ? { projeto: s.projectName, etapa: s.currentStage ?? null, mensagem: s.message } : null;
}

export function tempoDecorrido(inicio: string | null | undefined, fim: string | null | undefined, agora: number): string {
  const a = inicio ? Date.parse(inicio) : NaN;
  if (Number.isNaN(a)) return "—";
  const b = fim ? Date.parse(fim) : agora;
  const seg = Math.max(0, Math.floor(((Number.isNaN(b) ? agora : b) - a) / 1000));
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(Math.floor(seg / 3600))}:${p(Math.floor((seg % 3600) / 60))}:${p(seg % 60)}`;
}

/** Corpo do POST /api/worker/run: só Project_ID_ACC (o backend resolve o hub no cadastro). */
export const corpoExecucao = (projectIds: readonly string[] | "todos") =>
  projectIds === "todos" ? { allProjects: true } : { projectIds: [...new Set(projectIds)] };

export type RespostaInicio =
  | { tipo: "iniciado"; status: StatusRobo }
  | { tipo: "ocupado"; status?: StatusRobo; mensagem: string }
  | { tipo: "sessao" ; mensagem: string }
  | { tipo: "erro"; mensagem: string };
/** 202 = iniciado; 409 = já existe execução (mostra a ATIVA, sem criar outro monitor); 401 = sessão; demais = erro do backend. */
export function interpretarRespostaInicio(http: number, corpo: unknown): RespostaInicio {
  const b = (corpo ?? {}) as { message?: string; status?: StatusRobo; state?: string };
  if (http === 202 || (http >= 200 && http < 300 && b.state)) return { tipo: "iniciado", status: corpo as StatusRobo };
  if (http === 409) return { tipo: "ocupado", status: b.status, mensagem: b.message ?? "Robô já está em execução." };
  if (http === 401) return { tipo: "sessao", mensagem: "Sessão expirada. Entre novamente." };
  return { tipo: "erro", mensagem: b.message ?? `Falha ao iniciar o robô (HTTP ${http}).` };
}

export interface Temporizador { set: (fn: () => void, ms: number) => unknown; clear: (id: unknown) => void }
/**
 * Polling ÚNICO: no máximo UMA consulta em voo e UM timer agendado, qualquer que seja o número de chamadas a iniciar()/agora().
 * Intervalo pelo estado (3 s em execução, 30 s ocioso). agora() força consulta imediata (ex.: depois de iniciar o robô).
 */
export function criarPolling(consultar: () => Promise<StatusRobo | undefined>, t: Temporizador) {
  let ativo = false, emVoo = false, repetir = false;
  let timer: unknown = null;
  const limpar = () => { if (timer !== null) { t.clear(timer); timer = null; } };
  const ciclo = async () => {
    timer = null;
    if (emVoo) { repetir = true; return; }
    emVoo = true;
    let s: StatusRobo | undefined;
    try { s = await consultar(); } catch { s = undefined; } finally { emVoo = false; }
    if (!ativo) return;
    limpar();
    const ms = repetir ? 0 : intervaloPolling(s);
    repetir = false;
    timer = t.set(() => { void ciclo(); }, ms);
  };
  return {
    iniciar() { if (ativo) return; ativo = true; void ciclo(); },
    agora() { if (!ativo) return; limpar(); void ciclo(); },
    parar() { ativo = false; limpar(); },
    get agendados() { return timer === null ? 0 : 1; },
  };
}
