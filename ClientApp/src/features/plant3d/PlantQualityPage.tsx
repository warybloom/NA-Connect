// Quantitativa e Qualidade: contagens P&ID × 3D por categoria e indicadores de qualidade, todos derivados de /results.
import { AlertTriangle, Loader2 } from "lucide-react";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { fmtNum, fmtPct } from "@/components/plant3d/plantRulesApi";
import { AvisoStatus, Cartao, Estado, Kpi, PlantHeader, SemProjeto, usePlantResults } from "./plantShared";

const pct = (n: number, d: number) => (d > 0 ? (n / d) * 100 : null);

export default function PlantQualityPage() {
  const pj = usePlantProjects();
  const { results, loading, error, reload } = usePlantResults(pj.projectIdAcc);
  const t = results?.status === "ready" ? results.totals : null;
  const maxContagem = Math.max(1, ...(results?.categories ?? []).flatMap(c => [c.pidCount, c.pipingCount]));
  const cab = "px-3 py-2 text-right font-bold";

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Resultados · Quantitativa e Qualidade" subtitulo="Contagens reais por categoria (já com os filtros incluir/excluir salvos) e indicadores de qualidade."
        pj={pj} loading={loading} onRefresh={reload} />
      <SemProjeto pj={pj} />
      {loading && !results && <div className="flex justify-center p-10"><Loader2 className="h-10 w-10 animate-spin text-primary" /></div>}
      {error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não foi possível carregar os resultados">{error}</Estado>}
      <AvisoStatus status={results?.status} />

      {t && results && (
        <>
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <Kpi titulo="Pareamento (P&ID)" valor={fmtPct(pct(t.matched, t.pid))} nota="pareadas ÷ linhas do P&ID" cor="border-l-emerald-500" />
            <Kpi titulo="Divergência" valor={fmtPct(pct(t.divergent, t.matched + t.divergent))} nota="divergentes ÷ TAGs nos dois lados" cor="border-l-amber-500" />
            <Kpi titulo="Sem TAG P&ID" valor={fmtNum(t.taglessPid)} nota={`${fmtPct(pct(t.taglessPid, t.pid))} do P&ID`} cor="border-l-slate-500" />
            <Kpi titulo="Sem TAG 3D" valor={fmtNum(t.tagless3d)} nota={`${fmtPct(pct(t.tagless3d, t.piping))} do 3D`} cor="border-l-slate-500" />
          </div>

          <Cartao titulo="Quantitativo por categoria — P&ID × 3D">
            {results.categories.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma categoria retornada.</p> : (
              <div className="space-y-4">
                {results.categories.map(c => (
                  <div key={c.id} className="space-y-1">
                    <b className="text-sm">{c.label}</b>
                    {([["P&ID", c.pidCount, "bg-emerald-500"], ["3D", c.pipingCount, "bg-blue-500"]] as const).map(([lado, v, cor]) => (
                      <div key={lado} className="flex items-center gap-2 text-xs">
                        <span className="w-9 shrink-0 text-muted-foreground">{lado}</span>
                        <div className="h-3 flex-1 overflow-hidden rounded bg-secondary"><div className={`h-full ${cor}`} style={{ width: `${(v / maxContagem) * 100}%` }} /></div>
                        <b className="w-14 text-right tabular-nums">{fmtNum(v)}</b>
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            )}
          </Cartao>

          <div className="glass-card overflow-hidden rounded-xl border border-border/50">
            <div className="border-b border-border/50 bg-card/30 px-4 py-3"><h3 className="font-bold">Qualidade por categoria</h3></div>
            <div className="custom-scrollbar overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-background/60 text-[10px] uppercase text-muted-foreground">
                  <tr><th className="px-3 py-2 text-left font-bold">Categoria</th><th className={cab}>Pareamento</th><th className={cab}>Divergentes</th><th className={cab}>Falta 3D</th><th className={cab}>Sobra 3D</th><th className={cab}>Sem TAG P&ID</th><th className={cab}>Sem TAG 3D</th></tr>
                </thead>
                <tbody className="divide-y divide-border/30 tabular-nums">
                  {results.categories.map(c => (
                    <tr key={c.id}>
                      <td className="px-3 py-2 font-semibold">{c.label}</td>
                      <td className="px-3 py-2 text-right">{fmtPct(pct(c.matched, c.pidCount))}</td>
                      {[c.divergent, c.missing3d, c.extra3d, c.taglessPid, c.tagless3d].map((v, i) => <td key={i} className="px-3 py-2 text-right">{fmtNum(v)}</td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
