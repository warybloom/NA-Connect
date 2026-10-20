import type { FactsReport } from "./pmoFacts";
import { formatCurrency, formatHH } from "./pmoData";

export default function HistoricalMeasurementPanel({ reports }: { reports: readonly FactsReport[] }) {
  const measured=reports.filter(r=>(r.actual?.historicalMeasurement?.disciplines.length??0)>0 || (r.actual?.historicalMeasurement?.diagnostics.length??0)>0);
  if(!measured.length)return null;
  return <section className="rounded-xl border border-sky-500/30 bg-card p-4 space-y-3">
    <h3 className="font-semibold">Conciliação de medição histórica</h3>
    <p className="text-xs text-muted-foreground">Realizado gerencial = Timesheet original + complemento confirmado. Novos lançamentos reduzem automaticamente o complemento; avanço físico e previsto permanecem nas suas fontes.</p>
    {measured.map(r=>{
      const h=r.actual!.historicalMeasurement!, raw=r.actual!.timesheetOriginal;
      return <div key={r.projectId} className="space-y-2">
        <p className="text-sm font-medium">{r.nomeProjeto}</p>
        <div className="grid gap-2 sm:grid-cols-3 text-sm">
          <div>Timesheet original: <strong>{formatHH(raw?.hh??null)} HH</strong><br/>{formatCurrency(raw?.knownCalculatedCostSubtotal??null)}</div>
          <div>Complemento histórico: <strong>{formatHH(h.complementHours)} HH</strong><br/>{formatCurrency(h.complementCost)}</div>
          <div>Realizado gerencial: <strong>{formatHH(r.actual!.total.hh)} HH</strong><br/>{formatCurrency(r.actual!.total.knownCalculatedCostSubtotal)}</div>
        </div>
        <div className="overflow-x-auto"><table className="w-full text-xs">
          <thead><tr className="text-left"><th>Pacote / disciplina</th><th>Medido HH</th><th>Timesheet HH</th><th>Complemento HH</th><th>Complemento custo</th><th>Gerencial custo</th><th>Excedente HH / custo</th></tr></thead>
          <tbody>{h.disciplines.map(d=><tr key={d.confirmationId+":"+d.discipline} className="border-t border-border">
            <td className="py-2">{d.package} / {d.discipline}</td><td>{formatHH(d.confirmedHours)}</td><td>{formatHH(d.timesheetHours)}</td>
            <td>{formatHH(d.balance.complementHours)}</td><td>{formatCurrency(d.balance.complementCost)}</td>
            <td>{formatCurrency(d.balance.recognizedCost+d.balance.complementCost)}</td>
            <td>{formatHH(d.balance.excessHours)} / {formatCurrency(d.balance.excessCost)}</td>
          </tr>)}</tbody>
        </table></div>
        {h.diagnostics.length>0&&<ul className="text-xs text-amber-500">{h.diagnostics.map((s,i)=><li key={i}>{s}</li>)}</ul>}
        <details className="text-xs text-muted-foreground"><summary>Origem e confirmação</summary>
          {h.disciplines.map(d=><p key={d.confirmationId+":"+d.discipline} className="mt-1">{d.package} / {d.discipline}: {d.professional}; confirmação #{d.confirmationId}, {d.confirmedAtUtc}, {d.confirmedBy}. {d.origin}</p>)}
        </details>
      </div>;
    })}
  </section>;
}
