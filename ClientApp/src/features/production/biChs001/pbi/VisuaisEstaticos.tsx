// Visuais sem dados do modelo: shape, image, textbox e actionButton — reproduzidos a partir das propriedades do PBIR.
// Logos CHS e Whitefox (recurso "LOGO_NA") NÃO são desenhadas: a única logo da tela é a da barra lateral do N&A Connect
// (decisão do usuário 15/10/2026 — antes viravam 2 cópias da logo N&A no canvas, repetindo a da sidebar).
import type { CSSProperties, ReactNode } from "react";
import { FONTE_PADRAO, fonte, ler, pt, rgba, type Paragrafo, type VisualPbi } from "./modelo";
import { Contenedor } from "./Contenedor";
import fundo from "../assets/fundo.png";
import casa from "../assets/casa.png";
import schedule from "../assets/schedule.png";
import start from "../assets/start.png";
import capaPlanta from "../assets/capa-planta.png";
import naUsa from "../assets/na-usa.png";

/** Recursos do PBIX (generated/*.json → {recurso}) → arquivo do módulo. */
export const RECURSOS: Record<string, string> = {
  fundo, casa, schedule, start, capaPlanta, naUsa,
};

/** Recurso que marca as logos de cliente/parceiro do PBIX (CHS/Whitefox): o visual é omitido no canvas. */
export const RECURSO_LOGO_OMITIDA = "LOGO_NA";
export const ehLogoOmitida = (v: VisualPbi): boolean =>
  v.type === "image" && (ler<{ image?: { url?: { recurso?: string } } }>(v.o, "image", "sourceFile")?.image?.url?.recurso === RECURSO_LOGO_OMITIDA);

export const urlRecurso = (r: unknown): string | undefined =>
  r && typeof r === "object" && "recurso" in r ? RECURSOS[String((r as { recurso: string }).recurso)] : undefined;

/** visualLink do contêiner (navegação de página / bookmark). */
export interface Link { tipo: string; destino?: string }
export function linkDoVisual(v: VisualPbi): Link | undefined {
  const show = ler(v.c, "visualLink", "show", false);
  if (!show) return undefined;
  const tipo = String(ler(v.c, "visualLink", "type", ""));
  if (tipo === "PageNavigation") return { tipo, destino: ler(v.c, "visualLink", "navigationSection") };
  if (tipo === "Bookmark") return { tipo, destino: ler(v.c, "visualLink", "bookmark") };
  if (tipo === "WebUrl") return { tipo, destino: ler(v.c, "visualLink", "webUrl") };
  return { tipo };
}

// ---------------------------------------------------------------------------------------------- shape

function sombraForma(v: VisualPbi): string | undefined {
  if (!ler(v.o, "shadow", "show", false)) return undefined;
  const cor = rgba(ler(v.o, "shadow", "color", "#000000"), ler(v.o, "shadow", "transparency", 70));
  const blur = Number(ler(v.o, "shadow", "shadowBlur", 10));
  const preset = String(ler(v.o, "shadow", "shadowPositionPreset", "bottomRight"));
  const d = /center/i.test(preset) ? [0, 0] : /topLeft/i.test(preset) ? [-4, -4] : /top/i.test(preset) ? [4, -4] : [4, 4];
  return `${d[0]}px ${d[1]}px ${blur}px ${cor}`;
}

const ALINH_H: Record<string, CSSProperties["justifyContent"]> = { left: "flex-start", center: "center", right: "flex-end" };
const ALINH_V: Record<string, CSSProperties["alignItems"]> = { top: "flex-start", middle: "center", bottom: "flex-end" };

export function Forma({ v, onClick }: { v: VisualPbi; onClick?: () => void }) {
  const tile = String(ler(v.o, "shape", "tileShape", "rectangle"));
  const preenche = ler(v.o, "fill", "show", true);
  const corFundo = preenche ? rgba(ler(v.o, "fill", "fillColor", "#E6E6E6"), ler(v.o, "fill", "transparency", 0)) : "transparent";
  const contorno = ler(v.o, "outline", "show", true);
  const espessura = Number(ler(v.o, "outline", "weight", 1));
  const corContorno = rgba(ler(v.o, "outline", "lineColor", "#000000"), ler(v.o, "outline", "transparency", 0));
  const angulo = Number(ler(v.o, "rotation", "shapeAngle", ler(v.o, "rotation", "angle", 0))) || 0;
  const texto = ler(v.o, "text", "show", false) ? String(ler(v.o, "text", "text", "")) : "";

  if (tile === "line") {
    // Linha: traço horizontal no meio da caixa (rotacionado pelo ângulo), cor/espessura do contorno ou preenchimento.
    const cor = corContorno ?? corFundo;
    return (
      <Contenedor v={v} onClick={onClick}>
        <div style={{ position: "absolute", left: 0, right: 0, top: "50%", height: Math.max(1, espessura), marginTop: -Math.max(1, espessura) / 2,
          background: cor, transform: angulo ? `rotate(${angulo}deg)` : undefined }} />
      </Contenedor>
    );
  }

  const raio = tile === "rectangleRounded" ? Number(ler(v.o, "shape", "rectangleRoundedCurve", 0)) : 0;
  return (
    <Contenedor v={v} onClick={onClick}>
      <div className="bi-forma" style={{
        background: corFundo,
        border: contorno ? `${espessura}px solid ${corContorno}` : undefined,
        borderRadius: raio || undefined,
        boxShadow: sombraForma(v),
        transform: angulo ? `rotate(${angulo}deg)` : undefined,
        justifyContent: ALINH_H[String(ler(v.o, "text", "horizontalAlignment", "center")).toLowerCase()] ?? "center",
        alignItems: ALINH_V[String(ler(v.o, "text", "verticalAlignment", "middle")).toLowerCase()] ?? "center",
      }}>
        {texto && (
          <span style={{
            color: ler(v.o, "text", "fontColor", "#252423"), fontSize: pt(Number(ler(v.o, "text", "fontSize", 12))),
            fontWeight: ler(v.o, "text", "bold", false) ? 700 : 400, fontFamily: fonte(ler(v.o, "text", "fontFamily")),
            textAlign: (String(ler(v.o, "text", "horizontalAlignment", "center")).toLowerCase() as CSSProperties["textAlign"]),
          }}>{texto}</span>
        )}
      </div>
    </Contenedor>
  );
}

// ---------------------------------------------------------------------------------------------- image

const AJUSTE: Record<string, CSSProperties["objectFit"]> = { Normal: "contain", Fit: "fill", Fill: "cover" };

export function Imagem({ v, onClick }: { v: VisualPbi; onClick?: () => void }) {
  const sf = ler<{ image?: { url?: unknown; scaling?: string; name?: string } }>(v.o, "image", "sourceFile");
  const url = urlRecurso(sf?.image?.url) ?? urlRecurso(ler(v.o, "general", "imageUrl"));
  const escala = String(ler(v.o, "image", "fit", sf?.image?.scaling ?? "Normal"));
  if (ehLogoOmitida(v)) return null;
  return (
    <Contenedor v={v} onClick={onClick}>
      {url && (
        <img className="bi-imagem" src={url} alt={sf?.image?.name ?? ""} draggable={false}
          style={{ objectFit: AJUSTE[escala] ?? "contain" }} />
      )}
    </Contenedor>
  );
}

// -------------------------------------------------------------------------------------------- textbox

function tamanhoFonte(s: string | undefined, padraoPt: number): number {
  if (!s) return pt(padraoPt);
  const m = /^([\d.]+)(pt|px)?$/.exec(s.trim());
  if (!m) return pt(padraoPt);
  return m[2] === "px" ? Number(m[1]) : pt(Number(m[1]));
}

export function CaixaTexto({ v }: { v: VisualPbi }) {
  const paragrafos = ler<Paragrafo[]>(v.o, "general", "paragraphs", []) ?? [];
  const blocos: ReactNode[] = [];
  let lista: { tipo: string; itens: ReactNode[] } | null = null;
  const fecharLista = () => {
    if (!lista) return;
    const Tag = lista.tipo === "number" ? "ol" : "ul";
    blocos.push(<Tag key={`l${blocos.length}`} className="bi-texto-lista">{lista.itens}</Tag>);
    lista = null;
  };
  paragrafos.forEach((pg, i) => {
    const runs = pg.textRuns.map((r, j) => {
      const st = r.textStyle ?? {};
      return (
        <span key={j} style={{
          fontSize: tamanhoFonte(st.fontSize, 10), fontWeight: st.fontWeight === "bold" ? 700 : undefined,
          fontStyle: st.fontStyle as CSSProperties["fontStyle"], textDecoration: st.textDecoration,
          color: st.color ?? "#252423", fontFamily: st.fontFamily ? fonte(st.fontFamily) : undefined,
        }}>{r.value === "" ? "​" : r.value}</span>
      );
    });
    const alinhamento = (pg.horizontalTextAlignment ?? "left") as CSSProperties["textAlign"];
    if (pg.listType) {
      if (!lista || lista.tipo !== pg.listType) { fecharLista(); lista = { tipo: pg.listType, itens: [] }; }
      lista.itens.push(<li key={i} style={{ textAlign: alinhamento, marginLeft: (pg.indentLevel ?? 0) * 24 }}>{runs}</li>);
      return;
    }
    fecharLista();
    blocos.push(<p key={i} className="bi-texto-p" style={{ textAlign: alinhamento }}>{runs}</p>);
  });
  fecharLista();
  return (
    <Contenedor v={v}>
      <div className="bi-texto" style={{ fontFamily: FONTE_PADRAO }}>{blocos}</div>
    </Contenedor>
  );
}

// ------------------------------------------------------------------------------------------ actionButton

function IconeBotao({ tipo, cor }: { tipo: string; cor: string }) {
  if (tipo === "back")
    return (
      <svg viewBox="0 0 24 24" width="60%" height="60%" fill="none" stroke={cor} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <circle cx="12" cy="12" r="10" />
        <path d="M13.5 7.5 9 12l4.5 4.5M9 12h8" />
      </svg>
    );
  return null;
}

export function BotaoAcao({ v, onClick }: { v: VisualPbi; onClick?: () => void }) {
  const preenche = ler(v.o, "fill", "show", false);
  const imagem = ler<{ image?: { url?: unknown; scaling?: string } }>(v.o, "fill", "image");
  const urlImagem = urlRecurso(imagem?.image?.url);
  const transparencia = Number(ler(v.o, "fill", "transparency", 0));
  const corFundo = preenche && !urlImagem ? rgba(ler(v.o, "fill", "fillColor", "#FFFFFF"), transparencia) : undefined;
  const icone = String(ler(v.o, "icon", "shapeType", "blank"));
  return (
    <Contenedor v={v} onClick={onClick}>
      <div className="bi-botao" style={{ background: corFundo }}>
        {preenche && urlImagem && (
          <img src={urlImagem} alt="" draggable={false} className="bi-imagem"
            style={{ objectFit: AJUSTE[imagem?.image?.scaling ?? "Fill"] ?? "cover", opacity: 1 - transparencia / 100 }} />
        )}
        <IconeBotao tipo={icone} cor={String(ler(v.o, "icon", "lineColor", "#FFFFFF"))} />
      </div>
    </Contenedor>
  );
}
