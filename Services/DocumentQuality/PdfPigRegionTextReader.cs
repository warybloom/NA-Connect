using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace APS.WebApp.Services.DocumentQuality;

public interface IPdfRegionTextReader
{
    /// <summary>Lê os campos ATIVOS do template no perfil (formato) resolvido para este PDF.</summary>
    Task<PdfRegionReadResult> ReadAsync(Stream pdfStream, DocumentQualityReadPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>Lê apenas a camada de texto existente no PDF. Não rasteriza e não inventa conteúdo para PDF escaneado.</summary>
public sealed class PdfPigRegionTextReader : IPdfRegionTextReader
{
    /// <summary>Atalho: campos = os que têm região, sem mapeamento de formatos (perfil pela dimensão da página).</summary>
    public Task<PdfRegionReadResult> ReadAsync(Stream pdfStream, IReadOnlyList<OcrTemplateRegion> regions, CancellationToken cancellationToken = default) =>
        ReadAsync(pdfStream, new DocumentQualityReadPlan(regions, regions.Select(r => r.TargetField).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            null, Array.Empty<FormatMapping>()), cancellationToken);

    public async Task<PdfRegionReadResult> ReadAsync(Stream pdfStream, DocumentQualityReadPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.ActiveFields.Count == 0) throw new ArgumentException("Template sem campos auditados.", nameof(plan));
        await using var copy = new MemoryStream();
        await pdfStream.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;

        using var document = PdfDocument.Open(copy);
        var first = document.GetPage(1);
        var profile = DocumentQualityProfiles.Resolve(first.Width, first.Height, plan.Regions, plan.LdpFormat, plan.Mappings);
        var regions = profile.SheetType == null ? Array.Empty<OcrTemplateRegion>()
            : plan.Regions.Where(r => string.Equals(r.SheetType, profile.SheetType, StringComparison.OrdinalIgnoreCase)).ToArray();
        var output = new List<PdfRegionRead>();
        foreach (var field in plan.ActiveFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions.FirstOrDefault(r => string.Equals(r.TargetField, field, StringComparison.OrdinalIgnoreCase));
            if (region == null)
            {
                output.Add(new(field, null, false, DocumentFieldStatus.MotivoNaoCalibrado, 0, profile.SheetType ?? "—") { NotCalibrated = true });
                continue;
            }
            if (region.Page > document.NumberOfPages)
            {
                output.Add(new(field, null, false, $"A página {region.Page} não existe no PDF.", region.Page, region.SheetType));
                continue;
            }
            var value = Extract(document.GetPage(region.Page), region);
            output.Add(string.IsNullOrWhiteSpace(value)
                ? new(field, null, false, "LEITURA NÃO DISPONÍVEL / NECESSITA OCR DE IMAGEM", region.Page, region.SheetType)
                : new(field, value, true, null, region.Page, region.SheetType));
        }
        return new(profile.SheetType ?? "—", output) { SheetSource = profile.Origin, LdpFormat = profile.LdpFormat };
    }

    /// <summary>
    /// Texto da região calibrada, por PALAVRAS (01/10/2026). Antes as palavras eram remontadas letra a letra pela distância entre as
    /// caixas dos glifos (GlyphRectangle): glifo estreito ("I") abria espaço falso ("RECTI FI ER") e o caractere de espaço — caixa
    /// degenerada sobre a linha de base — caía numa "linha" própria e era descartado, colando palavras ("RECTIFIERCOLUMN").
    /// Agora: só os glifos com centro DENTRO da região (nada de fora é inventado) → extrator de palavras do PdfPig
    /// (NearestNeighbourWordExtractor, distância entre linhas de base/avanço do glifo, não a caixa do desenho) → palavras agrupadas
    /// por linha (linha de base), ordenadas por X e unidas por espaço; linhas preservadas com quebra de linha.
    /// </summary>
    internal static string Extract(Page page, OcrTemplateRegion region)
    {
        var scaleX = page.Width / Math.Max(1d, (double)region.ReferenceWidth);
        var scaleY = page.Height / Math.Max(1d, (double)region.ReferenceHeight);
        var left = (double)region.X * scaleX;
        var right = ((double)region.X + (double)region.Width) * scaleX;
        var top = page.Height - (double)region.Y * scaleY;
        var bottom = page.Height - ((double)region.Y + (double)region.Height) * scaleY;

        var letters = page.Letters
            .Where(letter => !string.IsNullOrWhiteSpace(letter.Value))
            .Where(letter =>
            {
                var centerX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2d;
                var centerY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2d;
                return centerX >= left && centerX <= right && centerY >= bottom && centerY <= top;
            })
            .ToArray();
        return TextFromLetters(letters);
    }

    /// <summary>Glifos (já recortados pela região) → texto por palavras/linhas. Público para teste com glifos reais/sintéticos.</summary>
    public static string TextFromLetters(IReadOnlyList<Letter> letters)
    {
        letters = letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).ToArray();
        if (letters.Count == 0) return string.Empty;

        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToArray();
        return JoinWords(words.Select(w => new RegionWord(w.Text, w.BoundingBox.Left, Baseline(w), Size(w))).ToArray());
    }

    internal sealed record RegionWord(string Text, double Left, double Baseline, double Size);

    /// <summary>Agrupa palavras por linha (linha de base, tolerância proporcional ao corpo do texto), ordena por X e junta com espaço.</summary>
    internal static string JoinWords(IReadOnlyList<RegionWord> words)
    {
        if (words.Count == 0) return string.Empty;
        var tolerance = Math.Max(1d, words.Select(w => w.Size).Median() * .5d);
        var lines = new List<List<RegionWord>>();
        foreach (var word in words.OrderByDescending(w => w.Baseline))
        {
            var line = lines.FirstOrDefault(existing => Math.Abs(existing.Average(x => x.Baseline) - word.Baseline) <= tolerance);
            if (line == null) { line = new(); lines.Add(line); }
            line.Add(word);
        }
        return string.Join("\n", lines
            .Select(line => string.Join(" ", line.OrderBy(w => w.Left).Select(w => w.Text.Trim())))
            .Where(line => line.Length > 0));
    }

    private static double Baseline(Word word) => word.Letters.Count == 0 ? word.BoundingBox.Bottom : word.Letters.Average(l => l.StartBaseLine.Y);

    private static double Size(Word word)
    {
        var size = word.Letters.Count == 0 ? 0 : word.Letters.Average(l => l.PointSize);
        return size > 0 ? size : Math.Max(1d, word.BoundingBox.Height);
    }
}

internal static class MedianExtensions
{
    public static double Median(this IEnumerable<double> source)
    {
        var values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0) return 0;
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }
}
