// Editor da TV — "Uma tela por disciplina" (20/10/2026): disciplinas de UM projeto, com a MESMA chave que a Programação Diária
// automática usa na exibição. Fontes do servidor, sem nada digitado: Norma de Coordenação do projeto (/disciplinas) e a
// programação de ontem/hoje (/programacao, data civil da Bahia). Se as duas falharem, a lista fica vazia e o erro é exibido.
import { useEffect, useState } from "react";
import { getJson } from "@/lib/api";
import {
  opcoesDisciplinaDiaria, type DisciplinasNorma, type OpcaoDisciplinaDiaria, type ProgramacaoResposta,
} from "@/features/production/analytics/gestaoVistaJamileLogic";
import { janelaDiariaAutomatica } from "./tvLogic";

export interface DisciplinasDiariaTv {
  /** Projeto a que as opções pertencem (null = nada carregado): evita usar opções de outro projeto durante a troca. */
  projeto: string | null;
  opcoes: OpcaoDisciplinaDiaria[];
  carregando: boolean;
  /** Falha da Norma e/ou da programação (a lista mostra só o que veio do servidor). */
  avisos: string[];
}

export function useDisciplinasDiariaTv(projeto: { id: string; name: string } | null, hoje: string): DisciplinasDiariaTv {
  const [estado, setEstado] = useState<DisciplinasDiariaTv>({ projeto: null, opcoes: [], carregando: false, avisos: [] });
  const id = projeto?.id ?? "", nome = projeto?.name ?? "";
  useEffect(() => {
    if (!id) { setEstado({ projeto: null, opcoes: [], carregando: false, avisos: [] }); return; }
    const ctrl = new AbortController();
    const { inicio, fim } = janelaDiariaAutomatica(hoje);
    setEstado({ projeto: null, opcoes: [], carregando: true, avisos: [] });
    Promise.allSettled([
      getJson<DisciplinasNorma>("/api/reports/gestao-vista-jamile/disciplinas?" + new URLSearchParams({ projectId: id }), ctrl.signal),
      getJson<ProgramacaoResposta>("/api/reports/gestao-vista-jamile/programacao?" + new URLSearchParams({ projectId: id, inicio, fim }), ctrl.signal),
    ]).then(([norma, prog]) => {
      if (ctrl.signal.aborted) return;
      const erro = (r: PromiseSettledResult<unknown>) => r.status === "rejected" ? (r.reason instanceof Error ? r.reason.message : "indisponível") : null;
      // Sem a Norma, a chave da disciplina poderia divergir da que a TV calcula (a tela classifica COM a Norma): nada é oferecido.
      if (norma.status === "rejected") {
        setEstado({ projeto: id, opcoes: [], carregando: false,
          avisos: [`Norma de Coordenação do projeto indisponível (${erro(norma)}): sem ela não é possível garantir a mesma disciplina que a TV exibe. Tente novamente.`] });
        return;
      }
      setEstado({
        projeto: id, carregando: false,
        avisos: erro(prog) ? [`Programação de ontem/hoje indisponível (${erro(prog)}): só as disciplinas cadastradas na Norma são oferecidas.`] : [],
        opcoes: opcoesDisciplinaDiaria({ norma: norma.value, dados: prog.status === "fulfilled" ? prog.value : null, projeto: { id, name: nome }, hoje }),
      });
    });
    return () => ctrl.abort();
  }, [id, nome, hoje]);
  // Opções de outro projeto (render entre a troca e o efeito) nunca são devolvidas.
  return estado.projeto === null || estado.projeto === id ? estado : { projeto: null, opcoes: [], carregando: true, avisos: [] };
}
