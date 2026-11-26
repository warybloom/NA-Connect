using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Qualidade;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 17 — Qualidade (seção 17 do Checkpoint 2): BASE LANÇAMENTO, RNC, DESVIO DE QUALIDADE, auditados
    /// separadamente. Reaproveita <see cref="QualidadeAbas"/>/<see cref="QualidadeCabecalhos"/>/
    /// <see cref="QualidadeParsers"/> (constantes/parsers puros, sem I/O — não são
    /// <c>QualityControlSyncService</c>) para não duplicar a lista de abas/campos/aliases já validada em
    /// produção. Nunca chama persistência de Qualidade. QLT-004 é consolidado por Projeto+Aba+Campo+Motivo
    /// (nunca um finding por linha) — nunca escreve "FALTA INFORMAÇÃO" em lugar nenhum, só relata que a
    /// automação precisaria usar esse placeholder.
    /// </summary>
    public sealed class Stage17QualityAuditRule : IOperationalAuditRule
    {
        public string Etapa => "17";
        public string NomeEtapa => "Qualidade";

        private static readonly string[] AliasesQualidade =
        {
            "03.06 - NCR NONCONFORMITY REPORT", "05.06 - NCR NONCONFORMITY REPORT",
            "03.06 - RNC RELATÓRIO DE NÃO CONFORMIDADE", "05.06 - RNC RELATÓRIO DE NÃO CONFORMIDADE"
        };

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditQualityReadOnlyAdapter _quality;
        private readonly IAuditDatabaseReadOnlyAdapter _database;

        public Stage17QualityAuditRule(IAuditAccReadOnlyAdapter acc, IAuditQualityReadOnlyAdapter quality, IAuditDatabaseReadOnlyAdapter database)
        {
            _acc = acc;
            _quality = quality;
            _database = database;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;
            int maxEvidencia = Math.Max(1, contexto.Options.MaxEvidencePerFinding);

            var pasta = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesQualidade, false, ct);
            requirements.Add(NovoRequisito(contexto, "QLT-001", "Pasta de Qualidade",
                pasta != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pasta?.ResolvedPath ?? "não localizada"));
            if (pasta == null)
            {
                findings.Add(NovoFinding(contexto, "QLT-001", "Pasta de Qualidade ausente",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "PASTA_QUALIDADE_AUSENTE"));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pasta.FolderId, false, ct);
            var candidatosExcel = conteudo.Itens
                .Where(i => (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrEmpty(i.LatestVersionUrn))
                .ToList();

            AccItemRef? candidatoComTresAbas = null;
            AuditWorkbookSnapshot? workbookSelecionado = null;

            foreach (var candidato in candidatosExcel)
            {
                await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
                var workbook = _quality.LerPastaDeQualidade(stream);
                bool temAsTres = TemAba(workbook, QualidadeAbas.BaseLancamento) && TemAba(workbook, QualidadeAbas.Rnc) && TemAba(workbook, QualidadeAbas.DesvioQualidade);
                if (temAsTres)
                {
                    candidatoComTresAbas = candidato;
                    workbookSelecionado = workbook;
                    break;
                }
            }

            requirements.Add(NovoRequisito(contexto, "QLT-002", "Candidato com as três abas esperadas",
                candidatoComTresAbas != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "BASE LANÇAMENTO + RNC + DESVIO DE QUALIDADE", candidatoComTresAbas != null ? "encontrado" : "nenhum candidato"));
            if (candidatoComTresAbas == null || workbookSelecionado == null)
            {
                findings.Add(NovoFinding(contexto, "QLT-002", "Nenhum candidato com as três abas esperadas (BASE LANÇAMENTO, RNC, DESVIO DE QUALIDADE)",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "NENHUM_CANDIDATO_COM_TRES_ABAS", pastaEncontrada: pasta.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            AuditarAba(contexto, candidatoComTresAbas.Name, pasta.ResolvedPath, workbookSelecionado, QualidadeAbas.BaseLancamento,
                QualidadeCabecalhos.BaseLancamento, QualidadeCabecalhos.BaseLancamentoObrigatorios, QualidadeCabecalhos.BaseLancamentoCamposMes, Array.Empty<string>(),
                maxEvidencia, findings, requirements);

            AuditarAba(contexto, candidatoComTresAbas.Name, pasta.ResolvedPath, workbookSelecionado, QualidadeAbas.Rnc,
                QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncObrigatorios, Array.Empty<string>(), QualidadeCabecalhos.RncCamposInteiros,
                maxEvidencia, findings, requirements);

            AuditarAba(contexto, candidatoComTresAbas.Name, pasta.ResolvedPath, workbookSelecionado, QualidadeAbas.DesvioQualidade,
                QualidadeCabecalhos.DesvioQualidade, QualidadeCabecalhos.DesvioQualidadeObrigatorios, Array.Empty<string>(), QualidadeCabecalhos.DesvioQualidadeCamposInteiros,
                maxEvidencia, findings, requirements);

            // QLT-007 — evidência de incompatibilidade de schema (SELECT-only contra INFORMATION_SCHEMA).
            await AuditarSchemaAsync(contexto, findings, requirements, ct);

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private void AuditarAba(
            OperationalAuditContext contexto, string nomeArquivo, string pastaEncontrada, AuditWorkbookSnapshot workbook, string nomeAba,
            Dictionary<string, string[]> mapaDeAliases, string[] camposObrigatorios, string[] camposMes, string[] camposInteiros, int maxEvidencia,
            List<OperationalFinding> findings, List<OperationalRequirementResult> requirements)
        {
            var chaveAba = workbook.NomesDasAbas.FirstOrDefault(n => NormalizarNomeAba(n) == NormalizarNomeAba(nomeAba));

            requirements.Add(NovoRequisito(contexto, "QLT-003", $"Aba '{nomeAba}' presente e única",
                chaveAba != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "presente e única", chaveAba ?? "ausente"));
            if (chaveAba == null)
            {
                findings.Add(NovoFinding(contexto, "QLT-003", $"Aba obrigatória '{nomeAba}' ausente",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ABA_OBRIGATORIA_AUSENTE", pastaEncontrada: pastaEncontrada, nomeArquivo: nomeArquivo, abaEncontrada: nomeAba));
                return;
            }

            int duplicatas = workbook.NomesDasAbas.Count(n => NormalizarNomeAba(n) == NormalizarNomeAba(nomeAba));
            if (duplicatas > 1)
            {
                findings.Add(NovoFinding(contexto, "QLT-003", $"Aba '{nomeAba}' duplicada ({duplicatas} ocorrências)",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, "ABA_DUPLICADA", pastaEncontrada: pastaEncontrada, nomeArquivo: nomeArquivo, abaEncontrada: nomeAba));
            }

            var aba = workbook.Abas[chaveAba];

            // QLT-004 — campo obrigatório vazio em linha real, CONSOLIDADO por Aba+Campo.
            foreach (var campo in camposObrigatorios)
            {
                var linhasComCampoVazio = new List<int>();
                foreach (var (numeroLinha, valores) in aba.Linhas)
                {
                    string? valor = ObterValorPorCampoLogico(valores, mapaDeAliases, campo);
                    if (string.IsNullOrWhiteSpace(valor)) linhasComCampoVazio.Add(numeroLinha);
                }

                requirements.Add(NovoRequisito(contexto, "QLT-004", $"{nomeAba}.{campo} preenchido em todas as linhas reais",
                    linhasComCampoVazio.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    "0 linhas vazias", linhasComCampoVazio.Count.ToString()));

                if (linhasComCampoVazio.Count > 0)
                {
                    var evidenciaLinhas = linhasComCampoVazio.Take(maxEvidencia).ToList();
                    findings.Add(new OperationalFinding
                    {
                        ExecutionId = contexto.ExecutionId,
                        DataHoraUtc = DateTime.UtcNow,
                        ProjectIdAcc = contexto.ProjectIdAcc,
                        Etapa = "17",
                        RegraId = "QLT-004",
                        CodigoProblema = "QLT-004",
                        Titulo = $"Campo obrigatório '{campo}' vazio em {linhasComCampoVazio.Count} linha(s) reais de '{nomeAba}' — a automação operacional preencheria com \"{QualidadeCabecalhos.ValorCampoObrigatorioAusente}\"",
                        ErrorDomain = OperationalAuditErrorDomain.Operacional,
                        Classificacao = OperationalAuditClassification.DadoAusente,
                        Severidade = OperationalAuditSeverity.Medio,
                        BloqueiaEtapa = false,
                        AutomacaoPodeCorrigir = false,
                        MotivoNormalizado = $"CAMPO_OBRIGATORIO_VAZIO_{campo.ToUpperInvariant()}",
                        PastaEncontrada = pastaEncontrada,
                        NomeArquivo = nomeArquivo,
                        AbaEncontrada = nomeAba,
                        Campo = campo,
                        QuantidadeOcorrencias = linhasComCampoVazio.Count,
                        Evidencia = $"Linhas: {string.Join(", ", evidenciaLinhas)}" + (linhasComCampoVazio.Count > maxEvidencia ? $" (+{linhasComCampoVazio.Count - maxEvidencia} não listadas)" : string.Empty)
                    });
                }
            }

            // QLT-005 — mês inválido (inclusive ",MAIO").
            foreach (var campoMes in camposMes)
            {
                int mesesInvalidos = 0;
                var exemplos = new List<int>();
                foreach (var (numeroLinha, valores) in aba.Linhas)
                {
                    string? valor = ObterValorPorCampoLogico(valores, mapaDeAliases, campoMes);
                    if (string.IsNullOrWhiteSpace(valor)) continue;

                    QualidadeParsers.TentarParseMes(valor, out _, out bool naoReconhecido);
                    if (naoReconhecido)
                    {
                        mesesInvalidos++;
                        if (exemplos.Count < maxEvidencia) exemplos.Add(numeroLinha);
                    }
                }

                requirements.Add(NovoRequisito(contexto, "QLT-005", $"{nomeAba}.{campoMes} sempre reconhecível como mês válido",
                    mesesInvalidos == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    "0 inválidos", mesesInvalidos.ToString()));
                if (mesesInvalidos > 0)
                {
                    findings.Add(NovoFinding(contexto, "QLT-005", $"{mesesInvalidos} valor(es) de mês não reconhecido(s) em '{nomeAba}.{campoMes}' (ex.: linhas {string.Join(", ", exemplos)})",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "MES_INVALIDO", pastaEncontrada: pastaEncontrada, nomeArquivo: nomeArquivo, abaEncontrada: nomeAba, campo: campoMes));
                }
            }

            // QLT-006 — linha rejeitável por erro de inteiro (campos inteiros conhecidos).
            int linhasRejeitaveis = 0;
            foreach (var campoInteiro in camposInteiros)
            {
                foreach (var (numeroLinha, valores) in aba.Linhas)
                {
                    string? valor = ObterValorPorCampoLogico(valores, mapaDeAliases, campoInteiro);
                    if (string.IsNullOrWhiteSpace(valor)) continue;
                    if (!int.TryParse(valor.Trim(), out _))
                    {
                        linhasRejeitaveis++;
                        findings.Add(NovoFinding(contexto, "QLT-006", $"Linha {numeroLinha} de '{nomeAba}' rejeitável — campo inteiro '{campoInteiro}' com valor não numérico ('{valor}')",
                            OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "LINHA_REJEITAVEL_ERRO_INTEIRO", pastaEncontrada: pastaEncontrada, nomeArquivo: nomeArquivo,
                            abaEncontrada: nomeAba, campo: campoInteiro, linha: numeroLinha, valorEncontrado: valor));
                    }
                }
            }
            requirements.Add(NovoRequisito(contexto, "QLT-006", $"{nomeAba}: nenhuma linha rejeitável por erro de inteiro",
                linhasRejeitaveis == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0", linhasRejeitaveis.ToString()));
        }

        /// <summary>
        /// Resolve o valor de um campo LÓGICO (ex.: "CodigoNA") procurando, na linha, uma coluna cujo texto
        /// de cabeçalho bata com QUALQUER alias real cadastrado em <see cref="QualidadeCabecalhos"/> para
        /// aquele campo (ex.: "CÓDIGO NA", "CÓDIGO N&amp;A", "N&amp;A"...) — nunca compara o nome lógico
        /// diretamente contra o texto do cabeçalho, porque eles quase nunca são a mesma string.
        /// </summary>
        private static string? ObterValorPorCampoLogico(IReadOnlyDictionary<string, string?> valores, Dictionary<string, string[]> mapaDeAliases, string campoLogico)
        {
            if (!mapaDeAliases.TryGetValue(campoLogico, out var aliases)) return null;

            foreach (var alias in aliases)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(k.Trim(), alias, StringComparison.OrdinalIgnoreCase));
                if (chave != null) return valores[chave];
            }
            return null;
        }

        private async Task AuditarSchemaAsync(OperationalAuditContext contexto, List<OperationalFinding> findings, List<OperationalRequirementResult> requirements, System.Threading.CancellationToken ct)
        {
            var tabelasEsperadas = new[] { "NCR BASE LANCAMENTO", "NCR NONCONFORMITY REPORT", "DESVIO DE QUALIDADE" };
            int tabelasComProblemaDeSchema = 0;

            foreach (var tabela in tabelasEsperadas)
            {
                var parametros = new Dictionary<string, object?> { ["@TableName"] = tabela };
                DataTable colunas = await _database.ConsultarAsync(
                    "SELECT COUNT(*) AS Quantidade FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName", parametros, ct);

                int quantidadeColunas = colunas.Rows.Count > 0 ? Convert.ToInt32(colunas.Rows[0]["Quantidade"]) : 0;
                if (quantidadeColunas == 0)
                {
                    tabelasComProblemaDeSchema++;
                    findings.Add(NovoFinding(contexto, "QLT-007", $"Tabela '{tabela}' não encontrada no schema — possível evidência de incompatibilidade de schema ou rollback anterior",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.BancoDados, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: false, "TABELA_QUALIDADE_AUSENTE_NO_SCHEMA"));
                }
            }

            requirements.Add(NovoRequisito(contexto, "QLT-007", "As três tabelas de Qualidade existem no schema",
                tabelasComProblemaDeSchema == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "3 tabelas presentes", $"{3 - tabelasComProblemaDeSchema} de 3"));
        }

        private static bool TemAba(AuditWorkbookSnapshot workbook, string nomeEsperado) =>
            workbook.NomesDasAbas.Any(n => NormalizarNomeAba(n) == NormalizarNomeAba(nomeEsperado));

        private static string NormalizarNomeAba(string valor) => valor.Trim().ToUpperInvariant();

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "17", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? pastaEncontrada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            string? campo = null, int? linha = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "17", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada, NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada,
                Campo = campo, Linha = linha, ValorEncontrado = valorEncontrado
            };
    }
}
