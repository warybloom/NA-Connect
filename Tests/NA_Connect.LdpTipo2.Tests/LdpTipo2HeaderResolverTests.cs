using System.Collections.Generic;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.LdpTipo2.Tests.TestHelpers;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// Cobre a detecção dinâmica da linha de cabeçalho (nunca linha 7 fixa) e a classificação de blocos
    /// de gate por assinatura de cabeçalhos vizinhos (dados x aplicabilidade) — itens 1 e 6 do plano de
    /// finalização de LDP Tipo 2 / Cronograma Tipo 2.
    /// </summary>
    public class LdpTipo2HeaderResolverTests
    {
        [Fact]
        public void ResolverLayout_ComCabecalhoNaLinha7_LocalizaLinhaEBlocosCorretamente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            Assert.Equal(7, resolvido.LinhaCabecalho);
            Assert.Equal(8, resolvido.PrimeiraLinhaDocumento);
        }

        [Fact]
        public void ResolverLayout_ComCabecalhoForaDaLinha7_LocalizaDinamicamente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 12);

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            Assert.Equal(12, resolvido.LinhaCabecalho);
            Assert.Equal(13, resolvido.PrimeiraLinhaDocumento);
        }

        [Fact]
        public void ResolverLayout_ComBlocosDeGateReordenadosEAplicabilidadeAntes_ResolveIgual()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(
                pkg,
                linhaCabecalho: 9,
                ordemGates: new byte[] { 90, 30, 60 },
                aplicabilidadeAntesDoBlocoDeGates: true);

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            Assert.Equal(9, resolvido.LinhaCabecalho);
            Assert.Equal(30, resolvido.Gate30.Percentual);
            Assert.Equal(60, resolvido.Gate60.Percentual);
            Assert.Equal(90, resolvido.Gate90.Percentual);
        }

        [Fact]
        public void ResolverLayout_ClassificaBlocoDeDadosEIndicadorDeAplicabilidadeSeparadamente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7);

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            // O bloco de DADOS ("Fase 30%" seguido de Document Status/Situation/...) e o indicador de
            // APLICABILIDADE ("Fase 30%" consecutivo a "Fase 60%"/"Fase 90%") são ocorrências físicas
            // DIFERENTES do mesmo texto de cabeçalho — nunca a mesma coluna.
            Assert.NotEqual(layout.Gates[30].ColunaFase, layout.ColunaAplicabilidade[30]);
            Assert.Equal(layout.Gates[30].ColunaFase, resolvido.Gate30.ColunaFaseManual);
            Assert.Equal(layout.ColunaAplicabilidade[30], resolvido.Gate30.ColunaIndicadorAplicabilidade);

            Assert.NotEqual(layout.Gates[60].ColunaFase, layout.ColunaAplicabilidade[60]);
            Assert.Equal(layout.Gates[60].ColunaFase, resolvido.Gate60.ColunaFaseManual);
            Assert.Equal(layout.ColunaAplicabilidade[60], resolvido.Gate60.ColunaIndicadorAplicabilidade);

            Assert.NotEqual(layout.Gates[90].ColunaFase, layout.ColunaAplicabilidade[90]);
            Assert.Equal(layout.Gates[90].ColunaFase, resolvido.Gate90.ColunaFaseManual);
            Assert.Equal(layout.ColunaAplicabilidade[90], resolvido.Gate90.ColunaIndicadorAplicabilidade);
        }

        [Fact]
        public void ResolverLayout_ComCamposDoBlocoDeGateEmOrdemEmbaralhada_ResolveIgual()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(
                pkg,
                linhaCabecalho: 7,
                ordemCamposDentroDoBlocoGate: new[]
                {
                    LdpTipo2WorkbookHelper.CampoBlocoGate.Situation,
                    LdpTipo2WorkbookHelper.CampoBlocoGate.Status,
                    LdpTipo2WorkbookHelper.CampoBlocoGate.Replanned,
                    LdpTipo2WorkbookHelper.CampoBlocoGate.Baseline
                });

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            // Mesmo com Situation/Status/Replanned/Baseline fisicamente fora da ordem "canônica", cada
            // coluna deve ser resolvida corretamente para seu papel semântico real (item 5 do checkpoint).
            Assert.Equal(layout.Gates[30].ColunaDocumentStatus, resolvido.Gate30.ColunaDocumentStatus);
            Assert.Equal(layout.Gates[30].ColunaSituation, resolvido.Gate30.ColunaSituation);
            Assert.Equal(layout.Gates[30].ColunaPlannedBaseline, resolvido.Gate30.ColunaPlannedBaseline);
            Assert.Equal(layout.Gates[30].ColunaReplannedDate, resolvido.Gate30.ColunaReplannedDate);

            Assert.Equal(layout.Gates[60].ColunaDocumentStatus, resolvido.Gate60.ColunaDocumentStatus);
            Assert.Equal(layout.Gates[90].ColunaReplannedDate, resolvido.Gate90.ColunaReplannedDate);
        }

        [Fact]
        public void ResolverLayout_ComTrioDeAplicabilidadeEmOrdemEmbaralhada_ResolveIgual()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(
                pkg,
                linhaCabecalho: 7,
                ordemAplicabilidade: new byte[] { 60, 30, 90 });

            var avisos = new List<string>();
            var resolvido = LdpTipo2HeaderResolver.ResolverLayout(layout.Worksheet, NullLogger.Instance, "teste.xlsx", avisos);

            Assert.Equal(layout.ColunaAplicabilidade[30], resolvido.Gate30.ColunaIndicadorAplicabilidade);
            Assert.Equal(layout.ColunaAplicabilidade[60], resolvido.Gate60.ColunaIndicadorAplicabilidade);
            Assert.Equal(layout.ColunaAplicabilidade[90], resolvido.Gate90.ColunaIndicadorAplicabilidade);
        }

        [Fact]
        public void LocalizarLinhaCabecalho_SemClientDocumentNumber_FalhaControladamente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var layout = LdpTipo2WorkbookHelper.MontarAbaLdp(pkg, linhaCabecalho: 7, incluirClientDocumentNumber: false);

            var ex = Assert.Throws<LdpTipo2LayoutException>(() => LdpTipo2HeaderResolver.LocalizarLinhaCabecalho(layout.Worksheet));
            Assert.Contains("Linha de cabeçalho não localizada", ex.Message);
        }

        [Fact]
        public void LocalizarLinhaCabecalho_PlanilhaVazia_FalhaControladamente()
        {
            using var pkg = LdpTipo2WorkbookHelper.NovoPacote();
            var ws = pkg.Workbook.Worksheets.Add("LDP");
            ws.Cells[1, 1].Value = "irrelevante";

            var ex = Assert.Throws<LdpTipo2LayoutException>(() => LdpTipo2HeaderResolver.LocalizarLinhaCabecalho(ws));
            Assert.Contains("Linha de cabeçalho não localizada", ex.Message);
        }
    }
}
