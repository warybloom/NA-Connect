/**
 * Curva S documental de qualquer projeto (LDP Tipo 1, Tipo 2 ou sem LDP),
 * na IDENTIDADE VISUAL exata do CHS001. Nunca simula cronograma WBX.
 *
 * Dados: GET /api/reports/curva-s; barras = incrementos por período,
 * linhas = acumulados. Gráfico e estilo: o MESMO GraficoCombo e visual
 * PBIR p03 da Gestão à Vista Tipo 2. A linha REPROGRAMADA é a série
 * cumulativa real da LDP, preservando a mesma base/denominador da baseline.
 */
import { useEffect, useMemo, useState } from "react";
import { getJson, ApiError } from "@/lib/api";
import { addDays, day, formatData, limitedMap, weekStart } from "@/components/pmo/pmoData";
import { consolidarCurvaS, type CurvaSPonto, type CurvaSReport, type PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { CurvaSIdentidadeVisual } from "./CurvaSIdentidadeVisual";
import { useLdpPorProjeto } from "../analytics/useLdpPorProjeto";
import tipo30 from "./generated/p03.json";
import type { VisualPbi } from "./pbi/modelo";
import type { DadosVisual, PontoCategoria } from "./dados/tipos";

type Metrica = "fisico" | "fisico-cronograma" | "hh" | "custo" | "documentos";
type Periodo = "diario" | "semanal" | "mensal";
const METRICAS: readonly { key: Metrica; label: string; planejado: keyof CurvaSPonto; realizado: keyof CurvaSPonto; reprogramado: keyof CurvaSPonto; unidade: string }[] = [
  { key: "fisico-cronograma", label: "Avanço por cronograma", planejado: "previstoHh", realizado: "realizadoHh", reprogramado: "reprogramadoHh", unidade: "%" },
  { key: "fisico", label: "Avanço por status de documentos", planejado: "previstoHh", realizado: "realizadoHh", reprogramado: "reprogramadoHh", unidade: "%" },
  { key: "hh", label: "HH", planejado: "previstoHh", realizado: "realizadoHh", reprogramado: "reprogramadoHh", unidade: "h" },
  { key: "custo", label: "Custo", planejado: "previstoCusto", realizado: "realizadoCusto", reprogramado: "reprogramadoCusto", unidade: "R$" },
  { key: "documentos", label: "Emissões (docs)", planejado: "previstoDocumentos", realizado: "emitidosDocumentos", reprogramado: "reprogramadoDocumentos", unidade: "docs" },
];

// Mantém TODA configuração visual do PBIX, incluindo cores, fontes, grade,
// disposição, barras e as TRÊS linhas acumuladas. Nunca recalcular baseline.
const ORIGINAL = (tipo30.visuais as unknown as VisualPbi[]).find(v => v.type === "lineClusteredColumnComboChart"
  && v.y > 350 && v.y < 750);
if (!ORIGINAL || ORIGINAL.fields?.Y?.length !== 2 || ORIGINAL.fields?.Y2?.length !== 3)
  throw new Error("Visual oficial do BI CHS001 mudou: revisão de paridade necessária.");

const VISUAL_DOC: VisualPbi = {
  ...ORIGINAL,
  fields: {
    ...ORIGINAL.fields,
    Y: ORIGINAL.fields.Y.map((f, i) => ({
      ...f, displayName: i === 0 ? "% Baseline · período" : "% Emitido · período",
    })),
    Y2: [
      ...ORIGINAL.fields.Y2.map((f, i) => ({
        ...f, displayName: i === 0 ? "% Previsto (baseline)" : i === 1 ? "% Reprogramado acumulado" : "% Emitido acumulado",
      })),
      {
        ...ORIGINAL.fields.Y2[2],
        entity: "CurvaS",
        prop: "AvancoFisicoAtual",
        queryRef: "CurvaS.AvancoFisicoAtual",
        displayName: "% Avanço físico atual (somente referência)",
      },
    ],
  },
};

// Modo principal: planejamento por HH + APENAS o avanço físico oficial de hoje.
// A linha de emissões (78%) não aparece como avanço físico: não existe histórico físico confiável.
const VISUAL_FISICO: VisualPbi = {
  ...VISUAL_DOC,
  fields: { ...VISUAL_DOC.fields,
    Y: [VISUAL_DOC.fields!.Y![0]],
    Y2: [VISUAL_DOC.fields!.Y2![0], VISUAL_DOC.fields!.Y2![1], VISUAL_DOC.fields!.Y2![3]],
  },
};

const ref = (f: { queryRef?: string; entity?: string; prop?: string }) =>
  f.queryRef ?? `${f.entity}.${f.prop}`;
const Y = VISUAL_DOC.fields!.Y;
const Y2 = VISUAL_DOC.fields!.Y2;
const refs = { planBar: ref(Y[0]), realBar: ref(Y[1]), planLine: ref(Y2[0]), replanLine: ref(Y2[1]), realLine: ref(Y2[2]),
  fisicoAtual: ref(Y2[3]) };

export function preparar(report: CurvaSReport, metrica: Metrica, periodo: Periodo): DadosVisual {
  const campo = METRICAS.find(m => m.key === metrica)!;
  const pontos = report.pontos.map(p => ({ ...p, iso: day(p.data) }))
    .sort((a,b) => (a.iso ?? "").localeCompare(b.iso ?? ""));
  if (pontos.length < 2 || pontos.some(p => !p.iso))
    return { estado: "pendente", motivo: "Curva S sem datas suficientes na fonte documental. Nenhuma evolução foi simulada." };

  const refData = day(report.dataReferencia);
  if (!refData) return { estado: "pendente", motivo: "Data de referência do relatório ausente." };
  const primeiro = pontos[0].iso!;
  const ultimo = pontos[pontos.length - 1].iso!;
  const ate = ultimo > refData ? ultimo : refData;
  const chave = (d: string) => periodo === "diario" ? d : periodo === "semanal" ? weekStart(d, 0) : d.slice(0, 7) + "-01";
  const proximo = (d: string) => periodo === "diario" ? addDays(d, 1) : periodo === "semanal" ? addDays(d, 7)
    : (() => { const x = new Date(d + "T12:00:00Z"); x.setUTCMonth(x.getUTCMonth() + 1, 1); return x.toISOString().slice(0, 10); })();

  const denominador = Number(pontos[pontos.length - 1][campo.planejado] ?? 0);
  if (!(denominador > 0))
    return { estado: "pendente", motivo: `Não existe total previsto de ${campo.label} maior que zero na LDP; percentual indisponível.` };
  const pct = (n: number) => n / denominador; // O motor PBIX usa 0..1, não 0..100.
  const resultado: PontoCategoria[] = [];
  let i = 0;
  let anterior: typeof pontos[number] | undefined;
  let prevAcumulado = 0;
  let realAcumulado = 0;
  for (let inicio = chave(primeiro); inicio <= chave(ate); inicio = proximo(inicio)) {
    const fim = addDays(proximo(inicio), -1);
    while (i < pontos.length && pontos[i].iso! <= fim) { anterior = pontos[i]; i++; }
    const previsto = Number(anterior?.[campo.planejado] ?? 0);
    // O relatório usa as datas de emissão. Sem emissão até a referência não vira dado de Timesheet.
    const realizado = metrica === "fisico" || metrica === "fisico-cronograma" || inicio > refData ? null : Number(anterior?.[campo.realizado] ?? 0);
    const reprogramadoBruto = anterior?.[campo.reprogramado];
    const reprogramado = reprogramadoBruto == null ? null : Number(reprogramadoBruto);
    const colPrev = Math.max(0, previsto - prevAcumulado);
    const colReal = realizado === null ? null : Math.max(0, realizado - realAcumulado);
    prevAcumulado = previsto;
    if (realizado !== null) realAcumulado = realizado;
    resultado.push({
      categoria: formatData(inicio),
      valores: {
        [refs.planBar]: pct(colPrev),
        [refs.realBar]: colReal === null ? null : pct(colReal),
        [refs.planLine]: pct(previsto),
        [refs.replanLine]: reprogramado === null ? null : pct(reprogramado),
        [refs.realLine]: realizado === null ? null : pct(realizado),
        // Avanço físico não é a taxa de documentos emitidos. Só há fotografia
        // oficial ATUAL (PmoAvancoRules), não série histórica certificada.
        [refs.fisicoAtual]: (metrica === "fisico" || metrica === "fisico-cronograma") && inicio <= refData && refData <= fim
          ? (metrica === "fisico-cronograma" ? report.avancoCronograma?.fisico ?? null : report.avanco?.fisico ?? null) === null
            ? null : (metrica === "fisico-cronograma" ? report.avancoCronograma!.fisico! : report.avanco!.fisico!) / 100
          : null,
      },
    });
  }
  return {
    estado: "ok", tipo: "pontos", pontos: resultado,
    formatos: {
      [refs.planBar]: "percentual", [refs.realBar]: "percentual",
      [refs.planLine]: "percentual", [refs.replanLine]: "percentual", [refs.realLine]: "percentual",
      [refs.fisicoAtual]: "percentual",
    },
  };
}

export function CurvaSDocumentalBi({ selection }: { selection: PmoSelecionado[] }) {
  const ldp = useLdpPorProjeto(selection);
  // A presença de gates vem do Tipo_LDP OFICIAL da aba LDP, não do nome.
  // Sem série temporal individual comprovada, não oferecer seleção falsa
  // de 30/60/90% que continuaria mostrando o agregado geral.
  const possuiTipo2 = ldp.projetos.some(p => p.tipoLdp === 2);
  const [metrica, setMetrica] = useState<Metrica>("fisico-cronograma");
  const [periodo, setPeriodo] = useState<Periodo>("semanal");
  const [report, setReport] = useState<CurvaSReport>();
  const [erro, setErro] = useState<string>();
  const [carregando, setCarregando] = useState(true);
  const chave = JSON.stringify(selection.map(p => [p.hubId, p.id]));

  useEffect(() => {
    const abort = new AbortController();
    setReport(undefined); setErro(undefined); setCarregando(true);
    (async () => {
      const projetos: [string, string][] = JSON.parse(chave);
      if (!projetos.length) { setErro("Selecione um projeto para a Curva S."); return; }
      const resposta = await limitedMap(projetos, async ([hubId, projectId]) => {
        const qs = new URLSearchParams({ hubId, projectId });
        // O Resumo ja publica o avanço físico oficial em gestao-vista.avanco.
        // Reutilizar a API EXISTENTE em vez de compilar/reiniciar backend
        // ou derivar avanço da quantidade de emissões (78% != avanço físico).
        const [curva, gerencial] = await Promise.allSettled([
          getJson<CurvaSReport>(`/api/reports/curva-s?${qs}`, abort.signal),
          getJson<{ avanco?: CurvaSReport["avanco"]; avancoCronograma?: CurvaSReport["avancoCronograma"] }>(`/api/reports/gestao-vista?${qs}`, abort.signal),
        ]);
        if (curva.status === "rejected") throw curva.reason;
        return {
          ...curva.value,
          avanco: gerencial.status === "fulfilled" ? gerencial.value.avanco ?? null : null,
          avancoCronograma: gerencial.status === "fulfilled" ? gerencial.value.avancoCronograma ?? null : null,
        };
      });
      if (!abort.signal.aborted)
        setReport(resposta.length === 1 ? resposta[0] : consolidarCurvaS(resposta));
    })().catch(e => {
      if (!abort.signal.aborted)
        setErro(e instanceof ApiError ? e.message : "Não foi possível consultar as curvas documentais.");
    }).finally(() => { if (!abort.signal.aborted) setCarregando(false); });
    return () => abort.abort();
  }, [chave]);

  const dados = useMemo<DadosVisual>(() => {
    if (erro) return { estado: "erro", mensagem: erro };
    if (carregando || !report) return { estado: "carregando" };
    return preparar(report, metrica, periodo);
  }, [erro, carregando, report, metrica, periodo]);

  const porCronograma = metrica === "fisico-cronograma";
  const porStatus = metrica === "fisico";
  const fonteFisica = porCronograma ? report?.avancoCronograma?.fisico : report?.avanco?.fisico;
  const botao = (ativo: boolean) => `rounded px-3 py-1.5 text-xs font-medium ${ativo
    ? "bg-sky-600 text-white" : "text-muted-foreground hover:bg-muted"}`;
  return (
    <CurvaSIdentidadeVisual
      idTeste="curva-s-documental-bi"
      subtitulo={<>Curva S com duas fontes independentes de avanço físico.
        {report ? ` Referência: ${formatData(report.dataReferencia)}.` : ""}
        {(porCronograma || porStatus) && (fonteFisica != null
          ? ` ${porCronograma ? "Cronograma" : "Status dos documentos"}: ${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(fonteFisica)}%.`
          : ` ${porCronograma ? "Cronograma" : "Status dos documentos"}: medição indisponível.`)}</>}
      controles={<>
        {METRICAS.map(m => (
          <button key={m.key} type="button" className={botao(metrica === m.key)}
            aria-pressed={metrica === m.key} onClick={() => setMetrica(m.key)}>{m.label}</button>
        ))}
        <span className="mx-1 border-l border-border" />
        {(["diario", "semanal", "mensal"] as const).map(p => (
          <button key={p} type="button" className={botao(periodo === p)}
            aria-pressed={periodo === p} onClick={() => setPeriodo(p)}>
            {p === "diario" ? "Diário" : p === "semanal" ? "Semanal" : "Mensal"}
          </button>
        ))}
      </>}
      visual={porCronograma || porStatus ? VISUAL_FISICO : VISUAL_DOC}
      marcoFisicoRef={porCronograma || porStatus ? refs.fisicoAtual : undefined}
      rotuloFisico={porCronograma ? "Avanço físico — Cronograma" : "Avanço físico — Status dos documentos"}
      descricaoFisico={porCronograma
        ? "Medição atual: trabalho planejado ponderado pelo percentual físico das tarefas não-resumo. As curvas de planejamento abaixo ainda vêm das datas da LDP; não representam uma série histórica física do cronograma."
        : "Medição atual pela configuração de pesos de Document_Status, preservada para homologação. As curvas de planejamento abaixo vêm da LDP; não representam um histórico físico por status."}
      dados={dados}
      aviso={<>Identidade visual do BI CHS001. Os planejamentos (baseline e reprogramado) nesta tela ainda vêm da LDP e não do trabalho distribuído por dia do cronograma.
        {porCronograma ? " O avanço por cronograma usa PERCENT_PHYSICAL_COMPLETED das tarefas e seus pesos de Trabalho no snapshot atual. Ainda não foi importada a série AssignmentByDay atualizada para traçar uma linha física histórica confiável." : porStatus ? " O avanço documental usa os percentuais por Document_Status, preservados integralmente. O histórico das alterações de status não é reconstruído nesta curva." : " Séries por período e acumuladas da LDP."}
        Reprogramado documental pela data da LDP, ou baseline quando não houve alteração.
        {report ? `${report.cobertura.totalDocumentos} documentos considerados na fonte; ${report.cobertura.semBaseline} sem baseline.` : ""}
        {possuiTipo2 ? " LDP Tipo 2 detectada: curva geral disponível; curvas temporais individuais 30/60/90% dependem de fontes por gate comprovadas." : ""}
        {ldp.erro ? " Tipo de LDP não pôde ser confirmado nesta sessão." : ""}</>}
    />
  );
}
