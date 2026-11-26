// Fileiras gráficas (estilo Gestão à Vista) compartilhadas pelo Resumo e pelas telas de fase.
// Regra: nenhum valor é fabricado. Dado ausente → "—" / "não avaliado" / "sem dados comprovados"; erro de rede → aviso, nunca 0%.
import { useEffect, useState, type ReactNode } from "react";
import { plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { Link } from "react-router-dom";
import type { PlantMeasurement } from "@/components/plant3d/plantRulesApi";
import { fmtNum, fmtPct, percentual } from "@/components/plant3d/plantRulesApi";
import { percentualValidos, type StandardsQuality } from "@/components/plant3d/plantStandardsLogic";
import { GvAnel, GvAviso, GvFileira, GvGauge, GvLegenda, GvPilha } from "./plantGv";
import { PLANT_ROTAS } from "./plantRoutes";

const MSG_MEDICAO: Record<string, string> = {
  unlinked: "Projeto sem vínculo com o Dashboard BIM: não há realidade (P&ID/3D) para comparar com a meta.",
  "no-baseline": "Ainda não há versão-base completa no Dashboard BIM: P&ID e 3D não estão disponíveis.",
  "no-mapping": "Sem DE/PARA salvo: P&ID e 3D não podem ser contados.",
};

const MSG_QUALIDADE: Record<string, string> = {
  unlinked: "Projeto sem vínculo com o Dashboard BIM: nenhum item foi avaliado.",
  "no-baseline": "Aguardando versão-base do Dashboard BIM: nenhum item foi avaliado.",
  "no-mapping": "Sem DE/PARA salvo: nenhum item foi avaliado.",
  "no-standards": "Nenhum padrão cadastrado: todos os itens permanecem NÃO AVALIADOS.",
};

type Carga = { loading: boolean; error: string };

const Carregando = ({ children }: { children: ReactNode }) => <GvAviso neutro>{children}</GvAviso>;

/** Quantitativa: meta digitada × P&ID feito × modelado em 3D, por categoria. */
export function FileiraQuantitativa({ titulo, subtitulo, data, loading, error, semFase, extra }: Carga & {
  titulo: string; subtitulo?: ReactNode; data: PlantMeasurement | null; semFase?: boolean; extra?: ReactNode;
}) {
  let corpo: ReactNode;
  if (semFase) {
    corpo = <GvAviso neutro>Nenhuma fase cadastrada: não há meta para comparar. Cadastre em <Link className="gv-link" to={PLANT_ROTAS.configuracao.criterios}>Critérios de Medição e Fases</Link>.</GvAviso>;
  } else if (loading) corpo = <Carregando>Carregando medição…</Carregando>;
  else if (error) corpo = <GvAviso>{error}</GvAviso>;
  else if (!data) corpo = <Carregando>Aguardando dados.</Carregando>;
  else if (data.status && data.status !== "ready") corpo = <GvAviso>{MSG_MEDICAO[data.status] ?? `Medição indisponível (${data.status}).`}</GvAviso>;
  else if (data.categories.length === 0) corpo = <GvAviso neutro>A fase não tem metas por categoria. Digite as metas em Critérios de Medição e Fases.</GvAviso>;
  else {
    corpo = (
      <>
        <div className="p3d-grade-3">
          {data.categories.map(c => (
            <div key={c.id} className="p3d-card">
              <h4>{c.label}</h4>
              <p className="p3d-card-nota">Meta (digitada no critério): <b>{c.expected === null ? "não definida" : fmtNum(c.expected)}</b></p>
              <div className="p3d-duplo">
                <GvGauge title="P&ID feito" value={percentual(c.pid, c.expected)}
                  fracao={c.pid === null ? "sem contagem" : `${fmtNum(c.pid)} / ${c.expected === null ? "—" : fmtNum(c.expected)}`} />
                <GvGauge title="Modelado 3D" value={percentual(c.piping, c.expected)}
                  fracao={c.piping === null ? "sem contagem" : `${fmtNum(c.piping)} / ${c.expected === null ? "—" : fmtNum(c.expected)}`} />
              </div>
              {c.expected === null && <p className="p3d-card-nota">Sem meta: percentual não calculado.</p>}
            </div>
          ))}
        </div>
        <GvLegenda itens={[{ cor: "p3d-cor-pid", rotulo: "P&ID feito" }, { cor: "p3d-cor-3d", rotulo: "Modelado em 3D" }]} />
      </>
    );
  }
  return <GvFileira id="fileira-quantitativa" titulo={titulo} subtitulo={subtitulo}>{corpo}{extra}</GvFileira>;
}

const COR_PROBLEMA = ["p3d-cor-erro", "p3d-cor-3d", "p3d-cor-pid", "p3d-cor-previsto", "p3d-cor-nd"];

/** Qualitativa: GET /standards/quality. "Não avaliado" é uma fatia explícita; sem avaliados → "—", nunca 100%. */
export function FileiraQualitativa({ titulo, subtitulo, quality, loading, error, comProblemas }: Carga & {
  titulo: string; subtitulo?: ReactNode; quality: StandardsQuality | null; comProblemas?: boolean;
}) {
  let corpo: ReactNode;
  if (loading) corpo = <Carregando>Carregando qualidade por padrões…</Carregando>;
  else if (error) corpo = <GvAviso>Indicador indisponível, sem valores exibidos. {error}</GvAviso>;
  else if (!quality) corpo = <Carregando>Aguardando dados.</Carregando>;
  else if (quality.status !== "ready") {
    corpo = (
      <>
        <GvAviso>{MSG_QUALIDADE[quality.status] ?? `Qualidade indisponível (${quality.status}).`}</GvAviso>
        {quality.status === "no-standards" && <p className="p3d-card-nota">Cadastre os padrões em <Link className="gv-link" to={PLANT_ROTAS.configuracao.catalogo}>Catálogo de Padrões e Validação</Link>.</p>}
      </>
    );
  } else if (quality.categories.length === 0) corpo = <GvAviso neutro>O backend não retornou categorias avaliadas.</GvAviso>;
  else {
    corpo = (
      <>
        <div className="p3d-grade-3">
          {quality.categories.map(c => {
            const p = percentualValidos(c);
            const semAvaliados = c.valid + c.invalid === 0;
            return (
              <div key={c.id} className="p3d-card">
                <GvAnel titulo={c.label}
                  partes={[{ valor: c.valid, tom: "ok", rotulo: "Válidos" }, { valor: c.invalid, tom: "erro", rotulo: "Inválidos" }, { valor: c.unchecked, tom: "nd", rotulo: "Não avaliados" }]}
                  centro={p === null ? "—" : fmtPct(p)} sub={p === null ? "não avaliado" : "dos avaliados"}
                  status={semAvaliados ? "Não avaliado" : undefined}
                  legenda={`${fmtNum(c.valid)} válidos · ${fmtNum(c.invalid)} inválidos · ${fmtNum(c.unchecked)} não avaliados (de ${fmtNum(c.total)})`} />
                {comProblemas && c.issues.length > 0 && (
                  <>
                    <GvPilha rotulo={`Tipos de problema — ${c.label}`}
                      partes={c.issues.map((i, k) => ({ valor: i.count, cor: COR_PROBLEMA[k % COR_PROBLEMA.length], rotulo: i.kind }))} />
                    <GvLegenda itens={c.issues.map((i, k) => ({ cor: COR_PROBLEMA[k % COR_PROBLEMA.length], rotulo: i.kind, valor: fmtNum(i.count) }))} />
                  </>
                )}
              </div>
            );
          })}
        </div>
        <GvLegenda itens={[{ cor: "p3d-cor-pid", rotulo: "Válido" }, { cor: "p3d-cor-erro", rotulo: "Inválido" }, { cor: "p3d-cor-nd", rotulo: "Não avaliado" }]} />
      </>
    );
  }
  return <GvFileira id="fileira-qualitativa" titulo={titulo} subtitulo={subtitulo}>{corpo}</GvFileira>;
}

type ConsistencySummary = {
  status: string;
  message: string;
  totalLines: number;
  verifiedLines: number;
  weightedEvaluated: number;
  fullyModelled: number;
  valvesPending: number;
  instrumentsPending: number;
  unverified: number;
};
const porcento = (n: number, d: number) => d > 0 ? fmtPct(100 * n / d) : "—";

/**
 * Indicadores executivos ligados a /consistency (projeto real / fase real).
 * Estatísticas são calculadas no backend sobre o projeto COMPLETO, e não
 * somente sobre a página de linhas retornada para detalhamento.
 */
export function FileiraConsistencia({ titulo, subtitulo, projectIdAcc = "", phaseId = "", refreshKey = 0 }: {
  titulo: string; subtitulo?: ReactNode; projectIdAcc?: string; phaseId?: string; refreshKey?: number;
}) {
  const [dados, setDados] = useState<ConsistencySummary | null>(null);
  const [erro, setErro] = useState("");
  const [carregando, setCarregando] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    setDados(null);
    setErro("");
    if (!projectIdAcc) return;
    const controller = new AbortController();
    setCarregando(true);
    const query = phaseId ? `?phaseId=${encodeURIComponent(phaseId)}&limit=1` : "?limit=1";
    plantRequest<ConsistencySummary>(`${projetoPath(projectIdAcc)}/consistency${query}`,
      { signal: controller.signal })
      .then(result => { if (!controller.signal.aborted) setDados(result); })
      .catch(err => {
        if (!controller.signal.aborted) setErro(err instanceof Error ? err.message : "Consistência indisponível");
      })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [projectIdAcc, phaseId, refreshKey, revision]);

  // Polling somente da assinatura leve do histórico: recalcular quando o DCF
  // mudar, sem reconstruir centenas de milhares de linhas a cada 15 s.
  useEffect(() => {
    if (!projectIdAcc) return;
    let active = true;
    let oldSignature: string | null = null;
    const check = async () => {
      if (document.hidden) return;
      try {
        const res = await plantRequest<{
          pnId?: { completedVersions?: number; latestCompletedAtUtc?: string };
          piping?: { completedVersions?: number; latestCompletedAtUtc?: string };
          pnIdDelta?: { completedVersions?: number; latestCompletedAtUtc?: string };
          pipingDelta?: { completedVersions?: number; latestCompletedAtUtc?: string };
          filtersUpdatedAtUtc?: string | null;
        }>(`${projetoPath(projectIdAcc)}/status`);
        if (!active) return;
        const signature = JSON.stringify([res.pnId, res.piping, res.pnIdDelta, res.pipingDelta, res.filtersUpdatedAtUtc]);
        if (oldSignature !== null && signature !== oldSignature) setRevision(x => x + 1);
        oldSignature = signature;
      } catch { /* manter último estado verificado e permitir Atualizar manualmente */ }
    };
    void check();
    const interval = window.setInterval(check, 15000);
    return () => { active = false; window.clearInterval(interval); };
  }, [projectIdAcc]);

  const pronto = dados?.status === "ready";
  const avaliado = pronto ? dados.verifiedLines : 0;
  const peso = pronto ? dados.weightedEvaluated : 0;
  const full = pronto ? dados.fullyModelled : 0;
  const valves = pronto ? dados.valvesPending : 0;
  const inst = pronto ? dados.instrumentsPending : 0;

  const cards = [
    {
      label: "Linhas 100% completas", n: full, d: peso,
      color: "ok" as const, opposite: "nd" as const,
      caption: "com avanço ponderado calculado",
    },
    {
      label: "Linhas com válvulas pendentes", n: valves, d: avaliado,
      color: "erro" as const, opposite: "ok" as const,
      caption: "das linhas com vínculos comprovados",
    },
    {
      label: "Linhas com instrumentos pendentes", n: inst, d: avaliado,
      color: "erro" as const, opposite: "ok" as const,
      caption: "das linhas com vínculos comprovados",
    },
  ];

  return (
    <GvFileira id="fileira-consistencia" titulo={titulo} subtitulo={subtitulo}>
      <div className="p3d-grade-3">
        {cards.map(item => (
          <div key={item.label} className="p3d-card">
            <GvAnel titulo={item.label}
              partes={item.d > 0 ? [
                { valor: item.n, tom: item.color, rotulo: item.label },
                { valor: Math.max(0, item.d - item.n), tom: item.opposite, rotulo: "Demais linhas avaliadas" },
              ] : []}
              centro={item.d > 0 ? porcento(item.n, item.d) : "—"}
              sub={item.d > 0 ? `${fmtNum(item.n)} de ${fmtNum(item.d)}` : "aguardando relação"}
              status={item.d === 0 ? "Sem dados comprovados" : undefined}
              legenda={item.d > 0 ? item.caption : undefined} />
          </div>
        ))}
      </div>
      {carregando && !dados && <GvAviso neutro>Verificando relações de linha…</GvAviso>}
      {erro && <GvAviso>Indicador indisponível: {erro}</GvAviso>}
      {!carregando && !erro && dados?.status !== "ready" && (
        <GvAviso neutro>{dados?.message || "Aguardando vínculo comprovado."} Nenhum percentual será inferido.
          {" "}Detalhes em <Link className="gv-link" to={PLANT_ROTAS.resultados.consistencia}>Resultados › Consistência</Link>.
        </GvAviso>
      )}
      {pronto && dados.unverified > 0 && (
        <GvAviso neutro>{fmtNum(dados.unverified)} de {fmtNum(dados.totalLines)} linhas ainda sem associação comprovada.
          {" "}As pendências de válvulas e instrumentos podem ocorrer na mesma linha.
        </GvAviso>
      )}
    </GvFileira>
  );
}
