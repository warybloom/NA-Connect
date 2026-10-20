// Cliente da administração de permissões (api/permissions/*). Toda chamada é validada no backend
// (permissions.manage + proibição de alterar o próprio acesso); aqui só transporte e mensagens.
import type { CatalogResource } from "./permissionTreeLogic";

export interface PermProfile {
  profileId: number;
  name: string;
  description: string | null;
  ativo: boolean;
  isSystem: boolean;
  isMasterAdmin: boolean;
  permissions: string[];
}

export interface PermUser {
  userId: number;
  accUserId: string | null;
  email: string;
  displayName: string | null;
  ativo: boolean;
  ultimoAcessoEm: string | null;
}

export type Effect = "ALLOW" | "DENY";
export interface PermOverride { projetoId: number | null; permissionKey: string; effect: Effect }

export interface KeyResolution {
  key: string;
  herdadoDe: string[];
  overrideEfetivo: Effect | null;
  efetivo: boolean;
  origem: string;
}

export interface UserAccess {
  user: PermUser;
  ativoNoProjeto: boolean;
  profileIds: number[];
  overrides: PermOverride[];
  projetoId: number | null;
  isMasterAdmin: boolean;
  permissoes: KeyResolution[];
}

export interface AuditEntry {
  id: number;
  dataHora: string;
  alteradoPorEmail: string | null;
  usuarioAfetadoId: number | null;
  usuarioAfetadoEmail: string | null;
  projetoId: number | null;
  alvo: string;
  referencia: string | null;
  valorAnterior: string | null;
  valorNovo: string | null;
}

export interface ProjetoNa { projetoId: number; projectIdAcc: string; nome: string; ativo: boolean }

export interface MembroProjeto {
  userId: number;
  email: string;
  nome: string | null;
  autodeskUserId: string | null;
  /** null = sem vínculo ACC registrado (só configuração manual). */
  ativoNoProjeto: boolean | null;
  ultimoAcessoEm: string | null;
  perfisDoProjeto: string[];
  perfisGlobais: string[];
  excecoes: number;
}

export interface ResumoSincronizacao {
  em: string;
  origem: string;
  listagemCompleta: boolean;
  encontrados: number;
  usuariosCriados: number;
  vinculados: number;
  reativados: number;
  desativados: number;
  perfisPadraoAtribuidos: number;
  ignoradosForaDoNa: number;
  aviso: string | null;
}

export interface MembrosDoProjeto { projeto: ProjetoNa; ultimaSincronizacao: ResumoSincronizacao | null; membros: MembroProjeto[] }

export type BulkModo = "ADD" | "REPLACE";
export type BulkStatus = "ALTERAR" | "JA_POSSUI" | "IGNORAR";
/** Vínculo interno perm.UserProjects: CRIAR (não existia), REATIVAR (estava inativo) ou null (já ativo). */
export type BulkVinculo = "CRIAR" | "REATIVAR";

export interface BulkItem {
  userId: number; usuario: string; email: string; projetoId: number; projectIdAcc: string; projeto: string;
  status: BulkStatus; motivo: string | null;
  perfisAntes: string[]; perfisAdicionados: string[]; perfisRemovidos: string[];
  overridesGravados: number; overridesRemovidos: number;
  vinculo?: BulkVinculo | null;
}

export interface BulkPreview {
  operacao: string; modo: BulkModo; usuarios: number; projetos: number; combinacoes: number;
  alterar: number; jaPossui: number; ignoradas: number; perfis: string[]; copiaPerfis: boolean; copiaOverrides: boolean;
  itens: BulkItem[]; avisos: string[];
}

export interface BulkResultado {
  sucesso: boolean; alterados: number; jaCorretos: number; ignorados: number; falhas: number; erro: string | null; plano: BulkPreview;
  vinculosCriados?: number; vinculosReativados?: number; perfisAdicionados?: number;
}

export interface BulkPerfisRequest { userIds: number[]; projectIdsAcc: string[]; profileIds: number[]; mode: BulkModo }
export interface BulkCopiaRequest {
  userId: number; sourceProjectIdAcc: string; targetProjectIdsAcc: string[]; copyProfiles: boolean; copyOverrides: boolean; mode: BulkModo;
}

export class PermissionsApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function chamar<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, {
    credentials: "include",
    ...init,
    headers: init?.body ? { "Content-Type": "application/json", ...(init.headers ?? {}) } : init?.headers,
  });
  if (!r.ok) {
    let msg = `HTTP ${r.status}`;
    try { const j = await r.json(); if (j?.message) msg = j.message; } catch { /* corpo vazio */ }
    throw new PermissionsApiError(r.status, msg);
  }
  const texto = await r.text();
  return (texto ? JSON.parse(texto) : undefined) as T;
}

/** Aplicação em lote: falha de gravação volta 500 COM o resumo (nada gravado) — devolve o resumo em vez de lançar. */
async function chamarLote(url: string, body: unknown): Promise<BulkResultado> {
  const r = await fetch(url, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const corpo: unknown = await r.json().catch(() => null);
  if (corpo && typeof corpo === "object" && "plano" in corpo && "sucesso" in corpo) return corpo as BulkResultado;
  const msg = corpo && typeof corpo === "object" && "message" in corpo ? String((corpo as { message: unknown }).message) : `HTTP ${r.status}`;
  throw new PermissionsApiError(r.status, msg);
}

const qsProjeto = (projectIdAcc: string | null) => (projectIdAcc ? `?projectId=${encodeURIComponent(projectIdAcc)}` : "");
const json = (body: unknown) => JSON.stringify(body);

export const permissionsApi = {
  catalogo: () => chamar<CatalogResource[]>("/api/permissions/catalog"),
  perfis: () => chamar<PermProfile[]>("/api/permissions/profiles"),
  criarPerfil: (name: string, description: string | null, permissions: string[]) =>
    chamar<{ profileId: number }>("/api/permissions/profiles", { method: "POST", body: json({ name, description, permissions }) }),
  duplicarPerfil: (profileId: number, name: string) =>
    chamar<{ profileId: number }>(`/api/permissions/profiles/${profileId}/duplicate`, { method: "POST", body: json({ name }) }),
  atualizarPerfil: (profileId: number, name: string, description: string | null, ativo: boolean) =>
    chamar<void>(`/api/permissions/profiles/${profileId}`, { method: "PUT", body: json({ name, description, ativo }) }),
  excluirPerfil: (profileId: number) =>
    chamar<void>(`/api/permissions/profiles/${profileId}`, { method: "DELETE" }),
  permissoesPerfil: (profileId: number, permissions: string[]) =>
    chamar<void>(`/api/permissions/profiles/${profileId}/permissions`, { method: "PUT", body: json({ permissions }) }),

  usuarios: () => chamar<PermUser[]>("/api/permissions/users"),
  cadastrarUsuario: (email: string, name: string | null) =>
    chamar<{ userId: number }>("/api/permissions/users", { method: "POST", body: json({ email, name }) }),
  /** Login N&A local (e-mail + senha). A senha só trafega nesta chamada; o hash nunca volta. */
  definirSenha: (userId: number, password: string, mustChangePassword: boolean) =>
    chamar<void>(`/api/permissions/users/${userId}/password`, { method: "PUT", body: json({ password, mustChangePassword }) }),
  acesso: (userId: number, projectIdAcc: string | null) =>
    chamar<UserAccess>(`/api/permissions/users/${userId}/access${qsProjeto(projectIdAcc)}`),
  perfisUsuario: (userId: number, projectIdAcc: string | null, profileIds: number[]) =>
    chamar<void>(`/api/permissions/users/${userId}/profiles${qsProjeto(projectIdAcc)}`, { method: "PUT", body: json({ profileIds }) }),
  overridesUsuario: (userId: number, projectIdAcc: string | null, overrides: { permissionKey: string; effect: Effect }[]) =>
    chamar<void>(`/api/permissions/users/${userId}/overrides${qsProjeto(projectIdAcc)}`, { method: "PUT", body: json({ overrides }) }),
  ativoNoProjeto: (userId: number, projectIdAcc: string, ativo: boolean) =>
    chamar<void>(`/api/permissions/users/${userId}/active${qsProjeto(projectIdAcc)}`, { method: "PUT", body: json({ ativo }) }),

  membrosDoProjeto: (projectIdAcc: string) =>
    chamar<MembrosDoProjeto>(`/api/permissions/projects/${encodeURIComponent(projectIdAcc)}/members`),
  sincronizarProjeto: (projectIdAcc: string) =>
    chamar<ResumoSincronizacao>(`/api/permissions/projects/${encodeURIComponent(projectIdAcc)}/sync-acc`, { method: "POST" }),

  lotePerfisPrevia: (req: BulkPerfisRequest) =>
    chamar<BulkPreview>("/api/permissions/bulk/profiles/preview", { method: "POST", body: json(req) }),
  lotePerfisAplicar: (req: BulkPerfisRequest) => chamarLote("/api/permissions/bulk/profiles/apply", req),
  loteCopiaPrevia: (req: BulkCopiaRequest) =>
    chamar<BulkPreview>("/api/permissions/bulk/copy-access/preview", { method: "POST", body: json(req) }),
  loteCopiaAplicar: (req: BulkCopiaRequest) => chamarLote("/api/permissions/bulk/copy-access/apply", req),

  auditoria: (top = 200) => chamar<AuditEntry[]>(`/api/permissions/audit?top=${top}`),
};
