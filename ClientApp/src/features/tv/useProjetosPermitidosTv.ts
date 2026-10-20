// Projetos que podem ser escolhidos na Configuração de Telas para TV:
//   • ELEGÍVEIS: projetos N&A ATIVOS já sincronizados (GET /api/tv-config/projects — fonte administrativa interna,
//     sem Autodesk e sem depender do AccessToken; a lista recebida já vem filtrada por Is_Ativo);
//   • quando o sistema aplica permissões: com permissão para ao menos uma tela da TV no projeto (permissões efetivas
//     resolvidas pelo backend em /api/permissions/me?projectId=). Ao ADICIONAR, podeTela() confere a tela escolhida.
// Mesmo mecanismo do seletor "Analisar" — nenhuma regra de permissão nova.
import { useCallback, useEffect, useMemo, useState } from "react";
import type { AccItem } from "@/lib/api";
import { limitedMap } from "@/components/pmo/pmoData";
import { buscarEfetivas, usePermissions } from "@/features/permissions/PermissionProvider";
import { criarCan, type Can } from "@/features/permissions/permissionLogic";

export function useProjetosPermitidosTv(projetos: readonly AccItem[], chavesQualquerTela: readonly string[]) {
  const { effective } = usePermissions();
  const aplica = !!effective && effective.enforced && !effective.isMasterAdmin;
  const [cans, setCans] = useState<ReadonlyMap<string, Can> | null>(null);
  const chaveProjetos = projetos.map((p) => p.id).join("|");

  useEffect(() => {
    if (!aplica || projetos.length === 0) { setCans(null); return; }
    const abort = new AbortController();
    setCans(null);
    const mapa = new Map<string, Can>();
    limitedMap([...projetos], async (p) => {
      try { mapa.set(p.id, criarCan(await buscarEfetivas(p.id, abort.signal), "project")); }
      catch { mapa.set(p.id, () => false); }                            // sem resposta = não oferecido
    }, 4).then(() => { if (!abort.signal.aborted) setCans(mapa); }).catch(() => undefined);
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aplica, chaveProjetos]);

  const podeTela = useCallback((projectId: string, chaves: readonly string[]) => !aplica || (cans?.get(projectId)?.(chaves) ?? false), [aplica, cans]);
  const chaveTelas = chavesQualquerTela.join("|");
  const opcoes = useMemo(() => {
    if (aplica && !cans) return [];
    return projetos.filter((p) => podeTela(p.id, chaveTelas.split("|")));
  }, [projetos, aplica, cans, podeTela, chaveTelas]);
  return { opcoes, verificando: aplica && !cans && projetos.length > 0, podeTela };
}
