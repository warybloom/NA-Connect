// Lançamentos → Pessoas: agregação por pessoa feita no backend sobre TODOS os lançamentos do filtro (nunca só a página).
import { useEffect, useState } from "react";
import { Info, Loader2, Search } from "lucide-react";
import { ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { FILTROS_VAZIOS, buscarPessoas, dataCurta, horas, type FiltrosLancamentos, type PaginaPessoas, type Pessoa } from "./lancamentosApi";
import {
  AvisoNorma, Diferenca, DrilldownLancamentos, EstadoErro, FiltrosBar, Indicador, Paginacao, opcoesDisciplina, td, tdNum, th, thNum,
  useDisciplinasDaNorma, useDisciplinasDoTimesheet,
} from "./LancamentosShared";

export default function LancamentosPessoasPage({ selection }: { selection: PmoSelecionado[] }) {
  const projectId = selection[0]?.id;
  const [filtros, setFiltros] = useState<FiltrosLancamentos>(FILTROS_VAZIOS);
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PaginaPessoas>();
  const [erro, setErro] = useState<unknown>();
  const [carregando, setCarregando] = useState(false);
  const [aberta, setAberta] = useState<Pessoa | null>(null);
  const norma = useDisciplinasDaNorma(projectId);
  const doTimesheet = useDisciplinasDoTimesheet(projectId);

  useEffect(() => {
    if (!projectId) return;
    let vivo = true;
    setCarregando(true); setErro(undefined);
    buscarPessoas(projectId, filtros, pagina)
      .then((d) => { if (vivo) setDados(d); })
      .catch((e) => { if (vivo) setErro(e); })
      .finally(() => { if (vivo) setCarregando(false); });
    return () => { vivo = false; };
  }, [projectId, filtros, pagina]);

  if (!projectId) return <ReportState title="Nenhum projeto selecionado">Selecione um projeto.</ReportState>;
  const aplicar = (f: FiltrosLancamentos) => { setPagina(1); setFiltros(f); };

  return (
    <div className="space-y-4">
      <FiltrosBar valor={filtros} onAplicar={aplicar} disciplinas={opcoesDisciplina(norma, doTimesheet)} carregando={carregando} />
      <AvisoNorma norma={norma} />
      {erro ? <EstadoErro erro={erro} /> : !dados ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando pessoas…</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Indicador rotulo="Pessoas" valor={dados.total.toLocaleString("pt-BR")} detalhe={`CR/OS ${dados.crOs}`} />
            <Indicador rotulo="HH planejada (rateada)" valor={horas(dados.totalPlannedHours)} />
            <Indicador rotulo="HH realizada" valor={horas(dados.totalWorkedHours)} />
            <Indicador rotulo="Diferença" valor={<Diferenca v={dados.totalWorkedHours - dados.totalPlannedHours} />} tom={dados.totalWorkedHours > dados.totalPlannedHours ? "alerta" : undefined} />
          </div>
          <p className="flex items-start gap-1.5 text-xs text-muted-foreground">
            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            O Timesheet não tem HH planejada por pessoa: a HH planejada de cada documento é rateada entre as pessoas na proporção das horas que cada uma lançou nele.
          </p>
          <section className="overflow-hidden rounded-xl border border-border bg-card/60">
            {dados.items.length === 0 ? <div className="p-6"><ReportState title="Nenhuma pessoa encontrada">Nenhum lançamento no filtro atual.</ReportState></div> : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead><tr className="border-b border-border">
                    <th className={th}>Pessoa</th><th className={thNum}>HH planejada</th><th className={thNum}>HH realizada</th><th className={thNum}>Diferença</th>
                    <th className={thNum}>Lançamentos</th><th className={thNum}>Documentos</th><th className={th}>Último lançamento</th><th className={th} />
                  </tr></thead>
                  <tbody>
                    {dados.items.map((p) => (
                      <tr key={p.name} className="cursor-pointer border-b border-border/50 hover:bg-accent/40" onClick={() => setAberta(p)}>
                        <td className={`${td} font-medium`}>{p.name}</td>
                        <td className={tdNum}>{horas(p.plannedHours)}</td>
                        <td className={tdNum}>{horas(p.workedHours)}</td>
                        <td className={tdNum}><Diferenca v={p.difference} /></td>
                        <td className={tdNum}>{p.entries.toLocaleString("pt-BR")}</td>
                        <td className={tdNum}>{p.documents.toLocaleString("pt-BR")}</td>
                        <td className={td}>{dataCurta(p.lastEntry)}</td>
                        <td className={`${td} text-right`}><Search className="ml-auto h-4 w-4 text-muted-foreground" aria-label="Ver lançamentos" /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <Paginacao pagina={pagina} total={dados.total} onPagina={setPagina} rotulo="pessoas" />
          </section>
        </>
      )}
      <DrilldownLancamentos projectId={projectId} filtros={filtros} recorte={aberta ? { person: aberta.name } : null}
        titulo={aberta ? `Lançamentos — ${aberta.name}` : ""} onFechar={() => setAberta(null)} />
    </div>
  );
}
