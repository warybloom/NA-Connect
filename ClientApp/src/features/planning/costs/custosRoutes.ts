// Custos do Projeto: grupo no sidebar do Painel de Planejamento (dados puros — usados por rotas, sidebar, guard e testes).
// Permissão experimental exclusiva de todas as telas de custos. Projeto = Project_ID_ACC atual.

export const PERMISSAO_CUSTOS = "planning.costs.analysis";

/** Trava temporária de homologação: só MasterAdmin enxerga o grupo no sidebar. */
export const podeExibirCustosDuranteHomologacao = (isMasterAdmin: boolean | undefined) => isMasterAdmin === true;

export const PERMISSOES_CUSTOS_POR_CAMINHO: Record<string,string> = {
  "/planejamento/custos/impostos": "planning.costs.taxes.view",
  "/planejamento/custos/profissionais": "planning.costs.professionals.view",
  "/planejamento/custos/softwares": "planning.costs.softwares.view",
  "/planejamento/custos/pacotes": "planning.costs.packages.view",
};

export const ROTAS_CUSTOS = {
  impostos: "/planejamento/custos/impostos",
  profissionais: "/planejamento/custos/profissionais",
  softwares: "/planejamento/custos/softwares",
  pacotes: "/planejamento/custos/pacotes",
} as const;

/** Ordem do sidebar: Impostos, Profissionais, Softwares, Pacotes. */
export const TELAS_CUSTOS: readonly { label: string; to: string }[] = [
  { label: "Impostos", to: ROTAS_CUSTOS.impostos },
  { label: "Profissionais", to: ROTAS_CUSTOS.profissionais },
  { label: "Softwares", to: ROTAS_CUSTOS.softwares },
  { label: "Pacotes", to: ROTAS_CUSTOS.pacotes },
];

/** Rotas antigas → destino atual. /planejamento/custos-do-projeto era a página única de abas. */
export const REDIRECIONAMENTOS_CUSTOS: Readonly<Record<string, string>> = {
  "/planejamento/custos-do-projeto": ROTAS_CUSTOS.profissionais,
  "/planejamento/custos": ROTAS_CUSTOS.profissionais,
};
