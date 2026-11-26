using APS.WebApp.Models;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.Planning;
using OfficeOpenXml;

namespace APS.WebApp.Services.OperationalAudit.Rules;

internal static class PlanningAuditSelection
{
    internal sealed record Selection(AccItemRef? Item, ResultadoPreflightPlanejamento? Result, string? Error);
    internal static async Task<Selection> SelectAsync(IEnumerable<AccItemRef> items, IAuditAccReadOnlyAdapter acc,
        IPlanningPreflightService preflight, PlanningSheetDefinition schema, OperationalAuditContext context, string folder)
    {
        var results = new List<Selection>();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        foreach (var item in items.Where(i => WeeklyScheduleContract.IsExcel(i.Extension) && !string.IsNullOrEmpty(i.LatestVersionUrn)))
        {
            try
            {
                await using var stream = await acc.BaixarConteudoDaVersaoAsync(context.ProjectIdAcc, item.LatestVersionUrn!, context.CancellationToken);
                using var package = new ExcelPackage(stream);
                var result = preflight.ExecutarPreflight(package, schema, context.ProjectIdAcc, folder, item.Name);
                results.Add(new(item, result, result.Sucesso ? null : result.CodigoResultado));
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
            {
                results.Add(new(item, new ResultadoPreflightPlanejamento { Sucesso = false, CodigoResultado = "ABA_INCORRETA", Mensagem = ex.Message }, "ABA_INCORRETA"));
            }
        }
        var valid = results.Where(r => r.Error == null).ToArray();
        if (valid.Length == 1) return valid[0];
        if (valid.Length > 1) return new(null, null, "ARQUIVO_AMBIGUO");
        // Sem candidato válido, a evidência de falha não é uma seleção para importação.
        return results.FirstOrDefault(r => r.Error == "ITEM_OBRIGATORIO_AUSENTE") ?? results.FirstOrDefault() ?? new(null, null, "PASTA_SEM_ARQUIVO");
    }
}
