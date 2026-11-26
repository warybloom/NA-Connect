import { AlertTriangle, Globe2, ShieldCheck, UsersRound } from "lucide-react";
import type { SidebarApiError, SidebarProfileDetail, SidebarProfileSummary } from "./sidebarApi";
import { profileOrigin, scopeKey, type SidebarScope } from "./sidebarProfileScope";

interface Props {
  scope: SidebarScope;
  profiles: SidebarProfileSummary[];
  profilesLoading: boolean;
  profilesError: SidebarApiError | null;
  detail: SidebarProfileDetail | null;
  viewerIsMasterAdmin: boolean;
  pendingDrafts: { key: string; label: string }[];
  disabled?: boolean;
  onScopeChange: (scope: SidebarScope) => void;
}

// Falha da API por perfil nunca bloqueia o escopo GLOBAL legado.
const PROFILE_ERRORS: Partial<Record<SidebarApiError["kind"], string>> = {
  missing: "O backend em execução não possui a API /api/sidebar/profiles. Menus por perfil ficam indisponíveis até a publicação do backend.",
  database: "Menus por perfil indisponíveis: banco inacessível ou schema de perfis (dbo.SidebarProfileConfiguration) ainda não aplicado.",
  forbidden: "Sua conta não pode gerenciar menus por perfil.",
  network: "O backend não respondeu. Menus por perfil ficam indisponíveis.",
};

export function SidebarScopeBar({ scope, profiles, profilesLoading, profilesError, detail, viewerIsMasterAdmin, pendingDrafts, disabled, onScopeChange }: Props) {
  const selected = scope.kind === "profile" ? profiles.find(p => p.profileId === scope.profileId) ?? null : null;
  const masterLocked = !!selected?.isMasterAdmin && !viewerIsMasterAdmin;
  const currentKey = scopeKey(scope);
  const otherDrafts = pendingDrafts.filter(d => d.key !== currentKey);
  return <section aria-label="Escopo do menu" className="rounded-lg border bg-card p-3 space-y-3">
    <div className="flex flex-wrap items-end gap-3">
      <fieldset className="text-sm" disabled={disabled}>
        <legend className="mb-1">Escopo</legend>
        <div role="radiogroup" aria-label="Escopo" className="inline-flex rounded-full border bg-background p-0.5">
          <button type="button" role="radio" aria-checked={scope.kind === "global"} onClick={() => onScopeChange({ kind: "global" })}
            className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 ${scope.kind === "global" ? "bg-primary text-primary-foreground" : "hover:bg-muted"}`}>
            <Globe2 className="h-4 w-4" aria-hidden="true" />GLOBAL (legado)
          </button>
          <button type="button" role="radio" aria-checked={scope.kind === "profile"} disabled={profilesLoading || !!profilesError || profiles.length === 0}
            onClick={() => profiles[0] && onScopeChange({ kind: "profile", profileId: selected?.profileId ?? profiles[0].profileId })}
            className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 disabled:opacity-50 ${scope.kind === "profile" ? "bg-primary text-primary-foreground" : "hover:bg-muted"}`}>
            <UsersRound className="h-4 w-4" aria-hidden="true" />Por Perfil
          </button>
        </div>
      </fieldset>
      {scope.kind === "profile" && <label className="text-sm">Perfil
        <select aria-label="Perfil" className="mt-1 block min-w-60 rounded border bg-background p-2" disabled={disabled} value={scope.profileId}
          onChange={e => onScopeChange({ kind: "profile", profileId: Number(e.target.value) })}>
          {profiles.map(p => <option key={p.profileId} value={p.profileId}>
            {p.name}{p.isMasterAdmin ? " · Master Admin" : ""}{p.configured ? " · rev. " + p.revision : " · sem layout próprio"}
          </option>)}
        </select>
      </label>}
    </div>
    {profilesLoading && <p className="text-xs text-muted-foreground">Carregando perfis…</p>}
    {profilesError && <p role="alert" className="text-xs text-amber-700 dark:text-amber-300">{PROFILE_ERRORS[profilesError.kind] ?? profilesError.message}</p>}
    {scope.kind === "global"
      ? <p className="text-xs text-muted-foreground">Menu global legado (dbo.SidebarOfficial). Perfis sem layout próprio herdam esta revisão e suas ocultações.</p>
      : <div className="space-y-1 text-xs text-muted-foreground">
        {detail && <p><strong className="text-foreground">{detail.profileName}</strong> · {profileOrigin(detail)}</p>}
        <p>Este layout define apenas <strong>visibilidade</strong> no menu. As permissões do usuário continuam sendo aplicadas: nenhum perfil concede acesso a projetos, telas Master-only ou permissions.manage.</p>
        <p>Publicar altera somente este perfil; o menu global e os demais perfis não mudam. Usuários com vários perfis no projeto veem a união dos itens visíveis.</p>
        {selected?.isMasterAdmin && <p className="inline-flex items-center gap-1"><ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />Perfil Master Admin: parte do catálogo completo e não herda as ocultações globais.</p>}
        {masterLocked && <p role="alert" className="text-amber-700 dark:text-amber-300">Somente Master Admin pode publicar o layout deste perfil. Você pode visualizar, mas não publicar.</p>}
      </div>}
    {otherDrafts.length > 0 && <p role="status" className="flex items-start gap-1.5 rounded border border-amber-500/40 bg-amber-500/10 p-2 text-xs">
      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      <span>Rascunhos não publicados preservados: {otherDrafts.map(d => d.label).join(", ")}. Cada rascunho pertence ao seu escopo e não é aplicado a outro perfil.</span>
    </p>}
  </section>;
}
