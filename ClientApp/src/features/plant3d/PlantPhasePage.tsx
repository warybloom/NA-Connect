// Resultados / Geral / Fase: metas digitadas por fase (nome livre, 30/60/90 ou outro) × realidade filtrada P&ID/3D.
// Fases nunca são inferidas de objetos/DWGs; metas são entrada do usuário. Salvar exige plant3d.database.edit.
import { useEffect, useMemo, useState } from "react";
import { useParams } from "react-router-dom";
import { toast } from "react-hot-toast";
import { AlertTriangle, ArrowDown, ArrowUp, Loader2, Lock, Plus, Save, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { descreverErro } from "@/components/plant3d/plantApi";
import { CATEGORIAS_META, PlantPhase, newId, ordenarFases } from "@/components/plant3d/plantRulesApi";
import {
  CAT_FIXAS, Estado, PlantHeader, SemProjeto, SeletorFase, resolverFase, useFaseSelecionada, usePlantPhases, usePlantResults,
} from "./plantShared";
import { MeasurementView } from "./MeasurementView";

const clone = (f: PlantPhase[]): PlantPhase[] => f.map(x => ({ ...x, expected: { ...x.expected }, weights: { ...x.weights } }));
const INPUT = "w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm text-foreground outline-none focus:border-primary disabled:opacity-60";

export default function PlantPhasePage({ phaseId, titulo = "Resultados · Fase", subtitulo = "Metas por fase (nome livre) comparadas à realidade filtrada do P&ID e do 3D." }: {
  phaseId?: string; titulo?: string; subtitulo?: string;
}) {
  const params = useParams();
  const rotaId = phaseId ?? params.id ?? "";
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const { can } = useProjectPermissions(projectIdAcc);
  const podeEditar = can("plant3d.database.edit");
  const fases = usePlantPhases(projectIdAcc);
  const { results } = usePlantResults(projectIdAcc);
  const [preferida, setPreferida] = useFaseSelecionada(projectIdAcc);

  const [draft, setDraft] = useState<PlantPhase[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [saveErro, setSaveErro] = useState("");
  const [versao, setVersao] = useState(0);

  // Rota /fase/:id define a fase ativa do sistema.
  useEffect(() => { if (rotaId && projectIdAcc && rotaId !== preferida) setPreferida(rotaId); }, [rotaId, projectIdAcc]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { setDraft(clone(fases.phases)); setSaveErro(""); }, [fases.phases]);

  const sujo = JSON.stringify(draft) !== JSON.stringify(fases.phases);
  const ativa = resolverFase(draft, preferida);
  const salva = ativa ? fases.phases.some(f => f.id === ativa.id) : false;

  const categorias = useMemo(() => {
    const base = results?.categories?.length ? results.categories.map(c => ({ id: c.id, label: c.label })) : [...CAT_FIXAS];
    const ids = new Set(base.map(c => c.id));
    // Tie-ins só têm meta digitada (o DE/PARA não tem fonte P&ID/3D para eles): sempre disponíveis para a contagem prevista.
    CATEGORIAS_META.forEach(c => { if (!ids.has(c.id)) { base.push({ id: c.id, label: c.label }); ids.add(c.id); } });
    const extras = Object.keys(ativa?.expected ?? {}).filter(k => !ids.has(k)).map(k => ({ id: k, label: k }));
    return [...base, ...extras];
  }, [results, ativa]);

  const alterar = (id: string, fn: (f: PlantPhase) => PlantPhase) => setDraft(d => d.map(f => (f.id === id ? fn(f) : f)));
  const nova = () => {
    const f: PlantPhase = { id: newId(), name: "", sortOrder: draft.reduce((m, x) => Math.max(m, x.sortOrder), -1) + 1, expected: {}, weights: {} };
    setDraft(d => [...d, f]); setPreferida(f.id);
  };
  const remover = (id: string) => {
    setDraft(d => d.filter(f => f.id !== id));
    if (ativa?.id === id) setPreferida("");
  };
  const mover = (id: string, dir: -1 | 1) => setDraft(d => {
    const ord = ordenarFases(d);
    const i = ord.findIndex(f => f.id === id), j = i + dir;
    if (i < 0 || j < 0 || j >= ord.length) return d;
    [ord[i], ord[j]] = [ord[j], ord[i]];
    return ord.map((f, k) => ({ ...f, sortOrder: k }));
  });
  const setNumero = (id: string, campo: "expected" | "weights", cat: string, texto: string) =>
    alterar(id, f => {
      const m = { ...f[campo] };
      const n = Number(texto.replace(",", "."));
      if (texto.trim() === "" || !Number.isFinite(n) || n < 0) delete m[cat]; else m[cat] = campo === "expected" ? Math.round(n) : n;
      return { ...f, [campo]: m } as PlantPhase;
    });

  const erros = useMemo(() => {
    const e: string[] = [];
    const nomes = new Map<string, number>();
    draft.forEach(f => { const k = f.name.trim().toLowerCase(); nomes.set(k, (nomes.get(k) ?? 0) + 1); });
    draft.forEach((f, i) => { if (!f.name.trim()) e.push(`Fase ${i + 1} sem nome.`); });
    nomes.forEach((n, k) => { if (k && n > 1) e.push(`Nome de fase duplicado: "${k}".`); });
    return e;
  }, [draft]);

  const salvar = async () => {
    if (!podeEditar || erros.length || salvando) return;
    setSalvando(true); setSaveErro("");
    try {
      const ord = ordenarFases(draft).map((f, i) => ({ ...f, name: f.name.trim(), sortOrder: i }));
      await fases.salvar(ord);
      setVersao(v => v + 1);
      toast.success("Fases e metas salvas.");
    } catch (e) {
      const msg = descreverErro(e, "Fases não foram salvas");
      setSaveErro(msg); toast.error(msg, { duration: 7000 });
    } finally { setSalvando(false); }
  };

  return (
    <div className="space-y-6">
      <PlantHeader titulo={titulo} subtitulo={subtitulo}
        pj={pj} loading={fases.loading} onRefresh={fases.reload}
        extra={sujo && <span className="text-xs font-semibold text-amber-700 dark:text-amber-400">Alterações não salvas</span>} />
      <SemProjeto pj={pj} />
      {fases.loading && <div className="flex justify-center p-6"><Loader2 className="h-8 w-8 animate-spin text-primary" /></div>}
      {fases.error && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Fases indisponíveis">{fases.error} Salvar fica bloqueado para não sobrescrever fases existentes.</Estado>}
      {projectIdAcc && !podeEditar && <Estado tom="blue" icon={<Lock className="h-5 w-5" />} titulo="Somente leitura">Criar/editar fases e metas exige a permissão <code>plant3d.database.edit</code>.</Estado>}

      {projectIdAcc && fases.loaded && (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <SeletorFase phases={ordenarFases(draft)} valor={ativa?.id ?? ""} onChange={setPreferida} />
            {podeEditar && <Button variant="outline" size="sm" onClick={nova} className="gap-1"><Plus className="h-4 w-4" /> Nova fase</Button>}
          </div>

          {draft.length === 0 && <Estado tom="neutral" titulo="Nenhuma fase cadastrada">{podeEditar ? "Crie uma fase (ex.: 30, 60, 90 ou qualquer nome) e digite as metas por categoria." : "Peça a um administrador do projeto para cadastrar as fases."}</Estado>}

          {ativa && (
            <>
              <h3 className="text-lg font-bold">Realidade × meta · <span className="text-primary">{ativa.name || "(sem nome)"}</span></h3>
              {salva && !sujo
                ? <MeasurementView projectIdAcc={projectIdAcc} phaseId={ativa.id} versao={versao} />
                : <Estado tom="amber" titulo="Fase ainda não salva">{salva ? "Há alterações pendentes: salve para recalcular a medição com as metas novas." : "Salve a fase para ver a medição."}</Estado>}

              <div className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
                <div className="flex flex-wrap items-end gap-3">
                  <label className="min-w-[200px] flex-1 space-y-1">
                    <span className="text-[10px] font-bold uppercase tracking-wider text-muted-foreground">Nome da fase</span>
                    <input className={INPUT} disabled={!podeEditar} value={ativa.name} placeholder="Ex.: 30 dias, Fase 2, Detalhamento…"
                      onChange={e => alterar(ativa.id, f => ({ ...f, name: e.target.value }))} />
                  </label>
                  {podeEditar && (
                    <div className="flex gap-1">
                      <Button variant="ghost" size="sm" aria-label="Mover fase para cima" onClick={() => mover(ativa.id, -1)}><ArrowUp className="h-4 w-4" /></Button>
                      <Button variant="ghost" size="sm" aria-label="Mover fase para baixo" onClick={() => mover(ativa.id, 1)}><ArrowDown className="h-4 w-4" /></Button>
                      <Button variant="ghost" size="sm" aria-label="Remover fase" className="text-destructive" onClick={() => remover(ativa.id)}><Trash2 className="h-4 w-4" /></Button>
                    </div>
                  )}
                </div>

                <div className="custom-scrollbar overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead className="text-[10px] uppercase text-muted-foreground">
                      <tr><th className="px-2 py-1 text-left font-bold">Categoria</th><th className="px-2 py-1 text-left font-bold">Meta (quantidade)</th><th className="px-2 py-1 text-left font-bold">Peso (opcional)</th></tr>
                    </thead>
                    <tbody className="divide-y divide-border/30">
                      {categorias.map(c => (
                        <tr key={c.id}>
                          <td className="px-2 py-1.5 font-semibold">{c.label}</td>
                          <td className="px-2 py-1.5"><input className={INPUT} inputMode="numeric" disabled={!podeEditar} aria-label={`Meta de ${c.label}`}
                            value={ativa.expected[c.id] ?? ""} onChange={e => setNumero(ativa.id, "expected", c.id, e.target.value)} /></td>
                          <td className="px-2 py-1.5"><input className={INPUT} inputMode="decimal" disabled={!podeEditar} aria-label={`Peso de ${c.label}`}
                            value={ativa.weights[c.id] ?? ""} onChange={e => setNumero(ativa.id, "weights", c.id, e.target.value)} /></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <p className="text-xs text-muted-foreground">Campo vazio = meta não definida (a medição mostra “—”, nunca 0%).</p>
              </div>
            </>
          )}

          {erros.length > 0 && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Corrija antes de salvar"><ul className="list-disc pl-4">{erros.map(m => <li key={m}>{m}</li>)}</ul></Estado>}
          {saveErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não salvo">{saveErro}</Estado>}
          {podeEditar && (
            <div className="flex justify-end gap-2">
              <Button variant="outline" disabled={!sujo || salvando} onClick={() => setDraft(clone(fases.phases))}>Descartar</Button>
              <Button className="gap-2 font-bold" disabled={!sujo || salvando || erros.length > 0 || !!fases.error} onClick={salvar}>
                {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} SALVAR FASES
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
