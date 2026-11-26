// Tema visual do N&A Connect (14/10/2026): CLARO, ESCURO ou AUTOMÁTICO (segue o sistema operacional).
// A preferência é guardada no localStorage do navegador (chave abaixo) pelo next-themes; o script inline de
// ClientApp/index.html lê a MESMA chave antes da primeira pintura para não piscar o tema errado.

export const THEME_STORAGE_KEY = "na-connect-theme";

export type PreferenciaTema = "light" | "dark" | "system";
export type TemaResolvido = "light" | "dark";

/** Padrão para quem nunca escolheu: o tema escuro histórico do N&A Connect. */
export const PREFERENCIA_PADRAO: PreferenciaTema = "dark";

export const OPCOES_TEMA: ReadonlyArray<{ valor: PreferenciaTema; rotulo: string; descricao: string }> = [
  { valor: "light", rotulo: "Claro", descricao: "Interface clara" },
  { valor: "dark", rotulo: "Escuro", descricao: "Interface escura" },
  { valor: "system", rotulo: "Automático", descricao: "Acompanha o tema do sistema operacional" },
];

/** Valor salvo desconhecido/corrompido volta ao padrão. */
export function normalizarPreferencia(bruto: string | null | undefined): PreferenciaTema {
  return bruto === "light" || bruto === "dark" || bruto === "system" ? bruto : PREFERENCIA_PADRAO;
}

export function resolverTema(preferencia: PreferenciaTema, sistemaEscuro: boolean): TemaResolvido {
  if (preferencia === "system") return sistemaEscuro ? "dark" : "light";
  return preferencia;
}
