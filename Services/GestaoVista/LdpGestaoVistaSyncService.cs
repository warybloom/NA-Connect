using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Orquestrador da Fase de ingestão da Gestão à Vista. Fluxo: extrair (puro) -> validar -> checar
    /// disponibilidade do schema [gestao] -> Custo Real -> Custo Previsto -> Timesheet -> resultado.
    /// Correção 17/09/2026 (regra arquitetural definitiva): a ETAPA 03
    /// (Etapa03TimesheetSnapshotService), que roda antes deste serviço no ciclo do Worker, é ADITIVA —
    /// grava gestao.TimesheetSnapshotAtual (estado ATUAL, delete-and-replace por projeto) SEM substituir
    /// nem desligar esta persistência. Este serviço CONTINUA gravando gestao.TimesheetLancamento (ledger
    /// HISTÓRICO, append-only por RowHash) exatamente como sempre gravou — as duas tabelas têm
    /// finalidades diferentes e coexistem; nunca "dupla persistência problemática". Nunca lança exceção
    /// para o chamador (GrdAutomationWorker também envolve a chamada em try/catch, como camada extra de
    /// segurança — mas este serviço já se protege sozinho). Nunca cria schema/tabela — se
    /// SchemaDisponivelAsync()==false, registra GESTAO_VISTA_SCHEMA_NAO_DISPONIVEL e sai sem tentar
    /// persistir nada.
    /// </summary>
    public sealed class LdpGestaoVistaSyncService : ILdpGestaoVistaSyncService
    {
        private readonly string _connectionString;
        private readonly ILogger<LdpGestaoVistaSyncService> _logger;
        private readonly IGestaoVistaExtractor _extractor;
        private readonly IGestaoVistaRepository _repository;

        public LdpGestaoVistaSyncService(
            IConfiguration configuration,
            ILogger<LdpGestaoVistaSyncService> logger,
            IGestaoVistaExtractor extractor,
            IGestaoVistaRepository repository)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
            _extractor = extractor;
            _repository = repository;
        }

        public async Task<GestaoVistaSyncResultado> SincronizarAsync(
            string projectIdAcc, Stream workbook, string nomeArquivo, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            var resultado = new GestaoVistaSyncResultado
            {
                ProjectIdAcc = projectIdAcc,
                NomeArquivo = nomeArquivo,
                DataSincronizacao = dataSincronizacao,
            };

            try
            {
                if (string.IsNullOrWhiteSpace(projectIdAcc))
                {
                    resultado.Erros.Add("[GESTÃO À VISTA] projectIdAcc não pode ser vazio.");
                    resultado.Sucesso = false;
                    return resultado;
                }

                var extracao = _extractor.Processar(workbook, projectIdAcc, nomeArquivo);
                resultado.Worksheets = extracao.Worksheets;
                resultado.Avisos.AddRange(extracao.Avisos);
                resultado.Erros.AddRange(extracao.Erros);

                if (extracao.Erros.Count > 0)
                {
                    _logger.LogError("[GESTÃO À VISTA] Extração falhou de forma crítica | Projeto: {Projeto} | Arquivo: {Arquivo} — persistência não será tentada.", projectIdAcc, nomeArquivo);
                    resultado.Sucesso = false;
                    return resultado;
                }

                CalcularContagensLidas(extracao, resultado);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                bool schemaDisponivel = await _repository.SchemaDisponivelAsync(connection, cancellationToken);
                resultado.SchemaDisponivel = schemaDisponivel;

                if (!schemaDisponivel)
                {
                    const string marcador = "GESTAO_VISTA_SCHEMA_NAO_DISPONIVEL";
                    _logger.LogWarning(
                        "[GESTÃO À VISTA] {Marcador} | Projeto: {Projeto} | Schema [gestao] ou alguma das 3 tabelas ainda não existe neste banco — migrations não são executadas automaticamente. Extração concluída, nada foi persistido.",
                        marcador, projectIdAcc);
                    resultado.Avisos.Add($"[GESTÃO À VISTA] {marcador}");
                    resultado.Sucesso = false;
                    return resultado;
                }

                using var transaction = connection.BeginTransaction();
                try
                {
                    await PersistirCustoRealAsync(connection, transaction, projectIdAcc, extracao, dataSincronizacao, resultado, cancellationToken);
                    await PersistirCustoPrevistoAsync(connection, transaction, projectIdAcc, extracao, dataSincronizacao, resultado, cancellationToken);

                    // Correção 17/09/2026 (regra arquitetural definitiva): a ETAPA 03
                    // (Services/GestaoVista/Etapa03TimesheetSnapshotService.cs) é ADITIVA — grava
                    // gestao.TimesheetSnapshotAtual (estado ATUAL) SEM substituir esta persistência.
                    // PersistirTimesheetAsync CONTINUA gravando gestao.TimesheetLancamento (ledger
                    // HISTÓRICO, append-only por RowHash) exatamente como sempre gravou — nunca "dupla
                    // persistência problemática": são tabelas diferentes, com finalidades diferentes,
                    // ambas precisam continuar alimentadas.
                    await PersistirTimesheetAsync(connection, transaction, projectIdAcc, extracao, dataSincronizacao, resultado, cancellationToken);

                    transaction.Commit();
                    resultado.Sucesso = true;

                    _logger.LogInformation(
                        "[GESTÃO À VISTA] Sincronização concluída | Projeto: {Projeto} | Arquivo: {Arquivo} | " +
                        "CustoReal: inseridos={CrIns} novasVersoes={CrNovas} idempotentes={CrIdem} | " +
                        "CustoPrevisto: inseridos={CpIns} idempotentes={CpIdem} | " +
                        "Timesheet: inseridos={TsIns} idempotentes={TsIdem}",
                        projectIdAcc, nomeArquivo,
                        resultado.CustoReal.Inseridos, resultado.CustoReal.NovasVersoes, resultado.CustoReal.IgnoradosIdempotentes,
                        resultado.CustoPrevisto.Inseridos, resultado.CustoPrevisto.IgnoradosIdempotentes,
                        resultado.Timesheet.Inseridos, resultado.Timesheet.IgnoradosIdempotentes);
                }
                catch (Exception ex)
                {
                    try { transaction.Rollback(); }
                    catch (Exception exRollback)
                    {
                        _logger.LogError(exRollback, "[GESTÃO À VISTA] Falha adicional ao executar o rollback.");
                    }

                    _logger.LogError(ex, "[GESTÃO À VISTA] ROLLBACK | Projeto: {Projeto} | Arquivo: {Arquivo} — nenhum dado desta execução foi mantido.", projectIdAcc, nomeArquivo);
                    resultado.Erros.Add($"[GESTÃO À VISTA] Falha na persistência: {ex.Message}");
                    resultado.Sucesso = false;
                }

                return resultado;
            }
            catch (Exception ex)
            {
                // Última rede de segurança: NENHUMA exceção desta função escapa para o Worker — a
                // sincronização operacional da LDP nunca pode falhar por causa da Gestão à Vista.
                _logger.LogError(ex, "[GESTÃO À VISTA] Falha inesperada não tratada nas camadas internas | Projeto: {Projeto} | Arquivo: {Arquivo}.", projectIdAcc, nomeArquivo);
                resultado.Erros.Add($"[GESTÃO À VISTA] Falha inesperada: {ex.Message}");
                resultado.Sucesso = false;
                return resultado;
            }
        }

        private static void CalcularContagensLidas(GestaoVistaExtracaoResultado extracao, GestaoVistaSyncResultado resultado)
        {
            var diagCustoReal = extracao.Worksheets.Find(d => d.PapelIdentificado == "CustoReal");
            var diagCustoPrevisto = extracao.Worksheets.Find(d => d.PapelIdentificado == "CustoProfissionais");
            var diagTimesheet = extracao.Worksheets.Find(d => d.PapelIdentificado == "Timesheet");

            resultado.CustoReal.LinhasLidas = diagCustoReal?.QuantidadeLinhas ?? 0;
            resultado.CustoPrevisto.LinhasLidas = diagCustoPrevisto?.QuantidadeLinhas ?? 0;
            resultado.Timesheet.LinhasLidas = diagTimesheet?.QuantidadeLinhas ?? 0;
        }

        private async Task PersistirCustoRealAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaExtracaoResultado extracao, DateTime dataSincronizacao, GestaoVistaSyncResultado resultado, CancellationToken cancellationToken)
        {
            foreach (var registro in extracao.CustoReal)
            {
                if (registro.CustoHora == null)
                {
                    resultado.CustoReal.LinhasInvalidasIgnoradas++;
                    continue;
                }

                resultado.CustoReal.LinhasValidas++;

                var outcome = await _repository.PersistirCustoRealAsync(connection, transaction, projectIdAcc, registro, dataSincronizacao, cancellationToken);
                switch (outcome)
                {
                    case GestaoVistaCustoRealOutcome.PrimeiraVersao:
                        resultado.CustoReal.Inseridos++;
                        break;
                    case GestaoVistaCustoRealOutcome.NovaVersao:
                        resultado.CustoReal.NovasVersoes++;
                        break;
                    case GestaoVistaCustoRealOutcome.MesmaVersao:
                        resultado.CustoReal.IgnoradosIdempotentes++;
                        break;
                }
            }
        }

        private async Task PersistirCustoPrevistoAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaExtracaoResultado extracao, DateTime dataSincronizacao, GestaoVistaSyncResultado resultado, CancellationToken cancellationToken)
        {
            foreach (var registro in extracao.CustoPrevisto)
            {
                if (registro.CustoHora == null)
                {
                    resultado.CustoPrevisto.LinhasInvalidasIgnoradas++;
                    continue;
                }

                resultado.CustoPrevisto.LinhasValidas++;

                bool inserido = await _repository.PersistirCustoPrevistoAsync(connection, transaction, projectIdAcc, registro, dataSincronizacao, cancellationToken);
                if (inserido) resultado.CustoPrevisto.Inseridos++;
                else resultado.CustoPrevisto.IgnoradosIdempotentes++;
            }
        }

        private async Task PersistirTimesheetAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaExtracaoResultado extracao, DateTime dataSincronizacao, GestaoVistaSyncResultado resultado, CancellationToken cancellationToken)
        {
            foreach (var registro in extracao.Timesheet)
            {
                resultado.Timesheet.LinhasValidas++;

                DateTime dataReferencia = registro.DataLancamento ?? dataSincronizacao;
                decimal? custoHoraVigente = await _repository.ObterCustoHoraVigenteAsync(
                    connection, transaction, projectIdAcc, registro.NomeProfissional, dataReferencia, cancellationToken);

                decimal? custoCalculado = null;
                if (custoHoraVigente == null)
                {
                    resultado.Avisos.Add($"[GESTÃO À VISTA][TIMESHEET] Linha {registro.LinhaExcel} ({registro.NomeProfissional}): sem custo/hora vigente para a data de referência — CustoCalculado ficará NULL.");
                }
                else if (registro.HorasTrabalhadas.HasValue)
                {
                    custoCalculado = registro.HorasTrabalhadas.Value * custoHoraVigente.Value;
                }

                bool inserido = await _repository.PersistirTimesheetAsync(connection, transaction, projectIdAcc, registro, custoCalculado, dataSincronizacao, cancellationToken);
                if (inserido) resultado.Timesheet.Inseridos++;
                else resultado.Timesheet.IgnoradosIdempotentes++;
            }
        }
    }
}
