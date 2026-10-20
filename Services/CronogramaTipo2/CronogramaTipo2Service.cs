using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpRouting;

namespace APS.WebApp.Services.CronogramaTipo2
{
    public sealed class CronogramaTipo2Service : ICronogramaTipo2Service
    {
        private readonly string _connectionString;
        private readonly ILogger<CronogramaTipo2Service> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly AccScheduleSyncService _scheduleSyncService;
        private readonly ILdpProjetoRepository _projetoRepository;
        private readonly ICronogramaTipo2Parser _parser;
        private readonly ICronogramaTipo2Repository _repository;

        public CronogramaTipo2Service(
            IConfiguration configuration,
            ILogger<CronogramaTipo2Service> logger,
            AutodeskApiService autodeskApi,
            AccScheduleSyncService scheduleSyncService,
            ILdpProjetoRepository projetoRepository,
            ICronogramaTipo2Parser parser,
            ICronogramaTipo2Repository repository)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
            _autodeskApi = autodeskApi;
            _scheduleSyncService = scheduleSyncService;
            _projetoRepository = projetoRepository;
            _parser = parser;
            _repository = repository;
        }

        public async Task<CronogramaTipo2Resultado> ProcessarAsync(
            string hubId,
            string projectId,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            var dataSincronizacao = DateTime.UtcNow;
            var resultado = new CronogramaTipo2Resultado { ProjectIdAcc = projectId, DataSincronizacao = dataSincronizacao };

            // 1. Tipo_LDP nunca vem do MPP nem é inventado — sempre de dbo.Projetos, através do mesmo
            // repositório já usado pelo roteamento Tipo 1/Tipo 2 (ILdpProjetoRepository).
            Projeto projeto;
            try
            {
                projeto = await _projetoRepository.ObterProjetoAtivoAsync(projectId, cancellationToken);
            }
            catch (Exception ex)
            {
                resultado.Erros.Add($"Falha ao consultar dbo.Projetos para '{projectId}': {ex.Message}");
                _logger.LogError(ex, $"[CRONOGRAMA TIPO 2] Falha ao consultar Tipo_LDP do projeto {projectId}.");
                return resultado;
            }

            if (projeto == null)
            {
                resultado.Erros.Add($"Projeto '{projectId}' não encontrado ou não está ativo em dbo.Projetos.");
                _logger.LogError($"[CRONOGRAMA TIPO 2] {resultado.Erros[^1]}");
                return resultado;
            }

            if (projeto.TipoLdp != TipoLdp.Tipo2)
            {
                resultado.Erros.Add(
                    $"Projeto '{projectId}' tem Tipo_LDP = {(byte)projeto.TipoLdp}, mas este módulo só processa Tipo_LDP = 2 (Tipo2). Processamento abortado, nada foi persistido.");
                _logger.LogError($"[CRONOGRAMA TIPO 2] {resultado.Erros[^1]}");
                return resultado;
            }

            byte tipoLdp = (byte)TipoLdp.Tipo2;

            // 2. Localiza e baixa o .mpp — reaproveita os mesmos métodos públicos já usados pelo
            // cronograma Tipo 1 (AccScheduleSyncService.FindFolderByPaths / AutodeskApiService), nunca
            // reimplementados aqui.
            string mppPath = Path.Combine(Path.GetTempPath(), $"CRONO_T2_{Guid.NewGuid()}.mpp");
            string nomeArquivo = null;

            try
            {
                var folderSchId = await _scheduleSyncService.FindFolderByPaths(hubId, projectId, new[]
                {
                    new[] { "01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO" },
                    new[] { "01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES" }
                }, accessToken);

                if (string.IsNullOrEmpty(folderSchId))
                {
                    resultado.Erros.Add("Pasta de Cronograma não encontrada no ACC.");
                    return resultado;
                }

                var schRes = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderSchId, ".mpp", accessToken);
                if (string.IsNullOrEmpty(schRes.FileId))
                {
                    resultado.Erros.Add("Arquivo .mpp não localizado na pasta de planejamento.");
                    return resultado;
                }

                var mppDetails = await _autodeskApi.GetItemDetailsAsync(projectId, schRes.FileId, accessToken);
                string mppVersionUrn = mppDetails?.TipVersionUrn ?? schRes.FileId;
                nomeArquivo = mppDetails?.DisplayName ?? schRes.FileName ?? "cronograma.mpp";

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, mppVersionUrn, accessToken))
                using (var fs = new FileStream(mppPath, FileMode.Create, FileAccess.Write))
                {
                    await dl.CopyToAsync(fs, cancellationToken);
                }

                // 3. Parser puro (sem SQL/ACC) — download já concluído, leitura em memória.
                resultado = await _parser.ProcessarAsync(mppPath, projectId, tipoLdp, nomeArquivo, dataSincronizacao, cancellationToken);
            }
            catch (Exception ex)
            {
                resultado.Erros.Add($"Falha ao localizar/baixar/ler o cronograma: {ex.Message}");
                _logger.LogError(ex, $"[CRONOGRAMA TIPO 2] Falha ao localizar/baixar/ler o cronograma do projeto {projectId}.");
                return resultado;
            }
            finally
            {
                if (File.Exists(mppPath)) File.Delete(mppPath);
            }

            if (resultado.Erros.Count > 0)
            {
                _logger.LogError(
                    $"[CRONOGRAMA TIPO 2] Leitura com {resultado.Erros.Count} erro(s) — persistência NÃO será executada. Projeto: {projectId}.");
                return resultado;
            }

            _logger.LogInformation(
                $"[CRONOGRAMA TIPO 2] Projeto: {projeto.NomeProjeto} | Project_ID_ACC: {projectId} | Tipo_LDP: {tipoLdp} | Arquivo: {resultado.NomeArquivo} | " +
                $"DataSincronizacao: {dataSincronizacao:O} | Total de tarefas: {resultado.TarefasAnalisadas}");

            // 4. Persistência transacional: uma única conexão + transação para TODO o arquivo.
            var resultadoPersistencia = await PersistirAsync(projectId, resultado, cancellationToken);

            if (!resultadoPersistencia.Sucesso)
            {
                resultado.Erros.AddRange(resultadoPersistencia.Erros.Select(e => $"[Persistência] {e}"));
                return resultado;
            }

            // 5. Matching SOMENTE LEITURA (nunca UPDATE) — só depois do commit.
            try
            {
                await ExecutarMatchingAsync(projectId, resultado, cancellationToken);
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Falha ao executar o matching de teste (não afeta os dados já persistidos): {ex.Message}");
                _logger.LogError(ex, $"[CRONOGRAMA TIPO 2] Falha ao executar matching de teste para o projeto {projectId}.");
            }

            return resultado;
        }

        /// <summary>
        /// Uma SqlConnection + uma SqlTransaction para TODO o resultado (seção 16 do pedido). Qualquer
        /// falha reverte a transação inteira — nenhum registro parcial desta execução permanece.
        /// </summary>
        private async Task<CronogramaTipo2PersistenceResult> PersistirAsync(
            string projectIdAcc,
            CronogramaTipo2Resultado resultadoLeitura,
            CancellationToken cancellationToken)
        {
            var resultado = new CronogramaTipo2PersistenceResult
            {
                DataSincronizacao = resultadoLeitura.DataSincronizacao,
                RegistrosRecebidos = resultadoLeitura.Registros.Count
            };

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int inseridos = await _repository.InserirLoteAsync(connection, transaction, projectIdAcc, resultadoLeitura.Registros, cancellationToken);

                        if (inseridos != resultadoLeitura.Registros.Count)
                        {
                            throw new InvalidOperationException(
                                $"Erro de consistência: {inseridos} registro(s) inserido(s), esperado {resultadoLeitura.Registros.Count}.");
                        }

                        transaction.Commit();
                        resultado.Sucesso = true;
                        resultado.RegistrosInseridos = inseridos;

                        _logger.LogInformation(
                            $"✅ [CRONOGRAMA TIPO 2] COMMIT | Projeto: {projectIdAcc} | Registros inseridos: {inseridos} | " +
                            $"Resumo: TarefasPersistidas={resultadoLeitura.TarefasPersistiveis} | TarefasResumo={resultadoLeitura.TarefasResumo} | " +
                            $"SemN&A={resultadoLeitura.TarefasSemNumeroNa} | SemFase={resultadoLeitura.TarefasSemFase} | " +
                            $"FaseInvalida={resultadoLeitura.TarefasComFaseInvalida} | Validas={resultadoLeitura.RegistrosValidos} | " +
                            $"Duplicadas={resultadoLeitura.RegistrosDuplicados}.");
                    }
                    catch (Exception ex)
                    {
                        try { transaction.Rollback(); }
                        catch (Exception exRollback) { _logger.LogError(exRollback, "❌ [CRONOGRAMA TIPO 2] Falha adicional ao executar o rollback."); }

                        resultado.Sucesso = false;
                        resultado.Erros.Add(ex.Message);

                        _logger.LogError(
                            ex,
                            $"❌ [CRONOGRAMA TIPO 2] ROLLBACK | Projeto: {projectIdAcc} — nenhum registro desta execução foi mantido.");
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Matching de teste (seção 17 do pedido): SOMENTE LEITURA, nunca gera UPDATE em
        /// dbo.Ldp_Documento_Gates. Compara apenas Project_ID_ACC + Numero_Documento_NA (com tolerância
        /// controlada de sufixo de revisão física — nunca Contains irrestrito) + Gate_Percentual.
        /// </summary>
        private async Task ExecutarMatchingAsync(string projectIdAcc, CronogramaTipo2Resultado resultado, CancellationToken cancellationToken)
        {
            var gatesLdp = await _repository.ObterGatesLdpParaMatchingAsync(projectIdAcc, cancellationToken);

            int encontrados = 0, naoEncontrados = 0, inativos = 0, ambiguos = 0;

            foreach (var registro in resultado.Registros.Where(r => r.StatusValidacaoDocumentoGate == CronogramaTipo2Parser.Status.Valido))
            {
                string classificacao = ClassificarMatch(registro, gatesLdp);

                switch (classificacao)
                {
                    case "MATCH_ENCONTRADO": encontrados++; break;
                    case "MATCH_NAO_ENCONTRADO": naoEncontrados++; break;
                    case "GATE_INATIVO_NA_LDP": inativos++; break;
                    case "MATCH_AMBIGUO": ambiguos++; break;
                }

                _logger.LogDebug(
                    $"[CRONOGRAMA TIPO 2] Matching | N&A: {registro.NumeroDocumentoNaNormalizado} | Gate: {registro.GatePercentual}% | Resultado: {classificacao}");
            }

            resultado.MatchesEncontrados = encontrados;
            resultado.MatchesNaoEncontrados = naoEncontrados;
            resultado.GatesInativos = inativos;
            resultado.MatchesAmbiguos = ambiguos;

            _logger.LogInformation(
                $"[CRONOGRAMA TIPO 2] MATCHING (somente leitura, nenhum UPDATE) | Projeto: {projectIdAcc} | " +
                $"Matches encontrados: {encontrados} | Matches não encontrados: {naoEncontrados} | " +
                $"Gates inativos na LDP: {inativos} | Matches ambíguos: {ambiguos}.");
        }

        /// <summary>
        /// Matching de teste com fallback N&amp;A -&gt; Client Document Number: primeiro tenta achar a
        /// linha da LDP pelo N&amp;A; só se nenhum candidato bater é que tenta pelo Client Document
        /// Number da mesma linha (nunca os dois ao mesmo tempo — N&amp;A e Client são namespaces
        /// diferentes que nunca coincidem, ver CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching,
        /// que implementa a mesma regra para o matching REAL/persistido).
        /// </summary>
        private static string ClassificarMatch(CronogramaTipo2Registro registro, System.Collections.Generic.IReadOnlyList<GateLdpParaMatching> gatesDoProjeto)
        {
            var candidatosDoGate = gatesDoProjeto.Where(g => g.GatePercentual == registro.GatePercentual).ToList();

            var candidatos = candidatosDoGate
                .Where(g => CronogramaTipo2DocumentNumberNormalizer.CorrespondeComRevisaoFisica(
                    registro.NumeroDocumentoNaNormalizado,
                    CronogramaTipo2DocumentNumberNormalizer.Normalizar(g.NumeroDocumentoNa)))
                .ToList();

            if (candidatos.Count == 0)
            {
                candidatos = candidatosDoGate
                    .Where(g => !string.IsNullOrWhiteSpace(g.NumeroDocumentoCliente))
                    .Where(g => CronogramaTipo2DocumentNumberNormalizer.CorrespondeComRevisaoFisica(
                        registro.NumeroDocumentoNaNormalizado,
                        CronogramaTipo2DocumentNumberNormalizer.Normalizar(g.NumeroDocumentoCliente)))
                    .ToList();
            }

            if (candidatos.Count == 0) return "MATCH_NAO_ENCONTRADO";
            if (candidatos.Count > 1) return "MATCH_AMBIGUO";

            return candidatos[0].GateAtivo ? "MATCH_ENCONTRADO" : "GATE_INATIVO_NA_LDP";
        }
    }
}
