using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Planning;
using OfficeOpenXml;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 14 — Scope Deviation (seção 14 do Checkpoint 2). Reaproveita SOMENTE a parte de preflight
    /// comprovadamente sem escrita (<see cref="IPlanningPreflightService"/> + <see cref="PlanningDefinicoes.ScopeDeviation"/>
    /// — puros, nunca abrem <c>SqlConnection</c>, nunca chamam <c>Save</c>/<c>SaveAs</c>) — não é o
    /// <c>PlanningControlSyncService</c> (esse continua intocado e nunca é referenciado aqui).
    /// </summary>
    public sealed class Stage14ScopeDeviationAuditRule : IOperationalAuditRule
    {
        public string Etapa => "14";
        public string NomeEtapa => "Scope Deviation";

        private static readonly string[] AliasesScopeDeviation = { "01.12 - SCOPE DEVIATION", "01.12 - CONTROLE DE DESVIO" };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IPlanningPreflightService _preflight;

        public Stage14ScopeDeviationAuditRule(IAuditAccReadOnlyAdapter acc, IPlanningPreflightService preflight)
        {
            _acc = acc;
            _preflight = preflight;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var definicao = PlanningDefinicoes.ScopeDeviation();

            var pasta = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesScopeDeviation, false, ct);
            if (pasta == null)
            {
                requirements.Add(NovoRequisito(contexto, "SCP-001", "Pasta de Scope Deviation", OperationalRequirementStatus.Failed, "pasta localizada", "não localizada"));
                findings.Add(NovoFinding(contexto, "SCP-001", "Pasta de Scope Deviation não localizada",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "PASTA_SCOPE_DEVIATION_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pasta.FolderId, false, ct);
            var selection = await PlanningAuditSelection.SelectAsync(conteudo.Itens, _acc, _preflight, definicao, contexto, pasta.ResolvedPath);
            var candidato = selection.Item;
            if (candidato == null || selection.Error is "ABA_INCORRETA" or "PADRAO_INCORRETO" or "PADRAO_AMBIGUO")
            {
                findings.Add(NovoFinding(contexto, "SCP-001", "Scope Deviation: " + selection.Error,
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, selection.Error ?? "ABA_INCORRETA", pastaEncontrada: pasta.ResolvedPath, probableOwner: "Autor do template / Planejamento"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            ResultadoPreflightPlanejamento resultado = selection.Result!;

            // Quando uma coluna OBRIGATÓRIA falta por completo em toda a faixa de busca do cabeçalho
            // (linhas 1-10), o preflight reaproveitado nem chega a popular ColunasAusentes — ele falha antes,
            // com Sucesso=false e a mensagem descrevendo os campos ausentes (PlanningHeaderResolver exige
            // TODOS os campos obrigatórios na mesma linha para sequer considerá-la candidata a cabeçalho).
            // ColunasAusentes só é populada quando o cabeçalho FOI localizado (logo, só pode listar colunas
            // OPCIONAIS ausentes nesse caso) — os dois caminhos juntos cobrem SCP-002 por completo.
            if (!resultado.Sucesso)
            {
                requirements.Add(NovoRequisito(contexto, "SCP-002", "Cabeçalho localizado com todas as colunas obrigatórias",
                    OperationalRequirementStatus.Failed, "cabeçalho localizado", resultado.CodigoResultado ?? "falha no preflight"));
                findings.Add(NovoFinding(contexto, "SCP-002", $"Coluna(s) obrigatória(s) ausente(s) no arquivo de Scope Deviation: {resultado.Mensagem}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "COLUNA_OBRIGATORIA_AUSENTE", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            requirements.Add(NovoRequisito(contexto, "SCP-002", "Colunas obrigatórias presentes",
                !resultado.ColunasAusentes.Any(c => definicao.CamposMinimosCabecalho.Contains(c)) ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 colunas ausentes", string.Join(", ", resultado.ColunasAusentes.Where(c => definicao.CamposMinimosCabecalho.Contains(c)))));
            foreach (var colunaAusente in resultado.ColunasAusentes.Where(c => definicao.CamposMinimosCabecalho.Contains(c)))
            {
                findings.Add(NovoFinding(contexto, "SCP-002", $"Coluna '{colunaAusente}' ausente no arquivo de Scope Deviation",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "COLUNA_OBRIGATORIA_AUSENTE", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name,
                    abaEncontrada: resultado.AbaSelecionada, campo: colunaAusente));
            }

            int linhasComDataOuStatusInvalido = resultado.RowsWithErrors;
            foreach (var evidence in resultado.DiagnosticosLinhas.Where(l => l.Value.Count > 0).Select(l => $"Linha {l.Key}: " + string.Join(" | ", l.Value)))
                findings.Add(NovoFinding(contexto, "SCP-003", "Registro com valor inválido: " + evidence,
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "LINHA_COM_DATA_OU_STATUS_INVALIDO", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name,
                    abaEncontrada: resultado.AbaSelecionada, campo: "DATA/STATUS", valorEncontrado: evidence));
            requirements.Add(NovoRequisito(contexto, "SCP-003", "Nenhuma linha real com data ou status inválido",
                linhasComDataOuStatusInvalido == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0", linhasComDataOuStatusInvalido.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string? ObterTexto(System.Data.DataRow linha, string coluna) =>
            linha.Table.Columns.Contains(coluna) && linha[coluna] != DBNull.Value ? linha[coluna]?.ToString() : null;

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "14", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? campo = null, string? valorEncontrado = null, string? probableOwner = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "14", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                Campo = campo, ValorEncontrado = valorEncontrado, ResponsavelProvavel = probableOwner
            };
    }
}
