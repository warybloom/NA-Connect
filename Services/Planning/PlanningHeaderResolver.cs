using APS.WebApp.Models;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
namespace APS.WebApp.Services.Planning;
public sealed class PlanningCabecalhoResolvido
{
    public int HeaderRow { get; set; }
    public int HeaderEndRow { get; set; }
    public Dictionary<string,int> Mapa { get; set; } = new();
    public List<string> CabecalhosEncontrados { get; set; } = new();
    public Dictionary<int,string> OriginalHeaders { get; set; } = new();
}
public static class PlanningHeaderResolver
{
    public const string CodigoNaoEncontrado = "PADRAO_INCORRETO";
    public const string CodigoAmbiguo = "PADRAO_AMBIGUO";
    public static PlanningCabecalhoResolvido Resolver(ExcelWorksheet ws, string nomeProcesso, PlanningSheetDefinition definicao, ILogger? logger = null) =>
        PlanningHeaderScan.Select(new[] { ws }, definicao, logger).Header;
}
