// Seletor MULTIPROJETO das telas ANALÍTICAS (Resumo, Análise Financeira...). Não altera o projeto principal.
// Candidatos: projetos ATIVOS do usuário no N&A Connect (/api/me/projects — UserProjects, a mesma autoridade dos
// relatórios SQL; não usa Autodesk/hubId) E, quando o sistema aplica permissões, só os projetos em que ele tem a
// permissão da tela (resolvida por projeto em /api/permissions/me?projectId=). O backend continua validando cada
// projeto em cada chamada — isto evita oferecer o que ele não pode ver.
import { useEffect, useMemo, useState } from "react";
import { Check, ChevronDown, Layers, Loader2 } from "lucide-react";
import { getJson } from "@/lib/api";
import { ME_PROJECTS_ENDPOINT, projetosInternos, type MeProject } from "@/features/production/productionSelectionLogic";
import { limitedMap } from "@/components/pmo/pmoData";
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { buscarEfetivas, usePermissions } from "@/features/permissions/PermissionProvider";
import { criarCan } from "@/features/permissions/permissionLogic";
import { cn } from "@/lib/utils";
import { useProjetoAtual, type ProjetoAtual } from "./ProjetoAtualContext";
import { projetosPermitidos } from "./projetoAnalise";
import { separarCodigo } from "./ProjectIdentity";

interface Candidato { id: string; nome: string }

export default function SeletorProjetosAnalise({ keys }: { keys: readonly string[] }) {
  const { projetoAtual, projetosAnalise, definirProjetosAnalise } = useProjetoAtual();
  const { effective } = usePermissions();
  const [candidatos, setCandidatos] = useState<Candidato[] | null>(null);
  const [permitidos, setPermitidos] = useState<ReadonlyMap<string, boolean> | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const aplicaPermissao = !!effective && effective.enforced && !effective.isMasterAdmin;
  const temProjeto = !!projetoAtual?.projectId;
  const chaveKeys = keys.join("|");

  useEffect(() => {
    if (!temProjeto) return;
    const abort = new AbortController();
    setCandidatos(null); setPermitidos(null); setErro(null);
    (async () => {
      const d = await getJson<MeProject[]>(ME_PROJECTS_ENDPOINT, abort.signal);
      const lista = projetosInternos(d ?? []).map((p) => ({ id: p.id.startsWith("b.") ? p.id : `b.${p.id}`, nome: p.attributes.name }));
      if (abort.signal.aborted) return;
      setCandidatos(lista);
      if (!aplicaPermissao) return;
      const mapa = new Map<string, boolean>();
      await limitedMap(lista, async (c) => {
        try { mapa.set(c.id, criarCan(await buscarEfetivas(c.id, abort.signal), "project")(chaveKeys.split("|"))); }
        catch { mapa.set(c.id, false); }                                  // sem resposta = não oferecido
      }, 4);
      if (!abort.signal.aborted) setPermitidos(mapa);
    })().catch((e) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Não foi possível listar os projetos."); });
    return () => abort.abort();
  }, [temProjeto, aplicaPermissao, chaveKeys]);

  const opcoes = useMemo(() => {
    if (!candidatos) return null;
    if (aplicaPermissao && !permitidos) return null;
    return projetosPermitidos(candidatos, aplicaPermissao ? permitidos : null);
  }, [candidatos, permitidos, aplicaPermissao]);

  // Seleção salva que perdeu a permissão/acesso é descartada.
  useEffect(() => {
    if (!opcoes) return;
    const validos = projetosAnalise.filter((p) => opcoes.some((o) => o.id === p.projectId));
    if (validos.length !== projetosAnalise.length) definirProjetosAnalise(validos);
  }, [opcoes, projetosAnalise, definirProjetosAnalise]);

  if (!projetoAtual) return null;
  const marcado = (id: string) => projetosAnalise.some((p) => p.projectId === id);
  const alternar = (c: Candidato) => {
    const lista: ProjetoAtual[] = marcado(c.id)
      ? projetosAnalise.filter((p) => p.projectId !== c.id)
      : [...projetosAnalise, { hubId: projetoAtual.hubId, projectId: c.id, nome: c.nome }];
    if (lista.length > 0) definirProjetosAnalise(lista);                 // nunca vazia
  };
  const rotulo = (n?: string) => (n ? separarCodigo(n).codigo ?? n : "—");
  const resumo = projetosAnalise.length <= 1 ? rotulo(projetosAnalise[0]?.nome ?? projetoAtual.nome) : `${projetosAnalise.length} projetos`;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button type="button" className="flex h-8 items-center gap-2 rounded-lg border border-border bg-card/60 px-3 text-xs font-bold hover:border-primary/50"
          title="Projetos considerados nesta análise (o projeto principal não muda)">
          <Layers className="h-3.5 w-3.5 text-primary" />
          <span className="text-muted-foreground">Analisar:</span> {resumo}
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="max-h-[60vh] w-80 overflow-y-auto">
        <DropdownMenuLabel className="text-[10px] uppercase tracking-widest">Projetos da análise</DropdownMenuLabel>
        <p className="px-2 pb-2 text-[11px] text-muted-foreground">
          Principal: <strong>{projetoAtual.nome ?? projetoAtual.projectId}</strong>. Telas operacionais continuam nele.
        </p>
        <DropdownMenuSeparator />
        {erro ? <p className="px-2 py-3 text-xs text-destructive">{erro}</p>
          : !opcoes ? <p className="flex items-center gap-2 px-2 py-3 text-xs text-muted-foreground"><Loader2 className="h-3.5 w-3.5 animate-spin" />Verificando acesso…</p>
          : opcoes.length === 0 ? <p className="px-2 py-3 text-xs text-muted-foreground">Nenhum projeto disponível.</p>
          : opcoes.map((c) => {
            const m = marcado(c.id);
            return (
              <button key={c.id} type="button" role="menuitemcheckbox" aria-checked={m} onClick={() => alternar(c)}
                disabled={m && projetosAnalise.length === 1}
                className={cn("flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-xs hover:bg-accent disabled:opacity-60",
                  c.id === projetoAtual.projectId && "font-bold")}>
                <span className={cn("flex h-4 w-4 flex-shrink-0 items-center justify-center rounded border", m ? "border-primary bg-primary text-primary-foreground" : "border-border")}>
                  {m && <Check className="h-3 w-3" />}
                </span>
                <span className="min-w-0 flex-1 truncate">{c.nome}</span>
                {c.id === projetoAtual.projectId && <span className="text-[9px] uppercase text-primary">principal</span>}
              </button>
            );
          })}
        {projetosAnalise.length > 1 && (
          <>
            <DropdownMenuSeparator />
            <button type="button" className="w-full px-2 py-1.5 text-left text-xs text-primary hover:underline"
              onClick={() => definirProjetosAnalise([projetoAtual])}>Voltar a analisar só o projeto principal</button>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
