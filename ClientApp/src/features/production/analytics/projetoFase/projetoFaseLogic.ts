/* GERÊNCIA DA ENGENHARIA — MULTIPROJETO: hierarquia PROJETO → FASE → DISCIPLINA (19/10/2026).
   Fase = somente campo REAL já presente no modelo:
     • documentos/entregas LDP Tipo 2: gate ativo (gates[].percentual → "FASE 30%" | "FASE 60%" | "FASE 90%");
     • programação semanal: coluna Weekly_Phase (`fase` da linha), texto como veio da fonte.
   Sem esse campo (LDP Tipo 1, documento Tipo 2 sem gate ativo, programação sem fase, Timesheet) → "Sem fase definida".
   Nunca se infere fase pelo nome/código. Cada projeto usa SEU catálogo de disciplinas (rótulo original da Norma do
   projeto); nada de um projeto é somado em outro. O normalizador de siglas NÃO entra aqui (só no consolidado). */
import type { FactsReport } from "@/components/pmo/pmoFacts";
import { projectFacts } from "@/components/pmo/pmoFacts";
import { somaConhecida } from "@/components/pmo/pmoMultiCore";
import {
  classificarDocumento, classificarProgramacao, classificarRealizado, criarCatalogo, dataISO, deduplicarProgramacao, entregasDaSemana, filtrarDia,
  hhLinha, rotulo, SEM_DISCIPLINA, type CatalogoDisciplinas, type ProgramacaoLinha, type ProgramacaoResposta,
} from "../gestaoVistaJamileLogic";

export const SEM_FASE = "__SEM_FASE__";
export const SEM_FASE_ROTULO = "Sem fase definida";
export type FormatoColuna = "inteiro" | "hh" | "moeda" | "pct";
/** razao = [numerador, denominador] → percentual recalculado em cada nível (nunca média de percentuais). */
export interface ColunaAgrupamento { chave: string; rotulo: string; formato: FormatoColuna; razao?: [string, string] }
export type Valores = Record<string, number | null>;
export interface LinhaDisciplina { chave: string; disciplina: string; valores: Valores; diagnosticos: string[] }
export interface GrupoFase { chave: string; fase: string; semFase: boolean; disciplinas: LinhaDisciplina[]; totais: Valores; detalhes: DetalheAtividade[] }
export interface GrupoProjeto {
  projectId: string; projeto: string; codigo: string; tipoLdp: number | null; fases: GrupoFase[];
  /** Total do PROJETO: soma das fases ou, quando as fases se sobrepõem (gates), o total oficial que conta cada item uma vez. */
  totais: Valores;
  /** Diferença explícita entre o total oficial e a soma das fases (valor não atribuível a uma fase). null = sem diferença. */
  naoAtribuido: Valores | null;
  notas: string[];
}
export interface Agrupamento { colunas: ColunaAgrupamento[]; projetos: GrupoProjeto[]; consolidado: Valores; notas: string[] }
/** Atividade da programação (folha opcional do grupo de fase). */
export interface DetalheAtividade { chave: string; data: string; documento: string | null; tarefa: string | null; disciplina: string;
  elaborador: string | null; verificador: string | null; aprovador: string | null; status: string | null; hh: number | null }

interface Item { fase: string | null; disciplina: string; rotulo: string; valores: Partial<Valores>; diagnostico?: string | null; detalhe?: DetalheAtividade }
interface ProjetoEntrada { projectId: string; projeto: string; tipoLdp: number | null; itens: Item[]; totaisOficiais?: Valores; reconciliar?: boolean; notas?: string[] }

const TOLERANCIA = 0.005;
const normalizar = (s: string) => s.normalize("NFC").replace(/\s+/g, " ").trim();
export const rotuloGate = (percentual: number) => `FASE ${percentual}%`;
/** Fase da programação: o texto real da coluna Weekly_Phase (vazio → sem fase). */
export const faseDaProgramacao = (fase: string | null | undefined) => { const f = normalizar(fase ?? ""); return f || null; };
/** Código curto do projeto para identificação nas tabelas ("CHS001 - ETHANOL PLANT" → "CHS001"). */
export const codigoDoProjeto = (nome: string, id: string) => /^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]/i.exec(nome)?.[1]?.toUpperCase() ?? (nome.trim() || id);

/** Ordem: gates 30/60/90 (numérica), fases textuais (alfabética), "Sem fase definida" por último. */
export function ordemFase(a: string, b: string) {
  if (a === b) return 0;
  if (a === SEM_FASE) return 1;
  if (b === SEM_FASE) return -1;
  const ga = /^FASE (\d+)%$/.exec(a), gb = /^FASE (\d+)%$/.exec(b);
  if (ga && gb) return Number(ga[1]) - Number(gb[1]);
  if (ga) return -1;
  if (gb) return 1;
  return a.localeCompare(b, "pt-BR");
}

function completar(colunas: readonly ColunaAgrupamento[], soma: (c: string) => number | null): Valores {
  const v: Valores = {};
  for (const c of colunas) if (!c.razao) v[c.chave] = soma(c.chave);
  for (const c of colunas) if (c.razao) {
    const [n, d] = c.razao; const num = v[n], den = v[d];
    v[c.chave] = typeof num === "number" && typeof den === "number" && den > 0 ? (num / den) * 100 : null;
  }
  return v;
}
const somar = (colunas: readonly ColunaAgrupamento[], linhas: readonly Partial<Valores>[]) =>
  completar(colunas, (c) => somaConhecida(linhas.map((l) => l[c] ?? null)));

function montar(colunas: ColunaAgrupamento[], entradas: ProjetoEntrada[], notas: string[] = []): Agrupamento {
  const projetos = entradas.map((p): GrupoProjeto => {
    const porFase = new Map<string, Item[]>();
    for (const it of p.itens) { const k = it.fase ?? SEM_FASE; if (!porFase.has(k)) porFase.set(k, []); porFase.get(k)!.push(it); }
    const fases = [...porFase].sort(([a], [b]) => ordemFase(a, b)).map(([k, itens]): GrupoFase => {
      const porDisc = new Map<string, Item[]>();
      for (const it of itens) { if (!porDisc.has(it.disciplina)) porDisc.set(it.disciplina, []); porDisc.get(it.disciplina)!.push(it); }
      const disciplinas = [...porDisc].map(([d, xs]): LinhaDisciplina => ({ chave: d, disciplina: xs[0].rotulo, valores: somar(colunas, xs.map((x) => x.valores)),
        diagnosticos: [...new Set(xs.flatMap((x) => (x.diagnostico ? [x.diagnostico] : [])))] }))
        .sort((a, b) => (a.chave === SEM_DISCIPLINA ? 1 : 0) - (b.chave === SEM_DISCIPLINA ? 1 : 0) || a.disciplina.localeCompare(b.disciplina, "pt-BR"));
      return { chave: k, fase: k === SEM_FASE ? SEM_FASE_ROTULO : k, semFase: k === SEM_FASE, disciplinas, totais: somar(colunas, itens.map((x) => x.valores)),
        detalhes: itens.flatMap((x) => (x.detalhe ? [x.detalhe] : [])).sort((a, b) => a.data.localeCompare(b.data) || (a.documento ?? "").localeCompare(b.documento ?? "", "pt-BR")) };
    });
    const somaFases = somar(colunas, fases.map((f) => f.totais));
    const totais = p.totaisOficiais ? completar(colunas, (c) => p.totaisOficiais![c] ?? somaFases[c]) : somaFases;
    let naoAtribuido: Valores | null = null;
    if (p.reconciliar && p.totaisOficiais) {
      const dif = completar(colunas, (c) => { const t = p.totaisOficiais![c], s = somaFases[c];
        return typeof t === "number" && Math.abs(t - (s ?? 0)) > TOLERANCIA ? t - (s ?? 0) : null; });
      if (colunas.some((c) => !c.razao && dif[c.chave] !== null)) naoAtribuido = dif;
    }
    return { projectId: p.projectId, projeto: p.projeto, codigo: codigoDoProjeto(p.projeto, p.projectId), tipoLdp: p.tipoLdp, fases, totais, naoAtribuido, notas: p.notas ?? [] };
  }).sort((a, b) => a.codigo.localeCompare(b.codigo, "pt-BR") || a.projeto.localeCompare(b.projeto, "pt-BR"));
  // Projetos são disjuntos: o consolidado é a soma dos totais de projeto (razões recalculadas, nunca média de %).
  return { colunas, projetos, consolidado: somar(colunas, projetos.map((p) => p.totais)), notas };
}

/** Catálogo de disciplinas SÓ do projeto (rótulos originais da Norma dele, sem sufixo de projeto). */
export function catalogoDoProjeto(cat: CatalogoDisciplinas, projectId: string, nome: string) {
  return criarCatalogo(cat.normas.filter((n) => n.projectId === projectId), [{ id: projectId, name: nome }]);
}

/* ------------------------------------------------------------------ 01. VISÃO GERAL — entregas da semana */
export const COLUNAS_ENTREGAS: ColunaAgrupamento[] = [
  { chave: "previstos", rotulo: "PREVISTOS", formato: "inteiro" }, { chave: "emitidos", rotulo: "EMITIDOS", formato: "inteiro" },
  { chave: "atrasados", rotulo: "EM ATRASO", formato: "inteiro" }, { chave: "aderencia", rotulo: "% ADERÊNCIA", formato: "pct", razao: ["emitidos", "previstos"] },
];
export function agruparEntregas(reports: readonly FactsReport[], cat: CatalogoDisciplinas, cutoff: string): Agrupamento {
  return montar(COLUNAS_ENTREGAS, reports.map((r) => {
    const c = catalogoDoProjeto(cat, r.projectId, r.nomeProjeto);
    // Cada entrega já é um gate (Tipo 2) ou um documento (Tipo 1): somar fases não duplica nada.
    const { entregas } = entregasDaSemana([r], c, cutoff);
    return { projectId: r.projectId, projeto: r.nomeProjeto, tipoLdp: r.tipoLdp,
      itens: entregas.map((e) => ({ fase: e.gate === null ? null : rotuloGate(e.gate), disciplina: e.chave, rotulo: rotulo(c, e.chave),
        valores: { previstos: 1, emitidos: e.emitido ? 1 : 0, atrasados: e.atrasado ? 1 : 0 } })) };
  }), ["Entregas da semana (domingo–sábado) da data de corte. Tipo 2: cada gate ativo é uma entrega da sua fase; Tipo 1 não tem fase na LDP."]);
}

/* ------------------------------------------------------------------ 02. PROGRAMAÇÃO — atividades da semana */
export const COLUNAS_PROGRAMACAO: ColunaAgrupamento[] = [
  { chave: "atividades", rotulo: "ATIVIDADES", formato: "inteiro" }, { chave: "hh", rotulo: "HH PLANEJADO", formato: "hh" },
];
export function agruparProgramacao(selecao: readonly { id: string; name: string }[], dados: readonly ProgramacaoResposta[], cat: CatalogoDisciplinas,
  dia: string | null): Agrupamento {
  const linhas = filtrarDia(deduplicarProgramacao(dados.flatMap((d) => d.linhas.map((l) => ({ ...l, projectId: d.projectId })))).linhas, dia);
  return montar(COLUNAS_PROGRAMACAO, selecao.filter((p) => dados.some((d) => d.projectId === p.id)).map((p) => {
    const c = catalogoDoProjeto(cat, p.id, p.name);
    return { projectId: p.id, projeto: p.name, tipoLdp: null,
      itens: linhas.filter((l) => l.projectId === p.id).map((l: ProgramacaoLinha & { projectId: string }) => {
        const k = classificarProgramacao(c, p.id, l), disc = rotulo(c, k.chave), hh = hhLinha(l);
        return { fase: faseDaProgramacao(l.fase), disciplina: k.chave, rotulo: disc, diagnostico: k.motivo, valores: { atividades: 1, hh },
          detalhe: { chave: `${p.id}:${l.idRegistro}`, data: l.dataAtividade.slice(0, 10), documento: l.documento, tarefa: l.tarefa, disciplina: disc,
            elaborador: l.elaborador, verificador: l.verificador, aprovador: l.aprovador, status: l.status, hh } };
      }) };
  }), ["Fase = coluna PHASE da programação semanal (Weekly_Phase), como importada. Linha sem fase → \"Sem fase definida\"."]);
}

/* ------------------------------------------------------------------ 03. DOCUMENTAÇÃO — status por fase */
export const COLUNAS_DOCUMENTOS: ColunaAgrupamento[] = [
  { chave: "total", rotulo: "TOTAL", formato: "inteiro" }, { chave: "emitidos", rotulo: "EMITIDO", formato: "inteiro" },
  { chave: "naoIniciados", rotulo: "NÃO INICIADO", formato: "inteiro" }, { chave: "emFluxo", rotulo: "EM FLUXO", formato: "inteiro" },
  { chave: "cancelados", rotulo: "CANCELADO", formato: "inteiro" },
];
type Situacao = { emitido: boolean; cancelado: boolean; status: string | null | undefined };
const contagem = (s: Situacao) => ({ total: 1, emitidos: s.emitido ? 1 : 0, cancelados: s.cancelado ? 1 : 0,
  emFluxo: !s.emitido && !s.cancelado && !!s.status?.trim() ? 1 : 0, naoIniciados: !s.emitido && !s.cancelado && !s.status?.trim() ? 1 : 0 });
const gatesAtivos = (d: FactsReport["documentos"][number]) => (d.gates ?? []).filter((g) => g.ativo);

export function agruparDocumentacao(reports: readonly FactsReport[], cat: CatalogoDisciplinas): Agrupamento {
  return montar(COLUNAS_DOCUMENTOS, reports.map((r) => {
    const c = catalogoDoProjeto(cat, r.projectId, r.nomeProjeto);
    const docs = r.documentos.filter((d) => !d.isActivity);
    const itens: Item[] = [];
    let sobreposicao = false;
    for (const d of docs) {
      const k = classificarDocumento(c, d), base = { disciplina: k.chave, rotulo: rotulo(c, k.chave), diagnostico: k.motivo };
      const gates = r.tipoLdp === 2 ? gatesAtivos(d) : [];
      if (gates.length > 1) sobreposicao = true;
      if (!gates.length) itens.push({ ...base, fase: null, valores: contagem({ emitido: d.emitido, cancelado: d.cancelado, status: d.statusInterno }) });
      // Situação DO GATE (emitido/cancelado/Document_Status da fase), não a do documento.
      else for (const g of gates) itens.push({ ...base, fase: rotuloGate(g.percentual),
        valores: contagem({ emitido: g.emitido === true, cancelado: g.cancelado, status: g.documentStatus }) });
    }
    // Total do projeto = cada DOCUMENTO uma vez (mesma regra da tela 03); as fases contam gates.
    const oficial = docs.reduce((s, d) => { const x = contagem({ emitido: d.emitido, cancelado: d.cancelado, status: d.statusInterno });
      (Object.keys(x) as (keyof typeof x)[]).forEach((k) => { s[k] = (s[k] ?? 0) + x[k]; }); return s; }, {} as Valores);
    return { projectId: r.projectId, projeto: r.nomeProjeto, tipoLdp: r.tipoLdp, itens, totaisOficiais: oficial,
      notas: sobreposicao ? ["Documento com mais de um gate ativo aparece em cada fase (situação do gate); o total do projeto conta cada documento uma vez."] : [] };
  }), ["Tipo 2: fase = gate ativo da LDP (situação do gate). Tipo 1 e documentos sem gate ativo: \"Sem fase definida\" (situação do documento)."]);
}

/* ------------------------------------------------------------------ 04. INDICADORES — HH/custo por fase */
export const COLUNAS_INDICADORES: ColunaAgrupamento[] = [
  { chave: "hhPrevisto", rotulo: "HH PLANEJADO", formato: "hh" }, { chave: "hhRealizado", rotulo: "HH REALIZADO", formato: "hh" },
  { chave: "consumoHh", rotulo: "%HH", formato: "pct", razao: ["hhRealizado", "hhPrevisto"] },
  { chave: "custoPrevisto", rotulo: "CUSTO PLANEJADO", formato: "moeda" }, { chave: "custoRealizado", rotulo: "CUSTO REALIZADO", formato: "moeda" },
  { chave: "consumoCusto", rotulo: "%CUSTO", formato: "pct", razao: ["custoRealizado", "custoPrevisto"] },
];
export function agruparIndicadores(reports: readonly FactsReport[], cat: CatalogoDisciplinas): Agrupamento {
  return montar(COLUNAS_INDICADORES, reports.map((r) => {
    const c = catalogoDoProjeto(cat, r.projectId, r.nomeProjeto);
    const itens: Item[] = [];
    for (const d of r.documentos) {
      const k = classificarDocumento(c, d), base = { disciplina: k.chave, rotulo: rotulo(c, k.chave), diagnostico: k.motivo };
      const gates = r.tipoLdp === 2 && !d.isActivity ? gatesAtivos(d).filter((g) => !g.cancelado) : [];
      // HH previsto por fase = esforço do gate (mesma fonte de "HH PREVISTO POR FASE"); custo não existe por gate.
      if (gates.length) for (const g of gates) itens.push({ ...base, fase: rotuloGate(g.percentual), valores: { hhPrevisto: g.esforcoHoras ?? null } });
      else itens.push({ ...base, fase: null, valores: { hhPrevisto: d.hhPrevisto, custoPrevisto: d.custoPrevisto } });
    }
    // Realizado: o Timesheet não identifica gate → sempre "Sem fase definida" (nada é rateado).
    const custoOficial = r.hhRealizado?.custoOficial === true;
    const realizado = r.hhRealizado ? r.hhRealizado.porDisciplina.map((x) => ({ d: x.disciplina, hh: x.hh, custo: custoOficial ? x.custo ?? null : null }))
      : r.actual?.byDiscipline.map((x) => ({ d: x.discipline, hh: x.values.hh, custo: x.values.knownCalculatedCostSubtotal })) ?? [];
    for (const x of realizado) {
      const k = classificarRealizado(c, r.projectId, x.d);
      itens.push({ fase: null, disciplina: k.chave, rotulo: rotulo(c, k.chave), diagnostico: k.motivo, valores: { hhRealizado: x.hh, custoRealizado: x.custo } });
    }
    if (r.hhRealizado && !custoOficial) for (const x of r.actual?.byDiscipline ?? []) {
      const k = classificarRealizado(c, r.projectId, x.discipline);
      itens.push({ fase: null, disciplina: k.chave, rotulo: rotulo(c, k.chave), diagnostico: k.motivo, valores: { custoRealizado: x.values.knownCalculatedCostSubtotal } });
    }
    const { cards } = projectFacts([r]);
    return { projectId: r.projectId, projeto: r.nomeProjeto, tipoLdp: r.tipoLdp, itens, reconciliar: true,
      totaisOficiais: { hhPrevisto: cards.HH_Previsto, hhRealizado: cards.HH_Realizado, custoPrevisto: cards.Custo_Previsto, custoRealizado: cards.Custo_Realizado },
      notas: r.tipoLdp === 2 ? ["HH planejado por fase = esforço do gate. Custo planejado não existe por gate: aparece em \"Não atribuído a fase\"."] : [] };
  }), ["Total do projeto = totais gerenciais oficiais. HH/custo realizado ficam em \"Sem fase definida\": o Timesheet não identifica gate e nada é rateado."]);
}

/** Datas exibidas nas folhas (AAAA-MM-DD → DD/MM). */
export const diaMes = (data: string) => { const d = dataISO(data); return d ? `${d.slice(8, 10)}/${d.slice(5, 7)}` : "—"; };
