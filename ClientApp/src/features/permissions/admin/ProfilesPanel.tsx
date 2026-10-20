// PERFIS: lista compacta à esquerda; perfil selecionado à direita (nome, descrição, status e árvore de permissões).
// Edição LOCAL: nada é salvo a cada checkbox — "Alterações não salvas" + SALVAR ALTERAÇÕES; trocar de perfil/aba ou sair
// com pendências pede confirmação. MasterAdmin é de sistema (somente leitura).
import { useCallback, useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Copy, KeyRound, Loader2, Lock, Plus, RotateCcw, Save, Search, ShieldCheck, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import PermissionTree from "./PermissionTree";
import PerfilDialog from "./PerfilDialog";
import { diferenca, filtrarArvore, type TreeNode } from "./permissionTreeLogic";
import { permissionsApi, type PermProfile } from "./permissionsAdminApi";
import { usePermissions } from "@/features/permissions/PermissionProvider";

interface Props {
  arvore: TreeNode[];
  totalPermissoes: number;
  onErro: (m: string) => void;
  /** Informa a página se há alterações não salvas (para confirmar troca de aba). */
  onPendenteChange: (pendente: boolean) => void;
  /** Pede confirmação (quando há pendências) antes de executar a ação. */
  confirmarSe: (acao: () => void) => void;
}

const PERFIL_PADRAO = "Somente Leitura";

function CartaoPerfil({ p, ativo, total, onClick }: { p: PermProfile; ativo: boolean; total: number; onClick: () => void }) {
  const Icone = p.isMasterAdmin ? Lock : p.name === PERFIL_PADRAO ? ShieldCheck : KeyRound;
  return (
    <button type="button" onClick={onClick}
      className={cn("flex w-full items-center gap-3 rounded-lg border px-3 py-2.5 text-left transition-colors",
        ativo ? "border-primary bg-primary/10 shadow-[0_0_0_1px_hsl(var(--primary)/0.4)]" : "border-border bg-background/40 hover:border-primary/40 hover:bg-muted/40",
        !p.ativo && "opacity-60")}>
      <span className={cn("flex h-8 w-8 shrink-0 items-center justify-center rounded-md", ativo ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground")}>
        <Icone className="h-4 w-4" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-1.5">
          <span className="truncate text-sm font-semibold text-foreground">{p.name}</span>
          {p.isSystem && <span className="rounded bg-muted px-1.5 py-0.5 text-[9px] font-black uppercase tracking-widest text-muted-foreground">Sistema</span>}
          {!p.isSystem && p.name === PERFIL_PADRAO && <span className="rounded bg-muted px-1.5 py-0.5 text-[9px] font-black uppercase tracking-widest text-muted-foreground">Padrão</span>}
        </span>
        <span className="block text-xs text-muted-foreground">
          {p.isMasterAdmin ? "Acesso total" : `${p.permissions.length} de ${total} permissões`}{!p.ativo && " · inativo"}
        </span>
      </span>
    </button>
  );
}

export default function ProfilesPanel({ arvore, totalPermissoes, onErro, onPendenteChange, confirmarSe }: Props) {
  const [perfis, setPerfis] = useState<PermProfile[] | null>(null);
  const [selId, setSelId] = useState<number | null>(null);
  const [nome, setNome] = useState("");
  const [descricao, setDescricao] = useState("");
  const [ativo, setAtivo] = useState(true);
  const [keys, setKeys] = useState<Set<string>>(new Set());
  const [busca, setBusca] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [excluindo, setExcluindo] = useState(false);
  const [dialogo, setDialogo] = useState<"criar" | "duplicar" | null>(null);
  const { effective } = usePermissions();

  const selecionar = useCallback((p: PermProfile | undefined) => {
    if (!p) { setSelId(null); return; }
    setSelId(p.profileId); setNome(p.name); setDescricao(p.description ?? ""); setAtivo(p.ativo); setKeys(new Set(p.permissions));
  }, []);

  const carregar = useCallback(async (manterId?: number | null) => {
    try {
      const lista = await permissionsApi.perfis();
      setPerfis(lista);
      selecionar(lista.find((p) => p.profileId === manterId) ?? lista[0]);
    } catch (e) {
      setPerfis([]);
      onErro(e instanceof Error ? e.message : "Falha ao carregar perfis.");
    }
  }, [onErro, selecionar]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { void carregar(); }, []);

  const atual = perfis?.find((p) => p.profileId === selId) ?? null;
  const protegido = atual?.isSystem ?? false;
  const arvoreFiltrada = useMemo(() => filtrarArvore(arvore, busca), [arvore, busca]);
  const dif = useMemo(() => diferenca(atual?.permissions ?? [], keys), [atual, keys]);
  const dadosAlterados = !!atual && (nome !== atual.name || (descricao.trim() || null) !== (atual.description?.trim() || null) || ativo !== atual.ativo);
  const pendente = !!atual && !protegido && (dadosAlterados || dif.adicionadas.length > 0 || dif.removidas.length > 0);

  useEffect(() => { onPendenteChange(pendente); }, [pendente, onPendenteChange]);
  useEffect(() => () => onPendenteChange(false), [onPendenteChange]);

  const descartar = () => { if (atual) selecionar(atual); };

  const salvar = async () => {
    if (!atual || !pendente) return;
    if (!nome.trim()) { onErro("O nome do perfil é obrigatório."); return; }
    setSalvando(true);
    try {
      if (dadosAlterados) await permissionsApi.atualizarPerfil(atual.profileId, nome.trim(), descricao.trim() || null, ativo);
      if (dif.adicionadas.length > 0 || dif.removidas.length > 0) await permissionsApi.permissoesPerfil(atual.profileId, [...keys].sort());
      toast.success(`Perfil "${nome.trim()}" salvo.`);
      await carregar(atual.profileId);
    } catch (e) {
      onErro(e instanceof Error ? e.message : "Falha ao salvar.");
    } finally {
      setSalvando(false);
    }
  };

  const excluir = async () => {
    if (!atual || atual.isSystem || effective?.isMasterAdmin !== true) return;
    const confirmado = window.confirm(
      `Excluir o perfil "${atual.name}"?\n\nAs atribuições deste perfil serão removidas de todos os usuários e projetos. Esta ação não pode ser desfeita.`,
    );
    if (!confirmado) return;
    setExcluindo(true);
    try {
      await permissionsApi.excluirPerfil(atual.profileId);
      toast.success(`Perfil "${atual.name}" excluído.`);
      await carregar();
    } catch (e) {
      onErro(e instanceof Error ? e.message : "Falha ao excluir perfil.");
    } finally {
      setExcluindo(false);
    }
  };

  const confirmarDialogo = async (n: string, d: string | null) => {
    const { profileId } = dialogo === "duplicar" && atual
      ? await permissionsApi.duplicarPerfil(atual.profileId, n)
      : await permissionsApi.criarPerfil(n, d, []);
    toast.success(dialogo === "duplicar" ? `Perfil duplicado como "${n}".` : `Perfil "${n}" criado. Marque as permissões e salve.`);
    setDialogo(null);
    await carregar(profileId);
  };

  return (
    <div className="grid gap-4 lg:grid-cols-[300px_minmax(0,1fr)]">
      {/* ------------------------------------------------------------------ lista de perfis */}
      <aside className="flex flex-col rounded-xl border border-border bg-card/60 p-3 lg:max-h-[calc(100vh-220px)]">
        <div className="mb-3 flex items-center justify-between px-1">
          <h3 className="text-xs font-black uppercase tracking-widest text-muted-foreground">Perfis {perfis ? `(${perfis.length})` : ""}</h3>
          <Button size="sm" onClick={() => confirmarSe(() => setDialogo("criar"))} className="h-8 gap-1.5 text-xs font-bold">
            <Plus className="h-3.5 w-3.5" />Novo
          </Button>
        </div>
        <div className="-mr-1 flex-1 space-y-1.5 overflow-y-auto pr-1 custom-scrollbar">
          {perfis === null && <p className="flex items-center gap-2 px-1 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando…</p>}
          {perfis?.length === 0 && <p className="px-1 text-sm text-muted-foreground">Nenhum perfil disponível.</p>}
          {perfis?.map((p) => (
            <CartaoPerfil key={p.profileId} p={p} ativo={p.profileId === selId} total={totalPermissoes}
              onClick={() => { if (p.profileId !== selId) confirmarSe(() => selecionar(p)); }} />
          ))}
        </div>
      </aside>

      {/* -------------------------------------------------------------- perfil selecionado */}
      {!atual ? (
        <section className="flex min-h-[300px] items-center justify-center rounded-xl border border-border bg-card/60 p-6 text-sm text-muted-foreground">
          {perfis === null ? "Carregando perfis…" : "Selecione ou crie um perfil."}
        </section>
      ) : (
        <section className="flex min-w-0 flex-col rounded-xl border border-border bg-card/60">
          <header className="flex flex-wrap items-start gap-3 border-b border-border px-5 py-4">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <h2 className="truncate text-lg font-black tracking-tight text-foreground">{atual.name}</h2>
                {atual.isSystem && <span className="flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-[10px] font-black uppercase tracking-widest text-muted-foreground"><Lock className="h-3 w-3" />Sistema</span>}
              </div>
              <p className="text-xs text-muted-foreground">
                {atual.isMasterAdmin ? "Acesso total ao N&A Connect" : `${keys.size} de ${totalPermissoes} permissões marcadas`}
              </p>
            </div>
            {effective?.isMasterAdmin === true && !atual.isSystem && (
              <Button size="sm" variant="destructive" onClick={() => void excluir()} disabled={excluindo || salvando} className="h-9 gap-1.5">
                {excluindo ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}Excluir
              </Button>
            )}
            <Button size="sm" variant="outline" onClick={() => confirmarSe(() => setDialogo("duplicar"))} disabled={atual.isMasterAdmin || excluindo} className="h-9 gap-1.5">
              <Copy className="h-3.5 w-3.5" />Duplicar
            </Button>
          </header>

          {atual.isMasterAdmin ? (
            <div className="m-5 rounded-lg border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
              <p className="flex items-center gap-2 font-semibold text-foreground"><Lock className="h-4 w-4" />Perfil de sistema protegido</p>
              <p className="mt-1">
                O MasterAdmin tem acesso total, não pode ser editado nem duplicado e só pode ser atribuído globalmente, por outro MasterAdmin.
                O MasterAdmin inicial vem da configuração <code>Permissions:MasterAdmins</code>.
              </p>
            </div>
          ) : (
            <>
              {/* Dados do perfil */}
              <div className="grid gap-4 border-b border-border px-5 py-4 md:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto]">
                <div className="space-y-1.5">
                  <Label htmlFor="perfil-edit-nome">Nome</Label>
                  <Input id="perfil-edit-nome" value={nome} maxLength={100} disabled={protegido || salvando} onChange={(e) => setNome(e.target.value)} />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="perfil-edit-desc">Descrição</Label>
                  <Textarea id="perfil-edit-desc" rows={1} value={descricao} maxLength={500} disabled={protegido || salvando}
                    className="min-h-[40px] resize-none" onChange={(e) => setDescricao(e.target.value)} placeholder="Opcional" />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="perfil-edit-ativo">Status</Label>
                  <div className="flex h-10 items-center gap-2">
                    <Switch id="perfil-edit-ativo" checked={ativo} disabled={protegido || salvando} onCheckedChange={setAtivo} />
                    <span className={cn("text-sm font-medium", ativo ? "text-emerald-500" : "text-muted-foreground")}>{ativo ? "Ativo" : "Inativo"}</span>
                  </div>
                </div>
              </div>

              {/* Permissões */}
              <div className="flex flex-wrap items-center gap-3 px-5 pt-4">
                <h3 className="text-xs font-black uppercase tracking-widest text-muted-foreground">Permissões</h3>
                <div className="relative ml-auto w-full max-w-xs">
                  <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                  <Input className="h-9 pl-8" placeholder="Buscar permissão…" value={busca} onChange={(e) => setBusca(e.target.value)} />
                </div>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto px-5 py-3 custom-scrollbar lg:max-h-[calc(100vh-420px)]">
                <PermissionTree nodes={arvoreFiltrada} selecionadas={keys} onChange={setKeys} readOnly={protegido || salvando} expandirTudo={busca.trim().length > 0} />
              </div>

              {/* Barra de salvamento */}
              <footer className={cn("flex flex-wrap items-center gap-3 border-t px-5 py-3 transition-colors",
                pendente ? "border-amber-500/40 bg-amber-500/5" : "border-border")}>
                {pendente ? (
                  <span className="flex items-center gap-2 text-sm font-medium text-amber-500">
                    <span className="h-2 w-2 rounded-full bg-amber-500" />
                    Alterações não salvas
                    {(dif.adicionadas.length > 0 || dif.removidas.length > 0) &&
                      <span className="text-xs font-normal text-muted-foreground">(+{dif.adicionadas.length} / −{dif.removidas.length} permissões)</span>}
                  </span>
                ) : (
                  <span className="text-sm text-muted-foreground">Tudo salvo.</span>
                )}
                <div className="ml-auto flex gap-2">
                  <Button variant="outline" size="sm" className="h-9 gap-1.5" disabled={!pendente || salvando} onClick={descartar}>
                    <RotateCcw className="h-3.5 w-3.5" />Descartar
                  </Button>
                  <Button size="sm" className="h-9 min-w-[190px] gap-1.5 font-bold uppercase tracking-wide" disabled={!pendente || salvando || !nome.trim()} onClick={salvar}>
                    {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}Salvar alterações
                  </Button>
                </div>
              </footer>
            </>
          )}
        </section>
      )}

      <PerfilDialog
        open={dialogo !== null}
        modo={dialogo ?? "criar"}
        nomeInicial={dialogo === "duplicar" && atual ? `${atual.name} (cópia)` : ""}
        descricaoInicial={dialogo === "duplicar" && atual ? atual.description ?? "" : ""}
        onClose={() => setDialogo(null)}
        onConfirmar={confirmarDialogo}
      />
    </div>
  );
}
