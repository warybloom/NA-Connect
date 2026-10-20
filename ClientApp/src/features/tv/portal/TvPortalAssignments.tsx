import { useEffect, useState } from "react";
import { TvDeviceSessions } from "./TvDeviceSessions";
type Assignment = { revision: number; grants: { presentationId: string; userIds: number[] }[] };
interface Data { assignments: Assignment; users: { userId: number; name: string }[]; presentations: { id: string; name: string; supported: boolean }[] }
export function TvPortalAssignments() {
  const [data, setData] = useState<Data | null>(null);
  const [message, setMessage] = useState("");
  useEffect(() => { const abort = new AbortController();
    fetch("/api/tv-portal/assignments", { credentials: "same-origin", signal: abort.signal }).then(async r => {
      if (!r.ok) throw Error("Não foi possível consultar as atribuições TV."); setData(await r.json());
    }).catch(e => { if (!abort.signal.aborted) setMessage(e.message); });
    return () => abort.abort();
  }, []);
  return <section className="rounded-xl border border-border p-5 space-y-3">
    <h2 className="font-bold">Acesso restrito às apresentações</h2>
    <p className="text-sm">Marque os usuários TV autorizados. Nenhuma atribuição concede acesso a relatórios fora do portal. O usuário também precisa de vínculo ativo aos projetos dos slides.</p>
    {message && <p role="status">{message}</p>}
    {data?.presentations.map(p => <fieldset key={p.id} className="border border-border p-3"><legend>{p.name}{!p.supported && " — NÃO APTO para acesso restrito"}</legend>
      {data.users.map(u => <label key={u.userId} className="block"><input type="checkbox" checked={data.assignments.grants.some(g => g.presentationId === p.id && g.userIds.includes(u.userId))} onChange={e => {
        const selected = data.assignments.grants.find(g => g.presentationId === p.id)?.userIds ?? [];
        const ids = e.target.checked ? [...selected, u.userId] : selected.filter(id => id !== u.userId);
        setData({ ...data, assignments: { ...data.assignments, grants: [...data.assignments.grants.filter(g => g.presentationId !== p.id), { presentationId: p.id, userIds: ids }] } });
      }} /> {u.name}</label>)}
    </fieldset>)}
    <button disabled={!data} className="rounded border px-4 py-2" onClick={async () => {
      const r = await fetch("/api/tv-portal/assignments", { method: "PUT", credentials: "same-origin", headers: { "Content-Type": "application/json" }, body: JSON.stringify(data?.assignments) });
      if (!r.ok) { setMessage(r.status === 409 ? "Alterado em outra sessão. Recarregue antes de salvar." : "Não foi possível salvar."); return; }
      const saved: Assignment = await r.json(); setData(d => d ? { ...d, assignments: saved } : null); setMessage("Atribuições salvas.");
    }}>Salvar atribuições TV</button>
    <TvDeviceSessions />
  </section>;
}
