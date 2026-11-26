using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using NA_Connect.Services;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 2 — Horas e Revisão (Checkpoint 3). Fonte de referência (nunca chamada):
    /// <c>Services\LdpUpdateService.cs</c>, método <c>UpdateLdpFromDatabaseAsync</c>. HR-001 usa a
    /// correspondência documental já aprovada (N&amp;A prioritário, Cliente fallback,
    /// <see cref="DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens"/> — nunca Contains cru), a
    /// mesma semântica consolidada na correção da Stage07. HR-002 reutiliza
    /// <see cref="CustomAttributeStateResolver"/> (mesmo resolvedor já usado pela Stage07 — nunca duplicado).
    /// HR-003 usa EXATAMENTE a condição de invalidez de REV encontrada em
    /// <c>UpdateLdpFromDatabaseAsync</c> (linhas ~545-554: <c>valRev.Contains(":") &amp;&amp; valRev.Length > 10</c>
    /// rejeita, o resto aceita — inclui timestamps, mas NÃO datas puras "AAAA-MM-DD" de 10 caracteres).
    /// HR-004 reproduz a busca de cabeçalho "HH REALIZADO"/"ACTUAL HH" exatamente na linha 7, sem usar
    /// PlanningHeaderResolver (que o método real também não usa).
    /// </summary>
    public sealed class Stage02HoursRevisionAuditRule : IOperationalAuditRule
    {
        public string Etapa => "2";
        public string NomeEtapa => "Horas e Revisão";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private static readonly string[] AliasesEngenharia = { "04 - ENGINEERING", "04. ENGINEERING", "04 - ENGENHARIA", "04. ENGENHARIA" };
        private const int LinhaCabecalhoLdp = 7;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage02HoursRevisionAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook)
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
            if (pastaGestao == null)
            {
                requirements.Add(NovoRequisito(contexto, "HR-000", "Pasta 09.14 (Gestão)", OperationalRequirementStatus.Inconclusive, "pasta localizada", "não localizada"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidatoLdp = conteudoGestao.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidatoLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            await using var streamLdp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdp.LatestVersionUrn!, ct);
            var workbook = _workbook.AbrirSomenteLeitura(streamLdp, linhaDeCabecalhoPadrao: LinhaCabecalhoLdp);
            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase)) ?? workbook.NomesDasAbas.FirstOrDefault();
            if (nomeAbaLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            var abaLdp = workbook.Abas[nomeAbaLdp];

            // HR-004 — cabeçalho HH REALIZADO / ACTUAL HH, EXATAMENTE na linha 7 (mesma faixa do método real
            // — não a resolução genérica de PlanningHeaderResolver, que o método real também não usa).
            bool colunaHorasEncontrada = abaLdp.Cabecalhos.Any(h =>
                (h?.Trim().ToUpperInvariant().Contains("HH REALIZADO") ?? false) || (h?.Trim().ToUpperInvariant().Contains("ACTUAL HH") ?? false));
            requirements.Add(NovoRequisito(contexto, "HR-004", "Cabeçalho HH REALIZADO / ACTUAL HH (linha 7)",
                colunaHorasEncontrada ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "coluna encontrada", colunaHorasEncontrada ? "encontrada" : "não encontrada"));
            if (!colunaHorasEncontrada)
            {
                findings.Add(NovoFinding(contexto, "HR-004", "Cabeçalho 'HH REALIZADO'/'ACTUAL HH' não encontrado na linha 7 da LDP",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "COLUNA_HH_REALIZADO_AUSENTE", nomeArquivo: candidatoLdp.Name, abaEncontrada: nomeAbaLdp));
            }

            var codigosLdp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, valores) in abaLdp.Linhas)
            {
                string? codigoNa = ObterValorDeColuna(valores, "N&A DOCUMENT NUMBER", "N&A");
                string? codigoCliente = ObterValorDeColuna(valores, "CLIENT DOCUMENT NUMBER");
                string? codigo = AuditDocumentCodeResolver.ResolverCodigoDaLinha(codigoNa, codigoCliente);
                if (codigo != null) codigosLdp.Add(codigo);
            }

            var pastaEngenharia = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEngenharia, false, ct);
            if (pastaEngenharia == null)
            {
                requirements.Add(NovoRequisito(contexto, "HR-001", "Pasta de arquivos vivos (04)", OperationalRequirementStatus.Inconclusive, "pasta localizada", "não localizada"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudoEngenharia = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, false, ct);
            var itensVivos = conteudoEngenharia.Itens.Where(i => !string.IsNullOrEmpty(i.LatestVersionUrn) && !i.IsHiddenFolder).ToList();

            // HR-001 — documento da LDP sem arquivo vivo correspondente no ACC, via correspondência por
            // prefixo de tokens (nunca Contains cru — seção 5/6 da correção Stage07, reaproveitada aqui).
            int semCorrespondencia = 0;
            var definicoes = await _acc.ObterDefinicoesDeAtributoAsync(contexto.ProjectIdAcc, pastaEngenharia.FolderId, ct);

            foreach (var codigo in codigosLdp)
            {
                var candidatos = itensVivos.Where(i => DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(i.Name, codigo)).ToList();
                if (candidatos.Count == 0)
                {
                    semCorrespondencia++;
                    findings.Add(NovoFinding(contexto, "HR-001", $"Documento '{codigo}' da LDP sem arquivo vivo correspondente no ACC",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "DOCUMENTO_LDP_SEM_ARQUIVO_VIVO", documentoNa: codigo));
                    continue;
                }

                // HR-002/HR-003 — só avaliados para documentos que TÊM arquivo vivo correspondente.
                foreach (var item in candidatos)
                {
                    bool defExiste = CustomAttributeStateResolver.DefinicaoExiste(definicoes, "REV.") || CustomAttributeStateResolver.DefinicaoExiste(definicoes, "REV");
                    requirements.Add(NovoRequisito(contexto, "HR-002", $"Definição REV/REV. para '{item.Name}'",
                        defExiste ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                        "cadastrada", defExiste ? "cadastrada" : "ausente"));
                    if (!defExiste)
                    {
                        findings.Add(NovoFinding(contexto, "HR-002", $"Arquivo vivo '{item.Name}' sem definição de atributo REV/REV. cadastrada no ACC",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.QualidadeConfiguracaoAcc, OperationalAuditSeverity.Alto,
                            bloqueiaEtapa: false, "DEFINICAO_REV_AUSENTE", nomeArquivo: item.Name, documentoNa: codigo));
                        continue;
                    }

                    var valores = await _acc.ObterValoresDeAtributoDaVersaoAsync(contexto.ProjectIdAcc, item.LatestVersionUrn!, ct);
                    var valorRev = valores.FirstOrDefault(v => string.Equals(v.Name.Trim(), "REV", StringComparison.OrdinalIgnoreCase) || string.Equals(v.Name.Trim(), "REV.", StringComparison.OrdinalIgnoreCase));
                    var estado = CustomAttributeStateResolver.ResolverEstado(valorRev);

                    if (estado == CustomAttributeState.ApiVisibilityInconclusive)
                    {
                        findings.Add(NovoFinding(contexto, "HR-002", $"API não fornece evidência suficiente sobre REV em '{item.Name}' (ausente ou oculto — inconclusivo)",
                            OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.QualidadeConfiguracaoAcc, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "API_VISIBILITY_INCONCLUSIVE", nomeArquivo: item.Name, documentoNa: codigo));
                        continue;
                    }

                    if (estado != CustomAttributeState.PresentVisible) continue; // ausente/oculto sem evidência: nada mais a checar aqui.

                    // HR-003 — EXATAMENTE a condição operacional de invalidez de REV.
                    string valRev = valorRev.Valor ?? string.Empty;
                    bool invalido = !string.IsNullOrEmpty(valRev) && valRev.Contains(':') && valRev.Length > 10;
                    requirements.Add(NovoRequisito(contexto, "HR-003", $"REV válida em '{item.Name}'",
                        !invalido ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                        "não descartada pela regra operacional", invalido ? "descartada (parece timestamp)" : "válida"));
                    if (invalido)
                    {
                        findings.Add(NovoFinding(contexto, "HR-003", $"Valor de REV '{valRev}' em '{item.Name}' seria descartado pela própria regra operacional (parece timestamp)",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "REV_INVALIDA_PELA_REGRA_OPERACIONAL", nomeArquivo: item.Name, documentoNa: codigo, valorEncontrado: valRev));
                    }
                }
            }

            requirements.Add(NovoRequisito(contexto, "HR-001", "Documentos da LDP com arquivo vivo correspondente",
                semCorrespondencia == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 sem correspondência", semCorrespondencia.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string? ObterValorDeColuna(IReadOnlyDictionary<string, string?> valores, params string[] candidatos)
        {
            foreach (var candidato in candidatos)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), candidato, StringComparison.OrdinalIgnoreCase));
                if (chave != null) return valores[chave];
            }
            return null;
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "2", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? abaEncontrada = null,
            string? documentoNa = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "2", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada, DocumentoNa = documentoNa, ValorEncontrado = valorEncontrado
            };
    }
}
