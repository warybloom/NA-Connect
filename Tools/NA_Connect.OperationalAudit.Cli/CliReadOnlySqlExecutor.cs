using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Safety;
using Microsoft.Data.SqlClient;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Executor SELECT-only genérico do CLI, reaproveitado por <see cref="SchemaReadOnlyValidator"/>,
    /// <see cref="PermissionReadOnlyValidator"/> e <see cref="ConnectionValidator"/> — nunca duplica a
    /// lógica de guarda: usa <see cref="SqlWriteVerbGuard"/> (mesma classe real da auditoria) ANTES de
    /// abrir qualquer conexão, exatamente como <c>AuditSourceReader</c> faz. Diferente de
    /// <c>AuditSourceReader</c>/<c>AuditRepository</c> só porque aceita a connection string diretamente
    /// (o CLI precisa consultar as DUAS conexões — origem E repositório — para diagnóstico; os tipos
    /// operacionais só conhecem a sua própria).
    /// </summary>
    /// <summary>Abstração mínima para permitir testar <see cref="SchemaReadOnlyValidator"/>/<see cref="PermissionReadOnlyValidator"/> com fakes, sem SQL Server real.</summary>
    public interface ICliSqlQueryExecutor
    {
        Task<DataTable> ExecutarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default);
    }

    public sealed class CliReadOnlySqlExecutor : ICliSqlQueryExecutor
    {
        private readonly string _connectionString;

        public CliReadOnlySqlExecutor(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<DataTable> ExecutarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default)
        {
            SqlWriteVerbGuard.GarantirSomenteLeitura(sql);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;

            if (parametros != null)
            {
                foreach (var kv in parametros)
                {
                    command.Parameters.AddWithValue(kv.Key, kv.Value ?? System.DBNull.Value);
                }
            }

            var tabela = new DataTable();
            await using var reader = await command.ExecuteReaderAsync(ct);
            tabela.Load(reader);
            return tabela;
        }
    }
}
