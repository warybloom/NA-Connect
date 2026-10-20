// Blocos visuais compartilhados das telas do Relatório de Produção (tokens do tema do N&A Connect).
import type { ReactNode } from "react";
import { KpiCard, LoadingState, ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";

export { KpiCard, LoadingState, ReportState };

export interface ContagemPorValor { valor: string | null; quantidade: number }

export const rotulo = (v: string | null | undefined) => (v == null ? "Não informado" : v.trim() === "" ? "(Vazio)" : v);
export const numero = (n: number | null | undefined, casas = 0) =>
  n == null ? "—" : new Intl.NumberFormat("pt-BR", { minimumFractionDigits: casas, maximumFractionDigits: casas }).format(n);
export const dataCurta = (v: string | null | undefined) => (v ? new Date(v).toLocaleDateString("pt-BR", { timeZone: "UTC" }) : "—");

/** Distribuição por valor com barra proporcional — contagens vindas do backend (conjunto inteiro do projeto). */
export function Distribuicao({ titulo, itens, vazio = "Sem registros." }: { titulo: string; itens: ContagemPorValor[]; vazio?: string }) {
  const max = Math.max(1, ...itens.map(i => i.quantidade));
  return (
    <article className="rounded-xl border border-border bg-card/70 p-5">
      <h3 className="text-sm font-semibold mb-4">{titulo}</h3>
      {itens.length === 0 ? <p className="text-sm text-muted-foreground">{vazio}</p> : (
        <ul className="space-y-2.5 max-h-80 overflow-y-auto pr-1 custom-scrollbar">
          {itens.map((i, idx) => (
            <li key={`${i.valor ?? "__null__"}-${idx}`} className="text-xs">
              <div className="flex justify-between gap-3 mb-1">
                <span className="truncate" title={rotulo(i.valor)}>{rotulo(i.valor)}</span>
                <span className="tabular-nums font-semibold">{numero(i.quantidade)}</span>
              </div>
              <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                <div className="h-full rounded-full bg-primary" style={{ width: `${(i.quantidade / max) * 100}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}

export function UltimaSincronizacao({ valor }: { valor: string | null | undefined }) {
  return <p className="text-xs text-muted-foreground">Última sincronização: {valor ? new Date(valor).toLocaleString("pt-BR") : "—"}</p>;
}

/**
 * Tela implementada só para UM projeto recebendo seleção múltipla (futuro portfólio/Gestão à Vista):
 * avisa em vez de somar/misturar projetos sem regra de consolidação comprovada.
 */
export function SomenteUmProjeto({ selection, children }: { selection: PmoSelecionado[]; children: ReactNode }) {
  if (selection.length > 1)
    return <ReportState title="Esta tela ainda não consolida vários projetos">Selecione um único projeto para visualizá-la.</ReportState>;
  return <>{children}</>;
}

export function EstadoCarregamento({ loading, error, onRetry, children }: { loading: boolean; error?: string; onRetry: () => void; children: ReactNode }) {
  if (loading) return <LoadingState />;
  if (error) return <ReportState title={error} error><button className="text-primary underline" onClick={onRetry}>Tentar novamente</button></ReportState>;
  return <>{children}</>;
}
