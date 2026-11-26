// Painel de Planejamento → Painel do Planejador (30/09/2026, ajuste): SÓ o painel do planejador.
// A listagem/configuração de projetos saiu daqui e virou item próprio do sidebar ("Configuração do Projeto",
// /planejamento/configuracao-do-projeto). Nenhum bloco de configuração LDP é exibido nesta tela — cada etapa da LDP
// é uma página/rota individual no sidebar.
// 29/10/2026: aba "Documentos de Gestão" — ÚNICO lugar onde o planejador classifica Gestão × Engenharia (lida pelas telas
// Controle de HH / Custo — Engenharia/Gestão). ?aba=documentos-gestao abre direto nela.
import { useSearchParams } from "react-router-dom";
import { PlannerDashboardPage } from "./planner/PlannerDashboardPage";
import { DocumentosGestaoSection } from "./documentosGestao/DocumentosGestaoSection";

const ABAS = [
  { id: "programacao", rotulo: "Programação semanal" },
  { id: "documentos-gestao", rotulo: "Documentos de Gestão" },
] as const;

export default function PainelPlanejadorPage() {
  const [params, setParams] = useSearchParams();
  const aba = params.get("aba") === "documentos-gestao" ? "documentos-gestao" : "programacao";
  return (
    <div className="space-y-3">
      <nav className="flex gap-1 border-b border-border" aria-label="Seções do Painel do Planejador">
        {ABAS.map(a => (
          <button key={a.id} type="button" aria-current={aba === a.id ? "page" : undefined}
            onClick={() => setParams(p => { const n = new URLSearchParams(p); if (a.id === "programacao") n.delete("aba"); else n.set("aba", a.id); return n; }, { replace: true })}
            className={`-mb-px border-b-2 px-3 py-1.5 text-sm font-semibold ${aba === a.id ? "border-sky-500 text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"}`}>
            {a.rotulo}
          </button>
        ))}
      </nav>
      {aba === "documentos-gestao" ? <DocumentosGestaoSection /> : <PlannerDashboardPage />}
    </div>
  );
}
