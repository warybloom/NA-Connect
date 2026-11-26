using APS.WebApp.Services.Reports;

namespace APS.WebApp.Services.Documents;

/// <summary>Resultado da Etapa 4B (contagens únicas por documento; diagnóstico separado de APPROVED × Emission_Date).</summary>
public sealed record DocumentEmissionAudit(int Documentos, int Cancelados, int Emitidos, int Approved, int ComEmissionDate,
    int ApprovedSemEmissionDate, int NaoEmitidos, int NoPrazo, int Atrasados, int SemPrazo)
{
    public string Resumo() =>
        $"Documentos={Documentos} | Cancelados={Cancelados} | Emitidos={Emitidos} | Approved={Approved} | ComEmissionDate={ComEmissionDate} | " +
        $"ApprovedSemEmissionDate={ApprovedSemEmissionDate} | NaoEmitidos={NaoEmitidos} | NoPrazo={NoPrazo} | Atrasados={Atrasados} | SemPrazo={SemPrazo}";
}

public interface IDocumentEmissionAuditService
{
    Task<DocumentEmissionAudit> AuditAsync(string projectId, CancellationToken ct);
}

/// <summary>
/// ETAPA 4B — CLASSIFICAÇÃO DE EMISSÃO: lê a LDP atual (mesma carga/snapshot dos relatórios via PmoExecutiveData) e classifica cada
/// documento pela regra canônica <see cref="DocumentEmissionRules"/>. SOMENTE LEITURA: não escreve no banco, no Excel nem no ACC.
/// Independente da Etapa 04 (usa a LDP já disponível no banco).
/// </summary>
public sealed class DocumentEmissionAuditService(IConfiguration configuration) : IDocumentEmissionAuditService
{
    public async Task<DocumentEmissionAudit> AuditAsync(string projectId, CancellationToken ct)
    {
        var report = await PmoExecutiveData.ReadAsync(configuration, projectId, false, ct, currentPlanningSnapshot: true);
        return Contar(report.Documentos);
    }

    /// <summary>Contagem a partir dos documentos já classificados (StatusDocumento = partição exclusiva da Gestão à Vista).</summary>
    public static DocumentEmissionAudit Contar(IReadOnlyList<PmoDocumento> docs)
    {
        // No projeto principal, AT é atividade de esforço/custo e não pertence ao universo documental.
        var documentos = docs.Where(d => d.IsDocument).ToArray();
        int Status(string s) => documentos.Count(d => d.StatusDocumento == s);
        var naoCancelados = documentos
            .Where(d => !DocumentEmissionRules.IsCancelled(d.StatusCliente, d.SituacaoEmissao, d.StatusInterno))
            .ToArray();

        return new(documentos.Length, Status("CANCELADO"), Status("EMITIDO"),
            naoCancelados.Count(d => DocumentEmissionRules.IsApproved(d.StatusCliente)),
            naoCancelados.Count(d => d.DataEmissao.HasValue),
            naoCancelados.Count(d => DocumentEmissionRules.IsApproved(d.StatusCliente) && !d.DataEmissao.HasValue),
            Status("NO_PRAZO") + Status("ATRASADO") + Status("SEM_PRAZO"),
            Status("NO_PRAZO"), Status("ATRASADO"), Status("SEM_PRAZO"));
    }
}
