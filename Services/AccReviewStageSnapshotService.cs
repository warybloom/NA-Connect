using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Extensions.Logging;
using NA_Connect.Services;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Consolidação e preparação do snapshot da etapa atual dos documentos em ACC Reviews (funcionalidade
    /// "ACC_REVIEW_STAGE"). Nunca faz uma segunda varredura dos Reviews — recebe um
    /// <see cref="AccReviewWorkflowService.WorkflowBuildResult"/> JÁ CONSTRUÍDO (mesma chamada de
    /// GetProjectWorkflowResultAsync reaproveitada pelo Worker para LdpUpdateService.UpdateWorkflowStatusAsync
    /// e para este serviço). Responsabilidades (seção 7):
    ///   AccReviewWorkflowService     -> coleta e interpretação do ACC (intocado por esta funcionalidade).
    ///   AccReviewStageSnapshotService -> consolidação de DWG/PDF em documento lógico + preparação do snapshot (este arquivo).
    ///   DatabaseService              -> transação SQL e SqlBulkCopy (SalvarSnapshotEtapasReviewTransacional).
    /// Nunca altera Status_In_Flow, Cronograma, Qualidade/RNC, LDP Tipo 2 ou a LDP em si.
    /// </summary>
    public class AccReviewStageSnapshotService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<AccReviewStageSnapshotService>? _logger;

        public AccReviewStageSnapshotService(DatabaseService databaseService, ILogger<AccReviewStageSnapshotService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Consolida <paramref name="workflowResult"/> em documentos lógicos e persiste o snapshot completo
        /// do projeto de forma transacional. Se <see cref="AccReviewWorkflowService.WorkflowBuildResult.Sucesso"/>
        /// for false, a transação NUNCA é aberta e o snapshot anterior é preservado (seção 4/11 — mesma
        /// blindagem que já protege a escrita de Status_In_Flow).
        /// </summary>
        public Task<ResultadoSnapshotEtapaReview> SincronizarSnapshotAsync(string projectIdAcc, AccReviewWorkflowService.WorkflowBuildResult workflowResult)
        {
            var resultado = new ResultadoSnapshotEtapaReview();

            if (workflowResult == null || !workflowResult.Sucesso)
            {
                resultado.Sucesso = false;
                resultado.CodigoResultado = "WORKFLOW_RESULT_INVALIDO";
                resultado.Mensagem = workflowResult == null
                    ? "WorkflowBuildResult nulo — snapshot não persistido, dados anteriores preservados."
                    : $"WorkflowBuildResult.Sucesso=false (CodigoResultado={workflowResult.CodigoResultado}) — snapshot não persistido, dados anteriores preservados.";

                _logger?.LogWarning("[ACC_REVIEW_STAGE][SNAPSHOT] Project_ID_ACC={ProjectIdAcc} | {Mensagem}", projectIdAcc, resultado.Mensagem);
                return Task.FromResult(resultado);
            }

            var documentos = ConsolidarDocumentosLogicos(workflowResult, resultado, _logger);

            resultado.ReviewsAnalisados = workflowResult.ReviewStagesByReviewId.Count;
            resultado.ReviewsAbertos = workflowResult.ReviewStagesByReviewId.Values.Count(r => !string.IsNullOrWhiteSpace(r.CurrentStepId));
            resultado.DocumentosFisicos = workflowResult.Entries.Count;
            resultado.DocumentosLogicos = documentos.Count;
            resultado.DocumentosComResponsavel = documentos.Count(d => !string.IsNullOrWhiteSpace(d.EtapaAtual) && d.ResponsibleNames.Count > 0);
            resultado.DocumentosSemResponsavel = documentos.Count(d => !string.IsNullOrWhiteSpace(d.EtapaAtual) && d.ResponsibleNames.Count == 0);
            resultado.DocumentosComVencimento = documentos.Count(d => d.DataVencimentoEtapa.HasValue);
            resultado.DocumentosSemVencimento = documentos.Count(d => !string.IsNullOrWhiteSpace(d.EtapaAtual) && !d.DataVencimentoEtapa.HasValue);

            var dataSincronizacao = DateTime.Now;
            var tabela = ConstruirDataTable(projectIdAcc, documentos, dataSincronizacao);

            var persistencia = _databaseService.SalvarSnapshotEtapasReviewTransacional(projectIdAcc, tabela);

            resultado.Sucesso = persistencia.Sucesso;
            resultado.CodigoResultado = persistencia.Sucesso ? "OK" : "SNAPSHOT_SQL_FALHOU";
            resultado.Mensagem = persistencia.Sucesso
                ? $"{persistencia.RegistrosInseridos} registro(s) inserido(s) ({resultado.Conflitos} conflito(s) ignorado(s) nesta execução)."
                : $"Falha ao persistir snapshot: {persistencia.Erro}. Rollback executado, snapshot anterior preservado.";
            resultado.RegistrosAnteriores = persistencia.RegistrosAnteriores;
            resultado.RegistrosInseridos = persistencia.RegistrosInseridos;
            resultado.RollbackExecutado = persistencia.RollbackExecutado;

            _logger?.LogInformation(
                "[ACC_REVIEW_STAGE][SNAPSHOT] Project_ID_ACC={ProjectIdAcc} | ReviewsAnalisados={ReviewsAnalisados} | ReviewsAbertos={ReviewsAbertos} | " +
                "DocumentosFisicos={DocumentosFisicos} | DocumentosLogicos={DocumentosLogicos} | ComResponsavel={ComResponsavel} | SemResponsavel={SemResponsavel} | " +
                "ComVencimento={ComVencimento} | SemVencimento={SemVencimento} | Conflitos={Conflitos} | RegistrosInseridos={RegistrosInseridos} | " +
                "Resultado={Resultado}",
                projectIdAcc, resultado.ReviewsAnalisados, resultado.ReviewsAbertos, resultado.DocumentosFisicos, resultado.DocumentosLogicos,
                resultado.DocumentosComResponsavel, resultado.DocumentosSemResponsavel, resultado.DocumentosComVencimento, resultado.DocumentosSemVencimento,
                resultado.Conflitos, resultado.RegistrosInseridos, persistencia.Sucesso ? "COMMIT" : "ROLLBACK");

            return Task.FromResult(resultado);
        }

        // =======================================================================================
        // CONSOLIDAÇÃO EM DOCUMENTO LÓGICO (seção 8/9)
        // =======================================================================================

        /// <summary>
        /// Agrupa TODAS as entradas físicas (todas as extensões, de TODOS os Reviews pelos quais o
        /// documento já passou) pelo nome-base normalizado. Dentro de cada grupo, o ReviewId com a
        /// DataAtualizacaoReview mais recente vence o histórico (seção 9 — mesmo critério já usado por
        /// AccReviewWorkflowService para o "workflowMap" legado); só os arquivos físicos desse ReviewId
        /// vencedor formam o documento lógico. Um empate real entre ReviewIds diferentes na MESMA data
        /// mais recente é a única situação de conflito desta funcionalidade — porque etapa/responsável/
        /// vencimento são atributos DO REVIEW (nunca da versão), dois arquivos do MESMO ReviewId nunca
        /// podem divergir nesses campos.
        /// </summary>
        internal static List<RegistroSnapshotEtapaReviewPreparado> ConsolidarDocumentosLogicos(
            AccReviewWorkflowService.WorkflowBuildResult workflowResult, ResultadoSnapshotEtapaReview resultado, ILogger? logger = null)
        {
            var documentos = new List<RegistroSnapshotEtapaReviewPreparado>();

            var grupos = workflowResult.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.FileNameWithoutExtension) && !string.IsNullOrWhiteSpace(e.ReviewId))
                .GroupBy(e => AccReviewWorkflowService.NormalizarNomeDocumento(e.FileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);

            foreach (var grupoDocumento in grupos)
            {
                if (string.IsNullOrEmpty(grupoDocumento.Key)) continue;

                var porReview = grupoDocumento
                    .GroupBy(e => e.ReviewId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DateTime dataMaisRecente = porReview.Max(g => g.Max(e => e.DataAtualizacaoReview ?? DateTime.MinValue));
                var candidatosMaisRecentes = porReview
                    .Where(g => g.Max(e => e.DataAtualizacaoReview ?? DateTime.MinValue) == dataMaisRecente)
                    .ToList();

                if (candidatosMaisRecentes.Count > 1)
                {
                    resultado.Conflitos++;
                    logger?.LogWarning(
                        "[ACC_REVIEW_STAGE][CONFLITO] Documento={Documento} | Codigo=ACC_REVIEW_STAGE_CONFLITO_ENTRE_EXTENSOES | " +
                        "Motivo=Empate entre {Qtd} Reviews diferentes na mesma DataAtualizacaoReview mais recente ({Data:O}) — nenhum vence de forma comprovada, documento ignorado nesta execução. ReviewIds={ReviewIds}",
                        grupoDocumento.Key, candidatosMaisRecentes.Count, dataMaisRecente,
                        string.Join(", ", candidatosMaisRecentes.Select(g => g.Key)));
                    continue;
                }

                var entradasVencedoras = candidatosMaisRecentes[0].ToList();
                string reviewIdVencedor = entradasVencedoras[0].ReviewId;

                workflowResult.ReviewStagesByReviewId.TryGetValue(reviewIdVencedor, out var metadadosEtapa);

                var registro = new RegistroSnapshotEtapaReviewPreparado
                {
                    ReviewId = reviewIdVencedor,
                    ReviewNumber = metadadosEtapa?.ReviewNumber,
                    ReviewName = metadadosEtapa?.ReviewName,
                    ReviewStatus = metadadosEtapa?.ReviewStatus,
                    WorkflowId = metadadosEtapa?.WorkflowId,
                    CurrentStepId = metadadosEtapa?.CurrentStepId,
                    EtapaAtual = metadadosEtapa?.CurrentStepName,
                    ResponsibleNames = metadadosEtapa?.ResponsibleNames ?? new List<string>(),
                    ResponsibleIds = metadadosEtapa?.ResponsibleIds ?? new List<string>(),
                    OrigemResponsavel = metadadosEtapa?.ResponsibleSource,
                    DataVencimentoEtapa = metadadosEtapa?.DueDateLocal,
                    OrigemVencimento = metadadosEtapa?.DueDateSource,
                    NumeroDocumento = entradasVencedoras.Select(e => e.FileNameWithoutExtension).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    NumeroDocumentoNormalizado = grupoDocumento.Key,
                    ArquivosFisicos = OrdenarUnicos(entradasVencedoras.Select(e => e.FileName)),
                    ItemUrns = OrdenarUnicos(entradasVencedoras.Select(e => e.ItemId)),
                    VersionUrns = OrdenarUnicos(entradasVencedoras.Select(e => e.VersionUrn)),
                    PastasEncontradas = OrdenarUnicos(entradasVencedoras.Select(e => e.PastaEncontrada)),
                    StatusNoFluxo = entradasVencedoras.Select(e => e.StatusCalculado).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                    DataAtualizacaoReview = metadadosEtapa?.ReviewUpdatedAt ?? entradasVencedoras[0].DataAtualizacaoReview
                };

                if (metadadosEtapa == null)
                {
                    logger?.LogWarning(
                        "[ACC_REVIEW_STAGE][DOCUMENTO] Documento={Documento} | ReviewId={ReviewId} | Metadados de etapa ausentes para este ReviewId — campos de etapa/responsável/vencimento gravados como NULL.",
                        registro.NumeroDocumento, reviewIdVencedor);
                }

                logger?.LogInformation(
                    "[ACC_REVIEW_STAGE][DOCUMENTO] Documento={Documento} | ReviewId={ReviewId} | Numero={Numero} | Etapa={Etapa} | Responsavel={Responsavel} | " +
                    "OrigemResponsavel={OrigemResponsavel} | Vencimento={Vencimento} | ArquivosFisicos={QtdArquivos}",
                    registro.NumeroDocumento, reviewIdVencedor, registro.ReviewNumber ?? "(n/d)", registro.EtapaAtual ?? "(n/d)",
                    registro.ResponsibleNames.Count > 0 ? string.Join("; ", registro.ResponsibleNames) : "(nenhum)",
                    registro.OrigemResponsavel ?? "(n/d)", registro.DataVencimentoEtapa?.ToString("yyyy-MM-dd") ?? "(n/d)", registro.ArquivosFisicos.Count);

                documentos.Add(registro);
            }

            return documentos;
        }

        private static List<string> OrdenarUnicos(IEnumerable<string> valores)
        {
            return valores
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // =======================================================================================
        // PREPARAÇÃO DO DATATABLE (seção 7 — "preparação do snapshot" é responsabilidade deste serviço;
        // DatabaseService só recebe o DataTable pronto e executa DELETE+SqlBulkCopy na mesma transação).
        // =======================================================================================

        internal static DataTable ConstruirDataTable(string projectIdAcc, List<RegistroSnapshotEtapaReviewPreparado> documentos, DateTime dataSincronizacao)
        {
            var tabela = new DataTable();

            tabela.Columns.Add("Project_ID_ACC", typeof(string));
            tabela.Columns.Add("ReviewId", typeof(string));
            tabela.Columns.Add("ReviewNumber", typeof(string));
            tabela.Columns.Add("ReviewName", typeof(string));
            tabela.Columns.Add("ReviewStatus", typeof(string));
            tabela.Columns.Add("WorkflowId", typeof(string));
            tabela.Columns.Add("CurrentStepId", typeof(string));
            tabela.Columns.Add("EtapaAtual", typeof(string));
            tabela.Columns.Add("ResponsavelEtapa", typeof(string));
            tabela.Columns.Add("ResponsavelEtapaIds", typeof(string));
            tabela.Columns.Add("OrigemResponsavel", typeof(string));
            tabela.Columns.Add("DataVencimentoEtapa", typeof(DateTime));
            tabela.Columns.Add("OrigemVencimento", typeof(string));
            tabela.Columns.Add("NumeroDocumento", typeof(string));
            tabela.Columns.Add("NumeroDocumentoNormalizado", typeof(string));
            tabela.Columns.Add("ArquivosFisicos", typeof(string));
            tabela.Columns.Add("ItemUrns", typeof(string));
            tabela.Columns.Add("VersionUrns", typeof(string));
            tabela.Columns.Add("PastasEncontradas", typeof(string));
            tabela.Columns.Add("StatusNoFluxo", typeof(string));
            tabela.Columns.Add("DataAtualizacaoReview", typeof(DateTime));
            tabela.Columns.Add("DataSincronizacao", typeof(DateTime));

            foreach (var doc in documentos)
            {
                var linha = tabela.NewRow();

                linha["Project_ID_ACC"] = (object?)projectIdAcc ?? DBNull.Value;
                linha["ReviewId"] = (object?)doc.ReviewId ?? DBNull.Value;
                linha["ReviewNumber"] = (object?)doc.ReviewNumber ?? DBNull.Value;
                linha["ReviewName"] = (object?)doc.ReviewName ?? DBNull.Value;
                linha["ReviewStatus"] = (object?)doc.ReviewStatus ?? DBNull.Value;
                linha["WorkflowId"] = (object?)doc.WorkflowId ?? DBNull.Value;
                linha["CurrentStepId"] = (object?)doc.CurrentStepId ?? DBNull.Value;
                linha["EtapaAtual"] = (object?)doc.EtapaAtual ?? DBNull.Value;
                linha["ResponsavelEtapa"] = doc.ResponsibleNames.Count > 0 ? string.Join("; ", doc.ResponsibleNames) : (object)DBNull.Value;
                linha["ResponsavelEtapaIds"] = doc.ResponsibleIds.Count > 0 ? string.Join("; ", doc.ResponsibleIds) : (object)DBNull.Value;
                linha["OrigemResponsavel"] = (object?)doc.OrigemResponsavel ?? DBNull.Value;
                // Só a DATA (seção 6) — nunca a hora, nunca o texto relativo ("Hoje"/"Amanhã").
                linha["DataVencimentoEtapa"] = doc.DataVencimentoEtapa.HasValue ? doc.DataVencimentoEtapa.Value.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value;
                linha["OrigemVencimento"] = (object?)doc.OrigemVencimento ?? DBNull.Value;
                linha["NumeroDocumento"] = (object?)doc.NumeroDocumento ?? DBNull.Value;
                linha["NumeroDocumentoNormalizado"] = (object?)doc.NumeroDocumentoNormalizado ?? DBNull.Value;
                linha["ArquivosFisicos"] = doc.ArquivosFisicos.Count > 0 ? string.Join("; ", doc.ArquivosFisicos) : (object)DBNull.Value;
                linha["ItemUrns"] = doc.ItemUrns.Count > 0 ? string.Join("; ", doc.ItemUrns) : (object)DBNull.Value;
                linha["VersionUrns"] = doc.VersionUrns.Count > 0 ? string.Join("; ", doc.VersionUrns) : (object)DBNull.Value;
                linha["PastasEncontradas"] = doc.PastasEncontradas.Count > 0 ? string.Join("; ", doc.PastasEncontradas) : (object)DBNull.Value;
                linha["StatusNoFluxo"] = (object?)doc.StatusNoFluxo ?? DBNull.Value;
                linha["DataAtualizacaoReview"] = (object?)doc.DataAtualizacaoReview ?? DBNull.Value;
                linha["DataSincronizacao"] = dataSincronizacao;

                tabela.Rows.Add(linha);
            }

            return tabela;
        }
    }
}
