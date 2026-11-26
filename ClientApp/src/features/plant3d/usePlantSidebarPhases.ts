// Fases salvas do projeto ATUAL para o sidebar dinâmico (AUDITORIA > Plant 3D > Resultados > <fase> > 3 telas).
// Cache em memória por projeto (TTL curto) com atualização imediata após salvar fase — sem duplicar itens (o sidebar
// deduplica por id) e sem novas requisições a cada render. Falha de rede NÃO derruba o menu: mantém o último estado.
import { useEffect, useState } from "react";
import { getPhasesComCarimbo } from "@/components/plant3d/plantRulesApi";
import { isAbort } from "@/components/plant3d/plantApi";
import type { PlantFaseNav } from "@/features/sidebar/officialNavigation";

const EVENTO = "plant3d:sidebar-phases";
const TTL_MS = 5 * 60_000;

type Entrada = { fases: PlantFaseNav[]; em: number };
const cache = new Map<string, Entrada>();
const emVoo = new Map<string, Promise<PlantFaseNav[]>>();

const paraNav = (fases: ReadonlyArray<{ id: string; name: string }>): PlantFaseNav[] => fases.map(f => ({ id: f.id, name: f.name }));
const vigente = (e: Entrada | undefined): e is Entrada => !!e && Date.now() - e.em < TTL_MS;

/** Sem AbortSignal de propósito: a requisição é compartilhada entre assinantes; quem desmonta só ignora o resultado. */
async function carregar(projectIdAcc: string): Promise<PlantFaseNav[]> {
  const pendente = emVoo.get(projectIdAcc);
  if (pendente) return pendente;
  const p = getPhasesComCarimbo(projectIdAcc)
    .then(r => { const fases = paraNav(r.phases); cache.set(projectIdAcc, { fases, em: Date.now() }); return fases; })
    .finally(() => emVoo.delete(projectIdAcc));
  emVoo.set(projectIdAcc, p);
  return p;
}

/** Chamado após salvar fases: aplica a lista salva no cache e avisa o sidebar (refresh imediato, sem nova ida ao servidor). */
export function publicarFasesNoSidebar(projectIdAcc: string, fases: ReadonlyArray<{ id: string; name: string }>) {
  if (!projectIdAcc) return;
  cache.set(projectIdAcc, { fases: paraNav(fases), em: Date.now() });
  window.dispatchEvent(new CustomEvent(EVENTO, { detail: projectIdAcc }));
}

/** Força nova leitura do servidor na próxima oportunidade (ex.: botão Atualizar). */
export function invalidarFasesDoSidebar(projectIdAcc?: string) {
  if (projectIdAcc) cache.delete(projectIdAcc); else cache.clear();
  window.dispatchEvent(new CustomEvent(EVENTO, { detail: projectIdAcc ?? "" }));
}

/** `ativo` = só quem enxerga a AUDITORIA (MasterAdmin) consulta fases; os demais não geram nenhuma requisição. */
export function usePlantSidebarPhases(projectIdAcc: string | undefined, ativo: boolean): PlantFaseNav[] {
  const [estado, setEstado] = useState<{ projeto: string; fases: PlantFaseNav[] } | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const aoMudar = (e: Event) => {
      const alvo = (e as CustomEvent<string>).detail;
      if (!alvo || alvo === projectIdAcc) setTick(t => t + 1);
    };
    window.addEventListener(EVENTO, aoMudar);
    return () => window.removeEventListener(EVENTO, aoMudar);
  }, [projectIdAcc]);

  useEffect(() => {
    if (!ativo || !projectIdAcc) { setEstado(null); return; }
    const emCache = cache.get(projectIdAcc);
    if (emCache) setEstado({ projeto: projectIdAcc, fases: emCache.fases });
    if (vigente(emCache)) return;
    const abort = new AbortController();
    carregar(projectIdAcc)
      .then(fases => { if (!abort.signal.aborted) setEstado({ projeto: projectIdAcc, fases }); })
      .catch(e => { if (!isAbort(e)) console.warn("Sidebar Plant 3D: fases indisponíveis.", e); });
    return () => abort.abort();
  }, [projectIdAcc, ativo, tick]);

  return ativo && projectIdAcc && estado?.projeto === projectIdAcc ? estado.fases : EMPTY;
}

const EMPTY: PlantFaseNav[] = [];
