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
