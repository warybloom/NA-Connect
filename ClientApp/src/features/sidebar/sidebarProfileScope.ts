import { CATALOG, DEFAULT_ITEMS, validateItems, type SidebarConfiguration, type SidebarItem } from "./sidebarModel";
import type { SidebarProfileDetail, SidebarProfileSummary } from "./sidebarApi";

/** Escopo do Gerenciador: GLOBAL (legado, dbo.SidebarOfficial) ou layout independente de um perfil. */
export type SidebarScope = { kind: "global" } | { kind: "profile"; profileId: number };
export const GLOBAL_SCOPE: SidebarScope = { kind: "global" };
export const scopeKey = (scope: SidebarScope) => scope.kind === "global" ? "global" : "profile:" + scope.profileId;

/**
 * Completa em memória entradas oficiais ausentes (catálogo 121 → 168) sem reordenar,
 * renomear ou desocultar itens existentes. Mesma regra do runtime em sidebarModel.applyConfiguration.
 */
export function completeCatalogItems(source: SidebarItem[]): SidebarItem[] {
  let items = validateItems(source, false).map(n => ({ ...n }));
  const ids = new Set(items.map(n => n.id));
  for (const fresh of CATALOG.filter(n => !ids.has(n.id))) {
    const referencia = fresh.id === "page:/controle-producao/resumo-cronograma"
      ? items.find(n => n.id === "page:/controle-producao/resumo") : undefined;
    if (referencia) {
      const order = referencia.order + 1;
      items = items.map(n => n.parentId === referencia.parentId && n.order >= order ? { ...n, order: n.order + 1 } : n);
      items.push({ id: fresh.id, parentId: referencia.parentId, order, label: fresh.label, icon: fresh.icon, hidden: fresh.hidden });
    } else {
      const order = Math.max(-1, ...items.filter(n => n.parentId === fresh.parentId).map(n => n.order)) + 1;
      items.push({ id: fresh.id, parentId: fresh.parentId, order, label: fresh.label, icon: fresh.icon, hidden: fresh.hidden });
    }
    ids.add(fresh.id);
  }
  return validateItems(items);
}

/** Catálogo oficial completo e totalmente visível: ponto de partida do Master Admin sem configuração própria. */
export const fullyVisibleDefault = (): SidebarItem[] => DEFAULT_ITEMS.map(n => ({ ...n, hidden: false }));

/**
 * Rascunho inicial de um perfil:
 * - configurado: o layout salvo do próprio perfil;
 * - Master Admin sem configuração: padrão oficial totalmente visível (NÃO herda as ocultações globais);
 * - perfil comum sem configuração: a revisão global publicada (que o backend devolve), herdando suas ocultações.
 */
export function initialProfileItems(detail: SidebarProfileDetail): SidebarItem[] {
  if (!detail.configured && detail.isMasterAdmin) return fullyVisibleDefault();
  return completeCatalogItems(detail.configuration.items);
}

/**
 * Revisão esperada pelo CAS do perfil. Sem configuração própria o backend devolve a revisão GLOBAL
 * como conteúdo herdado, mas o layout do perfil ainda não existe: a primeira publicação espera 0.
 */
export const expectedProfileRevision = (detail: Pick<SidebarProfileDetail, "configured" | "configuration">) =>
  detail.configured ? detail.configuration.revision : 0;

export function profileBaseConfiguration(detail: SidebarProfileDetail): SidebarConfiguration {
  return { revision: expectedProfileRevision(detail), items: initialProfileItems(detail) };
}

/** Somente Master Admin publica o layout de um perfil Master Admin (o backend também recusa com 403). */
export const canPublishProfile = (profile: Pick<SidebarProfileSummary, "isMasterAdmin">, viewerIsMasterAdmin: boolean) =>
  !profile.isMasterAdmin || viewerIsMasterAdmin;

/** Master-only (BI/AUDITORIA) só aparece no preview quando perfil E usuário atual são Master Admin. */
export const previewAsMasterAdmin = (scope: SidebarScope, profile: Pick<SidebarProfileSummary, "isMasterAdmin"> | null, viewerIsMasterAdmin: boolean) =>
  viewerIsMasterAdmin && (scope.kind === "global" || profile?.isMasterAdmin === true);

const canonical = (items: SidebarItem[]) => JSON.stringify([...items].sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
  .map(n => [n.id, n.parentId, n.order, n.label, n.icon, n.hidden]));
export const isDirty = (base: SidebarItem[], items: SidebarItem[]) => canonical(base) !== canonical(items);

/** Rascunho guardado ao trocar de escopo. Cada chave pertence a UM escopo; nunca é aplicado a outro. */
export interface ScopeDraft { config: SidebarConfiguration; items: SidebarItem[]; token: string; detail: SidebarProfileDetail | null }
export type ScopeDrafts = Readonly<Record<string, ScopeDraft>>;

/** Guarda o rascunho apenas se houver alteração não publicada; caso contrário descarta a chave. */
export function stashDraft(drafts: ScopeDrafts, key: string, draft: ScopeDraft | null): ScopeDrafts {
  const { [key]: _previous, ...rest } = drafts;
  return draft && isDirty(draft.config.items, draft.items) ? { ...rest, [key]: draft } : rest;
}

export function takeDraft(drafts: ScopeDrafts, key: string): [ScopeDraft | null, ScopeDrafts] {
  const { [key]: draft, ...rest } = drafts;
  return [draft ?? null, rest];
}

export function scopeLabel(key: string, profiles: SidebarProfileSummary[]): string {
  if (key === "global") return "Global";
  const id = Number(key.slice("profile:".length));
  return profiles.find(p => p.profileId === id)?.name ?? "Perfil " + id;
}

/** Descrição da origem do rascunho para o cabeçalho. */
export function profileOrigin(detail: SidebarProfileDetail): string {
  if (detail.configured) return "Layout próprio · revisão " + detail.configuration.revision;
  if (detail.isMasterAdmin) return "Sem layout próprio · padrão oficial completo, sem as ocultações globais";
  return "Sem layout próprio · herda o menu global (revisão " + detail.configuration.revision + ")";
}
