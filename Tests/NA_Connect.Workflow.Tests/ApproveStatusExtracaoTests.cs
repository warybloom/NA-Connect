using APS.WebApp.Services;
using Newtonsoft.Json.Linq;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a causa raiz do bug Status_In_Flow: "Cannot access child value on Newtonsoft.Json.Linq.JValue."
    /// quando approveStatus é JSON null. ExtrairApproveStatusSeguro nunca pode lançar exceção, para
    /// nenhuma forma de payload real ou malformada.
    /// </summary>
    public class ApproveStatusExtracaoTests
    {
        private static JObject NovaVersaoComApproveStatus(JToken? approveStatus)
        {
            var versao = new JObject { ["name"] = "P-0602.1-000-EL-DR-0001-02.dwg" };
            if (approveStatus != null) versao["approveStatus"] = approveStatus;
            return versao;
        }

        [Fact]
        public void PropriedadeAusente_NaoLancaExcecao_SemResultadoFinal()
        {
            var versao = NovaVersaoComApproveStatus(null);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.False(resultado.TemResultadoFinalExplicito);
            Assert.Equal("PROPRIEDADE_AUSENTE", resultado.CodigoExtracao);
            Assert.Null(resultado.Label);
        }

        [Fact]
        public void JsonNull_NaoLancaExcecao_SemResultadoFinal()
        {
            // Este é EXATAMENTE o payload real que derrubava BuildWorkflowResultAsync inteiro antes da
            // correção: um JValue representando JSON null, indexado sem checar o tipo primeiro.
            var versao = NovaVersaoComApproveStatus(JValue.CreateNull());

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.False(resultado.TemResultadoFinalExplicito);
            Assert.Equal("JSON_NULL", resultado.CodigoExtracao);
            Assert.Null(resultado.Label);
            Assert.Null(resultado.Value);
        }

        [Fact]
        public void ObjetoComLabelAprovado_RetornaLabelExatamenteComoVeio()
        {
            var approveStatus = new JObject { ["id"] = "f44e623d", ["label"] = "Aprovado", ["value"] = "APPROVED" };
            var versao = NovaVersaoComApproveStatus(approveStatus);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.True(resultado.TemResultadoFinalExplicito);
            Assert.Equal("Aprovado", resultado.Label);
            Assert.Equal("APPROVED", resultado.Value);
            Assert.Equal("OBJETO_COM_RESULTADO", resultado.CodigoExtracao);
        }

        [Fact]
        public void ObjetoComLabelApproved_NuncaTraduzParaAprovado()
        {
            // Nunca mapear/traduzir para um valor hardcoded — um Review em inglês deve chegar como
            // "Approved", nunca "Aprovado".
            var approveStatus = new JObject { ["label"] = "Approved", ["value"] = "APPROVED" };
            var versao = NovaVersaoComApproveStatus(approveStatus);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.Equal("Approved", resultado.Label);
        }

        [Fact]
        public void ObjetoApenasComValue_UsaValueComoLabelExibido()
        {
            var approveStatus = new JObject { ["value"] = "REJECTED" };
            var versao = NovaVersaoComApproveStatus(approveStatus);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.True(resultado.TemResultadoFinalExplicito);
            Assert.Equal("REJECTED", resultado.Label);
        }

        [Fact]
        public void ObjetoSemLabelNemValue_SemResultadoFinal()
        {
            var approveStatus = new JObject { ["id"] = "algum-id" };
            var versao = NovaVersaoComApproveStatus(approveStatus);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.False(resultado.TemResultadoFinalExplicito);
            Assert.Equal("OBJETO_SEM_LABEL_NEM_VALUE", resultado.CodigoExtracao);
        }

        [Theory]
        [InlineData("texto-inesperado")]
        [InlineData(42)]
        public void TipoInesperado_NaoLancaExcecao_SemResultadoFinal(object valorBruto)
        {
            JToken tokenInesperado = valorBruto is int i ? new JValue(i) : new JValue((string)valorBruto);
            var versao = NovaVersaoComApproveStatus(tokenInesperado);

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.False(resultado.TemResultadoFinalExplicito);
            Assert.Equal("TIPO_INESPERADO", resultado.CodigoExtracao);
        }

        [Fact]
        public void ArrayInesperado_NaoLancaExcecao()
        {
            var versao = NovaVersaoComApproveStatus(new JArray());

            var resultado = AccReviewWorkflowService.ExtrairApproveStatusSeguro(versao);

            Assert.False(resultado.TemResultadoFinalExplicito);
        }
    }
}
