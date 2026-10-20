using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Services.Qualidade;

namespace APS.WebApp.Services.Planning;

/// <summary>Contrato oficial de 21 campos; reconhecimento mínimo de dez e leitura tolerante por célula.</summary>
public static class WeeklyScheduleContract
{
    public const int HeaderRow = 7;
    // Identificadores históricos mantidos para consumidores; não definem o limiar de reconhecimento.
    public static readonly IReadOnlyList<string> RequiredHeaders = Array.AsReadOnly(new[]
        { "N&A DOCUMENT NUMBER", "DOCUMENT TITLE", "REPLANNED DATE" });
    public const string Table = "tb_WeeklyEngineeringSchedule";
    public sealed record Field(string Header, string SqlColumn, Type Type, bool NewColumn = false, bool Percentage = false, string[]? Aliases = null)
    {
        /// <summary>Header canônico + variantes aceitas (ex.: novo modelo 100% inglês vs. cabeçalho histórico).
        /// Comparação sempre por igualdade exata após PlanningHeaderScan.Normalize, nunca Contains.</summary>
        public IReadOnlyList<string> AllHeaders { get; } = Aliases == null ? new[] { Header } : new[] { Header }.Concat(Aliases).ToArray();
    }

    // Destinos legados reutilizados somente onde o significado é inequívoco.
    // Percentuais legados nvarchar mantêm a escala 0..1 observada no SQL real.
    public static readonly IReadOnlyList<Field> Fields = Array.AsReadOnly(new[]
    {
        new Field("ITEM", "Weekly_Item", typeof(string), true),
        new Field("PHASE", "Weekly_Phase", typeof(string), true),
        new Field("DISCIPLINE", "Disciplina", typeof(string)),
        new Field("N&A DOCUMENT NUMBER", "Numero_NA_Atividade", typeof(string)),
        new Field("DOCUMENT TITLE", "Nome_Tarefa", typeof(string)),
        new Field("PLANNED PROGRESS", "Perc_Previsto_Semana", typeof(string), Percentage: true),
        new Field("ACTUAL PROGRESS", "Perc_Realizado", typeof(string), Percentage: true),
        new Field("PROGRESS STATUS", "Weekly_Progress_Status", typeof(string), true, Aliases: new[] { "DOCUMENT STATUS" }),
        new Field("BASELINE DATE", "Weekly_Baseline_Date", typeof(DateTime), true),
        new Field("REPLANNED DATE", "Weekly_Replanned_Date", typeof(DateTime), true),
        new Field("PREPARATION COMPLETED", "Weekly_Preparation_Completed", typeof(string), true),
        new Field("VERIFICATION", "Weekly_Verification", typeof(string), true),
        new Field("APPROVAL", "Weekly_Approval", typeof(string), true),
        new Field("RESPONSIBLE / ASSIGNED", "Recurso", typeof(string)),
        new Field("HH Previsto", "Weekly_HH_Previsto", typeof(decimal), true, Aliases: new[] { "PLANNED HOURS" }),
        new Field("HH Previsto para Verificação", "Weekly_HH_Verificacao", typeof(decimal), true, Aliases: new[] { "PLANNED VERIFICATION HOURS" }),
        new Field("RESPONSIBLE / ASSIGNED VERIFICATION", "Weekly_Responsible_Verification", typeof(string), true),
        new Field("HH Previsto para Aprovação", "Weekly_HH_Aprovacao", typeof(decimal), true, Aliases: new[] { "PLANNED APPROVAL HOURS" }),
        new Field("RESPONSIBLE / ASSIGNED APPROVAL", "Weekly_Responsible_Approval", typeof(string), true),
        new Field("Liberado para execução S/N", "Weekly_Liberado_Execucao", typeof(string), true, Aliases: new[] { "RELEASED FOR EXECUTION Y/N" }),
        new Field("OBSERVAÇÃO DO DIA", "Weekly_Observacao_Dia", typeof(string), true, Aliases: new[] { "DAILY NOTES" }),
    });

    private static readonly string[] AliasesWeeklySchedule =
    {
        "01.13 - WEEKLY ENGINEERING SCHEDULE",
        "01.13 - PROGRAMAÇÃO SEMANAL ENGENHARIA",
        "01.13 - PROGRAMACAO SEMANAL ENGENHARIA"
    };
    public static string[] FolderAliases(string? language) => Normalize(language) == "PT"
        ? new[] { AliasesWeeklySchedule[1], AliasesWeeklySchedule[0], AliasesWeeklySchedule[2] }
        : AliasesWeeklySchedule.ToArray();

    /// <summary>Trim + espaços colapsados + maiúsculas (PlanningHeaderScan.Normalize, compartilhado) e,
    /// somente para esta comparação de headers Weekly, acentos removidos. Nunca altera o texto original da planilha.</summary>
    public static string Normalize(string? value) => RemoveDiacritics(PlanningHeaderScan.Normalize(value));

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
    public static bool IsExcel(string? extension) => Normalize(extension).TrimStart('.') is "XLSX" or "XLSM";

    public sealed record HeaderResult(IReadOnlyDictionary<string, int> Map, string[] Found, string[] Missing, string[] Extras, string[] Duplicates)
    {
        public bool Valid => PresentFields.Count() >= 10;
        public IEnumerable<Field> PresentFields => Fields.Where(f => Map.ContainsKey(Normalize(f.Header)));
        public IEnumerable<string> KnownMissing => Fields.Where(f => !Map.ContainsKey(Normalize(f.Header))).Select(f => f.Header);
    }

    public static HeaderResult ValidateHeaders(IEnumerable<string> headers, ILogger? logger = null, string sheetName = "", int row = HeaderRow, string fileName = "")
    {
        var found = headers.ToArray();
        var map = new Dictionary<string, int>();
        var duplicates = new List<string>();
        for (int i = 0; i < found.Length; i++)
        {
            string key = Normalize(found[i]);
            if (key.Length == 0) continue;
            // Alias (novo cabeçalho em inglês ou histórico em português) mapeia sempre para a chave canônica
            // do campo (Normalize(field.Header)), para que o restante do código continue lendo por field.Header.
            var field = Fields.FirstOrDefault(f => f.AllHeaders.Any(h => Normalize(h) == key));
            string mapKey = field?.Header is { } canonical ? Normalize(canonical) : key;
            if (!map.TryAdd(mapKey, i + 1) && field != null) duplicates.Add(found[i]);
        }
        var known = Fields.SelectMany(f => f.AllHeaders).Select(Normalize).ToHashSet();
        var missing = Fields.Select(f => f.Header).Where(h => !map.ContainsKey(Normalize(h))).ToArray();
        var extras = found.Where(h => Normalize(h).Length > 0 && !known.Contains(Normalize(h))).ToArray();
        var result = new HeaderResult(map, found, missing, extras, duplicates.ToArray());
        logger?.LogInformation("[WEEKLY][ABA_CANDIDATA] Aba={Aba} HeaderRow={HeaderRow} ObrigatoriosEncontrados=[{ObrigatoriosEncontrados}] ObrigatoriosAusentes=[{ObrigatoriosAusentes}] CamposConhecidosEncontrados={Conhecidos} CamposConhecidosAusentes=[{ConhecidosAusentes}] CamposExtras=[{Extras}] Resultado={Resultado} Motivo={Motivo} Duplicados=[{Duplicados}]",
            sheetName, row, string.Join(" | ", RequiredHeaders.Except(missing)), string.Join(" | ", missing),
            result.PresentFields.Count(), string.Join(" | ", result.KnownMissing), string.Join(" | ", extras),
            result.Valid ? "VALIDA" : "INVALIDA", result.Valid ? "MINIMO_10_CAMPOS_ATINGIDO" : "HEADER_OBRIGATORIO_AUSENTE", string.Join(" | ", duplicates));
        for (int i = 0; i < found.Length; i++)
            if (result.Valid && Normalize(found[i]).Length > 0 && !known.Contains(Normalize(found[i])))
                new PlanningValidationEvent(LogLevel.Warning, "ITEM_FORA_DO_PADRAO", "PROGRAMAÇÃO", fileName, sheetName, row, i + 1, found[i], found[i], Message: "Campo desconhecido ignorado; processamento continua.").Write(logger);
        return result;
    }

    public sealed record WorksheetCandidate<T>(T Sheet, HeaderResult Header, int Row = HeaderRow, int EndRow = HeaderRow);
    public sealed record WorkbookInspection<T>(IReadOnlyList<WorksheetCandidate<T>> ValidSheets)
    {
        public string? Error => ValidSheets.Count switch
        {
            0 => "ARQUIVO_WEEKLY_SCHEDULE_VALIDO_NAO_ENCONTRADO",
            1 => null,
            _ => "ABA_WEEKLY_SCHEDULE_AMBIGUA"
        };
    }

    // Compartilhado pelo operacional e pela auditoria; nome de aba não participa da seleção.
    public static WorkbookInspection<T> InspectWorksheets<T>(IEnumerable<T> sheets, Func<T, string> name,
        Func<T, IEnumerable<string>> headersAtSeven, ILogger? logger = null, string fileName = "", Func<T, int>? rowSelector = null)
    {
        var all = sheets.ToArray();
        logger?.LogInformation("[WEEKLY][WORKBOOK] Arquivo={Arquivo} QuantidadeAbas={QuantidadeAbas}", fileName, all.Select(name).Distinct().Count());
        var valid = new List<WorksheetCandidate<T>>();
        foreach (var sheet in all)
        {
            var header = ValidateHeaders(headersAtSeven(sheet), logger, name(sheet), rowSelector?.Invoke(sheet) ?? HeaderRow, fileName);
            if (header.Valid) valid.Add(new(sheet, header, rowSelector?.Invoke(sheet) ?? HeaderRow, rowSelector?.Invoke(sheet) ?? HeaderRow));
        }
        int best = valid.Select(c => c.Header.PresentFields.Count()).DefaultIfEmpty(0).Max();
        valid = valid.Where(c => c.Header.PresentFields.Count() == best).ToList();
        var inspection = new WorkbookInspection<T>(valid);
        logger?.LogInformation("[WEEKLY][WORKBOOK] Arquivo={Arquivo} AbasValidas={AbasValidas} Resultado={Resultado}",
            fileName, valid.Count, inspection.Error ?? "ABA WEEKLY IDENTIFICADA");
        return inspection;
    }

    public static APS.WebApp.Models.PlanningSheetDefinition Schema() => new()
    {
        ProcessName = "PROGRAMAÇÃO", DestinationTable = Table,
        Columns = Fields.Select(f => new APS.WebApp.Models.PlanningColumnDefinition
        { LogicalName = f.Header, AcceptedHeaders = f.AllHeaders.ToArray(), SqlColumnName = f.SqlColumn,
          DataType = f.Type == typeof(DateTime) ? APS.WebApp.Models.PlanningColumnDataType.Data : f.Type == typeof(decimal) ? APS.WebApp.Models.PlanningColumnDataType.Decimal : APS.WebApp.Models.PlanningColumnDataType.Texto,
          Required = RequiredHeaders.Contains(f.Header), RequiredValue = false }).ToList()
    };

    public static WorkbookInspection<ExcelWorksheet> InspectWorkbook(ExcelPackage package, ILogger? logger = null, string fileName = "", string profile = WeeklyProcessorRegistry.Official)
    {
        WeeklyProcessorRegistry.RequireActive(profile);
        logger?.LogInformation("[WEEKLY][WORKBOOK] Arquivo={Arquivo} QuantidadeAbas={QuantidadeAbas}", fileName, package.Workbook.Worksheets.Count);
        var valid = new List<WorksheetCandidate<ExcelWorksheet>>();
        foreach (var candidate in PlanningHeaderScan.Candidates(package.Workbook.Worksheets, Schema()))
        {
            var values = Enumerable.Range(1, candidate.Sheet.Dimension?.End.Column ?? 0)
                .Select(c => candidate.Header.OriginalHeaders.GetValueOrDefault(c, "")).ToArray();
            var header = ValidateHeaders(values, logger, candidate.Sheet.Name, candidate.Header.HeaderRow, fileName);
            if (header.Valid) valid.Add(new(candidate.Sheet, header, candidate.Header.HeaderRow, candidate.Header.HeaderEndRow));
        }
        if (valid.Count == 0)
        {
            try { PlanningHeaderScan.Select(package.Workbook.Worksheets, Schema(), logger, fileName); }
            catch (PlanningLayoutException) { } // Diagnóstico estruturado já emitido; seleção permanece inválida.
        }
        int best = valid.Select(c => c.Header.PresentFields.Count()).DefaultIfEmpty(0).Max();
        return new(valid.Where(c => c.Header.PresentFields.Count() == best).ToList());
    }

    public static ExcelWorksheet? FindWorksheet(ExcelPackage package)
    {
        var result = InspectWorkbook(package);
        if (result.ValidSheets.Count > 1) throw new InvalidDataException(result.Error);
        return result.ValidSheets.Count == 1 ? result.ValidSheets[0].Sheet : null;
    }

    public static HeaderResult Inspect(ExcelWorksheet sheet, ILogger? logger = null) => ValidateHeaders(
        Enumerable.Range(1, sheet.Dimension?.End.Column ?? 0).Select(c => sheet.Cells[HeaderRow, c].Text), logger, sheet.Name);

    public static string? SelectionError(int validCount) => validCount switch
    {
        0 => "ARQUIVO_WEEKLY_SCHEDULE_VALIDO_NAO_ENCONTRADO",
        1 => null,
        _ => "ARQUIVO_WEEKLY_SCHEDULE_AMBIGUO"
    };

    public static IReadOnlyList<string> SqlErrors(IReadOnlyDictionary<string, string> schema, IEnumerable<Field>? presentFields = null)
    {
        var errors = new List<string>();
        foreach (var field in presentFields ?? Fields)
        {
            string expected = field.Type == typeof(decimal) ? "decimal" : field.Type == typeof(DateTime) ? "date" : "nvarchar";
            if (!schema.TryGetValue(field.SqlColumn, out var actual)) errors.Add(field.SqlColumn + ": ausente");
            else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) errors.Add($"{field.SqlColumn}: esperado {expected}, encontrado {actual}");
        }
        return errors;
    }

    /// <summary>Valida os campos encontrados. Opcionais ausentes ficam NULL nos destinos existentes;
    /// destinos ainda não criados são omitidos somente quando o campo também não veio no Excel.</summary>
    public static void PrepareForSql(DataTable data, IReadOnlyDictionary<string, string> schema)
    {
        var errors = SqlErrors(schema).ToList();
        var technical = new Dictionary<string,string> { ["Id_Registro"]="int", ["Project_ID_ACC"]="nvarchar", ["Data_Sincronizacao"]="datetime",
            ["Tipo_Registro"]="nvarchar", ["Status_Importacao"]="nvarchar", ["Diagnostico_Importacao"]="nvarchar", ["Arquivo_Origem"]="nvarchar", ["Aba_Origem"]="nvarchar", ["Linha_Excel"]="int" };
        foreach (var column in technical)
            if (!schema.TryGetValue(column.Key, out var type) || type != column.Value) errors.Add(column.Key + ": ausente ou tipo incompatível");
        var expectedNames = Fields.Select(f=>f.SqlColumn).Concat(technical.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in data.Columns)
            if (!expectedNames.Contains(column.ColumnName)) errors.Add(column.ColumnName + ": fora do contrato SQL");
        if (schema.Count != 30) errors.Add($"Esperadas 30 colunas; encontradas {schema.Count}.");
        if (errors.Count > 0) throw new InvalidDataException("WEEKLY_SQL_INCOMPATIVEL: " + string.Join(" | ", errors));
    }

    public static DataTable Read(ExcelPackage package, string projectId, ILogger? logger = null, string fileName = "", string profile = WeeklyProcessorRegistry.Official)
    {
        var inspection = InspectWorkbook(package, logger, fileName, profile);
        if (inspection.Error != null)
        {
            var candidates = PlanningHeaderScan.Candidates(package.Workbook.Worksheets, Schema());
            var best = candidates.OrderByDescending(c => c.Header.Mapa.Count).FirstOrDefault();
            bool ambiguous = inspection.ValidSheets.Count > 1;
            string detail = ambiguous
                ? "Mais de uma aba possui a mesma aderência ao padrão: " + string.Join("; ", inspection.ValidSheets.Select(c => $"{c.Sheet.Name}={c.Header.PresentFields.Count()}/21"))
                : $"PADRÃO EXCEL INCORRETO, IMPOSSÍVEL OBTER INFORMAÇÕES. Melhor candidata: {best?.Sheet.Name}; campos reconhecidos: {best?.Header.Mapa.Count ?? 0}/21; mínimo necessário: 10.";
            return Sentinel(projectId, ambiguous ? "PADRAO_AMBIGUO" : "PADRAO_INCORRETO", detail, fileName, best?.Sheet.Name, best?.Header.HeaderRow);
        }
        var sheet = inspection.ValidSheets[0].Sheet;
        var header = inspection.ValidSheets[0].Header;
        // Assinatura estrutural não desempata colunas conhecidas duplicadas para gravar dados.
        if (header.Duplicates.Length > 0) return Sentinel(projectId, "PADRAO_AMBIGUO", "Headers oficiais duplicados: " + string.Join(" | ", header.Duplicates), fileName, sheet.Name, inspection.ValidSheets[0].Row);
        var table = CreateTable();
        table.ExtendedProperties["Weekly.PresentHeaders"] = header.PresentFields.Select(f => f.Header).ToArray();
        foreach (var missing in header.KnownMissing)
            new PlanningValidationEvent(LogLevel.Warning, "ITEM_OBRIGATORIO_AUSENTE", "PROGRAMAÇÃO", fileName, sheet.Name,
                inspection.ValidSheets[0].Row, Header: missing, Message: "Projeto=" + projectId + "; Ação=campo inexistente gravado como NULL; processamento mantido").Write(logger);
        var syncTime = DateTime.Now;
        int hoursRounded = 0;
        int invalidRows = 0;
        for (int r = inspection.ValidSheets[0].EndRow + 1; r <= (sheet.Dimension?.End.Row ?? 0); r++)
        {
            // Preserva linhas com qualquer campo conhecido preenchido, mesmo sem ITEM/documento/título.
            if (!header.PresentFields.Any(f => !string.IsNullOrWhiteSpace(Convert.ToString(sheet.Cells[r, header.Map[Normalize(f.Header)]].Value, CultureInfo.InvariantCulture)))) continue;
            var diagnostics = new List<string>();
            var row = table.NewRow();
            row["Project_ID_ACC"] = projectId;
            row["Data_Sincronizacao"] = syncTime;
            foreach (var field in Fields)
            {
                if (!header.Map.ContainsKey(Normalize(field.Header)))
                {
                    diagnostics.Add(field.Header + ": COLUNA NÃO EXISTE NO EXCEL.");
                    continue;
                }
                var cell = sheet.Cells[r, header.Map[Normalize(field.Header)]];
                try
                {
                    var value = ConvertCell(cell, field, package.Workbook.Date1904);
                    if (field.Type == typeof(decimal) && value is decimal rounded && Number(cell.Value) != rounded) hoursRounded++;
                    row[field.SqlColumn] = value ?? DBNull.Value;
                    if (value == null)
                    {
                        diagnostics.Add(field.Header + ": CÉLULA VAZIA NO EXCEL.");
                        new PlanningValidationEvent(LogLevel.Warning, "VALOR_OBRIGATORIO_VAZIO", "PROGRAMAÇÃO", fileName, sheet.Name, r, cell.Start.Column, field.Header,
                            cell.Text, "valor preenchido", "Projeto=" + projectId + "; Ação=NULL gravado; restante da linha preservado").Write(logger);
                    }
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                {
                    string expected = field.Type == typeof(DateTime) ? "data válida" : "número decimal";
                    diagnostics.Add($"{field.Header}: VALOR INVÁLIDO. Recebido='{cell.Text}'; esperado={expected}.");
                    new PlanningValidationEvent(LogLevel.Error, "PREENCHIMENTO_INCORRETO", "PROGRAMAÇÃO", fileName, sheet.Name,
                        r, cell.Start.Column, header.Found[cell.Start.Column - 1], cell.Text,
                        expected, "Projeto=" + projectId + "; Ação=NULL gravado; restante da linha preservado. " + ex.Message).Write(logger);
                }
            }
            if (diagnostics.Count > 0) invalidRows++;
            row["Tipo_Registro"] = "DADO";
            row["Status_Importacao"] = diagnostics.Count == 0 ? "OK" : "COM_ALERTAS";
            row["Diagnostico_Importacao"] = diagnostics.Count == 0 ? DBNull.Value : string.Join(" ", diagnostics);
            row["Arquivo_Origem"] = fileName;
            row["Aba_Origem"] = sheet.Name;
            row["Linha_Excel"] = r;
            table.Rows.Add(row);
        }
        logger?.LogInformation("[WEEKLY] Linhas de dados={Count}", table.Rows.Count);
        if (hoursRounded > 0) logger?.LogWarning("[WEEKLY][HH] Valores arredondados à escala SQL decimal(18,6)={Count} Regra=AwayFromZero", hoursRounded);
        table.ExtendedProperties["Planning.RowsWithErrors"] = invalidRows;
        return table;
    }

    public static DataTable CreateTable()
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add("Project_ID_ACC", typeof(string));
        table.Columns.Add("Data_Sincronizacao", typeof(DateTime));
        foreach (var field in Fields) table.Columns.Add(field.SqlColumn, field.Type);
        foreach (string column in new[] { "Tipo_Registro", "Status_Importacao", "Diagnostico_Importacao", "Arquivo_Origem", "Aba_Origem" }) table.Columns.Add(column, typeof(string));
        table.Columns.Add("Linha_Excel", typeof(int));
        return table;
    }

    public static DataTable Sentinel(string projectId, string status, string diagnostic, string? file = null, string? sheet = null, int? line = null)
    {
        var table = CreateTable();
        var row = table.NewRow();
        row["Project_ID_ACC"] = projectId;
        row["Data_Sincronizacao"] = DateTime.Now;
        row["Tipo_Registro"] = status;
        row["Status_Importacao"] = status;
        row["Diagnostico_Importacao"] = diagnostic;
        row["Arquivo_Origem"] = (object?)file ?? DBNull.Value;
        row["Aba_Origem"] = (object?)sheet ?? DBNull.Value;
        row["Linha_Excel"] = (object?)line ?? DBNull.Value;
        table.Rows.Add(row);
        return table;
    }

    private static object? ConvertCell(ExcelRange cell, Field field, bool date1904)
    {
        if (cell.Value is ExcelErrorValue) throw new FormatException("Erro de fórmula Excel: " + cell.Text);
        if (cell.Value == null || string.IsNullOrWhiteSpace(Convert.ToString(cell.Value, CultureInfo.InvariantCulture))) return null;
        if (field.Type == typeof(DateTime))
        {
            if (date1904 && cell.Value is double serial) return DateTime.FromOADate(serial + 1462);
            if (QualidadeParsers.TentarParseData(cell, out var date, out var error)) return date;
            throw new FormatException(error);
        }
        if (field.Percentage) return Percentage(cell.Value).ToString(CultureInfo.InvariantCulture);
        if (field.Type == typeof(decimal))
        {
            decimal hours = Number(cell.Value);
            // O BML real contém rateios com mais de seis casas. A escala SQL existente é seis.
            hours = decimal.Round(hours, 6, MidpointRounding.AwayFromZero);
            if (Math.Abs(hours) > 999999999999.999999m)
                throw new FormatException("HH fora da faixa decimal(18,6).");
            return hours;
        }
        return cell.Text.Trim();
    }

    public static decimal Number(object value)
    {
        if (value is DateTime or TimeSpan or bool) throw new FormatException("HH/percentual exige número, não horário/data.");
        // Remove apenas artefatos binários do double Excel (ex.: 17.600000000000001).
        if (value is double d) return Convert.ToDecimal(d);
        if (value is float f) return Convert.ToDecimal(f);
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();
        var culture = text.Contains(',') ? CultureInfo.GetCultureInfo("pt-BR") : CultureInfo.InvariantCulture;
        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, culture, out var number)) return number;
        throw new FormatException("Valor numérico inválido: " + text);
    }

    /// <summary>0.5, 50 e "50%" => 0.5; uma única normalização para a escala SQL legada 0..1.</summary>
    public static decimal Percentage(object value)
    {
        bool explicitPercent = value is string s && s.Trim().EndsWith('%');
        decimal number = Number(explicitPercent ? ((string)value).Trim().TrimEnd('%').Trim() : value);
        if (explicitPercent || number > 1) number /= 100;
        if (number < 0 || number > 1) throw new FormatException("Percentual fora de 0..100%.");
        return number;
    }
}
