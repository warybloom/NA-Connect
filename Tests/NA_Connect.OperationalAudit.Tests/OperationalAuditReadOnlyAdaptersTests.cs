using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Testa as implementações REAIS dos adaptadores que não dependem de ACC/SQL/MPP reais para funcionar
    /// (Workbook via EPPlus em memória; Database/Planning/Quality via composição sobre outros fakes/reais
    /// já seguros). Acc/Workflow/Schedule reais (que fariam HTTP/parsing de MPP reais) não são exercitados
    /// aqui — nenhuma chamada de rede real é permitida nesta fase (seção 6/20).
    /// </summary>
    public class OperationalAuditReadOnlyAdaptersTests
    {
        [Fact]
        public void AuditWorkbookReadOnlyAdapter_AbreEExpoeAbasECabecalhos()
        {
            byte[] bytes = ExcelTestBuilder.CriarXlsx("Aba1", new[] { "Col1", "Col2" }, new object?[][] { new object?[] { "V1", "V2" } }, linhaCabecalho: 1);
            using var stream = new MemoryStream(bytes);

            var adapter = new AuditWorkbookReadOnlyAdapter();
            var snapshot = adapter.AbrirSomenteLeitura(stream);

            Assert.Contains("Aba1", snapshot.NomesDasAbas);
            Assert.Equal(new[] { "Col1", "Col2" }, snapshot.Abas["Aba1"].Cabecalhos);
            Assert.Single(snapshot.Abas["Aba1"].Linhas);
        }

        [Fact]
        public void AuditWorkbookReadOnlyAdapter_LinhaTotalmenteVazia_NuncaEntraEmLinhas()
        {
            byte[] bytes = ExcelTestBuilder.CriarXlsx("Aba1", new[] { "Col1" }, new object?[][] { new object?[] { null }, new object?[] { "Real" } }, linhaCabecalho: 1);
            using var stream = new MemoryStream(bytes);

            var adapter = new AuditWorkbookReadOnlyAdapter();
            var snapshot = adapter.AbrirSomenteLeitura(stream);

            Assert.Single(snapshot.Abas["Aba1"].Linhas);
        }

        [Fact]
        public void AuditWorkbookReadOnlyAdapter_DetectaAbaOculta()
        {
            using var package = new OfficeOpenXml.ExcelPackage();
            var wsVisivel = package.Workbook.Worksheets.Add("Visivel"); // EPPlus exige ao menos uma aba visível para salvar.
            wsVisivel.Cells[1, 1].Value = "X";
            var wsOculta = package.Workbook.Worksheets.Add("Oculta");
            wsOculta.Cells[1, 1].Value = "Y";
            wsOculta.Hidden = OfficeOpenXml.eWorkSheetHidden.Hidden;
            using var stream = new MemoryStream(package.GetAsByteArray());

            var adapter = new AuditWorkbookReadOnlyAdapter();
            var snapshot = adapter.AbrirSomenteLeitura(stream);

            Assert.False(snapshot.Abas["Visivel"].Oculta);
            Assert.True(snapshot.Abas["Oculta"].Oculta);
        }

        private sealed class FakeAuditSourceReader : IAuditSourceReader
        {
            public DataTable Resposta { get; set; } = new DataTable();
            public string? UltimoSql { get; private set; }

            public Task<DataTable> ExecutarConsultaAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, System.Threading.CancellationToken ct = default)
            {
                UltimoSql = sql;
                return Task.FromResult(Resposta);
            }
        }

        [Fact]
        public async Task AuditDatabaseReadOnlyAdapter_RepassaParaOIAuditSourceReader()
        {
            var leitor = new FakeAuditSourceReader { Resposta = TestFixtures.Tabela(new[] { "X" }, new object?[] { 1 }) };
            var adapter = new AuditDatabaseReadOnlyAdapter(leitor);

            var resultado = await adapter.ConsultarAsync("SELECT X FROM Y");

            Assert.Equal("SELECT X FROM Y", leitor.UltimoSql);
            Assert.Single(resultado.Rows);
        }

        [Fact]
        public async Task AuditPlanningReadOnlyAdapter_LePrimeiraAba_ViaWorkbookAdapter()
        {
            var workbookFake = new FakeAuditWorkbookReadOnlyAdapter();
            var abaEsperada = TestFixtures.Aba("Sheet1", new[] { "A" }, new Dictionary<int, Dictionary<string, string?>>());
            workbookFake.Enfileirar(TestFixtures.Workbook(abaEsperada));
            var dbFake = new FakeAuditDatabaseReadOnlyAdapter();

            var adapter = new AuditPlanningReadOnlyAdapter(workbookFake, dbFake);
            var resultado = adapter.LerPrimeiraAba(new MemoryStream(), 1);

            Assert.Equal("Sheet1", resultado.Nome);
        }

        [Fact]
        public async Task AuditPlanningReadOnlyAdapter_ObterColunasDestinoSql_ConsultaInformationSchema()
        {
            var workbookFake = new FakeAuditWorkbookReadOnlyAdapter();
            var dbFake = new FakeAuditDatabaseReadOnlyAdapter();
            dbFake.ConfigurarResposta("INFORMATION_SCHEMA.COLUMNS", TestFixtures.Tabela(new[] { "COLUMN_NAME" }, new object?[] { "Col1" }, new object?[] { "Col2" }));

            var adapter = new AuditPlanningReadOnlyAdapter(workbookFake, dbFake);
            var colunas = await adapter.ObterColunasDestinoSqlAsync("tb_WeeklyEngineeringSchedule");

            Assert.Equal(new[] { "Col1", "Col2" }, colunas);
        }

        [Fact]
        public void AuditQualityReadOnlyAdapter_DelegaParaOWorkbookAdapter()
        {
            var workbookFake = new FakeAuditWorkbookReadOnlyAdapter();
            var esperado = TestFixtures.Workbook(TestFixtures.Aba("BASE LANÇAMENTO", new[] { "X" }, new Dictionary<int, Dictionary<string, string?>>()));
            workbookFake.Enfileirar(esperado);

            var adapter = new AuditQualityReadOnlyAdapter(workbookFake);
            var resultado = adapter.LerPastaDeQualidade(new MemoryStream());

            Assert.Same(esperado, resultado);
        }

        [Fact]
        public void AdaptadoresReais_ExistemEImplementamAsInterfaces()
        {
            // Prova estrutural de que as implementações reais de ACC/Workflow/Schedule existem e satisfazem
            // seus contratos — sem exercitar nenhuma chamada de rede/MPP real (proibido nesta fase).
            Assert.True(typeof(IAuditAccReadOnlyAdapter).IsAssignableFrom(typeof(AuditAccReadOnlyAdapter)));
            Assert.True(typeof(IAuditWorkflowReadOnlyAdapter).IsAssignableFrom(typeof(AuditWorkflowReadOnlyAdapter)));
            Assert.True(typeof(IAuditScheduleReadOnlyAdapter).IsAssignableFrom(typeof(AuditScheduleReadOnlyAdapter)));
        }
    }
}
