using System;
using System.IO;
using APS.WebApp.Services.OperationalAudit.Reporting;

namespace NA_Connect.OperationalAudit.Tests
{
    public class ReportFileNamingTests
    {
        [Fact]
        public void NomeArquivo_SeguePadraoComExecutionIdEDataHoraENFormat()
        {
            var executionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var momento = new DateTime(2026, 8, 6, 14, 30, 5, DateTimeKind.Utc);

            string nome = ReportFileNaming.NomeArquivo(executionId, momento, "json");

            Assert.Equal("Auditoria_Operacional_11111111222233334444555555555555_20260806_143005.json", nome);
        }

        [Theory]
        [InlineData("txt")]
        [InlineData("csv")]
        [InlineData("json")]
        public void NomeArquivo_TerminaComAExtensaoPedida(string extensao)
        {
            string nome = ReportFileNaming.NomeArquivo(Guid.NewGuid(), DateTime.UtcNow, extensao);
            Assert.EndsWith("." + extensao, nome);
        }

        [Fact]
        public void GarantirDiretorioECaminho_CriaODiretorioQuandoNaoExiste()
        {
            string diretorioTemp = Path.Combine(Path.GetTempPath(), "NAConnect_AuditTests_" + Guid.NewGuid().ToString("N"));
            Assert.False(Directory.Exists(diretorioTemp));

            try
            {
                string caminho = ReportFileNaming.GarantirDiretorioECaminho(diretorioTemp, Guid.NewGuid(), DateTime.UtcNow, "txt");

                Assert.True(Directory.Exists(diretorioTemp));
                Assert.Equal(diretorioTemp, Path.GetDirectoryName(caminho));
            }
            finally
            {
                if (Directory.Exists(diretorioTemp)) Directory.Delete(diretorioTemp, recursive: true);
            }
        }
    }
}
