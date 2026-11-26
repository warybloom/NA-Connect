using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Planning
{
    /// <summary>
    /// Persiste, numa ÚNICA SqlConnection/SqlTransaction, UMA tabela de Planning Control a partir de um
    /// ResultadoPreflightPlanejamento já validado (Sucesso=true). Valida o schema SQL real ANTES de abrir
    /// a transação (seção 17/19 do pedido) — se alguma coluna esperada não existir na tabela, nunca
    /// executa DELETE.
    /// </summary>
    public interface IPlanningRepository
    {
        Task<ResultadoPersistenciaPlanejamento> PersistirAsync(
            string projectId,
            PlanningSheetDefinition definicao,
            ResultadoPreflightPlanejamento preflight,
            CancellationToken cancellationToken = default);
    }
}
