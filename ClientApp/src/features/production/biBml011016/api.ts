export type EstadoFonte = "OK" | "VAZIO" | "PENDENTE" | "INDISPONIVEL";
export interface FonteBml { fonte: string; estado: EstadoFonte; motivo?: string | null; linhas: Record<string, unknown>[]; }

export class AcessoBmlNegado extends Error {}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const resposta = await fetch(url, { credentials: "include", cache: "no-store", signal });
  if ([401, 403, 503].includes(resposta.status)) throw new AcessoBmlNegado("Acesso ao BI não autorizado.");
  if (!resposta.ok) throw new Error(`Falha ao consultar o BI (HTTP ${resposta.status}).`);
  return resposta.json() as Promise<T>;
}

export const buscarFonteBml = (fonte: string, signal?: AbortSignal) =>
  getJson<FonteBml>(`/api/bi-bml011016/fontes/${encodeURIComponent(fonte)}`, signal);

export const recursoPbixBml = (nome: string) =>
  `/api/bi-bml011016/pbix/recurso?caminho=${encodeURIComponent(`Report/StaticResources/RegisteredResources/${nome}`)}`;
