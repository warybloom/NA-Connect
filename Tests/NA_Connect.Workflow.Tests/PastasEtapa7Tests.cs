using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a arquitetura de pastas da Etapa 7 (seção 3-12 do pedido de correção): quatro configurações
    /// independentes (ENGENHARIA_04, PRODUCAO_DOCUMENTOS_09, PRODUCAO_10, PRODUCAO_MODELOS_3D — esta
    /// última inalterada), as 14 disciplinas compartilhadas geradas por CriarCaminhosDisciplinas, aliases
    /// PT/EN por autoridade de prefixo numérico (ResolverPastaDisciplina) e coleta não recursiva.
    /// </summary>
    public class PastasEtapa7Tests
    {
        private static AccFolderItem Pasta(string nome) => new AccFolderItem { Name = nome, Type = "folders", Id = System.Guid.NewGuid().ToString(), IsHidden = false };

        private static LdpUpdateService.GrupoPastaConfig Config(string chave) =>
            LdpUpdateService.PastasAtributos.Single(c => c.Chave == chave);

        // 1 — ENGENHARIA_04 continua existindo.
        [Fact]
        public void Engenharia04_ContinuaExistindo()
        {
            Assert.Contains(LdpUpdateService.PastasAtributos, c => c.Chave == LdpUpdateService.CHAVE_ENGENHARIA_04);
        }

        // 2/3/4 — aliases da pasta principal de Engenharia (inclui "06 - ENGINEERING", correção 16/09/2026
        // para o CHS001 — nunca removido o prefixo "04").
        [Theory]
        [InlineData("04 - ENGINEERING")]
        [InlineData("04. ENGINEERING")]
        [InlineData("04 - ENGENHARIA")]
        [InlineData("04. ENGENHARIA")]
        [InlineData("06 - ENGINEERING")]
        [InlineData("06. ENGINEERING")]
        [InlineData("06 - ENGENHARIA")]
        [InlineData("06. ENGENHARIA")]
        public void Engenharia04_AceitaAliasesDaPastaPrincipal(string nomeReal)
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta(nomeReal) };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_ENGENHARIA_04));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado.Resultado);
            Assert.Equal(nomeReal, resultado.Pasta.Name);
        }

        // 5 — 04 procura disciplinas 04.01 até 04.14.
        [Fact]
        public void Engenharia04_ProcuraDisciplinas04_01Ate04_14()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("04");
            Assert.Equal(14, caminhos.Count);
            Assert.Equal("04.01 - PROCESS", caminhos[0][0]);
            Assert.Equal("04.14 - MANAGEMENT", caminhos[13][0]);
        }

        // 6 — 04.14 resolve MANAGEMENT/GESTÃO (autoridade de prefixo, texto real em português).
        [Fact]
        public void Engenharia04_1_4_ResolveManagementGestao()
        {
            var conteudo = new List<AccFolderItem> { Pasta("04.14 - GESTÃO") };
            var resultado = LdpUpdateService.ResolverPastaDisciplina(conteudo, "04.14 - MANAGEMENT", out var pastaEncontrada, out _);

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado);
            Assert.Equal("04.14 - GESTÃO", pastaEncontrada.Name);
        }

        // 7 — 04 não procura 09.14 (o bug histórico: "09.14 - MANAGEMENT" dentro da config 04).
        [Fact]
        public void Engenharia04_NaoContemPrefixo0914()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("04");
            Assert.All(caminhos, c => Assert.StartsWith("04.", c[0]));
            Assert.DoesNotContain(Config(LdpUpdateService.CHAVE_ENGENHARIA_04).CaminhosRelativos, c => c[0].StartsWith("09."));
        }

        // 8 — 04.13 aceita ENGINEERING SYSTEMS (nome canônico corrigido).
        [Fact]
        public void Engenharia04_13_EhEngineeringSystemsCanonico()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("04");
            Assert.Equal("04.13 - ENGINEERING SYSTEMS", caminhos[12][0]);
        }

        // 9 — 04.13 mantém compatibilidade com DIGITAL SYSTEMS (autoridade de prefixo, texto real diferente).
        [Fact]
        public void Engenharia04_13_MantemCompatibilidadeComDigitalSystems()
        {
            var conteudo = new List<AccFolderItem> { Pasta("04.13 - DIGITAL SYSTEMS") };
            var resultado = LdpUpdateService.ResolverPastaDisciplina(conteudo, "04.13 - ENGINEERING SYSTEMS", out var pastaEncontrada, out _);

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado);
            Assert.Equal("04.13 - DIGITAL SYSTEMS", pastaEncontrada.Name);
        }

        // 10/11/12 — aliases da pasta principal de PRODUCAO_DOCUMENTOS_09.
        [Theory]
        [InlineData("09 - ENGINEERING DOCUMENTS")]
        [InlineData("09 - DOCUMENTOS DE ENGENHARIA")]
        [InlineData("09 - DOCUMENTAÇÃO DE ENGENHARIA")]
        public void ProducaoDocumentos09_AceitaAliasesDaPastaPrincipal(string nomeReal)
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta(nomeReal) };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado.Resultado);
            Assert.Equal(nomeReal, resultado.Pasta.Name);
        }

        // 13 — 09 procura disciplinas 09.01 até 09.14.
        [Fact]
        public void ProducaoDocumentos09_ProcuraDisciplinas09_01Ate09_14()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("09");
            Assert.Equal(14, caminhos.Count);
            Assert.Equal("09.01 - PROCESS", caminhos[0][0]);
            Assert.Equal("09.14 - MANAGEMENT", caminhos[13][0]);
        }

        // 14/15/16 — Equipment/Electrical com prefixos corretos e distintos (bug histórico: os dois
        // usavam "09.09").
        [Fact]
        public void ProducaoDocumentos09_09_ResolveEquipment()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("09");
            Assert.Equal("09.09 - EQUIPMENT", caminhos[8][0]);
        }

        [Fact]
        public void ProducaoDocumentos09_10_ResolveElectrical()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("09");
            Assert.Equal("09.10 - ELECTRICAL", caminhos[9][0]);
        }

        [Fact]
        public void Electrical_NaoEhAssociadoA09_09()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("09");
            Assert.NotEqual(caminhos[8][0], caminhos[9][0]);
            Assert.DoesNotContain("ELECTRICAL", caminhos[8][0]);
        }

        // 17/18/19/20 — aliases da pasta principal de PRODUCAO_10 (inclui "12 - PRODUCT", correção
        // 16/09/2026 para o CHS001 — nunca removido o prefixo "10").
        [Theory]
        [InlineData("10 - PRODUCTION")]
        [InlineData("10 - PRODUCT")]
        [InlineData("10 - PRODUÇÃO")]
        [InlineData("10 - PRODUTO")]
        [InlineData("12 - PRODUCT")]
        [InlineData("12 - PRODUCTION")]
        [InlineData("12 - PRODUÇÃO")]
        [InlineData("12 - PRODUTO")]
        public void Producao10_AceitaAliasesDaPastaPrincipal(string nomeReal)
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta(nomeReal) };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_10));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado.Resultado);
            Assert.Equal(nomeReal, resultado.Pasta.Name);
        }

        // 21 — 10 procura disciplinas 10.01 até 10.14.
        [Fact]
        public void Producao10_ProcuraDisciplinas10_01Ate10_14()
        {
            var caminhos = LdpUpdateService.CriarCaminhosDisciplinas("10");
            Assert.Equal(14, caminhos.Count);
            Assert.Equal("10.01 - PROCESS", caminhos[0][0]);
            Assert.Equal("10.14 - MANAGEMENT", caminhos[13][0]);
        }

        // 22/23/24 — as três estruturas coexistem no mesmo projeto; encontrar uma não impede as outras.
        [Fact]
        public void Estruturas04_09_10_CoexistemNoMesmoProjeto()
        {
            var conteudoRaiz = new List<AccFolderItem>
            {
                Pasta("04 - ENGINEERING"),
                Pasta("09 - ENGINEERING DOCUMENTS"),
                Pasta("10 - PRODUCTION")
            };

            var resultado04 = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_ENGENHARIA_04));
            var resultado09 = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09));
            var resultado10 = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_10));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado04.Resultado);
            Assert.Equal("04 - ENGINEERING", resultado04.Pasta.Name);

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado09.Resultado);
            Assert.Equal("09 - ENGINEERING DOCUMENTS", resultado09.Pasta.Name);

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.Encontrada, resultado10.Resultado);
            Assert.Equal("10 - PRODUCTION", resultado10.Pasta.Name);
        }

        // Regressão explícita do bug removido: "09 - ENGINEERING DOCUMENTS" NUNCA resolve como
        // PRODUCAO_10 (antes era um fallback tolerante compartilhado).
        [Fact]
        public void Pasta09_NuncaResolveComoProducao10()
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta("09 - ENGINEERING DOCUMENTS") };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_10));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.NaoEncontrada, resultado.Resultado);
        }

        // 25 — cada configuração possui sua própria ChaveConfiguracao.
        [Fact]
        public void CadaConfiguracao_TemChavePropria()
        {
            var chaves = LdpUpdateService.PastasAtributos.Select(c => c.Chave).ToList();
            Assert.Equal(chaves.Count, chaves.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(LdpUpdateService.CHAVE_ENGENHARIA_04, chaves);
            Assert.Contains(LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09, chaves);
            Assert.Contains(LdpUpdateService.CHAVE_PRODUCAO_10, chaves);
            Assert.Contains(LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D, chaves);
        }

        // 26 — as três novas/ajustadas configurações são não recursivas.
        [Fact]
        public void TresConfiguracoes_SaoNaoRecursivas()
        {
            Assert.False(Config(LdpUpdateService.CHAVE_ENGENHARIA_04).IncluirSubpastasRecursivamente);
            Assert.False(Config(LdpUpdateService.CHAVE_PRODUCAO_DOCUMENTOS_09).IncluirSubpastasRecursivamente);
            Assert.False(Config(LdpUpdateService.CHAVE_PRODUCAO_10).IncluirSubpastasRecursivamente);
        }

        // 27 — PRODUCAO_MODELOS_3D continua recursiva, sem PastasPrincipaisAlternativas. Correção
        // 16/09/2026: "08 - FEDERATED MODEL" passou a ser um caminho-raiz ÚNICO (árvore inteira, um dos 4
        // roots exigidos — 06/08/09/12) — as 3 subpastas específicas de software que existiam antes para
        // "08" foram removidas por ficarem redundantes (a árvore inteira já as inclui). "07 - 3D AUTHORING
        // SOFTWARE" (fora da lista de 4 roots) continua com seus 3 caminhos específicos, inalterado.
        [Fact]
        public void ProducaoModelos3D_TemRootFederatedModelInteiro_SemRedundanciaComOsAntigosDe08()
        {
            var config = Config(LdpUpdateService.CHAVE_PRODUCAO_MODELOS_3D);
            Assert.True(config.IncluirSubpastasRecursivamente);
            Assert.Empty(config.PastasPrincipaisAlternativas);
            Assert.Equal(4, config.CaminhosRaiz.Count);
            Assert.Contains(config.CaminhosRaiz, c => c.Length == 1 && c[0] == "08 - FEDERATED MODEL");
            Assert.DoesNotContain(config.CaminhosRaiz, c => c.Length > 1 && c[0] == "08 - FEDERATED MODEL");
            // "07 - 3D AUTHORING SOFTWARE" (fora da lista de 4 roots) continua com os 3 caminhos específicos.
            Assert.Contains(config.CaminhosRaiz, c => c.SequenceEqual(new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.01 - AUTODESK REVIT" }));
            Assert.Contains(config.CaminhosRaiz, c => c.SequenceEqual(new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.02 - AUTODESK CIVIL 3D" }));
            Assert.Contains(config.CaminhosRaiz, c => c.SequenceEqual(new[] { "07 - 3D AUTHORING SOFTWARE", "07.01.05 - AUTODESK PLANT 3D" }));
        }

        // Correção 16/09/2026 — Etapa 07 recursiva aditiva: a varredura recursiva usada para cobrir
        // arquivos fora das 14 disciplinas fixas (ver ClassificarViaPastaPrincipalAlternativaAsync) reusa
        // ContemPalavraInvalida/NormalizarNomePasta para nunca descer em pastas de versão antiga/backup —
        // mesma regra já usada para não escolher essas pastas como principal/disciplina.
        [Theory]
        [InlineData("SUPERADOS")]
        [InlineData("BACKUP")]
        [InlineData("OLD")]
        [InlineData("ARCHIVE")]
        public void PastaDeVersaoAntiga_NuncaEhAlvoDeVarreduraRecursiva(string nomePastaInvalida)
        {
            Assert.True(LdpUpdateService.ContemPalavraInvalida(LdpUpdateService.NormalizarNomePasta(nomePastaInvalida)));
        }

        [Theory]
        [InlineData("ESTUDOS")]
        [InlineData("EXPORTAÇÃO")]
        [InlineData("Nova pasta")]
        [InlineData("06.01 - PROCESS")]
        public void PastaComNomeValido_PodeSerAlvoDeVarreduraRecursiva(string nomePastaValida)
        {
            Assert.False(LdpUpdateService.ContemPalavraInvalida(LdpUpdateService.NormalizarNomePasta(nomePastaValida)));
        }

        // 28/29/30 — normalização tolerante de pontuação/espaço/acento.
        [Theory]
        [InlineData("10.PRODUCTION")]
        [InlineData("10_PRODUCTION")]
        [InlineData("10 – PRODUCTION")]
        [InlineData("10 -  PRODUCTION")]
        public void NormalizarNomePasta_TolerapontuacaoEspacoETravessao(string variacao)
        {
            Assert.Equal(LdpUpdateService.NormalizarNomePasta("10 - PRODUCTION"), LdpUpdateService.NormalizarNomePasta(variacao));
        }

        [Fact]
        public void NormalizarNomePasta_RemoveAcentos()
        {
            Assert.Equal(LdpUpdateService.NormalizarNomePasta("10 - PRODUCAO"), LdpUpdateService.NormalizarNomePasta("10 - PRODUÇÃO"));
        }

        // 31 — prefixo numérico errado é rejeitado (disciplina de outro prefixo não é aceita).
        [Fact]
        public void ResolverPastaDisciplina_PrefixoErrado_EhRejeitado()
        {
            var conteudo = new List<AccFolderItem> { Pasta("10.02 - MECHANICAL") };
            var resultado = LdpUpdateService.ResolverPastaDisciplina(conteudo, "10.01 - PROCESS", out var pastaEncontrada, out _);

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.NaoEncontrada, resultado);
            Assert.Null(pastaEncontrada);
        }

        [Fact]
        public void ResolverPastaPrincipal_PrefixoErrado_EhRejeitado()
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta("11 - SOMETHING ELSE") };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_10));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.NaoEncontrada, resultado.Resultado);
        }

        // Não recursivo: coleta só arquivos diretos, subpastas (ex.: SUPERSEDED) são ignoradas — cobre
        // ContemPalavraInvalida para não escolher uma pasta de backup por engano.
        [Fact]
        public void PastaComPalavraInvalida_NuncaEhEscolhidaComoPrincipal()
        {
            var conteudoRaiz = new List<AccFolderItem> { Pasta("10 - PRODUCTION OLD") };
            var resultado = LdpUpdateService.ResolverPastaPrincipal(conteudoRaiz, Config(LdpUpdateService.CHAVE_PRODUCAO_10));

            Assert.Equal(LdpUpdateService.ResultadoBuscaPasta.NaoEncontrada, resultado.Resultado);
        }
    }
}
