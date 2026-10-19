// =============================================================================================
// Lógica PURA de permissões do frontend. O backend (PermissionService/PermissionResolver) já entrega as
// permissões EFETIVAS resolvidas — aqui NÃO se reimplementa precedência (MasterAdmin > DENY > ALLOW > perfis):
// can(key) só consulta o conjunto recebido. Ocultar/bloquear no frontend é UX; a proteção real é o backend.
// =============================================================================================

import { ROTAS_PRODUCAO, TODAS_KEYS_PRODUCAO } from "../production/productionRoutes";
import { ROTAS_BI_CHS001 } from "../production/biChs001/biChs001Paginas";
import { PERMISSOES_CUSTOS_POR_CAMINHO, REDIRECIONAMENTOS_CUSTOS, ROTAS_CUSTOS } from "../planning/costs/custosRoutes";
import { PERMISSOES_AUDITORIA_PROJETO, ROTA_AUDITORIA_PROJETO } from "../planning/auditoriaProjeto";
import { TEMPLATE_TYPES, rotaTemplate } from "../settings/templateTypes";

/** Resposta de GET /api/permissions/me (EffectivePermissions no backend). */
export interface EffectivePermissions {
  enforced: boolean;
  schemaInstalled: boolean;
  isMasterAdmin: boolean;
  email: string | null;
  projectIdAcc: string | null;
  /** Efetivas no escopo pedido (global + projeto informado). */
  permissions: string[];
  /** União global + qualquer projeto: usada para decidir se o MENU/ROTA aparece. */
  anyProject: string[];
}

export type Can = (key: string | readonly string[] | null | undefined) => boolean;

/**
 * can(key): true se a key (ou QUALQUER uma da lista) foi concedida. Key ausente (null) = sem exigência.
 *
 * SAFE MODE: o frontend só NEGA quando o backend respondeu de forma válida com enforced=true e o usuário não é
 * MasterAdmin. Sem resposta (carregando, erro de rede/API, resposta inválida) = "desconhecido" → NÃO bloqueia
 * (ocultar/bloquear na tela é UX; quem protege de fato é o [RequirePermission] no backend, que responde 403).
 * enforced=false = comportamento atual: tudo liberado.
 */
export const criarCan = (eff: EffectivePermissions | null, escopo: "project" | "any" = "project"): Can => {
  // Sem resposta validada: nunca exibir nem permitir acesso a telas restritas.
  if (!eff) return (key) => key == null;
  if (!eff.enforced || eff.isMasterAdmin) return () => true;
  const set = new Set(escopo === "any" ? eff.anyProject : eff.permissions);
  return (key) => {
    if (key == null) return true;
    const lista = typeof key === "string" ? [key] : key;
    return lista.some((k) => set.has(k));
  };
};

/** Resposta válida de /api/permissions/me? (o Vite, sem proxy, devolvia index.html com 200). */
export const ehEffectivePermissions = (v: unknown): v is EffectivePermissions => {
  const o = v as Partial<EffectivePermissions> | null;
  return !!o && typeof o === "object" && typeof o.enforced === "boolean" && typeof o.isMasterAdmin === "boolean"
    && Array.isArray(o.permissions) && Array.isArray(o.anyProject);
};

/** Nó genérico de navegação com key opcional; grupo sem key aparece se ALGUM filho aparecer. */
export interface NodoComPermissao<T> { permissionKey?: string | readonly string[]; children?: T[] }

export function filtrarPorPermissao<T extends NodoComPermissao<T>>(nodes: readonly T[], can: Can): T[] {
  const out: T[] = [];
  for (const node of nodes) {
    if (node.children && node.children.length > 0) {
      if (node.permissionKey != null && !can(node.permissionKey)) continue;
      const filhos = filtrarPorPermissao(node.children, can);
      if (filhos.length > 0) out.push({ ...node, children: filhos });
      continue;
    }
    if (node.permissionKey != null && can(node.permissionKey)) out.push(node);
  }
  return out;
}

// ------------------------------------------------------------------------------------------------
// Rotas: toda rota autenticada do App.tsx precisa de uma entrada aqui (teste de contrato garante).
// Ordem importa: o primeiro padrão que casar vence (mais específicos primeiro). `null` = sem exigência
// adicional além da sessão (redirecionamentos e a página de acesso negado).
// ------------------------------------------------------------------------------------------------
export interface RotaPermissao { pattern: string; keys: readonly string[] | null; tab?: Record<string, readonly string[]> }

// Controle de Produção e Templates: rotas geradas dos registries (fonte única) — entradas explícitas vêm ANTES dos
// padrões genéricos ("/controle-producao/*", "/configuracoes/templates/:tipo").

export const ROUTE_PERMISSIONS: readonly RotaPermissao[] = [
  { pattern: "/", keys: null },
  { pattern: "/admin", keys: null },
  { pattern: "/dashboard", keys: ["home.view"] },

  // Painel de Planejamento
  { pattern: "/planejamento/normas-de-coordenacao", keys: ["planning.coordination.view"] },
  { pattern: "/planejamento/programacao-semanal", keys: ["planning.weekly.current.view"] }, // redireciona para /vigente
  { pattern: "/planejamento/programacao-semanal/vigente", keys: ["planning.weekly.current.view"] },
  { pattern: "/planejamento/programacao-semanal/semana-anterior", keys: ["planning.weekly.previous.view"] },
  { pattern: "/planejamento/programacao-semanal/histograma", keys: ["planning.weekly.histogram.view"] },
  { pattern: "/planejamento/painel-do-planejador", keys: ["planning.planner.view"] },
  // Auditoria do Projeto (LDP × Timesheet): mesmas keys do backend /api/production-audit.
  { pattern: ROTA_AUDITORIA_PROJETO, keys: PERMISSOES_AUDITORIA_PROJETO },
  // Custos do Projeto (grupo): 4 telas + rotas antigas que redirecionam. Mesma key de /api/project-costs.
  ...Object.values(ROTAS_CUSTOS).map((pattern) => ({ pattern, keys: [PERMISSOES_CUSTOS_POR_CAMINHO[pattern]] as readonly string[] })),
  ...Object.entries(REDIRECIONAMENTOS_CUSTOS).map(([pattern,destination]) => ({ pattern, keys: [PERMISSOES_CUSTOS_POR_CAMINHO[destination]] as readonly string[] })),
  // Configuração do Projeto: cadastro de projetos (antes /painel) + Logo do Projeto.
  { pattern: "/planejamento/configuracao-do-projeto", keys: ["settings.projects.view"] },

  // Qualidade
  { pattern: "/qualidade/auditoria", keys: ["quality.audit.view"] },
  { pattern: "/qualidade/hh-sem-documento", keys: ["quality.unassigned.view", "quality.outside-ldp.view"] },
  { pattern: "/qualidade/classificacao-profissionais", keys: ["quality.professionals.view"] },
  { pattern: "/qualidade/comparativo-hh-custos", keys: ["quality.comparison.view"] },
  { pattern: "/qualidade/auditoria-divergencias", keys: ["quality.divergences.view"] },
  { pattern: "/qualidade/configuracao-avanco-fisico", keys: ["quality.progress.view"] },
  { pattern: "/qualidade/auditoria-documentos", keys: ["quality.documents.view"] },
  { pattern: "/qualidade/configuracao-pastas-documentos", keys: ["quality.folders.view"] },
  { pattern: "/qualidade/auditoria-ldp-acc", keys: ["quality.ldp-acc.view"] },
  { pattern: "/qualidade/auditoria-disciplinas", keys: ["quality.disciplines.view"] },
  // Rotas antigas mantidas apenas para redirecionamento.
  { pattern: "/planejamento/auditoria-qualidade-documentos", keys: ["quality.documents.view"] },
  { pattern: "/planejamento/pastas-documentos", keys: ["quality.folders.view"] },

  // Controle de Produção (Gerencial / Operacional)
  { pattern: "/controle-producao", keys: TODAS_KEYS_PRODUCAO }, // redireciona para a primeira tela permitida
  ...ROTAS_PRODUCAO.map((r) => ({ pattern: r.rota, keys: r.keys })),
  // BI — CHS001 (Original): uma rota por página do PBIX, com as keys da própria página (biChs001Paginas.ts).
  ...ROTAS_BI_CHS001.map((r) => ({ pattern: r.rota, keys: r.keys })),
  // Rotas antigas com variante (/gerencial|/operacional) redirecionam; tela inexistente → "não encontrada".
  { pattern: "/controle-producao/*", keys: TODAS_KEYS_PRODUCAO },

  // CDE
  { pattern: "/cde", keys: ["cde.documents.view"] },
  { pattern: "/cde/envio-documentos", keys: ["cde.docs.upload"] },
  { pattern: "/cde/documentos", keys: ["cde.docs.view"] },
  { pattern: "/cde/:hubId/:projectId", keys: ["cde.documents.view"] },
  { pattern: "/modelo-3d", keys: ["cde.model3d.view"] },
  { pattern: "/modelo-federado", keys: ["cde.model3d.view"] },
  { pattern: "/planejamento-4d", keys: ["cde.planning4d.view"] },
  { pattern: "/orcamento-5d", keys: ["cde.budget5d.view"] },
  { pattern: "/projects/:hubId", keys: ["cde.docs.view"] },
  { pattern: "/projects/:hubId/:projectId/:folderId", keys: ["cde.docs.view"] },
  { pattern: "/projetos/:projectId/conteudo/:folderId", keys: ["cde.docs.view"] },

  // Configurações
  { pattern: "/configuracao-ldp", keys: ["settings.ldp.view"] },
  { pattern: "/configuracao-ldp/template", keys: ["settings.ldp.template.view"] },
  { pattern: "/configuracao-ldp/cronograma", keys: ["settings.ldp.cronograma.view"] },
  { pattern: "/configuracao-ldp/espelho", keys: ["settings.ldp.espelho.view"] },
  { pattern: "/configuracao-ldp/excel", keys: ["settings.ldp.espelho.view"] },
  { pattern: "/configuracao-ldp/controle", keys: ["settings.ldp.controle.view"] },
  { pattern: "/painel", keys: ["settings.projects.view"] }, // redireciona à Configuração do Projeto
  { pattern: "/automacao/configuracao-robo", keys: ["robot.view"] },
  { pattern: "/configuracoes/telas-tv", keys: ["settings.tv.configure"] },
  { pattern: "/configuracoes/templates/auditoria-documental", keys: ["settings.ocr.view"] },
  ...TEMPLATE_TYPES.map((t) => ({ pattern: rotaTemplate(t.tipo), keys: [t.permissionKey] as readonly string[] })),
  { pattern: "/configuracoes/templates/:tipo", keys: TEMPLATE_TYPES.map((t) => t.permissionKey) },
  { pattern: "/configuracoes/sidebar", keys: ["permissions.manage"] },
  { pattern: "/configuracoes/permissoes", keys: ["permissions.manage"] },
  { pattern: "/configuracoes/identidade-visual", keys: ["settings.projects.view"] },
  { pattern: "/configuracoes/normalizador-siglas", keys: ["settings.projects.view"] },

  // Player da Gestão à Vista (TV) — acessado por Configuração de Telas para TV; não é mais item raiz do sidebar.
  { pattern: "/gestao-a-vista", keys: ["tv.view", "settings.tv.configure"] },

  // Relatórios → Extrações de Engenharia (demonstração visual; integração futura com o DashboardBIM).
  { pattern: "/extracoes-engenharia", keys: ["reports.view"] },

  // Rotas LEGADAS: continuam funcionando (fora do sidebar principal).
  { pattern: "/relatorios", keys: ["reports.view"] },
  { pattern: "/lancamentos", keys: ["reports.lancamentos.view"] },
  { pattern: "/agendamento", keys: ["robot.schedule.view"] },
  {
    pattern: "/plant3d",
    keys: ["plant3d.view"],
    tab: {
      "4d-schedule": ["plant3d.schedule4d.view"], dashboard: ["plant3d.results.view"], reports: ["plant3d.consolidated.view"],
      rules: ["plant3d.measurement.view"], database: ["plant3d.database.view"], mapping: ["plant3d.mapping.view"],
      "tag-rules": ["plant3d.naming.view"], "gen-reports": ["plant3d.gen.view"],
    },
  },
  { pattern: "/auditoria-3d", keys: ["plant3d.view"] },
  { pattern: "/templates", keys: ["settings.quality-template.view"] },
  { pattern: "/configurar-ldp", keys: ["settings.ldp-legacy.view"] },
  { pattern: "/ocr", keys: ["settings.ocr.view"] },
  { pattern: "/master-admin", keys: ["system.tenants.view"] },
];

const casa = (pattern: string, pathname: string): boolean => {
  const a = pattern.split("/").filter(Boolean);
  const b = pathname.split("/").filter(Boolean);
  // "*" final casa o restante do caminho (1+ segmentos), como no react-router.
  if (a[a.length - 1] === "*") {
    const prefixo = a.slice(0, -1);
    return b.length > prefixo.length && prefixo.every((seg, i) => seg.startsWith(":") || seg === b[i]);
  }
  return a.length === b.length && a.every((seg, i) => seg.startsWith(":") || seg === b[i]);
};

/**
 * Keys exigidas pela rota (e pela aba ?tab= quando houver). `undefined` = rota NÃO mapeada → bloqueada
 * (padrão negado); `null` = só sessão.
 */
export const permissaoDaRota = (pathname: string, search = ""): readonly string[] | null | undefined => {
  const normalizado = pathname.length > 1 ? pathname.replace(/\/+$/, "") : pathname;
  const rota = ROUTE_PERMISSIONS.find((r) => casa(r.pattern, normalizado));
  if (!rota) return undefined;
  const tab = new URLSearchParams(search).get("tab");
  if (rota.tab && tab && rota.tab[tab]) return rota.tab[tab];
  return rota.keys;
};

const ROTA_NAO_MAPEADA = "__rota-nao-mapeada__";

export const rotaPermitida = (pathname: string, search: string, can: Can): boolean => {
  const keys = permissaoDaRota(pathname, search);
  // Rota não mapeada: bloqueada só quando o sistema aplica permissões (key sentinela nunca é concedida);
  // em safe mode (enforced=false / desconhecido) can() libera tudo.
  if (keys === undefined) return can(ROTA_NAO_MAPEADA);
  return keys === null || can(keys);
};

/** Ordem de preferência da página inicial ("/"). */
export const ROTAS_INICIAIS: readonly string[] = [
  "/dashboard", "/controle-producao", "/planejamento/painel-do-planejador", "/planejamento/programacao-semanal/vigente",
  "/planejamento/normas-de-coordenacao", "/configuracao-ldp/template", "/relatorios", "/automacao/configuracao-robo", "/configuracoes/permissoes",
];

/** Decisão do PermissionRouteGuard. Carregando NUNCA é "negado". */
export const decisaoDaRota = (loading: boolean, pathname: string, search: string, can: Can): "carregando" | "permitido" | "negado" =>
  loading ? "carregando" : rotaPermitida(pathname, search, can) ? "permitido" : "negado";

/**
 * Entrada no AMBIENTE DO PROJETO (depois de escolher um card na Home): primeira área permitida, em ordem de uso.
 * A Home (/dashboard) não entra — ela é a tela de escolha.
 */
export const ROTAS_ENTRADA_PROJETO: readonly string[] = [
  "/controle-producao", "/planejamento/painel-do-planejador", "/planejamento/programacao-semanal/vigente",
  "/planejamento/normas-de-coordenacao", "/planejamento/configuracao-do-projeto", "/configuracao-ldp/template", "/relatorios", "/modelo-3d", "/configuracoes/permissoes",
];

export const rotaDeEntradaDoProjeto = (can: Can): string | undefined => ROTAS_ENTRADA_PROJETO.find((r) => rotaPermitida(r, "", can));
