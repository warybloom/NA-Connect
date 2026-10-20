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
    /// Etapa 16 — Hold Control (seção 16 do Checkpoint 2). Mesmo padrão de reaproveitamento de
    /// <see cref="IPlanningPreflightService"/> do Stage14 — só a parte de preflight comprovadamente sem
    /// escrita, nunca <c>PlanningControlSyncService</c>.
    /// </summary>
    public sealed class Stage16HoldControlAuditRule : IOperationalAuditRule
    {
        public string Etapa => "16";
        public string NomeEtapa => "Hold Control";

        private static readonly string[] AliasesHoldControl = { "01.15 - HOLD CONTROL", "01.15 - CONTROLE DE HOLDS" };
        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesEngenharia04 = { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IPlanningPreflightService _preflight;

        public Stage16HoldControlAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook, IPlanningPreflightService preflight)
        {
            _acc = acc;
            _workbook = workbook;
            _preflight = preflight;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var definicao = PlanningDefinicoes.HoldControl();

            var pasta = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesHoldControl, false, ct);
            if (pasta == null)
            {
                requirements.Add(NovoRequisito(contexto, "HLD-001", "Pasta de Hold Control", OperationalRequirementStatus.Failed, "pasta localizada", "não localizada"));
                findings.Add(NovoFinding(contexto, "HLD-001", "Pasta de Hold Control não localizada",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "PASTA_HOLD_CONTROL_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pasta.FolderId, false, ct);
            var selection = await PlanningAuditSelection.SelectAsync(conteudo.Itens, _acc, _preflight, definicao, contexto, pasta.ResolvedPath);
            var candidato = selection.Item;
            if (candidato == null || selection.Error is "ABA_INCORRETA" or "PADRAO_INCORRETO" or "PADRAO_AMBIGUO")
            {
                findings.Add(NovoFinding(contexto, "HLD-001", "Hold Control: " + selection.Error,
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, selection.Error ?? "ABA_INCORRETA", pastaEncontrada: pasta.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            ResultadoPreflightPlanejamento resultado = selection.Result!;

            // Ver comentário equivalente em Stage14ScopeDeviationAuditRule: quando falta uma coluna
            // OBRIGATÓRIA por completo, o preflight reaproveitado falha ANTES de popular ColunasAusentes
            // (PlanningHeaderResolver exige todos os campos obrigatórios na mesma linha para considerá-la
            // candidata a cabeçalho) — por isso os dois caminhos (falha total vs. ColunasAusentes) juntos
            // cobrem HLD-002 por completo.
            if (!resultado.Sucesso)
            {
                requirements.Add(NovoRequisito(contexto, "HLD-002", "Cabeçalho localizado com todas as colunas obrigatórias",
                    OperationalRequirementStatus.Failed, "cabeçalho localizado", resultado.CodigoResultado ?? "falha no preflight"));
                findings.Add(NovoFinding(contexto, "HLD-002", $"Coluna(s) obrigatória(s) ausente(s) no arquivo de Hold Control: {resultado.Mensagem}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "COLUNA_OBRIGATORIA_AUSENTE", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            requirements.Add(NovoRequisito(contexto, "HLD-002", "Colunas obrigatórias presentes",
                !resultado.ColunasAusentes.Any(c => definicao.CamposMinimosCabecalho.Contains(c)) ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 colunas ausentes", string.Join(", ", resultado.ColunasAusentes.Where(c => definicao.CamposMinimosCabecalho.Contains(c)))));
            foreach (var colunaAusente in resultado.ColunasAusentes.Where(c => definicao.CamposMinimosCabecalho.Contains(c)))
            {
                findings.Add(NovoFinding(contexto, "HLD-002", $"Coluna '{colunaAusente}' ausente no arquivo de Hold Control",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "COLUNA_OBRIGATORIA_AUSENTE", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name,
                    abaEncontrada: resultado.AbaSelecionada, campo: colunaAusente));
            }

            // HLD-003 — NumeroDocumentoNA preenchido sem correspondência na LDP ou ACC.
            var codigosNaNaLdp = await ObterCodigosNaDaLdpAsync(contexto, ct);
            var codigosNoAcc = await ObterCodigosNaDoAccAsync(contexto, ct);

            int semCorrespondencia = 0;
            if (resultado.Sucesso && resultado.Tabela != null)
            {
                foreach (System.Data.DataRow linha in resultado.Tabela.Rows)
                {
                    string? numeroDocumentoNa = ObterTexto(linha, "N_NA_DOCUMENTO");
                    if (string.IsNullOrWhiteSpace(numeroDocumentoNa)) continue;

                    bool correspondeNaLdp = codigosNaNaLdp.Contains(numeroDocumentoNa);
                    bool correspondeNoAcc = codigosNoAcc.Any(c => c.Contains(numeroDocumentoNa, StringComparison.OrdinalIgnoreCase));

                    if (!correspondeNaLdp && !correspondeNoAcc)
                    {
                        semCorrespondencia++;
                        findings.Add(NovoFinding(contexto, "HLD-003", $"NumeroDocumentoNA '{numeroDocumentoNa}' preenchido sem correspondência na LDP ou ACC",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "NUMERO_DOCUMENTO_NA_SEM_CORRESPONDENCIA", pastaEncontrada: pasta.ResolvedPath, nomeArquivo: candidato.Name,
                            documentoNa: numeroDocumentoNa));
                    }
                }
            }
            requirements.Add(NovoRequisito(contexto, "HLD-003", "Todo NumeroDocumentoNA tem correspondência na LDP ou ACC",
                semCorrespondencia == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0", semCorrespondencia.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private async Task<IReadOnlySet<string>> ObterCodigosNaDaLdpAsync(OperationalAuditContext contexto, System.Threading.CancellationToken ct)
        {
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            if (pastaGestao == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidato = conteudo.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidato == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
            var workbook = _workbook.AbrirSomenteLeitura(stream);
            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));
            if (nomeAbaLdp == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, valores) in workbook.Abas[nomeAbaLdp].Linhas)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), "N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Trim(), "N&A", StringComparison.OrdinalIgnoreCase));
                if (chave != null && !string.IsNullOrWhiteSpace(valores[chave])) codigos.Add(valores[chave]!.Trim());
            }
            return codigos;
        }

        private async Task<IReadOnlyList<string>> ObterCodigosNaDoAccAsync(OperationalAuditContext contexto, System.Threading.CancellationToken ct)
        {
            var pastaEngenharia = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEngenharia04, false, ct);
            if (pastaEngenharia == null) return Array.Empty<string>();

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, false, ct);
            return conteudo.Itens.Select(i => i.Name).ToList();
        }

        private static string? ObterTexto(System.Data.DataRow linha, string coluna) =>
            linha.Table.Columns.Contains(coluna) && linha[coluna] != DBNull.Value ? linha[coluna]?.ToString() : null;

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "16", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? campo = null, string? documentoNa = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "16", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                Campo = campo, DocumentoNa = documentoNa
            };
    }
}
