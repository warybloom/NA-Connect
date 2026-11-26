import type { ProgramacaoResposta } from "./gestaoVistaJamileLogic";

export interface DocumentoSemanal {
  projectId: string;
  codigo: string | null;
  dataPrevista: string;
  disciplina: string;
}
export interface DocumentoLdpSemanal {
  projectId: string;
  codigo: string | null;
  cancelado: boolean;
  statusCliente?: string | null;
  dataEmissao?: string | null;
}
export const STATUS_CANCELAMENTO = new Set(["CANCELED", "CANCELLED", "CANCELADO", "CANCELADA"]);
const normalizar = (v: string | null | undefined) => (v ?? "").trim().toUpperCase();

/**
 * Os pendentes pertencem SOMENTE à coorte da semana escolhida.
 * Emissões antecipadas de outras semanas entram no saldo de produção,
 * mas NUNCA liquidam um documento pendente diferente.
 */
export function calcularPendenciasSemanais(args: {
  itens: readonly DocumentoSemanal[];
  semana: {start: string; end: string};
  hoje: string;
  programacao: readonly ProgramacaoResposta[];
  documentosLdp: readonly DocumentoLdpSemanal[];
  statusValidos: Readonly<Record<string, readonly string[]>>;
}) {
  const {itens, semana, hoje, programacao, documentosLdp, statusValidos} = args;
  const estados = new Map<string, {id: number; data: string; status: string; validos: Set<string>}>();
  for (const grupo of programacao) {
    for (const row of [...grupo.linhas, ...(grupo.linhasSemData ?? [])]) {
      if (!row.documento) continue;
      const chave = grupo.projectId + "::" + normalizar(row.documento);
      const status = normalizar(row.status);
      const anterior = estados.get(chave);
      const data = "dataAtividade" in row ? (row.dataAtividade ?? "").slice(0,10) : "";
      const permitidos = new Set((statusValidos[grupo.projectId] ?? []).map(normalizar));
      const validos = anterior?.validos ?? new Set<string>();
      if (permitidos.has(status)) validos.add(status);
      if (!anterior || data > anterior.data || data === anterior.data && row.idRegistro >= anterior.id)
        estados.set(chave, {id: row.idRegistro, data, status, validos});
      else
        estados.set(chave, {...anterior, validos});
    }
  }
  const ldp = new Map<string, {cancelado: boolean; status: Set<string>; emissao: Set<string>}>();
  for (const d of documentosLdp) {
    if (!d.codigo) continue;
    const key = d.projectId + "::" + normalizar(d.codigo);
    const atual = ldp.get(key) ?? {cancelado: false, status: new Set<string>(), emissao: new Set<string>()};
    atual.cancelado ||= d.cancelado;
    if (d.statusCliente) atual.status.add(normalizar(d.statusCliente));
    if (d.dataEmissao) atual.emissao.add(d.dataEmissao.slice(0, 10));
    ldp.set(key, atual);
  }
  const cutoff = hoje > semana.end ? semana.end : hoje;
  const atrasadoAte = hoje > semana.end ? semana.end : hoje;
  const processados = new Set<string>();
  let cancelados = 0, aEmitir = 0, atrasados = 0, vencidos = 0, emitidosProgramados = 0;
  const porDisciplina = new Map<string,{aEmitir:number;atrasados:number;cancelados:number}>();
  const detalhe: {codigo: string; disciplina: string; estado: string; atrasado: boolean}[] = [];
  for (const item of itens) {
    if (!item.codigo || item.dataPrevista < semana.start || item.dataPrevista > semana.end) continue;
    const chave = item.projectId + "::" + normalizar(item.codigo);
    if (processados.has(chave)) continue;
    processados.add(chave);
    const historico = estados.get(chave);
    const fonteLdp = ldp.get(chave);
    const cancelado = fonteLdp?.cancelado || STATUS_CANCELAMENTO.has(historico?.status ?? "");
    const config = new Set((statusValidos[item.projectId] ?? []).map(normalizar));
    const emitido = !cancelado && (
      !![...(historico?.validos ?? [])].find(s => config.has(s)) ||
      !![...(fonteLdp?.status ?? [])].find(s => config.has(s))
    ) && (!fonteLdp?.emissao.size || [...fonteLdp.emissao].some(data => data <= cutoff));
    const vencido = item.dataPrevista < atrasadoAte || (hoje > semana.end && item.dataPrevista <= semana.end);
    const disciplina = item.disciplina;
    const total = porDisciplina.get(disciplina) ?? {aEmitir:0, atrasados:0, cancelados:0};
    if (cancelado) {
      cancelados++; total.cancelados++;
      detalhe.push({codigo: item.codigo, disciplina, estado: "cancelado", atrasado: false});
    } else {
      if (vencido) vencidos++;
      if (emitido) {
        emitidosProgramados++;
        detalhe.push({codigo: item.codigo, disciplina, estado: "emitido", atrasado: false});
      } else {
        aEmitir++; total.aEmitir++;
        if (vencido) {atrasados++; total.atrasados++;}
        detalhe.push({codigo: item.codigo, disciplina, estado: "a emitir", atrasado: vencido});
      }
    }
    porDisciplina.set(disciplina, total);
  }
  return {
    programados: processados.size, cancelados, aEmitir, atrasados, vencidos,
    emitidosProgramados, desvio: vencidos ? atrasados / vencidos * 100 : null,
    detalhe, porDisciplina
  };
}
