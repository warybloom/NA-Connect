using System.Text.RegularExpressions;
using APS.WebApp.Services.Reports;

namespace NA_Connect.Planning.Tests;

/// <summary>Auditoria do Projeto: Diferença = LDP − TIMESHEET; LDP = SUM(Actual_HH) (nunca Actual_HH_Total_Corrigido).</summary>
public sealed class ProductionAuditReconciliationRulesTests
{
    private static ProductionAuditSourceDocument D(decimal actual, decimal planned = 0, string? disc = "Elétrica") => new(disc, planned, actual, null);
    private static Dictionary<string, ProductionAuditSourceDocument> Map(params (string Code, ProductionAuditSourceDocument Doc)[] xs) => xs.ToDictionary(x => x.Code, x => x.Doc, StringComparer.Ordinal);

    [Fact]
    public void A_LdpMaiorQueTimesheet_DiferencaPositiva()
    {
        var r = ProductionAuditReconciliationRules.Documento("DOC-1", D(30), D(20));
        Assert.Equal(10m, r.Difference);
        Assert.Equal(ProductionAuditReconciliationRules.RealizadaDiferente, r.Status);
        Assert.Equal(30m, r.LdpActual); Assert.Equal(20m, r.TimesheetActual);
    }

    [Fact]
    public void B_LdpMenorQueTimesheet_DiferencaNegativa()
    {
        var r = ProductionAuditReconciliationRules.Documento("DOC-2", D(10), D(20));
        Assert.Equal(-10m, r.Difference);
        Assert.Equal(ProductionAuditReconciliationRules.RealizadaDiferente, r.Status);
    }

    [Fact]
    public void C_ValoresIguais_Conciliado()
    {
        var r = ProductionAuditReconciliationRules.Documento("DOC-3", D(15, 40), D(15.004m, 40));
        Assert.Equal(ProductionAuditReconciliationRules.Conciliado, r.Status);
        Assert.Contains("compatíveis", r.Reason);
    }

    [Fact]
    public void Totais_Consistentes_ComSomaDosDocumentos_EPresencaPreservada()
    {
        var o = ProductionAuditReconciliationRules.Conciliar("BML011.013",
            Map(("A", D(30)), ("B", D(10)), ("L", D(12))),
            Map(("A", D(20)), ("B", D(20)), ("T", D(8, disc: null))), ldpSemDocumento: 5, tsSemDocumento: 3);
        Assert.Equal(o.Documents.Sum(d => d.Difference), o.Difference);
        Assert.Equal(52m - 48m, o.Difference);
        Assert.Equal(ProductionAuditReconciliationRules.SomenteLdp, o.Documents.Single(d => d.DocumentCode == "L").Status);
        Assert.Equal(ProductionAuditReconciliationRules.SomenteTimesheet, o.Documents.Single(d => d.DocumentCode == "T").Status);
        Assert.Null(o.Documents.Single(d => d.DocumentCode == "T").Discipline);
        Assert.Equal(5m, o.LdpHoursWithoutDocument); Assert.Equal(3m, o.TimesheetHoursWithoutDocument);
        Assert.Equal(ProductionAuditReconciliationRules.Tolerancia, o.Tolerance);
        Assert.Equal([ProductionAuditReconciliationRules.NomeLdp, ProductionAuditReconciliationRules.NomeTimesheet], o.Sources.Select(s => s.Name));
    }

    [Fact]
    public void D_FonteLdp_UsaActualHH_ENaoOCorrigido()
    {
        var sql = ProductionAuditRepository.ReconciliationSql;
        Assert.DoesNotContain("Actual_HH_Total_Corrigido", sql);
        Assert.DoesNotContain("Actual_HH_Corrigido", sql);
        Assert.Matches(new Regex(@"SUM\(ISNULL\(Actual_HH,0\)\) Realizado\s*,\s*MAX\(Data_Sincronizacao\)\s*\S*\s*FROM dbo\.VW_Ldp_Unificada|SUM\(ISNULL\(Actual_HH,0\)\) Realizado,MAX\(Data_Sincronizacao\) Atualizado\s+FROM dbo\.VW_Ldp_Unificada"), sql);
    }
}
