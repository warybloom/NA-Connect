/* Agrupamento PROJETO → FASE → DISCIPLINA da Gerência da Engenharia (multiprojeto). Componente isolado: recebe o
   Agrupamento pronto (projetoFaseLogic.ts) e só apresenta. Grupos expansíveis/recolhíveis (botões com aria-expanded),
   totais por projeto e por fase, linha "Não atribuído a fase" explícita e consolidado opcional. */
import { Fragment, useMemo, useState } from "react";
import { ChevronDown, ChevronRight, TriangleAlert } from "lucide-react";
import { formatCurrency, formatHH } from "@/components/pmo/pmoData";
import { diaMes, type Agrupamento, type ColunaAgrupamento, type GrupoFase, type GrupoProjeto, type Valores } from "./projetoFaseLogic";
import "./projetoFase.css";

const nf2 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const nf1 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
export function formatar(c: ColunaAgrupamento, v: number | null | undefined) {
  if (typeof v !== "number" || !Number.isFinite(v)) return "—";
  return c.formato === "inteiro" ? String(Math.round(v)) : c.formato === "hh" ? formatHH(v) : c.formato === "moeda" ? formatCurrency(v) : nf1.format(v) + "%";
}

export type VisaoMultiprojeto = "projeto-fase" | "consolidado";
/** Alternância da visão no modo multiprojeto: hierarquia por projeto/fase (padrão) ou consolidado por disciplina. */
export function SeletorVisaoMultiprojeto({ valor, onChange }: { valor: VisaoMultiprojeto; onChange: (v: VisaoMultiprojeto) => void }) {
  return <div className="pf-visao" role="radiogroup" aria-label="Visão multiprojeto">
    {([["projeto-fase", "POR PROJETO E FASE"], ["consolidado", "CONSOLIDADO POR DISCIPLINA"]] as const).map(([v, r]) =>
      <button key={v} type="button" role="radio" aria-checked={valor === v} className={valor === v ? "pf-visao-ativa" : undefined} onClick={() => onChange(v)}>{r}</button>)}
  </div>;
}

function Celulas({ colunas, valores }: { colunas: readonly ColunaAgrupamento[]; valores: Valores }) {
  return <>{colunas.map((c) => <td key={c.chave} data-label={c.rotulo}>{formatar(c, valores[c.chave])}</td>)}</>;
}

export default function ProjetoFaseAgrupamento({ titulo, agrupamento, idBase, detalheFase }: {
  titulo: string; agrupamento: Agrupamento; idBase: string;
  /** "programacao": cada fase pode abrir a lista de atividades (folha). */
  detalheFase?: "programacao";
}) {
  const { colunas, projetos, consolidado, notas } = agrupamento;
  const [fechados, setFechados] = useState<Set<string>>(() => new Set());
  const [abertas, setAbertas] = useState<Set<string>>(() => new Set()); // fases com disciplinas visíveis
  const [atividades, setAtividades] = useState<Set<string>>(() => new Set());
  const [mostrarConsolidado, setMostrarConsolidado] = useState(false);
  const alternar = (s: Set<string>, set: (x: Set<string>) => void, k: string) => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); set(n); };
  const todasFases = useMemo(() => projetos.flatMap((p) => p.fases.map((f) => `${p.projectId}|${f.chave}`)), [projetos]);
  const nColunas = colunas.length + 1;

  if (!projetos.length) return <section className="pf-bloco" aria-label={titulo}><header className="pf-titulo">{titulo}</header>
    <p className="pf-vazio">Nenhum dado para os projetos selecionados neste período.</p></section>;

  return <section className="pf-bloco" aria-label={titulo}>
    <header className="pf-titulo">
      <span>{titulo}</span>
      <div className="pf-acoes">
        <button type="button" onClick={() => { setFechados(new Set()); setAbertas(new Set(todasFases)); }}>Expandir tudo</button>
        <button type="button" onClick={() => { setFechados(new Set(projetos.map((p) => p.projectId))); setAbertas(new Set()); setAtividades(new Set()); }}>Recolher tudo</button>
        <label><input type="checkbox" checked={mostrarConsolidado} onChange={(e) => setMostrarConsolidado(e.target.checked)} /> Consolidado</label>
      </div>
    </header>
    <div className="pf-scroll" role="region" aria-label={`${titulo} — tabela`} tabIndex={0}>
      <table className="pf-tabela">
        <thead><tr><th scope="col">PROJETO / FASE / DISCIPLINA</th>{colunas.map((c) => <th key={c.chave} scope="col">{c.rotulo}</th>)}</tr></thead>
        {projetos.map((p) => <Projeto key={p.projectId} p={p} colunas={colunas} idBase={idBase} aberto={!fechados.has(p.projectId)}
          onAlternar={() => alternar(fechados, setFechados, p.projectId)} abertas={abertas} onFase={(k) => alternar(abertas, setAbertas, k)}
          atividades={atividades} onAtividades={(k) => alternar(atividades, setAtividades, k)} detalheFase={detalheFase} nColunas={nColunas} />)}
        {mostrarConsolidado && <tfoot><tr className="pf-consolidado"><th scope="row">CONSOLIDADO — {projetos.length} PROJETO(S)</th><Celulas colunas={colunas} valores={consolidado} /></tr></tfoot>}
      </table>
    </div>
    {notas.map((n) => <p key={n} className="pf-nota">{n}</p>)}
  </section>;
}

function Projeto({ p, colunas, idBase, aberto, onAlternar, abertas, onFase, atividades, onAtividades, detalheFase, nColunas }: {
  p: GrupoProjeto; colunas: readonly ColunaAgrupamento[]; idBase: string; aberto: boolean; onAlternar: () => void;
  abertas: Set<string>; onFase: (k: string) => void; atividades: Set<string>; onAtividades: (k: string) => void; detalheFase?: "programacao"; nColunas: number;
}) {
  const id = `${idBase}-${p.codigo.replace(/[^A-Za-z0-9]/g, "")}-${p.projectId.slice(-6)}`;
  return <tbody className="pf-projeto" id={id}>
    <tr className="pf-linha-projeto">
      <th scope="rowgroup">
        <button type="button" aria-expanded={aberto} onClick={onAlternar} title={p.projeto}>
          {aberto ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}
          <b>{p.codigo}</b><span className="pf-nome-projeto">{p.projeto}</span>
          {p.tipoLdp !== null && <small>LDP TIPO {p.tipoLdp}</small>}
        </button>
      </th>
      <Celulas colunas={colunas} valores={p.totais} />
    </tr>
    {aberto && p.fases.map((f) => <Fase key={f.chave} p={p} f={f} colunas={colunas} aberta={abertas.has(`${p.projectId}|${f.chave}`)}
      onAlternar={() => onFase(`${p.projectId}|${f.chave}`)} comAtividades={detalheFase === "programacao" && f.detalhes.length > 0}
      atividadesAbertas={atividades.has(`${p.projectId}|${f.chave}`)} onAtividades={() => onAtividades(`${p.projectId}|${f.chave}`)} idBase={`${id}-fases`} nColunas={nColunas} />)}
    {aberto && p.naoAtribuido && <tr className="pf-linha-nao-atribuido" title="Diferença entre o total oficial do projeto e a soma das fases; exibida para manter o total auditável.">
      <th scope="row"><span className="pf-recuo-1">{p.codigo} · NÃO ATRIBUÍDO A FASE <TriangleAlert size={12} aria-hidden /></span></th>
      <Celulas colunas={colunas} valores={p.naoAtribuido} /></tr>}
    {aberto && p.notas.map((n) => <tr key={n} className="pf-linha-nota"><td colSpan={nColunas}>{p.codigo}: {n}</td></tr>)}
  </tbody>;
}

function Fase({ p, f, colunas, aberta, onAlternar, comAtividades, atividadesAbertas, onAtividades, idBase, nColunas }: {
  p: GrupoProjeto; f: GrupoFase; colunas: readonly ColunaAgrupamento[]; aberta: boolean; onAlternar: () => void;
  comAtividades: boolean; atividadesAbertas: boolean; onAtividades: () => void; idBase: string; nColunas: number;
}) {
  const id = `${idBase}-${f.chave.replace(/[^A-Za-z0-9]/g, "") || "x"}`;
  return <Fragment>
    <tr className={"pf-linha-fase" + (f.semFase ? " pf-sem-fase" : "")} id={id}>
      <th scope="row">
        <button type="button" className="pf-recuo-1" aria-expanded={aberta} onClick={onAlternar}>
          {aberta ? <ChevronDown size={13} aria-hidden /> : <ChevronRight size={13} aria-hidden />}
          <span className="pf-codigo">{p.codigo}</span> · {f.fase} <small>({f.disciplinas.length} disciplina(s))</small>
        </button>
      </th>
      <Celulas colunas={colunas} valores={f.totais} />
    </tr>
    {aberta && f.disciplinas.map((d) => <tr key={d.chave} className="pf-linha-disciplina" title={d.diagnosticos.join(" · ") || undefined}>
      <th scope="row"><span className="pf-recuo-2"><span className="pf-codigo">{p.codigo}</span> · {f.fase} · {d.disciplina}
        {d.diagnosticos.length > 0 && <TriangleAlert size={11} className="pf-alerta" aria-label={d.diagnosticos.join(" · ")} />}</span></th>
      <Celulas colunas={colunas} valores={d.valores} />
    </tr>)}
    {aberta && comAtividades && <tr className="pf-linha-atividades"><td colSpan={nColunas}>
      <button type="button" className="pf-recuo-2" aria-expanded={atividadesAbertas} onClick={onAtividades}>
        {atividadesAbertas ? <ChevronDown size={12} aria-hidden /> : <ChevronRight size={12} aria-hidden />} Atividades de {p.codigo} · {f.fase} ({f.detalhes.length})
      </button>
      {atividadesAbertas && <div className="pf-atividades"><table>
        <thead><tr>{["PROJETO", "FASE", "DIA", "DOCUMENTO", "TAREFA", "DISCIPLINA", "ELABORADOR", "VERIFICADOR", "APROVADOR", "SITUAÇÃO", "HH"].map((h) => <th key={h} scope="col">{h}</th>)}</tr></thead>
        <tbody>{f.detalhes.map((a) => <tr key={a.chave}>
          <td>{p.codigo}</td><td>{f.fase}</td><td>{diaMes(a.data)}</td><td>{a.documento ?? "—"}</td><td className="pf-texto-longo" title={a.tarefa ?? undefined}>{a.tarefa ?? "—"}</td>
          <td>{a.disciplina}</td><td>{a.elaborador ?? "—"}</td><td>{a.verificador ?? "—"}</td><td>{a.aprovador ?? "—"}</td><td>{a.status ?? "—"}</td>
          <td>{typeof a.hh === "number" ? nf2.format(a.hh) : "—"}</td></tr>)}</tbody>
      </table></div>}
    </td></tr>}
  </Fragment>;
}
