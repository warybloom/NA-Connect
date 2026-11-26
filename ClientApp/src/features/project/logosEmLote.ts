// Identidade Visual — UPLOAD EM LOTE (lógica pura, testada em __tests__).
// UMA logo → TODOS os projetos selecionados no topo. Não há associação individual: o multiselect é a fonte dos destinos.
// Persistência é a mesma da logo individual (enviarLogoProjeto, uma logo ativa por projeto).

export const TIPOS_LOGO = ["image/png", "image/jpeg", "image/webp"] as const;
export const TAMANHO_MAXIMO_LOGO = 1_048_576; // mesmo limite do backend (ProjectIdentityStore)

/** Arquivo aceito como logo (mesmas regras do backend: PNG/JPG/WebP até 1 MB). Null = ok. */
export function problemaArquivo(f: { name: string; type: string; size: number }): string | null {
  if (!(TIPOS_LOGO as readonly string[]).includes(f.type)) return "Formato não suportado (use PNG, JPG ou WebP).";
  if (f.size <= 0 || f.size > TAMANHO_MAXIMO_LOGO) return "Arquivo vazio ou maior que 1 MB.";
  return null;
}

/** Quantos dos projetos selecionados recebem a logo pela primeira vez e quantos terão a logo atual substituída. */
export function resumoAplicacao(selecionados: readonly string[], temLogo: (id: string) => boolean) {
  const substituir = selecionados.filter(temLogo).length;
  return { total: selecionados.length, semLogo: selecionados.length - substituir, substituir };
}

/** Rótulo do botão: "Aplicar logo ao projeto" / "Aplicar logo aos N projetos". */
export const rotuloAplicar = (n: number) => (n === 1 ? "Aplicar logo ao projeto" : `Aplicar logo aos ${n} projetos`);
