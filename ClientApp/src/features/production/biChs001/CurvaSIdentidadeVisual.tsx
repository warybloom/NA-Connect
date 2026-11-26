/**
 * Identidade visual ÚNICA da Curva S em qualquer projeto.
 * Utiliza exatamente o mesmo GraficoCombo do PBIX, não um segundo renderer.
 * A fonte de dados é uma preocupação separada (WBX do CHS001 ou LDP dos demais).
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { GraficoCombo } from "./pbi/Graficos";
import type { VisualPbi } from "./pbi/modelo";
import type { DadosVisual } from "./dados/tipos";
import "./biChs001.css";

export function CurvaSIdentidadeVisual({
  subtitulo, controles, visual, dados, aviso, marcoFisicoRef, rotuloFisico, descricaoFisico, idTeste = "curva-s-identidade-bi",
}: {
  subtitulo: ReactNode;
  controles?: ReactNode;
  visual?: VisualPbi;
  dados?: DadosVisual;
  aviso?: ReactNode;
  marcoFisicoRef?: string;
  rotuloFisico?: string;
  descricaoFisico?: string;
  idTeste?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [largura, setLargura] = useState(980);

  useEffect(() => {
    const elemento = ref.current;
    if (!elemento) return;
    const medir = () => setLargura(Math.max(320, Math.round(elemento.clientWidth)));
    medir();
    const ro = new ResizeObserver(medir);
    ro.observe(elemento);
    return () => ro.disconnect();
  }, []);

  const pronto = visual && dados?.estado === "ok" && dados.tipo === "pontos";
  return (
    <section className="space-y-3" data-testid={idTeste}>
      <header className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-card p-4">
        <div>
          <h2 className="text-base font-semibold text-foreground">Curva S</h2>
          <p className="text-xs text-muted-foreground">{subtitulo}</p>
        </div>
        {controles ? <div className="flex flex-wrap gap-1 rounded-md border border-border p-1">{controles}</div> : null}
      </header>
      {marcoFisicoRef && pronto && (() => {
        const p = dados.pontos.findLast(p => p.valores[marcoFisicoRef] != null);
        const valor = p ? p.valores[marcoFisicoRef] : null;
        return <div className="rounded-xl border border-rose-300 bg-white px-4 py-3 text-slate-900" data-testid="curva-s-avanco-fisico-oficial">
          <div className="text-xs font-bold uppercase tracking-wide text-rose-700">{rotuloFisico ?? "Avanço físico realizado"}</div>
          <div className="text-3xl font-extrabold text-rose-700">{typeof valor === "number" ? `${(valor * 100).toLocaleString("pt-BR", {minimumFractionDigits:2,maximumFractionDigits:2})}%` : "Indisponível"}</div>
          <div className="text-xs text-slate-600">{descricaoFisico ?? "Medição física atual, sem série temporal histórica certificada."}</div>
        </div>;
      })()}
      <div className="rounded-xl border border-border bg-white p-3 shadow-sm" data-testid="curva-s-grafico-pbix">
        <div className="mb-3 text-center text-sm font-semibold text-slate-900">PROGRESS CURVE</div>
        <div ref={ref} className="w-full overflow-x-auto">
          {pronto ? (
            <GraficoCombo v={visual} largura={largura} altura={360}
              pontos={dados.pontos} formatos={dados.formatos} rotulosPercentuais />
          ) : (
            <div className="min-h-[360px] p-8 text-sm text-slate-700" role={dados?.estado === "erro" ? "alert" : undefined}>
              {dados?.estado === "erro" ? dados.mensagem
                : dados?.estado === "pendente" ? dados.motivo
                : "Carregando a Curva S…"}
            </div>
          )}
        </div>
      </div>
      {aviso ? <div className="text-xs text-muted-foreground">{aviso}</div> : null}
    </section>
  );
}
