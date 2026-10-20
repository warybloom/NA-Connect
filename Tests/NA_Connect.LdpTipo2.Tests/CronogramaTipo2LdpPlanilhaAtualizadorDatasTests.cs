using System;
using System.Collections.Generic;
using System.IO;
using APS.WebApp.Services.CronogramaTipo2;
using NA_Connect.LdpTipo2.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre a leitura segura de células de data em CronogramaTipo2LdpPlanilhaAtualizador (checkpoint
    /// pós-canário): a LDP usa "-" como placeholder legítimo de "sem data" em PLANNED DATA (BASE LINE)
    /// e REPLANNED DATE, e a leitura antiga (GetValue&lt;DateTime?&gt;() direto) lançava FormatException
    /// nessas células. A correção passou a usar LdpTipo2CellReader.LerData (já usado pelo parser da
    /// LDP Tipo 2) para a célula EXISTENTE antes do diff — nunca para o valor novo, que já vem validado
    /// do Cronograma.
    /// </summary>
    public class CronogramaTipo2LdpPlanilhaAtualizadorDatasTests
    {
        private static string SalvarWorkbookTemporario(ExcelPackage pkg)
        {
            string caminho = Path.Combine(Path.GetTempPath(), $"LDP_T2_TESTE_{Guid.NewGuid()}.xlsx");
            File.WriteAllBytes(caminho, pkg.GetAsByteArray());
            return caminho;
        }

        private static (LdpTipo2WorkbookLayout Layout, string Origem) MontarComCelula(byte gate, string na, object valorReplannedDate)
        {
            var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, na, "CLI-001", "Título", new Dictionary<byte, string> { [gate] = "X" });

            if (valorReplannedDate != null)
                layout.Worksheet.Cells[8, layout.Gates[gate].ColunaReplannedDate].Value = valorReplannedDate;

            string origem = SalvarWorkbookTemporario(pkg);
            pkg.Dispose();
            return (layout, origem);
        }

        private static DateTime? LerReplannedDateDoDestino(LdpTipo2WorkbookLayout layout, byte gate, string destino)
        {
            using var pkgDestino = new ExcelPackage(new FileInfo(destino));
            var ws = pkgDestino.Workbook.Worksheets["LDP"];
            return ws.Cells[8, layout.Gates[gate].ColunaReplannedDate].GetValue<DateTime?>();
        }

        private static object LerReplannedDateBrutoDoDestino(LdpTipo2WorkbookLayout layout, byte gate, string destino)
        {
            using var pkgDestino = new ExcelPackage(new FileInfo(destino));
            var ws = pkgDestino.Workbook.Worksheets["LDP"];
            return ws.Cells[8, layout.Gates[gate].ColunaReplannedDate].Value;
        }

        [Theory]
        [InlineData((byte)30)]
        [InlineData((byte)60)]
        [InlineData((byte)90)]
        public void CelulaComHifen_MaisNovaDataValida_Atualiza(byte gate)
        {
            var (layout, origem) = MontarComCelula(gate, "DOC-001", "-");
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = gate,
                    Termino = new DateTime(2026, 9, 25)
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(1, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(new DateTime(2026, 9, 25), LerReplannedDateDoDestino(layout, gate, destino));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Theory]
        [InlineData((byte)30)]
        [InlineData((byte)60)]
        [InlineData((byte)90)]
        public void CelulaComHifen_MaisCronogramaSemData_NaoAltera(byte gate)
        {
            var (layout, origem) = MontarComCelula(gate, "DOC-001", "-");
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = gate,
                    Termino = null // Cronograma sem data — a célula "-" não deve ser tocada.
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(0, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.False(resultado.HouveAlteracao);
                Assert.Equal("-", LerReplannedDateBrutoDoDestino(layout, gate, destino));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CelulaVazia_MaisNovaDataValida_Atualiza()
        {
            var (layout, origem) = MontarComCelula(60, "DOC-001", null);
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    Termino = new DateTime(2026, 9, 25)
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(1, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.Equal(new DateTime(2026, 9, 25), LerReplannedDateDoDestino(layout, 60, destino));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CelulaComDateTimeNativo_MesmaData_NaoAltera()
        {
            var (layout, origem) = MontarComCelula(60, "DOC-001", new DateTime(2026, 9, 25));
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    Termino = new DateTime(2026, 9, 25) // mesma data já presente na célula
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(0, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.False(resultado.HouveAlteracao);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CelulaComDateTimeNativo_DataDiferente_Atualiza()
        {
            var (layout, origem) = MontarComCelula(60, "DOC-001", new DateTime(2026, 1, 1));
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    Termino = new DateTime(2026, 9, 25) // diferente da célula atual
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(1, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.Equal(new DateTime(2026, 9, 25), LerReplannedDateDoDestino(layout, 60, destino));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CelulaComStringDeDataValida_InterpretaCorretamente_NaoAlteraQuandoJaBateComoNovoValor()
        {
            // Se a leitura NÃO interpretasse "25/09/2026" como data (ex.: tratasse como texto opaco),
            // a comparação com o DateTime novo nunca bateria e a célula seria reescrita sempre —
            // HouveAlteracao=false aqui só é possível porque a string foi corretamente reconhecida
            // como a mesma data já presente.
            var (layout, origem) = MontarComCelula(60, "DOC-001", "25/09/2026");
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    Termino = new DateTime(2026, 9, 25)
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(0, resultado.CelulasDataReplanejadaAtualizadas);
                Assert.False(resultado.HouveAlteracao);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CelulaComTextoInvalidoNaoVazio_NaoLancaFormatException_RegistraWarning()
        {
            var (layout, origem) = MontarComCelula(60, "DOC-001", "TEXTO_INVALIDO_NAO_VAZIO");
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");
            var logger = new CapturingLogger();

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    Termino = new DateTime(2026, 9, 25)
                };

                PlanilhaAtualizacaoResultado resultado = null;
                var exception = Record.Exception(() =>
                    resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item }, logger));

                Assert.Null(exception); // nunca FormatException, mesmo com texto não reconhecido na célula.
                Assert.True(resultado.Sucesso);
                Assert.Contains(logger.Entradas, e => e.Message.Contains("não reconhecido"));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }
    }
}
