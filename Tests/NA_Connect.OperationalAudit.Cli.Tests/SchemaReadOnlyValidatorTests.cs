using System.Data;
using System.Linq;
using System.Threading.Tasks;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 10 e teste 13: validador de schema só SELECT.</summary>
    public class SchemaReadOnlyValidatorTests
    {
        private static DataTable TabelaDeObjetos(params (string Nome, string Tipo)[] objetos)
        {
            var tabela = new DataTable();
            tabela.Columns.Add("ObjectName", typeof(string));
            tabela.Columns.Add("ObjectType", typeof(string));
            foreach (var (nome, tipo) in objetos) tabela.Rows.Add(nome, tipo);
            return tabela;
        }

        [Fact]
        public async Task TodosOsObjetosPresentes_TodosPresentesTrue()
        {
            var executor = new FakeCliSqlQueryExecutor();
            executor.ConfigurarResposta("sys.tables", TabelaDeObjetos(
                ("AuditRun", "TABLE"), ("AuditProjectRun", "TABLE"), ("AuditStageResult", "TABLE"),
                ("AuditIssue", "TABLE"), ("AuditOccurrence", "TABLE"), ("AuditRequirementCatalog", "TABLE"), ("AuditRequirementResult", "TABLE"),
                ("VW_AuditIssues_BI", "VIEW"), ("VW_AuditOccurrences_BI", "VIEW"), ("VW_AuditStageSummary_BI", "VIEW"),
                ("VW_AuditProjectSummary_BI", "VIEW"), ("VW_AuditRequirementsCompliance_BI", "VIEW")));

            var resultado = await SchemaReadOnlyValidator.ValidarAsync(executor);

            Assert.True(resultado.TodosPresentes);
            Assert.Equal(12, resultado.Objetos.Count);
        }

        [Fact]
        public async Task ObjetoAusente_TodosPresentesFalse()
        {
            var executor = new FakeCliSqlQueryExecutor();
            executor.ConfigurarResposta("sys.tables", TabelaDeObjetos(("AuditRun", "TABLE"))); // só um dos 12.

            var resultado = await SchemaReadOnlyValidator.ValidarAsync(executor);

            Assert.False(resultado.TodosPresentes);
            Assert.Contains(resultado.Objetos, o => o.NomeCompleto == "[audit].[AuditIssue]" && !o.Existe);
        }

        [Fact]
        public async Task SoExecutaSelect_NuncaOutroVerbo()
        {
            var executor = new FakeCliSqlQueryExecutor();
            executor.ConfigurarResposta("sys.tables", new DataTable());

            await SchemaReadOnlyValidator.ValidarAsync(executor);

            Assert.All(executor.SqlExecutados, sql => Assert.StartsWith("SELECT", sql.TrimStart(), System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(executor.SqlExecutados, sql =>
                sql.Contains("INSERT", System.StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("UPDATE", System.StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("DELETE", System.StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("CREATE", System.StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("ALTER", System.StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("DROP", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task FalhaDeConexao_NuncaLanca_DevolveErroNoResultado()
        {
            var executor = new FakeCliSqlQueryExecutor { LancarExcecao = true };

            var resultado = await SchemaReadOnlyValidator.ValidarAsync(executor);

            Assert.False(resultado.TodosPresentes);
            Assert.NotNull(resultado.Erro);
        }
    }
}
