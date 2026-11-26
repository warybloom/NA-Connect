import { useEffect, useState } from "react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { plantRequest, projetoPath, descreverErro, isAbort, type PlantResults } from "@/components/plant3d/plantApi";
import { Cartao, Estado, PlantHeader, SemProjeto, Rosca } from "./plantShared";

type Cat = "line" | "equip" | "inst" | "valve" | "tieins";
const LABELS: Record<Cat,string> = { line:"Linhas",equip:"Equipamentos",inst:"Instrumentos",valve:"Válvulas",tieins:"Tie-ins" };
export default function PlantItemResultsPage({category}:{category:Cat}) {
  const pj=usePlantProjects();
  const [data,setData]=useState<PlantResults|null>(null);
  const [error,setError]=useState("");
  const [loading,setLoading]=useState(false);
  const [tick,setTick]=useState(0);
  const [page,setPage]=useState(0);
  const pageSize=35;
  useEffect(()=>{
    setPage(0);setData(null);setError("");
  },[category,pj.projectIdAcc]);
  useEffect(()=>{
    if(!pj.projectIdAcc)return;
    const controller=new AbortController();
    setLoading(true);setError("");
    plantRequest<PlantResults>(`${projetoPath(pj.projectIdAcc)}/results?category=${encodeURIComponent(category)}&limit=${pageSize}&offset=${page*pageSize}`,
      {signal:controller.signal}).then(setData).catch(e=>{if(!isAbort(e))setError(descreverErro(e,"Resultados por item"));})
      .finally(()=>{if(!controller.signal.aborted)setLoading(false);});
    return ()=>controller.abort();
  },[pj.projectIdAcc,category,page,tick]);
  const t=data?.status==="ready"?data.categories.find(c=>c.id===category):null;
  const total=t?.pidCount??0,modelled=t?.pipingCount??0;
  const identified=(t?.matched??0)+(t?.divergent??0);
  const missing=(t?.missing3d??0), extra=(t?.extra3d??0);
  const problems=(t?.divergent??0)+(t?.taglessPid??0)+(t?.tagless3d??0);
  return <div className="space-y-5">
    <PlantHeader pj={pj} titulo={`Resultados · ${LABELS[category]}`}
      subtitulo="Visão gráfica do comparativo P&ID × 3D, com os filtros salvos aplicados em todas as contagens."
      loading={loading} onRefresh={()=>setTick(v=>v+1)}/>
    <SemProjeto pj={pj}/>
    {error&&<Estado tom="red" titulo="Falha na leitura dos dados">{error}</Estado>}
    {data?.status==="no-mapping"&&<Estado tom="amber" titulo="Categoria sem DE/PARA">Cadastre a categoria em Configurações para habilitar os resultados.</Estado>}
    {data?.status==="no-baseline"&&<Estado tom="amber" titulo="Aguardando sincronização">A versão-base dos modelos ainda não está completa.</Estado>}
    {data?.status==="unlinked"&&<Estado tom="amber" titulo="Projeto não vinculado">Associe o projeto ao Dashboard BIM.</Estado>}
    {t&&<div className="grid gap-4 lg:grid-cols-3">
      <Cartao titulo="Quantitativo P&ID × 3D"><div className="flex justify-center"><Rosca centro={String(total)} sub="P&ID válidos" partes={[{valor:modelled,cor:"bg-blue-500",rotulo:"3D"},{valor:Math.max(0,total-modelled),cor:"bg-slate-500",rotulo:"Diferença"}]}/></div><p className="text-center text-sm text-muted-foreground">{modelled.toLocaleString("pt-BR")} registros 3D · {total.toLocaleString("pt-BR")} P&ID</p></Cartao>
      <Cartao titulo="Pareamento por TAG"><div className="flex justify-center"><Rosca centro={String(identified)} sub="TAGs nos 2 lados" partes={[{valor:t.matched,cor:"bg-emerald-500",rotulo:"Sem divergência"},{valor:t.divergent,cor:"bg-amber-500",rotulo:"Divergentes"},{valor:missing,cor:"bg-red-500",rotulo:"Ausentes 3D"}]}/></div><p className="text-center text-sm text-muted-foreground">{missing} ausentes no 3D · {extra} adicionais no 3D</p></Cartao>
      <Cartao titulo="Problemas identificados"><div className="flex justify-center"><Rosca centro={String(problems)} sub="Ocorrências" partes={[{valor:t.divergent,cor:"bg-amber-500",rotulo:"Divergências"},{valor:t.taglessPid,cor:"bg-slate-500",rotulo:"Sem TAG P&ID"},{valor:t.tagless3d,cor:"bg-red-500",rotulo:"Sem TAG 3D"}]}/></div><p className="text-center text-sm text-muted-foreground">Os problemas podem corresponder a objetos distintos; não são percentual de qualidade.</p></Cartao>
    </div>}
    {t&&<Cartao titulo="Itens e divergências">
      {data!.items.length===0?<p className="text-sm text-muted-foreground">Nenhum item nesta página de resultados.</p>:
      <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left text-muted-foreground"><th className="p-2">TAG</th><th className="p-2">Situação</th><th className="p-2">PnPID</th><th className="p-2">3D</th><th className="p-2">Diferenças</th></tr></thead>
        <tbody>{data!.items.map(x=><tr key={x.id} className="border-b border-border/40"><td className="p-2 font-mono">{x.tag||"Sem TAG válida"}</td><td className="p-2">{x.classification}</td><td className="p-2">{x.pidRowId||"—"}</td><td className="p-2">{x.pipingRowId||"—"}</td><td className="p-2 text-xs">{Array.isArray(x.details)?(x.details as Array<{name:string}>).map(y=>y.name).join(", ")||"—":"—"}</td></tr>)}</tbody>
      </table></div>}
      <div className="mt-4 flex items-center justify-between text-sm"><span>{data!.totalItems.toLocaleString("pt-BR")} registros de auditoria</span>
        <div className="flex gap-2"><button className="rounded border px-3 py-1 disabled:opacity-40" disabled={page===0||loading} onClick={()=>setPage(x=>x-1)}>Anterior</button><button className="rounded border px-3 py-1 disabled:opacity-40" disabled={loading||(page+1)*pageSize>=data!.totalItems} onClick={()=>setPage(x=>x+1)}>Próxima</button></div>
      </div>
    </Cartao>}
    {loading&&!data&&<p className="text-sm text-muted-foreground"><RefreshCw className="mr-2 inline h-4 w-4 animate-spin"/>Carregando dados reais...</p>}
    {category==="tieins"&&!t&&data?.status==="ready"&&<Estado tom="amber" titulo="Tie-ins sem configuração">Essa categoria precisa de tabela e TAG cadastradas no DE/PARA para entrar na auditoria.</Estado>}
  </div>;
}
