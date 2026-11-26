// Fonte única das rotas e permissões do módulo Plant 3D (AUDITORIA > Plant 3D). Sem imports: usado pelo sidebar,
// pela tabela de permissões de rota e pelo App.tsx. Rotas de fase são dinâmicas (uma por fase salva × 3 telas).

const R = "/plant3d";

export const PLANT_ROTAS = {
  resumo: `${R}/resumo`,
  resultados: {
    linhas: `${R}/resultados/linhas`,
    equipamentos: `${R}/resultados/equipamentos`,
    instrumentos: `${R}/resultados/instrumentos`,
    valvulas: `${R}/resultados/valvulas`,
    tieIns: `${R}/resultados/tie-ins`,
    auditoria: `${R}/resultados/auditoria`,
    consistencia: `${R}/resultados/consistencia`,
    qualidade: `${R}/resultados/qualidade`,
  },
  configuracao: {
    linhas: `${R}/configuracao/linhas`,
    equipamentos: `${R}/configuracao/equipamentos`,
    instrumentos: `${R}/configuracao/instrumentos`,
    valvulas: `${R}/configuracao/valvulas`,
    tieIns: `${R}/configuracao/tie-ins`,
    catalogo: `${R}/configuracao/catalogo-padroes`,
    criterios: `${R}/configuracao/criterios-medicao`,
    deParaTags: `${R}/configuracao/de-para-tags`,
  },
  planejamento4d: `${R}/planejamento-4d`,
  criterioMedicao: `${R}/criterio-medicao`,
  relatorios: {
    linhas: `${R}/relatorios/linhas`,
    equipamentos: `${R}/relatorios/equipamentos`,
    instrumentos: `${R}/relatorios/instrumentos`,
    valvulas: `${R}/relatorios/valvulas`,
    tieIns: `${R}/relatorios/tie-ins`,
    materiais: `${R}/relatorios/materiais`,
    suportes: `${R}/relatorios/suportes`,
    configuracao: `${R}/relatorios/configuracao`,
  },
} as const;

/** As 3 telas de cada fase, na ordem do contrato. O segmento da URL é o `slug`. */
export const TELAS_FASE = [
  { slug: "quantitativa", label: "Quantitativa" },
  { slug: "qualitativa", label: "Qualitativa" },
  { slug: "consistencia", label: "Consistência" },
] as const;
export type TelaFase = (typeof TELAS_FASE)[number]["slug"];

export const rotaFase = (phaseId: string, tela: TelaFase) =>
  `${R}/resultados/fases/${encodeURIComponent(phaseId)}/${tela}`;

/** Padrões de rota (react-router / permissionLogic) das telas de fase. */
export const PADRAO_FASE = `${R}/resultados/fases/:phaseId`;
export const padraoTelaFase = (tela: TelaFase) => `${PADRAO_FASE}/${tela}`;

/** Rotas antigas preservadas como redirecionamento. */
export const PLANT_REDIRECIONAMENTOS = {
  geral: `${R}/resultados/geral`,
  geralFase: `${R}/resultados/geral/fase/:id`,
  planejamento4dAntigo: `${R}/configuracao/planejamento-4d`,
} as const;

/** Chaves de permissão por tela (as mesmas já usadas antes + as novas telas de F3-F6). */
export const PLANT_KEYS = {
  resultados: "plant3d.results.view",
  config: "plant3d.mapping.view",
  criterios: "plant3d.measurement.view",
  schedule4d: "plant3d.schedule4d.view",
  relatorios: "plant3d.consolidated.view",
  editar: "plant3d.database.edit",
} as const;

/** Entradas de ROUTE_PERMISSIONS do módulo (ordem: específicos antes dos genéricos). */
export const PLANT_PERMISSOES_DE_ROTA: ReadonlyArray<{ pattern: string; keys: readonly string[] }> = [
  { pattern: PLANT_ROTAS.resumo, keys: [PLANT_KEYS.resultados] },
  { pattern: PLANT_REDIRECIONAMENTOS.geral, keys: [PLANT_KEYS.resultados] },
  { pattern: PLANT_REDIRECIONAMENTOS.geralFase, keys: [PLANT_KEYS.resultados] },
  ...Object.values(PLANT_ROTAS.resultados).map((pattern) => ({ pattern, keys: [PLANT_KEYS.resultados] })),
  ...TELAS_FASE.map((t) => ({ pattern: padraoTelaFase(t.slug), keys: [PLANT_KEYS.resultados] })),
  ...Object.values(PLANT_ROTAS.configuracao).map((pattern) => ({
    pattern, keys: [pattern === PLANT_ROTAS.configuracao.criterios ? PLANT_KEYS.criterios : PLANT_KEYS.config],
  })),
  { pattern: PLANT_ROTAS.planejamento4d, keys: [PLANT_KEYS.schedule4d] },
  { pattern: PLANT_REDIRECIONAMENTOS.planejamento4dAntigo, keys: [PLANT_KEYS.schedule4d] },
  { pattern: PLANT_ROTAS.criterioMedicao, keys: [PLANT_KEYS.criterios] },
  ...Object.values(PLANT_ROTAS.relatorios).map((pattern) => ({ pattern, keys: [PLANT_KEYS.relatorios] })),
];
