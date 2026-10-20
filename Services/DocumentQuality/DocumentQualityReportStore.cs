using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace APS.WebApp.Services.DocumentQuality;

public interface IDocumentQualityReportStore
{
    Task SaveAsync(DocumentQualityRunResult result, CancellationToken cancellationToken = default);
    string? GetPath(string projectIdAcc, Guid executionId);
    /// <summary>Execução mais recente do projeto (resultado completo), ou null se nunca houve execução.</summary>
    Task<DocumentQualityRunResult?> GetLatestAsync(string projectIdAcc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Relatórios por EXECUÇÃO (ExecutionId): CSV para Document Control + o resultado completo em JSON (a tela abre na execução mais
/// recente). Arquivos nunca são apagados nem sobrescritos por outra execução — o histórico analítico (achados, requisitos,
/// ocorrências por execução) fica em OperationalAudit (audit.AuditRun/AuditOccurrence/AuditRequirementResult).
/// </summary>
public sealed class DocumentQualityReportStore(string rootDirectory) : IDocumentQualityReportStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(DocumentQualityRunResult result, CancellationToken cancellationToken = default)
    {
        var path = PathFor(result.ProjectIdAcc, result.ExecutionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine("Projeto,Arquivo,Pasta,Número N&A,Número Cliente,Título esperado,Título encontrado,Revisão esperada,Revisão encontrada,Disciplina,Resultado,Motivo,Data/hora");
        foreach (var document in result.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(sb, result.ProjectName ?? result.ProjectIdAcc, document.FileName, document.Folder, document.NaDocumentNumber,
                document.ClientDocumentNumber, document.TitleExpected, document.TitleFound, document.RevisionExpected, document.RevisionFound,
                document.Discipline, document.Status, document.Reason, result.FinishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), cancellationToken);
        await File.WriteAllBytesAsync(Path.ChangeExtension(path, ".json"), JsonSerializer.SerializeToUtf8Bytes(result, Json), cancellationToken);
    }

    public async Task<DocumentQualityRunResult?> GetLatestAsync(string projectIdAcc, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(PathFor(projectIdAcc, Guid.Empty))!;
        if (!Directory.Exists(directory)) return null;
        DocumentQualityRunResult? latest = null;
        foreach (var file in Directory.EnumerateFiles(directory, "Auditoria_Qualidade_Documentos_*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(file);
                var result = await JsonSerializer.DeserializeAsync<DocumentQualityRunResult>(stream, Json, cancellationToken);
                if (result != null && string.Equals(result.ProjectIdAcc, projectIdAcc, StringComparison.OrdinalIgnoreCase)
                    && (latest == null || result.FinishedAtUtc > latest.FinishedAtUtc))
                    latest = result;
            }
            catch (JsonException) { /* arquivo corrompido: ignora, nunca apaga */ }
        }
        return latest;
    }

    public string? GetPath(string projectIdAcc, Guid executionId)
    {
        var path = PathFor(projectIdAcc, executionId);
        return File.Exists(path) ? path : null;
    }

    private string PathFor(string projectIdAcc, Guid executionId)
    {
        var projectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectIdAcc))).ToLowerInvariant()[..16];
        return Path.Combine(Path.GetFullPath(rootDirectory), projectHash, $"Auditoria_Qualidade_Documentos_{executionId:N}.csv");
    }

    private static void Append(StringBuilder sb, params string?[] fields) =>
        sb.AppendLine(string.Join(',', fields.Select(Escape)));

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '\"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }
}
