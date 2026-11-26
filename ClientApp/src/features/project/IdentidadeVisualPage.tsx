// Configurações → Identidade Visual dos Projetos (01/10/2026). Reaproveita EXATAMENTE a implementação existente da Logo do
// Projeto (mesmo componente, mesmas chamadas /api/project-identity, mesmo <ProjectLogo>, mesmo armazenamento em
// App_Data/project-identity). Dois modos: INDIVIDUAL (um projeto) e UPLOAD EM LOTE (vários projetos selecionados → UMA logo →
// aplicada a todos eles, com confirmação única quando alguma logo será substituída).
import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, CheckCircle2, ImagePlus, Images, Loader2, Palette, Trash2, X } from "lucide-react";
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { useProductionSelectionInterna } from "@/features/production/ProductionProjectSelector";
import { ProjectLogo, enviarLogoProjeto, removerLogoProjeto, useProjectLogoUrl, useRecarregarIdentidade, useTemLogo } from "@/features/project/ProjectIdentity";
import { useUserPreference } from "@/features/preferences/useUserPreference";
import { ehUmDe } from "@/features/preferences/userPreferences";
import SeletorProjetosTv from "@/features/tv/SeletorProjetosTv";
import { projetosAtivos } from "@/features/tv/tvLogic";
import { getProjetosRegistrados, normalizeAccId, type ProjetoRegistrado } from "@/lib/projectsRegistry";
import { cn } from "@/lib/utils";
import { problemaArquivo, resumoAplicacao, rotuloAplicar, TIPOS_LOGO } from "./logosEmLote";

const MODOS = ["individual", "lote"] as const;

export default function IdentidadeVisualPage() {
  const { can } = usePermissions();
  const { projetoAtual } = useProjetoAtual();
  // Mesma lista de projetos do restante do sistema (conta N&A no ACC); começa no projeto atual.
  // Projetos INTERNOS (/api/me/projects): a logo é guardada pelo Project_ID_ACC, sem Autodesk.
  const sel = useProductionSelectionInterna({ hubId: projetoAtual?.hubId, projectId: projetoAtual?.projectId });
  const projeto = sel.projects.find((p) => p.id === sel.projectId);
  const [modo, setModo] = useUserPreference<(typeof MODOS)[number]>("identidade-visual", "modo", "individual", { validar: ehUmDe(MODOS) });
  const podeEditar = can("settings.projects.edit");
  return (
    <div className="space-y-4 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <Palette className="h-6 w-6 text-primary" />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Identidade Visual dos Projetos</h1>
          <p className="text-sm text-muted-foreground">Logo de cada projeto, usada na Início, no menu, na TV e nos relatórios.</p>
        </div>
        <div role="group" aria-label="Modo" className="flex rounded-lg border border-border bg-background/60 p-0.5">
          {([["individual", "Individual"], ["lote", "Upload em lote"]] as const).map(([valor, rotulo]) => (
            <button key={valor} type="button" aria-pressed={modo === valor} onClick={() => setModo(valor)}
              className={cn("rounded-md px-3 py-1.5 text-xs font-black uppercase tracking-wider", modo === valor ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
              {rotulo}
            </button>
          ))}
        </div>
        {modo === "individual" && (
          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">
            Projeto
            <select aria-label="Projeto" className="h-9 w-80 max-w-full rounded-lg border border-border bg-background px-2 text-sm normal-case text-foreground"
              value={sel.projectId} disabled={sel.loadingHubs || sel.loadingProjects} onChange={(e) => sel.selecionarProjeto(e.target.value)}>
              <option value="">{sel.loadingHubs || sel.loadingProjects ? "Carregando projetos..." : "Selecione um projeto"}</option>
              {sel.projects.map((p) => <option key={p.id} value={p.id}>{p.attributes.name}</option>)}
            </select>
          </label>
        )}
      </section>
      {modo === "individual" ? (
        <IdentidadeVisualProjeto podeEditar={podeEditar} projetoAtual={projeto ? { projectId: projeto.id, nome: projeto.attributes.name } : null} />
      ) : podeEditar ? (
        <LogosEmLote projetos={sel.projects} carregando={sel.loadingHubs || sel.loadingProjects} />
      ) : (
        <p className="rounded-xl border border-dashed border-border p-5 text-sm text-muted-foreground">Você não tem permissão para alterar logos.</p>
      )}
    </div>
  );
}

type ResultadoProjeto = { id: string; nome: string; estado: "enviando" | "ok" | "erro"; mensagem?: string };

/** UPLOAD EM LOTE: projetos (multiselect, só ativos) + UMA logo → a mesma logo em TODOS os selecionados (mesma API/armazenamento). */
function LogosEmLote({ projetos, carregando }: { projetos: readonly import("@/lib/api").AccItem[]; carregando: boolean }) {
  const recarregar = useRecarregarIdentidade();
  const temLogo = useTemLogo();
  const input = useRef<HTMLInputElement>(null);
  const [registro, setRegistro] = useState<Map<string, ProjetoRegistrado> | null>(null);
  const [selecionados, setSelecionados] = useState<string[]>([]);
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [confirmando, setConfirmando] = useState(false);
  const [resultados, setResultados] = useState<ResultadoProjeto[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [arrastando, setArrastando] = useState(false);

  useEffect(() => { getProjetosRegistrados().then(setRegistro).catch(() => setRegistro(new Map())); }, []);
  // Só projetos ATIVOS (dbo.Projetos.Is_Ativo — mesmo registro da Início) entre os que o usuário enxerga no ACC.
  const ativos = useMemo(() => (registro
    ? projetosAtivos(projetos.map((p) => ({ id: p.id, nome: p.attributes.name, item: p })), (id) => registro.get(normalizeAccId(id))?.isAtivo).map((p) => p.item)
    : []), [projetos, registro]);
  // Destinos = seleção do topo (na ordem escolhida), restrita aos ativos. Não há associação individual.
  const alvos = useMemo(() => selecionados.map((id) => ativos.find((p) => p.id === id)).filter((p): p is (typeof ativos)[number] => !!p)
    .map((p) => ({ id: p.id, nome: p.attributes.name })), [selecionados, ativos]);
  const resumo = resumoAplicacao(alvos.map((p) => p.id), temLogo);

  useEffect(() => {
    if (!arquivo) { setPreview(null); return; }
    const url = URL.createObjectURL(arquivo);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [arquivo]);

  const escolherArquivo = (lista: FileList | File[] | null) => {
    const f = Array.from(lista ?? [])[0];
    if (!f) return;
    setArquivo(f);
    setResultados([]);
  };
  const problema = arquivo ? problemaArquivo(arquivo) : null;
  const podeAplicar = !!arquivo && !problema && alvos.length > 0 && !salvando;

  // Um projeto por vez, com a mesma chamada da logo individual; falha num projeto não desfaz os demais.
  const aplicar = async () => {
    if (!arquivo) return;
    setConfirmando(false);
    setSalvando(true);
    const lote = alvos.map((p): ResultadoProjeto => ({ ...p, estado: "enviando" }));
    setResultados(lote);
    for (const p of lote) {
      let r: ResultadoProjeto;
      try { await enviarLogoProjeto(p.id, arquivo); r = { ...p, estado: "ok" }; }
      catch (err) { r = { ...p, estado: "erro", mensagem: err instanceof Error ? err.message : "Falha ao salvar." }; }
      setResultados((atual) => atual.map((x) => (x.id === p.id ? r : x)));
    }
    recarregar();
    setSalvando(false);
  };
  // Confirmação única só quando alguma logo existente será substituída.
  const solicitarAplicacao = () => { if (resumo.substituir > 0) setConfirmando(true); else void aplicar(); };

  const ok = resultados.filter((r) => r.estado === "ok").length;
  const falhas = resultados.filter((r) => r.estado === "erro").length;

  return (
    <section className="space-y-5 rounded-xl border border-border bg-card/60 p-5">
      <div className="space-y-1">
        <span className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Projetos</span>
        <SeletorProjetosTv opcoes={ativos} selecionados={selecionados} onChange={setSelecionados} carregando={carregando || !registro} />
      </div>

      <div className="space-y-2">
        <span className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">Logo</span>
        <input ref={input} type="file" accept={TIPOS_LOGO.join(",")} className="hidden" aria-label="Logo"
          onChange={(e) => { escolherArquivo(e.target.files); e.target.value = ""; }} />
        {arquivo ? (
          <div className="flex flex-wrap items-center gap-4 rounded-lg border border-border p-3">
            <span className="flex h-20 w-36 flex-shrink-0 items-center justify-center overflow-hidden rounded-md bg-white">
              {preview && !problema ? <img src={preview} alt={arquivo.name} className="max-h-16 max-w-[90%] object-contain" /> : <ImagePlus className="h-5 w-5 text-muted-foreground" />}
            </span>
            <span className="min-w-[10rem] flex-1">
              <span className="block truncate text-sm font-bold">{arquivo.name}</span>
              {problema && <span className="text-xs text-destructive">{problema}</span>}
            </span>
            <Button size="sm" variant="outline" className="gap-2" disabled={salvando} onClick={() => input.current?.click()}><ImagePlus className="h-4 w-4" />Trocar arquivo</Button>
            <Button size="icon" variant="ghost" className="h-8 w-8" title="Remover logo" aria-label="Remover logo" disabled={salvando}
              onClick={() => { setArquivo(null); setResultados([]); }}><X className="h-4 w-4" /></Button>
          </div>
        ) : (
          <div onDragOver={(e) => { e.preventDefault(); setArrastando(true); }} onDragLeave={() => setArrastando(false)}
            onDrop={(e) => { e.preventDefault(); setArrastando(false); escolherArquivo(e.dataTransfer.files); }}
            className={cn("flex flex-wrap items-center justify-center gap-3 rounded-lg border-2 border-dashed px-4 py-6 text-sm text-muted-foreground",
              arrastando ? "border-primary bg-primary/5" : "border-border")}>
            <Images className="h-5 w-5" />Arraste a logo aqui (PNG, JPG ou WebP até 1 MB) ou
            <Button size="sm" variant="outline" className="gap-2" onClick={() => input.current?.click()}><ImagePlus className="h-4 w-4" />Selecionar logo</Button>
          </div>
        )}
      </div>

      <div className="space-y-3 rounded-lg bg-muted/40 p-3 text-sm">
        {alvos.length === 0 ? (
          <p className="text-muted-foreground">Selecione os projetos acima — a logo será aplicada a todos eles.</p>
        ) : (
          <div className="space-y-0.5">
            <p>Esta logo será aplicada a <strong>{resumo.total} {resumo.total === 1 ? "projeto" : "projetos"}</strong>.</p>
            {resumo.substituir > 0 && (
              <p className="text-xs text-muted-foreground">
                {resumo.semLogo} sem logo · <span className="font-bold text-amber-600 dark:text-amber-400">{resumo.substituir} terão a logo substituída</span>
              </p>
            )}
          </div>
        )}
        <Button className="gap-2" disabled={!podeAplicar} onClick={solicitarAplicacao}>
          {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}{rotuloAplicar(alvos.length)}
        </Button>
      </div>

      {resultados.length > 0 && (
        <div className="space-y-2">
          <span className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">
            Resultado{!salvando && ` — ${ok} salvo(s)${falhas > 0 ? `, ${falhas} com erro` : ""}`}
          </span>
          <ul className="divide-y divide-border rounded-lg border border-border">
            {resultados.map((r) => (
              <li key={r.id} className="flex flex-wrap items-center gap-3 px-3 py-2 text-sm">
                <ProjectLogo projectId={r.id} nome={r.nome} tamanho="sm" />
                <span className="min-w-[10rem] flex-1 truncate font-bold">{r.nome}</span>
                {r.estado === "enviando" && <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />}
                {r.estado === "ok" && <span className="flex items-center gap-1 text-xs font-bold text-emerald-600 dark:text-emerald-400"><CheckCircle2 className="h-3.5 w-3.5" />Salvo</span>}
                {r.estado === "erro" && <span className="flex items-center gap-1 text-xs text-destructive" title={r.mensagem}><AlertTriangle className="h-3.5 w-3.5" />Erro: {r.mensagem}</span>}
              </li>
            ))}
          </ul>
        </div>
      )}

      <AlertDialog open={confirmando} onOpenChange={setConfirmando}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Substituir logos existentes?</AlertDialogTitle>
            <AlertDialogDescription>
              {resumo.total} projetos selecionados: {resumo.semLogo} sem logo e {resumo.substituir} terão a logo atual substituída por "{arquivo?.name}".
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={() => void aplicar()}>{rotuloAplicar(resumo.total)}</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

export function IdentidadeVisualProjeto({ podeEditar, projetoAtual }: { podeEditar: boolean; projetoAtual: { projectId: string; nome?: string } | null }) {
  const recarregar = useRecarregarIdentidade();
  const urlAtual = useProjectLogoUrl(projetoAtual?.projectId);
  const input = useRef<HTMLInputElement>(null);
  const [ocupado, setOcupado] = useState(false);
  const [aviso, setAviso] = useState<{ ok: boolean; texto: string } | null>(null);

  if (!projetoAtual) {
    return (
      <section className="rounded-xl border border-dashed border-border p-5 text-sm text-muted-foreground">
        <h2 className="mb-1 text-sm font-black uppercase tracking-widest text-foreground">Logo do Projeto</h2>
        Selecione um projeto para configurar a identidade visual dele.
      </section>
    );
  }
  const nome = projetoAtual.nome ?? projetoAtual.projectId;

  const executar = async (acao: () => Promise<void>, sucesso: string) => {
    setOcupado(true); setAviso(null);
    try { await acao(); recarregar(); setAviso({ ok: true, texto: sucesso }); }
    catch (e) { setAviso({ ok: false, texto: e instanceof Error ? e.message : "Falha ao salvar a logo." }); }
    finally { setOcupado(false); if (input.current) input.current.value = ""; }
  };

  return (
    <section className="rounded-xl border border-border bg-card/60 p-5" aria-labelledby="titulo-logo">
      <div className="flex flex-wrap items-center gap-5">
        <ProjectLogo projectId={projetoAtual.projectId} nome={nome} tamanho="card" className="h-24 w-48 rounded-lg" />
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-[10px] font-black uppercase tracking-[0.3em] text-primary">Identidade Visual do Projeto</p>
          <h2 id="titulo-logo" className="text-lg font-black">Logo do Projeto — {nome}</h2>
          <p className="text-xs text-muted-foreground">
            Configurada uma única vez e usada em todo o N&amp;A Connect (Início, menu, TV e demais telas). PNG, JPG ou WebP até 1 MB;
            sem logo, é exibido o código do projeto.
          </p>
        </div>
        {podeEditar && (
          <div className="flex flex-wrap gap-2">
            <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" aria-label="Arquivo da logo"
              onChange={(e) => { const f = e.target.files?.[0]; if (f) void executar(() => enviarLogoProjeto(projetoAtual.projectId, f), "Logo atualizada."); }} />
            <Button size="sm" className="gap-2" disabled={ocupado} onClick={() => input.current?.click()}>
              {ocupado ? <Loader2 className="h-4 w-4 animate-spin" /> : <ImagePlus className="h-4 w-4" />}{urlAtual ? "Trocar logo" : "Enviar logo"}
            </Button>
            {urlAtual && (
              <Button size="sm" variant="ghost" className="gap-2 text-destructive hover:bg-destructive/10 hover:text-destructive" disabled={ocupado}
                onClick={() => void executar(() => removerLogoProjeto(projetoAtual.projectId), "Logo removida.")}>
                <Trash2 className="h-4 w-4" />Remover
              </Button>
            )}
          </div>
        )}
      </div>
      {aviso && (
        <p role="status" className={`mt-3 flex items-center gap-2 text-sm ${aviso.ok ? "text-emerald-600 dark:text-emerald-400" : "text-destructive"}`}>
          {aviso.ok ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}{aviso.texto}
        </p>
      )}
    </section>
  );
}
