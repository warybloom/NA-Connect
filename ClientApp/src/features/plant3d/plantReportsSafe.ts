// Núcleo puro (sem imports) da exportação segura de relatórios — testável em node. Espelha PlantReportFormat (C#).

const INICIO_FORMULA = /^[ ]*[=+\-@\t\r\n|%]/;

/** Neutraliza injeção de fórmula (CSV/Excel): texto que começaria uma fórmula ganha apóstrofo inicial (vira texto literal). */
export const neutralizarFormula = (v: unknown): string => {
  const s = v === null || v === undefined ? "" : String(v);
  return INICIO_FORMULA.test(s) ? "'" + s : s;
};

/** Nome sugerido: letras/dígitos/-/_ apenas. */
export const nomeSeguro = (v: string): string => v.replace(/[^\p{L}\p{N}_-]+/gu, "_").replace(/^_+|_+$/g, "");

/** Quantas linhas o XLSX do navegador leria: nunca mais que o teto, nunca mais que o total informado pelo servidor. */
export const linhasAExportar = (total: number, teto: number): number => Math.max(0, Math.min(Number.isFinite(total) ? total : 0, teto));
