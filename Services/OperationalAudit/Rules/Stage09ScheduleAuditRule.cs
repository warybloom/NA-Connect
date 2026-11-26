using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 9 — Cronograma (seção 12 do Checkpoint 2). Reconstrói somente a LEITURA necessária — nunca
    /// chama <c>AccScheduleSyncService.SyncScheduleToLdpAsync</c>, nunca persiste
    /// <c>Cronograma_Projetos</c> (só SELECT), nunca altera a LDP.
    /// </summary>
    public sealed class Stage09ScheduleAuditRule : IOperationalAuditRule
    {
        public string Etapa => "9";
        public string NomeEtapa => "Cronograma";

        private static readonly string[] AliasesCronograma =
        {
            "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES"
        };
        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesLdpPhysicalCompleted = { "%PHYSICAL COMPLETED", "% PHYSICAL COMPLETED", "PHYSICAL COMPLETED" };

        private const int LimiteAbsolutoAlteracoesParaZero = 5;
        private const double LimitePercentualAlteracoesParaZero = 5.0;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditScheduleReadOnlyAdapter _schedule;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IAuditDatabaseReadOnlyAdapter _database;

        public Stage09ScheduleAuditRule(
            IAuditAccReadOnlyAdapter acc, IAuditScheduleReadOnlyAdapter schedule, IAuditWorkbookReadOnlyAdapter workbook, IAuditDatabaseReadOnlyAdapter database)
        {
            _acc = acc;
            _schedule = schedule;
            _workbook = workbook;
            _database = database;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaCronograma = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesCronograma, false, ct);
            requirements.Add(NovoRequisito(contexto, "CRN-001", "Pasta 01.03",
                pastaCronograma != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pastaCronograma?.ResolvedPath ?? "não localizada"));

            if (pastaCronograma == null)
            {
                findings.Add(NovoFinding(contexto, "CRN-001", "Pasta 01.03 ausente",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "PASTA_CRONOGRAMA_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaCronograma.FolderId, false, ct);
            var candidatosMpp = conteudo.Itens
                .Where(i => string.Equals(i.Extension, "MPP", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.LatestVersionUrn))
                .ToList();

            requirements.Add(NovoRequisito(contexto, "CRN-002", "Arquivo MPP",
                candidatosMpp.Count > 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "ao menos 1 arquivo .mpp", candidatosMpp.Count.ToString()));

            if (candidatosMpp.Count == 0)
            {
                findings.Add(NovoFinding(contexto, "CRN-002", "Arquivo MPP ausente",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ARQUIVO_MPP_AUSENTE", pastaEncontrada: pastaCronograma.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var mppSelecionado = candidatosMpp.OrderByDescending(i => i.LastModifiedUtc ?? DateTime.MinValue).First();
            await using var streamMpp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, mppSelecionado.LatestVersionUrn!, ct);
            var cronograma = _schedule.LerSomenteLeitura(streamMpp);

            // CRN-003/004/005 — resolução de REALIZADO/PREVISTO.
            var tarefaExemplo = cronograma.Tarefas.FirstOrDefault();
            bool realizadoResolvido = tarefaExemplo?.CampoRealizadoResolvido != null;
            bool previstoResolvido = tarefaExemplo?.CampoPrevistoResolvido != null;

            requirements.Add(NovoRequisito(contexto, "CRN-003", "Campo REALIZADO resolvido",
                realizadoResolvido ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "resolvido por alias controlado", realizadoResolvido ? tarefaExemplo!.CampoRealizadoResolvido! : "não resolvido"));
            if (!realizadoResolvido)
            {
                findings.Add(NovoFinding(contexto, "CRN-003", "Campo REALIZADO não resolvido no MPP",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "CAMPO_REALIZADO_NAO_RESOLVIDO", nomeArquivo: mppSelecionado.Name));
            }

            requirements.Add(NovoRequisito(contexto, "CRN-004", "Campo PREVISTO resolvido",
                previstoResolvido ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "resolvido por alias controlado", previstoResolvido ? tarefaExemplo!.CampoPrevistoResolvido! : "não resolvido"));
            if (!previstoResolvido)
            {
                findings.Add(NovoFinding(contexto, "CRN-004", "Campo PREVISTO não resolvido no MPP",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "CAMPO_PREVISTO_NAO_RESOLVIDO", nomeArquivo: mppSelecionado.Name));
            }

            requirements.Add(NovoRequisito(contexto, "CRN-005", "REALIZADO e PREVISTO em campos distintos",
                !cronograma.RealizadoEPrevistoColidem ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "campos diferentes", cronograma.RealizadoEPrevistoColidem ? "mesmo campo" : "campos diferentes"));
            if (cronograma.RealizadoEPrevistoColidem)
            {
                findings.Add(NovoFinding(contexto, "CRN-005", "REALIZADO e PREVISTO resolvidos para o mesmo campo do MPP",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "REALIZADO_PREVISTO_MESMO_CAMPO", nomeArquivo: mppSelecionado.Name));
            }

            // CRN-011 — percentual inválido (fora de 0..100, ou não numérico onde deveria ser).
            var percentuaisInvalidos = cronograma.Tarefas.Where(t => t.ValorRealizado is < 0 or > 100 || t.ValorPrevisto is < 0 or > 100).ToList();
            requirements.Add(NovoRequisito(contexto, "CRN-011", "Percentuais dentro de 0..100",
                percentuaisInvalidos.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 inválidos", percentuaisInvalidos.Count.ToString()));
            foreach (var tarefa in percentuaisInvalidos)
            {
                findings.Add(NovoFinding(contexto, "CRN-011", $"Percentual inválido na tarefa '{tarefa.Nome}' (Realizado={tarefa.ValorRealizado}, Previsto={tarefa.ValorPrevisto})",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "PERCENTUAL_INVALIDO", nomeArquivo: mppSelecionado.Name, campo: tarefa.Nome));
            }

            // CRN-008 — zeramento em massa que seria bloqueado.
            int mudancasParaZero = cronograma.Tarefas.Count(t => t.ValorRealizado == 0);
            double percentualParaZero = cronograma.Tarefas.Count > 0 ? 100.0 * mudancasParaZero / cronograma.Tarefas.Count : 0;
            bool zeramentoEmMassa = mudancasParaZero > LimiteAbsolutoAlteracoesParaZero || percentualParaZero > LimitePercentualAlteracoesParaZero;

            requirements.Add(NovoRequisito(contexto, "CRN-008", "Nenhum zeramento em massa que seria bloqueado",
                !zeramentoEmMassa ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                $"<= {LimiteAbsolutoAlteracoesParaZero} ou <= {LimitePercentualAlteracoesParaZero}%", $"{mudancasParaZero} ({percentualParaZero:0.0}%)"));
            if (zeramentoEmMassa)
            {
                findings.Add(NovoFinding(contexto, "CRN-008", $"Zeramento em massa detectado — {mudancasParaZero} tarefas ({percentualParaZero:0.0}%) com REALIZADO=0; isso seria bloqueado pela sincronização real",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ZERAMENTO_EM_MASSA", nomeArquivo: mppSelecionado.Name));
            }

            // LDP: coluna %PHYSICAL COMPLETED (CRN-006/007) + reconciliação MPP x LDP (CRN-009/010).
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            if (pastaGestao != null)
            {
                var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
                var candidatoLdp = conteudoGestao.Itens.FirstOrDefault(i =>
                    (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(i.LatestVersionUrn));

                if (candidatoLdp != null)
                {
                    await using var streamLdp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdp.LatestVersionUrn!, ct);
                    var workbook = _workbook.AbrirSomenteLeitura(streamLdp);
                    var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));

                    if (nomeAbaLdp != null)
                    {
                        var aba = workbook.Abas[nomeAbaLdp];
                        var colunasPhysicalCompleted = aba.Cabecalhos.Where(c => AliasesLdpPhysicalCompleted.Any(a => string.Equals(a, c.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();

                        requirements.Add(NovoRequisito(contexto, "CRN-006", "Coluna %PHYSICAL COMPLETED presente",
                            colunasPhysicalCompleted.Count >= 1 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                            "1 coluna", colunasPhysicalCompleted.Count.ToString()));
                        if (colunasPhysicalCompleted.Count == 0)
                        {
                            findings.Add(NovoFinding(contexto, "CRN-006", "Coluna %PHYSICAL COMPLETED ausente na LDP",
                                OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                                bloqueiaEtapa: true, "COLUNA_PHYSICAL_COMPLETED_AUSENTE", nomeArquivo: candidatoLdp.Name, abaEncontrada: nomeAbaLdp));
                        }

                        requirements.Add(NovoRequisito(contexto, "CRN-007", "Coluna %PHYSICAL COMPLETED não duplicada",
                            colunasPhysicalCompleted.Count <= 1 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                            "no máximo 1", colunasPhysicalCompleted.Count.ToString()));
                        if (colunasPhysicalCompleted.Count > 1)
                        {
                            findings.Add(NovoFinding(contexto, "CRN-007", $"Coluna %PHYSICAL COMPLETED duplicada ({colunasPhysicalCompleted.Count} ocorrências) na LDP",
                                OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                                bloqueiaEtapa: true, "COLUNA_PHYSICAL_COMPLETED_DUPLICADA", nomeArquivo: candidatoLdp.Name, abaEncontrada: nomeAbaLdp));
                        }

                        var documentosNaLdp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (_, valores) in aba.Linhas)
                        {
                            var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), "N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Trim(), "N&A", StringComparison.OrdinalIgnoreCase));
                            if (chave != null && !string.IsNullOrWhiteSpace(valores[chave])) documentosNaLdp.Add(valores[chave]!.Trim());
                        }

                        var tarefasComDocumento = cronograma.Tarefas.Where(t => !string.IsNullOrWhiteSpace(t.NumeroDocumentoNA)).ToList();
                        foreach (var tarefa in tarefasComDocumento)
                        {
                            if (!documentosNaLdp.Contains(tarefa.NumeroDocumentoNA!))
                            {
                                findings.Add(NovoFinding(contexto, "CRN-009", $"Tarefa do MPP '{tarefa.Nome}' (doc. {tarefa.NumeroDocumentoNA}) sem documento correspondente na LDP",
                                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Medio,
                                    bloqueiaEtapa: false, "TAREFA_MPP_SEM_DOCUMENTO_LDP", nomeArquivo: mppSelecionado.Name, documentoNa: tarefa.NumeroDocumentoNA));
                            }
                        }
                        var codigosDeTarefasNoMpp = new HashSet<string>(tarefasComDocumento.Select(t => t.NumeroDocumentoNA!), StringComparer.OrdinalIgnoreCase);
                        foreach (var documentoNa in documentosNaLdp)
                        {
                            if (!codigosDeTarefasNoMpp.Contains(documentoNa))
                            {
                                findings.Add(NovoFinding(contexto, "CRN-010", $"Documento '{documentoNa}' da LDP sem tarefa correspondente no MPP",
                                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoAccSemLdp, OperationalAuditSeverity.Medio,
                                    bloqueiaEtapa: false, "DOCUMENTO_LDP_SEM_TAREFA_MPP", nomeArquivo: candidatoLdp.Name, documentoNa: documentoNa));
                            }
                        }
                    }
                }
            }

            // CRN-012 — Cronograma_Projetos (SELECT-only) diverge da leitura atual.
            var parametros = new Dictionary<string, object?> { ["@ProjectIdAcc"] = contexto.ProjectIdAcc };
            DataTable dadosSalvos = await _database.ConsultarAsync(
                "SELECT NA_DOCUMENT_NUMBER, PERCENT_PHYSICAL_COMPLETED FROM Cronograma_Projetos WHERE Project_ID_ACC = @ProjectIdAcc", parametros, ct);

            int divergencias = 0;
            var salvosPorDocumento = dadosSalvos.Rows.Cast<DataRow>()
                .Where(r => r["NA_DOCUMENT_NUMBER"] != DBNull.Value)
                .ToDictionary(r => r["NA_DOCUMENT_NUMBER"]!.ToString()!, r => r["PERCENT_PHYSICAL_COMPLETED"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["PERCENT_PHYSICAL_COMPLETED"]), StringComparer.OrdinalIgnoreCase);

            foreach (var tarefa in cronograma.Tarefas.Where(t => !string.IsNullOrWhiteSpace(t.NumeroDocumentoNA)))
            {
                if (salvosPorDocumento.TryGetValue(tarefa.NumeroDocumentoNA!, out var valorSalvo) && valorSalvo.HasValue && tarefa.ValorRealizado.HasValue
                    && Math.Abs(valorSalvo.Value - tarefa.ValorRealizado.Value) > 0.01)
                {
                    divergencias++;
                    findings.Add(NovoFinding(contexto, "CRN-012", $"Cronograma_Projetos ({valorSalvo}) diverge da leitura atual do MPP ({tarefa.ValorRealizado}) para o documento {tarefa.NumeroDocumentoNA}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "CRONOGRAMA_PROJETOS_DIVERGENTE", nomeArquivo: mppSelecionado.Name, documentoNa: tarefa.NumeroDocumentoNA,
                        valorEsperado: tarefa.ValorRealizado.ToString(), valorEncontrado: valorSalvo.ToString()));
                }
            }
            requirements.Add(NovoRequisito(contexto, "CRN-012", "Cronograma_Projetos consistente com a leitura atual",
                divergencias == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 divergências", divergencias.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "9", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? documentoNa = null, string? campo = null, string? valorEsperado = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "9", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                DocumentoNa = documentoNa, Campo = campo, ValorEsperado = valorEsperado, ValorEncontrado = valorEncontrado
            };
    }
}
