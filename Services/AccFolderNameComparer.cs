using System;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Normalização mínima de nomes de pasta do ACC para comparação por
    /// AutodeskApiService.FindFolderIdByPathAsync: apenas Trim + colapso de whitespace interno
    /// (espaços múltiplos, tabs) em um único espaço. NÃO remove acentos, hífens, pontuação ou
    /// dígitos, e não faz correspondência aproximada/fuzzy — é estritamente tolerância a variação
    /// de espaçamento.
    ///
    /// Motivação (canário Tipo 2, projeto b.9e5d47da-e37f-452f-9ef4-7df992f27033): a pasta real no
    /// ACC é "09 -   ENGINEERING DOCUMENTS" (3 espaços após o hífen), enquanto o caminho candidato
    /// configurado é "09 - ENGINEERING DOCUMENTS" (1 espaço) — a comparação exata (mesmo com Trim
    /// nas pontas) nunca batia porque Trim não colapsa whitespace interno.
    ///
    /// Distinto de LdpUpdateService.NormalizarNomePasta (Etapa 7): aquele também remove acentos e
    /// trata pontuação/hífen como equivalentes para um catálogo de aliases fixo — este helper é mais
    /// restrito de propósito e não deve ser confundido com aquele nem substituí-lo.
    /// </summary>
    internal static class AccFolderNameComparer
    {
        private static readonly Regex WhitespaceInterno = new(@"\s+", RegexOptions.Compiled);

        internal static string Normalize(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return string.Empty;
            return WhitespaceInterno.Replace(nome.Trim(), " ");
        }

        internal static bool AreEquivalent(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
