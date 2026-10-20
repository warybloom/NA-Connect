using APS.WebApp.Services;
using OfficeOpenXml;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Writeback GRD -> LDP da Etapa 04 (parte pura, sem ACC): cabeçalhos tolerantes às grafias históricas
    /// do template ("TRANMISTAL NUMBER", "EMISSION DATA", "ISSUED POUPURSE"), contagem POR DOCUMENTO
    /// alterado (qualquer um dos 3 campos) e filtro da linha de cabeçalho "Codigo N&A" da lista da GRD.
    /// </summary>
    public class GrdLdpWritebackTests
    {
        private const int LinhaDoc = 8;
        private static readonly DateTime DataGrd = new DateTime(2026, 9, 10);

        private static ExcelPackage CriarLdp(string cabecalhoTransmittal = "TRANMISTAL NUMBER",
            string cabecalhoEmission = "EMISSION DATA", string cabecalhoPurpose = "ISSUED POUPURSE")
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("LDP");
            ws.Cells[7, 2].Value = "DOCUMENT TITLE";
            ws.Cells[7, 4].Value = "N&A DOCUMENT NUMBER";
            ws.Cells[7, 10].Value = cabecalhoEmission;
            ws.Cells[7, 11].Value = cabecalhoTransmittal;
            ws.Cells[7, 12].Value = cabecalhoPurpose;

            ws.Cells[LinhaDoc, 4].Value = "NA-001-DOC";
            ws.Cells[LinhaDoc, 10].Value = DataGrd;
            ws.Cells[LinhaDoc, 11].Value = "GRD-001";
            ws.Cells[LinhaDoc, 12].Value = "PARA APROVAÇÃO";
            return package;
        }

        private static GrdInfo InfoIgualALdp() => new GrdInfo
        {
            TransmittalNumber = "GRD-001",
            EmissionDate = DataGrd,
            IssuedPurpose = "PARA APROVAÇÃO"
        };

        [Theory]
        [InlineData("TRANMISTAL NUMBER")]
        [InlineData("TRANSMITTAL NUMBER")]
        [InlineData("Tranmistal Number")]
        public void Transmittal_ReconhecidoNasDuasGrafias(string cabecalho)
        {
            using var p = CriarLdp(cabecalhoTransmittal: cabecalho);
            var colunas = GrdLdpWriteback.ResolverColunas(p.Workbook.Worksheets[0], 7);
            Assert.Equal(11, colunas.Transmittal);
        }

        [Theory]
        [InlineData("EMISSION DATA")]
        [InlineData("EMISSION DATE")]
        [InlineData("DATA DE EMISSÃO")]
        public void EmissionData_Reconhecida(string cabecalho)
        {
            using var p = CriarLdp(cabecalhoEmission: cabecalho);
            var colunas = GrdLdpWriteback.ResolverColunas(p.Workbook.Worksheets[0], 7);
            Assert.Equal(10, colunas.EmissionDate);
        }

        [Theory]
        [InlineData("ISSUED POUPURSE")]
        [InlineData("ISSUED PURPOSE")]
        public void IssuedPoupurse_Reconhecido(string cabecalho)
        {
            using var p = CriarLdp(cabecalhoPurpose: cabecalho);
            var colunas = GrdLdpWriteback.ResolverColunas(p.Workbook.Worksheets[0], 7);
            Assert.Equal(12, colunas.IssuedPurpose);
        }

        [Fact]
        public void Chave_NaDocumentNumber_SemFallback()
        {
            using var p = CriarLdp();
            var colunas = GrdLdpWriteback.ResolverColunas(p.Workbook.Worksheets[0], 7);
            Assert.Equal(4, colunas.Key);
            Assert.False(colunas.KeyPorFallback);
        }

        [Fact]
        public void NadaMudou_DocumentoNaoAlterado()
        {
            using var p = CriarLdp();
            var ws = p.Workbook.Worksheets[0];
            var colunas = GrdLdpWriteback.ResolverColunas(ws, 7);
            Assert.False(GrdLdpWriteback.AplicarGrdNaLinha(ws, LinhaDoc, colunas, InfoIgualALdp()));
        }

        [Fact]
        public void MudancaSoNoTransmittal_Altera()
        {
            using var p = CriarLdp();
            var ws = p.Workbook.Worksheets[0];
            var colunas = GrdLdpWriteback.ResolverColunas(ws, 7);
            var info = InfoIgualALdp();
            info.TransmittalNumber = "GRD-002";

            Assert.True(GrdLdpWriteback.AplicarGrdNaLinha(ws, LinhaDoc, colunas, info));
            Assert.Equal("GRD-002", ws.Cells[LinhaDoc, 11].Text);
        }

        [Fact]
        public void MudancaSoNaData_Altera()
        {
            using var p = CriarLdp();
            var ws = p.Workbook.Worksheets[0];
            var colunas = GrdLdpWriteback.ResolverColunas(ws, 7);
            var info = InfoIgualALdp();
            info.EmissionDate = DataGrd.AddDays(5);

            Assert.True(GrdLdpWriteback.AplicarGrdNaLinha(ws, LinhaDoc, colunas, info));
            Assert.Equal(DataGrd.AddDays(5), (DateTime)ws.Cells[LinhaDoc, 10].Value);
        }

        [Fact]
        public void MudancaSoNoPurpose_Altera()
        {
            using var p = CriarLdp();
            var ws = p.Workbook.Worksheets[0];
            var colunas = GrdLdpWriteback.ResolverColunas(ws, 7);
            var info = InfoIgualALdp();
            info.IssuedPurpose = "PARA CONSTRUÇÃO";

            Assert.True(GrdLdpWriteback.AplicarGrdNaLinha(ws, LinhaDoc, colunas, info));
            Assert.Equal("PARA CONSTRUÇÃO", ws.Cells[LinhaDoc, 12].Text);
        }

        [Fact]
        public void Resumo_ContaPorDocumento_EMatchESemMatch()
        {
            using var p = CriarLdp();
            var ws = p.Workbook.Worksheets[0];
            var colunas = GrdLdpWriteback.ResolverColunas(ws, 7);
            var info = InfoIgualALdp();
            info.TransmittalNumber = "GRD-009";
            info.IssuedPurpose = "PARA CONSTRUÇÃO"; // 2 campos no mesmo documento -> conta 1
            var dados = new Dictionary<string, GrdInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["na-001-doc"] = info,
                ["NA-999-INEXISTENTE"] = InfoIgualALdp()
            };

            var resumo = GrdLdpWriteback.AplicarConsolidado(ws, colunas, dados);

            Assert.Equal(1, resumo.MatchesLdp);
            Assert.Equal(1, resumo.DocumentosAlterados);
            Assert.Equal(1, resumo.SemMatch);
        }

        [Theory]
        [InlineData("Codigo N&A")]
        [InlineData("Código N&A")]
        [InlineData("CÓDIGO  N&A")]
        [InlineData("N&A DOCUMENT NUMBER")]
        [InlineData("Código N&A / N&A Document Number")]
        public void CabecalhoDaListaGrd_Ignorado(string valor)
        {
            Assert.True(GrdLdpWriteback.EhCabecalhoDaListaGrd(valor));
        }

        [Theory]
        [InlineData("BML011-016-M-DE-0001")]
        [InlineData("NA-001-DOC")]
        public void DocumentoReal_NaoEhCabecalho(string valor)
        {
            Assert.False(GrdLdpWriteback.EhCabecalhoDaListaGrd(valor));
        }
    }
}
