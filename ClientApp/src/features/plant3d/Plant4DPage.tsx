// Planejamento 4D nativo do N&A: Excel real + comparação DCF COMPLETED.
// O DCF não possui histórico de conclusão por item: exibimos a Curva S PLANEJADA
// e o percentual REALIZADO ATUAL, nunca pontos históricos estimados.
import { useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { Line, LineChart, CartesianGrid, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { Button } from "@/components/ui/button";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { descreverErro, isAbort, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { Cartao, Estado, PlantHeader, SemProjeto } from "./plantShared";

type Categoria = { id: string; label: string };
type Grouping = "dia" | "semana" | "mes";
type PlanRow = { tag: string; date: string; weight: number };
type Result = { status:string; reason:string|null; revisionToken:string|null; categoryId:string;
  total:number; matched:number; missing3d:number; divergent:number; missingPid:number;
  plannedToday:number; actualToday:number; totalWeight:number; asOfDate:string;
  plannedCurve:{date:string; planned:number}[];
  items:{tag:string; dueDate:string; weight:number; state:string}[] };

const campo = "rounded-lg border border-border bg-background px-3 py-2 text-sm text-foreground";
const rotulo:Record<string,string> = {
  matched:"Modelado e conferido", "missing-3d":"Ausente no 3D",
  divergent:"Divergência de atributos", "missing-pid":"Sem TAG correspondente no P&ID"
};
function isoDate(v: unknown):string|null {
  if(typeof v==="number") {
    const x=XLSX.SSF.parse_date_code(v);
    if(!x||x.y<1900||x.y>2100) return null;
    return `${x.y}-${String(x.m).padStart(2,"0")}-${String(x.d).padStart(2,"0")}`;
  }
  const s=String(v??"").trim();
  const iso=/^(\d{4})-(\d{1,2})-(\d{1,2})/.exec(s);
  const br=/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(s);
  const parts=iso?[+iso[1],+iso[2],+iso[3]]:br?[+br[3],+br[2],+br[1]]:null;
  if(!parts)return null;
  const [y,m,d]=parts;
  const dt=new Date(Date.UTC(y,m-1,d));
  return dt.getUTCFullYear()===y&&dt.getUTCMonth()+1===m&&dt.getUTCDate()===d
    ? `${y}-${String(m).padStart(2,"0")}-${String(d).padStart(2,"0")}` : null;
}
function weightOf(v:unknown):number|null {
  if(typeof v==="number")return Number.isFinite(v)&&v>0?v:null;
  const s=String(v??"").trim();
  const normalized=s.includes(",")?s.replace(/\./g,"").replace(",","."):s;
  const n=Number(normalized);
  return Number.isFinite(n)&&n>0?n:null;
}
export default function Plant4DPage(){
  const pj=usePlantProjects();
  const [categories,setCategories]=useState<Categoria[]>([]);
  const [category,setCategory]=useState("");
  const [book,setBook]=useState<Record<string,unknown[][]>>({});
  const [sheet,setSheet]=useState("");
  const [header,setHeader]=useState(0);
  const [tagCol,setTagCol]=useState("");
  const [dateCol,setDateCol]=useState("");
  const [weightCol,setWeightCol]=useState("");
  const [group,setGroup]=useState<Grouping>("mes");
  const [result,setResult]=useState<Result|null>(null);
  const [loading,setLoading]=useState(false);
  const [error,setError]=useState("");
  const [fileName,setFileName]=useState("");
  const [tick,setTick]=useState(0);

  useEffect(()=>{
    setResult(null);setCategories([]);setCategory("");setError("");
    if(!pj.projectIdAcc)return;
    const abort=new AbortController();
    plantRequest<{mapping:Categoria[]}>(`${projetoPath(pj.projectIdAcc)}/configuration`,{signal:abort.signal})
      .then(x=>{if(!abort.signal.aborted){const cats=x.mapping??[];setCategories(cats);setCategory(cats[0]?.id??"");}})
      .catch(e=>{if(!isAbort(e))setError(descreverErro(e,"Categorias Plant 3D"));});
    return ()=>abort.abort();
  },[pj.projectIdAcc,tick]);
  useEffect(()=>{setResult(null);},[category,group,sheet,header,tagCol,dateCol,weightCol,pj.projectIdAcc]);
  const data=book[sheet]??[];
  const headers=useMemo(()=> (data[header]??[]).map((name,i)=>({
    key:String(i),label:String(name??"").trim()||`Coluna ${i+1}`
  })),[data,header]);
  async function loadExcel(f:File|undefined){
    setError("");setResult(null);
    if(!f)return;
    if(!/\.xlsx$/i.test(f.name)){setError("Selecione um arquivo XLSX.");return;}
    if(f.size>6_000_000){setError("O arquivo excede 6 MB.");return;}
    try{
      const b=XLSX.read(await f.arrayBuffer(),{type:"array",cellDates:false,sheetRows:25050});
      const sheets:Record<string,unknown[][]>={};
      for(const name of b.SheetNames.slice(0,20)){
        const rows=XLSX.utils.sheet_to_json<unknown[]>(b.Sheets[name],{header:1,raw:true,defval:""});
        if(rows.length>0)sheets[name]=rows;
      }
      setBook(sheets);setFileName(f.name);
      setSheet(Object.keys(sheets)[0]??"");
      setHeader(0);setTagCol("");setDateCol("");setWeightCol("");
      if(!Object.keys(sheets).length)setError("Não há abas com dados no Excel.");
    }catch{setError("Falha ao ler o arquivo XLSX.");}
  }
  async function calculate(){
    if(!pj.projectIdAcc||!category||!sheet||tagCol===""||dateCol===""){
      setError("Selecione projeto, categoria, aba, TAG e data planejada.");return;
    }
    const rows:PlanRow[]=[];let bad=0;
    for(const r of data.slice(header+1)){
      const t=String(r[Number(tagCol)]??"").trim();
      const rawDate=r[Number(dateCol)];
      if(!t&&!String(rawDate??"").trim())continue;
      const d=isoDate(rawDate);
      const w=weightCol===""?1:weightOf(r[Number(weightCol)]);
      if(!t||!d||w===null){bad++;continue;}
      rows.push({tag:t,date:d,weight:w});
    }
    if(bad){setError(`${bad} linha(s) com TAG, data ou peso inválido(s). Corrija a planilha antes de calcular.`);return;}
    if(rows.length<1||rows.length>25000){setError("O escopo deve conter entre 1 e 25.000 TAGs.");return;}
    setError("");setLoading(true);setResult(null);
    try{
      const x=await plantRequest<Result>(`${projetoPath(pj.projectIdAcc)}/4d/calculate`,{
        method:"POST",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({categoryId:category,grouping:group,rows})
      });
      setResult(x);
    }catch(e){setError(descreverErro(e,"Cálculo 4D"));}finally{setLoading(false);}
  }
  const items=result?.items??[];
  return <div className="space-y-5">
    <PlantHeader titulo="Plant 3D · Planejamento 4D" pj={pj} loading={loading}
      onRefresh={()=>setTick(x=>x+1)}
      subtitulo="Cronograma Excel × modelo 3D comprovado nos snapshots COMPLETED do Dashboard BIM."/>
    <SemProjeto pj={pj}/>
    {error&&<Estado tom="red" titulo="Atenção ao planejamento">{error}</Estado>}
    <Cartao titulo="1. Configurar escopo">
      <div className="grid gap-3 md:grid-cols-3">
        <label className="text-sm">Categoria DE/PARA<select className={campo+" mt-1 w-full"} value={category} onChange={e=>setCategory(e.target.value)}>
          <option value="">Escolha a categoria</option>{categories.map(x=><option key={x.id} value={x.id}>{x.label} ({x.id})</option>)}
        </select></label>
        <label className="text-sm">Arquivo do cronograma (XLSX, até 6 MB)
          <input className={campo+" mt-1 w-full"} type="file" accept=".xlsx" onChange={e=>loadExcel(e.target.files?.[0])}/>
        </label>
        <label className="text-sm">Aba<select className={campo+" mt-1 w-full"} value={sheet} onChange={e=>{setSheet(e.target.value);setHeader(0);setTagCol("");setDateCol("");setWeightCol("");}}>
          <option value="">Escolha a aba</option>{Object.keys(book).map(x=><option key={x}>{x}</option>)}
        </select></label>
      </div>
      {sheet&&<div className="mt-4 grid gap-3 md:grid-cols-4">
        <label className="text-sm">Linha do cabeçalho<select className={campo+" mt-1 w-full"} value={header} onChange={e=>{setHeader(Number(e.target.value));setTagCol("");setDateCol("");setWeightCol("");}}>
          {data.slice(0,Math.min(data.length,20)).map((_,i)=><option key={i} value={i}>Linha {i+1}</option>)}
        </select></label>
        {([["TAG",tagCol,setTagCol],["Data prevista",dateCol,setDateCol],["Peso/HH (opcional)",weightCol,setWeightCol]] as const)
          .map(([label,value,set])=><label className="text-sm" key={label}>{label}
            <select className={campo+" mt-1 w-full"} value={value} onChange={e=>set(e.target.value)}>
              <option value="">— {label==="Peso/HH (opcional)"?"1 por item":"Selecionar"} —</option>
              {headers.map(x=><option key={x.key} value={x.key}>{x.label}</option>)}
            </select></label>)}
      </div>}
      {fileName&&<p className="mt-2 text-xs text-muted-foreground">Arquivo local: {fileName} · somente os dados mapeados são enviados ao servidor para cálculo.</p>}
      <div className="mt-4 flex flex-wrap items-center gap-3">
        <label className="text-sm">Agrupar curva em <select className={campo+" ml-2"} value={group} onChange={e=>setGroup(e.target.value as Grouping)}>
          <option value="dia">Dias</option><option value="semana">Semanas</option><option value="mes">Meses</option>
        </select></label>
        <Button onClick={calculate} disabled={loading||!pj.projectIdAcc||!sheet||!category}>{loading?"Calculando…":"CALCULAR 4D REAL"}</Button>
      </div>
    </Cartao>
    {result?.status==="no-baseline"&&<Estado tom="amber" titulo="Histórico incompleto">{result.reason}</Estado>}
    {result?.status==="ready"&&<>
      <div className="grid gap-4 md:grid-cols-3">
        <Cartao titulo="Planejado até hoje"><p className="text-3xl font-black">{result.plannedToday.toFixed(1)}%</p></Cartao>
        <Cartao titulo="Modelagem conferida hoje"><p className="text-3xl font-black">{result.actualToday.toFixed(1)}%</p></Cartao>
        <Cartao titulo="TAGs modeladas"><p className="text-3xl font-black">{result.matched} / {result.total}</p>
          <p className="text-xs text-muted-foreground">Divergentes: {result.divergent}; sem 3D: {result.missing3d}; sem P&ID: {result.missingPid}.</p></Cartao>
      </div>
      <Cartao titulo="Curva S planejada — datas e pesos do Excel">
        <div className="h-72 w-full"><ResponsiveContainer width="100%" height="100%">
          <LineChart data={result.plannedCurve}><CartesianGrid strokeDasharray="3 3"/>
            <XAxis dataKey="date" tick={{fontSize:11}}/><YAxis domain={[0,100]} unit="%"/>
            <Tooltip formatter={(v:number)=>[`${v}%`,"Planejado"]}/>
            <Line dataKey="planned" name="Planejado" stroke="#60a5fa" dot={false} strokeWidth={2}/>
          </LineChart></ResponsiveContainer></div>
        <p className="text-xs text-muted-foreground">Realizado: fotografia da última versão DCF COMPLETED, não curva histórica. Data de avaliação: {String(result.asOfDate).slice(0,10)}. Revisão: {result.revisionToken??"—"}.</p>
      </Cartao>
      <Cartao titulo="Conferência de TAGs">
        <div className="max-h-96 overflow-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left">
          <th className="p-2">TAG</th><th className="p-2">Previsão</th><th className="p-2">Peso</th><th className="p-2">Estado atual</th></tr></thead>
          <tbody>{items.slice(0,1000).map((x,i)=><tr key={i} className="border-b border-border/30"><td className="p-2 font-mono">{x.tag}</td>
            <td className="p-2">{x.dueDate}</td><td className="p-2">{x.weight}</td>
            <td className="p-2">{rotulo[x.state]??x.state}</td></tr>)}</tbody></table></div>
        {items.length>1000&&<p className="text-xs text-muted-foreground">Visualizando 1.000 de {items.length} linhas; resumo considera todas as linhas.</p>}
      </Cartao>
    </>}
  </div>;
}
