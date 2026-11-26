// Painel (Dialog) de acesso de UM usuário em UM escopo (projeto ou global): perfis, exceções ALLOW/DENY e situação no projeto.
// "Efetivo" vem do backend (mesmo PermissionResolver). O próprio usuário é somente leitura (o backend também bloqueia).
import { useCallback, useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Check, Copy, KeyRound, Loader2, Save, Search, X } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { cn } from "@/lib/utils";
import type { CatalogResource } from "./permissionTreeLogic";
import { permissionsApi, type Effect, type PermProfile, type UserAccess } from "./permissionsAdminApi";
import { useAlteracoesPendentes } from "./useAlteracoesPendentes";
import CopyAccessDialog from "./CopyAccessDialog";
import type { ProjetoRegistrado } from "@/lib/projectsRegistry";

type Escolha = "HERDADO" | Effect;

export const iniciais = (nome: string | null | undefined, email: string) => {
  const base = (nome?.trim() || email.split("@")[0]).split(/[\s._-]+/).filter(Boolean);
  return ((base[0]?.[0] ?? "?") + (base.length > 1 ? base[base.length - 1][0] : "")).toUpperCase();
};

export function Avatar({ nome, email, className }: { nome: string | null | undefined; email: string; className?: string }) {
  return (
    <span className={cn("flex shrink-0 items-center justify-center rounded-full bg-primary/15 font-black text-primary", className ?? "h-9 w-9 text-xs")}>
      {iniciais(nome, email)}
    </span>
  );
}

interface Props {
  open: boolean;
  onClose: () => void;
  userId: number | null;
  /** Project_ID_ACC do projeto, ou null = escopo global. */
  projectIdAcc: string | null;
  nomeEscopo: string;
  perfis: PermProfile[];
  catalogo: CatalogResource[];
  ehVoceMesmo: (email: string) => boolean;
  /** Chamado após qualquer alteração salva (para atualizar a lista). */
  onAlterado: () => void;
  /** Projetos N&A ativos (destinos de "Copiar acesso"). */
  projetos: ProjetoRegistrado[];
  /** Exibe "Senha do login N&A" (definir/redefinir). */
  podeDefinirSenha?: boolean;
}

export default function UserAccessDialog({ open, onClose, userId, projectIdAcc, nomeEscopo, perfis, catalogo, ehVoceMesmo, onAlterado, projetos, podeDefinirSenha }: Props) {
  const [copiando, setCopiando] = useState(false);
  const [acesso, setAcesso] = useState<UserAccess | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [perfisSel, setPerfisSel] = useState<Set<number>>(new Set());
  const [overrides, setOverrides] = useState<Map<string, Effect>>(new Map());
  const [busca, setBusca] = useState("");
  const [salvando, setSalvando] = useState<"perfis" | "excecoes" | "ativo" | "senha" | null>(null);
  const [novaSenha, setNovaSenha] = useState("");
  const [exigirTroca, setExigirTroca] = useState(false);

  const rotulo = useMemo(() => new Map(catalogo.map((r) => [r.key, r])), [catalogo]);
  const nivel = useMemo(() => {
    const pai = new Map(catalogo.map((r) => [r.key, r.parentKey]));
    const n = (k: string): number => { const p = pai.get(k); return p ? 1 + n(p) : 0; };
    return new Map(catalogo.map((r) => [r.key, n(r.key)]));
  }, [catalogo]);

  const carregar = useCallback(async () => {
    if (userId == null) return;
    setErro(null);
    try {
      const a = await permissionsApi.acesso(userId, projectIdAcc);
      setAcesso(a);
      setPerfisSel(new Set(a.profileIds));
      setOverrides(new Map(a.overrides.map((o) => [o.permissionKey, o.effect])));
    } catch (e) {
      setAcesso(null);
      setErro(e instanceof Error ? e.message : "Falha ao carregar o acesso do usuário.");
    }
  }, [userId, projectIdAcc]);

  useEffect(() => { if (open) { setAcesso(null); setBusca(""); setNovaSenha(""); setExigirTroca(false); void carregar(); } }, [open, carregar]);

  const usuario = acesso?.user ?? null;
  const somenteLeitura = !!usuario && ehVoceMesmo(usuario.email);
  const perfisAlterados = !!acesso && (perfisSel.size !== acesso.profileIds.length || acesso.profileIds.some((id) => !perfisSel.has(id)));
  const excecoesAlteradas = !!acesso && (overrides.size !== acesso.overrides.length ||
    acesso.overrides.some((o) => overrides.get(o.permissionKey) !== o.effect));
  const pendente = perfisAlterados || excecoesAlteradas;
  const { confirmarSe, dialogo } = useAlteracoesPendentes(open && pendente);
  const fechar = () => { if (!salvando) confirmarSe(onClose); };

  const executar = async (qual: "perfis" | "excecoes" | "ativo" | "senha", acao: () => Promise<void>, ok: string) => {
    setSalvando(qual);
    setErro(null);
    try { await acao(); toast.success(ok); await carregar(); onAlterado(); }
    catch (e) { setErro(e instanceof Error ? e.message : "Falha ao salvar."); }
    finally { setSalvando(null); }
  };

  const escolha = (k: string): Escolha => overrides.get(k) ?? "HERDADO";
  const definir = (k: string, v: Escolha) => setOverrides((m) => { const n = new Map(m); if (v === "HERDADO") n.delete(k); else n.set(k, v); return n; });
  const t = busca.trim().toLowerCase();
  const linhas = (acesso?.permissoes ?? []).filter((l) => !t || l.key.includes(t) || (rotulo.get(l.key)?.label.toLowerCase().includes(t) ?? false));

  return (
    <>
    <Dialog open={open} onOpenChange={(v) => { if (!v) fechar(); }}>
      <DialogContent className="flex max-h-[90dvh] max-w-[960px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (pendente || salvando) e.preventDefault(); }}>
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          {usuario ? (
            <div className="flex items-center gap-3">
              <Avatar nome={usuario.displayName} email={usuario.email} className="h-11 w-11 text-sm" />
              <div className="min-w-0">
                <DialogTitle className="truncate text-base font-black">{usuario.displayName ?? usuario.email}</DialogTitle>
                <DialogDescription className="truncate">{usuario.email} · <strong className="text-foreground">{nomeEscopo}</strong>
                  {acesso?.isMasterAdmin && <span className="ml-2 rounded bg-primary/15 px-1.5 py-0.5 text-[10px] font-black uppercase text-primary">MasterAdmin</span>}
                </DialogDescription>
              </div>
              {projectIdAcc && acesso && (
                <label className="ml-auto flex items-center gap-2 text-sm">
                  <Switch checked={acesso.ativoNoProjeto} disabled={somenteLeitura || salvando !== null}
                    onCheckedChange={(v) => executar("ativo", () => permissionsApi.ativoNoProjeto(usuario.userId, projectIdAcc, v),
                      v ? "Usuário ativado neste projeto." : "Usuário desativado neste projeto.")} />
                  <span className={acesso.ativoNoProjeto ? "text-emerald-500" : "text-muted-foreground"}>{acesso.ativoNoProjeto ? "Ativo no projeto" : "Inativo no projeto"}</span>
                </label>
              )}
            </div>
          ) : (
            <>
              <DialogTitle className="text-base font-black">Acesso do usuário</DialogTitle>
              <DialogDescription>{nomeEscopo}</DialogDescription>
            </>
          )}
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5 custom-scrollbar">
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-400">{erro}</p>}
          {!acesso && !erro && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando…</p>}
          {somenteLeitura && (
            <p className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-500">
              Você não pode alterar o seu próprio acesso. Peça a outro administrador.
            </p>
          )}
          {podeDefinirSenha && usuario && !somenteLeitura && (
            <form className="flex flex-wrap items-end gap-3 rounded-lg border border-border p-4" autoComplete="off"
              onSubmit={(e) => {
                e.preventDefault();
                const senha = novaSenha;
                if (senha.length < 8) {
                  setErro("A senha precisa ter pelo menos 8 caracteres para ser definida.");
                  return;
                }
                void executar("senha", async () => { await permissionsApi.definirSenha(usuario.userId, senha, exigirTroca); setNovaSenha(""); },
                  "Senha do login N&A definida.");
              }}>
              <div className="basis-full">
                <h4 className="flex items-center gap-2 text-sm font-bold"><KeyRound className="h-4 w-4 text-primary" />Senha do login N&amp;A</h4>
                <p className="text-xs text-muted-foreground">Define ou redefine a senha de acesso por e-mail (mínimo 8 caracteres). Não altera a conta Autodesk. A senha não é exibida depois de salva.</p>
              </div>
              <label className="flex w-64 flex-col gap-1.5 text-xs font-semibold text-muted-foreground">
                Nova senha (mínimo 8 caracteres)
                <Input className="h-10" type="password" autoComplete="new-password" maxLength={256} value={novaSenha}
                  onChange={(e) => { setNovaSenha(e.target.value); if (erro) setErro(null); }} disabled={salvando !== null} />
                <span aria-live="polite" className="text-[11px] font-normal text-muted-foreground">
                  {novaSenha.length < 8 ? `Faltam ${8 - novaSenha.length} caracteres para poder definir a senha.` : "Quantidade mínima atingida. Clique em Definir senha."}
                </span>
              </label>
              <label className="flex h-10 items-center gap-2 text-xs text-muted-foreground"
                title="Enquanto não existir troca de senha pelo próprio usuário, isso impede a entrada até nova redefinição.">
                <input type="checkbox" checked={exigirTroca} onChange={(e) => setExigirTroca(e.target.checked)} disabled={salvando !== null} />
                Exigir troca no próximo acesso
              </label>
              <Button type="submit" className="h-10 gap-1.5" disabled={salvando !== null}>
                {salvando === "senha" ? <Loader2 className="h-4 w-4 animate-spin" /> : <KeyRound className="h-4 w-4" />}Definir senha
              </Button>
            </form>
          )}

          {acesso && (
            <>
              {/* Perfis */}
              <section className="rounded-lg border border-border">
                <header className="flex items-center justify-between border-b border-border px-4 py-2.5">
                  <h3 className="text-xs font-black uppercase tracking-widest text-muted-foreground">
                    Perfis {projectIdAcc ? "deste projeto" : "globais (todos os projetos)"}
                  </h3>
                  <Button size="sm" className="h-8 gap-1.5" disabled={somenteLeitura || !perfisAlterados || salvando !== null}
                    onClick={() => executar("perfis", () => permissionsApi.perfisUsuario(acesso.user.userId, projectIdAcc, [...perfisSel]), "Perfis salvos.")}>
                    {salvando === "perfis" ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}Salvar perfis
                  </Button>
                </header>
                <div className="grid gap-2 p-4 sm:grid-cols-2 lg:grid-cols-3">
                  {perfis.filter((p) => p.ativo && !(p.isMasterAdmin && projectIdAcc)).map((p) => {
                    const marcado = perfisSel.has(p.profileId);
                    return (
                      <label key={p.profileId} className={cn("flex cursor-pointer items-center gap-2.5 rounded-md border px-3 py-2 text-sm transition-colors",
                        marcado ? "border-primary bg-primary/10" : "border-border hover:bg-muted/40", somenteLeitura && "cursor-not-allowed opacity-70")}>
                        <input type="checkbox" className="h-4 w-4 accent-primary" checked={marcado} disabled={somenteLeitura}
                          onChange={(e) => setPerfisSel((s) => { const n = new Set(s); if (e.target.checked) n.add(p.profileId); else n.delete(p.profileId); return n; })} />
                        <span className="min-w-0 flex-1 truncate">{p.name}</span>
                        <span className="text-[10px] text-muted-foreground">{p.isMasterAdmin ? "total" : p.permissions.length}</span>
                      </label>
                    );
                  })}
                </div>
                <p className="px-4 pb-3 text-[11px] text-muted-foreground">Vários perfis somam permissões. {projectIdAcc && "MasterAdmin só pode ser atribuído globalmente."}</p>
              </section>

              {/* Exceções */}
              <section className="rounded-lg border border-border">
                <header className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-2.5">
                  <h3 className="text-xs font-black uppercase tracking-widest text-muted-foreground">Exceções individuais</h3>
                  <div className="relative ml-auto w-56">
                    <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                    <Input className="h-8 pl-8 text-xs" placeholder="Buscar permissão…" value={busca} onChange={(e) => setBusca(e.target.value)} />
                  </div>
                  <Button size="sm" className="h-8 gap-1.5" disabled={somenteLeitura || !excecoesAlteradas || salvando !== null}
                    onClick={() => executar("excecoes", () => permissionsApi.overridesUsuario(acesso.user.userId, projectIdAcc,
                      [...overrides].map(([permissionKey, effect]) => ({ permissionKey, effect }))), "Exceções salvas.")}>
                    {salvando === "excecoes" ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}Salvar exceções
                  </Button>
                </header>
                <p className="px-4 pt-2 text-[11px] text-muted-foreground">
                  Precedência: MasterAdmin &gt; DENY &gt; ALLOW &gt; perfis &gt; negado. "Efetivo" mostra o que está salvo.
                </p>
                <div className="max-h-[40vh] overflow-y-auto px-4 pb-3 custom-scrollbar">
                  <table className="w-full text-sm">
                    <thead className="sticky top-0 z-10 bg-background text-[10px] uppercase tracking-wide text-muted-foreground">
                      <tr><th className="py-2 text-left">Permissão</th><th className="text-left">Herdado de</th><th className="w-[190px]">Exceção</th><th className="w-16">Efetivo</th></tr>
                    </thead>
                    <tbody>
                      {linhas.map((l) => (
                        <tr key={l.key} className="border-t border-border/50">
                          <td className="py-1.5" style={{ paddingLeft: (nivel.get(l.key) ?? 0) * 14 }}>
                            <div className="text-sm">{rotulo.get(l.key)?.label ?? l.key}</div>
                            <code className="text-[10px] text-muted-foreground">{l.key}</code>
                          </td>
                          <td className="text-xs text-muted-foreground">{l.herdadoDe.length ? l.herdadoDe.join(", ") : "—"}</td>
                          <td className="text-center">
                            <div className="inline-flex overflow-hidden rounded-md border border-border text-[10px] font-bold">
                              {(["HERDADO", "ALLOW", "DENY"] as Escolha[]).map((v) => (
                                <button key={v} type="button" disabled={somenteLeitura} onClick={() => definir(l.key, v)}
                                  className={cn("px-2 py-1 transition-colors", escolha(l.key) === v
                                    ? v === "ALLOW" ? "bg-emerald-600 text-white" : v === "DENY" ? "bg-rose-600 text-white" : "bg-muted text-foreground"
                                    : "text-muted-foreground hover:bg-muted/50")}>
                                  {v}
                                </button>
                              ))}
                            </div>
                          </td>
                          <td className="text-center" title={l.origem}>
                            {l.efetivo ? <Check className="mx-auto h-4 w-4 text-emerald-500" /> : <X className="mx-auto h-4 w-4 text-rose-500" />}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            </>
          )}
        </div>

        <footer className="flex items-center gap-3 border-t border-border px-6 py-3">
          {projectIdAcc && acesso && (
            <Button variant="outline" className="gap-1.5" disabled={somenteLeitura || pendente || salvando !== null} onClick={() => setCopiando(true)}
              title={pendente ? "Salve as alterações antes de copiar." : undefined}>
              <Copy className="h-4 w-4" />Copiar acesso para outros projetos
            </Button>
          )}
          {pendente && <span className="flex items-center gap-2 text-sm text-amber-500"><span className="h-2 w-2 rounded-full bg-amber-500" />Alterações não salvas</span>}
          <Button variant="outline" className="ml-auto" onClick={fechar} disabled={salvando !== null}>Fechar</Button>
        </footer>
      </DialogContent>
    </Dialog>
    {dialogo}
    {projectIdAcc && acesso && (
      <CopyAccessDialog
        open={copiando}
        onClose={() => setCopiando(false)}
        userId={acesso.user.userId}
        usuarioNome={acesso.user.displayName ?? acesso.user.email}
        origem={{ projectIdAcc, nome: nomeEscopo }}
        perfisOrigem={perfis.filter((p) => acesso.profileIds.includes(p.profileId)).map((p) => p.name)}
        overridesOrigem={acesso.overrides.length}
        projetos={projetos}
        onConcluido={onAlterado}
      />
    )}
    </>
  );
}
