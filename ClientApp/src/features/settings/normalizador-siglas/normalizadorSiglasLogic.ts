import { chave, chaveContexto, limpar, type RegraSigla } from "@/features/disciplinas/normalizadorSiglas";
import type { DadosRegra, LinhaPlano, MapeamentoColunas } from "./normalizadorSiglasApi";

export type FiltroStatus = "todas" | "ativas" | "inativas";

/** Busca por sigla, disciplina ou contexto (sem caixa/espaços; acentos contam, como na regra). */
export function filtrarRegras(regras: readonly RegraSigla[], busca: string, status: FiltroStatus): RegraSigla[] {
  const b = chave(busca);
  return regras
    .filter((r) => status === "todas" || (status === "ativas") === r.ativo)
    .filter((r) => !b || [r.siglaOriginal, r.disciplinaCanonica, r.contextoValor].some((v) => chave(v).includes(b)))
    .sort((a, z) => chave(a.disciplinaCanonica).localeCompare(chave(z.disciplinaCanonica), "pt-BR") || chave(a.siglaOriginal).localeCompare(chave(z.siglaOriginal), "pt-BR"));
}

export const rotuloContexto = (r: Pick<RegraSigla, "contextoTipo" | "contextoValor">) =>
  r.contextoTipo ? `${r.contextoTipo === "PROJETO" ? "Projeto" : "Fonte"}: ${r.contextoValor}` : "Global";

/** Validação local do formulário (o servidor revalida tudo). Mesmo texto de erro do backend quando possível. */
export function validarRegra(d: DadosRegra, existentes: readonly RegraSigla[], ignorarId: number | null): string[] {
  const erros: string[] = [];
  if (!limpar(d.siglaOriginal)) erros.push("Sigla vazia.");
  if (!limpar(d.disciplinaCanonica)) erros.push("Disciplina canônica vazia.");
  if (d.contextoTipo && !limpar(d.contextoValor)) erros.push("Tipo de contexto sem valor.");
  const k = chave(d.siglaOriginal), ctx = chaveContexto(d.contextoTipo, d.contextoValor);
  const dup = existentes.find((r) => r.id !== ignorarId && chave(r.siglaOriginal) === k && chaveContexto(r.contextoTipo, r.contextoValor) === ctx);
  if (k && dup) erros.push(`Já existe regra para a sigla '${dup.siglaOriginal}' (${rotuloContexto(dup)}) → ${dup.disciplinaCanonica}. Corrija a regra existente.`);
  return erros;
}

/** Siglas diferentes que somam no mesmo nome canônico (o que o painel multiprojeto vai agrupar). */
export function gruposPorNomeCanonico(regras: readonly RegraSigla[]) {
  const m = new Map<string, { nome: string; siglas: string[] }>();
  for (const r of regras.filter((x) => x.ativo)) {
    const k = chave(r.disciplinaCanonica);
    if (!m.has(k)) m.set(k, { nome: limpar(r.disciplinaCanonica), siglas: [] });
    m.get(k)!.siglas.push(r.siglaOriginal);
  }
  return [...m.values()].sort((a, b) => a.nome.localeCompare(b.nome, "pt-BR"));
}

export function mapeamentoCompleto(m: Partial<MapeamentoColunas>): m is MapeamentoColunas {
  const usadas = [m.sigla, m.disciplina, m.contextoTipo, m.contextoValor, m.ativo].filter(Boolean);
  return !!m.sigla && !!m.disciplina && new Set(usadas).size === usadas.length && (!m.contextoValor || !!m.contextoTipo);
}

export const ORDEM_ACOES: LinhaPlano["acao"][] = ["Rejeitada", "Alterar", "Incluir", "Duplicada", "Inalterada"];
