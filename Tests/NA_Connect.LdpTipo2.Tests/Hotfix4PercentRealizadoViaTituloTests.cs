using System;
using System.IO;
using System.Linq;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using Microsoft.Extensions.Logging.Abstractions;
using net.sf.mpxj;
using net.sf.mpxj.reader;
using Xunit.Abstractions;

namespace NA_Connect.LdpTipo2.Tests;

/// <summary>
/// HOTFIX 4 (checkpoint "Resolução de % Realizado por título de coluna"): comprovado por análise direta
/// do MPP real (TBkndTask/Props + CTable/Var2Data, feita pelo usuário) que "% Realizado" NUNCA existiu
/// como alias de CustomField nesse arquivo — é só o TÍTULO de uma coluna de apresentação (Table "Entry"),
/// apontando para TaskField.TEXT3, sem alias nenhum. Estes testes provam, contra o MPP real já versionado
/// (Services/cronograma/CHS001-000-PMO-SC-001-05.mpp — mesma fonte real usada no HOTFIX 2) e contra
/// cenários sintéticos mínimos, que a segunda via de resolução (CronogramaTipo2FieldResolver.
/// TentarResolverPorTituloDeTabela) encontra o FieldType certo, nunca escolhe arbitrariamente entre
/// FieldTypes distintos, e nunca aceita colidir com o campo já usado por Planned Physical.
/// </summary>
public class Hotfix4PercentRealizadoViaTituloTests
{
    private readonly ITestOutputHelper _output;

    public Hotfix4PercentRealizadoViaTituloTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string CaminhoMppReal()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.csproj"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Services", "cronograma", "CHS001-000-PMO-SC-001-05.mpp");
    }

    private static void AdicionarTabelaComColuna(ProjectFile project, string nomeTabela, FieldType fieldType, string titulo)
    {
        var tabela = new net.sf.mpxj.Table();
        tabela.setName(nomeTabela);
        var coluna = new Column(project);
        coluna.setFieldType(fieldType);
        coluna.setTitle(titulo);
        tabela.addColumn(coluna);
        project.getTables().add(tabela);
    }

    [Fact]
    public void MppReal_NaoTemAliasDeCustomField_MasResolvePorTituloDeColunaParaText3()
    {
        var project = new UniversalProjectReader().read(CaminhoMppReal());
        var resolver = new CronogramaTipo2FieldResolver(project);

        // Reconfirma o achado do HOTFIX 2/3: nenhum CustomField deste MPP tem alias "%realizado"/"% Realizado".
        bool viaAlias = resolver.TentarResolverQualquer(new[] { "%realizado", "% Realizado" }, out _, out _);
        Assert.False(viaAlias);

        // Segunda via: título de coluna de Table — deve achar exatamente TEXT3, sem ambiguidade.
        bool viaTitulo = resolver.TentarResolverPorTituloDeTabela(
            project, new[] { "%realizado", "% Realizado" }, out FieldType fieldType, out var candidatos);

        Assert.True(viaTitulo);
        Assert.True(TaskField.TEXT3.Equals(fieldType));
        Assert.Single(candidatos.Select(c => c.FieldType).Distinct());
        _output.WriteLine($"FieldType={fieldType} | DataType={fieldType.getDataType()} | Tabela={candidatos[0].Tabela} | Título=\"{candidatos[0].TituloOriginal}\"");
    }

    [Fact]
    public void MppReal_PercentRealizadoDeixaDeSerNullAposFallbackDeTitulo()
    {
        var project = new UniversalProjectReader().read(CaminhoMppReal());
        var parser = new CronogramaTipo2Parser(NullLogger<CronogramaTipo2Parser>.Instance);

        var resultado = parser.ProcessarProjeto(project, new CronogramaTipo2Resultado(), 2, DateTime.UtcNow, DateTime.UtcNow);

        var comValor = resultado.Registros.Where(r => r.PercentRealizado.HasValue).ToList();
        Assert.NotEmpty(comValor);
        // Amostra conhecida do MPP real (task "Purchase Order (CHS)", TEXT3="100%").
        Assert.Contains(comValor, r => r.NomeTarefa == "Purchase Order (CHS)" && r.PercentRealizado == 100m);
    }

    [Fact]
    public void TituloDuplicadoEmFieldTypesDiferentes_NuncaEscolheArbitrariamente()
    {
        var project = new ProjectFile();
        AdicionarTabelaComColuna(project, "Entry", TaskField.TEXT3, "% Realizado");
        AdicionarTabelaComColuna(project, "Tracking", TaskField.TEXT5, "% Realizado");

        var resolver = new CronogramaTipo2FieldResolver(project);
        bool encontrado = resolver.TentarResolverPorTituloDeTabela(
            project, new[] { "%realizado", "% Realizado" }, out _, out var candidatos);

        Assert.False(encontrado);
        Assert.Equal(2, candidatos.Select(c => c.FieldType).Distinct().Count());
    }

    [Fact]
    public void TituloRealizadoIgualAoCampoJaUsadoPorPlannedPhysical_EhRejeitadoNuncaAceitoPorColisao()
    {
        var project = new ProjectFile();
        project.getCustomFields().getOrCreate(TaskField.TEXT11).setAlias("% Previsto Reprog.");
        // Cenário defensivo (nunca observado no MPP real): "% Realizado" apontando para o MESMO campo
        // já resolvido como fonte de Planned Physical — precisa ser rejeitado, nunca aceito.
        AdicionarTabelaComColuna(project, "Entry", TaskField.TEXT11, "% Realizado");

        var task = project.addTask();
        task.set(TaskField.TEXT11, "42%");

        var parser = new CronogramaTipo2Parser(NullLogger<CronogramaTipo2Parser>.Instance);
        var resultado = parser.ProcessarProjeto(project, new CronogramaTipo2Resultado(), 2, DateTime.UtcNow, DateTime.UtcNow);

        Assert.Null(Assert.Single(resultado.Registros).PercentRealizado);
        Assert.Contains(resultado.Avisos, a => a.Contains("MESMO FieldType"));
    }
}
