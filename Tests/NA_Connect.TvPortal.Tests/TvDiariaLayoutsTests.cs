using System.Text.Json;
using APS.WebApp.Services.Tv;
using APS.WebApp.Services.Tv.Portal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Exibições do slide AUTOMÁTICO (20/10/2026): Padrão (ausente), Cards por disciplina e Uma tela por disciplina, por slide.
/// Duplicata só com mesmo projeto + mesma exibição + mesma disciplina; campos descartados em qualquer outro slide; JSON sem os
/// campos quando padrão (arquivo idêntico ao anterior); Portal TV com a mesma janela/recursos.
/// </summary>
public class TvDiariaLayoutsTests : IDisposable
{
    private const string Diaria = "gestao-a-vista-programacao-diaria", Semanal = "gestao-a-vista-programacao";
    private const string Chs = TvTelas.ProjetoChs001, Bml = "b.33333333-3333-3333-3333-333333333333";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-diaria-layouts-tests-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_dir, "tv-configuration.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* limpeza best-effort */ } }

    private static TvConfiguration Config(params TvSlide[] slides) => new(1, 0, null, null, new[] { new TvPresentation("tv-eng", "TV ENGENHARIA", 30, slides) });
    private static TvSlide Auto(string projeto, string? layout = null, string? disciplina = null, string? nome = null) =>
        new(Diaria, "", projeto, "CHS001", null, TvDiaAutomatico.Auto) { LayoutDiario = layout, Disciplina = disciplina, DisciplinaNome = nome };
    private static IReadOnlyList<TvSlide> Validos(params TvSlide[] slides) => TvConfigurationRules.Validate(Config(slides)).Presentations[0].Slides;

    [Fact]
    public void Exibicoes_AceitasENormalizadas()
    {
        var v = Validos(Auto(Chs), Auto(Bml, "padrao", "lixo", "lixo"), Auto(Bml, "cards", "C:X", "X"), Auto(Chs, "disciplina", "  C:MECHANICAL ", " MECÂNICA "), Auto(Chs, "cards"));
        Assert.Equal(new string?[] { null, null, "cards", "disciplina", "cards" }, v.Select(s => s.LayoutDiario));
        Assert.Equal(new string?[] { null, null, null, "C:MECHANICAL", null }, v.Select(s => s.Disciplina));
        Assert.Equal(new string?[] { null, null, null, "MECÂNICA", null }, v.Select(s => s.DisciplinaNome));
        Assert.All(v, s => Assert.Equal("AUTO", s.DataReferencia));
    }

    [Fact]
    public void Exibicoes_InvalidasRecusadas()
    {
        foreach (var ruim in new[] { "Cards", "CARDS", "padrão", "mosaico", "" })
            Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, ruim)));
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "disciplina")));
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "disciplina", "   ")));
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "disciplina", new string('X', 201))));
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "disciplina", "C:X", new string('X', 201))));
    }

    [Fact]
    public void Duplicata_SoMesmoProjetoMesmaExibicaoMesmaDisciplina()
    {
        // Distintos: exibições diferentes e disciplinas diferentes do mesmo projeto; mesma disciplina em outro projeto.
        Assert.Equal(6, Validos(Auto(Chs), Auto(Chs, "cards"), Auto(Chs, "disciplina", "C:MECHANICAL"), Auto(Chs, "disciplina", "C:CIVIL"),
            Auto(Bml, "disciplina", "C:MECHANICAL"), Auto(Bml)).Count);
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs), Auto(Chs, "padrao")));                         // padrão explícito = ausente
        Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "cards"), Auto(Chs, "cards")));
        var ex = Assert.Throws<ArgumentException>(() => Validos(Auto(Chs, "disciplina", "C:MECHANICAL", "MECÂNICA"), Auto(Chs, "disciplina", " C:MECHANICAL")));
        Assert.Contains("disciplina · C:MECHANICAL", ex.Message);   // item repetido sem rótulo: identificado pela chave
    }

    [Fact]
    public void Campos_DescartadosForaDoSlideAutomatico()
    {
        var v = Validos(new TvSlide(Diaria, "", Chs, null, null, "2026-10-15") { LayoutDiario = "cards", Disciplina = "C:X" },
            new TvSlide(Semanal, "", Chs, null) { LayoutDiario = "disciplina", Disciplina = "C:X", DisciplinaNome = "X" },
            new TvSlide(Diaria, "", Chs, null, null, "2026-10-16") { LayoutDiario = "mosaico" });   // data fixa: valor ignorado, não recusado
        Assert.All(v, s => { Assert.Null(s.LayoutDiario); Assert.Null(s.Disciplina); Assert.Null(s.DisciplinaNome); });
        Assert.Equal(new string?[] { "2026-10-15", null, "2026-10-16" }, v.Select(s => s.DataReferencia));
    }

    [Fact]
    public async Task Json_PadraoSemCamposNovos_ExibicoesPersistidasPorSlide()
    {
        var store = new TvConfigurationStore(Arquivo);
        await store.SaveAsync(Config(Auto(Chs), Auto(Chs, "cards"), Auto(Chs, "disciplina", "C:MECHANICAL", "MECÂNICA"),
            new TvSlide(Semanal, "", Chs, "CHS001")).Presentations, 0, "admin");
        var texto = await File.ReadAllTextAsync(Arquivo);
        using (var doc = JsonDocument.Parse(texto))
        {
            var slides = doc.RootElement.GetProperty("presentations")[0].GetProperty("slides");
            Assert.False(slides[0].TryGetProperty("layoutDiario", out _), "padrão = mesmo JSON de antes");
            Assert.False(slides[0].TryGetProperty("disciplina", out _));
            Assert.False(slides[3].TryGetProperty("layoutDiario", out _), "outras telas sem o campo");
            Assert.Equal("cards", slides[1].GetProperty("layoutDiario").GetString());
            Assert.Equal("C:MECHANICAL", slides[2].GetProperty("disciplina").GetString());
            Assert.Equal("MECÂNICA", slides[2].GetProperty("disciplinaNome").GetString());
        }
        var lida = (await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations[0].Slides;
        Assert.Equal(new string?[] { null, "cards", "disciplina", null }, lida.Select(s => s.LayoutDiario));
        Assert.Equal("C:MECHANICAL", lida[2].Disciplina);
        // Arquivo anterior (sem os campos) continua lido como padrão.
        await File.WriteAllTextAsync(Arquivo, """
        {"version":1,"revision":3,"savedAt":null,"savedBy":null,"presentations":[{"id":"sala","name":"SALA","secondsPerScreen":30,"slides":[
          {"screenId":"gestao-a-vista-programacao-diaria","hubId":"","projectId":"b.fcdb7ebd-1b23-440d-a04b-90391098522e","projectName":"CHS001","dataReferencia":"AUTO"}]}]}
        """);
        var antigo = (await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations[0].Slides[0];
        Assert.Null(antigo.LayoutDiario);
        Assert.Equal("AUTO", antigo.DataReferencia);
    }

    [Fact]
    public void Limite50_ContaCadaSlideDeDisciplina()
    {
        var slides = Enumerable.Range(0, 51).Select(i => Auto(Chs, "disciplina", $"N:D{i}")).ToArray();
        Assert.Throws<ArgumentException>(() => Validos(slides));
        Assert.Equal(50, Validos(slides[..50]).Count);
    }

    [Fact]
    public void Portal_MesmaJanelaParaTodasAsExibicoes()
    {
        var q = new QueryCollection(new Dictionary<string, StringValues> { ["inicio"] = "2026-10-19", ["fim"] = "2026-10-20" });
        var semana = new QueryCollection(new Dictionary<string, StringValues> { ["inicio"] = "2026-10-18", ["fim"] = "2026-10-24" });
        var agora = DateTimeOffset.Parse("2026-10-20T15:00:00Z");
        foreach (var s in new[] { Auto(Chs), Auto(Chs, "cards"), Auto(Chs, "disciplina", "C:MECHANICAL") })
        {
            Assert.True(TvPortalResources.PeriodoPermitido(s, TvPortalResources.ProgramacaoJamile, q, agora));
            Assert.False(TvPortalResources.PeriodoPermitido(s, TvPortalResources.ProgramacaoJamile, semana, agora));
        }
    }
}
