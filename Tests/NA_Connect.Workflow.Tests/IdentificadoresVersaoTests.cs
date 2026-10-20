using APS.WebApp.Services;
using Newtonsoft.Json.Linq;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre o segundo bug real do diagnóstico: o payload de GET /reviews/{id}/versions usa "itemUrn"
    /// (não "itemId"). A correspondência por identificador físico só funciona se essa extração priorizar
    /// o campo que realmente existe no payload.
    /// </summary>
    public class IdentificadoresVersaoTests
    {
        [Fact]
        public void ItemUrn_QuandoPresente_EhUsadoComoIdentificadorPrincipal()
        {
            var versao = new JObject { ["itemUrn"] = "urn:adsk.wipprod:dm.lineage:PgS3DlliShKzj4x3aomD-g" };

            var (itemUrn, origem) = AccReviewWorkflowService.ExtrairItemUrnDaVersao(versao);

            Assert.Equal("urn:adsk.wipprod:dm.lineage:PgS3DlliShKzj4x3aomD-g", itemUrn);
            Assert.Equal("ITEM_URN", origem);
        }

        [Fact]
        public void ItemId_SoEhUsado_QuandoItemUrnAusente()
        {
            var versao = new JObject { ["itemId"] = "urn:adsk.wipprod:dm.lineage:compat" };

            var (itemUrn, origem) = AccReviewWorkflowService.ExtrairItemUrnDaVersao(versao);

            Assert.Equal("urn:adsk.wipprod:dm.lineage:compat", itemUrn);
            Assert.Equal("ITEM_ID_COMPATIBILIDADE", origem);
        }

        [Fact]
        public void ItemUrn_PrevaleceSobreItemId_QuandoAmbosPresentes()
        {
            var versao = new JObject
            {
                ["itemUrn"] = "urn-real",
                ["itemId"] = "urn-compatibilidade-nunca-deveria-vencer"
            };

            var (itemUrn, origem) = AccReviewWorkflowService.ExtrairItemUrnDaVersao(versao);

            Assert.Equal("urn-real", itemUrn);
            Assert.Equal("ITEM_URN", origem);
        }

        [Fact]
        public void NenhumIdentificador_RetornaVazioENaoEncontrado()
        {
            var versao = new JObject();

            var (itemUrn, origem) = AccReviewWorkflowService.ExtrairItemUrnDaVersao(versao);

            Assert.Equal(string.Empty, itemUrn);
            Assert.Equal("NAO_ENCONTRADO", origem);
        }

        [Fact]
        public void VersionUrn_UsaCampoUrn_QuandoVersionUrnAusente()
        {
            // Payload real observado: "urn": "...", nunca "versionUrn".
            var versao = new JObject { ["urn"] = "urn:adsk.wipprod:fs.file:vf.PgS3DlliShKzj4x3aomD-g?version=8" };

            var (versionUrn, origem) = AccReviewWorkflowService.ExtrairVersionUrnDaVersao(versao);

            Assert.Equal("urn:adsk.wipprod:fs.file:vf.PgS3DlliShKzj4x3aomD-g?version=8", versionUrn);
            Assert.Equal("URN", origem);
        }

        [Fact]
        public void VersionUrn_PrevaleceSobreUrn_QuandoAmbosPresentes()
        {
            var versao = new JObject { ["versionUrn"] = "versao-real", ["urn"] = "urn-alternativo" };

            var (versionUrn, origem) = AccReviewWorkflowService.ExtrairVersionUrnDaVersao(versao);

            Assert.Equal("versao-real", versionUrn);
            Assert.Equal("VERSION_URN", origem);
        }
    }
}
