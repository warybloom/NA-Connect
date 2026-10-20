using APS.WebApp.Services.UploadGovernado;
using S = APS.WebApp.Services.UploadGovernado.UploadGovernadoStatus;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Envio governado pela LDP por ÁRVORE DE PASTAS: pastas-raiz protegidas valem para todas as subpastas (ancestralidade por
/// ID/parent); dentro da árvore, existe na LDP → permitido, não existe → bloqueado; disciplina × pasta não é validada.
/// </summary>
public class UploadGovernadoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-ldp-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best-effort */ } }

    private static readonly LdpDocumentoAtual[] Ldp =
    {
        new(1, "CHS001-000-PC1-RE-001", "CLI-PRC-001", "PROCESSO", "Relatório", "0"),
        new(2, "CHS001-000-ME1-DS-010", null, "MECÂNICA", "Folha de dados", "B"),
        new(4, "CHS001-000-XX1-RE-777", null, "PROCESSO", "Duplicado 1", "0"),
        new(5, "CHS001-000-XX1-RE-777", null, "PROCESSO", "Duplicado 2", "0"),
    };

    // Árvore do ACC (filho → pai):
    // ProjectFiles ─┬─ 09-ENG ── MEC ── SUB1 ── SUB2 ── SUB3      (09-ENG protegida)
    //               ├─ 10-CLI ── RECEBIDOS                         (10-CLI protegida)
    //               └─ 11-INT ── RASCUNHOS                         (não protegida)
    private static readonly Dictionary<string, string?> Pai = new()
    {
        ["ProjectFiles"] = null, ["09-ENG"] = "ProjectFiles", ["MEC"] = "09-ENG", ["SUB1"] = "MEC", ["SUB2"] = "SUB1", ["SUB3"] = "SUB2",
        ["10-CLI"] = "ProjectFiles", ["RECEBIDOS"] = "10-CLI", ["11-INT"] = "ProjectFiles", ["RASCUNHOS"] = "11-INT",
    };
    private static readonly PastaProtegida Eng = new("09-ENG", "Project Files/09 - DOCUMENTOS DE ENGENHARIA");
    private static readonly PastaProtegida Cli = new("10-CLI", "Project Files/10 - DOCUMENTOS CLIENTE");
    private static readonly PastaProtegida[] Raizes = { Eng, Cli };
    private static Task<PastaProtegida?> Raiz(string destino) =>
        UploadGovernadoRules.RaizQueContemAsync(destino, Raizes, id => Task.FromResult(Pai.TryGetValue(id, out var p) ? p : null));

    [Theory]
    [InlineData("09-ENG", "09-ENG")]       // a própria pasta raiz
    [InlineData("MEC", "09-ENG")]          // subpasta direta
    [InlineData("SUB3", "09-ENG")]         // 4 níveis abaixo
    [InlineData("RECEBIDOS", "10-CLI")]    // segunda raiz configurada ao mesmo tempo
    public async Task DestinoNaArvoreProtegida_ResolvePorPai(string destino, string raizEsperada) =>
        Assert.Equal(raizEsperada, (await Raiz(destino))?.FolderId);

    [Theory]
    [InlineData("RASCUNHOS")]
    [InlineData("11-INT")]
    [InlineData("ProjectFiles")]
    public async Task DestinoForaDasRaizes_RegraNaoAplicada(string destino)
    {
        Assert.Null(await Raiz(destino));
        var v = UploadGovernadoRules.Validar("QUALQUER-COISA-001.pdf", Ldp, null, destino, "x");
        Assert.Equal(S.NaoProtegida, v.Status);
        Assert.True(S.PermiteEnvio(v.Status));
    }

    [Fact]
    public async Task DocumentoNaLdp_EmPastaDeOutraDisciplina_Permitido()
    {
        // Documento de MECÂNICA enviado para a árvore "10 - DOCUMENTOS CLIENTE": disciplina não é validada.
        var v = UploadGovernadoRules.Validar("CHS001-000-ME1-DS-010_R1.dwg", Ldp, await Raiz("RECEBIDOS"), "RECEBIDOS", "…/RECEBIDOS");
        Assert.Equal(S.Pronto, v.Status);
        Assert.True(S.PermiteEnvio(v.Status));
        Assert.Equal("CHS001-000-ME1-DS-010", v.Documento);
        Assert.Equal("R1", v.Revisao);
        Assert.Equal("RECEBIDOS", v.FolderId);
    }

    [Fact]
    public async Task DocumentoForaDaLdp_NaArvoreProtegida_Bloqueado()
    {
        var v = UploadGovernadoRules.Validar("CHS001-000-PC1-RE-999.pdf", Ldp, await Raiz("SUB3"), "SUB3", null);
        Assert.Equal(S.ForaDaLdp, v.Status);
        Assert.False(S.PermiteEnvio(v.Status));
    }

    [Fact]
    public async Task IdentificacaoInalterada_NaArvoreProtegida()
    {
        var raiz = await Raiz("MEC");
        Assert.Equal(S.Ambiguo, UploadGovernadoRules.Validar("CHS001-000-XX1-RE-777-01.pdf", Ldp, raiz, "MEC", null).Status);
        Assert.Equal(S.NomeInvalido, UploadGovernadoRules.Validar("X.pdf", Ldp, raiz, "MEC", null).Status);
        var cliente = UploadGovernadoRules.Validar("CLI-PRC-001-B.pdf", Ldp, raiz, "MEC", null);   // fallback Client Document Number
        Assert.Equal((S.Pronto, "Client Document Number"), (cliente.Status, cliente.CampoIdentificador));
        // Sufixo colado sem fronteira não casa (regra oficial por prefixo de TOKENS).
        Assert.Equal(S.ForaDaLdp, UploadGovernadoRules.Validar("CHS001-000-PC1-RE-0011.pdf", Ldp, raiz, "MEC", null).Status);
    }

    [Fact]
    public async Task Ancestralidade_CicloOuProfundidade_NuncaLacoInfinito()
    {
        var ciclo = new Dictionary<string, string?> { ["A"] = "B", ["B"] = "A" };
        Assert.Null(await UploadGovernadoRules.RaizQueContemAsync("A", Raizes, id => Task.FromResult(ciclo.GetValueOrDefault(id))));
        Assert.Null(await UploadGovernadoRules.RaizQueContemAsync("X", Array.Empty<PastaProtegida>(), _ => Task.FromResult<string?>(null)));
    }

    [Fact]
    public async Task Configuracao_SoRaizes_PorProjeto_SalvaReabre()
    {
        var arquivo = Path.Combine(_dir, "document-folders.json");
        const string chs = "b.22222222-2222-2222-2222-222222222222", bml = "b.33333333-3333-3333-3333-333333333333";
        await new PastasDocumentosStore(arquivo).SaveAsync(chs, Raizes, "planejador@na.com");
        var lida = await new PastasDocumentosStore(arquivo).ReadAsync(chs);
        Assert.Equal(new[] { "09-ENG", "10-CLI" }, lida.Raizes.Select(r => r.FolderId));          // só as raízes, nunca subpastas
        Assert.Empty((await new PastasDocumentosStore(arquivo).ReadAsync(bml)).Raizes);
        await Assert.ThrowsAsync<ArgumentException>(() => new PastasDocumentosStore(arquivo).SaveAsync(chs, new[] { Eng, Eng }, null));
    }

    [Fact]
    public async Task FormatoAnteriorPorDisciplina_ViraConfiguracaoVazia()
    {
        Directory.CreateDirectory(_dir);
        var arquivo = Path.Combine(_dir, "document-folders.json");
        await File.WriteAllTextAsync(arquivo, """{ "b.x": { "regras": [ { "disciplina": "PROCESSO", "folderId": "f", "caminho": "c" } ] } }""");
        Assert.Empty((await new PastasDocumentosStore(arquivo).ReadAsync("b.x")).Raizes);
    }

    [Fact]
    public void LdpAtual_UsaASnapshotOficialComPlanoMitigado()
    {
        Assert.Contains(APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela, LdpDocumentosAtuaisRepository.Query);
        Assert.Contains(APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano, LdpDocumentosAtuaisRepository.Query);
    }
}
