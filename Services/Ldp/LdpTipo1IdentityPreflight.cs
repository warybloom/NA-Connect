using System.Globalization;

namespace APS.WebApp.Services;

/// <summary>
/// Pré-validação do código e dos campos DOC1–DOC5 de linhas da LDP Tipo 1.
/// Não altera valores da planilha: bloqueia um lote incoerente antes de qualquer INSERT.
/// Ativação na Etapa 11 é configurável e DESLIGADA por padrão até homologação.
/// </summary>
public static class LdpTipo1IdentityPreflight
{
    public sealed record Issue(int ExcelRow, string DocumentCode, string Detail);

    public static IReadOnlyList<Issue> Check(
        IEnumerable<(int ExcelRow, IReadOnlyDictionary<int, object> Cells)> rows)
    {
        var problems = new List<Issue>();
        foreach (var (excelRow, cells) in rows)
        {
            static string Get(IReadOnlyDictionary<int, object> row, int column) =>
                row.TryGetValue(column, out var value) && value is not null && value is not DBNull
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
                    : string.Empty;

            var number = Get(cells, 4);
            var columns = new[] { 25, 26, 27, 28, 29 };
            var names = new[] { "CR-OS", "Área", "DOC3", "DOC4", "DOC5" };
            var actualParts = columns.Select(column => Get(cells, column)).ToArray();
            // Uma linha com identidade estruturada completa não pode desaparecer só porque
            // a coluna D contém "-" (caso real VAM-3D-001 da LDP BML011.017).
            if (actualParts.All(x => !string.IsNullOrWhiteSpace(x)) && (number.Length == 0 || number == "-"))
            {
                problems.Add(new Issue(excelRow, number, "Número N&A ausente, apesar de DOC1–DOC5 estarem preenchidos: "
                    + string.Join("-", actualParts)));
                continue;
            }
            if (number.Length == 0) continue; // Linha realmente sem número N&A.
            var segments = number.Split('-');
            if (segments.Length != 5 || segments.Any(string.IsNullOrWhiteSpace))
            {
                if (number.StartsWith("BML011.", StringComparison.OrdinalIgnoreCase)
                    && actualParts.All(x => !string.IsNullOrWhiteSpace(x)))
                    problems.Add(new Issue(excelRow, number, "Código documental inválido para DOC1–DOC5 completos: "
                        + string.Join("-", actualParts)));
                continue; // Não assumir padrão de cinco segmentos sem identidade estruturada.
            }

            // DOC1=CR-OS, DOC2=Área, DOC3=Disciplina/atividade, DOC4=Tipo, DOC5=Sequencial.
            var expected = new[] { segments[0], segments[1], segments[2], segments[3], segments[4] };
            var differences = new List<string>();
            for (var i = 0; i < columns.Length; i++)
            {
                var actual = Get(cells, columns[i]);
                var equal = i == 4
                    ? string.Equals(expected[i].TrimStart('0'), actual.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
                    : string.Equals(expected[i].Trim(), actual, StringComparison.OrdinalIgnoreCase);
                if (!equal)
                    differences.Add($"{names[i]}: código '{expected[i]}', coluna '{actual}'");
            }
            if (differences.Count > 0)
                problems.Add(new Issue(excelRow, number, string.Join("; ", differences)));
        }
        return problems;
    }
}
