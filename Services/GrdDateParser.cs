using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Estende (nunca substitui) o parser de data existente (<see cref="DateTime.TryParse(string, out DateTime)"/>)
    /// para GRDs — a célula G4 da planilha GRD às vezes traz o mês como abreviação PT/EN em vez de número
    /// (ex.: "25/Agu/2026"), que <see cref="DateTime.TryParse(string, out DateTime)"/> sozinho não reconhece
    /// em nenhuma cultura. Tenta primeiro o parser padrão (cobre "25/08/2026", "2026-08-25" e qualquer outro
    /// formato que já funcionava); só na falha tenta normalizar a abreviação de mês para número e reparsear.
    /// </summary>
    public static class GrdDateParser
    {
        private static readonly IReadOnlyDictionary<string, int> MesesAbreviados = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jan"] = 1,
            ["Fev"] = 2, ["Feb"] = 2,
            ["Mar"] = 3,
            ["Abr"] = 4, ["Apr"] = 4,
            ["Mai"] = 5, ["May"] = 5,
            ["Jun"] = 6,
            ["Jul"] = 7,
            ["Ago"] = 8, ["Aug"] = 8, ["Agu"] = 8, // "Agu" é o valor real observado no log (variante de "Ago")
            ["Set"] = 9, ["Sep"] = 9,
            ["Out"] = 10, ["Oct"] = 10,
            ["Nov"] = 11,
            ["Dez"] = 12, ["Dec"] = 12,
        };

        // dd/MesAbreviado/yyyy ou dd-MesAbreviado-yyyy — mesmos separadores que já apareciam nos formatos
        // numéricos aceitos hoje (barra e hífen).
        private static readonly Regex PadraoDiaMesAbreviadoAno = new(
            @"^\s*(?<dia>\d{1,2})\s*[/\-]\s*(?<mes>[A-Za-zÀ-ÿ]{3,4})\s*[/\-]\s*(?<ano>\d{4})\s*$",
            RegexOptions.Compiled);

        public static bool TentarParse(string? valorBruto, out DateTime data)
        {
            data = default;
            if (string.IsNullOrWhiteSpace(valorBruto)) return false;

            // 1) Parser existente primeiro — cobre tudo que já funcionava (numérico, ISO etc.), sem mudar
            // nenhum comportamento já homologado.
            if (DateTime.TryParse(valorBruto, out data)) return true;

            // 2) Fallback: mês abreviado PT/EN (só entra aqui quando o parser padrão já falhou).
            var match = PadraoDiaMesAbreviadoAno.Match(valorBruto);
            if (!match.Success) return false;
            if (!MesesAbreviados.TryGetValue(match.Groups["mes"].Value, out var mes)) return false;

            int dia = int.Parse(match.Groups["dia"].Value, CultureInfo.InvariantCulture);
            int ano = int.Parse(match.Groups["ano"].Value, CultureInfo.InvariantCulture);
            try
            {
                data = new DateTime(ano, mes, dia);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                data = default;
                return false;
            }
        }
    }
}
