import { lerNumero, lerValor, type ItemVigenciaInput, type PeriodoProfissional, type VigenciaProfissionalInput } from "./costsApi";

export function somenteValorHora(period: PeriodoProfissional): boolean {
  return period.manualRate !== null && period.remuneration === 0 && !period.items.some((item) => item.active);
}

type TipoCalculo = ItemVigenciaInput["calculationType"];

type Item = {
  category: string;
  description: string;
  calculationType: TipoCalculo;
  amount: string;
  percentage: string;
};

export type FormState = {
  mode: "new" | "edit";
  costMode: "COMPOSICAO" | "HORA";
  periodId?: number;
  start: string;
  end: string;
  baseHours: string;
  remuneration: string;
  manualRate: string;
  items: Item[];
  copyPrevious: boolean;
  copiedIds: number[];
};

const textNumber = (value: number | null | undefined) => (value == null ? "" : String(value).replace(".", ","));

/** Data local (yyyy-MM-dd); toISOString() usaria UTC e viraria o dia à noite no Brasil. */
function hojeLocal() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function periodToForm(period: PeriodoProfissional): FormState {
  return {
    mode: "edit",
    costMode: somenteValorHora(period) ? "HORA" : "COMPOSICAO",
    periodId: period.id,
    start: period.start.slice(0, 10),
    end: period.end?.slice(0, 10) ?? "",
    baseHours: textNumber(period.monthlyHours),
    remuneration: textNumber(period.remuneration),
    manualRate: textNumber(period.manualRate),
    items: period.items.filter((i) => i.active).map((i) => ({
      category: i.category,
      description: i.description,
      calculationType: i.calculationType === "PERCENTUAL" ? "PERCENTUAL" : "FIXO",
      amount: textNumber(i.amount),
      percentage: textNumber(i.percentage),
    })),
    copyPrevious: false,
    copiedIds: [],
  };
}

export function newForm(hours: number, current: PeriodoProfissional | null): FormState {
  return {
    mode: "new",
    costMode: current && somenteValorHora(current) ? "HORA" : "COMPOSICAO",
    start: hojeLocal(),
    end: "",
    baseHours: textNumber(current?.monthlyHours ?? hours),
    remuneration: current ? textNumber(current.remuneration) : "0",
    manualRate: current && somenteValorHora(current) ? textNumber(current.manualRate) : "",
    items: [],
    copyPrevious: !current || !somenteValorHora(current),
    copiedIds: current?.items.filter((i) => i.active).map((i) => i.id) ?? [],
  };
}

/** Converte o formulário em payload numérico; retorna mensagem de erro quando algo não é número válido. */
export function montarPayload(form: FormState): { ok: true; value: Omit<VigenciaProfissionalInput, "confirmClosePrevious"> } | { ok: false; erro: string } {
  const hourlyOnly = form.costMode === "HORA";
  // A API mantém HH positiva; no modo por hora ela é apenas a referência do cadastro.
  const baseHours = lerNumero(form.baseHours);
  const monthlyHours = hourlyOnly ? (baseHours !== null && baseHours > 0 ? baseHours : 160) : baseHours;
  const remuneration = hourlyOnly ? 0 : lerValor(form.remuneration);
  const manualRateText = form.manualRate.trim();
  const manualRate = manualRateText ? lerValor(manualRateText) : null;
  const end = form.end.trim() || null;

  if (!form.start) return { ok: false, erro: "Informe a data de início da vigência." };
  if (end && end < form.start) return { ok: false, erro: "A data final deve ser igual ou posterior à inicial." };
  if (monthlyHours === null || monthlyHours <= 0) return { ok: false, erro: "HH base mensal deve ser um número maior que zero." };
  if (remuneration === null || remuneration < 0) return { ok: false, erro: "Remuneração deve ser um número maior ou igual a zero." };
  if (hourlyOnly && !manualRateText) return { ok: false, erro: "Informe o valor/hora." };
  if (manualRateText && (manualRate === null || manualRate < 0)) return { ok: false, erro: "Custo/hora manual deve ser um número maior ou igual a zero." };

  const items: ItemVigenciaInput[] = [];
  for (const [i, x] of (hourlyOnly ? [] : form.items).entries()) {
    const fixo = x.calculationType === "FIXO";
    const amount = fixo ? lerValor(x.amount) : 0;
    const percentage = fixo ? null : lerNumero(x.percentage);
    if (!x.category.trim() || !x.description.trim()) return { ok: false, erro: `Item ${i + 1}: informe categoria e descrição.` };
    if (amount === null || amount < 0) return { ok: false, erro: `Item ${i + 1}: valor mensal inválido.` };
    if (!fixo && (percentage === null || percentage < 0)) return { ok: false, erro: `Item ${i + 1}: percentual inválido.` };
    items.push({
      category: x.category.trim(),
      description: x.description.trim(),
      calculationType: x.calculationType,
      amount,
      percentage,
      calculationBase: "REMUNERACAO",
    });
  }

  return { ok: true, value: { start: form.start, end, monthlyHours, remuneration, manualRate, items,
    copyPreviousComposition: !hourlyOnly && form.mode === "new" && form.copyPrevious,
    excludePreviousItemIds: [],
  } };
}

