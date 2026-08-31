#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace APS.WebApp.Services.Parsers
{
    public interface ILdpFileParserFactory
    {
        /// <summary>
        /// Retorna o parser adequado para o arquivo, ou null se a extensão não for suportada.
        /// </summary>
        ILdpFileParser ObterParser(string nomeArquivo);
    }

    public class LdpFileParserFactory : ILdpFileParserFactory
    {
        private readonly List<ILdpFileParser> _parsers;

        // Injete a lista de parsers via DI (registre cada um como ILdpFileParser no Program.cs / Startup.cs)
        public LdpFileParserFactory(IEnumerable<ILdpFileParser> parsers)
        {
            _parsers = parsers.ToList();
        }

        public ILdpFileParser ObterParser(string nomeArquivo)
        {
            if (string.IsNullOrWhiteSpace(nomeArquivo)) return null;

            var extensao = Path.GetExtension(nomeArquivo)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extensao)) return null;

            return _parsers.FirstOrDefault(p => p.ExtensoesSuportadas.Contains(extensao));
        }
    }
}