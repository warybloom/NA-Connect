// PermissionProvider: busca UMA vez as permissões efetivas (já resolvidas pelo backend) e expõe can(key).
// Navegação e rotas usam o escopo "any" (união global + qualquer projeto); telas de projeto (Controle de
// Produção, CDE) usam useProjectPermissions(projectId), que resolve no projeto selecionado.
//
// SAFE MODE: carregando ou erro (rede, API, resposta inválida) NUNCA vira "Acesso negado" — can() libera e o
// backend continua sendo a barreira real. Só há bloqueio com resposta válida enforced=true de usuário não-master.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { criarCan, ehEffectivePermissions, type Can, type EffectivePermissions } from "./permissionLogic";

interface PermissionContextValue {
  loading: boolean;
  error: string | null;
  /** null = desconhecido (carregando ou erro) → nada é bloqueado no frontend. */
  effective: EffectivePermissions | null;
  /** Escopo global + qualquer projeto (menu, rotas). */
  can: Can;
  reload: () => void;
}

export const PermissionContext = createContext<PermissionContextValue | null>(null);

export async function buscarEfetivas(projectId?: string, signal?: AbortSignal): Promise<EffectivePermissions> {
  const qs = projectId ? `?projectId=${encodeURIComponent(projectId)}` : "";
  const r = await fetch(`/api/permissions/me${qs}`, { credentials: "include", signal });
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  const corpo: unknown = await r.json().catch(() => null);
  if (!ehEffectivePermissions(corpo)) throw new Error("Resposta inválida de /api/permissions/me.");
  return corpo;
}

export function PermissionProvider({ children }: { children: ReactNode }) {
  const [effective, setEffective] = useState<EffectivePermissions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [versao, setVersao] = useState(0);

  useEffect(() => {
    const ctrl = new AbortController();
    // Primeira verificação: aguardamos o servidor. Depois, atualizamos em segundo plano
    // SEM remover o <Outlet /> nem desmontar formulários quando a janela recebe foco
    // (inclusive quando o seletor de arquivos do Windows é fechado).
    const inicial = versao === 0;
    if (inicial) setLoading(true);
    buscarEfetivas(undefined, ctrl.signal)
      .then((dados) => {
        if (ctrl.signal.aborted) return;
        // Sem alteração de identidade/permissões, conservar a referência e evitar
        // invalidar guardas de rota e os componentes das páginas abertas.
        setEffective((anterior) => anterior && JSON.stringify(anterior) === JSON.stringify(dados) ? anterior : dados);
        setError(null);
      })
      .catch((e: unknown) => {
        if (ctrl.signal.aborted) return;
        const mensagem = e instanceof Error ? e.message : "Falha ao carregar permissões.";
        if (mensagem === "HTTP 401" || mensagem === "HTTP 403") {
          // Sessão realmente revogada: não preservar acesso na interface.
          setEffective(null);
          setError("Sessão expirada ou sem autorização. Entre novamente.");
        } else if (inicial) {
          setEffective(null);
          setError(mensagem);
        } else {
          // Falha transitória de rede: não destruir o formulário do usuário.
          // As APIs de gravação continuam validando autenticação e permissões.
          console.warn("[Permissões] Revalidação em segundo plano falhou; formulário preservado.", e);
        }
      })
      .finally(() => { if (!ctrl.signal.aborted && inicial) setLoading(false); });
    return () => ctrl.abort();
  }, [versao]);

  const reload = useCallback(() => setVersao((v) => v + 1), []);

  const ultimaVerificacao = useRef(0);
  useEffect(() => {
    // Foco + visibilitychange ocorrem juntos ao voltar do Excel ou do diálogo
    // de arquivo. Limitar a revalidação evita duas requisições simultâneas.
    // A atualização é invisível e não desmonta o formulário.
    const atualizar = () => {
      const agora = Date.now();
      if (agora - ultimaVerificacao.current < 15000) return;
      ultimaVerificacao.current = agora;
      reload();
    };
    const quandoVisivel = () => { if (document.visibilityState === "visible") atualizar(); };
    window.addEventListener("focus", atualizar);
    document.addEventListener("visibilitychange", quandoVisivel);
    return () => {
      window.removeEventListener("focus", atualizar);
      document.removeEventListener("visibilitychange", quandoVisivel);
    };
  }, [reload]);
  const value = useMemo<PermissionContextValue>(
    () => ({ loading, error, effective, can: criarCan(effective, "any"), reload }),
    [loading, error, effective, reload],
  );
  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
}

export function usePermissions(): PermissionContextValue {
  const ctx = useContext(PermissionContext);
  if (!ctx) throw new Error("usePermissions fora de PermissionProvider.");
  return ctx;
}

/**
 * Permissões resolvidas NO PROJETO (overrides/perfis do projeto + globais).
 * Só consulta o projeto quando o sistema está realmente aplicando permissões (enforced=true e não-master);
 * nos demais casos libera sem esperar. loading=true enquanto a resposta do projeto atual não chegou.
 */
export function useProjectPermissions(projectId: string | null | undefined): { loading: boolean; can: Can } {
  const { effective: global, loading: carregandoGlobal } = usePermissions();
  const precisaConsultar = !!projectId && !!global && global.enforced && !global.isMasterAdmin;
  const [resposta, setResposta] = useState<{
    projectId: string; subject: EffectivePermissions; eff: EffectivePermissions | null
  } | null>(null);

  useEffect(() => {
    setResposta(null);
    if (!precisaConsultar || !projectId || !global) return;
    const ctrl = new AbortController();
    buscarEfetivas(projectId, ctrl.signal)
      .then((eff) => {
        if (ctrl.signal.aborted) return;
        // Não reutilizar permissões de usuário diferente, projeto diferente ou
        // resposta obsoleta que chegou após uma troca rápida de contexto.
        if (eff.email?.toLowerCase() !== global.email?.toLowerCase()
          || eff.projectIdAcc?.toLowerCase() !== projectId.toLowerCase()) {
          setResposta({ projectId, subject: global, eff: null });
          return;
        }
        setResposta({ projectId, subject: global, eff });
      })
      .catch(() => {
        if (!ctrl.signal.aborted) setResposta({ projectId, subject: global, eff: null });
      });
    return () => ctrl.abort();
  }, [projectId, precisaConsultar, global]);

  const respostaAtual = resposta && resposta.projectId === projectId && resposta.subject === global ? resposta : null;
  const loading = carregandoGlobal || (precisaConsultar && respostaAtual === null);
  const can = useMemo<Can>(
    () => (precisaConsultar ? criarCan(respostaAtual?.eff ?? null, "project") : criarCan(global, "project")),
    [precisaConsultar, respostaAtual, global],
  );
  return { loading, can };
}
