// Envio DENTRO do navegador de pastas (FilesPage) — lógica pura, testada em __tests__.
// A regra é a do backend (UploadGovernadoRules via /api/upload-governado/validar e /enviar); aqui só o estado da fila na tela.
import { permiteEnvio, type StatusValidacao, type Validacao } from "./uploadGovernadoApi";

export type EstadoArquivo = "VALIDANDO" | "PRONTO" | "BLOQUEADO" | "ENVIANDO" | "ENVIADO" | "ERRO";

export interface ArquivoNaFila {
  chave: string;
  arquivo: File;
  estado: EstadoArquivo;
  validacao?: Validacao;
  /** 0–100 durante o envio. */
  progresso?: number;
  mensagem?: string;
}

/** Mensagem para a PRODUÇÃO (não técnica) quando o arquivo não pode subir. */
export const MENSAGEM_BLOQUEIO: Partial<Record<StatusValidacao, { titulo: string; texto: string }>> = {
  FORA_DA_LDP: {
    titulo: "Documento não encontrado na LDP",
    texto: "O documento não existe na LDP atual deste projeto. Entre em contato com o Planejamento para solicitar sua inclusão.",
  },
  DOCUMENTO_AMBIGUO: {
    titulo: "Documento ambíguo",
    texto: "O nome do arquivo corresponde a mais de um documento da LDP. Confira o nome do arquivo ou procure o Planejamento.",
  },
  NOME_INVALIDO: {
    titulo: "Nome de arquivo inválido",
    texto: "Use o número do documento no nome do arquivo, seguido da extensão (ex.: PDF, DWG).",
  },
};

/** Aplica o resultado da pré-validação (mesma ordem dos nomes enviados) aos arquivos que estavam VALIDANDO. */
export function aplicarValidacao(fila: readonly ArquivoNaFila[], chaves: readonly string[], resultados: readonly Validacao[]): ArquivoNaFila[] {
  const porChave = new Map(chaves.map((c, i) => [c, resultados[i]]));
  return fila.map((a) => {
    const v = porChave.get(a.chave);
    if (!v) return a;
    return permiteEnvio(v.status)
      ? { ...a, estado: "PRONTO", validacao: v, mensagem: undefined }
      : { ...a, estado: "BLOQUEADO", validacao: v, mensagem: MENSAGEM_BLOQUEIO[v.status]?.texto ?? v.mensagem };
  });
}

/** Arquivos do lote que sobem automaticamente (mesma ordem dos resultados); bloqueados nunca impedem os demais. */
export const prontosDoLote = (novos: readonly ArquivoNaFila[], resultados: readonly Validacao[]) =>
  novos.filter((_, i) => resultados[i] && permiteEnvio(resultados[i].status));

/** Estados finais: a linha pode ser fechada pelo usuário. */
export const ESTADOS_CONCLUIDOS: readonly EstadoArquivo[] = ["ENVIADO", "BLOQUEADO", "ERRO"];

export function contagem(fila: readonly ArquivoNaFila[]) {
  const n = (e: EstadoArquivo) => fila.filter((a) => a.estado === e).length;
  return { total: fila.length, validando: n("VALIDANDO"), prontos: n("PRONTO"), bloqueados: n("BLOQUEADO"), enviando: n("ENVIANDO"), enviados: n("ENVIADO"), erros: n("ERRO") };
}

/** Caminho legível da pasta atual a partir do breadcrumb ("Project Files / 06 - ENGINEERING / 06.01 - PROCESS"). */
export const caminhoDaPasta = (trilha: readonly { name: string }[]) => trilha.map((p) => p.name).join(" / ");
