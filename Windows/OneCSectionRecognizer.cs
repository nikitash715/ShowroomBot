using System.Drawing.Imaging;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace ShowroomBot.Windows;

public sealed record SectionRecognition(Rectangle Panel, Rectangle? TextBounds);

public sealed class SectionPanelNotFoundException : InvalidOperationException
{
    public SectionPanelNotFoundException() : base(
        "Не удалось однозначно распознать левую панель разделов 1С. Нужна раскрытая панель с различимыми границами.") { }
}

/// <summary>Image-based detection of the expanded left navigation panel; no section coordinates.</summary>
public sealed class OneCSectionRecognizer
{
    public async Task<SectionRecognition> RecognizeAsync(
        string screenshotPath, string section, CancellationToken cancellationToken)
    {
        using var screenshot = new Bitmap(screenshotPath);
        var panel = DetectPanel(screenshot);
        using var crop = screenshot.Clone(panel, PixelFormat.Format32bppArgb);
        var cropPath = Path.ChangeExtension(screenshotPath, ".panel.png");
        crop.Save(cropPath, ImageFormat.Png);

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(cropPath));
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException("Размер панели превышает максимальный размер Windows OCR.");
        }

        var languages = OcrEngine.AvailableRecognizerLanguages
            .Where(language => language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                               language.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            .GroupBy(language => language.LanguageTag.Split('-')[0])
            .Select(group => group.First()).ToArray();
        if (languages.Length == 0)
        {
            throw new InvalidOperationException("Для распознавания разделов установите английский или русский компонент OCR Windows.");
        }

        var matches = new List<Rectangle>();
        foreach (Language language in languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = OcrEngine.TryCreateFromLanguage(language)
                ?? throw new InvalidOperationException($"Недоступен OCR: {language.LanguageTag}.");
            var result = await engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.ChangeExtension(screenshotPath, $".{language.LanguageTag}.txt"),
                result.Text, cancellationToken);
            matches.AddRange(FindMatches(result, section, panel.Location));
        }

        // Different language engines may identify the same label. Distinct labels are ambiguous.
        var distinct = new List<Rectangle>();
        foreach (var match in matches)
        {
            if (!distinct.Any(existing => Rectangle.Intersect(existing, match).Height > 0))
                distinct.Add(match);
        }
        if (distinct.Count > 1)
            throw new InvalidOperationException($"В панели найдено несколько разделов «{section}»; клик отменён.");
        return new SectionRecognition(panel, distinct.Count == 1 ? distinct[0] : null);
    }

    private static IEnumerable<Rectangle> FindMatches(OcrResult result, string section, Point offset)
    {
        var expected = Normalize(section);
        for (var index = 0; index < result.Lines.Count; index++)
        {
            var words = result.Lines[index].Words.ToList();
            for (var count = 1; count <= 3 && index + count <= result.Lines.Count; count++)
            {
                if (count > 1)
                {
                    var next = result.Lines[index + count - 1];
                    var previous = words.Last().BoundingRect;
                    if (next.Words.Count == 0 || next.Words[0].BoundingRect.Top - previous.Bottom > previous.Height * 1.5)
                        break;
                    words.AddRange(next.Words);
                }
                if (Normalize(string.Join(" ", words.Select(word => word.Text))) != expected)
                    continue;
                var left = words.Min(word => word.BoundingRect.Left);
                var top = words.Min(word => word.BoundingRect.Top);
                var right = words.Max(word => word.BoundingRect.Right);
                var bottom = words.Max(word => word.BoundingRect.Bottom);
                yield return Rectangle.FromLTRB((int)Math.Floor(left) + offset.X, (int)Math.Floor(top) + offset.Y,
                    (int)Math.Ceiling(right) + offset.X, (int)Math.Ceiling(bottom) + offset.Y);
            }
        }
    }

    private static string Normalize(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static Rectangle DetectPanel(Bitmap image)
    {
        // Find a tall, narrow, solid-background region at the left of the current image.
        // Row votes tolerate text/icons; a colour transition determines the right edge.
        var votes = new List<(int Y, int Left, int Right, Color Color)>();
        var stride = Math.Max(2, image.Height / 240);
        var maxWidth = Math.Min(image.Width / 3, 500);
        for (var y = stride; y < image.Height - stride; y += stride)
        {
            for (var seed = 4; seed < Math.Min(image.Width / 8, 80); seed += 12)
            {
                var color = image.GetPixel(seed, y);
                var left = seed;
                while (left > 0 && Similar(image.GetPixel(left - 1, y), color)) left--;
                if (left > image.Width / 12) continue;
                var right = seed;
                for (; right < Math.Min(image.Width - 12, left + maxWidth + 12); right += 4)
                {
                    var same = 0;
                    for (var dx = 0; dx < 12; dx++)
                        if (Similar(image.GetPixel(right + dx, y), color)) same++;
                    if (same < 7) break;
                }
                if (right - left >= 70 && right - left <= maxWidth)
                {
                    votes.Add((y, left, right, color));
                    break;
                }
            }
        }

        var candidates = new List<Rectangle>();
        foreach (var seed in votes)
        {
            var compatible = votes.Where(vote => Math.Abs(vote.Left - seed.Left) <= 12 &&
                Math.Abs(vote.Right - seed.Right) <= 12 && Similar(vote.Color, seed.Color)).ToArray();
            if (compatible.Length < image.Height / stride * .35) continue;
            var groups = new List<List<(int Y, int Left, int Right, Color Color)>>();
            foreach (var vote in compatible)
            {
                if (groups.Count == 0 || vote.Y - groups[^1][^1].Y > Math.Max(stride * 4, image.Height / 25))
                    groups.Add([]);
                groups[^1].Add(vote);
            }
            foreach (var group in groups)
            {
                var top = group[0].Y;
                var bottom = Math.Min(image.Height, group[^1].Y + stride);
                if (bottom - top < image.Height * .4 || group.Count * stride < (bottom - top) * .6) continue;
                var left = group.Select(v => v.Left).Order().ElementAt(group.Count / 2);
                var right = group.Select(v => v.Right).Order().ElementAt(group.Count / 2);
                // Exclude the colour boundary itself from OCR and scrolling comparison.
                var rectangle = Rectangle.FromLTRB(left + 2, top, right - 4, bottom);
                if (!candidates.Any(existing => Math.Abs(existing.Right - rectangle.Right) < 16 &&
                    Math.Abs(existing.Top - rectangle.Top) < stride * 4)) candidates.Add(rectangle);
            }
        }
        if (candidates.Count != 1)
            throw new SectionPanelNotFoundException();
        return candidates[0];
    }

    private static bool Similar(Color a, Color b) =>
        Math.Abs(a.R - b.R) <= 18 && Math.Abs(a.G - b.G) <= 18 && Math.Abs(a.B - b.B) <= 18;
}
