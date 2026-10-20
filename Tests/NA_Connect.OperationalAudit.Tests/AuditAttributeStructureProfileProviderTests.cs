using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Services;
using APS.WebApp.Services.OperationalAudit.Configuration;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção pós-Checkpoint 2 ("duas fontes de verdade"): prova que
    /// <see cref="AuditAttributeStructureProfileProvider"/> nunca decide os caminhos permitidos de
    /// PRODUCAO_MODELOS_3D a partir de uma lista fixa reescrita dentro da auditoria — sempre a partir dos
    /// dados recebidos (núcleo puro <c>ConstruirPerfisModelos3DDeCaminhos</c>) ou, no caminho real, de
    /// <c>LdpUpdateService.PastasAtributos</c> lido diretamente em tempo de execução.
    /// </summary>
    public class AuditAttributeStructureProfileProviderTests
    {
        // ---------------------------------------------------------------------------------------------
        // Núcleo puro: qualquer configuração dada é a que decide — nunca um valor hardcoded na auditoria.
        // Prova direta do item 5 da correção ("alteração simulada da configuração não deixa uma cópia
        // estática decidir incorretamente"): usamos nomes de árvore que NÃO existem na configuração
        // operacional real de hoje ("77"/"88") e ainda assim o resultado os segue fielmente.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void NucleoPuro_SegueQualquerConfiguracaoDada_NuncaUmaListaFixaNaAuditoria()
        {
            var caminhosSimulados = new List<string[]>
            {
                new[] { "77 - ARVORE SIMULADA", "77.09 - SOFTWARE FICTICIO" },
                new[] { "88 - OUTRA ARVORE SIMULADA", "88.05 - OUTRO SOFTWARE" }
            };

            var perfis = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3DDeCaminhos(caminhosSimulados, recursivo: true, atributos: new[] { "REV." });

            Assert.Equal(2, perfis.Count);

            var perfil77 = Assert.Single(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_77");
            Assert.Equal(new[] { "77 - ARVORE SIMULADA" }, perfil77.AliasesRaiz);
            Assert.Equal(new[] { "77.09 - SOFTWARE FICTICIO" }, perfil77.CaminhosPermitidosRelativos);
            Assert.True(perfil77.Recursivo);
            Assert.Equal(new[] { "REV." }, perfil77.AtributosObrigatorios);

            var perfil88 = Assert.Single(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_88");
            Assert.Equal(new[] { "88.05 - OUTRO SOFTWARE" }, perfil88.CaminhosPermitidosRelativos);
        }

        [Fact]
        public void NucleoPuro_ArvoreComMultiplosCaminhosRaiz_AgrupaCorretamenteSemMisturarArvores()
        {
            var caminhos = new List<string[]>
            {
                new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.01 - AUTODESK REVIT" },
                new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.02 - AUTODESK CIVIL 3D" },
                new[] { "08 - FEDERATED MODEL", "08.01 - 3D AUTHORING SOFTWARE", "08.01.01 - AUTODESK REVIT" }
            };

            var perfis = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3DDeCaminhos(caminhos, recursivo: true, atributos: null);

            var perfil07 = Assert.Single(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_07");
            Assert.Equal(2, perfil07.CaminhosPermitidosRelativos.Length);
            Assert.Contains("07.01.01 - AUTODESK REVIT", perfil07.CaminhosPermitidosRelativos);
            Assert.Contains("07.01.02 - AUTODESK CIVIL 3D", perfil07.CaminhosPermitidosRelativos);

            var perfil08 = Assert.Single(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_08");
            Assert.Equal(new[] { "08.01 - 3D AUTHORING SOFTWARE/08.01.01 - AUTODESK REVIT" }, perfil08.CaminhosPermitidosRelativos);
        }

        [Fact]
        public void NucleoPuro_NenhumCaminho_DevolveListaVazia_NuncaInventaPerfil()
        {
            var perfis = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3DDeCaminhos(new List<string[]>(), recursivo: true, atributos: null);
            Assert.Empty(perfis);
        }

        [Fact]
        public void NucleoPuro_AtributosAusentes_NuncaLancaExcecao_DevolveVazio()
        {
            var caminhos = new List<string[]> { new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.01 - AUTODESK REVIT" } };
            var perfis = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3DDeCaminhos(caminhos, recursivo: false, atributos: null);

            var perfil = Assert.Single(perfis);
            Assert.Empty(perfil.AtributosObrigatorios);
            Assert.False(perfil.Recursivo);
        }

        // ---------------------------------------------------------------------------------------------
        // Integração com a fonte real: prova que o wrapper público realmente delega para
        // LdpUpdateService.PastasAtributos (leitura direta, mesmo assembly, sem reflection) — nunca uma
        // cópia paralela mantida dentro do provider.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void ConstruirPerfisModelos3D_DelegaParaLdpUpdateService_MesmaFonteQueAAutomacaoRealUsa()
        {
            var grupoReal = LdpUpdateService.PastasAtributos.Single(g => g.Chave == LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D);
            var esperado = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3DDeCaminhos(
                grupoReal.CaminhosRaiz, grupoReal.IncluirSubpastasRecursivamente, grupoReal.Atributos);

            var obtido = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3D();

            Assert.Equal(esperado.Count, obtido.Count);
            foreach (var perfilEsperado in esperado)
            {
                var perfilObtido = Assert.Single(obtido, p => p.ChaveConfiguracao == perfilEsperado.ChaveConfiguracao);
                Assert.Equal(perfilEsperado.AliasesRaiz, perfilObtido.AliasesRaiz);
                Assert.Equal(perfilEsperado.CaminhosPermitidosRelativos, perfilObtido.CaminhosPermitidosRelativos);
                Assert.Equal(perfilEsperado.Recursivo, perfilObtido.Recursivo);
                Assert.Equal(perfilEsperado.AtributosObrigatorios, perfilObtido.AtributosObrigatorios);
            }
        }

        [Fact]
        public void ConstruirPerfisModelos3D_ProduzExatamenteAsDuasArvoresReaisHoje_07E08()
        {
            var perfis = AuditAttributeStructureProfileProvider.ConstruirPerfisModelos3D();

            Assert.Equal(2, perfis.Count);
            Assert.Contains(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_07" && p.AliasesRaiz.Contains("07 - 3D AUTHORING SOFTWARE"));
            Assert.Contains(perfis, p => p.ChaveConfiguracao == "PRODUCAO_MODELOS_3D_08" && p.AliasesRaiz.Contains("08 - FEDERATED MODEL"));
        }

        // ---------------------------------------------------------------------------------------------
        // Contrato de proteção contra falso positivo (item 4 da correção): mesmo com um perfil real (não
        // um "discovery hint" hardcoded), a decisão de CONFIGURADA_E_NAO_PERMITIDA só ocorre quando a
        // lista de caminhos permitidos NÃO está vazia — nunca por ausência/ruído. E ausência total de
        // configuração operacional (grupo não encontrado) nunca inventa um perfil nem um erro.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void Contrato_ConfiguracaoVazia_NuncaAutorizaErrorDomainOperacional()
        {
            var avaliacao = AttributePathPermissionResolver.Resolver("QUALQUER/CAMINHO", System.Array.Empty<string>());
            Assert.Equal(AvaliacaoCaminhoPermitido.SemConfiguracaoSuficiente, avaliacao);
        }

        [Fact]
        public void Contrato_ConfiguracaoExplicitaPermiteCaminho_Aprova()
        {
            var avaliacao = AttributePathPermissionResolver.Resolver("07.01.01 - AUTODESK REVIT/000", new[] { "07.01.01 - AUTODESK REVIT" });
            Assert.Equal(AvaliacaoCaminhoPermitido.ConfiguradaEPermitida, avaliacao);
        }

        [Fact]
        public void Contrato_ConfiguracaoExplicitaProibeCaminho_SoAiGeraNaoPermitida()
        {
            var avaliacao = AttributePathPermissionResolver.Resolver("99.99 - FORA", new[] { "07.01.01 - AUTODESK REVIT" });
            Assert.Equal(AvaliacaoCaminhoPermitido.ConfiguradaENaoPermitida, avaliacao);
        }
    }
}
