using System;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Normaliza o número N&amp;A do cronograma Tipo 2 para uma representação de COMPARAÇÃO estável —
    /// nunca usada como valor de exibição (o texto original fica preservado à parte, em
    /// NumeroDocumentoNaOriginal). Deliberadamente NÃO usa Path.GetFileNameWithoutExtension nem remove o
    /// último grupo numérico: códigos N&amp;A têm hífens/pontos significativos que não podem ser tratados
    /// como separador de revisão aqui (isso é responsabilidade exclusiva do matcher — ver
    /// CronogramaTipo2Service.ClassificarMatch).
    /// </summary>
    public static class CronogramaTipo2DocumentNumberNormalizer
    {
        // Apenas extensões de arquivo conhecidas — nunca um sufixo numérico/alfanumérico genérico.
        private static readonly string[] ExtensoesConhecidas =
        {
            ".PDF", ".DWG", ".DXF", ".RVT", ".XLSX", ".XLS", ".DOCX", ".DOC", ".MPP"
        };

        public static string Normalizar(string valorOriginal)
        {
            if (string.IsNullOrWhiteSpace(valorOriginal)) return string.Empty;

            string semNbsp = valorOriginal.Replace(' ', ' ');
            string semEspacosExtras = Regex.Replace(semNbsp, @"\s+", " ").Trim();
            string upper = semEspacosExtras.ToUpperInvariant();

            foreach (var extensao in ExtensoesConhecidas)
            {
                if (upper.EndsWith(extensao, StringComparison.Ordinal))
                {
                    upper = upper.Substring(0, upper.Length - extensao.Length);
                    break;
                }
            }

            return upper;
        }

        /// <summary>
        /// Chave usada para persistência e matching (seção 13 do pedido): SEMPRE
        /// NumeroDocumentoNaNormalizado + "|" + GatePercentual — nunca inclui nome/descrição/disciplina
        /// da tarefa. A chave completa para banco/matching soma também Project_ID_ACC (conceitualmente),
        /// mas essa combinação é feita pelo chamador (ela nunca é gravada como um único campo com o
        /// projeto embutido).
        /// </summary>
        public static string MontarChaveDocumentoGate(string numeroDocumentoNaNormalizado, byte gatePercentual)
        {
            return $"{numeroDocumentoNaNormalizado}|{gatePercentual}";
        }

        /// <summary>
        /// Comparação controlada de revisão física (seção 12 do pedido): um número base do cronograma
        /// (ex.: "CHS001-000-PC3-DR-001") pode corresponder a um número da LDP com um sufixo físico de
        /// revisão iniciado por hífen (ex.: "CHS001-000-PC3-DR-001-05"), mas nunca a um número que apenas
        /// concatena dígitos ao final sem separador (ex.: "CHS001-000-PC3-DR-0010" NÃO corresponde a
        /// "CHS001-000-PC3-DR-001"). Ambos os valores já devem estar normalizados por Normalizar().
        /// </summary>
        public static bool CorrespondeComRevisaoFisica(string numeroBaseNormalizado, string numeroCandidatoNormalizado)
        {
            if (string.IsNullOrEmpty(numeroBaseNormalizado) || string.IsNullOrEmpty(numeroCandidatoNormalizado))
                return false;

            if (string.Equals(numeroBaseNormalizado, numeroCandidatoNormalizado, StringComparison.Ordinal))
                return true;

            return numeroCandidatoNormalizado.StartsWith(numeroBaseNormalizado + "-", StringComparison.Ordinal);
        }
    }
}
