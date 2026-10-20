// Regras PURAS de layout da Home (testáveis): colunas responsivas e itens por página.
// Cards: até 6 por fileira em telas largas × 5 fileiras = até 30 projetos antes de paginar; telas menores reduzem as
// colunas (as mesmas faixas das classes Tailwind usadas na grade). Lista: 50 linhas compactas por página.
export const MODOS_HOME = ["cards", "lista"] as const;
export type ModoHome = (typeof MODOS_HOME)[number];

export const FILEIRAS_HOME = 5;
export const LINHAS_LISTA_HOME = 50;

/** Mesmas faixas de GRADE_HOME: 2 (<768) · 3 (md) · 4 (lg) · 5 (xl) · 6 (2xl ≥1536). */
export function colunasHome(larguraPx: number): number {
  if (larguraPx >= 1536) return 6;
  if (larguraPx >= 1280) return 5;
  if (larguraPx >= 1024) return 4;
  if (larguraPx >= 768) return 3;
  return 2;
}

export const GRADE_HOME = "grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6";

export const itensPorPaginaHome = (modo: ModoHome, colunas: number) =>
  modo === "lista" ? LINHAS_LISTA_HOME : colunas * FILEIRAS_HOME;
