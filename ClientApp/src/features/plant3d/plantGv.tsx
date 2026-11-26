// Peças visuais no estilo Gestão à Vista para as telas Plant 3D (Resumo e telas de fase).
// Reusa o Gauge e o CSS/tokens do GV (features/production/analytics, o mesmo que a TV exibe) SEM editá-los; o anel e a barra
// empilhada seguem a mesma linguagem. Nenhum percentual é calculado aqui: quem chama passa o valor ou null ("—").
import type { ReactNode } from "react";
import "@/features/production/analytics/gestaoVistaJamile.css";
import "./plantGv.css";

export { Gauge as GvGauge } from "@/features/production/analytics/GestaoVistaJamilePage";

export const GvWrap = ({ children }: { children: ReactNode }) => <div className="gv-shell p3d-gv" data-testid="plant-gv">{children}</div>;

export function GvFileira({ titulo, subtitulo, children, id }: { titulo: string; subtitulo?: ReactNode; children: ReactNode; id?: string }) {
  return (
    <section className="p3d-fileira" data-testid={id}>
      <header><b>{titulo}</b>{subtitulo && <small>{subtitulo}</small>}</header>
      <div className="p3d-fileira-corpo">{children}</div>
    </section>
  );
}

export type GvTom = "ok" | "erro" | "nd" | "alt";
export type GvParte = { valor: number; tom: GvTom; rotulo: string };

const R = 42;

/** Anel (rosca). Sem partes com valor → trilho tracejado (estado "aguardando"): nunca desenha percentual. */
export function GvAnel({ titulo, partes, centro, sub, status, legenda }: {
  titulo: string; partes: GvParte[]; centro: string; sub?: string; status?: string; legenda?: ReactNode;
}) {
  const total = partes.reduce((n, p) => n + p.valor, 0);
  let acumulado = 0;
  return (
    <div className="p3d-anel">
      <p className="p3d-anel-titulo">{titulo}</p>
      <svg viewBox="0 0 120 120" role="img" aria-label={`${titulo}: ${centro}${sub ? " " + sub : ""}${status ? " — " + status : ""}`}>
        <circle cx="60" cy="60" r={R} strokeWidth="13" className={"p3d-anel-trilho" + (total === 0 ? " p3d-anel-trilho-vazio" : "")} fill="none" />
        {total > 0 && partes.filter(p => p.valor > 0).map(p => {
          const len = (p.valor / total) * 100;
          const el = (
            <circle key={p.rotulo} cx="60" cy="60" r={R} strokeWidth="13" pathLength={100} className={`p3d-seg p3d-seg-${p.tom}`}
              strokeDasharray={`${len} ${100 - len}`} strokeDashoffset={-acumulado} transform="rotate(-90 60 60)">
              <title>{`${p.rotulo}: ${p.valor.toLocaleString("pt-BR")}`}</title>
            </circle>
          );
          acumulado += len;
          return el;
        })}
        <text x="60" y={sub ? 62 : 66} textAnchor="middle" className="p3d-anel-centro">{centro}</text>
        {sub && <text x="60" y="76" textAnchor="middle" className="p3d-anel-sub">{sub}</text>}
      </svg>
      {status && <p className="p3d-anel-status">{status}</p>}
      {legenda && <p className="p3d-anel-legenda">{legenda}</p>}
    </div>
  );
}

const CLASSE_COR: Record<GvTom, string> = { ok: "p3d-cor-pid", erro: "p3d-cor-erro", nd: "p3d-cor-nd", alt: "p3d-cor-3d" };

/** Barra empilhada fina (ex.: tipos de problema). Sem total → caixa tracejada. */
export function GvPilha({ partes, rotulo }: { partes: Array<{ valor: number; cor: string; rotulo: string }>; rotulo: string }) {
  const total = partes.reduce((n, p) => n + p.valor, 0);
  if (total === 0) return <div className="p3d-pilha p3d-pilha-vazia" role="img" aria-label={`${rotulo}: sem ocorrências`} />;
  return (
    <div className="p3d-pilha" role="img" aria-label={`${rotulo}: ` + partes.map(p => `${p.rotulo} ${p.valor}`).join(", ")}>
      {partes.filter(p => p.valor > 0).map(p => <i key={p.rotulo} className={p.cor} style={{ width: `${(p.valor / total) * 100}%` }} title={`${p.rotulo}: ${p.valor}`} />)}
    </div>
  );
}

export function GvLegenda({ itens }: { itens: Array<{ cor: string; rotulo: string; valor?: string }> }) {
  return (
    <div className="gv-legend">
      {itens.map(i => <span key={i.rotulo}><i className={i.cor} />{i.rotulo}{i.valor !== undefined ? ` ${i.valor}` : ""}</span>)}
    </div>
  );
}

export const classeCorTom = (t: GvTom) => CLASSE_COR[t];
export const GvAviso = ({ children, neutro }: { children: ReactNode; neutro?: boolean }) =>
  <p className={"p3d-aviso" + (neutro ? " p3d-aviso-neutro" : "")}>{children}</p>;
