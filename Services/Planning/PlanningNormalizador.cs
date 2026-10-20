using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.Planning
{
    /// <summary>
    /// Normalização tolerante para comparação de nomes de aba e de cabeçalhos de coluna nas etapas de
    /// Planning Control (12/13/14/16) — sempre por igualdade EXATA depois de normalizar, nunca
    /// Contains/fuzzy (seções 5 e 13 do pedido). Espelha o mesmo comportamento já validado em
    /// QualidadeNormalizador (Etapa 17), como classe própria para não acoplar os dois módulos.
    /// </summary>
    public static class PlanningNormalizador
    {
        private static string RemoverAcentos(string texto)
        {
            string normalizado = texto.Normalize(NormalizationForm.FormD);
            var semAcento = new string(normalizado.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return semAcento.Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Normalização de NOME DE ABA (seção 13): maiúsculas/minúsculas, espaços nas pontas e acentos.
        /// </summary>
        public static string NormalizarNomeAba(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            string semAcento = RemoverAcentos(texto.Trim().ToUpperInvariant());
            return Regex.Replace(semAcento, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Normalização de CABEÇALHO DE COLUNA (seção 5): maiúsculas/minúsculas e espaços.
        /// Preserva acentos e pontuação; aliases devem ser explícitos no schema.
        /// </summary>
        public static string NormalizarCabecalho(string texto) => PlanningHeaderScan.Normalize(texto);

        /// <summary>
        /// Normalização de VALOR DE CÉLULA para checagem de ausência operacional (seção 11): trata como
        /// vazio null/string vazia/espaços/"-"/"--"/"N/A"/"NA"/"N.D."/"N/D" (após remover acentos e
        /// espaços). Nunca usada para o valor efetivamente gravado — só para decidir se a célula "tem
        /// conteúdo".
        /// </summary>
        public static bool EhValorAusente(string textoBruto)
        {
            if (string.IsNullOrWhiteSpace(textoBruto)) return true;

            string valor = RemoverAcentos(textoBruto.Trim().ToUpperInvariant());

            return valor switch
            {
                "-" => true,
                "--" => true,
                "N/A" => true,
                "NA" => true,
                "N.D." => true,
                "N/D" => true,
                _ => false
            };
        }
    }
}
