// Plant 3D › Resumo (página única, substitui "Geral"): 3 fileiras só gráficas no estilo Gestão à Vista, sem listas de objetos.
//  Quantitativa — meta da ÚLTIMA fase (digitada no critério) × P&ID feito × modelado 3D. Fases NÃO são somadas.
//  Qualitativa  — GET /standards/quality (válido / inválido / não avaliado).
//  Consistência — 3 anéis "sem dados comprovados" enquanto a relação linha → componentes não for provada.
import { useState } from "react";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { formatarUtc } from "@/components/plant3d/plantApi";
import { ordenarFases, ultimaFase } from "@/components/plant3d/plantRulesApi";
import { PlantHeader, SemProjeto, usePlantPhases, usePlantQuality } from "./plantShared";
import { useMeasurement } from "./MeasurementView";
import { GvWrap } from "./plantGv";
import { FileiraConsistencia, FileiraQualitativa, FileiraQuantitativa } from "./plantGvBlocos";
import { GatesBloco } from "./PlantPhaseBiPage";

export default function PlantResumoPage() {
  const pj = usePlantProjects();
  const [consistencyRefresh, setConsistencyRefresh] = useState(0);
  const { projectIdAcc } = pj;
  const fases = usePlantPhases(projectIdAcc);
  const quality = usePlantQuality(projectIdAcc);
  const ultima = ultimaFase(fases.phases);
  const medicao = useMeasurement(projectIdAcc, ultima?.id ?? "");

  const semFase = !!projectIdAcc && fases.loaded && !ultima;
  const baseQualidade = formatarUtc(quality.quality?.updatedAtUtc);
  const nFases = ordenarFases(fases.phases).length;

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Plant 3D · Resumo" subtitulo="Visão executiva do projeto: quantitativa, qualitativa e consistência."
        pj={pj} loading={fases.loading || quality.loading || medicao.loading}
        onRefresh={() => { fases.reload(); quality.reload(); setConsistencyRefresh(x=>x+1); }} />
      <SemProjeto pj={pj} />

      {projectIdAcc && (
        <GvWrap>
          <FileiraQuantitativa titulo="QUANTITATIVA"
            subtitulo={ultima
              ? <>Meta da última fase: <b>{ultima.name || "(sem nome)"}</b> · {nFases} {nFases === 1 ? "fase" : "fases"} cadastrada{nFases === 1 ? "" : "s"}; metas de fases não são somadas</>
              : "Meta da última fase × P&ID feito × modelado em 3D"}
            data={medicao.data} loading={fases.loading || medicao.loading} error={fases.error || medicao.error} semFase={semFase}
            extra={<GatesBloco fases={fases.phases} medicao={medicao.data} />} />

          <FileiraQualitativa titulo="QUALITATIVA"
            subtitulo={<>Conformidade com os padrões cadastrados{baseQualidade ? <> · base de {baseQualidade}</> : null}</>}
            quality={quality.quality} loading={quality.loading} error={quality.error} />

          <FileiraConsistencia titulo="CONSISTÊNCIA" subtitulo="Presença de componentes esperados por linha" projectIdAcc={projectIdAcc} refreshKey={consistencyRefresh} />
        </GvWrap>
      )}
    </div>
  );
}
