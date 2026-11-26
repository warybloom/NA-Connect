// =============================================================================================
// PREFERÊNCIAS DO USUÁRIO — camada ÚNICA de persistência de preferências VISUAIS (30/09/2026).
// Auditoria: não existe UserPreferences/UserSettings no backend nem tabela de layout; nesta fase a persistência é o
// localStorage do navegador, SEMPRE por aqui (nenhum componente chama localStorage direto para preferência visual).
//
// Chave: na:pref:v1 / <usuário> / <escopo> / <tela> / <configuração>
//   usuário = e-mail do usuário autenticado (vindo de /api/permissions/me), minúsculo;
//   escopo  = "global" ou o Project_ID_ACC (preferência que depende do projeto, ex.: colunas da LDP daquele projeto);
//   tela    = identificador estável da tela (ex.: "home", "lista-documentos");
//   config  = o que é guardado (ex.: "viewMode", "grid").
// Ex.: na:pref:v1/ana@na.com/global/home/viewMode · na:pref:v1/ana@na.com/b.123.../lista-documentos/grid
//
// Nunca é regra de acesso nem dado de negócio — só conveniência visual. Falha de armazenamento (modo privado, cota)
// nunca quebra a tela: leitura devolve o padrão, gravação é ignorada.
// =============================================================================================

export const PREFIXO_PREFERENCIAS = "na:pref:v1";
export const ESCOPO_GLOBAL = "global";

export interface ChavePreferencia {
  usuario: string | null | undefined;
  /** Project_ID_ACC quando a preferência depende do projeto; omitido/null = global. */
  projeto?: string | null;
  tela: string;
  config: string;
}

/** Armazenamento mínimo (injetável nos testes). */
export interface ArmazenamentoPreferencias {
  getItem(chave: string): string | null;
  setItem(chave: string, valor: string): void;
  removeItem(chave: string): void;
}

const seguro = (s: string) => s.trim().toLowerCase().replace(/[/\s]+/g, "_") || "_";

export const normalizarUsuario = (u: string | null | undefined) => (u && u.trim() ? seguro(u) : "anonimo");

export const montarChave = ({ usuario, projeto, tela, config }: ChavePreferencia) =>
  [PREFIXO_PREFERENCIAS, normalizarUsuario(usuario), projeto ? seguro(projeto) : ESCOPO_GLOBAL, seguro(tela), seguro(config)].join("/");

function armazenamentoPadrao(): ArmazenamentoPreferencias | null {
  try { return typeof window !== "undefined" && window.localStorage ? window.localStorage : null; } catch { return null; }
}

type Ouvinte = () => void;
const ouvintes = new Map<string, Set<Ouvinte>>();

/** Avisa os componentes montados que usam a mesma chave (ex.: duas instâncias da mesma grade). */
function notificar(chave: string) { ouvintes.get(chave)?.forEach((f) => f()); }

export function assinar(chave: string, f: Ouvinte): () => void {
  if (!ouvintes.has(chave)) ouvintes.set(chave, new Set());
  ouvintes.get(chave)!.add(f);
  return () => { ouvintes.get(chave)?.delete(f); };
}

export function lerPreferencia<T>(chave: ChavePreferencia, padrao: T, validar?: (v: unknown) => v is T,
  armazenamento: ArmazenamentoPreferencias | null = armazenamentoPadrao()): T {
  try {
    const bruto = armazenamento?.getItem(montarChave(chave));
    if (bruto == null) return padrao;
    const v: unknown = JSON.parse(bruto);
    if (validar && !validar(v)) return padrao;
    return (v ?? padrao) as T;
  } catch { return padrao; }
}

export function gravarPreferencia<T>(chave: ChavePreferencia, valor: T,
  armazenamento: ArmazenamentoPreferencias | null = armazenamentoPadrao()): void {
  const k = montarChave(chave);
  try {
    if (valor === undefined) armazenamento?.removeItem(k);
    else armazenamento?.setItem(k, JSON.stringify(valor));
  } catch { /* armazenamento indisponível: preferência só não persiste */ }
  notificar(k);
}

export function removerPreferencia(chave: ChavePreferencia, armazenamento: ArmazenamentoPreferencias | null = armazenamentoPadrao()) {
  const k = montarChave(chave);
  try { armazenamento?.removeItem(k); } catch { /* ignore */ }
  notificar(k);
}

// ---------------------------------------------------------------------------------------------
// Validadores simples reutilizáveis (preferência corrompida/antiga volta ao padrão, nunca quebra a tela).
// ---------------------------------------------------------------------------------------------
export const ehUmDe = <T extends string>(opcoes: readonly T[]) => (v: unknown): v is T => typeof v === "string" && (opcoes as readonly string[]).includes(v);
export const ehObjeto = <T extends object>() => (v: unknown): v is T => !!v && typeof v === "object" && !Array.isArray(v);
export const ehListaDeTexto = (v: unknown): v is string[] => Array.isArray(v) && v.every((x) => typeof x === "string");
