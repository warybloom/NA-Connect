using NA_Connect.Services.Coordination;
using OfficeOpenXml;
namespace NA_Connect.GestaoVista.Tests;

/// <summary>Validação do Excel de importação da Norma de Coordenação (SIGLA, DISCIPLINA) — nunca persiste.</summary>
public class CoordinationExcelImportTests
{
    static CoordinationExcelImportTests() => ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

    private static byte[] BuildXlsx(params (string? Sigla, string? Disciplina)[] linhas)
    {
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Normas");
        ws.Cells[1, 1].Value = "SIGLA"; ws.Cells[1, 2].Value = "DISCIPLINA";
        for (var i = 0; i < linhas.Length; i++)
        {
            ws.Cells[i + 2, 1].Value = linhas[i].Sigla;
            ws.Cells[i + 2, 2].Value = linhas[i].Disciplina;
        }
        return package.GetAsByteArray();
    }

    [Fact]
    public void ValidRowsAreAcceptedAndSiglaIsNormalized()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx(("cv", "Civil"), (" EL ", "Elétrica")), new HashSet<string>());
        Assert.Equal(2, resultado.LinhasValidas); Assert.Equal(0, resultado.LinhasInvalidas);
        Assert.Equal("CV", resultado.Linhas[0].SiglaNormalizada); Assert.Equal("EL", resultado.Linhas[1].SiglaNormalizada);
    }
    [Fact]
    public void EmptySiglaIsRejected()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx((null, "Civil")), new HashSet<string>());
        Assert.False(resultado.Linhas.Single().Valida);
        Assert.Contains("SIGLA vazia.", resultado.Linhas.Single().Erros);
    }
    [Fact]
    public void EmptyDisciplinaIsRejected()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx(("CV", null)), new HashSet<string>());
        Assert.False(resultado.Linhas.Single().Valida);
        Assert.Contains("DISCIPLINA vazia.", resultado.Linhas.Single().Erros);
    }
    [Fact]
    public void DuplicateSiglaWithinTheSameSheetIsRejected()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx(("CV", "Civil"), ("cv", "Civil 2")), new HashSet<string>());
        Assert.True(resultado.Linhas[0].Valida);
        Assert.False(resultado.Linhas[1].Valida);
        Assert.Contains(resultado.Linhas[1].Erros, e => e.Contains("duplicada"));
    }
    [Fact]
    public void FullyEmptyRowIsSkippedSilently()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx((null, null), ("CV", "Civil")), new HashSet<string>());
        Assert.Equal(1, resultado.TotalLinhas);
    }
    [Fact]
    public void SiglaAlreadyRegisteredIsOnlyAWarningNeverAnError()
    {
        var resultado = CoordinationExcelImport.Validar(BuildXlsx(("CV", "Civil")), new HashSet<string> { "CV" });
        var linha = resultado.Linhas.Single();
        Assert.True(linha.Valida);
        Assert.Contains(linha.Avisos, a => a.Contains("já cadastrada"));
    }
}
