using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using APS.WebApp.Services.Reports;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests
{
    /// <summary>
    /// CDE / Lista de Documentos Tipo 2 deve mostrar exatamente o que o gerador REAL do Espelho Tipo 2
    /// (LdpTipo2EspelhoService.Preencher) deixa VISÍVEL: roda o gerador numa planilha em memória e compara,
    /// coluna a coluna, com a montagem da lista (mesma escolha de gate atual, mesmos status/aplicabilidade).
    /// </summary>
    public class CdeDocumentListEspelhoEquivalenciaTests
    {
        private static LdpTipo2Gate Gate(byte pct, bool ativo, string status, DateTime? baseline) =>
            new() { GatePercentual = pct, GateAtivo = ativo, DocumentStatus = status, PlannedBaseline = baseline };

        private static ExcelWorksheet GerarEspelho(ExcelPackage package, List<LdpTipo2Documento> docs)
        {
            var ws = package.Workbook.Worksheets.Add("LDP");
            for (int c = 1; c <= 23; c++) { ws.Cells[7, c].Value = $"H{c}"; ws.Cells[8, c].Value = "modelo"; }
            LdpTipo2EspelhoService.Preencher(ws, docs);
            return ws;
        }

        public static IEnumerable<object[]> Cenarios()
        {
            var b30 = new DateTime(2026, 1, 10); var b60 = new DateTime(2026, 3, 10); var b90 = new DateTime(2026, 6, 10);
            yield return new object[] { new List<LdpTipo2Gate> { Gate(30, true, "APROVADO", b30), Gate(60, true, "EM ELABORAÇÃO", b60), Gate(90, true, "NÃO INICIADO", b90) } };
            yield return new object[] { new List<LdpTipo2Gate> { Gate(30, true, "APPROVED", b30), Gate(60, false, null!, b60), Gate(90, true, "APPROVED", b90) } };
            yield return new object[] { new List<LdpTipo2Gate> { Gate(30, false, null!, null), Gate(60, true, "CLIENT REVIEW", b60) } };
            yield return new object[] { new List<LdpTipo2Gate>() };
        }

        [Theory, MemberData(nameof(Cenarios))]
        public void ColunasVisiveisDoGerador_IguaisALista(List<LdpTipo2Gate> gates)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var doc = new LdpTipo2Documento
            {
                LinhaExcel = 8, ScheduleId = "1.2.3", Disciplina = "MECÂNICA", NumeroDocumentoNa = "NA-001", NumeroDocumentoCliente = "CLI-001",
                TituloDocumento = "Título", Formato = "A1", NumeroPaginas = 3, Revision = "B", DocumentStatus = "EMITIDO", IssuedPurpose = "PC",
                Gates = gates,
            };
            var ws = GerarEspelho(package, new List<LdpTipo2Documento> { doc });

            var lista = new Dictionary<string, object?>();
            CdeDocumentListRepository.PreencherGatesTipo2(lista, gates);

            Assert.Equal(ws.Cells[8, 9].Value, lista["gatePlannedBaseline"]);
            for (int i = 0; i < 3; i++)
            {
                int pct = (i + 1) * 30;
                Assert.Equal(ws.Cells[8, 18 + i].Value, lista[$"gate{pct}Status"]);
                Assert.Equal(ws.Cells[8, 21 + i].Value, lista[$"gate{pct}Aplica"]);
            }
        }

        [Fact]
        public void ColunasDaLista_SaoExatamenteAsVisiveisDoGerador()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = GerarEspelho(package, new List<LdpTipo2Documento> { new() { LinhaExcel = 8, NumeroDocumentoNa = "NA-1" } });

            var visiveis = Enumerable.Range(1, 23).Where(c => !ws.Column(c).Hidden).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 18, 19, 20, 21, 22, 23 }, visiveis);
            Assert.Equal(visiveis.Count, CdeDocumentListDefinitions.Tipo2.Count);
        }

        [Fact]
        public void Tipo1_TemAs15ColunasMantidasPeloEspelhoTipo1()
        {
            // MirrorLdpControlToProjectAsync apaga da coluna 16 em diante → 15 colunas, na ordem da LDP Tipo 1.
            Assert.Equal(15, CdeDocumentListDefinitions.Tipo1.Count);
            Assert.Equal(new[] { "item", "scheduleId", "discipline", "naDocumentNumber", "clientDocumentNumber", "documentTitle", "format",
                "pages", "plannedDateBaseline", "replannedDate", "emissionDate", "transmittalNumber", "issuedPurpose", "revision", "documentStatus" },
                CdeDocumentListDefinitions.Tipo1.Select(c => c.Key));
        }

        [Theory]
        [InlineData(1, "documentStatus", true, "Document_Status DESC, Sequencia ASC")]
        [InlineData(2, "documentStatus", true, "Sequencia ASC")]   // não é coluna visível do Tipo 2
        [InlineData(1, "Discipline; DROP TABLE x", false, "Sequencia ASC")]
        [InlineData(2, "discipline", false, "Discipline ASC, Sequencia ASC")]
        [InlineData(2, "item", true, "Sequencia DESC")]
        public void OrderBy_SomenteWhitelistDoTipo(int tipo, string sort, bool desc, string esperado) =>
            Assert.Equal(esperado, CdeDocumentListDefinitions.OrderBy(tipo, sort, desc));
    }
}
