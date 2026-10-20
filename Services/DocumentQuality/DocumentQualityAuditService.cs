using APS.WebApp.Services.UploadGovernado;

namespace APS.WebApp.Services.DocumentQuality;

public interface IDocumentQualityAuditService
{
    /// <param name="executionId">Id da execução (o executor em segundo plano gera antes de enfileirar); null = novo.</param>
    /// <param name="onProgress">Progresso por PDF (total, processados, arquivo atual) — só informativo, não altera a regra.</param>
    Task<DocumentQualityRunResult> RunAsync(string hubId, string projectIdAcc, string accessToken, string triggeredBy, CancellationToken cancellationToken = default,
        Guid? executionId = null, Action<DocumentQualityProgress>? onProgress = null);
}

/// <summary>Progresso da auditoria: total de PDFs, quantos já foram processados e o arquivo em leitura.</summary>
public sealed record DocumentQualityProgress(int Total, int Processed, string? CurrentFile);

public sealed class DocumentQualityAuditService(
    IOcrTemplateRepository templates,
    ILdpDocumentosAtuaisRepository ldpRepository,
    AutodeskApiService acc,
    IPdfRegionTextReader reader,
    IDocumentQualityOperationalAuditPersistence auditPersistence,
    IDocumentQualityReportStore reports,
    IConfiguration appConfiguration,
    ILogger<DocumentQualityAuditService> logger) : IDocumentQualityAuditService
{
    /// <summary>
    /// Proteção operacional (não truncamento): a árvore inteira da pasta-raiz é auditada; acima do limite a execução é recusada com
    /// mensagem explícita. Configurável em DocumentQuality:MaxFiles (padrão alto — a auditoria roda em segundo plano com progresso).
    /// </summary>
    public const int MaxFilesPadrao = 5000;
    private int MaxFiles => appConfiguration.GetValue("DocumentQuality:MaxFiles", MaxFilesPadrao) is > 0 and var n ? n : MaxFilesPadrao;

    public async Task<DocumentQualityRunResult> RunAsync(string hubId, string projectIdAcc, string accessToken, string triggeredBy, CancellationToken cancellationToken = default,
        Guid? executionId = null, Action<DocumentQualityProgress>? onProgress = null)
    {
        var started = DateTime.UtcNow;
        var id = executionId ?? Guid.NewGuid();
        var configuration = await templates.GetProjectConfigurationAsync(projectIdAcc, cancellationToken);
        configuration.EnsureReady();
        var packageId = configuration.PackageId!.Value;
        var regions = await templates.GetRegionsAsync(packageId, null, cancellationToken);
        if (regions.Count == 0) throw new DocumentQualityConfigurationException("O template selecionado não possui regiões calibradas.");
        // Campos auditados = campos ATIVOS do TEMPLATE (config.PacoteOcrCampo). Coordenada é por formato; campo sem coordenada no
        // formato usado = NÃO CALIBRADO. Sem a migration 07, compatibilidade: campos = os que têm região em algum formato.
        var templateFields = await templates.GetTemplateFieldsAsync(packageId, cancellationToken);
        var activeFields = templateFields == null
            ? regions.Select(r => r.TargetField).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : templateFields.Where(f => f.Active).OrderBy(f => f.Order).Select(f => f.Field).ToArray();
        if (templateFields == null) logger.LogWarning("[DOC_QUALITY] config.PacoteOcrCampo ausente (migration 07 pendente): campos derivados das regiões.");
        if (activeFields.Length == 0) throw new DocumentQualityConfigurationException("O template selecionado não possui campos auditados ativos.");
        var mappings = await templates.GetFormatMappingsAsync(packageId, cancellationToken);
        // A identificação é pelo NOME DO ARQUIVO (regra do envio governado); as regiões do template só AUDITAM campos — o template
        // escolhe livremente quais campos da LDP audita (o número no carimbo é apenas fallback de identificação).

        // A pasta configurada é uma RAIZ DE AUDITORIA: ela + todas as subpastas, em qualquer profundidade. Primeiro a árvore inteira é
        // descoberta (o total do progresso considera todas as subpastas); cada pasta é listada com paginação pelo método existente.
        var rootPath = configuration.AuditFolderPath ?? configuration.AuditFolderId!;
        var tree = await AccFolderTraversal.CollectAsync(configuration.AuditFolderId!, rootPath,
            async (folderId, ct) => await acc.GetFolderContentsAllPagesAsync(projectIdAcc, folderId, accessToken, ct, "AUDITORIA_DOCUMENTAL", folderId == configuration.AuditFolderId ? rootPath : null),
            item => Path.GetExtension(item.Name ?? "").Equals(".pdf", StringComparison.OrdinalIgnoreCase), cancellationToken);
        var pdfs = tree.Files;
        logger.LogInformation("[DOC_QUALITY] Árvore de {Raiz}: {Pastas} pasta(s) percorrida(s), {Pdfs} PDF(s).", rootPath, tree.FoldersVisited, pdfs.Count);
        if (pdfs.Count > MaxFiles)
            throw new InvalidOperationException($"A pasta-raiz \"{rootPath}\" tem {pdfs.Count} PDFs em {tree.FoldersVisited} pastas, acima do limite operacional de {MaxFiles} " +
                "(DocumentQuality:MaxFiles). Nada foi auditado — escolha uma pasta mais específica ou aumente o limite.");
        IReadOnlyList<LdpDocumentoAtual> ldp;
        try { ldp = await ldpRepository.ReadAsync(projectIdAcc, activeFields, cancellationToken); }
        catch (ArgumentException ex) { throw new DocumentQualityConfigurationException($"Campo auditado inválido no template: {ex.Message}"); }
        var documents = new List<DocumentQualityDocumentResult>(pdfs.Count);
        onProgress?.Invoke(new(pdfs.Count, 0, null));

        foreach (var file in pdfs)
        {
            var item = file.Item;
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(new(pdfs.Count, documents.Count, item.Name));
            try
            {
                await using var stream = await acc.DownloadFileStreamAsync(projectIdAcc, item.VersionUrn, accessToken);
                // Format da LDP do documento (identificado pelo nome) → perfil pelo MAPEAMENTO DO TEMPLATE; a página sempre valida.
                var ldpFormat = UploadGovernadoRules.IdentificarPorNomeArquivo(item.Name, ldp).Documento?.Formato;
                var read = await reader.ReadAsync(stream, new DocumentQualityReadPlan(regions, activeFields, ldpFormat, mappings), cancellationToken);
                // Folder = caminho REAL da subpasta onde o PDF está (a disciplina continua vindo da LDP, nunca do caminho).
                documents.Add(DocumentQualityComparisonEngine.Compare(item.Name, file.FolderPath, projectIdAcc, read, ldp));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[DOC_QUALITY] Falha de leitura do PDF {FileName}.", item.Name);
                documents.Add(new DocumentQualityDocumentResult
                {
                    FileName = item.Name, Folder = file.FolderPath,
                    Status = DocumentQualityStatus.ErroLeitura, Reason = "Falha ao abrir ou ler a camada de texto do PDF.",
                });
            }
        }
        onProgress?.Invoke(new(pdfs.Count, documents.Count, null));

        DocumentQualityComparisonEngine.MarkDuplicates(documents);
        var persistence = await auditPersistence.PersistAsync(id, started, projectIdAcc, configuration.ProjectName, documents, triggeredBy, cancellationToken);
        var finished = DateTime.UtcNow;
        var result = new DocumentQualityRunResult(id, started, finished, projectIdAcc, configuration.ProjectName,
            configuration.AuditFolderPath ?? configuration.AuditFolderId!, configuration.PackageId.Value, configuration.PackageName,
            Summary(documents), documents, persistence.Succeeded, persistence.Error);
        await reports.SaveAsync(result, cancellationToken);
        return result;
    }

    private static DocumentQualitySummary Summary(IReadOnlyList<DocumentQualityDocumentResult> documents) => new(
        documents.Count,
        documents.Count(d => d.Status == DocumentQualityStatus.Conforme),
        documents.Count(d => d.Status == DocumentQualityStatus.Divergencia),
        documents.Count(d => d.Status == DocumentQualityStatus.ForaDaLdp),
        documents.Count(d => d.Status == DocumentQualityStatus.NaoIdentificado),
        documents.Count(d => d.Status == DocumentQualityStatus.Duplicado),
        documents.Count(d => d.Status == DocumentQualityStatus.ErroLeitura))
    {
        Ambiguous = documents.Count(d => d.Status == DocumentQualityStatus.Ambiguo),
        NotCalibrated = documents.Count(d => d.Status == DocumentQualityStatus.NaoCalibrado),
    };
}
