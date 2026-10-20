import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, ArrowRight, CheckCircle2, FileSearch, RefreshCw } from "lucide-react";
import toast from "react-hot-toast";

type Item = {
  originalDocument: string; entries: number; hours: number; cost: number;
  treatment: string; assignedDiscipline: string | null; assignedDestination: string | null;
};
type Overview = {
  projectCode: string; items: Item[]; documentDestinations: string[]; disciplines: string[];
  totalHours: number; totalCost: number; pendingHours: number; confirmedAtHours: number;
};
type Mode = "DOCUMENTO" | "AT";
const formatHH = (v: number) => new Intl.NumberFormat("pt-BR", {maximumFractionDigits:2,minimumFractionDigits:2}).format(v) + " HH";
const money = (v: number) => new Intl.NumberFormat("pt-BR", {style:"currency",currency:"BRL"}).format(v);

export default function OutsideLdpQualityPage({projectId,canEdit=true}:{projectId?:string;canEdit?:boolean}) {
  const [data,setData]=useState<Overview | null>(null);
  const [error,setError]=useState("");
  const [busy,setBusy]=useState(false);
  const [reloadIndex,setReloadIndex]=useState(0);
  const [query,setQuery]=useState("");
  const [status,setStatus]=useState<"ALL"|"PENDENTE"|"AT">("ALL");
  const [editing,setEditing]=useState<Item | null>(null);
  const [mode,setMode]=useState<Mode>("DOCUMENTO");
  const [dest,setDest]=useState("");
  const [discipline,setDiscipline]=useState("");
  const [destinationFilter,setDestinationFilter]=useState("");

  const refresh=useCallback(()=>setReloadIndex(n=>n+1),[]);
  useEffect(()=>{
    if(!projectId){setData(null);return;}
    const abort=new AbortController();
    setError("");
    fetch(`/api/production-audit/timesheet-outside-ldp?projectId=${encodeURIComponent(projectId)}`,{
      credentials:"include",signal:abort.signal
    }).then(async r=>{
      const body=await r.json().catch(()=>({}));
      if(!r.ok)throw new Error(body.message??"Falha ao consultar documentos fora da LDP.");
      return body as Overview;
    }).then(x=>{if(!abort.signal.aborted)setData(x);})
      .catch(e=>{if(!abort.signal.aborted)setError(e instanceof Error?e.message:"Erro ao consultar documentos.");});
    return ()=>abort.abort();
  },[projectId,reloadIndex]);

  const rows=useMemo(()=>(data?.items??[]).filter(d=>
    (status==="ALL"||d.treatment===status)&&d.originalDocument.toLowerCase().includes(query.toLowerCase())
  ),[data,status,query]);
  const possibleDocs=useMemo(()=>(data?.documentDestinations??[])
    .filter(x=>x.toLowerCase().includes(destinationFilter.toLowerCase()))
    .slice(0,250),[data,destinationFilter]);

  function edit(d:Item) {
    setEditing(d);
    setDest("");
    setMode(d.treatment==="AT"?"AT":"DOCUMENTO");
    setDiscipline(d.assignedDiscipline??"");
    setDestinationFilter("");
  }

  async function save() {
    if(!canEdit||!editing||!projectId||busy)return;
    const destination=dest.trim().toUpperCase();
    if(mode==="DOCUMENTO" && !data?.documentDestinations.includes(destination)) {
      toast.error("Escolha um Número do Documento cadastrado na LDP do projeto.");return;
    }
    if(mode==="AT" && !discipline.trim()) {
      toast.error("Escolha a disciplina para classificar como AT.");return;
    }
    if(mode==="AT"&&editing.treatment==="AT"&&editing.assignedDiscipline===discipline){
      toast("Esse documento já está classificado como AT na disciplina selecionada.");return;
    }
    const notice=mode==="DOCUMENTO"
      ? `Direcionar ${formatHH(editing.hours)} e ${money(editing.cost)} de ${editing.originalDocument} para ${destination}?${editing.treatment==="AT"?" A classificação AT anterior será desativada.":""} O registro original será preservado.`
      : `Classificar ${formatHH(editing.hours)} e ${money(editing.cost)} como AT na disciplina ${discipline}? Essa classificação não cria documento na LDP.`;
    if(!window.confirm(notice))return;
    setBusy(true);
    try{
      const response=await fetch(`/api/production-audit/timesheet-outside-ldp/direction?projectId=${encodeURIComponent(projectId)}`,{
        method:"POST",credentials:"include",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({originalDocument:editing.originalDocument,mode,
          destinationDocument:mode==="DOCUMENTO"?destination:null,
          discipline:mode==="AT"?discipline:null})
      });
      const result=await response.json().catch(()=>({}));
      if(!response.ok)throw new Error(result.message??"Erro ao salvar direcionamento.");
      toast.success(`${formatHH(editing.hours)} direcionadas com sucesso.`);
      setEditing(null);refresh();
    }catch(e){toast.error(e instanceof Error?e.message:"Erro ao salvar.");}finally{setBusy(false);}
  }

  if(!projectId)return null;
  return <section className="rounded-2xl border border-border bg-card/70 p-4 md:p-5 space-y-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <p className="text-[11px] font-black uppercase tracking-widest text-muted-foreground">
          Qualidade · Conciliação documental
        </p>
        <h2 className="text-lg font-bold">Documentos do Timesheet fora da LDP</h2>
        <p className="max-w-3xl text-xs leading-relaxed text-muted-foreground">
          Lançamentos com Número do Documento identificado, mas ausente da LDP. Aqui você visualiza
          os AT já confirmados e pode direcionar cada código para um documento existente na LDP,
          sem substituir os dados originais. Esses registros não aparecem no quadro de HH sem código.
        </p>
      </div>
      <button type="button" onClick={refresh} className="inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-semibold hover:bg-accent">
        <RefreshCw size={14}/> Atualizar
      </button>
    </div>
    {error && <p role="alert" className="text-sm text-destructive"><AlertTriangle size={15} className="mr-1 inline"/>{error}</p>}
    {!data&&!error&&<p className="text-sm text-muted-foreground">Consultando Timesheet e LDP...</p>}
    {data&&<>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Metric label="Total fora da LDP" value={formatHH(data.totalHours)}/>
        <Metric label="Custo associado" value={money(data.totalCost)}/>
        <Metric label="AT já confirmadas" value={formatHH(data.confirmedAtHours)}/>
        <Metric label="Pendentes" value={formatHH(data.pendingHours)}/>
      </div>
      <p className="text-xs text-muted-foreground">
        AT confirmado = horas e custo já atribuídos à disciplina gerencial, sem criação de documento na LDP.
        Pendente = precisa de decisão. Direcionar para a LDP altera apenas o De/Para gerencial, preservando a origem.
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <select value={status} onChange={e=>setStatus(e.target.value as typeof status)}
          className="rounded-lg border border-border bg-background px-3 py-2 text-xs">
          <option value="ALL">Todos os estados</option>
          <option value="PENDENTE">Pendentes</option>
          <option value="AT">AT confirmados</option>
        </select>
        <input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Pesquisar Número do Documento"
          className="min-w-[230px] flex-1 rounded-lg border border-border bg-background px-3 py-2 text-xs"/>
        <span className="text-xs text-muted-foreground">{rows.length} códigos</span>
      </div>
      <div className="max-h-[500px] overflow-auto">
        <table className="w-full min-w-[840px] text-left text-xs">
          <thead className="sticky top-0 bg-card">
            <tr className="border-b text-muted-foreground">
              <th className="py-2 pr-3">Número do Documento original</th>
              <th className="py-2 text-right">Lançamentos</th>
              <th className="py-2 text-right">HH</th>
              <th className="py-2 text-right">Custo</th>
              <th className="py-2 pl-4">Situação</th>
              <th className="py-2 text-right">Ação</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(x=><tr key={x.originalDocument} className="border-b border-border/60">
              <td className="py-2.5 pr-3 font-semibold">{x.originalDocument}</td>
              <td className="py-2.5 text-right tabular-nums">{x.entries}</td>
              <td className="py-2.5 text-right tabular-nums">{formatHH(x.hours)}</td>
              <td className="py-2.5 text-right tabular-nums">{money(x.cost)}</td>
              <td className="py-2.5 pl-4">
                {x.treatment==="AT"
                  ? <span className="inline-flex items-center gap-1 text-emerald-600"><CheckCircle2 size={13}/> AT confirmado · {x.assignedDiscipline}</span>
                  : <span className="text-amber-600">Pendente de direcionamento</span>}
              </td>
              <td className="py-2.5 text-right">
                {canEdit&&<button type="button" onClick={()=>edit(x)}
                  className="inline-flex items-center gap-1 font-semibold text-primary hover:underline">
                  Direcionar <ArrowRight size={13}/>
                </button>}
              </td>
            </tr>)}
          </tbody>
        </table>
        {!rows.length&&<p className="p-3 text-sm text-muted-foreground">Nenhum documento encontrado no filtro.</p>}
      </div>
      {canEdit&&editing&&<div className="rounded-xl border border-primary/30 bg-muted/20 p-4 space-y-3">
        <div className="flex items-start justify-between gap-2">
          <div><h3 className="font-semibold text-sm"><FileSearch size={15} className="mr-1 inline"/> {editing.originalDocument}</h3>
          <p className="text-xs text-muted-foreground">{formatHH(editing.hours)} · {money(editing.cost)} · {editing.entries} lançamentos</p></div>
          <button type="button" onClick={()=>setEditing(null)} className="text-xs text-muted-foreground hover:underline">Cancelar</button>
        </div>
        <div className="flex flex-wrap gap-4 text-xs">
          <label className="inline-flex items-center gap-2"><input type="radio" checked={mode==="DOCUMENTO"} onChange={()=>setMode("DOCUMENTO")}/>
            Direcionar para Número do Documento da LDP</label>
          <label className="inline-flex items-center gap-2"><input type="radio" checked={mode==="AT"} onChange={()=>setMode("AT")}/>
            Tratar como AT por disciplina</label>
        </div>
        {mode==="DOCUMENTO"?<div className="space-y-2">
          <input value={destinationFilter} onChange={e=>{setDestinationFilter(e.target.value);setDest("");}}
            className="w-full rounded-lg border border-border bg-background px-3 py-2 text-xs" placeholder="Filtrar os documentos disponíveis na LDP"/>
          <select value={dest} onChange={e=>setDest(e.target.value)}
            className="w-full rounded-lg border border-border bg-background px-3 py-2 text-xs">
            <option value="">Selecione o Número do Documento de destino...</option>
            {possibleDocs.map(code=><option key={code} value={code}>{code}</option>)}
          </select>
        </div>:<select value={discipline} onChange={e=>setDiscipline(e.target.value)}
            className="w-full rounded-lg border border-border bg-background px-3 py-2 text-xs">
            <option value="">Selecione a disciplina da AT...</option>
            {data.disciplines.concat(editing.assignedDiscipline??[]).filter((x,i,a)=>a.indexOf(x)===i)
              .map(d=><option key={d} value={d}>{d}</option>)}
          </select>}
        {editing.treatment==="AT"&&mode==="DOCUMENTO"&&<p className="text-xs text-amber-700">
          Esta atividade já foi classificada como AT. Ao direcioná-la para a LDP, a classificação AT anterior
          será desativada para evitar dupla contabilização. O código e a descrição originais serão preservados.
        </p>}
        <button type="button" disabled={busy||(mode==="DOCUMENTO"?!dest:!discipline)}
          onClick={()=>void save()} className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-primary-foreground disabled:opacity-50">
          {busy?"Salvando...":"Confirmar direcionamento"}
        </button>
      </div>}
    </>}
  </section>;
}
function Metric({label,value}:{label:string;value:string}){
  return <div className="rounded-xl border border-border p-3">
    <p className="text-[11px] text-muted-foreground">{label}</p>
    <p className="mt-1 text-lg font-bold tabular-nums">{value}</p>
  </div>;
}
