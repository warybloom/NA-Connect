// Canvas da página do Power BI (displayOption FitToPage): a página inteira 2048×1123 é escalada para caber na área
// disponível, centralizada sobre a cor "outspace" da página. Visuais posicionados em px do canvas original.
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ler, type PaginaPbi } from "./pbi/modelo";
import { RECURSOS, urlRecurso } from "./pbi/VisuaisEstaticos";

export function BiChs001Canvas({ pagina, children }: { pagina: PaginaPbi; children: ReactNode }) {
  const caixa = useRef<HTMLDivElement>(null);
  const [escala, setEscala] = useState(0);
  useEffect(() => {
    const el = caixa.current;
    if (!el) return;
    const medir = () => {
      const r = el.getBoundingClientRect();
      setEscala(Math.max(0.1, Math.min(r.width / pagina.largura, r.height / pagina.altura)));
    };
    medir();
    const ro = new ResizeObserver(medir);
    ro.observe(el);
    return () => ro.disconnect();
  }, [pagina.largura, pagina.altura]);

  const outspace = ler<string>(pagina.objetos, "outspace", "color", "#FFFFFF");
  const imagemFundo = ler<{ image?: { url?: unknown; scaling?: string } }>(pagina.objetos, "background", "image")?.image;
  const urlFundo = urlRecurso(imagemFundo?.url) ?? RECURSOS.fundo;
  const transparencia = Number(ler(pagina.objetos, "background", "transparency", 0));
  const alinhamentoV = String(ler(pagina.objetos, "displayArea", "verticalAlignment", "Top"));

  return (
    <div ref={caixa} className="bi-chs001-area" style={{ background: outspace, alignItems: alinhamentoV === "Middle" ? "center" : "flex-start" }}>
      {escala > 0 && (
        <div className="bi-chs001-escala" style={{ width: pagina.largura * escala, height: pagina.altura * escala }}>
          <div className="bi-chs001-pagina" data-pagina={pagina.id}
            style={{ width: pagina.largura, height: pagina.altura, transform: `scale(${escala})` }}>
            <img className="bi-chs001-fundo" src={urlFundo} alt="" draggable={false}
              style={{ objectFit: imagemFundo?.scaling === "Fill" ? "cover" : imagemFundo?.scaling === "Normal" ? "contain" : "fill", opacity: 1 - transparencia / 100 }} />
            {children}
          </div>
        </div>
      )}
    </div>
  );
}
