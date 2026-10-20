// Catálogo de conteúdos da TV — DERIVADO do registro único do Controle de Produção (productionRoutes.ts → PRODUCTION_NAV):
// toda folha roteável vira uma tela de TV, com a MESMA permissão (permissionKeys), o MESMO containsCostData e o MESMO
// componente (ProductionPageContent.tsx). Nenhuma árvore manual: tela nova no Controle de Produção aparece aqui sozinha
// (e o teste de contrato exige que o backend — Services/Tv/TvConfiguration.cs → TvTelas — também a reconheça).
//
// IDs estáveis (apresentações já salvas continuam válidas):
//   • dashboards → screenId do registry (ldp, hold, ...); curva-s; resumo;
//   • "analise-financeira" = Análise Financeira → Visão Geral (id histórico);
//   • demais folhas → slug com "/" trocado por "-" (ex.: gestao-a-vista/visao-geral → gestao-a-vista-visao-geral);
//   • "gestao-a-vista" = painel PMO antigo (ViewGestaoAVista), fora do Controle de Produção — mantido por compatibilidade.
//   • "gestao-a-vista-programacao-diaria" (19/10/2026) = Programação Diária, SÓ na TV (fora de PRODUCTION_NAV): mesmo visual
//     da 02. PROGRAMAÇÃO, UM projeto e UMA data (dataReferencia) por item; pode ser repetida com projetos/datas diferentes.
//     dataReferencia "AUTO" (20/10/2026) = HOJE + PENDÊNCIAS DE ONTEM por disciplina, datas da Bahia recalculadas a cada dia.
//   • "gestao-vista-tipo2-<slug>" = Gestão à Vista Tipo 2 (reprodução do BI CHS001, biChs001Paginas.ts) — catálogo PRÓPRIO,
//     fora de PRODUCTION_NAV: só as 10 páginas prioritárias (PRIORIDADE_10), um projeto por item e EXCLUSIVAS da CHS001.
// Telas "em-desenvolvimento" ficam no catálogo como INDISPONÍVEIS (não selecionáveis, recusadas pelo backend).
import { Component, Suspense, lazy, type ErrorInfo, type ReactNode } from "react";
import {
  BarChart3, CalendarDays, ClipboardList, Construction, FileText, Gauge, LayoutDashboard, ListChecks, MonitorPlay, TrendingUp, Users, Wallet,
  type LucideIcon,
} from "lucide-react";
import ViewGestaoAVista from "@/components/pmo/ViewGestaoAVista";
import GestaoVistaJamilePage from "@/features/production/analytics/GestaoVistaJamilePage";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ReportState } from "@/components/reports/ReportComponents";
import { useNavegacaoTv, usePermissaoProjetos, type NavegacaoTv } from "./tvNavegacao";
import { getProductionScreen } from "@/features/production/productionScreens";
import { BI_CHS001_GRUPO, BI_CHS001_PAGINAS, PRIORIDADE_10 } from "@/features/production/biChs001/biChs001Paginas";
import { PRODUCTION_NAV, encontrarFolha, type ProductionLeaf, type ProductionPage } from "@/features/production/productionRoutes";
import { ConteudoPaginaProducao, paginaImplementada, type ProductionPageImplementada } from "@/features/production/ProductionPageContent";

export const TV_CURVA_S = "curva-s";
export const TV_RESUMO = "resumo";
export const TV_ANALISE_FINANCEIRA = "analise-financeira";
/** Painel PMO antigo (ViewGestaoAVista) — não é nenhuma das telas "GESTÃO À VISTA" do Controle de Produção. */
export const TV_GESTAO_A_VISTA = "gestao-a-vista";

/** Categorias do seletor, na ordem do Controle de Produção. */
export const CATEGORIAS_TV = ["Principais", "Produção", "Lançamentos", "Análise Financeira", "Gestão à Vista", "Gestão à Vista Tipo 2", "Outras"] as const;
export type CategoriaTv = (typeof CATEGORIAS_TV)[number];

type PaginaTv = ProductionPage | { tipo: "pmo-gestao-a-vista" } | { tipo: "gestao-vista-tipo2"; slug: string } | { tipo: "programacao-diaria" };

export interface TelaTv {
  id: string;
  /** Nome completo (seletor, sequência, título do player). */
  label: string;
  categoria: CategoriaTv;
  icon: LucideIcon;
  /** false = em desenvolvimento: aparece como indisponível, não pode ser adicionada nem salva. */
  tvSelectable: boolean;
  containsCostData: boolean;
  /**
   * true = a tela já consolida vários projetos (dashboards: união das linhas no backend; Curva S; telas analíticas).
   * false = um projeto por item — a TV nunca combina resultados de uma tela que não foi feita para isso.
   */
  multiProjeto: boolean;
  /** Permissão: QUALQUER uma destas keys — as MESMAS da rota no sistema. */
  chaves: readonly string[];
  page: PaginaTv;
  /** Rota de origem no Controle de Produção (null = painel fora do Controle de Produção). */
  slug: string | null;
  /** Projeto (Project_ID_ACC) ao qual a tela é EXCLUSIVA; ausente = qualquer projeto. Espelho de TvTela.ProjetoExclusivo. */
  projetoExclusivo?: string;
  /** true = cada item exige dataReferencia (AAAA-MM-DD). Espelho de TvTela.DataReferenciaObrigatoria. */
  exigeDataReferencia?: boolean;
}

/**
 * Quais tipos de página consolidam vários projetos. Record exaustivo: um tipo novo em ProductionPage não compila até
 * alguém decidir aqui (e no backend) se ele aceita vários projetos.
 */
const MULTIPROJETO: Record<ProductionPage["tipo"], boolean> = {
  dashboard: true, "curva-s": true, resumo: true, "analise-financeira": true, "resumo-tv-teste": true,
  // Gestão à Vista (Jamile) — validado 15/10/2026: todas as fontes consultam CADA projeto (gestao-vista, aba LDP, Norma,
  // programação); somas sobre os registros de cada projeto, percentuais pelos totais (projectFacts/entregasDaSemana),
  // disciplina pela Norma de CADA projeto (só a disciplina padrão oficial une projetos) e falha em qualquer projeto
  // bloqueia os números (ConsolidacaoIncompleta). No editor: consolidar em uma tela OU uma tela por projeto.
  jamile: true, "jamile-disciplina": true,
  // Usam o projeto principal (selection[0]) ou nunca foram validadas com vários projetos: um projeto por item.
  "lista-documentos": false, "gestao-hh-custo": false, "hh-custo": false, lancamentos: false,
  "resumo-lab": false, "em-desenvolvimento": false,
};

const ICONE: Partial<Record<ProductionPage["tipo"], LucideIcon>> = {
  resumo: ClipboardList, "curva-s": TrendingUp, "lista-documentos": FileText, "gestao-hh-custo": Wallet, "hh-custo": Wallet,
  lancamentos: Users, "analise-financeira": BarChart3, jamile: LayoutDashboard, "jamile-disciplina": Gauge, "resumo-tv-teste": MonitorPlay,
  "em-desenvolvimento": Construction,
};

/** Id da tela na TV a partir da folha do Controle de Produção (ver regras no topo do arquivo). */
export function idTelaTv(folha: Pick<ProductionLeaf, "slug" | "page">): string {
  if (folha.page.tipo === "dashboard") return folha.page.screenId;
  if (folha.slug === "analise-financeira/visao-geral") return TV_ANALISE_FINANCEIRA;
  return folha.slug.replace(/\//g, "-");
}

function categoria(folha: ProductionLeaf, grupo: string | null): CategoriaTv {
  if (folha.page.tipo === "dashboard") return "Produção";                 // inclui o grupo Qualidade e as telas fora do menu
  if (grupo === "Lançamentos") return "Lançamentos";
  if (grupo === "Análise Financeira") return "Análise Financeira";
  if (grupo === "GESTÃO À VISTA") return "Gestão à Vista";
  return "Principais";
}

const ROTULO_GRUPO: Record<string, string> = { "Lançamentos": "Lançamentos", "Análise Financeira": "Análise Financeira", "GESTÃO À VISTA": "Gestão à Vista" };

function telaDaFolha(folha: ProductionLeaf, grupo: string | null): TelaTv {
  const prefixo = grupo ? ROTULO_GRUPO[grupo] : undefined;
  return {
    id: idTelaTv(folha),
    label: prefixo ? `${prefixo} — ${folha.label}` : folha.label,
    categoria: categoria(folha, grupo),
    icon: (folha.page.tipo === "dashboard" ? getProductionScreen(folha.page.screenId)?.icon : ICONE[folha.page.tipo]) ?? ListChecks,
    tvSelectable: paginaImplementada(folha.page),
    containsCostData: folha.containsCostData,
    multiProjeto: MULTIPROJETO[folha.page.tipo],
    chaves: folha.permissionKeys,
    page: folha.page,
    slug: folha.slug,
  };
}

/** Painel PMO antigo, mantido para as apresentações que já o usam. */
const PAINEL_PMO: TelaTv = {
  id: TV_GESTAO_A_VISTA, label: "Painel PMO — Gestão à Vista (antigo)", categoria: "Outras", icon: MonitorPlay, tvSelectable: true,
  containsCostData: true, multiProjeto: true, chaves: ["reports.gestao.view"], page: { tipo: "pmo-gestao-a-vista" }, slug: null,
};

// ---------------------------------------------------------------------------------------------
// Gestão à Vista Tipo 2 (19/10/2026): o grupo do sidebar BI_CHS001_GRUPO (antigo "BI — CHS001 (Original)"). Reproduz o
// relatório Power BI da CHS001 — títulos e dados são da CHS001 — então cada item tem UM projeto e só pode ser a CHS001
// (backend: TvTelas.ProjetoChs001 + TvConfigurationRules). Só as 10 páginas prioritárias; as outras 13 páginas do BI
// (em desenvolvimento) NUNCA entram na TV. Mesma key de cada página (biChs001Paginas.ts → permissionKeys).
// ---------------------------------------------------------------------------------------------
export const PROJETO_GESTAO_VISTA_TIPO2 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e";
export const NOME_PROJETO_GESTAO_VISTA_TIPO2 = "CHS001 - ETHANOL PLANT";
export const idTelaTvTipo2 = (slugPaginaBi: string) => `gestao-vista-tipo2-${slugPaginaBi}`;

const TELAS_TIPO2: readonly TelaTv[] = BI_CHS001_PAGINAS
  .filter((p) => !p.emDesenvolvimento && (PRIORIDADE_10 as readonly string[]).includes(p.slug))
  .map((p): TelaTv => ({
    id: idTelaTvTipo2(p.slug), label: `${BI_CHS001_GRUPO} — ${p.nome}`, categoria: "Gestão à Vista Tipo 2", icon: Gauge, tvSelectable: true,
    containsCostData: p.containsCostData, multiProjeto: false, chaves: p.permissionKeys, page: { tipo: "gestao-vista-tipo2", slug: p.slug },
    slug: `bi-chs001/${p.slug}`, projetoExclusivo: PROJETO_GESTAO_VISTA_TIPO2,
  }));

// ---------------------------------------------------------------------------------------------
// Programação Diária (19/10/2026): só existe na TV. Reutiliza a 02. PROGRAMAÇÃO (mesmo endpoint semanal oficial, filtrado
// EXATAMENTE no dia do item), com UM projeto e UMA data salvos no slide. Mesmas keys e mesma classificação de custo da
// 02. PROGRAMAÇÃO (a semanal continua intacta no Controle de Produção e na TV).
// ---------------------------------------------------------------------------------------------
export const TV_PROGRAMACAO_DIARIA = "gestao-a-vista-programacao-diaria";
/** 01. VISÃO GERAL da Gestão à Vista (inserida antes das diárias automáticas quando ainda não está na sequência). */
export const TV_VISAO_GERAL = "gestao-a-vista-visao-geral";
const FOLHA_PROGRAMACAO = encontrarFolha("gestao-a-vista/programacao");
const TELA_PROGRAMACAO_DIARIA: TelaTv = {
  id: TV_PROGRAMACAO_DIARIA, label: "Gestão à Vista — Programação Diária (1 projeto · 1 dia)", categoria: "Gestão à Vista", icon: CalendarDays,
  tvSelectable: true, containsCostData: FOLHA_PROGRAMACAO?.containsCostData ?? false, multiProjeto: false,
  chaves: FOLHA_PROGRAMACAO?.permissionKeys ?? ["reports.gestao.view"], page: { tipo: "programacao-diaria" }, slug: null, exigeDataReferencia: true,
};

/**
 * TODAS as telas: folhas do Controle de Produção (inclusive fora do menu e em desenvolvimento) + Programação Diária (só TV) +
 * Gestão à Vista Tipo 2 + painel PMO.
 */
export const TELAS_TV: readonly TelaTv[] = [
  ...PRODUCTION_NAV.flatMap((item) => (item.kind === "leaf" ? [telaDaFolha(item, null)] : item.children.map((f) => telaDaFolha(f, item.label)))),
  TELA_PROGRAMACAO_DIARIA,
  ...TELAS_TIPO2,
  PAINEL_PMO,
];

/** Telas que podem ir para uma apresentação (implementadas). */
export const TELAS_TV_SELECIONAVEIS: readonly TelaTv[] = TELAS_TV.filter((t) => t.tvSelectable);

export const getTelaTv = (id: string | null | undefined): TelaTv | undefined => TELAS_TV.find((t) => t.id === id);

/** Keys da tela no sistema (as MESMAS da rota) — a tela só é oferecida a quem já pode abri-la. */
export const chavesTelaTv = (id: string): readonly string[] => getTelaTv(id)?.chaves ?? [];

/** Mantido por compatibilidade: keys das telas que não são dashboards do registry (null = dashboard). */
export const permissaoTelaTv = (id: string): readonly string[] | null => {
  const t = getTelaTv(id);
  return !t || t.page.tipo === "dashboard" ? null : t.chaves;
};

/** Telas que aceitam um único projeto por item. */
export const TELAS_TV_PROJETO_UNICO: readonly string[] = TELAS_TV.filter((t) => !t.multiProjeto).map((t) => t.id);

/** Tela → único projeto aceito (Gestão à Vista Tipo 2 = CHS001). Usado na validação (tvLogic) e no editor. */
export const TELAS_TV_PROJETO_EXCLUSIVO: Readonly<Record<string, string>> =
  Object.fromEntries(TELAS_TV.filter((t) => t.projetoExclusivo).map((t) => [t.id, t.projetoExclusivo!]));

/** Telas cujo item exige dataReferencia (Programação Diária). Usado na validação (tvLogic) e no editor. */
export const TELAS_TV_DATA_OBRIGATORIA: readonly string[] = TELAS_TV.filter((t) => t.exigeDataReferencia).map((t) => t.id);

/** Telas selecionáveis agrupadas por categoria (ordem do Controle de Produção dentro de cada categoria). */
export function telasPorCategoria<T extends Pick<TelaTv, "categoria">>(telas: readonly T[]): { categoria: CategoriaTv; telas: T[] }[] {
  return CATEGORIAS_TV.map((categoria) => ({ categoria, telas: telas.filter((t) => t.categoria === categoria) })).filter((g) => g.telas.length > 0);
}

/**
 * Renderiza o conteúdo de um slide com os projetos SALVOS no item. Nunca cai em outra tela: id desconhecido ou tela em
 * desenvolvimento exibem um aviso explícito. A permissão da tela é conferida no projeto principal (o backend de cada
 * relatório continua validando todos os projetos).
 */
export function ConteudoTv({ screenId, selection, dataReferencia = null }: { screenId: string; selection: PmoSelecionado[];
  /** Data salva no item (só telas exigeDataReferencia); nunca substituída por "hoje". */
  dataReferencia?: string | null }) {
  const tela = getTelaTv(screenId);
  if (!tela)
    return <ReportState title="Tela não encontrada" error>A tela “{screenId}” desta apresentação não existe mais no Controle de Produção. Remova-a em Configurações → Configuração de Telas para TV.</ReportState>;
  if (!tela.tvSelectable || (tela.page.tipo !== "pmo-gestao-a-vista" && tela.page.tipo !== "gestao-vista-tipo2" && tela.page.tipo !== "programacao-diaria"
    && !paginaImplementada(tela.page)))
    return <ReportState title={`${tela.label} — indisponível`}>Esta tela ainda está em desenvolvimento e não tem conteúdo para exibir.</ReportState>;
  if (selection.length === 0) return <ReportState title="Item sem projeto" error>Configure o projeto desta tela.</ReportState>;
  // Tela de um projeto só: nunca combina (configurações válidas já têm um único projeto; isto é só defesa).
  const projetos = tela.multiProjeto ? selection : selection.slice(0, 1);
  // Tela exclusiva de um projeto (Gestão à Vista Tipo 2 = CHS001): outro projeto nunca é exibido sob os títulos dela.
  // O backend já recusa salvar; isto é defesa para arquivo editado à mão.
  if (tela.projetoExclusivo && projetos.some((p) => p.id !== tela.projetoExclusivo))
    return <ReportState title={`${tela.label} — projeto não permitido`} error>
      Esta tela é exclusiva do projeto {NOME_PROJETO_GESTAO_VISTA_TIPO2}; o item está configurado com {projetos.map((p) => p.name || p.id).join(", ")}.
      Corrija em Configurações → Configuração de Telas para TV.
    </ReportState>;
  return (
    <GuardaPermissaoTv tela={tela} projetos={projetos}>
      <LimiteErroTv tela={tela.label}>
        {tela.page.tipo === "pmo-gestao-a-vista"
          ? <ViewGestaoAVista hub={projetos[0].hubId ?? ""} project={projetos[0].id} selection={projetos} />
          : tela.page.tipo === "gestao-vista-tipo2"
          ? <PaginaTipo2Tv slug={tela.page.slug} projectId={projetos[0].id} />
          : tela.page.tipo === "programacao-diaria"
          ? <GestaoVistaJamilePage selection={projetos} screen="programacao-diaria" dataReferencia={dataReferencia} />
          : <ConteudoPaginaProducao page={tela.page as ProductionPageImplementada} selection={projetos} />}
      </LimiteErroTv>
    </GuardaPermissaoTv>
  );
}

// Página do BI sob demanda: BiChs001Page carrega os layouts com import.meta.glob (e imagens/CSS próprios) — import estático
// traria tudo para o bundle principal e quebraria os testes Node deste arquivo. Mesmo componente de conteúdo do BI
// (PaginaBi/BiChs001Canvas/VisualBi), com a página e o projeto SALVOS no slide (sem URL, seletor nem redirecionamento).
const ConteudoPaginaBiChs001 = lazy(() =>
  import("@/features/production/biChs001/BiChs001Page").then((m) => ({ default: m.ConteudoPaginaBiChs001 })));

/** Dados do BI na TV: recarregados a cada 5 min (a TV fica ligada o dia todo; fora da TV o cache dura a sessão do BI). */
export const TV_MAX_IDADE_DADOS_BI_MS = 5 * 60_000;

/**
 * Links PageNavigation do BI dentro do player: só saltam para um slide Gestão à Vista Tipo 2 JÁ incluído na apresentação
 * (nunca para /controle-producao, nunca para fora do player). Página não incluída = link desabilitado.
 */
export function navegacaoBiNaTv(nav: NavegacaoTv | null) {
  return {
    disponivel: (slug: string) => !!nav?.destino(idTelaTvTipo2(slug)),
    ir: (slug: string) => { const d = nav?.destino(idTelaTvTipo2(slug)); if (nav && d) nav.irPara(d.indice); },
  };
}

function PaginaTipo2Tv({ slug, projectId }: { slug: string; projectId: string }) {
  const navegacao = navegacaoBiNaTv(useNavegacaoTv());
  return (
    <Suspense fallback={<ReportState title="Carregando página do BI…">Aguarde.</ReportState>}>
      <ConteudoPaginaBiChs001 slug={slug} projectId={projectId} navegacao={navegacao} maxIdadeDadosMs={TV_MAX_IDADE_DADOS_BI_MS} />
    </Suspense>
  );
}

/**
 * Mesma regra da rota, em TODOS os projetos do item: sem a key da tela em qualquer um deles → aviso com os projetos
 * afetados, sem montar a tela (nenhum endpoint de dados é chamado e nenhum total parcial aparece).
 */
function GuardaPermissaoTv({ tela, projetos, children }: { tela: TelaTv; projetos: PmoSelecionado[]; children: ReactNode }) {
  const perm = usePermissaoProjetos(projetos.map((p) => p.id), tela.chaves);
  if (perm.loading) return <ReportState title="Verificando permissões…">Aguarde.</ReportState>;
  if (perm.negados.length > 0)
    return <ReportState title={`Sem permissão para “${tela.label}”`} error>
      O usuário conectado nesta TV não tem permissão para esta tela em {perm.negados.length} de {projetos.length} projeto(s):{" "}
      {perm.negados.map((id) => projetos.find((p) => p.id === id)?.name || id).join(", ")}.
    </ReportState>;
  return <>{children}</>;
}

/** Uma tela com erro de renderização não derruba o rodízio: mostra o erro e o player segue para a próxima. */
class LimiteErroTv extends Component<{ tela: string; children: ReactNode }, { erro: string | null }> {
  state = { erro: null as string | null };
  static getDerivedStateFromError(e: unknown) { return { erro: e instanceof Error ? e.message : "Erro inesperado." }; }
  componentDidCatch(e: unknown, info: ErrorInfo) { console.error(`[TV] Falha ao exibir "${this.props.tela}"`, e, info.componentStack); }
  render() {
    return this.state.erro
      ? <ReportState title={`Não foi possível exibir “${this.props.tela}”`} error>{this.state.erro}</ReportState>
      : this.props.children;
  }
}
