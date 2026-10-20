using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 0 — DNA do Projeto (seção 7 do Checkpoint 2). Nunca corrige NamingStandard, nunca atualiza
    /// Projetos/Metadados_Projetos (só SELECT via <see cref="IAuditDatabaseReadOnlyAdapter"/>, guardado por
    /// <see cref="Safety.SqlWriteVerbGuard"/>), nunca assume que o primeiro Excel encontrado é a LDP correta.
    /// </summary>
    public sealed class Stage00ProjectDnaAuditRule : IOperationalAuditRule
    {
        public string Etapa => "0";
        public string NomeEtapa => "DNA do Projeto";

        private static readonly string[] AliasesPastaGestao =
        {
            "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO",
            "04.14 - MANAGEMENT", "10.14 - MANAGEMENT"
        };

        private readonly IAuditDatabaseReadOnlyAdapter _database;
        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;

        public Stage00ProjectDnaAuditRule(IAuditDatabaseReadOnlyAdapter database, IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook)
        {
            _database = database;
            _acc = acc;
            _workbook = workbook;
        }

        public async Task<OperationalRuleExecutionResult> ExecutarAsync(OperationalAuditContext contexto)
        {
            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            var ct = contexto.CancellationToken;

            // DNA-001 — identificação ACC completa.
            var linhaProjeto = await ObterLinhaDoProjetoAsync(contexto.ProjectIdAcc, ct);
            bool identificacaoCompleta = linhaProjeto != null
                && !string.IsNullOrWhiteSpace(Texto(linhaProjeto, "Project_ID_ACC"))
                && !string.IsNullOrWhiteSpace(Texto(linhaProjeto, "Account_ID_ACC"));

            requirements.Add(NovoRequisito(contexto, "DNA-001", contexto.ProjectIdAcc,
                identificacaoCompleta ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "Project_ID_ACC e Account_ID_ACC preenchidos", identificacaoCompleta ? "preenchidos" : "ausente(s)"));

            if (!identificacaoCompleta)
            {
                findings.Add(NovoFinding(contexto, "DNA-001", "Projeto sem Project_ID_ACC ou Account_ID_ACC",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, motivoNormalizado: "IDENTIFICACAO_ACC_INCOMPLETA"));
            }

            // DNA-005 — NamingStandard fixado como CLIENT por código de teste (AUT-CODE-001 da Fase 1).
            string? namingStandard = Texto(linhaProjeto, "NamingStandard");
            bool namingStandardSuspeito = string.Equals(namingStandard, "CLIENT", StringComparison.OrdinalIgnoreCase);
            requirements.Add(NovoRequisito(contexto, "DNA-005", contexto.ProjectIdAcc,
                namingStandardSuspeito ? OperationalRequirementStatus.Failed : OperationalRequirementStatus.Approved,
                "NamingStandard não fixado como CLIENT", namingStandard ?? "(ausente)"));
            if (namingStandardSuspeito)
            {
                findings.Add(NovoFinding(contexto, "DNA-005", "NamingStandard fixado como CLIENT — risco de ter sido travado por código de teste (AUT-CODE-001)",
                    OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.RiscoTecnico, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: false, motivoNormalizado: "NAMING_STANDARD_CLIENT_SUSPEITO"));
            }

            // DNA-002 — pasta de Gestão 09.14.
            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, navegarRecursivamente: false, ct);
            requirements.Add(NovoRequisito(contexto, "DNA-002", "Pasta de Gestão 09.14",
                pastaGestao != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "pasta localizada", pastaGestao?.ResolvedPath ?? "não localizada"));

            if (pastaGestao == null)
            {
                findings.Add(NovoFinding(contexto, "DNA-002", "Pasta de Gestão 09.14 não localizada nos aliases permitidos",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, motivoNormalizado: "PASTA_GESTAO_NAO_LOCALIZADA",
                    pastaEsperada: string.Join(" | ", AliasesPastaGestao)));

                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // DNA-003 — Excel candidato à LDP.
            var conteudo = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, recursivo: false, ct);
            var candidatosExcel = conteudo.Itens
                .Where(i => string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                .Where(i => !string.IsNullOrEmpty(i.LatestVersionUrn))
                .ToList();

            requirements.Add(NovoRequisito(contexto, "DNA-003", "Excel candidato à LDP na pasta de Gestão",
                candidatosExcel.Count > 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "ao menos 1 candidato", candidatosExcel.Count.ToString()));

            if (candidatosExcel.Count == 0)
            {
                findings.Add(NovoFinding(contexto, "DNA-003", "Nenhum Excel candidato à LDP na pasta de Gestão",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DocumentoLdpSemAcc, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, motivoNormalizado: "NENHUM_EXCEL_CANDIDATO", pastaEncontrada: pastaGestao.ResolvedPath));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // Nunca assume que o PRIMEIRO Excel é a LDP correta — examina todos os candidatos até achar
            // um com uma aba "LDP"; se nenhum tiver, DNA-004 aponta o primeiro como evidência.
            AccItemRef? candidatoComLdp = null;
            AuditWorksheetSnapshot? abaLdp = null;

            foreach (var candidato in candidatosExcel)
            {
                await using var stream = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidato.LatestVersionUrn!, ct);
                var workbook = _workbook.AbrirSomenteLeitura(stream);
                var nomeAbaLdp = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase));
                if (nomeAbaLdp != null)
                {
                    candidatoComLdp = candidato;
                    abaLdp = workbook.Abas[nomeAbaLdp];
                    break;
                }
            }

            requirements.Add(NovoRequisito(contexto, "DNA-004", "Aba LDP",
                abaLdp != null ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "aba com \"LDP\" no nome", abaLdp != null ? abaLdp.Nome : "não encontrada"));

            if (abaLdp == null)
            {
                findings.Add(NovoFinding(contexto, "DNA-004", "Aba LDP ausente em todos os Excel candidatos da pasta de Gestão",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, motivoNormalizado: "ABA_LDP_AUSENTE",
                    pastaEncontrada: pastaGestao.ResolvedPath, nomeArquivo: candidatosExcel.First().Name));
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // DNA-008 — coluna-chave configurada existe no cabeçalho real.
            string colunaChaveConfigurada = contexto.Options.OutputDirectory != null ? ObterColunaChaveConfigurada(contexto) : string.Empty;
            bool colunaChaveExiste = string.IsNullOrEmpty(colunaChaveConfigurada) ||
                abaLdp.Cabecalhos.Any(c => string.Equals(NormalizarCabecalho(c), NormalizarCabecalho(colunaChaveConfigurada), StringComparison.Ordinal));

            if (!string.IsNullOrEmpty(colunaChaveConfigurada))
            {
                requirements.Add(NovoRequisito(contexto, "DNA-008", "Coluna-chave configurada existe no cabeçalho real",
                    colunaChaveExiste ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                    colunaChaveConfigurada, string.Join(", ", abaLdp.Cabecalhos)));

                if (!colunaChaveExiste)
                {
                    findings.Add(NovoFinding(contexto, "DNA-008", $"Coluna-chave configurada ('{colunaChaveConfigurada}') não existe no cabeçalho real da LDP",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.ConfiguracaoProjeto, OperationalAuditSeverity.Alto,
                        bloqueiaEtapa: true, motivoNormalizado: "COLUNA_CHAVE_AUSENTE",
                        pastaEncontrada: pastaGestao.ResolvedPath, nomeArquivo: candidatoComLdp!.Name, abaEncontrada: abaLdp.Nome, campo: colunaChaveConfigurada));
                }
            }

            // DNA-006/DNA-007 — linhas reais da LDP.
            var codigosNaVistos = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            int linhasComAmbosVazios = 0;

            foreach (var (numeroLinha, valores) in abaLdp.Linhas)
            {
                string? codigoNa = ObterValorDeColunaCandidata(valores, "N&A DOCUMENT NUMBER", "N&A", "NA DOCUMENT NUMBER", "CÓDIGO N&A", "CODIGO N&A");
                string? codigoCliente = ObterValorDeColunaCandidata(valores, "CLIENT DOCUMENT NUMBER", "NÚMERO DOCUMENTO CLIENTE", "NUMERO DOCUMENTO CLIENTE", "CÓDIGO CLIENTE", "CODIGO CLIENTE");

                bool ambosVazios = string.IsNullOrWhiteSpace(codigoNa) && string.IsNullOrWhiteSpace(codigoCliente);
                if (ambosVazios)
                {
                    linhasComAmbosVazios++;
                    findings.Add(NovoFinding(contexto, "DNA-006", "Código N&A e código Cliente vazios na mesma linha real",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoAusente, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, motivoNormalizado: "CODIGOS_NA_E_CLIENTE_VAZIOS",
                        pastaEncontrada: pastaGestao.ResolvedPath, nomeArquivo: candidatoComLdp!.Name, abaEncontrada: abaLdp.Nome, linha: numeroLinha));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(codigoNa))
                {
                    if (!codigosNaVistos.TryGetValue(codigoNa, out var linhas))
                    {
                        linhas = new List<int>();
                        codigosNaVistos[codigoNa] = linhas;
                    }
                    linhas.Add(numeroLinha);
                }
            }

            requirements.Add(NovoRequisito(contexto, "DNA-006", "Nenhuma linha real com N&A e Cliente vazios",
                linhasComAmbosVazios == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0", linhasComAmbosVazios.ToString()));

            var codigosDuplicados = codigosNaVistos.Where(kv => kv.Value.Count > 1).ToList();
            requirements.Add(NovoRequisito(contexto, "DNA-007", "Nenhum código N&A repetido em múltiplas linhas",
                codigosDuplicados.Count == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0", codigosDuplicados.Count.ToString()));

            foreach (var (codigo, linhas) in codigosDuplicados)
            {
                findings.Add(NovoFinding(contexto, "DNA-007", $"Código N&A '{codigo}' repetido em {linhas.Count} linhas: {string.Join(", ", linhas)}",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoInvalido, OperationalAuditSeverity.Alto,
                    bloqueiaEtapa: true, motivoNormalizado: "CODIGO_NA_DUPLICADO",
                    pastaEncontrada: pastaGestao.ResolvedPath, nomeArquivo: candidatoComLdp!.Name, abaEncontrada: abaLdp.Nome,
                    campo: "Código N&A", valorEncontrado: codigo));
            }

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private async Task<IReadOnlyDictionary<string, string?>?> ObterLinhaDoProjetoAsync(string projectIdAcc, System.Threading.CancellationToken ct)
        {
            var parametros = new Dictionary<string, object?> { ["@ProjectIdAcc"] = projectIdAcc };
            DataTable tabela = await _database.ConsultarAsync(
                "SELECT Project_ID_ACC, Account_ID_ACC, NamingStandard FROM Projetos WHERE Project_ID_ACC = @ProjectIdAcc", parametros, ct);

            if (tabela.Rows.Count == 0) return null;

            var linha = tabela.Rows[0];
            var valores = new Dictionary<string, string?>();
            foreach (DataColumn coluna in tabela.Columns)
            {
                valores[coluna.ColumnName] = linha[coluna] == DBNull.Value ? null : linha[coluna]?.ToString();
            }
            return valores;
        }

        private static string? Texto(IReadOnlyDictionary<string, string?>? linha, string coluna) =>
            linha != null && linha.TryGetValue(coluna, out var valor) ? valor : null;

        private static string? ObterValorDeColunaCandidata(IReadOnlyDictionary<string, string?> valores, params string[] candidatos)
        {
            foreach (var candidato in candidatos)
            {
                var chave = valores.Keys.FirstOrDefault(k => string.Equals(NormalizarCabecalho(k), NormalizarCabecalho(candidato), StringComparison.Ordinal));
                if (chave != null) return valores[chave];
            }
            return null;
        }

        private static string NormalizarCabecalho(string valor) =>
            valor.Trim().ToUpperInvariant();

        /// <summary>Placeholder de leitura de configuração — o valor real (ProjectSettings:KeyColumn) é lido pelo runner/harness e pode ser injetado via extensão futura; aqui só evita quebrar quando não configurado.</summary>
        private static string ObterColunaChaveConfigurada(OperationalAuditContext contexto) => string.Empty;

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId,
                DataHoraUtc = DateTime.UtcNow,
                ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "0",
                RequirementCode = codigo,
                ObjetoAvaliado = objetoAvaliado,
                Status = status,
                ValorEsperado = esperado,
                ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado,
            string? pastaEncontrada = null, string? pastaEsperada = null, string? nomeArquivo = null, string? abaEncontrada = null,
            int? linha = null, string? campo = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId,
                DataHoraUtc = DateTime.UtcNow,
                ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "0",
                RegraId = codigoProblema,
                CodigoProblema = codigoProblema,
                Titulo = titulo,
                ErrorDomain = errorDomain,
                Classificacao = classificacao,
                Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa,
                AutomacaoPodeCorrigir = false,
                MotivoNormalizado = motivoNormalizado,
                PastaEncontrada = pastaEncontrada,
                PastaEsperada = pastaEsperada,
                NomeArquivo = nomeArquivo,
                AbaEncontrada = abaEncontrada,
                Linha = linha,
                Campo = campo,
                ValorEncontrado = valorEncontrado
            };
    }
}
