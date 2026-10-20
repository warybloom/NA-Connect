import {formatCurrency,formatHH} from "@/components/pmo/pmoData";
import type {PhaseField,ResumoDiscipline,ResumoPhaseMetric} from "./resumoPhaseData";
import "./resumoDisciplineCard.css";
export const resumoNumber=(v:number|null|undefined)=>typeof v==="number"&&Number.isFinite(v)?formatHH(v):"—";
export const resumoPercent=(v:number|null|undefined)=>typeof v==="number"&&Number.isFinite(v)?formatHH(v)+"%":"—";
export function ResumoPhaseStrip({fases,field,format=resumoNumber}:{fases:ResumoPhaseMetric[];field:PhaseField;format?:(v:number|null)=>string}){
  if(!fases.length)return null;
  return <div className="resumo-phase-strip">{fases.map(f=><span key={f.gate}><small>{f.gate}%</small><b>{format(f[field])}</b></span>)}</div>;
}
export default function ResumoDisciplineCard({discipline:d,tv=false,money=true}:{discipline:ResumoDiscipline;tv?:boolean;money?:boolean}){
  const docs=d.documentos;
  const rows:{title:string;value:number|null;field:PhaseField;format?:(v:number|null)=>string}[]=[
    {title:"HH previsto",value:d.HH_Previsto,field:"hhPrevisto"},
    {title:"HH realizado",value:d.HH_Realizado,field:"hhRealizado"},
    {title:"Saldo HH",value:d.SaldoHH,field:"saldoHh"},
    {title:"Consumo HH",value:d.ConsumoHH,field:"consumoHh",format:resumoPercent},
    {title:"Total documentos",value:docs.total,field:"total"},
    {title:"Emitidos",value:docs.emitidos,field:"emitidos"},
    {title:"Cancelados",value:docs.cancelados,field:"cancelados"},
    {title:"A emitir",value:docs.aEmitir,field:"aEmitir"},
    {title:"Atrasados",value:docs.atrasados,field:"atrasados"},
    {title:"No prazo",value:docs.noPrazo,field:"noPrazo"},
  ];
  const showMoney=money&&!tv;
  if(money||tv)rows.push(
    {title:"Custo previsto",value:showMoney?d.Custo_Previsto:d.Custo_Previsto!=null&&d.Custo_Previsto>0?100:null,field:"custoPrevisto",format:showMoney?formatCurrency:resumoPercent},
    {title:"Custo realizado",value:showMoney?d.Custo_Realizado:d.ConsumoCusto,field:"custoRealizado",format:showMoney?formatCurrency:resumoPercent},
    {title:"Consumo custo",value:d.ConsumoCusto,field:"consumoCusto",format:resumoPercent});
  return <article className={"resumo-discipline-card"+(tv?" resumo-discipline-tv":"")} data-testid="resumo-disciplina">
    <header><h3 title={d.Disciplina}>{d.Disciplina}</h3><small title={d.nomeProjeto}>{d.nomeProjeto}</small></header>
    <table><thead><tr><th>Indicador</th><th>Total</th>{d.fases.map(f=><th key={f.gate}>{f.gate}%</th>)}</tr></thead>
      <tbody>{rows.map(row=>{const fmt=row.format??resumoNumber;return <tr key={row.field} className={(row.value??0)<0?"resumo-negative":""}>
        <th scope="row">{row.title}</th><td>{fmt(row.value)}</td>{d.fases.map(f=>{
          const value=tv&&row.field==="custoPrevisto" ? f.custoPrevisto!=null&&f.custoPrevisto>0?100:null :
            tv&&row.field==="custoRealizado" ? f.consumoCusto : f[row.field];
          return <td key={f.gate}>{fmt(value)}</td>;
        })}
      </tr>})}</tbody></table>
    {!tv&&<footer>{docs.semPrazo>0&&<span>{docs.semPrazo} documentos sem prazo.</span>}
      {!d.oficial&&<span title={d.diagnostico??undefined}>{d.diagnostico??"Nome oficial pendente"}</span>}
      {d.pendencias.length>0&&<details><summary>Conferir DOC3/Norma ({d.pendencias.length})</summary>{d.pendencias.map((p,i)=><p key={i}>{p}</p>)}</details>}
    </footer>}
  </article>;
}
