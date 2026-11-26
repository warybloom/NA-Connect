// Lógica pura da seleção de projeto do Controle de Produção (fonte interna: /api/me/projects).
import type { AccItem } from "@/lib/api";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";

/** Endpoint interno: projetos ATIVOS do usuário N&A (UserProjects + dbo.Projetos). Não usa Autodesk. */
export const ME_PROJECTS_ENDPOINT = "/api/me/projects";

/**
 * Endpoint administrativo das Telas de TV: projetos N&A já sincronizados (LDP em dbo.Ldp_Unificada), sem filtro por
 * UserProjects de quem configura e sem Autodesk. Mesmo formato de /api/me/projects.
 */
export const TV_PROJECTS_ENDPOINT = "/api/tv-config/projects";

/** Contrato de GET /api/me/projects (MeProjectsController.MeProjectDto). */
export interface MeProject { projetoId: number; projectIdAcc: string; nomeProjeto: string; tipoLdp: number | null; isAtivo: boolean }

/** Converte para o mesmo formato que o seletor já renderiza (id + attributes.name), ordenado por nome. */
export function projetosInternos(lista: MeProject[]): AccItem[] {
  return lista
    .filter((p) => p.isAtivo && !!p.projectIdAcc)
    .map((p) => ({ id: p.projectIdAcc, attributes: { name: p.nomeProjeto || p.projectIdAcc } }))
    .sort((a, b) => a.attributes.name.localeCompare(b.attributes.name, "pt-BR"));
}

/**
 * Projeto inicial: o pedido (URL/projeto atual) se o usuário tiver acesso a ele; com UM único projeto autorizado,
 * esse projeto; caso contrário, nenhum (o usuário escolhe no seletor).
 */
export function projetoInicial(projetos: AccItem[], pedido: string | null | undefined): string {
  if (pedido && projetos.some((p) => p.id === pedido)) return pedido;
  return projetos.length === 1 ? projetos[0].id : "";
}

/**
 * Seleção usada pelas telas. hubId é apenas repassado (compatibilidade com CDE/links antigos): vazio NÃO impede
 * a seleção — os relatórios SQL identificam o projeto pelo Project_ID_ACC.
 */
export function montarSelecao(projetos: AccItem[], projectId: string, hubId: string): PmoSelecionado[] {
  const p = projetos.find((x) => x.id === projectId);
  return p ? [{ id: p.id, hubId, name: p.attributes.name }] : [];
}
