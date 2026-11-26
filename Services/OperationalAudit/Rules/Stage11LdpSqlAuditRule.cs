using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace APS.WebApp.Services.OperationalAudit.Rules
{
    /// <summary>
    /// Etapa 11 — LDP Unificada (Checkpoint 3). <c>Ldp_Unificada</c> recebe INSERTs sucessivos por desenho
    /// (histórico preservado — confirmado por pesquisa de código, <c>DatabaseService.SalvarDadosLdpUnificada</c>
    /// e comentário em <c>LdpUpdateService.cs</c>: "tabela bruta com o HISTÓRICO de todas as sincronizações").
    /// "Múltiplas linhas do mesmo documento em datas diferentes" NUNCA é, por si só, erro (seção 11 do
    /// pedido) — só é reportado quando há duplicidade real DENTRO do mesmo <c>Data_Sincronizacao</c>, ou
    /// quando a view <c>VW_Ldp_Unificada</c> (que resolve "última versão" via
    /// <c>ROW_NUMBER() PARTITION BY Project_ID_ACC, NA_Document_Number ORDER BY Data_Sincronizacao DESC,
    /// ID_Registro DESC</c>) devolve um registro que não é o mais recente. Chave lógica:
    /// (Project_ID_ACC, NA_Document_Number). Nunca escreve em <c>Ldp_Unificada</c> nem em nenhuma view —
    /// somente SELECT via <see cref="IAuditDatabaseReadOnlyAdapter"/>.
    /// </summary>
    public sealed class Stage11LdpSqlAuditRule : IOperationalAuditRule
    {
        public string Etapa => "11";
        public string NomeEtapa => "LDP Unificada";

        private static readonly string[] AliasesPastaGestao = { "09.14 - MANAGEMENT", "09.14 - GESTÃO", "09.14 - GESTAO" };
        private const int LinhaCabecalhoLdp = 7;
        private const int PrimeiraColunaPersistida = 1;
        private const int UltimaColunaPersistida = 35;
        private const int PrimeiraLinhaDados = 8;

        private readonly IAuditAccReadOnlyAdapter _acc;
        private readonly IAuditWorkbookReadOnlyAdapter _workbook;
        private readonly IAuditDatabaseReadOnlyAdapter _database;

        public Stage11LdpSqlAuditRule(IAuditAccReadOnlyAdapter acc, IAuditWorkbookReadOnlyAdapter workbook, IAuditDatabaseReadOnlyAdapter database)
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

            var parametrosProjeto = new Dictionary<string, object?> { ["@ProjectIdAcc"] = contexto.ProjectIdAcc };
            DataTable historico = await _database.ConsultarAsync(
                "SELECT ID_Registro, NA_Document_Number, Data_Sincronizacao FROM [Ldp_Unificada] WHERE Project_ID_ACC = @ProjectIdAcc", parametrosProjeto, ct);

            var pastaGestao = await _acc.LocalizarPastaPorAliasAsync(contexto.ProjectIdAcc, AliasesPastaGestao, false, ct);
            AccItemRef? candidatoLdp = null;
            AuditWorksheetSnapshot? abaLdp = null;

            if (pastaGestao != null)
            {
                var conteudoGestao = await _acc.ListarConteudoCompletoAsync(contexto.ProjectIdAcc, pastaGestao.FolderId, false, ct);
                candidatoLdp = conteudoGestao.Itens.FirstOrDefault(i =>
                    (string.Equals(i.Extension, "XLSX", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Extension, "XLSM", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(i.LatestVersionUrn));

                if (candidatoLdp != null)
                {
                    await using var streamLdp = await _acc.BaixarConteudoDaVersaoAsync(contexto.ProjectIdAcc, candidatoLdp.LatestVersionUrn!, ct);
                    var workbook = _workbook.AbrirSomenteLeitura(streamLdp, linhaDeCabecalhoPadrao: 0);
                    var nomeAba = workbook.NomesDasAbas.FirstOrDefault(n => n.Contains("LDP", StringComparison.OrdinalIgnoreCase)) ?? workbook.NomesDasAbas.FirstOrDefault();
                    if (nomeAba != null) abaLdp = workbook.Abas[nomeAba];
                }
            }

            // L11-001 — fonte indisponível: só AUTOMACAO quando há PROVA de que já existiu sincronização
            // para este projeto (linhas no histórico) e mesmo assim a fonte não pôde ser localizada agora.
            bool fonteDisponivel = abaLdp != null;
            requirements.Add(NovoRequisito(contexto, "L11-001", "Fonte LDP disponível para reconciliação",
                fonteDisponivel ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Inconclusive,
                "disponível", fonteDisponivel ? "disponível" : "indisponível"));
            if (!fonteDisponivel)
            {
                if (historico.Rows.Count > 0)
                {
                    findings.Add(NovoFinding(contexto, "L11-001", $"Ldp_Unificada já tem {historico.Rows.Count} registro(s) para este projeto, mas a fonte (LDP no ACC) não pôde ser localizada agora para reconciliação",
                        OperationalAuditErrorDomain.Automacao, OperationalAuditClassification.EstruturaAcc, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "FONTE_LDP_INDISPONIVEL_COM_HISTORICO"));
                }
                return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
            }

            // L11-002 — duplicidade histórica: 4 estados, nunca "INSERT = bug" por padrão.
            var porDocumento = historico.Rows.Cast<DataRow>()
                .Where(r => r["NA_Document_Number"] != DBNull.Value && !string.IsNullOrWhiteSpace(r["NA_Document_Number"]!.ToString()))
                .GroupBy(r => r["NA_Document_Number"]!.ToString()!, StringComparer.OrdinalIgnoreCase);

            int duplicidadesReais = 0, evidenciaInsuficiente = 0;
            foreach (var grupo in porDocumento)
            {
                var linhas = grupo.ToList();
                if (linhas.Count <= 1) continue;

                bool algumaDataNula = linhas.Any(r => r["Data_Sincronizacao"] == DBNull.Value);
                if (algumaDataNula)
                {
                    evidenciaInsuficiente++;
                    continue; // Estado B — evidência insuficiente, nunca "INSERT = bug".
                }

                var datasDistintas = linhas.Select(r => Convert.ToDateTime(r["Data_Sincronizacao"])).Distinct().Count();
                if (datasDistintas == linhas.Count) continue; // Estado A — snapshots históricos distintos, esperado.

                // Estado C — mais de uma linha do MESMO documento no MESMO Data_Sincronizacao (mesmo lote).
                duplicidadesReais++;
                findings.Add(NovoFinding(contexto, "L11-002", $"Documento '{grupo.Key}' tem {linhas.Count} linha(s) no MESMO Data_Sincronizacao em Ldp_Unificada (duplicidade dentro do mesmo lote, não histórico legítimo)",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.DadoDuplicado, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "LDP_UNIFICADA_DUPLICIDADE_NO_MESMO_LOTE", documentoNa: grupo.Key));
            }
            requirements.Add(NovoRequisito(contexto, "L11-002", "Nenhuma duplicidade real (mesmo lote) em Ldp_Unificada",
                duplicidadesReais == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 duplicidades reais", $"{duplicidadesReais} real(is), {evidenciaInsuficiente} com evidência insuficiente"));

            // L11-003 — dados além do range persistido (colunas 36+ nas linhas de dados).
            int linhasComDadosForaDoRange = 0;
            int ultimaLinhaLdp = abaLdp!.Linhas.Count > 0 ? abaLdp.Linhas.Keys.Max() : PrimeiraLinhaDados - 1;
            for (int r = PrimeiraLinhaDados; r <= ultimaLinhaLdp; r++)
            {
                if (!abaLdp.Linhas.TryGetValue(r, out var valores)) continue;
                bool temDadoForaDoRange = valores.Any(kv => kv.Key.StartsWith("Col", StringComparison.Ordinal)
                    && int.TryParse(kv.Key.Substring(3), out int col) && col > UltimaColunaPersistida
                    && !string.IsNullOrWhiteSpace(kv.Value));
                if (temDadoForaDoRange) linhasComDadosForaDoRange++;
            }
            requirements.Add(NovoRequisito(contexto, "L11-003", "Nenhuma linha física com dado além do range persistido (colunas 1-35)",
                linhasComDadosForaDoRange == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 linhas", linhasComDadosForaDoRange.ToString()));
            if (linhasComDadosForaDoRange > 0)
            {
                findings.Add(NovoFinding(contexto, "L11-003", $"{linhasComDadosForaDoRange} linha(s) da LDP têm dado preenchido além da coluna 35 (fora do que é persistido em Ldp_Unificada)",
                    OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.EstruturaPlanilha, OperationalAuditSeverity.Baixo,
                    bloqueiaEtapa: false, "DADO_ALEM_DO_RANGE_PERSISTIDO", nomeArquivo: candidatoLdp!.Name));
            }

            // L11-004 — elegibilidade real (Col4 OU Col2 preenchida — mesma condição do worker real).
            int elegiveisNaFonte = 0;
            for (int r = PrimeiraLinhaDados; r <= ultimaLinhaLdp; r++)
            {
                if (!abaLdp.Linhas.TryGetValue(r, out var valores)) continue;
                bool col4 = valores.TryGetValue("Col4", out var v4) && !string.IsNullOrWhiteSpace(v4);
                bool col2 = valores.TryGetValue("Col2", out var v2) && !string.IsNullOrWhiteSpace(v2);
                if (col4 || col2) elegiveisNaFonte++;
            }

            int elegiveisPersistidos = 0;
            if (historico.Rows.Count > 0)
            {
                var maxData = historico.Rows.Cast<DataRow>().Where(r => r["Data_Sincronizacao"] != DBNull.Value).Select(r => Convert.ToDateTime(r["Data_Sincronizacao"])).DefaultIfEmpty().Max();
                elegiveisPersistidos = historico.Rows.Cast<DataRow>().Count(r => r["Data_Sincronizacao"] != DBNull.Value && Convert.ToDateTime(r["Data_Sincronizacao"]) == maxData);
            }

            bool divergeElegibilidade = historico.Rows.Count > 0 && elegiveisNaFonte != elegiveisPersistidos;
            requirements.Add(NovoRequisito(contexto, "L11-004", "Linhas elegíveis na fonte == linhas do último lote persistido",
                !divergeElegibilidade ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Inconclusive,
                $"{elegiveisNaFonte} elegíveis na fonte", $"{elegiveisPersistidos} no último lote"));
            if (divergeElegibilidade)
            {
                findings.Add(NovoFinding(contexto, "L11-004", $"Quantidade de linhas elegíveis na LDP atual ({elegiveisNaFonte}) diverge do último lote persistido em Ldp_Unificada ({elegiveisPersistidos}) — pode refletir edição da LDP após a última sincronização, não necessariamente um erro",
                    OperationalAuditErrorDomain.Inconclusivo, OperationalAuditClassification.BancoDados, OperationalAuditSeverity.Medio,
                    bloqueiaEtapa: false, "DIVERGENCIA_ELEGIBILIDADE_LDP_UNIFICADA", nomeArquivo: candidatoLdp!.Name,
                    valorEsperado: elegiveisNaFonte.ToString(), valorEncontrado: elegiveisPersistidos.ToString()));
            }

            // L11-005 — VW_Ldp_Unificada deve devolver, por chave, a linha de MAIOR Data_Sincronizacao.
            DataTable view = await _database.ConsultarAsync(
                "SELECT NA_Document_Number, Data_Sincronizacao FROM [VW_Ldp_Unificada] WHERE Project_ID_ACC = @ProjectIdAcc", parametrosProjeto, ct);

            var maxPorDocumentoNaTabela = historico.Rows.Cast<DataRow>()
                .Where(r => r["NA_Document_Number"] != DBNull.Value && r["Data_Sincronizacao"] != DBNull.Value)
                .GroupBy(r => r["NA_Document_Number"]!.ToString()!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(r => Convert.ToDateTime(r["Data_Sincronizacao"])), StringComparer.OrdinalIgnoreCase);

            int viewDesatualizada = 0;
            foreach (DataRow linhaView in view.Rows)
            {
                if (linhaView["NA_Document_Number"] == DBNull.Value || linhaView["Data_Sincronizacao"] == DBNull.Value) continue;
                string doc = linhaView["NA_Document_Number"]!.ToString()!;
                DateTime dataView = Convert.ToDateTime(linhaView["Data_Sincronizacao"]);

                if (maxPorDocumentoNaTabela.TryGetValue(doc, out var dataMaximaReal) && dataView < dataMaximaReal)
                {
                    viewDesatualizada++;
                    findings.Add(NovoFinding(contexto, "L11-005", $"VW_Ldp_Unificada devolve '{doc}' com Data_Sincronizacao={dataView:O}, mas existe registro mais recente ({dataMaximaReal:O}) na tabela",
                        OperationalAuditErrorDomain.Operacional, OperationalAuditClassification.BancoDados, OperationalAuditSeverity.Medio,
                        bloqueiaEtapa: false, "VIEW_LDP_UNIFICADA_DESATUALIZADA", documentoNa: doc,
                        valorEsperado: dataMaximaReal.ToString("O"), valorEncontrado: dataView.ToString("O")));
                }
            }
            requirements.Add(NovoRequisito(contexto, "L11-005", "VW_Ldp_Unificada sempre retorna a última Data_Sincronizacao por documento",
                viewDesatualizada == 0 ? OperationalRequirementStatus.Approved : OperationalRequirementStatus.Failed,
                "0 desatualizados", viewDesatualizada.ToString()));

            return new OperationalRuleExecutionResult { Findings = findings, Requirements = requirements };
        }

        private static OperationalRequirementResult NovoRequisito(
            OperationalAuditContext contexto, string codigo, string objetoAvaliado, OperationalRequirementStatus status, string esperado, string encontrado) =>
            new OperationalRequirementResult
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "11", RequirementCode = codigo, ObjetoAvaliado = objetoAvaliado, Status = status, ValorEsperado = esperado, ValorEncontrado = encontrado
            };

        private static OperationalFinding NovoFinding(
            OperationalAuditContext contexto, string codigoProblema, string titulo,
            OperationalAuditErrorDomain errorDomain, OperationalAuditClassification classificacao, OperationalAuditSeverity severidade,
            bool bloqueiaEtapa, string motivoNormalizado, string? nomeArquivo = null, string? documentoNa = null,
            string? valorEsperado = null, string? valorEncontrado = null) =>
            new OperationalFinding
            {
                ExecutionId = contexto.ExecutionId, DataHoraUtc = DateTime.UtcNow, ProjectIdAcc = contexto.ProjectIdAcc,
                Etapa = "11", RegraId = codigoProblema, CodigoProblema = codigoProblema, Titulo = titulo,
                ErrorDomain = errorDomain, Classificacao = classificacao, Severidade = severidade,
                BloqueiaEtapa = bloqueiaEtapa, AutomacaoPodeCorrigir = false, MotivoNormalizado = motivoNormalizado,
                NomeArquivo = nomeArquivo, DocumentoNa = documentoNa, ValorEsperado = valorEsperado, ValorEncontrado = valorEncontrado
            };
    }
}
