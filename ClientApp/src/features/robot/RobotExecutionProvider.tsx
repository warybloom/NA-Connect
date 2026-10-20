// RobotExecutionProvider: ÚNICO ponto que consulta GET /api/worker/status na aplicação. Montado no shell autenticado
// (App.tsx, acima da Home e do Layout), então navegar entre Home, Planejamento e Produção não desmonta o acompanhamento.
// Botão INICIAR ROBÔ, monitor global e Configuração do Robô leem/atualizam este estado — nenhum faz polling próprio.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import toast from "react-hot-toast";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { PERMISSOES_ACOMPANHAR_ROBO, chaveExecucao, criarPolling, type StatusRobo } from "./robotExecution";

interface RobotExecutionValue {
  /** Último estado real devolvido pelo backend (undefined = ainda não consultado / sem acesso). */
  status: StatusRobo | undefined;
  /** Falha ao consultar o status (rede/HTTP). */
  erro: string | null;
  /** Usuário pode acompanhar (robot.view). */
  habilitado: boolean;
  /** Adota um status vindo de outra resposta (202 do POST, ou o status ativo de um 409) e consulta de novo em seguida. */
  aceitar: (s: StatusRobo | undefined) => void;
  atualizar: () => void;
  expandido: boolean;
  setExpandido: (v: boolean) => void;
  chaveFechada: string | null;
  fechar: () => void;
}

const RobotExecutionContext = createContext<RobotExecutionValue | null>(null);
const CHAVE_FECHADA = "na.robot.monitor.fechado";
const lerFechada = () => { try { return window.sessionStorage.getItem(CHAVE_FECHADA); } catch { return null; } };

export function RobotExecutionProvider({ children }: { children: ReactNode }) {
  const { can, loading } = usePermissions();
  const habilitado = !loading && can(PERMISSOES_ACOMPANHAR_ROBO);
  const [status, setStatus] = useState<StatusRobo>();
  const [erro, setErro] = useState<string | null>(null);
  const [expandido, setExpandido] = useState(false);
  const [chaveFechada, setChaveFechada] = useState<string | null>(lerFechada);
  const anterior = useRef<string>();

  const consultar = useCallback(async (): Promise<StatusRobo | undefined> => {
    try {
      const r = await fetch("/api/worker/status", { credentials: "include" });
      if (!r.ok) { setErro(`Status do robô indisponível (HTTP ${r.status}).`); return undefined; }
      const s = (await r.json()) as StatusRobo;
      setStatus(s); setErro(null);
      return s;
    } catch {
      setErro("Status do robô indisponível (sem conexão com o backend).");
      return undefined;
    }
  }, []);

  const polling = useRef<ReturnType<typeof criarPolling>>();
  if (!polling.current) polling.current = criarPolling(consultar, { set: (fn, ms) => window.setTimeout(fn, ms), clear: (id) => window.clearTimeout(id as number) });

  useEffect(() => {
    const p = polling.current!;
    if (!habilitado) return;
    p.iniciar();
    return () => p.parar();
  }, [habilitado]);

  // Toast único na transição RUNNING → COMPLETED/ERROR.
  useEffect(() => {
    if (!status) return;
    const chave = `${status.state}|${status.finishedAt}`;
    if (anterior.current?.startsWith("RUNNING") && anterior.current !== chave) {
      if (status.state === "COMPLETED") toast.success(`Robô concluído: ${status.message}`, { duration: 8000 });
      if (status.state === "ERROR") toast.error(`Robô com erro: ${status.message}`, { duration: 10000 });
    }
    anterior.current = chave;
  }, [status]);

  const aceitar = useCallback((s: StatusRobo | undefined) => {
    if (s) { setStatus(s); setErro(null); }
    polling.current?.agora();
  }, []);
  const atualizar = useCallback(() => polling.current?.agora(), []);
  const fechar = useCallback(() => {
    const chave = chaveExecucao(status);
    setChaveFechada(chave); setExpandido(false);
    try { window.sessionStorage.setItem(CHAVE_FECHADA, chave); } catch { /* sem storage: fecha só nesta aba */ }
  }, [status]);

  const value = useMemo<RobotExecutionValue>(() => ({ status, erro, habilitado, aceitar, atualizar, expandido, setExpandido, chaveFechada, fechar }),
    [status, erro, habilitado, aceitar, atualizar, expandido, chaveFechada, fechar]);
  return <RobotExecutionContext.Provider value={value}>{children}</RobotExecutionContext.Provider>;
}

export function useRobotExecution(): RobotExecutionValue {
  const ctx = useContext(RobotExecutionContext);
  if (!ctx) throw new Error("useRobotExecution exige RobotExecutionProvider (App.tsx).");
  return ctx;
}
