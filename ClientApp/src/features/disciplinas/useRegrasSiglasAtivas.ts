import { useEffect, useState } from "react";
import { listarRegrasAtivas, NormalizadorError } from "@/features/settings/normalizador-siglas/normalizadorSiglasApi";
import type { RegraSigla } from "./normalizadorSiglas";

/**
 * Regras ATIVAS do Normalizador de Siglas, só quando `ativo` (painel multiprojeto). Indisponível (migração não aplicada,
 * backend sem o endpoint, sem permissão) = lista vazia + aviso: o painel segue exatamente como antes, sem normalizar.
 */
export function useRegrasSiglasAtivas(ativo: boolean) {
  const [estado, setEstado] = useState<{ regras: RegraSigla[]; aviso: string | null; carregando: boolean }>({ regras: [], aviso: null, carregando: false });
  useEffect(() => {
    if (!ativo) { setEstado({ regras: [], aviso: null, carregando: false }); return; }
    const ctrl = new AbortController();
    setEstado((s) => ({ ...s, carregando: true }));
    listarRegrasAtivas(ctrl.signal).then(
      (r) => { if (!ctrl.signal.aborted) setEstado({ regras: r.regras.filter((x) => x.ativo), aviso: null, carregando: false }); },
      (e) => { if (!ctrl.signal.aborted) setEstado({ regras: [], carregando: false,
        aviso: e instanceof NormalizadorError && e.naoInstalado ? "Normalizador de Siglas não instalado: consolidado sem normalização."
          : `Normalizador de Siglas indisponível (${e instanceof Error ? e.message : "erro"}): consolidado sem normalização.` }); });
    return () => ctrl.abort();
  }, [ativo]);
  return estado;
}
