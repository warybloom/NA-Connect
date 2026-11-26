// Painel executivo por fase (F5): uma tela só com as 3 fileiras gráficas (Quantitativa · Qualitativa · Consistência) da fase escolhida,
// no estilo Gestão à Vista. Rota sugerida: PADRAO_FASE (/plant3d/resultados/fases/:phaseId). As fases são dinâmicas (cadastro em
// Critérios de Medição e Fases); fase desconhecida mostra aviso, nunca é criada. Metas de fases cumulativas NUNCA são somadas.
// Também exporta GatesBloco, usado pelo Resumo e por este painel: cada fase vira um gate com a sua própria meta × o realizado global.
import { Link, NavLink, useParams } from "react-router-dom";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { CATEGORIAS_META, fmtNum, fmtPct, ordenarFases, ultimaFase, type PlantMeasurement, type PlantPhase } from "@/components/plant3d/plantRulesApi";
import { TELAS_FASE, PADRAO_FASE, rotaFase } from "./plantRoutes";
import { PlantHeader, SemProjeto, usePlantPhases, usePlantQuality } from "./plantShared";
import { useMeasurement } from "./MeasurementView";
import { GvAviso, GvLegenda, GvWrap } from "./plantGv";
import { FileiraConsistencia, FileiraQualitativa, FileiraQuantitativa } from "./plantGvBlocos";
import { atingimentoPorFase, faseEmAndamento, larguraBarra } from "./plantPhaseBi";
import "./plantPhaseBi.css";

/** Gates por fase e categoria: barras P&ID e 3D = realizado global ÷ meta DA FASE (jamais somada). Sem meta/contagem → barra tracejada. */
export function GatesBloco({ fases, medicao, destaqueId }: { fases: PlantPhase[]; medicao: PlantMeasurement | null; destaqueId?: string }) {
  const lista = ordenarFases(fases);
  if (lista.length === 0) return null;
  const pronto = !!medicao && (!medicao.status || medicao.status === "ready");
  const cartoes = CATEGORIAS_META.map(c => {
    const real = pronto ? medicao?.categories.find(x => x.id === c.id) : undefined;
    const linhas = atingimentoPorFase(lista, c.id, real ? { pid: real.pid, piping: real.piping } : null);
    return { ...c, linhas, temMeta: linhas.some(l => l.meta !== null), atual: destaqueId ?? faseEmAndamento(linhas) };
  }).filter(c => c.temMeta);
  return (
    <div data-testid="gates-por-fase" className="space-y-3 pt-4">
      <p className="p3d-card-nota">
        Gates por fase — cada fase compara a <b>sua</b> meta com o realizado global atual do projeto. Metas de fases cumulativas não são somadas.
      </p>
      {!pronto && <GvAviso neutro>Realizado indisponível ({medicao?.status ?? "aguardando medição"}): somente as metas digitadas aparecem; nenhuma barra é desenhada.</GvAviso>}
      {cartoes.length === 0
        ? <GvAviso neutro>Nenhuma fase tem metas por categoria digitadas.</GvAviso>
        : (
          <div className="p3d-grade-3">
            {cartoes.map(c => (
              <div key={c.id} className="p3d-card">
                <h4>{c.label}</h4>
                <div className="p3d-gates-lista">
                  {c.linhas.map(l => (
                    <div key={l.phaseId} className={"p3d-gate" + (l.phaseId === c.atual ? " p3d-gate-atual" : "")}
                      role="img" aria-label={`${l.name}: meta ${fmtNum(l.meta)}; P&ID ${fmtPct(l.pidPct)}; 3D ${fmtPct(l.pipingPct)}`}>
                      <span className="p3d-gate-nome" title={l.name}>{l.name}</span>
                      <span className="p3d-gate-barras">
                        <span className={"p3d-gate-trilho" + (l.pidPct === null ? " p3d-gate-trilho-vazio" : "")} title={`P&ID: ${fmtPct(l.pidPct)}`}><i className="p3d-cor-pid" style={{ width: `${larguraBarra(l.pidPct)}%` }} /></span>
                        <span className={"p3d-gate-trilho" + (l.pipingPct === null ? " p3d-gate-trilho-vazio" : "")} title={`3D: ${fmtPct(l.pipingPct)}`}><i className="p3d-cor-3d" style={{ width: `${larguraBarra(l.pipingPct)}%` }} /></span>
                      </span>
                      <span className="p3d-gate-meta">{l.meta === null ? "sem meta" : <>meta {fmtNum(l.meta)}<br />3D {fmtPct(l.pipingPct)}</>}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      <GvLegenda itens={[{ cor: "p3d-cor-pid", rotulo: "P&ID feito ÷ meta da fase" }, { cor: "p3d-cor-3d", rotulo: "Modelado 3D ÷ meta da fase" }]} />
    </div>
  );
}

export default function PlantPhaseBiPage() {
  const { phaseId = "" } = useParams();
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const fases = usePlantPhases(projectIdAcc);
  const quality = usePlantQuality(projectIdAcc);
  // Sem :phaseId na rota → última fase (o gate mais avançado), igual ao Resumo.
  const fase = phaseId ? fases.phases.find(f => f.id === phaseId) ?? null : ultimaFase(fases.phases);
  const naoEncontrada = !!projectIdAcc && fases.loaded && !fase;
  const medicao = useMeasurement(projectIdAcc, fase?.id ?? "");
  const nome = fase ? fase.name || "(sem nome)" : "fase";

  return (
    <div className="space-y-6">
      <PlantHeader titulo={`${fase ? nome : "Fase"} · Painel executivo`}
        subtitulo="Quantitativa, qualitativa e consistência da fase em uma tela. Metas de fases não são somadas."
        pj={pj} loading={fases.loading || medicao.loading || quality.loading}
        onRefresh={() => { fases.reload(); quality.reload(); }} />
      <SemProjeto pj={pj} />
      {fases.error && <GvWrap><GvAviso>{fases.error}</GvAviso></GvWrap>}
      {naoEncontrada && <GvWrap><GvAviso>{phaseId ? "Esta fase não existe (mais) neste projeto." : "Nenhuma fase cadastrada."} Cadastre ou escolha uma fase em Critérios de Medição e Fases.</GvAviso></GvWrap>}

      {projectIdAcc && fase && (
        <GvWrap>
          <nav className="p3d-navfase" aria-label="Telas da fase">
            {ordenarFases(fases.phases).map(f => (
              <NavLink key={f.id} to={PADRAO_FASE.replace(":phaseId", encodeURIComponent(f.id))} end>{f.name || "(sem nome)"}</NavLink>
            ))}
            {TELAS_FASE.map(t => <Link key={t.slug} to={rotaFase(fase.id, t.slug)}>{t.label} ›</Link>)}
          </nav>
          <FileiraQuantitativa titulo="QUANTITATIVA" subtitulo={<>Fase: <b>{nome}</b> · meta digitada × P&ID feito × modelado em 3D</>}
            data={medicao.data} loading={medicao.loading} error={medicao.error}
            extra={<GatesBloco fases={fases.phases} medicao={medicao.data} destaqueId={fase.id} />} />
          <FileiraQualitativa titulo="QUALITATIVA" comProblemas
            subtitulo={<>Fase: <b>{nome}</b> · conformidade com os padrões cadastrados (sobre o realizado global do projeto)</>}
            quality={quality.quality} loading={quality.loading} error={quality.error} />
          <FileiraConsistencia titulo="CONSISTÊNCIA" subtitulo={<>Fase: <b>{nome}</b></>} />
        </GvWrap>
      )}
    </div>
  );
}
