using System.Text.Json;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Programação Diária na TV (19/10/2026): TvSlide.DataReferencia obrigatória e ISO real na tela
/// gestao-a-vista-programacao-diaria, descartada nas demais; JSON antigo continua válido; vários itens da mesma tela com
/// projetos/datas diferentes; Portal TV só lê a semana da data salva no slide.
/// </summary>
public class TvProgramacaoDiariaTests : IDisposable
{
    private const string Diaria = "gestao-a-vista-programacao-diaria", Semanal = "gestao-a-vista-programacao";
    private const string Chs = TvTelas.ProjetoChs001, Bml = "b.33333333-3333-3333-3333-333333333333";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-diaria-tests-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_dir, "tv-configuration.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* limpeza best-effort */ } }

    private static TvConfiguration Config(params TvSlide[] slides) => new(1, 0, null, null, new[] { new TvPresentation("tv-eng", "TV ENGENHARIA", 30, slides) });

    [Fact]
    public void Catalogo_TelaNova_UmProjeto_DataObrigatoria_MesmasKeysDaSemanal()
    {
        var diaria = TvTelas.Obter(Diaria)!;
        var semanal = TvTelas.Obter(Semanal)!;
        Assert.False(diaria.MultiProjeto);
        Assert.True(diaria.DataReferenciaObrigatoria);
        Assert.Equal(semanal.PermissionKeys, diaria.PermissionKeys);
        Assert.True(semanal.MultiProjeto);                       // semanal intacta
        Assert.False(semanal.DataReferenciaObrigatoria);
        Assert.Single(TvTelas.Todas, t => t.DataReferenciaObrigatoria);
        Assert.Contains(Diaria, TvConfigurationRules.TelasPermitidas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-02-30")]
    [InlineData("2025-02-29")]
    [InlineData("2026-13-01")]
    [InlineData("15/10/2026")]
    [InlineData("2026-1-5")]
    [InlineData("2026-10-15T00:00:00")]
    [InlineData(" 2026-10-15")]
    public void Diaria_SemDataOuDataInvalida_Recusada(string? data) =>
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, "CHS001", null, data))));

    [Theory]
    [InlineData("2026-10-15")]
    [InlineData("2024-02-29")]
    public void Diaria_DataIsoReal_Aceita(string data) =>
        Assert.Equal(data, TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, "CHS001", null, data))).Presentations[0].Slides[0].DataReferencia);

    [Fact]
    public void Diaria_NuncaMesclaProjetos()
    {
        var dois = new[] { new TvSlideProjeto("", Chs, "CHS001"), new TvSlideProjeto("", Bml, "BML011") };
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, "CHS001", dois, "2026-10-15"))));
    }

    [Fact]
    public void DemaisTelas_DataOpcionalDescartada_NaoMudaDados()
    {
        var v = TvConfigurationRules.Validate(Config(new TvSlide(Semanal, "", Chs, "CHS001", null, "2026-10-15"), new TvSlide("ldp", "", Chs, "CHS001", null, "lixo")));
        Assert.All(v.Presentations[0].Slides, s => Assert.Null(s.DataReferencia));
    }

    [Fact]
    public async Task JsonAntigo_SemCampo_ContinuaValido_ESalvaSemAlterarOutrasApresentacoes()
    {
        Directory.CreateDirectory(_dir);
        // Arquivo gravado antes de 19/10/2026: slides sem "dataReferencia".
        await File.WriteAllTextAsync(Arquivo, """
        {"version":1,"revision":3,"savedAt":null,"savedBy":null,"presentations":[
          {"id":"sala","name":"SALA","secondsPerScreen":30,"slides":[{"screenId":"gestao-a-vista-programacao","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":"CHS001","projetos":null}],"ativa":true,"atualizadoEm":"2026-10-01T10:00:00+00:00"},
          {"id":"outra","name":"OUTRA","secondsPerScreen":45,"slides":[{"screenId":"ldp","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":null}]}]}
        """);
        var store = new TvConfigurationStore(Arquivo);
        var lida = await store.ReadAsync();
        Assert.Equal(2, lida.Presentations.Count);
        Assert.Null(lida.Presentations[0].Slides[0].DataReferencia);

        // Acrescenta 3 Programações Diárias (repetição da mesma tela, projeto/data próprios) só na primeira TV.
        var sala = lida.Presentations[0];
        var novaSala = sala with { Slides = sala.Slides.Concat(new[] {
            new TvSlide(Diaria, "", Chs, "CHS001", null, "2026-10-15"),
            new TvSlide(Diaria, "", Bml, "BML011", null, "2026-10-15"),
            new TvSlide(Diaria, "", Chs, "CHS001", null, "2026-10-16") }).ToArray() };
        await store.SaveAsync(new[] { novaSala, lida.Presentations[1] }, 3, "admin");

        var reaberta = await new TvConfigurationStore(Arquivo).ReadAsync();
        Assert.Equal(new string?[] { null, "2026-10-15", "2026-10-15", "2026-10-16" }, reaberta.Presentations[0].Slides.Select(s => s.DataReferencia));
        Assert.Equal(new[] { Chs, Chs, Bml, Chs }, reaberta.Presentations[0].Slides.Select(s => s.ProjectId));
        // Outra apresentação preservada (conteúdo e data de atualização).
        Assert.Equal(JsonSerializer.Serialize(lida.Presentations[1]), JsonSerializer.Serialize(reaberta.Presentations[1]));
        Assert.Equal(lida.Presentations[1].AtualizadoEm, reaberta.Presentations[1].AtualizadoEm);
        // Persistido em camelCase no JSON.
        Assert.Contains("\"dataReferencia\": \"2026-10-16\"", await File.ReadAllTextAsync(Arquivo));
    }

    [Fact]
    public void Limite_50_Itens_TambemValeParaADiaria()
    {
        var slides = Enumerable.Range(0, 51).Select(i => new TvSlide(Diaria, "", Chs, null, null, new DateOnly(2026, 10, 1).AddDays(i).ToString("yyyy-MM-dd"))).ToArray();
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(slides)));
        Assert.Equal(50, TvConfigurationRules.Validate(Config(slides[..50])).Presentations[0].Slides.Count);
    }

    private static IQueryCollection Query(string inicio, string fim) =>
        new QueryCollection(new Dictionary<string, StringValues> { ["inicio"] = inicio, ["fim"] = fim });

    [Fact]
    public void Portal_Diaria_SoAceitaASemanaDaDataSalva()
    {
        var slide = new TvSlide(Diaria, "", Chs, null, null, "2026-10-15");          // quinta → semana 11–17/10
        Assert.True(TvPortalResources.PeriodoPermitido(slide, TvPortalResources.ProgramacaoJamile, Query("2026-10-11", "2026-10-17")));
        Assert.False(TvPortalResources.PeriodoPermitido(slide, TvPortalResources.ProgramacaoJamile, Query("2026-10-04", "2026-10-10")));
        Assert.False(TvPortalResources.PeriodoPermitido(slide, TvPortalResources.ProgramacaoJamile, Query("2026-01-01", "2026-12-31")));
        Assert.False(TvPortalResources.PeriodoPermitido(slide with { DataReferencia = null }, TvPortalResources.ProgramacaoJamile, Query("2026-10-11", "2026-10-17")));
        // Demais recursos do slide (LDP, Norma) e demais telas: sem restrição extra de período.
        Assert.True(TvPortalResources.PeriodoPermitido(slide, "/api/reports/gestao-vista", Query("", "")));
        Assert.True(TvPortalResources.PeriodoPermitido(new TvSlide(Semanal, "", Chs, null), TvPortalResources.ProgramacaoJamile, Query("2026-01-04", "2026-01-10")));
        Assert.Contains(TvPortalResources.ProgramacaoJamile, TvPortalResources.ForScreen(Diaria));
        Assert.True(TvPortalResources.Supported(new TvPresentation("tv", "TV", 30, new[] { slide })));
    }

    [Fact]
    public void Serializacao_Web_CamposOpcionaisDoSlide()
    {
        var json = JsonSerializer.Serialize(new TvSlide(Diaria, "", Chs, null, null, "2026-10-15"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"dataReferencia\":\"2026-10-15\"", json);
        var antigo = JsonSerializer.Deserialize<TvSlide>("{\"screenId\":\"ldp\",\"hubId\":\"\",\"projectId\":\"" + Chs + "\",\"projectName\":null}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(antigo.DataReferencia);
        Assert.Null(antigo.Projetos);
    }
}
