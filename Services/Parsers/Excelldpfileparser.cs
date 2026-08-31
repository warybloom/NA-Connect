using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Models;
using OfficeOpenXml;

namespace APS.WebApp.Services.Parsers
{
    public class ExcelLdpFileParser : ILdpFileParser
    {
        public IReadOnlyCollection<string> ExtensoesSuportadas { get; } = new[] { ".xlsx", ".xls", ".xlsm" };

        public ExcelLdpFileParser()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public async Task<List<LdpAba>> ExtrairAbasAsync(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var abas = new List<LdpAba>();

            using var package = new ExcelPackage(memoryStream);
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                var aba = new LdpAba { Nome = worksheet.Name };

                if (worksheet.Dimension == null)
                {
                    abas.Add(aba); // aba vazia, mas ainda entra na lista pro diagnóstico
                    continue;
                }

                int totalCols = worksheet.Dimension.End.Column;
                int totalRows = worksheet.Dimension.End.Row;

                for (int row = 1; row <= totalRows; row++)
                {
                    var linha = new List<string>(totalCols);
                    for (int col = 1; col <= totalCols; col++)
                    {
                        linha.Add(worksheet.Cells[row, col].Text);
                    }
                    aba.Linhas.Add(linha);
                }

                abas.Add(aba);
            }

            return abas;
        }
    }
}