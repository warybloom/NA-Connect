// MAPEAMENTO DE FORMATOS DA LDP (por template): valor da coluna Format da LDP → perfil calibrado / detectar pela dimensão / ignorar.
// Nada é assumido globalmente ("D" pode ser ANSI D, ARCH D ou um código do cliente). Sem mapeamento = detectar pela dimensão.
// A dimensão física do PDF continua protegendo: um perfil cuja página não confere nunca é usado (o backend faz essa checagem).
import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Loader2, Ruler, Save } from "lucide-react";

type Modo = "PERFIL" | "DIMENSAO" | "IGNORAR";
interface Mapeamento { valorFormatoLdp: string; modo: Modo; tipoFolha: string | null }
interface Resposta {
  migracaoPendente: boolean; mapeamentos: Mapeamento[]; valoresLdp: string[];
  perfis: { tipoFolha: string; referenceWidth: number; referenceHeight: number }[];
}

const DIMENSAO = "__DIMENSAO__", IGNORAR = "__IGNORAR__";

export default function FormatMappingPanel({ pacoteId, podeEditar }: { pacoteId: number; podeEditar: boolean }) {
  const [dados, setDados] = useState<Resposta | null>(null);
  const [escolhas, setEscolhas] = useState<Record<string, string>>({});
  const [novoValor, setNovoValor] = useState("");
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    const abort = new AbortController();
    setDados(null);
    fetch(`/api/admin/ocr/pacotes/${pacoteId}/formatos`, { signal: abort.signal })
      .then(async (r) => { if (!r.ok) throw new Error(`Falha HTTP ${r.status}`); return r.json() as Promise<Resposta>; })
      .then((d) => {
        setDados(d);
        setEscolhas(Object.fromEntries(d.mapeamentos.map((m) => [m.valorFormatoLdp, m.modo === "PERFIL" ? m.tipoFolha ?? DIMENSAO : m.modo === "IGNORAR" ? IGNORAR : DIMENSAO])));
      })
      .catch((e: unknown) => { if (!abort.signal.aborted) toast.error(e instanceof Error ? e.message : "Falha ao carregar o mapeamento de formatos."); });
    return () => abort.abort();
  }, [pacoteId]);

  // Valores reais da LDP (projetos que usam o template) + os já mapeados + os adicionados à mão.
  const valores = useMemo(() => [...new Set([...(dados?.valoresLdp ?? []), ...Object.keys(escolhas)])].sort((a, b) => a.localeCompare(b)), [dados, escolhas]);

  const salvar = async () => {
    setSalvando(true);
    try {
      // Só valores com escolha diferente de "detectar pela dimensão" precisam ser gravados (sem mapeamento = dimensão).
      const mapeamentos = Object.entries(escolhas).filter(([, v]) => v !== DIMENSAO).map(([valorFormatoLdp, v]) => (
        v === IGNORAR ? { valorFormatoLdp, modo: "IGNORAR", tipoFolha: null } : { valorFormatoLdp, modo: "PERFIL", tipoFolha: v }));
      const r = await fetch(`/api/admin/ocr/pacotes/${pacoteId}/formatos`, {
        method: "PUT", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ mapeamentos }),
      });
      const b = await r.json().catch(() => null);
      if (!r.ok) throw new Error(b?.message ?? `Falha HTTP ${r.status}`);
      toast.success("Mapeamento de formatos salvo.");
    } catch (e) { toast.error(e instanceof Error ? e.message : "Falha ao salvar o mapeamento."); }
    finally { setSalvando(false); }
  };

  if (!dados) return <div className="flex items-center gap-2 p-4 text-xs text-slate-400"><Loader2 className="h-4 w-4 animate-spin" />Carregando mapeamento de formatos…</div>;

  return (
    <section className="space-y-3 rounded-2xl border border-border/60 bg-slate-900 p-5">
      <div className="flex flex-wrap items-center gap-3">
        <Ruler className="h-4 w-4 text-primary" />
        <h4 className="flex-1 text-[11px] font-black uppercase tracking-[0.15em] text-primary">Mapeamento de formatos da LDP</h4>
        {podeEditar && <button type="button" disabled={salvando || dados.migracaoPendente} onClick={() => void salvar()}
          className="flex items-center gap-2 rounded-lg bg-primary px-3 py-1.5 text-[10px] font-black uppercase text-primary-foreground disabled:opacity-50">
          {salvando ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}Salvar mapeamento
        </button>}
      </div>
      <p className="text-[10px] leading-relaxed text-slate-400">
        Diga o que cada valor da coluna <strong className="text-slate-200">Format</strong> da LDP significa neste template. Sem mapeamento, o perfil é
        detectado pela dimensão da página. Mesmo mapeado, um perfil cuja dimensão não confere com o PDF nunca é usado.
      </p>
      {dados.migracaoPendente && <p className="rounded-lg bg-amber-500/10 p-2 text-[10px] font-bold text-amber-300">Migration 07 pendente: o mapeamento só pode ser salvo depois de aplicada.</p>}
      <div className="overflow-hidden rounded-xl border border-border/60">
        <table className="w-full text-xs text-slate-200">
          <thead className="bg-slate-950/60 text-left text-[9px] uppercase tracking-widest text-slate-400"><tr><th className="px-3 py-2">Valor na LDP</th><th className="px-3 py-2">Perfil calibrado</th></tr></thead>
          <tbody>
            {valores.length === 0 && <tr><td colSpan={2} className="px-3 py-3 text-[10px] text-slate-500">Nenhum valor de Format encontrado na LDP.</td></tr>}
            {valores.map((v) => (
              <tr key={v} className="border-t border-border/60">
                <td className="px-3 py-1.5 font-mono font-bold">{v}</td>
                <td className="px-3 py-1.5">
                  <select aria-label={`Perfil para ${v}`} disabled={!podeEditar} value={escolhas[v] ?? DIMENSAO}
                    onChange={(e) => setEscolhas((m) => ({ ...m, [v]: e.target.value }))}
                    className="w-full rounded-md border border-border bg-slate-950/60 px-2 py-1 text-xs text-foreground">
                    <option value={DIMENSAO}>Detectar pela dimensão</option>
                    {dados.perfis.map((p) => <option key={p.tipoFolha} value={p.tipoFolha}>{p.tipoFolha} ({Math.round(p.referenceWidth)}×{Math.round(p.referenceHeight)} pt)</option>)}
                    <option value={IGNORAR}>Ignorar (não suportado)</option>
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {podeEditar && <div className="flex gap-2">
        <input value={novoValor} onChange={(e) => setNovoValor(e.target.value)} placeholder="Outro valor de Format…" maxLength={50}
          className="flex-1 rounded-md border border-border bg-slate-950/60 px-2 py-1 text-xs text-foreground" />
        <button type="button" className="rounded-md border border-border px-3 text-[10px] font-bold uppercase text-slate-300"
          onClick={() => { const v = novoValor.trim(); if (v) { setEscolhas((m) => ({ ...m, [v]: m[v] ?? DIMENSAO })); setNovoValor(""); } }}>Adicionar</button>
      </div>}
    </section>
  );
}
