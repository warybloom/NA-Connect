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
    /// Implementação real de ICronogramaTipo2Repository. Nunca executa DDL, nunca recria a tabela,
    /// nunca faz DELETE/UPDATE em linhas antigas — a tabela dbo.Cronograma_Projetos já existe (estrutura
    /// inspecionada por leitura antes desta implementação: 32 colunas, PK ID_Cronograma int IDENTITY,
    /// únicas colunas NOT NULL além da PK são Project_ID_ACC, UID_Tarefa e Tipo_LDP; as demais colunas
    /// "legadas" do Tipo 1 — CR_OS, PERCENT_PHYSICAL_COMPLETED, PERCENT_PREDICTED_PHYSICAL, WBS_PERCENT,
    /// PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE, Trabalho — são todas nullable e ficam
    /// NULL nas linhas do Tipo 2 nesta fase, por não existir mapeamento controlado e validado para elas
    /// no contexto do Tipo 2 ainda).
    /// </summary>
    public sealed class CronogramaTipo2Repository : ICronogramaTipo2Repository
    {
        private readonly string _connectionString;

        public CronogramaTipo2Repository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
        }

        private const string InsertSql = @"
            INSERT INTO dbo.Cronograma_Projetos
            (
                Project_ID_ACC, UID_Tarefa, EDT_WBS, NA_DOCUMENT_NUMBER, TASK_NAME, Data_Sincronizacao,
                Tipo_LDP, NA_Document_Number_Normalizado, Fase, Gate_Percentual,
                Eh_Tarefa_Resumo, Eh_Candidato_Documento_Gate, Chave_Documento_Gate,
                Status_Validacao_Documento_Gate, Mensagem_Validacao_Documento_Gate,
                Percent_Previsto_LB, Percent_Previsto_Reprogramado, Percent_Realizado,
                Disciplina_Cronograma, Status_Documento_Engenharia,
                Campos_Customizados_JSON, Data_Leitura_Cronograma,
                Termino_Linha_Base, Termino,
                Status_Matching_LDP_Gate, Mensagem_Matching_LDP_Gate,
                Ldp_Gate_Id_Match, Ldp_Registro_Id_Match, Data_Matching_LDP_Gate
            )
            VALUES
            (
                @ProjectIdAcc, @UidTarefa, @EdtWbs, @NaDocumentNumber, @TaskName, @DataSincronizacao,
                @TipoLdp, @NaDocumentNumberNormalizado, @Fase, @GatePercentual,
                @EhTarefaResumo, @EhCandidatoDocumentoGate, @ChaveDocumentoGate,
                @StatusValidacaoDocumentoGate, @MensagemValidacaoDocumentoGate,
                @PercentPrevistoLb, @PercentPrevistoReprogramado, @PercentRealizado,
                @DisciplinaCronograma, @StatusDocumentoEngenharia,
                @CamposCustomizadosJson, @DataLeituraCronograma,
                @TerminoLinhaBase, @Termino,
                @StatusMatchingLdpGate, @MensagemMatchingLdpGate,
                @LdpGateIdMatch, @LdpRegistroIdMatch, @DataMatchingLdpGate
            );";

        /// <summary>
        /// Limites reais de dbo.Cronograma_Projetos em CARACTERES (seção 17 do pedido — max_length de
        /// sys.columns vem em BYTES para NVARCHAR, sempre o dobro dos caracteres). Validado aqui ANTES de
        /// qualquer INSERT: o SqlParameter com Size fixo trunca silenciosamente quando o valor excede o
        /// limite, então o SQL Server nunca chegaria a rejeitar a linha — a checagem precisa ser feita no
        /// código, para todo o lote de uma vez, antes de abrir o primeiro comando.
        /// </summary>
        private static readonly (string Coluna, int MaxChars, Func<CronogramaTipo2Registro, string> Obter)[] LimitesNvarchar =
        {
            ("NA_DOCUMENT_NUMBER", 100, r => r.NumeroDocumentoNaOriginal),
            ("NA_Document_Number_Normalizado", 300, r => r.NumeroDocumentoNaNormalizado),
            ("Fase", 50, r => r.FaseOriginal),
            ("Chave_Documento_Gate", 500, r => r.ChaveDocumentoGate),
            ("Status_Validacao_Documento_Gate", 100, r => r.StatusValidacaoDocumentoGate),
            ("Mensagem_Validacao_Documento_Gate", 1000, r => r.MensagemValidacaoDocumentoGate),
            ("Disciplina_Cronograma", 200, r => r.Disciplina),
            ("Status_Documento_Engenharia", 300, r => r.StatusDocumentoEngenharia),
            // Colunas legadas (uso do INSERT do Tipo 2, ver comentário de classe) — também NVARCHAR de
            // tamanho fixo em dbo.Cronograma_Projetos; a mesma proteção contra truncamento silencioso se
            // aplica a elas. Project_ID_ACC não entra aqui: é constante para o lote inteiro e validado à
            // parte, fora do loop por registro.
            ("UID_Tarefa", 100, r => r.TaskUniqueId?.ToString() ?? r.TaskId?.ToString() ?? string.Empty),
            ("EDT_WBS", 100, r => r.EdtWbs),
            // Fase 4B (seção 9 do pedido): colunas de status/mensagem de matching com a LDP. Normalmente
            // NULL no momento do INSERT (o matching roda depois, via CronogramaTipo2LdpSyncService), mas
            // validadas aqui também pela mesma razão — nenhum campo NVARCHAR de tamanho fixo pode truncar
            // silenciosamente, mesmo que hoje só chegue preenchido através de um UPDATE futuro.
            ("Status_Matching_LDP_Gate", 100, r => r.StatusMatchingLdpGate),
            ("Mensagem_Matching_LDP_Gate", 1000, r => r.MensagemMatchingLdpGate),
        };

        private static void ValidarTamanhosOuLancar(string projectIdAcc, IReadOnlyCollection<CronogramaTipo2Registro> registros)
        {
            var violacoes = new List<string>();

            if (!string.IsNullOrEmpty(projectIdAcc) && projectIdAcc.Length > 100)
            {
                violacoes.Add($"Campo 'Project_ID_ACC' | Tamanho encontrado: {projectIdAcc.Length} | Tamanho permitido: 100.");
            }

            foreach (var registro in registros)
            {
                string identificadorTarefa = $"TaskUniqueId={registro.TaskUniqueId?.ToString() ?? "null"} | Nome='{registro.NomeTarefa}'";

                foreach (var limite in LimitesNvarchar)
                {
                    string valor = limite.Obter(registro);
                    if (string.IsNullOrEmpty(valor)) continue;

                    if (valor.Length > limite.MaxChars)
                    {
                        violacoes.Add(
                            $"Tarefa [{identificadorTarefa}] | Campo '{limite.Coluna}' | Tamanho encontrado: {valor.Length} | Tamanho permitido: {limite.MaxChars}.");
                    }
                }
            }

            if (violacoes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Validação de tamanho falhou para {violacoes.Count} campo(s) — nenhum registro deste lote foi inserido (rollback total). Detalhes: "
                    + string.Join(" || ", violacoes));
            }
        }

        public async Task<int> InserirLoteAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string projectIdAcc,
            IReadOnlyCollection<CronogramaTipo2Registro> registros,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (string.IsNullOrWhiteSpace(projectIdAcc)) throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));
            if (registros == null) throw new ArgumentNullException(nameof(registros));

            // Validação de tamanho ANTES de qualquer INSERT (seção 17) — se qualquer campo de qualquer
            // registro exceder o limite real da coluna, nada deste lote é inserido.
            ValidarTamanhosOuLancar(projectIdAcc, registros);

            int totalInserido = 0;

            foreach (var registro in registros)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // UID_Tarefa é NOT NULL na tabela real — mesmo comportamento de fallback já usado pelo
                // Tipo 1 (DatabaseService.SalvarTarefaCronograma / AccScheduleSyncService.PersistToSql):
                // prioriza o identificador único da tarefa, cai para o ID de exibição, e por fim string
                // vazia — nunca inventa um valor técnico novo.
                string uidTarefa = registro.TaskUniqueId?.ToString() ?? registro.TaskId?.ToString() ?? "";

                using (var cmd = new SqlCommand(InsertSql, connection, transaction))
                {
                    cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                    cmd.Parameters.Add("@UidTarefa", SqlDbType.NVarChar, 100).Value = uidTarefa;
                    cmd.Parameters.Add("@EdtWbs", SqlDbType.NVarChar, 100).Value = (object)registro.EdtWbs ?? DBNull.Value;
                    cmd.Parameters.Add("@NaDocumentNumber", SqlDbType.NVarChar, 100).Value = (object)registro.NumeroDocumentoNaOriginal ?? DBNull.Value;
                    cmd.Parameters.Add("@TaskName", SqlDbType.NVarChar, -1).Value = (object)registro.NomeTarefa ?? DBNull.Value;
                    cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime).Value = registro.DataSincronizacao;

                    cmd.Parameters.Add("@TipoLdp", SqlDbType.TinyInt).Value = registro.TipoLdp;
                    cmd.Parameters.Add("@NaDocumentNumberNormalizado", SqlDbType.NVarChar, 300).Value = (object)registro.NumeroDocumentoNaNormalizado ?? DBNull.Value;
                    cmd.Parameters.Add("@Fase", SqlDbType.NVarChar, 50).Value = (object)registro.FaseOriginal ?? DBNull.Value;
                    cmd.Parameters.Add("@GatePercentual", SqlDbType.TinyInt).Value = (object)registro.GatePercentual ?? DBNull.Value;

                    cmd.Parameters.Add("@EhTarefaResumo", SqlDbType.Bit).Value = registro.EhTarefaResumo;
                    cmd.Parameters.Add("@EhCandidatoDocumentoGate", SqlDbType.Bit).Value = registro.EhCandidatoDocumentoGate;
                    cmd.Parameters.Add("@ChaveDocumentoGate", SqlDbType.NVarChar, 500).Value = (object)registro.ChaveDocumentoGate ?? DBNull.Value;

                    cmd.Parameters.Add("@StatusValidacaoDocumentoGate", SqlDbType.NVarChar, 100).Value = (object)registro.StatusValidacaoDocumentoGate ?? DBNull.Value;
                    cmd.Parameters.Add("@MensagemValidacaoDocumentoGate", SqlDbType.NVarChar, 1000).Value = (object)registro.MensagemValidacaoDocumentoGate ?? DBNull.Value;

                    var pPrevistoLb = cmd.Parameters.Add("@PercentPrevistoLb", SqlDbType.Decimal);
                    pPrevistoLb.Precision = 18; pPrevistoLb.Scale = 6;
                    pPrevistoLb.Value = (object)registro.PercentPrevistoLb ?? DBNull.Value;

                    var pPrevistoReprog = cmd.Parameters.Add("@PercentPrevistoReprogramado", SqlDbType.Decimal);
                    pPrevistoReprog.Precision = 18; pPrevistoReprog.Scale = 6;
                    pPrevistoReprog.Value = (object)registro.PercentPrevistoReprogramado ?? DBNull.Value;

                    var pRealizado = cmd.Parameters.Add("@PercentRealizado", SqlDbType.Decimal);
                    pRealizado.Precision = 18; pRealizado.Scale = 6;
                    pRealizado.Value = (object)registro.PercentRealizado ?? DBNull.Value;

                    cmd.Parameters.Add("@DisciplinaCronograma", SqlDbType.NVarChar, 200).Value = (object)registro.Disciplina ?? DBNull.Value;
                    cmd.Parameters.Add("@StatusDocumentoEngenharia", SqlDbType.NVarChar, 300).Value = (object)registro.StatusDocumentoEngenharia ?? DBNull.Value;

                    cmd.Parameters.Add("@CamposCustomizadosJson", SqlDbType.NVarChar, -1).Value = (object)registro.CamposCustomizadosJson ?? DBNull.Value;
                    cmd.Parameters.Add("@DataLeituraCronograma", SqlDbType.DateTime2).Value = registro.DataLeituraCronograma;

                    // Fase 4B (seção 9 do pedido). Termino_Linha_Base/Termino vêm do MPP (parser); os
                    // demais (status/mensagem/ids/data de matching) ficam NULL neste INSERT — só recebem
                    // valor depois, via UPDATE do CronogramaTipo2LdpSyncService, sempre restrito ao
                    // snapshot atual (ID_Cronograma exato).
                    cmd.Parameters.Add("@TerminoLinhaBase", SqlDbType.DateTime2).Value = (object)registro.TerminoLinhaBase ?? DBNull.Value;
                    cmd.Parameters.Add("@Termino", SqlDbType.DateTime2).Value = (object)registro.Termino ?? DBNull.Value;
                    cmd.Parameters.Add("@StatusMatchingLdpGate", SqlDbType.NVarChar, 100).Value = (object)registro.StatusMatchingLdpGate ?? DBNull.Value;
                    cmd.Parameters.Add("@MensagemMatchingLdpGate", SqlDbType.NVarChar, 1000).Value = (object)registro.MensagemMatchingLdpGate ?? DBNull.Value;
                    cmd.Parameters.Add("@LdpGateIdMatch", SqlDbType.BigInt).Value = (object)registro.LdpGateIdMatch ?? DBNull.Value;
                    cmd.Parameters.Add("@LdpRegistroIdMatch", SqlDbType.BigInt).Value = (object)registro.LdpRegistroIdMatch ?? DBNull.Value;
                    cmd.Parameters.Add("@DataMatchingLdpGate", SqlDbType.DateTime2).Value = (object)registro.DataMatchingLdpGate ?? DBNull.Value;

                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    totalInserido++;
                }
            }

            return totalInserido;
        }

        public async Task<IReadOnlyList<GateLdpParaMatching>> ObterGatesLdpParaMatchingAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            const string query = @"
                SELECT Numero_Documento_NA, Numero_Documento_Cliente, Gate_Percentual, Gate_Ativo
                FROM dbo.VW_Ldp_Documento_Gates_Ultima_Versao
                WHERE Project_ID_ACC = @ProjectIdAcc";

            var resultado = new List<GateLdpParaMatching>();

            // Consulta isolada, própria conexão — somente leitura, nunca parte da transação de escrita
            // do cronograma (o matching acontece só DEPOIS do commit, ver CronogramaTipo2Service). Nunca
            // gera UPDATE — a view já filtra projeto ativo + Tipo_LDP = 2 + última versão de cada gate.
            using (var connection = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 200).Value = projectIdAcc;

                await connection.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        resultado.Add(new GateLdpParaMatching
                        {
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
    }
}
