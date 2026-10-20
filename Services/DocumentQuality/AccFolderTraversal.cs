namespace APS.WebApp.Services.DocumentQuality;

/// <summary>Arquivo encontrado na árvore + o caminho REAL da pasta onde ele está.</summary>
public sealed record AccTreeFile(AccFolderItem Item, string FolderId, string FolderPath);

public sealed record AccTreeResult(IReadOnlyList<AccTreeFile> Files, int FoldersVisited);

/// <summary>
/// TRAVESSIA DA ÁRVORE DE PASTAS do ACC a partir de uma RAIZ (01/10/2026 — Auditoria de Qualidade): a pasta configurada + todas as
/// subpastas, em qualquer profundidade (BFS). Cada pasta é listada pelo método que já cuida da paginação
/// (AutodeskApiService.GetFolderContentsAllPagesAsync, recebido como delegate — testável sem ACC).
/// Desce SÓ em Type == "folders" (nunca em item); usa IDs reais do ACC (nunca nomes); FolderId visitado não é relido (ciclo/duplicata
/// não gera laço); o mesmo item só entra uma vez; respeita o CancellationToken entre pastas. Sem limite artificial de profundidade.
/// </summary>
public static class AccFolderTraversal
{
    public static async Task<AccTreeResult> CollectAsync(string rootFolderId, string rootPath,
        Func<string, CancellationToken, Task<IReadOnlyList<AccFolderItem>>> listFolder, Func<AccFolderItem, bool> include,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Id, string Path)>();
        var files = new List<AccTreeFile>();
        pending.Enqueue((rootFolderId, rootPath));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folderId, folderPath) = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(folderId) || !visited.Add(folderId)) continue;

            foreach (var item in await listFolder(folderId, cancellationToken))
            {
                if (item.IsHidden) continue;
                if (item.Type == "folders")
                {
                    if (!string.IsNullOrWhiteSpace(item.Id) && !visited.Contains(item.Id)) pending.Enqueue((item.Id, $"{folderPath}/{item.Name}"));
                }
                else if (item.Type == "items" && include(item))
                {
                    var key = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : item.VersionUrn;
                    if (!string.IsNullOrWhiteSpace(key) && seenItems.Add(key)) files.Add(new(item, folderId, folderPath));
                }
            }
        }

        // Ordem estável para o progresso e o relatório: por caminho da pasta, depois nome do arquivo.
        var ordered = files.OrderBy(f => f.FolderPath, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(ordered, visited.Count);
    }
}
