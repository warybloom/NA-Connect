using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using APS.WebApp.Services.Planning;
using NA_Connect.Planning.Tests.TestHelpers;

namespace NA_Connect.Planning.Tests
{
    public class PlanningPreflightServiceTests
    {
        private static PlanningPreflightService NovoServico() =>
            new PlanningPreflightService(NullLogger<PlanningPreflightService>.Instance);

        [Fact]
        public void ExecutarPreflight_AbaComNomeNaoSuportado_RetornaTemplateDeAbaNaoSuportado()
        {
            using var package = WorkbookHelper.NovoPacote();
            WorkbookHelper.AdicionarAba(package, "CAP");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.External(), "PROJ-1", "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG", "arquivo.xlsx");

            Assert.False(resultado.Sucesso);
            Assert.Equal("PADRAO_INCORRETO", resultado.CodigoResultado);
        }

        [Fact]
        public void ExecutarPreflight_External_SelecionaActionItemList()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            WorkbookHelper.EscreverLinha(ws, 3, "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "01/01/2026", "Fulano", "Empresa X", "Descrição do item", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.External(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal("ACTION ITEM LIST", resultado.AbaSelecionada);
            Assert.Equal(1, resultado.LinhasValidas);
        }

        [Fact]
        public void ExecutarPreflight_Scope_SelecionaControle()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "Descrição do desvio", "DESV-001", "01/01/2026", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.ScopeDeviation(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal("CONTROLE", resultado.AbaSelecionada);
        }

        [Fact]
        public void ExecutarPreflight_Hold_SelecionaControle()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "Nº HOLD", "Nº N&A DO DOCUMENTO", "TÍTULO DO DOCUMENTO", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "HOLD-001", "NA-DOC-001", "Título do documento", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.HoldControl(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal("CONTROLE", resultado.AbaSelecionada);
        }

        [Fact]
        public void ExecutarPreflight_LinhaComTraco_EhIgnorada()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "-", "-", "-", "-", "-");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.ScopeDeviation(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(0, resultado.LinhasValidas);
            Assert.Single(resultado.LinhasIgnoradas);
        }

        [Fact]
        public void ExecutarPreflight_LinhaComNA_PreservaTextoPreenchido()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "Nº HOLD", "Nº N&A DO DOCUMENTO", "TÍTULO DO DOCUMENTO", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "N/A", "N/A", "N/A", "N/A", "N/A");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.HoldControl(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal("N/A", resultado.Tabela.Rows[0]["ITEM"]);
        }

        [Fact]
        public void ExecutarPreflight_FormulaSemResultadoNoIdentificador_LinhaEhIgnorada()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            WorkbookHelper.EscreverLinha(ws, 3, "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3");

            // ID (identificador) é uma fórmula sem resultado calculado — nunca deve virar "0" ou vazio
            // silencioso. DESCRIPTION e STATUS (também identificadores) ficam vazios; NAME (não é
            // identificador) tem conteúdo só para a linha não cair na classificação VAZIA/PLACEHOLDER —
            // nenhum identificador tem conteúdo real, então a linha inteira é ignorada.
            ws.Cells[4, 1].Formula = "A1";
            ws.Cells[4, 3].Value = "Fulano";

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.External(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(DBNull.Value, resultado.Tabela.Rows[0]["ID"]);
        }

        [Fact]
        public void ExecutarPreflight_LinhaValidaAposVariasLinhasVazias_EhEncontrada()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "STATUS");
            // Linhas 4 a 7 ficam totalmente vazias (nada escrito).
            WorkbookHelper.EscreverLinha(ws, 8, "1", "Descrição do desvio", "DESV-001", "01/01/2026", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.ScopeDeviation(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal("1", resultado.Tabela.Rows[0]["ITEM"]);
        }

        [Fact]
        public void ExecutarPreflight_NenhumaColunaUsaMapeamentoOrdinal_OrdemFisicaDiferenteNaoAlteraValores()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            // Ordem física embaralhada.
            WorkbookHelper.EscreverLinha(ws, 3, "STATUS", "Nº DO DESVIO", "DATA", "DESCRIÇÃO", "ITEM");
            WorkbookHelper.EscreverLinha(ws, 4, "Aberto", "DESV-777", "01/01/2026", "Descrição embaralhada", "7");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.ScopeDeviation(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal("7", linha["ITEM"]);
            Assert.Equal("DESV-777", linha["N_DO_DESVIO"]);
            Assert.Equal("Descrição embaralhada", linha["DESCRICAO"]);
        }

        [Fact]
        public void ExecutarPreflight_ScheduleEReschedule_RejeitamTextoQueNaoEhData()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            WorkbookHelper.EscreverLinha(ws, 3, "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "SCHEDULE", "RESCHEDULE", "CYCLE", "AFE");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "01/01/2026", "Fulano", "Empresa X", "Descrição do item", "Aberto", "TESTE", "TESTE");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.Internal(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Single(resultado.Tabela.Rows);
            Assert.Equal(DBNull.Value, resultado.Tabela.Rows[0]["Schedule"]);
            Assert.Equal(1, resultado.RowsWithErrors);
        }

        [Fact]
        public void ExecutarPreflight_DataInvalidaNaoEhPersistidaComoTexto()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            WorkbookHelper.EscreverLinha(ws, 3, "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "não é uma data válida", "Fulano", "Empresa X", "Descrição", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.External(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(1, resultado.LinhasValidas);
            Assert.Equal(DBNull.Value, resultado.Tabela.Rows[0]["Date"]);
            Assert.Equal(1, resultado.RowsWithErrors);
            Assert.Equal(typeof(string), resultado.Tabela.Columns["Date"].DataType);
        }

        [Fact]
        public void ExecutarPreflight_DataEQuantHh_SaoPreservadosComoTexto()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "DESCRIÇÃO", "Nº DO DESVIO", "DATA", "QUANT. HH", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "Descrição do desvio", "DESV-001", "02/08/2026", "150", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.ScopeDeviation(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            var linha = resultado.Tabela.Rows.Cast<System.Data.DataRow>().Single();
            Assert.Equal("2026-08-02", linha["DATA"]);
            Assert.Equal("150", linha["QUANT_HH"]);
            Assert.Equal(typeof(string), resultado.Tabela.Columns["DATA"].DataType);
            Assert.Equal(typeof(string), resultado.Tabela.Columns["QUANT_HH"].DataType);
        }

        [Fact]
        public void ExecutarPreflight_ProjectIdEDataSincronizacao_SaoPreenchidosEIguaisEmTodasAsLinhas()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "CONTROLE");
            WorkbookHelper.EscreverLinha(ws, 3, "ITEM", "Nº HOLD", "Nº N&A DO DOCUMENTO", "TÍTULO DO DOCUMENTO", "STATUS");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "HOLD-001", "NA-DOC-001", "Título 1", "Aberto");
            WorkbookHelper.EscreverLinha(ws, 5, "2", "HOLD-002", "NA-DOC-002", "Título 2", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.HoldControl(), "b.PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.Equal(2, resultado.LinhasValidas);
            var linhas = resultado.Tabela.Rows.Cast<System.Data.DataRow>().ToList();
            Assert.All(linhas, l => Assert.Equal("b.PROJ-1", l["Project_ID_ACC"]));
            Assert.Single(linhas.Select(l => (System.DateTime)l["Data_Sincronizacao"]).Distinct());
        }

        [Fact]
        public void ExecutarPreflight_IdRegistro_NuncaEhColunaDoDataTable()
        {
            using var package = WorkbookHelper.NovoPacote();
            var ws = WorkbookHelper.AdicionarAba(package, "ACTION ITEM LIST");
            WorkbookHelper.EscreverLinha(ws, 3, "ID", "DATE", "NAME", "COMPANY", "DESCRIPTION", "STATUS", "CYCLE", "AFE", "Name2", "Company3");
            WorkbookHelper.EscreverLinha(ws, 4, "1", "01/01/2026", "Fulano", "Empresa X", "Descrição", "Aberto");

            var resultado = NovoServico().ExecutarPreflight(package, PlanningDefinicoes.External(), "PROJ-1", "pasta", "arquivo.xlsx");

            Assert.True(resultado.Sucesso);
            Assert.False(resultado.Tabela.Columns.Contains("Id_Registro"));
        }
    }
}
