// Relatórios → Extrações de Engenharia — DEMONSTRAÇÃO VISUAL (30/09/2026).
// Prepara a integração futura com o DashboardBIM, que é um SISTEMA SEPARADO: o N&A Connect abrirá o DashboardBIM
// no projeto/recurso correspondente por LINK CONTEXTUAL. Nada aqui busca dados, chama API ou monta URL real — não há
// endereço oficial ainda. Os cards são estáticos; não há botão que leve a erro.
import { Cable, Gauge, GitFork, Network, Package, Waypoints } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";

interface Extracao { chave: string; nome: string; descricao: string; icone: LucideIcon }

/** Extrações previstas do ecossistema DashboardBIM (chave = recurso futuro; sem URL até existir rota oficial). */
export const EXTRACOES_ENGENHARIA: readonly Extracao[] = [
  { chave: "linhas", nome: "Lista de Linhas", descricao: "Linhas de tubulação do modelo: diâmetro, especificação, fluido, isolamento e rastreabilidade.", icone: Waypoints },
  { chave: "instrumentos", nome: "Lista de Instrumentos", descricao: "Instrumentos com tag, tipo, faixa e malha de controle associada.", icone: Gauge },
  { chave: "valvulas", nome: "Lista de Válvulas", descricao: "Válvulas por tipo, classe de pressão, diâmetro e linha de origem.", icone: GitFork },
  { chave: "equipamentos", nome: "Lista de Equipamentos", descricao: "Equipamentos com tag, serviço, dados de projeto e localização.", icone: Package },
];

export default function ExtracoesEngenhariaPage() {
  const { projetoAtual } = useProjetoAtual();
  return (
    <div className="space-y-6 animate-enter">
      <header className="flex flex-wrap items-end gap-4">
        <div className="flex-1">
          <p className="text-[10px] font-black uppercase tracking-[0.3em] text-primary">Relatórios</p>
          <h1 className="text-2xl font-black uppercase tracking-tight">Extrações de Engenharia</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Listas técnicas extraídas dos modelos de engenharia pelo <strong className="text-foreground">DashboardBIM</strong>.
            O N&amp;A Connect vai abrir cada lista diretamente no projeto selecionado — os dois sistemas continuam independentes.
          </p>
        </div>
        <span className="flex items-center gap-2 rounded-full border border-border bg-card/60 px-3 py-1.5 text-xs text-muted-foreground">
          <Network className="h-3.5 w-3.5 text-primary" />
          {projetoAtual?.nome ? <>Projeto: <strong className="text-foreground">{projetoAtual.nome}</strong></> : "Integração por projeto"}
        </span>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {EXTRACOES_ENGENHARIA.map((e) => (
          <article key={e.chave} className="relative flex flex-col gap-3 overflow-hidden rounded-xl border border-border/60 bg-gradient-to-br from-card to-card/40 p-5">
            <span className="absolute right-3 top-3 rounded-full border border-primary/30 bg-primary/10 px-2 py-0.5 text-[9px] font-black uppercase tracking-widest text-primary">
              Em breve
            </span>
            <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary/10 text-primary"><e.icone className="h-5 w-5" /></span>
            <h2 className="text-base font-black">{e.nome}</h2>
            <p className="text-xs leading-relaxed text-muted-foreground">{e.descricao}</p>
            <p className="mt-auto flex items-center gap-1.5 pt-2 text-[10px] font-bold uppercase tracking-wider text-muted-foreground/80">
              <Cable className="h-3 w-3" /> Via DashboardBIM
            </p>
          </article>
        ))}
      </div>

      <p className="text-[11px] text-muted-foreground">
        Demonstração conceitual: nenhuma lista é consultada nesta versão. A integração usará links contextualizados por projeto e
        recurso assim que as rotas oficiais do DashboardBIM estiverem definidas.
      </p>
    </div>
  );
}
