// Conteúdo de uma tela do Controle de Produção a partir do seu ProductionPage (productionRoutes.ts) — ÚNICO resolvedor
// página → componente, usado pela rota /controle-producao/<slug> (ControleProducaoPage) e pelo player da TV
// (tvScreens.tsx). Nenhum cálculo aqui: cada tela existente recebe a seleção de projetos de quem chama.
// Permissão e seleção de projeto são responsabilidade de quem chama (rota: projeto da URL/contexto; TV: projetos salvos).
import ViewCurvaS from "@/components/pmo/ViewCurvaS";
import ViewTimesheet from "@/components/pmo/ViewTimesheet";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import CdeDocumentList from "@/pages/cde/CdeDocumentList";
import ProductionScreen from "./ProductionScreen";
import ResumoProducaoPage from "./analytics/ResumoProducaoPage";
import ResumoTvTestePage from "./analytics/ResumoTvTestePage";
import GestaoCustoHhPage from "./analytics/GestaoCustoHhPage";
import GestaoVistaJamilePage from "./analytics/GestaoVistaJamilePage";
import GestaoVistaDisciplinasPage from "./analytics/GestaoVistaDisciplinasPage";
import AnaliseFinanceiraPage from "./analytics/AnaliseFinanceiraPage";
import LancamentosPessoasPage from "./lancamentos/LancamentosPessoasPage";
import LancamentosDisciplinasPage from "./lancamentos/LancamentosDisciplinasPage";
import LancamentosDocumentosPage from "./lancamentos/LancamentosDocumentosPage";
import type { ProductionPage } from "./productionRoutes";

/**
 * Páginas implementadas (com componente). "em-desenvolvimento" não tem conteúdo (quem chama exibe o aviso) e "resumo-lab"
 * é um tipo reservado sem folha nem tela roteável.
 */
export type ProductionPageImplementada = Exclude<ProductionPage, { tipo: "em-desenvolvimento" } | { tipo: "resumo-lab" }>;

export const paginaImplementada = (p: ProductionPage): p is ProductionPageImplementada => p.tipo !== "em-desenvolvimento" && p.tipo !== "resumo-lab";

export function ConteudoPaginaProducao({ page, selection }: { page: ProductionPageImplementada; selection: PmoSelecionado[] }) {
  const principal = selection[0];
  switch (page.tipo) {
    case "dashboard": return <ProductionScreen screenId={page.screenId} selection={selection} />;
    case "curva-s": return <ViewCurvaS selection={selection} />;
    case "resumo": return <ResumoProducaoPage selection={selection} />;
    case "jamile": return <GestaoVistaJamilePage selection={selection} screen={page.screen} />;
    case "jamile-disciplina": return <GestaoVistaDisciplinasPage selection={selection} mode={page.mode} />;
    case "resumo-tv-teste": return <ResumoTvTestePage selection={selection} />;
    case "gestao-hh-custo": return <GestaoCustoHhPage selection={selection} />;
    case "analise-financeira": return <AnaliseFinanceiraPage visao={page.visao} selection={selection} />;
    case "lista-documentos":
      return <CdeDocumentList key={`${principal?.hubId ?? ""}|${principal?.id ?? ""}`} hubId={principal?.hubId ?? ""}
        projectId={principal?.id ?? ""} projectName={principal?.name || principal?.id || ""} />;
    case "lancamentos":
      return page.visao === "pessoas" ? <LancamentosPessoasPage selection={selection} />
        : page.visao === "disciplinas" ? <LancamentosDisciplinasPage selection={selection} />
        : <LancamentosDocumentosPage selection={selection} />;
    case "hh-custo": return <ViewTimesheet hub={principal?.hubId ?? ""} project={principal?.id ?? ""} selection={selection} />;
  }
}
