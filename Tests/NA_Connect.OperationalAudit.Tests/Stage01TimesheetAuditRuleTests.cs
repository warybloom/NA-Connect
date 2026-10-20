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
    public class Stage01TimesheetAuditRuleTests
    {
        private const string FolderId = "F-GESTAO";

        private static (Stage01TimesheetAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditWorkbookReadOnlyAdapter Workbook, FakeAuditDatabaseReadOnlyAdapter Db) NovaRegra(
            string filtroProjeto = "PROJ1", string filtroDoc1 = "DOC1", bool comAbaCustoReal = true)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();

            acc.ConfigurarPasta("09.14", TestFixtures.Pasta(FolderId, "09.14 - MANAGEMENT"));
            var itemLdp = TestFixtures.Item("ILDP", "LDP.xlsx", FolderId, "09.14 - MANAGEMENT", versionUrn: "urn:ldp");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemLdp));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });

            var linhas = new Dictionary<int, Dictionary<string, string?>>
            {
                [8] = new() { ["Col24"] = filtroProjeto, ["Col25"] = filtroDoc1 }
            };
            var abaLdp = TestFixtures.Aba("LDP", Array.Empty<string>(), linhas);
            var abas = comAbaCustoReal
                ? new[] { abaLdp, TestFixtures.Aba("Custo Real", Array.Empty<string>(), new()) }
                : new[] { abaLdp };
            workbook.Enfileirar(TestFixtures.Workbook(abas));

            return (new Stage01TimesheetAuditRule(acc, workbook, db), acc, workbook, db);
        }

        private static readonly string[] Colunas = { "Data", "Nome", "Planejadas", "Trabalhadas", "Projeto", "Funcao", "Disciplina", "DocumentoCodigo", "Documento", "Descricao" };

        [Fact]
        public void Etapa_correta()
        {
            var (regra, _, _, _) = NovaRegra();
            Assert.Equal("1", regra.Etapa);
        }

        [Fact]
        public async Task TS001_FalhaNaConsulta_GeraFindingInconclusivo()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarExcecao("tbTimeSheetRealizadoDocumento", new InvalidOperationException("Timeout expired"));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-001");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
            Assert.Equal(OperationalAuditSeverity.Critico, finding.Severidade);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task TS001_ConsultaBemSucedida_NaoGeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-001");
            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "TS-001" && r.Status == OperationalRequirementStatus.Approved);
        }

        [Fact]
        public async Task TS002_FiltrosVazios_GeraFinding()
        {
            var (regra, _, _, db) = NovaRegra(filtroProjeto: "", filtroDoc1: " ");
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-002");
            Assert.Equal(OperationalAuditClassification.DadoAusente, finding.Classificacao);
        }

        [Fact]
        public async Task TS002_FiltrosPreenchidosSemRegistros_NuncaAfirmaErro()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas)); // 0 linhas.

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-002");
        }

        [Fact]
        public async Task TS003_CampoObrigatorioAusente_GeraFindingConsolidado()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "", 1m, 1m, "PROJ1", "F1", "DISC1", "", "DOC-A", "desc" },
                new object?[] { DateTime.Today, "", 1m, 1m, "PROJ1", "F1", "DISC1", "", "DOC-B", "desc" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-003" && f.Campo == "Nome");
            Assert.Contains("DOC-A", finding.Evidencia);
            Assert.Contains("DOC-B", finding.Evidencia);
        }

        [Fact]
        public async Task TS003_CamposPreenchidos_NaoGeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "Fulano", 1m, 1m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-003");
        }

        [Fact]
        public async Task TS004_ValorNaoNumerico_GeraFindingAutomacao()
        {
            var (regra, _, _, db) = NovaRegra();
            var tabela = TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "Fulano", "abc-nao-numerico", 1m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" });
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", tabela);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-004" && f.Campo == "Planejadas");
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.DadoInvalido, finding.Classificacao);
        }

        [Fact]
        public async Task TS004_ValorComMuitosDigitos_GeraRiscoInconclusivo()
        {
            var (regra, _, _, db) = NovaRegra();
            var tabela = TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "Fulano", "1234567890123456.789", 1m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" });
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", tabela);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-004" && f.Campo == "Planejadas");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.RiscoTecnico, finding.Classificacao);
        }

        [Fact]
        public async Task TS004_ValorNormal_NaoGeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            var tabela = TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "Fulano", 8.5m, 8.0m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" });
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", tabela);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-004");
        }

        [Fact]
        public async Task TS005_RegistroFisicamenteDuplicado_GeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            var linha = new object?[] { DateTime.Today, "Fulano", 8.5m, 8.0m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" };
            var tabela = TestFixtures.Tabela(Colunas, linha, (object?[])linha.Clone());
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", tabela);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-005");
            Assert.Equal(OperationalAuditClassification.DadoDuplicado, finding.Classificacao);
        }

        [Fact]
        public async Task TS005_RegistrosDistintos_NaoGeraFinding()
        {
            var (regra, _, _, db) = NovaRegra();
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas,
                new object?[] { DateTime.Today, "Fulano", 8.5m, 8.0m, "PROJ1", "F1", "DISC1", "COD1", "DOC-A", "desc" },
                new object?[] { DateTime.Today, "Ciclano", 4.0m, 4.0m, "PROJ1", "F1", "DISC1", "COD2", "DOC-B", "desc" }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-005");
        }

        [Fact]
        public async Task TS006_AbaCustoRealAusente_GeraFinding()
        {
            var (regra, _, _, db) = NovaRegra(comAbaCustoReal: false);
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "TS-006");
            Assert.Equal(OperationalAuditClassification.EstruturaPlanilha, finding.Classificacao);
        }

        [Fact]
        public async Task TS006_AbaCustoRealPresente_NaoGeraFinding()
        {
            var (regra, _, _, db) = NovaRegra(comAbaCustoReal: true);
            db.ConfigurarResposta("tbTimeSheetRealizadoDocumento", TestFixtures.Tabela(Colunas));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto());

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "TS-006");
        }
    }
}
