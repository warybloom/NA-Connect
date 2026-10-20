using OfficeOpenXml;

namespace NA_Connect.GestaoVista.Tests.TestHelpers
{
    /// <summary>
    /// Monta, em memória (EPPlus — mesma lib usada pelo extrator real), workbooks sintéticos com as
    /// abas internas da LDP. Nunca fixa linha/coluna nos testes — cada teste decide onde escrever, e o
    /// código real (GestaoVistaHeaderResolver) deve encontrar dinamicamente, exatamente como faria com
    /// uma LDP real.
    /// </summary>
    internal static class GestaoVistaWorkbookHelper
    {
        public static ExcelPackage NovoPacote()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            return new ExcelPackage();
        }

        public static ExcelWorksheet AdicionarAba(ExcelPackage pkg, string nome, eWorkSheetHidden visibilidade = eWorkSheetHidden.Visible)
        {
            var ws = pkg.Workbook.Worksheets.Add(nome);
            ws.Hidden = visibilidade;
            return ws;
        }

        public static void EscreverLinha(ExcelWorksheet ws, int linha, params object[] valores)
        {
            for (int i = 0; i < valores.Length; i++)
            {
                ws.Cells[linha, i + 1].Value = valores[i];
            }
        }

        /// <summary>Aba Timesheet completa com cabeçalho na linha informada (default 1, numerado como na
        /// LDP real auditada) e as linhas de dado fornecidas. incluirCusto adiciona a coluna "15. CUSTO"
        /// (GestaoVistaCabecalhos.CustoInformado) ao final — mesma posição da LDP real auditada.</summary>
        public static ExcelWorksheet MontarTimesheet(ExcelPackage pkg, eWorkSheetHidden visibilidade = eWorkSheetHidden.VeryHidden, int linhaCabecalho = 1, bool incluirData = false, bool incluirCusto = false)
        {
            var ws = AdicionarAba(pkg, "Timesheet", visibilidade);

            var cabecalhos = new List<object> { "Nome", "2. Planejadas", "3. Trabalhadas", "4. Projeto", "5. Função",
                "6. Disciplina", "7. Documento Código", "8. Documento", "9. Descrição" };
            if (incluirData) cabecalhos.Add("Data");
            if (incluirCusto) cabecalhos.Add("15. CUSTO");
            EscreverLinha(ws, linhaCabecalho, cabecalhos.ToArray());

            return ws;
        }

        public static ExcelWorksheet MontarCustoReal(ExcelPackage pkg, eWorkSheetHidden visibilidade = eWorkSheetHidden.VeryHidden, int linhaCabecalho = 1)
        {
            var ws = AdicionarAba(pkg, "Custo Real", visibilidade);
            EscreverLinha(ws, linhaCabecalho, "função", "disciplina", "nome", "Remuneração", "Encargos", "Softwares", "Computadores", "Total", "Custo/hora");
            return ws;
        }

        public static ExcelWorksheet MontarCustoProfissionais(ExcelPackage pkg, eWorkSheetHidden visibilidade = eWorkSheetHidden.VeryHidden, int linhaCabecalho = 2)
        {
            var ws = AdicionarAba(pkg, "Custo profissionais", visibilidade);
            // Coluna B (índice 2) deliberadamente SEM cabeçalho — mesma estrutura da LDP real auditada.
            ws.Cells[linhaCabecalho, 1].Value = "Função";
            ws.Cells[linhaCabecalho, 3].Value = "Disciplina";
            ws.Cells[linhaCabecalho, 4].Value = "Recurso";
            ws.Cells[linhaCabecalho, 5].Value = "Remuneração";
            ws.Cells[linhaCabecalho, 6].Value = "Encargos";
            ws.Cells[linhaCabecalho, 7].Value = "Softwares";
            ws.Cells[linhaCabecalho, 8].Value = "Computadores";
            ws.Cells[linhaCabecalho, 9].Value = "Total";
            ws.Cells[linhaCabecalho, 10].Value = "Custo/hora";
            return ws;
        }

        public static ExcelWorksheet MontarCusto(ExcelPackage pkg, eWorkSheetHidden visibilidade = eWorkSheetHidden.Hidden)
        {
            // Aba deliberadamente reconhecida e IGNORADA pelo extrator — nunca usada como fonte.
            var ws = AdicionarAba(pkg, "CUSTO", visibilidade);
            EscreverLinha(ws, 7, "HH PLANEJADO", "HH REALIZADO");
            return ws;
        }
    }
}
