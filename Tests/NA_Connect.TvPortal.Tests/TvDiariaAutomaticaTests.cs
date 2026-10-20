using System.Text.Json;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Programação Diária AUTOMÁTICA (20/10/2026): DataReferencia = "AUTO" aceita só na tela diária, sem duplicar o mesmo projeto;
/// HOJE/ONTEM pela data civil da Bahia; Portal TV só aceita a janela [ontem, hoje] calculada no servidor. Datas fixas, JSON
/// antigo e as outras telas continuam como antes.
/// </summary>
public class TvDiariaAutomaticaTests : IDisposable
{
    private const string Diaria = "gestao-a-vista-programacao-diaria", Semanal = "gestao-a-vista-programacao";
    private const string Chs = TvTelas.ProjetoChs001, Bml = "b.33333333-3333-3333-3333-333333333333";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-diaria-auto-tests-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_dir, "tv-configuration.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* limpeza best-effort */ } }

    private static TvConfiguration Config(params TvSlide[] slides) => new(1, 0, null, null, new[] { new TvPresentation("tv-eng", "TV ENGENHARIA", 30, slides) });
    private static TvSlide Auto(string projeto, string? nome = null) => new(Diaria, "", projeto, nome, null, TvDiaAutomatico.Auto);

    [Fact]
    public void Auto_AceitoNaDiaria_EVariacoesRecusadas()
    {
        Assert.Equal("AUTO", TvConfigurationRules.Validate(Config(Auto(Chs))).Presentations[0].Slides[0].DataReferencia);
        foreach (var ruim in new[] { "auto", " AUTO", "AUTO ", "Auto", "HOJE", "" })
            Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, null, null, ruim))));
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, null))));   // ausência continua erro
        Assert.True(TvConfigurationRules.DataReferenciaDoSlideValida("2026-10-15"));
        Assert.False(TvConfigurationRules.DataReferenciaValida("AUTO"));                                                    // regra de data fixa intacta
    }

    [Fact]
    public void Auto_MesmoProjetoNaoDuplica_OutrosProjetosEDataFixaOk()
    {
        var ex = Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(Auto(Chs, "CHS001"), Auto(Bml), Auto(Chs, "CHS001"))));
        Assert.Contains("CHS001", ex.Message);
        var ok = TvConfigurationRules.Validate(Config(Auto(Chs), Auto(Bml), new TvSlide(Diaria, "", Chs, null, null, "2026-10-15")));
        Assert.Equal(new[] { "AUTO", "AUTO", "2026-10-15" }, ok.Presentations[0].Slides.Select(s => s.DataReferencia));
        // Mesma automática em OUTRA apresentação é permitida.
        var duas = new TvConfiguration(1, 0, null, null, new[] { new TvPresentation("a", "A", 30, new[] { Auto(Chs) }), new TvPresentation("b", "B", 30, new[] { Auto(Chs) }) });
        Assert.Equal(2, TvConfigurationRules.Validate(duas).Presentations.Count);
        // Multiprojeto continua proibido na diária, também em AUTO.
        var dois = new[] { new TvSlideProjeto("", Chs, "CHS001"), new TvSlideProjeto("", Bml, "BML011") };
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(new TvSlide(Diaria, "", Chs, "CHS001", dois, TvDiaAutomatico.Auto))));
    }

    [Fact]
    public void DemaisTelas_AutoDescartado()
    {
        var v = TvConfigurationRules.Validate(Config(new TvSlide(Semanal, "", Chs, null, null, "AUTO"), new TvSlide("gestao-a-vista-visao-geral", "", Chs, null, null, "AUTO")));
        Assert.All(v.Presentations[0].Slides, s => Assert.Null(s.DataReferencia));
    }

    [Fact]
    public async Task JsonAntigo_EDataFixa_Preservados_AoSalvarAutomaticas()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Arquivo, """
        {"version":1,"revision":7,"savedAt":null,"savedBy":null,"presentations":[
          {"id":"sala","name":"SALA","secondsPerScreen":30,"slides":[
            {"screenId":"gestao-a-vista-programacao","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":"CHS001","projetos":null},
            {"screenId":"gestao-a-vista-programacao-diaria","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":"CHS001","dataReferencia":"2026-10-15"}],"ativa":true},
          {"id":"outra","name":"OUTRA","secondsPerScreen":45,"slides":[{"screenId":"ldp","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":null}],"atualizadoEm":"2026-10-01T10:00:00+00:00"}]}
        """);
        var store = new TvConfigurationStore(Arquivo);
        var lida = await store.ReadAsync();
        var sala = lida.Presentations[0];
        var nova = sala with { Slides = new[] { new TvSlide("gestao-a-vista-visao-geral", "", Chs, "CHS001",
            new[] { new TvSlideProjeto("", Chs, "CHS001"), new TvSlideProjeto("", Bml, "BML011") }) }.Concat(sala.Slides).Concat(new[] { Auto(Chs, "CHS001"), Auto(Bml, "BML011") }).ToArray() };
        await store.SaveAsync(new[] { nova, lida.Presentations[1] }, 7, "admin");
        var reaberta = await new TvConfigurationStore(Arquivo).ReadAsync();
        Assert.Equal(new string?[] { null, null, "2026-10-15", "AUTO", "AUTO" }, reaberta.Presentations[0].Slides.Select(s => s.DataReferencia));
        Assert.Equal(JsonSerializer.Serialize(lida.Presentations[1]), JsonSerializer.Serialize(reaberta.Presentations[1]));
        Assert.Contains("\"dataReferencia\": \"AUTO\"", await File.ReadAllTextAsync(Arquivo));
    }

    [Fact]
    public void Limite50_ValeComAutomaticas()
    {
        var slides = Enumerable.Range(0, 51).Select(i => Auto($"b.{i:D8}-0000-0000-0000-000000000000")).ToArray();
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(Config(slides)));
        Assert.Equal(50, TvConfigurationRules.Validate(Config(slides[..50])).Presentations[0].Slides.Count);
    }

    [Theory]
    [InlineData("2026-10-20T02:59:59Z", "2026-10-19")]   // 23:59:59 em Salvador
    [InlineData("2026-10-20T03:00:00Z", "2026-10-20")]   // 00:00:00 em Salvador
    [InlineData("2026-10-25T14:00:00Z", "2026-10-25")]   // domingo
    [InlineData("2026-01-01T02:00:00Z", "2025-12-31")]   // virada de ano
    public void Hoje_DataCivilDaBahia(string instante, string esperado) =>
        Assert.Equal(DateOnly.Parse(esperado), TvDiaAutomatico.Hoje(DateTimeOffset.Parse(instante)));

    [Fact]
    public void Janela_OntemEHoje_DomingoESegundaCruzamASemana()
    {
        Assert.Equal((new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25)), TvDiaAutomatico.Janela(new DateOnly(2026, 10, 25)));   // domingo ← sábado
        Assert.Equal((new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26)), TvDiaAutomatico.Janela(new DateOnly(2026, 10, 26)));   // segunda ← domingo
    }

    private static IQueryCollection Query(string inicio, string fim) =>
        new QueryCollection(new Dictionary<string, StringValues> { ["inicio"] = inicio, ["fim"] = fim });
    private static bool Pode(TvSlide slide, string inicio, string fim, string agora, string resource = TvPortalResources.ProgramacaoJamile) =>
        TvPortalResources.PeriodoPermitido(slide, resource, Query(inicio, fim), DateTimeOffset.Parse(agora));

    [Fact]
    public void Portal_Auto_SoAJanelaCalculadaNoServidor()
    {
        var auto = Auto(Chs);
        const string terca = "2026-10-20T15:00:00Z";
        Assert.True(Pode(auto, "2026-10-19", "2026-10-20", terca));
        Assert.False(Pode(auto, "2026-10-18", "2026-10-24", terca), "semana inteira");
        Assert.False(Pode(auto, "2026-10-20", "2026-10-20", terca), "só hoje");
        Assert.False(Pode(auto, "2026-10-18", "2026-10-19", terca), "dia anterior fora da tolerância");
        Assert.False(Pode(auto, "2026-10-20", "2026-10-19", terca), "invertida");
        Assert.False(Pode(auto, "2026-01-01", "2026-12-31", terca), "ano");
        Assert.False(Pode(auto, "", "", terca));
        // Domingo: janela cruza a semana.
        Assert.True(Pode(auto, "2026-10-24", "2026-10-25", "2026-10-25T15:00:00Z"));
        // Virada do dia: ±5 min aceitam as duas janelas; fora da tolerância só a vigente.
        Assert.True(Pode(auto, "2026-10-19", "2026-10-20", "2026-10-20T02:57:00Z"));   // TV já virou, servidor 23:57
        Assert.True(Pode(auto, "2026-10-18", "2026-10-19", "2026-10-20T03:02:00Z"));   // TV atrasada, servidor 00:02
        Assert.False(Pode(auto, "2026-10-18", "2026-10-19", "2026-10-20T03:10:00Z"));
        // Outros recursos do slide (LDP para a conciliação, Norma): sem restrição de período, como antes.
        Assert.True(Pode(auto, "", "", terca, "/api/reports/gestao-vista"));
        // Slide de data FIXA continua só com a semana da data salva (inalterado).
        var fixa = new TvSlide(Diaria, "", Chs, null, null, "2026-10-15");
        Assert.True(Pode(fixa, "2026-10-11", "2026-10-17", terca));
        Assert.False(Pode(fixa, "2026-10-19", "2026-10-20", terca));
        // Telas sem data (semanal) inalteradas.
        Assert.True(Pode(new TvSlide(Semanal, "", Chs, null), "2026-01-04", "2026-01-10", terca));
    }
}
