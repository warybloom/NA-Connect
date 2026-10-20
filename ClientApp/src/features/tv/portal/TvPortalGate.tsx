import { useEffect, useState, type ReactNode } from "react";
import { PermissionContext } from "@/features/permissions/PermissionProvider";
import TvPlayerPage from "../TvPlayerPage";
import { localTvInterval } from "./portalTransport";

interface PortalSession { restricted: boolean; canConfigure: boolean }
interface Choice { id: string; name: string; supported: boolean }
interface DeviceStatus { available: boolean; registered: boolean; cookieRejected?: boolean; expiresAt: string | null; idleDays: number }

export function TvPortalGate({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<PortalSession | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const abort = new AbortController();
    let retry: number | undefined;
    // TV ligada antes do servidor (ou backend reiniciando): tenta de novo sozinha; só 401 pede login.
    const check = () => fetch("/api/tv-portal/session", { credentials: "same-origin", signal: abort.signal }).then(async r => {
      if (!r.ok) throw Object.assign(Error(r.status === 401 ? "Sessão expirada. Entre novamente." : "Não foi possível validar o acesso. Tentando novamente…"), { status: r.status });
      const s = await r.json();
      if (typeof s.restricted !== "boolean") throw Error("Resposta de acesso inválida.");
      setError(null); setSession(s);
    }).catch(e => {
      if (abort.signal.aborted) return;
      setError(e.message);
      if (e?.status !== 401) retry = window.setTimeout(check, 15000);
    });
    void check();
    return () => { abort.abort(); window.clearTimeout(retry); };
  }, []);
  if (error) return <div role="alert" className="p-8">{error} <a href="/login">Entrar</a></div>;
  if (!session) return <div role="status" className="p-8">Validando acesso…</div>;
  return session.restricted ? <TvPortal /> : <>{children}</>;
}

function TvPortal() {
  const [choices, setChoices] = useState<Choice[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState("");
  const [seconds, setSeconds] = useState(30);
  const [playing, setPlaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [device, setDevice] = useState<DeviceStatus | null>(null);
  const [deviceMessage, setDeviceMessage] = useState<string | null>(null);
  useEffect(() => {
    if (playing) return;
    const abort = new AbortController();
    fetch("/api/tv-portal/device", { credentials: "same-origin", signal: abort.signal })
      .then(async r => { if (r.ok) setDevice(await r.json()); }).catch(() => undefined);
    return () => abort.abort();
  }, [playing]);
  // Opt-in explícito: o servidor grava só o hash e devolve um cookie HttpOnly; o JS nunca vê nem guarda credencial.
  const alterarDispositivo = async (manter: boolean) => {
    setDeviceMessage(null);
    const r = await fetch("/api/tv-portal/device", { method: manter ? "POST" : "DELETE", credentials: "same-origin",
      headers: { "Content-Type": "application/json" }, body: manter ? JSON.stringify({ label: "TV" }) : undefined }).catch(() => null);
    if (!r?.ok) {
      const body = r ? await r.json().catch(() => null) : null;
      setDeviceMessage(body?.message ?? "Não foi possível alterar a conexão desta TV.");
      return;
    }
    const status = await fetch("/api/tv-portal/device", { credentials: "same-origin" }).then(s => s.ok ? s.json() : null).catch(() => null);
    setDevice(status);
  };
  useEffect(() => {
    window.history.replaceState(null, "", "/tv/iniciar");
    if (playing) return;
    setLoading(true);
    setError(null);
    const abort = new AbortController();
    fetch("/api/tv-portal/presentations", { credentials: "same-origin", signal: abort.signal }).then(async r => {
      if (!r.ok) throw Error(r.status === 401 ? "Sessão expirada." : "Não foi possível carregar as apresentações.");
      const values: Choice[] = await r.json(); setChoices(values);
      setSelected(previous => values.some(v => v.id === previous && v.supported) ? previous : values.find(v => v.supported)?.id ?? "");
    }).catch(e => { if (!abort.signal.aborted) setError(e.message); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [playing]);
  // Local rendering context: permits ONLY mounting the selected dashboard component. It is not an API grant.
  const viewContext = { loading: false, error: null, effective: { enforced: false, schemaInstalled: true,
    isMasterAdmin: false, email: null, projectIdAcc: null, permissions: ["tv.view"], anyProject: ["tv.view"] },
    can: () => true, reload: () => {} };
  if (playing) return <PermissionContext.Provider value={viewContext}>
    <TvPlayerPage portal={{ presentationId: selected, seconds, onExit: () => setPlaying(false) }} />
  </PermissionContext.Provider>;
  return <main className="min-h-screen bg-background text-foreground flex items-center justify-center p-6">
    <section className="w-full max-w-lg space-y-5 rounded-xl border border-border bg-card p-8">
      <h1 className="text-2xl font-bold">Apresentações TV</h1>
      {error && <p role="alert">{error}</p>}
      {loading && <p role="status">Carregando apresentações autorizadas...</p>}
      {!loading && !error && !choices.length && <p>Nenhuma apresentação autorizada. Solicite a atribuição ao administrador.</p>}
      <label className="block">Apresentação<select aria-label="Apresentação" className="block w-full rounded border bg-background p-2" value={selected} onChange={e => setSelected(e.target.value)}>
        <option value="">Selecione</option>{choices.map(c => <option key={c.id} value={c.id} disabled={!c.supported}>{c.name}{!c.supported ? " — NÃO APTO" : ""}</option>)}
      </select></label>
      <label className="block">Intervalo entre telas (segundos)<input aria-label="Intervalo entre telas" className="block w-full rounded border bg-background p-2" type="number" min={10} max={300} step={1} value={seconds} onChange={e => setSeconds(Number(e.target.value))} /></label>
      <p className="text-sm text-muted-foreground">De 10 a 300 segundos, somente nesta reprodução.</p>
      <button className="rounded bg-primary text-primary-foreground px-4 py-3 disabled:opacity-40" disabled={loading || !selected || localTvInterval(seconds) === null || !!error} onClick={() => {
        if (localTvInterval(seconds) === null || !choices.some(c => c.id === selected && c.supported)) return;
        // Fullscreen requested in the click gesture; player can also toggle its own container.
        void document.documentElement.requestFullscreen?.().catch(() => undefined);
        setPlaying(true);
      }}>Iniciar apresentação</button>
      {device?.available && <div className="space-y-2 rounded border border-border p-3 text-sm" data-testid="tv-dispositivo">
        {device.registered
          ? <p>Esta TV fica conectada sem novo login até {device.expiresAt ? new Date(device.expiresAt).toLocaleDateString("pt-BR") : "—"}, renovando enquanto estiver em uso (expira após {device.idleDays} dias desligada). O administrador pode revogar a qualquer momento.</p>
          : <p>Use somente em TV/computador dedicado: mantém esta conta TV conectada após reinícios do servidor e do navegador.</p>}
        <button type="button" className="rounded border px-3 py-2" onClick={() => void alterarDispositivo(!device.registered)}>
          {device.registered ? "Desconectar este dispositivo" : "Manter esta TV conectada"}</button>
        {device.cookieRejected && <p role="alert">Este navegador recusou o cookie seguro: abra a TV pelo endereço HTTPS do N&A Connect para mantê-la conectada.</p>}
        {deviceMessage && <p role="alert">{deviceMessage}</p>}
      </div>}
      <a className="block text-sm underline" href="/Authentication/OAuth/Logout">Sair</a>
    </section>
  </main>;
}
