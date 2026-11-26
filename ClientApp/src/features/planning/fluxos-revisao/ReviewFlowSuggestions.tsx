import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { obterSugestoes, type ReviewStatusSuggestion } from "@/features/production/analytics/doc03/reviewFlowsApi";
import { normalizarEtapa } from "@/features/production/analytics/doc03/documentacaoFluxoLogic";
import type { EtapaEditavel } from "./fluxosRevisaoLogic";

type Project = { id: string; nome: string };
type Props = { projects: Project[]; initialProjectIds: string[]; stages: EtapaEditavel[]; onStages: (stages: EtapaEditavel[]) => void };
const rowKey = (r: ReviewStatusSuggestion) => `${r.projectId}::${r.original}`;

export default function ReviewFlowSuggestions({ projects, initialProjectIds, stages, onStages }: Props) {
  const [projectIds, setProjectIds] = useState<string[]>(initialProjectIds);
  const [rows, setRows] = useState<ReviewStatusSuggestion[]>([]);
  const [names, setNames] = useState<Record<string, string>>({});
  const [selected, setSelected] = useState<string[]>([]);
  const [ignored, setIgnored] = useState<string[]>([]);
  const [confirmed, setConfirmed] = useState<string[]>([]);
  const [summary, setSummary] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const key = projectIds.join(",");
  useEffect(() => {
    if (!key) { setRows([]); return; }
    const c = new AbortController(); setLoading(true); setError("");
    void obterSugestoes(key.split(","), c.signal).then(r => {
      setRows(r.statuses); setNames(Object.fromEntries(r.statuses.map(x => [rowKey(x), x.suggestedStage ?? ""])));
      setSelected([]); setIgnored([]); setConfirmed([]); setSummary(false);
    }).catch(e => { if (!c.signal.aborted) setError(e instanceof Error ? e.message : "Falha ao consultar status."); })
      .finally(() => { if (!c.signal.aborted) setLoading(false); });
    return () => c.abort();
  }, [key]);
  const chosen = rows.filter(r => selected.includes(rowKey(r)) && !ignored.includes(rowKey(r)) && !confirmed.includes(rowKey(r)));
  const apply = (items: ReviewStatusSuggestion[]) => {
    const next = stages.map(s => ({ ...s }));
    for (const row of items) {
      const name = (names[rowKey(row)] ?? "").trim();
      if (!name) { setError(`Informe o nome oficial para ${row.original}.`); return; }
      const alias = normalizarEtapa(row.original);
      const target = next.findIndex(s => normalizarEtapa(s.name) === normalizarEtapa(name));
      const other = next.findIndex((s, i) => i !== target && s.valores.split(/[;\n]/).some(v => normalizarEtapa(v) === alias));
      if (other >= 0) { setError(`O alias ${row.original} já pertence à etapa ${next[other].name}.`); return; }
      if (target < 0) {
        if (next.length === 1 && !next[0].name.trim() && !next[0].valores.trim()) next[0] = { name, valores: row.original };
        else next.push({ name, valores: row.original });
      }
      else if (!next[target].valores.split(/[;\n]/).some(v => normalizarEtapa(v) === alias))
        next[target].valores = [next[target].valores.trim(), row.original].filter(Boolean).join("; ");
    }
    onStages(next); setConfirmed(xs => [...xs, ...items.map(rowKey)]); setSelected(xs => xs.filter(k => !items.some(r => rowKey(r) === k)));
    setSummary(false); setError("");
  };
  return <fieldset className="space-y-3 rounded-lg border border-border p-3">
    <legend className="px-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">Sugestões dos status sincronizados</legend>
    <p className="text-xs text-muted-foreground">Selecione projetos e confirme os aliases. A confirmação altera somente o formulário; Salvar grava o fluxo.</p>
    <div className="max-h-32 overflow-y-auto rounded border border-border p-2 sm:grid sm:grid-cols-2">{projects.map(p =>
      <label key={p.id} className="flex items-center gap-2 text-sm"><input type="checkbox" checked={projectIds.includes(p.id)} onChange={() => setProjectIds(xs => xs.includes(p.id) ? xs.filter(x => x !== p.id) : [...xs, p.id])} />{p.nome}</label>)}</div>
    {loading && <p className="text-sm">Consultando Status_In_Flow real…</p>}
    {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    {!loading && !!projectIds.length && !rows.length && !error && <p className="text-sm text-muted-foreground">Nenhum status encontrado.</p>}
    {!!rows.length && <div className="max-h-80 overflow-auto"><table className="w-full text-sm"><thead><tr className="text-left text-xs uppercase text-muted-foreground">
      <th>Sel.</th><th>Projeto / original</th><th>Docs</th><th>Português / inglês</th><th>Etapa de destino</th><th>Situação</th><th>Ações</th>
    </tr></thead><tbody>{rows.map(r => { const k = rowKey(r), done = confirmed.includes(k), skip = ignored.includes(k), mapped = r.situation === "mapeado";
      return <tr key={k} className="border-t border-border align-top">
        <td><input type="checkbox" aria-label={`Selecionar ${r.original}`} disabled={mapped || done || skip} checked={selected.includes(k)} onChange={e => setSelected(xs => e.target.checked ? [...xs, k] : xs.filter(x => x !== k))} /></td>
        <td><small className="text-muted-foreground">{projects.find(p => p.id === r.projectId)?.nome ?? r.projectId}</small><br /><b>{r.original}</b></td>
        <td>{r.count}</td><td>{r.portuguese ?? "—"} / {r.english ?? "—"}<small className="block">{r.kind}</small></td>
        <td><input className="w-40 rounded border border-border bg-background px-2 py-1" list="review-stage-candidates" value={names[k] ?? ""} disabled={mapped || done || skip}
          onChange={e => setNames(xs => ({ ...xs, [k]: e.target.value }))} aria-label={`Nome oficial para ${r.original}`} />
          {!!r.candidates.length && <small className="block">Candidatos: {r.candidates.join(" / ")}</small>}</td>
        <td>{done ? "confirmado no formulário" : skip ? "ignorado" : r.situation}</td>
        <td className="whitespace-nowrap">{!mapped && !done && !skip && <><Button type="button" size="sm" variant="outline" onClick={() => apply([r])}>Confirmar sugestão</Button>{" "}
          <Button type="button" size="sm" variant="ghost" onClick={() => setNames(xs => ({ ...xs, [k]: "" }))}>Alterar sugestão</Button>{" "}
          <Button type="button" size="sm" variant="ghost" onClick={() => setIgnored(xs => [...xs, k])}>Ignorar</Button></>}</td>
      </tr>; })}</tbody></table></div>}
    <datalist id="review-stage-candidates">{[...new Set([...stages.map(s => s.name), ...rows.flatMap(r => r.candidates)])].map(n => <option key={n} value={n} />)}</datalist>
    {!!chosen.length && <Button type="button" size="sm" variant="outline" onClick={() => setSummary(true)}>Confirmar selecionados ({chosen.length})</Button>}
    {summary && <div className="rounded border border-primary/40 bg-card p-3 text-sm"><b>Revise as alterações:</b>
      <ul className="my-2 list-disc pl-5">{chosen.map(r => <li key={rowKey(r)}>{r.original} → {names[rowKey(r)] || "(sem nome)"}</li>)}</ul>
      <div className="flex gap-2"><Button type="button" size="sm" onClick={() => apply(chosen)}>Aplicar selecionados ao formulário</Button>
        <Button type="button" size="sm" variant="outline" onClick={() => setSummary(false)}>Voltar</Button></div></div>}
  </fieldset>;
}
