using System;
using System.Collections.Generic;
using System.IO;
using APS.WebApp.Services.CronogramaTipo2;
using NA_Connect.LdpTipo2.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Checkpoint "Planned Physical / Physical Completed" da LDP Tipo 2 (seção 11 do pedido): cobre os
    /// três gates, 0%/100%, NULL preservando o valor anterior, valor igual não gerando alteração, valor
    /// diferente gerando alteração, e que só as colunas do gate correto são tocadas — as demais (outros
    /// gates, Fase, Document Status, Situation, datas) permanecem intocadas. Gate inativo não escrever é
    /// coberto estruturalmente em CronogramaTipo2LdpSyncMatchingTests (CronogramaParaGateInativo nunca
    /// chega a AtualizacoesParaPublicar — o atualizador de planilha não tem noção de "gate ativo").
    /// </summary>
    public class CronogramaTipo2LdpPlanilhaAtualizadorPhysicalTests
    {
        private static string SalvarWorkbookTemporario(ExcelPackage pkg)
        {
            string caminho = Path.Combine(Path.GetTempPath(), $"LDP_T2_TESTE_{Guid.NewGuid()}.xlsx");
            File.WriteAllBytes(caminho, pkg.GetAsByteArray());
            return caminho;
        }

        private static (LdpTipo2WorkbookLayout Layout, string Origem) MontarComTresGates(string na, byte[] gatesAtivos)
        {
            var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            var aplicabilidade = new Dictionary<byte, string>();
            foreach (var g in gatesAtivos) aplicabilidade[g] = "X";
            LdpTipo2WorkbookHelper.EscreverDocumento(layout, 8, na, "CLI-001", "Título", aplicabilidade);

            string origem = SalvarWorkbookTemporario(pkg);
            pkg.Dispose();
            return (layout, origem);
        }

        private static decimal? LerFracaoBruta(LdpTipo2WorkbookLayout layout, byte gate, string destino, bool completed)
        {
            using var pkgDestino = new ExcelPackage(new FileInfo(destino));
            var ws = pkgDestino.Workbook.Worksheets["LDP"];
            int col = completed ? layout.Gates[gate].ColunaPhysicalCompleted : layout.Gates[gate].ColunaPlannedPhysical;
            return ws.Cells[8, col].GetValue<decimal?>();
        }

        [Theory]
        [InlineData((byte)30)]
        [InlineData((byte)60)]
        [InlineData((byte)90)]
        public void SoAtualizaOGateCorrespondente_OutrosGatesPermanecemIntocados(byte gate)
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 30, 60, 90 });
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = gate,
                    PlannedPhysical = 80m,
                    PercentRealizado = 60m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1, resultado.CelulasPlannedPhysicalAtualizadas);
                Assert.Equal(1, resultado.CelulasRealizadoAtualizadas);
                Assert.Equal(0.80m, LerFracaoBruta(layout, gate, destino, completed: false));
                Assert.Equal(0.60m, LerFracaoBruta(layout, gate, destino, completed: true));

                foreach (byte outro in new byte[] { 30, 60, 90 })
                {
                    if (outro == gate) continue;
                    Assert.Null(LerFracaoBruta(layout, outro, destino, completed: false));
                    Assert.Null(LerFracaoBruta(layout, outro, destino, completed: true));
                }

                switch (gate)
                {
                    case 30: Assert.Equal(1, resultado.Gate30FisicoAtualizado); Assert.Equal(0, resultado.Gate60FisicoAtualizado + resultado.Gate90FisicoAtualizado); break;
                    case 60: Assert.Equal(1, resultado.Gate60FisicoAtualizado); Assert.Equal(0, resultado.Gate30FisicoAtualizado + resultado.Gate90FisicoAtualizado); break;
                    case 90: Assert.Equal(1, resultado.Gate90FisicoAtualizado); Assert.Equal(0, resultado.Gate30FisicoAtualizado + resultado.Gate60FisicoAtualizado); break;
                }
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void ZeroPorCento_EhEscritoComoZeroExplicito()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = 0m,
                    PercentRealizado = 0m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1, resultado.CelulasPlannedPhysicalAtualizadas);
                Assert.Equal(1, resultado.CelulasRealizadoAtualizadas);
                Assert.Equal(0m, LerFracaoBruta(layout, 60, destino, completed: false));
                Assert.Equal(0m, LerFracaoBruta(layout, 60, destino, completed: true));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CemPorCento_EhEscritoCorretamente()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = 100m,
                    PercentRealizado = 100m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1m, LerFracaoBruta(layout, 60, destino, completed: false));
                Assert.Equal(1m, LerFracaoBruta(layout, 60, destino, completed: true));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void CronogramaNulo_PreservaValorAnteriorDaLdp()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });

            // Pré-popula a LDP de origem com um valor manual já existente (85%) antes de rodar o updater —
            // simula uma célula preenchida manualmente que o Cronograma não deve sobrescrever com NULL.
            using (var pkgPre = new ExcelPackage(new FileInfo(origem)))
            {
                var wsPre = pkgPre.Workbook.Worksheets["LDP"];
                wsPre.Cells[8, layout.Gates[60].ColunaPlannedPhysical].Value = 0.85m;
                wsPre.Cells[8, layout.Gates[60].ColunaPhysicalCompleted].Value = 0.85m;
                pkgPre.Save();
            }

            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = null,
                    PercentRealizado = null
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.False(resultado.HouveAlteracao);
                Assert.Equal(0, resultado.CelulasPlannedPhysicalAtualizadas);
                Assert.Equal(0, resultado.CelulasRealizadoAtualizadas);
                Assert.Equal(2, resultado.ValoresSemOrigemPhysical); // Planned + Completed, ambos sem origem
                Assert.Equal(0.85m, LerFracaoBruta(layout, 60, destino, completed: false));
                Assert.Equal(0.85m, LerFracaoBruta(layout, 60, destino, completed: true));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void ValorJaIgual_NaoGeraAlteracao()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });

            using (var pkgPre = new ExcelPackage(new FileInfo(origem)))
            {
                var wsPre = pkgPre.Workbook.Worksheets["LDP"];
                wsPre.Cells[8, layout.Gates[60].ColunaPlannedPhysical].Value = 0.80m;
                wsPre.Cells[8, layout.Gates[60].ColunaPhysicalCompleted].Value = 0.60m;
                pkgPre.Save();
            }

            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = 80m,
                    PercentRealizado = 60m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.False(resultado.HouveAlteracao);
                Assert.Equal(0, resultado.CelulasPlannedPhysicalAtualizadas);
                Assert.Equal(0, resultado.CelulasRealizadoAtualizadas);
                Assert.Equal(2, resultado.ValoresIguaisPhysical);
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void ValorDiferente_GeraAlteracao()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });

            using (var pkgPre = new ExcelPackage(new FileInfo(origem)))
            {
                var wsPre = pkgPre.Workbook.Worksheets["LDP"];
                wsPre.Cells[8, layout.Gates[60].ColunaPlannedPhysical].Value = 0.40m;
                pkgPre.Save();
            }

            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = 75m
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.HouveAlteracao);
                Assert.Equal(1, resultado.CelulasPlannedPhysicalAtualizadas);
                Assert.Equal(0.75m, LerFracaoBruta(layout, 60, destino, completed: false));
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        /// <summary>
        /// HOTFIX 3 (checkpoint "Escrita %PHYSICAL COMPLETED por fase"): casos exatos pedidos — cada
        /// gate recebe SEU PRÓPRIO Percent_Realizado, nunca o mesmo valor replicado nos três, e o gate
        /// errado nunca é tocado. Reusa exatamente os mesmos helpers dos testes acima (nenhuma
        /// infraestrutura nova).
        /// </summary>
        [Theory]
        [InlineData((byte)30, 10)]
        [InlineData((byte)60, 35)]
        [InlineData((byte)90, 80)]
        public void PercentRealizado_EscreveSomenteNoGateDaFase(byte gate, decimal percentRealizado)
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 30, 60, 90 });
            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = gate,
                    PercentRealizado = percentRealizado
                };

                var resultado = CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                Assert.True(resultado.Sucesso);
                Assert.Equal(1, resultado.CelulasRealizadoAtualizadas);
                Assert.Equal(percentRealizado / 100m, LerFracaoBruta(layout, gate, destino, completed: true));

                foreach (byte outro in new byte[] { 30, 60, 90 })
                {
                    if (outro == gate) continue;
                    Assert.Null(LerFracaoBruta(layout, outro, destino, completed: true));
                }
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }

        [Fact]
        public void FormatoPercentualDaCelula_EhPreservadoAposEscrita()
        {
            var (layout, origem) = MontarComTresGates("DOC-001", new byte[] { 60 });

            // Simula a formatação real da LDP: célula já formatada como percentual antes de qualquer escrita.
            using (var pkgPre = new ExcelPackage(new FileInfo(origem)))
            {
                var wsPre = pkgPre.Workbook.Worksheets["LDP"];
                wsPre.Cells[8, layout.Gates[60].ColunaPlannedPhysical].Style.Numberformat.Format = "0%";
                pkgPre.Save();
            }

            string destino = Path.Combine(Path.GetTempPath(), $"LDP_T2_DESTINO_{Guid.NewGuid()}.xlsx");

            try
            {
                var item = new CronogramaTipo2LdpPlanilhaAtualizacaoItem
                {
                    NumeroDocumentoNaNormalizadoLdp = "DOC-001",
                    GatePercentual = 60,
                    PlannedPhysical = 42m
                };

                CronogramaTipo2LdpPlanilhaAtualizador.AtualizarCopia(origem, destino, new[] { item });

                using var pkgDestino = new ExcelPackage(new FileInfo(destino));
                var ws = pkgDestino.Workbook.Worksheets["LDP"];
                var cell = ws.Cells[8, layout.Gates[60].ColunaPlannedPhysical];

                Assert.Equal("0%", cell.Style.Numberformat.Format);
                Assert.Equal(0.42m, cell.GetValue<decimal?>());
            }
            finally
            {
                File.Delete(origem);
                if (File.Exists(destino)) File.Delete(destino);
            }
        }
    }
}
