using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Safety;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <inheritdoc cref="IAuditSourceReader"/>
    public sealed class AuditSourceReader : IAuditSourceReader
    {
        private readonly string _connectionString;
        private readonly ILogger<AuditSourceReader> _logger;

        public AuditSourceReader(IConfiguration configuration, OperationalAuditOptions options, ILogger<AuditSourceReader> logger)
        {
            _connectionString = AuditConnectionStringResolver.Resolver(
                configuration, options.SourceConnectionStringName, "leitura de dados operacionais pela auditoria");
            _logger = logger;
        }

        public async Task<DataTable> ExecutarConsultaAsync(
            string sql,
            IReadOnlyDictionary<string, object?>? parametros = null,
            CancellationToken cancellationToken = default)
        {
            // Primeira barreira: em código, antes de qualquer conexão de rede ser aberta (seção 4/18).
            SqlWriteVerbGuard.GarantirSomenteLeitura(sql);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;

            if (parametros != null)
            {
                foreach (var kv in parametros)
                {
                    command.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
                }
            }

            var tabela = new DataTable();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            tabela.Load(reader);

            _logger.LogDebug("[AUDITORIA][SQL][LEITURA] Linhas={Linhas}", tabela.Rows.Count);
            return tabela;
        }
    }
}
