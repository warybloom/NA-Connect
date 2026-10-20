using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Contrato SQL de <c>AuditRepository.cs</c> — lê o TEXTO-FONTE real do arquivo (não reflexão sobre
    /// tipos, não execução real de SQL) e verifica que todo comando embutido nele só escreve/lê nas 7
    /// tabelas do schema <c>audit</c>, nunca em nenhuma tabela operacional. Isto é deliberadamente mais
    /// forte que um teste de reflexão: um comando SQL é só uma string em tempo de compilação, então
    /// reflexão sobre tipos referenciados nunca o alcançaria — só inspecionar o texto do comando alcança.
    /// </summary>
    public class AuditRepositorySqlContractTests
    {
        private static readonly string[] TabelasPermitidas =
        {
            "audit.AuditRun",
            "audit.AuditProjectRun",
            "audit.AuditStageResult",
            "audit.AuditIssue",
            "audit.AuditOccurrence",
            "audit.AuditRequirementCatalog",
            "audit.AuditRequirementResult"
        };

        private static readonly string[] IdentificadoresOperacionaisProibidos =
        {
            "dbo.",
            "Projetos",
            "Ldp_Unificada",
            "Cronograma_Projetos",
            "ACC_Attribute_Sync_State",
            "ACC_Review_Current_Stage",
            "tb_ExternalActionLog",
            "tb_InternalActionLog",
            "tb_ScopeDeviation",
            "tb_HoldControl",
            "tb_WeeklyEngineeringSchedule",
            "NCR BASE LANCAMENTO",
            "NCR NONCONFORMITY REPORT",
            "DESVIO DE QUALIDADE"
        };

        private static readonly string CodigoFonte = RepoFileLocator.LerArquivoDaRaiz(
            "Services", "OperationalAudit", "Persistence", "AuditRepository.cs");

        /// <summary>Extrai o conteúdo de cada string (verbatim "@..." ou normal) atribuída a "CommandText =".</summary>
        private static readonly List<string> ComandosSql = ExtrairComandosSql(CodigoFonte);

        private static List<string> ExtrairComandosSql(string codigoFonte)
        {
            var comandos = new List<string>();
            foreach (Match m in Regex.Matches(codigoFonte, @"CommandText\s*=\s*@?""(?<sql>[\s\S]*?)"";"))
            {
                comandos.Add(m.Groups["sql"].Value);
            }
            return comandos;
        }

        private static IEnumerable<string> ExtrairTabelas(string sql, string palavraChave)
        {
            foreach (Match m in Regex.Matches(sql, palavraChave + @"\s+([\[\]\w\.]+)", RegexOptions.IgnoreCase))
            {
                yield return m.Groups[1].Value.Replace("[", "").Replace("]", "");
            }
        }

        [Fact]
        public void ArquivoFonteFoiEncontradoEContemPeloMenosUmComandoSql()
        {
            Assert.False(string.IsNullOrWhiteSpace(CodigoFonte));
            Assert.NotEmpty(ComandosSql);
        }

        [Fact]
        public void NenhumComandoSqlReferenciaQualquerIdentificadorOperacionalProibido()
        {
            string todoOTexto = string.Join("\n---\n", ComandosSql);
            var violacoes = IdentificadoresOperacionaisProibidos
                .Where(proibido => todoOTexto.IndexOf(proibido, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.True(violacoes.Count == 0,
                "AuditRepository.cs contém referência a identificador operacional proibido: " + string.Join(", ", violacoes));
        }

        [Fact]
        public void TodoInsertUpdateOuSelectSoReferenciaTabelasDoSchemaAudit()
        {
            var violacoes = new List<string>();

            foreach (var sql in ComandosSql)
            {
                var tabelas = ExtrairTabelas(sql, "INSERT INTO")
                    .Concat(ExtrairTabelas(sql, "UPDATE"))
                    .Concat(ExtrairTabelas(sql, "FROM"));

                foreach (var tabela in tabelas)
                {
                    bool permitida = TabelasPermitidas.Any(p => string.Equals(p, tabela, StringComparison.OrdinalIgnoreCase));
                    if (!permitida)
                    {
                        violacoes.Add($"Tabela '{tabela}' referenciada fora da lista permitida. SQL: {Resumir(sql)}");
                    }
                }
            }

            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        [Theory]
        [InlineData("audit.AuditRun")]
        [InlineData("audit.AuditProjectRun")]
        [InlineData("audit.AuditStageResult")]
        [InlineData("audit.AuditIssue")]
        [InlineData("audit.AuditOccurrence")]
        [InlineData("audit.AuditRequirementCatalog")]
        [InlineData("audit.AuditRequirementResult")]
        public void CadaUmaDasSeteTabelasDeAuditoriaRecebeAoMenosUmInsert(string tabela)
        {
            bool temInsert = ComandosSql.Any(sql =>
                ExtrairTabelas(sql, "INSERT INTO").Any(t => string.Equals(t, tabela, StringComparison.OrdinalIgnoreCase)));

            Assert.True(temInsert, $"Nenhum INSERT INTO {tabela} encontrado em AuditRepository.cs.");
        }

        [Theory]
        [InlineData("audit.AuditRun")]
        [InlineData("audit.AuditIssue")]
        public void ApenasAuditRunEAuditIssue_RecebemUpdate(string tabela)
        {
            bool temUpdate = ComandosSql.Any(sql =>
                ExtrairTabelas(sql, "UPDATE").Any(t => string.Equals(t, tabela, StringComparison.OrdinalIgnoreCase)));

            Assert.True(temUpdate, $"Nenhum UPDATE em {tabela} encontrado em AuditRepository.cs.");
        }

        [Fact]
        public void NenhumaTabelaAlemDeAuditRunEAuditIssue_RecebeUpdate()
        {
            var tabelasComUpdate = ComandosSql
                .SelectMany(sql => ExtrairTabelas(sql, "UPDATE"))
                .Select(t => t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.All(tabelasComUpdate, t =>
                Assert.True(
                    string.Equals(t, "audit.AuditRun", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "audit.AuditIssue", StringComparison.OrdinalIgnoreCase),
                    $"UPDATE inesperado em '{t}' — só audit.AuditRun e audit.AuditIssue deveriam receber UPDATE."));
        }

        [Fact]
        public void AuditRequirementCatalog_RecebeInsertESelect_NuncaUpdateOuDelete()
        {
            bool temInsert = ComandosSql.Any(sql =>
                ExtrairTabelas(sql, "INSERT INTO").Any(t => string.Equals(t, "audit.AuditRequirementCatalog", StringComparison.OrdinalIgnoreCase)));
            bool temSelect = ComandosSql.Any(sql =>
                sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) >= 0 &&
                ExtrairTabelas(sql, "FROM").Any(t => string.Equals(t, "audit.AuditRequirementCatalog", StringComparison.OrdinalIgnoreCase)));
            bool temUpdate = ComandosSql.Any(sql =>
                ExtrairTabelas(sql, "UPDATE").Any(t => string.Equals(t, "audit.AuditRequirementCatalog", StringComparison.OrdinalIgnoreCase)));

            Assert.True(temInsert);
            Assert.True(temSelect);
            Assert.False(temUpdate);
        }

        [Fact]
        public void NenhumComandoSqlContemVerboDeEscritaProibidoAlemDeInsertUpdate()
        {
            string[] verbosProibidos = { "DELETE", "TRUNCATE", "DROP", "ALTER TABLE", "CREATE TABLE", "EXEC ", "EXECUTE " };
            var violacoes = new List<string>();

            foreach (var sql in ComandosSql)
            {
                foreach (var verbo in verbosProibidos)
                {
                    if (Regex.IsMatch(sql, @"(?<![\w'])" + Regex.Escape(verbo), RegexOptions.IgnoreCase))
                    {
                        violacoes.Add($"Verbo proibido '{verbo}' encontrado em: {Resumir(sql)}");
                    }
                }
            }

            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        private static string Resumir(string sql)
        {
            string umaLinha = sql.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return umaLinha.Length <= 150 ? umaLinha : umaLinha[..150] + "...";
        }

        // =========================================================================================
        // Correção de Checkpoint 1, item 5/10 — corrida de ObterOuCriarRequirementCatalogAsync.
        //
        // "Não acessar SQL real" está em vigor nesta fase, e AuditRepository usa SqlConnection/
        // SqlTransaction CONCRETOS (nunca uma interface de abstração de banco — mesma convenção de
        // DatabaseService.cs), então não há como injetar um dublê e exercitar a corrida de verdade sem um
        // SQL Server real. Por isso estes são testes de CONTRATO: provam, por inspeção do texto-fonte real
        // do método, que a FORMA do código implementa as duas camadas de defesa exigidas — não simulam a
        // corrida em si. Este é exatamente o mesmo tipo de teste alternativo explicitamente aceito pela
        // instrução original ("teste concorrente OU teste contratual que prove o comportamento").
        // =========================================================================================

        private static readonly string CorpoObterOuCriarRequirementCatalogAsync = ExtrairCorpoDoMetodo(
            CodigoFonte, "ObterOuCriarRequirementCatalogAsync");

        /// <summary>O SELECT protegido por UPDLOCK/HOLDLOCK vive em seu próprio helper (SelecionarRequirementCatalogIdAsync), reutilizado tanto antes do INSERT quanto na recuperação pós-corrida — ver ObterOuCriarRequirementCatalog_SelectDeRecuperacaoUsaAMesmaChaveNaturalDoSelectOriginal.</summary>
        private static readonly string CorpoSelecionarRequirementCatalogIdAsync = ExtrairCorpoDoMetodo(
            CodigoFonte, "SelecionarRequirementCatalogIdAsync");

        private static string ExtrairCorpoDoMetodo(string codigoFonte, string nomeDoMetodo)
        {
            // Exige o prefixo de assinatura (public/private [static] async Task&lt;...&gt;) imediatamente
            // antes do nome — nunca casa com um CALL SITE do método (ex.: "await NomeDoMetodo(..." dentro de
            // outro método), só com a sua declaração real.
            var inicio = Regex.Match(
                codigoFonte,
                $@"\b(public|private)\s+(static\s+)?async\s+Task<\w+\??>\s+{Regex.Escape(nomeDoMetodo)}\s*\(",
                RegexOptions.IgnoreCase);
            Assert.True(inicio.Success, $"Declaração de {nomeDoMetodo} não encontrada em AuditRepository.cs.");

            int chaveAbre = codigoFonte.IndexOf('{', inicio.Index);
            int profundidade = 0;
            int i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}')
                {
                    profundidade--;
                    if (profundidade == 0) break;
                }
            }
            return codigoFonte[chaveAbre..(i + 1)];
        }

        [Fact]
        public void ObterOuCriarRequirementCatalog_ChamaOSelectProtegidoAntesDoInsert()
        {
            // ObterOuCriarRequirementCatalogAsync não monta o SELECT protegido inline — delega a
            // SelecionarRequirementCatalogIdAsync (verificado a seguir) e usa o resultado ANTES de sequer
            // tentar o INSERT.
            int indexDoSelect = CorpoObterOuCriarRequirementCatalogAsync.IndexOf("SelecionarRequirementCatalogIdAsync", StringComparison.Ordinal);
            int indexDoInsert = CorpoObterOuCriarRequirementCatalogAsync.IndexOf("INSERT INTO audit.AuditRequirementCatalog", StringComparison.Ordinal);
            Assert.True(indexDoSelect >= 0 && indexDoInsert >= 0 && indexDoSelect < indexDoInsert,
                "O SELECT protegido precisa ser chamado antes do INSERT.");
        }

        [Fact]
        public void SelecionarRequirementCatalogId_UsaUpdlockHoldlockNaChaveNaturalExata()
        {
            // A chave natural do SELECT protegido precisa ser exatamente (StageNumber, RequirementCode) —
            // a mesma da constraint UQ_AuditRequirementCatalog_Stage_Code do schema (verificado
            // separadamente em OperationalAuditSchemaContractTests).
            Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", CorpoSelecionarRequirementCatalogIdAsync, StringComparison.Ordinal);
            Assert.Contains("WHERE StageNumber = @StageNumber AND RequirementCode = @RequirementCode", CorpoSelecionarRequirementCatalogIdAsync);
        }

        [Fact]
        public void ObterOuCriarRequirementCatalog_TrataViolacaoDeChaveUnica_SemAbortarCegamente()
        {
            string corpo = CorpoObterOuCriarRequirementCatalogAsync;

            Assert.Contains("catch (SqlException ex) when (ex.Number is 2601 or 2627)", corpo, StringComparison.Ordinal);

            // Depois do catch, precisa haver um NOVO SELECT (reaproveitando o mesmo helper usado antes do
            // INSERT) — nunca simplesmente relançar a exceção sem antes tentar recuperar o Id real.
            int indexDoCatch = corpo.IndexOf("catch (SqlException", StringComparison.Ordinal);
            string corpoDoCatch = corpo[indexDoCatch..];
            Assert.Contains("SelecionarRequirementCatalogIdAsync", corpoDoCatch, StringComparison.Ordinal);
            Assert.Contains("return existenteAposCorrida.Value", corpoDoCatch, StringComparison.Ordinal);
        }

        [Fact]
        public void ObterOuCriarRequirementCatalog_SelectDeRecuperacaoUsaAMesmaChaveNaturalDoSelectOriginal()
        {
            // Garante que a recuperação pós-corrida não é uma busca "parecida" mas com critério diferente —
            // é exatamente a mesma função (SelecionarRequirementCatalogIdAsync) chamada duas vezes: antes do
            // INSERT e, se ele colidir, de novo depois.
            int ocorrencias = Regex.Matches(CorpoObterOuCriarRequirementCatalogAsync, "SelecionarRequirementCatalogIdAsync").Count;
            Assert.Equal(2, ocorrencias);
        }

        [Fact]
        public void ObterOuCriarRequirementCatalog_NaoRemoveAConstraintUnica_ScriptContinuaExigindoAChaveNatural()
        {
            // Ponte com OperationalAuditSchemaContractTests: a correção da corrida no C# só faz sentido se
            // a constraint única correspondente continuar existindo no schema.
            string sqlScript = RepoFileLocator.LerArquivoDaRaiz("Scripts", "OperationalAuditSchema.sql");
            Assert.Contains("UQ_AuditRequirementCatalog_Stage_Code UNIQUE (StageNumber, RequirementCode)", sqlScript);
        }

        [Fact]
        public void PersistirProjetoAsync_MantemRollbackEContinuidadeDeLote_MesmoSeARecuperacaoPosCorridaFalhar()
        {
            // "Transação continua válida": se mesmo depois do SELECT de recuperação a linha não for
            // encontrada (estado verdadeiramente inesperado), ObterOuCriarRequirementCatalogAsync lança
            // InvalidOperationException — e essa exceção precisa ser capturada pelo catch-geral de
            // PersistirProjetoAsync, que faz rollback e devolve Falha (nunca deixa a exceção vazar e
            // derrubar o processamento dos DEMAIS projetos do lote).
            string corpoPersistirProjeto = ExtrairCorpoDoMetodo(CodigoFonte, "PersistirProjetoAsync");

            Assert.Contains("catch (Exception ex)", corpoPersistirProjeto, StringComparison.Ordinal);
            Assert.Contains("transaction.RollbackAsync", corpoPersistirProjeto, StringComparison.Ordinal);
            Assert.Contains("AuditProjectPersistResult.Falha(ex.Message)", corpoPersistirProjeto, StringComparison.Ordinal);

            // ObterOuCriarRequirementCatalogAsync é chamado DENTRO do bloco try da mesma transação —
            // qualquer exceção dele (incluindo a de recuperação pós-corrida) cai neste catch-geral.
            int indexDoTry = corpoPersistirProjeto.IndexOf("try", StringComparison.Ordinal);
            int indexDaChamada = corpoPersistirProjeto.IndexOf("ObterOuCriarRequirementCatalogAsync", StringComparison.Ordinal);
            int indexDoCatch = corpoPersistirProjeto.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
            Assert.True(indexDoTry >= 0 && indexDoTry < indexDaChamada && indexDaChamada < indexDoCatch,
                "ObterOuCriarRequirementCatalogAsync precisa ser chamado dentro do try protegido pelo catch-geral de PersistirProjetoAsync.");
        }

        [Fact]
        public void ObterOuCriarRequirementCatalog_NenhumaTabelaAlemDeAuditRequirementCatalogEhReferenciadaParaEscrita()
        {
            var tabelas = ExtrairTabelas(CorpoObterOuCriarRequirementCatalogAsync, "INSERT INTO")
                .Concat(ExtrairTabelas(CorpoObterOuCriarRequirementCatalogAsync, "UPDATE"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.All(tabelas, t => Assert.Equal("audit.AuditRequirementCatalog", t, ignoreCase: true));
            Assert.NotEmpty(tabelas);
        }
    }
}
