using System.Drawing.Imaging;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace ShowroomBot.Windows;

public sealed record SectionRecognition(Rectangle Panel, Rectangle? TextBounds);
public sealed record RecognizedText(string Text, Rectangle Bounds);

public sealed class SectionPanelNotFoundException : InvalidOperationException
{
    public SectionPanelNotFoundException() : base(
        "Не удалось однозначно распознать левую панель разделов 1С. Нужна раскрытая панель с различимыми границами.") { }
}

/// <summary>Image-based detection of the expanded left navigation panel; no section coordinates.</summary>
public sealed class OneCSectionRecognizer
{
    public async Task<IReadOnlyList<RecognizedText>> ReadConfiguratorLinesAsync(string path, CancellationToken token)
    {
        using var source = new Bitmap(path);
        var labels = new List<RecognizedText>(await ReadLinesAsync(path, token));
        // Classic Configurator uses very small fonts. Tile before enlarging so even
        // a full-HD remote desktop stays within the Windows OCR image limit.
        const int scale = 3;
        var tileSize = checked((int)OcrEngine.MaxImageDimension / scale);
        const int overlap = 32;
        for (var top = 0; top < source.Height; top += tileSize - overlap)
        for (var left = 0; left < source.Width; left += tileSize - overlap)
        {
            token.ThrowIfCancellationRequested();
            var region = new Rectangle(left, top, Math.Min(tileSize, source.Width - left),
                Math.Min(tileSize, source.Height - top));
            using var enlarged = new Bitmap(region.Width * scale, region.Height * scale, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(enlarged))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, enlarged.Width, enlarged.Height), region, GraphicsUnit.Pixel);
            }
            var tilePath = Path.ChangeExtension(path, $".configurator-{left}-{top}.png");
            enlarged.Save(tilePath, ImageFormat.Png);
            var recognized = await ReadLinesAsync(tilePath, token);
            labels.AddRange(recognized.Select(l => new RecognizedText(l.Text, Rectangle.FromLTRB(
                left + l.Bounds.Left / scale, top + l.Bounds.Top / scale,
                left + (l.Bounds.Right + scale - 1) / scale, top + (l.Bounds.Bottom + scale - 1) / scale))));
        }
        var view = OneCConfiguratorRecognizer.Analyze(source, labels);
        if (view.Editor == null && OneCConfiguratorRecognizer.FindModuleCaptionRegion(source, view) is { } caption)
        {
            // Full-screen OCR can drop the entire bold identifier on the grey MDI
            // caption. Recognize that narrow strip separately on a white background.
            var captionScale = Math.Max(1, Math.Min(3, (int)OcrEngine.MaxImageDimension / caption.Width));
            using var strip = new Bitmap(caption.Width, caption.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(strip))
                graphics.DrawImage(source, new Rectangle(Point.Empty, strip.Size), caption, GraphicsUnit.Pixel);
            for (var y = 0; y < strip.Height; y++)
            for (var x = 0; x < strip.Width; x++)
            {
                var color = strip.GetPixel(x, y);
                strip.SetPixel(x, y, y < 4 || y >= strip.Height - 3 || x < 22 ||
                    color.R >= 160 || color.G >= 160 || color.B >= 160 ? Color.White : Color.Black);
            }
            var lastInk = 0;
            for (var x = 22; x < strip.Width; x++)
            {
                var ink = false;
                for (var y = 4; y < strip.Height - 3; y++)
                    if (strip.GetPixel(x, y).R < 120) { ink = true; break; }
                if (ink) lastInk = x;
                if (lastInk > 0 && x - lastInk > 40) break;
            }
            var textWidth = Math.Min(strip.Width, lastInk + 12);
            using var enlarged = new Bitmap(textWidth * captionScale, strip.Height * captionScale + 60, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(enlarged))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(strip, new Rectangle(0, 30, textWidth * captionScale, strip.Height * captionScale),
                    new Rectangle(0, 0, textWidth, strip.Height), GraphicsUnit.Pixel);
            }
            var captionPath = Path.ChangeExtension(path, ".module-caption.png");
            enlarged.Save(captionPath, ImageFormat.Png);
            var recognized = await ReadLinesAsync(captionPath, token);
            labels.AddRange(recognized.Select(l => new RecognizedText(l.Text, Rectangle.FromLTRB(
                caption.Left + l.Bounds.Left / captionScale, caption.Top + (l.Bounds.Top - 30) / captionScale,
                caption.Left + (l.Bounds.Right + captionScale - 1) / captionScale,
                caption.Top + (l.Bounds.Bottom - 30 + captionScale - 1) / captionScale))));
        }
        return labels.Distinct().ToArray();
    }

    // Shared Windows OCR entry point for recognizers that need relative layout.
    public async Task<IReadOnlyList<RecognizedText>> ReadTreeLinesAsync(string path, Rectangle tree, CancellationToken token)
    {
        using var source = new Bitmap(path);
        var scale = Math.Max(1, Math.Min(3, (int)OcrEngine.MaxImageDimension / Math.Max(tree.Width, tree.Height)));
        using var cropped = new Bitmap(tree.Width * scale, tree.Height * scale, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, cropped.Width, cropped.Height), tree, GraphicsUnit.Pixel);
        }
        var treePath = Path.ChangeExtension(path, ".tree.png");
        cropped.Save(treePath, ImageFormat.Png);
        var labels = await ReadLinesAsync(treePath, token);
        return labels.Select(l => new RecognizedText(l.Text, Rectangle.FromLTRB(
            tree.Left + l.Bounds.Left / scale, tree.Top + l.Bounds.Top / scale,
            tree.Left + (l.Bounds.Right + scale - 1) / scale, tree.Top + (l.Bounds.Bottom + scale - 1) / scale))).ToArray();
    }

    public async Task<IReadOnlyList<RecognizedText>> ReadLinesAsync(string path, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(token);
        using var stream = await file.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token);
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException("Размер снимка превышает предел Windows OCR.");
        var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith("ru"))
            ?? throw new InvalidOperationException("Для консоли Toolkit установите русский компонент Windows OCR.");
        var result = await OcrEngine.TryCreateFromLanguage(language)!.RecognizeAsync(bitmap).AsTask(token);
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".toolkit-ocr.txt"), result.Text, token);
        var lines = result.Lines.Where(l => l.Words.Count > 0).Select(l => new RecognizedText(l.Text,
            Rectangle.FromLTRB((int)l.Words.Min(w => w.BoundingRect.Left), (int)l.Words.Min(w => w.BoundingRect.Top),
                (int)Math.Ceiling(l.Words.Max(w => w.BoundingRect.Right)), (int)Math.Ceiling(l.Words.Max(w => w.BoundingRect.Bottom)))));
        return lines.Concat(result.Lines.SelectMany(l => l.Words).Select(w => new RecognizedText(w.Text,
            Rectangle.FromLTRB((int)w.BoundingRect.Left, (int)w.BoundingRect.Top,
                (int)Math.Ceiling(w.BoundingRect.Right), (int)Math.Ceiling(w.BoundingRect.Bottom))))).ToArray();
    }
    public async Task<SectionRecognition> RecognizeAsync(
        string screenshotPath, string section, CancellationToken cancellationToken)
    {
        using var screenshot = new Bitmap(screenshotPath);
        var panel = DetectPanel(screenshot, cancellationToken);
        return await RecognizeRegionAsync(screenshotPath, screenshot, panel, section, "panel", cancellationToken);
    }

    public async Task<(Rectangle Workspace, Rectangle? Command)> RecognizeCommandAsync(
        string screenshotPath, string command, CancellationToken cancellationToken)
    {
        using var screenshot = new Bitmap(screenshotPath);
        var panel = DetectPanel(screenshot, cancellationToken);
        // Derive the working area from the detected navigation boundary, never from fixed coordinates.
        var workspace = Rectangle.FromLTRB(panel.Right + 4, 0, screenshot.Width, screenshot.Height);
        if (workspace.Width <= 0 || workspace.Height <= 0)
            throw new InvalidOperationException("Не удалось определить рабочую область 1С.");
        var action = await RecognizeRegionAsync(screenshotPath, screenshot, workspace, command, "workspace-command", cancellationToken);
        return (workspace, action.TextBounds);
    }

    private static async Task<SectionRecognition> RecognizeRegionAsync(
        string screenshotPath, Bitmap screenshot, Rectangle panel, string section, string suffix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var crop = screenshot.Clone(panel, PixelFormat.Format32bppArgb);
        // Small 1C link fonts lose letters at native RDP resolution. Enlarge before
        // OCR, then map the recognized bounds back to the original screenshot.
        var scale = suffix == "panel" ? 1d : Math.Min(2d,
            (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
        using var ocrImage = new Bitmap((int)Math.Floor(crop.Width * scale),
            (int)Math.Floor(crop.Height * scale), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(ocrImage))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(crop, new Rectangle(0, 0, ocrImage.Width, ocrImage.Height));
        }
        var scaleX = (double)ocrImage.Width / crop.Width;
        var scaleY = (double)ocrImage.Height / crop.Height;
        var cropPath = Path.ChangeExtension(screenshotPath, $".{suffix}.png");
        ocrImage.Save(cropPath, ImageFormat.Png);

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(cropPath)).AsTask(cancellationToken);
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken);
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

        var matches = new List<(Rectangle Bounds, int ExtraCharacters)>();
        foreach (Language language in languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = OcrEngine.TryCreateFromLanguage(language)
                ?? throw new InvalidOperationException($"Недоступен OCR: {language.LanguageTag}.");
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.ChangeExtension(screenshotPath, $".{suffix}.{language.LanguageTag}.txt"),
                result.Text, cancellationToken);
            matches.AddRange(FindMatches(result, section, panel.Location, suffix == "panel", scaleX, scaleY));
        }

        // Different language engines may identify the same label. Distinct labels are ambiguous.
        var distinct = new List<Rectangle>();
        foreach (var candidate in matches.Where(match => match.ExtraCharacters == matches.Min(item => item.ExtraCharacters)))
        {
            var match = candidate.Bounds;
            if (!distinct.Any(existing => Rectangle.Intersect(existing, match) is var overlap && overlap.Height > 0 && overlap.Width > 0))
                distinct.Add(match);
        }
        if (distinct.Count > 1)
            throw new InvalidOperationException($"В области {suffix} найдено несколько элементов «{section}»; клик отменён.");
        return new SectionRecognition(panel, distinct.Count == 1 ? distinct[0] : null);
    }

    private static IEnumerable<(Rectangle Bounds, int ExtraCharacters)> FindMatches(OcrResult result, string section, Point offset, bool wholeLabel,
        double scaleX, double scaleY)
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
                // OCR may place several independent links on one line. Match the label's
                // own consecutive words and return only their bounds.
                for (var start = 0; start < words.Count; start++)
                {
                    var label = string.Empty;
                    for (var end = start; end < words.Count; end++)
                    {
                        if (end > start)
                        {
                            var previous = words[end - 1].BoundingRect;
                            var current = words[end].BoundingRect;
                            var sameRow = current.Top < previous.Bottom && current.Bottom > previous.Top;
                            if (sameRow && current.Left - previous.Right > Math.Max(previous.Height, current.Height) * 2)
                                break;
                        }
                        label += Normalize(words[end].Text);
                        if (!expected.StartsWith(label, StringComparison.Ordinal)) break;
                        if (label != expected) continue;
                        // A section is a complete navigation label, not a word inside another section.
                        if (wholeLabel && (start != 0 || end != words.Count - 1)) continue;
                        var matched = words.GetRange(start, end - start + 1);
                        var left = matched.Min(word => word.BoundingRect.Left);
                        var top = matched.Min(word => word.BoundingRect.Top);
                        var right = matched.Max(word => word.BoundingRect.Right);
                        var bottom = matched.Max(word => word.BoundingRect.Bottom);
                        yield return (Rectangle.FromLTRB((int)Math.Floor(left / scaleX) + offset.X, (int)Math.Floor(top / scaleY) + offset.Y,
                            (int)Math.Ceiling(right / scaleX) + offset.X, (int)Math.Ceiling(bottom / scaleY) + offset.Y), words.Sum(word => Normalize(word.Text).Length) - expected.Length);
                        break;
                    }
                }
            }
        }
    }

    private static string Normalize(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static Rectangle DetectPanel(Bitmap image, CancellationToken cancellationToken = default)
    {
        // Find a tall, narrow, solid-background region at the left of the current image.
        // Row votes tolerate text/icons; a colour transition determines the right edge.
        var votes = new List<(int Y, int Left, int Right, Color Color)>();
        var stride = Math.Max(2, image.Height / 240);
        var maxWidth = Math.Min(image.Width / 3, 500);
        for (var y = stride; y < image.Height - stride; y += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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
