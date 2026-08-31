using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using APS.WebApp.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace APS.WebApp.Services.Parsers
{
    /// <summary>
    /// Requer o pacote NuGet DocumentFormat.OpenXml.
    /// Prioriza tabelas reais do Word (é onde planilhas coladas/LDPs em Word normalmente ficam).
    /// Se o documento não tiver nenhuma tabela, cai para uma extração heurística de parágrafos,
    /// dividindo cada linha por múltiplos espaços ou tabulação — útil apenas se o texto já
    /// estiver formatado em colunas alinhadas.
    /// </summary>
    public class DocxLdpFileParser : ILdpFileParser
    {
        public IReadOnlyCollection<string> ExtensoesSuportadas { get; } = new[] { ".docx" };

        private static readonly Regex SeparadorColunas = new(@"\t|\s{2,}", RegexOptions.Compiled);

        public Task<List<LdpAba>> ExtrairAbasAsync(Stream stream)
        {
            var abas = new List<LdpAba>();

            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return Task.FromResult(abas);

            var tabelas = body.Descendants<Table>().ToList();

            if (tabelas.Count > 0)
            {
                int idx = 1;
                foreach (var tabela in tabelas)
                {
                    var aba = new LdpAba { Nome = $"Tabela {idx++}" };

                    foreach (var linha in tabela.Elements<TableRow>())
                    {
                        var celulas = linha.Elements<TableCell>()
                            .Select(c => c.InnerText?.Trim() ?? string.Empty)
                            .ToList();
                        aba.Linhas.Add(celulas);
                    }

                    abas.Add(aba);
                }
            }
            else
            {
                // Fallback: sem tabela, tenta ler parágrafos como linhas "colunizadas"
                var aba = new LdpAba { Nome = "Texto" };
                foreach (var paragrafo in body.Descendants<Paragraph>())
                {
                    var texto = paragrafo.InnerText ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(texto))
                    {
                        aba.Linhas.Add(new List<string>());
                        continue;
                    }

                    var celulas = SeparadorColunas.Split(texto)
                        .Select(c => c.Trim())
                        .Where(c => c.Length > 0)
                        .ToList();

                    aba.Linhas.Add(celulas);
                }
                abas.Add(aba);
            }

            return Task.FromResult(abas);
        }
    }
}