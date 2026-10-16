// Contêiner de visual do Power BI: posição absoluta no canvas 2048×1123, fundo, borda, sombra e título conforme
// visualContainerObjects (c.*). O conteúdo (gráfico, cartão…) recebe a área útil já descontado o título.
import type { CSSProperties, ReactNode } from "react";
import { COR_TEXTO, FONTE_TITULO, fonte, fundoPadrao, ler, pt, rgba, tituloAutomatico, tituloPadrao, type VisualPbi } from "./modelo";

export interface AreaUtil { largura: number; altura: number }

const ALINHAMENTO: Record<string, CSSProperties["textAlign"]> = { left: "left", center: "center", right: "right" };

export function sombraContenedor(v: VisualPbi): string | undefined {
  if (!ler(v.c, "dropShadow", "show", false)) return undefined;
  const cor = rgba(ler(v.c, "dropShadow", "color", "#000000"), ler(v.c, "dropShadow", "transparency", 70));
  const interna = ler<string>(v.c, "dropShadow", "position", "Outer") === "Inner";
  const preset = ler<string>(v.c, "dropShadow", "preset", "BottomRight");
  const d = /center/i.test(preset) ? [0, 0] : /top/i.test(preset) ? [3, -3] : [3, 3];
  return `${interna ? "inset " : ""}${d[0]}px ${d[1]}px 10px ${cor}`;
}

export function alturaTitulo(v: VisualPbi): number {
  if (!ler(v.c, "title", "show", tituloPadrao(v.type))) return 0;
  const tamanho = pt(Number(ler(v.c, "title", "fontSize", 12)));
  const quebra = ler(v.c, "title", "titleWrap", true);
  const texto = String(ler(v.c, "title", "text", tituloAutomatico(v)) ?? "");
  // Estimativa de linhas para títulos com quebra (largura média de caractere ≈ 0,55 × fonte).
  const linhas = quebra ? Math.max(1, Math.ceil((texto.length * tamanho * 0.55) / Math.max(40, v.w - 16))) : 1;
  return Math.round(linhas * tamanho * 1.35 + 8);
}

export function Contenedor({ v, children, onClick, titulo }: { v: VisualPbi; children?: ReactNode; onClick?: () => void; titulo?: string }) {
  const fundo = ler(v.c, "background", "show", fundoPadrao(v.type));
  const borda = ler(v.c, "border", "show", false);
  const raio = Number(ler(v.c, "border", "radius", 0)) || 0;
  const mostrarTitulo = ler(v.c, "title", "show", tituloPadrao(v.type));
  const textoTitulo = titulo ?? String(ler(v.c, "title", "text", tituloAutomatico(v)) ?? "");
  const hTitulo = mostrarTitulo ? alturaTitulo(v) : 0;
  const estilo: CSSProperties = {
    left: v.x, top: v.y, width: v.w, height: v.h, zIndex: v.z,
    background: fundo ? rgba(ler(v.c, "background", "color", "#FFFFFF"), ler(v.c, "background", "transparency", 0)) : undefined,
    border: borda ? `${Number(ler(v.c, "border", "width", 1))}px solid ${ler(v.c, "border", "color", "#CCCCCC")}` : undefined,
    borderRadius: raio || undefined,
    boxShadow: sombraContenedor(v),
    cursor: onClick ? "pointer" : undefined,
  };
  return (
    <div className="bi-v" data-visual={v.id} data-tipo={v.type} style={estilo} onClick={onClick}>
      {mostrarTitulo && (
        <div className="bi-v-titulo" style={{
          height: hTitulo, color: ler(v.c, "title", "fontColor", COR_TEXTO), fontSize: pt(Number(ler(v.c, "title", "fontSize", 12))),
          fontWeight: ler(v.c, "title", "bold", false) ? 700 : 400, fontFamily: fonte(ler(v.c, "title", "fontFamily"), FONTE_TITULO),
          textAlign: ALINHAMENTO[String(ler(v.c, "title", "alignment", "left")).toLowerCase()] ?? "left",
          background: rgba(ler(v.c, "title", "background")) ?? undefined,
          whiteSpace: ler(v.c, "title", "titleWrap", true) ? "normal" : "nowrap",
          borderTopLeftRadius: raio || undefined, borderTopRightRadius: raio || undefined,
        }}>{textoTitulo}</div>
      )}
      <div className="bi-v-corpo" style={{ top: hTitulo, borderRadius: raio && !mostrarTitulo ? raio : undefined }}>{children}</div>
    </div>
  );
}

/** Selo de pendência — nunca acompanhado de número. */
export function PendenteParidade({ motivo, compacto }: { motivo: string; compacto?: boolean }) {
  return (
    <div className={compacto ? "bi-pendente bi-pendente-compacto" : "bi-pendente"} title={motivo}>
      <span className="bi-pendente-selo">PENDENTE DE PARIDADE</span>
      {!compacto && <span className="bi-pendente-motivo">{motivo}</span>}
    </div>
  );
}

export function EstadoCarregando() {
  return <div className="bi-carregando" aria-label="Carregando dados" />;
}

export function EstadoErro({ mensagem }: { mensagem: string }) {
  return <div className="bi-erro" title={mensagem}>{mensagem}</div>;
}
