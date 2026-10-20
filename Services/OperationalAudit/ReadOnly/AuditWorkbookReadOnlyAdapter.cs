using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>
    /// Implementação real de <see cref="IAuditWorkbookReadOnlyAdapter"/> via EPPlus. O <see cref="ExcelPackage"/>
    /// é local a este método (nunca devolvido, nunca guardado em campo) — impossível para qualquer chamador
    /// obter uma referência ao pacote e chamar <c>Save()</c>/<c>SaveAs()</c> por engano; só a projeção
    /// imutável <see cref="AuditWorkbookSnapshot"/> sai daqui.
    /// </summary>
    public sealed class AuditWorkbookReadOnlyAdapter : IAuditWorkbookReadOnlyAdapter
    {
        public AuditWorkbookSnapshot AbrirSomenteLeitura(Stream conteudo, int linhaDeCabecalhoPadrao = 1)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(conteudo);

            var abas = new Dictionary<string, AuditWorksheetSnapshot>();
            var nomes = new List<string>();

            foreach (var ws in package.Workbook.Worksheets)
            {
                nomes.Add(ws.Name);
                abas[ws.Name] = LerAba(ws, linhaDeCabecalhoPadrao);
            }

            return new AuditWorkbookSnapshot { NomesDasAbas = nomes, Abas = abas };
        }

        private static AuditWorksheetSnapshot LerAba(ExcelWorksheet ws, int linhaDeCabecalho)
        {
            int totalLinhas = ws.Dimension?.End.Row ?? 0;
            int totalColunas = ws.Dimension?.End.Column ?? 0;

            var cabecalhos = new List<string>();
            if (totalColunas > 0 && linhaDeCabecalho > 0 && linhaDeCabecalho <= totalLinhas)
            {
                for (int c = 1; c <= totalColunas; c++)
                {
                    cabecalhos.Add(ws.Cells[linhaDeCabecalho, c].Text?.Trim() ?? string.Empty);
                }
            }

            var linhas = new Dictionary<int, IReadOnlyDictionary<string, string?>>();
            var celulasComFormula = new HashSet<string>();

            for (int r = linhaDeCabecalho + 1; r <= totalLinhas; r++)
            {
                var valores = new Dictionary<string, string?>();
                bool linhaTemAlgumValor = false;

                for (int c = 1; c <= totalColunas; c++)
                {
                    var celula = ws.Cells[r, c];
                    string chave = c - 1 < cabecalhos.Count && !string.IsNullOrWhiteSpace(cabecalhos[c - 1])
                        ? cabecalhos[c - 1]
                        : $"Col{c}";

                    string? texto = celula.Text;
                    if (!string.IsNullOrWhiteSpace(texto)) linhaTemAlgumValor = true;
                    valores[chave] = texto;

                    if (!string.IsNullOrEmpty(celula.Formula))
                    {
                        celulasComFormula.Add(celula.Address);
                    }
                }

                if (linhaTemAlgumValor)
                {
                    linhas[r] = valores;
                }
                // Linhas totalmente vazias nunca entram em Linhas — nunca geram finding (seção 7/17).
            }

            return new AuditWorksheetSnapshot
            {
                Nome = ws.Name,
                Oculta = ws.Hidden != eWorkSheetHidden.Visible,
                LinhaDeCabecalho = linhaDeCabecalho,
                Cabecalhos = cabecalhos,
                TotalDeLinhasFisicas = totalLinhas,
                Linhas = linhas,
                CelulasComFormula = celulasComFormula
            };
        }
    }
}
