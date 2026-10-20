using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Leitura do contrato oficial da Etapa 15 e de seu destino SQL, sem escrita.</summary>
    public interface IAuditPlanningReadOnlyAdapter
    {
        AuditWorksheetSnapshot LerPrimeiraAba(Stream conteudo, int linhaDeCabecalho);

        AuditWorksheetSnapshot LerAba(Stream conteudo, string nomeAba, int linhaDeCabecalho);
        AuditWorkbookSnapshot LerAbas(Stream conteudo, int linhaDeCabecalho);
        Task<string?> ObterIdiomaProjetoAsync(string projectId, CancellationToken ct = default);
        Task<IReadOnlyDictionary<string, string>> ObterSchemaWeeklyAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> ObterColunasDestinoSqlAsync(string nomeTabela, CancellationToken ct = default);
    }
}
