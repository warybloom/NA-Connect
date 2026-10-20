// Entrada nas PASTAS do projeto (Project Files) — o N&A Connect como máscara do CDE (01/10/2026).
// Mesmo endpoint e mesmo navegador (FilesPage) já usados pela Home e pela aba Docs do CDE: nada de browser paralelo.

/** Pasta raiz "Project Files" do projeto (GET /DataManagement/Data/GetProjectRootFolder). */
export async function buscarPastaRaiz(hubId: string, projectId: string, signal?: AbortSignal): Promise<string> {
  const r = await fetch(`/DataManagement/Data/GetProjectRootFolder?hubId=${encodeURIComponent(hubId)}&projectId=${encodeURIComponent(projectId)}`,
    { credentials: "include", signal });
  if (r.status === 401) throw new Error("Sessão expirada. Entre novamente.");
  if (r.status === 403) throw new Error("Você não tem acesso às pastas deste projeto.");
  if (!r.ok) throw new Error(`Não foi possível localizar a pasta Project Files (HTTP ${r.status}).`);
  const data = (await r.json()) as { rootFolderId?: string };
  if (!data?.rootFolderId) throw new Error("O ACC não retornou a pasta Project Files do projeto.");
  return data.rootFolderId;
}

/** Rota do navegador de pastas (FilesPage) numa pasta do projeto. */
export const rotaPasta = (hubId: string, projectId: string, folderId: string) =>
  `/projects/${encodeURIComponent(hubId)}/${encodeURIComponent(projectId)}/${encodeURIComponent(folderId)}`;

/**
 * Trilha VISUAL das pastas (só exibição; IDs/navegação reais intactos). O caminho do ACC pode começar pela pasta-raiz técnica do
 * projeto antes de "Project Files" — para o usuário a trilha começa em "Project Files", sem "Contas"/"Projetos" e sem duplicar.
 */
export function trilhaVisivel<T extends { id: string; name: string }>(caminho: readonly T[]): T[] {
  const ehProjectFiles = (n: string) => n.trim().toLowerCase() === "project files";
  const i = caminho.findIndex((p) => ehProjectFiles(p.name));
  if (i >= 0) return caminho.slice(i);
  // Sem "Project Files" no caminho: a 1ª pasta é a raiz técnica do projeto (nome interno) → exibida como "Project Files".
  return caminho.length > 0 ? [{ ...caminho[0], name: "Project Files" }, ...caminho.slice(1)] : [];
}
