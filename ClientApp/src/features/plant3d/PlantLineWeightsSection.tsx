// Pesos de linha (GET/PUT /projects/{id}/line-weights): parcelas configuráveis por projeto (modelagem, válvulas, instrumentos…)
// com soma exatamente 100%. Os pesos podem ser guardados sem consistência comprovada; esta tela NÃO calcula nada com eles.
import { useEffect, useMemo, useState } from "react";
import { toast } from "react-hot-toast";
import { AlertTriangle, Loader2, Lock, Plus, Save, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { descreverErro } from "@/components/plant3d/plantApi";
import { putLineWeights } from "@/components/plant3d/plantStandardsApi";
import { LineWeight, PARCELAS_SUGERIDAS, chaveDoRotulo, somaPesos, validarPesos } from "@/components/plant3d/plantStandardsLogic";
import { formatarUtc } from "@/components/plant3d/plantApi";
import { Estado, useLineWeights } from "./plantShared";

const INPUT = "w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm text-foreground outline-none focus:border-primary disabled:opacity-60";

export default function PlantLineWeightsSection({ projectIdAcc }: { projectIdAcc: string }) {
  const { can } = useProjectPermissions(projectIdAcc);
  const podeEditar = can("plant3d.database.edit");
  const { data, setData, loading, error, reload } = useLineWeights(projectIdAcc);
  const [draft, setDraft] = useState<LineWeight[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [saveErro, setSaveErro] = useState("");

  useEffect(() => { setDraft(data ? data.weights.map(w => ({ ...w })) : []); setSaveErro(""); }, [data]);

  const sujo = !!data && JSON.stringify(draft) !== JSON.stringify(data.weights);
  const erros = useMemo(() => validarPesos(draft), [draft]);
  const soma = somaPesos(draft);
  const usadas = new Set(draft.map(w => w.key));
  const sugestoes = PARCELAS_SUGERIDAS.filter(p => !usadas.has(p.key));

  const alterar = (i: number, fn: (w: LineWeight) => LineWeight) => setDraft(d => d.map((w, k) => (k === i ? fn(w) : w)));
  const incluir = (rotulo: string, chave?: string) => {
    const base = chave ?? (chaveDoRotulo(rotulo) || "parcela");
    let key = base, n = 2;
    while (usadas.has(key)) key = `${base.slice(0, 36)}-${n++}`;
    setDraft(d => [...d, { key, label: rotulo, percent: 0 }]);
  };

  const salvar = async () => {
    if (!podeEditar || salvando || erros.length) return;
    setSalvando(true); setSaveErro("");
    try {
      const r = await putLineWeights(projectIdAcc, draft);
      setData(r);
      toast.success(r.weights.length ? "Pesos de linha salvos." : "Pesos de linha desativados (lista vazia).");
    } catch (e) {
      const msg = descreverErro(e, "Pesos de linha não foram salvos");
      setSaveErro(msg); toast.error(msg, { duration: 7000 });
    } finally { setSalvando(false); }
  };

  return (
    <section className="glass-card space-y-4 rounded-xl border border-border/50 p-5" data-testid="pesos-de-linha">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-lg font-bold">Pesos de linha</h3>
        {data?.updatedAtUtc && <span className="text-xs text-muted-foreground">Salvo em {formatarUtc(data.updatedAtUtc) ?? "data inválida"}</span>}
      </div>
      <p className="text-sm text-muted-foreground">
        Quanto cada parcela vale no avanço de uma linha (soma = 100%). Os pesos ficam guardados no projeto; a Consistência P&ID × 3D
        continua pendente até a relação linha → componentes ser comprovada, e nenhum cálculo usa estes pesos antes disso.
      </p>
      {loading && <div className="flex justify-center p-4"><Loader2 className="h-6 w-6 animate-spin text-primary" /></div>}
      {error && (
        <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Pesos indisponíveis">
          {error} Salvar fica bloqueado para não sobrescrever pesos existentes. <button type="button" className="underline" onClick={reload}>Tentar de novo</button>
        </Estado>
      )}
      {!podeEditar && <Estado tom="blue" icon={<Lock className="h-5 w-5" />} titulo="Somente leitura">Alterar pesos exige a permissão <code>plant3d.database.edit</code>.</Estado>}

      {data && (
        <>
          {draft.length === 0 && <p className="text-sm text-muted-foreground">Nenhum peso definido (inativo).</p>}
          <div className="space-y-2">
            {draft.map((w, i) => (
              <div key={w.key} className="grid grid-cols-[minmax(0,1fr)_7rem_auto] items-center gap-2">
                <input className={INPUT} disabled={!podeEditar} value={w.label} aria-label={`Nome da parcela ${i + 1}`} onChange={e => alterar(i, x => ({ ...x, label: e.target.value }))} />
                <div className="flex items-center gap-1">
                  <input className={INPUT} disabled={!podeEditar} inputMode="decimal" value={Number.isFinite(w.percent) ? w.percent : ""} aria-label={`Percentual de ${w.label || `parcela ${i + 1}`}`}
                    onChange={e => { const n = Number(e.target.value.replace(",", ".")); alterar(i, x => ({ ...x, percent: e.target.value.trim() === "" ? NaN : n })); }} />
                  <span className="text-sm text-muted-foreground">%</span>
                </div>
                {podeEditar ? <Button variant="ghost" size="sm" aria-label={`Remover ${w.label}`} className="text-destructive" onClick={() => setDraft(d => d.filter((_, k) => k !== i))}><Trash2 className="h-4 w-4" /></Button> : <span />}
              </div>
            ))}
          </div>

          {podeEditar && (
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="outline" size="sm" className="gap-1" onClick={() => incluir(`Parcela ${draft.length + 1}`, `parcela-${draft.length + 1}`)}><Plus className="h-4 w-4" /> Nova parcela</Button>
              {sugestoes.map(p => <Button key={p.key} variant="ghost" size="sm" onClick={() => incluir(p.label, p.key)}>+ {p.label}</Button>)}
            </div>
          )}

          {draft.length > 0 && (
            <p className={`text-sm font-semibold ${Math.abs(soma - 100) <= 0.005 ? "text-emerald-700 dark:text-emerald-400" : "text-amber-700 dark:text-amber-400"}`}>
              Soma: {soma.toLocaleString("pt-BR")}% {Math.abs(soma - 100) <= 0.005 ? "✓" : "— precisa ser 100%"}
            </p>
          )}
          {erros.length > 0 && sujo && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Corrija antes de salvar"><ul className="list-disc pl-4">{erros.map(m => <li key={m}>{m}</li>)}</ul></Estado>}
          {saveErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não salvo">{saveErro}</Estado>}
          {podeEditar && (
            <div className="flex justify-end gap-2">
              <Button variant="outline" disabled={!sujo || salvando} onClick={() => setDraft(data.weights.map(w => ({ ...w })))}>Descartar</Button>
              <Button className="gap-2 font-bold" disabled={!sujo || salvando || erros.length > 0} onClick={salvar}>
                {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} SALVAR PESOS
              </Button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
