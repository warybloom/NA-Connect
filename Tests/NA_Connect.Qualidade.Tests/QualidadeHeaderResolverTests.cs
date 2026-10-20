using OfficeOpenXml;
using APS.WebApp.Services.Qualidade;
using NA_Connect.Qualidade.Tests.TestHelpers;

namespace NA_Connect.Qualidade.Tests
{
    public class QualidadeHeaderResolverTests
    {
        [Fact]
        public void Resolver_CabecalhoDeslocado_EncontraLinhaCorretaSemDependerDePosicaoFixa()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");

            // Linhas 1 a 4 são título/metadado — o cabeçalho real só aparece na linha 5.
            WorkbookHelper.EscreverLinha(ws, 1, "RELATÓRIO DE NÃO CONFORMIDADE");
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto: XPTO");
            WorkbookHelper.EscreverLinha(ws, 5, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");

            var resolvido = QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo);

            Assert.Equal(5, resolvido.HeaderRow);
            Assert.Equal(1, resolvido.Mapa["CodigoFormulario"]);
            Assert.Equal(2, resolvido.Mapa["Projeto"]);
        }

        [Fact]
        public void Resolver_CabecalhoNaLinha1ComTodosOsCamposMinimos_EhAceito()
        {
            // Diferente das Etapas Planning (3-10), a Qualidade aceita cabeçalho já na linha 1 — algumas
            // planilhas reais de Qualidade não têm nenhuma linha de título acima do cabeçalho.
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 1, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");

            var resolvido = QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo);

            Assert.Equal(1, resolvido.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoNaLinha2_EhAceito()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 1, "RELATÓRIO DE NÃO CONFORMIDADE");
            WorkbookHelper.EscreverLinha(ws, 2, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");

            var resolvido = QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo);

            Assert.Equal(2, resolvido.HeaderRow);
        }

        [Fact]
        public void Resolver_CabecalhoAposLinha10_EhRejeitado()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 11, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");

            var ex = Assert.Throws<QualidadeLayoutException>(() =>
                QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo));

            Assert.Equal(QualidadeHeaderResolver.CodigoNaoEncontrado, ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_NenhumaLinhaComOsCabecalhosMinimos_LancaCabecalhoNaoEncontrado()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 1, "PROJETO", "CR"); // faltam CÓDIGO DO FORMULÁRIO e DESCRIÇÃO DA SITUAÇÃO

            var ex = Assert.Throws<QualidadeLayoutException>(() =>
                QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo));

            Assert.Equal(QualidadeHeaderResolver.CodigoNaoEncontrado, ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_DuasLinhasCandidatas_LancaCabecalhoAmbiguo()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 3, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");
            WorkbookHelper.EscreverLinha(ws, 8, "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO");

            var ex = Assert.Throws<QualidadeLayoutException>(() =>
                QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo));

            Assert.Equal(QualidadeHeaderResolver.CodigoAmbiguo, ex.CodigoResultado);
        }

        [Fact]
        public void Resolver_ResponsavelNaoCasaComResponsavelAcao_MapeiaColunasDistintas()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(
                ws, 3,
                "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO", "RESPONSÁVEL", "RESPONSÁVEL AÇÃO");

            var resolvido = QualidadeHeaderResolver.Resolver(ws, "RNC", QualidadeCabecalhos.Rnc, QualidadeCabecalhos.RncCabecalhoMinimo);

            Assert.True(resolvido.Mapa.ContainsKey("Responsavel"));
            Assert.True(resolvido.Mapa.ContainsKey("ResponsavelAcao"));
            Assert.NotEqual(resolvido.Mapa["Responsavel"], resolvido.Mapa["ResponsavelAcao"]);
            Assert.Equal(5, resolvido.Mapa["Responsavel"]);
            Assert.Equal(6, resolvido.Mapa["ResponsavelAcao"]);
        }

        [Fact]
        public void Resolver_NuncaMapeiaPorPosicaoFisicaQuandoColunasEstaoForaDeOrdem()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "DESVIO DE QUALIDADE");

            // Ordem física deliberadamente diferente da ordem "natural" dos campos.
            WorkbookHelper.EscreverLinha(
                ws, 3,
                "DESCRIÇÃO DA INCONSISTÊNCIA", "NÚMERO DO DOCUMENTO", "CR", "PROJETO");

            var resolvido = QualidadeHeaderResolver.Resolver(
                ws, "DESVIO DE QUALIDADE", QualidadeCabecalhos.DesvioQualidade, QualidadeCabecalhos.DesvioQualidadeCabecalhoMinimo);

            Assert.Equal(1, resolvido.Mapa["DescricaoInconsistencia"]);
            Assert.Equal(2, resolvido.Mapa["NumeroDocumento"]);
            Assert.Equal(3, resolvido.Mapa["CR"]);
            Assert.Equal(4, resolvido.Mapa["Projeto"]);
        }
    }
}
