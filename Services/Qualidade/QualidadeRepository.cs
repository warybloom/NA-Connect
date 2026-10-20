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

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Implementação da persistência transacional de UMA tabela de Qualidade (seção 12 do pedido). Ordem
    /// sempre: 1) sem linhas válidas -&gt; só consulta quantidade anterior, NUNCA deleta; 2) valida que as
    /// colunas do DataTable existem na tabela real (antes de abrir transação); 3) conta registros
    /// anteriores; 4) DELETE por Project_ID_ACC; 5) SqlBulkCopy nomeado; 6) COMMIT. Qualquer exceção
    /// provoca rollback só desta tabela — nunca desfaz outra tabela que já teve commit.
    /// </summary>
    public sealed class QualidadeRepository : IQualidadeRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<QualidadeRepository> _logger;

        public QualidadeRepository(IConfiguration configuration, ILogger<QualidadeRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        public async Task<ResultadoTabelaQualidade> PersistirTabelaAsync(
            string projectId,
            ResultadoTabelaQualidade preparado,
            CancellationToken cancellationToken = default)
        {
            if (preparado == null || !preparado.Sucesso)
            {
                throw new InvalidOperationException(
                    "QualidadeRepository.PersistirTabelaAsync chamado com preparado nulo ou Sucesso=false — a persistência nunca deve ser tentada nesse estado.");
            }

            if (preparado.Tabela == null || preparado.LinhasValidas == 0)
            {
                using var connectionSomenteLeitura = new SqlConnection(_connectionString);
                await connectionSomenteLeitura.OpenAsync(cancellationToken);
                preparado.RegistrosAnteriores = await ContarPorProjetoAsync(connectionSomenteLeitura, null, preparado.TabelaDestino, projectId, cancellationToken);

                _logger.LogWarning(
                    "[QUALIDADE][BANCO] Aba={Aba} | Tabela={Tabela} | {CodigoResultado} | RegistrosAnteriores={RegistrosAnteriores} — nenhum DELETE executado, dados anteriores preservados.",
                    preparado.Aba, preparado.TabelaDestino, preparado.CodigoResultado, preparado.RegistrosAnteriores);

                return preparado;
            }

            var cronometro = Stopwatch.StartNew();
            string nomeTabelaSemSchema = ObterNomeTabelaSemSchema(preparado.TabelaDestino);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var colunasEsperadas = preparado.Tabela.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var colunasAusentesNoSql = await ValidarSchemaAsync(connection, nomeTabelaSemSchema, colunasEsperadas, cancellationToken);

            if (colunasAusentesNoSql.Count > 0)
            {
                preparado.Sucesso = false;
                preparado.SucessoComAlertas = false;
                preparado.CodigoResultado = QualidadeCodigosResultado.ErroSql;
                preparado.Mensagem = $"A tabela {preparado.TabelaDestino} não possui a(s) coluna(s) [{string.Join(", ", colunasAusentesNoSql)}]. Nenhuma alteração foi feita.";
                preparado.Erros.Add(preparado.Mensagem);

                _logger.LogError(
                    "[QUALIDADE][BANCO][ERRO] Aba={Aba} | Tabela={Tabela} | Motivo=SCHEMA_SQL_INCOMPATIVEL | ColunasAusentes=[{Colunas}]",
                    preparado.Aba, preparado.TabelaDestino, string.Join(", ", colunasAusentesNoSql));

                return preparado;
            }

            using var transaction = connection.BeginTransaction();

            try
            {
                preparado.RegistrosAnteriores = await ContarPorProjetoAsync(connection, transaction, preparado.TabelaDestino, projectId, cancellationToken);

                using (var cmdDelete = new SqlCommand($"DELETE FROM {preparado.TabelaDestino} WHERE Project_ID_ACC = @pid", connection, transaction))
                {
                    cmdDelete.Parameters.Add("@pid", SqlDbType.NVarChar).Value = projectId;
                    int removidos = await cmdDelete.ExecuteNonQueryAsync(cancellationToken);

                    _logger.LogInformation(
                        "[QUALIDADE][BANCO][DELETE] Aba={Aba} | Tabela={Tabela} | RegistrosRemovidos={Removidos}",
                        preparado.Aba, preparado.TabelaDestino, removidos);
                }

                using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
                {
                    DestinationTableName = preparado.TabelaDestino,
                    BulkCopyTimeout = 300,
                    BatchSize = 1000
                })
                {
                    // Mapeamento explícito NOMEADO — nunca por posição/ordinal.
                    foreach (DataColumn coluna in preparado.Tabela.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(coluna.ColumnName, coluna.ColumnName);
                    }

                    await bulkCopy.WriteToServerAsync(preparado.Tabela, cancellationToken);
                }

                preparado.LinhasInseridas = preparado.Tabela.Rows.Count;

                if (preparado.LinhasInseridas != preparado.LinhasValidas)
                {
                    throw new InvalidOperationException(
                        "Erro de consistência: quantidade inserida difere da quantidade preparada no preflight.");
                }

                transaction.Commit();
                preparado.CommitExecutado = true;
                cronometro.Stop();

                _logger.LogInformation(
                    "[QUALIDADE][SNAPSHOT] Aba={Aba} | Tabela={Tabela} | RegistrosAnteriores={Anteriores} | LinhasInseridas={Inseridas} | " +
                    "CamposPreenchidosAutomaticamente={CamposPreenchidos} | DuracaoMs={DuracaoMs} | CommitExecutado=true",
                    preparado.Aba, preparado.TabelaDestino, preparado.RegistrosAnteriores, preparado.LinhasInseridas,
                    preparado.CamposPreenchidosAutomaticamente, cronometro.Elapsed.TotalMilliseconds);

                return preparado;
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                    preparado.RollbackExecutado = true;
                }
                catch (Exception exRollback)
                {
                    _logger.LogError(exRollback, "[QUALIDADE][BANCO][ROLLBACK] Falha adicional ao executar o rollback. Aba={Aba}", preparado.Aba);
                }

                preparado.Sucesso = false;
                preparado.SucessoComAlertas = false;
                preparado.CodigoResultado = QualidadeCodigosResultado.ErroSql;
                preparado.Mensagem = ex.Message;
                preparado.Erros.Add(ex.Message);
                cronometro.Stop();

                _logger.LogError(
                    ex,
                    "[QUALIDADE][BANCO][ROLLBACK] Aba={Aba} | Tabela={Tabela} | RegistrosAnteriores={Anteriores} | DuracaoMs={DuracaoMs} | RollbackExecutado={RollbackExecutado}",
                    preparado.Aba, preparado.TabelaDestino, preparado.RegistrosAnteriores, cronometro.Elapsed.TotalMilliseconds,
                    preparado.RollbackExecutado ? "sim" : "não");

                return preparado;
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

            using (var cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName", connection))
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
    }
}
