using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Planning;
using OfficeOpenXml;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Mecânica COMPARTILHADA entre Stage12 (External) e Stage13 (Internal) — "mesma filosofia... regra
    /// independente" (seção 13 do pedido do Checkpoint 3): cada chamador passa seus próprios aliases,
    /// <see cref="PlanningSheetDefinition"/> real e prefixo de RegraId (EXT-*/INT-*), e os resultados nunca
    /// se misturam (cada execução produz Findings/Requirements só com o prefixo do chamador). Não é uma
    /// regra em si — nunca implementa <see cref="IOperationalAuditRule"/> — só evita duplicar ~150 linhas
    /// idênticas entre as duas regras reais e independentes. Reaproveita <see cref="IPlanningPreflightService"/>
    /// diretamente — mesmo padrão já estabelecido por Stage14/Stage16 (Checkpoint 2): nunca um adaptador
    /// próprio, nunca <c>PlanningControlSyncService</c> (a auditoria não chama o serviço de escrita).
    /// </summary>
    internal static class ActionLogAuditHelper
    {
        public static async Task<OperationalRuleExecutionResult> ExecutarAsync(
            OperationalAuditContext contexto,
            IAuditAccReadOnlyAdapter acc,
            IPlanningPreflightService preflight,
            IReadOnlyList<string> aliasesPasta,
            PlanningSheetDefinition definicao,
            string etapa,
            string prefixo)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pasta = await acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, aliasesPasta, false, ct);
            requirements.Add(NovoRequisito(contexto, etapa, $"{prefixo}-001", $"Pasta {definicao.FolderName}",
                pasta != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pasta?.ResolvedPath ?? "não localizada"));
            if (pasta == null)
            {
                findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-001", $"Pasta '{definicao.FolderName}' não encontrada pelos caminhos/aliases aceitos",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "PASTA_ACTION_LOG_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudo = await acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pasta.FolderId, false, ct);
            var candidatosPlanilha = conteudo.Itens.Where(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn)).ToList();

            if (candidatosPlanilha.Count == 0)
            {
                bool somenteXlsLegado = conteudo.Itens.Any(i => string.Equals(i.Extension, "XLS", StringComparison.OrdinalIgnoreCase));
                if (somenteXlsLegado)
                {
                    findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-005", "Somente arquivo .xls legado disponível — o pipeline real só suporta .xlsx/.xlsm (requer conversão manual)",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: true, "SOMENTE_XLS_LEGADO", pastaEncontrada: pasta.ResolvedPath));
                    return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
                }

                findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-002", $"Nenhum candidato .xlsx/.xlsm com assinatura de cabeçalho nas primeiras dez linhas foi encontrado em '{pasta.ResolvedPath}'",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "NENHUM_CANDIDATO_ACTION_ITEM_LIST", pastaEncontrada: pasta.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var candidatosComAba = new List<(AccItemRef Item, byte[] Bytes)>();
            bool headerIncomplete = false;

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            foreach (var item in candidatosPlanilha)
            {
                await using var stream = await acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, item.LatestVersionUrn!, ct);
                using var memoria = new MemoryStream();
                await stream.CopyToAsync(memoria, ct);
                byte[] bytes = memoria.ToArray();

                bool temAbaAceita = false;
                try
                {
                using (var package = new ExcelPackage(new MemoryStream(bytes)))
                {
                    try { PlanningHeaderScan.Select(package.Workbook.Worksheets, definicao); temAbaAceita = true; }
                    catch (PlanningLayoutException ex) { temAbaAceita = false; headerIncomplete |= PlanningHeaderScan.Candidates(package.Workbook.Worksheets, definicao).Any(c => c.Header.Mapa.Count > 0); }
                }
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
                {
                    // Um Excel ilegível não impede avaliar os demais candidatos da pasta.
                    temAbaAceita = false;
                }
                if (temAbaAceita) candidatosComAba.Add((item, bytes));
            }

            if (candidatosComAba.Count == 0)
            {
                findings.Add(NovoFinding(contexto, etapa, headerIncomplete ? $"{prefixo}-003" : $"{prefixo}-002", $"Nenhum dos {candidatosPlanilha.Count} candidato(s) .xlsx/.xlsm em '{pasta.ResolvedPath}' contém a assinatura de cabeçalho esperada",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "NENHUM_CANDIDATO_ACTION_ITEM_LIST", pastaEncontrada: pasta.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            requirements.Add(NovoRequisito(contexto, etapa, $"{prefixo}-002", "Candidato identificado pelo cabeçalho",
                candidatosComAba.Count == 1 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, "1 candidato", candidatosComAba.Count.ToString()));

            if (candidatosComAba.Count != 1)
            {
                findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-002", "Mais de um arquivo contém a assinatura do template",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ARQUIVO_AMBIGUO", pastaEncontrada: pasta.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            var escolhido = candidatosComAba.Single();

            using var packageEscolhido = new ExcelPackage(new MemoryStream(escolhido.Bytes));
            var resultadoPreflight = preflight.ExecutarPreflight(packageEscolhido, definicao, contexto.ProjectIdAcc, pasta.ResolvedPath, escolhido.Item.Name);

            if (!resultadoPreflight.Sucesso)
            {
                requirements.Add(NovoRequisito(contexto, etapa, $"{prefixo}-003", "Cabeçalho localizado sem ambiguidade (linhas 1-10)",
                    OperationalRequirementStatus.Failed, "cabeçalho localizado", resultadoPreflight.CodigoResultado ?? "falha"));
                findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-003", $"Cabeçalho necessário não localizado ou ambíguo em '{escolhido.Item.Name}': {resultadoPreflight.Mensagem}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "CABECALHO_ACTION_ITEM_LIST_AUSENTE_OU_AMBIGUO", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: escolhido.Item.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            requirements.Add(NovoRequisito(contexto, etapa, $"{prefixo}-003", "Cabeçalho localizado sem ambiguidade (linhas 1-10)",
                OperationalRequirementStatus.Approved, "cabeçalho localizado", $"linha {resultadoPreflight.HeaderRow}"));

            // Registros inválidos já foram isolados pelo mesmo preflight operacional.
            var linhasComConteudoInvalidadas = resultadoPreflight.DiagnosticosLinhas
                .Where(l => l.Value.Count > 0).Select(l => $"Linha {l.Key}: " + string.Join(" | ", l.Value))
                .ToList();
            requirements.Add(NovoRequisito(contexto, etapa, $"{prefixo}-004", "Nenhuma linha importada com alertas",
                linhasComConteudoInvalidadas.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 linhas", linhasComConteudoInvalidadas.Count.ToString()));
            if (linhasComConteudoInvalidadas.Count > 0)
            {
                // Uma linha inválida nunca faz as válidas parecerem inválidas — reportada à parte,
                // resultadoPreflight.LinhasValidas continua contando só as realmente válidas.
                findings.Add(NovoFinding(contexto, etapa, $"{prefixo}-004", $"{linhasComConteudoInvalidadas.Count} linha(s) preservadas com diagnósticos em '{escolhido.Item.Name}'",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Baixo,
                    bloqueiaEtapa: false, "LINHA_IMPORTADA_COM_ALERTAS", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: escolhido.Item.Name,
                    evidencia: string.Join(" | ", linhasComConteudoInvalidadas.Take(Math.Max(1, contexto.Options.MaxEvidencePerFinding)))));
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string etapa, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = etapa, RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string etapa, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? evidencia = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = etapa, RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, Evidencia = evidencia
            };
    }
}
