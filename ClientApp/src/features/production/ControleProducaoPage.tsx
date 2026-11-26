// CONTROLE DE PRODUÇÃO — cada tela é uma ENTRADA PRÓPRIA do sidebar, direto abaixo de "Controle de Produção":
//   /controle-producao/<tela>?hub=<hubId>&projeto=<projectId>
// Sem Gerencial/Operacional: quem vê cada tela é decidido pela permissão (productionRoutes.ts). As rotas antigas com
// variante redirecionam. O projeto vem, nesta ordem, da URL → do PROJETO ATUAL (Home/contexto) → do seletor.
import { lazy, Suspense, useEffect, useMemo } from "react";
import { Navigate, useLocation, useSearchParams } from "react-router-dom";
import { Construction } from "lucide-react";

import { ReportState } from "@/components/reports/ReportComponents";
import { usePageHeader } from "@/components/PageHeaderContext";
import { usePermissions, useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { AcessoNegado } from "@/features/permissions/PermissionRouteGuard";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import ProductionProjectSelector, { useProductionSelectionInterna } from "./ProductionProjectSelector";
import SeletorProjetosAnalise from "@/features/project/SeletorProjetosAnalise";
import { paraSelecao } from "@/features/project/projetoAnalise";
import DocumentosPorStatus from "./analytics/DocumentosPorStatus";
import { ConteudoPaginaProducao, paginaImplementada } from "./ProductionPageContent";
import { ehStatusDocumento } from "./analytics/resumoLogic";
import { BI_CHS001_PAGINAS, BI_CHS001_PREFIXO, paginaBiPorSlugProducao, rotaBiChs001 } from "./biChs001/biChs001Paginas";
import { encontrarFolha, folhasProducao, rotaExternaRedirecionada, rotaLegada, rotaProducao, semVarianteLegada, slugRedirecionado, type ProductionLeaf } from "./productionRoutes";

// BI — CHS001 (Original): módulo próprio, carregado sob demanda (layouts/imagens só baixam ao abrir o BI).
const BiChs001Page = lazy(() => import("./biChs001/BiChs001Page"));

function EmDesenvolvimento({ titulo, descricao }: { titulo: string; descricao: string }) {
  return (
    <div className="flex min-h-[40vh] items-center justify-center">
      <div className="w-full max-w-lg rounded-xl border border-border bg-card/60 p-8 text-center">
        <Construction className="mx-auto mb-3 h-8 w-8 text-amber-500" />
        <h2 className="text-lg font-bold">{titulo}</h2>
        <span className="mt-2 inline-block rounded-full border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-[10px] font-black uppercase tracking-widest text-amber-500">
          Em desenvolvimento
        </span>
        <p className="mt-3 text-sm text-muted-foreground">{descricao}</p>
      </div>
    </div>
  );
}

function TelaProducao({ folha }: { folha: ProductionLeaf }) {
  const [params, setParams] = useSearchParams();
  const { projetoAtual, definirProjeto, projetosAnalise } = useProjetoAtual();
  // URL (link compartilhado/recarga) prevalece; sem URL, abre no PROJETO ATUAL escolhido na Home.
  // Lista INTERNA (/api/me/projects): o projeto atual (Project_ID_ACC) é reconhecido mesmo sem Autodesk/hubId.
  const s = useProductionSelectionInterna({
    hubId: params.get("hub") ?? projetoAtual?.hubId ?? null,
    projectId: params.get("projeto") ?? (params.get("hub") ? null : projetoAtual?.projectId ?? null),
  });

  const projetoSel = s.selection[0];
  // Mantém conta/projeto na URL e atualiza o PROJETO ATUAL quando o usuário troca de projeto nesta tela.
  useEffect(() => {
    const proximos = new URLSearchParams(params);
    let mudou = false;
    for (const [k, v] of Object.entries({ hub: s.hubId, projeto: s.projectId })) {
      if (v && proximos.get(k) !== v) { proximos.set(k, v); mudou = true; }
      if (!v && proximos.has(k)) { proximos.delete(k); mudou = true; }
    }
    if (mudou) setParams(proximos, { replace: true });
    if (s.projectId) definirProjeto({ hubId: s.hubId, projectId: s.projectId, nome: projetoSel?.name });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [s.hubId, s.projectId, projetoSel?.name]);

  // Drill-down do Resumo: Lista de Documentos filtrada por status (?status=), nos projetos da análise quando analise=1.
  const statusFiltro = folha.page.tipo === "lista-documentos" ? params.get("status") : null;
  const drillAnalitico = ehStatusDocumento(statusFiltro) && params.get("analise") === "1";
  const analitica = !!folha.analitica || drillAnalitico;

  // Telas ANALÍTICAS usam os projetos da análise (padrão = [principal]); as operacionais, só o principal.
  // Só vale enquanto o projeto da tela é o principal do contexto (ao trocar, o contexto já volta a [principal]).
  const selecaoAnalise = useMemo(
    () => (projetoAtual?.projectId === s.projectId && projetosAnalise.length > 0 && s.selection.length > 0 ? paraSelecao(projetosAnalise) : s.selection),
    [projetosAnalise, s.selection, s.projectId, projetoAtual?.projectId]);
  const selecao = analitica ? selecaoAnalise : s.selection;

  usePageHeader(
    projetoSel ? projetoSel.name : "Controle de Produção — selecione um projeto",
    <div className="flex items-center gap-2">
      {analitica && projetoAtual && <SeletorProjetosAnalise keys={folha.permissionKeys} />}
      <ProductionProjectSelector s={s} />
    </div>,
    [projetoSel?.id, projetoSel?.name, s.projectId, s.projects, s.loadingProjects, s.loadingHubs, s.hubId, analitica, projetoAtual?.projectId, folha.slug]
  );

  // Permissão da tela NO PROJETO selecionado (o backend valida as mesmas keys em cada endpoint).
  const perm = useProjectPermissions(s.projectId || null);

  let conteudo;
  if (folha.page.tipo === "em-desenvolvimento") conteudo = <EmDesenvolvimento titulo={folha.label} descricao={folha.page.descricao} />;
  else if (s.selection.length === 0) conteudo = <ReportState title="Nenhum projeto selecionado">Selecione um projeto no topo da página.</ReportState>;
  else if (perm.loading) conteudo = <ReportState title="Carregando permissões do projeto…">Aguarde.</ReportState>;
  else if (!perm.can(folha.permissionKeys)) conteudo = <AcessoNegado detalhe={`Você não tem permissão para "${folha.label}" neste projeto.`} />;
  else if (folha.page.tipo === "lista-documentos" && ehStatusDocumento(statusFiltro))
    conteudo = <DocumentosPorStatus status={statusFiltro} selection={selecao} />;
  // Resolvedor único página → componente (o mesmo do player da TV). Telas não analíticas: selecao = s.selection.
  else if (paginaImplementada(folha.page)) conteudo = <ConteudoPaginaProducao page={folha.page} selection={selecao} />;
  else conteudo = <EmDesenvolvimento titulo={folha.label} descricao="Tela ainda não implementada." />;

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
        <span>Controle de Produção</span><span>›</span><span className="text-foreground">{folha.label}</span>
      </div>
      {s.error && <p className="text-sm text-rose-400">{s.error}</p>}
      {conteudo}
    </div>
  );
}

/** /controle-producao (sem tela): URL antiga ?tela= → rota nova; senão, primeira tela permitida. */
export function ControleProducaoIndex() {
  const [params] = useSearchParams();
  const { can } = usePermissions();
  const escopo = new URLSearchParams();
  if (params.get("hub")) escopo.set("hub", params.get("hub")!);
  if (params.get("projeto")) escopo.set("projeto", params.get("projeto")!);
  const qs = escopo.toString() ? `?${escopo}` : "";
  const legada = rotaLegada(params.get("tela"));
  if (legada) return <Navigate to={legada + qs} replace />;
  const permitidas = folhasProducao().filter((f) => !f.foraDoMenu && can(f.permissionKeys));
  // A abertura de Controle de Produção sempre prioriza Resumo quando permitido.
  const primeira = permitidas.find((f) => f.slug === "resumo") ?? permitidas[0];
  return primeira ? <Navigate to={rotaProducao(primeira.slug) + qs} replace /> : <AcessoNegado />;
}

export default function ControleProducaoPage() {
  const location = useLocation();
  // Rotas antigas com variante (/controle-producao/gerencial|operacional/...) → rota plana.
  const plana = semVarianteLegada(location.pathname);
  const slug = useMemo(() => location.pathname.split("/").slice(2).filter(Boolean).join("/"), [location.pathname]);
  if (plana) return <Navigate to={plana + location.search} replace />;
  const externa = rotaExternaRedirecionada(slug);
  if (externa) return <Navigate to={externa} replace />;
  const destino = slugRedirecionado(slug);
  if (destino) return <Navigate to={rotaProducao(destino) + location.search} replace />;
  // BI — CHS001 (Original): /controle-producao/bi-chs001/<pagina> — registro próprio (fora de PRODUCTION_NAV/TV).
  if (slug === BI_CHS001_PREFIXO) return <Navigate to={rotaBiChs001(BI_CHS001_PAGINAS[0].slug) + location.search} replace />;
  const paginaBi = paginaBiPorSlugProducao(slug);
  if (paginaBi)
    return (
      <Suspense fallback={<ReportState title="Carregando BI CHS001…">Aguarde.</ReportState>}>
        <BiChs001Page key={slug} pagina={paginaBi} />
      </Suspense>
    );
  const folha = encontrarFolha(slug);
  if (!folha)
    return <ReportState title="Tela não encontrada" error>O endereço "{location.pathname}" não corresponde a nenhuma tela do Controle de Produção.</ReportState>;
  return (
    <div className="flex h-full flex-col animate-enter">
      {/* key: trocar de tela remonta (filtros e página zerados). */}
      <TelaProducao key={slug} folha={folha} />
    </div>
  );
}
