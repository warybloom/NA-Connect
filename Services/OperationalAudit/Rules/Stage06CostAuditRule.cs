using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 6 — Custo (Checkpoint 3). Fonte de referência (nunca chamada):
    /// <c>Services\LdpToCustoSyncService.cs</c>, método <c>ExecuteFullSyncProcessAsync</c>. Reproduz
    /// exatamente os contratos reais: abas "LDP" e "CUSTO" obrigatórias (linhas 70-77), cabeçalho fixo na
    /// linha 7 (LINHA_CABECALHO=7, linha 23/189-192), chave "N&amp;A DOCUMENT NUMBER" com 3 aliases só do
    /// lado LDP (linhas 104-106) — CUSTO só aceita o literal exato, sem alias (linha 84/143). Colunas
    /// não-chave copiadas best-effort: ITEM, DISCIPLINE, N&amp;A DOCUMENT NUMBER, CLIENT DOCUMENT NUMBER,
    /// DOCUMENT TITLE, FORMAT, DOCUMENT STATUS, CR, CR-OS, AREA CODE, DISCIPLINE ACTIVITY, DOC TYPE CODE,
    /// SEQ (linhas 83-87) — nunca uma lista diferente da automação real.
    /// </summary>
    public sealed class Stage06CostAuditRule : IOperationalAuditRule
    {
        public string Etapa => "6";
        public string NomeEtapa => "Custo";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesChaveNaLdp = { "N&A DOCUMENT NUMBER", "NÚMERO DOCUMENTO N&A", "NÚMERO DOCUMENTO N&A " };
        private static readonly string[] ColunasNaoChave =
        {
            "ITEM", "DISCIPLINE", "CLIENT DOCUMENT NUMBER", "DOCUMENT TITLE", "FORMAT", "DOCUMENT STATUS",
            "CR", "CR-OS", "AREA CODE", "DISCIPLINE ACTIVITY", "DOC TYPE CODE", "SEQ"
        };
        private const int LinhaCabecalho = 7;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage06CostAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _acc = acc;
            _workbook = workbook;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            if (pastaGestao == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidato = conteudoGestao.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidato == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
            var workbook = _workbook.AbrirSomenteLeitura(stream, linhaDeCabecalhoPadrao: LinhaCabecalho);

            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => string.Equals(n.Trim(), "LDP", StringComparison.OrdinalIgnoreCase));
            var nomeAbaCusto = workbook.NomesDasAbas.FirstOrDefault(n => string.Equals(n.Trim(), "CUSTO", StringComparison.OrdinalIgnoreCase));

            bool ambasPresentes = nomeAbaLdp != null && nomeAbaCusto != null;
            requirements.Add(NovoRequisito(contexto, "CST-001", "Abas LDP e CUSTO presentes",
                ambasPresentes ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "ambas presentes", $"LDP={(nomeAbaLdp != null ? "presente" : "ausente")}, CUSTO={(nomeAbaCusto != null ? "presente" : "ausente")}"));
            if (!ambasPresentes)
            {
                findings.Add(NovoFinding(contexto, "CST-001", $"Aba obrigatória ausente — LDP={(nomeAbaLdp != null ? "presente" : "ausente")}, CUSTO={(nomeAbaCusto != null ? "presente" : "ausente")}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Critico,
                    bloqueiaEtapa: true, "ABA_LDP_OU_CUSTO_AUSENTE", nomeArquivo: candidato.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var abaLdp = workbook.Abas[nomeAbaLdp!];
            var abaCusto = workbook.Abas[nomeAbaCusto!];

            string? colunaChaveLdp = abaLdp.Cabecalhos.FirstOrDefault(h => AliasesChaveNaLdp.Any(a => string.Equals(a.Trim(), h?.Trim(), StringComparison.OrdinalIgnoreCase)));
            requirements.Add(NovoRequisito(contexto, "CST-002", "Cabeçalho N&A DOCUMENT NUMBER (ou alias) na LDP",
                colunaChaveLdp != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "encontrado", colunaChaveLdp ?? "não encontrado"));
            if (colunaChaveLdp == null)
            {
                findings.Add(NovoFinding(contexto, "CST-002", "Cabeçalho 'N&A DOCUMENT NUMBER' (ou alias aceito) não localizado na linha 7 da LDP",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "CABECALHO_CHAVE_AUSENTE_LDP", nomeArquivo: candidato.Name, abaEncontrada: nomeAbaLdp));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var colunasAusentes = new List<string>();
            foreach (var coluna in ColunasNaoChave)
            {
                bool presenteLdp = abaLdp.Cabecalhos.Any(h => string.Equals(h?.Trim(), coluna, StringComparison.OrdinalIgnoreCase));
                bool presenteCusto = abaCusto.Cabecalhos.Any(h => string.Equals(h?.Trim(), coluna, StringComparison.OrdinalIgnoreCase));
                if (!presenteLdp || !presenteCusto) colunasAusentes.Add($"{coluna} (LDP={(presenteLdp ? "sim" : "não")}, CUSTO={(presenteCusto ? "sim" : "não")})");
            }
            requirements.Add(NovoRequisito(contexto, "CST-003", "Colunas não-chave presentes em LDP e CUSTO",
                colunasAusentes.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 ausentes", colunasAusentes.Count.ToString()));
            if (colunasAusentes.Count > 0)
            {
                findings.Add(NovoFinding(contexto, "CST-003", $"Coluna(s) não-chave ausente(s) em CUSTO/LDP (serão ignoradas silenciosamente pela sincronização real): {string.Join(", ", colunasAusentes)}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Baixo,
                    bloqueiaEtapa: false, "COLUNA_NAO_CHAVE_AUSENTE", nomeArquivo: candidato.Name, abaEncontrada: nomeAbaLdp,
                    evidencia: string.Join(" | ", colunasAusentes)));
            }

            var codigosNaLdp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int linhasSemNa = 0;
            foreach (var (_, valores) in abaLdp.Linhas)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), colunaChaveLdp, StringComparison.OrdinalIgnoreCase));
                string? codigo = chave != null ? valores[chave]?.Trim() : null;
                if (string.IsNullOrWhiteSpace(codigo)) { linhasSemNa++; continue; }
                codigosNaLdp.Add(codigo);
            }
            requirements.Add(NovoRequisito(contexto, "CST-004", "Linhas da LDP com N&A Document Number preenchido",
                linhasSemNa == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Warning,
                "0 linhas sem N&A", linhasSemNa.ToString()));
            if (linhasSemNa > 0)
            {
                findings.Add(NovoFinding(contexto, "CST-004", $"{linhasSemNa} linha(s) real(is) da LDP sem N&A Document Number — ignoradas silenciosamente pela sincronização de Custo real",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Baixo,
                    bloqueiaEtapa: false, "LINHA_LDP_SEM_NA", nomeArquivo: candidato.Name, abaEncontrada: nomeAbaLdp));
            }

            var codigosNaCusto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, valores) in abaCusto.Linhas)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), "N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase));
                string? codigo = chave != null ? valores[chave]?.Trim() : null;
                if (!string.IsNullOrWhiteSpace(codigo)) codigosNaCusto.Add(codigo);
            }

            var somenteNaLdp = codigosNaLdp.Except(codigosNaCusto, StringComparer.OrdinalIgnoreCase).ToList();
            var somenteNoCusto = codigosNaCusto.Except(codigosNaLdp, StringComparer.OrdinalIgnoreCase).ToList();
            int divergencias = somenteNaLdp.Count + somenteNoCusto.Count;

            requirements.Add(NovoRequisito(contexto, "CST-005", "Chaves N&A consistentes entre LDP e CUSTO",
                divergencias == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 divergências", divergencias.ToString()));
            if (divergencias > 0)
            {
                findings.Add(NovoFinding(contexto, "CST-005", $"Divergência de chaves entre LDP e CUSTO — {somenteNaLdp.Count} código(s) só na LDP, {somenteNoCusto.Count} código(s) só em CUSTO",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "DIVERGENCIA_CHAVES_LDP_CUSTO", nomeArquivo: candidato.Name,
                    evidencia: $"Só LDP: {string.Join(", ", somenteNaLdp.Take(20))} | Só CUSTO: {string.Join(", ", somenteNoCusto.Take(20))}"));
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "6", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? abaEncontrada = null, string? evidencia = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "6", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada, Evidencia = evidencia
            };
    }
}
