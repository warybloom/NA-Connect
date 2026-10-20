using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage00ProjectDnaAuditRuleTests
    {
        private static (Stage00ProjectDnaAuditRule Regra, FakeAuditDatabaseReadOnlyAdapter Db, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook) NovaRegra()
        {
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            return (new Stage00ProjectDnaAuditRule(db, acc, workbook), db, acc, workbook);
        }

        [Fact]
        public void Etapa_e_NomeEtapa_corretos()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("0", regra.Etapa);
            Assert.False(string.IsNullOrWhiteSpace(regra.NomeEtapa));
        }

        [Fact]
        public async Task DNA001_ProjetoSemAccountIdAcc_GeraFinding()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(
                new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" },
                new object?[] { "PRJ-1", null, "NA" }));
            acc.ConfigurarPasta("09.14", null); // pasta de gestão ausente -> corta cedo, mas DNA-001 já deve ter sido registrado antes.

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-001");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.ConfiguracaoProjeto, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);

            var requisito = Assert.Single(resultado.Requirements, r => r.RequirementCode == "DNA-001");
            Assert.Equal(OperationalRequirementStatus.Failed, requisito.Status);
        }

        [Fact]
        public async Task DNA001_ProjetoComIdentificacaoCompleta_NaoGeraFinding()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(
                new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" },
                new object?[] { "PRJ-1", "ACC-1", "NA" }));
            acc.ConfigurarPasta("09.14", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "DNA-001");
            var requisito = Assert.Single(resultado.Requirements, r => r.RequirementCode == "DNA-001");
            Assert.Equal(OperationalRequirementStatus.Approved, requisito.Status);
        }

        [Fact]
        public async Task DNA005_NamingStandardClient_GeraFindingDeAutomacao()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(
                new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" },
                new object?[] { "PRJ-1", "ACC-1", "CLIENT" }));
            acc.ConfigurarPasta("09.14", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-005");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.RiscoTecnico, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
        }

        [Fact]
        public async Task DNA005_NamingStandardNA_NaoGeraFinding()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(
                new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" },
                new object?[] { "PRJ-1", "ACC-1", "NA" }));
            acc.ConfigurarPasta("09.14", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "DNA-005");
        }

        [Fact]
        public async Task DNA002_PastaDeGestaoAusente_GeraFindingEParaImediatamente()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            acc.ConfigurarPasta("09.14", null);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-002");
            Assert.Equal(OperationalAuditClassification.EstruturaAcc, finding.Classificacao);
            Assert.True(finding.BloqueiaEtapa);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId is "DNA-003" or "DNA-004");
        }

        [Fact]
        public async Task DNA003_NenhumExcelCandidato_GeraFinding()
        {
            var (regra, db, acc, _) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "documento.pdf", "F1", "09.14 - MANAGEMENT")));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-003");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task DNA004_AbaLdpAusenteEmTodosOsCandidatos_GeraFinding()
        {
            var (regra, db, acc, workbook) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "controle.xlsx", "F1", "09.14 - MANAGEMENT")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1, 2, 3 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("Outra Aba", new[] { "X" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-004");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task DNA006_CodigosNaEClienteVaziosNaMesmaLinha_GeraFinding()
        {
            var (regra, db, acc, workbook) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "LDP.xlsx", "F1", "09.14 - MANAGEMENT")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [2] = new() { ["N&A DOCUMENT NUMBER"] = null, ["CLIENT DOCUMENT NUMBER"] = null }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER" }, linhas)));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-006");
            Assert.Equal(2, finding.Linha);
        }

        [Fact]
        public async Task DNA007_MesmoCodigoNaEmMultiplasLinhas_GeraFinding()
        {
            var (regra, db, acc, workbook) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "LDP.xlsx", "F1", "09.14 - MANAGEMENT")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [2] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-1" },
                [3] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-1" }
            };
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, linhas)));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "DNA-007");
            Assert.True(finding.BloqueiaEtapa);
            Assert.Contains("DOC-1", finding.ValorEncontrado);
        }

        [Fact]
        public async Task LinhaTotalmenteVazia_NuncaGeraFinding()
        {
            var (regra, db, acc, workbook) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "LDP.xlsx", "F1", "09.14 - MANAGEMENT")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });

            // O adaptador de workbook real nunca inclui linhas totalmente vazias em Linhas — simulado aqui
            // com um dicionário de linhas vazio, provando que nenhum finding aparece quando não há linha real.
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, new Dictionary<int, Dictionary<string, string?>>())));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId is "DNA-006" or "DNA-007");
        }

        [Fact]
        public async Task NenhumRequisitoAprovadoGeraFindingCorrespondente()
        {
            var (regra, db, acc, workbook) = NovaRegra();
            db.ConfigurarResposta("FROM Projetos", TestFixtures.Tabela(new[] { "Project_ID_ACC", "Account_ID_ACC", "NamingStandard" }, new object?[] { "PRJ-1", "ACC-1", "NA" }));
            var pasta = TestFixtures.Pasta("F1", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pasta);
            acc.ConfigurarListagem("F1", TestFixtures.Listagem(TestFixtures.Item("I1", "LDP.xlsx", "F1", "09.14 - MANAGEMENT")));
            acc.ConfigurarConteudo("urn:1", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A DOCUMENT NUMBER" }, new Dictionary<int, Dictionary<string, string?>>
            {
                [2] = new() { ["N&A DOCUMENT NUMBER"] = "DOC-1" }
            })));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var codigosAprovados = resultado.Requirements.Where(r => r.Status == OperationalRequirementStatus.Approved).Select(r => r.RequirementCode);
            foreach (var codigo in codigosAprovados)
            {
                Assert.DoesNotContain(resultado.Findings, f => f.RegraId == codigo);
            }
        }
    }
}
