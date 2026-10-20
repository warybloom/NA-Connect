using System.Collections.Generic;
using AccIntegration.WebApp.Data;
using APS.WebApp.Services;
using OfficeOpenXml;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (Etapa 01 universal, regressão funcional corrigida): cobre as partes PURAS
    /// (sem ACC/SQL) extraídas de TimesheetDumpService.ResetAndDumpTimesheetAsync — reconstrução de
    /// cabeçalhos, escrita de linhas + fórmula de CUSTO, e contagem de linhas de dados (usada tanto para
    /// medir "linhas antigas" antes de limpar quanto para a validação Banco==Excel depois de escrever).
    /// TbTimeSheetRealizadoDocumento é um POCO simples (sem DbContext) — pode ser instanciado direto em
    /// memória, sem precisar de SQL Server real (mesmo padrão de GestaoVistaWorkbookHelper).
    /// </summary>
    public class TimesheetDumpServiceUnitTests
    {
        static TimesheetDumpServiceUnitTests()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private static ExcelWorksheet NovaAbaTimesheet()
        {
            var package = new ExcelPackage();
            return package.Workbook.Worksheets.Add("TIMESHEET");
        }

        private static TbTimeSheetRealizadoDocumento NovoRegistro(string nome, decimal trabalhadas) =>
            new TbTimeSheetRealizadoDocumento
            {
                Nome = nome,
                Planejadas = 8,
                Trabalhadas = trabalhadas,
                Projeto = "CHS001",
                Funcao = "Engenheiro",
                Disciplina = "PROCESSO",
                DocumentoCodigo = "CHS001-000-PC1-RE-001",
                Documento = "CHS001-000-PC1-RE-001.docx",
                Descricao = "Relatório Técnico",
                Doc1 = "CHS001",
                Doc2 = "000",
                Doc3 = "PC1",
                Doc4 = "RE",
                Doc5 = "001"
            };

        [Fact]
        public void EscreverCabecalhosTimesheet_Reconstroi15CabecalhosNaLinha1EmNegrito()
        {
            var ws = NovaAbaTimesheet();
            TimesheetDumpService.EscreverCabecalhosTimesheet(ws);

            Assert.Equal(15, TimesheetDumpService.HeadersTimesheet.Length);
            for (int i = 0; i < TimesheetDumpService.HeadersTimesheet.Length; i++)
            {
                Assert.Equal(TimesheetDumpService.HeadersTimesheet[i], ws.Cells[1, i + 1].Text);
                Assert.True(ws.Cells[1, i + 1].Style.Font.Bold);
            }
        }

        [Fact]
        public void EscreverLinhasTimesheet_UmaLinhaPorRegistro_ComFormulaDeCustoEmTodas()
        {
            var ws = NovaAbaTimesheet();
            var registros = new List<TbTimeSheetRealizadoDocumento>
            {
                NovoRegistro("Fulano", 8),
                NovoRegistro("Ciclano", 6),
                NovoRegistro("Beltrano", 4),
            };

            int formulasEscritas = TimesheetDumpService.EscreverLinhasTimesheet(ws, registros);

            Assert.Equal(3, formulasEscritas);
            for (int i = 0; i < registros.Count; i++)
            {
                int linha = i + 2;
                Assert.Equal(registros[i].Nome, ws.Cells[linha, 1].Text);
                Assert.Equal($"C{linha}*(SUMIF('Custo Real'!C:C, TIMESHEET!A{linha}, 'Custo Real'!I:I))", ws.Cells[linha, 15].Formula);
            }
        }

        [Fact]
        public void ContarLinhasDeDados_AbaVazia_RetornaZero()
        {
            var ws = NovaAbaTimesheet();
            Assert.Equal(0, TimesheetDumpService.ContarLinhasDeDados(ws));
        }

        [Fact]
        public void ContarLinhasDeDados_ExcluiALinhaDeCabecalho()
        {
            var ws = NovaAbaTimesheet();
            TimesheetDumpService.EscreverCabecalhosTimesheet(ws);
            TimesheetDumpService.EscreverLinhasTimesheet(ws, new List<TbTimeSheetRealizadoDocumento> { NovoRegistro("Fulano", 8) });

            Assert.Equal(1, TimesheetDumpService.ContarLinhasDeDados(ws));
        }

        [Fact]
        public void AbaAntigaCom1000Linhas_NovaConsultaCom500_ResultadoFinalTemSomente500_SemResiduo()
        {
            // Simula exatamente o cenário pedido: aba com 1000 linhas de uma execução anterior, nova
            // consulta com 500 registros — depois de Clear() + reescrita, só 500 linhas devem existir,
            // nenhuma residual das 1000 antigas.
            var ws = NovaAbaTimesheet();
            TimesheetDumpService.EscreverCabecalhosTimesheet(ws);
            var registrosAntigos = new List<TbTimeSheetRealizadoDocumento>();
            for (int i = 0; i < 1000; i++) registrosAntigos.Add(NovoRegistro($"Antigo{i}", 1));
            TimesheetDumpService.EscreverLinhasTimesheet(ws, registrosAntigos);
            Assert.Equal(1000, TimesheetDumpService.ContarLinhasDeDados(ws));

            int linhasAntigas = TimesheetDumpService.ContarLinhasDeDados(ws);
            Assert.Equal(1000, linhasAntigas);

            // Reset completo — mesma operação que ResetAndDumpTimesheetAsync faz antes de reescrever.
            ws.Cells.Clear();
            TimesheetDumpService.EscreverCabecalhosTimesheet(ws);
            var registrosNovos = new List<TbTimeSheetRealizadoDocumento>();
            for (int i = 0; i < 500; i++) registrosNovos.Add(NovoRegistro($"Novo{i}", 1));
            int formulasEscritas = TimesheetDumpService.EscreverLinhasTimesheet(ws, registrosNovos);

            Assert.Equal(500, formulasEscritas);
            int linhasFinais = TimesheetDumpService.ContarLinhasDeDados(ws);
            Assert.Equal(500, linhasFinais);

            // Nenhuma linha antiga sobra: a linha 502 (primeira depois do último registro novo, linha 1
            // de cabeçalho + 500 dados = última linha usada é 501) deve estar vazia.
            Assert.True(string.IsNullOrEmpty(ws.Cells[502, 1].Text));
            Assert.Equal("Novo0", ws.Cells[2, 1].Text);
            Assert.Equal("Novo499", ws.Cells[501, 1].Text);
        }

        [Fact]
        public void ValidacaoBancoIgualExcel_ContagemBate()
        {
            var ws = NovaAbaTimesheet();
            TimesheetDumpService.EscreverCabecalhosTimesheet(ws);
            var registros = new List<TbTimeSheetRealizadoDocumento> { NovoRegistro("Fulano", 8), NovoRegistro("Ciclano", 6) };

            TimesheetDumpService.EscreverLinhasTimesheet(ws, registros);
            int linhasExcel = TimesheetDumpService.ContarLinhasDeDados(ws);

            Assert.Equal(registros.Count, linhasExcel);
        }
    }
}
