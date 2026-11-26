using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 4 — GRD (Checkpoint 3). Fonte de referência (nunca chamada):
    /// <c>Services\GrdToLdpSyncService.cs</c>, método <c>SyncGrdsToLdpAsync</c>. Reproduz exatamente os
    /// contratos reais encontrados por pesquisa de código (nunca uma lista/critério inventado): aliases da
    /// pasta 02.02 (linhas 40-43), candidato = .xlsx/.xlsm (linha 281), template G3(+H3+I3) contém "GRD" e
    /// G4 é uma data válida (linhas 106-123), documentos cobertos em C24.. (linhas 126-138), desempate por
    /// EmissionDate mais recente e, em empate, TransmittalNumber ordinalmente maior (linhas 150-160), e
    /// correspondência por igualdade exata case-insensitive (linha 230/66) — a mesma que o serviço real usa,
    /// nunca <see cref="NA_Connect.Services.DocumentMatchingService"/> (que o serviço real também não usa
    /// aqui). Coluna de TRANSMITTAL/REMESSA/GRD na LDP, linha 7 (linhas 215-223).
    /// </summary>
    public sealed class Stage04GrdAuditRule : IOperationalAuditRule
    {
        public string Etapa => "4";
        public string NomeEtapa => "GRD";

        private static readonly string[] AliasesGrd = { "02.02 - OUTGOING TRANSMITTALS TO CLIENT", "02.02 - GRD ENVIADA AO CLIENTE" };
        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private const int LinhaCabecalhoLdp = 7;
        private const int LinhaTemplateIdentificador = 3;
        private const int LinhaTemplateData = 4;
        private const int ColunaDocumento = 3; // C
        private const int ColunaPropositoEmissao = 7; // G
        private const int LinhaInicioDocumentos = 24;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage04GrdAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _acc = acc;
            _workbook = workbook;
        }

        private sealed record GrdInfo(string TransmittalNumber, DateTime EmissionDate, string ArquivoOrigem, IReadOnlyList<string> Documentos);

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaGrd = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesGrd, true, ct);
            requirements.Add(NovoRequisito(contexto, "GRD-001", "Pasta 02.02",
                pastaGrd != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pastaGrd?.ResolvedPath ?? "não localizada"));
            if (pastaGrd == null)
            {
                findings.Add(NovoFinding(contexto, "GRD-001", "Pasta 02.02 não localizada pelos aliases operacionais",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "PASTA_GRD_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudoGrd = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGrd.FolderId, true, ct);
            var candidatos = conteudoGrd.Itens.Where(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn)).ToList();

            requirements.Add(NovoRequisito(contexto, "GRD-002", "Candidato .xlsx/.xlsm válido em 02.02",
                candidatos.Count > 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "ao menos 1 candidato", candidatos.Count.ToString()));
            if (candidatos.Count == 0)
            {
                findings.Add(NovoFinding(contexto, "GRD-002", "Pasta 02.02 existe, mas nenhum arquivo .xlsx/.xlsm candidato foi encontrado",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoAusenteAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, "NENHUM_CANDIDATO_GRD", pastaEncontrada: pastaGrd.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var consolidado = new Dictionary<string, GrdInfo>(StringComparer.OrdinalIgnoreCase);
            int forasDoTemplate = 0;

            foreach (var item in candidatos)
            {
                await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, item.LatestVersionUrn!, ct);
                var workbook = _workbook.AbrirSomenteLeitura(stream, linhaDeCabecalhoPadrao: 0);
                var nomeAba = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("GRD", StringComparison.OrdinalIgnoreCase)) ?? workbook.NomesDasAbas.FirstOrDefault();
                if (nomeAba == null) { forasDoTemplate++; continue; }
                var aba = workbook.Abas[nomeAba];

                string rawT = string.Concat(
                    ObterCelula(aba, LinhaTemplateIdentificador, ColunaDocumento + 4),
                    ObterCelula(aba, LinhaTemplateIdentificador, ColunaDocumento + 5),
                    ObterCelula(aba, LinhaTemplateIdentificador, ColunaDocumento + 6)).Trim();
                int posGrd = rawT.IndexOf("GRD", StringComparison.OrdinalIgnoreCase);
                string rawDate = ObterCelula(aba, LinhaTemplateData, ColunaDocumento + 4).Trim();

                if (string.IsNullOrEmpty(rawT) || posGrd < 0 || !DateTime.TryParse(rawDate, out var emissionDate))
                {
                    forasDoTemplate++;
                    findings.Add(NovoFinding(contexto, "GRD-003", $"Arquivo '{item.Name}' fora do template GRD (identificador='{rawT}', data='{rawDate}')",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "GRD_FORA_DO_TEMPLATE", nomeArquivo: item.Name, abaEncontrada: nomeAba));
                    continue;
                }

                string transmittalNumber = rawT.Substring(posGrd);
                var documentos = new List<string>();
                int ultimaLinha = aba.Linhas.Count > 0 ? aba.Linhas.Keys.Max() : LinhaInicioDocumentos - 1;
                for (int r = LinhaInicioDocumentos; r <= ultimaLinha; r++)
                {
                    string doc = ObterCelula(aba, r, ColunaDocumento).Trim();
                    if (!string.IsNullOrEmpty(doc)) documentos.Add(doc);
                }

                var novaInfo = new GrdInfo(transmittalNumber, emissionDate, item.Name, documentos);
                foreach (var doc in documentos)
                {
                    if (!consolidado.TryGetValue(doc, out var existente))
                    {
                        consolidado[doc] = novaInfo;
                        continue;
                    }

                    bool ehMaisRecente = novaInfo.EmissionDate > existente.EmissionDate;
                    if (!ehMaisRecente && novaInfo.EmissionDate == existente.EmissionDate)
                    {
                        ehMaisRecente = string.Compare(novaInfo.TransmittalNumber, existente.TransmittalNumber, StringComparison.OrdinalIgnoreCase) > 0;
                    }
                    if (ehMaisRecente) consolidado[doc] = novaInfo;
                    // Critério determinístico legítimo do algoritmo real (data + desempate por texto do
                    // transmittal) — nunca reportamos GRD-007 quando o desempate é resolvido por ele.
                }
            }

            // Coluna de LDP para reconciliação (GRD-004/005/006).
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
                    var workbookLdp = _workbook.AbrirSomenteLeitura(streamLdp, linhaDeCabecalhoPadrao: LinhaCabecalhoLdp);
                    var nomeAbaLdp = workbookLdp.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase)) ?? workbookLdp.NomesDasAbas.FirstOrDefault();

                    if (nomeAbaLdp != null)
                    {
                        var abaLdp = workbookLdp.Abas[nomeAbaLdp];
                        bool temColunaTransmittal = abaLdp.Cabecalhos.Any(h =>
                            h != null && (h.Contains("TRANSMITTAL", StringComparison.OrdinalIgnoreCase) ||
                                          h.Contains("REMESSA", StringComparison.OrdinalIgnoreCase) ||
                                          h.Contains("GRD", StringComparison.OrdinalIgnoreCase)));

                        requirements.Add(NovoRequisito(contexto, "GRD-006", "Coluna TRANSMITTAL/REMESSA/GRD na LDP",
                            temColunaTransmittal ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                            "coluna encontrada", temColunaTransmittal ? "encontrada" : "ausente"));

                        if (!temColunaTransmittal)
                        {
                            findings.Add(NovoFinding(contexto, "GRD-006", "Cabeçalho da LDP sem coluna de TRANSMITTAL/REMESSA/GRD aceita pelo algoritmo real — o bloco operacional de atualização de GRD seria silenciosamente pulado",
                                OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                                bloqueiaEtapa: false, "COLUNA_TRANSMITTAL_AUSENTE", nomeArquivo: candidatoLdp.Name, abaEncontrada: nomeAbaLdp));
                        }
                        else
                        {
                            var codigosLdp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var (_, valores) in abaLdp.Linhas)
                            {
                                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), "N&A DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase));
                                if (chave != null && !string.IsNullOrWhiteSpace(valores[chave])) codigosLdp.Add(valores[chave]!.Trim());
                            }

                            int semCobertura = 0;
                            foreach (var codigo in codigosLdp)
                            {
                                if (!consolidado.ContainsKey(codigo))
                                {
                                    semCobertura++;
                                    findings.Add(NovoFinding(contexto, "GRD-004", $"Documento '{codigo}' da LDP nunca coberto por GRD válida",
                                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoAusenteAcc, OperationalAuditSeverity.Medio,
                                        bloqueiaEtapa: false, "DOCUMENTO_LDP_SEM_GRD", documentoNa: codigo));
                                }
                            }
                            requirements.Add(NovoRequisito(contexto, "GRD-004", "Documentos da LDP cobertos por GRD válida",
                                semCobertura == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, "0 sem cobertura", semCobertura.ToString()));

                            int semLinhaLdp = 0;
                            foreach (var doc in consolidado.Keys)
                            {
                                if (!codigosLdp.Contains(doc))
                                {
                                    semLinhaLdp++;
                                    findings.Add(NovoFinding(contexto, "GRD-005", $"Documento '{doc}' presente na GRD sem linha correspondente na LDP",
                                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoAusenteLdp, OperationalAuditSeverity.Medio,
                                        bloqueiaEtapa: false, "DOCUMENTO_GRD_SEM_LDP", documentoNa: doc));
                                }
                            }
                            requirements.Add(NovoRequisito(contexto, "GRD-005", "Documentos da GRD com linha correspondente na LDP",
                                semLinhaLdp == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed, "0 sem linha LDP", semLinhaLdp.ToString()));
                        }
                    }
                }
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string ObterCelula(AuditWorksheetSnapshot aba, int linha, int coluna) =>
            aba.Linhas.TryGetValue(linha, out var valores) && valores.TryGetValue($"Col{coluna}", out var v) ? v ?? string.Empty : string.Empty;

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "4", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? documentoNa = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "4", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada, DocumentoNa = documentoNa
            };
    }
}
