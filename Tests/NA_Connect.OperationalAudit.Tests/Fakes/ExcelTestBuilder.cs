using System.IO;
using OfficeOpenXml;

namespace NA_Connect.OperationalAudit.Tests.Fakes
{
    /// <summary>Monta um .xlsx real, em memória, para os testes de Stage14/Stage16 que precisam de um ExcelPackage de verdade (o preflight reaproveitado exige um ExcelPackage, não uma projeção fake).</summary>
    internal static class ExcelTestBuilder
    {
        public static byte[] CriarXlsx(string nomeAba, string[] cabecalhos, object?[][] linhas, int linhaCabecalho = 3)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add(nomeAba);

            for (int c = 0; c < cabecalhos.Length; c++) ws.Cells[linhaCabecalho, c + 1].Value = cabecalhos[c];

            for (int r = 0; r < linhas.Length; r++)
            {
                for (int c = 0; c < linhas[r].Length; c++)
                {
                    ws.Cells[linhaCabecalho + 1 + r, c + 1].Value = linhas[r][c];
                }
            }

            return package.GetAsByteArray();
        }

        public static byte[] CriarXlsxSemAba(string nomeAbaQualquer)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            package.Workbook.Worksheets.Add(nomeAbaQualquer);
            return package.GetAsByteArray();
        }
    }
}
