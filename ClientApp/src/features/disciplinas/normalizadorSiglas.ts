/* Normalizador de Siglas de Disciplina — função ÚNICA de normalização no frontend.
   Espelho de Services/DisciplineAlias/SiglaNormalizer.cs: os dois lados rodam os mesmos casos de
   Tests/NA_Connect.DisciplineAlias.Tests/Fixtures/casos_resolucao.json.
   Consumida SOMENTE por painéis que agregam disciplinas entre VÁRIOS projetos (Gerência da Engenharia multiprojeto).
   Nunca renomeia dado de origem: só decide sob qual nome canônico PT-BR um item é somado no consolidado. */

export type ContextoTipo = "PROJETO" | "FONTE";
export type FonteDisciplina = "LDP" | "TIMESHEET" | "PROGRAMACAO";
export const FONTES: readonly FonteDisciplina[] = ["LDP", "TIMESHEET", "PROGRAMACAO"];

export interface RegraSigla {
  id: number; siglaOriginal: string; disciplinaCanonica: string; contextoTipo: ContextoTipo | null; contextoValor: string | null;
  ativo: boolean; origem: "MANUAL" | "IMPORTACAO"; importacaoId: number | null; versao: number;
  criadoEm: string; criadoPor: string; alteradoEm: string; alteradoPor: string;
}
export type OrigemResolucao = "Contexto" | "Global" | "Original";
export interface Resolucao { nome: string; origem: OrigemResolucao; regraId: number | null; conflito: string | null }
export interface ContextoResolucao { projetos: readonly (string | null | undefined)[]; fonte: FonteDisciplina | string | null }

/** Texto para exibição: NFC, espaços colapsados e aparados; caixa e acentos preservados. */
export const limpar = (v: string | null | undefined) => (v ?? "").normalize("NFC").replace(/\s+/gu, " ").trim();
/** Chave de comparação: igual a limpar() em maiúsculas. Acentos NÃO são removidos (nunca une por semelhança). */
export const chave = (v: string | null | undefined) => limpar(v).toUpperCase();
export const chaveContexto = (tipo: string | null | undefined, valor: string | null | undefined) =>
  chave(tipo) ? `${chave(tipo)}:${chave(valor)}` : "";

/** Prioridade: regra do contexto (projeto/fonte) → global → rótulo original. Conflito no mesmo nível = original + diagnóstico. */
export function resolver(regras: readonly RegraSigla[], candidatos: readonly (string | null | undefined)[], rotuloOriginal: string,
  contexto: ContextoResolucao): Resolucao {
  const ativas = regras.filter((r) => r.ativo);
  const chaves = [...new Set(candidatos.map(chave).filter(Boolean))];
  const projetos = new Set(contexto.projetos.map(chave).filter(Boolean));
  const fonte = chave(contexto.fonte);
  const noContexto = (r: RegraSigla) =>
    (r.contextoTipo === "PROJETO" && projetos.has(chave(r.contextoValor))) || (r.contextoTipo === "FONTE" && !!fonte && chave(r.contextoValor) === fonte);
  const niveis: [(r: RegraSigla) => boolean, OrigemResolucao][] = [[noContexto, "Contexto"], [(r) => r.contextoTipo === null, "Global"]];
  for (const [filtro, origem] of niveis) {
    for (const c of chaves) {
      const achadas = ativas.filter((r) => chave(r.siglaOriginal) === c && filtro(r));
      if (!achadas.length) continue;
      const nomes = [...new Map(achadas.map((r) => [chave(r.disciplinaCanonica), r.disciplinaCanonica])).values()];
      if (nomes.length > 1)
        return { nome: limpar(rotuloOriginal), origem: "Original", regraId: null,
          conflito: `Sigla '${achadas[0].siglaOriginal}' com regras conflitantes (${nomes.join(" / ")}); mantido o rótulo original.` };
      const regra = [...achadas].sort((a, b) => a.id - b.id)[0];
      return { nome: limpar(regra.disciplinaCanonica), origem, regraId: regra.id, conflito: null };
    }
  }
  return { nome: limpar(rotuloOriginal), origem: "Original", regraId: null, conflito: null };
}

/** Código curto do projeto ("CHS001 - ETHANOL PLANT" → "CHS001"), usado como identificador de contexto PROJETO. */
export const codigoProjeto = (nome: string | null | undefined) => /^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]/i.exec(nome ?? "")?.[1]?.toUpperCase() ?? null;

/** Item a agregar no painel multiprojeto (documento/gate, linha de programação ou realizado por disciplina). */
export interface ItemDisciplina { projectId: string; projectName?: string | null; fonte: FonteDisciplina; sigla?: string | null; rotulo: string }

/** Normalizador pronto para um painel: resolve e memoriza por item. Sem regras = identidade (rótulo original). */
export function criarNormalizador(regras: readonly RegraSigla[]) {
  const cache = new Map<string, Resolucao>();
  return {
    ativo: regras.some((r) => r.ativo),
    resolver(item: ItemDisciplina): Resolucao {
      const k = [item.projectId, item.projectName ?? "", item.fonte, item.sigla ?? "", item.rotulo].join("\u0001");
      let r = cache.get(k);
      if (!r) {
        r = resolver(regras, [item.sigla, item.rotulo], item.rotulo,
          { projetos: [item.projectId, item.projectName, codigoProjeto(item.projectName)], fonte: item.fonte });
        cache.set(k, r);
      }
      return r;
    },
  };
}
export type NormalizadorSiglas = ReturnType<typeof criarNormalizador>;
