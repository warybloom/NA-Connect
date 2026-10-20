// PRÉ-VISUALIZAÇÃO SÓ DE DESENVOLVIMENTO (não importada pelo app nem pelo build): renderiza uma página do BI com linhas
// exportadas do banco. As fixtures (fixture-ldp.json / fixture-prog.json / fixture-crono.json, linhas de /api/bi-chs001/*) NÃO são versionadas:
// copie-as para esta pasta só durante a conferência e apague depois. Uso: http://localhost:5173/src/features/production/biChs001/__preview__/preview.html?p=p12
import { createRoot } from "react-dom/client";
import { useEffect, useState } from "react";
import { BiChs001Canvas } from "../BiChs001Canvas";
import { VisualBi } from "../pbi/VisualBi";
import { dadosDoVisual, segmentacaoNaoReproduzida, type FonteCarregada } from "../dados/motor";
import { avisosDeFonte, infoFonte } from "../dados/avisos";
import { converterLinhas } from "../dados/entidades";
import type { PaginaPbi, VisualPbi } from "../pbi/modelo";
import "../biChs001.css";

const LAYOUTS = import.meta.glob<{ default: PaginaPbi }>("../generated/p*.json");
const FIXTURES = import.meta.glob<{ default: Record<string, unknown>[] }>("./fixture-*.json", { eager: true });
const linhas = (arq: string) => FIXTURES[arq]?.default ?? [];
// ?fonte=pbix → cópias temporárias de _bi_chs001_replica/fixtures_pbix (fixture-pbix-*.json, já com os nomes do BI).
const PBIX = new URLSearchParams(location.search).get("fonte") === "pbix";
const fontes: Record<string, FonteCarregada> = PBIX ? {
  "LDP (4)": { estado: "ok" as const, linhas: linhas("./fixture-pbix-ldp4.json") as never },
  "Programação Semanal": { estado: "ok" as const, linhas: linhas("./fixture-pbix-programacao.json") as never },
  WBX: { estado: "ok" as const, linhas: linhas("./fixture-pbix-wbx.json") as never },
  AssignmentByDay: { estado: "ok" as const, linhas: linhas("./fixture-pbix-abd.json") as never },
} : {
  "LDP (4)": { estado: "ok" as const, linhas: converterLinhas("LDP (4)", linhas("./fixture-ldp.json")) },
  "Programação Semanal": { estado: "ok" as const, linhas: converterLinhas("Programação Semanal", linhas("./fixture-prog.json")) },
  WBX: { estado: "ok" as const, linhas: converterLinhas("WBX", linhas("./fixture-crono.json")),
    versaoId: (FIXTURES["./fixture-meta.json"]?.default as unknown as { versaoId?: number } | undefined)?.versaoId },
  // Série do cronograma isolado (linhas de /api/bi-chs001/schedule/trabalho-diario), se copiada.
  ...(FIXTURES["./fixture-diario.json"] ? { AssignmentByDay: { estado: "ok" as const, linhas: converterLinhas("AssignmentByDay", linhas("./fixture-diario.json")) } } : {}),
};
function App() {
  const id = new URLSearchParams(location.search).get("p") ?? "p12";
  const [pg, setPg] = useState<PaginaPbi>();
  useEffect(() => { LAYOUTS[`../generated/${id}.json`]?.().then((m) => setPg(m.default)); }, [id]);
  if (!pg) return null;
  const dados = (v: VisualPbi) => ["shape", "image", "textbox", "actionButton"].includes(v.type) ? { estado: "pendente" as const, motivo: "" }
    : v.type === "slicer" ? { estado: "pendente" as const, motivo: segmentacaoNaoReproduzida(v) ?? "preview" } : dadosDoVisual(v, { pagina: pg, fontes, segmentacoes: {} });
  return (
    <div style={{ height: "100vh", display: "flex", flexDirection: "column", gap: 4 }}>
      {infoFonte(fontes).map((a) => <div key={a} className="bi-info-fonte">{a}</div>)}
      {avisosDeFonte(pg, fontes).map((a) => <div key={a} className="bi-aviso-fonte">{a}</div>)}
      <div style={{ flex: 1, minHeight: 0 }}><BiChs001Canvas pagina={pg}>{pg.visuais.map((v) => <VisualBi key={v.id} v={v} dados={dados(v)} />)}</BiChs001Canvas></div>
    </div>
  );
}
createRoot(document.getElementById("root")!).render(<App />);
