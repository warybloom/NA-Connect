// Formulário do pacote Por Disciplina: texto digitado → contrato do backend. Nenhum valor financeiro é calculado aqui;
// o rateio, a divergência com a LDP e a apropriação vêm do servidor (simulação/prévia).
import { lerValor, type Abrangencia, type DisciplinaCadastrada, type PacoteDisciplinaInput } from "./costsApi";

export interface LinhaDisciplinaForm { chave: string; disciplina: string; hh: string; custo: string; abrangencia: Abrangencia; documentos: string[]; justificativa: string }
export interface CabecalhoPacoteForm { nome: string; alias: string; empresa: string; descricao: string }

let sequencia = 0;
export const novaLinhaDisciplina = (disciplina = ""): LinhaDisciplinaForm =>
  ({ chave: `d${++sequencia}`, disciplina, hh: "", custo: "", abrangencia: "TODOS", documentos: [], justificativa: "" });

const numTexto = (n: number) => n.toLocaleString("pt-BR", { useGrouping: false, maximumFractionDigits: 6 });

export function linhasDoCadastro(cadastro: DisciplinaCadastrada[]): LinhaDisciplinaForm[] {
  return cadastro.map((c) => ({ ...novaLinhaDisciplina(c.disciplina), hh: numTexto(c.hhPrevisto), custo: numTexto(c.custoPrevisto), abrangencia: c.abrangencia,
    documentos: [...c.documentos], justificativa: c.justificativa ?? "" }));
}

/** Converte o formulário; erros de digitação são locais, regras de rateio ficam no backend. */
export function montarPacoteDisciplina(cab: CabecalhoPacoteForm, linhas: LinhaDisciplinaForm[]): { input: PacoteDisciplinaInput } | { erros: string[] } {
  const erros: string[] = [];
  if (!cab.nome.trim()) erros.push("Informe o nome do pacote.");
  if (linhas.length === 0) erros.push("Adicione ao menos uma disciplina.");
  const disciplinas = linhas.map((l, i) => {
    const hh = lerValor(l.hh); const custo = l.custo.trim() ? lerValor(l.custo) : 0;
    const n = i + 1;
    if (!l.disciplina) erros.push(`Linha ${n}: escolha a disciplina.`);
    if (hh === null || hh < 0) erros.push(`Linha ${n}: HH previsto inválido.`);
    if (custo === null || custo < 0) erros.push(`Linha ${n}: custo previsto inválido.`);
    if (l.abrangencia === "SELECIONADOS" && l.documentos.length === 0) erros.push(`Linha ${n}: selecione ao menos um documento.`);
    return { disciplina: l.disciplina, hhPrevisto: hh ?? 0, custoPrevisto: custo ?? 0, abrangencia: l.abrangencia,
      documentos: l.abrangencia === "SELECIONADOS" ? l.documentos : null, justificativa: l.justificativa.trim() || null };
  });
  if (erros.length) return { erros };
  return { input: { nomeReferencia: cab.nome.trim(), aliasTimesheet: cab.alias.trim() || null, empresa: cab.empresa.trim() || null, descricao: cab.descricao.trim() || null, disciplinas } };
}

/** A simulação só vale para exatamente a entrada que foi simulada: qualquer alteração exige nova simulação antes de salvar. */
export const assinaturaEntrada = (input: PacoteDisciplinaInput) => JSON.stringify(input);

export const sinal = (v: number) => (v > 0 ? "+" : "") + v.toLocaleString("pt-BR", { maximumFractionDigits: 2 });
