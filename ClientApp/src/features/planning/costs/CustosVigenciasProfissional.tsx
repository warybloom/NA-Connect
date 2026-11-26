// Histórico salarial (vigências) de um profissional. Nada aqui apaga histórico: "Excluir" desativa a vigência via API
// (Ativo = 0) e "Editar" corrige o próprio período; os itens anteriores são desativados e regravados pelo backend.
import { useCallback, useEffect, useState, type FormEvent } from "react";
import toast from "react-hot-toast";
import { Loader2, Pencil, Plus, Trash2, X } from "lucide-react";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  custosApi, dataFmt, dinheiro, horasFmt,
  type PeriodoProfissional, type PreviaVigencia, type VigenciaProfissionalInput,
} from "./costsApi";
import { montarPayload, newForm, periodToForm, somenteValorHora, type FormState } from "./professionalPeriodForm";

import { ConfirmarDesativacao, mensagemDe } from "./CustosShared";

type TipoCalculo = "FIXO" | "PERCENTUAL";
type Item = FormState["items"][number];
const rotuloPeriodo = (p: { start: string; end: string | null }) => `${dataFmt(p.start)} – ${p.end ? dataFmt(p.end) : "atual"}`;

const CATEGORIAS: { value: string; label: string }[] = [
  { value: "ENCARGOS", label: "Encargos" },
  { value: "SOFTWARE", label: "Software" },
  { value: "COMPUTADOR", label: "Computadores" },
  { value: "BENEFICIO", label: "Benefício" },
  { value: "OUTRO", label: "Outro" },
];

const NOVO_ITEM = (): Item => ({
  category: "ENCARGOS",
  description: "Encargos",
  calculationType: "FIXO",
  amount: "",
  percentage: "",
});


export default function CustosVigenciasProfissional({
  projectId, professionalId, hours, onSaved,
}: {
  projectId: string; professionalId: number; hours: number; onSaved: () => void;
}) {
  const [periods, setPeriods] = useState<PeriodoProfissional[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<FormState | null>(null);
  const [erroForm, setErroForm] = useState<string | null>(null);
  const [excluir, setExcluir] = useState<PeriodoProfissional | null>(null);
  const [encerramento, setEncerramento] = useState<{ preview: PreviaVigencia; payload: VigenciaProfissionalInput } | null>(null);
  const [versao, setVersao] = useState(0);
  const recarregar = useCallback(() => setVersao((v) => v + 1), []);

  useEffect(() => {
    let vivo = true;
    setLoading(true);
    custosApi.vigencias(projectId, professionalId)
      .then((rows) => { if (vivo) setPeriods(rows); })
      .catch((e) => { if (vivo) toast.error(mensagemDe(e)); })
      .finally(() => { if (vivo) setLoading(false); });
    return () => { vivo = false; };
  }, [projectId, professionalId, versao]);

  const current = periods?.find((p) => p.active && !p.end) ?? periods?.find((p) => p.active) ?? null;
  const setItem = (index: number, patch: Partial<Item>) =>
    setForm((f) => (f ? { ...f, items: f.items.map((x, i) => (i === index ? { ...x, ...patch } : x)) } : f));
  const patchForm = (patch: Partial<FormState>) => setForm((f) => (f ? { ...f, ...patch } : f));

  const concluir = (mensagem: string) => {
    toast.success(mensagem);
    setForm(null);
    setErroForm(null);
    recarregar();
    onSaved();
  };

  const criar = async (payload: VigenciaProfissionalInput, preview: PreviaVigencia) => {
    setSaving(true);
    try {
      await custosApi.criarVigencia(projectId, professionalId, payload);
      setEncerramento(null);
      concluir(preview.isHistoricalBackfill
        ? "Vigência histórica registrada sem alterar períodos posteriores."
        : "Nova vigência registrada; o histórico anterior foi preservado.");
    } catch (e) {
      setEncerramento(null);
      setErroForm(mensagemDe(e));
    } finally {
      setSaving(false);
    }
  };

  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    if (!form) return;
    const montado = montarPayload(form);
    if (!montado.ok) { setErroForm(montado.erro); return; }
    setErroForm(null);

    if (form.mode === "edit") {
      setSaving(true);
      try {
        await custosApi.atualizarVigencia(projectId, professionalId, form.periodId!, {
          ...montado.value,
          confirmClosePrevious: false,
          copyPreviousComposition: false,
          excludePreviousItemIds: [],
        });
        concluir("Vigência atualizada.");
      } catch (err) {
        setErroForm(mensagemDe(err));
      } finally {
        setSaving(false);
      }
      return;
    }

    setSaving(true);
    let preview: PreviaVigencia;
    try {
      preview = await custosApi.previaVigencia(projectId, professionalId, montado.value.start, montado.value.end);
    } catch (err) {
      setErroForm(mensagemDe(err));
      setSaving(false);
      return;
    }
    setSaving(false);

    const ativos = current?.items.filter((i) => i.active) ?? [];
    const payload: VigenciaProfissionalInput = {
      ...montado.value,
      confirmClosePrevious: preview.confirmationRequired,
      copyPreviousComposition: preview.isHistoricalBackfill ? false : montado.value.copyPreviousComposition,
      excludePreviousItemIds: ativos.filter((i) => !form.copiedIds.includes(i.id)).map((i) => i.id),
    };
    if (preview.confirmationRequired) setEncerramento({ preview, payload });
    else await criar(payload, preview);
  };

  const confirmarExclusao = async () => {
    if (!excluir) return;
    setSaving(true);
    try {
      await custosApi.desativarVigencia(projectId, professionalId, excluir.id);
      toast.success("Vigência excluída do cálculo e preservada no histórico.");
      if (form?.periodId === excluir.id) setForm(null);
      setExcluir(null);
      recarregar();
      onSaved();
    } catch (err) {
      toast.error(mensagemDe(err));
    } finally {
      setSaving(false);
    }
  };

  const inputC = "mt-1 block h-10 w-full rounded border bg-background px-2";

  return (
    <section className="space-y-4 rounded-xl border p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-semibold">Histórico de vigências</h3>
          <p className="text-xs text-muted-foreground">
            Cada alteração de salário/custos vale de uma data A até B. O fim da vigência atual pode ficar em aberto.
          </p>
        </div>
        <Button
          size="sm"
          className="gap-1.5"
          onClick={() => { setErroForm(null); setForm(newForm(hours, current)); }}
          disabled={saving || loading}
        >
          <Plus className="h-3.5 w-3.5" />Nova vigência
        </Button>
      </div>

      {loading && !periods ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando histórico…</p>
      ) : (
        <div className="overflow-auto rounded-lg border">
          <table className="w-full min-w-[840px] text-sm">
            <caption className="sr-only">Vigências salariais do profissional</caption>
            <thead>
              <tr className="border-b">
                <th scope="col" className="p-2 text-left">Vigência</th>
                <th scope="col" className="p-2 text-right">Remuneração</th>
                <th scope="col" className="p-2 text-right">Total mensal</th>
                <th scope="col" className="p-2 text-right">HH base</th>
                <th scope="col" className="p-2 text-right">Custo/hora</th>
                <th scope="col" className="p-2 text-left">Situação</th>
                <th scope="col" className="p-2 text-right">Ações</th>
              </tr>
            </thead>
            <tbody>
              {(periods ?? []).length === 0 && (
                <tr><td colSpan={7} className="p-6 text-center text-muted-foreground">Nenhuma vigência cadastrada.</td></tr>
              )}
              {(periods ?? []).map((p) => (
                <tr key={p.id} className={cn("border-b", !p.active && "opacity-50", form?.periodId === p.id && "bg-primary/5")}>
                  <td className="p-2">{rotuloPeriodo(p)}</td>
                  <td className="p-2 text-right tabular-nums">{dinheiro(p.remuneration)}</td>
                  <td className="p-2 text-right font-semibold tabular-nums">{dinheiro(p.monthlyCost)}</td>
                  <td className="p-2 text-right tabular-nums">{somenteValorHora(p) ? "—" : horasFmt(p.monthlyHours)}</td>
                  <td className="p-2 text-right font-semibold tabular-nums">{dinheiro(p.hourlyRate)}</td>
                  <td className="p-2">{p.active ? (somenteValorHora(p) ? "Ativa · somente valor/hora" : p.manualRateUsed ? "Ativa · custo/h manual" : "Ativa · calculado") : "Excluída (preservada)"}</td>
                  <td className="p-2">
                    <div className="flex justify-end gap-1">
                      {p.active && (
                        <>
                          <Button size="sm" variant="ghost" className="h-8 gap-1" disabled={saving}
                            onClick={() => { setErroForm(null); setForm(periodToForm(p)); }}
                            aria-label={`Editar vigência ${rotuloPeriodo(p)}`}>
                            <Pencil className="h-3.5 w-3.5" />Editar
                          </Button>
                          <Button size="sm" variant="ghost" className="h-8 gap-1 text-rose-500" disabled={saving}
                            onClick={() => setExcluir(p)}
                            aria-label={`Excluir vigência ${rotuloPeriodo(p)}`}>
                            <Trash2 className="h-3.5 w-3.5" />Excluir
                          </Button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {form && (
        <form className="space-y-3 rounded-xl border bg-background/40 p-4" onSubmit={salvar} noValidate>
          <div className="flex items-center justify-between gap-2">
            <div>
              <h4 className="font-semibold">{form.mode === "edit" ? "Editar vigência" : "Nova vigência"}</h4>
              <p className="text-xs text-muted-foreground">
                {form.mode === "edit"
                  ? "Corrige apenas este período. Para registrar um aumento a partir de uma data, use “Nova vigência”."
                  : "Uma data inicial posterior encerra a vigência aberta no dia anterior — você confirma antes de gravar."}
              </p>
            </div>
            <Button type="button" size="icon" variant="ghost" onClick={() => { setForm(null); setErroForm(null); }} disabled={saving} aria-label="Fechar formulário de vigência">
              <X className="h-4 w-4" />
            </Button>
          </div>

          <label className="block text-xs">Tipo de custo
            <select className={inputC} value={form.costMode} onChange={(e) => patchForm({ costMode: e.target.value as FormState["costMode"] })}>
              <option value="COMPOSICAO">Composição mensal</option>
              <option value="HORA">Somente valor/hora</option>
            </select>
          </label>
          {form.costMode === "HORA" && <p className="text-xs text-muted-foreground">Informe o valor/hora e a vigência. O custo será aplicado às horas trabalhadas no Timesheet, sem remuneração ou composição mensal.</p>}

          <div className={cn("grid gap-2 sm:grid-cols-2", form.costMode === "HORA" ? "lg:grid-cols-3" : "lg:grid-cols-5")}>
            <label className="text-xs">Início
              <input required className={inputC} type="date" value={form.start} onChange={(e) => patchForm({ start: e.target.value })} />
            </label>
            <label className="text-xs">Fim (opcional — vazio = em aberto)
              <input className={inputC} type="date" value={form.end} min={form.start || undefined} onChange={(e) => patchForm({ end: e.target.value })} />
            </label>
            {form.costMode === "COMPOSICAO" && <>
            <label className="text-xs">HH base mensal
              <input className={inputC} inputMode="decimal" value={form.baseHours} onChange={(e) => patchForm({ baseHours: e.target.value })} />
            </label>
            <label className="text-xs">Remuneração (R$)
              <input className={inputC} inputMode="decimal" placeholder="Ex.: 8.500,00" value={form.remuneration} onChange={(e) => patchForm({ remuneration: e.target.value })} />
            </label>
            </>}
            <label className="text-xs">{form.costMode === "HORA" ? "Valor/hora (R$)" : "Custo/hora manual (opcional)"}
              <input className={inputC} inputMode="decimal" value={form.manualRate} onChange={(e) => patchForm({ manualRate: e.target.value })} />
            </label>
          </div>

          {form.costMode === "COMPOSICAO" && form.items.map((item, index) => {
            const n = index + 1;
            const categorias = CATEGORIAS.some((c) => c.value === item.category)
              ? CATEGORIAS
              : [...CATEGORIAS, { value: item.category, label: item.category }];
            return (
              <div key={index} className="grid gap-2 rounded border p-2 sm:grid-cols-2 lg:grid-cols-5">
                <select aria-label={`Categoria do item ${n}`} className="h-10 rounded border bg-background px-2" value={item.category}
                  onChange={(e) => setItem(index, { category: e.target.value })}>
                  {categorias.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
                </select>
                <input aria-label={`Descrição do item ${n}`} className="h-10 rounded border bg-background px-2" placeholder="Descrição" value={item.description}
                  onChange={(e) => setItem(index, { description: e.target.value })} />
                <select aria-label={`Tipo de cálculo do item ${n}`} className="h-10 rounded border bg-background px-2" value={item.calculationType}
                  onChange={(e) => setItem(index, { calculationType: e.target.value as TipoCalculo })}>
                  <option value="FIXO">Valor fixo mensal</option>
                  <option value="PERCENTUAL">% da remuneração</option>
                </select>
                {item.calculationType === "FIXO" ? (
                  <input aria-label={`Valor mensal do item ${n}`} className="h-10 rounded border bg-background px-2" inputMode="decimal" placeholder="Valor mensal" value={item.amount}
                    onChange={(e) => setItem(index, { amount: e.target.value })} />
                ) : (
                  <input aria-label={`Percentual do item ${n}`} className="h-10 rounded border bg-background px-2" inputMode="decimal" placeholder="Percentual" value={item.percentage}
                    onChange={(e) => setItem(index, { percentage: e.target.value })} />
                )}
                <Button type="button" variant="outline" onClick={() => patchForm({ items: form.items.filter((_, i) => i !== index) })}>Remover item</Button>
              </div>
            );
          })}

          {form.costMode === "COMPOSICAO" && form.mode === "new" && current && (
            <fieldset className="space-y-1">
              <label className="flex items-center gap-2 text-xs">
                <input type="checkbox" checked={form.copyPrevious} onChange={(e) => patchForm({ copyPrevious: e.target.checked })} />
                Copiar composição ativa da vigência atual ({rotuloPeriodo(current)})
              </label>
              {form.copyPrevious && current.items.filter((i) => i.active).map((item) => (
                <label key={item.id} className="ml-5 flex items-center gap-2 text-xs">
                  <input
                    type="checkbox"
                    checked={form.copiedIds.includes(item.id)}
                    onChange={(e) => patchForm({
                      copiedIds: e.target.checked ? [...form.copiedIds, item.id] : form.copiedIds.filter((id) => id !== item.id),
                    })}
                  />
                  {item.category} · {item.description} · {item.calculationType === "PERCENTUAL" ? `${horasFmt(item.percentage)}%` : dinheiro(item.amount)}
                </label>
              ))}
            </fieldset>
          )}

          {erroForm && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erroForm}</p>}

          <div className="flex flex-wrap gap-2">
            {form.costMode === "COMPOSICAO" && <Button type="button" variant="outline" onClick={() => patchForm({ items: [...form.items, NOVO_ITEM()] })}>
              Adicionar encargo, software ou equipamento
            </Button>}
            <Button type="submit" disabled={saving} className="gap-1.5">
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              {form.mode === "edit" ? "Salvar alterações" : "Pré-visualizar e salvar vigência"}
            </Button>
          </div>
        </form>
      )}

      <ConfirmarDesativacao
        aberto={excluir !== null}
        ocupado={saving}
        onCancelar={() => setExcluir(null)}
        onConfirmar={() => void confirmarExclusao()}
        titulo={excluir ? `Excluir a vigência ${rotuloPeriodo(excluir)}?` : "Excluir vigência?"}
        descricao="A vigência deixa de entrar no cálculo, mas o registro é apenas desativado e continua preservado no histórico do banco."
      />

      <AlertDialog open={encerramento !== null} onOpenChange={(v) => { if (!v && !saving) setEncerramento(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Encerrar a vigência atual?</AlertDialogTitle>
            <AlertDialogDescription>
              {encerramento && (
                <>A vigência aberta será encerrada em <b>{dataFmt(encerramento.preview.closeOpenPeriodOn)}</b> e a nova começa em{" "}
                  <b>{dataFmt(encerramento.payload.start)}</b> {encerramento.payload.manualRate !== null && encerramento.payload.remuneration === 0 && encerramento.payload.items.length === 0
                    ? <>com valor/hora de <b>{dinheiro(encerramento.payload.manualRate)}</b>.</>
                    : <>com remuneração de <b>{dinheiro(encerramento.payload.remuneration)}</b>.</>}
                  {" "}Nenhum período anterior é apagado.</>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Voltar</AlertDialogCancel>
            <AlertDialogAction disabled={saving} onClick={(e) => { e.preventDefault(); if (encerramento) void criar(encerramento.payload, encerramento.preview); }}>
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : "Confirmar nova vigência"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
