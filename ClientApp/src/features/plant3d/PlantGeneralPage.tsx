// Resultados / Geral: BI do projeto (totais reais de /results) + medição da fase dinâmica selecionada.
import { AlertTriangle, Loader2 } from "lucide-react";
import { Link } from "react-router-dom";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { fmtNum, fmtPct } from "@/components/plant3d/plantRulesApi";
import { formatarUtc } from "@/components/plant3d/plantApi";
import {
  AvisoStatus, BarraEmpilhada, Cartao, Estado, Kpi, Legenda, PlantHeader, Rosca, SemProjeto, SeletorFase,
  resolverFase, useFaseSelecionada, usePlantPhases, usePlantResults,
} from "./plantShared";
import { MeasurementView } from "./MeasurementView";

export default function PlantGeneralPage() {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const { results, loading, error, reload } = usePlantResults(projectIdAcc);
  const fases = usePlantPhases(projectIdAcc);
  const [preferida, setPreferida] = useFaseSelecionada(projectIdAcc);
  const fase = resolverFase(fases.phases, preferida);

  const t = results?.status === "ready" ? results.totals : null;
  const base = formatarUtc(results?.updatedAtUtc);
  const cobertura = t && t.pid > 0 ? (t.matched / t.pid) * 100 : null;

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Resultados · Geral" subtitulo="Visão consolidada do projeto: P&ID × 3D e avanço por fase."
        pj={pj} loading={loading} onRefresh={() => { reload(); fases.reload(); }}
        extra={results?.status === "ready" && <p className="text-xs text-muted-foreground">Base atualizada em: <b className="text-foreground">{base ?? "data não informada pelo backend"}</b></p>} />
      <SemProjeto pj={pj} />
      {loading && !results && <div className="flex justify-center p-10"><Loader2 className="h-10 w-10 animate-spin text-primary" /></div>}
      {error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não foi possível carregar os resultados">{error}</Estado>}
      <AvisoStatus status={results?.status} />

      {t && results && (
        <>
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <Kpi titulo="Itens válidos no P&ID" valor={fmtNum(t.pid)} cor="border-l-emerald-500" />
            <Kpi titulo="Itens válidos no 3D" valor={fmtNum(t.piping)} cor="border-l-blue-500" />
            <Kpi titulo="Pareadas por TAG" valor={fmtNum(t.matched)} nota={cobertura === null ? undefined : `${fmtPct(cobertura)} das linhas do P&ID`} cor="border-l-emerald-500" />
            <Kpi titulo="Pendências" valor={fmtNum(t.missing3d + t.extra3d + t.divergent)} nota="faltam no 3D + sobram + divergentes" cor="border-l-amber-500" />
          </div>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
            <Cartao titulo="Situação geral das TAGs" className="lg:col-span-1">
              <div className="flex flex-wrap items-center gap-4">
                <Rosca centro={fmtPct(cobertura)} sub="pareadas / P&ID" partes={[
                  { valor: t.matched, cor: "bg-emerald-500", rotulo: "Pareadas" }, { valor: t.divergent, cor: "bg-amber-500", rotulo: "Divergentes" },
                  { valor: t.missing3d, cor: "bg-red-500", rotulo: "Faltam no 3D" }, { valor: t.extra3d, cor: "bg-orange-500", rotulo: "Sobram no 3D" }]} />
                <div className="min-w-[160px] flex-1 space-y-1.5">
                  <Legenda rotulo="Pareadas" valor={fmtNum(t.matched)} cor="bg-emerald-500" />
                  <Legenda rotulo="Divergentes" valor={fmtNum(t.divergent)} cor="bg-amber-500" />
                  <Legenda rotulo="Faltam no 3D" valor={fmtNum(t.missing3d)} cor="bg-red-500" />
                  <Legenda rotulo="Sobram no 3D" valor={fmtNum(t.extra3d)} cor="bg-orange-500" />
                </div>
              </div>
            </Cartao>

            <Cartao titulo="Por categoria (P&ID × 3D)" className="lg:col-span-2">
              {results.categories.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma categoria retornada.</p> : (
                <div className="space-y-3">
                  {results.categories.map(c => (
                    <div key={c.id} className="space-y-1">
                      <div className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                        <b>{c.label}</b>
                        <span className="text-xs text-muted-foreground">P&ID {fmtNum(c.pidCount)} · 3D {fmtNum(c.pipingCount)}</span>
                      </div>
                      <BarraEmpilhada partes={[
                        { valor: c.matched, cor: "bg-emerald-500", rotulo: "Pareadas" }, { valor: c.divergent, cor: "bg-amber-500", rotulo: "Divergentes" },
                        { valor: c.missing3d, cor: "bg-red-500", rotulo: "Faltam no 3D" }, { valor: c.extra3d, cor: "bg-orange-500", rotulo: "Sobram no 3D" }]} />
                    </div>
                  ))}
                </div>
              )}
            </Cartao>
          </div>
        </>
      )}

      {projectIdAcc && (
        <section className="space-y-3">
          {fases.phases.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              {fases.phases.map((f, index) => (
                <Link key={f.id} to={`/plant3d/resultados/geral/fase/${encodeURIComponent(f.id)}`}
                  className="rounded-xl border border-border/60 bg-card p-4 transition-colors hover:border-primary/70 hover:bg-primary/5">
                  <span className="text-xs font-bold text-primary">Tela {index === 0 ? "1" : `1.${index}`}</span>
                  <p className="mt-1 font-semibold text-foreground">{f.name}</p>
                  <p className="text-xs text-muted-foreground">Abrir medição e metas desta fase</p>
                </Link>
              ))}
            </div>
          )}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h3 className="text-lg font-bold">Avanço por fase{fase ? <span className="text-primary"> · {fase.name || "(sem nome)"}</span> : null}</h3>
            {fases.phases.length > 0 && <SeletorFase phases={fases.phases} valor={fase?.id ?? ""} onChange={setPreferida} />}
          </div>
          {fases.loading && <div className="flex justify-center p-6"><Loader2 className="h-8 w-8 animate-spin text-primary" /></div>}
          {fases.error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Fases indisponíveis">{fases.error}</Estado>}
          {fases.loaded && fases.phases.length === 0 && (
            <Estado tom="neutral" titulo="Nenhuma fase cadastrada">Cadastre fases e metas em Resultados › Fase. Nenhuma meta é assumida automaticamente.</Estado>
          )}
          {fase && <MeasurementView projectIdAcc={projectIdAcc} phaseId={fase.id} />}
          {fase && <p className="text-xs text-muted-foreground">A fase escolhida aqui vale para todas as telas Plant 3D deste projeto neste navegador.</p>}
        </section>
      )}
    </div>
  );
}
