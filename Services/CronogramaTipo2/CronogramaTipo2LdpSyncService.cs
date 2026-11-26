using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>Valores possíveis de Status_Matching_LDP_Gate (seção 13 do pedido da Fase 4B) — nunca
    /// strings soltas espalhadas pelo código. Este status é sobre o RELACIONAMENTO com a LDP; nunca
    /// sobrescreve Status_Validacao_Documento_Gate, que continua representando a validade estrutural da
    /// tarefa (Fase 4A).</summary>
    public static class StatusMatchingLdp
    {
        public const string MatchEncontrado = "MATCH_ENCONTRADO";
        public const string CronogramaNaoEncontrado = "CRONOGRAMA_NAO_ENCONTRADO";
        public const string MatchAmbiguo = "MATCH_AMBIGUO";
        public const string GateInativoSemCronograma = "GATE_INATIVO_SEM_CRONOGRAMA";
        public const string CronogramaParaGateInativo = "CRONOGRAMA_PARA_GATE_INATIVO";
        public const string LdpGateNaoEncontrado = "LDP_GATE_NAO_ENCONTRADO";
    }

    /// <summary>Origem do match de um candidato do cronograma contra uma linha da LDP — nunca uma
    /// terceira opção "ambos", já que N&amp;A e Client Document Number são namespaces que nunca
    /// coincidem (regra de negócio confirmada).</summary>
    public static class OrigemMatchLdp
    {
        public const string Na = "MATCH_NA";
        public const string Cliente = "MATCH_CLIENT";
    }

    /// <summary>Decisão de matching de UM gate da LDP contra os candidatos do cronograma — saída pura de
    /// ResolverDecisoesDeMatching, consumida por SincronizarAsync para persistir.</summary>
    internal sealed class GateMatchDecision
    {
        public GateLdpParaSincronizacao Gate { get; init; }
        public string Status { get; init; }
        public List<CronogramaTipo2Registro> Candidatos { get; init; }

        /// <summary>MATCH_NA, MATCH_CLIENT, ou null quando nenhum candidato foi encontrado.</summary>
        public string OrigemMatch { get; init; }

        /// <summary>true quando o MATCH_AMBIGUO veio de duplicidade de N&amp;A/Client DENTRO da própria
        /// LDP (erro de dado — nunca escolher uma linha arbitrariamente); false quando veio de múltiplas
        /// tarefas do cronograma batendo com uma única linha da LDP (comportamento pré-existente,
        /// inalterado).</summary>
        public bool AmbiguoPorDuplicidadeLdp { get; init; }
    }

    /// <summary>
    /// Implementação real de ICronogramaTipo2LdpSyncService. O X da LDP (Gate_Ativo) é sempre a
    /// autoridade sobre quais gates são aplicáveis — este serviço nunca ativa nem desativa gate, só
    /// alimenta com os valores do cronograma os gates que a LDP já declarou ativos.
    /// </summary>
    public sealed class CronogramaTipo2LdpSyncService : ICronogramaTipo2LdpSyncService
    {
        private readonly string _connectionString;
        private readonly ILogger<CronogramaTipo2LdpSyncService> _logger;
        private readonly ICronogramaTipo2LdpSyncRepository _repository;

        private const int MaxMensagem = 1000;

        public CronogramaTipo2LdpSyncService(
            IConfiguration configuration,
            ILogger<CronogramaTipo2LdpSyncService> logger,
            ICronogramaTipo2LdpSyncRepository repository)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
            _repository = repository;
        }

        public async Task<CronogramaTipo2LdpSyncResult> SincronizarAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default)
        {
            var dataExecucao = DateTime.UtcNow;
            var resultado = new CronogramaTipo2LdpSyncResult { ProjectIdAcc = projectIdAcc, DataExecucao = dataExecucao };

            if (string.IsNullOrWhiteSpace(projectIdAcc))
            {
                resultado.Erros.Add("projectIdAcc não pode ser vazio.");
                return resultado;
            }

            IReadOnlyList<CronogramaTipo2Registro> candidatosCronograma;
            IReadOnlyList<GateLdpParaSincronizacao> gatesLdp;
            try
            {
                candidatosCronograma = await _repository.ObterUltimoSnapshotCronogramaAsync(projectIdAcc, cancellationToken);
                gatesLdp = await _repository.ObterGatesLdpUltimaVersaoAsync(projectIdAcc, cancellationToken);
            }
            catch (Exception ex)
            {
                resultado.Erros.Add($"Falha ao carregar snapshots: {ex.Message}");
                _logger.LogError(ex, $"[CRONOGRAMA TIPO 2 -> LDP] Falha ao carregar snapshot do cronograma ou gates da LDP do projeto {projectIdAcc}.");
                return resultado;
            }

            resultado.GatesLdpAnalisados = gatesLdp.Count;
            resultado.GatesAtivos = gatesLdp.Count(g => g.GateAtivo);
            resultado.GatesInativos = gatesLdp.Count(g => !g.GateAtivo);

            // 1. Matriz de decisão (seção 13 do pedido): iteração pelos GATES DA LDP — nunca Contains
            // irrestrito, sempre CorrespondeComRevisaoFisica (mesma regra controlada da Fase 4A). N&A
            // primeiro, Client Document Number como fallback (seções 8/13/17 da Fase LDP/Cronograma
            // Tipo 2) — lógica pura extraída para ResolverDecisoesDeMatching (testável sem SQL).
            var (acoesGate, candidatosUsados) = ResolverDecisoesDeMatching(gatesLdp, candidatosCronograma);

            resultado.MatchesEncontrados = acoesGate.Count(a => a.Status == StatusMatchingLdp.MatchEncontrado);
            resultado.CronogramasNaoEncontrados = acoesGate.Count(a => a.Status == StatusMatchingLdp.CronogramaNaoEncontrado);
            resultado.MatchesAmbiguos = acoesGate.Count(a => a.Status == StatusMatchingLdp.MatchAmbiguo);
            resultado.CronogramasParaGatesInativos = acoesGate.Count(a => a.Status == StatusMatchingLdp.CronogramaParaGateInativo);

            var candidatosSemGate = candidatosCronograma.Where(c => !candidatosUsados.Contains(c)).ToList();
            resultado.CronogramasSemGateNaLdp = candidatosSemGate.Count;

            // WBS (%) usa exclusivamente Work do snapshot no item de publicacao.
            // EDT_WBS continua estrutural; codigos divergentes entre gates nao bloqueiam Work.

            // 3. Persistência: UMA conexão + UMA transação para toda a execução (seção 17). Qualquer falha
            // reverte todos os UPDATEs (nenhum gate parcial, nenhum status parcial).
            var dataAtualizacao = DateTime.UtcNow;

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var acao in acoesGate)
                        {
                            switch (acao.Status)
                            {
                                case StatusMatchingLdp.MatchEncontrado:
                                    await ProcessarMatchEncontradoAsync(connection, transaction, projectIdAcc, acao.Gate, acao.Candidatos[0], acao.OrigemMatch, dataAtualizacao, resultado, cancellationToken);
                                    break;

                                case StatusMatchingLdp.MatchAmbiguo:
                                    await ProcessarStatusMultiploAsync(
                                        connection, transaction, acao.Gate, acao.Candidatos, StatusMatchingLdp.MatchAmbiguo,
                                        acao.AmbiguoPorDuplicidadeLdp
                                            ? $"Gate {acao.Gate.GatePercentual}%: N&A/Client Document Number duplicado na própria LDP (mais de uma linha compartilha o mesmo número) — ambíguo, nenhum UPDATE realizado. Documento envolvido: '{acao.Gate.NumeroDocumentoNa}' (Client: '{acao.Gate.NumeroDocumentoCliente}')."
                                            : $"Gate {acao.Gate.GatePercentual}% do documento '{acao.Gate.NumeroDocumentoNa}' corresponde a {acao.Candidatos.Count} tarefas do cronograma — ambíguo, nenhum UPDATE realizado. Tarefas envolvidas: " +
                                              string.Join(", ", acao.Candidatos.Select(c => $"UID {c.UidTarefaPersistido} ('{c.NomeTarefa}')")),
                                        "MATCH_AMBIGUO", dataAtualizacao, resultado, cancellationToken);
                                    break;

                                case StatusMatchingLdp.CronogramaParaGateInativo:
                                    await ProcessarStatusMultiploAsync(
                                        connection, transaction, acao.Gate, acao.Candidatos, StatusMatchingLdp.CronogramaParaGateInativo,
                                        $"Gate {acao.Gate.GatePercentual}% do documento '{acao.Gate.NumeroDocumentoNa}' está INATIVO na LDP (sem X), mas {acao.Candidatos.Count} tarefa(s) do cronograma foram encontradas para ele — inconsistência de planejamento, nenhum UPDATE realizado.",
                                        "CRONOGRAMA_PARA_GATE_INATIVO", dataAtualizacao, resultado, cancellationToken);
                                    break;

                                case StatusMatchingLdp.CronogramaNaoEncontrado:
                                    resultado.Avisos.Add(
                                        $"[CRONOGRAMA TIPO 2 -> LDP] CRONOGRAMA_NAO_ENCONTRADO: gate {acao.Gate.GatePercentual}% do documento '{acao.Gate.NumeroDocumentoNa}' está ativo (X) na LDP, mas nenhuma tarefa do cronograma corresponde — valor existente preservado, nenhum UPDATE realizado.");
                                    break;

                                // GATE_INATIVO_SEM_CRONOGRAMA: situação válida (seção 13), nenhuma ação, nenhum aviso.
                            }
                        }

                        foreach (var candidato in candidatosSemGate)
                        {
                            if (!candidato.IdCronograma.HasValue) continue;

                            string mensagem = TruncarMensagem(
                                $"Nenhum gate correspondente encontrado na LDP para N&A '{candidato.NumeroDocumentoNaOriginal}' + Gate {candidato.GatePercentual}%.", MaxMensagem);
                            resultado.Avisos.Add($"[CRONOGRAMA TIPO 2 -> LDP] LDP_GATE_NAO_ENCONTRADO: {mensagem}");

                            await AtualizarStatusCronogramaAsync(
                                connection, transaction, candidato, StatusMatchingLdp.LdpGateNaoEncontrado, mensagem, null, null, dataAtualizacao, cancellationToken);
                        }

                        if (resultado.GatesAtualizadosBanco != resultado.MatchesEncontrados)
                        {
                            throw new InvalidOperationException(
                                $"Erro de consistência: GatesAtualizadosBanco ({resultado.GatesAtualizadosBanco}) != MatchesEncontrados ({resultado.MatchesEncontrados}).");
                        }

                        transaction.Commit();
                        resultado.Sucesso = true;

                        _logger.LogInformation(
                            $"✅ [CRONOGRAMA TIPO 2 -> LDP] COMMIT | Projeto: {projectIdAcc} | Gates LDP analisados: {resultado.GatesLdpAnalisados} (ativos: {resultado.GatesAtivos}, inativos: {resultado.GatesInativos}) | " +
                            $"Matches encontrados: {resultado.MatchesEncontrados} | Não encontrados: {resultado.CronogramasNaoEncontrados} | " +
                            $"Ambíguos: {resultado.MatchesAmbiguos} | Para gate inativo: {resultado.CronogramasParaGatesInativos} | " +
                            $"Sem gate na LDP: {resultado.CronogramasSemGateNaLdp} | Gates atualizados no banco: {resultado.GatesAtualizadosBanco} | " +
                            $"WBS divergente: {resultado.DocumentosComWbsDivergente}.");
                    }
                    catch (Exception ex)
                    {
                        try { transaction.Rollback(); }
                        catch (Exception exRollback) { _logger.LogError(exRollback, "❌ [CRONOGRAMA TIPO 2 -> LDP] Falha adicional ao executar o rollback."); }

                        resultado.Sucesso = false;
                        resultado.GatesAtualizadosBanco = 0;
                        resultado.CelulasDataBaselineAtualizadas = 0;
                        resultado.CelulasDataReplanejadaAtualizadas = 0;
                        resultado.CelulasPlannedPhysicalAtualizadas = 0;
                        resultado.CelulasRealizadoAtualizadas = 0;
                        resultado.Erros.Add(ex.Message);

                        _logger.LogError(ex, $"❌ [CRONOGRAMA TIPO 2 -> LDP] ROLLBACK | Projeto: {projectIdAcc} — nenhum UPDATE desta execução foi mantido.");
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Decisão PURA de matching (sem I/O): para cada gate da LDP, tenta achar candidatos do
        /// cronograma pelo N&amp;A da linha; se nenhum bater, tenta pelo Client Document Number da mesma
        /// linha (fallback — canonicalização para o N&amp;A é automática, pois o candidato é sempre
        /// associado ao "gate" desta iteração, cujo NumeroDocumentoNa já é a identidade canônica da
        /// linha). Como o laço externo já percorre TODAS as linhas da LDP, tentar N&amp;A-depois-Client
        /// linha a linha é equivalente a "buscar entre todos os N&amp;A primeiro, depois entre todos os
        /// Client" — e como N&amp;A e Client nunca coincidem (regra de negócio confirmada), não há ordem
        /// de prioridade a resolver DENTRO da mesma linha entre os dois. Se essa premissa mudar no
        /// futuro, este método precisa ser revisto.
        ///
        /// Também detecta ambiguidade por DUPLICIDADE DENTRO DA PRÓPRIA LDP (duas ou mais linhas com o
        /// mesmo N&amp;A ou o mesmo Client para o mesmo Gate_Percentual) — nunca escolhe uma linha
        /// arbitrariamente; force MATCH_AMBIGUO mesmo que só exista um único candidato do cronograma.
        /// </summary>
        internal static (List<GateMatchDecision> Decisoes, HashSet<CronogramaTipo2Registro> CandidatosUsados) ResolverDecisoesDeMatching(
            IReadOnlyList<GateLdpParaSincronizacao> gatesLdp,
            IReadOnlyList<CronogramaTipo2Registro> candidatosCronograma)
        {
            var naPorGrupo = gatesLdp
                .Select(g => (Gate: g, Na: CronogramaTipo2DocumentNumberNormalizer.Normalizar(g.NumeroDocumentoNa)))
                .Where(x => x.Na.Length > 0)
                .GroupBy(x => (x.Gate.GatePercentual, x.Na))
                .ToDictionary(gr => gr.Key, gr => gr.Select(x => x.Gate.LdpRegistroId).Distinct().Count());

            var clientePorGrupo = gatesLdp
                .Where(g => !string.IsNullOrWhiteSpace(g.NumeroDocumentoCliente))
                .Select(g => (Gate: g, Cliente: CronogramaTipo2DocumentNumberNormalizer.Normalizar(g.NumeroDocumentoCliente)))
                .GroupBy(x => (x.Gate.GatePercentual, x.Cliente))
                .ToDictionary(gr => gr.Key, gr => gr.Select(x => x.Gate.LdpRegistroId).Distinct().Count());

            var candidatosUsados = new HashSet<CronogramaTipo2Registro>();
            var decisoes = new List<GateMatchDecision>();

            foreach (var gate in gatesLdp)
            {
                string naNorm = CronogramaTipo2DocumentNumberNormalizer.Normalizar(gate.NumeroDocumentoNa);
                string clienteNorm = string.IsNullOrWhiteSpace(gate.NumeroDocumentoCliente)
                    ? null
                    : CronogramaTipo2DocumentNumberNormalizer.Normalizar(gate.NumeroDocumentoCliente);

                var candidatosNa = candidatosCronograma
                    .Where(c => c.GatePercentual == gate.GatePercentual)
                    .Where(c => CronogramaTipo2DocumentNumberNormalizer.CorrespondeComRevisaoFisica(naNorm, c.NumeroDocumentoNaNormalizado))
                    .ToList();

                List<CronogramaTipo2Registro> candidatos;
                string origem;
                bool ambiguoLdp;

                if (candidatosNa.Count > 0)
                {
                    candidatos = candidatosNa;
                    origem = OrigemMatchLdp.Na;
                    ambiguoLdp = naPorGrupo.TryGetValue((gate.GatePercentual, naNorm), out int qtdLinhasNa) && qtdLinhasNa > 1;
                }
                else if (clienteNorm != null)
                {
                    var candidatosCliente = candidatosCronograma
                        .Where(c => c.GatePercentual == gate.GatePercentual)
                        .Where(c => CronogramaTipo2DocumentNumberNormalizer.CorrespondeComRevisaoFisica(clienteNorm, c.NumeroDocumentoNaNormalizado))
                        .ToList();

                    candidatos = candidatosCliente;
                    origem = candidatosCliente.Count > 0 ? OrigemMatchLdp.Cliente : null;
                    ambiguoLdp = candidatosCliente.Count > 0
                        && clientePorGrupo.TryGetValue((gate.GatePercentual, clienteNorm), out int qtdLinhasCliente) && qtdLinhasCliente > 1;
                }
                else
                {
                    candidatos = new List<CronogramaTipo2Registro>();
                    origem = null;
                    ambiguoLdp = false;
                }

                string status = gate.GateAtivo
                    ? (ambiguoLdp
                        ? StatusMatchingLdp.MatchAmbiguo
                        : candidatos.Count switch
                        {
                            0 => StatusMatchingLdp.CronogramaNaoEncontrado,
                            1 => StatusMatchingLdp.MatchEncontrado,
                            _ => StatusMatchingLdp.MatchAmbiguo
                        })
                    : (candidatos.Count == 0
                        ? StatusMatchingLdp.GateInativoSemCronograma
                        : StatusMatchingLdp.CronogramaParaGateInativo);

                foreach (var c in candidatos) candidatosUsados.Add(c);
                decisoes.Add(new GateMatchDecision { Gate = gate, Status = status, Candidatos = candidatos, OrigemMatch = origem, AmbiguoPorDuplicidadeLdp = ambiguoLdp });
            }

            return (decisoes, candidatosUsados);
        }

        private async Task ProcessarMatchEncontradoAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string projectIdAcc,
            GateLdpParaSincronizacao gate,
            CronogramaTipo2Registro candidato,
            string origemMatch,
            DateTime dataAtualizacao,
            CronogramaTipo2LdpSyncResult resultado,
            CancellationToken cancellationToken)
        {
            string mensagem = TruncarMensagem(
                $"[{origemMatch}] Gate {gate.GatePercentual}% do documento '{gate.NumeroDocumentoNa}' (Cronograma: '{candidato.NumeroDocumentoNaOriginal}') atualizado a partir da tarefa UID {candidato.UidTarefaPersistido}.",
                MaxMensagem);

            // Ldp_Documento_Gates.Planned_Baseline/Replanned_Date/Planned_Physical/Physical_Completed.
            int linhasGate = await _repository.AtualizarGateAsync(
                connection, transaction,
                gate.Id, projectIdAcc, gate.LdpRegistroId, gate.GatePercentual,
                candidato.TerminoLinhaBase, candidato.Termino, candidato.PercentPrevistoReprogramado, candidato.PercentRealizado,
                candidato.UidTarefaPersistido, candidato.DataSincronizacao, dataAtualizacao,
                StatusMatchingLdp.MatchEncontrado, mensagem, cancellationToken);

            if (linhasGate != 1)
            {
                throw new InvalidOperationException(
                    $"UPDATE de dbo.Ldp_Documento_Gates (Id={gate.Id}) afetou {linhasGate} linha(s), esperado 1 — abortando.");
            }

            resultado.GatesAtualizadosBanco++;
            if (candidato.TerminoLinhaBase.HasValue) resultado.CelulasDataBaselineAtualizadas++;
            if (candidato.Termino.HasValue) resultado.CelulasDataReplanejadaAtualizadas++;
            if (candidato.PercentPrevistoReprogramado.HasValue) resultado.CelulasPlannedPhysicalAtualizadas++;
            if (candidato.PercentRealizado.HasValue) resultado.CelulasRealizadoAtualizadas++;

            // Alimenta a publicação condicional no ACC (item 3.3 do checkpoint) — o SQL já é a fonte de
            // verdade; a planilha só é escrita/publicada depois, e só se algo realmente mudar (diff em
            // CronogramaTipo2LdpPlanilhaAtualizador).
            resultado.AtualizacoesParaPublicar.Add(new CronogramaTipo2LdpPlanilhaAtualizacaoItem
            {
                NumeroDocumentoNaNormalizadoLdp = CronogramaTipo2DocumentNumberNormalizer.Normalizar(gate.NumeroDocumentoNa),
                GatePercentual = gate.GatePercentual,
                TerminoLinhaBase = candidato.TerminoLinhaBase,
                Termino = candidato.Termino,
                PlannedPhysical = candidato.PercentPrevistoReprogramado,
                PercentRealizado = candidato.PercentRealizado,
                WbsPercentPorWork = CronogramaTipo2Work.LerPercentualSnapshot(candidato.CamposCustomizadosJson)
            });

            await AtualizarStatusCronogramaAsync(
                connection, transaction, candidato, StatusMatchingLdp.MatchEncontrado, mensagem, gate.Id, gate.LdpRegistroId, dataAtualizacao, cancellationToken);
        }

        private async Task ProcessarStatusMultiploAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            GateLdpParaSincronizacao gate,
            List<CronogramaTipo2Registro> candidatos,
            string statusParaAviso,
            string mensagemBruta,
            string statusPersistido,
            DateTime dataAtualizacao,
            CronogramaTipo2LdpSyncResult resultado,
            CancellationToken cancellationToken)
        {
            string mensagem = TruncarMensagem(mensagemBruta, MaxMensagem);
            resultado.Avisos.Add($"[CRONOGRAMA TIPO 2 -> LDP] {statusParaAviso}: {mensagem}");

            foreach (var candidato in candidatos)
            {
                if (!candidato.IdCronograma.HasValue) continue;
                await AtualizarStatusCronogramaAsync(connection, transaction, candidato, statusPersistido, mensagem, null, null, dataAtualizacao, cancellationToken);
            }
        }

        private async Task AtualizarStatusCronogramaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            CronogramaTipo2Registro candidato,
            string status,
            string mensagem,
            long? ldpGateId,
            long? ldpRegistroId,
            DateTime dataMatching,
            CancellationToken cancellationToken)
        {
            if (!candidato.IdCronograma.HasValue)
            {
                throw new InvalidOperationException(
                    $"Registro do cronograma sem ID_Cronograma (N&A '{candidato.NumeroDocumentoNaOriginal}', Gate {candidato.GatePercentual}%) — não é possível atualizar o status de matching.");
            }

            int linhas = await _repository.AtualizarStatusMatchingCronogramaAsync(
                connection, transaction, candidato.IdCronograma.Value, status, mensagem, ldpGateId, ldpRegistroId, dataMatching, cancellationToken);

            if (linhas != 1)
            {
                throw new InvalidOperationException(
                    $"UPDATE de status de matching em dbo.Cronograma_Projetos (ID_Cronograma={candidato.IdCronograma}) afetou {linhas} linha(s), esperado 1 — abortando.");
            }
        }

        private static string TruncarMensagem(string mensagem, int max)
        {
            if (string.IsNullOrEmpty(mensagem) || mensagem.Length <= max) return mensagem;
            const string marcador = "...(truncado)";
            return mensagem.Substring(0, max - marcador.Length) + marcador;
        }
    }
}
