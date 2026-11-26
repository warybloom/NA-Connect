using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Implementação real de ICronogramaTipo2LdpSyncRepository. Nunca executa DDL. Os SELECTs usam
    /// exclusivamente as views *_Ultima_Versao (nunca somam/misturam sincronizações antigas); os UPDATEs
    /// são sempre recebidos com uma SqlConnection/SqlTransaction JÁ ABERTAS pelo chamador
    /// (CronogramaTipo2LdpSyncService) — nunca abre conexão/transação própria, nunca chama Commit/Rollback.
    /// </summary>
    public sealed class CronogramaTipo2LdpSyncRepository : ICronogramaTipo2LdpSyncRepository
    {
        private readonly string _connectionString;

        public CronogramaTipo2LdpSyncRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
        }

        public async Task<IReadOnlyList<CronogramaTipo2Registro>> ObterUltimoSnapshotCronogramaAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            const string query = @"
                SELECT
                    ID_Cronograma, UID_Tarefa, TASK_NAME, EDT_WBS, Campos_Customizados_JSON,
                    NA_DOCUMENT_NUMBER, NA_Document_Number_Normalizado, Fase, Gate_Percentual,
                    Chave_Documento_Gate, Status_Validacao_Documento_Gate,
                    Percent_Previsto_Reprogramado, Percent_Realizado, Termino_Linha_Base, Termino, Data_Sincronizacao
                FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao
                WHERE Project_ID_ACC = @ProjectIdAcc
                  AND Tipo_LDP = 2
                  AND Status_Validacao_Documento_Gate = 'VALIDO'";

            var resultado = new List<CronogramaTipo2Registro>();

            using (var connection = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 100).Value = projectIdAcc;

                await connection.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.Add(new CronogramaTipo2Registro
                        {
                            IdCronograma = Convert.ToInt32(reader["ID_Cronograma"]),
                            UidTarefaPersistido = reader["UID_Tarefa"] as string,
                            NomeTarefa = reader["TASK_NAME"] as string,
                            EdtWbs = reader["EDT_WBS"] as string,
                            CamposCustomizadosJson = reader["Campos_Customizados_JSON"] as string,
                            NumeroDocumentoNaOriginal = reader["NA_DOCUMENT_NUMBER"] as string,
                            NumeroDocumentoNaNormalizado = reader["NA_Document_Number_Normalizado"] as string,
                            FaseOriginal = reader["Fase"] as string,
                            GatePercentual = reader["Gate_Percentual"] == DBNull.Value ? (byte?)null : Convert.ToByte(reader["Gate_Percentual"]),
                            ChaveDocumentoGate = reader["Chave_Documento_Gate"] as string,
                            StatusValidacaoDocumentoGate = reader["Status_Validacao_Documento_Gate"] as string,
                            PercentPrevistoReprogramado = reader["Percent_Previsto_Reprogramado"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["Percent_Previsto_Reprogramado"]),
                            PercentRealizado = reader["Percent_Realizado"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["Percent_Realizado"]),
                            TerminoLinhaBase = reader["Termino_Linha_Base"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["Termino_Linha_Base"]),
                            Termino = reader["Termino"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["Termino"]),
                            DataSincronizacao = Convert.ToDateTime(reader["Data_Sincronizacao"])
                        });
                    }
                }
            }

            return resultado;
        }

        public async Task<IReadOnlyList<GateLdpParaSincronizacao>> ObterGatesLdpUltimaVersaoAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            const string query = @"
                SELECT Id, Ldp_Registro_Id, Project_ID_ACC, Numero_Documento_NA, Numero_Documento_Cliente, Gate_Percentual, Gate_Ativo
                FROM dbo.VW_Ldp_Documento_Gates_Ultima_Versao
                WHERE Project_ID_ACC = @ProjectIdAcc";

            var resultado = new List<GateLdpParaSincronizacao>();

            using (var connection = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = projectIdAcc;

                await connection.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.Add(new GateLdpParaSincronizacao
                        {
                            Id = Convert.ToInt64(reader["Id"]),
                            LdpRegistroId = Convert.ToInt64(reader["Ldp_Registro_Id"]),
                            ProjectIdAcc = reader["Project_ID_ACC"] as string,
                            NumeroDocumentoNa = reader["Numero_Documento_NA"] as string,
                            NumeroDocumentoCliente = reader["Numero_Documento_Cliente"] as string,
                            GatePercentual = Convert.ToByte(reader["Gate_Percentual"]),
                            GateAtivo = Convert.ToBoolean(reader["Gate_Ativo"])
                        });
                    }
                }
            }

            return resultado;
        }

        private const string UpdateGateSql = @"
            UPDATE dbo.Ldp_Documento_Gates
            SET
                Planned_Baseline = @PlannedBaseline,
                Replanned_Date = @ReplannedDate,
                Planned_Physical = @PlannedPhysical,
                Physical_Completed = @PhysicalCompleted,
                Cronograma_UID_Tarefa = @CronogramaUidTarefa,
                Cronograma_Data_Sincronizacao = @CronogramaDataSincronizacao,
                Data_Atualizacao_Cronograma = @DataAtualizacao,
                Status_Sincronizacao_Cronograma = @StatusSincronizacao,
                Mensagem_Sincronizacao_Cronograma = @MensagemSincronizacao
            WHERE Id = @Id
              AND Project_ID_ACC = @ProjectIdAcc
              AND Ldp_Registro_Id = @LdpRegistroId
              AND Gate_Percentual = @GatePercentual;";

        public async Task<int> AtualizarGateAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            long gateId,
            string projectIdAcc,
            long ldpRegistroId,
            byte gatePercentual,
            DateTime? plannedBaseline,
            DateTime? replannedDate,
            decimal? plannedPhysical,
            decimal? physicalCompleted,
            string cronogramaUidTarefa,
            DateTime cronogramaDataSincronizacao,
            DateTime dataAtualizacao,
            string statusSincronizacao,
            string mensagemSincronizacao,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            using (var cmd = new SqlCommand(UpdateGateSql, connection, transaction))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = gateId;
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = projectIdAcc;
                cmd.Parameters.Add("@LdpRegistroId", SqlDbType.BigInt).Value = ldpRegistroId;
                cmd.Parameters.Add("@GatePercentual", SqlDbType.TinyInt).Value = gatePercentual;

                // Planned_Baseline/Replanned_Date são DATE em Ldp_Documento_Gates — só a parte da data
                // (seção 4/18 do pedido), nunca a hora preservada aqui (essa fica só no Cronograma_Projetos).
                cmd.Parameters.Add("@PlannedBaseline", SqlDbType.Date).Value = (object)plannedBaseline?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@ReplannedDate", SqlDbType.Date).Value = (object)replannedDate?.Date ?? DBNull.Value;

                var pPlanned = cmd.Parameters.Add("@PlannedPhysical", SqlDbType.Decimal);
                pPlanned.Precision = 18; pPlanned.Scale = 6;
                pPlanned.Value = (object)plannedPhysical ?? DBNull.Value;

                var pPhysical = cmd.Parameters.Add("@PhysicalCompleted", SqlDbType.Decimal);
                pPhysical.Precision = 18; pPhysical.Scale = 6;
                pPhysical.Value = (object)physicalCompleted ?? DBNull.Value;

                cmd.Parameters.Add("@CronogramaUidTarefa", SqlDbType.NVarChar, 200).Value = (object)cronogramaUidTarefa ?? DBNull.Value;
                cmd.Parameters.Add("@CronogramaDataSincronizacao", SqlDbType.DateTime2).Value = cronogramaDataSincronizacao;
                cmd.Parameters.Add("@DataAtualizacao", SqlDbType.DateTime2).Value = dataAtualizacao;
                cmd.Parameters.Add("@StatusSincronizacao", SqlDbType.NVarChar, 100).Value = (object)statusSincronizacao ?? DBNull.Value;
                cmd.Parameters.Add("@MensagemSincronizacao", SqlDbType.NVarChar, 1000).Value = (object)mensagemSincronizacao ?? DBNull.Value;

                return await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private const string UpdateStatusMatchingCronogramaSql = @"
            UPDATE dbo.Cronograma_Projetos
            SET
                Status_Matching_LDP_Gate = @Status,
                Mensagem_Matching_LDP_Gate = @Mensagem,
                Ldp_Gate_Id_Match = @LdpGateId,
                Ldp_Registro_Id_Match = @LdpRegistroId,
                Data_Matching_LDP_Gate = @DataMatching
            WHERE ID_Cronograma = @IdCronograma;";

        public async Task<int> AtualizarStatusMatchingCronogramaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int idCronograma,
            string statusMatchingLdpGate,
            string mensagemMatchingLdpGate,
            long? ldpGateIdMatch,
            long? ldpRegistroIdMatch,
            DateTime dataMatchingLdpGate,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            using (var cmd = new SqlCommand(UpdateStatusMatchingCronogramaSql, connection, transaction))
            {
                cmd.Parameters.Add("@IdCronograma", SqlDbType.Int).Value = idCronograma;
                cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 100).Value = (object)statusMatchingLdpGate ?? DBNull.Value;
                cmd.Parameters.Add("@Mensagem", SqlDbType.NVarChar, 1000).Value = (object)mensagemMatchingLdpGate ?? DBNull.Value;
                cmd.Parameters.Add("@LdpGateId", SqlDbType.BigInt).Value = (object)ldpGateIdMatch ?? DBNull.Value;
                cmd.Parameters.Add("@LdpRegistroId", SqlDbType.BigInt).Value = (object)ldpRegistroIdMatch ?? DBNull.Value;
                cmd.Parameters.Add("@DataMatching", SqlDbType.DateTime2).Value = dataMatchingLdpGate;

                return await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
