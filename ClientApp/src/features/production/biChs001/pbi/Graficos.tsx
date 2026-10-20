// Gráficos do Power BI reproduzidos em SVG: clusteredBarChart, clusteredColumnChart, donutChart/pieChart e
// line*ComboChart. Geometria derivada da área do visual (px do canvas 2048×1123) e das propriedades do PBIR
// (labels, categoryAxis, valueAxis, legend, dataPoint). Sem dado → nenhuma barra/fatia (o selo de pendência é do chamador).
import { CORES_DADOS, COR_TEXTO_SECUNDARIO, FONTE_PADRAO, fonte, ler, lerCategoria, lerSerie, nomeCampo, pt, rgba, type CampoPbi, type VisualPbi } from "./modelo";
import { formatarValor } from "./formatacao";
import type { FormatoValor, PontoCategoria } from "../dados/tipos";

interface Props { v: VisualPbi; largura: number; altura: number; pontos: PontoCategoria[]; formatos?: Record<string, FormatoValor> }

const refCampo = (c: CampoPbi) => c.queryRef ?? `${c.entity}.${c.prop}`;
const larguraTexto = (s: string, px: number) => s.length * px * 0.56;

/** Cor da série/ponto: seletor por categoria → por série (metadata) → padrão do visual → paleta do tema. */
function corPonto(v: VisualPbi, serie: CampoPbi, idxSerie: number, categoria?: { coluna?: string; valor: string; entidade?: string }): string {
  const porCat = categoria ? lerCategoria<string>(v.o, "dataPoint", "fill", categoria.coluna, categoria.valor, categoria.entidade) : undefined;
  const transp = Number(ler(v.o, "dataPoint", "fillTransparency", 0));
  const cor = porCat ?? lerSerie<string>(v.o, "dataPoint", "fill", refCampo(serie)) ?? (idxSerie === 0 ? ler<string>(v.o, "dataPoint", "fill") : undefined)
    ?? CORES_DADOS[idxSerie % CORES_DADOS.length];
  return rgba(cor, transp) ?? cor;
}

function rotulos(v: VisualPbi, padraoVisivel: boolean) {
  return {
    mostrar: ler(v.o, "labels", "show", padraoVisivel) as boolean,
    tamanho: pt(Number(ler(v.o, "labels", "fontSize", 9))),
    cor: ler<string>(v.o, "labels", "color", "#252423") ?? "#252423",
    negrito: !!ler(v.o, "labels", "bold", false),
    posicao: String(ler(v.o, "labels", "labelPosition", "Auto")),
  };
}

function eixoCategoria(v: VisualPbi) {
  return {
    mostrar: ler(v.o, "categoryAxis", "show", true) as boolean,
    tamanho: pt(Number(ler(v.o, "categoryAxis", "fontSize", 9))),
    cor: ler<string>(v.o, "categoryAxis", "labelColor", COR_TEXTO_SECUNDARIO) ?? COR_TEXTO_SECUNDARIO,
    negrito: !!ler(v.o, "categoryAxis", "bold", false),
  };
}

function eixoValor(v: VisualPbi) {
  return {
    mostrar: ler(v.o, "valueAxis", "show", true) as boolean,
    tamanho: pt(Number(ler(v.o, "valueAxis", "fontSize", 9))),
    cor: ler<string>(v.o, "valueAxis", "labelColor", COR_TEXTO_SECUNDARIO) ?? COR_TEXTO_SECUNDARIO,
    grade: ler(v.o, "valueAxis", "gridlineShow", true) as boolean,
  };
}

/** Escala "agradável" (como o Power BI): máximo arredondado e 4–6 marcas. */
function escala(max: number, inteiro = true): { topo: number; marcas: number[] } {
  if (!(max > 0)) return { topo: 1, marcas: [0, 1] };
  const bruto = max / 5;
  const pot = 10 ** Math.floor(Math.log10(bruto));
  // Contagens: o Power BI nunca usa marcas fracionárias (eixo de 0 a 4 → 0,1,2,3,4).
  const passo = Math.max(inteiro ? 1 : 0, [1, 2, 2.5, 5, 10].map((m) => m * pot).find((p) => p >= bruto) ?? 10 * pot);
  const topo = Math.ceil(max / passo) * passo;
  const marcas: number[] = [];
  for (let x = 0; x <= topo + 1e-9; x += passo) marcas.push(Number(x.toFixed(10)));
  return { topo, marcas };
}

/** Quebra de rótulo de categoria em até `linhas` linhas na largura disponível. */
function quebrar(texto: string, largura: number, px: number, linhas = 2): string[] {
  const palavras = texto.split(/\s+/);
  const out: string[] = [];
  let atual = "";
  for (const p of palavras) {
    const tentativa = atual ? `${atual} ${p}` : p;
    if (larguraTexto(tentativa, px) <= largura || !atual) atual = tentativa;
    else { out.push(atual); atual = p; }
  }
  if (atual) out.push(atual);
  if (out.length > linhas) { const r = out.slice(0, linhas); r[linhas - 1] = `${r[linhas - 1].slice(0, Math.max(1, Math.floor(largura / (px * 0.56)) - 1))}…`; return r; }
  return out.map((l) => (larguraTexto(l, px) > largura ? `${l.slice(0, Math.max(1, Math.floor(largura / (px * 0.56)) - 1))}…` : l));
}

// ------------------------------------------------------------------------------------------- barras

export function GraficoBarras({ v, largura, altura, pontos, formatos, horizontal }: Props & { horizontal: boolean }) {
  const series = v.fields?.Y ?? [];
  const cat = v.fields?.Category?.[0];
  const rot = rotulos(v, false);
  const ec = eixoCategoria(v);
  const ev = eixoValor(v);
  const max = Math.max(0, ...pontos.flatMap((p) => series.map((s) => Number(p.valores[refCampo(s)] ?? 0))));
  const { topo, marcas } = escala(max, series.every((s) => (formatos?.[refCampo(s)] ?? "inteiro") === "inteiro"));
  const fmt = (n: number | null, s: CampoPbi) => formatarValor(n, formatos?.[refCampo(s)] ?? "inteiro");
  const pad = 6;
  const n = Math.max(1, pontos.length);

  if (horizontal) {
    const larguraCat = ec.mostrar ? Math.min(largura * 0.45, Math.max(...pontos.map((p) => larguraTexto(p.categoria, ec.tamanho)), 20) + 10) : 0;
    const alturaEixo = ev.mostrar ? ev.tamanho + 8 : 0;
    const x0 = pad + larguraCat, x1 = largura - pad - (rot.mostrar ? larguraTexto(fmt(max, series[0] ?? ({} as CampoPbi)), rot.tamanho) + 8 : 0);
    const y0 = pad, y1 = altura - pad - alturaEixo;
    const banda = (y1 - y0) / n;
    const espessura = (banda * 0.8) / Math.max(1, series.length);
    const escalaX = (val: number) => x0 + ((x1 - x0) * val) / topo;
    return (
      <svg width={largura} height={altura} className="bi-svg" style={{ fontFamily: FONTE_PADRAO }}>
        {ev.mostrar && marcas.map((m) => (
          <g key={m}>
            {ev.grade && <line x1={escalaX(m)} x2={escalaX(m)} y1={y0} y2={y1} stroke="#C8C6C4" strokeDasharray="1 3" strokeWidth={1} />}
            <text x={escalaX(m)} y={y1 + ev.tamanho + 2} textAnchor="middle" fontSize={ev.tamanho} fill={ev.cor}>{formatarValor(m, "inteiro", { displayUnits: 0 })}</text>
          </g>
        ))}
        {pontos.map((p, i) => (
          <g key={p.categoria}>
            {ec.mostrar && (
              <text x={x0 - 6} y={y0 + banda * i + banda / 2} dominantBaseline="central" textAnchor="end" fontSize={ec.tamanho} fill={ec.cor}
                fontWeight={ec.negrito ? 700 : 400}>{quebrar(p.categoria, larguraCat - 6, ec.tamanho, 1)[0]}<title>{p.categoria}</title></text>
            )}
            {series.map((s, j) => {
              const val = Number(p.valores[refCampo(s)] ?? 0);
              const y = y0 + banda * i + banda * 0.1 + espessura * j;
              const w = Math.max(0, escalaX(val) - x0);
              return (
                <g key={j}>
                  <rect x={x0} y={y} width={w} height={espessura} fill={corPonto(v, s, j, { coluna: cat?.prop, valor: p.categoria, entidade: cat?.entity })}>
                    <title>{`${p.categoria}: ${fmt(p.valores[refCampo(s)] ?? null, s)}`}</title>
                  </rect>
                  {rot.mostrar && p.valores[refCampo(s)] !== null && (
                    <text x={x0 + w + 4} y={y + espessura / 2} dominantBaseline="central" fontSize={rot.tamanho} fill={rot.cor}
                      fontWeight={rot.negrito ? 700 : 400}>{fmt(p.valores[refCampo(s)] ?? null, s)}</text>
                  )}
                </g>
              );
            })}
          </g>
        ))}
      </svg>
    );
  }

  // Colunas
  const larguraBanda = (largura - 2 * pad) / n;
  const linhasRotulo = larguraBanda < 70 ? 3 : 2;
  const alturaCat = ec.mostrar ? ec.tamanho * 1.2 * linhasRotulo + 6 : 0;
  const larguraEixo = ev.mostrar ? larguraTexto(formatarValor(topo, "inteiro"), ev.tamanho) + 8 : 0;
  const x0 = pad + larguraEixo, x1 = largura - pad;
  const y0 = pad + (rot.mostrar ? rot.tamanho + 6 : 0), y1 = altura - pad - alturaCat;
  const banda = (x1 - x0) / n;
  const espessura = (banda * 0.8) / Math.max(1, series.length);
  const escalaY = (val: number) => y1 - ((y1 - y0) * val) / topo;
  return (
    <svg width={largura} height={altura} className="bi-svg" style={{ fontFamily: FONTE_PADRAO }}>
      {ev.mostrar && marcas.map((m) => (
        <g key={m}>
          {ev.grade && <line x1={x0} x2={x1} y1={escalaY(m)} y2={escalaY(m)} stroke="#C8C6C4" strokeDasharray="1 3" strokeWidth={1} />}
          <text x={x0 - 6} y={escalaY(m)} dominantBaseline="central" textAnchor="end" fontSize={ev.tamanho} fill={ev.cor}>{formatarValor(m, "inteiro", { displayUnits: 0 })}</text>
        </g>
      ))}
      {pontos.map((p, i) => (
        <g key={p.categoria}>
          {series.map((s, j) => {
            const val = Number(p.valores[refCampo(s)] ?? 0);
            const x = x0 + banda * i + banda * 0.1 + espessura * j;
            const y = escalaY(val);
            return (
              <g key={j}>
                <rect x={x} y={y} width={espessura} height={Math.max(0, y1 - y)} fill={corPonto(v, s, j, { coluna: cat?.prop, valor: p.categoria, entidade: cat?.entity })}>
                  <title>{`${p.categoria}: ${fmt(p.valores[refCampo(s)] ?? null, s)}`}</title>
                </rect>
                {rot.mostrar && p.valores[refCampo(s)] !== null && (
                  <text x={x + espessura / 2} y={y - 4} textAnchor="middle" fontSize={rot.tamanho} fill={rot.cor} fontWeight={rot.negrito ? 700 : 400}>
                    {fmt(p.valores[refCampo(s)] ?? null, s)}
                  </text>
                )}
              </g>
            );
          })}
          {ec.mostrar && quebrar(p.categoria, banda - 4, ec.tamanho, linhasRotulo).map((l, k) => (
            <text key={k} x={x0 + banda * i + banda / 2} y={y1 + 4 + ec.tamanho * (k + 1) * 1.15} textAnchor="middle" fontSize={ec.tamanho}
              fill={ec.cor} fontWeight={ec.negrito ? 700 : 400}>{l}</text>
          ))}
        </g>
      ))}
    </svg>
  );
}

// --------------------------------------------------------------------------------------- rosca/pizza

export function GraficoRosca({ v, largura, altura, pontos, formatos, rosca }: Props & { rosca: boolean }) {
  const serie = v.fields?.Y?.[0];
  const cat = v.fields?.Category?.[0];
  const rot = rotulos(v, true);
  const mostrarLegenda = ler(v.o, "legend", "show", true) as boolean;
  const posLegenda = String(ler(v.o, "legend", "position", "RightCenter"));
  const tamLegenda = pt(Number(ler(v.o, "legend", "fontSize", 8)));
  const legendaLateral = mostrarLegenda && /Right|Left/.test(posLegenda);
  const larguraLegenda = legendaLateral ? Math.min(largura * 0.4, Math.max(...pontos.map((p) => larguraTexto(p.categoria, tamLegenda)), 30) + 24) : 0;
  const alturaLegenda = mostrarLegenda && !legendaLateral ? tamLegenda + 10 : 0;
  const area = { x: /Left/.test(posLegenda) ? larguraLegenda : 0, y: /Top/.test(posLegenda) ? alturaLegenda : 0,
    w: largura - larguraLegenda, h: altura - alturaLegenda };
  const total = pontos.reduce((s, p) => s + Number(p.valores[serie ? refCampo(serie) : ""] ?? 0), 0);
  const cx = area.x + area.w / 2, cy = area.y + area.h / 2;
  const r = Math.max(10, Math.min(area.w, area.h) / 2 - (rot.mostrar ? rot.tamanho * 2.4 : 8));
  const ri = rosca ? r * 0.55 : 0;
  const borda = ler(v.o, "dataPoint", "borderShow", false) ? { cor: ler<string>(v.o, "dataPoint", "borderColor", "#FFFFFF"), w: Number(ler(v.o, "dataPoint", "borderSize", 1)) } : { cor: "#FFFFFF", w: 1 };
  let a0 = -Math.PI / 2;
  const fatias = pontos.map((p, i) => {
    const val = Number(p.valores[serie ? refCampo(serie) : ""] ?? 0);
    const ang = total > 0 ? (val / total) * Math.PI * 2 : 0;
    const f = { p, i, a0, a1: a0 + ang, val, cor: serie ? corPonto(v, serie, i, { coluna: cat?.prop, valor: p.categoria, entidade: cat?.entity }) : CORES_DADOS[i] };
    // Rosca/pizza: cada categoria recebe a cor do tema na ordem quando não há cor por ponto.
    if (serie && !lerCategoria(v.o, "dataPoint", "fill", cat?.prop, p.categoria, cat?.entity)) f.cor = CORES_DADOS[i % CORES_DADOS.length];
    a0 += ang;
    return f;
  });
  const arco = (a: number, b: number) => {
    const big = b - a > Math.PI ? 1 : 0;
    const p = (ang: number, rr: number) => `${cx + rr * Math.cos(ang)} ${cy + rr * Math.sin(ang)}`;
    if (b - a >= Math.PI * 2 - 1e-6) b = a + Math.PI * 2 - 1e-4;
    return ri > 0
      ? `M ${p(a, r)} A ${r} ${r} 0 ${big} 1 ${p(b, r)} L ${p(b, ri)} A ${ri} ${ri} 0 ${big} 0 ${p(a, ri)} Z`
      : `M ${cx} ${cy} L ${p(a, r)} A ${r} ${r} 0 ${big} 1 ${p(b, r)} Z`;
  };
  return (
    <svg width={largura} height={altura} className="bi-svg" style={{ fontFamily: FONTE_PADRAO }}>
      {fatias.map((f) => (
        <path key={f.p.categoria} d={arco(f.a0, f.a1)} fill={f.cor} stroke={borda.cor} strokeWidth={borda.w}>
          <title>{`${f.p.categoria}: ${formatarValor(f.val, formatos?.[serie ? refCampo(serie) : ""] ?? "inteiro")}`}</title>
        </path>
      ))}
      {rot.mostrar && fatias.filter((f) => f.val > 0).map((f) => {
        const meio = (f.a0 + f.a1) / 2;
        const x = cx + (r + 10) * Math.cos(meio), y = cy + (r + 10) * Math.sin(meio);
        const pct = total > 0 ? f.val / total : 0;
        return (
          <g key={`l${f.p.categoria}`}>
            <line x1={cx + r * Math.cos(meio)} y1={cy + r * Math.sin(meio)} x2={x} y2={y} stroke="#A19F9D" strokeWidth={1} />
            <text x={x + (Math.cos(meio) >= 0 ? 3 : -3)} y={y} dominantBaseline="central" textAnchor={Math.cos(meio) >= 0 ? "start" : "end"}
              fontSize={rot.tamanho} fill={rot.cor} fontWeight={rot.negrito ? 700 : 400}>
              {`${formatarValor(f.val, "inteiro")} (${formatarValor(pct, "percentual")})`}
            </text>
          </g>
        );
      })}
      {mostrarLegenda && (legendaLateral
        ? (
          <g>
            {ler(v.o, "legend", "showTitle", true) && cat && (
              <text x={/Left/.test(posLegenda) ? 6 : largura - larguraLegenda + 6} y={cy - (pontos.length * (tamLegenda + 6)) / 2 - tamLegenda}
                fontSize={tamLegenda} fontWeight={700} fill="#252423">{nomeCampo(cat)}</text>
            )}
            {pontos.map((p, i) => {
              const x = /Left/.test(posLegenda) ? 6 : largura - larguraLegenda + 6;
              const y = cy - (pontos.length * (tamLegenda + 6)) / 2 + i * (tamLegenda + 6);
              return (
                <g key={p.categoria}>
                  <circle cx={x + 4} cy={y + tamLegenda / 2} r={4} fill={fatias[i]?.cor} />
                  <text x={x + 12} y={y + tamLegenda / 2} dominantBaseline="central" fontSize={tamLegenda} fill={COR_TEXTO_SECUNDARIO}>{p.categoria}</text>
                </g>
              );
            })}
          </g>
        )
        : (
          <g>
            {pontos.map((p, i) => {
              const y = /Top/.test(posLegenda) ? tamLegenda / 2 + 4 : altura - tamLegenda / 2 - 4;
              const x = 8 + pontos.slice(0, i).reduce((s, q) => s + larguraTexto(q.categoria, tamLegenda) + 22, 0);
              return (
                <g key={p.categoria}>
                  <circle cx={x + 4} cy={y} r={4} fill={fatias[i]?.cor} />
                  <text x={x + 12} y={y} dominantBaseline="central" fontSize={tamLegenda} fill={COR_TEXTO_SECUNDARIO}>{p.categoria}</text>
                </g>
              );
            })}
          </g>
        ))}
    </svg>
  );
}

// ------------------------------------------------------------------------------------------ combo

/**
 * Combinação colunas agrupadas (Y, eixo primário) + linhas (Y2, eixo secundário). Usada nas curvas S.
 * PBIR dos Resumos: valueAxis.show = false (primário oculto) e secShow = true (secundário com rótulos); labels desligados;
 * lineStyles por série (tracejado) e marcadores. BLANK numa linha = lacuna (o Power BI não liga pontos através de BLANK).
 * Sem pontos → só legenda (o selo de pendência é do chamador).
 */
export function GraficoCombo({ v, largura, altura, pontos, formatos }: Props) {
  const colunas = v.fields?.Y ?? [];
  const linhas = v.fields?.Y2 ?? [];
  const mostrarLegenda = ler(v.o, "legend", "show", true) as boolean;
  const tamLegenda = pt(Number(ler(v.o, "legend", "fontSize", 8)));
  const corLegenda = ler<string>(v.o, "legend", "labelColor", COR_TEXTO_SECUNDARIO) ?? COR_TEXTO_SECUNDARIO;
  const espessuraLinha = Number(ler(v.o, "lineStyles", "strokeWidth", 3));
  const marcadores = ler(v.o, "lineStyles", "showMarker", false) as boolean;
  const series = [...colunas.map((c, i) => ({ c, tipo: "col" as const, cor: corPonto(v, c, i), tracejada: false })),
    ...linhas.map((c, i) => ({ c, tipo: "lin" as const, cor: corPonto(v, c, colunas.length + i),
      tracejada: lerSerie<string>(v.o, "lineStyles", "lineStyle", refCampo(c)) === "dashed" }))];
  let xl = 8;
  const legenda = series.map((s) => {
    const nome = nomeCampo(s.c);
    const item = { ...s, nome, x: xl };
    xl += larguraTexto(nome, tamLegenda) + 30;
    return item;
  });
  const offset = Math.max(0, (largura - xl) / 2);

  const ec = eixoCategoria(v);
  const mostrarPrim = ler(v.o, "valueAxis", "show", true) as boolean;
  const mostrarSec = ler(v.o, "valueAxis", "secShow", true) as boolean;
  const tamSec = pt(Number(ler(v.o, "valueAxis", "secFontSize", 9)));
  const corSec = ler<string>(v.o, "valueAxis", "secLabelColor", COR_TEXTO_SECUNDARIO) ?? COR_TEXTO_SECUNDARIO;
  const negritoSec = !!ler(v.o, "valueAxis", "secBold", false);
  const fmtEixo = (n: number, c?: CampoPbi) => formatarValor(n, (c && formatos?.[refCampo(c)]) ?? "decimal", { casas: 0, displayUnits: 0 });
  const valor = (p: PontoCategoria, c: CampoPbi) => p.valores[refCampo(c)] ?? null;

  const maxCol = Math.max(0, ...pontos.flatMap((p) => colunas.map((c) => valor(p, c) ?? 0)));
  const maxLin = Math.max(0, ...pontos.flatMap((p) => linhas.map((c) => valor(p, c) ?? 0)));
  const ePrim = escala(maxCol, false), eSec = escala(maxLin, false);
  const topoLegenda = mostrarLegenda ? 22 : 4;
  const larguraSec = mostrarSec && linhas.length ? Math.max(...eSec.marcas.map((m) => larguraTexto(fmtEixo(m, linhas[0]), tamSec))) + 10 : 0;
  const larguraPrim = mostrarPrim && colunas.length ? Math.max(...ePrim.marcas.map((m) => larguraTexto(fmtEixo(m, colunas[0]), ec.tamanho))) + 10 : 0;
  const x0 = 8 + larguraPrim, x1 = largura - 8 - larguraSec;
  const n = Math.max(1, pontos.length);
  const banda = (x1 - x0) / n;
  const rotulosCat = pontos.map((p) => (formatos?.["dCalendario.Data Corte"] === "data" || /^\d{4}-\d{2}-\d{2}$/.test(p.categoria) ? formatarValor(p.categoria, "data") : p.categoria));
  const larguraRotulo = Math.max(0, ...rotulosCat.map((r) => larguraTexto(r, ec.tamanho)));
  const inclinar = larguraRotulo + 4 > banda;
  const alturaCat = ec.mostrar ? (inclinar ? larguraRotulo * 0.72 + 8 : ec.tamanho + 8) : 0;
  const y0 = topoLegenda + 6, y1 = altura - 4 - alturaCat;
  const yPrim = (x: number) => y1 - ((y1 - y0) * x) / ePrim.topo;
  const ySec = (x: number) => y1 - ((y1 - y0) * x) / eSec.topo;
  const larguraColuna = (banda * 0.7) / Math.max(1, colunas.length);

  return (
    <svg width={largura} height={altura} className="bi-svg" style={{ fontFamily: fonte(undefined) }}>
      {mostrarLegenda && legenda.map((s) => (
        <g key={s.nome} transform={`translate(${offset},0)`}>
          {s.tipo === "col"
            ? <rect x={s.x} y={6} width={10} height={10} fill={s.cor} />
            : <line x1={s.x - 2} x2={s.x + 14} y1={11} y2={11} stroke={s.cor} strokeWidth={3} strokeDasharray={s.tracejada ? "5 3" : undefined} />}
          <text x={s.x + 18} y={11} dominantBaseline="central" fontSize={tamLegenda} fill={corLegenda}>{s.nome}</text>
        </g>
      ))}
      {pontos.length === 0 && <line x1={8} x2={largura - 8} y1={altura - 24} y2={altura - 24} stroke="#C8C6C4" />}
      {pontos.length > 0 && (
        <g>
          {mostrarSec && linhas.length > 0 && eSec.marcas.map((m) => (
            <g key={`s${m}`}>
              <line x1={x0} x2={x1} y1={ySec(m)} y2={ySec(m)} stroke="#E1DFDD" strokeWidth={1} />
              <text x={x1 + 6} y={ySec(m)} dominantBaseline="central" fontSize={tamSec} fill={corSec} fontWeight={negritoSec ? 700 : 400}>{fmtEixo(m, linhas[0])}</text>
            </g>
          ))}
          {mostrarPrim && colunas.length > 0 && ePrim.marcas.map((m) => (
            <text key={`p${m}`} x={x0 - 6} y={yPrim(m)} dominantBaseline="central" textAnchor="end" fontSize={ec.tamanho} fill={ec.cor}>{fmtEixo(m, colunas[0])}</text>
          ))}
          <line x1={x0} x2={x1} y1={y1} y2={y1} stroke="#C8C6C4" />
          {pontos.map((p, i) => colunas.map((c, j) => {
            const val = valor(p, c);
            if (val === null) return null;
            const x = x0 + banda * i + banda * 0.15 + larguraColuna * j;
            const y = yPrim(Math.max(0, val)), h = Math.max(0, y1 - y);
            return (
              <rect key={`${i}-${j}`} x={x} y={y} width={larguraColuna} height={h} fill={series[j].cor}>
                <title>{`${rotulosCat[i]} · ${nomeCampo(c)}: ${formatarValor(val, formatos?.[refCampo(c)] ?? "decimal")}`}</title>
              </rect>
            );
          }))}
          {linhas.map((c, j) => {
            const s = series[colunas.length + j];
            // Segmentos contínuos entre valores não-BLANK.
            const trechos: { x: number; y: number; val: number; i: number }[][] = [[]];
            pontos.forEach((p, i) => {
              const val = valor(p, c);
              if (val === null) { if (trechos[trechos.length - 1].length) trechos.push([]); return; }
              trechos[trechos.length - 1].push({ x: x0 + banda * i + banda / 2, y: ySec(val), val, i });
            });
            return (
              <g key={`l${j}`}>
                {trechos.filter((t) => t.length).map((t, k) => (
                  <polyline key={k} points={t.map((q) => `${q.x},${q.y}`).join(" ")} fill="none" stroke={s.cor} strokeWidth={espessuraLinha}
                    strokeLinejoin="round" strokeDasharray={s.tracejada ? "6 4" : undefined} />
                ))}
                {trechos.flat().map((q) => (
                  <circle key={q.i} cx={q.x} cy={q.y} r={marcadores ? Math.max(2.5, espessuraLinha) : 6} fill={marcadores ? s.cor : "transparent"}>
                    <title>{`${rotulosCat[q.i]} · ${nomeCampo(c)}: ${formatarValor(q.val, formatos?.[refCampo(c)] ?? "decimal")}`}</title>
                  </circle>
                ))}
              </g>
            );
          })}
          {ec.mostrar && rotulosCat.map((r, i) => {
            const cx = x0 + banda * i + banda / 2;
            return inclinar
              ? <text key={i} x={cx} y={y1 + 6} transform={`rotate(-45 ${cx} ${y1 + 6})`} textAnchor="end" dominantBaseline="hanging" fontSize={ec.tamanho} fill={ec.cor}>{r}</text>
              : <text key={i} x={cx} y={y1 + 6} textAnchor="middle" dominantBaseline="hanging" fontSize={ec.tamanho} fill={ec.cor}>{r}</text>;
          })}
        </g>
      )}
    </svg>
  );
}
