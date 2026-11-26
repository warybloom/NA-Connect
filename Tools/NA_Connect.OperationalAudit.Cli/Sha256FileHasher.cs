using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Checkpoint 4.1, seção 1: cálculo e persistência de hash SHA-256, formato compatível com
    /// <c>sha256sum</c> (<c>HASH *caminho</c>) — só leitura de arquivo, nunca escreve/altera o arquivo
    /// hasheado.
    /// </summary>
    public static class Sha256FileHasher
    {
        public static string ComputeHash(string absoluteFilePath)
        {
            using var stream = File.OpenRead(absoluteFilePath);
            byte[] hash = SHA256.HashData(stream);
            return System.Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Chave = identificador estável do arquivo (ex.: caminho relativo ao repo) — nunca o caminho absoluto, para o baseline continuar válido em outra máquina/checkout.</summary>
        public static IReadOnlyDictionary<string, string> ComputeHashes(IReadOnlyDictionary<string, string> nameToAbsolutePath)
        {
            var resultado = new Dictionary<string, string>();
            foreach (var (nome, caminhoAbsoluto) in nameToAbsolutePath)
            {
                resultado[nome] = ComputeHash(caminhoAbsoluto);
            }
            return resultado;
        }

        public static void WriteBaseline(string outputFilePath, IReadOnlyDictionary<string, string> nameToHash)
        {
            string? diretorio = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(diretorio)) Directory.CreateDirectory(diretorio);

            var linhas = nameToHash.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} *{kv.Key}");
            File.WriteAllLines(outputFilePath, linhas);
        }

        /// <summary><c>null</c> quando o arquivo de baseline não existe — NUNCA tratado como dicionário vazio (que pareceria "0 diferenças" incorretamente).</summary>
        public static IReadOnlyDictionary<string, string>? ReadBaseline(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            var resultado = new Dictionary<string, string>();
            foreach (var linha in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(linha)) continue;
                int separador = linha.IndexOf(" *", System.StringComparison.Ordinal);
                if (separador < 0) continue;

                string hash = linha.Substring(0, separador).Trim();
                string nome = linha.Substring(separador + 2).Trim();
                resultado[nome] = hash;
            }
            return resultado;
        }
    }
}
