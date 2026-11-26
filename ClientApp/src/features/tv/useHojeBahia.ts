// Data civil de HOJE na Bahia, atualizada sozinha na virada do dia (TV ligada 24/7, sem F5). Um único timer leve
// (30 s) compara a data; o estado só muda quando o dia muda — quem usa a data como key remonta e busca os dados de novo.
import { useEffect, useState } from "react";
import { dataCivilBahia } from "./tvLogic";

export const INTERVALO_VERIFICACAO_DIA_MS = 30_000;

export function useHojeBahia(intervaloMs = INTERVALO_VERIFICACAO_DIA_MS): string {
  const [hoje, setHoje] = useState(() => dataCivilBahia());
  useEffect(() => {
    const t = window.setInterval(() => {
      const agora = dataCivilBahia();
      setHoje((atual) => (atual === agora ? atual : agora));
    }, intervaloMs);
    return () => window.clearInterval(t);
  }, [intervaloMs]);
  return hoje;
}
