export interface BiBml011016Pagina {
  ordem: number; nome: string; slug: string; pageId: string; arquivo: string; visuais: number; fontes: readonly string[];
}

const p = (ordem: number, nome: string, slug: string, pageId: string, visuais: number, fontes: readonly string[]): BiBml011016Pagina =>
  ({ ordem, nome, slug, pageId, arquivo: `p${String(ordem).padStart(2, "0")}.json`, visuais, fontes });

export const BI_BML011016_GRUPO = "BI BML011.016";
export const BI_BML011016_PREFIXO = "/bi-bml011016";
export const BI_BML011016_PAGINAS: readonly BiBml011016Pagina[] = [
  p(1, "Resumo", "resumo", "38a3182809330acffc14", 34, ["ldp", "cronograma", "cronograma-diario", "programacao-semanal"]),
  p(2, "LDP", "ldp", "755020d27e930bb94b5d", 18, ["ldp"]),
  p(3, "CAP externo", "cap-externo", "86343ea67302a67c5d0a", 13, ["cap-externo"]),
  p(4, "Critical Analysis", "critical-analysis", "b9be43f96388058e9521", 10, []),
  p(5, "Programação", "programacao", "38d4c2bafdff941e8a18", 8, ["cronograma", "cronograma-diario"]),
  p(6, "Controle de Desvio", "controle-de-desvio", "c10a612a78cf4d20e8af", 13, ["desvios"]),
  p(7, "Controle de Holds", "controle-de-holds", "64d0531f013196e9fe93", 9, ["holds"]),
  p(8, "Curva financeira", "curva-financeira", "d89e953a8ea773b7097c", 6, ["curva-financeira"]),
  p(9, "Pagamento", "pagamento", "595fd633e9df6e1f6efa", 7, ["pagamentos"]),
];
export const rotaBiBml011016 = (slug: string) => `${BI_BML011016_PREFIXO}/${slug}`;
export const paginaBiBmlPorSlug = (slug?: string) => BI_BML011016_PAGINAS.find((pagina) => pagina.slug === slug);
export const paginaBiBmlPorId = (id?: string) => BI_BML011016_PAGINAS.find((pagina) => pagina.pageId === id);
