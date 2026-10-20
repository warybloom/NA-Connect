using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seção 5 e testes 11/12: segredo e connection string completa nunca aparecem no log.</summary>
    public class SecretRedactorTests
    {
        [Fact]
        public void ResumirConnectionString_NuncaContemSenha()
        {
            string resumo = SecretRedactor.ResumirConnectionString("Server=srv1;Database=db1;User ID=admin;Password=MinhaSenhaSecreta;");

            Assert.DoesNotContain("MinhaSenhaSecreta", resumo);
            Assert.Contains("srv1", resumo);
            Assert.Contains("db1", resumo);
        }

        [Fact]
        public void ResumirConnectionString_NuncaContemUserId()
        {
            string resumo = SecretRedactor.ResumirConnectionString("Server=srv1;Database=db1;User ID=usuario_secreto;Password=x;");

            Assert.DoesNotContain("usuario_secreto", resumo);
        }

        [Fact]
        public void ResumirConnectionString_Vazia_NuncaLanca()
        {
            Assert.Equal("(não configurada)", SecretRedactor.ResumirConnectionString(""));
            Assert.Equal("(não configurada)", SecretRedactor.ResumirConnectionString(null));
        }

        [Fact]
        public void ResumirConnectionString_FormatoInvalido_NuncaEcoaOValorBruto()
        {
            string bruto = "isto-nao-e-uma-connection-string-valida-mas-poderia-conter-SENHA=123";
            string resumo = SecretRedactor.ResumirConnectionString(bruto);

            Assert.DoesNotContain("SENHA=123", resumo);
        }

        [Theory]
        [InlineData("APS:ClientSecret")]
        [InlineData("Password")]
        [InlineData("Some:Token")]
        [InlineData("ApiKey")]
        public void PareceChaveSensivel_DetectaNomesConhecidos(string chave)
        {
            Assert.True(SecretRedactor.PareceChaveSensivel(chave));
        }

        [Fact]
        public void PareceChaveSensivel_NuncaFalsoPositivoParaChaveComum()
        {
            Assert.False(SecretRedactor.PareceChaveSensivel("OperationalAudit:OutputDirectory"));
        }

        [Fact]
        public void Mascarar_NuncaDevolveOValorOriginal()
        {
            string mascarado = SecretRedactor.Mascarar("valor-super-secreto");

            Assert.DoesNotContain("valor-super-secreto", mascarado);
            Assert.Equal("***REDACTED***", mascarado);
        }

        [Fact]
        public void RedigirSeAparecer_RemoveTodosOsValoresConhecidosDoTexto()
        {
            string texto = "Falha ao autenticar com client_secret=ABC123 e token=XYZ789";
            string redigido = SecretRedactor.RedigirSeAparecer(texto, new[] { "ABC123", "XYZ789" });

            Assert.DoesNotContain("ABC123", redigido);
            Assert.DoesNotContain("XYZ789", redigido);
        }
    }
}
