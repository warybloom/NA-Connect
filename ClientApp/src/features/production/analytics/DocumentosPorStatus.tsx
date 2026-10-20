// Lista de Documentos FILTRADA por status (aberta pelos cards do Resumo: /controle-producao/lista-documentos?status=X).
// Usa a MESMA partição da Gestão à Vista (statusDocumento calculado no backend, Tipo 1/Tipo 2) — a quantidade listada
// é exatamente a do card clicado. "Ver lista completa" volta à grade normal da Lista de Documentos.
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowDown, ArrowUp, ArrowUpDown, ListChecks, Search, X } from "lucide-react";
import ConsolidadoBanner from "@/components/pmo/ConsolidadoBanner";
import { formatCurrency, formatData, formatHH, usePmoReports } from "@/components/pmo/pmoData";
import type { EnrichedDocument, FactsReport } from "@/components/pmo/pmoFacts";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ReportState } from "@/components/reports/ReportComponents";
import { useUserPreference } from "@/features/preferences/useUserPreference";
import { cn } from "@/lib/utils";
import { rotaProducao } from "../productionRoutes";
import { documentosDoStatus, ROTULO_STATUS, type StatusDocumento } from "./resumoLogic";

type Col = { key: keyof EnrichedDocument; label: string; fmt?: (d: EnrichedDocument) => string; num?: boolean };
const COLUNAS: Col[] = [
  { key: "codigo", label: "Documento" }, { key: "titulo", label: "Título" }, { key: "disciplina", label: "Disciplina" },
  { key: "dataAlvo", label: "Data alvo", fmt: (d) => formatData(d.dataAlvo) }, { key: "dataEmissao", label: "Emissão", fmt: (d) => formatData(d.dataEmissao) },
  { key: "hhPrevisto", label: "HH prev.", fmt: (d) => formatHH(d.hhPrevisto), num: true }, { key: "hhRealizado", label: "HH real.", fmt: (d) => formatHH(d.hhRealizado), num: true },
  { key: "custoPrevisto", label: "Custo prev.", fmt: (d) => formatCurrency(d.custoPrevisto), num: true },
  { key: "avancoFisico", label: "Avanço físico", fmt: (d) => (d.avancoFisico === null ? "—" : `${formatHH(d.avancoFisico)}%`), num: true },
];

export default function DocumentosPorStatus({ status, selection }: { status: StatusDocumento; selection: PmoSelecionado[] }) {
  const { reports, falhas, loading, error, retry } = usePmoReports("gestao-vista", { hub: "", project: "", selection });
  const [busca, setBusca] = useState("");
  // Ordenação lembrada (preferência visual centralizada).
  const [ordem, setOrdem] = useUserPreference<{ key: string; dir: "asc" | "desc" } | null>("documentos-por-status", "sort", null);
  const nomes = useMemo(() => new Map(reports.map((r) => [r.projectId, r.nomeProjeto])), [reports]);

  const linhas = useMemo(() => {
    const q = busca.trim().toLowerCase();
    const base = documentosDoStatus(reports as FactsReport[], status)
      .filter((d) => !q || [d.codigo, d.titulo, d.disciplina].some((v) => v?.toLowerCase().includes(q)));
    if (!ordem) return base;
    const k = ordem.key as keyof EnrichedDocument;
    return [...base].sort((a, b) => {
      const va = a[k], vb = b[k];
      const c = va == null ? 1 : vb == null ? -1 : typeof va === "number" && typeof vb === "number" ? va - vb : String(va).localeCompare(String(vb), "pt-BR");
      return ordem.dir === "asc" ? c : -c;
    });
  }, [reports, status, busca, ordem]);

  if (loading) return <ReportState title="Carregando documentos…">Mesma fonte da Gestão à Vista.</ReportState>;
  if (error) return <ReportState title={error} error><button className="text-primary underline" onClick={retry}>Tentar novamente</button></ReportState>;
  const total = documentosDoStatus(reports as FactsReport[], status).length;

  return (
    <section className="flex min-h-0 flex-col gap-3">
      <ConsolidadoBanner selection={selection} falhas={falhas} />
      <div className="flex flex-wrap items-center gap-2">
        <span className="flex items-center gap-2 rounded-full border border-primary/40 bg-primary/10 px-3 py-1 text-xs font-bold text-primary">
          Filtro: {ROTULO_STATUS[status]} ({total})
          <Link to={rotaProducao("lista-documentos")} title="Remover filtro" aria-label="Remover filtro"><X className="h-3.5 w-3.5" /></Link>
        </span>
        <span className="relative ml-auto">
          <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input aria-label="Pesquisar" placeholder="Pesquisar documento, título, disciplina" value={busca} onChange={(e) => setBusca(e.target.value)}
            className="h-8 w-64 rounded-md border border-border bg-background pl-8 pr-2.5 text-xs" />
        </span>
        <Link to={rotaProducao("lista-documentos")} className="flex h-8 items-center gap-1.5 rounded-md border border-border px-2.5 text-xs font-bold hover:bg-accent">
          <ListChecks className="h-3.5 w-3.5" />Ver lista completa
        </Link>
      </div>
      <div className="max-h-[calc(100vh-15rem)] min-h-[240px] overflow-auto rounded-xl border border-border bg-card/60 custom-scrollbar">
        <table className="w-full text-left text-xs">
          <thead className="sticky top-0 z-10 bg-secondary text-muted-foreground">
            <tr>
              {selection.length > 1 && <th className="px-2 py-2 font-semibold">Projeto</th>}
              {COLUNAS.map((c) => {
                const ativa = ordem?.key === c.key;
                const Icone = !ativa ? ArrowUpDown : ordem!.dir === "asc" ? ArrowUp : ArrowDown;
                return (
                  <th key={c.key} className={cn("px-2 py-2 font-semibold", c.num && "text-right")}>
                    <button type="button" className="inline-flex items-center gap-1 hover:text-foreground"
                      onClick={() => setOrdem((o) => (o?.key !== c.key ? { key: c.key, dir: "asc" } : o.dir === "asc" ? { key: c.key, dir: "desc" } : null))}>
                      {c.label}<Icone className={cn("h-3 w-3", ativa ? "text-primary" : "opacity-40")} />
                    </button>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {linhas.length === 0 ? (
              <tr><td colSpan={COLUNAS.length + 1} className="px-3 py-8 text-center text-muted-foreground">Nenhum documento.</td></tr>
            ) : linhas.map((d) => (
              <tr key={d.rowId} className="border-t border-border hover:bg-secondary/40">
                {selection.length > 1 && <td className="px-2 py-1.5 font-bold text-amber-500">{nomes.get(d.projectId)}</td>}
                {COLUNAS.map((c) => (
                  <td key={c.key} className={cn("max-w-[320px] truncate px-2 py-1.5", c.num && "text-right tabular-nums")}
                    title={c.fmt ? c.fmt(d) : String(d[c.key] ?? "—")}>
                    {c.fmt ? c.fmt(d) : String(d[c.key] ?? "—")}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
