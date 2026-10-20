// USUÁRIOS: PROJETO → membros (preenchidos automaticamente pelo login e pela sincronização ACC) → painel do usuário.
// Quem entra pela Autodesk é registrado com os seus projetos ACC e recebe o perfil padrão (Somente Leitura) nos projetos
// sem perfil. "Sincronizar usuários do ACC" traz todos os membros do projeto.
// Usuário N&A (sem ACC): cadastro manual (nome + e-mail) → senha no painel do usuário → vínculo/perfis no projeto.
import { useCallback, useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { CheckSquare, Globe, Layers, Loader2, RefreshCw, Search, UserPlus, Users, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { getProjetosRegistrados, type ProjetoRegistrado } from "@/lib/projectsRegistry";
import { usePermissions } from "../PermissionProvider";
import UserAccessDialog, { Avatar } from "./UserAccessDialog";
import BulkProfilesDialog from "./BulkProfilesDialog";
import type { CatalogResource } from "./permissionTreeLogic";
import { permissionsApi, type MembroProjeto, type MembrosDoProjeto, type PermProfile, type PermUser, type ResumoSincronizacao } from "./permissionsAdminApi";

interface Props { catalogo: CatalogResource[]; onErro: (m: string) => void; onAviso: (m: string) => void }

const GLOBAL = "__global__";

const dataHora = (iso: string | null) => (iso ? new Date(iso.endsWith("Z") || iso.includes("+") ? iso : `${iso}Z`).toLocaleString("pt-BR") : "—");

function ResumoSync({ r }: { r: ResumoSincronizacao }) {
  const itens: [string, number][] = [["encontrados", r.encontrados], ["novos", r.usuariosCriados + r.vinculados], ["reativados", r.reativados],
    ["desativados", r.desativados], ["Somente Leitura atribuído", r.perfisPadraoAtribuidos]];
  return (
    <span className="text-xs text-muted-foreground">
      Última sincronização ACC: <strong className="text-foreground">{dataHora(r.em)}</strong>
      {itens.map(([rotulo, n]) => <span key={rotulo}> · {n} {rotulo}</span>)}
    </span>
  );
}

function LinhaMembro({ m, selecionado, onSelecionar, onClick }: { m: MembroProjeto; selecionado: boolean; onSelecionar: (v: boolean) => void; onClick: () => void }) {
  const inativo = m.ativoNoProjeto === false;
  return (
    <div className={cn("flex items-center gap-3 rounded-lg border bg-background/40 pl-3 transition-colors",
      selecionado ? "border-primary bg-primary/5" : "border-border hover:border-primary/50", inativo && "opacity-60")}>
      <input type="checkbox" aria-label={`Selecionar ${m.nome ?? m.email}`} className="h-4 w-4 shrink-0 cursor-pointer accent-primary"
        checked={selecionado} onChange={(e) => onSelecionar(e.target.checked)} />
    <button type="button" onClick={onClick}
      className="grid min-w-0 flex-1 grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_auto] items-center gap-4 rounded-r-lg px-3 py-3 text-left hover:bg-muted/40">
      <span className="flex min-w-0 items-center gap-3">
        <Avatar nome={m.nome} email={m.email} />
        <span className="min-w-0">
          <span className="block truncate text-sm font-semibold text-foreground">{m.nome ?? m.email}</span>
          <span className="block truncate text-xs text-muted-foreground">{m.email}</span>
        </span>
      </span>
      <span className="flex min-w-0 flex-wrap gap-1">
        {m.perfisDoProjeto.length === 0 && m.perfisGlobais.length === 0 && <span className="text-xs text-muted-foreground">Sem perfil</span>}
        {m.perfisDoProjeto.map((p) => <span key={p} className="rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-semibold text-primary">{p}</span>)}
        {m.perfisGlobais.map((p) => (
          <span key={`g-${p}`} title="Perfil global (vale em todos os projetos)" className="flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-[11px] font-semibold text-muted-foreground">
            <Globe className="h-3 w-3" />{p}
          </span>
        ))}
        {m.excecoes > 0 && <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-[11px] font-semibold text-amber-500">{m.excecoes} exceç{m.excecoes === 1 ? "ão" : "ões"}</span>}
      </span>
      <span className="text-right text-[11px]">
        {m.ativoNoProjeto === true && <span className="font-semibold text-emerald-500">Ativo</span>}
        {inativo && <span className="font-semibold text-muted-foreground">Inativo no projeto</span>}
        {m.ativoNoProjeto === null && <span className="text-muted-foreground">Sem vínculo ACC</span>}
        <span className="block text-muted-foreground">{m.ultimoAcessoEm ? `Último acesso ${dataHora(m.ultimoAcessoEm)}` : "Ainda não entrou"}</span>
      </span>
    </button>
    </div>
  );
}

export default function UsersPanel({ catalogo, onErro }: Props) {
  const { effective } = usePermissions();
  const [projetos, setProjetos] = useState<ProjetoRegistrado[] | null>(null);
  const [escopo, setEscopo] = useState<string>("");
  const [dados, setDados] = useState<MembrosDoProjeto | null>(null);
  const [usuariosGlobais, setUsuariosGlobais] = useState<PermUser[] | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [sincronizando, setSincronizando] = useState(false);
  const [perfis, setPerfis] = useState<PermProfile[]>([]);
  const [busca, setBusca] = useState("");
  const [aberto, setAberto] = useState<number | null>(null);
  // Seleção por id (não por índice): a busca/filtro nunca apaga o que já foi marcado.
  const [selecionados, setSelecionados] = useState<Map<number, string>>(new Map());
  const [lote, setLote] = useState<"projeto" | "multi" | null>(null);
  const [novoNome, setNovoNome] = useState("");
  const [novoEmail, setNovoEmail] = useState("");
  const [criando, setCriando] = useState(false);
  const [todosUsuarios, setTodosUsuarios] = useState<PermUser[]>([]);
  const [vincular, setVincular] = useState("");

  useEffect(() => {
    permissionsApi.perfis().then(setPerfis).catch(() => onErro("Falha ao carregar perfis."));
    getProjetosRegistrados()
      .then((m) => {
        const ativos = [...m.values()].filter((p) => p.isAtivo && p.projectIdAcc).sort((a, b) => a.nomeProjeto.localeCompare(b.nomeProjeto));
        setProjetos(ativos);
        setEscopo((atual) => atual || ativos[0]?.projectIdAcc || GLOBAL);
      })
      .catch(() => { setProjetos([]); onErro("Falha ao carregar projetos."); });
  }, [onErro]);

  const carregar = useCallback(async () => {
    if (!escopo) return;
    setCarregando(true);
    try {
      if (escopo === GLOBAL) { setDados(null); setUsuariosGlobais(await permissionsApi.usuarios()); }
      else { setUsuariosGlobais(null); setDados(await permissionsApi.membrosDoProjeto(escopo)); }
    } catch (e) {
      setDados(null); setUsuariosGlobais(null);
      onErro(e instanceof Error ? e.message : "Falha ao carregar usuários.");
    } finally { setCarregando(false); }
  }, [escopo, onErro]);

  useEffect(() => { void carregar(); }, [carregar]);

  const carregarTodos = useCallback(() => {
    permissionsApi.usuarios().then(setTodosUsuarios).catch(() => setTodosUsuarios([]));
  }, []);
  useEffect(() => { carregarTodos(); }, [carregarTodos]);

  // Cadastro manual N&A: não cria vínculo nem perfil. Abre o painel do usuário no escopo atual, onde o administrador
  // define a senha e — se for um projeto — ativa o vínculo (UserProjects) e atribui perfis pelo fluxo existente.
  const criarUsuarioNa = async () => {
    const email = novoEmail.trim();
    if (!email.includes("@")) { onErro("Informe um e-mail válido."); return; }
    setCriando(true);
    try {
      const { userId } = await permissionsApi.cadastrarUsuario(email, novoNome.trim() || null);
      toast.success("Usuário N&A cadastrado. Defina a senha e o acesso.");
      setNovoNome(""); setNovoEmail("");
      carregarTodos();
      await carregar();
      setAberto(userId);
    } catch (e) {
      onErro(e instanceof Error ? e.message : "Falha ao cadastrar usuário.");
    } finally { setCriando(false); }
  };

  const sincronizar = async () => {
    if (escopo === GLOBAL) return;
    setSincronizando(true);
    try {
      const r = await permissionsApi.sincronizarProjeto(escopo);
      toast.success(`Sincronizado: ${r.encontrados} membros no ACC · ${r.usuariosCriados + r.vinculados} novos · ${r.reativados} reativados · ${r.desativados} desativados.`);
      await carregar();
    } catch (e) {
      onErro(e instanceof Error ? e.message : "Falha na sincronização com o ACC.");
    } finally { setSincronizando(false); }
  };

  const nomeProjeto = escopo === GLOBAL ? "Global (todos os projetos)" : projetos?.find((p) => p.projectIdAcc === escopo)?.nomeProjeto ?? escopo;
  const t = busca.trim().toLowerCase();
  const membros = useMemo(() => {
    const base: MembroProjeto[] = escopo === GLOBAL
      ? (usuariosGlobais ?? []).map((u) => ({ userId: u.userId, email: u.email, nome: u.displayName, autodeskUserId: u.accUserId, ativoNoProjeto: null,
          ultimoAcessoEm: u.ultimoAcessoEm, perfisDoProjeto: [], perfisGlobais: [], excecoes: 0 }))
      : dados?.membros ?? [];
    return base.filter((m) => !t || `${m.nome ?? ""} ${m.email}`.toLowerCase().includes(t));
  }, [escopo, usuariosGlobais, dados, t]);
  const ativos = membros.filter((m) => m.ativoNoProjeto !== false).length;
  const selecionar = (m: MembroProjeto, v: boolean) => setSelecionados((atual) => {
    const n = new Map(atual); if (v) n.set(m.userId, m.nome ?? m.email); else n.delete(m.userId); return n;
  });
  const selecionarVisiveis = () => setSelecionados((atual) => { const n = new Map(atual); membros.forEach((m) => n.set(m.userId, m.nome ?? m.email)); return n; });
  const usuariosLote = [...selecionados].map(([userId, nome]) => ({ userId, nome }));
  const membrosIds = new Set((dados?.membros ?? []).map((m) => m.userId));
  const foraDoProjeto = escopo === GLOBAL ? [] : todosUsuarios.filter((u) => !membrosIds.has(u.userId));

  return (
    <div className="space-y-4">
      <section className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card/60 p-4">
        <label className="flex min-w-[280px] flex-col gap-1.5 text-xs font-semibold text-muted-foreground">
          Projeto
          <select className="h-10 rounded-md border border-border bg-background px-3 text-sm font-medium text-foreground"
            value={escopo} onChange={(e) => { setEscopo(e.target.value); setBusca(""); setSelecionados(new Map()); }} disabled={projetos === null}>
            {projetos === null && <option value="">Carregando projetos…</option>}
            {projetos?.map((p) => <option key={p.projectIdAcc} value={p.projectIdAcc}>{p.nomeProjeto}</option>)}
            <option value={GLOBAL}>Global — perfis válidos em todos os projetos</option>
          </select>
        </label>
        <div className="relative w-64">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input className="h-10 pl-8" placeholder="Buscar usuário…" value={busca} onChange={(e) => setBusca(e.target.value)} />
        </div>
        {escopo !== GLOBAL && (
          <Button variant="outline" className="ml-auto h-10 gap-2" onClick={sincronizar} disabled={sincronizando || carregando || !escopo}>
            {sincronizando ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
            Sincronizar usuários do ACC
          </Button>
        )}
      </section>

      <section className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card/60 p-4" aria-label="Usuário N&A sem ACC">
        <form className="flex flex-wrap items-end gap-3" onSubmit={(e) => { e.preventDefault(); void criarUsuarioNa(); }}>
          <label className="flex w-56 flex-col gap-1.5 text-xs font-semibold text-muted-foreground">
            Nome
            <Input className="h-10" value={novoNome} onChange={(e) => setNovoNome(e.target.value)} maxLength={200} autoComplete="off" />
          </label>
          <label className="flex w-64 flex-col gap-1.5 text-xs font-semibold text-muted-foreground">
            E-mail
            <Input className="h-10" type="email" required value={novoEmail} onChange={(e) => setNovoEmail(e.target.value)} maxLength={256} autoComplete="off" />
          </label>
          <Button type="submit" className="h-10 gap-2" disabled={criando || !novoEmail.trim()}>
            {criando ? <Loader2 className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}
            Criar usuário N&amp;A
          </Button>
        </form>
        {escopo !== GLOBAL && foraDoProjeto.length > 0 && (
          <label className="ml-auto flex min-w-[260px] flex-col gap-1.5 text-xs font-semibold text-muted-foreground">
            Vincular usuário existente a este projeto
            <select className="h-10 rounded-md border border-border bg-background px-3 text-sm font-medium text-foreground" value={vincular}
              onChange={(e) => { const id = Number(e.target.value); setVincular(""); if (id) setAberto(id); }}>
              <option value="">Selecionar usuário…</option>
              {foraDoProjeto.map((u) => <option key={u.userId} value={u.userId}>{u.displayName ? `${u.displayName} — ${u.email}` : u.email}</option>)}
            </select>
          </label>
        )}
        <p className="basis-full text-[11px] text-muted-foreground">
          Usuário N&amp;A entra por e-mail e senha, sem conta Autodesk. O cadastro não concede projeto nem perfil: abra o usuário para definir a senha, ativar o vínculo no projeto e atribuir perfis.
        </p>
      </section>

      <section className="rounded-xl border border-border bg-card/60">
        <header className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-3">
          <Users className="h-4 w-4 text-primary" />
          <h3 className="text-sm font-black uppercase tracking-widest text-foreground">{nomeProjeto}</h3>
          <span className="text-xs text-muted-foreground">{membros.length} usuário{membros.length === 1 ? "" : "s"}{escopo !== GLOBAL && ` · ${ativos} ativo${ativos === 1 ? "" : "s"}`}</span>
          {membros.length > 0 && (
            <button type="button" onClick={selecionarVisiveis} className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
              <CheckSquare className="h-3.5 w-3.5" />Selecionar todos visíveis
            </button>
          )}
          <span className="ml-auto">
            {escopo !== GLOBAL && (dados?.ultimaSincronizacao
              ? <ResumoSync r={dados.ultimaSincronizacao} />
              : dados && <span className="text-xs text-muted-foreground">Membros ACC ainda não sincronizados para este projeto.</span>)}
          </span>
        </header>
        {selecionados.size > 0 && (
          <div className="sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b border-primary/30 bg-primary/10 px-4 py-2.5 backdrop-blur">
            <span className="text-sm font-bold text-primary">{selecionados.size} usuário{selecionados.size === 1 ? "" : "s"} selecionado{selecionados.size === 1 ? "" : "s"}</span>
            <span className="ml-auto flex flex-wrap gap-2">
              {escopo !== GLOBAL && (
                <Button size="sm" className="h-8 gap-1.5" onClick={() => setLote("projeto")}><Layers className="h-3.5 w-3.5" />Aplicar perfis</Button>
              )}
              <Button size="sm" variant="outline" className="h-8 gap-1.5" onClick={() => setLote("multi")}><Layers className="h-3.5 w-3.5" />Aplicar em vários projetos</Button>
              <Button size="sm" variant="ghost" className="h-8 gap-1.5" onClick={() => setSelecionados(new Map())}><X className="h-3.5 w-3.5" />Limpar seleção</Button>
            </span>
          </div>
        )}
        <div className="space-y-2 p-3">
          {carregando && <p className="flex items-center gap-2 px-1 py-4 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando usuários…</p>}
          {!carregando && membros.length === 0 && (
            <div className="px-1 py-8 text-center text-sm text-muted-foreground">
              {escopo === GLOBAL
                ? "Nenhum usuário registrado ainda. Usuários Autodesk aparecem ao entrar no N&A Connect; usuários N&A são criados acima."
                : "Nenhum usuário vinculado a este projeto ainda. Use “Sincronizar usuários do ACC” para trazer os membros do projeto."}
            </div>
          )}
          {!carregando && membros.map((m) => (
            <LinhaMembro key={m.userId} m={m} selecionado={selecionados.has(m.userId)} onSelecionar={(v) => selecionar(m, v)} onClick={() => setAberto(m.userId)} />
          ))}
        </div>
      </section>

      <UserAccessDialog
        open={aberto !== null}
        onClose={() => setAberto(null)}
        userId={aberto}
        projectIdAcc={escopo === GLOBAL ? null : escopo}
        nomeEscopo={nomeProjeto}
        perfis={perfis}
        catalogo={catalogo}
        ehVoceMesmo={(email) => !!effective?.email && email.toLowerCase() === effective.email.toLowerCase()}
        onAlterado={() => { void carregar(); }}
        projetos={projetos ?? []}
        podeDefinirSenha
      />

      <BulkProfilesDialog
        open={lote !== null}
        onClose={() => setLote(null)}
        usuarios={usuariosLote}
        projetos={projetos ?? []}
        projetoAtual={escopo === GLOBAL ? "" : escopo}
        multiProjeto={lote === "multi"}
        perfis={perfis}
        onConcluido={() => { void carregar(); }}
      />
    </div>
  );
}
