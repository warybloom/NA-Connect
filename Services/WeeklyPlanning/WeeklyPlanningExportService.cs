using APS.WebApp.Models.WeeklyPlanning;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace APS.WebApp.Services.WeeklyPlanning;

public sealed record WeeklyExportRow(string DocumentNumber, string Title, string Discipline,
    DateOnly PlannedDate, string Responsible, decimal? WeeklyPlannedHours, string? Note, string State);
public sealed record WeeklyExportDataset(string ProjectId, DateOnly Start, DateOnly End,
    DateTimeOffset? Deadline, bool IsPreview, IReadOnlyList<WeeklyExportRow> Rows);

/// <summary>Output only: consumes a dataset, never imports an Excel file as operational state.</summary>
public sealed class WeeklyPlanningExportService
{
    public byte[] Export(WeeklyExportDataset data)
    {
        if (data.End < data.Start || data.End.DayNumber - data.Start.DayNumber > 6 ||
            data.Rows.Any(x => x.PlannedDate < data.Start || x.PlannedDate > data.End || x.WeeklyPlannedHours < 0))
            throw new ArgumentException("Dataset semanal inválido.");
        if (!data.IsPreview && data.Rows.Any(x => x.State != SubmissionState.APROVADO.ToString()))
            throw new InvalidOperationException("Exportação final exige todas as linhas aprovadas.");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(data.IsPreview ? "PREVIEW - NÃO OFICIAL" : "Programação semanal");
        sheet.Cells[1, 1].Value = data.IsPreview ? "PREVIEW — NÃO APROVADO / NÃO OPERACIONAL" : "PROGRAMAÇÃO SEMANAL APROVADA";
        sheet.Cells[2, 1].Value = data.ProjectId;
        sheet.Cells[3, 1].Value = $"{data.Start:dd/MM/yyyy} → {data.End:dd/MM/yyyy}";
        sheet.Cells[3, 5].Value = data.Deadline?.ToString("yyyy-MM-dd HH:mm zzz") ?? "Prazo não definido";
        string[] headers = ["Documento", "Título", "Disciplina", "Data planejada", "Responsável", "HH previsto semana", "Observação", "Estado"];
        for (int i = 0; i < headers.Length; i++) sheet.Cells[5, i + 1].Value = headers[i];
        int row = 6;
        foreach (var item in data.Rows)
        {
            // Assign Value, never Formula: text beginning with '=' remains literal text.
            sheet.Cells[row, 1].Value = item.DocumentNumber;
            sheet.Cells[row, 2].Value = item.Title;
            sheet.Cells[row, 3].Value = item.Discipline;
            sheet.Cells[row, 4].Value = item.PlannedDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cells[row, 4].Style.Numberformat.Format = "dd/mm/yyyy";
            sheet.Cells[row, 5].Value = item.Responsible;
            sheet.Cells[row, 6].Value = item.WeeklyPlannedHours;
            sheet.Cells[row, 6].Style.Numberformat.Format = "0.00";
            sheet.Cells[row, 7].Value = item.Note;
            sheet.Cells[row, 8].Value = item.State;
            row++;
        }
        sheet.Cells[5, 1, 5, 8].Style.Font.Bold = true;
        sheet.Cells[5, 1, 5, 8].Style.Fill.PatternType = ExcelFillStyle.Solid;
        sheet.Cells[5, 1, 5, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(220, 235, 245));
        sheet.Cells[5, 1, Math.Max(5, row - 1), 8].AutoFilter = true;
        sheet.View.FreezePanes(6, 4);
        sheet.Column(1).Width = 30; sheet.Column(2).Width = 52; sheet.Column(3).Width = 22;
        sheet.Column(4).Width = 19; sheet.Column(5).Width = 28; sheet.Column(6).Width = 22;
        sheet.Column(7).Width = 48; sheet.Column(8).Width = 18;
        sheet.Cells[1, 1, Math.Max(6, row - 1), 8].Style.WrapText = true;
        package.Workbook.Properties.Title = data.IsPreview ? "Preview de programação semanal" : "Programação semanal";
        return package.GetAsByteArray();
    }
    public static WeeklyExportDataset Preview(string projectId, DateOnly start, DateOnly end) => new(
        projectId, start, end, null, true,
        [new("DEMO-PRO-001", "Documento ilustrativo — sem vínculo operacional", "PROCESSO", start,
            "Responsável de demonstração", 20m, "Dados fictícios. HH semanal 20; não confundir com HH total.", "RASCUNHO")]);
}
