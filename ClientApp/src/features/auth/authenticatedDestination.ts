type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

/** Routing hint after CheckStatus succeeds. Authorization remains on the server.
 * An unavailable TV endpoint must never lock ordinary administrators out.
 */
export async function authenticatedDestination(
  fetchImpl: FetchLike = fetch,
  signal?: AbortSignal,
): Promise<"/tv/iniciar" | "/login" | null> {
  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  if (signal?.aborted) controller.abort();
  const timer = setTimeout(abort, 4000);
  try {
    const response = await fetchImpl("/api/tv-portal/session", {
      credentials: "include", signal: controller.signal,
      headers: { Accept: "application/json" },
    });
    if (response.status === 401) return "/login";
    if (!response.ok) return null;
    const session: unknown = await response.json();
    return typeof session === "object" && session !== null
      && "restricted" in session && session.restricted === true ? "/tv/iniciar" : null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", abort);
  }
}
