using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Persistence;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Implementação real — repassa para <see cref="IAuditSourceReader"/> (Checkpoint 1: guard + conexão isolada já resolvidos ali), nunca abre uma segunda conexão própria.</summary>
    public sealed class AuditDatabaseReadOnlyAdapter : IAuditDatabaseReadOnlyAdapter
    {
        private readonly IAuditSourceReader _leitor;

        public AuditDatabaseReadOnlyAdapter(IAuditSourceReader leitor)
        {
            _leitor = leitor;
        }

        public Task<DataTable> ConsultarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default) =>
            _leitor.ExecutarConsultaAsync(sql, parametros, ct);
    }
}
