using APS.WebApp.Services;
using APS.WebApp.Services.DocumentQuality;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>Auditoria de Qualidade: a pasta configurada é RAIZ — todos os PDFs da árvore (qualquer profundidade) entram.</summary>
public sealed class AccFolderTraversalTests
{
    private static AccFolderItem Pasta(string id, string nome) => new() { Id = id, Name = nome, Type = "folders" };
    private static AccFolderItem Pdf(string id, string nome) => new() { Id = id, Name = nome, Type = "items", VersionUrn = "v:" + id };

    /// <summary>ACC falso: FolderId → conteúdo (o lister real é GetFolderContentsAllPagesAsync, que já junta as páginas).</summary>
    private sealed class AccFalso(Dictionary<string, AccFolderItem[]> arvore)
    {
        public List<string> Listadas { get; } = new();
        public Task<IReadOnlyList<AccFolderItem>> Listar(string folderId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Listadas.Add(folderId);
            return Task.FromResult<IReadOnlyList<AccFolderItem>>(arvore.TryGetValue(folderId, out var itens) ? itens : Array.Empty<AccFolderItem>());
        }
    }

    private static readonly Func<AccFolderItem, bool> SoPdf = i => i.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    private static Task<AccTreeResult> Coletar(AccFalso acc, CancellationToken ct = default) =>
        AccFolderTraversal.CollectAsync("raiz", "Project Files/06 - ENGINEERING", acc.Listar, SoPdf, ct);

    [Fact]
    public async Task T1_RaizComPdfDireto()
    {
        var r = await Coletar(new(new() { ["raiz"] = new[] { Pdf("i1", "A.pdf"), Pdf("i2", "B.dwg") } }));
        Assert.Equal(new[] { "A.pdf" }, r.Files.Select(f => f.Item.Name));
    }

    [Fact]
    public async Task T2_RaizSemPdf_SubpastaComPdf()
    {
        var r = await Coletar(new(new() { ["raiz"] = new[] { Pasta("p1", "06.01 - PROCESS") }, ["p1"] = new[] { Pdf("i1", "doc1.pdf") } }));
        Assert.Equal("doc1.pdf", Assert.Single(r.Files).Item.Name);
    }

    [Fact]
    public async Task T3_TresNiveis_T9_CaminhoRealPreservado()
    {
        var r = await Coletar(new(new()
        {
            ["raiz"] = new[] { Pasta("p1", "06.01 - PROCESS") },
            ["p1"] = new[] { Pasta("p2", "P&ID") },
            ["p2"] = new[] { Pasta("p3", "TRAIN 01"), Pdf("i2", "nivel2.pdf") },
            ["p3"] = new[] { Pdf("i3", "nivel3.pdf") },
        }));
        Assert.Equal(new[] { "Project Files/06 - ENGINEERING/06.01 - PROCESS/P&ID", "Project Files/06 - ENGINEERING/06.01 - PROCESS/P&ID/TRAIN 01" },
            r.Files.Select(f => f.FolderPath));
        Assert.Equal("p3", r.Files.Single(f => f.Item.Name == "nivel3.pdf").FolderId);
        Assert.Equal(4, r.FoldersVisited);
    }

    [Fact]
    public async Task T4_VariasSubpastasIrmas()
    {
        var r = await Coletar(new(new()
        {
            ["raiz"] = new[] { Pasta("pr", "06.01 - PROCESS"), Pasta("pi", "06.02 - PIPING"), Pasta("me", "06.03 - MECHANICAL") },
            ["pr"] = new[] { Pdf("1", "doc1.pdf"), Pdf("2", "doc2.pdf") }, ["pi"] = new[] { Pdf("3", "doc3.pdf") }, ["me"] = new[] { Pdf("4", "doc4.pdf") },
        }));
        Assert.Equal(new[] { "doc1.pdf", "doc2.pdf", "doc3.pdf", "doc4.pdf" }, r.Files.Select(f => f.Item.Name));
    }

    [Fact]
    public async Task T5_PastaVazia()
    {
        var r = await Coletar(new(new() { ["raiz"] = new[] { Pasta("vazia", "06.04 - CIVIL") } }));
        Assert.Empty(r.Files);
        Assert.Equal(2, r.FoldersVisited);
    }

    [Fact]
    public async Task T6_SubpastaPaginada_TodosOsItensEntram_ListadaUmaVez()
    {
        // GetFolderContentsAllPagesAsync devolve TODAS as páginas da pasta (ex.: 3 páginas de 100); a travessia não repagina.
        var muitos = Enumerable.Range(1, 250).Select(i => Pdf($"i{i}", $"doc{i:000}.pdf")).ToArray();
        var acc = new AccFalso(new() { ["raiz"] = new[] { Pasta("p1", "PROCESS") }, ["p1"] = muitos });
        var r = await Coletar(acc);
        Assert.Equal(250, r.Files.Count);
        Assert.Equal(1, acc.Listadas.Count(id => id == "p1"));
    }

    [Fact]
    public async Task T7_CicloEFolderIdRepetido_NaoGeramLaco()
    {
        var acc = new AccFalso(new()
        {
            ["raiz"] = new[] { Pasta("p1", "A"), Pasta("p1", "A (atalho)") },
            ["p1"] = new[] { Pasta("raiz", "volta para a raiz"), Pasta("p2", "B") },
            ["p2"] = new[] { Pasta("p1", "volta para A"), Pdf("i1", "x.pdf") },
        });
        var r = await Coletar(acc);
        Assert.Single(r.Files);
        Assert.Equal(new[] { "raiz", "p1", "p2" }, acc.Listadas);
    }

    [Fact]
    public async Task T8_MesmoDocumentoNaoEntraDuasVezes()
    {
        var r = await Coletar(new(new()
        {
            ["raiz"] = new[] { Pdf("i1", "doc.pdf"), Pdf("i1", "doc.pdf"), Pasta("p1", "SUB") },
            ["p1"] = new[] { Pdf("i1", "doc.pdf") },
        }));
        Assert.Single(r.Files);
    }

    [Fact]
    public async Task T10_CancellationInterrompeATravessia()
    {
        using var cts = new CancellationTokenSource();
        var acc = new AccFalso(new() { ["raiz"] = new[] { Pasta("p1", "A"), Pasta("p2", "B") }, ["p1"] = new[] { Pdf("i1", "a.pdf") } });
        Task<IReadOnlyList<AccFolderItem>> ListarECancelar(string id, CancellationToken ct) { if (id == "p1") cts.Cancel(); return acc.Listar(id, ct); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AccFolderTraversal.CollectAsync("raiz", "R", ListarECancelar, SoPdf, cts.Token));
        Assert.DoesNotContain("p2", acc.Listadas);
    }

    [Fact]
    public async Task SoDesceEmFolders_ItemNuncaViraPasta_OcultosIgnorados()
    {
        var acc = new AccFalso(new()
        {
            ["raiz"] = new[] { new AccFolderItem { Id = "lnk", Name = "atalho.pdf", Type = "items" }, new AccFolderItem { Id = "h", Name = "oculta", Type = "folders", IsHidden = true } },
            ["lnk"] = new[] { Pdf("x", "nao-deveria.pdf") }, ["h"] = new[] { Pdf("y", "oculto.pdf") },
        });
        var r = await Coletar(acc);
        Assert.Equal(new[] { "atalho.pdf" }, r.Files.Select(f => f.Item.Name));
        Assert.Equal(new[] { "raiz" }, acc.Listadas);
    }
}
