using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using APS.WebApp.Models;
using UglyToad.PdfPig;

namespace APS.WebApp.Services.Parsers
{
    /// <summary>
    /// Requer o pacote NuGet UglyToad.PdfPig.
    /// PDF não tem noção nativa de "célula" — isso aqui é uma heurística:
    /// cada linha de texto da página vira uma "linha" da grade, e a linha é
    /// quebrada em colunas sempre que há 2+ espaços seguidos (ou tab), que é odotnet add package UglyToad.PdfPig
    /// padrão mais comum quando alguém exporta uma tabela para PDF mantendo
    /// alinhamento por espaçamento.
    ///
    /// Isso funciona bem para PDFs "tabulares" simples. PDFs com tabelas
    /// desenhadas graficamente (sem espaçamento real no texto) podem não
    /// segmentar corretamente as colunas — nesse caso, considere usar OCR
    /// ou extração de tabela dedicada (ex.: um serviço externo) antes de
    /// cair nesse parser.
    /// </summary>
    public class PdfLdpFileParser : ILdpFileParser
    {
        public IReadOnlyCollection<string> ExtensoesSuportadas { get; } = new[] { ".pdf" };

        private static readonly Regex SeparadorColunas = new(@"\t|\s{2,}", RegexOptions.Compiled);

        public Task<List<LdpAba>> ExtrairAbasAsync(Stream stream)
        {
            var abas = new List<LdpAba>();

            using var pdf = PdfDocument.Open(stream);

            int numeroPagina = 1;
            foreach (var pagina in pdf.GetPages())
            {
                var aba = new LdpAba { Nome = $"Página {numeroPagina}" };

                // Agrupa palavras por linha aproximada (mesma faixa de Y) para reconstituir o texto em linhas
                var palavrasOrdenadas = pagina.GetWords()
                    .OrderByDescending(w => w.BoundingBox.Bottom)
                    .ThenBy(w => w.BoundingBox.Left)
                    .ToList();

        var linhasTexto = new List<string>();
var linhaAtual = new List<string>();
double? yAtual = null;
double? xFimAnterior = null;
const double toleranciaY = 3.0;
const double limiarGapColuna = 8.0; // ajuste conforme o espaçamento típico do seu PDF

foreach (var palavra in palavrasOrdenadas)
{
    var y = palavra.BoundingBox.Bottom;

    if (yAtual == null || System.Math.Abs(y - yAtual.Value) <= toleranciaY)
    {
        if (linhaAtual.Count > 0)
        {
            var gap = palavra.BoundingBox.Left - (xFimAnterior ?? palavra.BoundingBox.Left);
            linhaAtual[linhaAtual.Count - 1] += gap > limiarGapColuna ? "  " : " ";
            linhaAtual[linhaAtual.Count - 1] += palavra.Text;
        }
        else
        {
            linhaAtual.Add(palavra.Text);
        }
        yAtual ??= y;
    }
    else
    {
        linhasTexto.Add(string.Concat(linhaAtual));
        linhaAtual = new List<string> { palavra.Text };
        yAtual = y;
    }

    xFimAnterior = palavra.BoundingBox.Right;
}
if (linhaAtual.Count > 0)
    linhasTexto.Add(string.Concat(linhaAtual));

                foreach (var linhaTexto in linhasTexto)
                {
                    var celulas = SeparadorColunas.Split(linhaTexto)
                        .Select(c => c.Trim())
                        .Where(c => c.Length > 0)
                        .ToList();
                    aba.Linhas.Add(celulas);
                }

                abas.Add(aba);
                numeroPagina++;
            }

            return Task.FromResult(abas);
        }
    }
}