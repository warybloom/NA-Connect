using APS.WebApp.Services.UploadGovernado;
using APS.WebApp.Services.Reports;

namespace APS.WebApp.Services.DocumentQuality;

public static class DocumentPresenceStatus
{
    public const string Present = "PRESENTE_NO_ACC";
    public const string Missing = "AUSENTE_NO_ACC";
    public const string CancelledPresent = "CANCELADO_PRESENTE_NO_ACC";
    public const string CancelledMissing = "CANCELADO_SEM_ACC";
}

public sealed record DocumentPresenceLocation(
    string FileName,
    string FolderPath,
    string FolderId,
    string ItemId,
    string? MatchedBy,
    string? MatchedCode);

public sealed record DocumentPresenceOutsideFile(string FileName, string FolderPath, string Reason);
public sealed record DocumentPresenceRow(
    long LdpRecordId,
    string? NaDocumentNumber,
    string? ClientDocumentNumber,
    string? Title,
    string? Discipline,
    string? DocumentStatus,
    string? Situation,
    string? StatusInFlow,
    bool IsCancelled,
    string Status,
    int Occurrences,
    IReadOnlyList<DocumentPresenceLocation> Locations);

public sealed record DocumentPresenceAuditResult(
    string ProjectId,
    DateTimeOffset ExecutedAt,
    int RootsScanned,
    int FoldersVisited,
    int FilesScanned,
    int TotalLdpDocuments,
    int ActiveLdpDocuments,
    int CancelledDocuments,
    int Present,
    int Missing,
    int CancelledPresent,
    int CancelledMissing,
    int MultipleOccurrences,
    int OutsideLdp,
    int AmbiguousFiles,
    int UnidentifiedFiles,
    IReadOnlyList<DocumentPresenceRow> Documents,
    IReadOnlyList<DocumentPresenceOutsideFile> OutsideFiles);

/// <summary>
/// Reconciliação LDP -> ACC. A LDP é a autoridade do universo documental; a árvore configurada no ACC é varrida
/// recursivamente. AT não entra porque ILdpDocumentosAtuaisRepository já expõe somente documentos.
/// Nenhum arquivo é baixado: a auditoria usa apenas nomes/IDs/caminhos do ACC.
/// </summary>
public sealed class DocumentPresenceAuditService(
    PastasDocumentosStore folders,
    ILdpDocumentosAtuaisRepository ldpRepository,
    AutodeskApiService acc,
    DocumentPresenceAuditStore store,
    ILogger<DocumentPresenceAuditService> logger)
{
    public async Task<DocumentPresenceAuditResult> RunAsync(string projectId, string accessToken, CancellationToken ct)
    {
        var config = await folders.ReadAsync(projectId, ct);
        if (config.Raizes.Count == 0)
            throw new InvalidOperationException("Configure pelo menos uma Pasta de Documentos antes de executar a auditoria LDP x ACC.");

        var ldp = await ldpRepository.ReadAsync(
            projectId,
            new[] { "Document_Status", "Situation", "Status_In_Flow" },
            ct);
        var files = new Dictionary<string, AccTreeFile>(StringComparer.Ordinal);
        var foldersVisited = 0;

        foreach (var root in config.Raizes)
        {
            ct.ThrowIfCancellationRequested();
            var tree = await AccFolderTraversal.CollectAsync(
                root.FolderId,
                string.IsNullOrWhiteSpace(root.Caminho) ? root.FolderId : root.Caminho,
                async (folderId, token) => await acc.GetFolderContentsAllPagesAsync(
                    projectId, folderId, accessToken, token, "AUDITORIA_LDP_ACC",
                    folderId == root.FolderId ? root.Caminho : null),
                _ => true,
                ct);

            foldersVisited += tree.FoldersVisited;
            foreach (var file in tree.Files)
            {
                var key = !string.IsNullOrWhiteSpace(file.Item.Id) ? file.Item.Id : file.Item.VersionUrn;
                if (!string.IsNullOrWhiteSpace(key)) files.TryAdd(key, file);
            }
        }

        var found = new Dictionary<long, List<DocumentPresenceLocation>>();
        var outside = new List<DocumentPresenceOutsideFile>();
        var ambiguous = 0;
        var unidentified = 0;

        foreach (var file in files.Values)
        {
            var identification = UploadGovernadoRules.IdentificarPorNomeArquivo(file.Item.Name, ldp);
            switch (identification.Resultado)
            {
                case IdentificacaoResultado.Encontrado:
                {
                    var doc = identification.Documento!;
                    if (!found.TryGetValue(doc.IdRegistro, out var locations))
                        found[doc.IdRegistro] = locations = [];
                    locations.Add(new(
                        file.Item.Name ?? "",
                        file.FolderPath,
                        file.FolderId,
                        file.Item.Id ?? file.Item.VersionUrn ?? "",
                        identification.Campo,
                        identification.Codigo));
                    break;
                }
                case IdentificacaoResultado.NaoEncontrado:
                    outside.Add(new(file.Item.Name ?? "", file.FolderPath, "Arquivo não corresponde a documento da LDP atual."));
                    break;
                case IdentificacaoResultado.Ambiguo:
                    ambiguous++;
                    outside.Add(new(file.Item.Name ?? "", file.FolderPath, "Nome corresponde a mais de um documento da LDP."));
                    break;
                case IdentificacaoResultado.NomeInvalido:
                    unidentified++;
                    break;
            }
        }

        var rows = ldp
            .Select(doc =>
            {
                found.TryGetValue(doc.IdRegistro, out var locations);
                var loc = (IReadOnlyList<DocumentPresenceLocation>)(locations?.OrderBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray() ?? []);

                var documentStatus = doc.Extra("Document_Status");
                var situation = doc.Extra("Situation");
                var statusInFlow = doc.Extra("Status_In_Flow");
                var cancelled = PmoExecutiveRules.Cancelado(documentStatus, situation, statusInFlow);
                var status = cancelled
                    ? (loc.Count > 0 ? DocumentPresenceStatus.CancelledPresent : DocumentPresenceStatus.CancelledMissing)
                    : (loc.Count > 0 ? DocumentPresenceStatus.Present : DocumentPresenceStatus.Missing);

                return new DocumentPresenceRow(
                    doc.IdRegistro,
                    doc.NaDocumentNumber,
                    doc.ClientDocumentNumber,
                    doc.Titulo,
                    doc.Disciplina,
                    documentStatus,
                    situation,
                    statusInFlow,
                    cancelled,
                    status,
                    loc.Count,
                    loc);
            })
            .OrderBy(x => x.Discipline, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.NaDocumentNumber ?? x.ClientDocumentNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var active = rows.Count(x => !x.IsCancelled);
        var cancelled = rows.Length - active;
        var present = rows.Count(x => x.Status == DocumentPresenceStatus.Present);
        var missing = rows.Count(x => x.Status == DocumentPresenceStatus.Missing);
        var cancelledPresent = rows.Count(x => x.Status == DocumentPresenceStatus.CancelledPresent);
        var cancelledMissing = rows.Count(x => x.Status == DocumentPresenceStatus.CancelledMissing);
        var multiple = rows.Count(x => x.Occurrences > 1);

        logger.LogInformation(
            "[AUDITORIA_LDP_ACC] Projeto={ProjectId} LDP={Total} Ativos={Ativos} Cancelados={Cancelados} Presentes={Presentes} Ausentes={Ausentes} CanceladosSemACC={CanceladosSemAcc} Arquivos={Arquivos} Pastas={Pastas}",
            projectId, rows.Length, active, cancelled, present, missing, cancelledMissing, files.Count, foldersVisited);

        var result = new DocumentPresenceAuditResult(
            projectId,
            DateTimeOffset.UtcNow,
            config.Raizes.Count,
            foldersVisited,
            files.Count,
            rows.Length,
            active,
            cancelled,
            present,
            missing,
            cancelledPresent,
            cancelledMissing,
            multiple,
            outside.Count(x => x.Reason.StartsWith("Arquivo não corresponde", StringComparison.Ordinal)),
            ambiguous,
            unidentified,
            rows,
            outside.OrderBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray());

        await store.SaveAsync(result, ct);
        return result;
    }
}
