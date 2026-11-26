import { useCallback, useEffect, useRef, useState } from "react";
import { Bell, AlertTriangle, Check, ChevronDown, ChevronUp, RefreshCw, X } from "lucide-react";

interface Item {
  id: string; projeto: string; etapa: string; titulo: string; mensagem: string;
  ultimaOcorrencia: string; primeiraOcorrencia: string; repeticoes: number;
  situacao: string; lida: boolean;
}
interface Resposta { items: Item[]; unreadCount: number }
const chaveResumo = "na.notifications.summary.v1";
const horario = (s: string) => {
  const d = new Date(s);
  return isNaN(d.getTime()) ? "Sem horário" : d.toLocaleString("pt-BR");
};

export function NotificationsMenu() {
  const [items, setItems] = useState<Item[]>([]);
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const [summary, setSummary] = useState(false);
  const [onlyUnread, setOnlyUnread] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const host = useRef<HTMLDivElement>(null);

  const reload = useCallback(async (signal?: AbortSignal) => {
    try {
      const res = await fetch("/api/notifications?days=7", { credentials: "include", cache: "no-store", signal });
      if (!res.ok) throw new Error(res.status === 404 ? "Central ainda não disponível no backend." : "Falha ao consultar notificações.");
      const data = await res.json() as Resposta;
      if (signal?.aborted) return;
      setItems(data.items ?? []); setUnread(data.unreadCount ?? 0); setError(null);
      try {
        if (!sessionStorage.getItem(chaveResumo)) {
          sessionStorage.setItem(chaveResumo, "1");
          if (data.unreadCount > 0) setSummary(true);
        }
      } catch { /* armazenamento bloqueado nao impede a central */ }
    } catch (e) {
      if (!signal?.aborted) setError(e instanceof Error ? e.message : "Falha ao consultar notificações.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void reload(controller.signal);
    const timer = window.setInterval(() => void reload(controller.signal), 60000);
    const onFocus = () => void reload(controller.signal);
    window.addEventListener("focus", onFocus);
    return () => { controller.abort(); window.clearInterval(timer); window.removeEventListener("focus", onFocus); };
  }, [reload]);

  useEffect(() => {
    const outside = (event: MouseEvent) => {
      if (host.current && !host.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", outside);
    return () => document.removeEventListener("mousedown", outside);
  }, []);

  const marcarLida = async (item: Item) => {
    if (item.lida || busy) return;
    setBusy(item.id);
    try {
      const r = await fetch("/api/notifications/" + encodeURIComponent(item.id) + "/read", {
        method: "POST", credentials: "include",
      });
      if (!r.ok) throw new Error("Falha ao salvar leitura.");
      setItems(prev => prev.map(n => n.id === item.id ? { ...n, lida: true } : n));
      setUnread(n => Math.max(0, n - 1)); setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao marcar como lida.");
    } finally { setBusy(null); }
  };
  const lista = onlyUnread ? items.filter(i => !i.lida) : items;
  return (
    <div ref={host} className="relative">
      <button type="button" title="Central de notificações" aria-label={"Notificações: " + unread + " não lidas"}
        aria-expanded={open} onClick={() => { setOpen(v => !v); setSummary(false); }}
        className="relative rounded-md p-2 text-muted-foreground transition-colors hover:bg-primary/5 hover:text-primary">
        <Bell className="h-4 w-4" />
        {unread > 0 && <span className="absolute -right-1 -top-1 flex min-h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[9px] font-bold text-destructive-foreground">{unread > 99 ? "99+" : unread}</span>}
      </button>
      {open && (
        <section role="dialog" aria-label="Central de notificações"
          className="absolute right-0 top-full z-[110] mt-2 w-[min(92vw,440px)] overflow-hidden rounded-xl border border-border bg-card shadow-2xl">
          <div className="flex items-center justify-between border-b border-border p-3">
            <div><p className="text-sm font-semibold">Notificações da engenharia</p><p className="text-[11px] text-muted-foreground">{unread} não lida(s) • últimos 7 dias</p></div>
            <div className="flex gap-1">
              <button aria-label="Atualizar" className="rounded-md p-2 hover:bg-muted" onClick={() => void reload()}><RefreshCw className="h-4 w-4" /></button>
              <button aria-label="Fechar" className="rounded-md p-2 hover:bg-muted" onClick={() => setOpen(false)}><X className="h-4 w-4" /></button>
            </div>
          </div>
          <div className="flex gap-4 border-b border-border px-3 py-2 text-xs">
            <button className={!onlyUnread ? "font-semibold text-primary" : "text-muted-foreground"} onClick={() => setOnlyUnread(false)}>Todas ({items.length})</button>
            <button className={onlyUnread ? "font-semibold text-primary" : "text-muted-foreground"} onClick={() => setOnlyUnread(true)}>Não lidas ({unread})</button>
          </div>
          {error && <p role="alert" className="border-b border-border p-3 text-xs text-destructive">{error}</p>}
          <div className="max-h-[min(60vh,520px)] overflow-y-auto">
            {loading && <p className="p-4 text-xs text-muted-foreground">Carregando...</p>}
            {!loading && lista.length === 0 && <p className="p-4 text-xs text-muted-foreground">Nenhum alerta encontrado.</p>}
            {lista.map(item => (
              <article key={item.id} className={"border-b border-border/70 p-3 last:border-0 " + (item.lida ? "opacity-60" : "bg-amber-500/5")}>
                <div className="flex items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
                  <div className="min-w-0 flex-1">
                    <p className="text-xs font-bold">{item.projeto}</p>
                    <p className="mt-1 text-xs font-medium">{item.titulo}</p>
                    <p className="mt-1 truncate text-[11px] text-muted-foreground">{item.etapa}</p>
                    <p className="mt-1 text-[11px] text-muted-foreground">{horario(item.ultimaOcorrencia)} • {item.repeticoes} ocorrência(s) • {item.situacao}</p>
                    {expanded === item.id && <div className="mt-2 rounded-md bg-muted/60 p-2 text-xs break-words">
                      <p>{item.mensagem || "Sem detalhes adicionais no histórico."}</p>
                      <p className="mt-2 text-muted-foreground">Primeiro registro: {horario(item.primeiraOcorrencia)}</p>
                    </div>}
                    <div className="mt-2 flex items-center gap-3">
                      <button className="flex items-center gap-1 text-[11px] text-primary" onClick={() => setExpanded(n => n === item.id ? null : item.id)}>
                        Detalhes {expanded === item.id ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
                      </button>
                      {!item.lida && <button disabled={busy === item.id} className="flex items-center gap-1 text-[11px] text-muted-foreground hover:text-primary" onClick={() => void marcarLida(item)}><Check className="h-3 w-3" /> Marcar como lida</button>}
                    </div>
                  </div>
                </div>
              </article>
            ))}
          </div>
          <p className="border-t border-border px-3 py-2 text-[10px] text-muted-foreground">Ocorrências registradas. Um erro antigo não confirma uma pendência aberta.</p>
        </section>
      )}
      {summary && !open && (
        <aside className="absolute right-0 top-full z-[105] mt-2 w-[min(92vw,365px)] rounded-xl border border-border bg-card p-4 shadow-2xl">
          <button aria-label="Fechar resumo" className="absolute right-2 top-2 rounded p-1 hover:bg-muted" onClick={() => setSummary(false)}><X className="h-4 w-4" /></button>
          <p className="pr-6 text-sm font-semibold">Resumo de alertas da engenharia</p>
          <p className="mt-2 text-xs text-muted-foreground">Há {unread} alerta(s) não lido(s) nos seus projetos. Situação: a verificar.</p>
          <button className="mt-3 rounded-md bg-primary px-3 py-2 text-xs font-semibold text-primary-foreground" onClick={() => { setOpen(true); setSummary(false); }}>Ver notificações</button>
        </aside>
      )}
    </div>
  );
}
