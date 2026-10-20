using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services.OperationalAudit;
using NA_Connect.OperationalAudit.Tests.Fakes;

namespace NA_Connect.OperationalAudit.Tests
{
    public class OperationalAuditStageSelectionTests
    {
        private static List<IOperationalAuditRule> Regras() => new()
        {
            new FakeOperationalAuditRule("0"),
            new FakeOperationalAuditRule("7"),
            new FakeOperationalAuditRule("9"),
            new FakeOperationalAuditRule("17")
        };

        [Fact]
        public void ListaDeEtapasVazia_SelecionaTodasAsRegras()
        {
            var selecionadas = OperationalAuditRunner.SelecionarRegras(Regras(), new List<string>());
            Assert.Equal(4, selecionadas.Count);
        }

        [Fact]
        public void ListaDeEtapasPreenchida_FiltraSoAsEtapasListadas()
        {
            var selecionadas = OperationalAuditRunner.SelecionarRegras(Regras(), new List<string> { "7", "17" });
            Assert.Equal(new[] { "7", "17" }, selecionadas.Select(r => r.Etapa));
        }

        [Fact]
        public void ListaDeEtapas_ComparacaoInsensivelACaixa()
        {
            var selecionadas = OperationalAuditRunner.SelecionarRegras(Regras(), new List<string> { "0" });
            Assert.Single(selecionadas);
        }

        [Fact]
        public void EtapaNaoExistente_NaoQuebraENaoSelecionaNada()
        {
            var selecionadas = OperationalAuditRunner.SelecionarRegras(Regras(), new List<string> { "99" });
            Assert.Empty(selecionadas);
        }
    }
}
