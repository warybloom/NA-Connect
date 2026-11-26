import {
  classificarProgramacao, classificarVisaoGeral, documentoVisaoGeral,
  type CatalogoDisciplinas, type ProgramacaoResposta
} from "./gestaoVistaJamileLogic";

export interface EmissaoComprovada {
  projectId: string;
  codigo: string;
  disciplina: string;
  status: string;
  dataPrevista: string | null;
}
export interface SaldoDisciplina {
  disciplina: string;
  programados: number;
  emitidos: number;
  saldo: number;
  percentual: number | null;
}
/**
 * Retrato atual da emissão pelo status definido pelo planejador.
 * A comparação é com a meta da semana; status sem carimbo de emissão NÃO
 * determina a semana da emissão e não é reconstituído retroativamente.
 */
export function calcularSaldoPositivoPorDisciplina(
  todosDados: readonly ProgramacaoResposta[],
  semanaItens: readonly { chave: string; disciplina: string; projectId: string; codigo: string | null }[],
  validos: Readonly<Record<string, readonly string[]>>,
  cat: CatalogoDisciplinas,
  cancelados: ReadonlySet<string> = new Set(),
  periodo: {start: string; end: string} | null = null,
  datasEmissao: ReadonlyMap<string, string> = new Map(),
  statusLdp: ReadonlyMap<string, string> = new Map()
) {
  const programados = new Map<string, Set<string>>();
  for (const doc of semanaItens) {
    const chave = doc.projectId + "::" + (doc.codigo ?? "").trim().toUpperCase();
    if (!documentoVisaoGeral(doc.codigo)) continue;
    if (!programados.has(doc.disciplina)) programados.set(doc.disciplina, new Set());
    programados.get(doc.disciplina)!.add(chave);
  }
  const statusPorDocumento = new Map<string, Set<string>>();
  const latest = new Map<string, {
    id: number; status: string; disciplina: string; projectId: string;
    codigo: string; dataPrevista: string | null
  }>();
  for (const dados of todosDados) {
    const projeto = dados.projectId;
    for (const row of [...dados.linhas, ...(dados.linhasSemData ?? [])]) {
      if (!documentoVisaoGeral(row.documento)) continue;
      const codigo = row.documento!.trim().toUpperCase();
      const key = projeto + "::" + codigo;
      if (cancelados.has(key)) continue;
      const status = (row.status ?? "").trim().toUpperCase();
      if (status) {
        if (!statusPorDocumento.has(key)) statusPorDocumento.set(key,new Set());
        statusPorDocumento.get(key)!.add(status);
      }
      const id = row.idRegistro;
      const prev = latest.get(key);
      if (prev && prev.id > id) continue;
      const classif = classificarVisaoGeral(cat, classificarProgramacao(cat, projeto, {
        ...row, dataAtividade: "dataAtividade" in row ? row.dataAtividade : ""
      } as ProgramacaoResposta["linhas"][number]));
      latest.set(key, {
        id, status, disciplina: classif, projectId: projeto, codigo,
        dataPrevista: "dataAtividade" in row && typeof row.dataAtividade === "string"
          ? row.dataAtividade.slice(0, 10) : null
      });
    }
  }
  const elegiveis = [...latest.values()]
    .flatMap(r => {
      const key=r.projectId+"::"+r.codigo;
      const allowed=new Set((validos[r.projectId] ?? []).map(v=>v.trim().toUpperCase()));
      const values=[...(statusPorDocumento.get(key)??[]), (statusLdp.get(key)??"").trim().toUpperCase()];
      const qualifying=values.find(v=>allowed.has(v));
      if (!qualifying) return [];
      if (periodo) {
        const emissao=datasEmissao.get(key)?.slice(0,10);
        if(!emissao||emissao<periodo.start||emissao>periodo.end)return [];
      }
      return [{...r,status:qualifying}];
    });
  const emitidosPorDisciplina = new Map<string, Set<string>>();
  for (const doc of elegiveis) {
    if (!emitidosPorDisciplina.has(doc.disciplina)) emitidosPorDisciplina.set(doc.disciplina, new Set());
    emitidosPorDisciplina.get(doc.disciplina)!.add(doc.projectId + "::" + doc.codigo);
  }
  const disciplinas = [...new Set([...programados.keys(), ...emitidosPorDisciplina.keys()])];
  const porDisciplina: SaldoDisciplina[] = disciplinas.map(disciplina => {
    const previsto = programados.get(disciplina)?.size ?? 0;
    const realizado = emitidosPorDisciplina.get(disciplina)?.size ?? 0;
    return {
      disciplina, programados: previsto, emitidos: realizado,
      saldo: realizado - previsto,
      percentual: previsto > 0 ? realizado / previsto * 100 : null
    };
  });
  const previstoTotal = semanaItens.filter(d => documentoVisaoGeral(d.codigo)).length;
  const realizadoTotal = elegiveis.length;
  return {
    porDisciplina,
    porProjeto: [...new Set([...todosDados.map(d => d.projectId), ...semanaItens.map(d => d.projectId)])].map(projectId => ({
      projectId,
      programados: semanaItens.filter(d => d.projectId === projectId).length,
      emitidos: elegiveis.filter(d => d.projectId === projectId).length
    })),
    programados: previstoTotal,
    emitidos: realizadoTotal,
    saldo: realizadoTotal - previstoTotal,
    acimaDaMeta: porDisciplina.reduce((v, d) => v + Math.max(0, d.saldo), 0),
    emitidosDetalhe: elegiveis.map(({ id: _id, ...d }) => d as EmissaoComprovada)
  };
}
