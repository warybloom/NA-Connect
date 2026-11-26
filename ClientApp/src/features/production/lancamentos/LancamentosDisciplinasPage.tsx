// Lançamentos → Disciplinas: sugestões da Norma de Coordenação do projeto + disciplinas do Timesheet fora da Norma
// + NÃO MAPEADO. Nenhuma HH é escondida. Todos os filtros (período/pessoa/documento/disciplina) valem no backend.
import { useEffect, useMemo, useState } from "react";
import { Loader2, Search } from "lucide-react";
import { ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { cn } from "@/lib/utils";
import { origemDe } from "./disciplinasNorma";
import { FILTROS_VAZIOS, buscarDisciplinas, horas, percentual, type Disciplina, type FiltrosLancamentos, type ResultadoDisciplinas } from "./lancamentosApi";
import {
  AvisoNorma, ChipsDisciplina, Diferenca, DrilldownLancamentos, EstadoErro, FiltrosBar, Indicador, opcoesDisciplina, td, tdNum, th, thNum,
  useDisciplinasDaNorma, useDisciplinasDoTimesheet,
} from "./LancamentosShared";

export default function LancamentosDisciplinasPage({ selection }: { selection: PmoSelecionado[] }) {
  const projectId = selection[0]?.id;
  const [filtros, setFiltros] = useState<FiltrosLancamentos>(FILTROS_VAZIOS);
  const [dados, setDados] = useState<ResultadoDisciplinas>();
  const [erro, setErro] = useState<unknown>();
  const [carregando, setCarregando] = useState(false);
  const [aberta, setAberta] = useState<Disciplina | null>(null);
  const norma = useDisciplinasDaNorma(projectId);
  const doTimesheet = useDisciplinasDoTimesheet(projectId);
  const opcoes = useMemo(() => opcoesDisciplina(norma, doTimesheet), [norma, doTimesheet]);

  useEffect(() => {
    if (!projectId) return;
    let vivo = true;
    setCarregando(true); setErro(undefined);
    buscarDisciplinas(projectId, filtros)
      .then((d) => { if (vivo) setDados(d); })
      .catch((e) => { if (vivo) setErro(e); })
      .finally(() => { if (vivo) setCarregando(false); });
    return () => { vivo = false; };
  }, [projectId, filtros]);

  const totais = useMemo(() => {
    const itens = dados?.items ?? [];
    return {
      planejada: itens.reduce((s, d) => s + d.plannedHours, 0), realizada: itens.reduce((s, d) => s + d.workedHours, 0),
      estourados: itens.reduce((s, d) => s + d.overrunDocuments, 0),
      foraDaNorma: norma.estado === "ok" ? itens.filter((d) => origemDe(d.discipline, norma.disciplinas) === "timesheet").length : 0,
    };
  }, [dados, norma]);

  if (!projectId) return <ReportState title="Nenhum projeto selecionado">Selecione um projeto.</ReportState>;

  return (
    <div className="space-y-4">
      <FiltrosBar valor={filtros} onAplicar={setFiltros} disciplinas={opcoes} carregando={carregando} />
      <div className="space-y-2">
        <AvisoNorma norma={norma} />
        <ChipsDisciplina opcoes={opcoes} selecionada={filtros.discipline} onSelecionar={(nome) => setFiltros((f) => ({ ...f, discipline: nome }))} />
      </div>
      {erro ? <EstadoErro erro={erro} /> : !dados ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando disciplinas…</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Indicador rotulo="HH planejada" valor={horas(totais.planejada)} detalhe="dos documentos no filtro" />
            <Indicador rotulo="HH realizada" valor={horas(totais.realizada)} detalhe={`CR/OS ${dados.crOs}`} />
            <Indicador rotulo="Documentos estourados" valor={totais.estourados.toLocaleString("pt-BR")} tom={totais.estourados > 0 ? "alerta" : "ok"} />
            <Indicador rotulo="Fora da Norma" valor={totais.foraDaNorma.toLocaleString("pt-BR")} detalhe="disciplinas do Timesheet" tom={totais.foraDaNorma > 0 ? "alerta" : undefined} />
          </div>
          <section className="overflow-hidden rounded-xl border border-border bg-card/60">
            {dados.items.length === 0 ? <div className="p-6"><ReportState title="Nenhuma disciplina encontrada">Nenhum lançamento no filtro atual.</ReportState></div> : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead><tr className="border-b border-border">
                    <th className={th}>Disciplina</th><th className={thNum}>HH planejada</th><th className={thNum}>HH realizada</th><th className={thNum}>Diferença</th>
                    <th className={thNum}>% consumido</th><th className={thNum}>Documentos</th><th className={thNum}>Pessoas</th><th className={thNum}>Estourados</th><th className={th} />
                  </tr></thead>
                  <tbody>
                    {dados.items.map((d) => {
                      const origem = origemDe(d.discipline, norma.disciplinas);
                      return (
                        <tr key={d.discipline} className="cursor-pointer border-b border-border/50 hover:bg-accent/40" onClick={() => setAberta(d)}>
                          <td className={td}>
                            <div className="font-medium">{d.discipline}</div>
                            {origem === "nao-mapeado" && <div className="text-[11px] text-rose-500">Sem disciplina mapeada</div>}
                            {origem === "timesheet" && norma.estado === "ok" && <div className="text-[11px] text-amber-600 dark:text-amber-400">Fora da Norma de Coordenação</div>}
                          </td>
                          <td className={tdNum}>{horas(d.plannedHours)}</td>
                          <td className={tdNum}>{horas(d.workedHours)}</td>
                          <td className={tdNum}><Diferenca v={d.difference} /></td>
                          <td className={cn(tdNum, (d.consumedPercent ?? 0) > 100 && "text-rose-500")}>{percentual(d.consumedPercent)}</td>
                          <td className={tdNum}>{d.documents.toLocaleString("pt-BR")}</td>
                          <td className={tdNum}>{d.people.toLocaleString("pt-BR")}</td>
                          <td className={cn(tdNum, d.overrunDocuments > 0 && "font-semibold text-rose-500")}>{d.overrunDocuments.toLocaleString("pt-BR")}</td>
                          <td className={`${td} text-right`}><Search className="ml-auto h-4 w-4 text-muted-foreground" aria-label="Ver lançamentos" /></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
      <DrilldownLancamentos projectId={projectId} filtros={filtros} recorte={aberta ? { discipline: aberta.discipline } : null}
        titulo={aberta ? `Lançamentos — ${aberta.discipline}` : ""} onFechar={() => setAberta(null)} />
    </div>
  );
}
