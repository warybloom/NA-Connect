using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Validação preventiva de um valor decimal contra a precisão/escala REAL de uma coluna SQL
    /// (DECIMAL/NUMERIC), lida em tempo de execução via DatabaseService.ObterEsquemaColunas — nunca um
    /// limite hardcoded. Usada pelo Cronograma Tipo 1 (AccScheduleSyncService) para identificar, ANTES do
    /// envio ao SQL, a linha causadora de um estouro aritmético (erro 8115), sem truncar, arredondar ou
    /// zerar o valor. Only what still fits in the SQL column format is accepted.
    /// </summary>
    public static class ValidadorNumericoSql
    {
        /// <summary>
        /// Retorna true quando <paramref name="valor"/> cabe em uma coluna DECIMAL/NUMERIC(precision,scale)
        /// — ou seja, sua parte inteira não excede (precision - scale) dígitos. Não valida a escala em si
        /// (o SQL Server já arredonda a escala automaticamente sem lançar overflow); o estouro aritmético
        /// (8115) é sempre por excesso de dígitos INTEIROS.
        /// </summary>
        public static bool Validar(decimal valor, int precision, int scale, out string motivo, out int maxDigitosInteiros)
        {
            maxDigitosInteiros = Math.Max(precision - scale, 0);

            decimal valorAbsoluto = Math.Abs(valor);
            decimal parteInteira = Math.Truncate(valorAbsoluto);

            int digitosInteiros = parteInteira == 0m
                ? 1
                : (int)Math.Floor(Math.Log10((double)parteInteira)) + 1;

            if (digitosInteiros > maxDigitosInteiros)
            {
                motivo = $"{digitosInteiros} dígitos inteiros; destino suporta somente {maxDigitosInteiros}";
                return false;
            }

            motivo = null;
            return true;
        }
    }
}
