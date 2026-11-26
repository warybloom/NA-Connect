import { useCallback, useEffect, useState } from "react";

interface Device {
  id: string; userId: number; userName: string; label: string; createdAt: string; lastSeenAt: string; expiresAt: string;
  revokedAt: string | null; revokedReason: string | null; active: boolean;
}
const data = (iso: string | null) => iso ? new Date(iso).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" }) : "—";

/** TVs mantidas conectadas (sessão de dispositivo). Revogar encerra a TV na próxima requisição (≤ 15 s). */
export function TvDeviceSessions() {
  const [state, setState] = useState<{ enabled: boolean; devices: Device[] } | null>(null);
  const [message, setMessage] = useState("");
  const load = useCallback((signal?: AbortSignal) => fetch("/api/tv-portal/devices", { credentials: "same-origin", signal }).then(async r => {
    if (!r.ok) throw Error("Não foi possível consultar as TVs conectadas.");
    setState(await r.json());
  }).catch(e => { if (!signal?.aborted) setMessage(e.message); }), []);
  useEffect(() => { const abort = new AbortController(); void load(abort.signal); return () => abort.abort(); }, [load]);
  if (!state) return message ? <p role="status">{message}</p> : null;
  const ativos = state.devices.filter(d => d.active);
  return <section className="space-y-2" data-testid="tv-dispositivos-admin">
    <h3 className="font-bold">TVs conectadas (sessão de dispositivo)</h3>
    {!state.enabled && <p className="text-sm">Recurso desligado no servidor (TvPortal:DeviceSession:Enabled). Nenhuma TV permanece conectada.</p>}
    {message && <p role="status">{message}</p>}
    {state.enabled && !ativos.length && <p className="text-sm">Nenhuma TV conectada.</p>}
    {ativos.map(d => <div key={d.id} className="flex flex-wrap items-center gap-3 border border-border p-2 text-sm">
      <span className="font-medium">{d.userName}</span><span>{d.label}</span>
      <span>último uso {data(d.lastSeenAt)}</span><span>expira {data(d.expiresAt)}</span>
      <button type="button" className="rounded border px-3 py-1" onClick={async () => {
        const r = await fetch(`/api/tv-portal/devices/${encodeURIComponent(d.id)}`, { method: "DELETE", credentials: "same-origin" }).catch(() => null);
        setMessage(r?.ok ? "TV desconectada." : "Não foi possível revogar.");
        void load();
      }}>Revogar</button>
    </div>)}
  </section>;
}
