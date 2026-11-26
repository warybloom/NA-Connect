import { tvPortalUrl } from "@/features/tv/portal/portalTransport";
export class ApiError extends Error {
  constructor(public status: number, public detail?: string) {
    super((status === 401 ? 'Sua sessão expirou. Entre novamente.' : status === 403 ? 'Você não tem acesso a este projeto.' : 'Não foi possível carregar os dados. Tente novamente.')
      + (status >= 500 ? ` (HTTP ${status}${detail ? ` · ${detail}` : ''})` : ''));
  }
}
// Mensagem do backend (já sem SQL/token) — em 5xx traz "(ref: <id>)" que casa com o log do servidor.
async function detalhe(response: Response): Promise<string | undefined> {
  try { const body = await response.clone().json(); return typeof body?.message === 'string' ? body.message : undefined; } catch { return undefined; }
}
export async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(tvPortalUrl(url), { signal, credentials: 'same-origin', headers: { Accept: 'application/json' } });
  if (!response.ok) throw new ApiError(response.status, response.status >= 500 ? await detalhe(response) : undefined);
  if (!response.headers.get('content-type')?.includes('application/json')) throw new ApiError(502);
  return response.json() as Promise<T>;
}
export interface AccItem { id: string; attributes: { name: string; region?: string } }
export interface AccList { data: AccItem[] }
export interface WeeklyRow {
  id: number; document: string | null; title: string | null; discipline: string | null;
  type: string | null; status: string | null; diagnostic: string | null;
  file: string | null; sheet: string | null; line: number | null;
}
export interface WeeklyReport {
  dataRows: number; warningRows: number; sentinelRows: number; unclassifiedRows: number;
  totalRows: number; filteredRows: number; lastSync: string | null;
  page: number; pageSize: number; rows: WeeklyRow[];
}
