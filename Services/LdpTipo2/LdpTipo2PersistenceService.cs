using System;
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
    /// Orquestra a Fase 3: valida o LdpTipo2Resultado e CADA documento antes de tocar no banco (falha
    /// controlada, nada é persistido se algo estiver errado) e, só então, abre UMA SqlConnection + UMA
    /// SqlTransaction para todo o arquivo/projeto, insere a linha principal de cada documento
    /// (ILdpTipo2Repository), atribui o ID_Registro real devolvido aos três gates daquele documento e
    /// os insere (ILdpTipo2GateRepository.InserirLoteNaTransacaoAsync) — tudo na MESMA transação.
    /// Qualquer falha em qualquer documento/gate provoca rollback total: nenhuma linha principal ou
    /// gate desta execução permanece parcialmente gravado.
    /// </summary>
    public sealed class LdpTipo2PersistenceService : ILdpTipo2PersistenceService
    {
        private readonly string _connectionString;
        private readonly ILogger<LdpTipo2PersistenceService> _logger;
        private readonly ILdpTipo2Repository _repository;
        private readonly ILdpTipo2GateRepository _gateRepository;

        public LdpTipo2PersistenceService(
            IConfiguration configuration,
            ILogger<LdpTipo2PersistenceService> logger,
            ILdpTipo2Repository repository,
            ILdpTipo2GateRepository gateRepository)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
            _repository = repository;
            _gateRepository = gateRepository;
        }

        public async Task<LdpTipo2PersistenceResult> PersistirAsync(
            LdpTipo2Resultado resultadoLeitura,
            CancellationToken cancellationToken = default)
        {
            // Única DataSincronizacao para TODO o arquivo (nunca uma por linha) — necessário para
            // reconstruir o snapshot da execução em Ldp_Unificada.Data_Sincronizacao e em
            // Ldp_Documento_Gates.Data_Sincronizacao.
            DateTime dataSincronizacao = DateTime.UtcNow;
            var resultado = new LdpTipo2PersistenceResult { DataSincronizacao = dataSincronizacao };

            if (resultadoLeitura == null)
            {
                resultado.Erros.Add("resultadoLeitura nulo.");
                _logger.LogError("[LDP TIPO 2 SQL] Persistência ABORTADA: resultadoLeitura nulo.");
                return resultado;
            }

            resultado.DocumentosRecebidos = resultadoLeitura.Documentos?.Count ?? 0;

            // ---- Validações de nível "resultado" — nenhuma delas toca no banco. ----
            if (string.IsNullOrWhiteSpace(resultadoLeitura.ProjectIdAcc))
                resultado.Erros.Add("ProjectIdAcc não preenchido no resultado da leitura.");

            if (resultadoLeitura.Documentos == null)
                resultado.Erros.Add("Lista de documentos nula.");

            if (resultadoLeitura.Erros != null && resultadoLeitura.Erros.Count > 0)
            {
                resultado.Erros.Add($"Resultado da leitura contém {resultadoLeitura.Erros.Count} erro(s) crítico(s) de layout — persistência abortada.");
                resultado.Erros.AddRange(resultadoLeitura.Erros);
            }

            if (resultado.Erros.Count > 0)
            {
                _logger.LogError(
                    $"❌ [LDP TIPO 2 SQL] Persistência ABORTADA antes de qualquer INSERT | Projeto: {resultadoLeitura.ProjectIdAcc} | " +
                    $"{resultado.Erros.Count} erro(s): {string.Join(" | ", resultado.Erros)}");
                return resultado;
            }

            // ---- Validação de CADA documento — ainda sem tocar no banco (seção 6/16 do pedido). ----
            foreach (var documento in resultadoLeitura.Documentos)
            {
                if (!ValidarDocumento(documento, out string motivo))
                {
                    resultado.Erros.Add(motivo);
                }
            }

            if (resultado.Erros.Count > 0)
            {
                _logger.LogError(
                    $"❌ [LDP TIPO 2 SQL] Persistência ABORTADA — {resultado.Erros.Count} documento(s) inválido(s) | Projeto: {resultadoLeitura.ProjectIdAcc} | " +
                    $"Motivos: {string.Join(" | ", resultado.Erros.Take(10))}{(resultado.Erros.Count > 10 ? " | ..." : "")}");
                return resultado;
            }

            _logger.LogInformation(
                $"[LDP TIPO 2 SQL] Projeto: {resultadoLeitura.ProjectIdAcc} | Arquivo: {resultadoLeitura.NomeArquivo} | " +
                $"DataSincronizacao: {dataSincronizacao:O} | Documentos recebidos: {resultado.DocumentosRecebidos}");

            // ---- Persistência real: UMA conexão + UMA transação para TODO o resultado. ----
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var documento in resultadoLeitura.Documentos)
                        {
                            long idRegistro = await _repository.InserirDocumentoPrincipalAsync(
                                connection, transaction, documento, resultadoLeitura.ProjectIdAcc, dataSincronizacao, cancellationToken);

                            resultado.DocumentosInseridos++;

                            // Valores AUTORITATIVOS dos gates: nunca confiamos apenas no que o parser
                            // preencheu (LdpTipo2Parser roda antes de qualquer INSERT e não conhece o
                            // ID real da linha principal). Sobrescrevemos aqui, sempre DEPOIS do INSERT
                            // bem-sucedido acima, a partir da própria linha principal já persistida —
                            // isso GARANTE por construção (não apenas verifica) que o N&A/Cliente/Projeto
                            // gravados nos três gates são exatamente os do documento principal.
                            foreach (var gate in documento.Gates)
                            {
                                gate.LdpRegistroId = idRegistro;
                                gate.ProjectIdAcc = resultadoLeitura.ProjectIdAcc;
                                gate.NumeroDocumentoNa = documento.NumeroDocumentoNa;
                                gate.NumeroDocumentoCliente = documento.NumeroDocumentoCliente;
                                gate.DataSincronizacao = dataSincronizacao;
                            }

                            // Confirmação explícita (não apenas confiar na atribuição acima): o N&A de
                            // cada gate precisa ser idêntico ao do documento principal antes do INSERT.
                            if (documento.Gates.Any(g => !string.Equals(g.NumeroDocumentoNa, documento.NumeroDocumentoNa, StringComparison.Ordinal)))
                            {
                                throw new InvalidOperationException(
                                    $"Inconsistência de N&A entre documento e gate na linha Excel {documento.LinhaExcel} ('{documento.NumeroDocumentoNa}').");
                            }

                            await _gateRepository.InserirLoteNaTransacaoAsync(connection, transaction, documento.Gates, cancellationToken);

                            resultado.GatesInseridos += documento.Gates.Count;
                            resultado.GatesAtivosInseridos += documento.Gates.Count(g => g.GateAtivo);
                            resultado.GatesInativosInseridos += documento.Gates.Count(g => !g.GateAtivo);

                            _logger.LogDebug(
                                $"[LDP TIPO 2 SQL] Linha Excel: {documento.LinhaExcel} | Número N&A: {documento.NumeroDocumentoNa} | ID principal: {idRegistro} | " +
                                $"Gate 30 inserido: {documento.Gates.Any(g => g.GatePercentual == 30)} | " +
                                $"Gate 60 inserido: {documento.Gates.Any(g => g.GatePercentual == 60)} | " +
                                $"Gate 90 inserido: {documento.Gates.Any(g => g.GatePercentual == 90)}");
                        }

                        // Validação obrigatória (seção 5 do pedido) antes do commit.
                        int esperado = resultado.DocumentosInseridos * 3;
                        if (resultado.GatesInseridos != esperado)
                        {
                            throw new InvalidOperationException(
                                $"Erro de consistência: GatesInseridos ({resultado.GatesInseridos}) != DocumentosInseridos * 3 ({esperado}).");
                        }

                        transaction.Commit();
                        resultado.Sucesso = true;

                        _logger.LogInformation(
                            $"✅ [LDP TIPO 2 SQL] COMMIT | Projeto: {resultadoLeitura.ProjectIdAcc} | Arquivo: {resultadoLeitura.NomeArquivo} | " +
                            $"DataSincronizacao: {dataSincronizacao:O} | Documentos inseridos: {resultado.DocumentosInseridos} | " +
                            $"Gates inseridos: {resultado.GatesInseridos} (ativos: {resultado.GatesAtivosInseridos}, inativos: {resultado.GatesInativosInseridos})");
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception exRollback)
                        {
                            _logger.LogError(exRollback, "❌ [LDP TIPO 2 SQL] Falha adicional ao executar o rollback.");
                        }

                        resultado.Sucesso = false;
                        resultado.DocumentosInseridos = 0;
                        resultado.GatesInseridos = 0;
                        resultado.GatesAtivosInseridos = 0;
                        resultado.GatesInativosInseridos = 0;
                        resultado.Erros.Add(ex.Message);

                        _logger.LogError(
                            ex,
                            $"❌ [LDP TIPO 2 SQL] ROLLBACK | Projeto: {resultadoLeitura.ProjectIdAcc} | Arquivo: {resultadoLeitura.NomeArquivo} | " +
                            $"DataSincronizacao: {dataSincronizacao:O} — nenhuma linha principal ou gate desta execução foi mantido.");
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Valida um documento antes de qualquer INSERT (seção 6/16 do pedido): número N&A preenchido,
        /// exatamente três gates, cada gate com percentual em {30,60,90} e sem percentual duplicado.
        /// </summary>
        private static bool ValidarDocumento(LdpTipo2Documento documento, out string motivo)
        {
            if (documento == null)
            {
                motivo = "documento nulo na lista de documentos.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(documento.NumeroDocumentoNa))
            {
                motivo = $"Linha Excel {documento.LinhaExcel}: número N&A vazio — documento não será persistido.";
                return false;
            }

            int totalGates = documento.Gates?.Count ?? 0;
            if (totalGates != 3)
            {
                motivo = $"Linha Excel {documento.LinhaExcel} ({documento.NumeroDocumentoNa}): esperado exatamente 3 gates, encontrado {totalGates}.";
                return false;
            }

            var percentuais = documento.Gates.Select(g => g.GatePercentual).ToList();

            if (percentuais.Any(p => !LdpTipo2Gate.GateValido(p)))
            {
                motivo = $"Linha Excel {documento.LinhaExcel} ({documento.NumeroDocumentoNa}): gate percentual inválido ({string.Join(",", percentuais)}) — só 30/60/90 são aceitos.";
                return false;
            }

            if (percentuais.Distinct().Count() != 3)
            {
                motivo = $"Linha Excel {documento.LinhaExcel} ({documento.NumeroDocumentoNa}): gate percentual duplicado ({string.Join(",", percentuais)}).";
                return false;
            }

            motivo = null;
            return true;
        }
    }
}
