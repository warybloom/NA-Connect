using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 1 — Timesheet (Checkpoint 3). Fonte de referência (nunca chamada):
    /// <c>Services\TimesheetDumpService.cs</c>, método <c>ResetAndDumpTimesheetAsync</c>. Reproduz somente as
    /// LEITURAS necessárias: baixa a mesma LDP para extrair os filtros reais (célula X8/Y8 = Projeto/doc1,
    /// exatamente como o método real faz), consulta <c>tbTimeSheetRealizadoDocumento</c> por SELECT via
    /// <see cref="IAuditDatabaseReadOnlyAdapter"/>, e confere a aba "Custo Real" na mesma LDP. Nunca escreve
    /// TIMESHEET, nunca sobe arquivo, nunca chama ResetAndDumpTimesheetAsync.
    /// </summary>
    public sealed class Stage01TimesheetAuditRule : IOperationalAuditRule
    {
        public string Etapa => "1";
        public string NomeEtapa => "Timesheet";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private const string TabelaTimesheet = "tbTimeSheetRealizadoDocumento";

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IAuditDatabaseReadOnlyAdapter _database;

        public Stage01TimesheetAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook, IAuditDatabaseReadOnlyAdapter database)
        {
            _acc = acc;
            _workbook = workbook;
            _database = database;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            requirements.Add(NovoRequisito(contexto, "TS-000", "Pasta 09.14 (Gestão)",
                pastaGestao != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Inconclusive,
                "pasta localizada", pastaGestao?.ResolvedPath ?? "não localizada"));
            if (pastaGestao == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
            var candidatoLdp = conteudoGestao.Itens.FirstOrDefault(i =>
                (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(i.LatestVersionUrn));
            if (candidatoLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };

            await using var streamLdp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdp.LatestVersionUrn!, ct);
            // linhaDeCabecalhoPadrao=0: leitura POSICIONAL crua ("Col24"/"Col25"), mesma técnica de
            // ResetAndDumpTimesheetAsync (wsLdp.Cells[8,24]/[8,25]) — nunca um cabeçalho nomeado, porque o
            // filtro real não depende de nenhum nome de coluna.
            var workbook = _workbook.AbrirSomenteLeitura(streamLdp, linhaDeCabecalhoPadrao: 0);
            var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase)) ?? workbook.NomesDasAbas.FirstOrDefault();

            // TS-006 — aba "Custo Real" necessária pela fórmula de CUSTO da TIMESHEET (ResetAndDumpTimesheetAsync
            // linha ~112: "=C{row}*(SUMIF('Custo Real'!C:C, ...))") — ausência nunca lança lá, só corrompe
            // silenciosamente a fórmula; aqui reportamos isso explicitamente.
            bool temAbaCustoReal = workbook.NomesDasAbas.Any(n => string.Equals(n.Trim(), "Custo Real", StringComparison.OrdinalIgnoreCase));
            requirements.Add(NovoRequisito(contexto, "TS-006", "Aba 'Custo Real' presente",
                temAbaCustoReal ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "aba presente", temAbaCustoReal ? "presente" : "ausente"));
            if (!temAbaCustoReal)
            {
                findings.Add(NovoFinding(contexto, "TS-006", "Aba 'Custo Real' ausente — a fórmula de CUSTO escrita pela Etapa 1 ficará como #REF! para todas as linhas",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "ABA_CUSTO_REAL_AUSENTE", nomeArquivo: candidatoLdp.Name));
            }

            if (nomeAbaLdp == null) return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            var abaLdp = workbook.Abas[nomeAbaLdp];

            string? filtroCR = abaLdp.Linhas.TryGetValue(8, out var linha8) && linha8.TryGetValue("Col24", out var vCr) ? vCr?.Trim() : null;
            string? filtroCROS = linha8 != null && linha8.TryGetValue("Col25", out var vCros) ? vCros?.Trim() : null;

            // TS-002 — filtros vazios: a query real ainda executaria (Projeto='' AND doc1='') e devolveria
            // silenciosamente zero linhas, sem nenhum sinal distinto de "não configurado" vs. "sem dados".
            bool filtrosVazios = string.IsNullOrWhiteSpace(filtroCR) && string.IsNullOrWhiteSpace(filtroCROS);
            requirements.Add(NovoRequisito(contexto, "TS-002", "Filtros de Projeto/doc1 preenchidos",
                !filtrosVazios ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "preenchidos", filtrosVazios ? "ambos vazios" : "preenchidos"));
            if (filtrosVazios)
            {
                findings.Add(NovoFinding(contexto, "TS-002", "Filtros de Projeto (X8) e doc1 (Y8) estão vazios na LDP — a consulta real produziria zero registros sem nenhuma sinalização",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "FILTROS_TIMESHEET_VAZIOS", nomeArquivo: candidatoLdp.Name, abaEncontrada: nomeAbaLdp));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // TS-001 — leitura de tbTimeSheetRealizadoDocumento (SELECT-only, mesmos dois filtros do método real).
            DataTable tabela;
            try
            {
                var parametros = new Dictionary<string, object?> { ["@Projeto"] = filtroCR, ["@Doc1"] = filtroCROS };
                tabela = await _database.ConsultarAsync(
                    $"SELECT * FROM [{TabelaTimesheet}] WHERE Projeto = @Projeto AND doc1 = @Doc1", parametros, ct);
            }
            catch (Exception ex)
            {
                requirements.Add(NovoRequisito(contexto, "TS-001", "tbTimeSheetRealizadoDocumento acessível",
                    OperationalRequirementStatus.Failed, "consulta bem-sucedida", "falha na consulta"));
                // Sem prova concreta de bug de código (a consulta é idêntica à real) — nunca "chuta" AUTOMACAO
                // a partir de uma falha de leitura isolada; timeout/schema indisponível é ambiente/infra.
                findings.Add(NovoFinding(contexto, "TS-001", $"{TabelaTimesheet} inacessível: {ex.Message}",
                    OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.BancoDados, OperationalAuditSeverity.Critico,
                    bloqueiaEtapa: true, "TIMESHEET_TABELA_INACESSIVEL", nomeArquivo: candidatoLdp.Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }
            requirements.Add(NovoRequisito(contexto, "TS-001", "tbTimeSheetRealizadoDocumento acessível",
                OperationalRequirementStatus.Approved, "consulta bem-sucedida", $"{tabela.Rows.Count} linha(s)"));

            // TS-003 — campos operacionalmente obrigatórios nulos/vazios, consolidados por Projeto+Campo+Motivo.
            var camposObrigatorios = new[] { "DocumentoCodigo", "Nome", "Disciplina" };
            var consolidadoInvalidos = new Dictionary<(string Projeto, string Campo), List<string>>();
            int maxEvidencia = Math.Max(1, contexto.Options.MaxEvidencePerFinding);

            // TS-005 — duplicidade lógica: como a leitura real NÃO tem nenhuma chave de deduplicação (seção
            // 5 da correção — confirmado por pesquisa no código-fonte), a única duplicidade que podemos
            // afirmar sem inventar uma chave de negócio é a linha FISICAMENTE idêntica em todas as colunas.
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicatasEncontradas = new List<string>();

            foreach (DataRow linha in tabela.Rows)
            {
                string projeto = TextoOuVazio(linha, "Projeto");
                foreach (var campo in camposObrigatorios)
                {
                    if (string.IsNullOrWhiteSpace(TextoOuVazio(linha, campo)))
                    {
                        var chave = (projeto, campo);
                        if (!consolidadoInvalidos.TryGetValue(chave, out var evidencias))
                        {
                            evidencias = new List<string>();
                            consolidadoInvalidos[chave] = evidencias;
                        }
                        if (evidencias.Count < maxEvidencia)
                        {
                            evidencias.Add($"Documento={TextoOuVazio(linha, "Documento")}, Data={TextoOuVazio(linha, "Data")}");
                        }
                    }
                }

                string assinatura = string.Join("|", tabela.Columns.Cast<DataColumn>().Select(c => linha[c]?.ToString() ?? string.Empty));
                if (!vistos.Add(assinatura))
                {
                    duplicatasEncontradas.Add($"Documento={TextoOuVazio(linha, "Documento")}, Nome={TextoOuVazio(linha, "Nome")}, Data={TextoOuVazio(linha, "Data")}");
                }
            }

            requirements.Add(NovoRequisito(contexto, "TS-003", "Campos obrigatórios preenchidos (DocumentoCodigo/Nome/Disciplina)",
                consolidadoInvalidos.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 grupos com campo ausente", consolidadoInvalidos.Count.ToString()));
            foreach (var ((projeto, campo), evidencias) in consolidadoInvalidos)
            {
                findings.Add(NovoFinding(contexto, "TS-003", $"Campo obrigatório '{campo}' nulo/vazio em registro(s) do projeto '{projeto}'",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "CAMPO_OBRIGATORIO_TIMESHEET_AUSENTE", nomeArquivo: candidatoLdp.Name, campo: campo,
                    evidencia: string.Join(" | ", evidencias)));
            }

            requirements.Add(NovoRequisito(contexto, "TS-005", "Nenhum registro fisicamente duplicado",
                duplicatasEncontradas.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 duplicatas", duplicatasEncontradas.Count.ToString()));
            if (duplicatasEncontradas.Count > 0)
            {
                findings.Add(NovoFinding(contexto, "TS-005", $"{duplicatasEncontradas.Count} registro(s) fisicamente duplicado(s) (todas as colunas idênticas) em {TabelaTimesheet}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoDuplicado, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "REGISTRO_TIMESHEET_DUPLICADO", nomeArquivo: candidatoLdp.Name,
                    evidencia: string.Join(" | ", duplicatasEncontradas.Take(maxEvidencia))));
            }

            // TS-004 — precisão de Planejadas/Trabalhadas: origem numeric(38,15) -> decimal? .NET -> destino
            // Excel (double IEEE754, ~15-17 dígitos significativos via EPPlus .Value). Só ARRISCADO quando o
            // valor tem mais dígitos significativos do que o destino suporta com segurança; só CONCRETO
            // quando a própria conversão numérica falha.
            int riscosPrecisao = 0, valoresImpossiveis = 0;
            foreach (DataRow linha in tabela.Rows)
            {
                foreach (var campo in new[] { "Planejadas", "Trabalhadas" })
                {
                    if (!tabela.Columns.Contains(campo) || linha[campo] == DBNull.Value) continue;
                    string bruto = linha[campo]!.ToString() ?? string.Empty;

                    if (!decimal.TryParse(bruto, out decimal valor))
                    {
                        valoresImpossiveis++;
                        findings.Add(NovoFinding(contexto, "TS-004", $"Valor de '{campo}'='{bruto}' não pôde ser representado como decimal — impossível de sincronizar com segurança",
                            OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Medio,
                            bloqueiaEtapa: false, "VALOR_HORAS_IMPOSSIVEL", nomeArquivo: candidatoLdp.Name, campo: campo, valorEncontrado: bruto));
                        continue;
                    }

                    int digitosSignificativos = valor.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
                        .Replace("-", "").Replace(".", "").TrimStart('0').Length;
                    if (digitosSignificativos > 15)
                    {
                        riscosPrecisao++;
                        findings.Add(NovoFinding(contexto, "TS-004", $"Valor de '{campo}'={bruto} tem {digitosSignificativos} dígitos significativos — risco de truncamento ao converter para double no Excel (destino real da TIMESHEET)",
                            OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Baixo,
                            bloqueiaEtapa: false, "RISCO_PRECISAO_HORAS", nomeArquivo: candidatoLdp.Name, campo: campo, valorEncontrado: bruto));
                    }
                }
            }
            requirements.Add(NovoRequisito(contexto, "TS-004", "Planejadas/Trabalhadas representáveis sem risco de precisão",
                (riscosPrecisao == 0 && valoresImpossiveis == 0) ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 riscos/valores impossíveis", $"{riscosPrecisao} risco(s), {valoresImpossiveis} impossível(is)"));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static string TextoOuVazio(DataRow linha, string coluna) =>
            linha.Table.Columns.Contains(coluna) && linha[coluna] != DBNull.Value ? linha[coluna]!.ToString() ?? string.Empty : string.Empty;

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "1", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? abaEncontrada = null, string? campo = null,
            string? valorEncontrado = null, string? evidencia = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "1", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, AbaEncontrada = abaEncontrada, Campo = campo, ValorEncontrado = valorEncontrado, Evidencia = evidencia
            };
    }
}
