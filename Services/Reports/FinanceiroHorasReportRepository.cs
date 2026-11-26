namespace APS.WebApp.Services.Reports;

public interface IFinanceiroHorasReportRepository
{
    Task<PmoExecutiveReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>LDP planejada, Cronograma, Weekly e Timesheet; leitura ap?s autoriza??o por projeto.</summary>
public sealed class FinanceiroHorasReportRepository(IConfiguration configuration) : IFinanceiroHorasReportRepository
{
    public Task<PmoExecutiveReport> ReadAsync(string projectId, CancellationToken cancellationToken) =>
        PmoExecutiveData.ReadAsync(configuration, projectId, true, cancellationToken);
}
