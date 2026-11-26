import { docKey, type PmoReport } from "@/components/pmo/pmoData";

export type DocumentTitleIndex = ReadonlyMap<string, string | null>;

/**
 * Indexa o título oficial que já vem da LDP no relatório executivo.
 *
 * O vínculo é estritamente projeto + número do documento. Se a LDP contiver
 * mais de um título distinto para a mesma chave, o resultado é null: escolher
 * um deles silenciosamente tornaria a apresentação não auditável.
 */
export function criarIndiceTitulosDocumentos(
  reports: readonly Pick<PmoReport, "projectId" | "documentos">[],
): DocumentTitleIndex {
  const encontrados = new Map<string, Set<string>>();

  for (const report of reports) {
    for (const documento of report.documentos) {
      const titulo = documento.titulo?.trim();
      if (!documento.codigo?.trim() || !titulo) continue;
      const chave = docKey(report.projectId, documento.codigo);
      const titulos = encontrados.get(chave) ?? new Set<string>();
      titulos.add(titulo);
      encontrados.set(chave, titulos);
    }
  }

  return new Map(
    [...encontrados].map(([chave, titulos]) => [
      chave,
      titulos.size === 1 ? [...titulos][0] : null,
    ]),
  );
}

export function obterTituloDocumentoOficial(
  indice: DocumentTitleIndex,
  projectId: string,
  numeroDocumento: string | null,
): string | null {
  if (!numeroDocumento?.trim()) return null;
  return indice.get(docKey(projectId, numeroDocumento)) ?? null;
}
