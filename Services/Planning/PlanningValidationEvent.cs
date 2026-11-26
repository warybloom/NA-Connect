using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.Planning;

public sealed record PlanningValidationEvent(
    LogLevel Severity, string Code, string TemplateType, string FilePath, string Worksheet = "",
    int? ExcelRow = null, int? ExcelColumn = null, string Header = "", string? RawValue = null,
    string ExpectedValue = "", string Message = "")
{
    public void Write(ILogger? logger) => logger?.Log(Severity,
        "[{Code}] Template={TemplateType} FilePath={FilePath} Workbook={Workbook} Aba={Worksheet} Linha={ExcelRow} Coluna={ExcelColumn} Header={Header} ValorRecebido={RawValue} ValorEsperado={ExpectedValue} Detalhe={Message}",
        Code, TemplateType, FilePath, Path.GetFileName(FilePath), Worksheet, ExcelRow, ExcelColumn, Header,
        RawValue ?? "<NULL>", ExpectedValue, Message);

    public static void EmptyFolder(ILogger? logger, string template, string path, int count) =>
        new PlanningValidationEvent(LogLevel.Error, "PASTA_SEM_ARQUIVO", template, path,
            ExpectedValue: "XLSX / XLSM", RawValue: count.ToString(),
            Message: "Pasta localizada sem arquivo Excel compatível; quantidade de arquivos encontrados informada em ValorRecebido.").Write(logger);
}
