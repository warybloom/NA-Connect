/**
 * Adaptador SOMENTE de apresentação do contrato oficial /api/reports/gestao-vista.
 * Situação = LDP.Situation; Status = LDP.Document_Status; ambos documento único/revisão vigente.
 * A ausência da fonte nunca vira 0, nem tenta usar o KPI de LDP (outro universo).
 */
import type { FactsReport } from "@/components/pmo/pmoFacts";

export type CategoriaStatus = {
  chave: string; rotulo: string; documentos: number;
  valoresOriginais?: { valor: string; documentos: number }[];
};
export interface ConsolidadoDocumentalApi {
  disponivel: boolean; fonte: string; linhasDocumentais: number; documentosUnicos: number; linhasRepetidasIgnoradas: number;
  situacao: { total: number; emitidos: number; cancelados: number; aEmitir: number };
  prazo: { atrasados: number; noPrazo: number; semPrazo: number; classificadosComoEmitidoOuCanceladoPeloStatus: number };
  status: { total: number; categorias: CategoriaStatus[] };
  diagnosticos: string[];
}
export type ChaveStatus = "APROVADO" | "CANCELADO" | "EM_ANALISE_CLIENTE" | "INFORMATIVO" | "ATENDIMENTO_COMENTARIOS" | "EM_ELABORACAO";
export const STATUS_OFICIAIS: readonly { chave: ChaveStatus; titulo: string }[] = [
  { chave: "APROVADO", titulo: "Aprovado" },
  { chave: "CANCELADO", titulo: "Cancelado" },
  { chave: "EM_ANALISE_CLIENTE", titulo: "Em análise do cliente" },
  { chave: "INFORMATIVO", titulo: "Informativo" },
  { chave: "ATENDIMENTO_COMENTARIOS", titulo: "Atendimento a comentários" },
  { chave: "EM_ELABORACAO", titulo: "Em elaboração" },
];
export interface ResumoDocumentosOficial {
  disponivel: boolean;
  situacao: { total: number | null; emitidos: number | null; cancelados: number | null; aEmitir: number | null };
  status: Record<ChaveStatus,number|null>;
  origem: string;
}
export function consolidarDocumentoOficial(reports: readonly FactsReport[]): ResumoDocumentosOficial {
  const items = reports.map(r=>r.consolidadoDocumental);
  const pronto = reports.length>0 && items.every((x): x is ConsolidadoDocumentalApi => !!x?.disponivel);
  const soma=(field:(r:ConsolidadoDocumentalApi)=>number)=>pronto?items.reduce((s,r)=>s+field(r!),0):null;
  const total=soma(r=>r.situacao.total);
  const emitidos=soma(r=>r.situacao.emitidos);
  const cancelados=soma(r=>r.situacao.cancelados);
  const aEmitir=soma(r=>r.situacao.aEmitir);
  const checks=pronto&&total===emitidos!+cancelados!+aEmitir!;
  // Inconsistência jamais é escondida com um cálculo inventado.
  const disponivel=!!checks;
  const valor=(x:number|null)=>disponivel?x:null;
  const status=Object.fromEntries(STATUS_OFICIAIS.map(({chave})=>[
    chave,disponivel?items.reduce((s,item)=>s+(item!.status.categorias.find(c=>c.chave===chave)?.documentos??0),0):null
  ])) as Record<ChaveStatus,number|null>;
  return { disponivel, situacao:{total:valor(total),emitidos:valor(emitidos),cancelados:valor(cancelados),aEmitir:valor(aEmitir)},
    status, origem:disponivel?"Situation e Document_Status: LDP vigente (documento único)":
      "Aguarda consolidado oficial Situation/Document_Status de todos os projetos selecionados" };
}
