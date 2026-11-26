// Projeto Plant 3D efetivo: projeto atual (useProjetoAtual) tem prioridade; o seletor só existe sem projeto atual.
import { useCallback, useEffect, useState } from "react";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { PlantProject, descreverErro, isAbort, plantRequest } from "./plantApi";

export function usePlantProjects() {
  const { projetoAtual } = useProjetoAtual();
  const atualId = projetoAtual?.projectId ?? "";
  const [projects, setProjects] = useState<PlantProject[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");
  const [escolhido, setEscolhido] = useState("");
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    setError("");
    (async () => {
      try {
        const r = await plantRequest<{ projects: PlantProject[] }>("/projects", { signal: abort.signal });
        if (abort.signal.aborted) return;
        setProjects((r.projects ?? []).map(p => ({ ...p, dashboardProjectId: p.dashboardProjectId ?? null })));
        setLoaded(true);
      } catch (e) {
        if (isAbort(e)) return;
        setError(descreverErro(e, "Lista de projetos Plant 3D"));
        setLoaded(true);
      }
    })();
    return () => abort.abort();
  }, [tick]);

  const projectIdAcc = atualId || escolhido;
  const entry = projects.find(p => p.projectIdAcc === projectIdAcc) ?? null;
  const nome = entry?.name || projetoAtual?.nome || "";
  /** true = lista carregada e o projeto não tem vínculo; null = ainda não dá para afirmar. */
  const naoVinculado = loaded && !error && entry ? entry.dashboardProjectId === null : null;

  const reload = useCallback(() => setTick(t => t + 1), []);

  return {
    projects, loaded, error, reload,
    projectIdAcc, nome, entry, naoVinculado,
    temProjetoAtual: !!atualId,
    escolhido, setEscolhido,
  };
}
