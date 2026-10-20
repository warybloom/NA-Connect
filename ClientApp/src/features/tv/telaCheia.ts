// Tela cheia REAL do player da TV (Fullscreen API nativa). O estado vem sempre do navegador
// (document.fullscreenElement + evento fullscreenchange): ESC, F11 ou sair pelo botão mantêm a UI sincronizada.
import { useCallback, useEffect, useState, type RefObject } from "react";

/** Subconjunto do document usado aqui (permite testar sem DOM). Inclui os nomes prefixados do Safari. */
export interface DocumentoTelaCheia {
  fullscreenElement?: Element | null;
  webkitFullscreenElement?: Element | null;
  fullscreenEnabled?: boolean;
  webkitFullscreenEnabled?: boolean;
  exitFullscreen?: () => Promise<void>;
  webkitExitFullscreen?: () => Promise<void> | void;
  addEventListener: (tipo: string, fn: () => void) => void;
  removeEventListener: (tipo: string, fn: () => void) => void;
}

export interface ElementoTelaCheia {
  requestFullscreen?: (opcoes?: FullscreenOptions) => Promise<void>;
  webkitRequestFullscreen?: () => Promise<void> | void;
}

export const EVENTOS_TELA_CHEIA = ["fullscreenchange", "webkitfullscreenchange"] as const;

export const elementoEmTelaCheia = (doc: DocumentoTelaCheia): Element | null =>
  doc.fullscreenElement ?? doc.webkitFullscreenElement ?? null;

export const telaCheiaSuportada = (doc: DocumentoTelaCheia, alvo: ElementoTelaCheia | null) =>
  !!alvo && (doc.fullscreenEnabled ?? doc.webkitFullscreenEnabled ?? true) && !!(alvo.requestFullscreen || alvo.webkitRequestFullscreen);

export type ResultadoTelaCheia = "entrou" | "saiu" | "sem-suporte" | "falhou";

/** Entra com o CONTAINER da apresentação ou sai, conforme o estado real do navegador. Nunca lança. */
export async function alternarTelaCheia(alvo: ElementoTelaCheia | null, doc: DocumentoTelaCheia): Promise<ResultadoTelaCheia> {
  try {
    if (elementoEmTelaCheia(doc)) {
      if (doc.exitFullscreen) await doc.exitFullscreen();
      else await doc.webkitExitFullscreen?.();
      return "saiu";
    }
    if (!alvo || !telaCheiaSuportada(doc, alvo)) return "sem-suporte";
    if (alvo.requestFullscreen) await alvo.requestFullscreen({ navigationUI: "hide" });
    else await alvo.webkitRequestFullscreen?.();
    return "entrou";
  } catch (e) {
    // Ex.: navegador/ambiente bloqueia (iframe sem allowfullscreen, política, sem gesto do usuário).
    console.warn("[TV] Tela cheia recusada pelo navegador:", e instanceof Error ? e.message : e);
    return "falhou";
  }
}

/** Sai da tela cheia se estiver ativa (ao fechar o player). */
export async function sairDaTelaCheia(doc: DocumentoTelaCheia) {
  if (elementoEmTelaCheia(doc)) await alternarTelaCheia(null, doc);
}

export function useTelaCheia(alvo: RefObject<HTMLElement | null>) {
  const doc = document as unknown as DocumentoTelaCheia;
  const [ativa, setAtiva] = useState(() => !!elementoEmTelaCheia(doc));
  const [aviso, setAviso] = useState<string | null>(null);

  useEffect(() => {
    const sincronizar = () => setAtiva(!!elementoEmTelaCheia(doc));
    EVENTOS_TELA_CHEIA.forEach((t) => doc.addEventListener(t, sincronizar));
    sincronizar();
    return () => EVENTOS_TELA_CHEIA.forEach((t) => doc.removeEventListener(t, sincronizar));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!aviso) return;
    const t = window.setTimeout(() => setAviso(null), 5000);
    return () => window.clearTimeout(t);
  }, [aviso]);

  const alternar = useCallback(async () => {
    const r = await alternarTelaCheia(alvo.current as ElementoTelaCheia | null, doc);
    if (r === "sem-suporte") setAviso("Tela cheia não suportada neste navegador.");
    else if (r === "falhou") setAviso("O navegador não permitiu a tela cheia. Use F11.");
    else setAviso(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [alvo]);

  return { ativa, alternar, aviso };
}
