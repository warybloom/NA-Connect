using System.IO;
using System.Linq;
using APS.WebApp.Models;
using APS.WebApp.Services.GestaoVista;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.GestaoVista.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.GestaoVista.Tests
{
    /// <summary>
    /// Cobre a extração pura (sem banco) das 3 abas internas da LDP: enumeração de TODAS as worksheets
    /// (Visible/Hidden/VeryHidden), localização dinâmica de cabeçalho, tolerância a aba ausente/CUSTO
    /// ignorada, e as regras de cálculo/normalização de cada campo (seção 16 do pedido).
    /// </summary>
    public class GestaoVistaExtractorTests
    {
        /// <summary>Nome do placeholder adicionado por <see cref="Processar"/> quando o teste só monta
        /// abas Hidden/VeryHidden (a auditoria real confirma que toda LDP real também tem abas visíveis,
        /// como CAPA/LDP — mas o EPPlus recusa salvar um workbook sem NENHUMA aba visível, então os testes
        /// que exercitam só abas ocultas precisam desse placeholder para conseguir serializar).</summary>
        private const string PlaceholderAbaVisivel = "__PlaceholderVisivel__";

        private static GestaoVistaExtracaoResultado Processar(ExcelPackage pkg, string projectIdAcc = "PROJ-TESTE")
        {
            if (!pkg.Workbook.Worksheets.Any(w => w.Hidden == eWorkSheetHidden.Visible))
            {
                pkg.Workbook.Worksheets.Add(PlaceholderAbaVisivel);
            }

            using var ms = new MemoryStream(pkg.GetAsByteArray());
            var extractor = new GestaoVistaExtractor(NullLogger<GestaoVistaExtractor>.Instance);
            return extractor.Processar(ms, projectIdAcc, "teste.xlsx");
        }

        [Fact]
        public void EnumeraAbaVeryHidden_EncontraTimesheet()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var wsTs = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, eWorkSheetHidden.VeryHidden);
            GestaoVistaWorkbookHelper.EscreverLinha(wsTs, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");

            var resultado = Processar(pkg);

            var diag = Assert.Single(resultado.Worksheets, d => d.NomeAba == "Timesheet");
            Assert.Equal("VeryHidden", diag.Visibilidade);
            Assert.True(diag.Encontrada);
            Assert.Single(resultado.Timesheet);
        }

        [Fact]
        public void EnumeraAbaHidden_EncontraCustoReal()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var wsCr = GestaoVistaWorkbookHelper.MontarCustoReal(pkg, eWorkSheetHidden.Hidden);
            GestaoVistaWorkbookHelper.EscreverLinha(wsCr, 2, "Engenheiro", "Elétrica", "Ana", 1000m, 60m, 50m, 20m, 1130m, 6.42m);

            var resultado = Processar(pkg);

            var diag = Assert.Single(resultado.Worksheets, d => d.NomeAba == "Custo Real");
            Assert.Equal("Hidden", diag.Visibilidade);
            Assert.True(diag.Encontrada);
            Assert.Single(resultado.CustoReal);
        }

        [Fact]
        public void AbaCusto_EhReconhecidaEIgnorada_NuncaUsadaComoFonte()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            GestaoVistaWorkbookHelper.MontarCusto(pkg);

            var resultado = Processar(pkg);

            var diag = Assert.Single(resultado.Worksheets, d => d.NomeAba == "CUSTO");
            Assert.Equal("CUSTO_IGNORADA", diag.PapelIdentificado);
            Assert.False(diag.Encontrada);
            Assert.Empty(resultado.CustoReal);
            Assert.Empty(resultado.CustoPrevisto);
            Assert.Empty(resultado.Timesheet);
        }

        [Fact]
        public void AbaObrigatoriaAusente_ProduzDiagnosticoControlado_NuncaExcecao()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            // Nenhuma aba Timesheet/CustoReal/CustoProfissionais — só uma aba qualquer, irrelevante.
            GestaoVistaWorkbookHelper.AdicionarAba(pkg, "Outra Aba");

            var resultado = Processar(pkg);

            Assert.Empty(resultado.Erros); // aba ausente é aviso, nunca erro crítico.
            Assert.Contains(resultado.Avisos, a => a.Contains("Timesheet"));
            Assert.Contains(resultado.Avisos, a => a.Contains("Custo Real"));
            Assert.Contains(resultado.Avisos, a => a.Contains("Custo profissionais"));
            Assert.Empty(resultado.Timesheet);
            Assert.Empty(resultado.CustoReal);
            Assert.Empty(resultado.CustoPrevisto);
        }

        [Fact]
        public void TimesheetHeaderDinamico_NaoAssumeLinha1()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, linhaCabecalho: 4);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 5, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");

            var resultado = Processar(pkg);

            var diag = resultado.Worksheets.Single(d => d.NomeAba == "Timesheet");
            Assert.Equal(4, diag.LinhaCabecalho);
            Assert.Single(resultado.Timesheet);
        }

        [Fact]
        public void CustoProfissionaisHeaderNaLinha2_ComoNaLdpReal()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoProfissionais(pkg, linhaCabecalho: 2);
            ws.Cells[3, 1].Value = "Engenheiro Sênior";
            ws.Cells[3, 2].Value = "ENGINEER"; // GrupoFuncao — sem cabeçalho, mas com dado
            ws.Cells[3, 3].Value = "Elétrica";
            ws.Cells[3, 5].Value = 1200m;
            ws.Cells[3, 6].Value = 72m;

            var resultado = Processar(pkg);

            var diag = resultado.Worksheets.Single(d => d.NomeAba == "Custo profissionais");
            Assert.Equal(2, diag.LinhaCabecalho);
            var registro = Assert.Single(resultado.CustoPrevisto);
            Assert.Equal("Engenheiro Sênior", registro.Funcao);
            Assert.Equal("ENGINEER", registro.GrupoFuncao);
            Assert.Equal("Elétrica", registro.Disciplina);
        }

        [Fact]
        public void CustoProfissionais_BlocoMix_EhDetectadoENaoImportado()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoProfissionais(pkg, linhaCabecalho: 2);
            ws.Cells[3, 1].Value = "Engenheiro";
            ws.Cells[3, 3].Value = "Elétrica";
            ws.Cells[3, 5].Value = 1000m;
            ws.Cells[4, 1].Value = "MIX"; // início do bloco MIX — não tabular
            ws.Cells[5, 1].Value = "Projetista";
            ws.Cells[5, 3].Value = "Civil";
            ws.Cells[5, 5].Value = 900m;

            var resultado = Processar(pkg);

            Assert.Single(resultado.CustoPrevisto); // só a linha ANTES do MIX
            var diag = resultado.Worksheets.Single(d => d.NomeAba == "Custo profissionais");
            Assert.Contains(diag.Warnings, w => w.Contains("MIX"));
        }

        [Fact]
        public void Timesheet_SemColunaData_DataLancamentoFicaNull()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, incluirData: false);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");

            var resultado = Processar(pkg);

            var lancamento = Assert.Single(resultado.Timesheet);
            Assert.Null(lancamento.DataLancamento);
        }

        [Fact]
        public void Timesheet_ComColunaData_DataLancamentoEhLida()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, incluirData: true);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc", new System.DateTime(2026, 3, 5));

            var resultado = Processar(pkg);

            var lancamento = Assert.Single(resultado.Timesheet);
            Assert.Equal(new System.DateTime(2026, 3, 5), lancamento.DataLancamento);
        }

        [Fact]
        public void Timesheet_ComColunaCusto_CustoInformadoNaPlanilhaEhLidoComoEvidencia()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            ws.Cells[1, 10].Value = "15. CUSTO"; // coluna O na LDP real; posição aqui é irrelevante — resolução é por nome.
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");
            ws.Cells[2, 10].Value = 69.14772727m;

            var resultado = Processar(pkg);

            var lancamento = Assert.Single(resultado.Timesheet);
            Assert.Equal(69.14772727m, lancamento.CustoInformadoNaPlanilha);
        }

        // Correção 2026-09-22 (Etapa 03 ViaPlanilha=0): a Etapa 01 grava "15. CUSTO" como fórmula SUMIF com
        // referência de coluna inteira e o EPPlus salva SEM valor em cache — a leitura tem de avaliar a fórmula.
        [Fact]
        public void Timesheet_ColunaCustoComFormulaSemCache_EhAvaliadaEmMemoria()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var taxas = GestaoVistaWorkbookHelper.AdicionarAba(pkg, "Taxas Teste", eWorkSheetHidden.Hidden);
            taxas.Cells[2, 3].Value = "Ana"; taxas.Cells[2, 9].Value = 12.5;
            taxas.Cells[3, 3].Value = "Bia"; taxas.Cells[3, 9].Value = 20.0;
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            ws.Cells[1, 10].Value = "15. CUSTO";
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 3, "Zé", 10, 4, "Proj X", "Engenheiro", "Elétrica", "DOC-2", "Documento 2", "Desc");
            ws.Cells[2, 10].Formula = $"C2*(SUMIF('Taxas Teste'!C:C, {ws.Name}!A2, 'Taxas Teste'!I:I))";
            ws.Cells[3, 10].Formula = $"C3*(SUMIF('Taxas Teste'!C:C, {ws.Name}!A3, 'Taxas Teste'!I:I))";

            var resultado = Processar(pkg);

            Assert.Equal(2, resultado.Timesheet.Count);
            Assert.Equal(100m, resultado.Timesheet[0].CustoInformadoNaPlanilha); // 8 h × 12,5
            Assert.Equal(0m, resultado.Timesheet[1].CustoInformadoNaPlanilha);   // sem taxa na planilha: a própria fórmula dá 0
        }

        [Fact]
        public void FormulaCache_LimitaSoReferenciasDeColunaInteira()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var outra = GestaoVistaWorkbookHelper.AdicionarAba(pkg, "Custo Real");
            outra.Cells[60, 1].Value = 1;
            var ws = GestaoVistaWorkbookHelper.AdicionarAba(pkg, "TS");
            ws.Cells[5, 1].Value = 1;

            Assert.Equal("C2*(SUMIF('Custo Real'!$C$1:$C$60, TS!A2, 'Custo Real'!$I$1:$I$60))",
                GestaoVistaFormulaCache.LimitarColunasInteiras(ws, "C2*(SUMIF('Custo Real'!C:C, TS!A2, 'Custo Real'!I:I))"));
            Assert.Equal("SUM($B$1:$B$5)+SUM(A1:B2)", GestaoVistaFormulaCache.LimitarColunasInteiras(ws, "SUM(B:B)+SUM(A1:B2)"));
        }

        [Fact]
        public void Timesheet_SemColunaCusto_CustoInformadoNaPlanilhaFicaNull()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");

            var resultado = Processar(pkg);

            var lancamento = Assert.Single(resultado.Timesheet);
            Assert.Null(lancamento.CustoInformadoNaPlanilha);
        }

        [Fact]
        public void Timesheet_LinhaVazia_EhIgnorada()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");
            // Linha 3 totalmente vazia — nunca conta como lançamento.

            var resultado = Processar(pkg);

            Assert.Single(resultado.Timesheet);
        }

        [Theory]
        [InlineData("1000", 1000)]
        [InlineData("1.000,50", 1000.50)]
        [InlineData("1000.50", 1000.50)]
        public void CustoReal_AceitaDecimalPtBrEInvariant(string textoRemuneracao, double esperado)
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            ws.Cells[2, 3].Value = "Ana";
            ws.Cells[2, 4].Value = textoRemuneracao; // Remuneração como TEXTO, formatos variados.

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Equal((decimal)esperado, registro.Remuneracao);
        }

        [Fact]
        public void CustoReal_TotalDaComposicaoContinuaRecalculado_MesmoQuandoCustoHoraDiretoVence()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            // Remuneração 1000 + Encargos 60 + Softwares 50 + Computadores 20 = 1130; /176 = 6.4204545...
            // Custo/hora direto = 55 (valor real da coluna, prioridade sobre a composição — regra oficial
            // 2026-09-22). CustoTotalMensal segue sendo o recálculo da composição (campo separado, nunca
            // usado por ObterCustoHoraVigenteAsync).
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Engenheiro", "Elétrica", "Ana", 1000m, 60m, 50m, 20m, /*Total*/ 999999m, /*CustoHora direto*/ 55m);

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Equal(1130m, registro.CustoTotalMensal);
            Assert.Equal(55m, registro.CustoHora);
        }

        [Fact]
        public void CustoReal_SemColunaCustoHoraDireta_ComposicaoContinuaFuncionando_ComportamentoAnteriorPreservado()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            // Só os 4 componentes — coluna "Custo/hora" (9) deliberadamente vazia nesta linha.
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Engenheiro", "Elétrica", "Ana", 1000m, 60m, 50m, 20m);

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Equal(1130m, registro.CustoTotalMensal);
            Assert.Equal(1130m / 176m, registro.CustoHora);
        }

        [Fact]
        public void CustoReal_ProfissionalSemComposicaoMasComCustoHoraDireto_UsaOValorDireto()
        {
            // Auditoria real BML011.013 (2026-09-22): profissionais especialistas/contratados sem
            // composição salarial formal, com custo/hora digitado direto na coluna (Antonio Clareti
            // R$250, Everton Eustaquio R$100, Tim Chowdhury R$130) — os 4 componentes ficam vazios e a
            // composição calculava CustoHora null/0, apagando o custo real desses profissionais.
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 2, "Engenheiro Sênior", "processo", "Antonio Clareti", null, null, null, null, null, 250m);

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Null(registro.CustoTotalMensal); // composição continua null — nenhum componente informado.
            Assert.Equal(250m, registro.CustoHora);  // mas o custo/hora direto nunca é perdido.
        }

        [Fact]
        public void CustoReal_ProfissionalIncompleto_SemNenhumComponente_CustoHoraFicaNull()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            ws.Cells[2, 3].Value = "Ana"; // só o nome, nenhum componente de custo, nenhum custo/hora direto.

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Null(registro.CustoHora);
            var diag = resultado.Worksheets.Single(d => d.NomeAba == "Custo Real");
            Assert.Contains(diag.Warnings, w => w.Contains("nem custo/hora direto nem componentes de composição preenchidos"));
        }

        [Fact]
        public void CustoReal_ComponenteAusente_ContaComoZeroNaSoma_NuncaNullForcadoNoComponente()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            ws.Cells[2, 3].Value = "Ana";
            ws.Cells[2, 4].Value = 1000m; // só Remuneração — Encargos/Softwares/Computadores ausentes.

            var resultado = Processar(pkg);

            var registro = Assert.Single(resultado.CustoReal);
            Assert.Null(registro.Encargos); // componente individual permanece NULL, nunca 0 forçado.
            Assert.Equal(1000m, registro.CustoTotalMensal); // soma trata ausente como 0.
            Assert.Equal(1000m / 176m, registro.CustoHora);
        }

        [Fact]
        public void MultiplasWorksheets_TodosOsPapeisEnumeradosNoMesmoWorkbook()
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var wsTs = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            GestaoVistaWorkbookHelper.EscreverLinha(wsTs, 2, "Ana", 10, 8, "Proj X", "Engenheiro", "Elétrica", "DOC-1", "Documento 1", "Desc");
            var wsCr = GestaoVistaWorkbookHelper.MontarCustoReal(pkg);
            GestaoVistaWorkbookHelper.EscreverLinha(wsCr, 2, "Engenheiro", "Elétrica", "Ana", 1000m, 60m, 50m, 20m);
            var wsCp = GestaoVistaWorkbookHelper.MontarCustoProfissionais(pkg);
            wsCp.Cells[3, 1].Value = "Engenheiro";
            wsCp.Cells[3, 3].Value = "Elétrica";
            wsCp.Cells[3, 5].Value = 1200m;
            GestaoVistaWorkbookHelper.MontarCusto(pkg);

            var resultado = Processar(pkg);

            // 4 abas reais montadas pelo teste + o placeholder visível que Processar() adiciona porque
            // nenhuma delas é Visible (mesma razão documentada em PlaceholderAbaVisivel).
            var esperado = new[] { "Timesheet", "Custo Real", "Custo profissionais", "CUSTO", PlaceholderAbaVisivel }.OrderBy(n => n);
            Assert.Equal(esperado, resultado.Worksheets.Select(d => d.NomeAba).OrderBy(n => n));
            Assert.Single(resultado.Timesheet);
            Assert.Single(resultado.CustoReal);
            Assert.Single(resultado.CustoPrevisto);
        }
    }
}
