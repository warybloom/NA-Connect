using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <summary>
    /// Único ponto de leitura de dados operacionais para a auditoria — sempre via
    /// <c>ConnectionStrings:AuditSourceConnection</c> (nunca <c>LocalConnection</c>). Toda regra
    /// (Checkpoint 2+) que precisar consultar tabelas/views operacionais deve passar por aqui, nunca abrir
    /// uma <see cref="Microsoft.Data.SqlClient.SqlConnection"/> própria.
    /// </summary>
    public interface IAuditSourceReader
    {
        /// <summary>
        /// Executa um SELECT e devolve o resultado como <see cref="DataTable"/>. Lança
        /// <see cref="Safety.ForbiddenOperationException"/> imediatamente (antes de abrir qualquer conexão)
        /// se <paramref name="sql"/> não for reconhecido como somente-leitura por
        /// <see cref="Safety.SqlWriteVerbGuard"/>.
        /// </summary>
        Task<DataTable> ExecutarConsultaAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken cancellationToken = default);
    }
}
