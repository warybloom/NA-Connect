import { useEffect, useState } from "react";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { descreverErro, isAbort, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { useFaseSelecionada } from "./plantShared";
import { Cartao, Estado, PlantHeader, Rosca, SemProjeto } from "./plantShared";

type Line = {tag:string;modelled3d:boolean;expectedValves:number;actualValves:number;expectedInstruments:number;actualInstruments:number;expectedEquipment:number;actualEquipment:number;associationsVerified:boolean;valvesPercent:number|null;instrumentsPercent:number|null;overallPercent:number|null;issues:string[]};
type Consistency = {status:string;message:string;revisionToken:string|null;tagLineColumn:string;totalLines:number;fullyModelled:number;incomplete:number;unverified:number;lines:Line[]};
export default function PlantConsistencyDetailPage() {
  const pj=usePlantProjects();
  const [phase]=useFaseSelecionada(pj.projectIdAcc);
  const [state,setState]=useState<Consistency|null>(null);
  const [error,setError]=useState("");
  const [loading,setLoading]=useState(false);
  const [offset,setOffset]=useState(0);
  const [tick,setTick]=useState(0);
  useEffect(()=>setOffset(0),[pj.projectIdAcc,phase]);
  useEffect(()=>{
    if(!pj.projectIdAcc)return;
    const abort=new AbortController();setLoading(true);setError("");
    plantRequest<Consistency>(`${projetoPath(pj.projectIdAcc)}/consistency?limit=60&offset=${offset}${phase?`&phaseId=${encodeURIComponent(phase)}`:""}`,{signal:abort.signal})
      .then(setState).catch(e=>{if(!isAbort(e))setError(descreverErro(e,"Consistência de linha"));})
      .finally(()=>{if(!abort.signal.aborted)setLoading(false);});
    return ()=>abort.abort();
  },[pj.projectIdAcc,phase,offset,tick]);
  const t=state?.status==="ready"?state:null;
  const completed=t?.fullyModelled??0,partial=t?.incomplete??0,unknown=t?.unverified??0;
  return <div className="space-y-5">
    <PlantHeader titulo="Resultados · Consistência de linhas" pj={pj} loading={loading} onRefresh={()=>setTick(x=>x+1)}
      subtitulo="Completude por TagLine: válvulas e instrumentos esperados no P&ID versus componentes 3D comprovados."/>
    <SemProjeto pj={pj}/>
    {error&&<Estado tom="red" titulo="Não foi possível calcular">{error}</Estado>}
    {state?.status!=="ready"&&state&&<Estado tom="amber" titulo="Consistência ainda não comprovada">{state.message} Os resultados só ficam disponíveis após sincronizar e cadastrar as associações de TagLine.</Estado>}
    <div className="grid gap-4 md:grid-cols-3">
      <Cartao titulo="Linhas 100% verificadas"><div className="flex justify-center"><Rosca centro={t?String(completed):"—"} sub="completas" partes={t?[{valor:completed,cor:"bg-emerald-500",rotulo:"Completas"},{valor:partial,cor:"bg-amber-500",rotulo:"Parciais"},{valor:unknown,cor:"bg-slate-500",rotulo:"Não verificadas"}]:[]}/></div></Cartao>
      <Cartao titulo="Pendências de modelagem"><div className="flex justify-center"><Rosca centro={t?String(partial):"—"} sub="incompletas" partes={t?[{valor:partial,cor:"bg-red-500",rotulo:"Pendentes"},{valor:completed,cor:"bg-emerald-500",rotulo:"Completas"},{valor:unknown,cor:"bg-slate-500",rotulo:"Não verificadas"}]:[]}/></div></Cartao>
      <Cartao titulo="Sem vínculo comprovado"><div className="flex justify-center"><Rosca centro={state?String(state.unverified):"—"} sub="não verificadas" partes={state?.status==="ready"?[{valor:unknown,cor:"bg-slate-500",rotulo:"Não verificadas"},{valor:completed+partial,cor:"bg-emerald-500",rotulo:"Verificadas"}]:[]}/></div></Cartao>
    </div>
    {state&&<Cartao titulo={`Linhas analisadas · atributo ${state.tagLineColumn}`}>
      <div className="overflow-auto"><table className="w-full min-w-[730px] text-sm">
        <thead><tr className="border-b text-left text-xs text-muted-foreground"><th className="p-2">Linha</th><th className="p-2">Válvulas 3D/P&ID</th><th className="p-2">Instrumentos 3D/P&ID</th><th className="p-2">Avanço ponderado</th><th className="p-2">Pendência</th></tr></thead>
        <tbody>{state.lines.map(x=><tr key={x.tag} className="border-b border-border/40"><td className="p-2 font-mono">{x.tag}</td><td className="p-2">{x.actualValves}/{x.expectedValves}</td><td className="p-2">{x.actualInstruments}/{x.expectedInstruments}</td><td className="p-2">{x.associationsVerified&&x.overallPercent!==null?`${x.overallPercent.toFixed(1)}%`:"Não comprovado"}</td><td className="p-2 text-xs">{x.issues.join("; ")||"Sem pendências verificadas"}</td></tr>)}</tbody>
      </table></div>
      <div className="mt-3 flex justify-between gap-2 text-xs text-muted-foreground">
        <span>Total do projeto: {state.totalLines} linhas</span><div className="flex gap-2"><button disabled={offset===0||loading} onClick={()=>setOffset(x=>Math.max(0,x-60))} className="rounded border px-3 py-1 disabled:opacity-50">Anterior</button><button disabled={loading||offset+60>=state.totalLines} onClick={()=>setOffset(x=>x+60)} className="rounded border px-3 py-1 disabled:opacity-50">Próxima</button></div>
      </div>
    </Cartao>}
  </div>;
}
