using APS.WebApp.Models;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.Planning;

/// <summary>Reconstrói headers mesclados e procura assinaturas nas dez primeiras linhas de todas as abas.</summary>
public static class PlanningHeaderScan
{
    public static string Normalize(string? value) => System.Text.RegularExpressions.Regex.Replace(value?.Trim() ?? "", @"\s+", " ").ToUpperInvariant();
    public sealed record Cell(int Column, string Original, int EndRow);

    public static IReadOnlyList<Cell> Headers(ExcelWorksheet sheet, int row)
    {
        var cells = new List<Cell>();
        for (int c = 1; c <= (sheet.Dimension?.End.Column ?? 0); c++)
        {
            string? merged = sheet.MergedCells[row, c];
            var range = merged == null ? null : new ExcelAddress(merged);
            if (range != null && (range.Start.Row != row || range.Start.Column != c)) continue;
            string text = sheet.Cells[row, c].Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            // No HOLD oficial a mescla vertical cobre as duas linhas: texto único, dados após EndRow.
            // Mesclas horizontais possuem um único header; valores abaixo são dados, não subheaders.
            cells.Add(new(c, text, range?.End.Row ?? row));
        }
        return cells;
    }

    public sealed record Candidate(ExcelWorksheet Sheet, PlanningCabecalhoResolvido Header, string[] Missing, string[] Duplicates)
    {
        public int Score => Header.Mapa.Count + Duplicates.Length;
        public bool Valid => Header.Mapa.Count >= (Header.Mapa.Count + Missing.Length > 10 ? 10 : 5) && Duplicates.Length == 0;
    }

    public static IReadOnlyList<Candidate> Candidates(IEnumerable<ExcelWorksheet> sheets, PlanningSheetDefinition schema)
    {
        var results = new List<Candidate>();
        foreach (var sheet in sheets)
        for (int row = 1; row <= Math.Min(10, sheet.Dimension?.End.Row ?? 0); row++)
        {
            var cells = Headers(sheet, row);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            foreach (var field in schema.Columns)
            {
                var aliases = field.AcceptedHeaders.Select(Normalize).ToHashSet();
                var matches = cells.Where(c => aliases.Contains(Normalize(c.Original))).ToArray();
                if (matches.Length == 1) map[field.LogicalName] = matches[0].Column;
                if (matches.Length > 1) duplicates.Add(field.LogicalName);
            }
            results.Add(new(sheet, new PlanningCabecalhoResolvido
            {
                HeaderRow = row, HeaderEndRow = cells.Where(c => map.Values.Contains(c.Column)).Select(c => c.EndRow).DefaultIfEmpty(row).Max(),
                Mapa = map, CabecalhosEncontrados = cells.Select(c => c.Original).ToList(),
                OriginalHeaders = cells.ToDictionary(c => c.Column, c => c.Original)
            }, schema.Columns.Select(f => f.LogicalName).Where(f => !map.ContainsKey(f)).ToArray(), duplicates.ToArray()));
        }
        return results;
    }

    public static Candidate Select(IEnumerable<ExcelWorksheet> sheets, PlanningSheetDefinition schema, ILogger? logger = null, string file = "")
    {
        var all = sheets.ToArray();
        var candidates = Candidates(all, schema);
        int score = candidates.Select(c => c.Score).DefaultIfEmpty(0).Max();
        var best = candidates.Where(c => c.Score == score && score > 0).ToArray();
        int minimum = schema.Columns.Count > 10 ? 10 : 5;
        if (score < minimum)
            throw new PlanningLayoutException("PADRAO_INCORRETO", $"PADRÃO EXCEL INCORRETO, IMPOSSÍVEL OBTER INFORMAÇÕES. Melhor candidata: {best.FirstOrDefault()?.Sheet.Name}; campos reconhecidos: {score}/{schema.Columns.Count}; mínimo necessário: {minimum}.");
        if (best.Length > 1 || best.Any(c => c.Duplicates.Length > 0))
            throw new PlanningLayoutException("PADRAO_AMBIGUO", "Mais de uma aba/linha ou coluna possui a mesma aderência ao padrão: " + string.Join("; ", best.Select(c => $"{c.Sheet.Name}:{c.Header.HeaderRow}={score}/{schema.Columns.Count}")));
        var selected = best[0];
        foreach (var field in schema.Columns.Where(f => !selected.Header.Mapa.ContainsKey(f.LogicalName)))
            new PlanningValidationEvent(LogLevel.Warning, "ITEM_OBRIGATORIO_AUSENTE", schema.ProcessName, file, selected.Sheet.Name,
                selected.Header.HeaderRow, Header: field.AcceptedHeaders[0], Message: "Ação=campo inexistente gravado como NULL; processamento mantido").Write(logger);
        foreach (var cell in selected.Header.OriginalHeaders.Where(c => !selected.Header.Mapa.Values.Contains(c.Key)))
            new PlanningValidationEvent(LogLevel.Warning, "ITEM_FORA_DO_PADRAO", schema.ProcessName, file, selected.Sheet.Name,
                selected.Header.HeaderRow, cell.Key, cell.Value, cell.Value, Message: "Campo desconhecido ignorado; processamento continua.").Write(logger);
        return selected;
    }
}
