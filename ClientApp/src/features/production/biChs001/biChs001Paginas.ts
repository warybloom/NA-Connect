// =============================================================================================
// BI — CHS001 (Original): REGISTRO das 23 páginas do relatório Power BI CHS001-000-PM0-RT-002-01 (dados puros, sem React).
// Nomes e ordem = pages.json do PBIR (conferido em 15/10/2026). Rotas estáveis /controle-producao/bi-chs001/<slug>.
//
// ISOLAMENTO (deliberado): estas folhas NÃO entram em PRODUCTION_NAV (productionRoutes.ts). Lá, toda folha vira tela de
// TV automaticamente (tvScreens.tsx + contrato com Services/Tv/TvTelas.cs) — e a reprodução do BI não deve ir para a TV
// sem implementação completa e revisão de autorização. O sidebar (officialNavigation.ts), o guard de URL
// (permissionLogic.ts) e a rota (ControleProducaoPage.tsx) leem este registro diretamente.
//
// PERMISSÃO (somente keys já existentes no catálogo; nenhuma key nova):
//   • páginas sem valor monetário → "reports.gestao.view" (mesma key das telas GESTÃO À VISTA);
//   • páginas com custo absoluto (Controle de Hora e Custo / Gestão) → "production.managerial.hh-custo.view".
// Cada endpoint de dados continua exigindo a SUA key no backend (ex.: dashboards de produção).
// =============================================================================================

export interface BiChs001Pagina {
  /** Posição no relatório original (1..23). */
  ordem: number;
  /** Nome exatamente como no Power BI. */
  nome: string;
  slug: string;
  /** Id da página no PBIR (pages.json) — usado na navegação interna do BI (visualLink PageNavigation). */
  pageId: string;
  /** Arquivo de layout extraído em ./generated (gerado por _bi_chs001_replica/tools/extrair_layout.py). */
  arquivo: string;
  permissionKeys: readonly string[];
  containsCostData: boolean;
  /**
   * true = página fora do grupo prioritário (PRIORIDADE_10_TELAS.md): layout extraído, mas dados/paridade ainda não
   * trabalhados — o sidebar mostra "Em desenvolvimento" para não confundir com entrega concluída.
   */
  emDesenvolvimento: boolean;
}

// Keys EXCLUSIVAS do BI Tipo 2: não vazam de outros painéis de Produção.
const VER = ["production.bi-chs001.view"] as const;
const CUSTO = ["production.bi-chs001.cost.view"] as const;

/** Grupo prioritário (PRIORIDADE_10_TELAS.md, 15/10/2026): Resumo 30/60/90, LDP Engineering/3D 30/60/90 e Programação. */
export const PRIORIDADE_10 = ["resumo-30", "resumo-60", "resumo-90", "ldp-engineering-30", "ldp-engineering-60", "ldp-engineering-90",
  "ldp-3d-30", "ldp-3d-60", "ldp-3d-90", "programacao"] as const;

const p = (ordem: number, nome: string, slug: string, pageId: string, custo = false): BiChs001Pagina => ({
  ordem, nome, slug, pageId, arquivo: `p${String(ordem).padStart(2, "0")}.json`,
  permissionKeys: custo ? CUSTO : VER, containsCostData: custo,
  emDesenvolvimento: !(PRIORIDADE_10 as readonly string[]).includes(slug),
});

export const BI_CHS001_PAGINAS: readonly BiChs001Pagina[] = [
  p(1, "Capa", "capa", "0e0a3ca00d8598b7bb84"),
  p(2, "Resumo Geral", "resumo-geral", "38a3182809330acffc14"),
  p(3, "Resumo 30%", "resumo-30", "03af0eebc780428b46b2"),
  p(4, "Resumo 60%", "resumo-60", "dbdd8baab8e8eb03281f"),
  p(5, "Resumo 90%", "resumo-90", "7340affd805e6aa3300a"),
  p(6, "Progress to Date", "progress-to-date", "72f80a88315cee7fb168"),
  p(7, "Planned Activities", "planned-activities", "69e8cff8f68548a04ce0"),
  p(8, "LDP - 3D - 30%", "ldp-3d-30", "fbeec2691dbe6e33e5ce"),
  p(9, "LDP - 3D - 60%", "ldp-3d-60", "95b26829169a5f2d0252"),
  p(10, "LDP - 3D - 90%", "ldp-3d-90", "f7e070ab93ba4e1aa3bd"),
  p(11, "3D - MODEL", "3d-model", "2090864dbc6a0b1171c5"),
  p(12, "LDP - Engineering - 30%", "ldp-engineering-30", "4d0c3d1a40ad48f4ead6"),
  p(13, "LDP - Engineering - 60%", "ldp-engineering-60", "24ea8fc081207ebc9c38"),
  p(14, "LDP - Engineering - 90%", "ldp-engineering-90", "a549eb6c70bd79a484e7"),
  p(15, "CAP externo", "cap-externo", "86343ea67302a67c5d0a"),
  p(16, "CAP Interno", "cap-interno", "26a62bc7c692b2e1249a"),
  p(17, "Programação", "programacao", "2511ad67d65b7a106a91"),
  p(18, "Controle de Desvio", "controle-de-desvio", "e1f2e870d2d2db344ee4"),
  p(19, "Controle de Holds", "controle-de-holds", "4befc41400931dca0e77"),
  p(20, "Não Conformidades", "nao-conformidades", "30d64d433d0e63e4dda4"),
  p(21, "Controle de Hora e Custo", "controle-de-hora-e-custo", "61c7be92626b5cc9a9ec", true),
  p(22, "Controle de Hora e Custo Gestão", "controle-de-hora-e-custo-gestao", "5b89c18d21476e277c0e", true),
  p(23, "Controle de Qualidade", "controle-de-qualidade", "7241d736d086a0990512"),
];

/**
 * Rótulo do grupo no sidebar (dentro de Controle de Produção) e categoria da TV. Até 19/10/2026 era
 * "BI — CHS001 (Original)"; slugs e rotas (/controle-producao/bi-chs001/<slug>) NÃO mudaram.
 */
export const BI_CHS001_GRUPO = "Gestão à Vista Tipo 2";
export const BI_CHS001_PREFIXO = "bi-chs001";

export const rotaBiChs001 = (slug: string) => `/controle-producao/${BI_CHS001_PREFIXO}/${slug}`;

/** slug do Controle de Produção ("bi-chs001/<pagina>") → página do BI; undefined se não for do BI. */
export function paginaBiPorSlugProducao(slugProducao: string | undefined): BiChs001Pagina | undefined {
  if (!slugProducao?.startsWith(`${BI_CHS001_PREFIXO}/`)) return undefined;
  const slug = slugProducao.slice(BI_CHS001_PREFIXO.length + 1);
  return BI_CHS001_PAGINAS.find((pg) => pg.slug === slug);
}

export const ehSlugBiChs001 = (slugProducao: string | undefined) =>
  slugProducao === BI_CHS001_PREFIXO || !!slugProducao?.startsWith(`${BI_CHS001_PREFIXO}/`);

export const paginaBiPorId = (pageId: string | undefined) => BI_CHS001_PAGINAS.find((pg) => pg.pageId === pageId);

/** Rotas + keys (any-of) para o guard de URL (permissionLogic.ts). */
export const ROTAS_BI_CHS001: readonly { rota: string; keys: readonly string[] }[] =
  BI_CHS001_PAGINAS.map((pg) => ({ rota: rotaBiChs001(pg.slug), keys: pg.permissionKeys }));

export const TODAS_KEYS_BI_CHS001: readonly string[] = [...new Set(BI_CHS001_PAGINAS.flatMap((pg) => pg.permissionKeys))];
