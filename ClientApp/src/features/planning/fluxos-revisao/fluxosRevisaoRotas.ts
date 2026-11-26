// Painel de Planejamento → Fluxos de Revisão: rota e permissões (sem dependências; usado pelo menu, pelas regras de rota e pela página).
export const ROTA_FLUXOS_REVISAO = "/planejamento/fluxos-de-revisao";
/** Rota antiga (Configurações) — só redireciona. */
export const ROTA_FLUXOS_REVISAO_ANTIGA = "/configuracoes/fluxos-revisao";
/** Visualização é independente; criar/editar/associar preserva a permissão de edição já existente. */
export const PERMISSOES_FLUXOS_REVISAO = { ver: "planning.review-flows.view", editar: "planning.coordination.edit" } as const;
