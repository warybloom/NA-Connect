import { useEffect, useMemo, useState } from "react";
import { FolderKanban } from "lucide-react";
import { ApiError, getJson, type AccItem, type AccList } from "@/lib/api";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { ME_PROJECTS_ENDPOINT, montarSelecao, projetoInicial, projetosInternos, type MeProject } from "./productionSelectionLogic";

/**
 * Seleção global do Relatório de Produção: Conta → UM projeto (sem "Todos" nesta fase). Mesmas fontes do
 * PMO (/DataManagement/Data/Hubs e /DataManagement/Data/Projetos). hub/projeto iniciais podem vir de fora
 * (ex.: query string) — base para a futura integração CDE → Relatório de Produção. O acesso continua
 * sendo validado por endpoint (IProjectReportAccess); isto é só a escolha de escopo na UI.
 */
export const CONTA_PADRAO = "N&A Consultores LTDA";
const normalizar = (s: string) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toUpperCase();
const ehContaNA = (h: AccItem) => normalizar(h.attributes.name).includes("N&A CONSULTORES");

export function useProductionSelection(inicial: { hubId?: string | null; projectId?: string | null }) {
  const [hubs, setHubs] = useState<AccItem[]>([]);
  const [projects, setProjects] = useState<AccItem[]>([]);
  const [hubId, setHubId] = useState(inicial.hubId ?? "");
  const [projectId, setProjectId] = useState(inicial.projectId ?? "");
  const [loadingHubs, setLoadingHubs] = useState(true);
  const [loadingProjects, setLoadingProjects] = useState(false);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const abort = new AbortController();
    getJson<AccList>("/DataManagement/Data/Hubs", abort.signal)
      .then(d => {
        if (abort.signal.aborted) return;
        const lista = d.data ?? [];
        setHubs(lista);
        // Conta fixa do produto: N&A Consultores LTDA (não é mais um filtro exposto na tela). hub da URL só vale
        // se existir; senão a conta N&A; na ausência dela, a primeira conta acessível.
        setHubId(atual => (lista.some(h => h.id === atual) ? atual : (lista.find(ehContaNA) ?? lista[0])?.id ?? ""));
      })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : "Não foi possível carregar as contas."); })
      .finally(() => { if (!abort.signal.aborted) setLoadingHubs(false); });
    return () => abort.abort();
  }, []);

  useEffect(() => {
    if (!hubId) { setProjects([]); return; }
    const abort = new AbortController();
    setLoadingProjects(true); setProjects([]);
    getJson<AccList>(`/DataManagement/Data/Projetos?hubId=${encodeURIComponent(hubId)}`, abort.signal)
      .then(d => {
        if (abort.signal.aborted) return;
        const lista = [...(d.data ?? [])].sort((a, b) => a.attributes.name.localeCompare(b.attributes.name, "pt-BR"));
        setProjects(lista);
        // Projeto que não pertence à conta atual (ou sem acesso) nunca permanece selecionado.
        setProjectId(atual => (lista.some(p => p.id === atual) ? atual : ""));
      })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : "Não foi possível carregar os projetos."); })
      .finally(() => { if (!abort.signal.aborted) setLoadingProjects(false); });
    return () => abort.abort();
  }, [hubId]);

  const projetoSelecionado = projects.find(p => p.id === projectId);

  // Hoje sempre 0 ou 1 projeto; o formato já é o de portfólio (PmoSelecionado[]).
  const selection = useMemo<PmoSelecionado[]>(
    () => (projetoSelecionado ? [{ id: projetoSelecionado.id, hubId, name: projetoSelecionado.attributes.name }] : []),
    [projetoSelecionado, hubId]
  );

  const contaAtual = hubs.find(h => h.id === hubId);
  return {
    hubs, projects, hubId, projectId, selection, error, loadingHubs, loadingProjects,
    contaNome: contaAtual?.attributes.name, contaEhPadrao: contaAtual ? ehContaNA(contaAtual) : false,
    selecionarHub: (id: string) => { setHubId(id); setProjectId(""); },
    selecionarProjeto: setProjectId,
  };
}

/**
 * Seleção do CONTROLE DE PRODUÇÃO (relatórios SQL). A lista de projetos vem da fonte INTERNA (/api/me/projects:
 * UserProjects + dbo.Projetos), então funciona para usuário N&A sem Autodesk. O projeto inicial vem da URL ou do
 * PROJETO ATUAL. Nenhuma chamada Autodesk (Hubs/Projetos): hubId é só repassado quando já existe (URL/projeto atual)
 * e os relatórios SQL não o exigem. Mesmo formato de retorno de useProductionSelection.
 */
export function useProductionSelectionInterna(inicial: { hubId?: string | null; projectId?: string | null },
  /** Fonte interna da lista (mesmo formato): /api/me/projects (padrão) ou a administrativa da TV. */
  fonte: string = ME_PROJECTS_ENDPOINT) {
  const [projects, setProjects] = useState<AccItem[]>([]);
  const [hubId, setHubId] = useState(inicial.hubId ?? "");
  const [projectId, setProjectId] = useState(inicial.projectId ?? "");
  const [loadingProjects, setLoadingProjects] = useState(true);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const abort = new AbortController();
    getJson<MeProject[]>(fonte, abort.signal)
      .then(d => {
        if (abort.signal.aborted) return;
        const lista = projetosInternos(d ?? []);
        setProjects(lista);
        setProjectId(atual => projetoInicial(lista, atual));
      })
      .catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e.message : "Não foi possível carregar os projetos."); })
      .finally(() => { if (!abort.signal.aborted) setLoadingProjects(false); });
    return () => abort.abort();
  }, [fonte]);

  const selection = useMemo<PmoSelecionado[]>(() => montarSelecao(projects, projectId, hubId), [projects, projectId, hubId]);
  return {
    hubs: [] as AccItem[], projects, hubId, projectId, selection, error, loadingHubs: false, loadingProjects,
    contaNome: undefined as string | undefined, contaEhPadrao: false,
    selecionarHub: (id: string) => setHubId(id),
    selecionarProjeto: setProjectId,
  };
}

type Selecao = ReturnType<typeof useProductionSelection>;

/** Seletor compacto de projeto (a conta é fixa e não aparece como filtro). */
export default function ProductionProjectSelector({ s }: { s: Selecao }) {
  return (
    <label className="flex items-center gap-2 text-xs text-muted-foreground">
      <FolderKanban className="h-3.5 w-3.5 flex-shrink-0" />
      <span className="sr-only">Projeto</span>
      <select aria-label="Projeto" value={s.projectId} disabled={s.projects.length === 0 || s.loadingProjects || s.loadingHubs}
        onChange={e => s.selecionarProjeto(e.target.value)}
        className="h-8 w-72 max-w-full rounded-lg border border-border bg-background px-2 text-sm text-foreground disabled:opacity-50">
        <option value="">{s.loadingHubs || s.loadingProjects ? "Carregando projetos..." : "Selecione um projeto"}</option>
        {s.projects.map(p => <option key={p.id} value={p.id}>{p.attributes.name}</option>)}
      </select>
    </label>
  );
}
