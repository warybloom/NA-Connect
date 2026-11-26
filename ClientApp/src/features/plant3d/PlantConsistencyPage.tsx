// Consistência: linha P&ID × válvulas/instrumentos esperados × 3D confirmado.
// CONTRATO_03: só com relação linha→componente PROVADA por dados do DCF. Não existe endpoint de relação no contrato,
// então a tela mostra a pendência explícita e apenas contagens reais por categoria (sem razões entre elas).
import { AlertTriangle, Link2Off, Loader2 } from "lucide-react";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { fmtNum } from "@/components/plant3d/plantRulesApi";
import { AvisoStatus, Cartao, Estado, PlantHeader, SemProjeto, usePlantResults } from "./plantShared";

export default function PlantConsistencyPage() {
  const pj = usePlantProjects();
  const { results, loading, error, reload } = usePlantResults(pj.projectIdAcc);
  const foco = (results?.categories ?? []).filter(c => ["line", "valve", "inst"].includes(c.id));

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Resultados · Consistência" subtitulo="Linha × válvulas e instrumentos esperados × 3D confirmado."
        pj={pj} loading={loading} onRefresh={reload} />
      <SemProjeto pj={pj} />

      <Estado tom="amber" icon={<Link2Off className="h-6 w-6" />} titulo="Vínculo linha → válvulas/instrumentos não comprovado">
        A consistência por linha só é calculada quando os dados do DCF provarem a relação entre a linha e suas válvulas/instrumentos.
        Hoje essa relação não é fornecida pelo backend, e <b>não é inferida por coincidência de TAG</b>. Por isso nenhum percentual de presença
        por linha é exibido. Quando o backend expuser o vínculo verificado, esta tela passará a mostrar o peso proporcional de presença (até 100%) por linha.
      </Estado>

      {loading && !results && <div className="flex justify-center p-10"><Loader2 className="h-10 w-10 animate-spin text-primary" /></div>}
      {error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não foi possível carregar as contagens">{error}</Estado>}
      <AvisoStatus status={results?.status} />

      {results?.status === "ready" && (
        <Cartao titulo="Contagens reais disponíveis (sem relação entre elas)">
          {foco.length === 0 ? <p className="text-sm text-muted-foreground">Categorias de linhas, válvulas e instrumentos não retornadas pelo backend.</p> : (
            <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
              {foco.map(c => (
                <div key={c.id} className="rounded-lg border border-border/50 p-4">
                  <b>{c.label}</b>
                  <p className="mt-1 text-sm text-muted-foreground">P&ID: <b className="tabular-nums text-foreground">{fmtNum(c.pidCount)}</b> · 3D: <b className="tabular-nums text-foreground">{fmtNum(c.pipingCount)}</b></p>
                </div>
              ))}
            </div>
          )}
        </Cartao>
      )}
    </div>
  );
}
