// =============================================================================================
// REGISTRO ÚNICO da navegação do Controle de Produção (dados puros — sem React/ícones, usado pelo sidebar, pelas
// rotas, pelo guard de URL e pelos testes de contrato). Cada folha é uma rota estável e PLANA:
//   /controle-producao/<slug>
// Não há mais agrupamento Gerencial/Operacional (30/09/2026): quem vê cada tela é decidido só pela permissão.
// As rotas antigas /controle-producao/<gerencial|operacional>/<slug> redirecionam para a rota plana.
// Os dashboards continuam em productionScreens.ts (não duplicados): a folha só aponta para o screenId.
//
// PERMISSÃO DE CADA TELA = QUALQUER uma de permissionKeys (keys já existentes no catálogo; nenhuma key nova):
//   • dashboards: production.managerial.<tela>.view OU production.operational.<tela>.view;
//   • telas com valor monetário absoluto (containsCostData): SOMENTE as keys gerenciais/financeiras;
//   • Lista de Documentos: cde.document-list.view (mesma key/endpoint da antiga aba do CDE).
//
// PREPARAÇÃO (não implementado — só registrado):
//   • LDP com HH/custo deverá reutilizar os canonical facts de components/pmo/ViewGestaoAVista.tsx.
//   • Controle de HH / Custo e Análise Financeira: base = ViewTimesheet.tsx + /api/reports/financeiro-horas.
// =============================================================================================
import type { ProductionScreenId } from "./productionScreens";
import { ROTA_AUDITORIA_PROJETO } from "../planning/auditoriaProjeto";

export type ProductionPage =
  | { tipo: "dashboard"; screenId: ProductionScreenId }
  | { tipo: "curva-s" }
  | { tipo: "hh-custo" }
  | { tipo: "lista-documentos" }
  | { tipo: "resumo" }
  | { tipo: "jamile"; screen: "visao-geral" | "programacao" | "documentacao" | "indicadores" }
  | { tipo: "jamile-disciplina"; mode: "hh" | "custo" }
  | { tipo: "resumo-tv-teste" }
  | { tipo: "gestao-hh-custo" }
  | { tipo: "resumo-lab" }
  | { tipo: "analise-financeira"; visao: "geral" | "projeto" | "documento" }
  | { tipo: "lancamentos"; visao: "pessoas" | "disciplinas" | "documentos" }
  | { tipo: "em-desenvolvimento"; descricao: string };

export interface ProductionLeaf {
  kind: "leaf";
  slug: string;
  label: string;
  page: ProductionPage;
  /** Permissão da tela: QUALQUER uma destas keys (literais — conferidas contra o PermissionCatalog por contrato). */
  permissionKeys: readonly string[];
  /** true = a tela exibe valor monetário absoluto (só keys gerenciais/financeiras a liberam). */
  containsCostData: boolean;
  /**
   * true = fora do SIDEBAR, mas a rota continua válida (compatibilidade/reaproveitamento). Usado pelos dashboards
   * nivelamento-programacao, controle-programacao e controle-documental — nada foi apagado.
   */
  foraDoMenu?: boolean;
  /**
   * true = tela ANALÍTICA: aceita vários projetos (projetosAnalise do ProjetoAtualContext, seletor "Analisar").
   * Telas sem esta marca são operacionais e usam só o projeto principal.
   */
  analitica?: boolean;
}

export interface ProductionGroup {
  kind: "group";
  label: string;
  children: ProductionLeaf[];
}

export type ProductionNavItem = ProductionLeaf | ProductionGroup;

const dash = (slug: string, label: string, screenId: ProductionScreenId, segmento: string): ProductionLeaf => ({
  kind: "leaf", slug, label, page: { tipo: "dashboard", screenId }, containsCostData: false,
  permissionKeys: [`production.managerial.${segmento}.view`, `production.operational.${segmento}.view`],
});

// Chaves escritas por extenso nas folhas abaixo (e nos helpers) para o contrato conseguir lê-las.
export const PRODUCTION_NAV: readonly ProductionNavItem[] = [
  {
    // Painel geral do projeto: MESMA fonte e MESMAS regras da Gestão à Vista (/api/reports/gestao-vista +
    // projectFacts/statusCounts) e da Curva S (avanço). Exibe custo → só keys que já liberam custo na Gestão à Vista.
    kind: "leaf", slug: "resumo", label: "Resumo", page: { tipo: "resumo" }, containsCostData: true, analitica: true,
    permissionKeys: ["reports.gestao.view", "production.managerial.hh-custo.view"],
  },
  {
    kind: "leaf", slug: "gestao-hh-custo", label: "Gestão à Vista — HH e Custo",
    page: { tipo: "gestao-hh-custo" }, containsCostData: true,
    permissionKeys: ["production.managerial.hh-custo.view"],
  },
  {
    // Tela operacional de documentos (antes: CDE → Documentos → aba Lista de Documentos).
    kind: "leaf", slug: "lista-documentos", label: "Lista de Documentos", page: { tipo: "lista-documentos" }, containsCostData: false,
    permissionKeys: ["cde.document-list.view"],
  },
  {
    kind: "leaf", slug: "curva-s", label: "Curva S", page: { tipo: "curva-s" }, containsCostData: false,
    permissionKeys: ["production.managerial.curva-s.view", "production.operational.curva-s.view"],
  },
  dash("ldp", "LDP", "ldp", "ldp"),
  dash("programacao", "Programação Semanal", "programacao", "programacao"),
  dash("cap-externo", "CAP Externo", "cap-externo", "cap-externo"),
  dash("cap-interno", "CAP Interno", "cap-interno", "cap-interno"),
  dash("desvio-escopo", "Desvio de Escopo", "desvio-escopo", "desvio-escopo"),
  dash("change-order", "Change Order", "change-order", "change-order"),
  dash("hold", "HOLD's", "hold", "hold"),
  {
    kind: "leaf", slug: "boletim-medicao", label: "Boletim de Medição", containsCostData: false,
    page: { tipo: "em-desenvolvimento", descricao: "Boletim de Medição do projeto. Estrutura de navegação criada; a tela será implementada em uma próxima fase." },
    permissionKeys: ["production.managerial.boletim.view", "production.operational.boletim.view"],
  },
  {
    kind: "group", label: "Qualidade", children: [
      dash("desvio-qualidade", "Desvio de Qualidade", "desvio-qualidade", "desvio-qualidade"),
      dash("rnc", "RNC", "rnc", "rnc"),
    ],
  },
  {
    // Exibe HH e custo absolutos (reutiliza ViewTimesheet — mesma tela do painel PMO): só a key gerencial libera.
    kind: "leaf", slug: "controle-hh-custo", label: "Controle de HH / Custo", page: { tipo: "hh-custo" }, containsCostData: true,
    permissionKeys: ["production.managerial.hh-custo.view"],
  },
  {
    // Timesheet do projeto (Project_ID_ACC + CR/OS canônico), agregado no backend. Mesmas keys de /api/production-audit.
    kind: "group", label: "Lançamentos", children: [
      {
        kind: "leaf", slug: "lancamentos/pessoas", label: "Pessoas", page: { tipo: "lancamentos", visao: "pessoas" }, containsCostData: false,
        permissionKeys: ["production.managerial.hh-custo.view", "reports.gestao.view"],
      },
      {
        kind: "leaf", slug: "lancamentos/disciplinas", label: "Disciplinas", page: { tipo: "lancamentos", visao: "disciplinas" }, containsCostData: false,
        permissionKeys: ["production.managerial.hh-custo.view", "reports.gestao.view"],
      },
      {
        kind: "leaf", slug: "lancamentos/documentos", label: "Documentos", page: { tipo: "lancamentos", visao: "documentos" }, containsCostData: false,
        permissionKeys: ["production.managerial.hh-custo.view", "reports.gestao.view"],
      },
    ],
  },
  {
    kind: "group", label: "Análise Financeira", children: [
      // Fonte: /api/reports/gestao-vista (mesmos totais da Gestão à Vista). Visão geral/disciplina/projeto e documento.
      {
        kind: "leaf", slug: "analise-financeira/visao-geral", label: "Visão Geral", containsCostData: true, analitica: true,
        page: { tipo: "analise-financeira", visao: "geral" },
        permissionKeys: ["production.financial.project.view"],
      },
      {
        kind: "leaf", slug: "analise-financeira/projeto", label: "Projeto", containsCostData: true, analitica: true,
        page: { tipo: "analise-financeira", visao: "projeto" },
        permissionKeys: ["production.financial.project.view"],
      },
      {
        kind: "leaf", slug: "analise-financeira/documento", label: "Documento", containsCostData: true, analitica: true,
        page: { tipo: "analise-financeira", visao: "documento" },
        permissionKeys: ["production.financial.document.view"],
      },
      {
        // Auditoria 30/09/2026: nenhuma fonte atual (gestao-vista/financeiro-horas/Timesheet espelho) traz a pessoa —
        // não há como calcular por pessoa sem inventar dado.
        kind: "leaf", slug: "analise-financeira/pessoa", label: "Pessoa", containsCostData: true,
        page: { tipo: "em-desenvolvimento", descricao: "Análise financeira por pessoa. As fontes atuais (Gestão à Vista / Financeiro de Horas) não trazem a pessoa de cada lançamento; a tela depende dessa informação no backend." },
        permissionKeys: ["production.financial.person.view"],
      },
    ],
  },
  {
    // Recriação fiel das quatro telas do PDF "Painel Gestão À VIsta".
    // Sob a mesma categoria ficam as telas de detalhamento separadas.
    kind: "group", label: "GESTÃO À VISTA", children: [
      { kind: "leaf", slug: "gestao-a-vista/visao-geral", label: "01. VISÃO GERAL",
        page: { tipo: "jamile", screen: "visao-geral" }, containsCostData: false,
        permissionKeys: ["reports.gestao.view"] },
      { kind: "leaf", slug: "gestao-a-vista/programacao", label: "02. PROGRAMAÇÃO",
        page: { tipo: "jamile", screen: "programacao" }, containsCostData: false,
        permissionKeys: ["reports.gestao.view"] },
      { kind: "leaf", slug: "gestao-a-vista/documentacao", label: "03. DOCUMENTAÇÃO",
        page: { tipo: "jamile", screen: "documentacao" }, containsCostData: false,
        permissionKeys: ["reports.gestao.view"] },
      { kind: "leaf", slug: "gestao-a-vista/indicadores", label: "04. INDICADORES",
        page: { tipo: "jamile", screen: "indicadores" }, containsCostData: true,
        permissionKeys: ["production.managerial.hh-custo.view"] },
      { kind: "leaf", slug: "gestao-a-vista/hh-disciplina", label: "HH por Disciplina",
        page: { tipo: "jamile-disciplina", mode: "hh" }, containsCostData: false,
        permissionKeys: ["reports.gestao.view"] },
      { kind: "leaf", slug: "gestao-a-vista/custo-disciplina", label: "Custo por Disciplina",
        page: { tipo: "jamile-disciplina", mode: "custo" }, containsCostData: true,
        permissionKeys: ["production.managerial.hh-custo.view"] },
    ],
  },
  {
    // Tela própria (folha). A tela definitiva de indicadores (BI) ainda não existe: shell honesto, sem métricas.
    kind: "leaf", slug: "indicadores-producao", label: "Indicadores de Produção", containsCostData: false,
    page: { tipo: "em-desenvolvimento", descricao: "Indicadores de Produção do projeto. Estrutura de navegação criada; os indicadores (BI) serão implementados em uma próxima fase." },
    permissionKeys: ["production.managerial.indicadores.view", "production.operational.indicadores.view"],
  },
  // Piloto TV: acessível pelo sidebar só ao perfil administrativo (MasterAdmin/permissions.manage).
  // O backend de dados continua autorizando cada API; esconder valores monetários não retira controles.
  {
    kind: "leaf", slug: "resumo-tv-teste", label: "Resumo TV — Testes",
    page: { tipo: "resumo-tv-teste" }, containsCostData: true, analitica: true,
    permissionKeys: ["permissions.manage"],
  },
  // Dashboards existentes que NÃO estão na árvore do sidebar: continuam registrados e acessíveis pela rota, com as
  // mesmas keys e endpoints — apenas fora do menu.
  { ...dash("nivelamento-programacao", "Nivelamento da Programação", "nivelamento-programacao", "nivelamento"), foraDoMenu: true },
  { ...dash("controle-programacao", "Controle da Programação", "controle-programacao", "controle-programacao"), foraDoMenu: true },
  { ...dash("controle-documental", "Controle Documental", "controle-documental", "controle-documental"), foraDoMenu: true },
];

function itens(incluirForaDoMenu: boolean): ProductionNavItem[] {
  const valida = (l: ProductionLeaf) => incluirForaDoMenu || !l.foraDoMenu;
  const out: ProductionNavItem[] = [];
  for (const item of PRODUCTION_NAV) {
    if (item.kind === "leaf") { if (valida(item)) out.push(item); continue; }
    const filhos = item.children.filter(valida);
    if (filhos.length > 0) out.push({ ...item, children: filhos });
  }
  return out;
}

/** Árvore do SIDEBAR (sem as telas foraDoMenu). O filtro por permissão é feito pelo Layout (filtrarPorPermissao). */
export const navProducao = (): ProductionNavItem[] => itens(false);

/** TODAS as telas roteáveis (inclui as foraDoMenu) — rotas, guard de URL e redirecionamentos. */
export const folhasProducao = (): ProductionLeaf[] => itens(true).flatMap((i) => (i.kind === "leaf" ? [i] : i.children));

export const rotaProducao = (slug: string) => `/controle-producao/${slug}`;

export const encontrarFolha = (slug: string | undefined): ProductionLeaf | undefined => folhasProducao().find((l) => l.slug === slug);

/** Rotas + keys (any-of) de TODAS as folhas (para o guard de URL). */
export const ROTAS_PRODUCAO: readonly { rota: string; keys: readonly string[] }[] =
  folhasProducao().map((l) => ({ rota: rotaProducao(l.slug), keys: l.permissionKeys }));

/** Todas as keys que liberam alguma tela do Controle de Produção. */
export const TODAS_KEYS_PRODUCAO: readonly string[] = [...new Set(folhasProducao().flatMap((l) => l.permissionKeys))];

/**
 * Slugs que deixaram de ser tela e viraram grupo → primeira tela do grupo. /controle-producao/lancamentos (folha única
 * antiga) → /controle-producao/lancamentos/pessoas. Não confundir com a rota global /lancamentos (Desvios/Holds/NC).
 */
export const REDIRECIONAMENTOS_PRODUCAO: Readonly<Record<string, string>> = {
  lancamentos: "lancamentos/pessoas",
};
export const slugRedirecionado = (slug: string | undefined): string | undefined => (slug ? REDIRECIONAMENTOS_PRODUCAO[slug] : undefined);

/** Telas que SAÍRAM do Controle de Produção para outra área → rota absoluta de destino. */
export const REDIRECIONAMENTOS_EXTERNOS_PRODUCAO: Readonly<Record<string, string>> = {
  auditoria: ROTA_AUDITORIA_PROJETO, // Auditoria do Projeto → Painel de Planejamento
};
export const rotaExternaRedirecionada = (slug: string | undefined): string | undefined => (slug ? REDIRECIONAMENTOS_EXTERNOS_PRODUCAO[slug] : undefined);

/** URL antiga (?tela=<screenId>) → rota plana equivalente. */
export function rotaLegada(screenId: string | null): string | undefined {
  if (!screenId) return undefined;
  const folha = folhasProducao().find((l) => l.page.tipo === "dashboard" && l.page.screenId === screenId);
  return folha ? rotaProducao(folha.slug) : undefined;
}

/**
 * Caminho antigo com variante (/controle-producao/gerencial/ldp) → caminho plano (/controle-producao/ldp).
 * Devolve undefined quando o caminho já é plano.
 */
export function semVarianteLegada(pathname: string): string | undefined {
  const m = /^\/controle-producao\/(gerencial|operacional)(\/.*)?$/.exec(pathname);
  if (!m) return undefined;
  return m[2] && m[2] !== "/" ? `/controle-producao${m[2]}` : "/controle-producao";
}
