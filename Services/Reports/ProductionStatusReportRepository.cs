namespace APS.WebApp.Services.Reports;

public sealed record MapaCalorDocumento(long IdRegistro, string? Codigo, string? Titulo, string? Disciplina,
    string? StatusInterno, string? StatusCliente, string? SituacaoEmissao, decimal? AvancoFisico, string ClasseFisica,
    bool Cancelado = false, string? CorrelacaoCronograma = null, string StatusDocumento = "SEM_PRAZO")
{
    public string? DisciplinaCanonica { get; init; }
}
public sealed record ProductionStatusReport(IReadOnlyList<MapaCalorDocumento> Documentos);
public interface IProductionStatusReportRepository
{
    Task<ProductionStatusReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}
public sealed class ProductionStatusReportRepository(IConfiguration configuration) : IProductionStatusReportRepository
{
    public async Task<ProductionStatusReport> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        // Mesmo universo/leitura da Gestão à Vista (currentPlanningSnapshot: true): Total e classificação idênticos entre as abas.
        var report = await PmoExecutiveData.ReadAsync(configuration, projectId, false, cancellationToken, currentPlanningSnapshot: true);
        return new(report.Documentos.Where(d => d.IsDocument).Select(d => new MapaCalorDocumento(long.Parse(d.RowId.Split(':')[^1]), d.Codigo,
            d.Titulo, d.Disciplina, d.StatusInterno, d.StatusCliente, d.SituacaoEmissao, d.AvancoFisico,
            d.ClasseFisica, d.Cancelado, d.CorrelacaoCronograma, d.StatusDocumento)
            { DisciplinaCanonica = d.DisciplinaCanonica }).ToArray());
    }
}
