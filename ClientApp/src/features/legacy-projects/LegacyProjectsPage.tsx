import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Database, FileText, Plus, RefreshCw, Search, ShieldCheck, Timer } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { getJson } from "@/lib/api";
import { useProductionSelection } from "@/features/production/ProductionProjectSelector";

type LegacyInput = { projectIdAcc:string; hubIdAcc:string; projectName:string; timesheetProject:string; crOs:string; afe:string };
type Registered = LegacyInput & { documentValidation:string; createdAtUtc:string;updatedAtUtc:string };
type Simulation = {totalHh:number|null;totalLancamentos:number;totalDocumentos:number;
  disciplinas:{disciplina:string;hh:number|null;lancamentos:number;documentos:number}[];
  hhSemDisciplina:number;diferencaHh:number|null;somenteSimulacao:boolean;aviso:string};
type SourceSummary = {entries:number; hours:number; first:string|null;last:string|null};
type DocSummary = {documentCode:string;doc2:string|null;doc3:string|null;doc4:string|null;doc5:string|null;entries:number;hours:number};
type RawRow = {date:string|null;professional:string|null;function:string|null;discipline:string|null;description:string|null;project:string|null;afe:string|null;hours:number};
type DocumentRow = RawRow & {afeOrDoc1:string|null;documentCode:string|null;document:string|null;doc2:string|null;doc3:string|null;doc4:string|null;doc5:string|null};
type Preview = {timesheetProject:string;crOs:string;afe:string;byAfe:SourceSummary;byDocument:SourceSummary;
  documents:DocSummary[];afeRows:RawRow[];documentRows:DocumentRow[];
  afeTruncated:boolean;documentTruncated:boolean;notice:string};
const ENDPOINT="/api/legacy-projects";
const EMPTY:LegacyInput={projectIdAcc:"",hubIdAcc:"",projectName:"",timesheetProject:"",crOs:"",afe:""};
const css="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-foreground outline-none focus:ring-2 focus:ring-primary";
const hh=(v:number)=>Number(v??0).toLocaleString("pt-BR",{minimumFractionDigits:2,maximumFractionDigits:2})+" HH";
const date=(x:string|null)=>x?new Date(x).toLocaleDateString("pt-BR"):"—";
async function request<T>(url:string,body:unknown):Promise<T>{
  const res=await fetch(url,{method:"POST",credentials:"same-origin",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
  if(!res.ok){const d=await res.json().catch(()=>null) as {message?:string}|null;throw new Error(d?.message??`Erro HTTP ${res.status}`);}
  return res.json() as Promise<T>;
}
function Info({label,value,sub}:{label:string;value:string;sub?:string}){
  return <div className="glass-card rounded-xl border border-border/50 p-5">
    <p className="text-xs font-semibold uppercase text-muted-foreground">{label}</p>
    <p className="mt-2 text-2xl font-black text-foreground">{value}</p>
    {sub&&<p className="mt-1 text-xs text-muted-foreground">{sub}</p>}
  </div>;
}
export default function LegacyProjectsPage(){
  const selection=useProductionSelection({});
  const [params]=useSearchParams();
  const initialProject=params.get("projectId");
  const openedRef=useRef("");
  const [registrados,setRegistrados]=useState<Registered[]>([]);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState("");
  const [modal,setModal]=useState(false);
  const [draft,setDraft]=useState<LegacyInput>(EMPTY);
  const [saving,setSaving]=useState(false);
  const [previewing,setPreviewing]=useState(false);
  const [selected,setSelected]=useState<Registered|null>(null);
  const [simulation,setSimulation]=useState<Simulation|null>(null);
  const [simulating,setSimulating]=useState(false);
  const [result,setResult]=useState<Preview|null>(null);
  const [afeOffset,setAfeOffset]=useState(0);
  const [docOffset,setDocOffset]=useState(0);

  const reload=useCallback(async()=>{
    setLoading(true);
    try{setRegistrados(await getJson<Registered[]>(ENDPOINT));setError("");}
    catch(e){setError(e instanceof Error?e.message:"Falha ao carregar projetos antigos.");}
    finally{setLoading(false);}
  },[]);
  useEffect(()=>{void reload();},[reload]);
  useEffect(()=>{
    if(loading||!initialProject||openedRef.current===initialProject)return;
    const found=registrados.find(x=>x.projectIdAcc===initialProject);
    if(found){openedRef.current=initialProject;void abrirDetalhes(found);}
  },[loading,initialProject,registrados]);
  const currentAcc=selection.projects.find(p=>p.id===selection.projectId);
  const existing=registrados.find(x=>x.projectIdAcc===selection.projectId);

  function abrir(){
    if(!currentAcc)return;
    setDraft(existing??{
      projectIdAcc:currentAcc.id,hubIdAcc:selection.hubId,projectName:currentAcc.attributes.name,
      timesheetProject:"",crOs:"",afe:""
    });
    setResult(null);setError("");setModal(true);
  }
  const valid=!!draft.projectIdAcc&&!!draft.hubIdAcc&&!!draft.timesheetProject.trim()&&
    (!!draft.crOs.trim()||!!draft.afe.trim());

  async function preview(offsetA=0,offsetD=0,from?:LegacyInput){
    const data=from??draft;
    if(!data.timesheetProject.trim()||(!data.crOs.trim()&&!data.afe.trim())){
      setError("Informe o código do Projeto no Timesheet e pelo menos um identificador CR-OS ou AFE.");return;
    }
    setPreviewing(true);setError("");
    try{
      const p=await request<Preview>(ENDPOINT+"/preview",{
        timesheetProject:data.timesheetProject,crOs:data.crOs,afe:data.afe,
        afeOffset:offsetA,documentOffset:offsetD});
      setResult(p);setAfeOffset(offsetA);setDocOffset(offsetD);
    }catch(e){setError(e instanceof Error?e.message:"Falha na prévia.");}
    finally{setPreviewing(false);}
  }
  async function salvar(){
    if(!valid)return;
    setSaving(true);setError("");
    try{
      const saved=await request<Registered>(ENDPOINT,draft);
      await reload();
      setSelected(saved);setModal(false);
      setResult(null);
      await abrirDetalhes(saved);
    }catch(e){setError(e instanceof Error?e.message:"Erro ao cadastrar projeto.");}
    finally{setSaving(false);}
  }
  async function abrirDetalhes(item:Registered,offsetA=0,offsetD=0){
    if(selected?.projectIdAcc!==item.projectIdAcc)setSimulation(null);
    setSelected(item);setPreviewing(true);setError("");
    try{
      const r=await getJson<Preview>(`${ENDPOINT}/${encodeURIComponent(item.projectIdAcc)}?afeOffset=${offsetA}&documentOffset=${offsetD}`);
      setResult(r);setAfeOffset(offsetA);setDocOffset(offsetD);
    }catch(e){setError(e instanceof Error?e.message:"Falha ao consultar o projeto.");}
    finally{setPreviewing(false);}
  }
  async function simular(item:Registered){
    setSimulating(true);setError("");setSimulation(null);
    try{setSimulation(await getJson<Simulation>(`${ENDPOINT}/${encodeURIComponent(item.projectIdAcc)}/simulate`));}
    catch(e){setError(e instanceof Error?e.message:"Falha na simulação de disciplinas.");}
    finally{setSimulating(false);}
  }
  const rows=useMemo(()=>registrados.slice().sort((a,b)=>a.projectName.localeCompare(b.projectName,"pt-BR")),[registrados]);
  const isDraft=result!==null && modal;
  function page(a:number,d:number){if(isDraft)void preview(a,d);else if(selected)void abrirDetalhes(selected,a,d);}
  const info=result&&<div className="space-y-4">
    <div className="grid gap-3 sm:grid-cols-2">
      <Info label="AFE · lançamentos auxiliares" value={hh(result.byAfe.hours)} sub={`${result.byAfe.entries} registros na fonte bruta; não somar com documentos`}/>
      <Info label="CR-OS (Doc1) · Timesheet corrigido" value={hh(result.byDocument.hours)} sub={`${result.byDocument.entries} registros da visão corrigida N&A`}/>
    </div>
    <p className="rounded-lg border border-amber-500/30 p-3 text-sm text-amber-600 dark:text-amber-300">
      {result.notice}
    </p>
    <div className="glass-card rounded-xl border border-border/50 p-4">
      <h3 className="mb-3 font-semibold">Documentos encontrados ({result.documents.length}{result.documents.length===500?"+":""} grupos)</h3>
      <div className="max-h-72 overflow-auto"><table className="w-full min-w-[650px] text-left text-xs">
        <thead className="sticky top-0 bg-background"><tr className="border-b">
          <th className="p-2">Código do documento</th><th className="p-2">Área (Doc2)</th>
          <th className="p-2">Disciplina (Doc3)</th><th className="p-2">Tipo (Doc4)</th>
          <th className="p-2">Sequencial (Doc5)</th><th className="p-2">HH</th>
        </tr></thead>
        <tbody>{result.documents.map((x,i)=><tr key={i} className="border-b border-border/40">
          <td className="p-2 font-mono">{x.documentCode}</td><td className="p-2">{x.doc2??"—"}</td>
          <td className="p-2">{x.doc3??"—"}</td><td className="p-2">{x.doc4??"—"}</td>
          <td className="p-2">{x.doc5??"—"}</td><td className="p-2 whitespace-nowrap">{hh(x.hours)}</td>
        </tr>)}</tbody></table></div>
    </div>
    {(["afe","document"] as const).map(source=>{
      const base=source==="afe"?afeOffset:docOffset;
      const list=source==="afe"?result.afeRows:result.documentRows;
      const summary=source==="afe"?result.byAfe:result.byDocument;
      return <div key={source} className="glass-card rounded-xl border border-border/50 p-4">
        <h3 className="mb-2 font-semibold">{source==="afe"?"Lançamentos por AFE (fonte bruta auxiliar)":"Lançamentos documentais corrigidos"} · {summary.entries} linhas</h3>
        <div className="max-h-80 overflow-auto"><table className="w-full min-w-[740px] text-left text-xs">
          <thead className="sticky top-0 bg-background"><tr className="border-b"><th className="p-2">Data</th>
            <th className="p-2">Profissional</th><th className="p-2">Disciplina</th>
            {source==="document"&&<th className="p-2">Código do documento</th>}
            <th className="p-2">Descrição</th><th className="p-2">HH</th>
          </tr></thead><tbody>{list.map((x,i)=><tr key={i} className="border-b border-border/40">
            <td className="p-2 whitespace-nowrap">{date(x.date)}</td><td className="p-2">{x.professional??"—"}</td>
            <td className="p-2">{x.discipline??"—"}</td>
            {source==="document"&&<td className="p-2 font-mono">{(x as DocumentRow).documentCode??"—"}</td>}
            <td className="p-2">{x.description??"—"}</td><td className="p-2 whitespace-nowrap">{hh(x.hours)}</td>
          </tr>)}</tbody></table></div>
        <div className="mt-3 flex items-center justify-between text-xs"><span>Linhas {summary.entries?base+1:0}–{Math.min(base+100,summary.entries)} de {summary.entries}</span>
          <div className="flex gap-2">
            <Button size="sm" variant="outline" disabled={previewing||base===0}
              onClick={()=>page(source==="afe"?Math.max(0,base-100):afeOffset,source==="document"?Math.max(0,base-100):docOffset)}>Anterior</Button>
            <Button size="sm" variant="outline" disabled={previewing||base+100>=summary.entries}
              onClick={()=>page(source==="afe"?base+100:afeOffset,source==="document"?base+100:docOffset)}>Próxima</Button>
          </div>
        </div>
      </div>;
    })}
  </div>;

  return <div className="mx-auto max-w-7xl space-y-6 p-4 md:p-6">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><h1 className="flex items-center gap-2 text-2xl font-bold"><Database className="h-6 w-6"/> ADD Projeto sem LDP</h1>
        <p className="mt-1 text-sm text-muted-foreground">Projetos ACC integrados ao cadastro principal e à Home, com CR-OS e AFE separados. Sem gerar LDP.</p>
      </div><Button variant="outline" onClick={()=>void reload()} disabled={loading}><RefreshCw className="mr-2 h-4 w-4"/>Atualizar</Button>
    </div>
    {error&&<div role="alert" className="rounded-lg border border-red-500/40 p-3 text-sm text-red-500">{error}</div>}
    <div className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
      <div className="flex items-center gap-2 font-semibold"><Search className="h-4 w-4"/> Buscar projeto no ACC</div>
      <div className="grid gap-3 md:grid-cols-[1fr_2fr_auto] md:items-end">
        <label className="text-sm">Conta ACC<select className={css+" mt-1"} value={selection.hubId} onChange={e=>selection.selecionarHub(e.target.value)}>
          {selection.hubs.map(h=><option key={h.id} value={h.id}>{h.attributes.name}</option>)}</select></label>
        <label className="text-sm">Projeto ACC<select className={css+" mt-1"} value={selection.projectId} onChange={e=>selection.selecionarProjeto(e.target.value)}>
          <option value="">Selecione um projeto</option>{selection.projects.map(p=><option key={p.id} value={p.id}>{p.attributes.name}</option>)}</select></label>
        <Button disabled={!currentAcc} onClick={abrir}><Plus className="mr-2 h-4 w-4"/>{existing?"Editar cadastro":"ADD Projeto"}</Button>
      </div>
      {selection.error&&<p className="text-sm text-amber-600">{selection.error} Verifique a conexão Autodesk.</p>}
    </div>
    <div className="glass-card rounded-xl border border-border/50 p-5">
      <h2 className="mb-4 flex items-center gap-2 font-semibold"><ShieldCheck className="h-4 w-4"/> Projetos históricos registrados ({registrados.length})</h2>
      {loading?<p className="text-sm text-muted-foreground">Carregando cadastros…</p>:
        rows.length===0?<p className="text-sm text-muted-foreground">Nenhum projeto cadastrado ainda.</p>:
        <div className="overflow-x-auto"><table className="w-full min-w-[700px] text-left text-sm"><thead><tr className="border-b">
          <th className="p-2">Projeto ACC</th><th className="p-2">Projeto Timesheet</th><th className="p-2">CR-OS</th><th className="p-2">AFE</th><th className="p-2">Situação</th><th className="p-2">Ação</th>
        </tr></thead><tbody>{rows.map(x=><tr key={x.projectIdAcc} className="border-b border-border/40">
          <td className="p-2">{x.projectName}</td><td className="p-2">{x.timesheetProject}</td><td className="p-2">{x.crOs||"—"}</td><td className="p-2">{x.afe||"—"}</td>
          <td className="p-2 text-amber-600">{x.documentValidation==="PROVISORIO"?"Documentos provisórios":x.documentValidation}</td>
          <td className="p-2"><Button size="sm" variant="outline" onClick={()=>void abrirDetalhes(x)}>Abrir auditoria</Button></td>
        </tr>)}</tbody></table></div>}
    </div>
    {selected&&!modal&&<div className="space-y-4"><div className="flex flex-wrap items-center justify-between gap-2">
      <h2 className="flex items-center gap-2 text-lg font-bold"><FileText className="h-5 w-5"/>{selected.projectName} · Timesheet</h2>
      <div className="flex flex-wrap gap-2">
        <Button onClick={()=>void simular(selected)} disabled={simulating||previewing}>
          <ShieldCheck className="mr-2 h-4 w-4"/>{simulating?"Simulando…":"Simular disciplinas"}
        </Button>
        <Button variant="outline" onClick={()=>{setDraft({...selected});setModal(true);setError("");}}><Timer className="mr-2 h-4 w-4"/>Alterar vínculos</Button>
      </div>
    </div>
    {simulation&&<section className="glass-card space-y-3 rounded-xl border border-border/50 p-5" aria-label="Simulação por disciplina">
      <h3 className="font-bold">Simulação da LDP provisória · por disciplina</h3>
      <p className="text-sm text-muted-foreground">{simulation.aviso}</p>
      <div className="grid gap-3 sm:grid-cols-3">
        <Info label="HH documentais realizadas" value={simulation.totalHh==null?"—":hh(simulation.totalHh)} sub={`${simulation.totalLancamentos} lançamentos`}/>
        <Info label="Códigos documentais" value={String(simulation.totalDocumentos)}/>
        <Info label="Diferença de reconciliação" value={simulation.diferencaHh==null?"—":hh(simulation.diferencaHh)}
           sub={simulation.hhSemDisciplina===0?"Todas as HH classificadas":"Há HH sem disciplina mapeada"}/>
      </div>
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr className="border-b">
        <th className="p-2">Disciplina normalizada</th><th className="p-2 text-right">Documentos</th>
        <th className="p-2 text-right">Lançamentos</th><th className="p-2 text-right">HH realizadas</th></tr></thead>
        <tbody>{simulation.disciplinas.map(d=><tr className="border-b border-border/40" key={d.disciplina}>
          <td className="p-2">{d.disciplina}</td><td className="p-2 text-right">{d.documentos}</td>
          <td className="p-2 text-right">{d.lancamentos}</td>
          <td className="p-2 text-right">{d.hh==null?"—":hh(d.hh)}</td>
        </tr>)}</tbody></table></div>
    </section>}
    {previewing?<p>Consultando lançamentos…</p>:info}</div>}
    <Dialog open={modal} onOpenChange={setModal}>
      <DialogContent className="max-w-5xl"><DialogHeader><DialogTitle>Cadastro de projeto sem LDP</DialogTitle>
        <DialogDescription>Selecione a origem dos registros históricos. A busca é exata: Projeto + AFE ou Projeto + Doc1 (CR-OS).</DialogDescription></DialogHeader>
        <div className="grid gap-3 md:grid-cols-3">
          <label className="text-sm md:col-span-3">Projeto ACC<input className={css+" mt-1"} value={draft.projectName} readOnly/></label>
          <label className="text-sm">Projeto no Timesheet *<input className={css+" mt-1"} placeholder="Código no campo Projeto" value={draft.timesheetProject} onChange={e=>{setDraft(x=>({...x,timesheetProject:e.target.value}));setResult(null);}}/></label>
          <label className="text-sm">CR-OS (Doc1)<input className={css+" mt-1"} placeholder="CR-OS nos documentos" value={draft.crOs} onChange={e=>{setDraft(x=>({...x,crOs:e.target.value}));setResult(null);}}/></label>
          <label className="text-sm">AFE (tbTimeSheet)<input className={css+" mt-1"} placeholder="AFE dos lançamentos brutos" value={draft.afe} onChange={e=>{setDraft(x=>({...x,afe:e.target.value}));setResult(null);}}/></label>
        </div>
        <p className="text-xs text-muted-foreground">CR-OS e AFE são independentes (ex.: Projeto BML011, CR-OS BML011.013). Documentos vêm do Timesheet corrigido; Doc2 = Área; Doc5 = Sequencial (confirmados). Custos ainda não calculados.</p>
        <div className="flex flex-wrap justify-end gap-2"><Button variant="outline" onClick={()=>void preview()} disabled={previewing||!valid}>{previewing?"Consultando…":"Prévia dos lançamentos"}</Button>
          <Button onClick={()=>void salvar()} disabled={saving||!valid}>{saving?"Salvando…":"Registrar projeto sem LDP"}</Button></div>
        {modal&&error&&<p className="text-sm text-red-500">{error}</p>}
        {modal&&info}
      </DialogContent>
    </Dialog>
  </div>;
}
