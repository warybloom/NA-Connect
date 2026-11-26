// Visuais com dados: card (legado), cardVisual (novo cartão), slicer (segmentação), tableEx (tabela) e pivotTable (matriz).
// Valores vêm SEMPRE de DadosVisual (camada de dados); estados carregando/erro/pendente são exibidos sem número.
import { Fragment, useState, type CSSProperties } from "react";
import type { DadosVisual, FormatoValor, NoMatriz, Valor } from "../dados/tipos";
import EXPANSAO_PBIR from "../generated/expansao_matrizes.json";
import { formatarValor } from "./formatacao";
import { COR_TEXTO, COR_TEXTO_SECUNDARIO, FONTE_PADRAO, fonte, ler, lerSerie, nomeCampo, pt, rgba, type CampoPbi, type VisualPbi } from "./modelo";
import { EstadoCarregando, EstadoErro, PendenteParidade } from "./Contenedor";

const refCampo = (c: CampoPbi) => c.queryRef ?? `${c.entity}.${c.prop}`;

/** Corpo comum para estados sem valor. */
export function EstadoDados({ d, compacto }: { d: DadosVisual; compacto?: boolean }) {
  if (d.estado === "carregando") return <EstadoCarregando />;
  if (d.estado === "erro") return <EstadoErro mensagem={d.mensagem} />;
  if (d.estado === "pendente") return <PendenteParidade motivo={d.motivo} compacto={compacto} />;
  return null;
}

function texto(valor: Valor | undefined, formato: FormatoValor | undefined, op?: { casas?: number; displayUnits?: number }) {
  if (valor === null || valor === undefined) return "(Blank)";
  return formatarValor(valor, formato ?? (typeof valor === "number" ? "inteiro" : "texto"), op);
}

// -------------------------------------------------------------------------------------------- card

export function Cartao({ v, d }: { v: VisualPbi; d: DadosVisual }) {
  const campo = v.fields?.Values?.[0];
  const mostrarCategoria = ler(v.o, "categoryLabels", "show", true) as boolean;
  if (d.estado !== "ok" || d.tipo !== "valores" || !campo) return <EstadoDados d={d} compacto />;
  const valor = d.valores[refCampo(campo)];
  return (
    <div className="bi-cartao">
      <span style={{
        fontSize: pt(Number(ler(v.o, "labels", "fontSize", 27))), fontWeight: ler(v.o, "labels", "bold", false) ? 700 : 400,
        color: ler(v.o, "labels", "color", COR_TEXTO), fontFamily: fonte(ler(v.o, "labels", "fontFamily"), "'DIN', 'Segoe UI', sans-serif"),
      }}>
        {texto(valor, d.formatos?.[refCampo(campo)], { casas: ler(v.o, "labels", "labelPrecision"), displayUnits: ler(v.o, "labels", "labelDisplayUnits", 0) })}
      </span>
      {mostrarCategoria && <span className="bi-cartao-categoria">{nomeCampo(campo)}</span>}
    </div>
  );
}

// ------------------------------------------------------------------------------------------ cardVisual

export function CartaoNovo({ v, d }: { v: VisualPbi; d: DadosVisual }) {
  const dados = v.fields?.Data ?? [];
  // layout.orientation do novo cartão: 0 = horizontal (uma linha), 1 = vertical (uma coluna), 2 = grade (columnCount).
  const orientacao = Number(ler(v.o, "layout", "orientation", 2));
  const porLinha = orientacao === 1 ? 1 : orientacao === 0 ? dados.length || 1 : Number(ler(v.o, "layout", "columnCount", dados.length || 1));
  const colunas = Math.max(1, Math.min(porLinha, dados.length || 1));
  const grade = ler(v.o, "grid", "show", false) ? `${Number(ler(v.o, "grid", "width", 1))}px solid ${rgba(ler(v.o, "grid", "color", "#CCCCCC"), ler(v.o, "grid", "transparency", 0))}` : undefined;
  const estiloValor = (c: CampoPbi): CSSProperties => ({
    fontSize: pt(Number(lerSerie(v.o, "value", "fontSize", refCampo(c)) ?? ler(v.o, "value", "fontSize", 32))),
    fontWeight: (lerSerie(v.o, "value", "bold", refCampo(c)) ?? ler(v.o, "value", "bold", true)) ? 700 : 400,
    color: lerSerie<string>(v.o, "value", "fontColor", refCampo(c)) ?? ler(v.o, "value", "fontColor", COR_TEXTO),
    fontFamily: fonte(ler(v.o, "value", "fontFamily"), "'Segoe UI Semibold', 'Segoe UI', sans-serif"),
    textAlign: String(ler(v.o, "value", "horizontalAlignment", "left")) as CSSProperties["textAlign"],
  });
  const mostrarRotulo = ler(v.o, "label", "show", true) as boolean;
  const estiloRotulo: CSSProperties = {
    fontSize: pt(Number(ler(v.o, "label", "fontSize", 12))), fontWeight: ler(v.o, "label", "bold", false) ? 700 : 400,
    color: ler(v.o, "label", "fontColor", COR_TEXTO_SECUNDARIO), textAlign: String(ler(v.o, "value", "horizontalAlignment", "left")) as CSSProperties["textAlign"],
    ...(ler(v.o, "label", "textWrap", false) ? { whiteSpace: "normal" as const, overflow: "visible" } : {}),
  };
  return (
    <div className="bi-cartao-novo" style={{ gridTemplateColumns: `repeat(${colunas}, 1fr)`, justifyContent: "center" }}>
      {dados.map((c, i) => (
        <div key={i} className="bi-cartao-novo-tile" style={{ borderLeft: i % colunas > 0 ? grade : undefined, borderTop: i >= colunas ? grade : undefined }}>
          {d.estado === "ok" && d.tipo === "valores" && d.pendentes?.[refCampo(c)]
            ? <PendenteParidade motivo={d.pendentes[refCampo(c)]} compacto />
            : d.estado === "ok" && d.tipo === "valores"
              ? <span style={estiloValor(c)}>{texto(d.valores[refCampo(c)], d.formatos?.[refCampo(c)])}</span>
              : <EstadoDados d={d} compacto />}
          {mostrarRotulo && <span style={estiloRotulo}>{nomeCampo(c)}</span>}
        </div>
      ))}
    </div>
  );
}

// --------------------------------------------------------------------------------------------- slicer

export function Segmentacao({ v, d, valorSalvo }: { v: VisualPbi; d: DadosVisual; valorSalvo?: string }) {
  const [aberta, setAberta] = useState(false);
  const campo = v.fields?.Values?.[0];
  const modo = String(ler(v.o, "data", "mode", "Basic"));
  const mostrarCabecalho = ler(v.o, "header", "show", true) as boolean;
  const tamItens = pt(Number(ler(v.o, "items", "textSize", 10)));
  const corItens = ler<string>(v.o, "items", "fontColor", COR_TEXTO) ?? COR_TEXTO;
  const cabecalho: CSSProperties = {
    fontSize: pt(Number(ler(v.o, "header", "textSize", 10))), fontWeight: ler(v.o, "header", "bold", false) ? 700 : 400,
    color: ler(v.o, "header", "fontColor", COR_TEXTO), fontFamily: fonte(ler(v.o, "header", "fontFamily")),
  };
  const opcoes = d.estado === "ok" && d.tipo === "opcoes" ? d : null;
  const resumo = opcoes
    ? (opcoes.selecionadas.length === 0 ? "All" : opcoes.selecionadas.length === 1 ? opcoes.opcoes.find((o) => o.chave === opcoes.selecionadas[0])?.rotulo ?? "" : "Multiple selections")
    : valorSalvo ?? "All";
  return (
    <div className="bi-segmentacao" style={{ fontFamily: FONTE_PADRAO }}>
      {mostrarCabecalho && campo && (
        <div className="bi-segmentacao-cabecalho" style={cabecalho}>
          <span>{nomeCampo(campo)}</span>
          {opcoes && opcoes.selecionadas.length > 0 && (
            <button type="button" className="bi-segmentacao-limpar" title="Limpar seleções" onClick={opcoes.limpar}>⌫</button>
          )}
        </div>
      )}
      {modo === "Dropdown" ? (
        <div className="bi-segmentacao-dropdown">
          <button type="button" className="bi-segmentacao-caixa" style={{ fontSize: tamItens, color: corItens }} onClick={() => opcoes && setAberta((a) => !a)}
            disabled={!opcoes} title={d.estado === "pendente" ? d.motivo : undefined}>
            <span>{resumo}</span><span className="bi-segmentacao-seta">⌄</span>
          </button>
          {aberta && opcoes && (
            <div className="bi-segmentacao-lista" style={{ fontSize: tamItens, color: corItens }}>
              {opcoes.opcoes.map((o) => (
                <label key={o.chave}>
                  <input type="checkbox" checked={opcoes.selecionadas.includes(o.chave)} onChange={() => opcoes.alternar(o.chave)} />
                  <span>{o.rotulo}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="bi-segmentacao-lista bi-segmentacao-lista-fixa" style={{ fontSize: tamItens, color: corItens }}>
          {opcoes ? opcoes.opcoes.map((o) => (
            <label key={o.chave}>
              <input type="checkbox" checked={opcoes.selecionadas.includes(o.chave)} onChange={() => opcoes.alternar(o.chave)} />
              <span>{o.rotulo}</span>
            </label>
          )) : <span>{resumo}</span>}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------- matriz

interface ExpansaoPbir { niveis: string[]; recolhidoPorPadrao: boolean[]; abertos: (string | null)[][] }
const chaveCaminho = (c: Valor[]) => JSON.stringify(c.map((x) => (x === null ? null : String(x))));

/** Nó aberto? Padrão do nível (expansionStates.levels[n].isCollapsed) invertido pelos nós "isToggled" salvos no PBIR. */
export function noAbertoInicial(exp: ExpansaoPbir | undefined, no: NoMatriz): boolean {
  const recolhido = exp?.recolhidoPorPadrao[no.nivel] ?? true;
  const alternado = !!exp?.abertos.some((c) => chaveCaminho(c as Valor[]) === chaveCaminho(no.caminho));
  return recolhido ? alternado : !alternado;
}

/** Matriz em degraus (stepped layout): cabeçalho de linha + Values; subtotais de linha desligados = linha aberta sem valores. */
export function Matriz({ v, d }: { v: VisualPbi; d: Extract<DadosVisual, { tipo: "matriz" }> }) {
  const exp = (EXPANSAO_PBIR as Record<string, ExpansaoPbir>)[v.id];
  const [alternados, setAlternados] = useState<Set<string>>(new Set());
  const rows = v.fields?.Rows ?? [];
  const valores = v.fields?.Values ?? [];
  const subtotais = ler(v.o, "subTotals", "rowSubtotals", true) as boolean;
  const cab: CSSProperties = {
    background: ler(v.o, "columnHeaders", "backColor", "#FFFFFF"), color: ler(v.o, "columnHeaders", "fontColor", COR_TEXTO),
    fontSize: pt(Number(ler(v.o, "columnHeaders", "fontSize", 9))), fontWeight: ler(v.o, "columnHeaders", "bold", false) ? 700 : 400,
    fontFamily: fonte(ler(v.o, "columnHeaders", "fontFamily"), "'Segoe UI Semibold', 'Segoe UI', sans-serif"),
    textAlign: String(ler(v.o, "columnHeaders", "alignment", "Left")).toLowerCase() as CSSProperties["textAlign"], whiteSpace: "nowrap",
  };
  const padding = Number(ler(v.o, "grid", "rowPadding", 2));
  const gradeH = ler(v.o, "grid", "gridHorizontal", true) ? `${Number(ler(v.o, "grid", "gridHorizontalWeight", 1))}px solid ${ler(v.o, "grid", "gridHorizontalColor", "#E1DFDD")}` : undefined;
  const gradeV = ler(v.o, "grid", "gridVertical", false) ? `${Number(ler(v.o, "grid", "gridVerticalWeight", 1))}px solid ${ler(v.o, "grid", "gridVerticalColor", "#E1DFDD")}` : undefined;
  const corpo: CSSProperties = { fontSize: pt(Number(ler(v.o, "values", "fontSize", 9))), color: ler(v.o, "values", "fontColorPrimary", COR_TEXTO) };
  const recuo = Number(ler(v.o, "rowHeaders", "steppedLayoutIndentation", 10));
  const alinhamento = (c: CampoPbi) => lerSerie<string>(v.o, "columnFormatting", "alignment", refCampo(c))?.toLowerCase() as CSSProperties["textAlign"];
  const aberto = (no: NoMatriz) => noAbertoInicial(exp, no) !== alternados.has(chaveCaminho(no.caminho));
  const alternar = (no: NoMatriz) => setAlternados((s) => {
    const k = chaveCaminho(no.caminho), n = new Set(s);
    if (n.has(k)) n.delete(k); else n.add(k);
    return n;
  });
  const linhas = (nos: NoMatriz[]): JSX.Element[] => nos.map((no) => {
    const abre = no.filhos.length > 0 && aberto(no);
    const rotulo = no.caminho[no.nivel];
    return (
      <Fragment key={chaveCaminho(no.caminho)}>
        <tr style={{ background: no.nivel === 0 ? "#FFFFFF" : undefined }}>
          <td style={{ ...corpo, padding: `${padding}px 6px`, paddingLeft: 6 + no.nivel * recuo, borderBottom: gradeH, borderRight: gradeV,
            fontWeight: no.filhos.length ? 600 : 400 }}>
            <span className="bi-matriz-no">
              {no.filhos.length
                ? <button type="button" className="bi-matriz-botao" aria-expanded={abre} onClick={() => alternar(no)}>{abre ? "−" : "+"}</button>
                : <span className="bi-matriz-folga" />}
              <span>{rotulo === null || rotulo === undefined ? "" : String(rotulo)}</span>
            </span>
          </td>
          {valores.map((c, j) => (
            <td key={j} style={{ ...corpo, padding: `${padding}px 6px`, borderBottom: gradeH, borderRight: gradeV, textAlign: alinhamento(c), whiteSpace: "nowrap" }}>
              {abre && !subtotais ? "" : no.valores[refCampo(c)] === null || no.valores[refCampo(c)] === undefined ? ""
                : formatarValor(no.valores[refCampo(c)], d.formatos?.[refCampo(c)] ?? "texto")}
            </td>
          ))}
        </tr>
        {abre && linhas(no.filhos)}
      </Fragment>
    );
  });
  return (
    <div className="bi-tabela-rolagem">
      <table className="bi-tabela" style={{ fontFamily: FONTE_PADRAO }}>
        <thead>
          <tr>
            <th style={{ ...cab, padding: `${padding + 2}px 6px`, borderRight: gradeV }}>{rows[0] ? nomeCampo(rows[0]) : ""}</th>
            {valores.map((c, i) => <th key={i} style={{ ...cab, padding: `${padding + 2}px 6px`, borderRight: gradeV }}>{nomeCampo(c)}</th>)}
          </tr>
        </thead>
        <tbody>{linhas(d.nos)}</tbody>
      </table>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------- tabela

export function Tabela({ v, d }: { v: VisualPbi; d: DadosVisual }) {
  const colunas = v.fields?.Values ?? (v.fields ? Object.values(v.fields).flat() : []);
  const cab: CSSProperties = {
    background: ler(v.o, "columnHeaders", "backColor", "#FFFFFF"), color: ler(v.o, "columnHeaders", "fontColor", COR_TEXTO),
    fontSize: pt(Number(ler(v.o, "columnHeaders", "fontSize", 9))), fontWeight: ler(v.o, "columnHeaders", "bold", false) ? 700 : 400,
    fontFamily: fonte(ler(v.o, "columnHeaders", "fontFamily"), "'Segoe UI Semibold', 'Segoe UI', sans-serif"),
    textAlign: String(ler(v.o, "columnHeaders", "alignment", "Left")).toLowerCase() as CSSProperties["textAlign"],
    whiteSpace: ler(v.o, "columnHeaders", "wordWrap", true) ? "normal" : "nowrap",
  };
  const padding = Number(ler(v.o, "grid", "rowPadding", 2));
  const gradeH = ler(v.o, "grid", "gridHorizontal", true) ? `${Number(ler(v.o, "grid", "gridHorizontalWeight", 1))}px solid ${ler(v.o, "grid", "gridHorizontalColor", "#E1DFDD")}` : undefined;
  const gradeV = ler(v.o, "grid", "gridVertical", false) ? `${Number(ler(v.o, "grid", "gridVerticalWeight", 1))}px solid ${ler(v.o, "grid", "gridVerticalColor", "#E1DFDD")}` : undefined;
  const corpo: CSSProperties = {
    fontSize: pt(Number(ler(v.o, "values", "fontSize", 9))), color: ler(v.o, "values", "fontColorPrimary", COR_TEXTO),
    whiteSpace: ler(v.o, "values", "wordWrap", true) ? "normal" : "nowrap",
  };
  const fundo1 = ler<string>(v.o, "values", "backColorPrimary", "#FFFFFF");
  const fundo2 = ler<string>(v.o, "values", "backColorSecondary", "#F3F2F1");
  const largura = (c: CampoPbi) => lerSerie<number>(v.o, "columnWidth", "value", refCampo(c));
  const alinhamento = (c: CampoPbi) => lerSerie<string>(v.o, "columnFormatting", "alignment", refCampo(c))?.toLowerCase() as CSSProperties["textAlign"];
  const linhas = d.estado === "ok" && d.tipo === "linhas" ? d.linhas : [];
  const formatos = d.estado === "ok" && d.tipo === "linhas" ? d.formatos : undefined;
  return (
    <div className="bi-tabela-rolagem">
      <table className="bi-tabela" style={{ fontFamily: FONTE_PADRAO }}>
        <thead>
          <tr>
            {colunas.map((c, i) => (
              <th key={i} style={{ ...cab, width: largura(c), padding: `${padding + 2}px 6px`, borderRight: gradeV }}>{nomeCampo(c)}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {linhas.map((l, i) => (
            <tr key={i} style={{ background: i % 2 === 0 ? fundo1 : fundo2 }}>
              {colunas.map((c, j) => (
                <td key={j} style={{ ...corpo, padding: `${padding}px 6px`, borderBottom: gradeH, borderRight: gradeV, textAlign: alinhamento(c) }}>
                  {l[refCampo(c)] === null || l[refCampo(c)] === undefined ? "" : formatarValor(l[refCampo(c)], formatos?.[refCampo(c)] ?? "texto")}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {d.estado !== "ok" && <div className="bi-tabela-estado"><EstadoDados d={d} /></div>}
    </div>
  );
}
