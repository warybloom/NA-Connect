using Microsoft.Extensions.Logging.Abstractions;
using APS.WebApp.Services.Planning;
using NA_Connect.Planning.Tests.TestHelpers;

namespace NA_Connect.Planning.Tests
{
    public class PlanningHeaderResolverTests
    {
        private static readonly NullLogger Logger = NullLogger.Instance;

        [Fact]
        public void Resolver_CabecalhoNaLinha1_EhAceito()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 1, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolved = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);
            Assert.Equal(1, resolved.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoNaLinha2_EhAceito()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 2, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolved = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);
            Assert.Equal(2, resolved.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoNaLinha3_EhAceito()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.Equal(3, resolvido.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoNaLinha10_EhAceito()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 10, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.Equal(10, resolvido.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoAposLinha10_EhRejeitado()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 11, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var ex = Assert.Throws<PlanningLayoutException>(() =>
                PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger));

            Assert.Equal("PADRAO_INCORRETO", ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_DuasLinhasComMesmaPontuacao_GeraAmbiguidade()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 4, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 7, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var ex = Assert.Throws<PlanningLayoutException>(() =>
                PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger));

            Assert.Equal(PlanningHeaderResolver.CodigoAmbiguo, ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_CabecalhoParcial_NaoEhAceitoComoCandidato()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            // Faltam Nº DO DESVIO, DATA e STATUS (obrigatórios) — só ITEM e DESCRIÇÃO não bastam.
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO");

            var ex = Assert.Throws<PlanningLayoutException>(() =>
                PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger));

            Assert.Equal(PlanningHeaderResolver.CodigoNaoEncontrado, ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_ColunaDeslocada_ContinuaLocalizadaPeloNome()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            // Ordem física deliberadamente diferente da ordem "natural" dos campos.
            WorkbookHelper.EscreverLinha(ws, 5, "STATUS", "Nº DO DESVIO", "DATA", "DESCRIÇÃO", "ITEM");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.Equal(1, resolvido.Mapa["Status"]);
            Assert.Equal(2, resolvido.Mapa["NumeroDesvio"]);
            Assert.Equal(3, resolvido.Mapa["Data"]);
            Assert.Equal(4, resolvido.Mapa["Descricao"]);
            Assert.Equal(5, resolvido.Mapa["Item"]);
        }

        [Fact]
        public void Resolver_AcentoNaoImpedeLocalizacao()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            // "DESCRICAO" sem acento é um alias explícito do campo Descricao.
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRICAO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.True(resolvido.Mapa.ContainsKey("Descricao"));
        }

        [Fact]
        public void Resolver_QuebraDeLinhaNoCabecalho_NaoImpedeLocalizacao()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 3, "ITEM\n", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.True(resolvido.Mapa.ContainsKey("Item"));
        }

        [Fact]
        public void Resolver_NumeroDoDesvioNaoConfundeComDesvioInterno()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            var definicao = PlanningDefinicoes.ScopeDeviation();

            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS", "DESVIO INTERNO");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.True(resolvido.Mapa.ContainsKey("NumeroDesvio"));
            Assert.True(resolvido.Mapa.ContainsKey("DesvioInterno"));
            Assert.NotEqual(resolvido.Mapa["NumeroDesvio"], resolvido.Mapa["DesvioInterno"]);
        }

        [Fact]
        public void Resolver_HeadersOficiaisName2Company3_SaoDistintos()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            var definicao = PlanningDefinicoes.External();

            // NAME e COMPANY aparecem fisicamente duas vezes — a primeira ocorrência pertence a
            // Nome/Empresa (quem abriu), a segunda a NomeResponsavel/EmpresaResponsavel (responsável).
            WorkbookHelper.EscreverLinha(
                ws, 3,
                "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "Name2", "Company3", "CYCLE", "AFE");

            var resolvido = PlanningHeaderResolver.Resolver(ws, definicao.ProcessName, definicao, Logger);

            Assert.Equal(3, resolvido.Mapa["Name"]);
            Assert.Equal(4, resolvido.Mapa["Company"]);
            Assert.Equal(7, resolvido.Mapa["Name2"]);
            Assert.Equal(8, resolvido.Mapa["Company3"]);
        }
    }
}
