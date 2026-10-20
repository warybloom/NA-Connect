using APS.WebApp.Services.ProjectIdentity;

namespace NA_Connect.Planning.Tests;

/// <summary>Logo do Projeto: configurada uma vez, lida por todas as telas; só imagem real (bytes), sem SVG.</summary>
public class ProjectIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logo-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best-effort */ } }

    private const string Proj = "b.22222222-2222-2222-2222-222222222222";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9];

    [Fact]
    public async Task Salvar_Reabrir_Trocar_Remover()
    {
        var store = new ProjectIdentityStore(_dir);
        Assert.Empty(await store.ListAsync());
        var v1 = await store.SaveAsync(Proj, Png, "adm@na.com");
        Assert.Equal((1L, "image/png"), (v1.Version, v1.ContentType));

        var reaberto = new ProjectIdentityStore(_dir);                                   // app reiniciado
        var lido = await reaberto.ReadAsync(Proj);
        Assert.Equal(Png, lido!.Value.Bytes);
        Assert.Single(await reaberto.ListAsync());

        var v2 = await reaberto.SaveAsync(Proj, Jpg, null);                                // troca de formato
        Assert.Equal((2L, "image/jpeg"), (v2.Version, v2.ContentType));
        Assert.False(File.Exists(Path.Combine(_dir, Proj + ".png")));                      // arquivo antigo não sobra

        Assert.True(await reaberto.DeleteAsync(Proj));
        Assert.Null(await reaberto.ReadAsync(Proj));
        Assert.Empty(await reaberto.ListAsync());
    }

    [Fact]
    public async Task Recusa_Svg_TextoDisfarcado_Grande_E_ProjetoInvalido()
    {
        var store = new ProjectIdentityStore(_dir);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Proj, "<svg onload=alert(1)>"u8.ToArray(), null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Proj, [.. Png, .. new byte[ProjectIdentityStore.TamanhoMaximo]], null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("../../etc", Png, null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Proj, [], null));
        Assert.Null(await store.ReadAsync("../index.json"));
    }
}
