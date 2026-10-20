using System.Globalization;
using System.Text;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.UploadGovernado;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.PdfFonts;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Fase 1 — confiabilidade: identificação pelo NOME DO ARQUIVO (mesma regra do envio governado) e leitura do texto das regiões por
/// PALAVRAS (PdfPig), sem espaços falsos dentro de palavras ("RECTI FI ERCOLUMN").
/// </summary>
public sealed class DocumentQualityReadingTests
{
    private const string Projeto = "b.11111111-1111-1111-1111-111111111111";

    // LDP com os documentos reais citados (e vizinhos com prefixo parecido, para provar que o casamento é exato por tokens).
    private static readonly LdpDocumentoAtual[] Ldp =
    {
        new(10, "CHS001-000-PC3-FD-001", "CL-000-001", "PROCESSO", "Process Flow Diagram - Fermentation", "0"),
        new(11, "CHS001-000-PC3-FD-002", "CL-000-002", "PROCESSO", "Process Flow Diagram - Distillation", "0"),
        new(12, "CHS001-000-PC4-LI-002", "CL-000-003", "PROCESSO", "Line List", "0"),
        new(13, "CHS001-048-PC3-FD-001", "CL-048-001", "PROCESSO", "Process Flow Diagram - Utilities", "0"),
        new(14, "CHS001-045-PC3-FD-001", "CL-045-001", "PROCESSO", "Process Instrumentation Diagram - Rectifier Column", "0"),
    };

    // ---------------------------------------------------------------- IDENTIFICAÇÃO (F, G, H, I)

    [Theory]
    [InlineData("CHS001-000-PC3-FD-001.pdf", 10L)]
    [InlineData("CHS001-000-PC3-FD-002.pdf", 11L)]
    [InlineData("CHS001-000-PC4-LI-002.pdf", 12L)]
    [InlineData("CHS001-048-PC3-FD-001.pdf", 13L)]
    [InlineData("CHS001-048-PC3-FD-001-R02.pdf", 13L)] // sufixo de revisão no nome continua identificando
    public void F_a_I_IdentificaPeloNomeDoArquivo_MesmoComCarimboIlegivel(string arquivo, long idEsperado)
    {
        // Carimbo sem texto incorporado (falha de leitura) e número lido errado: antes viravam NÃO IDENTIFICADO / FORA DA LDP.
        foreach (var leitura in new[]
        {
            Leitura(Nao(DocumentQualityFields.NaDocumentNumber)),
            Leitura(Lido(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC3-FO-00I")),
        })
        {
            var r = DocumentQualityComparisonEngine.Compare(arquivo, "Project Files/PDF", Projeto, leitura, Ldp);
            Assert.Equal(idEsperado, r.LdpRecordId);
            Assert.NotEqual(DocumentQualityStatus.ForaDaLdp, r.Status);
            Assert.NotEqual(DocumentQualityStatus.NaoIdentificado, r.Status);
            Assert.StartsWith("NOME DO ARQUIVO", r.IdentifiedBy);
        }
    }

    [Fact]
    public void CarimboComNumeroDiferenteDoNome_ViraDivergenciaDoCampo_NaoForaDaLdp()
    {
        var r = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto,
            Leitura(Lido(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC3-FD-002")), Ldp);
        Assert.Equal(10, r.LdpRecordId);
        Assert.Equal(DocumentQualityStatus.Divergencia, r.Status);
        Assert.Equal(DocumentFieldStatus.Divergente, r.Fields.Single().Status);
    }

    [Fact]
    public void IdentificacaoUsaAMesmaRegraDoEnvioGovernado()
    {
        foreach (var arquivo in new[] { "CHS001-000-PC3-FD-001.pdf", "CHS001-999-XX-001.pdf", "LOGO.pdf" })
        {
            var envio = UploadGovernadoRules.Validar(arquivo, Ldp, new PastaProtegida("raiz", "Project Files"), "raiz", "Project Files");
            var porNome = UploadGovernadoRules.IdentificarPorNomeArquivo(arquivo, Ldp);
            Assert.Equal(envio.IdRegistroLdp, porNome.Documento?.IdRegistro);
        }
    }

    [Fact]
    public void NomeNaoCorresponde_MasCarimboIdentifica_UsaOCarimboComoFallback()
    {
        var r = DocumentQualityComparisonEngine.Compare("desenho final.pdf", "Pasta", Projeto,
            Leitura(Lido(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC4-LI-002")), Ldp);
        Assert.Equal(12, r.LdpRecordId);
        Assert.StartsWith("CARIMBO", r.IdentifiedBy);
    }

    // ---------------------------------------------------------------- 5.3 — situações distintas

    [Fact]
    public void ArquivoSemCorrespondenciaNaLdp_ForaDaLdp_ComMotivoDoNome()
    {
        var r = DocumentQualityComparisonEngine.Compare("CHS001-999-PC3-FD-001.pdf", "Pasta", Projeto, Leitura(Nao(DocumentQualityFields.NaDocumentNumber)), Ldp);
        Assert.Equal(DocumentQualityStatus.ForaDaLdp, r.Status);
        Assert.Contains("nome do arquivo", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NomeIlegivelECarimboSemTexto_ErroDeLeitura_NuncaForaDaLdp()
    {
        var r = DocumentQualityComparisonEngine.Compare("scan.pdf", "Pasta", Projeto, Leitura(Nao(DocumentQualityFields.NaDocumentNumber)), Ldp);
        Assert.Equal(DocumentQualityStatus.ErroLeitura, r.Status);
    }

    [Fact]
    public void CodigoAmbiguo_StatusAmbiguo_NaoForaDaLdp()
    {
        var ldp = Ldp.Append(new LdpDocumentoAtual(99, "CHS001-000-PC3-FD-001", null, "PROCESSO", "Cópia", "0")).ToArray();
        var r = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto, Leitura(Nao(DocumentQualityFields.NaDocumentNumber)), ldp);
        Assert.Equal(DocumentQualityStatus.Ambiguo, r.Status);
    }

    [Fact]
    public void IdentificadoComCampoNaoLido_ErroDeLeitura_ComDocumentoIdentificado()
    {
        var r = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto, Leitura(Nao(DocumentQualityFields.DocumentTitle)), Ldp);
        Assert.Equal(DocumentQualityStatus.ErroLeitura, r.Status);
        Assert.Equal(10, r.LdpRecordId);
    }

    // ---------------------------------------------------------------- LEITURA (J, K, L, M)

    [Fact]
    public async Task J_K_TextoPosicionadoGlifoAGlifo_LeRectifierColumnSemEspacosFalsos()
    {
        // Estilo CAD: cada glifo posicionado individualmente e SEM caractere de espaço entre as palavras.
        await using var pdf = new MemoryStream(Pdf(Glifos(60, 700, 10, "RECTIFIER COLUMN")));
        var valor = await Ler(pdf, Regiao(DocumentQualityFields.DocumentTitle, 50, 80, 300, 25));
        Assert.Equal("RECTIFIER COLUMN", valor);
        Assert.DoesNotContain("RECTI FI ER", valor);
    }

    [Fact]
    public void K_GlifoEstreito_NaoAbreEspacoFalsoDentroDaPalavra()
    {
        // Fonte embutida: a caixa do desenho do "I" é estreita, mas o AVANÇO (linha de base) é contínuo. A heurística antiga
        // (vão entre caixas > 75% da largura da caixa anterior) abria espaço depois de cada "I": "RECTI FI ER".
        var letras = new List<Letter>();
        double x = 100;
        foreach (var palavra in new[] { "RECTIFIER", "COLUMN" })
        {
            foreach (var c in palavra)
            {
                var avanco = c == 'I' ? 2.8 : 7.2;
                var tinta = c == 'I' ? (Esq: x + 0.9, Dir: x + 1.9) : (Esq: x + 0.5, Dir: x + avanco - 0.5);
                letras.Add(Letra(c.ToString(), tinta.Esq, tinta.Dir, x, x + avanco));
                x += avanco;
            }
            x += 2.8; // espaço entre as palavras sem caractere de espaço (CAD)
        }
        Assert.Equal("RECTIFIER COLUMN", PdfPigRegionTextReader.TextFromLetters(letras));
    }

    private static Letter Letra(string valor, double esquerda, double direita, double inicio, double fim) =>
        new(valor, new PdfRectangle(esquerda, 700, direita, 707), new PdfPoint(inicio, 700), new PdfPoint(fim, 700), fim - inicio, 10,
            new FontDetails("Arial", false, 400, false), TextRenderingMode.Fill, GrayColor.Black, GrayColor.Black, 10, 0);

    [Fact]
    public async Task J_TextoEmTjUnico_ContinuaCorreto()
    {
        await using var pdf = new MemoryStream(Pdf(Texto(60, 700, 10, "RECTIFIER COLUMN")));
        Assert.Equal("RECTIFIER COLUMN", await Ler(pdf, Regiao(DocumentQualityFields.DocumentTitle, 50, 80, 300, 25)));
    }

    [Fact]
    public async Task L_TituloEmDuasLinhas_PreservaAsLinhas_EComparaComALdp()
    {
        await using var pdf = new MemoryStream(Pdf(Glifos(60, 700, 10, "PROCESS INSTRUMENTATION DIAGRAM -") + "\n" + Glifos(60, 686, 10, "RECTIFIER COLUMN")));
        var valor = await Ler(pdf, Regiao(DocumentQualityFields.DocumentTitle, 50, 80, 400, 40));
        Assert.Equal("PROCESS INSTRUMENTATION DIAGRAM -\nRECTIFIER COLUMN", valor);

        var r = DocumentQualityComparisonEngine.Compare("CHS001-045-PC3-FD-001.pdf", "Pasta", Projeto, Leitura(Lido(DocumentQualityFields.DocumentTitle, valor)), Ldp);
        Assert.Equal(DocumentQualityStatus.Conforme, r.Status); // quebra de linha/espaços: normalização segura
    }

    [Fact]
    public async Task M_RegiaoIncompleta_NaoInventaAParteAusente()
    {
        // Região cobre só a 2ª linha do título: lê só o que está dentro e a comparação acusa a divergência real.
        await using var pdf = new MemoryStream(Pdf(Glifos(60, 700, 10, "PROCESS INSTRUMENTATION DIAGRAM -") + "\n" + Glifos(60, 686, 10, "RECTIFIER COLUMN")));
        var valor = await Ler(pdf, Regiao(DocumentQualityFields.DocumentTitle, 50, 101, 400, 14));
        Assert.Equal("RECTIFIER COLUMN", valor);

        var r = DocumentQualityComparisonEngine.Compare("CHS001-045-PC3-FD-001.pdf", "Pasta", Projeto, Leitura(Lido(DocumentQualityFields.DocumentTitle, valor)), Ldp);
        Assert.Equal(DocumentQualityStatus.Divergencia, r.Status);
    }

    [Fact]
    public async Task PalavraCortadaPelaBordaDaRegiao_SoEntramOsGlifosDeDentro()
    {
        await using var pdf = new MemoryStream(Pdf(Glifos(60, 700, 10, "RECTIFIER")));
        // Região termina no meio da palavra (x ≈ 60 + "RECTI"): nada além do que está dentro é lido.
        var valor = await Ler(pdf, Regiao(DocumentQualityFields.DocumentTitle, 50, 80, 38, 25));
        Assert.True(valor.Length < "RECTIFIER".Length && "RECTIFIER".StartsWith(valor), valor);
    }

    [Fact]
    public void DivergenciaRealContinuaDivergencia()
    {
        var r = DocumentQualityComparisonEngine.Compare("CHS001-045-PC3-FD-001.pdf", "Pasta", Projeto,
            Leitura(Lido(DocumentQualityFields.DocumentTitle, "Process Instrumentation Diagram - Stripper Column")), Ldp);
        Assert.Equal(DocumentQualityStatus.Divergencia, r.Status);
    }

    // ---------------------------------------------------------------- helpers

    private static PdfRegionReadResult Leitura(params PdfRegionRead[] campos) => new("A4", campos);
    private static PdfRegionRead Lido(string campo, string valor) => new(campo, valor, true, null, 1, "A4");
    private static PdfRegionRead Nao(string campo) => new(campo, null, false, "LEITURA NÃO DISPONÍVEL / NECESSITA OCR DE IMAGEM", 1, "A4");
    private static OcrTemplateRegion Regiao(string campo, decimal x, decimal y, decimal w, decimal h) => new(1, 1, "A4", campo, x, y, w, h, 1, 612, 792);

    private static async Task<string> Ler(Stream pdf, OcrTemplateRegion regiao)
    {
        var r = await new PdfPigRegionTextReader().ReadAsync(pdf, new[] { regiao });
        return Assert.Single(r.Fields).Value ?? "";
    }

    // Larguras AFM da Helvetica (1/1000 em) — para posicionar cada glifo como um CAD faz.
    private static readonly Dictionary<char, int> Larguras = new()
    {
        ['A'] = 667, ['C'] = 722, ['D'] = 722, ['E'] = 667, ['F'] = 611, ['G'] = 778, ['I'] = 278, ['L'] = 556, ['M'] = 833, ['N'] = 722,
        ['O'] = 778, ['P'] = 667, ['R'] = 722, ['S'] = 667, ['T'] = 611, ['U'] = 722, ['-'] = 333, [' '] = 278,
    };

    private static string Glifos(double x, double y, double tamanho, string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto)
        {
            if (c != ' ') sb.Append(string.Create(CultureInfo.InvariantCulture, $"BT /F1 {tamanho} Tf {x:0.###} {y:0.###} Td ({c}) Tj ET\n"));
            x += Larguras[c] * tamanho / 1000d;
        }
        return sb.ToString();
    }

    private static string Texto(double x, double y, double tamanho, string texto) =>
        string.Create(CultureInfo.InvariantCulture, $"BT /F1 {tamanho} Tf {x:0.###} {y:0.###} Td ({texto}) Tj ET\n");

    private static byte[] Pdf(string content)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
