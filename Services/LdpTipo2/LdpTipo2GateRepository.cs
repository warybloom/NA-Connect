using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Implementação real (leitura/gravação) de dbo.Ldp_Documento_Gates. A tabela já foi criada
    /// manualmente — esta classe nunca executa DDL, nunca recria a tabela, nunca faz DELETE.
    ///
    /// RESOLVIDO NA FASE 3: o campo Ldp_Registro_Id (bigint, NOT NULL) é preenchido com o ID_Registro
    /// real (int, IDENTITY) devolvido por LdpTipo2Repository.InserirDocumentoPrincipalAsync via
    /// OUTPUT INSERTED.ID_Registro no INSERT de dbo.Ldp_Unificada — nunca um código textual. Tanto
    /// SalvarLoteAsync quanto InserirLoteNaTransacaoAsync continuam recusando (lançando exceção)
    /// qualquer gate sem LdpRegistroId válido (> 0), como salvaguarda.
    /// </summary>
    public sealed class LdpTipo2GateRepository : ILdpTipo2GateRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<LdpTipo2GateRepository> _logger;

        public LdpTipo2GateRepository(IConfiguration configuration, ILogger<LdpTipo2GateRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        public async Task<IReadOnlyList<LdpTipo2Gate>> ObterPorProjetoESincronizacaoAsync(
            string projectIdAcc,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            const string query = @"
                SELECT
                    Id, Ldp_Registro_Id, Project_ID_ACC, Numero_Documento_NA, Numero_Documento_Cliente,
                    Gate_Percentual, Gate_Ativo, Fase, Document_Status, Situation,
                    Planned_Baseline, Replanned_Date, Planned_Physical, Physical_Completed, Data_Sincronizacao
                FROM dbo.Ldp_Documento_Gates
                WHERE Project_ID_ACC = @ProjectIdAcc AND Data_Sincronizacao = @DataSincronizacao";

            var resultado = new List<LdpTipo2Gate>();

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = projectIdAcc;
                cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = dataSincronizacao;

                await conn.OpenAsync(cancellationToken);

                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.Add(new LdpTipo2Gate
                        {
                            Id = reader["Id"] as long?,
                            LdpRegistroId = reader["Ldp_Registro_Id"] as long?,
                            ProjectIdAcc = reader["Project_ID_ACC"] as string,
                            NumeroDocumentoNa = reader["Numero_Documento_NA"] as string,
                            NumeroDocumentoCliente = reader["Numero_Documento_Cliente"] as string,
                            GatePercentual = Convert.ToByte(reader["Gate_Percentual"]),
                            GateAtivo = Convert.ToBoolean(reader["Gate_Ativo"]),
                            Fase = reader["Fase"] as string,
                            DocumentStatus = reader["Document_Status"] as string,
                            Situation = reader["Situation"] as string,
                            PlannedBaseline = reader["Planned_Baseline"] as DateTime?,
                            ReplannedDate = reader["Replanned_Date"] as DateTime?,
                            PlannedPhysical = reader["Planned_Physical"] as decimal?,
                            PhysicalCompleted = reader["Physical_Completed"] as decimal?,
                            DataSincronizacao = Convert.ToDateTime(reader["Data_Sincronizacao"])
                        });
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Lê diretamente de dbo.Ldp_Documento_Gates (não da view *_Ultima_Versao, cuja definição vive
        /// fora deste repositório e cujo conjunto de colunas não pôde ser confirmado neste ambiente) —
        /// somente diagnóstico, nunca usado pelo caminho de persistência. Traz todas as sincronizações do
        /// projeto (não filtra por Data_Sincronizacao) para permitir inspecionar o histórico completo de
        /// rastreamento com o cronograma.
        /// </summary>
        public async Task<IReadOnlyList<LdpTipo2GateDiagnostico>> ObterDiagnosticoPorProjetoAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            const string query = @"
                SELECT
                    Id, Ldp_Registro_Id, Project_ID_ACC, Numero_Documento_NA, Numero_Documento_Cliente,
                    Gate_Percentual, Gate_Ativo, Planned_Physical, Physical_Completed,
                    Planned_Baseline, Replanned_Date, Data_Sincronizacao,
                    Cronograma_UID_Tarefa, Cronograma_Data_Sincronizacao, Data_Atualizacao_Cronograma,
                    Status_Sincronizacao_Cronograma, Mensagem_Sincronizacao_Cronograma
                FROM dbo.Ldp_Documento_Gates
                WHERE Project_ID_ACC = @ProjectIdAcc";

            var resultado = new List<LdpTipo2GateDiagnostico>();

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = projectIdAcc;

                await conn.OpenAsync(cancellationToken);

                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.Add(new LdpTipo2GateDiagnostico
                        {
                            Id = Convert.ToInt64(reader["Id"]),
                            LdpRegistroId = Convert.ToInt64(reader["Ldp_Registro_Id"]),
                            ProjectIdAcc = reader["Project_ID_ACC"] as string,
                            NumeroDocumentoNa = reader["Numero_Documento_NA"] as string,
                            NumeroDocumentoCliente = reader["Numero_Documento_Cliente"] as string,
                            GatePercentual = Convert.ToByte(reader["Gate_Percentual"]),
                            GateAtivo = Convert.ToBoolean(reader["Gate_Ativo"]),
                            PlannedPhysical = reader["Planned_Physical"] as decimal?,
                            PhysicalCompleted = reader["Physical_Completed"] as decimal?,
                            PlannedBaseline = reader["Planned_Baseline"] as DateTime?,
                            ReplannedDate = reader["Replanned_Date"] as DateTime?,
                            DataSincronizacao = Convert.ToDateTime(reader["Data_Sincronizacao"]),
                            CronogramaUidTarefa = reader["Cronograma_UID_Tarefa"] as string,
                            CronogramaDataSincronizacao = reader["Cronograma_Data_Sincronizacao"] as DateTime?,
                            DataAtualizacaoCronograma = reader["Data_Atualizacao_Cronograma"] as DateTime?,
                            StatusSincronizacaoCronograma = reader["Status_Sincronizacao_Cronograma"] as string,
                            MensagemSincronizacaoCronograma = reader["Mensagem_Sincronizacao_Cronograma"] as string
                        });
                    }
                }
            }

            return resultado;
        }

        public async Task SalvarLoteAsync(
            IReadOnlyCollection<LdpTipo2Gate> gates,
            CancellationToken cancellationToken = default)
        {
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (gates.Count == 0) return;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);

                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        await ExecutarInsertsAsync(conn, transaction, gates, cancellationToken);

                        transaction.Commit();
                        _logger.LogInformation($"[LDP TIPO 2] {gates.Count} gate(s) inserido(s) em lote (transação própria) em dbo.Ldp_Documento_Gates.");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, $"❌ [LDP TIPO 2] Falha ao inserir lote de {gates.Count} gate(s) — rollback executado.");
                        throw;
                    }
                }
            }
        }

        public async Task InserirLoteNaTransacaoAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            IReadOnlyCollection<LdpTipo2Gate> gates,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (gates.Count == 0) return;

            // Não abre conexão/transação própria e NÃO chama Commit/Rollback aqui: quem decide o
            // destino da transação inteira (que também cobre a linha principal em Ldp_Unificada) é
            // sempre o chamador (LdpTipo2PersistenceService) — uma exceção aqui só propaga para cima.
            await ExecutarInsertsAsync(connection, transaction, gates, cancellationToken);
        }

        /// <summary>
        /// Validação + INSERT parametrizado propriamente dito, compartilhado por SalvarLoteAsync (dona
        /// da transação) e InserirLoteNaTransacaoAsync (transação de outro dono). Recusa o lote inteiro
        /// (nenhum INSERT é executado) se qualquer gate não tiver um Ldp_Registro_Id válido (> 0) — ver
        /// observação em LdpTipo2Gate.LdpRegistroId.
        /// </summary>
        private static async Task ExecutarInsertsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            IReadOnlyCollection<LdpTipo2Gate> gates,
            CancellationToken cancellationToken)
        {
            var invalidos = gates
                .Select(g => (Gate: g, Valido: GateValidoParaInsercao(g, out var motivo), Motivo: motivo))
                .Where(x => !x.Valido)
                .ToList();

            if (invalidos.Count > 0)
            {
                string motivos = string.Join(" | ", invalidos.Select(x => x.Motivo).Distinct());
                throw new InvalidOperationException(
                    $"{invalidos.Count} gate(s) inválido(s) no lote (ex.: sem Ldp_Registro_Id válido). Nenhum INSERT foi executado. Motivos: {motivos}");
            }

            const string insertSql = @"
                INSERT INTO dbo.Ldp_Documento_Gates
                (
                    Ldp_Registro_Id, Project_ID_ACC, Numero_Documento_NA, Numero_Documento_Cliente,
                    Gate_Percentual, Gate_Ativo, Fase, Document_Status, Situation,
                    Planned_Baseline, Replanned_Date, Planned_Physical, Physical_Completed, Data_Sincronizacao
                )
                VALUES
                (
                    @LdpRegistroId, @ProjectIdAcc, @NumeroDocumentoNa, @NumeroDocumentoCliente,
                    @GatePercentual, @GateAtivo, @Fase, @DocumentStatus, @Situation,
                    @PlannedBaseline, @ReplannedDate, @PlannedPhysical, @PhysicalCompleted, @DataSincronizacao
                )";

            foreach (var gate in gates)
            {
                using (var cmd = new SqlCommand(insertSql, connection, transaction))
                {
                    cmd.Parameters.Add("@LdpRegistroId", SqlDbType.BigInt).Value = gate.LdpRegistroId.Value;
                    cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = gate.ProjectIdAcc;
                    cmd.Parameters.Add("@NumeroDocumentoNa", SqlDbType.NVarChar, 300).Value = (object)gate.NumeroDocumentoNa ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroDocumentoCliente", SqlDbType.NVarChar, 300).Value = (object)gate.NumeroDocumentoCliente ?? DBNull.Value;
                    cmd.Parameters.Add("@GatePercentual", SqlDbType.TinyInt).Value = gate.GatePercentual;
                    cmd.Parameters.Add("@GateAtivo", SqlDbType.Bit).Value = gate.GateAtivo;
                    cmd.Parameters.Add("@Fase", SqlDbType.NVarChar, 100).Value = (object)gate.Fase ?? DBNull.Value;
                    cmd.Parameters.Add("@DocumentStatus", SqlDbType.NVarChar, 200).Value = (object)gate.DocumentStatus ?? DBNull.Value;
                    cmd.Parameters.Add("@Situation", SqlDbType.NVarChar, 200).Value = (object)gate.Situation ?? DBNull.Value;
                    cmd.Parameters.Add("@PlannedBaseline", SqlDbType.Date).Value = (object)gate.PlannedBaseline ?? DBNull.Value;
                    cmd.Parameters.Add("@ReplannedDate", SqlDbType.Date).Value = (object)gate.ReplannedDate ?? DBNull.Value;

                    var pPlanned = cmd.Parameters.Add("@PlannedPhysical", SqlDbType.Decimal);
                    pPlanned.Precision = 18;
                    pPlanned.Scale = 6;
                    pPlanned.Value = (object)gate.PlannedPhysical ?? DBNull.Value;

                    var pCompleted = cmd.Parameters.Add("@PhysicalCompleted", SqlDbType.Decimal);
                    pCompleted.Precision = 18;
                    pCompleted.Scale = 6;
                    pCompleted.Value = (object)gate.PhysicalCompleted ?? DBNull.Value;

                    cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime2).Value = gate.DataSincronizacao;

                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        /// <summary>
        /// Validação obrigatória antes do INSERT (seção 10/11 do pedido): ProjectIdAcc preenchido,
        /// LdpRegistroId > 0, GatePercentual em {30,60,90}, DataSincronizacao preenchida.
        /// </summary>
        private static bool GateValidoParaInsercao(LdpTipo2Gate gate, out string motivo)
        {
            if (gate == null) { motivo = "objeto de gate nulo"; return false; }
            if (string.IsNullOrWhiteSpace(gate.ProjectIdAcc)) { motivo = "ProjectIdAcc vazio"; return false; }
            if (!gate.LdpRegistroId.HasValue || gate.LdpRegistroId.Value <= 0) { motivo = "LdpRegistroId ausente ou <= 0"; return false; }
            if (!LdpTipo2Gate.GateValido(gate.GatePercentual)) { motivo = $"GatePercentual inválido ({gate.GatePercentual}) — só 30/60/90"; return false; }
            if (gate.DataSincronizacao == default) { motivo = "DataSincronizacao não preenchida"; return false; }

            motivo = null;
            return true;
        }
    }
}
