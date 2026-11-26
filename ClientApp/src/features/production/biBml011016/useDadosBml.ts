/** BI BML011.016 — snapshot fiel do DataModel EMBUTIDO no PBIX original.
 * Dados lidos de rota master-only. Nenhum valor e atribuido como SQL "atual".
 * Algumas medidas DAX especificas foram auditadas em daxSnapshot.ts.
 */
import { useEffect, useMemo, useState } from "react";
import type { DadosVisual, FormatoValor, Valor } from "@/features/production/biChs001/dados/tipos";
import type { CampoPbi, VisualPbi } from "@/features/production/biChs001/pbi/modelo";
import { AcessoBmlNegado } from "./api";
import { chave, diasSemana, filtroWBX, kpi, kpiPercentual, lerCol, medidasContexto,
  semanaAtual, type Linha, type Selecoes, type TabelasSnapshot } from "./daxSnapshot";

const REAIS = new Set(["ACTION ITEM LIST","Assignment","AssignmentByDay","DESVIO","HOLDS","LDP (4)","WBX","dCalendario","dEAP","dSemana"]);
const TODOS = ["WBX","Assignment","AssignmentByDay","dSemana","dCalendario","dEAP","LDP (4)","ACTION ITEM LIST","DESVIO","HOLDS"];
const campos = (v: VisualPbi) => Object.values(v.fields ?? {}).flat();
const ref = (c:CampoPbi) => c.queryRef ?? c.nativeQueryRef ?? c.prop ?? "campo";
const valor=(l:Linha,c:CampoPbi):Valor=>{const x=c.prop?lerCol(l,c.prop):null;
  return x===null||x===undefined?null:typeof x==="number"||typeof x==="string"?x:String(x);};
const int=(v:Valor)=>typeof v==="number"?v:typeof v==="string"&&v.trim()!==""?Number(v):NaN;
const agregacao=(rows:Linha[],c:CampoPbi):Valor=>{
  const xs=rows.map(l=>valor(l,c)).filter((v):v is string|number=>v!==null);
  if(c.kind==="column")return xs[0]??null;
  if(!xs.length)return null;
  if(c.fn===5)return xs.length;
  if(c.fn===2)return new Set(xs.map(String)).size;
  if(c.fn===3)return [...xs].sort((a,b)=>String(a).localeCompare(String(b)))[0]??null;
  if(c.fn===4)return [...xs].sort((a,b)=>String(b).localeCompare(String(a)))[0]??null;
  const ns=xs.map(int);
  if(ns.some(n=>!Number.isFinite(n)))return null;
  if(c.fn===0)return ns.reduce((a,b)=>a+b,0);
  if(c.fn===1)return ns.reduce((a,b)=>a+b,0)/ns.length;
  if(c.fn===6){ns.sort((a,b)=>a-b);const m=Math.floor(ns.length/2);return ns.length%2?ns[m]:(ns[m-1]+ns[m])/2;}
  return null;
};
const fonteDe=(f:CampoPbi)=>f.entity==="KPI"?"KPI":f.entity??"";
function formatosMedidas(fields:CampoPbi[]) {
  return Object.fromEntries(fields.filter(c=>c.kind==="measure"&&kpiPercentual(c.prop??""))
    .map(c=>[ref(c),"percentual" as FormatoValor]));
}
export function converterSnapshot(v:VisualPbi,base:TabelasSnapshot,s:Selecoes,alterar:(key:string,v?:string)=>void):DadosVisual {
  const cs=campos(v);
  if(!cs.length)return {estado:"pendente",motivo:"Visual sem fonte de dados."};
  if(v.type==="slicer"){
    const f=cs[0];const e=f.entity??"";
    if(!REAIS.has(e)||!base[e])return {estado:"pendente",motivo:"Tabela da segmentacao nao existe no DataModel original."};
    const key=`${e}:${f.prop}`;
    const values=[...new Set(base[e].map(l=>valor(l,f)).filter((x):x is string|number=>x!==null))]
      .map(x=>({chave:String(x),rotulo:String(x)}));
    const selected=s[key]??(key==="dSemana:Data" ? [`${semanaAtual(base,s)}T00:00:00.000`] : []);
    return {estado:"ok",tipo:"opcoes",opcoes:values,selecionadas:selected,
      alternar:(value)=>alterar(key,value),limpar:()=>alterar(key)};
  }
  const unknown=cs.filter(c=>c.entity!=="KPI"&&!REAIS.has(c.entity??""));
  if(unknown.length)return {estado:"pendente",motivo:`A entidade ${unknown[0].entity??"?"} nao existe no modelo embutido do PBIX.`};
  const ctx=medidasContexto(base,s);
  const daFonte=(f:CampoPbi):Linha[]=>{
    const e=f.entity??"";
    if(e==="WBX")return ctx.tasks;
    return base[e]??[];
  };
  const med=(f:CampoPbi,semana?:string,grupo?:Linha[]):Valor=>{
    if(f.kind==="measure"&&f.entity==="LDP (4)"){
      // Contagens DAX originais do PBIX: CALCULATE(COUNTROWS('LDP (4)'), [STATUS] = literal).
      const linhas=base["LDP (4)"]??[];
      const chaveMed=chave(f.prop??"");
      if(chaveMed==="emitido")return linhas.filter(l=>String(lerCol(l,"SITUATION")??"").trim().toUpperCase()==="EMITIDO").length;
      if(chaveMed==="cancelado")return linhas.filter(l=>String(lerCol(l,"DOCUMENT STATUS")??"").trim().toUpperCase()==="CANCELADO").length;
      if(chaveMed.includes("revis")&&chaveMed.includes("bml"))
        return linhas.filter(l=>{const x=chave(String(lerCol(l,"DOCUMENT STATUS")??""));return x.startsWith("eman")&&x.endsWith("lisedocliente");}).length;
      if(chaveMed.includes("revis")&&chaveMed.includes("na"))
        return linhas.filter(l=>String(lerCol(l,"DOCUMENT STATUS")??"").trim().toUpperCase()==="IN REVIEW N&A").length;
      if(chaveMed==="iniciar")
        return linhas.filter(l=>String(lerCol(l,"DOCUMENT STATUS")??"").trim().toUpperCase()==="NOT STARTED").length;
      return null;
    }
    if(f.kind==="measure")return kpi(f.prop??"",base,s,semana,grupo);
    return agregacao(grupo??daFonte(f),f);
  };
  const formatos=formatosMedidas(cs);
  if(["card","cardVisual","multiRowCard"].includes(v.type)) {
    const valores:Record<string,Valor>={};const pendentes:Record<string,string>={};
    for(const c of cs){const value=med(c);valores[ref(c)]=value;
      if(value===null)pendentes[ref(c)]=c.kind==="measure"?
        `Medida DAX "${c.prop}" nao implementada nesta etapa.`:"Campo sem valor no PBIX.";}
    return {estado:"ok",tipo:"valores",valores,formatos,pendentes};
  }
  if(v.type==="pivotTable"||v.type==="tableEx"){
    const ent=cs.find(c=>c.entity!=="KPI")?.entity??"";
    const rows=ent==="WBX"?ctx.tasks:base[ent]??[];
    if(!rows.length)return {estado:"pendente",motivo:"Tabela original vazia."};
    const linhas=rows.map(l=>{
      const map:Record<string,Valor>={};
      for(const c of cs){
        if(c.kind==="measure")map[ref(c)]=kpi(c.prop??"",base,s,undefined,[l]);
        else map[ref(c)]=valor(l,c);
      }return map;
    });
    return {estado:"ok",tipo:"linhas",linhas,formatos};
  }
  if(["lineClusteredColumnComboChart","lineStackedColumnComboChart","clusteredBarChart","clusteredColumnChart","donutChart","pieChart"].includes(v.type)){
    const cat=v.fields?.Category?.[0]??v.fields?.Axis?.[0]??cs[0];
    const ys=[...(v.fields?.Y??[]),...(v.fields?.Y2??[]),...(v.fields?.Values??[])].filter(c=>c!==cat);
    if(!ys.length)return {estado:"pendente",motivo:"Medida do grafico nao identificada."};
    const weekly=cat.entity==="dCalendario"&&chave(cat.prop??"").includes("semana");
    if(weekly){
      const semanas=diasSemana(base);
      // O dSemana do PBIX começa em 17/05/2026, mas o cronograma
      // BML011.016 possui entregáveis com baseline em 14/05/2026
      // (Equipment Tags/Preliminary Drawing/Load Information).
      // Apenas o marco de início é acrescentado; baseline e semanas
      // originais NÃO são deslocadas nem recalculadas.
      const ehCurvaS=v.type==="lineClusteredColumnComboChart"&&
        ys.some(c=>chave(c.prop??"")==="previstoreprogcurva");
      const marcoInicioBml="2026-05-14"; // verificado dbo.Cronograma_Projetos
      const incluirMarco=ehCurvaS&&semanas.length>0&&marcoInicioBml<semanas[0];
      const datas=incluirMarco?[marcoInicioBml,...semanas]:semanas;
      const pts=datas.map(data=>{
        const valores=Object.fromEntries(ys.map(c=>{
          let x:Valor;
          if(incluirMarco&&data===marcoInicioBml){
            const prop=chave(c.prop??"");
            if(prop==="previstoreprogcurva")x=ctx.curva(data,false);
            else if(prop==="previstolbcurva")x=ctx.curva(data,true);
            else if(prop==="accrealizadocurva")x=ctx.totalTrend>0?ctx.sumAte(data,"Trabalho Real")/ctx.totalTrend:null;
            else x=null; // Não antecipar a barra semanal de 17/05 para 14/05.
          }else x=med(c,data);
          return [ref(c),typeof x==="number"?x:null];
        }));
        return {categoria:data,valores};
      });
      return {estado:"ok",tipo:"pontos",pontos:pts,formatos};
    }
    const e=cat.entity??"";const rows=e==="WBX"?ctx.tasks:base[e]??[];
    if(!rows.length)return {estado:"pendente",motivo:"Nao ha dados originais para essa entidade."};
    const groups=new Map<string,Linha[]>();
    for(const row of rows){const key=String(valor(row,cat)??"—");groups.set(key,[...(groups.get(key)??[]),row]);}
    const points=[...groups.entries()].map(([categoria,g])=>({
      categoria,
      valores:Object.fromEntries(ys.map(c=>{
        const value=med(c,undefined,g);
        return [ref(c),typeof value==="number"?value:null];
      }))
    }));
    if(points.every(p=>Object.values(p.valores).every(x=>x===null)))
      return {estado:"pendente",motivo:"Medidas desse grafico sem equivalencia no DataModel."};
    return {estado:"ok",tipo:"pontos",pontos:points,formatos};
  }
  return {estado:"pendente",motivo:"Tipo de visual ainda nao reproduzido."};
}
export function useDadosBml(visuais:readonly VisualPbi[],autorizado:boolean) {
  const entidades=useMemo(()=>{
    const es=new Set(visuais.flatMap(v=>campos(v).map(c=>c.entity??"")));
    if(es.has("KPI")||es.has("AssignmentByDay")||es.has("dCalendario")||es.has("WBX")){
      ["WBX","AssignmentByDay","dSemana","dCalendario"].forEach(e=>es.add(e));
    }
    return TODOS.filter(e=>es.has(e));
  },[visuais]);
  const key=entidades.join("|");
  const [tables,setTables]=useState<TabelasSnapshot>({});
  const [loading,setLoading]=useState(true);
  const [erro,setErro]=useState<string>();
  const [negado,setNegado]=useState(false);
  const [selecionados,setSelecionados]=useState<Selecoes>({});
  useEffect(()=>{
    const ctrl=new AbortController();setTables({});setErro(undefined);setNegado(false);setSelecionados({});
    if(!autorizado||!key){setLoading(!!autorizado);return ()=>ctrl.abort();}
    setLoading(true);
    Promise.all(key.split("|").map(async e=>{
      const r=await fetch(`/api/bi-bml011016/pbix/snapshot/${encodeURIComponent(e)}`,
        {credentials:"include",cache:"no-store",signal:ctrl.signal});
      if(r.status===401||r.status===403)throw new AcessoBmlNegado();
      if(!r.ok)throw new Error(`Snapshot ${e} indisponivel (HTTP ${r.status})`);
      const v=await r.json() as Linha[];
      return [e,v] as const;
    })).then(res=>{if(!ctrl.signal.aborted)setTables(Object.fromEntries(res));})
      .catch((e:unknown)=>{if(ctrl.signal.aborted)return;if(e instanceof AcessoBmlNegado)setNegado(true);
        else setErro(e instanceof Error?e.message:"Erro ao carregar snapshot PBIX.");})
      .finally(()=>{if(!ctrl.signal.aborted)setLoading(false);});
    return ()=>ctrl.abort();
  },[key,autorizado]);
  const alterar=(key:string,v?:string)=>setSelecionados(prev=>{
    const current=prev[key]??[];return {...prev,[key]:v===undefined?[]:current.includes(v)?current.filter(x=>x!==v):[v]};
  });
  return {negado,erro,origem:"PBIX original · snapshot, não sincronização SQL",
    dadosDe:(v:VisualPbi):DadosVisual=>!autorizado?{estado:"pendente",motivo:"Sem autorizacao."}:
      loading?{estado:"carregando"}:erro?{estado:"erro",mensagem:erro}:converterSnapshot(v,tables,selecionados,alterar)};
}
