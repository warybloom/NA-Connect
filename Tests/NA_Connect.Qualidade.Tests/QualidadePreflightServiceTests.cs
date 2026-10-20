using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using APS.WebApp.Models;
using APS.WebApp.Services.Qualidade;
using NA_Connect.Qualidade.Tests.TestHelpers;

namespace NA_Connect.Qualidade.Tests
{
    public class QualidadePreflightServiceTests
    {
        private static QualidadePreflightService NovoServico(ILogger<QualidadePreflightService> logger = null) =>
            new QualidadePreflightService(logger ?? NullLogger<QualidadePreflightService>.Instance);

        private static ExcelPackage PacoteComAbaDesvio(out ExcelWorksheet ws)
        {
            var package = WorkbookHelper.NovoPacote();
            ws = WorkbookHelper.AdicionarAba(package, "DESVIO DE QUALIDADE");
            WorkbookHelper.EscreverLinha(ws, 1, "PROJETO", "CR", "NÚMERO DO DOCUMENTO", "DESCRIÇÃO DA INCONSISTÊNCIA", "QTD. DE DOCUMENTOS");
            return package;
        }

        private static ExcelPackage PacoteComAbaRnc(out ExcelWorksheet ws)
        {
            var package = WorkbookHelper.NovoPacote();
            ws = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(ws, 1, "DATA REGISTRO", "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO", "DIAS EM ABERTO");
            return package;
        }

        private static ExcelPackage PacoteComAbaBaseLancamento(out ExcelWorksheet ws)
        {
            var package = WorkbookHelper.NovoPacote();
            ws = WorkbookHelper.AdicionarAba(package, "BASE LANÇAMENTO");
            WorkbookHelper.EscreverLinha(ws, 1, "CLIENTE", "CR", "CÓDIGO N&A", "DESCRICAO", "MES");
            return package;
        }

        [Fact]
        public void PrepararTabela_LinhaCompletamenteVazia_NaoRecebePreenchimentoENaoViraRegistro()
        {
            using var package = PacoteComAbaDesvio(out var ws);
            // Linha 2 fica totalmente vazia; linha 3 é a única com dado real.
            WorkbookHelper.EscreverLinha(ws, 3, "Projeto XPTO", "CR-001", "DOC-001", "Inconsistência real", 1);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasVazias);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(0, resultado.LinhasComPreenchimentoAutomatico);
            Assert.DoesNotContain(resultado.Tabela.Rows.Cast<System.Data.DataRow>(), r => (string)r["Projeto"] == QualidadeCabecalhos.ValorCampoObrigatorioAusente);
        }

        [Fact]
        public void PrepararTabela_LinhaPlaceholder_NaoRecebePreenchimentoENaoViraRegistro()
        {
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "-", "-", "-", "-", "-");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasPlaceholder);
            Assert.Equal(0, resultado.LinhasValidas);
            Assert.Equal(0, resultado.LinhasComPreenchimentoAutomatico);
            Assert.Equal(QualidadeCodigosResultado.SemLinhasValidas, resultado.CodigoResultado);
            Assert.Null(resultado.Tabela);
        }

        [Fact]
        public void PrepararTabela_LinhaRealComUmCampoObrigatorioVazio_RecebeFaltaInformacaoEPermaneceValida()
        {
            // Reproduz a linha 98 real da BML011.013: só NumeroDocumento vazio, demais obrigatórios OK.
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto XPTO", "CR-098", null, "Inconsistência da linha 98", 2);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.True(resultado.SucessoComAlertas);
            Assert.Equal(QualidadeCodigosResultado.SucessoComAlertas, resultado.CodigoResultado);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(1, resultado.LinhasComPreenchimentoAutomatico);
            Assert.Equal(1, resultado.CamposPreenchidosAutomaticamente);
            Assert.Equal(0, resultado.LinhasRejeitadas);

            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, linha["NumeroDocumento"]);
            Assert.Equal("Projeto XPTO", linha["Projeto"]);

            var preenchido = Assert.Single(resultado.CamposPreenchidosDetalhe);
            Assert.Equal("NumeroDocumento", preenchido.CampoLogico);
            Assert.Equal(2, preenchido.NumeroLinha);
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, preenchido.ValorAplicado);
        }

        [Fact]
        public void PrepararTabela_LinhaRealComVariosCamposObrigatoriosVazios_RecebeFaltaInformacaoEmTodos()
        {
            // Reproduz a linha 412 real: Projeto, CR e NumeroDocumento vazios; DescricaoInconsistencia OK.
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, null, null, null, "Inconsistência da linha 412", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.True(resultado.SucessoComAlertas);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(1, resultado.LinhasComPreenchimentoAutomatico);
            Assert.Equal(3, resultado.CamposPreenchidosAutomaticamente);

            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, linha["Projeto"]);
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, linha["CR"]);
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, linha["NumeroDocumento"]);
            Assert.Equal("Inconsistência da linha 412", linha["DescricaoInconsistencia"]);

            Assert.Equal(3, resultado.CamposPreenchidosDetalhe.Count);
            Assert.All(resultado.CamposPreenchidosDetalhe, c => Assert.Equal(2, c.NumeroLinha));
            Assert.Equal(new[] { "Projeto", "CR", "NumeroDocumento" }, resultado.CamposPreenchidosDetalhe.Select(c => c.CampoLogico));
        }

        [Fact]
        public void PrepararTabela_CamposDuasLinhasComPreenchimento_GeraDoisAjustesEQuatroCamposComoNoArquivoReal()
        {
            // Combina as duas linhas reais (98 e 412) no mesmo preflight — resultado esperado do pedido:
            // LinhasValidasComPreenchimentoAutomatico=2, CamposPreenchidosAutomaticamente=4.
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto XPTO", "CR-098", null, "Inconsistência 98", 2); // linha 98: 1 campo
            WorkbookHelper.EscreverLinha(ws, 3, null, null, null, "Inconsistência 412", null); // linha 412: 3 campos

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(2, resultado.LinhasValidas);
            Assert.Equal(2, resultado.LinhasComPreenchimentoAutomatico);
            Assert.Equal(4, resultado.CamposPreenchidosAutomaticamente);
            Assert.Equal(0, resultado.LinhasRejeitadas);
        }

        [Fact]
        public void PrepararTabela_CampoOpcionalVazio_ContinuaNulo()
        {
            using var package = PacoteComAbaDesvio(out var ws);
            // QuantidadeDocumentos (opcional, tipado inteiro) fica vazio — não é obrigatório em
            // DesvioQualidadeObrigatorios, então nunca recebe "FALTA INFORMAÇÃO".
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto XPTO", "CR-001", "DOC-001", "Descrição real", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(0, resultado.LinhasComPreenchimentoAutomatico);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(DBNull.Value, linha["QuantidadeDocumentos"]);
        }

        [Fact]
        public void PrepararTabela_CampoDeDataVazio_ContinuaNuloSemBloquearALinha()
        {
            using var package = PacoteComAbaRnc(out var ws);
            // DataRegistro (tipada Data, opcional) vazia; demais obrigatórios preenchidos.
            WorkbookHelper.EscreverLinha(ws, 2, null, "FORM-001", "Projeto XPTO", "CR-001", "Situação real", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(0, resultado.LinhasRejeitadas);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(DBNull.Value, linha["DataRegistro"]);
        }

        [Fact]
        public void PrepararTabela_CampoNumericoVazio_ContinuaNuloSemBloquearALinha()
        {
            using var package = PacoteComAbaRnc(out var ws);
            // DiasEmAberto (tipado Inteiro, opcional) vazio.
            WorkbookHelper.EscreverLinha(ws, 2, "13/01/2026", "FORM-001", "Projeto XPTO", "CR-001", "Situação real", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(DBNull.Value, linha["DiasEmAberto"]);
        }

        [Fact]
        public void PrepararTabela_TextoInvalidoEmCampoDeData_RejeitaSomenteALinha()
        {
            using var package = PacoteComAbaRnc(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "não é uma data válida", "FORM-001", "Projeto XPTO", "CR-001", "Situação com data inválida", null);
            WorkbookHelper.EscreverLinha(ws, 3, "13/01/2026", "FORM-002", "Projeto XPTO", "CR-002", "Situação válida", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasRejeitadas);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Contains(resultado.Erros, e => e.Contains("Linha 2"));

            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal("FORM-002", linha["CodigoFormulario"]);
        }

        [Fact]
        public void PrepararTabela_TextoInvalidoEmCampoNumerico_RejeitaSomenteALinha()
        {
            using var package = PacoteComAbaRnc(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "13/01/2026", "FORM-001", "Projeto XPTO", "CR-001", "Situação com número inválido", "abc");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasRejeitadas);
            Assert.Equal(0, resultado.LinhasValidas);
            Assert.Equal(QualidadeCodigosResultado.SemLinhasValidas, resultado.CodigoResultado);
        }

        [Fact]
        public void PrepararTabela_PreenchimentoAutomatico_RegistraWarningNuncaError()
        {
            var logger = new CapturingLogger<QualidadePreflightService>();
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto XPTO", "CR-098", null, "Inconsistência 98", 2);

            var resultado = NovoServico(logger).PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            var entradaPreenchimento = Assert.Single(logger.Entradas, e => e.Message.Contains("[QUALIDADE][CAMPO_PREENCHIDO_AUTOMATICAMENTE]"));
            Assert.Equal(LogLevel.Warning, entradaPreenchimento.Level);
            Assert.DoesNotContain(logger.Entradas, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public void PrepararTabela_SemLinhasValidas_NaoEhErroEPreservaDadosAnteriores()
        {
            using var package = PacoteComAbaDesvio(out _);
            // Nenhuma linha de dado abaixo do cabeçalho.

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.False(resultado.SucessoComAlertas);
            Assert.Equal(QualidadeCodigosResultado.SemLinhasValidas, resultado.CodigoResultado);
            Assert.Null(resultado.Tabela);
        }

        [Fact]
        public void PrepararTabela_LinhaSemAjuste_CodigoResultadoEhSucessoSimples()
        {
            using var package = PacoteComAbaDesvio(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Projeto XPTO", "CR-001", "DOC-001", "Descrição completa", 1);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.DesvioQualidade(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.False(resultado.SucessoComAlertas);
            Assert.Equal(QualidadeCodigosResultado.Sucesso, resultado.CodigoResultado);
        }

        [Fact]
        public void PrepararTabela_CodigoNaVazio_RecebeFaltaInformacaoEPermaneceValida()
        {
            // Achado real: [dbo].[NCR BASE LANCAMENTO].[CodigoNA] é NOT NULL — a linha (com Cliente/CR/
            // Descricao preenchidos, mas CÓDIGO N&A ainda não atribuído) precisa do preenchimento
            // automático, não pode virar DBNull nem ser rejeitada.
            using var package = PacoteComAbaBaseLancamento(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Cliente Alfa", "CR-001", null, "Descrição real", "JAN");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.True(resultado.SucessoComAlertas);
            Assert.Equal(0, resultado.LinhasRejeitadas);
            Assert.Equal(1, resultado.LinhasValidas);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(QualidadeCabecalhos.ValorCampoObrigatorioAusente, linha["CodigoNA"]);
            Assert.NotEqual(DBNull.Value, linha["CodigoNA"]);
        }

        [Fact]
        public void PrepararTabela_MesComAbreviacaoReal_EhConvertidoParaNumero()
        {
            // Reproduz o achado real: coluna SQL [Mes] é TINYINT, Excel usa "JAN".
            using var package = PacoteComAbaBaseLancamento(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Cliente Alfa", "CR-001", "NA-001", "Descrição real", "JAN");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(0, resultado.LinhasRejeitadas);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(1, linha["Mes"]);
            Assert.Equal(typeof(int), resultado.Tabela.Columns["Mes"].DataType);
        }

        [Fact]
        public void PrepararTabela_MesComNumeroValido_EhAceitoDireto()
        {
            using var package = PacoteComAbaBaseLancamento(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Cliente Alfa", "CR-001", "NA-001", "Descrição real", "12");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(12, linha["Mes"]);
        }

        [Fact]
        public void PrepararTabela_MesVazio_ContinuaNuloSemBloquearALinha()
        {
            using var package = PacoteComAbaBaseLancamento(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Cliente Alfa", "CR-001", "NA-001", "Descrição real", null);

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(0, resultado.LinhasRejeitadas);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(DBNull.Value, linha["Mes"]);
        }

        [Fact]
        public void PrepararTabela_MesNaoReconhecido_GravaNuloComWarningENuncaRejeitaALinha()
        {
            var logger = new CapturingLogger<QualidadePreflightService>();
            using var package = PacoteComAbaBaseLancamento(out var ws);
            WorkbookHelper.EscreverLinha(ws, 2, "Cliente Alfa", "CR-001", "NA-001", "Descrição real", "XYZ");

            var resultado = NovoServico(logger).PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            Assert.Equal(0, resultado.LinhasRejeitadas);
            Assert.Equal(1, resultado.LinhasValidas);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal(DBNull.Value, linha["Mes"]);

            var entrada = Assert.Single(logger.Entradas, e => e.Message.Contains("[QUALIDADE][CAMPO_CONVERSAO_NAO_RECONHECIDA]"));
            Assert.Equal(LogLevel.Warning, entrada.Level);
        }

        [Fact]
        public void PrepararTabela_AbaAusente_RetornaErroEstruturalSemAfetarOutrasAbas()
        {
            // Arquivo só com RNC — Base e Desvio ausentes. Cada PrepararTabela é chamado isoladamente:
            // a ausência da Base não impede preparar a RNC presente.
            using var package = WorkbookHelper.NovoPacote();
            var wsRnc = WorkbookHelper.AdicionarAba(package, "RNC");
            WorkbookHelper.EscreverLinha(wsRnc, 1, "DATA REGISTRO", "CÓDIGO DO FORMULÁRIO", "PROJETO", "CR", "DESCRIÇÃO DA SITUAÇÃO", "DIAS EM ABERTO");
            WorkbookHelper.EscreverLinha(wsRnc, 2, "13/01/2026", "FORM-001", "Projeto XPTO", "CR-001", "Situação real", 1);

            var servico = NovoServico();

            var resultadoBase = servico.PrepararTabela(package, QualidadeTabelaDefinicoes.BaseLancamento(), "PROJ-1");
            var resultadoRnc = servico.PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.False(resultadoBase.Sucesso);
            Assert.Equal(QualidadeCodigosResultado.ErroEstrutural, resultadoBase.CodigoResultado);

            Assert.True(resultadoRnc.Sucesso);
            Assert.Equal(1, resultadoRnc.LinhasValidas);
        }

        [Fact]
        public void PrepararTabela_NenhumaColunaUsaMapeamentoOrdinal_OrdemFisicaDiferenteNaoAlteraValores()
        {
            using var package = WorkbookHelper.NovoPacote();
            var wsRnc = WorkbookHelper.AdicionarAba(package, "RNC");
            // Ordem física deliberadamente embaralhada.
            WorkbookHelper.EscreverLinha(wsRnc, 1, "DESCRIÇÃO DA SITUAÇÃO", "CR", "PROJETO", "CÓDIGO DO FORMULÁRIO");
            WorkbookHelper.EscreverLinha(wsRnc, 2, "Situação embaralhada", "CR-999", "Projeto Embaralhado", "FORM-999");

            var resultado = NovoServico().PrepararTabela(package, QualidadeTabelaDefinicoes.Rnc(), "PROJ-1");

            Assert.True(resultado.Sucesso);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal("Situação embaralhada", (string)linha["DescricaoSituacao"]);
            Assert.Equal("CR-999", (string)linha["CR"]);
            Assert.Equal("Projeto Embaralhado", (string)linha["Projeto"]);
            Assert.Equal("FORM-999", (string)linha["CodigoFormulario"]);
        }

        private static APS.WebApp.Models.ResultadoTabelaQualidade ResultadoOk(bool comAlertas = false) =>
            new APS.WebApp.Models.ResultadoTabelaQualidade { Sucesso = true, SucessoComAlertas = comAlertas };

        private static APS.WebApp.Models.ResultadoTabelaQualidade ResultadoErro() =>
            new APS.WebApp.Models.ResultadoTabelaQualidade { Sucesso = false, SucessoComAlertas = false };

        [Fact]
        public void DeterminarStatusGeral_TodasComSucessoSemAlertas_RetornaSucesso()
        {
            var resultados = new[] { ResultadoOk(), ResultadoOk(), ResultadoOk() };
            Assert.Equal("CONCLUIDO COM SUCESSO", NA_Connect.Services.QualityControlSyncService.DeterminarStatusGeral(resultados));
        }

        [Fact]
        public void DeterminarStatusGeral_AlgumaComAlertas_RetornaAlertas()
        {
            var resultados = new[] { ResultadoOk(), ResultadoOk(comAlertas: true), ResultadoOk() };
            Assert.Equal("CONCLUIDO COM ALERTAS", NA_Connect.Services.QualityControlSyncService.DeterminarStatusGeral(resultados));
        }

        [Fact]
        public void DeterminarStatusGeral_AlgumaFalhouOutrasOk_RetornaParcialmente()
        {
            var resultados = new[] { ResultadoOk(), ResultadoErro(), ResultadoOk() };
            Assert.Equal("CONCLUIDO PARCIALMENTE", NA_Connect.Services.QualityControlSyncService.DeterminarStatusGeral(resultados));
        }

        [Fact]
        public void DeterminarStatusGeral_TodasFalharam_RetornaComErros()
        {
            var resultados = new[] { ResultadoErro(), ResultadoErro(), ResultadoErro() };
            Assert.Equal("CONCLUIDO COM ERROS", NA_Connect.Services.QualityControlSyncService.DeterminarStatusGeral(resultados));
        }
    }
}
