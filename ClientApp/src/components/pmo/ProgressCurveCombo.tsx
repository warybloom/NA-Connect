import { useMemo } from "react";
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

type Period = Record<string, string | number | null>;
type Props = {
  rows: Period[];
  plannedKey: string;
  actualKey: string;
  plannedLabel: string;
  actualLabel: string;
  unit: (value: number) => string;
};

/**
 * Power BI CHS001 > "LDP - 3D - 30%" > "Progress Curve":
 * barras agrupadas: baseline do PERÍODO + realizado do PERÍODO
 * linhas: baseline ACUMULADO + realizado ACUMULADO; legenda superior.
 *
 * O Power BI tem ainda uma terceira linha Reprogramado. A Curva S documental
 * N&A não possui uma série temporal de reprogramação comprovada: não fabricá-la.
 * Percentuais usam o mesmo denominador (baseline final); fonte e valores
 * absolutos continuam inalterados no backend e na reconciliação da tela.
 */
export default function ProgressCurveCombo({
  rows, plannedKey, actualKey, plannedLabel, actualLabel, unit,
}: Props) {
  const values = useMemo(() => {
    const denominator = Math.max(0, ...rows.map(row => Number(row[plannedKey] ?? 0)));
    let previousPlanned = 0;
    let previousActual = 0;
    return rows.map(row => {
      const planned = Math.max(0, Number(row[plannedKey] ?? 0));
      const actual = row[actualKey] === null || row[actualKey] === undefined
        ? null : Math.max(0, Number(row[actualKey]));
      const intervalPlanned = Math.max(0, planned - previousPlanned);
      const intervalActual = actual === null ? null : Math.max(0, actual - previousActual);
      previousPlanned = planned;
      if (actual !== null) previousActual = actual;
      const pct = (value: number) => denominator > 0 ? (100 * value / denominator) : 0;
      return {
        DataFormatada: row.DataFormatada,
        baselinePeriodo: pct(intervalPlanned),
        realizadoPeriodo: intervalActual === null ? null : pct(intervalActual),
        baselineAcumulado: pct(planned),
        realizadoAcumulado: actual === null ? null : pct(actual),
        previstoAbsoluto: planned,
        realizadoAbsoluto: actual,
      };
    });
  }, [rows, plannedKey, actualKey]);

  // Como no PBIX: colunas no eixo primário (oculto, escala própria) e linhas no secundário (à direita, com rótulos).
  // A escala percentual é deliberadamente fixa para manter a leitura comparável entre projetos, filtros e TVs.
  const maxColuna = Math.max(0, ...values.flatMap(d => [d.baselinePeriodo, d.realizadoPeriodo ?? 0]));
  const maxAxisColuna = maxColuna > 0 ? maxColuna * 1.15 : 1;

  return <div className="h-[360px] w-full">
    <ResponsiveContainer width="100%" height="100%">
      <ComposedChart data={values} margin={{ top: 12, right: 8, left: 8, bottom: 4 }}
        barGap={2} barCategoryGap="30%">
        <CartesianGrid vertical={false} stroke="hsl(var(--chart-grid))" opacity={0.6}/>
        <XAxis dataKey="DataFormatada" tick={{ fill:"hsl(var(--chart-axis))",fontSize:10 }}
          minTickGap={24} axisLine={{ stroke:"hsl(var(--chart-grid))" }} tickLine={false}/>
        <YAxis yAxisId="periodo" hide domain={[0,maxAxisColuna]}/>
        <YAxis yAxisId={0} orientation="right" domain={[0,100]} ticks={[0,20,40,60,80,100]}
          allowDataOverflow width={44} tickFormatter={(x:number)=>`${x.toFixed(0)}%`}
          tick={{ fill:"hsl(var(--chart-axis))",fontSize:10 }} axisLine={false} tickLine={false}/>
        <Tooltip contentStyle={{ backgroundColor:"hsl(var(--popover))",border:"1px solid hsl(var(--border))",
          borderRadius:8,color:"hsl(var(--popover-foreground))" }}
          labelFormatter={(label)=>`Semana/período: ${label}`}
          formatter={(v:number,name:string,payload:any)=>{
            if(v===null||v===undefined)return ["—",name];
            const abs = name===plannedLabel ? payload?.payload?.previstoAbsoluto :
              name===actualLabel ? payload?.payload?.realizadoAbsoluto : undefined;
            return [abs==null ? `${v.toFixed(1)}%` : `${v.toFixed(1)}% (${unit(abs)})`,name];
          }}/>
        <Legend verticalAlign="top" height={38} iconSize={10}
          wrapperStyle={{fontSize:11,color:"hsl(var(--foreground))"}}/>
        <Bar yAxisId="periodo" dataKey="baselinePeriodo" name="% Baseline · período"
          fill="#002E5E" maxBarSize={26} isAnimationActive={false}/>
        <Bar yAxisId="periodo" dataKey="realizadoPeriodo" name="% Realizado · período"
          fill="#016531" maxBarSize={26} isAnimationActive={false}/>
        <Line yAxisId={0} dataKey="baselineAcumulado" name={plannedLabel}
          type="linear" stroke="#E31A1C" strokeWidth={3} strokeDasharray="7 5"
          dot={{r:3,fill:"#E31A1C",stroke:"#E31A1C",strokeWidth:1}} activeDot={{r:5}}
          isAnimationActive={false}/>
        <Line yAxisId={0} dataKey="realizadoAcumulado" name={actualLabel}
          type="linear" stroke="#047C62" strokeWidth={3}
          dot={{r:3,fill:"#047C62",stroke:"#047C62",strokeWidth:1}} activeDot={{r:5}}
          connectNulls={false} isAnimationActive={false}/>
      </ComposedChart>
    </ResponsiveContainer>
  </div>;
}
