// Medição de uma fase: metas digitadas × realidade filtrada (P&ID e 3D) vinda de GET /measurement. Sem métrica inventada.
import { useEffect, useState } from "react";
import { AlertTriangle, Loader2 } from "lucide-react";
import { descreverErro, isAbort } from "@/components/plant3d/plantApi";
import { PlantMeasurement, fmtNum, fmtPct, getMeasurement, percentual } from "@/components/plant3d/plantRulesApi";
import { Estado, Progresso } from "./plantShared";

export function useMeasurement(projectIdAcc: string, phaseId: string, versao = 0) {
  const [data, setData] = useState<PlantMeasurement | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    setData(null); setError("");
    if (!projectIdAcc || !phaseId) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const m = await getMeasurement(projectIdAcc, phaseId, abort.signal);
        if (!abort.signal.aborted) setData(m);
      } catch (e) {
        if (!isAbort(e)) setError(descreverErro(e, "Medição da fase"));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, phaseId, versao]);
  return { data, loading, error };
}

const StatusMsg: Record<string, string> = {
  unlinked: "Projeto sem vínculo com o Dashboard BIM: não há realidade para comparar com as metas.",
  "no-baseline": "Ainda não há versão-base completa no Dashboard BIM: a realidade (P&ID/3D) não está disponível.",
  "no-mapping": "Sem DE/PARA salvo: a realidade (P&ID/3D) não pode ser contada.",
};

export function MeasurementView({ projectIdAcc, phaseId, versao }: { projectIdAcc: string; phaseId: string; versao?: number }) {
  const { data, loading, error } = useMeasurement(projectIdAcc, phaseId, versao);
  if (loading) return <div className="flex justify-center p-8"><Loader2 className="h-8 w-8 animate-spin text-primary" /></div>;
  if (error) return <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Medição indisponível">{error}</Estado>;
  if (!data) return null;
  if (data.status && data.status !== "ready") {
    return <Estado tom="amber" titulo="Medição sem realidade para comparar">{StatusMsg[data.status] ?? `Status: ${data.status}.`}</Estado>;
  }
  if (data.categories.length === 0) {
    return <Estado tom="neutral" titulo="Fase sem metas">O backend não devolveu categorias para esta fase. Digite as metas por categoria na edição da fase.</Estado>;
  }
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      {data.categories.map(c => {
        const pPid = percentual(c.pid, c.expected);
        const pPip = percentual(c.piping, c.expected);
        return (
          <div key={c.id} className="glass-card space-y-3 rounded-xl border border-border/50 p-4">
            <div className="flex items-baseline justify-between gap-2">
              <h4 className="font-bold">{c.label}</h4>
              <span className="text-xs text-muted-foreground">Meta: <b className="tabular-nums text-foreground">{c.expected === null ? "não definida" : fmtNum(c.expected)}</b></span>
            </div>
            <div className="space-y-1">
              <div className="flex justify-between text-xs"><span className="font-semibold text-emerald-700 dark:text-emerald-400">P&ID · {fmtNum(c.pid)}</span><b className="tabular-nums">{fmtPct(pPid)}</b></div>
              <Progresso pct={pPid} cor="bg-emerald-500" rotulo={`${c.label} P&ID`} />
            </div>
            <div className="space-y-1">
              <div className="flex justify-between text-xs"><span className="font-semibold text-blue-700 dark:text-blue-400">3D · {fmtNum(c.piping)}</span><b className="tabular-nums">{fmtPct(pPip)}</b></div>
              <Progresso pct={pPip} cor="bg-blue-500" rotulo={`${c.label} 3D`} />
            </div>
            {c.expected === null && <p className="text-[11px] text-muted-foreground">Sem meta digitada para esta categoria: percentual não calculado.</p>}
          </div>
        );
      })}
    </div>
  );
}
