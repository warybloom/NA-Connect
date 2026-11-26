// Avisos de FONTE (exibidos fora do canvas, nunca alteram número): quando o dado atual tem valor que os filtros/medidas do PBIX
// não conhecem, o resultado literal do DAX muda de sentido. O filtro original é mantido; o aviso explica o efeito.
// Caso real (15/10/2026): a EDT 1.5 do .mpp CHS001 chama-se "Emgineering - 30%"; o PBIX filtra "Engineering - 30%".
// No cronograma ISOLADO do BI (bi_chs001) a correspondência explícita bi_chs001.Alias_Rotulo (só CHS001, só tarefa de resumo)
// entrega "Engineering - 30%" a filtros e medidas, preservando o valor bruto ("Level 4 Original") — informado em infoFonte.
// Na fonte legada (sem alias) o valor bruto continua literal e o aviso abaixo aparece.
import type { PaginaPbi } from "../pbi/modelo";
import { compilarFiltros } from "./filtrosPbi";
import type { FonteCarregada } from "./motor";

/** Valores de WBX[Level 4] citados nos filtros de página dos Resumos e nas medidas KPI do PBIX (PBIR + DataModel). */
export const NIVEL4_PBIX = new Set(["Engineering - 15%", "Engineering - 30%", "Engineering - 60%", "Engineering - 90%", "Field Activies",
  "Milestone", "WFX Deliverables", "CHS Deliverables"]);

export function avisosDeFonte(pagina: PaginaPbi, fontes: Record<string, FonteCarregada>): string[] {
  const wbx = fontes.WBX;
  if (wbx?.estado !== "ok" || !wbx.linhas) return [];
  const contagem = new Map<string, number>();
  for (const l of wbx.linhas) {
    const v = l["Level 4"];
    if (typeof v === "string" && !NIVEL4_PBIX.has(v)) contagem.set(v, (contagem.get(v) ?? 0) + 1);
  }
  if (contagem.size === 0) return [];
  const filtroPagina = compilarFiltros(pagina.filtros, "WBX");
  return [...contagem].map(([valor, n]) => {
    const entra = filtroPagina.aplicar({ "Level 4": valor });
    return `Cronograma atual: WBX[Level 4] = "${valor}" (${n} tarefas) não existe no PBIX. Filtros e medidas do BI foram mantidos `
      + `literalmente: essas tarefas ${entra ? "ENTRAM nesta página (o filtro original é por exclusão)" : "NÃO entram nesta página"} `
      + `e não contam em nenhuma medida "Engineering - n%". Corrigir o nome na fonte (.mpp) ou aprovar uma correspondência explícita.`;
  });
}

/** Linhas informativas: de onde vieram as tarefas e quais correspondências do BI foram aplicadas (valor bruto → rótulo do BI). */
export function infoFonte(fontes: Record<string, FonteCarregada>): string[] {
  const wbx = fontes.WBX;
  if (wbx?.estado !== "ok" || !wbx.linhas) return [];
  const out: string[] = [];
  out.push(wbx.versaoId !== undefined
    ? `Cronograma: BI CHS001 isolado (bi_chs001), versão ${wbx.versaoId} — cópia do .mpp oficial lida com MPXJ.`
    : "Cronograma: fonte legada (VW_Cronograma_Tipo2_Ultima_Versao) — o cronograma isolado do BI ainda não está disponível; Início, LB, trabalho real, pai e série diária ficam PENDENTE.");
  const alias = new Map<string, { bi: string; n: number }>();
  for (const l of wbx.linhas) {
    const bi = l["Level 4"], bruto = l["Level 4 Original"];
    if (typeof bi === "string" && typeof bruto === "string" && bi !== bruto) {
      const a = alias.get(bruto) ?? { bi, n: 0 };
      a.n++;
      alias.set(bruto, a);
    }
  }
  for (const [bruto, { bi, n }] of alias)
    out.push(`Correspondência do BI aplicada (só CHS001, auditável em bi_chs001.Alias_Rotulo): "${bruto}" (valor do .mpp, preservado) → "${bi}" em ${n} tarefas.`);
  return out;
}
