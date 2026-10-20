// Lançamentos → Documentos: planejado/realizado total e situação são do documento inteiro; a coluna "no filtro" mostra
// o realizado dentro do período/pessoa escolhidos. Lançamentos sem código de documento aparecem no indicador próprio.
import { useEffect, useState } from "react";
import { Loader2, Search } from "lucide-react";
import { ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { cn } from "@/lib/utils";
import { FILTROS_VAZIOS, buscarDocumentos, horas, percentual, type Documento, type FiltrosLancamentos, type PaginaDocumentos } from "./lancamentosApi";
import {
  AvisoNorma, Diferenca, DrilldownLancamentos, EstadoErro, FiltrosBar, Indicador, Paginacao, SeloStatus, opcoesDisciplina, td, tdNum, th, thNum,
  useDisciplinasDaNorma, useDisciplinasDoTimesheet,
} from "./LancamentosShared";

export default function LancamentosDocumentosPage({ selection }: { selection: PmoSelecionado[] }) {
  const projectId = selection[0]?.id;
  const [filtros, setFiltros] = useState<FiltrosLancamentos>(FILTROS_VAZIOS);
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PaginaDocumentos>();
  const [erro, setErro] = useState<unknown>();
  const [carregando, setCarregando] = useState(false);
  const [aberto, setAberto] = useState<Documento | null>(null);
  const norma = useDisciplinasDaNorma(projectId);
  const doTimesheet = useDisciplinasDoTimesheet(projectId);

  useEffect(() => {
    if (!projectId) return;
    let vivo = true;
    setCarregando(true); setErro(undefined);
    buscarDocumentos(projectId, filtros, pagina)
      .then((d) => { if (vivo) setDados(d); })
      .catch((e) => { if (vivo) setErro(e); })
      .finally(() => { if (vivo) setCarregando(false); });
    return () => { vivo = false; };
  }, [projectId, filtros, pagina]);

  if (!projectId) return <ReportState title="Nenhum projeto selecionado">Selecione um projeto.</ReportState>;
  const aplicar = (f: FiltrosLancamentos) => { setPagina(1); setFiltros(f); };
  const filtrado = Boolean(filtros.from || filtros.to || filtros.person);

  return (
    <div className="space-y-4">
      <FiltrosBar valor={filtros} onAplicar={aplicar} disciplinas={opcoesDisciplina(norma, doTimesheet)} carregando={carregando}
        campos={["from", "to", "document", "discipline", "person", "status"]} />
      <AvisoNorma norma={norma} />
      {erro ? <EstadoErro erro={erro} /> : !dados ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando documentos…</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Indicador rotulo="Documentos" valor={dados.total.toLocaleString("pt-BR")} detalhe={`CR/OS ${dados.crOs}`} />
            <Indicador rotulo="HH planejada" valor={horas(dados.totalPlannedHours)} />
            <Indicador rotulo={filtrado ? "HH realizada no filtro" : "HH realizada"} valor={horas(dados.totalWorkedHours)} />
            <Indicador rotulo="Lançamentos sem documento" valor={horas(dados.workedHoursWithoutDocument)} detalhe="HH sem código de documento"
              tom={dados.workedHoursWithoutDocument > 0 ? "alerta" : undefined} />
          </div>
          <section className="overflow-hidden rounded-xl border border-border bg-card/60">
            {dados.items.length === 0 ? <div className="p-6"><ReportState title="Nenhum documento encontrado">Nenhum documento no filtro atual.</ReportState></div> : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead><tr className="border-b border-border">
                    <th className={th}>Documento</th><th className={th}>Disciplina</th><th className={thNum}>HH planejada</th>
                    {filtrado && <th className={thNum}>HH no filtro</th>}
                    <th className={thNum}>HH realizada</th><th className={thNum}>Diferença</th><th className={thNum}>Excesso</th><th className={thNum}>% consumido</th>
                    <th className={th}>Situação</th><th className={thNum}>Pessoas</th><th className={thNum}>Lançamentos</th><th className={th} />
                  </tr></thead>
                  <tbody>
                    {dados.items.map((d) => (
                      <tr key={d.documentCode} className="cursor-pointer border-b border-border/50 hover:bg-accent/40" onClick={() => setAberto(d)}>
                        <td className={cn(td, "max-w-[320px]")}>
                          <div className="font-medium">{d.documentCode}</div>
                          {d.document && <div className="truncate text-xs text-muted-foreground" title={d.document}>{d.document}</div>}
                        </td>
                        <td className={td}>{d.discipline}</td>
                        <td className={tdNum}>{horas(d.plannedHours)}</td>
                        {filtrado && <td className={tdNum}>{horas(d.workedHours)}</td>}
                        <td className={tdNum}>{horas(d.totalWorkedHours)}</td>
                        <td className={tdNum}><Diferenca v={d.difference} /></td>
                        <td className={cn(tdNum, d.excess > 0 && "font-semibold text-rose-500")}>{horas(d.excess)}</td>
                        <td className={cn(tdNum, (d.consumedPercent ?? 0) > 100 && "text-rose-500")}>{percentual(d.consumedPercent)}</td>
                        <td className={td}><SeloStatus status={d.status} /></td>
                        <td className={tdNum}>{d.people.toLocaleString("pt-BR")}</td>
                        <td className={tdNum}>{d.entries.toLocaleString("pt-BR")}</td>
                        <td className={`${td} text-right`}><Search className="ml-auto h-4 w-4 text-muted-foreground" aria-label="Ver lançamentos" /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <Paginacao pagina={pagina} total={dados.total} onPagina={setPagina} rotulo="documentos" />
          </section>
          {filtrado && <p className="text-xs text-muted-foreground">Planejado, realizado total, excesso e situação são do documento inteiro; "HH no filtro" é o realizado dentro do período/pessoa.</p>}
        </>
      )}
      <DrilldownLancamentos projectId={projectId} filtros={filtros} recorte={aberto ? { document: aberto.documentCode } : null}
        titulo={aberto ? aberto.documentCode : ""} descricao={aberto?.document ?? undefined} onFechar={() => setAberto(null)} />
    </div>
  );
}
