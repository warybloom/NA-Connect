// UX transport only. Server revalidates identity, allowlist, active presentation, slide, project and resource on every GET.
let scope: { presentation: string; slide: number } | null = null;
export const setTvPortalScope = (value: typeof scope) => { scope = value; };
export const localTvInterval = (value: unknown): number | null =>
  typeof value === "number" && Number.isInteger(value) && value >= 10 && value <= 300 ? value : null;
export function tvPortalUrl(url: string): string {
  if (!scope || url.startsWith("/api/tv-portal/")) return url;
  const [resource, query = ""] = url.split("?");
  const params = new URLSearchParams(query);
  params.set("resource", resource);
  return `/api/tv-portal/data/${encodeURIComponent(scope.presentation)}/${scope.slide}?${params}`;
}
// Revalidação do player no portal. 401/403/404/409 = acesso encerrado/revogado: tira a apresentação da tela na hora.
// Rede/5xx (backend reiniciando, túnel oscilando) = transitório: mantém o que já foi autorizado por algumas tentativas
// e se recupera sozinho quando o servidor volta. Antes, qualquer falha deixava a TV parada até alguém reiniciar.
export const TV_FALHAS_TOLERADAS = 4;
export type FalhaRevalidacaoTv = "encerrado" | "transitorio";
export const classificarFalhaTv = (status: number | null): FalhaRevalidacaoTv =>
  status !== null && [401, 403, 404, 409].includes(status) ? "encerrado" : "transitorio";
export function mensagemFalhaTv(status: number | null): string {
  if (status === 401) return "Sessão da TV encerrada. Entre novamente.";
  if (status === 403 || status === 404) return "Acesso revogado: apresentação não autorizada para esta conta.";
  if (status === 409) return "Apresentação NÃO APTA para o acesso restrito.";
  return "Servidor indisponível. Reconectando automaticamente…";
}
