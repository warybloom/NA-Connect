// Lógica pura do painel executivo por fase (F5). Sem imports: testável em node.
// REGRA DE OURO: fases são gates CUMULATIVOS (30/60/90…). Metas de fases NUNCA são somadas; cada fase compara a sua meta com o
// MESMO realizado global do projeto. Dado ausente (meta, contagem) → null, nunca 0 inventado.

export type FaseMeta = { id: string; name: string; sortOrder: number; expected: Record<string, number> };
export type Realizado = { pid: number | null; piping: number | null };
export type AtingimentoFase = {
  phaseId: string; name: string; meta: number | null; pidPct: number | null; pipingPct: number | null;
  pidFeito: number | null; pipingFeito: number | null;
};

const pct = (real: number | null, meta: number | null): number | null =>
  real === null || meta === null || !(meta > 0) ? null : (real / meta) * 100;

/** Uma linha por fase, na ordem do cadastro. Percentual pode passar de 100 (realizado acima do gate) — o gráfico limita só a barra. */
export function atingimentoPorFase(fases: ReadonlyArray<FaseMeta>, categoria: string, realizado: Realizado | null | undefined): AtingimentoFase[] {
  const ordenadas = [...fases].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, "pt-BR"));
  return ordenadas.map(f => {
    const meta = Object.prototype.hasOwnProperty.call(f.expected, categoria) && Number.isFinite(f.expected[categoria]) ? f.expected[categoria] : null;
    return {
      phaseId: f.id, name: f.name || "(sem nome)", meta,
      pidPct: pct(realizado?.pid ?? null, meta), pipingPct: pct(realizado?.piping ?? null, meta),
      pidFeito: realizado?.pid ?? null, pipingFeito: realizado?.piping ?? null,
    };
  });
}

/** Largura da barra (0–100), limitada; null → sem barra. */
export const larguraBarra = (p: number | null): number => (p === null || !Number.isFinite(p) ? 0 : Math.max(0, Math.min(100, p)));

/** A fase "atual" do painel: a primeira cujo gate ainda não foi atingido em 3D; se todas atingidas, a última. Sem dados → null. */
export function faseEmAndamento(linhas: ReadonlyArray<AtingimentoFase>): string | null {
  const comDado = linhas.filter(l => l.pipingPct !== null);
  if (comDado.length === 0) return null;
  return (comDado.find(l => (l.pipingPct ?? 0) < 100) ?? comDado[comDado.length - 1]).phaseId;
}
