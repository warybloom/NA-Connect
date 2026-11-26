import { localTvInterval } from "./portalTransport";

// Retomada automática da apresentação na TV restrita (página recarregada, navegador reiniciado, frontend republicado).
// Guarda SÓ o id da apresentação e o intervalo local: não são segredos e não autorizam nada; o servidor revalida
// identidade, atribuição e projetos a cada GET. Credencial nenhuma passa por aqui (o dispositivo usa cookie HttpOnly).
export const CHAVE_RETOMADA_TV = "na.tv.retomada";
export interface RetomadaTv { presentationId: string; seconds: number }
type Armazenamento = Pick<Storage, "getItem" | "setItem" | "removeItem">;

const local = (): Armazenamento | null => { try { return window.localStorage; } catch { return null; } };

/** Só retoma o que ainda está na lista autorizada desta conta e apto. */
export function retomadaValida(valor: unknown, escolhas: { id: string; supported: boolean }[]): RetomadaTv | null {
  if (!valor || typeof valor !== "object") return null;
  const { presentationId, seconds } = valor as Record<string, unknown>;
  if (typeof presentationId !== "string" || localTvInterval(seconds) === null) return null;
  return escolhas.some(c => c.id === presentationId && c.supported) ? { presentationId, seconds: seconds as number } : null;
}

export function lerRetomadaTv(armazenamento: Armazenamento | null = local()): unknown {
  try { const bruto = armazenamento?.getItem(CHAVE_RETOMADA_TV); return bruto ? JSON.parse(bruto) : null; } catch { return null; }
}

export function salvarRetomadaTv(valor: RetomadaTv, armazenamento: Armazenamento | null = local()): void {
  try { armazenamento?.setItem(CHAVE_RETOMADA_TV, JSON.stringify({ presentationId: valor.presentationId, seconds: valor.seconds })); } catch { /* sem armazenamento: só não retoma */ }
}

export function limparRetomadaTv(armazenamento: Armazenamento | null = local()): void {
  try { armazenamento?.removeItem(CHAVE_RETOMADA_TV); } catch { /* idem */ }
}
