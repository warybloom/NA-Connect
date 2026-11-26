// Peças compartilhadas pelas telas de Custos do Projeto (Impostos / Profissionais / Softwares / Pacotes).
import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Loader2 } from "lucide-react";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Input } from "@/components/ui/input";
import { ReportState } from "@/components/reports/ReportComponents";
import { cn } from "@/lib/utils";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { CustosError, custosApi, dinheiro, horasFmt, type Resumo } from "./costsApi";

/** Projeto atual (Project_ID_ACC) — o mesmo escolhido na Home; nada de Autodesk. */
export function useProjetoCustos() {
  const { projetoAtual } = useProjetoAtual();
  return { projectId: projetoAtual?.projectId, nome: projetoAtual?.nome };
}

/** Carregamento simples com recarga manual. */
export function useCarga<T>(carregar: (() => Promise<T>) | null, deps: unknown[]) {
  const [dados, setDados] = useState<T>();
  const [erro, setErro] = useState<unknown>();
  const [carregando, setCarregando] = useState(false);
  const [versao, setVersao] = useState(0);
  useEffect(() => {
    if (!carregar) return;
    let vivo = true;
    setCarregando(true); setErro(undefined);
    carregar().then((d) => { if (vivo) setDados(d); }).catch((e) => { if (vivo) setErro(e); }).finally(() => { if (vivo) setCarregando(false); });
    return () => { vivo = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, versao]);
  const recarregar = useCallback(() => setVersao((v) => v + 1), []);
  return { dados, erro, carregando, recarregar };
}

export function CabecalhoCustos({ titulo, descricao, acoes }: { titulo: string; descricao: string; acoes?: ReactNode }) {
  const { projectId, nome } = useProjetoCustos();
  const resumo = useCarga<Resumo>(projectId ? () => custosApi.resumo(projectId) : null, [projectId]);
  return (
    <header className="space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[11px] font-black uppercase tracking-widest text-muted-foreground">Custos do Projeto{nome ? ` · ${nome}` : ""}</p>
          <h1 className="text-2xl font-semibold tracking-tight">{titulo}</h1>
          <p className="text-sm text-muted-foreground">{descricao}</p>
        </div>
        {acoes && <div className="flex flex-wrap gap-2">{acoes}</div>}
      </div>
      {resumo.dados && (
        <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
          <MiniValor rotulo="Profissionais (mês)" valor={dinheiro(resumo.dados.professionals)} />
          <MiniValor rotulo="Pacotes" valor={dinheiro(resumo.dados.packages)} />
          <MiniValor rotulo="Despesas" valor={dinheiro(resumo.dados.expenses)} />
          <MiniValor rotulo="Custo previsto" valor={dinheiro(resumo.dados.costPredicted)} />
          <MiniValor rotulo="Reconhecido" valor={dinheiro(resumo.dados.recognized)} />
          <MiniValor rotulo="Excedente" valor={dinheiro(resumo.dados.excess)} alerta={resumo.dados.excess > 0} />
        </div>
      )}
      {resumo.dados?.timesheetStatus && resumo.dados.timesheetStatus !== "OK" && (
        <p className="text-xs text-amber-500">Timesheet: {resumo.dados.timesheetStatus} · HH realizadas {horasFmt(resumo.dados.workedHours)}</p>
      )}
    </header>
  );
}

function MiniValor({ rotulo, valor, alerta }: { rotulo: string; valor: string; alerta?: boolean }) {
  return (
    <div className={cn("rounded-lg border bg-card/60 px-3 py-2", alerta ? "border-rose-500/40" : "border-border")}>
      <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
      <p className={cn("text-sm font-semibold tabular-nums", alerta && "text-rose-500")}>{valor}</p>
    </div>
  );
}

export function SemProjeto() {
  return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na Home para gerenciar os custos.</ReportState>;
}

export function ErroCustos({ erro }: { erro: unknown }) {
  const e = erro as CustosError;
  return <ReportState title={e?.status === 403 ? "Sem permissão" : "Custos indisponíveis"} error>{e?.message || "Tente novamente."}</ReportState>;
}

export const Carregando = ({ texto }: { texto: string }) => (
  <p className="flex items-center gap-2 py-6 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />{texto}</p>
);

/** Campo com label real (formulários de cadastro/edição). */
export function Campo({ rotulo, dica, children, className }: { rotulo: string; dica?: string; children: ReactNode; className?: string }) {
  return (
    <label className={cn("flex flex-col gap-1.5", className)}>
      <span className="text-xs font-semibold text-foreground">{rotulo}</span>
      {children}
      {dica && <span className="text-[11px] text-muted-foreground">{dica}</span>}
    </label>
  );
}

export const CampoTexto = (p: React.ComponentProps<typeof Input>) => <Input {...p} className={cn("h-10", p.className)} />;

/** Confirmação antes de desativar (nunca exclusão física: o backend só marca Ativo = 0). */
export function ConfirmarDesativacao({ aberto, titulo, descricao, onConfirmar, onCancelar, ocupado }: {
  aberto: boolean; titulo: string; descricao: string; onConfirmar: () => void; onCancelar: () => void; ocupado?: boolean;
}) {
  return (
    <AlertDialog open={aberto} onOpenChange={(v) => { if (!v) onCancelar(); }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{titulo}</AlertDialogTitle>
          <AlertDialogDescription>{descricao}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={ocupado}>Cancelar</AlertDialogCancel>
          <AlertDialogAction disabled={ocupado} onClick={(e) => { e.preventDefault(); onConfirmar(); }} className="bg-rose-600 text-white hover:bg-rose-700">
            {ocupado ? <Loader2 className="h-4 w-4 animate-spin" /> : "Desativar"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export const SeloAtivo = ({ ativo }: { ativo: boolean }) => (
  <span className={cn("inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold", ativo ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400" : "bg-muted text-muted-foreground")}>
    {ativo ? "Ativo" : "Inativo"}
  </span>
);

/** Indicadores de elegibilidade de projeto destino (ATIVO e/ou SINCRONIZADO). */
export const SelosProjeto = ({ projeto }: { projeto: { isActive: boolean; hasSync: boolean } }) => (
  <span className="flex shrink-0 gap-1">
    {projeto.isActive && <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold text-emerald-600">ATIVO</span>}
    {projeto.hasSync && <span className="rounded-full bg-sky-500/10 px-2 py-0.5 text-[10px] font-bold text-sky-600">SINCRONIZADO</span>}
  </span>
);

export const thC ="px-3 py-2 text-left text-[11px] font-semibold uppercase tracking-wide text-muted-foreground";
export const thCNum = cn(thC, "text-right");
export const tdC = "px-3 py-2";
export const tdCNum = "px-3 py-2 text-right tabular-nums";

export const mensagemDe = (e: unknown) => (e instanceof Error ? e.message : "Não foi possível concluir a operação.");
