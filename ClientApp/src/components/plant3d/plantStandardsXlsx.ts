// Leitura de planilha (XLSX/XLS/CSV) NO NAVEGADOR com a dependência `xlsx` já instalada. Nenhum binário vai ao servidor:
// só a lista de valores escolhida segue no PUT /standards. Carregada sob demanda (não pesa o bundle principal).
import { LIMITES_PADROES, limparValor } from "./plantStandardsLogic";

export type AbaPlanilha = { nome: string; linhas: string[][]; truncada: boolean };
export type PlanilhaLida = { arquivo: string; abas: AbaPlanilha[] };

const EXTENSOES = /\.(xlsx|xls|csv)$/i;
const MAX_ABAS = 20;
const MAX_COLUNAS = 60;

/** Valida e lê o arquivo. Fórmulas/HTML/estilos não são processados; só o texto exibido das células. */
export async function lerPlanilha(arquivo: File): Promise<PlanilhaLida> {
  if (!EXTENSOES.test(arquivo.name)) throw new Error("Formato não suportado: use .xlsx, .xls ou .csv.");
  if (arquivo.size === 0) throw new Error("O arquivo está vazio.");
  if (arquivo.size > LIMITES_PADROES.arquivoBytes) {
    throw new Error(`Arquivo acima de ${Math.round(LIMITES_PADROES.arquivoBytes / 1024 / 1024)} MB.`);
  }
  const XLSX = await import("xlsx");
  let wb;
  try {
    wb = XLSX.read(await arquivo.arrayBuffer(), {
      type: "array", cellFormula: false, cellHTML: false, cellStyles: false, cellNF: false, bookVBA: false,
      sheetRows: LIMITES_PADROES.linhasPlanilha + 1,
    });
  } catch (e) {
    throw new Error("Não foi possível ler a planilha: " + (e instanceof Error ? e.message : String(e)));
  }
  const abas: AbaPlanilha[] = [];
  for (const nome of wb.SheetNames.slice(0, MAX_ABAS)) {
    const ws = wb.Sheets[nome];
    if (!ws) continue;
    const bruto = XLSX.utils.sheet_to_json<unknown[]>(ws, { header: 1, raw: false, blankrows: false, defval: "" });
    const linhas = bruto.slice(0, LIMITES_PADROES.linhasPlanilha).map(l => (Array.isArray(l) ? l : []).slice(0, MAX_COLUNAS).map(c => limparValor(c)));
    if (linhas.length > 0) abas.push({ nome, linhas, truncada: bruto.length > LIMITES_PADROES.linhasPlanilha });
  }
  if (abas.length === 0) throw new Error("A planilha não tem células preenchidas.");
  return { arquivo: arquivo.name, abas };
}

export const larguraAba = (aba: AbaPlanilha) => aba.linhas.reduce((n, l) => Math.max(n, l.length), 0);

/** Cabeçalho (1ª linha) quando marcado; senão "Coluna A, B…". */
export function nomesColunas(aba: AbaPlanilha, comCabecalho: boolean): string[] {
  const n = larguraAba(aba);
  return Array.from({ length: n }, (_, i) => {
    const cab = comCabecalho ? aba.linhas[0]?.[i] : "";
    return cab ? `${cab}` : `Coluna ${rotuloExcel(i)}`;
  });
}

const rotuloExcel = (i: number): string => {
  let s = "", n = i + 1;
  while (n > 0) { const r = (n - 1) % 26; s = String.fromCharCode(65 + r) + s; n = Math.floor((n - 1) / 26); }
  return s;
};

/** Valores brutos de uma coluna (sem deduplicar: quem chama usa `unirValores` e informa os descartes). */
export function valoresDaColuna(aba: AbaPlanilha, coluna: number, comCabecalho: boolean): string[] {
  return aba.linhas.slice(comCabecalho ? 1 : 0).map(l => l[coluna] ?? "");
}
