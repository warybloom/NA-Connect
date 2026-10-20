// Contrato entre a camada de dados (fontes N&A Connect) e os visuais reproduzidos do BI.
// Regra: nenhum número é inventado. Se o dado do visual não tem fonte oficial mapeada com segurança, o visual recebe
// { estado: "pendente" } e mostra o selo PENDENTE DE PARIDADE — nunca um valor de exemplo.

export type Valor = string | number | null;

export interface PontoCategoria {
  categoria: string;
  /** Valor por queryRef da projeção (Y/Y2) do visual. */
  valores: Record<string, number | null>;
}

export interface Opcao { chave: string; rotulo: string }

/** Nó da matriz hierárquica (pivotTable com Rows): um valor de um nível + medidas calculadas no grupo. */
export interface NoMatriz {
  /** Caminho de valores da raiz até este nó (identidade usada pelo estado de expansão do PBIR). */
  caminho: Valor[];
  nivel: number;
  valores: Record<string, Valor>;
  filhos: NoMatriz[];
}

export type DadosVisual =
  | { estado: "carregando" }
  | { estado: "pendente"; motivo: string }
  | { estado: "erro"; mensagem: string }
  | { estado: "ok"; tipo: "valores"; valores: Record<string, Valor>; formatos?: Record<string, FormatoValor>;
      /** Cartão com vários campos: queryRef → motivo do campo SEM fonte (o bloco mostra o selo, os demais o valor). */
      pendentes?: Record<string, string> }
  | { estado: "ok"; tipo: "pontos"; pontos: PontoCategoria[]; formatos?: Record<string, FormatoValor> }
  | { estado: "ok"; tipo: "linhas"; linhas: Record<string, Valor>[]; formatos?: Record<string, FormatoValor> }
  | { estado: "ok"; tipo: "matriz"; nos: NoMatriz[]; formatos?: Record<string, FormatoValor>;
      /** queryRef de cada nível de Rows (Level 4…7), na ordem do PBIR. */
      niveis: string[] }
  | { estado: "ok"; tipo: "opcoes"; opcoes: Opcao[]; selecionadas: string[]; alternar: (chave: string) => void; limpar: () => void };

export type FormatoValor = "inteiro" | "decimal" | "percentual" | "data" | "texto" | "moeda" | "horas";

/** Estado devolvido para visuais sem necessidade de dados (formas, imagens, textos fixos). */
export const SEM_DADOS: DadosVisual = { estado: "pendente", motivo: "" };
