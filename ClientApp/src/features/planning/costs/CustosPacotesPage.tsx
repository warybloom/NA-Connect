// Custos do Projeto → Pacotes: pacotes contratados (costs.Package), documentos com rateio AUTO/MANUAL, aditivos,
// reconhecimento/excedentes e importação transacional por Excel (preview → commit, backend existente).
import { useEffect, useMemo, useState, type FormEvent } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, Download, FileSpreadsheet, Loader2, Package as PackageIcon, Pencil, Plus, Search, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import {
  CustosError, custosApi, preCarregarCatalogoLdp, dataFmt, dinheiro, horasFmt, lerDocumentosColados, lerExtrasDocumento, lerExtrasImportacao, lerNumero, mesclarExtras, montarDocumento,
  semPesoConfigurado, SEM_DISCIPLINA, ehPorDisciplina, type ModeloPacote, type SuporteModelo,
  type Aditivo, type CamposDocumento, type DocumentActual, type DocumentoInput, type DocumentoPacote, type ExtrasDocumento, type Modo, type Pacote,
  type PreviaImportacao, type Rateio, type Reconhecimento, type Resumo,
} from "./costsApi";
import {
  CabecalhoCustos, Campo, CampoTexto, Carregando, ConfirmarDesativacao, ErroCustos, SeloAtivo, SemProjeto, mensagemDe, tdC, tdCNum, thC, thCNum,
  useCarga, useProjetoCustos,
} from "./CustosShared";
import {
  AvisoModeloIndisponivel, DetalheDisciplina, ImportarExcelDisciplina, PacoteDisciplinaDialog, SeletorModelo, SeloModeloPacote, downloadModeloDisciplina,
} from "./PacotesPorDisciplina";

const CHAVE_MODELO = "na.custos.pacotes.modelo";
function modeloInicial(): ModeloPacote {
  try { return localStorage.getItem(CHAVE_MODELO) === "DISCIPLINA" ? "DISCIPLINA" : "DOCUMENTO"; } catch { return "DOCUMENTO"; }
}

/** Modelo oficial gerado pelo mesmo backend que lê a importação. Não inicia nenhuma gravação. */
async function downloadModeloPacotes(projectId: string) {
  const blob = await custosApi.baixarModeloPacotes(projectId);
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = "Modelo_Custos_Pacotes.xlsx";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export default function CustosPacotesPage() {
  const { projectId } = useProjetoCustos();
  const pacotes = useCarga<Pacote[]>(projectId ? () => custosApi.pacotes(projectId) : null, [projectId]);
  const [busca, setBusca] = useState("");
  const [mostrarInativos, setMostrarInativos] = useState(false);
  const [aberto, setAberto] = useState<Pacote | null>(null);
  const [novo, setNovo] = useState(false);
  const [importar, setImportar] = useState(false);
  const [baixandoModelo, setBaixandoModelo] = useState(false);
  // Chave de modelo: muda campos do cadastro, template e importação. Pacotes existentes mantêm o próprio modelo.
  const [modelo, setModeloState] = useState<ModeloPacote>(modeloInicial);
  const setModelo = (m: ModeloPacote) => { setModeloState(m); try { localStorage.setItem(CHAVE_MODELO, m); } catch { /* preferência local opcional */ } };
  const suporte = useCarga<SuporteModelo>(projectId && modelo === "DISCIPLINA" ? () => custosApi.suporteModelo(projectId) : null, [projectId, modelo]);
  const disciplinaIndisponivel = modelo === "DISCIPLINA" && suporte.dados?.disciplineModelInstalled !== true;
  // Inicia a consulta à LDP enquanto o usuário navega na tela: abrir o cadastro reaproveita o mesmo resultado.
  useEffect(() => { if (projectId && modelo === "DISCIPLINA") preCarregarCatalogoLdp(projectId); }, [projectId, modelo]);
  const baixarModelo = async () => {
    if (!projectId || baixandoModelo) return;
    setBaixandoModelo(true);
    try { if (modelo === "DISCIPLINA") await downloadModeloDisciplina(projectId); else await downloadModeloPacotes(projectId); }
    catch (e) { toast.error(mensagemDe(e)); }
    finally { setBaixandoModelo(false); }
  };

  const lista = useMemo(() => {
    const t = busca.trim().toLowerCase();
    return (pacotes.dados ?? []).filter((p) => (mostrarInativos || p.ativo) && (!t || `${p.nomeReferencia} ${p.empresa ?? ""} ${p.aliasTimesheet ?? ""}`.toLowerCase().includes(t)));
  }, [pacotes.dados, busca, mostrarInativos]);
  const totais = useMemo(() => {
    const ativos = (pacotes.dados ?? []).filter((p) => p.ativo);
    return { qtd: ativos.length, hh: ativos.reduce((s, p) => s + p.hhEfetiva, 0), custo: ativos.reduce((s, p) => s + p.custoEfetivo, 0), docs: ativos.reduce((s, p) => s + p.documents, 0) };
  }, [pacotes.dados]);

  if (!projectId) return <SemProjeto />;
  const abertoAtual = aberto ? (pacotes.dados ?? []).find((p) => p.id === aberto.id) ?? aberto : null;

  return (
    <div className="space-y-4">
      <CabecalhoCustos titulo="Pacotes" descricao="Pacotes contratados: documentos, rateio de HH e custo, aditivos e reconhecimento das horas do Timesheet."
        acoes={<>
          <SeletorModelo valor={modelo} onChange={setModelo} />
          <Button variant="outline" className="gap-1.5" onClick={() => void baixarModelo()} disabled={baixandoModelo || disciplinaIndisponivel}>
            {baixandoModelo ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
            Baixar modelo Excel
          </Button>
          <Button variant="outline" className="gap-1.5" onClick={() => setImportar(true)} disabled={disciplinaIndisponivel}><FileSpreadsheet className="h-4 w-4" />Importar Excel</Button>
          <Button className="gap-1.5" onClick={() => setNovo(true)} disabled={disciplinaIndisponivel}><Plus className="h-4 w-4" />Novo pacote</Button>
        </>} />
      {modelo === "DISCIPLINA" && (suporte.erro
        ? ((suporte.erro as CustosError).status === 404
          ? <AvisoModeloIndisponivel mensagem="O backend em execução ainda não possui o modelo Por Disciplina: reinicie o backend com a versão atual." />
          : <ErroCustos erro={suporte.erro} />)
        : suporte.dados && !suporte.dados.disciplineModelInstalled && <AvisoModeloIndisponivel mensagem={suporte.dados.message} />)}
      {pacotes.erro ? <ErroCustos erro={pacotes.erro} /> : !pacotes.dados ? <Carregando texto="Carregando pacotes…" /> : pacotes.dados.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border p-10 text-center">
          <PackageIcon className="mx-auto h-8 w-8 text-muted-foreground" />
          <p className="mt-2 font-semibold">Nenhum pacote cadastrado</p>
          <p className="text-sm text-muted-foreground">Cadastre um pacote ou importe a planilha de pacotes e documentos.</p>
          <p className="mt-2 text-xs text-muted-foreground">Custos internos do Timesheet não são exibidos nesta aba enquanto não houver pacote cadastrado.</p>
        </div>
      ) : (
        <>
          <ComoCalcula />
          <SecaoCustosReais projectId={projectId} />
          <div className="grid gap-3 sm:grid-cols-4">
            <Indic rotulo="Pacotes ativos" valor={totais.qtd.toLocaleString("pt-BR")} />
            <Indic rotulo="HH efetiva" valor={horasFmt(totais.hh)} />
            <Indic rotulo="Custo efetivo" valor={dinheiro(totais.custo)} />
            <Indic rotulo="Documentos" valor={totais.docs.toLocaleString("pt-BR")} />
          </div>
          <section className="overflow-hidden rounded-xl border border-border bg-card/60">
            <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
              <div className="relative w-full max-w-sm">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <CampoTexto className="pl-8" placeholder="Buscar pacote, empresa ou alias…" value={busca} onChange={(e) => setBusca(e.target.value)} aria-label="Buscar pacote" />
              </div>
              <label className="flex items-center gap-2 text-xs text-muted-foreground"><input type="checkbox" checked={mostrarInativos} onChange={(e) => setMostrarInativos(e.target.checked)} />Mostrar inativos</label>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead><tr className="border-b border-border">
                  <th className={thC}>Nome do pacote</th><th className={thC}>Empresa</th><th className={thC} title={DICA_ALIAS}>Alias no Timesheet</th>
                  <th className={thCNum}>HH base</th><th className={thCNum}>HH aditivos</th><th className={thCNum}>HH efetiva</th>
                  <th className={thCNum}>Custo base</th><th className={thCNum}>Custo aditivos</th><th className={thCNum}>Custo efetivo</th>
                  <th className={thCNum}>Documentos</th><th className={thC}>Situação</th>
                </tr></thead>
                <tbody>
                  {lista.length === 0 && <tr><td colSpan={11} className="px-3 py-6 text-center text-muted-foreground">Nenhum pacote para a busca.</td></tr>}
                  {lista.map((p) => (
                    <tr key={p.id} className={cn("cursor-pointer border-b border-border/50 hover:bg-accent/40", !p.ativo && "text-muted-foreground")} onClick={() => setAberto(p)}>
                      <td className={`${tdC} font-medium`}><span className="flex items-center gap-2">{p.nomeReferencia}<SeloModeloPacote modelo={p.modeloCadastro} /></span></td>
                      <td className={tdC}>{p.empresa ?? "—"}</td>
                      <td className={tdC}>{p.aliasTimesheet ?? "—"}</td>
                      <td className={tdCNum}>{horasFmt(p.hhBase)}</td>
                      <td className={tdCNum}>{horasFmt(p.hhAddenda)}</td>
                      <td className={`${tdCNum} font-semibold`}>{horasFmt(p.hhEfetiva)}</td>
                      <td className={tdCNum}>{dinheiro(p.custoBase)}</td>
                      <td className={tdCNum}>{dinheiro(p.custoAddenda)}</td>
                      <td className={`${tdCNum} font-semibold`}>{dinheiro(p.custoEfetivo)}</td>
                      <td className={tdCNum}>{ehPorDisciplina(p) ? `${(p.disciplinas ?? 0).toLocaleString("pt-BR")} disc. · LDP` : p.documents.toLocaleString("pt-BR")}</td>
                      <td className={tdC}><SeloAtivo ativo={p.ativo} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
      {novo && (modelo === "DISCIPLINA"
        ? <PacoteDisciplinaDialog projectId={projectId} onFechar={() => setNovo(false)} onSalvo={() => { setNovo(false); pacotes.recarregar(); }} />
        : <NovoPacote projectId={projectId} onFechar={() => setNovo(false)} onSalvo={() => { setNovo(false); pacotes.recarregar(); }} />)}
      {importar && (modelo === "DISCIPLINA"
        ? <ImportarExcelDisciplina projectId={projectId} onFechar={() => setImportar(false)} onImportado={() => pacotes.recarregar()} />
        : <ImportarExcel projectId={projectId} onFechar={() => setImportar(false)} onImportado={() => pacotes.recarregar()} />)}
      <Sheet open={abertoAtual !== null} onOpenChange={(v) => { if (!v) setAberto(null); }}>
        <SheetContent side="right" className="flex w-full flex-col gap-0 p-0 sm:max-w-4xl">
          {abertoAtual && <DetalhePacote projectId={projectId} p={abertoAtual} onAlterado={pacotes.recarregar} onFechar={() => setAberto(null)} />}
        </SheetContent>
      </Sheet>
    </div>
  );
}

const DICA_ALIAS = "Texto usado para localizar o profissional no Timesheet (contém)";

/** Regras de leitura da aba: todos os valores vêm do backend. */
const ComoCalcula = () => (
  <section className="rounded-xl border border-sky-500/30 bg-sky-500/5 p-4 text-xs text-muted-foreground">
    <h2 className="mb-1 text-sm font-semibold text-foreground">Como os valores de pacote são calculados</h2>
    <ul className="list-disc space-y-0.5 pl-4">
      <li><b>HH de pacote é limitada ao previsto efetivo</b> (base + aditivos, após redistribuição de documentos cancelados). HH do Timesheet acima disso é <b>excedente</b>.</li>
      <li><b>O Timesheet não aumenta o contrato</b>: só aditivos alteram HH/custo efetivos.</li>
      <li><b>Custo é liberado pelo peso do Document_Status</b> configurado no Avanço físico. Status sem peso configurado libera R$ 0.</li>
      <li>Documento <b>CANCELADO</b> tem HH/custo efetivos zerados; seu previsto é redistribuído aos demais documentos do pacote.</li>
      <li>Disciplina exibida é a <b>canônica da Norma de Coordenação</b> (Doc3 + catálogo global), não a digitada na LDP.</li>
      <li><b>Por Disciplina</b>: o total da disciplina é rateado igualmente entre os documentos ativos da LDP atual; horas sem documento do profissional são rateadas do mesmo modo. Só o <b>apropriado</b> (até o previsto) entra nos indicadores; o excedente fica visível no pacote.</li>
    </ul>
  </section>
);

const SeloAmbiguo = ({ className }: { className?: string }) => (
  <span className={cn("rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-bold text-amber-600 dark:text-amber-400", className)}
    title="Os lançamentos do Timesheet casam com mais de um pacote/profissional e não foram atribuídos. Ajuste o Alias no Timesheet.">MATCH AMBÍGUO – revisar alias</span>
);

const Indic = ({ rotulo, valor }: { rotulo: string; valor: string }) => (
  <div className="rounded-xl border border-border bg-card/60 p-4">
    <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
    <p className="text-xl font-semibold tabular-nums">{valor}</p>
  </div>
);

// ----------------------------------------------------------------------------------------------- detalhe ----
function DetalhePacote({ projectId, p, onAlterado, onFechar }: { projectId: string; p: Pacote; onAlterado: () => void; onFechar: () => void }) {
  const [desativar, setDesativar] = useState(false);
  const [editar, setEditar] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  const desativarPacote = async () => {
    setOcupado(true);
    try { await custosApi.desativar(projectId, "package", p.id); toast.success("Pacote desativado."); setDesativar(false); onAlterado(); onFechar(); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setOcupado(false); }
  };
  return (
    <>
      <SheetHeader className="border-b border-border px-6 py-4 text-left">
        <div className="flex items-start justify-between gap-3 pr-6">
          <div>
            <SheetTitle className="flex items-center gap-2">{p.nomeReferencia}<SeloModeloPacote modelo={p.modeloCadastro} /><SeloAtivo ativo={p.ativo} /></SheetTitle>
            <SheetDescription>{p.empresa ?? "Sem empresa"} · <span title={DICA_ALIAS}>Alias no Timesheet (contém): {p.aliasTimesheet ?? "—"}</span>{p.descricao ? ` · ${p.descricao}` : ""}</SheetDescription>
          </div>
          <div className="flex shrink-0 gap-2">
            <Button variant="outline" size="sm" className="gap-1.5" onClick={() => setEditar(true)}><Pencil className="h-3.5 w-3.5" />Editar</Button>
            {p.ativo && <Button variant="outline" size="sm" className="text-rose-500" onClick={() => setDesativar(true)}>Desativar pacote</Button>}
          </div>
        </div>
        {!ehPorDisciplina(p) && <div className="grid gap-2 pt-2 sm:grid-cols-4">
          <Mini rotulo="HH efetiva" valor={`${horasFmt(p.hhEfetiva)}`} detalhe={`base ${horasFmt(p.hhBase)} + aditivos ${horasFmt(p.hhAddenda)}`} />
          <Mini rotulo="Custo efetivo" valor={dinheiro(p.custoEfetivo)} detalhe={`base ${dinheiro(p.custoBase)} + aditivos ${dinheiro(p.custoAddenda)}`} />
          <Mini rotulo="Custo/hora contratado" valor={p.hhEfetiva > 0 ? dinheiro(p.custoEfetivo / p.hhEfetiva) : "—"} />
          <Mini rotulo="Documentos ativos" valor={p.documents.toLocaleString("pt-BR")} />
        </div>}
      </SheetHeader>
      {ehPorDisciplina(p) ? (
        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4"><DetalheDisciplina projectId={projectId} p={p} onAlterado={onAlterado} /></div>
      ) : (
      <Tabs defaultValue="documentos" className="flex min-h-0 flex-1 flex-col">
        <TabsList className="mx-6 mt-3 w-fit">
          <TabsTrigger value="documentos">Documentos</TabsTrigger>
          <TabsTrigger value="aditivos">Aditivos</TabsTrigger>
          <TabsTrigger value="reconhecimento">Reconhecimento</TabsTrigger>
        </TabsList>
        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4">
          <TabsContent value="documentos" className="mt-0"><SecaoDocumentos projectId={projectId} p={p} onAlterado={onAlterado} /></TabsContent>
          <TabsContent value="aditivos" className="mt-0"><SecaoAditivos projectId={projectId} p={p} onAlterado={onAlterado} /></TabsContent>
          <TabsContent value="reconhecimento" className="mt-0"><SecaoReconhecimento projectId={projectId} p={p} /></TabsContent>
        </div>
      </Tabs>
      )}
      <ConfirmarDesativacao aberto={desativar} ocupado={ocupado} onCancelar={() => setDesativar(false)} onConfirmar={desativarPacote}
        titulo={`Desativar ${p.nomeReferencia}?`} descricao="O pacote deixa de compor o custo previsto e o reconhecimento. Documentos e aditivos são mantidos no histórico." />
      {editar && <EditarPacote projectId={projectId} p={p} onFechar={() => setEditar(false)} onSalvo={() => { setEditar(false); onAlterado(); }} />}
    </>
  );
}

/** Rota PUT ainda não publicada no backend responde 404/405 sem mensagem própria. */
const mensagemEdicao = (x: unknown) =>
  x instanceof CustosError && (x.status === 404 || x.status === 405) && x.message === "Não foi possível concluir a operação."
    ? "Edição ainda não disponível no backend (rota PUT não publicada). Atualize/reinicie o backend e tente novamente."
    : mensagemDe(x);

function EditarPacote({ projectId, p, onFechar, onSalvo }: { projectId: string; p: Pacote; onFechar: () => void; onSalvo: () => void }) {
  const [v, setV] = useState({ nome: p.nomeReferencia, empresa: p.empresa ?? "", alias: p.aliasTimesheet ?? "", descricao: p.descricao ?? "", ativo: p.ativo });
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    if (!v.nome.trim()) { setErro("Informe o nome do pacote / profissional."); return; }
    setSalvando(true); setErro(null);
    try {
      await custosApi.atualizarPacote(projectId, p.id, { nomeReferencia: v.nome.trim(), empresa: v.empresa.trim() || null, aliasTimesheet: v.alias.trim() || null, descricao: v.descricao.trim() || null, ativo: v.ativo });
      toast.success("Pacote atualizado."); onSalvo();
    } catch (x) { setErro(mensagemEdicao(x)); } finally { setSalvando(false); }
  };
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !salvando) onFechar(); }}>
      <DialogContent className="max-w-lg">
        <form onSubmit={salvar} className="space-y-4">
          <DialogHeader><DialogTitle>Editar pacote</DialogTitle><DialogDescription>Cabeçalho do pacote (coluna Pacote / Profissional do Excel).</DialogDescription></DialogHeader>
          <Campo rotulo="Pacote / Profissional"><CampoTexto value={v.nome} onChange={(e) => setV({ ...v, nome: e.target.value })} autoFocus /></Campo>
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Empresa"><CampoTexto value={v.empresa} onChange={(e) => setV({ ...v, empresa: e.target.value })} /></Campo>
            <Campo rotulo="Alias no Timesheet" dica={DICA_ALIAS}><CampoTexto value={v.alias} onChange={(e) => setV({ ...v, alias: e.target.value })} /></Campo>
          </div>
          <Campo rotulo="Descrição"><CampoTexto value={v.descricao} onChange={(e) => setV({ ...v, descricao: e.target.value })} /></Campo>
          <div className="grid gap-2 sm:grid-cols-2">
            <Mini rotulo="HH base" valor={horasFmt(p.hhBase)} detalhe="Contratual — altere por aditivo" />
            <Mini rotulo="Custo base" valor={dinheiro(p.custoBase)} detalhe="Contratual — altere por aditivo" />
          </div>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={v.ativo} onChange={(e) => setV({ ...v, ativo: e.target.checked })} />Pacote ativo</label>
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onFechar} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={salvando} className="gap-1.5">{salvando && <Loader2 className="h-4 w-4 animate-spin" />}Salvar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function SecaoCustosReais({projectId}:{projectId:string}){
  const actuals=useCarga<DocumentActual[]>(()=>custosApi.custoRealDocumentos(projectId),[projectId]);
  const summary=useCarga<Resumo>(()=>custosApi.resumo(projectId),[projectId]);
  return <section className="space-y-3 rounded-xl border border-border bg-card/50 p-4"><div><h2 className="font-semibold">Custo real por documento</h2><p className="text-xs text-muted-foreground">Custo interno usa a vigência profissional na data do Timesheet. HH sem match ou sem custo vigente aparecem como pendência. Planejado e saldo exibidos abaixo são somente de pacotes; não representam orçamento total de profissionais internos.</p></div>
    {summary.dados?.timesheetStatus&&summary.dados.timesheetStatus!=="OK"&&<p role="status" className="rounded border border-amber-500/40 bg-amber-500/10 p-3 text-sm">Timesheet: {summary.dados.timesheetStatus}</p>}
    {actuals.erro?<ErroCustos erro={actuals.erro}/>:!actuals.dados?<Carregando texto="Calculando custos por documento…"/>:actuals.dados.length===0?<p className="rounded border border-dashed p-6 text-center text-sm text-muted-foreground">Nenhum lançamento documental reconciliado para este projeto. Confira a configuração de CR/OS e os matches.</p>:<div className="overflow-auto rounded border"><table className="w-full text-sm"><thead><tr className="border-b"><th className="p-2 text-left">Documento</th><th className="p-2 text-right">HH real</th><th className="p-2 text-right">Custo real interno</th><th className="p-2 text-right">Custo pacote reconhecido</th><th className="p-2 text-right">Custo real total</th><th className="p-2 text-right">Planejado pacote</th><th className="p-2 text-right">Saldo pacote</th><th className="p-2 text-right">HH sem custo/match</th></tr></thead><tbody>{actuals.dados.map(row=><tr key={row.documentCode} className="border-b"><td className="p-2 font-medium">{row.documentCode||"Sem documento"}</td><td className="p-2 text-right">{horasFmt(row.workedHours)}</td><td className="p-2 text-right">{dinheiro(row.internalActualCost)}</td><td className="p-2 text-right">{dinheiro(row.packageRecognizedCost)}</td><td className="p-2 text-right font-semibold">{dinheiro(row.actualCost)}</td><td className="p-2 text-right">{dinheiro(row.packagePlannedCost)}</td><td className={cn("p-2 text-right",row.packageRemaining<0&&"text-rose-600")}>{dinheiro(row.packageRemaining)}</td><td className={cn("p-2 text-right",(row.uncostedHours>0||row.ambiguousRows>0)&&"font-semibold text-amber-600")}>{horasFmt(row.uncostedHours)}{row.ambiguousRows>0?` · ${row.ambiguousRows} ambiguidades`:""}</td></tr>)}</tbody></table></div>}
  </section>;
}

const Mini = ({ rotulo, valor, detalhe }: { rotulo: string; valor: string; detalhe?: string }) => (
  <div className="rounded-lg border border-border bg-background/60 px-3 py-2">
    <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
    <p className="text-sm font-semibold tabular-nums">{valor}</p>
    {detalhe && <p className="text-[10px] text-muted-foreground">{detalhe}</p>}
  </div>
);

// --------------------------------------------------------------------------------------------- documentos ----
function SecaoDocumentos({ projectId, p, onAlterado }: { projectId: string; p: Pacote; onAlterado: () => void }) {
  const docs = useCarga<DocumentoPacote[]>(() => custosApi.documentos(projectId, p.id), [projectId, p.id]);
  const rateio = useCarga<Rateio>(() => custosApi.rateio(projectId, p.id), [projectId, p.id]);
  const [form, setForm] = useState<CamposDocumento>(CAMPOS_VAZIOS);
  const [colar, setColar] = useState(false);
  const [texto, setTexto] = useState("");
  const colado = useMemo(() => lerDocumentosColados(texto), [texto]);
  const [salvando, setSalvando] = useState(false);
  const [desativar, setDesativar] = useState<DocumentoPacote | null>(null);
  const [editar, setEditar] = useState<DocumentoPacote | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const rec = useCarga<Reconhecimento[]>(() => custosApi.reconhecimento(projectId), [projectId]);
  const finalPorId = useMemo(() => new Map((rateio.dados?.documentos ?? []).map((d) => [d.documentId, d])), [rateio.dados]);
  const recPorCodigo = useMemo(() => new Map((rec.dados ?? []).filter((r) => r.packageId === p.id).map((r) => [chaveDoc(r.documentoCodigo), r])), [rec.dados, p.id]);
  // Prioridade: documento enriquecido → rateio → reconhecimento. Nenhum valor financeiro é calculado aqui.
  const extrasPorId = useMemo(() => new Map((docs.dados ?? []).map((d) => [d.id,
    mesclarExtras(lerExtrasDocumento(d), lerExtrasDocumento(finalPorId.get(d.id)), lerExtrasDocumento(recPorCodigo.get(chaveDoc(d.documentoCodigo))))])),
  [docs.dados, finalPorId, recPorCodigo]);
  const diagnosticosRateio = rateio.dados ? lerExtrasDocumento(rateio.dados).diagnosticos : [];
  const depois = () => { docs.recarregar(); rateio.recarregar(); rec.recarregar(); onAlterado(); };

  const enviar = async (lista: DocumentoInput[]) => {
    if (lista.length === 0) { toast.error("Informe ao menos um documento."); return; }
    setSalvando(true);
    try { await custosApi.adicionarDocumentos(projectId, p.id, lista); toast.success(`${lista.length} documento(s) adicionado(s).`); setForm(CAMPOS_VAZIOS); setTexto(""); setColar(false); depois(); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setSalvando(false); }
  };
  const adicionarUm = (e: FormEvent) => {
    e.preventDefault();
    const r = montarDocumento(form);
    if ("erro" in r) { toast.error(r.erro); return; }
    void enviar([r.doc]);
  };
  const confirmar = async () => {
    if (!desativar) return;
    setOcupado(true);
    try { await custosApi.desativar(projectId, "document", desativar.id); toast.success("Documento desativado."); setDesativar(null); depois(); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setOcupado(false); }
  };

  return (
    <div className="space-y-4">
      {rateio.dados?.erro && (
        <p role="alert" className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-amber-600 dark:text-amber-400">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />Rateio inconsistente: {rateio.dados.erro}
        </p>
      )}
      {diagnosticosRateio.length > 0 && (
        <div role="status" className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-amber-700 dark:text-amber-400">
          <p className="flex items-center gap-2 font-semibold"><AlertTriangle className="h-4 w-4 shrink-0" />Diagnóstico do rateio/reconhecimento (valores exibidos mesmo assim)</p>
          <ul className="mt-1 list-disc pl-6 text-xs">{diagnosticosRateio.map((t) => <li key={t}>{t}</li>)}</ul>
        </div>
      )}
      {rec.erro ? <ErroCustos erro={rec.erro} /> : null}
      <div className="overflow-hidden rounded-lg border border-border">
        {docs.erro ? <div className="p-4"><ErroCustos erro={docs.erro} /></div> : !docs.dados ? <div className="px-4"><Carregando texto="Carregando documentos…" /></div> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead><tr className="border-b border-border">
                <th className={thC}>Documento · Status · Doc3 · Disciplina (Norma) · Peso</th>
                <th className={thCNum} title="HH prevista original do documento (antes de redistribuição e aditivos)">HH base</th>
                <th className={thCNum} title="HH prevista após redistribuição de cancelados e aditivos — teto da HH contabilizada">HH efetiva</th>
                <th className={thCNum}>HH Timesheet</th>
                <th className={thCNum} title="HH do Timesheet limitada à HH efetiva">HH contabilizada</th>
                <th className={thCNum}>HH excedente</th>
                <th className={thCNum}>Custo base</th>
                <th className={thCNum}>Custo efetivo</th>
                <th className={thCNum} title="Custo efetivo liberado pelo peso do Document_Status">Custo liberado (status)</th>
                <th className={thC}>Situação</th><th className={thC} />
              </tr></thead>
              <tbody>
                {docs.dados.length === 0 && <tr><td colSpan={11} className="px-3 py-6 text-center text-muted-foreground">Nenhum documento neste pacote.</td></tr>}
                {docs.dados.map((d) => {
                  const e = extrasPorId.get(d.id) ?? lerExtrasDocumento(d);
                  const semPeso = semPesoConfigurado(e);
                  const disciplinaOk = e.disciplinaCanonica !== null && e.disciplinaCanonica !== SEM_DISCIPLINA;
                  return (
                    <tr key={d.id} className={cn("border-b border-border/50 align-top", !d.ativo && "text-muted-foreground", e.cancelado && "bg-rose-500/5")}>
                      <td className={tdC}>
                        <div className={cn("font-medium", e.cancelado && "line-through decoration-rose-500/70")}>{d.documentoCodigo}</div>
                        <div className="mt-1 flex flex-wrap items-center gap-1 text-[10px]">
                          <SeloStatus status={e.documentStatus} cancelado={e.cancelado} />
                          <span className="rounded bg-muted px-1.5 py-0.5 font-mono" title="Sigla Doc3">{e.doc3 ?? "Doc3 —"}</span>
                          <span className={cn("rounded px-1.5 py-0.5", disciplinaOk ? "bg-muted font-semibold" : "bg-amber-500/15 font-semibold text-amber-600 dark:text-amber-400")}
                            title={disciplinaOk ? "Disciplina canônica (Doc3 + Norma de Coordenação + catálogo global)" : "Doc3 + Norma de Coordenação não resolveram um Nome padrão único — revise em Qualidade › Nomes das Disciplinas"}>
                            {e.disciplinaCanonica ?? "Disciplina da Norma não resolvida"}
                          </span>
                          {semPeso
                            ? <span className="rounded bg-amber-500/15 px-1.5 py-0.5 font-bold text-amber-600 dark:text-amber-400" title="Document_Status sem peso no Avanço físico: custo liberado R$ 0">SEM PESO CONFIGURADO</span>
                            : e.statusPercent !== null && <span className="rounded bg-sky-500/15 px-1.5 py-0.5 font-semibold text-sky-600 dark:text-sky-400" title="Peso configurado do Document_Status">{pctFmt(e.statusPercent)}</span>}
                          {e.matchAmbiguo && <SeloAmbiguo />}
                        </div>
                        {e.cancelado && <p className="mt-1 text-[10px] font-semibold text-rose-500">CANCELADO · HH/custo redistribuídos aos demais documentos</p>}
                        {e.cancelado && ((e.hhEfetiva ?? 0) > 0 || (e.custoEfetivo ?? 0) > 0 || (e.hhContabilizada ?? 0) > 0) && (
                          <p className="mt-0.5 text-[10px] text-amber-600 dark:text-amber-400">⚠ Cancelado com efetivo/contabilizado diferente de zero no retorno do backend — rateio ainda não redistribuído.</p>
                        )}
                        {e.diagnosticos.length > 0 && <ul className="mt-1 space-y-0.5 text-[10px] text-amber-600 dark:text-amber-400">{e.diagnosticos.map((t) => <li key={t}>⚠ {t}</li>)}</ul>}
                      </td>
                      <td className={tdCNum}>{hhOuTraco(e.hhBase)}<div className="mt-0.5"><SeloModo modo={d.modoHH} /></div></td>
                      <td className={`${tdCNum} font-semibold`}>{hhOuTraco(e.hhEfetiva)}<Parcelas redistribuido={e.hhRedistribuida} adicional={e.hhAdicional} fmt={horasFmt} /></td>
                      <td className={tdCNum}>{hhOuTraco(e.hhTimesheet)}</td>
                      <td className={tdCNum}>{hhOuTraco(e.hhContabilizada)}</td>
                      <td className={cn(tdCNum, (e.hhExcedente ?? 0) > 0 && "font-semibold text-rose-500")}>{hhOuTraco(e.hhExcedente)}</td>
                      <td className={tdCNum}>{dinheiroOuTraco(e.custoBase)}<div className="mt-0.5"><SeloModo modo={d.modoCusto} /></div></td>
                      <td className={`${tdCNum} font-semibold`}>{dinheiroOuTraco(e.custoEfetivo)}<Parcelas redistribuido={e.custoRedistribuido} adicional={e.custoAdicional} fmt={dinheiro} /></td>
                      <td className={cn(tdCNum, "font-semibold", semPeso && "text-amber-600 dark:text-amber-400")}
                        title={e.custoLiberado === null && !semPeso ? "Backend ainda não informou o custo liberado pelo status" : undefined}>
                        {semPeso ? dinheiro(0) : dinheiroOuTraco(e.custoLiberado)}
                      </td>
                      <td className={tdC}><SeloAtivo ativo={d.ativo} /></td>
                      <td className={`${tdC} whitespace-nowrap text-right`}>{d.ativo && <>
                        {p.ativo && <Button variant="ghost" size="sm" className="h-7 gap-1" onClick={() => setEditar(d)}><Pencil className="h-3.5 w-3.5" />Editar</Button>}
                        <Button variant="ghost" size="sm" className="h-7 text-rose-500" onClick={() => setDesativar(d)}>Desativar</Button>
                      </>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {p.ativo && (
        <div className="space-y-3 rounded-lg border border-border bg-background/40 p-3">
          <div className="flex items-center justify-between">
            <h4 className="text-sm font-semibold">{colar ? "Adicionar vários documentos" : "Adicionar documento"}</h4>
            <Button variant="ghost" size="sm" onClick={() => setColar(!colar)}>{colar ? "Adicionar um" : "Adicionar vários"}</Button>
          </div>
          {colar ? (
            <div className="space-y-2">
              <Textarea rows={6} value={texto} onChange={(e) => setTexto(e.target.value)} aria-label="Documentos (um por linha)"
                placeholder={"Um por linha: NÚMERO DO DOCUMENTO;HH PREVISTO;VALOR/HORA (vazios = AUTO)\nBML011.013-000-PL0-AT-001;40;150,00\nBML011.013-000-PL0-AT-002;;"} />
              {colado.erros.length > 0 && <ul role="alert" className="space-y-0.5 text-xs text-rose-500">{colado.erros.map((t) => <li key={t}>{t}</li>)}</ul>}
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <span>{colado.documentos.length} documento(s) reconhecido(s). Aceita colar as colunas Número do documento, HH previsto e Valor/hora do Excel.</span>
                <Button disabled={salvando || colado.erros.length > 0} onClick={() => void enviar(colado.documentos)} className="gap-1.5">{salvando && <Loader2 className="h-4 w-4 animate-spin" />}Adicionar documentos</Button>
              </div>
            </div>
          ) : (
            <form onSubmit={adicionarUm} className="grid gap-2 sm:grid-cols-[1.4fr_120px_130px_150px_auto]">
              <CamposDocumentoInputs v={form} onChange={setForm} />
              <Button type="submit" disabled={salvando} className="h-10 gap-1.5">{salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}Adicionar</Button>
            </form>
          )}
          <p className="text-[11px] text-muted-foreground">Mesmos campos da planilha. HH previsto vazio = AUTO (rateio do saldo). Com Valor/hora, o custo previsto é calculado pelo backend (HH previsto × Valor/hora); sem Valor/hora, custo AUTO. O backend recusa rateio MANUAL acima do total efetivo. Disciplina não é informada: a oficial vem do Doc3 + Norma de Coordenação.</p>
        </div>
      )}
      <ConfirmarDesativacao aberto={desativar !== null} ocupado={ocupado} onCancelar={() => setDesativar(null)} onConfirmar={confirmar}
        titulo="Desativar documento?" descricao="O documento sai do rateio e do reconhecimento deste pacote. O histórico é mantido." />
      {editar && <EditarDocumento projectId={projectId} p={p} d={editar} extras={extrasPorId.get(editar.id) ?? lerExtrasDocumento(editar)}
        onFechar={() => setEditar(null)} onSalvo={() => { setEditar(null); depois(); }} />}
    </div>
  );
}

const CAMPOS_VAZIOS: CamposDocumento = { codigo: "", hh: "", valorHora: "" };

/** Número do documento · HH previsto · Valor/hora · custo previsto estimado (só visual). */
const CamposDocumentoInputs = ({ v, onChange }: { v: CamposDocumento; onChange: (v: CamposDocumento) => void }) => (
  <>
    <CampoTexto aria-label="Número do documento" placeholder="Número do documento" value={v.codigo} onChange={(e) => onChange({ ...v, codigo: e.target.value })} />
    <CampoTexto aria-label="HH previsto (vazio = AUTO)" inputMode="decimal" placeholder="HH previsto (AUTO)" value={v.hh} onChange={(e) => onChange({ ...v, hh: e.target.value })} />
    <CampoTexto aria-label="Valor/hora (R$)" inputMode="decimal" placeholder="Valor/hora R$" value={v.valorHora} onChange={(e) => onChange({ ...v, valorHora: e.target.value })} />
    <EstimativaCusto hh={v.hh} valorHora={v.valorHora} />
  </>
);

/** Prévia visual rotulada: o custo previsto oficial é o que o backend calcula/devolve depois de salvar. */
function EstimativaCusto({ hh, valorHora }: { hh: string; valorHora: string }) {
  const h = lerNumero(hh); const vh = lerNumero(valorHora);
  return (
    <div className="flex h-10 flex-col justify-center rounded-md border border-dashed border-border px-2 text-right leading-tight"
      title="Estimativa visual (HH previsto × Valor/hora). O custo previsto oficial é calculado pelo backend ao salvar.">
      <span className="text-[10px] text-muted-foreground">Custo previsto (estimativa)</span>
      <span className="text-xs font-semibold tabular-nums">{vh === null ? "AUTO" : h === null ? "—" : dinheiro(h * vh)}</span>
    </div>
  );
}

const numCampo = (n: number) => n.toLocaleString("pt-BR", { useGrouping: false, maximumFractionDigits: 6 });

function EditarDocumento({ projectId, p, d, extras, onFechar, onSalvo }: {
  projectId: string; p: Pacote; d: DocumentoPacote; extras: ExtrasDocumento; onFechar: () => void; onSalvo: () => void;
}) {
  // Custo MANUAL gravado sem Valor/hora (cadastro antigo): mantido como está, salvo se o usuário informar Valor/hora ou optar por AUTO.
  const custoLegado = d.modoCusto === "MANUAL" && extras.valorHora === null ? d.custoAlocado : null;
  const [v, setV] = useState<CamposDocumento>({
    codigo: d.documentoCodigo, hh: d.modoHH === "MANUAL" ? numCampo(d.hhAlocada) : "", valorHora: extras.valorHora !== null ? numCampo(extras.valorHora) : "",
  });
  const [manterLegado, setManterLegado] = useState(custoLegado !== null);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    const r = montarDocumento(v, manterLegado ? custoLegado : null);
    if ("erro" in r) { setErro(r.erro); return; }
    setSalvando(true); setErro(null);
    try { await custosApi.atualizarDocumento(projectId, p.id, d.id, r.doc); toast.success("Documento atualizado."); onSalvo(); }
    catch (x) { setErro(mensagemEdicao(x)); } finally { setSalvando(false); }
  };
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !salvando) onFechar(); }}>
      <DialogContent className="max-w-2xl">
        <form onSubmit={salvar} className="space-y-4">
          <DialogHeader>
            <DialogTitle>Editar documento</DialogTitle>
            <DialogDescription>Pacote / Profissional: <b>{p.nomeReferencia}</b>. Disciplina não é editável — vem do Doc3 + Norma de Coordenação ({extras.disciplinaCanonica ?? "não resolvida"}).</DialogDescription>
          </DialogHeader>
          <div className="grid gap-2 sm:grid-cols-[1.4fr_120px_130px_150px]"><CamposDocumentoInputs v={v} onChange={setV} /></div>
          {custoLegado !== null && v.valorHora.trim() === "" && (
            <label className="flex items-center gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-700 dark:text-amber-400">
              <input type="checkbox" checked={manterLegado} onChange={(e) => setManterLegado(e.target.checked)} />
              Manter custo MANUAL já gravado ({dinheiro(custoLegado)}, sem Valor/hora). Desmarque para custo AUTO, ou informe o Valor/hora.
            </label>
          )}
          <div className="grid gap-2 sm:grid-cols-3">
            <Mini rotulo="Custo base atual (backend)" valor={dinheiroOuTraco(extras.custoBase)} />
            <Mini rotulo="Custo efetivo atual (backend)" valor={dinheiroOuTraco(extras.custoEfetivo)} />
            <Mini rotulo="HH efetiva atual (backend)" valor={hhOuTraco(extras.hhEfetiva)} />
          </div>
          <p className="text-[11px] text-muted-foreground">HH previsto vazio = AUTO. Com Valor/hora, o backend recalcula o custo previsto (HH previsto × Valor/hora) e valida o rateio do pacote.</p>
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onFechar} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={salvando} className="gap-1.5">{salvando && <Loader2 className="h-4 w-4 animate-spin" />}Salvar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

const chaveDoc = (codigo: string) => codigo.trim().toUpperCase();
const hhOuTraco = (v: number | null) => (v === null ? "—" : horasFmt(v));
const dinheiroOuTraco = (v: number | null) => (v === null ? "—" : dinheiro(v));
const pctFmt = (v: number) => `${v.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`;

/** Parcelas do efetivo informadas pelo backend (só exibe o que vier diferente de zero). */
const Parcelas = ({ redistribuido, adicional, fmt }: { redistribuido: number | null; adicional: number | null; fmt: (v: number) => string }) => (
  (redistribuido ?? 0) !== 0 || (adicional ?? 0) !== 0
    ? <div className="text-[10px] font-normal text-muted-foreground">{[(redistribuido ?? 0) !== 0 && `redistr. ${fmt(redistribuido!)}`, (adicional ?? 0) !== 0 && `aditivo ${fmt(adicional!)}`].filter(Boolean).join(" · ")}</div>
    : null
);

const SeloStatus = ({ status, cancelado }: { status: string | null; cancelado: boolean }) => (
  <span className={cn("rounded px-1.5 py-0.5 font-bold uppercase", cancelado ? "bg-rose-500/15 text-rose-600 dark:text-rose-400" : status ? "bg-emerald-500/15 text-emerald-700 dark:text-emerald-400" : "bg-muted text-muted-foreground")}
    title="Document_Status (LDP)">{status ?? (cancelado ? "CANCELADO" : "Sem status")}</span>
);

const SeloModo = ({ modo }: { modo: Modo }) => (
  <span className={cn("rounded px-1.5 py-0.5 text-[10px] font-bold", modo === "MANUAL" ? "bg-amber-500/15 text-amber-600 dark:text-amber-400" : "bg-sky-500/15 text-sky-600 dark:text-sky-400")}>{modo}</span>
);

// ---------------------------------------------------------------------------------------------- aditivos ----
function SecaoAditivos({ projectId, p, onAlterado }: { projectId: string; p: Pacote; onAlterado: () => void }) {
  const aditivos = useCarga<Aditivo[]>(() => custosApi.aditivos(projectId, p.id), [projectId, p.id]);
  const [form, setForm] = useState({ data: new Date().toISOString().slice(0, 10), descricao: "", hh: "", custo: "" });
  const [salvando, setSalvando] = useState(false);
  const [desativar, setDesativar] = useState<Aditivo | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const adicionar = async (e: FormEvent) => {
    e.preventDefault();
    const hh = lerNumero(form.hh) ?? 0; const custo = lerNumero(form.custo) ?? 0;
    if (!form.descricao.trim() || hh < 0 || custo < 0 || (hh === 0 && custo === 0)) { toast.error("Informe a descrição e HH e/ou custo adicional (não negativos)."); return; }
    setSalvando(true);
    try { await custosApi.adicionarAditivo(projectId, p.id, { data: form.data, descricao: form.descricao.trim(), hhAdicional: hh, custoAdicional: custo }); toast.success("Aditivo registrado."); setForm({ ...form, descricao: "", hh: "", custo: "" }); aditivos.recarregar(); onAlterado(); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setSalvando(false); }
  };
  const confirmar = async () => {
    if (!desativar) return;
    setOcupado(true);
    try { await custosApi.desativar(projectId, "addendum", desativar.id); toast.success("Aditivo desativado."); setDesativar(null); aditivos.recarregar(); onAlterado(); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setOcupado(false); }
  };

  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-lg border border-border">
        {aditivos.erro ? <div className="p-4"><ErroCustos erro={aditivos.erro} /></div> : !aditivos.dados ? <div className="px-4"><Carregando texto="Carregando aditivos…" /></div> : (
          <table className="w-full text-sm">
            <thead><tr className="border-b border-border"><th className={thC}>Data</th><th className={thC}>Descrição</th><th className={thCNum}>HH adicional</th><th className={thCNum}>Custo adicional</th><th className={thC}>Situação</th><th className={thC} /></tr></thead>
            <tbody>
              {aditivos.dados.length === 0 && <tr><td colSpan={6} className="px-3 py-6 text-center text-muted-foreground">Nenhum aditivo registrado.</td></tr>}
              {aditivos.dados.map((a) => (
                <tr key={a.id} className={cn("border-b border-border/50", !a.ativo && "text-muted-foreground line-through")}>
                  <td className={tdC}>{dataFmt(a.data)}</td><td className={tdC}>{a.descricao}</td>
                  <td className={tdCNum}>{horasFmt(a.hhAdicional)}</td><td className={tdCNum}>{dinheiro(a.custoAdicional)}</td>
                  <td className={tdC}><SeloAtivo ativo={a.ativo} /></td>
                  <td className={`${tdC} text-right`}>{a.ativo && <Button variant="ghost" size="sm" className="h-7 text-rose-500" onClick={() => setDesativar(a)}>Desativar</Button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {p.ativo && (
        <form onSubmit={adicionar} className="grid gap-2 rounded-lg border border-border bg-background/40 p-3 sm:grid-cols-[150px_1fr_120px_150px_auto]">
          <CampoTexto aria-label="Data" type="date" value={form.data} onChange={(e) => setForm({ ...form, data: e.target.value })} />
          <CampoTexto aria-label="Descrição" placeholder="Descrição do aditivo" value={form.descricao} onChange={(e) => setForm({ ...form, descricao: e.target.value })} />
          <CampoTexto aria-label="HH adicional" inputMode="decimal" placeholder="HH adicional" value={form.hh} onChange={(e) => setForm({ ...form, hh: e.target.value })} />
          <CampoTexto aria-label="Custo adicional" inputMode="decimal" placeholder="Custo adicional (R$)" value={form.custo} onChange={(e) => setForm({ ...form, custo: e.target.value })} />
          <Button type="submit" disabled={salvando} className="h-10 gap-1.5">{salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}Registrar</Button>
        </form>
      )}
      <ConfirmarDesativacao aberto={desativar !== null} ocupado={ocupado} onCancelar={() => setDesativar(null)} onConfirmar={confirmar}
        titulo="Desativar aditivo?" descricao="O aditivo deixa de somar na HH e no custo efetivos do pacote. O histórico é mantido." />
    </div>
  );
}

// ------------------------------------------------------------------------------------------ reconhecimento ----
function SecaoReconhecimento({ projectId, p }: { projectId: string; p: Pacote }) {
  const rec = useCarga<Reconhecimento[]>(() => custosApi.reconhecimento(projectId), [projectId]);
  const linhas = (rec.dados ?? []).filter((r) => r.packageId === p.id);
  const soma = (k: keyof Reconhecimento) => linhas.reduce((s, r) => s + (r[k] as number), 0);
  if (rec.erro) return <ErroCustos erro={rec.erro} />;
  if (!rec.dados) return <Carregando texto="Calculando reconhecimento…" />;
  if (linhas.length === 0) return <p className="py-4 text-sm text-muted-foreground">Sem reconhecimento: o pacote não tem documentos ativos ou o pacote está inativo.</p>;
  return (
    <div className="space-y-3">
      <div className="grid gap-2 sm:grid-cols-4">
        <Mini rotulo="HH Timesheet" valor={horasFmt(soma("timesheetHours"))} />
        <Mini rotulo="HH contabilizada" valor={horasFmt(soma("recognizedHours"))} />
        <Mini rotulo="HH excedente (não entra no projeto)" valor={horasFmt(soma("excessHours"))} />
        <Mini rotulo="Custo liberado pelos status" valor={dinheiro(soma("recognizedCost"))} />
      </div>
      <p className="text-xs text-muted-foreground">
        A HH contabilizada é limitada ao previsto efetivo do documento. O custo não é calculado pela HH do Timesheet:
        ele é o custo efetivo contratual multiplicado pelo peso configurado do Document_Status.
      </p>
      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead><tr className="border-b border-border">
            <th className={thC}>Documento</th><th className={thCNum}>HH Timesheet</th><th className={thCNum}>HH efetiva (teto)</th><th className={thCNum}>HH contabilizada</th>
            <th className={thCNum}>HH excedente</th><th className={thCNum}>Custo efetivo</th><th className={thCNum}>Custo liberado (status)</th>
          </tr></thead>
          <tbody>
            {linhas.map((r) => (
              <tr key={r.documentoCodigo} className="border-b border-border/50">
                <td className={tdC}>
                  <span className="font-medium">{r.documentoCodigo}</span>
                  {r.ambiguousMatch && <SeloAmbiguo className="ml-2" />}
                </td>
                <td className={tdCNum}>{horasFmt(r.timesheetHours)}</td><td className={tdCNum}>{horasFmt(r.contractedHours)}</td>
                <td className={tdCNum}>{horasFmt(r.recognizedHours)}</td>
                <td className={cn(tdCNum, r.excessHours > 0 && "font-semibold text-rose-500")}>{horasFmt(r.excessHours)}</td>
                <td className={tdCNum}>{dinheiro(r.theoreticalCost)}</td>
                <td className={cn(tdCNum, "font-semibold")}>{dinheiro(r.recognizedCost)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ------------------------------------------------------------------------------------------- novo pacote ----
function NovoPacote({ projectId, onFechar, onSalvo }: { projectId: string; onFechar: () => void; onSalvo: () => void }) {
  const [v, setV] = useState({ nome: "", empresa: "", alias: "", descricao: "" });
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    if (!v.nome.trim()) { setErro("Informe o nome do pacote / profissional."); return; }
    setSalvando(true); setErro(null);
    try {
      await custosApi.criarPacote(projectId, {
        nomeReferencia: v.nome.trim(),
        empresa: v.empresa.trim() || null,
        aliasTimesheet: v.alias.trim() || null,
        descricao: v.descricao.trim() || null,
        hhBase: 0,
        custoBase: 0,
      });
      toast.success("Pacote cadastrado. Agora adicione os documentos com HH previsto e Valor/hora.");
      onSalvo();
    } catch (x) { setErro(mensagemDe(x)); } finally { setSalvando(false); }
  };

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !salvando) onFechar(); }}>
      <DialogContent className="max-w-lg">
        <form onSubmit={salvar} className="space-y-4">
          <DialogHeader>
            <DialogTitle>Novo pacote</DialogTitle>
            <DialogDescription>
              Cadastre o cabeçalho do pacote/profissional. HH base e custo base serão formados pela soma dos documentos cadastrados.
            </DialogDescription>
          </DialogHeader>
          <Campo rotulo="Pacote / Profissional">
            <CampoTexto value={v.nome} onChange={(e) => setV({ ...v, nome: e.target.value })} autoFocus />
          </Campo>
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Empresa"><CampoTexto value={v.empresa} onChange={(e) => setV({ ...v, empresa: e.target.value })} /></Campo>
            <Campo rotulo="Alias no Timesheet" dica={DICA_ALIAS}><CampoTexto value={v.alias} onChange={(e) => setV({ ...v, alias: e.target.value })} /></Campo>
          </div>
          <Campo rotulo="Descrição"><CampoTexto value={v.descricao} onChange={(e) => setV({ ...v, descricao: e.target.value })} /></Campo>
          <p className="rounded-md border border-border bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
            Depois de criar, adicione os documentos informando <b>Número do documento + HH previsto + Valor/hora</b>.
            O custo previsto de cada documento será calculado no backend e os totais base do pacote serão atualizados automaticamente.
          </p>
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onFechar} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={salvando} className="gap-1.5">{salvando && <Loader2 className="h-4 w-4 animate-spin" />}Cadastrar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ----------------------------------------------------------------------------------------------- importação ----
function ImportarExcel({ projectId, onFechar, onImportado }: { projectId: string; onFechar: () => void; onImportado: () => void }) {
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [previa, setPrevia] = useState<PreviaImportacao | null>(null);
  const [etapa, setEtapa] = useState<"escolher" | "previa" | "concluido">("escolher");
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [resultado, setResultado] = useState<string | null>(null);
  const [baixandoModelo, setBaixandoModelo] = useState(false);

  const baixarModelo = async () => {
    setBaixandoModelo(true); setErro(null);
    try { await downloadModeloPacotes(projectId); }
    catch (x) { setErro(mensagemDe(x)); }
    finally { setBaixandoModelo(false); }
  };

  const gerarPrevia = async () => {
    if (!arquivo) return;
    setOcupado(true); setErro(null);
    try { setPrevia(await custosApi.previaImportacao(projectId, arquivo)); setEtapa("previa"); }
    catch (x) { setErro(mensagemDe(x)); } finally { setOcupado(false); }
  };
  const confirmar = async () => {
    if (!arquivo) return;
    setOcupado(true); setErro(null);
    try {
      const r = await custosApi.confirmarImportacao(projectId, arquivo);
      setResultado(`${r.packagesCreated} pacote(s) e ${r.documentsCreated} documento(s) criados.`); setEtapa("concluido"); onImportado();
    } catch (x) { setErro(mensagemDe(x)); } finally { setOcupado(false); }
  };

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !ocupado) onFechar(); }}>
      <DialogContent className="max-w-5xl">
        <DialogHeader>
          <DialogTitle>Importar pacotes e documentos (Excel)</DialogTitle>
          <DialogDescription>Uma linha por documento. A importação é transacional: tudo ou nada.</DialogDescription>
        </DialogHeader>
        <div className="space-y-2 rounded-lg border border-border bg-background/40 p-3 text-xs text-muted-foreground">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="font-semibold text-foreground">Preencha o modelo oficial do N&A Connect</p>
            <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => void baixarModelo()} disabled={ocupado || baixandoModelo}>
              {baixandoModelo ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
              Baixar modelo Excel
            </Button>
          </div>
          <p className="font-semibold text-foreground">Modelo da planilha</p>
          <div className="overflow-x-auto">
            <table className="w-full text-[11px]">
              <thead><tr className="border-b border-border"><th className="px-2 py-1 text-left">Pacote / Profissional</th><th className="px-2 py-1 text-left">Número do documento</th><th className="px-2 py-1 text-right">HH previsto</th><th className="px-2 py-1 text-right">Valor/hora</th></tr></thead>
              <tbody><tr><td className="px-2 py-1">EMPRESA X – Civil</td><td className="px-2 py-1 font-mono">BML011.013-000-PL0-AT-001</td><td className="px-2 py-1 text-right">40</td><td className="px-2 py-1 text-right">150,00</td></tr></tbody>
            </table>
          </div>
          <p>O backend calcula o custo do documento = HH previsto × Valor/hora. Opcionais: Empresa e Alias no Timesheet ({DICA_ALIAS.toLowerCase()}).</p>
          <p>Cabeçalhos aceitos (sem acento/espaço): <b>Pacote</b> ou Profissional · <b>Número do documento</b> (DocumentoCodigo, N&A Document Number) · <b>HH previsto</b> (HH prevista documento, HHAlocada) · <b>Valor/hora</b> (Custo hora, Valor por hora).</p>
          <p>Formato anterior continua aceito: Custo documento/CustoAlocado e totais do pacote (HH total pacote/HHBase, Custo total pacote/CustoBase). Disciplina da planilha não é oficial: vale a canônica da Norma de Coordenação (Doc3).</p>
        </div>
        {etapa === "escolher" && (
          <div className="space-y-3">
            <label className="flex cursor-pointer flex-col items-center gap-2 rounded-lg border border-dashed border-border p-6 text-center hover:bg-accent/40">
              <Upload className="h-6 w-6 text-muted-foreground" />
              <span className="text-sm">{arquivo ? arquivo.name : "Selecionar arquivo .xlsx (até 10 MB)"}</span>
              <input type="file" accept=".xlsx" className="sr-only" onChange={(e) => { setArquivo(e.target.files?.[0] ?? null); setErro(null); }} />
            </label>
          </div>
        )}
        {etapa === "previa" && previa && (
          <div className="space-y-3">
            <div className="flex flex-wrap gap-2 text-sm">
              <span className="rounded-full bg-emerald-500/15 px-3 py-1 font-semibold text-emerald-600 dark:text-emerald-400">{previa.valid} válida(s)</span>
              <span className={cn("rounded-full px-3 py-1 font-semibold", previa.invalid > 0 ? "bg-rose-500/15 text-rose-500" : "bg-muted text-muted-foreground")}>{previa.invalid} com erro</span>
            </div>
            <div className="max-h-[45vh] overflow-auto rounded-lg border border-border">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-card"><tr className="border-b border-border">
                  <th className={thC}>Linha</th><th className={thC}>Pacote / Profissional</th><th className={thC}>Documento</th>
                  <th className={thCNum}>HH prevista</th><th className={thCNum}>Valor/hora</th><th className={thCNum} title="Calculado pelo backend (HH × Valor/hora)">Custo calculado</th><th className={thC}>Validação</th>
                </tr></thead>
                <tbody>
                  {previa.rows.map((r) => {
                    const x = lerExtrasImportacao(r);
                    return (
                      <tr key={r.row} className={cn("border-b border-border/50", r.valid === 0 && "bg-rose-500/5")}>
                        <td className={tdC}>{r.row}</td>
                        <td className={tdC}>{r.package ?? "—"}{(x.empresa || x.alias) && <div className="text-[10px] text-muted-foreground">{[x.empresa, x.alias && `alias: ${x.alias}`].filter(Boolean).join(" · ")}</div>}</td>
                        <td className={`${tdC} font-mono text-xs`}>{r.document ?? "—"}</td>
                        <td className={tdCNum}>{hhOuTraco(x.hhPrevista)}</td>
                        <td className={tdCNum}>{dinheiroOuTraco(x.valorHora)}</td>
                        <td className={`${tdCNum} font-semibold`}>{dinheiroOuTraco(x.custoDocumento)}</td>
                        <td className={cn(tdC, r.valid === 0 ? "text-rose-500" : "text-emerald-600 dark:text-emerald-400")}>
                          {r.valid === 0 ? r.error : "OK"}
                          {x.aviso && <div className="text-[10px] text-amber-600 dark:text-amber-400">⚠ {x.aviso}</div>}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {previa.invalid > 0 && <p className="text-sm text-rose-500">Corrija as linhas com erro na planilha e gere a prévia novamente — nada é importado parcialmente.</p>}
          </div>
        )}
        {etapa === "concluido" && <p className="rounded-md border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-600 dark:text-emerald-400">Importação concluída: {resultado}</p>}
        {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}
        <DialogFooter>
          {etapa === "concluido" ? <Button onClick={onFechar}>Fechar</Button> : <>
            <Button variant="outline" onClick={etapa === "previa" ? () => { setEtapa("escolher"); setPrevia(null); } : onFechar} disabled={ocupado}>{etapa === "previa" ? "Voltar" : "Cancelar"}</Button>
            {etapa === "escolher"
              ? <Button onClick={gerarPrevia} disabled={!arquivo || ocupado} className="gap-1.5">{ocupado && <Loader2 className="h-4 w-4 animate-spin" />}Gerar prévia</Button>
              : <Button onClick={confirmar} disabled={ocupado || !previa || previa.invalid > 0 || previa.valid === 0} className="gap-1.5">{ocupado && <Loader2 className="h-4 w-4 animate-spin" />}Confirmar importação</Button>}
          </>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
