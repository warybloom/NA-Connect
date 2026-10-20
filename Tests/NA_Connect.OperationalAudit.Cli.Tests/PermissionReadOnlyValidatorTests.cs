using System.Data;
using System.Threading.Tasks;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 11 e teste 14: validador de permissões só SELECT (HAS_PERMS_BY_NAME/fn_my_permissions), nunca INSERT/UPDATE/DELETE reais.</summary>
    public class PermissionReadOnlyValidatorTests
    {
        [Fact]
        public async Task SomenteLeitura_RelataSemPermissaoAlemDoNecessario()
        {
            var executor = new FakeCliSqlQueryExecutor();
            var tabela = new DataTable();
            tabela.Columns.Add("PodeSelect", typeof(int));
            tabela.Columns.Add("PodeInsert", typeof(int));
            tabela.Columns.Add("PodeUpdate", typeof(int));
            tabela.Columns.Add("PodeDelete", typeof(int));
            tabela.Rows.Add(1, 0, 0, 0);
            executor.ConfigurarResposta("HAS_PERMS_BY_NAME", tabela);

            var relatorio = await PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(executor);

            Assert.True(relatorio.PodeSelect);
            Assert.False(relatorio.TemPermissaoAlemDoNecessarioParaLeitura);
        }

        [Fact]
        public async Task PermissaoDeEscritaPresente_RelataComoAlemDoNecessario_NuncaTentaCorrigir()
        {
            var executor = new FakeCliSqlQueryExecutor();
            var tabela = new DataTable();
            tabela.Columns.Add("PodeSelect", typeof(int));
            tabela.Columns.Add("PodeInsert", typeof(int));
            tabela.Columns.Add("PodeUpdate", typeof(int));
            tabela.Columns.Add("PodeDelete", typeof(int));
            tabela.Rows.Add(1, 1, 0, 0);
            executor.ConfigurarResposta("HAS_PERMS_BY_NAME", tabela);

            var relatorio = await PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(executor);

            Assert.True(relatorio.TemPermissaoAlemDoNecessarioParaLeitura);
            // Nunca um INSERT/UPDATE/DELETE real foi tentado — só a consulta de metadados.
            Assert.Single(executor.SqlExecutados);
            Assert.Contains("HAS_PERMS_BY_NAME", executor.SqlExecutados[0]);
        }

        [Fact]
        public async Task AcessoAoSchemaAudit_ConsultaSomenteFnMyPermissions()
        {
            var executor = new FakeCliSqlQueryExecutor();
            var tabela = new DataTable();
            tabela.Columns.Add("Quantidade", typeof(int));
            tabela.Rows.Add(3);
            executor.ConfigurarResposta("fn_my_permissions", tabela);

            bool? temAcesso = await PermissionReadOnlyValidator.ValidarAcessoAoSchemaAuditAsync(executor);

            Assert.True(temAcesso);
            Assert.Contains(executor.SqlExecutados, sql => sql.Contains("fn_my_permissions"));
        }

        [Fact]
        public async Task FalhaDeConexao_NuncaLanca()
        {
            var executor = new FakeCliSqlQueryExecutor { LancarExcecao = true };

            var relatorio = await PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(executor);
            bool? acesso = await PermissionReadOnlyValidator.ValidarAcessoAoSchemaAuditAsync(executor);

            Assert.NotNull(relatorio.Erro);
            Assert.Null(acesso);
        }
    }
}
