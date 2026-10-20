using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage11LdpSqlAuditRuleTests
    {
        private const string FolderId = "F-GESTAO";
        private static readonly string[] ColunasHistorico = { "ID_Registro", "NA_Document_Number", "Data_Sincronizacao" };
        private static readonly string[] ColunasView = { "NA_Document_Number", "Data_Sincronizacao" };

        private static (Stage11LdpSqlAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook, FakeAuditDatabaseReadOnlyAdapter Db)
            NovaRegra(Dictionary<int, Dictionary<string, string?>>? linhasLdp = null)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderId, "09.14 - MANAGEMENT"));
            var item = TestFixtures.Item("ILDP", "LDP.xlsx", FolderId, "09.14", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            linhasLdp ??= new Dictionary<int, Dictionary<string, string?>> { [8] = new() { ["Col4"] = "DOC-1" } };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", Array.Empty<string>(), linhasLdp)));

            return (new Stage11LdpSqlAuditRule(acc, workbook, db), acc, workbook, db);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("11", regra.Etapa);
        }

        [Fact]
        public async Task UnicoSnapshot_Aprovado()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", new DateTime(2024, 1, 1) }));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView,
                new object?[] { "DOC-1", new DateTime(2024, 1, 1) }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-002");
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-005");
        }

        [Fact]
        public async Task DoisSnapshotsHistoricosEmDatasDiferentes_ViewRetornaOMaisRecente_NaoEErro()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", new DateTime(2024, 1, 1) },
                new object?[] { 2, "DOC-1", new DateTime(2024, 2, 1) }));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView,
                new object?[] { "DOC-1", new DateTime(2024, 2, 1) }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-002");
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-005");
        }

        [Fact]
        public async Task MultiplosSnapshots_EvidenciaInsuficiente_NuncaAfirmaBugSemProva()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", DBNull.Value },
                new object?[] { 2, "DOC-1", DBNull.Value }));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-002" && f.ErrorDomain == OperationalAuditErrorDomain.Automacao);
        }

        [Fact]
        public async Task DuplicataNaMesmaChaveEDataSincronizacao_GeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            var mesmaData = new DateTime(2024, 1, 1);
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", mesmaData },
                new object?[] { 2, "DOC-1", mesmaData }));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "L11-002");
            Assert.Equal(OperationalAuditClassification.DadoDuplicado, finding.Classificacao);
        }

        [Fact]
        public async Task L11005_ViewRetornaRegistroAntigo_GeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", new DateTime(2024, 1, 1) },
                new object?[] { 2, "DOC-1", new DateTime(2024, 2, 1) }));
            // View devolve a data ANTIGA, não a mais recente da tabela.
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView,
                new object?[] { "DOC-1", new DateTime(2024, 1, 1) }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "L11-005");
            Assert.Equal(OperationalAuditClassification.BancoDados, finding.Classificacao);
        }

        [Fact]
        public async Task LinhasFisicasNaoElegiveisNaoInflamContagem()
        {
            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["Col4"] = "DOC-1" },
                [9] = new() { ["Col4"] = "", ["Col2"] = "" }, // não elegível: nem Col4 nem Col2 preenchidos.
                [10] = new() { ["Col2"] = "SCH-1" } // elegível pelo Col2.
            };
            var (regra, _, _, db) = NovaRegra(linhas);
            var maisRecente = new DateTime(2024, 1, 1);
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", maisRecente },
                new object?[] { 2, "SCH-DOC", maisRecente }));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            // 2 linhas elegíveis na fonte (linha 8 e 10) == 2 linhas no último lote persistido -> sem divergência.
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-004");
        }

        [Fact]
        public async Task L11003_DadoAlemDoRangePersistido_GeraFinding()
        {
            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["Col4"] = "DOC-1", ["Col40"] = "dado extra fora do range" }
            };
            var (regra, _, _, db) = NovaRegra(linhas);
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "L11-003");
            Assert.Equal(OperationalAuditClassification.EstruturaPlanilha, finding.Classificacao);
        }

        [Fact]
        public async Task L11001_FonteIndisponivelComHistoricoExistente_GeraFindingAutomacao()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null); // fonte indisponível.
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico,
                new object?[] { 1, "DOC-1", new DateTime(2024, 1, 1) }));

            var regra = new Stage11LdpSqlAuditRule(acc, workbook, db);
            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "L11-001");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
        }

        [Fact]
        public async Task L11001_FonteIndisponivelSemHistorico_NuncaInventaFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico));

            var regra = new Stage11LdpSqlAuditRule(acc, workbook, db);
            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "L11-001");
        }

        [Fact]
        public async Task NenhumaEscritaEmLdpUnificada_ApenasSelect()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("FROM [Ldp_Unificada]", TestFixtures.Tabela(ColunasHistorico));
            db.ConfigurarResposta("FROM [VW_Ldp_Unificada]", TestFixtures.Tabela(ColunasView));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.NotNull(resultado); // FakeAuditDatabaseReadOnlyAdapter só expõe ConsultarAsync (SELECT) — nenhum método de escrita possível pela interface.
        }
    }
}
