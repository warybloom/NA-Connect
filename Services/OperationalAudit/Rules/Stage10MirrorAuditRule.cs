using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 10 — Espelhamento (seção 13 do Checkpoint 2). Nunca chama
    /// <c>LdpUpdateService.MirrorLdpControlToProjectAsync</c>. Como o operacional real hoje não persiste
    /// nenhum marcador de "última tentativa"/status de upload, MIR-009/MIR-010 usam heurísticas honestas
    /// baseadas em contagem de linhas (documentado abaixo) em vez de um sinal direto que simplesmente não
    /// existe no sistema hoje.
    /// </summary>
    public sealed class Stage10MirrorAuditRule : IOperationalAuditRule
    {
        public string Etapa => "10";
        public string NomeEtapa => "Espelhamento da LDP";

        private static readonly string[] AliasesEspelho = { "01.06 - LISTA DE DOCUMENTOS PROJETO", "01.06 - MASTER DOCUMENT LIST" };
        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage10MirrorAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _acc = acc;
            _workbook = workbook;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            // LDP principal (fonte) — mesma pasta de Gestão das demais regras.
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            AccItemRef? candidatoLdpFonte = null;
            AuditWorksheetSnapshot? abaLdpFonte = null;

            if (pastaGestao != null)
            {
                var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
                candidatoLdpFonte = conteudoGestao.Itens.FirstOrDefault(i =>
                    (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(i.LatestVersionUrn));

                if (candidatoLdpFonte != null)
                {
                    await using var streamFonte = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdpFonte.LatestVersionUrn!, ct);
                    var workbookFonte = _workbook.AbrirSomenteLeitura(streamFonte);
                    var nomeAbaLdpFonte = workbookFonte.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));
                    if (nomeAbaLdpFonte != null) abaLdpFonte = workbookFonte.Abas[nomeAbaLdpFonte];
                }
            }

            requirements.Add(NovoRequisito(contexto, "MIR-003", "LDP principal disponível",
                candidatoLdpFonte != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "arquivo disponível", candidatoLdpFonte != null ? candidatoLdpFonte.Name : "indisponível"));
            if (candidatoLdpFonte == null)
            {
                findings.Add(NovoFinding(contexto, "MIR-003", "LDP principal indisponível — não foi possível localizar/baixar o arquivo fonte",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "LDP_PRINCIPAL_INDISPONIVEL"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // Pasta do espelho.
            var pastaEspelho = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesEspelho, false, ct);
            requirements.Add(NovoRequisito(contexto, "MIR-001", "Pasta 01.06",
                pastaEspelho != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pastaEspelho?.ResolvedPath ?? "não localizada"));
            if (pastaEspelho == null)
            {
                findings.Add(NovoFinding(contexto, "MIR-001", "Pasta 01.06 ausente",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "PASTA_ESPELHO_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudoEspelho = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaEspelho.FolderId, false, ct);
            var candidatoEspelho = conteudoEspelho.Itens.FirstOrDefault(i =>
                string.Equals(i.Name, candidatoLdpFonte.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.LatestVersionUrn));

            // MIR-002 — primeira publicação (comportamento esperado, não um problema).
            if (candidatoEspelho == null)
            {
                findings.Add(NovoFinding(contexto, "MIR-002", "Primeira publicação do espelho ainda não existe (comportamento esperado)",
                    OperationalAuditErrorDomain.ComportamentoEsperado, OperationalAuditClassification.ComportamentoEsperado, OperationalAuditSeverity.Informativo,
                    bloqueiaEtapa: false, "PRIMEIRA_PUBLICACAO", pastaEncontrada: pastaEspelho.ResolvedPath, nomeArquivo: candidatoLdpFonte.Name));

                requirements.Add(NovoRequisito(contexto, "MIR-002", "Espelho existe (ou é comprovadamente a primeira publicação)",
                    OperationalRequirementStatus.NotApplicable, "espelho existente OU primeira publicação", "primeira publicação"));

                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // MIR-011 — pasta correta (verificação estrutural: o item precisa ter sido encontrado dentro da própria pasta 01.06).
            bool pastaCorreta = string.Equals(candidatoEspelho.ParentFolderId, pastaEspelho.FolderId, StringComparison.Ordinal);
            requirements.Add(NovoRequisito(contexto, "MIR-011", "Espelho na pasta 01.06 correta",
                pastaCorreta ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                pastaEspelho.FolderId, candidatoEspelho.ParentFolderId));
            if (!pastaCorreta)
            {
                findings.Add(NovoFinding(contexto, "MIR-011", $"Espelho '{candidatoEspelho.Name}' encontrado em pasta incorreta",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "ESPELHO_EM_PASTA_INCORRETA", nomeArquivo: candidatoEspelho.Name));
            }

            await using var streamEspelho = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoEspelho.LatestVersionUrn!, ct);
            var workbookEspelho = _workbook.AbrirSomenteLeitura(streamEspelho);

            // MIR-004 — aba LDP no espelho.
            var nomeAbaLdpEspelho = workbookEspelho.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));
            requirements.Add(NovoRequisito(contexto, "MIR-004", "Aba LDP no espelho",
                nomeAbaLdpEspelho != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "aba com \"LDP\" no nome", nomeAbaLdpEspelho ?? "ausente"));
            if (nomeAbaLdpEspelho == null)
            {
                findings.Add(NovoFinding(contexto, "MIR-004", "Aba LDP ausente no espelho",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ABA_LDP_AUSENTE_NO_ESPELHO", nomeArquivo: candidatoEspelho.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            var abaLdpEspelho = workbookEspelho.Abas[nomeAbaLdpEspelho];

            // MIR-007/MIR-008 — abas mantidas/removidas (a regra operacional real só mantém "LDP"/"CAPA").
            bool temAbaCapa = workbookEspelho.NomesDasAbas.Any(n => n.Contains("CAPA", StringComparison.OrdinalIgnoreCase));
            var abasNaoPermitidas = workbookEspelho.NomesDasAbas
                .Where(n => !n.Contains("LDP", StringComparison.OrdinalIgnoreCase) && !n.Contains("CAPA", StringComparison.OrdinalIgnoreCase))
                .ToList();

            requirements.Add(NovoRequisito(contexto, "MIR-008", "Aba CAPA presente no espelho",
                temAbaCapa ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Inconclusive,
                "aba CAPA presente", temAbaCapa ? "presente" : "ausente"));
            if (!temAbaCapa)
            {
                findings.Add(NovoFinding(contexto, "MIR-008", "Aba obrigatória (CAPA) parece ter sido removida do espelho",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "ABA_OBRIGATORIA_REMOVIDA", nomeArquivo: candidatoEspelho.Name));
            }

            requirements.Add(NovoRequisito(contexto, "MIR-007", "Nenhuma aba privada indevidamente mantida",
                abasNaoPermitidas.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "só LDP/CAPA", string.Join(", ", abasNaoPermitidas)));
            foreach (var abaIndevida in abasNaoPermitidas)
            {
                findings.Add(NovoFinding(contexto, "MIR-007", $"Aba '{abaIndevida}' indevidamente mantida no espelho (só LDP/CAPA deveriam sobreviver)",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Baixo,
                    bloqueiaEtapa: false, "ABA_PRIVADA_MANTIDA", nomeArquivo: candidatoEspelho.Name, abaEncontrada: abaIndevida));
            }

            // MIR-006 — fórmula ainda existente onde deveria haver valor.
            requirements.Add(NovoRequisito(contexto, "MIR-006", "Nenhuma fórmula residual na aba LDP do espelho",
                abaLdpEspelho.CelulasComFormula.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 fórmulas", abaLdpEspelho.CelulasComFormula.Count.ToString()));
            foreach (var endereco in abaLdpEspelho.CelulasComFormula)
            {
                findings.Add(NovoFinding(contexto, "MIR-006", $"Fórmula ainda existente na célula {endereco} do espelho, onde deveria haver um valor resolvido",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "FORMULA_RESIDUAL_NO_ESPELHO", nomeArquivo: candidatoEspelho.Name, abaEncontrada: nomeAbaLdpEspelho, campo: endereco));
            }

            // MIR-005 — coluna-chave ausente ou vazia.
            var colunaChave = abaLdpEspelho.Cabecalhos.FirstOrDefault(c =>
                c.Contains("N&A", StringComparison.OrdinalIgnoreCase) || c.Contains("DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase));
            bool colunaChaveOk = colunaChave != null && abaLdpEspelho.Linhas.Values.Any(v => v.TryGetValue(colunaChave, out var val) && !string.IsNullOrWhiteSpace(val));

            requirements.Add(NovoRequisito(contexto, "MIR-005", "Coluna-chave presente e com valores no espelho",
                colunaChaveOk ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "coluna presente e preenchida", colunaChave ?? "ausente"));
            if (!colunaChaveOk)
            {
                findings.Add(NovoFinding(contexto, "MIR-005", "Coluna-chave ausente ou vazia em todas as linhas do espelho",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "COLUNA_CHAVE_AUSENTE_OU_VAZIA", nomeArquivo: candidatoEspelho.Name, abaEncontrada: nomeAbaLdpEspelho));
            }

            // MIR-009/MIR-012 — comparação de linhas fonte x espelho.
            if (abaLdpFonte != null)
            {
                int linhasFonte = abaLdpFonte.Linhas.Count;
                int linhasEspelho = abaLdpEspelho.Linhas.Count;

                requirements.Add(NovoRequisito(contexto, "MIR-012", "Quantidade de linhas do espelho igual à da fonte",
                    linhasFonte == linhasEspelho ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    linhasFonte.ToString(), linhasEspelho.ToString()));
                if (linhasFonte != linhasEspelho)
                {
                    findings.Add(NovoFinding(contexto, "MIR-012", $"Quantidade de linhas divergente: fonte={linhasFonte}, espelho={linhasEspelho}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "QUANTIDADE_DE_LINHAS_DIVERGENTE", nomeArquivo: candidatoEspelho.Name,
                        valorEsperado: linhasFonte.ToString(), valorEncontrado: linhasEspelho.ToString()));
                }

                bool desatualizado = linhasFonte > linhasEspelho;
                requirements.Add(NovoRequisito(contexto, "MIR-009", "Espelho não está desatualizado em relação à fonte",
                    !desatualizado ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    $">= {linhasFonte} linhas", linhasEspelho.ToString()));
                if (desatualizado)
                {
                    findings.Add(NovoFinding(contexto, "MIR-009", $"Espelho desatualizado — fonte tem {linhasFonte} linhas reais, espelho tem só {linhasEspelho}",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "ESPELHO_DESATUALIZADO", nomeArquivo: candidatoEspelho.Name));
                }

                // MIR-010 — evidência indireta de falha de upload anterior: fonte tem dados reais, espelho existe mas está vazio.
                bool evidenciaDeFalhaAnterior = linhasFonte > 0 && linhasEspelho == 0;
                if (evidenciaDeFalhaAnterior)
                {
                    findings.Add(NovoFinding(contexto, "MIR-010", "Espelho existe mas está sem nenhuma linha real, enquanto a fonte tem dados — possível evidência de falha de upload anterior",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: false, "EVIDENCIA_DE_FALHA_DE_UPLOAD_ANTERIOR", nomeArquivo: candidatoEspelho.Name));
                }
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "10", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? campo = null, string? valorEsperado = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "10", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                Campo = campo, ValorEsperado = valorEsperado, ValorEncontrado = valorEncontrado
            };
    }
}
