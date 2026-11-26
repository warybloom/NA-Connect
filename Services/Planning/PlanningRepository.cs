using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Planning
{
    /// <summary>
    /// Implementação da persistência transacional de uma tabela de Planning Control (seções 17/19 do
    /// pedido). Ordem sempre: 1) validar que todas as colunas SQL esperadas existem na tabela real (sem
    /// abrir transação — se faltar alguma, PREFLIGHT_COLUNA_OBRIGATORIA_AUSENTE e nenhum DELETE), 2) contar
    /// registros anteriores, 3) DELETE por Project_ID_ACC, 4) SqlBulkCopy com ColumnMappings NOMEADO —
    /// tudo dentro da MESMA SqlTransaction. Qualquer exceção provoca rollback único.
    /// </summary>
    public sealed class PlanningRepository : IPlanningRepository
    {
        public const string CodigoColunaSqlAusente = "PREFLIGHT_COLUNA_OBRIGATORIA_AUSENTE";

        private readonly string _connectionString;
        private readonly ILogger<PlanningRepository> _logger;

        public PlanningRepository(IConfiguration configuration, ILogger<PlanningRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        public async Task<ResultadoPersistenciaPlanejamento> PersistirAsync(
            string projectId,
            PlanningSheetDefinition definicao,
            ResultadoPreflightPlanejamento preflight,
            CancellationToken cancellationToken = default)
        {
            if (preflight == null || !preflight.Sucesso || preflight.Tabela == null)
            {
                throw new InvalidOperationException(
                    "PlanningRepository.PersistirAsync chamado com preflight nulo, Sucesso=false ou sem tabela preparada — a persistência nunca deve ser tentada nesse estado.");
            }

            if (preflight.Tabela.Rows.Count == 0)
                return new ResultadoPersistenciaPlanejamento { Sucesso = true };

            var resultado = new ResultadoPersistenciaPlanejamento();
            var cronometro = Stopwatch.StartNew();

            string nomeTabelaSemSchema = ObterNomeTabelaSemSchema(definicao.DestinationTable);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Valida TODAS as colunas do DataTable preparado — incluindo Project_ID_ACC e
            // Data_Sincronizacao, não só os campos de negócio. Id_Registro nunca entra aqui porque nunca
            // é adicionado ao DataTable (é IDENTITY nas quatro tabelas — mesma convenção já usada pelo
            // código legado e por QualidadeRepository: nunca mapeado, nunca inserido, gerado pelo SQL).
            var colunasEsperadas = preflight.Tabela.Columns
                .Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .ToList();

            var colunasAusentesNoSql = await ValidarSchemaAsync(connection, nomeTabelaSemSchema, colunasEsperadas, cancellationToken);

            if (colunasAusentesNoSql.Count > 0)
            {
                resultado.Sucesso = false;
                resultado.Erro =
                    $"{CodigoColunaSqlAusente}: a tabela {definicao.DestinationTable} não possui a(s) coluna(s) [{string.Join(", ", colunasAusentesNoSql)}]. Nenhuma alteração foi feita.";

                _logger.LogError(
                    "[PLANNING_SYNC][PREFLIGHT][ERRO] Processo={Processo} | Motivo={Motivo} | Tabela={Tabela} | ColunasAusentes=[{Colunas}]",
                    definicao.ProcessName, CodigoColunaSqlAusente, definicao.DestinationTable, string.Join(", ", colunasAusentesNoSql));

                return resultado;
            }

            using var transaction = connection.BeginTransaction();

            try
            {
                resultado.RegistrosAnteriores = await ContarPorProjetoAsync(connection, transaction, definicao.DestinationTable, projectId, cancellationToken);

                await DeletarPorProjetoAsync(connection, transaction, definicao, projectId, cancellationToken);

                resultado.LinhasInseridas = await BulkCopyAsync(connection, transaction, definicao, preflight.Tabela, cancellationToken);

                if (resultado.LinhasInseridas != preflight.Tabela.Rows.Count)
                {
                    throw new InvalidOperationException(
                        "Erro de consistência: quantidade inserida difere da quantidade preparada no preflight.");
                }

                transaction.Commit();
                resultado.Sucesso = true;
                resultado.CommitExecutado = true;
                cronometro.Stop();

                _logger.LogInformation(
                    "[PLANNING_SYNC][SNAPSHOT] Processo={Processo} | Project_ID_ACC={ProjectIdAcc} | Tabela={Tabela} | RegistrosAnteriores={Anteriores} | " +
                    "LinhasInseridas={Inseridas} | DuracaoMs={DuracaoMs}",
                    definicao.ProcessName, projectId, definicao.DestinationTable, resultado.RegistrosAnteriores, resultado.LinhasInseridas, cronometro.Elapsed.TotalMilliseconds);

                return resultado;
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                    resultado.RollbackExecutado = true;
                }
                catch (Exception exRollback)
                {
                    _logger.LogError(exRollback, "[PLANNING_SYNC][SNAPSHOT] Falha adicional ao executar o rollback. Processo={Processo}", definicao.ProcessName);
                }

                resultado.Sucesso = false;
                resultado.Erro = ex.Message;
                cronometro.Stop();

                _logger.LogError(
                    ex,
                    "[PLANNING_SYNC][SNAPSHOT] Rollback | Processo={Processo} | Project_ID_ACC={ProjectIdAcc} | Tabela={Tabela} | RegistrosAnteriores={Anteriores} | " +
                    "DuracaoMs={DuracaoMs} | RollbackExecutado={RollbackExecutado}",
                    definicao.ProcessName, projectId, definicao.DestinationTable, resultado.RegistrosAnteriores, cronometro.Elapsed.TotalMilliseconds,
                    resultado.RollbackExecutado ? "sim" : "não");

                return resultado;
            }
        }

        private static string ObterNomeTabelaSemSchema(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return string.Empty;
            return tableName.Replace("[", string.Empty).Replace("]", string.Empty).Split('.').Last().Trim();
        }

        private static async Task<List<string>> ValidarSchemaAsync(SqlConnection connection, string nomeTabelaSemSchema, List<string> colunasEsperadas, CancellationToken cancellationToken)
        {
            var colunasReais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @tableName", connection))
            {
                cmd.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = nomeTabelaSemSchema;

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    colunasReais.Add(reader.GetString(0));
                }
            }

            return colunasEsperadas.Where(c => !colunasReais.Contains(c)).ToList();
        }

        private static async Task<int> ContarPorProjetoAsync(SqlConnection connection, SqlTransaction transaction, string tabela, string projectId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {tabela} WHERE Project_ID_ACC = @pid", connection, transaction);
            cmd.Parameters.Add("@pid", SqlDbType.NVarChar).Value = projectId;
            return (int)await cmd.ExecuteScalarAsync(cancellationToken);
        }

        private async Task DeletarPorProjetoAsync(SqlConnection connection, SqlTransaction transaction, PlanningSheetDefinition definicao, string projectId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand($"DELETE FROM {definicao.DestinationTable} WHERE Project_ID_ACC = @pid", connection, transaction);
            cmd.Parameters.Add("@pid", SqlDbType.NVarChar).Value = projectId;
            int removidos = await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation(
                "[PLANNING_SYNC][SNAPSHOT] DELETE | Processo={Processo} | Tabela={Tabela} | RegistrosRemovidos={Removidos}",
                definicao.ProcessName, definicao.DestinationTable, removidos);
        }

        private async Task<int> BulkCopyAsync(SqlConnection connection, SqlTransaction transaction, PlanningSheetDefinition definicao, DataTable dt, CancellationToken cancellationToken)
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepNulls, transaction)
            {
                DestinationTableName = definicao.DestinationTable,
                BulkCopyTimeout = 300,
                BatchSize = 1000
            };

            // Mapeamento explícito NOMEADO — nunca por posição/ordinal (seção 17).
            foreach (DataColumn coluna in dt.Columns)
            {
                bulkCopy.ColumnMappings.Add(coluna.ColumnName, coluna.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(dt, cancellationToken);

            _logger.LogInformation(
                "[PLANNING_SYNC][SNAPSHOT] BULK_COPY | Processo={Processo} | Tabela={Tabela} | RegistrosInseridos={Inseridos}",
                definicao.ProcessName, definicao.DestinationTable, dt.Rows.Count);

            return dt.Rows.Count;
        }
    }
}
