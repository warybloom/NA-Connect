// Guarda de rota: acesso direto por URL a uma rota sem permissão cai em "Acesso negado" (a API também
// responderia 403 — isto é UX). Rota não mapeada em ROUTE_PERMISSIONS = bloqueada (padrão negado).
import type { ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { ShieldOff } from "lucide-react";
import { usePermissions, useProjectPermissions } from "./PermissionProvider";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { ROTAS_INICIAIS, criarCan, decisaoDaRota, rotaPermitida } from "./permissionLogic";

export function AcessoNegado({ detalhe }: { detalhe?: string }) {
  return (
    <div className="flex min-h-[50vh] flex-col items-center justify-center gap-3 text-center">
      <ShieldOff className="h-10 w-10 text-muted-foreground" />
      <h2 className="text-lg font-semibold text-foreground">Página indisponível</h2>
      <p className="max-w-md text-sm text-muted-foreground">
        {detalhe ?? "Não foi possível abrir esta página."}
      </p>
    </div>
  );
}

export default function PermissionRouteGuard({ children }: { children?: ReactNode }) {
  const { loading: loadingGlobal, can: anyProjectCan, effective, error } = usePermissions();
  const { projetoAtual } = useProjetoAtual();
  const { loading: loadingProjeto, can: projectCan } = useProjectPermissions(projetoAtual?.projectId);
  const location = useLocation();
  if (!loadingGlobal && error) return <div className="min-h-[45vh] flex items-center justify-center text-sm text-muted-foreground">Não foi possível verificar seu acesso. Tente atualizar a página.</div>;
  // Sem projeto selecionado, nunca aproveitar anyProject para abrir uma tela
  // operacional por URL direta. Primeiro selecionar um projeto autorizado.
  const exigeProjeto = /^(\/controle-producao|\/planejamento(?:\/|$)|\/qualidade(?:\/|$)|\/cde(?:\/|$)|\/modelo-3d|\/modelo-federado|\/projects(?:\/|$)|\/projetos(?:\/|$)|\/configuracao-ldp|\/relatorios(?:\/|$)|\/extracoes-engenharia)/.test(location.pathname);
  // Exceção APENAS para a tela de configuração do Resumo TV:
  // abre o seletor de projeto sem mandar para a Home; permissão administrativa
  // continua exigida pela guarda e depois pelo ControleProducaoPage/API.
  const tvSemProjeto = location.pathname === "/controle-producao/resumo-tv-teste";
  if (!projetoAtual?.projectId && exigeProjeto && !tvSemProjeto)
    return <Navigate to="/" replace />;
  // Sem projeto, apenas permissões globais; Início permite selecionar um projeto autorizado.
  const globalOnlyCan = criarCan(effective, "project");
  const can = projetoAtual?.projectId ? projectCan : location.pathname === "/dashboard" ? anyProjectCan : globalOnlyCan;
  const decisao = decisaoDaRota(loadingGlobal || loadingProjeto, location.pathname, location.search, can);
  if (decisao === "carregando") {
    return (
      <div className="flex min-h-[40vh] items-center justify-center text-sm text-muted-foreground">Carregando permissões…</div>
    );
  }
  if (decisao === "negado") return <Navigate to="/" replace />;
  return <>{children ?? <Outlet />}</>;
}

/** "/" → primeira área permitida (Início, se houver). Sem nenhuma: Acesso negado. */
export function RotaInicialPermitida() {
  const { loading: globalLoading, can: anyProjectCan } = usePermissions();
  const { projetoAtual } = useProjetoAtual();
  const { loading: projectLoading, can: projectCan } = useProjectPermissions(projetoAtual?.projectId);
  if (globalLoading || projectLoading) return <div className="flex min-h-[40vh] items-center justify-center text-sm text-muted-foreground">Verificando acesso…</div>;
  const destino = projetoAtual?.projectId
    ? ROTAS_INICIAIS.find((r) => rotaPermitida(r, "", projectCan))
    : (anyProjectCan("home.view") ? "/dashboard"
      : anyProjectCan("settings.tv.configure") ? "/configuracoes/telas-tv" : undefined);
  // Não usar o privilégio de outro projeto para abrir Produção nesta unidade.
  return destino ? <Navigate to={destino} replace /> : <AcessoNegado detalhe="Nenhuma página disponível para esta conta." />;
}
