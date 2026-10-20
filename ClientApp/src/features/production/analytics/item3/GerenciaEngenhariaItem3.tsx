import { useId, type ReactNode } from "react";
import "./gerenciaEngenhariaItem3.css";

export interface ItemSemanaEngenharia {
  key: string;
  data: string;
  dia: string;
  disciplina: string;
  codigoDocumento: string | null;
  tituloDocumento: string | null;
  elaborador: string | null;
  verificador: string | null;
  aprovador: string | null;
  situacao: string | null;
  hhPrevisto: number | null;
}

export interface GrupoDisciplinaEngenharia {
  key: string;
  nome: string;
  entregaveis: number;
  hh: number | null;
}

export interface GerenciaEngenhariaItem3Props {
  itens: readonly ItemSemanaEngenharia[];
  grupos: readonly GrupoDisciplinaEngenharia[];
  agruparPorDia: boolean;
  onAgruparPorDia: (agrupar: boolean) => void;
  descricaoPeriodo: string;
  /** Padrão "SEMANA COMPLETA"; o modo histórico usa outro título para não se passar pela semana atual. */
  titulo?: string;
  carregando?: boolean;
  mensagemVazia?: ReactNode;
  aviso?: ReactNode;
  rodape?: ReactNode;
}

const fmt = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2 });
const valor = (n: number | null) => n === null ? "—" : fmt.format(n);

function porData(itens: readonly ItemSemanaEngenharia[]) {
  const grupos = new Map<string, ItemSemanaEngenharia[]>();
  for (const item of itens) {
    const grupo = grupos.get(item.data) ?? [];
    grupo.push(item);
    grupos.set(item.data, grupo);
  }
  return [...grupos];
}

function GraficoEntregaveis({ grupos }: { grupos: readonly GrupoDisciplinaEngenharia[] }) {
  const max = Math.max(1, ...grupos.map(g => g.entregaveis));
  return <div className="ge3-bars" role="img" aria-label="Documentos por disciplina">
    {grupos.map(g => <div className="ge3-bar-row" key={g.key} title={`${g.nome}: ${g.entregaveis}`}>
      <span>{g.nome}</span><div><i style={{ width: `${g.entregaveis / max * 100}%` }} /><b>{g.entregaveis}</b></div>
    </div>)}
  </div>;
}

function GraficoHh({ grupos }: { grupos: readonly GrupoDisciplinaEngenharia[] }) {
  const max = Math.max(1, ...grupos.map(g => g.hh ?? 0));
  return <div className="ge3-hh-scroll">
    <div className="ge3-hh" role="img" aria-label="HH por disciplina">
      {grupos.map(g => <div className="ge3-hh-col" key={g.key} title={`${g.nome}: ${g.hh === null ? "HH não informado na programação" : fmt.format(g.hh) + " h"}`}>
        <div><i style={{ height: `${(g.hh ?? 0) / max * 100}%` }}><b>{valor(g.hh)}</b></i></div>
        <span>{g.nome}</span>
      </div>)}
    </div>
  </div>;
}

export function GerenciaEngenhariaItem3({
  itens, grupos, agruparPorDia, onAgruparPorDia, descricaoPeriodo, titulo = "SEMANA COMPLETA",
  carregando = false, mensagemVazia, aviso, rodape,
}: GerenciaEngenhariaItem3Props) {
  const tituloId = useId();
  const blocos = agruparPorDia ? porData(itens) : [["", [...itens]] as [string, ItemSemanaEngenharia[]]];

  return <div className="ge3-layout">
    <section className="ge3-week" aria-labelledby={tituloId}>
      <div className="ge3-tools">
        <strong id={tituloId}>{titulo}</strong>
        <span>{descricaoPeriodo} · {itens.length} atividade(s)</span>
        <label><input type="checkbox" checked={agruparPorDia} onChange={e => onAgruparPorDia(e.target.checked)} /> AGRUPAR POR DIA</label>
      </div>
      <div className="ge3-table-scroll">
        <table>
          <thead><tr>{[
            "DIA", "DISCIPLINA", "CÓDIGO DO DOCUMENTO", "TÍTULO DO DOCUMENTO",
            "ELABORADOR", "VERIFICADOR", "APROVADOR", "SITUAÇÃO", "HH PREVISTO",
          ].map(coluna => <th key={coluna}>{coluna}</th>)}</tr></thead>
          <tbody>{blocos.flatMap(([data, linhas]) => [
            ...(agruparPorDia ? [<tr className="ge3-day-row" key={`dia-${data}`}><td colSpan={9}>
              {linhas[0]?.dia} · {linhas.length} atividade(s) · {valor(linhas.some(x => x.hhPrevisto !== null) ? linhas.reduce((s, x) => s + (x.hhPrevisto ?? 0), 0) : null)} h
            </td></tr>] : []),
            ...linhas.map(item => <tr key={item.key}>
              <td>{item.dia}</td><td>{item.disciplina}</td><td>{item.codigoDocumento ?? "—"}</td>
              <td className="ge3-title-cell"><span title={item.tituloDocumento ?? undefined}>{item.tituloDocumento ?? "—"}</span></td>
              <td>{item.elaborador ?? "—"}</td><td>{item.verificador ?? "—"}</td><td>{item.aprovador ?? "—"}</td>
              <td>{item.situacao ?? "—"}</td><td>{valor(item.hhPrevisto)}</td>
            </tr>),
          ])}</tbody>
        </table>
        {carregando && <p className="ge3-state">Consultando a programação…</p>}
        {!carregando && !itens.length && <div className="ge3-state">{mensagemVazia ?? "Nenhuma atividade no período."}</div>}
      </div>
      {aviso && <div className="ge3-warning">{aviso}</div>}
    </section>

    <div className="ge3-discipline-grid">
      <section className="ge3-panel"><h3>DOCUMENTOS POR DISCIPLINA</h3>
        {grupos.length ? <GraficoEntregaveis grupos={grupos} /> : <p className="ge3-state">—</p>}
      </section>
      <section className="ge3-panel"><h3>HH POR DISCIPLINA</h3>
        {grupos.length ? <GraficoHh grupos={grupos} /> : <p className="ge3-state">—</p>}
      </section>
    </div>
    {rodape && <div className="ge3-footer">{rodape}</div>}
  </div>;
}
