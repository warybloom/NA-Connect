/**
 * Núcleo PURO (sem React/rede) da consolidação multi-projeto de /relatorios.
 * Regra: 1 projeto = o projeto; N projetos = CONSOLIDADO de todos os selecionados. Métricas aditivas somam; percentuais nunca são médias simples
 * (recalculados sobre as somas); o que não é aditivo (avanço %) é exibido POR PROJETO, sem total inventado. Nenhuma chave documental é global:
 * a identidade de um documento é sempre (projeto, código).
 * Arquitetura preparada para um futuro modo COMPARAÇÃO (ModoRelatorio) sem implementá-lo agora.
 */
export type ModoRelatorio = 'CONSOLIDADO'; // futuro: | 'COMPARACAO' — não implementado

export interface PmoSelecionado { id: string; hubId: string; name: string }

export const rotuloConsolidado = (n: number) => n > 1 ? `CONSOLIDADO — ${n} PROJETOS` : null;

/** Soma SQL: valores conhecidos; null só quando NENHUM valor é conhecido. */
export const somaConhecida = (values: (number | null | undefined)[]): number | null => {
  const k = values.filter((v): v is number => typeof v === 'number' && Number.isFinite(v));
  return k.length ? k.reduce((a, v) => a + v, 0) : null;
};

/**
 * Paginação de uma lista consolidada = concatenação (projeto por projeto, na ordem da seleção) das listas já paginadas no servidor.
 * `counts[i]` = linhas filtradas do projeto i; devolve, para a página global, o intervalo local [from,to) de cada projeto que participa.
 */
export function planSlice(counts: number[], page: number, pageSize: number): { project: number; from: number; to: number }[] {
  const start = (page - 1) * pageSize, end = start + pageSize;
  const out: { project: number; from: number; to: number }[] = [];
  let offset = 0;
  counts.forEach((c, project) => {
    const from = Math.max(start, offset) - offset, to = Math.min(end, offset + c) - offset;
    if (to > from) out.push({ project, from, to });
    offset += c;
  });
  return out;
}
/** Páginas (1-based) do servidor, de tamanho `serverPageSize`, necessárias para cobrir o intervalo local [from,to). */
export function serverPagesFor(from: number, to: number, serverPageSize: number): number[] {
  const first = Math.floor(from / serverPageSize) + 1, last = Math.floor((to - 1) / serverPageSize) + 1;
  return Array.from({ length: last - first + 1 }, (_, i) => first + i);
}

// ---------- Curva S ----------
export interface CurvaSPonto {
  data: string; previstoHh: number; realizadoHh: number; previstoCusto: number; realizadoCusto: number;
  previstoDocumentos: number; emitidosDocumentos: number;
}
export interface CurvaSCobertura {
  totalDocumentos: number; cancelados: number; ambiguos: number; semBaseline: number; semHhPrevisto: number; semCustoPrevisto: number;
  hhPrevistoTotalProjeto: number | null; hhCurvaPrevisto: number; hhCurvaRealizado: number; hhCancelados: number; hhSemBaseline: number;
  hhAtividades: number; hhSemDocumento: number | null; custoCurvaPrevisto: number; custoCurvaRealizado: number;
}
export interface CurvaSReport {
  projectId: string; dataReferencia: string; pontos: CurvaSPonto[]; cobertura: CurvaSCobertura; fontePrevisto: string; fonteRealizado: string;
}
const dia = (v: string) => v.slice(0, 10);

/**
 * Curva S consolidada: cada projeto já traz o ACUMULADO próprio nas suas datas de evento. Na linha do tempo comum (união das datas), o acumulado
 * consolidado em D é a soma dos acumulados de cada projeto em D (último ponto ≤ D). Aditivo puro (HH, custo, documentos) — nunca média de curvas.
 */
export function consolidarCurvaS(reports: CurvaSReport[]): CurvaSReport {
  const datas = [...new Set(reports.flatMap(r => r.pontos.map(p => dia(p.data))))].sort();
  const ptrs = reports.map(() => 0);
  const ultimo: (CurvaSPonto | undefined)[] = reports.map(() => undefined);
  const pontos: CurvaSPonto[] = datas.map(d => {
    reports.forEach((r, i) => { while (ptrs[i] < r.pontos.length && dia(r.pontos[ptrs[i]].data) <= d) ultimo[i] = r.pontos[ptrs[i]++]; });
    const s = (f: (p: CurvaSPonto) => number) => ultimo.reduce((a, p) => a + (p ? f(p) : 0), 0);
    return { data: d, previstoHh: s(p => p.previstoHh), realizadoHh: s(p => p.realizadoHh), previstoCusto: s(p => p.previstoCusto),
      realizadoCusto: s(p => p.realizadoCusto), previstoDocumentos: s(p => p.previstoDocumentos), emitidosDocumentos: s(p => p.emitidosDocumentos) };
  });
  const c = reports.map(r => r.cobertura);
  const soma = (f: (x: CurvaSCobertura) => number) => c.reduce((a, x) => a + f(x), 0);
  const cobertura: CurvaSCobertura = {
    totalDocumentos: soma(x => x.totalDocumentos), cancelados: soma(x => x.cancelados), ambiguos: soma(x => x.ambiguos), semBaseline: soma(x => x.semBaseline),
    semHhPrevisto: soma(x => x.semHhPrevisto), semCustoPrevisto: soma(x => x.semCustoPrevisto),
    hhPrevistoTotalProjeto: somaConhecida(c.map(x => x.hhPrevistoTotalProjeto)), hhCurvaPrevisto: soma(x => x.hhCurvaPrevisto),
    hhCurvaRealizado: soma(x => x.hhCurvaRealizado), hhCancelados: soma(x => x.hhCancelados), hhSemBaseline: soma(x => x.hhSemBaseline),
    hhAtividades: soma(x => x.hhAtividades), hhSemDocumento: somaConhecida(c.map(x => x.hhSemDocumento)), custoCurvaPrevisto: soma(x => x.custoCurvaPrevisto), custoCurvaRealizado: soma(x => x.custoCurvaRealizado),
  };
  const ref = reports.map(r => dia(r.dataReferencia)).sort().pop() ?? '';
  return { projectId: 'CONSOLIDADO', dataReferencia: ref, pontos, cobertura, fontePrevisto: reports[0]?.fontePrevisto ?? '', fonteRealizado: reports[0]?.fonteRealizado ?? '' };
}

// ---------- NCR / grupos ----------
export interface GrupoQualidade { status: string | null; discipline: string | null; count: number }
/** Une grupos (status × disciplina) de vários projetos somando as contagens (aditivo). */
export function mergeGruposQualidade(groups: GrupoQualidade[][]): GrupoQualidade[] {
  const m = new Map<string, GrupoQualidade>();
  for (const g of groups.flat()) {
    const k = JSON.stringify([g.status, g.discipline]);
    const cur = m.get(k); if (cur) cur.count += g.count; else m.set(k, { ...g });
  }
  return [...m.values()].sort((a, b) => (a.status ?? '').localeCompare(b.status ?? '') || (a.discipline ?? '').localeCompare(b.discipline ?? ''));
}

// ---------- Mapa de Calor ----------
export const STATUS_CANONICO = ['CANCELADO', 'EMITIDO', 'NO_PRAZO', 'ATRASADO', 'SEM_PRAZO'] as const;
export interface DocStatus { projectId: string; projectName: string; disciplina: string | null; statusDocumento: string }
export const chaveDisciplina = (v: string | null, semValor: string) => v?.trim().toUpperCase() || semValor;

/** Matriz Disciplina × Status canônico consolidada (cada documento numa única célula) + detalhe por projeto de cada célula (para tooltip/futuro). */
export function matrizStatusDisciplina(docs: DocStatus[], semValor: string) {
  const linhas = new Map<string, { c: Record<string, number>; porProjeto: Map<string, Record<string, number>> }>();
  for (const d of docs) {
    const k = chaveDisciplina(d.disciplina, semValor);
    const l = linhas.get(k) ?? { c: Object.fromEntries(STATUS_CANONICO.map(s => [s, 0])), porProjeto: new Map() };
    l.c[d.statusDocumento] = (l.c[d.statusDocumento] ?? 0) + 1;
    const pp = l.porProjeto.get(d.projectName) ?? Object.fromEntries(STATUS_CANONICO.map(s => [s, 0]));
    pp[d.statusDocumento] = (pp[d.statusDocumento] ?? 0) + 1;
    l.porProjeto.set(d.projectName, pp); linhas.set(k, l);
  }
  const rows = [...linhas.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([chave, l]) => ({ chave, c: l.c, porProjeto: l.porProjeto,
    total: STATUS_CANONICO.reduce((n, s) => n + (l.c[s] ?? 0), 0) }));
  const colunas = Object.fromEntries(STATUS_CANONICO.map(s => [s, rows.reduce((n, r) => n + (r.c[s] ?? 0), 0)])) as Record<string, number>;
  return { rows, colunas, soma: rows.reduce((n, r) => n + r.total, 0) };
}
