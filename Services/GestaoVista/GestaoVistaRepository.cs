using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Implementação real de IGestaoVistaRepository. Nunca executa DDL (CREATE SCHEMA/TABLE) — as
    /// migrations 01/05/06/07 são sempre manuais. RowHash é calculado aqui (não no extrator), sempre com
    /// GestaoVistaRowHasher e os campos documentados em cada método — mudar esses campos muda o
    /// comportamento de idempotência, então nunca fazer isso sem atualizar os testes correspondentes.
    /// </summary>
    public sealed class GestaoVistaRepository(ILogger<GestaoVistaRepository> logger) : IGestaoVistaRepository
    {
        public async Task<bool> SchemaDisponivelAsync(SqlConnection connection, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT
                    DB_NAME() AS Banco,
                    CASE WHEN EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao') THEN 1 ELSE 0 END AS SchemaExiste,
                    CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'TimesheetLancamento') THEN 1 ELSE 0 END AS TimesheetExiste,
                    CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'CustoProfissionalRealHistorico') THEN 1 ELSE 0 END AS CustoRealExiste,
                    CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'CustoProfissionalPrevisto') THEN 1 ELSE 0 END AS CustoPrevistoExiste;";

            using var cmd = new SqlCommand(sql, connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return false;

            var banco = reader["Banco"] as string ?? "?";
            var schemaExiste = Convert.ToInt32(reader["SchemaExiste"]) == 1;
            var timesheetExiste = Convert.ToInt32(reader["TimesheetExiste"]) == 1;
            var custoRealExiste = Convert.ToInt32(reader["CustoRealExiste"]) == 1;
            var custoPrevistoExiste = Convert.ToInt32(reader["CustoPrevistoExiste"]) == 1;
            // CustoTimesheetInformado (coluna) NÃO é mais exigida aqui: essa coluna não existe fisicamente em
            // gestao.TimesheetLancamento (migration manual 18 nunca foi criada) e PersistirTimesheetAsync
            // parou de gravá-la (ver abaixo) — mesma correção já aplicada do lado de leitura em
            // Services/Reports/PmoExecutiveData.cs e Services/Planning/PmoActualFactsProvider.cs (16/09/2026).
            // Confirmado manualmente: as 3 tabelas existem em DB_NA_CONNECT; exigir essa coluna fazia
            // SchemaDisponivelAsync retornar false mesmo com o schema real disponível.
            var disponivel = schemaExiste && timesheetExiste && custoRealExiste && custoPrevistoExiste;

            logger.LogInformation(
                "[GESTÃO À VISTA] Checagem de schema | Banco={Banco} TimesheetLancamento={TimesheetLancamento} CustoProfissionalRealHistorico={CustoProfissionalRealHistorico} CustoProfissionalPrevisto={CustoProfissionalPrevisto} SchemaDisponivel={SchemaDisponivel}",
                banco, timesheetExiste, custoRealExiste, custoPrevistoExiste, disponivel);

            return disponivel;
        }

        // ---- Custo Real (SCD tipo 2) ----

        public async Task<GestaoVistaCustoRealOutcome> PersistirCustoRealAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaCustoRealExtraido registro, DateTime dataSincronizacao, CancellationToken cancellationToken = default)
        {
            if (registro.CustoHora == null)
                throw new InvalidOperationException($"CustoHora nulo para '{registro.NomeProfissional}' — o chamador deve filtrar linhas inválidas antes de persistir.");

            byte[] hashAtual = GestaoVistaRowHasher.CustoReal(registro);

            const string sqlVigente = @"
                SELECT TOP 1 Id, RowHash
                FROM gestao.CustoProfissionalRealHistorico
                WHERE Project_ID_ACC = @Projeto AND NomeProfissional = @Nome AND Ativo = 1 AND VigenciaFim IS NULL
                ORDER BY VigenciaInicio DESC;";

            long? idVigente = null;
            byte[] hashVigente = null;

            using (var cmd = new SqlCommand(sqlVigente, connection, transaction))
            {
                cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                cmd.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = registro.NomeProfissional;

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    idVigente = Convert.ToInt64(reader["Id"]);
                    hashVigente = (byte[])reader["RowHash"];
                }
            }

            if (idVigente.HasValue && HashesIguais(hashVigente, hashAtual))
            {
                return GestaoVistaCustoRealOutcome.MesmaVersao;
            }

            if (idVigente.HasValue)
            {
                const string sqlFechar = @"
                    UPDATE gestao.CustoProfissionalRealHistorico SET VigenciaFim = @DataSincronizacao WHERE Id = @Id;";
                using var cmdFechar = new SqlCommand(sqlFechar, connection, transaction);
                cmdFechar.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = dataSincronizacao;
                cmdFechar.Parameters.Add("@Id", SqlDbType.BigInt).Value = idVigente.Value;
                await cmdFechar.ExecuteNonQueryAsync(cancellationToken);
            }

            const string sqlInserir = @"
                INSERT INTO gestao.CustoProfissionalRealHistorico
                    (Project_ID_ACC, NomeProfissional, Funcao, Disciplina, Remuneracao, Encargos, Softwares, Computadores,
                     CustoTotalMensal, CustoHora, VigenciaInicio, VigenciaFim, Ativo, RowHash)
                VALUES
                    (@Projeto, @Nome, @Funcao, @Disciplina, @Remuneracao, @Encargos, @Softwares, @Computadores,
                     @CustoTotalMensal, @CustoHora, @VigenciaInicio, NULL, 1, @RowHash);";

            using (var cmdInserir = new SqlCommand(sqlInserir, connection, transaction))
            {
                cmdInserir.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                cmdInserir.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = registro.NomeProfissional;
                cmdInserir.Parameters.Add("@Funcao", SqlDbType.NVarChar, 200).Value = (object)registro.Funcao ?? DBNull.Value;
                cmdInserir.Parameters.Add("@Disciplina", SqlDbType.NVarChar, 200).Value = (object)registro.Disciplina ?? DBNull.Value;
                AdicionarDecimal(cmdInserir, "@Remuneracao", registro.Remuneracao);
                AdicionarDecimal(cmdInserir, "@Encargos", registro.Encargos);
                AdicionarDecimal(cmdInserir, "@Softwares", registro.Softwares);
                AdicionarDecimal(cmdInserir, "@Computadores", registro.Computadores);
                AdicionarDecimal(cmdInserir, "@CustoTotalMensal", registro.CustoTotalMensal);
                AdicionarDecimal(cmdInserir, "@CustoHora", registro.CustoHora);
                cmdInserir.Parameters.Add("@VigenciaInicio", SqlDbType.DateTime2).Value = dataSincronizacao;
                cmdInserir.Parameters.Add("@RowHash", SqlDbType.Binary, 32).Value = hashAtual;

                await cmdInserir.ExecuteNonQueryAsync(cancellationToken);
            }

            return idVigente.HasValue ? GestaoVistaCustoRealOutcome.NovaVersao : GestaoVistaCustoRealOutcome.PrimeiraVersao;
        }

        public async Task<decimal?> ObterCustoHoraVigenteAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            string nomeProfissional, DateTime dataReferencia, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT TOP 1 CustoHora
                FROM gestao.CustoProfissionalRealHistorico
                WHERE Project_ID_ACC = @Projeto AND NomeProfissional = @Nome
                  AND VigenciaInicio <= @DataRef
                  AND (VigenciaFim IS NULL OR VigenciaFim > @DataRef)
                ORDER BY VigenciaInicio DESC;";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            cmd.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = nomeProfissional;
            cmd.Parameters.Add("@DataRef", SqlDbType.DateTime2).Value = dataReferencia;

            var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
            return resultado == null || resultado == DBNull.Value ? null : Convert.ToDecimal(resultado);
        }

        // ---- Custo Previsto (snapshot, sem histórico) ----

        public async Task<bool> PersistirCustoPrevistoAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaCustoPrevistoExtraido registro, DateTime dataSincronizacao, CancellationToken cancellationToken = default)
        {
            if (registro.CustoHora == null)
                throw new InvalidOperationException($"CustoHora nulo para função '{registro.Funcao}' — o chamador deve filtrar linhas inválidas antes de persistir.");

            byte[] hash = GestaoVistaRowHasher.CustoPrevisto(registro);

            if (await ExisteHashAsync(connection, transaction, "gestao.CustoProfissionalPrevisto", projectIdAcc, hash, cancellationToken))
            {
                return false;
            }

            const string sql = @"
                INSERT INTO gestao.CustoProfissionalPrevisto
                    (Project_ID_ACC, Funcao, GrupoFuncao, Disciplina, Remuneracao, Encargos, Softwares, Computadores,
                     CustoTotalMensal, CustoHora, Data_Sincronizacao, RowHash)
                VALUES
                    (@Projeto, @Funcao, @GrupoFuncao, @Disciplina, @Remuneracao, @Encargos, @Softwares, @Computadores,
                     @CustoTotalMensal, @CustoHora, @DataSincronizacao, @RowHash);";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            cmd.Parameters.Add("@Funcao", SqlDbType.NVarChar, 200).Value = registro.Funcao;
            cmd.Parameters.Add("@GrupoFuncao", SqlDbType.NVarChar, 200).Value = (object)registro.GrupoFuncao ?? DBNull.Value;
            cmd.Parameters.Add("@Disciplina", SqlDbType.NVarChar, 200).Value = (object)registro.Disciplina ?? DBNull.Value;
            AdicionarDecimal(cmd, "@Remuneracao", registro.Remuneracao);
            AdicionarDecimal(cmd, "@Encargos", registro.Encargos);
            AdicionarDecimal(cmd, "@Softwares", registro.Softwares);
            AdicionarDecimal(cmd, "@Computadores", registro.Computadores);
            AdicionarDecimal(cmd, "@CustoTotalMensal", registro.CustoTotalMensal);
            AdicionarDecimal(cmd, "@CustoHora", registro.CustoHora);
            cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = dataSincronizacao;
            cmd.Parameters.Add("@RowHash", SqlDbType.Binary, 32).Value = hash;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // ---- Timesheet (idempotência por RowHash — ver GestaoVistaRowHasher.Timesheet para os campos) ----

        public async Task<bool> PersistirTimesheetAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaTimesheetLancamentoExtraido registro, decimal? custoCalculado, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            byte[] hash = GestaoVistaRowHasher.Timesheet(registro);

            if (await ExisteHashAsync(connection, transaction, "gestao.TimesheetLancamento", projectIdAcc, hash, cancellationToken))
            {
                return false;
            }

            // CustoTimesheetInformado não existe fisicamente em gestao.TimesheetLancamento (migration manual
            // 18 nunca criada) — gravar essa coluna faria o INSERT falhar com SqlException 207 (mesma causa
            // já corrigida do lado de leitura). registro.CustoInformadoNaPlanilha fica sem persistir até a
            // migration existir; nunca é substituído por CustoCalculado.
            const string sql = @"
                INSERT INTO gestao.TimesheetLancamento
                    (Project_ID_ACC, NomeProfissional, HorasPlanejadas, HorasTrabalhadas, Projeto, Funcao, Disciplina,
                     DocumentoCodigo, Documento, Descricao, DataLancamento, CustoCalculado, RowHash, Data_Sincronizacao)
                VALUES
                    (@Projeto, @Nome, @HorasPlanejadas, @HorasTrabalhadas, @ProjetoPlanilha, @Funcao, @Disciplina,
                     @DocumentoCodigo, @Documento, @Descricao, @DataLancamento, @CustoCalculado, @RowHash, @DataSincronizacao);";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            cmd.Parameters.Add("@Nome", SqlDbType.NVarChar, 300).Value = registro.NomeProfissional;
            AdicionarDecimal(cmd, "@HorasPlanejadas", registro.HorasPlanejadas);
            AdicionarDecimal(cmd, "@HorasTrabalhadas", registro.HorasTrabalhadas);
            cmd.Parameters.Add("@ProjetoPlanilha", SqlDbType.NVarChar, 300).Value = (object)registro.Projeto ?? DBNull.Value;
            cmd.Parameters.Add("@Funcao", SqlDbType.NVarChar, 200).Value = (object)registro.Funcao ?? DBNull.Value;
            cmd.Parameters.Add("@Disciplina", SqlDbType.NVarChar, 200).Value = (object)registro.Disciplina ?? DBNull.Value;
            cmd.Parameters.Add("@DocumentoCodigo", SqlDbType.NVarChar, 300).Value = (object)registro.DocumentoCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@Documento", SqlDbType.NVarChar, 500).Value = (object)registro.Documento ?? DBNull.Value;
            cmd.Parameters.Add("@Descricao", SqlDbType.NVarChar, 1000).Value = (object)registro.Descricao ?? DBNull.Value;
            cmd.Parameters.Add("@DataLancamento", SqlDbType.Date).Value = (object)registro.DataLancamento ?? DBNull.Value;
            AdicionarDecimal(cmd, "@CustoCalculado", custoCalculado);
            cmd.Parameters.Add("@RowHash", SqlDbType.Binary, 32).Value = hash;
            cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = dataSincronizacao;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // ---- Auxiliares ----

        private static async Task<bool> ExisteHashAsync(SqlConnection connection, SqlTransaction transaction, string tabela, string projectIdAcc, byte[] hash, CancellationToken cancellationToken)
        {
            string sql = $"SELECT COUNT(1) FROM {tabela} WHERE Project_ID_ACC = @Projeto AND RowHash = @RowHash;";
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@Projeto", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            cmd.Parameters.Add("@RowHash", SqlDbType.Binary, 32).Value = hash;
            var count = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(count) > 0;
        }

        private static void AdicionarDecimal(SqlCommand cmd, string nome, decimal? valor)
        {
            var p = cmd.Parameters.Add(nome, SqlDbType.Decimal);
            p.Precision = 18;
            p.Scale = 6;
            p.Value = (object)valor ?? DBNull.Value;
        }

        private static bool HashesIguais(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
