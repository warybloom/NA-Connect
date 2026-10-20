import { balance, consumption, factKey, projectFacts, statusCounts, type FactsReport } from "@/components/pmo/pmoFacts";
import { disciplinasParaExibicao, pertenceAoGrupo, type DisciplinaExibida } from "./disciplinasApresentacao";

export const RESUMO_GATES = [30, 60, 90] as const;
export interface ResumoPhaseMetric {
  gate: number; total: number | null; emitidos: number | null; cancelados: number | null; aEmitir: number | null;
  atrasados: number | null; noPrazo: number | null; semPrazo: number | null;
  hhPrevisto: number | null; hhRealizado: number | null; consumoHh: number | null; saldoHh: number | null;
  custoPrevisto: number | null; custoRealizado: number | null; consumoCusto: number | null; semHhPrevisto: number;
}
interface PhaseDocument { rowId: string; codigo: string | null; disciplina: string | null; disciplinaNorma: string | null; fases: ResumoPhaseMetric[] }
export type ResumoReport = FactsReport & { resumoFases?: { fases: ResumoPhaseMetric[]; documentos: PhaseDocument[]; pendencias: string[] } };
export type PhaseField = Exclude<keyof ResumoPhaseMetric, "gate" | "semHhPrevisto">;
const empty = (gate: number): ResumoPhaseMetric => ({gate,total:null,emitidos:null,cancelados:null,aEmitir:null,atrasados:null,noPrazo:null,
  semPrazo:null,hhPrevisto:null,hhRealizado:null,consumoHh:null,saldoHh:null,custoPrevisto:null,custoRealizado:null,consumoCusto:null,semHhPrevisto:0});
const sum = (values: (number | null | undefined)[]) => values.length && values.every(v=>typeof v==="number" && Number.isFinite(v))
  ? (values as number[]).reduce((a,b)=>a+b,0) : null;

/** Only consolidates backend phase facts. There is no reconstruction from general status or financial source. */
export function resumoFases(reports: readonly ResumoReport[], rows?: (report: ResumoReport)=>PhaseDocument[]) {
  const type2 = reports.filter(r=>r.tipoLdp===2);
  if (!type2.length) return [];
  return RESUMO_GATES.map(gate=>{
    if(type2.some(r=>!r.resumoFases)) return empty(gate);
    const values=type2.flatMap(r=>rows ? rows(r).flatMap(d=>d.fases.filter(f=>f.gate===gate)) : r.resumoFases!.fases.filter(f=>f.gate===gate));
    if (!values.length) return {...empty(gate),total:0,emitidos:0,cancelados:0,aEmitir:0,atrasados:0,noPrazo:0,semPrazo:0,hhPrevisto:0};
    const s=(field:PhaseField)=>sum(values.map(v=>v[field]));
    return {...empty(gate), total:s("total"),emitidos:s("emitidos"),cancelados:s("cancelados"),aEmitir:s("aEmitir"),
      atrasados:s("atrasados"),noPrazo:s("noPrazo"),semPrazo:s("semPrazo"),hhPrevisto:s("hhPrevisto"),
      hhRealizado:s("hhRealizado"),custoPrevisto:s("custoPrevisto"),custoRealizado:s("custoRealizado"),
      consumoHh:consumption(s("hhRealizado"),s("hhPrevisto")),saldoHh:balance(s("hhPrevisto"),s("hhRealizado")),
      consumoCusto:consumption(s("custoRealizado"),s("custoPrevisto")),semHhPrevisto:values.reduce((a,v)=>a+v.semHhPrevisto,0)};
  });
}

export interface ResumoDiscipline extends DisciplinaExibida {
  projectId: string; nomeProjeto: string; tipoLdp: number; documentos: ReturnType<typeof statusCounts> & { aEmitir: number };
  fases: ResumoPhaseMetric[]; pendencias: string[];
}

/** Resolve each project independently: different project Normas must never contaminate one another. */
export function resumoDisciplinas(reports: readonly ResumoReport[]): ResumoDiscipline[] {
  return reports.flatMap(report=>{
    const groups=disciplinasParaExibicao([report]);
    // Actual facts already use the project's Norma. Add that proven official name to the existing resolver result.
    const officialNames=new Set(report.documentos.map(d=>d.disciplinaNorma).filter((n):n is string=>!!n));
    const resolved=groups.map(g=>{
      const raw=projectFacts([report]).disciplines.find(d=>g.origens.includes(d.chave));
      const official=raw&&[...officialNames].find(n=>factKey(n)===factKey(raw.Disciplina));
      return official ? {...g,Disciplina:official,oficial:true,diagnostico:null,idExibicao:"NORMA:"+factKey(official)} :
        g.oficial ? g : {...g,Disciplina:"SEM MAPEAMENTO"};
    });
    // Include documentary disciplines without planning/actual rows; no document may disappear from the bottom cards.
    for(const d of report.documentos) {
      if(resolved.some(g=>pertenceAoGrupo(d.disciplinaCanonica,d.disciplina,g)))continue;
      const key=factKey(d.disciplinaCanonica??d.disciplina);
      resolved.push({chave:key,idExibicao:key,origens:[key],Disciplina:d.disciplinaNorma??"SEM MAPEAMENTO",oficial:!!d.disciplinaNorma,
        diagnostico:d.disciplinaNorma?null:"Documento sem correspondência na Norma",HH_Previsto:null,HH_Realizado:null,SaldoHH:null,ConsumoHH:null,
        Custo_Previsto:null,Custo_Realizado:null,SaldoCusto:null,ConsumoCusto:null,DocsSemCusto:0,DocsComCusto:0,DocsCusto:0});
    }
    const merged=new Map<string,typeof resolved[number]>();
    for(const g of resolved){
      const prior=merged.get(g.idExibicao);
      if(!prior){merged.set(g.idExibicao,g);continue;}
      const hp=sum([prior.HH_Previsto,g.HH_Previsto]),hr=sum([prior.HH_Realizado,g.HH_Realizado]);
      const cp=sum([prior.Custo_Previsto,g.Custo_Previsto]),cr=sum([prior.Custo_Realizado,g.Custo_Realizado]);
      merged.set(g.idExibicao,{...prior,origens:[...new Set([...prior.origens,...g.origens])],HH_Previsto:hp,HH_Realizado:hr,
        SaldoHH:balance(hp,hr),ConsumoHH:consumption(hr,hp),Custo_Previsto:cp,Custo_Realizado:cr,SaldoCusto:balance(cp,cr),ConsumoCusto:consumption(cr,cp)});
    }
    return [...merged.values()].map(g=>{
      const docs=report.documentos.filter(d=>pertenceAoGrupo(d.disciplinaCanonica,d.disciplina,g));
      const counts=statusCounts(docs);
      return {...g,projectId:report.projectId,nomeProjeto:report.nomeProjeto,tipoLdp:report.tipoLdp,
        idExibicao:report.projectId+":"+g.idExibicao,documentos:{...counts,aEmitir:counts.atrasados+counts.noPrazo+counts.semPrazo},
        fases:resumoFases([report],r=>r.resumoFases!.documentos.filter(d=>docs.some(doc=>doc.rowId===d.rowId || doc.codigo===d.codigo))),
        pendencias:docs.filter(d=>!d.disciplinaNorma).map(d=>{
          const diagnostic=d as typeof d & {diagnosticoDisciplinaNorma?:string;normaCoordenacaoId?:number|null};
          return `${report.nomeProjeto} · ${d.codigo??d.rowId} · DOC3 ${d.siglaDoc3??"ausente"} · Norma ${diagnostic.normaCoordenacaoId??"não vinculada"} · ${diagnostic.diagnosticoDisciplinaNorma??d.fonteDisciplinaNorma??"resolvedor indisponível"}`;
        })};
    });
  });
}

export function resumoPageSize(width:number,height:number,type2:boolean){
  const cols=width<1000?1:width<1600?2:3;
  // A row contains thirteen indicators. 4K increases legibility, rather than squeezing a second row of cards.
  const rows=height>2600&&!type2?2:1;
  return cols*rows;
}
