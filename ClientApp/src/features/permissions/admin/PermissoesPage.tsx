// /configuracoes/permissoes — administração de PERFIS e USUÁRIOS (permissions.manage, validado no backend).
import { useCallback, useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, History, KeyRound, LogIn, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { usePermissions } from "../PermissionProvider";
import ProfilesPanel from "./ProfilesPanel";
import UsersPanel from "./UsersPanel";
import { useAlteracoesPendentes } from "./useAlteracoesPendentes";
import { montarArvore, type CatalogResource } from "./permissionTreeLogic";
import { permissionsApi, type AuditEntry } from "./permissionsAdminApi";

type Aba = "perfis" | "usuarios" | "auditoria";

function Auditoria({ onErro }: { onErro: (m: string) => void }) {
  const [linhas, setLinhas] = useState<AuditEntry[]>([]);
  useEffect(() => { permissionsApi.auditoria().then(setLinhas).catch((e) => onErro(e instanceof Error ? e.message : "Falha.")); }, [onErro]);
  return (
    <div className="max-h-[70vh] overflow-y-auto rounded-xl border border-border bg-card/60 p-3 custom-scrollbar">
      <table className="w-full text-xs">
        <thead className="text-left uppercase tracking-wide text-muted-foreground"><tr><th>Data (UTC)</th><th>Por</th><th>Alvo</th><th>Usuário</th><th>Ref.</th><th>Antes</th><th>Depois</th></tr></thead>
        <tbody>
          {linhas.map((a) => (
            <tr key={a.id} className="border-t border-border/50 align-top">
              <td className="whitespace-nowrap py-1">{a.dataHora.replace("T", " ").slice(0, 19)}</td>
              <td>{a.alteradoPorEmail ?? "—"}</td><td>{a.alvo}</td><td>{a.usuarioAfetadoEmail ?? "—"}</td><td>{a.referencia ?? "—"}</td>
              <td className="max-w-[260px] break-all text-muted-foreground">{a.valorAnterior ?? "—"}</td>
              <td className="max-w-[260px] break-all">{a.valorNovo ?? "—"}</td>
            </tr>
          ))}
          {linhas.length === 0 && <tr><td colSpan={7} className="py-4 text-center text-muted-foreground">Sem alterações registradas.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

export default function PermissoesPage() {
  const { effective } = usePermissions();
  const [aba, setAba] = useState<Aba>("perfis");
  const [catalogo, setCatalogo] = useState<CatalogResource[] | null>(null);
  const [erroFatal, setErroFatal] = useState<string | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [perfilPendente, setPerfilPendente] = useState(false);
  const { confirmarSe, dialogo } = useAlteracoesPendentes(perfilPendente);

  useEffect(() => {
    permissionsApi.catalogo().then(setCatalogo).catch((e: unknown) => setErroFatal(e instanceof Error ? e.message : "Falha ao carregar o catálogo."));
  }, []);

  const arvore = useMemo(() => montarArvore(catalogo ?? []), [catalogo]);
  const onErro = useCallback((m: string) => setErro(m), []);
  const onAviso = useCallback((m: string) => { setErro(null); toast.success(m); }, []);
  const precisaRelogin = !!erro && erro.includes("sair e entrar");

  const ABAS: { id: Aba; label: string; icon: typeof Users }[] = [
    { id: "perfis", label: "Perfis", icon: KeyRound },
    { id: "usuarios", label: "Usuários", icon: Users },
    { id: "auditoria", label: "Auditoria", icon: History },
  ];

  return (
    <div className="space-y-4 animate-enter">
      {effective && !effective.schemaInstalled && (
        <p className="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-500">
          O banco de permissões ainda não foi instalado. Aplique <code>Migrations/Permissions/20260929_001_Permissions.sql</code> (após aprovação).
        </p>
      )}
      {effective && effective.schemaInstalled && !effective.enforced && (
        <p className="rounded-md border border-border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
          Permissões configuráveis, mas ainda <strong>não aplicadas</strong> (Permissions:Enforce = false): todos os usuários logados mantêm o acesso atual.
        </p>
      )}
      {erroFatal ? (
        <p className="text-sm text-rose-400">{erroFatal}</p>
      ) : !catalogo ? (
        <p className="text-sm text-muted-foreground">Carregando catálogo…</p>
      ) : (
        <>
          <div className="flex gap-1 rounded-xl border border-border bg-card/60 p-1">
            {ABAS.map((a) => (
              <button key={a.id} type="button" onClick={() => { if (a.id !== aba) confirmarSe(() => { setErro(null); setAba(a.id); }); }}
                className={cn("flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors",
                  aba === a.id ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
                <a.icon className="h-4 w-4" />{a.label}
              </button>
            ))}
            <span className="ml-auto self-center pr-3 text-xs text-muted-foreground">{catalogo.length} permissões no catálogo</span>
          </div>
          {erro && (
            <div role="alert" className={cn("flex flex-wrap items-center gap-3 rounded-lg border px-4 py-3 text-sm",
              precisaRelogin ? "border-amber-500/40 bg-amber-500/10 text-amber-500" : "border-rose-500/40 bg-rose-500/10 text-rose-400")}>
              {precisaRelogin ? <LogIn className="h-4 w-4 shrink-0" /> : <AlertTriangle className="h-4 w-4 shrink-0" />}
              <span className="flex-1">{erro}</span>
              {precisaRelogin && (
                <Button size="sm" onClick={() => { window.location.href = "/Authentication/OAuth/Logout"; }} className="h-8 gap-1.5">
                  <LogIn className="h-3.5 w-3.5" />Sair e entrar novamente
                </Button>
              )}
              <button type="button" onClick={() => setErro(null)} className="text-xs underline opacity-70 hover:opacity-100">Fechar</button>
            </div>
          )}
          {aba === "perfis" && (
            <ProfilesPanel arvore={arvore} totalPermissoes={catalogo.length} onErro={onErro}
              onPendenteChange={setPerfilPendente} confirmarSe={confirmarSe} />
          )}
          {aba === "usuarios" && <UsersPanel catalogo={catalogo} onErro={onErro} onAviso={onAviso} />}
          {aba === "auditoria" && <Auditoria onErro={onErro} />}
        </>
      )}
      {dialogo}
    </div>
  );
}
