/** Curva oficial CHS001: as mesmas cinco medidas e o mesmo gráfico do BI Tipo 2. */
import { useEffect, useMemo, useState } from "react";
import { CurvaSIdentidadeVisual } from "./CurvaSIdentidadeVisual";
import type { PaginaPbi } from "./pbi/modelo";
import { useDadosBi } from "./dados/useDadosBi";

type Fase = "geral" | "30" | "60" | "90";
const OPCOES: readonly { id: Fase; nome: string }[] = [
  { id: "geral", nome: "Resumo Geral" },
  { id: "30", nome: "30%" },
  { id: "60", nome: "60%" },
  { id: "90", nome: "90%" },
];
const CARREGAR: Record<Fase, () => Promise<{ default: unknown }>> = {
  geral: () => import("./generated/p02.json"),
  "30": () => import("./generated/p03.json"),
  "60": () => import("./generated/p04.json"),
  "90": () => import("./generated/p05.json"),
};

export function CurvaSOficialChs001({ projectId }: { projectId: string }) {
  const [fase, setFase] = useState<Fase>("30");
  const [pagina, setPagina] = useState<PaginaPbi>();
  const [erroLayout, setErroLayout] = useState<string>();

  useEffect(() => {
    let ativo = true;
    setPagina(undefined);
    setErroLayout(undefined);
    CARREGAR[fase]().then(({ default: json }) => {
      if (ativo) setPagina(json as PaginaPbi);
    }).catch(() => {
      if (ativo) setErroLayout("Não foi possível carregar a definição oficial do BI CHS001.");
    });
    return () => { ativo = false; };
  }, [fase]);

  // Não transformar aqui: é a MESMA função de dados da Gestão à Vista Tipo 2.
  const { dadosDe } = useDadosBi(pagina, projectId);
  const grafico = useMemo(() => pagina?.visuais.find(
    v => v.type === "lineClusteredColumnComboChart" && v.y > 350 && v.y < 750
  ), [pagina]);
  const dados = erroLayout
    ? { estado: "erro" as const, mensagem: erroLayout }
    : grafico ? dadosDe(grafico) : undefined;

  return (
    <CurvaSIdentidadeVisual
      idTeste="curva-s-bi-chs001"
      subtitulo="Progresso de cronograma — mesmos dados e medidas do BI CHS001 / Gestão à Vista Tipo 2."
      visual={grafico}
      dados={dados}
      controles={OPCOES.map(op => (
        <button key={op.id} type="button" aria-pressed={fase === op.id}
          onClick={() => setFase(op.id)}
          className={`rounded px-3 py-1.5 text-xs font-medium ${fase === op.id
            ? "bg-sky-600 text-white" : "text-muted-foreground hover:bg-muted"}`}>
          {op.nome}
        </button>
      ))}
      aviso={<>Séries oficiais do cronograma: baseline e realizado por período; baseline, reprogramado
        e realizado acumulados. Ausência de fonte não vira zero.</>}
    />
  );
}
