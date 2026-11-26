// Resultados › <fase salva> › Quantitativa | Qualitativa | Consistência. A fase vem da rota (:phaseId) e precisa existir em /phases:
// fase desconhecida mostra aviso — nunca é criada nem "inferida". Quantitativa e Qualitativa são reais; Consistência aguarda a relação.
import { useState } from "react";
import { Link, NavLink, useParams } from "react-router-dom";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { PADRAO_FASE, TELAS_FASE, rotaFase, type TelaFase } from "./plantRoutes";
import { GatesBloco } from "./PlantPhaseBiPage";
import "./plantPhaseBi.css";
import { PlantHeader, SemProjeto, usePlantPhases, usePlantQuality } from "./plantShared";
import { useMeasurement } from "./MeasurementView";
import { GvAviso, GvWrap } from "./plantGv";
import { FileiraConsistencia, FileiraQualitativa, FileiraQuantitativa } from "./plantGvBlocos";

export default function PlantFaseResultadoPage({ tela }: { tela: TelaFase }) {
  const { phaseId = "" } = useParams();
  const [consistencyRefresh, setConsistencyRefresh] = useState(0);
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const fases = usePlantPhases(projectIdAcc);
  const fase = fases.phases.find(f => f.id === phaseId) ?? null;
  const naoEncontrada = !!projectIdAcc && fases.loaded && !fase;
  const medicao = useMeasurement(tela === "quantitativa" ? projectIdAcc : "", fase?.id ?? "");
  const quality = usePlantQuality(tela === "qualitativa" ? projectIdAcc : "");
  const rotulo = TELAS_FASE.find(t => t.slug === tela)?.label ?? tela;
  const nome = fase ? fase.name || "(sem nome)" : "fase";

  return (
    <div className="space-y-6">
      <PlantHeader titulo={`${fase ? nome : "Fase"} · ${rotulo}`} subtitulo="Resultados por fase salva em Critérios de Medição e Fases."
        pj={pj} loading={fases.loading || medicao.loading || quality.loading}
        onRefresh={() => { fases.reload(); quality.reload(); setConsistencyRefresh(x=>x+1); }} />
      <SemProjeto pj={pj} />
      {fases.error && <GvWrap><GvAviso>{fases.error}</GvAviso></GvWrap>}
      {naoEncontrada && <GvWrap><GvAviso>Esta fase não existe (mais) neste projeto. Escolha uma fase no menu Resultados.</GvAviso></GvWrap>}

      {projectIdAcc && fase && (
        <GvWrap>
          <nav className="p3d-navfase" aria-label="Telas da fase">
            {TELAS_FASE.map(t => <NavLink key={t.slug} to={rotaFase(fase.id, t.slug)} end>{t.label}</NavLink>)}
            <Link to={PADRAO_FASE.replace(":phaseId", encodeURIComponent(fase.id))}>Painel executivo da fase ›</Link>
          </nav>
          {tela === "quantitativa" && (
            <FileiraQuantitativa titulo="QUANTITATIVA" subtitulo={<>Fase: <b>{nome}</b> · meta digitada × P&ID feito × modelado em 3D · meta da fase, sem somar as demais</>}
              data={medicao.data} loading={medicao.loading} error={medicao.error}
              extra={<GatesBloco fases={fases.phases} medicao={medicao.data} destaqueId={fase.id} />} />
          )}
          {tela === "qualitativa" && (
            <FileiraQualitativa titulo="QUALITATIVA" comProblemas
              subtitulo={<>Fase: <b>{nome}</b> · conformidade com os padrões cadastrados (calculada sobre o realizado global do projeto)</>}
              quality={quality.quality} loading={quality.loading} error={quality.error} />
          )}
          {tela === "consistencia" && <FileiraConsistencia titulo="CONSISTÊNCIA" subtitulo={<>Fase: <b>{nome}</b></>} projectIdAcc={projectIdAcc} phaseId={fase.id} refreshKey={consistencyRefresh} />}
        </GvWrap>
      )}
    </div>
  );
}
