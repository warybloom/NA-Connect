// Despacho visual do PBIR → componente. Visuais ocultos (isHidden) não são renderizados, como no Power BI.
import type { DadosVisual } from "../dados/tipos";
import { alturaTitulo, Contenedor, PendenteParidade } from "./Contenedor";
import { BotaoAcao, CaixaTexto, Forma, Imagem, linkDoVisual, type Link } from "./VisuaisEstaticos";
import { Cartao, CartaoNovo, EstadoDados, Matriz, Segmentacao, Tabela } from "./VisuaisDados";
import { GraficoBarras, GraficoCombo, GraficoRosca } from "./Graficos";
import { formatarData } from "./formatacao";
import { ler, type VisualPbi } from "./modelo";

export interface PropsVisualBi {
  v: VisualPbi;
  dados: DadosVisual;
  /** Navegação/bookmark do visualLink; undefined = link sem destino reproduzível. */
  aoClicarLink?: (link: Link) => void;
  linkDisponivel?: (link: Link) => boolean;
}

/** Valor salvo na segmentação do PBIX (general.filter) — exibido só quando a segmentação não tem fonte mapeada. */
function valorSalvoSegmentacao(v: VisualPbi): string | undefined {
  // Intervalo (modo Between): datas salvas em data.startDate/endDate.
  const inicio = ler<string>(v.o, "data", "startDate"), fim = ler<string>(v.o, "data", "endDate");
  if (String(ler(v.o, "data", "mode", "")) === "Between" && inicio && fim) return `${formatarData(inicio)} – ${formatarData(fim)}`;
  const f = ler<any>(v.o, "general", "filter");
  const valores = f?.filter?.Where?.[0]?.Condition?.In?.Values;
  if (!Array.isArray(valores) || valores.length === 0) return undefined;
  const brutos = valores.map((t: any[]) => String(t?.[0]?.Literal?.Value ?? "").replace(/^datetime'|'$/g, "").replace(/^'|'$/g, ""));
  return brutos.length === 1 ? formatarData(brutos[0]) : "Multiple selections";
}

export function VisualBi({ v, dados, aoClicarLink, linkDisponivel }: PropsVisualBi) {
  if (v.hidden) return null;
  const link = linkDoVisual(v);
  const clicavel = link && aoClicarLink && (!linkDisponivel || linkDisponivel(link)) ? () => aoClicarLink(link) : undefined;
  const area = { largura: v.w, altura: Math.max(0, v.h - alturaTitulo(v)) };

  switch (v.type) {
    case "shape": return <Forma v={v} onClick={clicavel} />;
    case "image": return <Imagem v={v} onClick={clicavel} />;
    case "textbox": return <CaixaTexto v={v} />;
    case "actionButton": return <BotaoAcao v={v} onClick={clicavel} />;
    case "card": return <Contenedor v={v}><Cartao v={v} d={dados} /></Contenedor>;
    case "cardVisual": return <Contenedor v={v}><CartaoNovo v={v} d={dados} /></Contenedor>;
    case "slicer": return <Contenedor v={v}><Segmentacao v={v} d={dados} valorSalvo={valorSalvoSegmentacao(v)} /></Contenedor>;
    case "tableEx":
    case "pivotTable":
      return <Contenedor v={v}>{dados.estado === "ok" && dados.tipo === "matriz" ? <Matriz v={v} d={dados} /> : <Tabela v={v} d={dados} />}</Contenedor>;
    case "clusteredBarChart":
    case "clusteredColumnChart":
      return (
        <Contenedor v={v}>
          {dados.estado === "ok" && dados.tipo === "pontos"
            ? <GraficoBarras v={v} {...area} pontos={dados.pontos} formatos={dados.formatos} horizontal={v.type === "clusteredBarChart"} />
            : <EstadoDados d={dados} />}
        </Contenedor>
      );
    case "donutChart":
    case "pieChart":
      return (
        <Contenedor v={v}>
          {dados.estado === "ok" && dados.tipo === "pontos"
            ? <GraficoRosca v={v} {...area} pontos={dados.pontos} formatos={dados.formatos} rosca={v.type === "donutChart"} />
            : <EstadoDados d={dados} />}
        </Contenedor>
      );
    case "lineClusteredColumnComboChart":
    case "lineStackedColumnComboChart":
      return (
        <Contenedor v={v}>
          <GraficoCombo v={v} {...area} pontos={dados.estado === "ok" && dados.tipo === "pontos" ? dados.pontos : []}
            formatos={dados.estado === "ok" && dados.tipo === "pontos" ? dados.formatos : undefined} />
          {!(dados.estado === "ok") && <div className="bi-sobreposicao"><EstadoDados d={dados} /></div>}
        </Contenedor>
      );
    default:
      // Visuais personalizados (Autodesk viewer, advancedSlicerVisual…) ainda sem reprodução segura.
      return (
        <Contenedor v={v}>
          <PendenteParidade motivo={`Visual "${v.type}" ainda não reproduzido.`} />
        </Contenedor>
      );
  }
}
