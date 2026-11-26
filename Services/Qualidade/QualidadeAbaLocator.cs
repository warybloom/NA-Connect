using System.Linq;
using OfficeOpenXml;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Localiza as três abas obrigatórias do arquivo de Qualidade por NOME exato (após normalização
    /// tolerante — seção 7 do pedido). Nunca aceita fallback por posição/Contains/primeira aba com
    /// dados. Se qualquer uma das três estiver ausente, lança QualidadeLayoutException com o código
    /// ARQUIVO_QUALIDADE_ABA_OBRIGATORIA_AUSENTE listando exatamente quais faltam.
    /// </summary>
    public static class QualidadeAbaLocator
    {
        public const string CodigoAbaAusente = "ARQUIVO_QUALIDADE_ABA_OBRIGATORIA_AUSENTE";

        public sealed class AbasQualidade
        {
            public ExcelWorksheet BaseLancamento { get; set; }
            public ExcelWorksheet Rnc { get; set; }
            public ExcelWorksheet DesvioQualidade { get; set; }
        }

        private static ExcelWorksheet LocalizarAbaPorNome(ExcelPackage package, string nomeCanonico, System.Collections.Generic.List<string> ausentes)
        {
            string alvo = QualidadeNormalizador.NormalizarNomeAba(nomeCanonico);

            var candidatas = package.Workbook.Worksheets
                .Where(w => QualidadeNormalizador.NormalizarNomeAba(w.Name) == alvo)
                .ToList();

            if (candidatas.Count == 0)
            {
                ausentes.Add(nomeCanonico);
                return null;
            }

            // Mais de uma aba com o mesmo nome normalizado também é uma ausência funcional da aba
            // única e inequívoca exigida — nunca escolhe silenciosamente a primeira.
            if (candidatas.Count > 1)
            {
                ausentes.Add($"{nomeCanonico} (encontrada {candidatas.Count} vezes — nome ambíguo)");
                return null;
            }

            return candidatas[0];
        }

        /// <summary>
        /// Localiza UMA aba isoladamente, sem lançar exceção — usada pelo processamento independente por
        /// tabela (seção 11 do pedido: "uma falha estrutural em uma aba não pode bloquear as outras
        /// duas"). Devolve null e um erro descritivo quando ausente ou ambígua, para o chamador registrar
        /// QUALIDADE_ERRO_ESTRUTURAL só daquela tabela.
        /// </summary>
        public static ExcelWorksheet LocalizarAbaUnica(ExcelPackage package, string nomeCanonico, out string erro)
        {
            var ausentes = new System.Collections.Generic.List<string>();
            var worksheet = LocalizarAbaPorNome(package, nomeCanonico, ausentes);

            if (ausentes.Count > 0)
            {
                string abasDisponiveis = string.Join(", ", package.Workbook.Worksheets.Select(w => w.Name));
                erro = $"Aba '{nomeCanonico}' ausente ou ambígua ({ausentes[0]}). Abas encontradas no arquivo: [{abasDisponiveis}].";
                return null;
            }

            erro = null;
            return worksheet;
        }

        public static AbasQualidade LocalizarTresAbas(ExcelPackage package)
        {
            var ausentes = new System.Collections.Generic.List<string>();

            var abas = new AbasQualidade
            {
                BaseLancamento = LocalizarAbaPorNome(package, QualidadeAbas.BaseLancamento, ausentes),
                Rnc = LocalizarAbaPorNome(package, QualidadeAbas.Rnc, ausentes),
                DesvioQualidade = LocalizarAbaPorNome(package, QualidadeAbas.DesvioQualidade, ausentes)
            };

            if (ausentes.Count > 0)
            {
                throw new QualidadeLayoutException(
                    CodigoAbaAusente,
                    $"Aba(s) obrigatória(s) ausente(s) ou ambígua(s): {string.Join(", ", ausentes)}. " +
                    $"Abas exigidas: '{QualidadeAbas.BaseLancamento}', '{QualidadeAbas.Rnc}', '{QualidadeAbas.DesvioQualidade}'. " +
                    $"Abas encontradas no arquivo: [{string.Join(", ", package.Workbook.Worksheets.Select(w => w.Name))}].");
            }

            return abas;
        }
    }
}
