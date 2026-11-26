using System.Text.RegularExpressions;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Normaliza o valor do campo customizado "Fase" para um dos três gates válidos (30/60/90). Só
    /// aceita variações de espaçamento em torno de "30%"/"60%"/"90%" — qualquer outro texto (mesmo que
    /// pareça relacionado, como "Gate 30", "FA", "FD", "30" sem "%") é rejeitado explicitamente e fica a
    /// cargo do chamador classificar como FASE_INVALIDA (a tarefa continua sendo persistida).
    /// </summary>
    public static class CronogramaTipo2PhaseParser
    {
        private static readonly Regex PadraoFaseValida = new Regex(@"^(30|60|90)\s*%$", RegexOptions.Compiled);

        public static bool TentarObterGatePercentual(string faseOriginal, out byte gatePercentual)
        {
            gatePercentual = 0;
            if (string.IsNullOrWhiteSpace(faseOriginal)) return false;

            string normalizado = NormalizarEspacos(faseOriginal).Trim();
            var match = PadraoFaseValida.Match(normalizado);
            if (!match.Success) return false;

            gatePercentual = byte.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static string NormalizarEspacos(string valor)
        {
            // Espaço não separável (U+00A0), comum em texto copiado do Project/Excel, vira espaço comum
            // antes de colapsar espaços duplicados.
            string semNbsp = valor.Replace(' ', ' ');
            return Regex.Replace(semNbsp, @"\s+", " ");
        }
    }
}
