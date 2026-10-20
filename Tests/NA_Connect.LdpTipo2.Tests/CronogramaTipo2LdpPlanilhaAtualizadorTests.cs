using System;
using System.Collections.Generic;
using System.IO;
using APS.WebApp.Services.CronogramaTipo2;
using NA_Connect.LdpTipo2.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre o diff célula-a-célula de CronogramaTipo2LdpPlanilhaAtualizador (item 3.1 do checkpoint
    /// pré-canário): rodar a mesma atualização duas vezes seguidas sobre a mesma célula já preenchida
    /// com o valor esperado não deve reescrever nem contar como alteração — pré-requisito para a
    /// publicação condicional no ACC nunca gerar uma versão desnecessária da LDP.
    /// </summary>
    public class CronogramaTipo2LdpPlanilhaAtualizadorTests
    {
        private static string SalvarWorkbookTemporario(ExcelPackage pkg)
        {
            string caminho = Path.Combine(Path.GetTempPath(), $"LDP_T2_TESTE_{Guid.NewGuid()}.xlsx");
            File.WriteAllBytes(caminho, pkg.GetAsByteArray());
            return caminho;
        }

        [Fact]
        public void AtualizarCopia_PrimeiraExecucao_EscreveEContaComoAlteracao()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título", new Dictionary<byte, string> { [60] = "X" });

            string origem = SalvarWorkbookTemporario(pkg);
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    TerminoLinhaBase = new DateTime(2026, 3, 1),
                    Termino = new DateTime(2026, 3, 10),
                    PercentRealizado = 55m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1, resultado.CelulasDataBaselineAtualizadas);
                Assert.Equal(1, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.Equal(1, resultado.CelulasRealizadoAtualizadas);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void AtualizarCopia_SegundaExecucaoComMesmoValor_NaoReescreveNemConta()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título", new Dictionary<byte, string> { [60] = "X" });

            string origem = SalvarWorkbookTemporario(pkg);
            string destino1 = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");
            string destino2 = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    TerminoLinhaBase = new DateTime(2026, 3, 1),
                    Termino = new DateTime(2026, 3, 10),
                    PercentRealizado = 55m
                };

                // 1ª execução: origem (sem valores) -> destino1 (com valores escritos).
                var primeira = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino1, new[] { item });
                Assert.True(primeira.HouveAlteracao);

                // 2ª execução: usa destino1 (já com os valores) como NOVA origem, mesmíssimo item ->
                // simula "rodar o mesmo Tipo 2 duas vezes sem mudança na origem" (item 7 do checkpoint).
                var segunda = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(destino1, destino2, new[] { item });

                Assert.True(segunda.Sucesso);
                Assert.False(segunda.HouveAlteracao);
                Assert.Equal(0, segunda.CelulasDataBaselineAtualizadas);
                Assert.Equal(0, segunda.CelulasDataReplanejadaAtualizadas);
                Assert.Equal(0, segunda.CelulasRealizadoAtualizadas);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino1)) File.Delete(destino1);
                if (File.Exists(destino2)) File.Delete(destino2);
            }
        }

        [Fact]
        public void AtualizarCopia_ValorGenuinamenteDiferente_ReescreveEConta()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, "DOC-001", "CLI-001", "Título", new Dictionary<byte, string> { [60] = "X" });

            string origem = SalvarWorkbookTemporario(pkg);
            string destino1 = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");
            string destino2 = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var primeiro = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PercentRealizado = 55m
                };
                CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino1, new[] { primeiro });

                var segundo = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PercentRealizado = 70m // valor realmente diferente
                };
                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(destino1, destino2, new[] { segundo });

                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1, resultado.CelulasRealizadoAtualizadas);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino1)) File.Delete(destino1);
                if (File.Exists(destino2)) File.Delete(destino2);
            }
        }
    }
}
