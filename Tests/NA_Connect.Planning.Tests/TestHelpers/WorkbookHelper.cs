using OfficeOpenXml;

namespace NA_Connect.Planning.Tests.TestHelpers
{
    internal static class WorkbookHelper
    {
        public static ExcelPackage NovoPacote()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            return new ExcelPackage();
        }

        public static ExcelWorksheet AdicionarAba(ExcelPackage package, string nome)
        {
            return package.Workbook.Worksheets.Add(nome);
        }

        public static void EscreverLinha(ExcelWorksheet ws, int row, params object[] valores)
        {
            for (int i = 0; i < valores.Length; i++)
            {
                ws.Cells[row, i + 1].Value = valores[i];
            }
        }
    }
}
