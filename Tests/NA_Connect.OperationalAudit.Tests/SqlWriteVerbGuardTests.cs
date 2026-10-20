using APS.WebApp.Services.OperationalAudit.Safety;

namespace NA_Connect.OperationalAudit.Tests
{
    public class SqlWriteVerbGuardTests
    {
        [Theory]
        [InlineData("SELECT * FROM dbo.Foo")]
        [InlineData("select * from dbo.Foo")]
        [InlineData("  \n\t SELECT 1")]
        [InlineData("-- comentario de linha\nSELECT 1")]
        [InlineData("/* comentario de bloco */ SELECT 1")]
        [InlineData("/* comentario\nem varias\nlinhas */ SELECT 1")]
        [InlineData("-- linha 1\n-- linha 2\n/* bloco */ -- outra linha\nSELECT 1")]
        [InlineData("WITH cte AS (SELECT 1 AS X) SELECT X FROM cte")]
        public void EhComandoDeEscrita_ComandosDeLeitura_RetornaFalse(string sql)
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("INSERT INTO dbo.Foo VALUES (1)")]
        [InlineData("UPDATE dbo.Foo SET X = 1")]
        [InlineData("DELETE FROM dbo.Foo")]
        [InlineData("MERGE INTO dbo.Foo USING dbo.Bar ON 1=1 WHEN MATCHED THEN DELETE;")]
        [InlineData("TRUNCATE TABLE dbo.Foo")]
        [InlineData("ALTER TABLE dbo.Foo ADD Y INT")]
        [InlineData("CREATE TABLE dbo.Foo (X INT)")]
        [InlineData("DROP TABLE dbo.Foo")]
        [InlineData("   DELETE FROM dbo.Foo")]
        [InlineData("-- nota\nDELETE FROM dbo.Foo")]
        [InlineData("/* nota */ DELETE FROM dbo.Foo")]
        [InlineData("delete from dbo.Foo")]
        public void EhComandoDeEscrita_ComandosDeEscrita_RetornaTrue(string sql)
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("EXEC sp_DoSomethingCustom")]
        [InlineData("EXECUTE sp_DoSomethingCustom")]
        [InlineData("EXEC dbo.sp_MinhaProcedure")]
        public void EhComandoDeEscrita_ExecDeProcedureDesconhecida_TratadaComoEscritaPorPadrao(string sql)
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("EXEC sp_help 'dbo.Foo'")]
        [InlineData("exec sp_columns 'dbo.Foo'")]
        [InlineData("EXECUTE SP_TABLES")]
        public void EhComandoDeEscrita_ExecDeProcedureDeSistemaConhecidaComoLeitura_RetornaFalse(string sql)
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("SELECT UpdatedAt, CreatedAt FROM dbo.Foo")]
        [InlineData("SELECT DeletedFlag FROM dbo.Foo")]
        [InlineData("SELECT InsertedBy FROM dbo.Foo")]
        public void EhComandoDeEscrita_NomesDeColunaParecidosComVerbosDeEscrita_NaoGeramFalsoPositivo(string sql)
        {
            // So o PRIMEIRO token e comparado por igualdade exata — "UpdatedAt" nunca e confundido com "UPDATE".
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("SELECT 'este registro foi DELETE-ado manualmente' AS Nota FROM dbo.Foo")]
        [InlineData("SELECT Observacao FROM dbo.Foo WHERE Observacao = 'UPDATE realizado pelo usuario'")]
        public void EhComandoDeEscrita_VerboDeEscritaDentroDeLiteralDeString_NaoGeraFalsoPositivo(string sql)
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EhComandoDeEscrita_EntradaNulaOuEmBranco_RetornaFalse(string? sql)
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Fact]
        public void GarantirSomenteLeitura_ComandoDeLeitura_NaoLanca()
        {
            var exception = Record.Exception(() => SqlWriteVerbGuard.GarantirSomenteLeitura("SELECT * FROM dbo.Foo"));
            Assert.Null(exception);
        }

        [Fact]
        public void GarantirSomenteLeitura_ComandoDeEscrita_LancaForbiddenOperationException()
        {
            Assert.Throws<ForbiddenOperationException>(() => SqlWriteVerbGuard.GarantirSomenteLeitura("DELETE FROM dbo.Foo"));
        }

        [Fact]
        public void GarantirSomenteLeitura_MensagemDeErro_NaoExpoeSqlCompletoQuandoMuitoLongo()
        {
            string sqlLongo = "DELETE FROM dbo.Foo WHERE " + new string('X', 500);
            var ex = Assert.Throws<ForbiddenOperationException>(() => SqlWriteVerbGuard.GarantirSomenteLeitura(sqlLongo));
            Assert.Contains("...", ex.Message);
            Assert.True(ex.Message.Length < sqlLongo.Length);
        }

        // =========================================================================================
        // Correção de Checkpoint 1, item 6/10: CTE e múltiplas instruções no mesmo lote.
        // =========================================================================================

        [Theory]
        [InlineData("WITH Dados AS (SELECT 1 AS Id) SELECT * FROM Dados;")]
        [InlineData("WITH A AS (SELECT 1 AS X), B AS (SELECT 2 AS Y) SELECT * FROM A, B;")]
        [InlineData("WITH Dados (Id) AS (SELECT 1) SELECT * FROM Dados;")]
        public void EhComandoDeEscrita_CteSeguidoDeSelect_Permitido(string sql)
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("WITH Dados AS (SELECT 1 AS Id) DELETE FROM dbo.Foo;")]
        [InlineData("WITH Dados AS\n(\n    SELECT 1 AS Id\n)\nDELETE FROM dbo.Foo;")]
        public void EhComandoDeEscrita_CteSeguidoDeDelete_Bloqueado(string sql)
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Theory]
        [InlineData("WITH Dados AS (SELECT 1 AS Id) UPDATE dbo.Foo SET Valor = 1;")]
        [InlineData("WITH Dados AS\n(\n    SELECT 1 AS Id\n)\nUPDATE dbo.Foo SET Valor = 1;")]
        public void EhComandoDeEscrita_CteSeguidoDeUpdate_Bloqueado(string sql)
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Fact]
        public void EhComandoDeEscrita_SelectSeguidoDeDelete_Bloqueado()
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 1;\nDELETE FROM dbo.Foo;"));
        }

        [Fact]
        public void EhComandoDeEscrita_SelectSeguidoDeUpdate_Bloqueado()
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 1;\nUPDATE dbo.Foo SET Valor = 1;"));
        }

        [Fact]
        public void EhComandoDeEscrita_SelectSeguidoDeExec_Bloqueado()
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 1;\nEXEC dbo.ProcedimentoDeEscrita;"));
        }

        [Fact]
        public void EhComandoDeEscrita_SelectSeguidoDeMerge_Bloqueado()
        {
            string sql = "SELECT 1;\nMERGE dbo.Foo AS Target USING dbo.Bar AS Source ON 1 = 0 WHEN NOT MATCHED THEN INSERT DEFAULT VALUES;";
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Fact]
        public void EhComandoDeEscrita_PalavraInsertDentroDeComentario_Permitida()
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita("-- este comentario fala de INSERT mas nao executa nada\nSELECT 1;"));
        }

        [Fact]
        public void EhComandoDeEscrita_MultiplosComentariosAntesDeUpdate_Bloqueado()
        {
            string sql = "-- primeira nota\n-- segunda nota\n/* bloco de\ncomentario */ -- terceira nota\nUPDATE dbo.Foo SET Valor = 1;";
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita(sql));
        }

        [Fact]
        public void EhComandoDeEscrita_PontoEVirgulaDentroDeStringLiteral_NaoQuebraAInstrucaoIndevidamente()
        {
            // O ";" dentro da string faz parte do literal — nao pode ser interpretado como separador de
            // instrucoes, e a instrucao inteira continua sendo um SELECT inofensivo.
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 'a; DELETE FROM dbo.Foo' AS Texto;"));
        }

        [Fact]
        public void EhComandoDeEscrita_LoteComTresInstrucoes_SoAUltimaEhEscrita_AindaAssimBloqueado()
        {
            Assert.True(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 1; SELECT 2; DELETE FROM dbo.Foo;"));
        }

        [Fact]
        public void EhComandoDeEscrita_LoteComTresInstrucoesTodasDeLeitura_Permitido()
        {
            Assert.False(SqlWriteVerbGuard.EhComandoDeEscrita("SELECT 1; SELECT 2; SELECT 3;"));
        }
    }
}
