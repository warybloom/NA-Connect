using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Normalização tolerante para comparação de nomes de aba e de cabeçalhos de coluna — sempre por
    /// igualdade EXATA depois de normalizar, nunca Contains/fuzzy (seções 7 e 9 do pedido).
    /// </summary>
    public static class QualidadeNormalizador
    {
        private static string RemoverAcentos(string texto)
        {
            string normalizado = texto.Normalize(NormalizationForm.FormD);
            var semAcento = new string(normalizado.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return semAcento.Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Normalização de NOME DE ABA (seção 7): maiúsculas/minúsculas, espaços nas pontas e acentos.
        /// Não remove pontuação — nomes de aba não têm pontuação relevante neste arquivo.
        /// </summary>
        public static string NormalizarNomeAba(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            string semAcento = RemoverAcentos(texto.Trim().ToUpperInvariant());
            return Regex.Replace(semAcento, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Normalização de CABEÇALHO DE COLUNA (seção 9): maiúsculas/minúsculas, acentos, pontuação e
        /// espaços duplicados removidos. "RESPONSÁVEL" normaliza para "RESPONSAVEL" e "RESPONSÁVEL AÇÃO"
        /// para "RESPONSAVEL ACAO" — strings diferentes, então nunca colidem numa comparação de
        /// igualdade exata.
        /// </summary>
        public static string NormalizarCabecalho(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            string semAcento = RemoverAcentos(texto.Trim().ToUpperInvariant());
            string semPontuacao = Regex.Replace(semAcento, @"[^\w\s]", " ");
            return Regex.Replace(semPontuacao, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Normalização de VALOR DE CÉLULA para checagem de ausência operacional (seção 4 do pedido de
        /// preenchimento automático): trata como ausente null/string vazia/espaços/"-"/"--"/"N/A"/"NA"/
        /// "N.D."/"N/D" (após remover acentos e espaços). Nunca usada para o valor efetivamente gravado —
        /// só para decidir se a célula "tem conteúdo real" (classificação de linha vazia/placeholder) e
        /// se um campo textual obrigatório precisa do preenchimento automático "FALTA INFORMAÇÃO".
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
