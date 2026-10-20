import {useMemo,useState} from "react";
import type {PmoSelecionado} from "@/components/pmo/pmoMultiCore";
import {formatCurrency,formatHH,usePmoReports} from "@/components/pmo/pmoData";
import {enrichDocuments,projectFacts,type FactsReport} from "@/components/pmo/pmoFacts";
import {ReportState} from "@/components/reports/ReportComponents";
import {disciplinasParaExibicao,pertenceAoGrupo} from "./disciplinasApresentacao";
import {FASES,faseHhPrevisto} from "./gestaoVistaJamileLogic";
const fmt=(n:number|null|undefined)=>typeof n==="number"?formatHH(n):"—";
const brl=(n:number|null|undefined)=>typeof n==="number"?formatCurrency(n):"—";
export default function GestaoVistaDisciplinasPage({selection,mode}:{selection:PmoSelecionado[];mode:"hh"|"custo"}){
 const {reports,falhas,loading,error,retry}=usePmoReports("gestao-vista",{hub:"",project:"",selection});
 const fr=reports as FactsReport[];
 const groups=useMemo(()=>disciplinasParaExibicao(fr),[fr]);
 const [active,setActive]=useState("");
 if(loading)return <ReportState title="Carregando Gestão à Vista…">Consultando disciplinas.</ReportState>;
 if(error)return <ReportState title={error} error><button onClick={retry} className="underline text-primary">Tentar novamente</button></ReportState>;
 // Consolidado: um projeto sem dados (falha/sem permissão) bloqueia os números — total parcial nunca aparece como total.
 if(falhas.length>0)return <ReportState title={`Consolidação incompleta: ${falhas.length} de ${selection.length} projeto(s) sem dados`} error>
  Os números não são exibidos para não apresentar um total parcial como consolidado.
  <ul className="mt-2 text-left">{falhas.map(f=><li key={f.projectId}><b>{f.projectName}</b> — {f.message}</li>)}</ul>
  <button onClick={retry} className="mt-2 underline text-primary">Tentar novamente</button></ReportState>;
 const consolidado=selection.length>1;
 const nomeProjeto=new Map(fr.map(r=>[r.projectId,r.nomeProjeto]));
 const facts=projectFacts(fr),selected=groups.find(g=>g.idExibicao===active);
 const type2=fr.some(r=>r.tipoLdp===2);
 const docs=selected?enrichDocuments(fr).filter(d=>!d.isActivity&&pertenceAoGrupo(d.disciplinaCanonica,d.disciplina,selected)):[];
 const value=(hh:number|null|undefined,cost:number|null|undefined)=>mode==="hh"?fmt(hh):brl(cost);
 const metrics=mode==="hh"?[
  ["HH PLANEJADO",fmt(facts.cards.HH_Previsto)],["HH REALIZADO",fmt(facts.cards.HH_Realizado)],
  ["SALDO HH",fmt(facts.cards.SaldoHH)],["% HH CONSUMIDO",facts.cards.ConsumoHH==null?"—":fmt(facts.cards.ConsumoHH)+"%"]
 ]:[
  ["CUSTO PLANEJADO",brl(facts.cards.Custo_Previsto)],["CUSTO REALIZADO",brl(facts.cards.Custo_Realizado)],
  ["SALDO DE CUSTO",brl(facts.cards.SaldoCusto)],["% CUSTO CONSUMIDO",facts.cards.ConsumoCusto==null?"—":fmt(facts.cards.ConsumoCusto)+"%"]
 ];
 return <div className="space-y-4 rounded bg-card p-4 text-foreground">
  <header className="border-b-2 border-[#5582aa] pb-3"><b className="text-sm text-[#164b73] dark:text-sky-300">GERÊNCIA DA ENGENHARIA · GESTÃO À VISTA</b>
   <h2 className="text-xl font-black">{mode==="hh"?"HH POR DISCIPLINA":"CONTROLE DE CUSTO POR DISCIPLINA"}</h2>
   {consolidado&&<small className="block truncate text-xs text-muted-foreground" title={selection.map(p=>p.name).join("\n")} data-testid="gv-escopo">
    CONSOLIDADO — {selection.length} PROJETOS: {selection.map(p=>p.name).join(" · ")}</small>}
  </header>
  <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{metrics.map(([label,val])=><div key={label} className="rounded border border-border bg-card p-3 text-center shadow-sm">
   <b className="text-xs">{label}</b><p className="mt-2 text-xl font-black text-[#0e1553] dark:text-sky-100">{val}</p></div>)}</div>
  <p className="text-xs font-black text-[#406b90] dark:text-sky-300">CLIQUE NA DISCIPLINA PARA VER OS DOCUMENTOS E LANÇAMENTOS</p>
  <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{groups.map(d=>{
   const phases=faseHhPrevisto(fr,x=>pertenceAoGrupo(x.disciplinaCanonica,x.disciplina,d));
   return <button key={d.idExibicao} onClick={()=>setActive(active===d.idExibicao?"":d.idExibicao)}
    className={"rounded border border-t-4 p-3 text-left shadow-sm "+(active===d.idExibicao?"border-blue-800 dark:border-sky-400 bg-muted":"border-border border-t-[#5582aa] bg-card")}>
    <strong className="block truncate text-xs text-[#0e1553] dark:text-sky-100" title={d.Disciplina}>{d.Disciplina}</strong>
    <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
     <span>Previsto <b className="block">{value(d.HH_Previsto,d.Custo_Previsto)}</b></span>
     <span>Realizado <b className="block">{value(d.HH_Realizado,d.Custo_Realizado)}</b></span>
    </div>
    <p className="mt-2 border-t border-border pt-2 text-xs">Saldo <b>{value(d.SaldoHH,d.SaldoCusto)}</b></p>
    {mode==="hh"&&type2&&<div className="mt-2 grid grid-cols-3 divide-x divide-border rounded border border-border bg-muted/60 text-center">
     {FASES.map(g=><div key={g} className="p-1 text-[10px]"><b>{g}%</b><br/>{fmt(phases[g])} h</div>)}
    </div>}
    {mode==="custo"&&<div className="mt-2 text-xs">Consumo: <b>{d.ConsumoCusto==null?"—":fmt(d.ConsumoCusto)+"%"}</b></div>}
   </button>;})}</div>
  {selected&&<section className="rounded border border-border">
   <h3 className="bg-[#0e1553] px-4 py-2 text-xs font-black text-white">{selected.Disciplina} · DOCUMENTOS</h3>
   <div className="max-h-[350px] overflow-auto"><table className="w-full text-left text-xs"><thead className="bg-muted"><tr>
     {[...(consolidado?["PROJETO"]:[]),"CÓDIGO DO DOCUMENTO","SITUAÇÃO","PREVISTO","REALIZADO"].map(x=><th key={x} className="p-2">{x}</th>)}
   </tr></thead><tbody>{docs.map(d=><tr key={d.rowId} className="border-t border-border">
    {consolidado&&<td className="p-2">{nomeProjeto.get(d.projectId)??d.projectId}</td>}
    <td className="p-2">{d.codigo}</td><td className="p-2">{d.statusDocumento}</td>
    <td className="p-2">{value(d.hhPrevisto,d.custoPrevisto)}</td><td className="p-2">{value(d.hhRealizado,d.custoRealizado)}</td>
   </tr>)}</tbody></table></div>
  </section>}
 </div>;
}
