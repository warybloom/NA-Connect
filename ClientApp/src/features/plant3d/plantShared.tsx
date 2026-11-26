// Peças compartilhadas das telas Plant 3D (CONTRATO_03): cabeçalho com projeto, estados honestos, gráficos SVG/CSS sem biblioteca.
import { ReactNode, useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { motion } from "framer-motion";
import { Loader2, LayoutList, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { PlantResults, PlantStatus, descreverErro, isAbort, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { PlantPhase, getPhasesComCarimbo, putPhases } from "@/components/plant3d/plantRulesApi";
import { getLineWeights, getStandardsQuality } from "@/components/plant3d/plantStandardsApi";
import type { LineWeights, StandardsQuality } from "@/components/plant3d/plantStandardsLogic";
import { publicarFasesNoSidebar } from "./usePlantSidebarPhases";

export type PlantProjectCtx = ReturnType<typeof usePlantProjects>;

// ───────── estados ─────────
const ESTADO_TOM = {
  amber: "border-amber-500/30 bg-amber-500/5 text-amber-700 dark:text-amber-400",
  red: "border-red-500/30 bg-red-500/5 text-red-700 dark:text-red-400",
  blue: "border-blue-500/30 bg-blue-500/5 text-blue-700 dark:text-blue-400",
  neutral: "border-border/50 bg-card/30 text-muted-foreground",
} as const;

export function Estado({ tom, icon, titulo, children }: { tom: keyof typeof ESTADO_TOM; icon?: ReactNode; titulo: string; children?: ReactNode }) {
  return (
    <div className={`glass-card flex items-start gap-4 rounded-xl border p-5 ${ESTADO_TOM[tom]}`}>
      {icon && <div className="mt-0.5 shrink-0">{icon}</div>}
      <div className="min-w-0 space-y-1">
        <h3 className="font-bold text-foreground">{titulo}</h3>
        <div className="text-sm text-muted-foreground">{children}</div>
      </div>
    </div>
  );
}

// ───────── cabeçalho / projeto ─────────
export function PlantHeader(props: {
  titulo: string; subtitulo?: string; pj: PlantProjectCtx; onRefresh?: () => void; loading?: boolean; extra?: ReactNode;
}) {
  const { pj } = props;
  return (
    <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
      className="glass-card flex flex-col justify-between gap-4 rounded-xl border border-border/50 p-5 md:flex-row md:items-center">
      <div className="min-w-0 flex-1 space-y-2">
        <h2 className="text-2xl font-bold text-primary">{props.titulo}</h2>
        {props.subtitulo && <p className="text-sm text-muted-foreground">{props.subtitulo}</p>}
        {!pj.temProjetoAtual ? (
          <div className="flex flex-wrap items-center gap-3">
            <label htmlFor="plantProjectPick" className="flex items-center gap-2 text-sm font-medium text-muted-foreground"><LayoutList className="h-4 w-4" /> Projeto:</label>
            <select id="plantProjectPick" value={pj.escolhido} onChange={e => pj.setEscolhido(e.target.value)}
              className="min-w-[240px] rounded-lg border border-border bg-background px-3 py-2 text-sm font-bold text-foreground outline-none focus:ring-2 focus:ring-primary">
              <option value="">-- Escolha um Projeto --</option>
              {pj.projects.map(p => <option key={p.projectIdAcc} value={p.projectIdAcc}>{p.name || p.projectIdAcc}{p.dashboardProjectId ? "" : " — não vinculado"}</option>)}
            </select>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Projeto atual: <b className="text-foreground">{pj.nome || pj.projectIdAcc}</b></p>
        )}
        {props.extra}
      </div>
      {props.onRefresh && (
        <Button onClick={props.onRefresh} disabled={props.loading || !pj.projectIdAcc} className="gap-2 font-bold">
          {props.loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />} ATUALIZAR
        </Button>
      )}
    </motion.div>
  );
}

/** Avisos comuns de projeto ausente / lista de projetos com erro. */
export function SemProjeto({ pj }: { pj: PlantProjectCtx }) {
  return (
    <>
      {pj.error && <Estado tom="red" titulo="Projetos não carregados">{pj.error}</Estado>}
      {!pj.projectIdAcc && !pj.error && (
        <Estado tom="neutral" titulo="Nenhum projeto selecionado">
          {pj.loaded && pj.projects.length === 0 ? "Seu usuário não tem projetos habilitados para o Plant 3D." : "Escolha um projeto acima (ou defina o projeto atual na Home)."}
        </Estado>
      )}
    </>
  );
}

/** Mensagens de status do /results compartilhadas por todas as telas. */
export function AvisoStatus({ status }: { status?: PlantResults["status"] | null }) {
  if (!status || status === "ready") return null;
  const t = {
    unlinked: ["Projeto sem vínculo com o Dashboard BIM", "Nenhum dado é exibido enquanto o projeto N&A não estiver associado a um projeto do Dashboard BIM (Configuração › DE/PARA e TAGs)."],
    "no-baseline": ["Aguardando versão-base", "O Dashboard BIM ainda não tem versão completa de P&ID e/ou 3D para este projeto."],
    "no-mapping": ["Sem mapeamento DE/PARA salvo", "Configure e salve o DE/PARA e TAGs para que contagens e comparações existam."],
  }[status];
  return <Estado tom="amber" titulo={t[0]}>{t[1]}</Estado>;
}

// ───────── gráficos ─────────
export type Parte = { valor: number; cor: string; rotulo: string };

export function BarraEmpilhada({ partes, altura = "h-2.5" }: { partes: Parte[]; altura?: string }) {
  const total = partes.reduce((n, p) => n + p.valor, 0);
  if (total === 0) return <div className={`${altura} rounded-full bg-secondary`} />;
  return (
    <div className={`flex ${altura} overflow-hidden rounded-full bg-secondary`} role="img" aria-label={partes.map(p => `${p.rotulo}: ${p.valor}`).join(", ")}>
      {partes.filter(p => p.valor > 0).map(p => <div key={p.rotulo} className={p.cor} style={{ width: `${(p.valor / total) * 100}%` }} title={`${p.rotulo}: ${p.valor}`} />)}
    </div>
  );
}

export function Progresso({ pct, cor = "bg-primary", rotulo }: { pct: number | null; cor?: string; rotulo: string }) {
  const v = pct === null ? 0 : Math.max(0, Math.min(100, pct));
  return (
    <div className="h-2.5 overflow-hidden rounded-full bg-secondary" role="progressbar" aria-label={rotulo} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct === null ? undefined : Math.round(v)}>
      <div className={`h-full rounded-full ${cor}`} style={{ width: `${v}%` }} />
    </div>
  );
}

const COR_HEX: Record<string, string> = {
  "bg-emerald-500": "#10b981", "bg-amber-500": "#f59e0b", "bg-red-500": "#ef4444", "bg-orange-500": "#f97316",
  "bg-blue-500": "#3b82f6", "bg-slate-500": "#64748b", "bg-primary": "#6366f1",
};

export function Rosca({ partes, centro, sub }: { partes: Parte[]; centro: string; sub: string }) {
  const total = partes.reduce((n, p) => n + p.valor, 0);
  const R = 42, C = 2 * Math.PI * R;
  let acumulado = 0;
  return (
    <svg viewBox="0 0 120 120" className="h-36 w-36 shrink-0" role="img" aria-label={`${sub}: ${centro}`}>
      <circle cx="60" cy="60" r={R} fill="none" stroke="currentColor" strokeOpacity="0.12" strokeWidth="14" />
      {total > 0 && partes.filter(p => p.valor > 0).map(p => {
        const len = (p.valor / total) * C;
        const el = (
          <circle key={p.rotulo} cx="60" cy="60" r={R} fill="none" stroke={COR_HEX[p.cor] ?? "#6366f1"} strokeWidth="14"
            strokeDasharray={`${len} ${C - len}`} strokeDashoffset={-acumulado} transform="rotate(-90 60 60)"><title>{`${p.rotulo}: ${p.valor}`}</title></circle>
        );
        acumulado += len;
        return el;
      })}
      <text x="60" y="58" textAnchor="middle" className="fill-foreground text-[17px] font-black">{centro}</text>
      <text x="60" y="73" textAnchor="middle" className="fill-muted-foreground text-[8px]">{sub}</text>
    </svg>
  );
}

export function Kpi({ titulo, valor, nota, cor = "border-l-primary" }: { titulo: string; valor: string; nota?: string; cor?: string }) {
  return (
    <div className={`glass-card rounded-xl border-l-4 p-4 ${cor}`}>
      <p className="text-[11px] font-bold uppercase tracking-wider text-muted-foreground">{titulo}</p>
      <p className="mt-1 text-3xl font-black tabular-nums text-foreground">{valor}</p>
      {nota && <p className="mt-1 text-xs text-muted-foreground">{nota}</p>}
    </div>
  );
}

export function Legenda({ rotulo, valor, cor }: { rotulo: string; valor: string; cor: string }) {
  return (
    <div className="flex items-center justify-between gap-3 text-sm">
      <span className="flex items-center gap-2 text-muted-foreground"><span className={`h-2.5 w-2.5 rounded-full ${cor}`} />{rotulo}</span>
      <b className="tabular-nums text-foreground">{valor}</b>
    </div>
  );
}

export const Cartao = ({ titulo, children, className = "" }: { titulo: string; children: ReactNode; className?: string }) => (
  <section className={`glass-card space-y-3 rounded-xl border border-border/50 p-5 ${className}`}>
    <h3 className="text-sm font-bold">{titulo}</h3>
    {children}
  </section>
);

// ───────── dados: resultados ─────────
/** Carrega /results (limit pequeno: aqui só interessam totais e categorias). */
export function usePlantResults(projectIdAcc: string) {
  const [results, setResults] = useState<PlantResults | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  useEffect(() => {
    setResults(null); setError("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const r = await plantRequest<PlantResults>(`${projetoPath(projectIdAcc)}/results?limit=1&offset=0`, { signal: abort.signal });
        if (!r || typeof r !== "object" || !r.status) throw new Error("Resposta inesperada de /results (campo status ausente).");
        if (r.status === "ready" && !r.totals) throw new Error("Resposta inesperada de /results (totals ausente).");
        setResults({ ...r, items: r.items ?? [], categories: r.categories ?? [], totalItems: r.totalItems ?? 0 });
      } catch (e) {
        if (isAbort(e)) return;
        setError(descreverErro(e, "Resultados"));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);
  // Dados em tempo real: /status é leve e incorpora a data das regras salvas.
  // /results (pesado) só é recarregado quando a versão ou configuração muda.
  useEffect(() => {
    if (!projectIdAcc) return;
    let alive = true;
    let previous: string | null = null;
    const check = async () => {
      if (document.hidden) return;
      try {
        const status = await plantRequest<PlantStatus>(`${projetoPath(projectIdAcc)}/status`);
        if (!alive) return;
        const signature = JSON.stringify([
          status.pnId?.completedVersions, status.pnId?.latestCompletedAtUtc,
          status.piping?.completedVersions, status.piping?.latestCompletedAtUtc,
          status.pnIdDelta?.completedVersions, status.pnIdDelta?.latestCompletedAtUtc,
          status.pipingDelta?.completedVersions, status.pipingDelta?.latestCompletedAtUtc,
          status.filtersUpdatedAtUtc ?? null,
        ]);
        if (previous !== null && previous !== signature) setTick(t => t + 1);
        previous = signature;
      } catch { /* status indisponível: a tela conserva resultados e botão Atualizar */ }
    };
    void check();
    const id = window.setInterval(check, 15000);
    return () => { alive = false; window.clearInterval(id); };
  }, [projectIdAcc]);
  const reload = useCallback(() => setTick(t => t + 1), []);
  return { results, loading, error, reload };
}

// ───────── dados: fases ─────────
export function usePlantPhases(projectIdAcc: string) {
  const [phases, setPhases] = useState<PlantPhase[]>([]);
  const carimbo = useRef<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  useEffect(() => {
    setPhases([]); setLoaded(false); setError("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const r = await getPhasesComCarimbo(projectIdAcc, abort.signal);
        if (abort.signal.aborted) return;
        carimbo.current = r.updatedAtUtc; setPhases(r.phases); setLoaded(true);
      } catch (e) {
        if (isAbort(e)) return;
        setError(descreverErro(e, "Fases de medição"));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);
  const reload = useCallback(() => setTick(t => t + 1), []);
  const salvar = useCallback(async (lista: PlantPhase[]) => {
    // Anti-substituição silenciosa: o PUT troca a lista inteira; se o servidor mudou desde a leitura, recusa e pede atualização.
    const atual = await getPhasesComCarimbo(projectIdAcc);
    if (atual.updatedAtUtc !== carimbo.current) {
      throw new Error("As fases foram alteradas por outra sessão desde que esta tela foi aberta. Clique em ATUALIZAR e refaça a edição para não substituir o trabalho existente.");
    }
    const salvo = await putPhases(projectIdAcc, lista);
    try { carimbo.current = (await getPhasesComCarimbo(projectIdAcc)).updatedAtUtc; } catch { carimbo.current = null; /* próximo salvar exigirá ATUALIZAR */ }
    setPhases(salvo); setLoaded(true); setError("");
    publicarFasesNoSidebar(projectIdAcc, salvo); // sidebar dinâmico atualiza na hora, sem duplicar itens
    return salvo;
  }, [projectIdAcc]);
  return { phases, loaded, loading, error, reload, salvar };
}

// ───────── dados: qualidade por padrões (GET /standards/quality) e pesos de linha (GET /line-weights) ─────────
/** Falha NÃO vira "0%": `error` preenchido = indicador indisponível (ex.: backend ainda sem o endpoint). */
export function usePlantQuality(projectIdAcc: string) {
  const [quality, setQuality] = useState<StandardsQuality | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  useEffect(() => {
    setQuality(null); setError("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const q = await getStandardsQuality(projectIdAcc, abort.signal);
        if (!abort.signal.aborted) setQuality(q);
      } catch (e) {
        if (!isAbort(e)) setError(descreverErro(e, "Qualidade por padrões"));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);
  const reload = useCallback(() => setTick(t => t + 1), []);
  return { quality, loading, error, reload };
}

export function useLineWeights(projectIdAcc: string) {
  const [data, setData] = useState<LineWeights | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  useEffect(() => {
    setData(null); setError("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      try {
        const w = await getLineWeights(projectIdAcc, abort.signal);
        if (!abort.signal.aborted) setData(w);
      } catch (e) {
        if (!isAbort(e)) setError(descreverErro(e, "Pesos de linha"));
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);
  const reload = useCallback(() => setTick(t => t + 1), []);
  return { data, setData, loading, error, reload };
}

// ───────── fase selecionada: uma só para o sistema todo (persistida por projeto) ─────────
const FASE_EVENTO = "plant3d:phase-changed";
const faseKey = (p: string) => "plant3d.phase." + p;
const lerFase = (p: string) => { try { return p ? localStorage.getItem(faseKey(p)) ?? "" : ""; } catch { return ""; } };

export function setFaseSelecionada(projectIdAcc: string, phaseId: string) {
  try { if (phaseId) localStorage.setItem(faseKey(projectIdAcc), phaseId); else localStorage.removeItem(faseKey(projectIdAcc)); } catch { /* storage indisponível */ }
  window.dispatchEvent(new Event(FASE_EVENTO));
}

export function useFaseSelecionada(projectIdAcc: string): [string, (id: string) => void] {
  const subscribe = useCallback((cb: () => void) => {
    window.addEventListener(FASE_EVENTO, cb); window.addEventListener("storage", cb);
    return () => { window.removeEventListener(FASE_EVENTO, cb); window.removeEventListener("storage", cb); };
  }, []);
  const id = useSyncExternalStore(subscribe, () => lerFase(projectIdAcc), () => "");
  const set = useCallback((novo: string) => setFaseSelecionada(projectIdAcc, novo), [projectIdAcc]);
  return [id, set];
}

/** Fase efetiva: a preferida (rota/seleção) se existir; senão a primeira; vazio se não há fases. */
export function resolverFase(phases: PlantPhase[], preferida: string): PlantPhase | null {
  return phases.find(f => f.id === preferida) ?? phases[0] ?? null;
}

export function SeletorFase({ phases, valor, onChange }: { phases: PlantPhase[]; valor: string; onChange: (id: string) => void }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5" role="tablist" aria-label="Fase de medição">
      {phases.map(f => (
        <button key={f.id} type="button" role="tab" aria-selected={f.id === valor} onClick={() => onChange(f.id)}
          className={`rounded-full border px-3 py-1 text-xs font-bold transition-colors ${f.id === valor ? "border-primary bg-primary text-primary-foreground" : "border-border text-muted-foreground hover:text-foreground"}`}>
          {f.name || "(sem nome)"}
        </button>
      ))}
    </div>
  );
}

/** Mantém callbacks estáveis entre renders. */
export function useLatest<T>(v: T) { const r = useRef(v); r.current = v; return r; }

export const CAT_FIXAS = [
  { id: "line", label: "Linhas" }, { id: "equip", label: "Equipamentos" }, { id: "inst", label: "Instrumentos" }, { id: "valve", label: "Válvulas" },
];
