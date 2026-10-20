using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage09ScheduleAuditRuleTests
    {
        private const string FolderCronograma = "F-CRN";

        private static (Stage09ScheduleAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditScheduleReadOnlyAdapter Schedule, FakeAuditWorkbookReadOnlyAdapter Workbook, FakeAuditDatabaseReadOnlyAdapter Db)
            NovaRegra()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var schedule = new FakeAuditScheduleReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            db.ConfigurarResposta("Cronograma_Projetos", new System.Data.DataTable());
            return (new Stage09ScheduleAuditRule(acc, schedule, workbook, db), acc, schedule, workbook, db);
        }

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _, _) = NovaRegra();
            Assert.Equal("9", regra.Etapa);
        }

        [Fact]
        public async Task CRN001_PastaAusente_GeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            acc.ConfigurarPasta("01.03", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CRN-001");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task CRN002_ArquivoMppAusente_GeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            acc.ConfigurarPasta("01.03", TestFixtures.Pasta(FolderCronograma, "01.03"));
            acc.ConfigurarListagem(FolderCronograma, TestFixtures.Listagem(TestFixtures.Item("I1", "outro.pdf", FolderCronograma, "01.03")));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "CRN-002");
        }

        private static void ConfigurarMppBasico(FakeAuditAccReadOnlyAdapter acc, FakeAuditScheduleReadOnlyAdapter schedule, AuditScheduleSnapshot? snapshot = null)
        {
            acc.ConfigurarPasta("01.03", TestFixtures.Pasta(FolderCronograma, "01.03"));
            acc.ConfigurarListagem(FolderCronograma, TestFixtures.Listagem(TestFixtures.Item("I1", "cronograma.mpp", FolderCronograma, "01.03")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });
            schedule.Resultado = snapshot ?? new AuditScheduleSnapshot
            {
                Tarefas = new[] { new AuditScheduleTaskRef { Uid = "1", Nome = "Tarefa 1", CampoRealizadoResolvido = "Number1", ValorRealizado = 50, CampoPrevistoResolvido = "Number2", ValorPrevisto = 60 } },
                RealizadoEPrevistoColidem = false
            };
        }

        [Fact]
        public async Task CRN003E004_CamposNaoResolvidos_GeramFindings()
        {
            var (regra, acc, schedule, _, _) = NovaRegra();
            ConfigurarMppBasico(acc, schedule, new AuditScheduleSnapshot
            {
                Tarefas = new[] { new AuditScheduleTaskRef { Uid = "1", Nome = "T1", CampoRealizadoResolvido = null, CampoPrevistoResolvido = null } }
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "CRN-003");
            Assert.Single(resultado.Findings, f => f.RegraId == "CRN-004");
        }

        [Fact]
        public async Task CRN005_RealizadoEPrevistoColidem_GeraFinding()
        {
            var (regra, acc, schedule, _, _) = NovaRegra();
            ConfigurarMppBasico(acc, schedule, new AuditScheduleSnapshot
            {
                Tarefas = new[] { new AuditScheduleTaskRef { Uid = "1", Nome = "T1", CampoRealizadoResolvido = "Number1", CampoPrevistoResolvido = "Number1" } },
                RealizadoEPrevistoColidem = true
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CRN-005");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task CRN011_PercentualInvalido_GeraFinding()
        {
            var (regra, acc, schedule, _, _) = NovaRegra();
            ConfigurarMppBasico(acc, schedule, new AuditScheduleSnapshot
            {
                Tarefas = new[] { new AuditScheduleTaskRef { Uid = "1", Nome = "T1", CampoRealizadoResolvido = "Number1", ValorRealizado = 150, CampoPrevistoResolvido = "Number2", ValorPrevisto = 50 } }
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "CRN-011");
        }

        [Fact]
        public async Task CRN008_ZeramentoEmMassa_GeraFinding()
        {
            var (regra, acc, schedule, _, _) = NovaRegra();
            var tarefas = Enumerable.Range(1, 10).Select(i => new AuditScheduleTaskRef { Uid = i.ToString(), Nome = $"T{i}", CampoRealizadoResolvido = "Number1", ValorRealizado = 0, CampoPrevistoResolvido = "Number2", ValorPrevisto = 50 }).ToArray();
            ConfigurarMppBasico(acc, schedule, new AuditScheduleSnapshot { Tarefas = tarefas });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CRN-008");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task CRN006E007_ColunaPhysicalCompleted_AusenteEDuplicada()
        {
            var (regra, acc, schedule, workbook, _) = NovaRegra();
            ConfigurarMppBasico(acc, schedule);
            var pastaGestao = TestFixtures.Pasta("F-GESTAO", "09.14");
            acc.ConfigurarPasta("09.14", pastaGestao);
            acc.ConfigurarListagem("F-GESTAO", TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", "F-GESTAO", "09.14")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });

            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Single(resultado.Findings, f => f.RegraId == "CRN-006");
        }

        [Fact]
        public async Task CRN012_CronogramaProjetosDivergente_GeraFinding()
        {
            var (regra, acc, schedule, _, db) = NovaRegra();
            ConfigurarMppBasico(acc, schedule, new AuditScheduleSnapshot
            {
                Tarefas = new[] { new AuditScheduleTaskRef { Uid = "1", Nome = "T1", NumeroDocumentoNA = "DOC-1", CampoRealizadoResolvido = "Number1", ValorRealizado = 80, CampoPrevistoResolvido = "Number2", ValorPrevisto = 90 } }
            });
            db.ConfigurarResposta("Cronograma_Projetos", TestFixtures.Tabela(
                new[] { "NA_DOCUMENT_NUMBER", "PERCENT_PHYSICAL_COMPLETED" },
                new object?[] { "DOC-1", 20.0 }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "CRN-012");
            Assert.Equal("DOC-1", finding.DocumentoNa);
        }

        [Fact]
        public async Task TudoResolvidoSemDivergencia_NaoGeraFindings()
        {
            var (regra, acc, schedule, _, db) = NovaRegra();
            ConfigurarMppBasico(acc, schedule);
            db.ConfigurarResposta("Cronograma_Projetos", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.Empty(resultado.Findings);
        }
    }
}
