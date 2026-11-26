// Dois comparativos lado a lado e uma única coluna CENTRAL com nomes das disciplinas.
// A tabela agregada continua sendo projectFacts/disciplinasParaExibicao; apenas apresentação.
import {useMemo} from "react";
import {formatCurrency,formatHH} from "@/components/pmo/pmoData";
import type {FactsReport} from "@/components/pmo/pmoFacts";
import {disciplinasParaExibicao} from "./disciplinasApresentacao";
interface Rastro {previsto:number|null;realizado:number|null}
const compacto=(n:number|null)=>n==null?"—":new Intl.NumberFormat("pt-BR",{style:"currency",currency:"BRL",notation:"compact",maximumFractionDigits:1}).format(n);
function Bars({previsto,realizado,referencia,formato}:{previsto:number|null;realizado:number|null;referencia:number;formato:(n:number|null)=>string}){
  return <div className="flex min-w-0 flex-col justify-center gap-1.5" title={"Previsto: "+formato(previsto)+" / Realizado: "+formato(realizado)}>
    {([["Prev.",previsto,"bg-slate-400"],["Real.",realizado,"bg-orange-500"]] as const).map(([nome,valor,cor])=>
      <div key={nome} className="grid min-w-0 grid-cols-[32px_minmax(0,1fr)_minmax(55px,auto)] items-center gap-1.5 text-[10px]">
        <span className="text-muted-foreground">{nome}</span>
        <div className="h-2.5 overflow-hidden rounded-lg bg-muted">
          <div className={"h-full rounded-lg "+cor} style={{width:Math.max(0,Math.min(100,(valor??0)/referencia*100))+"%"}}/>
        </div>
        <strong className="min-w-0 truncate text-right tabular-nums">{formato(valor)}</strong>
      </div>
    )}
  </div>;
}
export default function GraficosResumoDisciplinas({reports}:{reports:FactsReport[]}){
  const dados=useMemo(()=>disciplinasParaExibicao(reports),[reports]);
  const maxHH=Math.max(1,...dados.flatMap(x=>[x.HH_Previsto??0,x.HH_Realizado??0]));
  const maxBRL=Math.max(1,...dados.flatMap(x=>[x.Custo_Previsto??0,x.Custo_Realizado??0]));
  return <section aria-label="HH e custo por disciplina com nome único centralizado" className="space-y-2">
    <h2 className="text-[10px] font-black uppercase tracking-[.25em] text-muted-foreground">Comparativo de HH e custos por disciplina</h2>
    <div className="min-w-0 rounded-xl border border-border border-t-4 border-t-primary/60 bg-card/60 p-3 sm:p-4">
      <div className="grid grid-cols-[minmax(0,1fr)_minmax(110px,24%)_minmax(0,1fr)] items-center gap-2 border-b border-border pb-3 text-center text-[10px] font-black uppercase tracking-wide sm:gap-4 sm:text-xs">
        <div>Horas <span className="hidden text-muted-foreground md:inline">· Previsto x Realizado</span></div>
        <div className="text-primary">Disciplina</div>
        <div>Custo <span className="hidden text-muted-foreground md:inline">· Previsto x Realizado</span></div>
      </div>
      <div className="max-h-[520px] divide-y divide-border/40 overflow-y-auto">
        {dados.map(d=><div key={d.idExibicao} className="grid min-h-[63px] grid-cols-[minmax(0,1fr)_minmax(110px,24%)_minmax(0,1fr)] items-center gap-2 py-2 sm:gap-4">
          <Bars previsto={d.HH_Previsto} realizado={d.HH_Realizado} referencia={maxHH} formato={v=>v==null?"—":formatHH(v)}/>
          <div className="min-w-0 text-center text-[10px] font-black uppercase leading-tight text-foreground sm:text-xs" title={d.diagnostico??d.Disciplina}>{d.Disciplina}</div>
          <Bars previsto={d.Custo_Previsto} realizado={d.Custo_Realizado} referencia={maxBRL} formato={compacto}/>
        </div>)}
        {!dados.length&&<div className="py-7 text-center text-xs text-muted-foreground">Nenhuma disciplina disponível no projeto.</div>}
      </div>
      <div className="mt-3 flex flex-wrap justify-between gap-2 text-[10px] text-muted-foreground">
        <span>Escalas proporcionais dentro de cada indicador · Detalhes completos ao apontar</span>
        <span>Uma identificação por disciplina, compartilhada pelos dois comparativos</span>
      </div>
    </div>
  </section>;
}
