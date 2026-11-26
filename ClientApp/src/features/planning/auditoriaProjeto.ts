// Auditoria do Projeto (LDP × Timesheet) — tela do Painel de Planejamento (dados puros: rota, sidebar, guard, testes).
// Permissão: as MESMAS keys que o backend já exige em /api/production-audit (ProjectAccess + PermissionService):
// nenhuma key nova. A rota antiga /controle-producao/auditoria redireciona para cá.

export const ROTA_AUDITORIA_PROJETO = "/planejamento/auditoria-projeto";
export const ROTA_ANTIGA_AUDITORIA_PROJETO = "/controle-producao/auditoria";
export const PERMISSOES_AUDITORIA_PROJETO: readonly string[] = ["production.managerial.hh-custo.view", "reports.gestao.view"];
