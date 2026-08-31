using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Parsers
{
    public class CsvLdpFileParser : ILdpFileParser
    {
        public IReadOnlyCollection<string> ExtensoesSuportadas { get; } = new[] { ".csv", ".tsv", ".txt" };

        private static readonly char[] DelimitadoresCandidatos = { ';', ',', '\t', '|' };

        public async Task<List<LdpAba>> ExtrairAbasAsync(Stream stream)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var conteudo = await reader.ReadToEndAsync();

            var todasLinhas = conteudo
                .Replace("\r\n", "\n")
                .Split('\n');

            var delimitador = DetectarDelimitador(todasLinhas);

            var aba = new LdpAba { Nome = "CSV" };

            foreach (var linhaTexto in todasLinhas)
            {
                if (string.IsNullOrWhiteSpace(linhaTexto))
                {
                    aba.Linhas.Add(new List<string>());
                    continue;
                }

                var celulas = linhaTexto
                    .Split(delimitador)
                    .Select(c => c.Trim().Trim('"'))
                    .ToList();

                aba.Linhas.Add(celulas);
            }

            return new List<LdpAba> { aba };
        }

        /// <summary>
        /// Heurística simples: conta ocorrências de cada delimitador candidato nas
        /// primeiras linhas não vazias e escolhe o mais frequente/consistente.
        /// </summary>
        private static char DetectarDelimitador(string[] linhas)
        {
            var amostra = linhas.Where(l => !string.IsNullOrWhiteSpace(l)).Take(5).ToList();
            if (amostra.Count == 0) return ',';

            char melhor = ',';
            int melhorContagem = -1;

            foreach (var delimitador in DelimitadoresCandidatos)
            {
                int contagem = amostra.Sum(l => l.Count(c => c == delimitador));
                if (contagem > melhorContagem)
                {
                    melhorContagem = contagem;
                    melhor = delimitador;
                }
            }

            return melhor;
        }
    }
}