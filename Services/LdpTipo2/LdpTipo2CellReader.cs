using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Helpers isolados de leitura de célula para o parser da LDP Tipo 2. Sempre lê pelo valor
    /// exibido/calculado (ExcelRange.Text) — nunca pela fórmula crua — e nunca lança exceção por um
    /// valor de célula inesperado: registra warning (quando um logger/linha/cabeçalho forem passados)
    /// e devolve null, para que uma célula ruim nunca interrompa o arquivo inteiro.
    /// </summary>
    public static class LdpTipo2CellReader
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// Prefixo de dia da semana em português (ruído de apresentação vindo do Cronograma Tipo 2, ex.:
        /// "Qua 12/08/26") — reconhecido SOMENTE no início do texto, opcionalmente seguido de ponto.
        /// Nunca usado como regra de negócio (o dia da semana não é validado contra a data) — ver hotfix
        /// de normalização Cronograma Tipo 2 -> LDP Tipo 2.
        /// </summary>
        private static readonly Regex PrefixoDiaSemanaCronograma = new Regex(
            @"^(SEG|TER|QUA|QUI|SEX|S[AÁ]B|DOM)\.?\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Formatos explícitos observados nas datas do Cronograma Tipo 2 (ex.: "12/08/26"), além
        /// dos já cobertos pelo TryParse dependente de cultura logo acima.</summary>
        private static readonly string[] FormatosDataCronogramaTipo2 = { "dd/MM/yy", "dd/MM/yyyy" };

        private static bool EhValorVazioConhecido(string textoMaiusculo)
        {
            return textoMaiusculo == "-" || textoMaiusculo == "N/A" || textoMaiusculo == "NA";
        }

        /// <summary>
        /// Texto simples: Trim, e trata os "vazios conhecidos" (-, N/A, NA) como null. Preserva o
        /// restante EXATAMENTE como veio (sem maiúsculas, sem remover acentos/pontuação).
        /// </summary>
        public static string LerTexto(ExcelRange cell)
        {
            string valor = cell?.Text;
            if (string.IsNullOrWhiteSpace(valor)) return null;

            string aparado = valor.Trim();
            return EhValorVazioConhecido(aparado.ToUpperInvariant()) ? null : aparado;
        }

        /// <summary>
        /// Leitura específica do número N&amp;A: usa o valor calculado/exibido da célula (cell.Text),
        /// nunca a fórmula. Se a célula tiver fórmula mas nenhum valor calculado disponível (Text
        /// vazio), devolve null e sinaliza formulaSemValor=true para o chamador tratar como linha sem
        /// N&amp;A (nunca inventa o documento, nunca devolve a fórmula como código). O valor original é
        /// preservado sem qualquer normalização adicional (sem ToUpperInvariant, sem remover acentos,
        /// hífens ou pontos) — a normalização para matching fica para a fase do cronograma Tipo 2.
        /// </summary>
        public static string LerNumeroDocumentoNaCalculado(ExcelRange cell, out bool formulaSemValor)
        {
            formulaSemValor = false;
            if (cell == null) return null;

            string texto = cell.Text?.Trim();
            bool temFormula = !string.IsNullOrWhiteSpace(cell.Formula);

            if (string.IsNullOrWhiteSpace(texto))
            {
                if (temFormula) formulaSemValor = true;
                return null;
            }

            return texto;
        }

        /// <summary>
        /// Lê uma data aceitando: valor DateTime nativo da célula, serial OLE Automation, texto em
        /// pt-BR/en-US/invariant. "-", "N/A", "NA" e vazio viram null. Qualquer outro valor não
        /// reconhecido também vira null, mas gera warning (linha + cabeçalho + valor) em vez de
        /// interromper o arquivo.
        /// </summary>
        public static DateTime? LerData(ExcelRange cell, ILogger logger, int linha, string cabecalho)
        {
            if (cell == null) return null;

            string texto = cell.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            string maiusculo = texto.ToUpperInvariant();
            if (EhValorVazioConhecido(maiusculo)) return null;

            if (cell.Value is DateTime dtValue)
            {
                return dtValue;
            }

            if (cell.Value is double serial)
            {
                try
                {
                    var data = DateTime.FromOADate(serial);
                    if (data.Year >= 1900 && data.Year <= 2100) return data;
                }
                catch (ArgumentException)
                {
                    // serial fora do intervalo válido de OLE Automation — cai para as tentativas de texto abaixo
                }
            }

            // Remove SOMENTE um prefixo reconhecido de dia da semana em português no início do texto
            // (ex.: "Qua 12/08/26" -> "12/08/26") — ruído de apresentação vindo do Cronograma Tipo 2,
            // nunca validado contra a data resultante. O restante do texto segue o parsing normal abaixo,
            // sem nenhuma outra tolerância adicional (ex.: "qualquer coisa 12/08/26" continua não
            // reconhecido).
            Match prefixoMatch = PrefixoDiaSemanaCronograma.Match(texto);
            string textoSemPrefixo = prefixoMatch.Success ? texto.Substring(prefixoMatch.Length).Trim() : texto;

            foreach (var cultura in new[] { PtBr, EnUs, CultureInfo.InvariantCulture })
            {
                if (DateTime.TryParse(textoSemPrefixo, cultura, DateTimeStyles.None, out var data) && data.Year >= 1900 && data.Year <= 2100)
                {
                    LogNormalizacaoDataSePrefixoRemovido(logger, prefixoMatch.Success, texto, data, linha, cabecalho);
                    return data;
                }
            }

            if (DateTime.TryParseExact(textoSemPrefixo, FormatosDataCronogramaTipo2, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataExata)
                && dataExata.Year >= 1900 && dataExata.Year <= 2100)
            {
                LogNormalizacaoDataSePrefixoRemovido(logger, prefixoMatch.Success, texto, dataExata, linha, cabecalho);
                return dataExata;
            }

            logger?.LogWarning($"[LDP TIPO 2] Linha {linha} | Cabeçalho: {cabecalho} | Valor de data não reconhecido: '{texto}' — tratado como vazio.");
            return null;
        }

        private static void LogNormalizacaoDataSePrefixoRemovido(ILogger logger, bool prefixoRemovido, string original, DateTime normalizado, int linha, string cabecalho)
        {
            if (!prefixoRemovido) return;
            logger?.LogDebug(
                "[CRONOGRAMA TIPO 2][NORMALIZACAO][DATA] Original=\"{Original}\" | Normalizado={Normalizado:yyyy-MM-dd} | Linha={Linha} | Cabecalho={Cabecalho}",
                original, normalizado, linha, cabecalho);
        }

        /// <summary>
        /// Lê um decimal genérico (custos, HH) aceitando separador decimal ponto ou vírgula e prefixo
        /// "R$". Nunca usa <see cref="decimal.Parse(string)"/> sem cultura controlada.
        /// </summary>
        public static decimal? LerDecimal(ExcelRange cell, ILogger logger, int linha, string cabecalho)
        {
            if (cell == null) return null;

            string texto = cell.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            string maiusculo = texto.ToUpperInvariant();
            if (EhValorVazioConhecido(maiusculo)) return null;

            if (cell.Value is double d) return Convert.ToDecimal(d);
            if (cell.Value is decimal dec) return dec;

            string limpo = texto.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();

            if (decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valorInvariant))
                return valorInvariant;

            if (decimal.TryParse(limpo, NumberStyles.Any, PtBr, out decimal valorPtBr))
                return valorPtBr;

            logger?.LogWarning($"[LDP TIPO 2] Linha {linha} | Cabeçalho: {cabecalho} | Valor decimal não reconhecido: '{texto}' — tratado como vazio.");
            return null;
        }

        /// <summary>
        /// Lê um percentual, representado internamente em PONTOS PERCENTUAIS (25% -> 25; célula
        /// formatada como percentual guardando a fração 0,25 -> 25; 100% -> 100). Faixa válida: 0 a
        /// 100 — fora dela, warning e null (nunca gravado no banco nesta fase, então não há risco de
        /// persistir um valor fora de faixa).
        ///
        /// <paramref name="normalizarEscalaCronogramaTipo2"/>: aplica a correção de escala x100 conhecida
        /// especificamente da coluna "WBS (%)" da LDP Tipo 2 (ex.: 2000 -> 20, 9902 -> 99,02) — NUNCA
        /// ligado por padrão, para não relaxar a validação de outros campos percentuais (Planned/Physical
        /// Completed) que não têm esse problema conhecido. Ver hotfix de normalização Cronograma Tipo 2 ->
        /// LDP Tipo 2.
        /// </summary>
        public static decimal? LerPercentual(ExcelRange cell, ILogger logger, int linha, string cabecalho, bool normalizarEscalaCronogramaTipo2 = false)
        {
            if (cell == null) return null;

            string texto = cell.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            string maiusculo = texto.ToUpperInvariant();
            if (EhValorVazioConhecido(maiusculo)) return null;

            decimal? bruto = null;

            if (texto.EndsWith("%", StringComparison.Ordinal))
            {
                string semSimbolo = texto.Substring(0, texto.Length - 1).Trim();
                if (decimal.TryParse(semSimbolo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v1))
                    bruto = v1;
                else if (decimal.TryParse(semSimbolo, NumberStyles.Any, PtBr, out decimal v2))
                    bruto = v2;
            }
            else if (cell.Value is double dv)
            {
                bool formatoPercentual = cell.Style?.Numberformat?.Format?.Contains('%') == true;
                bruto = formatoPercentual ? (decimal)dv * 100m : (decimal)dv;
            }
            else if (decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v3))
            {
                bruto = v3;
            }
            else if (decimal.TryParse(texto, NumberStyles.Any, PtBr, out decimal v4))
            {
                bruto = v4;
            }

            if (bruto == null)
            {
                logger?.LogWarning($"[LDP TIPO 2] Linha {linha} | Cabeçalho: {cabecalho} | Percentual não reconhecido: '{texto}' — tratado como vazio.");
                return null;
            }

            decimal valorFinal = bruto.Value;

            // Escala x100 conhecida especificamente da fonte Cronograma Tipo 2 (seção "WBS — regra de
            // normalização" do hotfix): 1000 -> 10, 2000 -> 20, 9902 -> 99,02. Só a divisão por 100 —
            // nunca divisões sucessivas até "caber" na faixa. Valor já dentro de 0-100 nunca é tocado.
            if (normalizarEscalaCronogramaTipo2 && valorFinal > 100m)
            {
                decimal original = valorFinal;
                valorFinal /= 100m;
                logger?.LogDebug(
                    "[CRONOGRAMA TIPO 2][NORMALIZACAO][WBS] Original={Original} | Normalizado={Normalizado} | Linha={Linha} | Cabecalho={Cabecalho}",
                    original, valorFinal, linha, cabecalho);
            }

            if (valorFinal < 0 || valorFinal > 100)
            {
                logger?.LogWarning($"[LDP TIPO 2] Linha {linha} | Cabeçalho: {cabecalho} | Percentual fora da faixa 0-100: {valorFinal} — tratado como vazio.");
                return null;
            }

            return valorFinal;
        }

        public static int? LerInt(ExcelRange cell)
        {
            string texto = cell?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            return int.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out int valor)
                ? valor
                : (int?)null;
        }
    }
}
