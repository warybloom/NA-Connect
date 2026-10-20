// Formatação de valores no estilo Power BI (pt-BR para números e datas, como no relatório original).
import type { FormatoValor, Valor } from "../dados/tipos";

const INT = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 0 });

function unidades(n: number, displayUnits: number, casas: number): string {
  // labelDisplayUnits do Power BI: 0 = automático, 1 = nenhum, 1000 = mil, 1e6 = milhões...
  const fmt = (x: number, suf: string) => `${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: casas, maximumFractionDigits: casas }).format(x)}${suf}`;
  const auto = displayUnits === 0;
  if ((auto && Math.abs(n) >= 1e9) || displayUnits === 1e9) return fmt(n / 1e9, " bi");
  if ((auto && Math.abs(n) >= 1e6) || displayUnits === 1e6) return fmt(n / 1e6, " Mi");
  if ((auto && Math.abs(n) >= 1e4) || displayUnits === 1e3) return fmt(n / 1e3, " Mil");
  return fmt(n, "");
}

export interface OpcoesFormato { casas?: number; displayUnits?: number }

export function formatarValor(v: Valor | undefined, formato: FormatoValor = "inteiro", op: OpcoesFormato = {}): string {
  if (v === null || v === undefined || v === "") return "";
  if (typeof v === "string") {
    if (formato === "data") return formatarData(v);
    return v;
  }
  switch (formato) {
    case "percentual": return `${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: op.casas ?? 2, maximumFractionDigits: op.casas ?? 2 }).format(v * 100)}%`;
    case "moeda": return `R$ ${unidades(v, op.displayUnits ?? 1, op.casas ?? 2)}`;
    case "horas":
    case "decimal": return unidades(v, op.displayUnits ?? 1, op.casas ?? 2);
    case "inteiro": return op.displayUnits && op.displayUnits !== 1 ? unidades(v, op.displayUnits, op.casas ?? 0) : INT.format(v);
    default: return String(v);
  }
}

/** Datas ISO (yyyy-MM-dd[THH:mm:ss]) → dd/MM/yyyy sem conversão de fuso. Outros textos ficam como vieram da fonte. */
export function formatarData(s: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : s;
}
