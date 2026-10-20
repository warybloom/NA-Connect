// Ficheiro: ClientApp/src/features/auth/AuthTransition.tsx
// Transição em pixels entre a tela cinematográfica e o Auth Shell. CSS puro, sem biblioteca.
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from "react";

/** Cobrir e revelar: ~380 ms cada, total ~800 ms (dentro de 600–1000 ms). */
export const PIXEL_COVER_MS = 380;
export const PIXEL_REVEAL_MS = 420;
/** Com prefers-reduced-motion: troca simples com fade curto. */
export const FADE_MS = 160;

const COLS = 16;
const ROWS = 10;
const REDUCED_MOTION = "(prefers-reduced-motion: reduce)";

export type TransitionPhase = "idle" | "covering" | "revealing";

export function prefersReducedMotion() {
  return typeof window !== "undefined" && (window.matchMedia?.(REDUCED_MOTION).matches ?? false);
}

/**
 * Executa `swap` no instante em que a tela está coberta. Retorna a fase atual para o overlay.
 * Chamadas durante uma transição em andamento são ignoradas (sem duplo clique).
 */
export function usePixelTransition() {
  const [phase, setPhase] = useState<TransitionPhase>("idle");
  const [reduced, setReduced] = useState(prefersReducedMotion);
  const timers = useRef<number[]>([]);

  useEffect(() => {
    const mq = window.matchMedia?.(REDUCED_MOTION);
    if (!mq) return;
    const onChange = () => setReduced(mq.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);

  useEffect(() => () => timers.current.forEach(window.clearTimeout), []);

  const run = useCallback(
    (swap: () => void) => {
      if (phase !== "idle") return;
      const cover = reduced ? FADE_MS : PIXEL_COVER_MS;
      const reveal = reduced ? FADE_MS : PIXEL_REVEAL_MS;
      setPhase("covering");
      timers.current.push(
        window.setTimeout(() => {
          swap();
          setPhase("revealing");
          timers.current.push(window.setTimeout(() => setPhase("idle"), reveal));
        }, cover),
      );
    },
    [phase, reduced],
  );

  return { phase, reduced, run };
}

/** Atraso determinístico por célula: varredura diagonal com ruído, para não parecer uma grade mecânica. */
function cellDelay(index: number) {
  const col = index % COLS;
  const row = Math.floor(index / COLS);
  const diagonal = (col + row) / (COLS + ROWS - 2);
  const noise = ((index * 7919) % 97) / 97;
  return Math.round(diagonal * 150 + noise * 110);
}

export function PixelTransitionOverlay({ phase, reduced }: { phase: TransitionPhase; reduced: boolean }) {
  const cells = useMemo(() => Array.from({ length: COLS * ROWS }, (_, i) => cellDelay(i)), []);
  if (phase === "idle") return null;

  if (reduced) return <div className="na-pixels na-pixels--fade" data-phase={phase} aria-hidden="true" />;

  return (
    <div
      className="na-pixels"
      data-phase={phase}
      aria-hidden="true"
      style={{ "--na-pixel-cols": COLS, "--na-pixel-rows": ROWS } as CSSProperties}
    >
      {cells.map((delay, i) => (
        <span key={i} className="na-pixels__cell" style={{ "--na-pixel-delay": `${delay}ms` } as CSSProperties} />
      ))}
    </div>
  );
}
