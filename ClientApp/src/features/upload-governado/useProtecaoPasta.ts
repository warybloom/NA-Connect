// Proteção da pasta ABERTA no navegador do CDE: consultada assim que a pasta abre (não depende de arrastar arquivo).
// Mesma regra do envio (RaizQueContemAsync: raiz configurada + qualquer subpasta, em qualquer profundidade).
import { useEffect, useState } from "react";
import { consultarProtecao } from "./uploadGovernadoApi";

/** true = dentro de uma árvore protegida pela LDP; false = sem regra; null = ainda consultando ou indisponível. */
export function useProtecaoPasta(hubId: string | undefined, projectId: string | undefined, folderId: string | undefined): boolean | null {
  const [protegida, setProtegida] = useState<boolean | null>(null);
  useEffect(() => {
    setProtegida(null);
    if (!hubId || !projectId || !folderId) return;
    const abort = new AbortController();
    consultarProtecao(hubId, projectId, folderId, abort.signal)
      .then((r) => { if (!abort.signal.aborted) setProtegida(r.protegida); })
      .catch(() => { /* sem permissão/indisponível: não mostra indicador (o envio continua validando no servidor) */ });
    return () => abort.abort();
  }, [hubId, projectId, folderId]);
  return protegida;
}
