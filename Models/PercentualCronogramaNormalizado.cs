using System;
using System.Globalization;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Classe do valor bruto de um percentual do Cronograma Tipo 1 — nunca colapsada em "zero". A
    /// distinção entre VALOR_NULO/VALOR_VAZIO (ausência) e VALOR_ZERO_EXPLICITO (zero real) é a correção
    /// central do incidente BML011.015/016 (100%/90%/81,25% sobrescritos por 0% na LDP porque o
    /// normalizador antigo tratava "sem valor" como "zero válido").
    /// </summary>
    public enum ClasseValorPercentual
    {
        VALOR_NULO,
        VALOR_VAZIO,
        VALOR_NAO_NUMERICO,
        VALOR_ZERO_EXPLICITO,
        VALOR_VALIDO,
        VALOR_AUSENTE
    }

    /// <summary>
    /// Resultado da normalização central de UM valor bruto de percentual do Cronograma Tipo 1 (MPP →
    /// SQL/LDP). Único ponto de conversão de escala usado por REALIZADO e PREVISTO — nunca reimplementado
    /// em outro lugar. "Pontos percentuais" = a mesma escala 0-100 que já vai para o SQL (6% = 6.00);
    /// "valor Excel" = a fração 0-1 que uma célula formatada como percentual precisa receber para exibir
    /// o mesmo número (6% = 0.06).
    /// </summary>
    public sealed class PercentualCronogramaNormalizado
    {
        public object ValorBruto { get; set; }
        public string TipoBruto { get; set; }
        public string TextoBruto { get; set; }
        public string CampoMPP { get; set; }
        public string Alias { get; set; }
        public string EstrategiaConversao { get; set; }

        public ClasseValorPercentual ClasseValor { get; set; }

        /// <summary>True quando o dado bruto tinha algum conteúdo (não nulo, não vazio) — independentemente de ser válido.</summary>
        public bool TemValor { get; set; }

        /// <summary>True SOMENTE quando o valor foi convertido com sucesso e é literalmente zero (nunca inferido de ausência).</summary>
        public bool ValorZeroExplicito { get; set; }

        /// <summary>True quando o valor pode ser usado como número explícito (zero real ou não-zero) — nunca true para nulo/vazio/não-numérico.</summary>
        public bool Valido { get; set; }

        public string MotivoErro { get; set; }
        public decimal ValorEmPontosPercentuais { get; set; }
        public decimal ValorParaExcel { get; set; }

        /// <summary>
        /// Resultado para quando o CAMPO em si (não o valor de uma tarefa) não foi resolvido — ver
        /// ResolucaoCampoCronograma.Encontrado == false. Nunca confundido com um valor de tarefa ausente:
        /// aqui nem chegamos a ler nenhuma tarefa porque não existe campo confiável para ler.
        /// </summary>
        public static PercentualCronogramaNormalizado CampoNaoResolvido(string campoMpp, string alias, string motivo) =>
            new PercentualCronogramaNormalizado
            {
                ValorBruto = null,
                TipoBruto = "(campo não resolvido)",
                TextoBruto = "(campo não resolvido)",
                CampoMPP = campoMpp,
                Alias = alias,
                ClasseValor = ClasseValorPercentual.VALOR_AUSENTE,
                TemValor = false,
                ValorZeroExplicito = false,
                Valido = false,
                MotivoErro = motivo,
                EstrategiaConversao = "CAMPO_NAO_RESOLVIDO"
            };

        public override string ToString() =>
            Valido
                ? $"Bruto={TextoBruto} ({TipoBruto}) | Classe={ClasseValor} | Estrategia={EstrategiaConversao} | Pontos%={ValorEmPontosPercentuais:0.##} | ValorExcel={ValorParaExcel:0.####}"
                : $"Bruto={TextoBruto} ({TipoBruto}) | Classe={ClasseValor} | INVALIDO | Motivo={MotivoErro}";
    }

    /// <summary>
    /// Normalizador central e ÚNICO de percentuais do Cronograma Tipo 1. Nunca decide a escala por um
    /// limiar numérico arbitrário (ex.: "se dbl &lt;= 1.0, multiplica por 100") e NUNCA colapsa ausência em
    /// zero (ver seção 5 da manutenção técnica — incidente BML011.015/016).
    ///
    /// Regra de escala, baseada no TIPO real do dado bruto, nunca no seu valor:
    ///   - Texto contendo o sinal '%' explícito (ex.: "6%"): o número antes do '%' já É a escala de
    ///     pontos percentuais (6% = 6 pontos) — nunca multiplicado/dividido.
    ///   - Texto numérico SEM sinal '%' (ex.: "6" ou "6,00"): mantém a convenção histórica deste
    ///     projeto (campos de texto customizados do MPP já são digitados em pontos percentuais).
    ///   - Número nativo do MPXJ (java.lang.Number, double, decimal, int): sempre pontos percentuais
    ///     (0-100), nunca fração — convenção do MPXJ para os campos usados aqui.
    /// </summary>
    public static class NormalizadorPercentualCronograma
    {
        private const decimal LimiteMaximoPontosPercentuais = 1000m;

        public static bool TryNormalizarPercentualCronograma(
            object valorBruto,
            string campoMpp,
            string alias,
            out PercentualCronogramaNormalizado resultado)
        {
            string tipoBruto = valorBruto?.GetType().Name ?? "null";

            resultado = new PercentualCronogramaNormalizado
            {
                ValorBruto = valorBruto,
                TipoBruto = tipoBruto,
                CampoMPP = campoMpp,
                Alias = alias
            };

            // -------------------------------------------------------------------------------------
            // NULO: nunca convertido em zero. "Inválido para atualização" (seção 5) — o chamador decide
            // não alterar a célula/registro correspondente, nunca grava 0 no lugar.
            // -------------------------------------------------------------------------------------
            if (valorBruto == null)
            {
                resultado.TextoBruto = "(null)";
                resultado.ClasseValor = ClasseValorPercentual.VALOR_NULO;
                resultado.TemValor = false;
                resultado.ValorZeroExplicito = false;
                resultado.Valido = false;
                resultado.MotivoErro = "Valor bruto nulo — ausência, nunca convertida em zero.";
                resultado.EstrategiaConversao = "AUSENTE_NULO";
                return false;
            }

            string textoBruto = valorBruto.ToString() ?? string.Empty;
            resultado.TextoBruto = textoBruto;

            decimal pontos;
            string estrategia;

            if (valorBruto is string)
            {
                string texto = textoBruto.Trim();

                if (texto.Length == 0)
                {
                    resultado.ClasseValor = ClasseValorPercentual.VALOR_VAZIO;
                    resultado.TemValor = false;
                    resultado.ValorZeroExplicito = false;
                    resultado.Valido = false;
                    resultado.MotivoErro = "Texto vazio — ausência, nunca convertida em zero.";
                    resultado.EstrategiaConversao = "AUSENTE_VAZIO";
                    return false;
                }

                bool contemSinalPercentual = texto.Contains('%');
                string limpo = texto.Replace("%", string.Empty).Trim().Replace(",", ".");

                if (!decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out pontos))
                {
                    resultado.ClasseValor = ClasseValorPercentual.VALOR_NAO_NUMERICO;
                    resultado.TemValor = true;
                    resultado.ValorZeroExplicito = false;
                    resultado.Valido = false;
                    resultado.MotivoErro = $"Texto não numérico após remover '%': '{texto}'.";
                    resultado.EstrategiaConversao = "TEXTO_NAO_NUMERICO";
                    return false;
                }

                estrategia = contemSinalPercentual
                    ? "TEXTO_COM_PERCENTUAL_LITERAL"
                    : "TEXTO_NUMERICO_SEM_SINAL_ASSUME_PONTOS";
            }
            else if (valorBruto is java.lang.Number numeroJava)
            {
                pontos = Convert.ToDecimal(numeroJava.doubleValue(), CultureInfo.InvariantCulture);
                estrategia = "NUMERICO_NATIVO_ESCALA_PONTOS";
            }
            else if (valorBruto is decimal || valorBruto is double || valorBruto is float ||
                     valorBruto is int || valorBruto is long)
            {
                pontos = Convert.ToDecimal(valorBruto, CultureInfo.InvariantCulture);
                estrategia = "NUMERICO_NATIVO_ESCALA_PONTOS";
            }
            else
            {
                resultado.ClasseValor = ClasseValorPercentual.VALOR_NAO_NUMERICO;
                resultado.TemValor = true;
                resultado.ValorZeroExplicito = false;
                resultado.Valido = false;
                resultado.MotivoErro = $"Tipo bruto não suportado para percentual: '{tipoBruto}'.";
                resultado.EstrategiaConversao = "TIPO_NAO_SUPORTADO";
                return false;
            }

            resultado.TemValor = true;
            resultado.EstrategiaConversao = estrategia;

            if (pontos < 0m || pontos > LimiteMaximoPontosPercentuais)
            {
                resultado.ClasseValor = ClasseValorPercentual.VALOR_NAO_NUMERICO;
                resultado.ValorZeroExplicito = false;
                resultado.Valido = false;
                resultado.ValorEmPontosPercentuais = pontos;
                resultado.MotivoErro = $"Percentual fora da faixa esperada [0, {LimiteMaximoPontosPercentuais}] pontos: {pontos}. " +
                    "Não corrigido por divisão heurística — valor de origem precisa ser verificado no MPP.";
                return false;
            }

            resultado.ValorEmPontosPercentuais = pontos;
            resultado.ValorParaExcel = pontos / 100m;
            resultado.ValorZeroExplicito = pontos == 0m;
            resultado.ClasseValor = resultado.ValorZeroExplicito ? ClasseValorPercentual.VALOR_ZERO_EXPLICITO : ClasseValorPercentual.VALOR_VALIDO;
            resultado.Valido = true;
            return true;
        }
    }
}
