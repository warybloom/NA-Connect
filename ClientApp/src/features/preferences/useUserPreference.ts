// Hook ÚNICO de preferências visuais (ver userPreferences.ts). Uso:
//   const [modo, setModo] = useUserPreference("home", "viewMode", "cards", { validar: ehUmDe(["cards", "lista"]) });
//   const [grade, setGrade] = useUserPreference("lista-documentos", "grid", PADRAO, { projeto: projectId });
// O usuário vem das permissões efetivas (e-mail). Enquanto a identidade não carregou, a leitura devolve o padrão e as
// gravações ficam só em memória — ao identificar o usuário, a preferência salva dele é aplicada.
import { useCallback, useContext, useEffect, useMemo, useState } from "react";
import { PermissionContext } from "@/features/permissions/PermissionProvider";
import { assinar, gravarPreferencia, lerPreferencia, montarChave, type ChavePreferencia } from "./userPreferences";

export interface OpcoesPreferencia<T> {
  /** Project_ID_ACC quando a preferência é por projeto. */
  projeto?: string | null;
  validar?: (v: unknown) => v is T;
}

/** Identidade usada nas chaves de preferência (null enquanto carrega). */
export function useUsuarioPreferencias(): { usuario: string | null; pronto: boolean } {
  const ctx = useContext(PermissionContext);
  if (!ctx) return { usuario: null, pronto: true };        // fora do provider (testes/telas públicas): chave "anonimo"
  if (ctx.loading) return { usuario: null, pronto: false };
  return { usuario: ctx.effective?.email ?? null, pronto: true };
}

export function useUserPreference<T>(tela: string, config: string, padrao: T, opcoes: OpcoesPreferencia<T> = {}):
  [T, (valor: T | ((atual: T) => T)) => void, { pronto: boolean }] {
  const { usuario, pronto } = useUsuarioPreferencias();
  const { projeto = null, validar } = opcoes;
  const chave = useMemo<ChavePreferencia>(() => ({ usuario, projeto, tela, config }), [usuario, projeto, tela, config]);
  const k = montarChave(chave);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const ler = useCallback(() => lerPreferencia<T>(chave, padrao, validar), [k]);
  const [valor, setValor] = useState<T>(() => (pronto ? ler() : padrao));

  // Troca de usuário/projeto/tela (ou identidade carregada) → aplica o que está salvo para a nova chave.
  useEffect(() => { if (pronto) setValor(ler()); }, [k, pronto, ler]);
  // Outra instância gravou a mesma chave → sincroniza.
  useEffect(() => assinar(k, () => setValor(ler())), [k, ler]);

  const definir = useCallback((v: T | ((atual: T) => T)) => {
    setValor((atual) => {
      const novo = typeof v === "function" ? (v as (a: T) => T)(atual) : v;
      if (pronto) queueMicrotask(() => gravarPreferencia(chave, novo));
      return novo;
    });
  }, [chave, pronto]);

  return [valor, definir, { pronto }];
}
