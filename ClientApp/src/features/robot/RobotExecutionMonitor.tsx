// Monitor GLOBAL do robô (canto inferior direito). Montado UMA vez no shell autenticado (App.tsx) — aparece na Home e nas áreas
// internas. Tudo vem do backend (RobotRunGate): projeto X/Y, etapa X/Y, percentual por etapas reais, histórico recente, erro.
import { useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import { AlertTriangle, Bot, CheckCircle2, ChevronDown, ChevronUp, Loader2, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { useRobotExecution } from "./RobotExecutionProvider";
import {
  deveExibirMonitor, erroRobo, etapaAtual, etapasConcluidas, percentualRobo, proximaEtapa, rotuloEtapa, rotuloMinimizado, tempoDecorrido,
} from "./robotExecution";

/** Telas em que o monitor não sobrepõe o conteúdo (player de TV em tela cheia). */
export const ROTAS_SEM_MONITOR_ROBO: readonly string[] = ["/gestao-a-vista"];
const hora = (iso?: string | null) => (iso ? new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }) : "—");
const horaSeg = (iso?: string | null) => (iso ? new Date(iso).toLocaleTimeString("pt-BR") : "—");

export function RobotExecutionMonitor() {
  const { status: s, expandido, setExpandido, chaveFechada, fechar, habilitado } = useRobotExecution();
  const { pathname } = useLocation();
  const [agora, setAgora] = useState(() => Date.now());
  const rodando = s?.state === "RUNNING";
  // Relógio só para o "tempo decorrido" exibido; o percentual NÃO depende dele.
  useEffect(() => {
    if (!rodando) { setAgora(Date.now()); return; }
    const t = window.setInterval(() => setAgora(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, [rodando]);

  if (!habilitado || !s || ROTAS_SEM_MONITOR_ROBO.includes(pathname) || !deveExibirMonitor(s, chaveFechada, agora)) return null;
  const pct = percentualRobo(s);
  const erro = erroRobo(s);
  const atual = etapaAtual(s);
  const proxima = proximaEtapa(s);
  const concluidas = etapasConcluidas(s);
  const cor = s.state === "ERROR" ? "border-rose-500/60" : s.state === "COMPLETED" ? "border-emerald-500/60" : "border-primary/60";

  if (!expandido) {
    return (
      <div className="fixed bottom-4 right-4 z-[60] flex max-w-[calc(100vw-2rem)] items-center gap-1" data-testid="robot-monitor" data-state={s.state}>
        <button type="button" onClick={() => setExpandido(true)} aria-label="Expandir monitor do robô"
          className={cn("flex min-w-0 items-center gap-2 rounded-full border bg-card/95 px-3 py-2 text-xs font-bold shadow-lg backdrop-blur", cor)}>
          {rodando ? <Loader2 className="h-4 w-4 shrink-0 animate-spin text-primary" /> : s.state === "ERROR" ? <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" /> : <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />}
          <Bot className="h-4 w-4 shrink-0" />
          <span className="truncate">{rotuloMinimizado(s)}</span>
          <ChevronUp className="h-3.5 w-3.5 shrink-0 opacity-60" />
        </button>
        {!rodando && <button type="button" onClick={fechar} aria-label="Fechar monitor do robô" className="rounded-full border border-border bg-card/95 p-2 shadow-lg"><X className="h-3.5 w-3.5" /></button>}
      </div>
    );
  }

  return (
    <section className={cn("fixed bottom-4 right-4 z-[60] w-[380px] max-w-[calc(100vw-2rem)] rounded-xl border bg-card/95 p-4 text-xs shadow-2xl backdrop-blur", cor)}
      data-testid="robot-monitor" data-state={s.state} aria-label="Monitor de execução do robô">
      <header className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2 font-black uppercase tracking-widest">
          {rodando ? <Loader2 className="h-4 w-4 animate-spin text-primary" /> : s.state === "ERROR" ? <AlertTriangle className="h-4 w-4 text-rose-400" /> : <CheckCircle2 className="h-4 w-4 text-emerald-400" />}
          {rodando ? "Robô em execução" : s.state === "COMPLETED" ? "Concluído" : "Erro"}
        </div>
        <div className="flex items-center gap-2">
          {pct !== null && <span className="text-lg font-black tabular-nums">{pct}%</span>}
          <button type="button" onClick={() => setExpandido(false)} aria-label="Minimizar monitor do robô" className="rounded p-1 hover:bg-accent"><ChevronDown className="h-4 w-4" /></button>
          {!rodando && <button type="button" onClick={fechar} aria-label="Fechar monitor do robô" className="rounded p-1 hover:bg-accent"><X className="h-4 w-4" /></button>}
        </div>
      </header>

      <p className="mt-2 truncate text-sm font-bold">{s.projectName ?? s.projectId ?? (s.manual ? "Execução manual" : "Ciclo automático")}</p>
      {pct !== null ? (
        <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct}>
          <div className={cn("h-full transition-all", s.state === "ERROR" ? "bg-rose-500" : s.state === "COMPLETED" ? "bg-emerald-500" : "bg-primary")} style={{ width: `${pct}%` }} />
        </div>
      ) : <p className="mt-2 text-muted-foreground">Progresso por etapas indisponível para este ciclo (sem plano de etapas).</p>}

      <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-1">
        {(s.queueTotal ?? 0) > 0 && <><dt className="text-muted-foreground">Projeto</dt><dd className="font-semibold" data-testid="robot-projeto-xy">{s.queuePosition ?? 0} de {s.queueTotal}</dd></>}
        {(s.stageTotal ?? 0) > 0 && <><dt className="text-muted-foreground">Etapa</dt><dd className="font-semibold" data-testid="robot-etapa-xy">{s.stagePosition ?? 0} de {s.stageTotal}</dd></>}
        {(s.overallTotalStages ?? 0) > 0 && <><dt className="text-muted-foreground">Etapas processadas</dt><dd className="font-semibold">{s.overallCompletedStages ?? 0} de {s.overallTotalStages}</dd></>}
        <dt className="text-muted-foreground">Iniciado</dt><dd>{hora(s.startedAt)}</dd>
        <dt className="text-muted-foreground">Tempo decorrido</dt><dd className="tabular-nums">{tempoDecorrido(s.startedAt, s.finishedAt, agora)}</dd>
        {s.nextProject && rodando && <><dt className="text-muted-foreground">Próximo projeto</dt><dd className="truncate">{s.nextProject}</dd></>}
      </dl>

      {rodando && (atual || s.currentStage) && <div className="mt-3"><p className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Atual</p><p className="font-semibold">{atual ? rotuloEtapa(atual) : s.currentStage}</p></div>}
      {concluidas.length > 0 && (
        <div className="mt-2"><p className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Concluídas</p>
          <ul>{concluidas.map((e) => <li key={e.id} className="flex items-center gap-1"><CheckCircle2 className="h-3 w-3 text-emerald-400" />{rotuloEtapa(e)}</li>)}</ul></div>
      )}
      {rodando && proxima && <div className="mt-2"><p className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Próxima</p><p>{rotuloEtapa(proxima)}</p></div>}

      {erro && (
        <div className="mt-3 rounded-md border border-rose-500/50 bg-rose-500/10 p-2 text-rose-200" role="alert" data-testid="robot-erro">
          <p className="font-bold">{[erro.projeto, erro.etapa].filter(Boolean).join(" · ") || "Erro na execução"}</p>
          <p className="break-words">{erro.mensagem}</p>
        </div>
      )}
      {!rodando && <p className="mt-3 break-words text-muted-foreground">{s.message}</p>}

      {(s.recentEntries?.length ?? 0) > 0 && (
        <details className="mt-3">
          <summary className="cursor-pointer text-[10px] font-black uppercase tracking-widest text-muted-foreground">Histórico recente</summary>
          <ul className="mt-1 max-h-40 space-y-0.5 overflow-y-auto font-mono text-[10px]">
            {[...(s.recentEntries ?? [])].reverse().slice(0, 10).map((e, i) => (
              <li key={`${e.at}-${i}`} className={cn(e.status.toUpperCase().includes("ERRO") && "text-rose-400")}>{horaSeg(e.at)} {e.stage} — {e.status}</li>
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}
