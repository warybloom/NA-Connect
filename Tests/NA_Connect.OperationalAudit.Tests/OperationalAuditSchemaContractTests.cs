using System;
using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Contrato do script <c>Scripts\OperationalAuditSchema.sql</c> — lê o TEXTO real do arquivo (o script
    /// nunca é executado por este teste, nem por nada nesta fase) e confirma, por inspeção textual, que ele
    /// contém exatamente os objetos exigidos pelo Checkpoint 1: schema [audit], as 7 tabelas, as 5 views
    /// oficiais do contrato Power BI, PKs/FKs/índices únicos esperados, e nenhuma criação/alteração de
    /// objeto [dbo] ou DROP/ALTER de tabela operacional.
    /// </summary>
    public class OperationalAuditSchemaContractTests
    {
        private static readonly string Sql = RepoFileLocator.LerArquivoDaRaiz("Scripts", "OperationalAuditSchema.sql");

        [Fact]
        public void ArquivoDeScriptFoiEncontradoENaoEstaVazio()
        {
            Assert.False(string.IsNullOrWhiteSpace(Sql));
        }

        [Fact]
        public void CriaOSchemaAudit()
        {
            Assert.Matches(new Regex(@"CREATE SCHEMA\s+\[?audit\]?", RegexOptions.IgnoreCase), Sql);
        }

        [Theory]
        [InlineData("AuditRun")]
        [InlineData("AuditProjectRun")]
        [InlineData("AuditStageResult")]
        [InlineData("AuditIssue")]
        [InlineData("AuditOccurrence")]
        [InlineData("AuditRequirementCatalog")]
        [InlineData("AuditRequirementResult")]
        public void CriaCadaUmaDasSeteTabelasObrigatorias(string tabela)
        {
            var padrao = new Regex($@"CREATE TABLE\s+audit\.{Regex.Escape(tabela)}\b", RegexOptions.IgnoreCase);
            Assert.Matches(padrao, Sql);
        }

        [Theory]
        [InlineData("VW_AuditIssues_BI")]
        [InlineData("VW_AuditOccurrences_BI")]
        [InlineData("VW_AuditStageSummary_BI")]
        [InlineData("VW_AuditProjectSummary_BI")]
        [InlineData("VW_AuditRequirementsCompliance_BI")]
        public void CriaCadaUmaDasCincoViewsOficiais_ComONomeExato(string view)
        {
            var padrao = new Regex($@"CREATE (OR ALTER )?VIEW\s+audit\.{Regex.Escape(view)}\b", RegexOptions.IgnoreCase);
            Assert.Matches(padrao, Sql);
        }

        [Theory]
        [InlineData("vw_AuditRunOverview")]
        [InlineData("vw_ProjectComplianceOverview")]
        [InlineData("vw_StageResultDetail")]
        [InlineData("vw_OpenIssues")]
        [InlineData("vw_CustomAttributeFindings")]
        public void NaoContemMaisAsViewsAlternativasSubstituidas(string nomeAntigo)
        {
            Assert.DoesNotContain(nomeAntigo, Sql, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("AuditRun", "PK_AuditRun")]
        [InlineData("AuditProjectRun", "PK_AuditProjectRun")]
        [InlineData("AuditStageResult", "PK_AuditStageResult")]
        [InlineData("AuditIssue", "PK_AuditIssue")]
        [InlineData("AuditOccurrence", "PK_AuditOccurrence")]
        [InlineData("AuditRequirementCatalog", "PK_AuditRequirementCatalog")]
        [InlineData("AuditRequirementResult", "PK_AuditRequirementResult")]
        public void CadaTabela_TemPrimaryKey(string tabela, string nomeEsperadoDaPk)
        {
            Assert.Contains("CONSTRAINT " + nomeEsperadoDaPk + " PRIMARY KEY", Sql, StringComparison.OrdinalIgnoreCase);
            // A tabela nao pode aparecer definida sem a construcao de PK entre sua definicao e a proxima tabela.
            Assert.True(TabelaContemTrecho(tabela, "PRIMARY KEY"), $"Tabela {tabela} nao contem PRIMARY KEY em sua propria definicao.");
        }

        [Theory]
        [InlineData("AuditProjectRun", "AuditRun")]
        [InlineData("AuditStageResult", "AuditProjectRun")]
        [InlineData("AuditRequirementResult", "AuditProjectRun")]
        [InlineData("AuditRequirementResult", "AuditRequirementCatalog")]
        [InlineData("AuditOccurrence", "AuditIssue")]
        [InlineData("AuditOccurrence", "AuditRun")]
        public void ForeignKeysEsperadasExistem(string tabelaFilha, string tabelaPai)
        {
            Assert.True(
                TabelaContemTrecho(tabelaFilha, $"REFERENCES audit.{tabelaPai}"),
                $"Nenhuma FOREIGN KEY de {tabelaFilha} para audit.{tabelaPai} encontrada.");
        }

        [Fact]
        public void AuditIssue_TemIndiceUnicoDeDedupHashPorProjeto()
        {
            Assert.True(TabelaContemTrecho("AuditIssue", "UNIQUE (ProjectIdAcc, DedupHash)"));
        }

        [Fact]
        public void AuditOccurrence_TemIndiceUnicoDeOccurrenceHashPorIssue()
        {
            Assert.True(TabelaContemTrecho("AuditOccurrence", "UNIQUE (AuditIssueId, OccurrenceHash)"));
        }

        [Fact]
        public void AuditRequirementCatalog_TemChaveNaturalUnicaDeEtapaMaisCodigo()
        {
            Assert.True(TabelaContemTrecho("AuditRequirementCatalog", "UNIQUE (StageNumber, RequirementCode)"));
        }

        [Fact]
        public void NenhumObjetoDboEhCriadoOuAlterado()
        {
            Assert.DoesNotMatch(new Regex(@"CREATE\s+(TABLE|VIEW|PROCEDURE|FUNCTION)\s+dbo\.", RegexOptions.IgnoreCase), Sql);
            Assert.DoesNotMatch(new Regex(@"ALTER\s+(TABLE|VIEW|PROCEDURE|FUNCTION)\s+dbo\.", RegexOptions.IgnoreCase), Sql);
        }

        [Fact]
        public void NenhumDropDeTabelaOperacionalExiste()
        {
            Assert.DoesNotMatch(new Regex(@"DROP\s+TABLE", RegexOptions.IgnoreCase), Sql);
        }

        [Fact]
        public void NenhumAlterTableOperacionalExiste()
        {
            // O script pode (e deve) usar ALTER apenas dentro do proprio schema audit, se algum dia
            // precisar — nesta versao nao usa ALTER TABLE nenhum; a garantia aqui e' que nao existe ALTER
            // TABLE algum fora do schema audit (equivalente a dizer: nenhum ALTER TABLE dbo.*).
            var alters = Regex.Matches(Sql, @"ALTER\s+TABLE\s+([\[\]\w\.]+)", RegexOptions.IgnoreCase);
            foreach (Match m in alters)
            {
                string alvo = m.Groups[1].Value.Replace("[", "").Replace("]", "");
                Assert.StartsWith("audit.", alvo, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ScriptNuncaContemComandoDeExecucaoContraOBancoAlemDaCriacaoDeObjetos()
        {
            // Defesa adicional: o script não pode conter INSERT/UPDATE/DELETE de DADOS (só DDL) — ele cria
            // estrutura, nunca grava linhas, e nunca deve ser confundido com um script de seed/migração de
            // dados.
            Assert.DoesNotMatch(new Regex(@"^\s*INSERT\s+INTO", RegexOptions.IgnoreCase | RegexOptions.Multiline), Sql);
            Assert.DoesNotMatch(new Regex(@"^\s*DELETE\s+FROM", RegexOptions.IgnoreCase | RegexOptions.Multiline), Sql);
        }

        /// <summary>Verifica se o trecho aparece dentro do bloco de definição da tabela indicada (do "CREATE TABLE audit.X" até o próximo "GO").</summary>
        private static bool TabelaContemTrecho(string tabela, string trecho)
        {
            var inicio = Regex.Match(Sql, $@"CREATE TABLE\s+audit\.{Regex.Escape(tabela)}\b", RegexOptions.IgnoreCase);
            if (!inicio.Success) return false;

            int indexFim = Sql.IndexOf("\nGO", inicio.Index, StringComparison.OrdinalIgnoreCase);
            if (indexFim < 0) indexFim = Sql.Length;

            string blocoDaTabela = Sql[inicio.Index..indexFim];
            return blocoDaTabela.IndexOf(trecho, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
