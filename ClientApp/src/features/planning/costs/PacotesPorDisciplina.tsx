// Pacotes → modelo Por Disciplina: totais por disciplina, documentos da LDP Unificada (Norma de Coordenação), rateio igualitário,
// DE/PARA Pacote × LDP e apropriação do Timesheet (bruto × apropriado × excedente). Todos os números vêm do backend.
import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, FileSpreadsheet, Layers, ListChecks, Loader2, Pencil, Plus, Trash2, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import {
  custosApi, dinheiro, horasFmt,
  type CatalogoLdp, type DetalhePacoteDisciplina, type ModeloPacote, type Pacote, type PlanoDisciplina, type PreviaImportacaoDisciplina,
  type PreviaPacoteDisciplina, type Profissional,
} from "./costsApi";
import { Campo, CampoTexto, Carregando, ErroCustos, mensagemDe, tdC, tdCNum, thC, thCNum, useCarga } from "./CustosShared";
import { assinaturaEntrada, linhasDoCadastro, montarPacoteDisciplina, novaLinhaDisciplina, sinal, type LinhaDisciplinaForm } from "./pacoteDisciplinaForm";

// ------------------------------------------------------------------------------------------- seletor ----
export function SeletorModelo({ valor, onChange }: { valor: ModeloPacote; onChange: (m: ModeloPacote) => void }) {
  const opcoes: { id: ModeloPacote; rotulo: string; dica: string; Icone: typeof Layers }[] = [
    { id: "DOCUMENTO", rotulo: "Por Documento", dica: "Cadastro detalhado: um previsto por documento.", Icone: ListChecks },
    { id: "DISCIPLINA", rotulo: "Por Disciplina", dica: "Totais por disciplina, rateados igualmente entre os documentos da LDP.", Icone: Layers },
  ];
  return (
    <div role="radiogroup" aria-label="Modelo de cadastro" className="inline-flex rounded-lg border border-border bg-card/60 p-1">
      {opcoes.map(({ id, rotulo, dica, Icone }) => (
        <button key={id} type="button" role="radio" aria-checked={valor === id} title={dica} onClick={() => onChange(id)}
          className={cn("flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
            valor === id ? "bg-primary text-primary-foreground shadow-sm" : "text-muted-foreground hover:text-foreground")}>
          <Icone className="h-4 w-4" />{rotulo}
        </button>
      ))}
    </div>
  );
}

export const SeloModeloPacote = ({ modelo }: { modelo?: ModeloPacote }) => (
  <span className={cn("rounded px-1.5 py-0.5 text-[10px] font-bold uppercase",
    modelo === "DISCIPLINA" ? "bg-violet-500/15 text-violet-600 dark:text-violet-400" : "bg-sky-500/15 text-sky-600 dark:text-sky-400")}>
    {modelo === "DISCIPLINA" ? "Por disciplina" : "Por documento"}
  </span>
);

const Origem = ({ tipo }: { tipo: "SIMULAÇÃO" | "LDP" | "TIMESHEET" | "PACOTE" }) => (
  <span className={cn("rounded px-1 py-0.5 text-[9px] font-bold tracking-wide",
    tipo === "SIMULAÇÃO" ? "bg-amber-500/15 text-amber-600" : tipo === "LDP" ? "bg-sky-500/15 text-sky-600" : tipo === "TIMESHEET" ? "bg-emerald-500/15 text-emerald-600" : "bg-violet-500/15 text-violet-600")}>
    {tipo}
  </span>
);

export async function downloadModeloDisciplina(projectId: string) {
  const blob = await custosApi.baixarModeloDisciplina(projectId);
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = "Modelo_Pacotes_Por_Disciplina.xlsx";
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// Rascunho temporário por projeto e pacote; não grava dados no servidor.
type RascunhoDisciplina = {
  cab: { nome: string; alias: string; empresa: string; descricao: string };
  linhas: LinhaDisciplinaForm[];
  atualizadoEm: number;
};
function chaveRascunho(projectId: string, packageId?: number): string {
  return `na.pacotes.disciplina.rascunho.v1:${projectId}:${packageId ?? "novo"}`;
}
function lerRascunho(key: string): RascunhoDisciplina | null {
  try {
    const txt = sessionStorage.getItem(key);
    if (!txt) return null;
    const data: unknown = JSON.parse(txt);
    if (!data || typeof data !== "object") return null;
    const draft = data as Partial<RascunhoDisciplina>;
    if (!draft.cab || !Array.isArray(draft.linhas) || !draft.linhas.length ||
        !draft.linhas.every(x => x && typeof x.chave === "string" && typeof x.disciplina === "string")) return null;
    return draft as RascunhoDisciplina;
  } catch { return null; }
}
// ---------------------------------------------------------------------------------- cadastro / edição ----
export function PacoteDisciplinaDialog({ projectId, editar, onFechar, onSalvo }: {
  projectId: string; editar?: DetalhePacoteDisciplina; onFechar: () => void; onSalvo: () => void;
}) {
  const catalogo = useCarga<CatalogoLdp>(() => custosApi.disciplinasLdp(projectId), [projectId]);
  const profissionais = useCarga<Profissional[]>(() => custosApi.profissionais(projectId), [projectId]);
  const p = editar?.pacote;
  const draftKey = chaveRascunho(projectId, p?.id);
  const [rascunhoInicial] = useState(() => lerRascunho(draftKey));
  const [cab, setCab] = useState(() => rascunhoInicial?.cab ?? { nome: p?.nomeReferencia ?? "", alias: p?.aliasTimesheet ?? "", empresa: p?.empresa ?? "", descricao: p?.descricao ?? "" });
  const [linhas, setLinhas] = useState<LinhaDisciplinaForm[]>(() => rascunhoInicial?.linhas ?? (editar ? linhasDoCadastro(editar.cadastro) : [novaLinhaDisciplina()]));
  useEffect(() => {
    try { sessionStorage.setItem(draftKey, JSON.stringify({ cab, linhas, atualizadoEm: Date.now() } satisfies RascunhoDisciplina)); }
    catch { /* armazenamento opcional, sem alterar banco */ }
  }, [draftKey, cab, linhas]);
  const [previa, setPrevia] = useState<{ assinatura: string; dados: PreviaPacoteDisciplina } | null>(null);
  const [ocupado, setOcupado] = useState<"simular" | "salvar" | null>(null);
  const [erros, setErros] = useState<string[]>([]);

  const montado = useMemo(() => montarPacoteDisciplina(cab, linhas), [cab, linhas]);
  const atual = "input" in montado && previa?.assinatura === assinaturaEntrada(montado.input) ? previa.dados : null;
  const porDisciplina = useMemo(() => new Map((catalogo.dados?.disciplinas ?? []).map((d) => [d.disciplina, d])), [catalogo.dados]);
  const alterar = (chave: string, x: Partial<LinhaDisciplinaForm>) => setLinhas((ls) => ls.map((l) => (l.chave === chave ? { ...l, ...x } : l)));

  const simular = async () => {
    if ("erros" in montado) { setErros(montado.erros); return; }
    setErros([]); setOcupado("simular");
    try { setPrevia({ assinatura: assinaturaEntrada(montado.input), dados: await custosApi.simularPacoteDisciplina(projectId, montado.input) }); }
    catch (e) { toast.error(mensagemDe(e)); } finally { setOcupado(null); }
  };
  const salvar = async () => {
    if (!("input" in montado) || !atual?.valido) return;
    setOcupado("salvar");
    try {
      if (p) await custosApi.atualizarPacoteDisciplina(projectId, p.id, montado.input);
      else await custosApi.criarPacoteDisciplina(projectId, montado.input);
      try { sessionStorage.removeItem(draftKey); } catch { /* opcional */ }
      toast.success(p ? "Pacote por disciplina atualizado (versão anterior preservada no histórico)." : "Pacote por disciplina cadastrado.");
      onSalvo();
    } catch (e) { toast.error(mensagemDe(e)); } finally { setOcupado(null); }
  };

  return (
    <Dialog open onOpenChange={() => { /* evitar fechamento acidental ao trocar de janela */ }}>
      <DialogContent onInteractOutside={(e) => e.preventDefault()} onEscapeKeyDown={(e) => e.preventDefault()}
        className="max-h-[92vh] overflow-y-auto sm:max-w-5xl [&>button.absolute]:hidden">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">{p ? `Editar ${p.nomeReferencia}` : "Novo pacote"} <SeloModeloPacote modelo="DISCIPLINA" /></DialogTitle>
          <DialogDescription>
            Informe o total previsto de cada disciplina. Os documentos vêm da LDP Unificada (disciplina pela Norma de Coordenação) e recebem rateio igualitário.
            HH e custo realizados vêm do Timesheet — nada é lançado aqui.
          </DialogDescription>
        </DialogHeader>
        <p role="status" className="text-xs text-emerald-600">Rascunho salvo automaticamente nesta aba. Pode alternar para o Excel e voltar sem perder o preenchimento.{rascunhoInicial ? " Rascunho anterior recuperado." : ""}</p>
        <div className="grid gap-3 sm:grid-cols-2">
          <Campo rotulo="Nome do pacote"><CampoTexto value={cab.nome} onChange={(e) => setCab({ ...cab, nome: e.target.value })} placeholder="Ex.: Pacote Everton" /></Campo>
          <Campo rotulo="Profissional (alias no Timesheet)" dica="Texto que identifica o profissional nos lançamentos do Timesheet (contém).">
            <CampoTexto list="pacote-disciplina-profissionais" value={cab.alias} onChange={(e) => setCab({ ...cab, alias: e.target.value })} placeholder="Ex.: Everton" />
            <datalist id="pacote-disciplina-profissionais">
              {(profissionais.dados ?? []).filter((x) => x.ativo).map((x) => <option key={x.id} value={x.aliasTimesheet || x.nome}>{x.nome}</option>)}
            </datalist>
          </Campo>
          <Campo rotulo="Empresa"><CampoTexto value={cab.empresa} onChange={(e) => setCab({ ...cab, empresa: e.target.value })} /></Campo>
          <Campo rotulo="Descrição"><CampoTexto value={cab.descricao} onChange={(e) => setCab({ ...cab, descricao: e.target.value })} /></Campo>
        </div>

        {catalogo.erro ? <ErroCustos erro={catalogo.erro} /> : !catalogo.dados ? <Carregando texto="Lendo disciplinas da LDP Unificada…" /> : (
          <section className="space-y-3">
            {catalogo.dados.diagnostico && <p className="flex items-center gap-1.5 text-xs text-amber-600"><AlertTriangle className="h-3.5 w-3.5" />{catalogo.dados.diagnostico}</p>}
            {catalogo.dados.disciplinas.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma disciplina com documentos na LDP deste projeto.</p>}
            {linhas.map((l, i) => {
              const ldp = porDisciplina.get(l.disciplina);
              const plano = atual?.disciplinas[i];
              return (
                <div key={l.chave} className="space-y-3 rounded-xl border border-border bg-card/50 p-3">
                  <div className="grid gap-3 md:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1fr)_auto]">
                    <Campo rotulo="Disciplina">
                      <select className="h-10 rounded-md border border-input bg-background px-2 text-sm" value={l.disciplina}
                        onChange={(e) => alterar(l.chave, { disciplina: e.target.value, documentos: [] })}>
                        <option value="">Selecione…</option>
                        {catalogo.dados!.disciplinas.map((d) => (
                          <option key={d.disciplina} value={d.disciplina} disabled={linhas.some((o) => o.chave !== l.chave && o.disciplina === d.disciplina)}>
                            {d.disciplina} · {d.ativos} doc. ativos
                          </option>
                        ))}
                      </select>
                    </Campo>
                    <Campo rotulo="HH previsto total" dica={ldp ? `LDP (ativos): ${horasFmt(ldp.hhLdpAtivos)} HH` : undefined}>
                      <div className="flex gap-1">
                        <CampoTexto inputMode="decimal" value={l.hh} onChange={(e) => alterar(l.chave, { hh: e.target.value })} placeholder="0" />
                        {ldp && <Button type="button" variant="outline" size="sm" className="h-10 shrink-0 text-xs" title="Sugerir o HH previsto da LDP (pode ser ajustado)"
                          onClick={() => alterar(l.chave, { hh: String(ldp.hhLdpAtivos).replace(".", ",") })}>LDP</Button>}
                      </div>
                    </Campo>
                    <Campo rotulo="Custo previsto total (R$)" dica={ldp && ldp.custoLdpAtivos > 0 ? `LDP: ${dinheiro(ldp.custoLdpAtivos)}` : undefined}>
                      <CampoTexto inputMode="decimal" value={l.custo} onChange={(e) => alterar(l.chave, { custo: e.target.value })} placeholder="0,00" />
                    </Campo>
                    <div className="flex items-end">
                      <Button type="button" variant="ghost" size="icon" aria-label="Remover disciplina" disabled={linhas.length === 1}
                        onClick={() => setLinhas((ls) => ls.filter((x) => x.chave !== l.chave))}><Trash2 className="h-4 w-4" /></Button>
                    </div>
                  </div>
                  <div className="flex flex-wrap items-center gap-4 text-sm">
                    <span className="text-xs font-semibold">Abrangência:</span>
                    <label className="flex items-center gap-1.5"><input type="radio" checked={l.abrangencia === "TODOS"} onChange={() => alterar(l.chave, { abrangencia: "TODOS" })} />
                      Todos os documentos ativos{ldp ? ` (${ldp.ativos})` : ""}</label>
                    <label className="flex items-center gap-1.5"><input type="radio" checked={l.abrangencia === "SELECIONADOS"} onChange={() => alterar(l.chave, { abrangencia: "SELECIONADOS" })} />
                      Documentos selecionados{l.abrangencia === "SELECIONADOS" ? ` (${l.documentos.length})` : ""}</label>
                  </div>
                  {l.abrangencia === "SELECIONADOS" && ldp && (
                    <div className="max-h-48 overflow-y-auto rounded-lg border border-border">
                      <div className="flex gap-3 border-b border-border px-3 py-1.5 text-xs">
                        <button type="button" className="text-primary hover:underline" onClick={() => alterar(l.chave, { documentos: ldp.itens.filter((d) => !d.cancelado).map((d) => d.codigo) })}>Marcar ativos</button>
                        <button type="button" className="text-primary hover:underline" onClick={() => alterar(l.chave, { documentos: [] })}>Limpar</button>
                      </div>
                      {ldp.itens.map((d) => (
                        <label key={d.codigo} className={cn("flex items-center gap-2 px-3 py-1 text-xs hover:bg-accent/40", d.cancelado && "text-muted-foreground line-through")}>
                          <input type="checkbox" disabled={d.cancelado} checked={l.documentos.includes(d.codigo)}
                            onChange={(e) => alterar(l.chave, { documentos: e.target.checked ? [...l.documentos, d.codigo] : l.documentos.filter((x) => x !== d.codigo) })} />
                          <span className="font-mono">{d.codigo}</span><span className="truncate">{d.titulo ?? ""}</span>
                          <span className="ml-auto shrink-0">{d.status ?? "—"} · {d.plannedHH === null ? "—" : horasFmt(d.plannedHH)} HH</span>
                        </label>
                      ))}
                    </div>
                  )}
                  <Campo rotulo="Justificativa da divergência Pacote × LDP (opcional)">
                    <Textarea rows={1} value={l.justificativa} onChange={(e) => alterar(l.chave, { justificativa: e.target.value })} maxLength={1000} />
                  </Campo>
                  {plano && <ResultadoPlano plano={plano} simulacao />}
                </div>
              );
            })}
            <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => setLinhas((ls) => [...ls, novaLinhaDisciplina()])}><Plus className="h-4 w-4" />Disciplina</Button>
          </section>
        )}

        {erros.length > 0 && <ul className="list-disc rounded-lg border border-rose-500/30 bg-rose-500/5 py-2 pl-6 text-sm text-rose-600">{erros.map((e) => <li key={e}>{e}</li>)}</ul>}
        {atual && (
          <div className={cn("rounded-lg border p-3 text-sm", atual.valido ? "border-emerald-500/30 bg-emerald-500/5" : "border-rose-500/30 bg-rose-500/5")}>
            <p className="flex flex-wrap items-center gap-2 font-semibold"><Origem tipo="SIMULAÇÃO" />
              Total do pacote {horasFmt(atual.hhTotal)} HH · {dinheiro(atual.custoTotal)} — LDP {horasFmt(atual.hhLdpTotal)} HH · divergência {sinal(atual.hhTotal - atual.hhLdpTotal)} HH</p>
            {!atual.valido && <p className="mt-1 text-rose-600">Rateio inconsistente: corrija os erros indicados antes de salvar.</p>}
            {atual.erros.map((e) => <p key={e} className="text-rose-600">{e}</p>)}
          </div>
        )}
        {previa && !atual && <p className="text-xs text-amber-600">Os dados mudaram depois da simulação. Simule novamente para salvar.</p>}
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onFechar} disabled={ocupado !== null}>Fechar (manter rascunho)</Button>
          <Button variant="ghost" disabled={ocupado !== null} onClick={() => {
            setCab({ nome: p?.nomeReferencia ?? "", alias: p?.aliasTimesheet ?? "", empresa: p?.empresa ?? "", descricao: p?.descricao ?? "" });
            setLinhas(editar ? linhasDoCadastro(editar.cadastro) : [novaLinhaDisciplina()]);
            setPrevia(null); setErros([]);
          }}>Limpar rascunho</Button>
          <Button variant="outline" onClick={() => void simular()} disabled={ocupado !== null || !catalogo.dados}>
            {ocupado === "simular" ? <Loader2 className="h-4 w-4 animate-spin" /> : "Simular rateio"}
          </Button>
          <Button onClick={() => void salvar()} disabled={ocupado !== null || !atual?.valido} title={!atual ? "Simule o rateio antes de salvar." : undefined}>
            {ocupado === "salvar" ? <Loader2 className="h-4 w-4 animate-spin" /> : p ? "Salvar alterações" : "Salvar pacote"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** DE/PARA Pacote × LDP + documentos do rateio (simulado ou vigente). */
function ResultadoPlano({ plano, simulacao }: { plano: PlanoDisciplina; simulacao?: boolean }) {
  const [todos, setTodos] = useState(false);
  const docs = todos ? plano.documentos : plano.documentos.slice(0, 8);
  return (
    <div className="space-y-2 rounded-lg border border-border bg-background/60 p-3 text-xs">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
        <Origem tipo={simulacao ? "SIMULAÇÃO" : "PACOTE"} />
        <span>Pacote <b>{horasFmt(plano.hhPrevisto)} HH</b> · {dinheiro(plano.custoPrevisto)}</span>
        <span className="flex items-center gap-1"><Origem tipo="LDP" /><b>{horasFmt(plano.hhLdp)} HH</b> · {dinheiro(plano.custoLdp)}</span>
        <span className={cn("font-semibold", plano.divergenciaHH !== 0 ? "text-amber-600" : "text-emerald-600")}>Divergência {sinal(plano.divergenciaHH)} HH</span>
        <span>{plano.documentosElegiveis} documento(s) elegível(is) de {plano.documentosDisciplina} na disciplina</span>
      </div>
      {plano.erros.map((e) => <p key={e} className="text-rose-600">{e}</p>)}
      {plano.avisos.map((a) => <p key={a} className="text-amber-600">{a}</p>)}
      {plano.documentos.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead><tr className="border-b border-border"><th className={thC}>Documento</th><th className={thC}>Status</th><th className={thCNum}>HH rateio</th><th className={thCNum}>Custo rateio</th><th className={thCNum}>HH LDP</th></tr></thead>
            <tbody>
              {docs.map((d) => (
                <tr key={d.documentoCodigo} className="border-b border-border/40">
                  <td className={`${tdC} font-mono`} title={d.titulo ?? undefined}>{d.documentoCodigo}</td><td className={tdC}>{d.documentStatus ?? "—"}</td>
                  <td className={tdCNum}>{horasFmt(d.hhPrevisto)}</td><td className={tdCNum}>{dinheiro(d.custoPrevisto)}</td><td className={tdCNum}>{d.hhLdp === null ? "—" : horasFmt(d.hhLdp)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {plano.documentos.length > 8 && <button type="button" className="mt-1 text-primary hover:underline" onClick={() => setTodos(!todos)}>{todos ? "Mostrar menos" : `Mostrar os ${plano.documentos.length} documentos`}</button>}
        </div>
      )}
      {plano.excluidos.length > 0 && <p className="text-muted-foreground">Fora do rateio ativo: {plano.excluidos.map((x) => `${x.documentoCodigo} (${x.motivo})`).join("; ")}</p>}
    </div>
  );
}

// ------------------------------------------------------------------------------------------- detalhe ----
export function DetalheDisciplina({ projectId, p, onAlterado }: { projectId: string; p: Pacote; onAlterado: () => void }) {
  const detalhe = useCarga<DetalhePacoteDisciplina>(() => custosApi.detalhePacoteDisciplina(projectId, p.id), [projectId, p.id]);
  const [editar, setEditar] = useState(false);
  if (detalhe.erro) return <ErroCustos erro={detalhe.erro} />;
  if (!detalhe.dados) return <Carregando texto="Calculando rateio e apropriação…" />;
  const d = detalhe.dados; const t = d.totais;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">Rateio pela LDP <b>atual</b>: documentos novos entram e cancelados saem automaticamente; o total do pacote não muda.</p>
        {p.ativo && <Button variant="outline" size="sm" className="gap-1.5" onClick={() => setEditar(true)}><Pencil className="h-3.5 w-3.5" />Editar disciplinas</Button>}
      </div>
      <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
        <Cartao rotulo="HH previsto" valor={horasFmt(t.hhPrevisto)} origem="PACOTE" />
        <Cartao rotulo="HH Timesheet (bruto)" valor={horasFmt(t.hhBruto)} origem="TIMESHEET" />
        <Cartao rotulo="HH apropriado do Timesheet" valor={horasFmt(t.hhApropriado)} detalhe="até o teto disciplinar" />
        <Cartao rotulo="HH excedente" valor={horasFmt(t.hhExcedente)} alerta={t.hhExcedente > 0} detalhe="não apropriado" />
        <Cartao rotulo="HH saldo" valor={horasFmt(t.hhSaldo)} />
        <Cartao rotulo="HH pendente" valor={horasFmt(t.hhPendente)} alerta={t.hhPendente > 0} detalhe="tratar individualmente" />
        <Cartao rotulo="Custo previsto" valor={dinheiro(t.custoPrevisto)} origem="PACOTE" />
        <Cartao rotulo="Custo bruto" valor={dinheiro(t.custoBruto)} detalhe="HH bruto × custo/hora do pacote" />
        <Cartao rotulo="Custo apropriado" valor={dinheiro(t.custoApropriado)} />
        <Cartao rotulo="Custo excedente" valor={dinheiro(t.custoExcedente)} alerta={t.custoExcedente > 0} />
        <Cartao rotulo="Custo liberado (status)" valor={dinheiro(t.custoLiberado)} detalhe="peso do Document_Status" />
      </div>
      {d.historicalMeasurement && d.historicalMeasurement.disciplines.length > 0 && (
        <section className="rounded-xl border border-sky-500/40 bg-sky-500/5 p-3 space-y-2">
          <h3 className="font-semibold">Medição histórica confirmada</h3>
          <p className="text-xs text-muted-foreground">Complemento recalculado do Timesheet corrigido atual. Medição confirmada é evidência gerencial; novos lançamentos substituem o complemento.</p>
          <div className="grid gap-2 sm:grid-cols-4">
            <Cartao rotulo="HH complemento histórico" valor={horasFmt(d.historicalMeasurement.complementHours)} />
            <Cartao rotulo="Custo complemento histórico" valor={dinheiro(d.historicalMeasurement.complementCost)} />
            <Cartao rotulo="HH gerencial do pacote" valor={horasFmt(d.hhGerencial ?? null)} />
            <Cartao rotulo="Custo gerencial do pacote" valor={dinheiro(d.custoGerencial ?? null)} />
          </div>
          <div className="overflow-x-auto"><table className="w-full text-xs"><thead><tr>
            <th className={thC}>Disciplina</th><th className={thCNum}>HH medidas</th><th className={thCNum}>HH Timesheet</th>
            <th className={thCNum}>HH complemento</th><th className={thCNum}>Custo complemento</th><th className={thCNum}>Custo gerencial</th>
          </tr></thead><tbody>{d.historicalMeasurement.disciplines.map(h=><tr key={h.discipline}>
            <td className={tdC}>{h.discipline}</td><td className={tdCNum}>{horasFmt(h.confirmedHours)}</td>
            <td className={tdCNum}>{horasFmt(h.timesheetHours)}</td><td className={tdCNum}>{horasFmt(h.balance.complementHours)}</td>
            <td className={tdCNum}>{dinheiro(h.balance.complementCost)}</td><td className={tdCNum}>{dinheiro(h.balance.recognizedCost+h.balance.complementCost)}</td>
          </tr>)}</tbody></table></div>
          {d.historicalMeasurement.diagnostics.map((s,i)=><p key={i} className="text-xs text-amber-600">{s}</p>)}
          <details className="text-xs text-muted-foreground"><summary>Histórico e origem</summary>
            {d.historicalMeasurement.disciplines.map(h=><p key={h.discipline}>{h.discipline}: confirmação #{h.confirmationId}, {h.confirmedAtUtc}, {h.confirmedBy}. {h.origin}</p>)}
          </details>
        </section>
      )}
      {d.excedentes.length > 0 && (
        <section className="rounded-xl border border-rose-500/40 bg-rose-500/5 p-3 text-sm">
          <h3 className="mb-1 flex items-center gap-1.5 font-semibold text-rose-600"><AlertTriangle className="h-4 w-4" />Alertas documentais e excedentes ({d.excedentes.length})</h3>
          <p className="mb-2 text-xs text-muted-foreground">Concentração acima do rateio documental é alerta e pode ser apropriada dentro do teto da disciplina. Excedentes disciplinares permanecem visíveis; o Timesheet original é preservado.</p>
          <ul className="space-y-0.5 text-xs">
            {d.excedentes.map((x) => <li key={`${x.disciplina}|${x.documentoCodigo}`}>{x.disciplina} · <span className="font-mono">{x.documentoCodigo ?? "—"}</span> — previsto {horasFmt(x.hhPrevisto)} · bruto {horasFmt(x.hhBruto)} · apropriado {horasFmt(x.hhApropriado)} · <b className="text-rose-600">excedente {horasFmt(x.hhExcedente)}</b></li>)}
          </ul>
        </section>
      )}
      {d.pendencias.length > 0 && (
        <section className="rounded-xl border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
          <h3 className="mb-1 font-semibold text-amber-700 dark:text-amber-400">Horas do Timesheet não apropriadas</h3>
          <ul className="space-y-0.5 text-xs">{d.pendencias.map((x, i) => <li key={i}><b>{horasFmt(x.horas)} HH</b> ({x.lancamentos} lançamento(s)) · {x.documentoCodigo ?? x.disciplina ?? "—"} — {x.motivo}</li>)}</ul>
        </section>
      )}
      {d.rateioAtual.disciplinas.map((plano) => {
        const cadastro = d.cadastro.find((c) => c.disciplina === plano.disciplina);
        const linhas = d.apropriacao.filter((a) => a.disciplina === plano.disciplina);
        return (
          <section key={plano.disciplina} className="space-y-2 rounded-xl border border-border bg-card/50 p-3">
            <h3 className="font-semibold">{plano.disciplina} <span className="text-xs font-normal text-muted-foreground">· {plano.abrangencia === "TODOS" ? "todos os documentos ativos" : "documentos selecionados"}</span></h3>
            {cadastro && cadastro.hhPrevistoLdp !== null && (
              <p className="text-xs text-muted-foreground">No cadastro: LDP {horasFmt(cadastro.hhPrevistoLdp)} HH em {cadastro.documentosLdp ?? 0} documento(s){cadastro.justificativa ? ` · Justificativa: ${cadastro.justificativa}` : ""}</p>
            )}
            <ResultadoPlano plano={plano} />
            {linhas.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead><tr className="border-b border-border">
                    <th className={thC}>Documento</th><th className={thCNum}>Previsto</th><th className={thCNum}>Com documento</th><th className={thCNum}>Rateio s/ doc.</th>
                    <th className={thCNum}>Bruto</th><th className={thCNum}>Apropriado</th><th className={thCNum}>Excedente</th><th className={thCNum}>Saldo</th><th className={thCNum}>Custo apropriado</th>
                  </tr></thead>
                  <tbody>
                    {linhas.map((a) => (
                      <tr key={a.documentoCodigo} className="border-b border-border/40">
                        <td className={`${tdC} font-mono`}>{a.documentoCodigo}</td><td className={tdCNum}>{horasFmt(a.hhPrevisto)}</td><td className={tdCNum}>{horasFmt(a.hhDireto)}</td>
                        <td className={tdCNum}>{horasFmt(a.hhRateado)}</td><td className={tdCNum}>{horasFmt(a.hhBruto)}</td><td className={`${tdCNum} font-semibold`}>{horasFmt(a.hhApropriado)}</td>
                        <td className={cn(tdCNum, a.hhExcedente > 0 && "font-semibold text-rose-600")}>{horasFmt(a.hhExcedente)}</td><td className={tdCNum}>{horasFmt(a.hhSaldo)}</td>
                        <td className={tdCNum}>{dinheiro(a.custoApropriado)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        );
      })}
      {editar && <PacoteDisciplinaDialog projectId={projectId} editar={d} onFechar={() => setEditar(false)} onSalvo={() => { setEditar(false); detalhe.recarregar(); onAlterado(); }} />}
    </div>
  );
}

function Cartao({ rotulo, valor, detalhe, alerta, origem }: { rotulo: string; valor: string; detalhe?: string; alerta?: boolean; origem?: "PACOTE" | "TIMESHEET" }) {
  return (
    <div className={cn("rounded-lg border bg-card/60 px-3 py-2", alerta ? "border-rose-500/40" : "border-border")}>
      <p className="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}{origem && <Origem tipo={origem} />}</p>
      <p className={cn("text-sm font-semibold tabular-nums", alerta && "text-rose-500")}>{valor}</p>
      {detalhe && <p className="text-[10px] text-muted-foreground">{detalhe}</p>}
    </div>
  );
}

// ------------------------------------------------------------------------------------------ importação ----
export function ImportarExcelDisciplina({ projectId, onFechar, onImportado }: { projectId: string; onFechar: () => void; onImportado: () => void }) {
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [previa, setPrevia] = useState<PreviaImportacaoDisciplina | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const escolher = async (f: File | null) => {
    setArquivo(f); setPrevia(null);
    if (!f) return;
    setOcupado(true);
    try { setPrevia(await custosApi.previaImportacaoDisciplina(projectId, f)); } catch (e) { toast.error(mensagemDe(e)); } finally { setOcupado(false); }
  };
  const confirmar = async () => {
    if (!arquivo || !previa?.valido) return;
    setOcupado(true);
    try {
      const r = await custosApi.confirmarImportacaoDisciplina(projectId, arquivo);
      toast.success(`${r.packagesCreated} pacote(s) e ${r.documentsCreated} disciplina(s) importados.`);
      onImportado(); onFechar();
    } catch (e) { toast.error(mensagemDe(e)); } finally { setOcupado(false); }
  };
  return (
    <Dialog open onOpenChange={() => { /* importação não fecha por perda de foco */ }}>
      <DialogContent onInteractOutside={(e) => e.preventDefault()} onEscapeKeyDown={(e) => e.preventDefault()}
        className="max-h-[92vh] overflow-y-auto sm:max-w-4xl [&>button.absolute]:hidden">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">Importar pacotes <SeloModeloPacote modelo="DISCIPLINA" /></DialogTitle>
          <DialogDescription>Use o modelo Por Disciplina. A prévia mostra o rateio e a divergência com a LDP; nada é gravado antes de confirmar.</DialogDescription>
        </DialogHeader>
        <label className="flex cursor-pointer items-center gap-2 rounded-lg border border-dashed border-border p-4 text-sm hover:bg-accent/40">
          <Upload className="h-4 w-4" />{arquivo ? arquivo.name : "Escolher arquivo .xlsx"}
          <input type="file" accept=".xlsx" className="hidden" onChange={(e) => void escolher(e.target.files?.[0] ?? null)} />
        </label>
        {ocupado && !previa && <Carregando texto="Lendo planilha e simulando rateio…" />}
        {previa && (
          <div className="space-y-3">
            <p className={cn("text-sm font-semibold", previa.valido ? "text-emerald-600" : "text-rose-600")}>
              {previa.valido ? `Pronto para importar: ${previa.pacotes.length} pacote(s).` : `Há problemas: ${previa.invalidas} linha(s) inválida(s) ou rateio inconsistente.`}
            </p>
            {previa.rows.filter((r) => r.erro).map((r) => <p key={r.row} className="text-xs text-rose-600">Linha {r.row}: {r.erro}</p>)}
            {previa.pacotes.map((pk) => (
              <section key={pk.pacote} className="space-y-2 rounded-xl border border-border p-3">
                <h3 className="flex items-center gap-2 font-semibold"><FileSpreadsheet className="h-4 w-4" />{pk.pacote}<span className="text-xs font-normal text-muted-foreground">{pk.profissional ?? "sem alias"} · {pk.empresa ?? "sem empresa"}</span></h3>
                {pk.erros.map((e) => <p key={e} className="text-xs text-rose-600">{e}</p>)}
                {pk.previa.erros.map((e) => <p key={e} className="text-xs text-rose-600">{e}</p>)}
                {pk.previa.disciplinas.map((pl) => <div key={pl.disciplina}><p className="mb-1 text-xs font-semibold">{pl.disciplina}</p><ResultadoPlano plano={pl} simulacao /></div>)}
              </section>
            ))}
          </div>
        )}
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onFechar} disabled={ocupado}>Cancelar</Button>
          <Button onClick={() => void confirmar()} disabled={ocupado || !previa?.valido}>{ocupado && previa ? <Loader2 className="h-4 w-4 animate-spin" /> : "Confirmar importação"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Aviso exibido no modelo Por Disciplina enquanto o script 09D não foi aplicado. */
export function AvisoModeloIndisponivel({ mensagem }: { mensagem: string | null }) {
  return (
    <div className="flex items-start gap-2 rounded-xl border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
      <div><p className="font-semibold">Modelo Por Disciplina ainda não habilitado</p><p className="text-xs text-muted-foreground">{mensagem ?? "Aplique Scripts/09D_PackageDisciplineModel.sql em DB_NA_CONNECT."} Os pacotes Por Documento continuam funcionando normalmente.</p></div>
    </div>
  );
}
