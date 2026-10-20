using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>Audita o contrato flexível da Etapa 15: mínimo de dez campos oficiais nas primeiras dez linhas, sem nome de aba fixo.</summary>
    public sealed class Stage15WeeklyScheduleAuditRule : IOperationalAuditRule
    {
        public string Etapa => "15";
        public string NomeEtapa => "Weekly Engineering Schedule";
        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditPlanningReadOnlyAdapter _planning;
        private readonly ILogger<Stage15WeeklyScheduleAuditRule> _logger;

        public Stage15WeeklyScheduleAuditRule(IAuditAccReadOnlyAdapter acc, IAuditPlanningReadOnlyAdapter planning,
            ILogger<Stage15WeeklyScheduleAuditRule>? logger = null)
        {
            _acc = acc;
            _planning = planning;
            _logger = logger ?? NullLogger<Stage15WeeklyScheduleAuditRule>.Instance;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;
            void Check(string code, string title, bool ok, string expected, string actual, string reason)
            {
                requirements.Add(NovoRequisito(contexto, code, title,
                    ok ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, expected, actual));
                if (!ok) findings.Add(NovoFinding(contexto, code, title, OperationalAuditErrorDomain.Operacional,
                    OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto, true, reason,
                    valorEsperado: expected, valorEncontrado: actual));
            }
            OperationalRuleExecutionResult Result() => new() { Findings = findings, Requirements = requirements };
            string? language = await _planning.ObterIdiomaProjetoAsync(contexto.ProjectIdAcc, ct);
            // O adaptador real recebe segmentos de UM caminho, apesar do nome do argumento.
            // Tentar cada alias separadamente evita tratar pastas alternativas como pastas aninhadas.
            AccFolderRef? folder = null;
            foreach (var alias in WeeklyScheduleContract.FolderAliases(language))
            {
                foreach (var root in new[] { "01 - PLANNING & CONTROL", "01 - PLANEJAMENTO E CONTROLE" })
                {
                    folder = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, new[] { "Project Files", root, alias }, false, ct);
                    if (folder != null) break;
                }
                if (folder != null) break;
            }
            Check("WKS-001", "Pasta oficial 01.13", folder != null, "pasta localizada", folder?.ResolvedPath ?? "ausente", "PASTA_WEEKLY_SCHEDULE_AUSENTE");
            if (folder == null) return Result();
            var listing = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, folder.FolderId, false, ct);
            if (!listing.PaginacaoCompleta)
            {
                Check("WKS-002", "Listagem completa", false, "completa", listing.MotivoPaginacaoIncompleta ?? "incompleta", "LISTAGEM_WEEKLY_SCHEDULE_INCOMPLETA");
                return Result();
            }
            var valid = new List<WeeklyScheduleContract.WorksheetCandidate<AuditWorksheetSnapshot>>();
            bool ambiguousWorksheet = false;
            foreach (var candidate in listing.Itens.Where(i => WeeklyScheduleContract.IsExcel(i.Extension)))
            {
                if (string.IsNullOrWhiteSpace(candidate.LatestVersionUrn))
                {
                    Check("WKS-002", "Versao disponivel", false, "URN da versao", candidate.Name, "VERSAO_WEEKLY_SCHEDULE_INDISPONIVEL");
                    return Result();
                }
                await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidate.LatestVersionUrn, ct);
                try
                {
                    var workbook = _planning.LerAbas(stream, WeeklyScheduleContract.HeaderRow);
                    var rows = workbook.Abas.Values.SelectMany(sheet => sheet.HeaderRows.Count == 0 ? new[] { sheet } :
                        sheet.HeaderRows.Select(r => new AuditWorksheetSnapshot { Nome = sheet.Nome, LinhaDeCabecalho = r.Key, Cabecalhos = r.Value }));
                    var inspection = WeeklyScheduleContract.InspectWorksheets(rows,
                        s => s.Nome, s => s.LinhaDeCabecalho >= 1 && s.LinhaDeCabecalho <= 10 ? s.Cabecalhos : Array.Empty<string>(), _logger, candidate.Name, s => s.LinhaDeCabecalho);
                    if (inspection.Error == "ABA_WEEKLY_SCHEDULE_AMBIGUA") ambiguousWorksheet = true;
                    if (inspection.Error == null) valid.Add(inspection.ValidSheets[0]);
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
                { _logger.LogWarning(ex, "[WEEKLY] Candidato ilegivel: {Arquivo}", candidate.Name); }
            }
            var selectionError = WeeklyScheduleContract.SelectionError(valid.Count);
            Check("WKS-002", "Exatamente um arquivo Weekly valido", selectionError == null, "1", valid.Count.ToString(), selectionError ?? "");
            Check("WKS-003", "Exatamente uma aba por conteudo no arquivo selecionado", valid.Count == 1, "1",
                valid.Count == 1 ? valid[0].Sheet.Nome : "nenhuma aba selecionavel",
                valid.Count == 0 && ambiguousWorksheet ? "ABA_WEEKLY_SCHEDULE_AMBIGUA" : selectionError ?? "");
            Check("WKS-004", "Header nas primeiras 10 linhas", valid.Count > 0, "1..10", valid.Count > 0 ? valid[0].Row.ToString() : "ausente", "HEADER_OBRIGATORIO_AUSENTE");
            Check("WKS-005", "Mínimo de 10 dos 21 campos oficiais", valid.Count > 0, "10/21 campos oficiais",
                valid.Count > 0 ? valid[0].Header.PresentFields.Count() + "/21" : "incompleto", "HEADER_OBRIGATORIO_AUSENTE");
            if (valid.Count == 1)
            {
                var header = valid[0].Header;
                foreach (var missing in header.KnownMissing)
                    findings.Add(NovoFinding(contexto, "WKS-005", "Coluna oficial ausente",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha,
                        OperationalAuditSeverity.Medio, false, "ITEM_OBRIGATORIO_AUSENTE", valorEncontrado: missing));
                if (header.Extras.Length > 0) findings.Add(NovoFinding(contexto, "WKS-004", "Colunas extras ignoradas",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha,
                    OperationalAuditSeverity.Medio, false, "WEEKLY_COLUNAS_EXTRAS", valorEncontrado: string.Join(" | ", header.Extras)));
                if (header.Duplicates.Length > 0) findings.Add(NovoFinding(contexto, "WKS-004", "Mapeamento de header duplicado",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha,
                    OperationalAuditSeverity.Alto, true, "HEADER_WEEKLY_DUPLICADO", valorEncontrado: string.Join(" | ", header.Duplicates)));
                var sqlErrors = WeeklyScheduleContract.SqlErrors(await _planning.ObterSchemaWeeklyAsync(ct), header.PresentFields);
                Check("WKS-006", "Destinos SQL dos campos encontrados", sqlErrors.Count == 0,
                    header.PresentFields.Count() + " campos compativeis", string.Join(" | ", sqlErrors), "WEEKLY_SQL_INCOMPATIVEL");
            }
            return Result();
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "15", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? valorEsperado = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "15", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                ValorEsperado = valorEsperado, ValorEncontrado = valorEncontrado
            };
    }
}
