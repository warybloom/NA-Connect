using System;
using System.Collections.Generic;
using System.Globalization;
using OfficeOpenXml;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Parsers controlados de data e inteiro (seção 10 do pedido). Nunca transformam um valor inválido
    /// em um valor "seguro" silencioso (DateTime.MinValue, arredondamento, zero) — ou o valor é
    /// reconhecido com confiança, ou o parser devolve um erro explícito para o preflight reportar com
    /// aba e linha.
    /// </summary>
    public static class QualidadeParsers
    {
        private static readonly CultureInfo CulturaPtBr = new("pt-BR");

        /// <summary>
        /// Aceita, nesta ordem: DateTime real do Excel (cell.Value), número serial do Excel (double, seja
        /// em cell.Value ou como texto numérico), ou texto pt-BR reconhecível como data válida (ano entre
        /// 1900 e 2100). Célula vazia devolve sucesso com valor null (DBNull) — não é erro. Qualquer outro
        /// conteúdo devolve sucesso=false com uma mensagem, sem preencher um valor "seguro".
        /// </summary>
        public static bool TentarParseData(ExcelRange cell, out DateTime? valor, out string erro)
        {
            valor = null;
            erro = null;

            if (cell.Value is DateTime dataReal)
            {
                valor = dataReal;
                return true;
            }

            string texto = cell.Text?.Trim();

            if (string.IsNullOrWhiteSpace(texto))
            {
                return true; // vazio -> DBNull, não é erro
            }

            if (cell.Value is double serialDoValor)
            {
                if (TentarConverterSerialExcel(serialDoValor, out var dataDoSerial))
                {
                    valor = dataDoSerial;
                    return true;
                }
            }
            else if (double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double serialDoTexto))
            {
                if (TentarConverterSerialExcel(serialDoTexto, out var dataDoTexto))
                {
                    valor = dataDoTexto;
                    return true;
                }
            }

            if (DateTime.TryParse(texto, CulturaPtBr, DateTimeStyles.None, out DateTime dataTexto) && AnoValido(dataTexto))
            {
                valor = dataTexto;
                return true;
            }

            erro = $"valor de data inválido: '{texto}'.";
            return false;
        }

        private static bool TentarConverterSerialExcel(double serial, out DateTime data)
        {
            data = default;
            try
            {
                var candidata = DateTime.FromOADate(serial);
                if (!AnoValido(candidata)) return false;
                data = candidata;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool AnoValido(DateTime data) => data.Year >= 1900 && data.Year <= 2100;

        /// <summary>
        /// Vazio -&gt; sucesso com valor null (DBNull). Texto não numérico ou numérico não inteiro (ex.:
        /// "3,5") -&gt; sucesso=false com mensagem — nunca arredonda ou trunca silenciosamente.
        /// </summary>
        public static bool TentarParseInteiro(string texto, out int? valor, out string erro)
        {
            valor = null;
            erro = null;

            texto = texto?.Trim();

            if (string.IsNullOrWhiteSpace(texto))
            {
                return true; // vazio -> DBNull, não é erro
            }

            if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inteiro))
            {
                valor = inteiro;
                return true;
            }

            erro = $"valor inteiro inválido: '{texto}'.";
            return false;
        }

        /// <summary>
        /// Conversão determinística nome-do-mês → número 1-12 (campo "Mes" de BASE LANÇAMENTO, cuja
        /// coluna SQL real [dbo].[NCR BASE LANCAMENTO].[Mes] é TINYINT, enquanto o Excel real preenche
        /// com abreviação PT/EN — "JAN", "FEV" etc.). Vazio -&gt; sucesso, valor null (DBNull). Número já
        /// entre 1 e 12 -&gt; aceito direto. Texto não reconhecido -&gt; NUNCA bloqueia a linha (diferente de
        /// TentarParseData/TentarParseInteiro): grava DBNull e sinaliza <paramref name="naoReconhecido"/>
        /// para o chamador registrar Warning — decisão explícita do usuário, não invenção de valor.
        /// </summary>
        private static readonly Dictionary<string, int> MesesPorNome = new(StringComparer.OrdinalIgnoreCase)
        {
            ["JAN"] = 1, ["JANEIRO"] = 1, ["JANUARY"] = 1,
            ["FEV"] = 2, ["FEVEREIRO"] = 2, ["FEB"] = 2, ["FEBRUARY"] = 2,
            ["MAR"] = 3, ["MARÇO"] = 3, ["MARCO"] = 3, ["MARCH"] = 3,
            ["ABR"] = 4, ["ABRIL"] = 4, ["APR"] = 4, ["APRIL"] = 4,
            ["MAI"] = 5, ["MAIO"] = 5, ["MAY"] = 5,
            ["JUN"] = 6, ["JUNHO"] = 6, ["JUNE"] = 6,
            ["JUL"] = 7, ["JULHO"] = 7, ["JULY"] = 7,
            ["AGO"] = 8, ["AGOSTO"] = 8, ["AUG"] = 8, ["AUGUST"] = 8,
            ["SET"] = 9, ["SETEMBRO"] = 9, ["SEP"] = 9, ["SEPT"] = 9, ["SEPTEMBER"] = 9,
            ["OUT"] = 10, ["OUTUBRO"] = 10, ["OCT"] = 10, ["OCTOBER"] = 10,
            ["NOV"] = 11, ["NOVEMBRO"] = 11, ["NOVEMBER"] = 11,
            ["DEZ"] = 12, ["DEZEMBRO"] = 12, ["DEC"] = 12, ["DECEMBER"] = 12,
        };

        public static void TentarParseMes(string texto, out int? valor, out bool naoReconhecido)
        {
            valor = null;
            naoReconhecido = false;

            texto = texto?.Trim();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return; // vazio -> DBNull, não é erro
            }

            if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numero) && numero >= 1 && numero <= 12)
            {
                valor = numero;
                return;
            }

            if (MesesPorNome.TryGetValue(texto, out int mesEncontrado))
            {
                valor = mesEncontrado;
                return;
            }

            naoReconhecido = true;
        }
    }
}
