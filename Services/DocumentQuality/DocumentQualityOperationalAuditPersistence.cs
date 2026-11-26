using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Aggregation;
using APS.WebApp.Services.OperationalAudit.Dedup;
using APS.WebApp.Services.OperationalAudit.Persistence;

namespace APS.WebApp.Services.DocumentQuality;

public sealed record DocumentQualityPersistenceResult(bool Succeeded, string? Error);

public interface IDocumentQualityOperationalAuditPersistence
{
    Task<DocumentQualityPersistenceResult> PersistAsync(Guid executionId, DateTime startedAtUtc, string projectIdAcc, string? projectName,
        IReadOnlyList<DocumentQualityDocumentResult> documents, string triggeredBy, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persiste o resultado somente quando uma auditoria é executada. A fábrica é deliberadamente
/// lazy: telas/endpoints de configuração não devem precisar de AuditRepositoryConnection.
/// </summary>
public sealed class DocumentQualityOperationalAuditPersistence(Func<IAuditRepository> repositoryFactory, ILogger<DocumentQualityOperationalAuditPersistence> logger)
    : IDocumentQualityOperationalAuditPersistence
{
    private const string Stage = "DOCQ";

    public async Task<DocumentQualityPersistenceResult> PersistAsync(Guid executionId, DateTime startedAtUtc, string projectIdAcc, string? projectName,
        IReadOnlyList<DocumentQualityDocumentResult> documents, string triggeredBy, CancellationToken cancellationToken = default)
    {
        long? runId = null;
        IAuditRepository? repository = null;
        try
        {
            repository = repositoryFactory();
            runId = await repository.IniciarAuditRunAsync(executionId, triggeredBy, new[] { Stage }, cancellationToken);
            var now = DateTime.UtcNow;
            var requirements = BuildRequirements(executionId, projectIdAcc, documents, now);
            var findings = BuildFindings(executionId, projectIdAcc, projectName, documents, now);
            foreach (var finding in findings)
            {
                finding.DedupHash = DedupHashCalculator.CalcularDedupHash(finding);
                finding.OccurrenceHash = DedupHashCalculator.CalcularOccurrenceHash(finding, executionId);
            }
            var stageResult = RequirementStatusAggregator.Agregar(executionId, projectIdAcc, Stage, "Auditoria de Qualidade de Documentos",
                requirements, findings, startedAtUtc, now);
            var persisted = await repository.PersistirProjetoAsync(runId.Value, new OperationalAuditProjectData
            {
                ProjectIdAcc = projectIdAcc,
                ProjectName = projectName,
                Findings = findings,
                Requirements = requirements,
                StageResults = new[] { stageResult },
            }, cancellationToken);
            if (!persisted.Sucesso) throw new InvalidOperationException(persisted.Erro ?? "Falha ao persistir o projeto na auditoria operacional.");
            var status = findings.Count == 0 ? OperationalAuditRunStatus.Completed : OperationalAuditRunStatus.CompletedWithWarnings;
            await repository.FinalizarAuditRunAsync(runId.Value, status, 1, findings.Count, requirements.Count, null, cancellationToken);
            return new(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[DOC_QUALITY] Falha ao persistir a execução {ExecutionId} na Auditoria Operacional.", executionId);
            if (runId.HasValue && repository != null)
            {
                try { await repository.FinalizarAuditRunAsync(runId.Value, OperationalAuditRunStatus.Failed, 1, 0, 0, ex.Message, cancellationToken); }
                catch (Exception finishEx) { logger.LogWarning(finishEx, "[DOC_QUALITY] Falha ao finalizar AuditRun {AuditRunId}.", runId); }
            }
            return new(false, ex.Message);
        }
    }

    private static List<OperationalRequirementResult> BuildRequirements(Guid executionId, string projectIdAcc,
        IReadOnlyList<DocumentQualityDocumentResult> documents, DateTime now)
    {
        var result = new List<OperationalRequirementResult>();
        foreach (var document in documents)
        {
            var identificationStatus = document.Status switch
            {
                DocumentQualityStatus.ForaDaLdp or DocumentQualityStatus.NaoIdentificado or DocumentQualityStatus.Duplicado or DocumentQualityStatus.Ambiguo => OperationalRequirementStatus.Failed,
                DocumentQualityStatus.ErroLeitura when document.LdpRecordId == null => OperationalRequirementStatus.Inconclusive,
                _ => OperationalRequirementStatus.Approved,
            };
            result.Add(Requirement(executionId, projectIdAcc, "DOCQ_IDENTIFICATION", document.FileName,
                identificationStatus, document.NaDocumentNumber ?? document.ClientDocumentNumber, document.Document ?? document.Reason, now));
            if (document.Fields.Count == 0)
            {
                result.Add(Requirement(executionId, projectIdAcc, "DOCQ_READABILITY", document.FileName,
                    document.Status == DocumentQualityStatus.ErroLeitura ? OperationalRequirementStatus.Inconclusive : OperationalRequirementStatus.Approved,
                    "PDF com texto incorporado", document.Reason, now));
                continue;
            }
            foreach (var field in document.Fields)
            {
                var status = field.Status switch
                {
                    DocumentFieldStatus.Ok => OperationalRequirementStatus.Approved,
                    DocumentFieldStatus.NaoLido or DocumentFieldStatus.NaoCalibrado => OperationalRequirementStatus.Inconclusive,
                    _ => OperationalRequirementStatus.Failed,
                };
                result.Add(Requirement(executionId, projectIdAcc, Code(field.Field), document.FileName, status, field.Expected, field.Found, now));
            }
        }
        return result;
    }

    private static OperationalRequirementResult Requirement(Guid executionId, string projectIdAcc, string code, string file, OperationalRequirementStatus status,
        string? expected, string? found, DateTime now) => new()
    {
        ExecutionId = executionId, DataHoraUtc = now, ProjectIdAcc = projectIdAcc, Etapa = Stage,
        RequirementCode = code, ObjetoAvaliado = file, Status = status, ValorEsperado = expected, ValorEncontrado = found,
    };

    private static List<OperationalFinding> BuildFindings(Guid executionId, string projectIdAcc, string? projectName,
        IReadOnlyList<DocumentQualityDocumentResult> documents, DateTime now)
    {
        var result = new List<OperationalFinding>();
        foreach (var document in documents)
        {
            // NÃO CALIBRADO = lacuna do template para o formato (configuração), não problema do documento: sem achado.
            foreach (var field in document.Fields.Where(field => field.Status is not (DocumentFieldStatus.Ok or DocumentFieldStatus.NaoCalibrado)))
                result.Add(Finding(executionId, projectIdAcc, projectName, document, now, Code(field.Field), field.Label,
                    field.Status == DocumentFieldStatus.NaoLido ? OperationalAuditClassification.DadoAusente : OperationalAuditClassification.DadoInvalido,
                    field.Field, field.Expected, field.Found, field.Reason ?? document.Reason));

            if (document.Status is DocumentQualityStatus.ForaDaLdp or DocumentQualityStatus.NaoIdentificado or DocumentQualityStatus.Duplicado or DocumentQualityStatus.Ambiguo
                || (document.Status == DocumentQualityStatus.ErroLeitura && document.Fields.Count == 0))
            {
                var classification = document.Status switch
                {
                    DocumentQualityStatus.ForaDaLdp => OperationalAuditClassification.DocumentoAccSemLdp,
                    DocumentQualityStatus.Duplicado or DocumentQualityStatus.Ambiguo => OperationalAuditClassification.DadoDuplicado,
                    _ => OperationalAuditClassification.DadoAusente,
                };
                result.Add(Finding(executionId, projectIdAcc, projectName, document, now,
                    GeneralCode(document.Status),
                    document.Status, classification, "Documento", null, document.Document, document.Reason));
            }
        }
        return result;
    }

    private static OperationalFinding Finding(Guid executionId, string projectIdAcc, string? projectName, DocumentQualityDocumentResult document,
        DateTime now, string code, string title, OperationalAuditClassification classification, string field, string? expected, string? found, string reason) => new()
    {
        ExecutionId = executionId, DataHoraUtc = now, Projeto = projectName ?? projectIdAcc, ProjectIdAcc = projectIdAcc,
        Etapa = Stage, RegraId = code, CodigoProblema = code, Titulo = title, Descricao = reason,
        ErrorDomain = OperationalAuditErrorDomain.Operacional, Classificacao = classification,
        Severidade = classification == OperationalAuditClassification.DocumentoAccSemLdp ? OperationalAuditSeverity.Alto : OperationalAuditSeverity.Medio,
        DocumentoNa = document.NaDocumentNumber, DocumentoCliente = document.ClientDocumentNumber, NomeArquivo = document.FileName,
        Extensao = Path.GetExtension(document.FileName), PastaEncontrada = document.Folder, Campo = field,
        ValorEsperado = expected, ValorEncontrado = found, FonteEvidencia = "PDF/ACC + LDP atual",
        Evidencia = reason, AcaoRecomendada = "Revisar o PDF e a LDP com Document Control.", BloqueiaEtapa = false,
        AutomacaoPodeCorrigir = false, MotivoNormalizado = reason,
    };

    private static string Code(string field) => "DOCQ_FIELD_" + field;
    private static string GeneralCode(string status) => status switch
    {
        DocumentQualityStatus.ForaDaLdp => "DOCQ_IDENTIFICATION",
        DocumentQualityStatus.NaoIdentificado => "DOCQ_IDENTIFICATION",
        DocumentQualityStatus.Duplicado => "DOCQ_IDENTIFICATION",
        DocumentQualityStatus.Ambiguo => "DOCQ_IDENTIFICATION",
        DocumentQualityStatus.ErroLeitura => "DOCQ_READABILITY",
        _ => "DOCQ_DOCUMENTO",
    };
}
