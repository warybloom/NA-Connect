using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Implementação real de IGestaoVistaTimesheetSnapshotRepository. Nunca executa DDL — a migration
    /// manual (Migrations/GestaoVista/08_criar_tabela_timesheet_snapshot_atual.sql) é sempre aplicada à
    /// parte, nunca por este código. Tolerância de validação: 0.01 (arredondamento decimal apenas).
    /// </summary>
    public sealed class GestaoVistaTimesheetSnapshotRepository : IGestaoVistaTimesheetSnapshotRepository
    {
        private const decimal Tolerancia = 0.01m;
        private readonly string _connectionString;
        private readonly ILogger<GestaoVistaTimesheetSnapshotRepository> _logger;

        public GestaoVistaTimesheetSnapshotRepository(IConfiguration configuration, ILogger<GestaoVistaTimesheetSnapshotRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        public async Task<bool> TabelaDisponivelAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                const string sql = @"
                    SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'TimesheetSnapshotAtual'
                    ) THEN 1 ELSE 0 END;";
                using var cmd = new SqlCommand(sql, connection);
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(resultado) == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ETAPA 03][TIMESHEET] Falha ao checar disponibilidade de gestao.TimesheetSnapshotAtual.");
                return false;
            }
        }

        public async Task<(int Linhas, decimal HH)> ObterResumoAtualAsync(string projectIdAcc, CancellationToken cancellationToken = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = @"
                SELECT COUNT(*), ISNULL(SUM(HorasTrabalhadas), 0)
                FROM gestao.TimesheetSnapshotAtual WHERE Project_ID_ACC = @Projeto;";
            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return (0, 0m);
            return (Convert.ToInt32(reader[0]), Convert.ToDecimal(reader[1]));
        }

        public async Task<decimal?> ObterCustoHoraVigenteAsync(
            string projectIdAcc, string nomeProfissional, DateTime dataReferencia, CancellationToken cancellationToken = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = @"
                SELECT TOP 1 CustoHora
                FROM gestao.CustoProfissionalRealHistorico
                WHERE Project_ID_ACC = @Projeto AND NomeProfissional = @Nome
                  AND VigenciaInicio <= @DataRef
                  AND (VigenciaFim IS NULL OR VigenciaFim > @DataRef)
                ORDER BY VigenciaInicio DESC;";
            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            cmd.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = (object)nomeProfissional ?? DBNull.Value;
            cmd.Parameters.Add("@DataRef", SqlDbType.DateTime2).Value = dataReferencia;

            var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDecimal(resultado);
        }

        public async Task<GestaoVistaTimesheetSnapshotCommitResultado> SubstituirSnapshotAsync(
            string projectIdAcc, IReadOnlyList<GestaoVistaTimesheetSnapshotLinha> linhas, decimal hhEsperadoExcel,
            DateTime dataSincronizacao, CancellationToken cancellationToken = default)
        {
            var resultado = new GestaoVistaTimesheetSnapshotCommitResultado();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                var antes = await ObterResumoInternoAsync(connection, null, projectIdAcc, cancellationToken);
                resultado.LinhasAntes = antes.Linhas;
                resultado.HHAntes = antes.HH;

                using var transaction = connection.BeginTransaction();
                try
                {
                    const string sqlRemover = "DELETE FROM gestao.TimesheetSnapshotAtual WHERE Project_ID_ACC = @Projeto;";
                    using (var cmdRemover = new SqlCommand(sqlRemover, connection, transaction))
                    {
                        cmdRemover.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                        resultado.Removidos = await cmdRemover.ExecuteNonQueryAsync(cancellationToken);
                    }

                    const string sqlInserir = @"
                        INSERT INTO gestao.TimesheetSnapshotAtual
                            (Project_ID_ACC, NomeProfissional, HorasPlanejadas, HorasTrabalhadas, Projeto, Funcao, Disciplina,
                             DocumentoCodigo, Documento, Descricao, Doc1, Doc2, Doc3, Doc4, Doc5, DataLancamento, CustoCalculado,
                             Data_Sincronizacao)
                        VALUES
                            (@Projeto, @Nome, @HorasPlanejadas, @HorasTrabalhadas, @ProjetoPlanilha, @Funcao, @Disciplina,
                             @DocumentoCodigo, @Documento, @Descricao, @Doc1, @Doc2, @Doc3, @Doc4, @Doc5, @DataLancamento, @CustoCalculado,
                             @DataSincronizacao);";

                    decimal somaInserida = 0m;
                    foreach (var linha in linhas)
                    {
                        using var cmdInserir = new SqlCommand(sqlInserir, connection, transaction);
                        cmdInserir.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                        cmdInserir.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = (object)linha.NomeProfissional ?? DBNull.Value;
                        AdicionarDecimal(cmdInserir, "@HorasPlanejadas", linha.HorasPlanejadas);
                        AdicionarDecimal(cmdInserir, "@HorasTrabalhadas", linha.HorasTrabalhadas);
                        cmdInserir.Parameters.Add("@ProjetoPlanilha", SqlDbType.NVarChar, 300).Value = (object)linha.Projeto ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Funcao", SqlDbType.NVarChar, 200).Value = (object)linha.Funcao ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Disciplina", SqlDbType.NVarChar, 200).Value = (object)linha.Disciplina ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@DocumentoCodigo", SqlDbType.NVarChar, 300).Value = (object)linha.DocumentoCodigo ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Documento", SqlDbType.NVarChar, 500).Value = (object)linha.Documento ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Descricao", SqlDbType.NVarChar, 1000).Value = (object)linha.Descricao ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Doc1", SqlDbType.NVarChar, 200).Value = (object)linha.Doc1 ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Doc2", SqlDbType.NVarChar, 200).Value = (object)linha.Doc2 ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Doc3", SqlDbType.NVarChar, 200).Value = (object)linha.Doc3 ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Doc4", SqlDbType.NVarChar, 200).Value = (object)linha.Doc4 ?? DBNull.Value;
                        cmdInserir.Parameters.Add("@Doc5", SqlDbType.NVarChar, 200).Value = (object)linha.Doc5 ?? DBNull.Value;
                        // Nunca inventa DataLancamento — DBNull quando a fonte não tinha data.
                        cmdInserir.Parameters.Add("@DataLancamento", SqlDbType.Date).Value = (object)linha.DataLancamento ?? DBNull.Value;
                        AdicionarDecimal(cmdInserir, "@CustoCalculado", linha.CustoCalculado);
                        cmdInserir.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = dataSincronizacao;

                        await cmdInserir.ExecuteNonQueryAsync(cancellationToken);
                        somaInserida += linha.HorasTrabalhadas ?? 0m;
                        resultado.LinhasInseridas++;
                    }

                    decimal diferenca = Math.Abs(hhEsperadoExcel - somaInserida);
                    resultado.DiferencaExcelBanco = diferenca;

                    if (!DentroDaTolerancia(hhEsperadoExcel, somaInserida))
                    {
                        transaction.Rollback();
                        resultado.Sucesso = false;
                        resultado.Motivo = "DIVERGENCIA_EXCEL_BANCO";
                        resultado.Erros.Add(
                            $"HH do Excel ({hhEsperadoExcel}) diverge do HH que seria inserido ({somaInserida}) além da tolerância ({Tolerancia}) — ROLLBACK, snapshot anterior preservado.");
                        _logger.LogError(
                            "[ETAPA 03][TIMESHEET] ROLLBACK | Projeto={Projeto} | HHExcel={HHExcel} | HHCandidato={HHCandidato} | Diferenca={Diferenca}",
                            projectIdAcc, hhEsperadoExcel, somaInserida, diferenca);
                        return resultado;
                    }

                    transaction.Commit();
                }
                catch (Exception exTransacao)
                {
                    try { transaction.Rollback(); }
                    catch (Exception exRollback)
                    {
                        _logger.LogError(exRollback, "[ETAPA 03][TIMESHEET] Falha adicional ao executar o rollback.");
                    }

                    _logger.LogError(exTransacao, "[ETAPA 03][TIMESHEET] ROLLBACK | Projeto={Projeto} — falha durante a substituição do snapshot.", projectIdAcc);
                    resultado.Sucesso = false;
                    resultado.Motivo = "FALHA_TRANSACAO";
                    resultado.Erros.Add(exTransacao.Message);
                    return resultado;
                }

                var depois = await ObterResumoInternoAsync(connection, null, projectIdAcc, cancellationToken);
                resultado.LinhasDepois = depois.Linhas;
                resultado.HHDepois = depois.HH;
                resultado.CustoDepois = depois.Custo;
                resultado.Sucesso = true;
                resultado.Motivo = "OK";
                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ETAPA 03][TIMESHEET] Falha inesperada ao abrir conexão/substituir snapshot | Projeto={Projeto}.", projectIdAcc);
                resultado.Sucesso = false;
                resultado.Motivo = "FALHA_CONEXAO";
                resultado.Erros.Add(ex.Message);
                return resultado;
            }
        }

        private static async Task<(int Linhas, decimal HH, decimal Custo)> ObterResumoInternoAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc, CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT COUNT(*), ISNULL(SUM(HorasTrabalhadas), 0), ISNULL(SUM(CustoCalculado), 0)
                FROM gestao.TimesheetSnapshotAtual WHERE Project_ID_ACC = @Projeto;";
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return (0, 0m, 0m);
            return (Convert.ToInt32(reader[0]), Convert.ToDecimal(reader[1]), Convert.ToDecimal(reader[2]));
        }

        /// <summary>Regra pura de validação (seção "VALIDAÇÃO OBRIGATÓRIA" do pedido): ABS(hhExcel -
        /// hhBanco) &lt;= 0.01 — tolerância só de arredondamento decimal, nunca de negócio. Extraída como
        /// função pura para ser testável isoladamente, sem SQL Server.</summary>
        internal static bool DentroDaTolerancia(decimal hhExcel, decimal hhBanco, decimal tolerancia = Tolerancia) =>
            Math.Abs(hhExcel - hhBanco) <= tolerancia;

        private static void AdicionarDecimal(SqlCommand cmd, string nome, decimal? valor)
        {
            var p = cmd.Parameters.Add(nome, SqlDbType.Decimal);
            p.Precision = 18;
            p.Scale = 6;
            p.Value = (object)valor ?? DBNull.Value;
        }
    }
}
