using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>
    /// Acesso somente-leitura ao banco operacional (seção 5 do Checkpoint 2) — todo comando passa por
    /// <see cref="Safety.SqlWriteVerbGuard"/> (via <see cref="Persistence.IAuditSourceReader"/>, já
    /// construído no Checkpoint 1) antes de qualquer conexão abrir. Nunca usa <c>DatabaseService</c> —
    /// conexão própria, sempre <c>AuditSourceConnection</c>.
    /// </summary>
    public interface IAuditDatabaseReadOnlyAdapter
    {
        Task<DataTable> ConsultarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default);
    }
}
