using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre AttributeSync:ForceOverwriteAll (Etapa 7): o único ponto onde a flag altera comportamento é
    /// se um CACHE_HIT é honrado (pula POST) ou ignorado (força reenvio) — testado via os predicados
    /// puros DeveHonrarCacheHit/EhReenvioForcado. As demais regras (deduplicação por VersionUrn, filtro de
    /// valores vazios/placeholder, localização de pastas, matching) são verificadas aqui como regressão,
    /// pois ForceOverwriteAll não pode alterá-las. Não acessa ACC ou SQL real — grava/leitura reais do
    /// cache (dbo.ACC_Attribute_Sync_State) e o POST em si (UpdateCustomAttributesForVersionAsync)
    /// permanecem código pré-existente e não modificado por esta funcionalidade.
    /// </summary>
    public class AttributeSyncForceOverwriteTests
    {
        private static IConfiguration ConfigComForceOverwrite(bool? valor)
        {
            var dict = new Dictionary<string, string>();
            if (valor.HasValue) dict["AttributeSync:ForceOverwriteAll"] = valor.Value.ToString();
            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        // 12 — ForceOverwriteAll=false é o valor padrão (chave ausente do appsettings).
        [Fact]
        public void LerForceOverwriteAll_ChaveAusente_RetornaFalse()
        {
            Assert.False(LdpUpdateService.LerForceOverwriteAll(ConfigComForceOverwrite(null)));
        }

        [Fact]
        public void LerForceOverwriteAll_ChaveTrue_RetornaTrue()
        {
            Assert.True(LdpUpdateService.LerForceOverwriteAll(ConfigComForceOverwrite(true)));
        }

        [Fact]
        public void LerForceOverwriteAll_ChaveFalse_RetornaFalse()
        {
            Assert.False(LdpUpdateService.LerForceOverwriteAll(ConfigComForceOverwrite(false)));
        }

        // 1 — ForceOverwriteAll=false mantém CACHE_HIT sem POST (DeveHonrarCacheHit=true -> loop real dá "continue").
        [Fact]
        public void ForceOverwriteAllFalse_ComCacheHit_HonraCacheEPulaPost()
        {
            Assert.True(LdpUpdateService.DeveHonrarCacheHit(cacheHit: true, forceOverwriteAll: false));
            Assert.False(LdpUpdateService.EhReenvioForcado(cacheHit: true, forceOverwriteAll: false));
        }

        // 2/3 — ForceOverwriteAll=true ignora o CACHE_HIT e o roteia para reenvio forçado (o mesmo
        // caminho de POST usado por um CACHE_MISS normal).
        [Fact]
        public void ForceOverwriteAllTrue_ComCacheHit_IgnoraCacheEForcaPost()
        {
            Assert.False(LdpUpdateService.DeveHonrarCacheHit(cacheHit: true, forceOverwriteAll: true));
            Assert.True(LdpUpdateService.EhReenvioForcado(cacheHit: true, forceOverwriteAll: true));
        }

        // ForceOverwriteAll nunca cria trabalho quando já é CACHE_MISS — continua sendo um envio normal,
        // nunca contado como "forçado" (a flag só tem efeito quando havia mesmo um CACHE_HIT a ignorar).
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SemCacheHit_ForceOverwriteAllNuncaMarcaComoForcado(bool forceOverwriteAll)
        {
            Assert.False(LdpUpdateService.DeveHonrarCacheHit(cacheHit: false, forceOverwriteAll: forceOverwriteAll));
            Assert.False(LdpUpdateService.EhReenvioForcado(cacheHit: false, forceOverwriteAll: forceOverwriteAll));
        }

        // 6 — campos vazios/placeholder da LDP nunca são enviados como valor (evita apagar atributo real
        // no ACC), em qualquer um dos dois modos de ForceOverwriteAll.
        [Fact]
        public void EhValorVazioOuPlaceholder_Null_RetornaTrue()
        {
            Assert.True(LdpUpdateService.EhValorVazioOuPlaceholder(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-")]
        [InlineData("N/A")]
        [InlineData("n/a")]
        [InlineData(" - ")]
        public void EhValorVazioOuPlaceholder_ValoresSemInformacao_RetornaTrue(string valor)
        {
            Assert.True(LdpUpdateService.EhValorVazioOuPlaceholder(valor));
        }

        [Theory]
        [InlineData("PROCESS AND INSTRUMENTATION DIAGRAM")]
        [InlineData("P-0602.1-310-PX-DR-0055")]
        [InlineData("A0")]
        public void EhValorVazioOuPlaceholder_ValorReal_RetornaFalse(string valor)
        {
            Assert.False(LdpUpdateService.EhValorVazioOuPlaceholder(valor));
        }

        // 8 — mesma VersionUrn não recebe dois POSTs na mesma execução, com ou sem ForceOverwriteAll (a
        // deduplicação é independente da flag — roda ANTES da decisão de cache no loop real).
        [Fact]
        public void VersionUrnJaAtualizadaNestaExecucao_MesmaConfiguracao_DetectaDuplicidade()
        {
            var atualizadas = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["urn:adsk.wipprod:fs.file:vf.ABC?version=1"] = "PRODUCAO_10"
            };

            bool jaAtualizada = LdpUpdateService.VersionUrnJaAtualizadaNestaExecucao(
                atualizadas, "urn:adsk.wipprod:fs.file:vf.ABC?version=1", "PRODUCAO_10", out bool mesmaConfiguracao);

            Assert.True(jaAtualizada);
            Assert.True(mesmaConfiguracao);
        }

        [Fact]
        public void VersionUrnJaAtualizadaNestaExecucao_ConfiguracaoDiferente_DetectaDuplicidadeEntreConfiguracoes()
        {
            var atualizadas = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["urn:adsk.wipprod:fs.file:vf.ABC?version=1"] = "ENGENHARIA_04"
            };

            bool jaAtualizada = LdpUpdateService.VersionUrnJaAtualizadaNestaExecucao(
                atualizadas, "urn:adsk.wipprod:fs.file:vf.ABC?version=1", "PRODUCAO_10", out bool mesmaConfiguracao);

            Assert.True(jaAtualizada);
            Assert.False(mesmaConfiguracao);
        }

        // 9 — DWG e PDF (VersionUrns diferentes) nunca são tratados como duplicados entre si.
        [Fact]
        public void VersionUrnJaAtualizadaNestaExecucao_VersionUrnDiferente_NaoDetectaDuplicidade()
        {
            var atualizadas = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["urn:adsk.wipprod:fs.file:vf.DWG?version=1"] = "PRODUCAO_10"
            };

            bool jaAtualizada = LdpUpdateService.VersionUrnJaAtualizadaNestaExecucao(
                atualizadas, "urn:adsk.wipprod:fs.file:vf.PDF?version=1", "PRODUCAO_10", out bool mesmaConfiguracao);

            Assert.False(jaAtualizada);
            Assert.False(mesmaConfiguracao);
        }

        // 10 — as quatro configurações de pasta permanecem inalteradas por esta funcionalidade.
        [Fact]
        public void PastasAtributos_ContinuamAsQuatroConfiguracoesOriginais()
        {
            Assert.Equal(4, LdpUpdateService.PastasAtributos.Count);
            Assert.Contains(LdpUpdateService.PastasAtributos, c => c.Chave == LdpUpdateService.CHAVE_ENGENHARIA_04);
            Assert.Contains(LdpUpdateService.PastasAtributos, c => c.Chave == LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09);
            Assert.Contains(LdpUpdateService.PastasAtributos, c => c.Chave == LdpUpdateService.CHAVE_PRODUCAO_10);
            Assert.Contains(LdpUpdateService.PastasAtributos, c => c.Chave == LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D);
        }

        // 7/11 — o matching (DocumentMatchingService) continua ignorando arquivos não correspondentes,
        // independentemente de ForceOverwriteAll (a flag não tem nenhum parâmetro de matching).
        [Fact]
        public void Matching_ArquivoNaoCorrespondente_ContinuaIgnorado()
        {
            Assert.False(NA_Connect.Services.DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-00550.dwg", "P-0602.1-310-PX-DR-0055"));
        }

        [Fact]
        public void Matching_ArquivoCorrespondente_ContinuaEncontrado()
        {
            Assert.True(NA_Connect.Services.DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-310-PX-DR-0055-02.dwg", "P-0602.1-310-PX-DR-0055"));
        }
    }
}
