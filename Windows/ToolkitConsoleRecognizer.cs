using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Drawing.Drawing2D;
using System.Text.Json;
using Windows.Media.Ocr;

namespace ShowroomBot.Windows;

public sealed record ToolkitConsoleLayout(Rectangle? ConsoleTab, Rectangle? TextTab,
    Rectangle? Execute, Rectangle? Editor, Rectangle? Result, int? RowCount, bool Busy, string? Error, string ExecutionStamp);

/// <summary>Toolkit anchors and bordered regions, independent of scenario automation.</summary>
public sealed class ToolkitConsoleRecognizer(OneCSectionRecognizer ocr)
{
    public async Task<ToolkitConsoleLayout> RecognizeAsync(string path, CancellationToken token)
    {
        var labels = await ocr.ReadLinesAsync(path, token);
        using var image = new Bitmap(path);
        var layout = Analyze(image, labels, token);
        if (layout.Execute == null && layout.TextTab is Rectangle text)
        {
            // Isolate the command row: surrounding headings and query text caused Windows
            // OCR to omit even a clearly visible Execute caption on the incident screenshot.
            var title = labels.Where(l => l.Text.Contains("Консоль разработчика", StringComparison.OrdinalIgnoreCase) &&
                    l.Bounds.Bottom < text.Top).OrderByDescending(l => l.Bounds.Bottom).FirstOrDefault();
            var region = Rectangle.Intersect(Rectangle.FromLTRB(
                Math.Max(0, (title?.Bounds.Left ?? 0) - text.Height * 8),
                Math.Max(0, title?.Bounds.Bottom + 1 ?? text.Top - text.Height * 6),
                Math.Min(image.Width, text.Right + text.Height * 12), text.Top - 2),
                new Rectangle(Point.Empty, image.Size));
            if (region.Width > 0 && region.Height > 0)
            {
                using var original = image.Clone(region, PixelFormat.Format32bppArgb);
                original.Save(Path.ChangeExtension(path, ".toolkit-search.png"), ImageFormat.Png);
                using (var overlay = new Bitmap(image))
                {
                    using var graphics = Graphics.FromImage(overlay);
                    using var pen = new Pen(Color.Red, 2);
                    graphics.DrawRectangle(pen, region);
                    overlay.Save(Path.ChangeExtension(path, ".toolkit-search-overlay.png"), ImageFormat.Png);
                }
                var attempts = new List<object>();
                var scale = Math.Max(1, Math.Min(3, ((int)OcrEngine.MaxImageDimension - 48) /
                    Math.Max(region.Width, region.Height)));
                foreach (var threshold in new[] { 0, 145, 180 })
                {
                    token.ThrowIfCancellationRequested();
                    var cropPath = Path.ChangeExtension(path, $".toolkit-toolbar-{threshold}.png");
                    using var processed = PrepareToolbar(original, scale, threshold, token);
                    processed.Save(cropPath, ImageFormat.Png);
                    var toolbar = await ocr.ReadLinesAsync(cropPath, token);
                    var mapped = toolbar.Select(l => l with { Bounds = Rectangle.FromLTRB(
                        region.Left + (int)Math.Floor((l.Bounds.Left - 24d) / scale),
                        region.Top + (int)Math.Floor((l.Bounds.Top - 24d) / scale),
                        region.Left + (int)Math.Ceiling((l.Bounds.Right - 24d) / scale),
                        region.Top + (int)Math.Ceiling((l.Bounds.Bottom - 24d) / scale)) })
                        .Where(l => region.Contains(l.Bounds)).ToArray();
                    labels = labels.Concat(mapped).ToArray();
                    layout = Analyze(image, labels, token);
                    attempts.Add(new { Image = cropPath, Scale = scale, Padding = 24, Threshold = threshold,
                        LabelsInScreenshot = mapped });
                    await File.WriteAllTextAsync(Path.ChangeExtension(path, ".toolkit-search.json"),
                        JsonSerializer.Serialize(new { Source = path, ImageSize = image.Size, SearchRegion = region,
                            Coordinates = "original client screenshot pixels", Attempts = attempts,
                            Execute = layout.Execute, Found = layout.Execute != null },
                            new JsonSerializerOptions { WriteIndented = true }), token);
                    if (layout.Execute != null) break;
                }
            }
        }
        if (layout.Execute == null)
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".toolkit-not-found.json"),
                JsonSerializer.Serialize(new { Source = path, ImageSize = image.Size, Layout = layout,
                    Reason = layout.TextTab == null ? "Text tab not confirmed; toolbar search is unsafe" :
                        "Execute missing or ambiguous in confirmed toolbar", LabelsInScreenshot = labels },
                    new JsonSerializerOptions { WriteIndented = true }), token);
        if ((!layout.RowCount.HasValue || layout.RowCount == 0) && layout.Result != null)
        {
            var heading = labels.First(l => Regex.IsMatch(l.Text, @"Результат\s*\(", RegexOptions.IgnoreCase));
            // Full-frame OCR may return only «Результат (Таблица,» as a
            // separate fragment. Include the remainder of that same row;
            // enlarging the fragment alone can never recover the row count.
            var region = Rectangle.Intersect(new Rectangle(heading.Bounds.Left - 4, heading.Bounds.Top - 4,
                Math.Max(heading.Bounds.Width + 8, heading.Bounds.Height * 40), heading.Bounds.Height + 8),
                new Rectangle(Point.Empty, image.Size));
            using var crop = new Bitmap(region.Width * 2, region.Height * 2);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, new Rectangle(0, 0, crop.Width, crop.Height), region, GraphicsUnit.Pixel);
            }
            var cropPath = Path.ChangeExtension(path, ".toolkit-result-heading.png");
            crop.Save(cropPath, ImageFormat.Png);
            var headings = await ocr.ReadLinesAsync(cropPath, token);
            var corrected = headings.Where(l => Regex.IsMatch(l.Text, @"Результат\s*\(", RegexOptions.IgnoreCase))
                .Select(l => new RecognizedText(l.Text, new Rectangle(
                    region.Left + l.Bounds.Left / 2, region.Top + l.Bounds.Top / 2,
                    (l.Bounds.Width + 1) / 2, (l.Bounds.Height + 1) / 2))).ToArray();
            layout = Analyze(image, corrected.Concat(labels).ToArray(), token);
        }
        return layout;
    }

    public static ToolkitConsoleLayout Analyze(Bitmap image, IReadOnlyList<RecognizedText> labels, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var titles = labels.Where(l => l.Text.Contains("Консоль разработчика", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.Bounds.Top).ToArray();
        // OCR can omit Toolkit from either the tab or the active form heading.
        // Require two separate headings and confirm Toolkit in at least one.
        var tabTitle = titles.FirstOrDefault();
        var formTitle = titles.FirstOrDefault(l => tabTitle != null && l.Bounds.Top > tabTitle.Bounds.Bottom);
        var toolkitConfirmed = new[] { tabTitle, formTitle }.Any(l => l != null &&
            Regex.IsMatch(l.Text, @"T[oо0]{1,2}[lI1]k[iI1]t", RegexOptions.IgnoreCase));
        var tab = toolkitConfirmed ? tabTitle?.Bounds : null;
        if (!toolkitConfirmed) formTitle = null;
        // A crowded tab strip truncates the console name. In that case OCR
        // sees only the full form heading; confirm it using the toolbar below.
        if (toolkitConfirmed && formTitle == null && tabTitle != null &&
            tabTitle.Bounds.Top > tabTitle.Bounds.Height * 3 &&
            labels.Any(l => l.Text.Trim() == "Текст" && l.Bounds.Top > tabTitle.Bounds.Bottom) &&
            labels.Any(l => l.Text.Contains("Параметры", StringComparison.OrdinalIgnoreCase) &&
                l.Bounds.Top > tabTitle.Bounds.Bottom))
            formTitle = tabTitle;
        if (tab is Rectangle tabBounds)
        {
            // OCR may merge the entire row of tabs into one line. Its center
            // can belong to a different tab; use the console word's own bounds.
            var consoleWord = labels.FirstOrDefault(l =>
                l.Text.Trim().Equals("Консоль", StringComparison.OrdinalIgnoreCase) &&
                tabBounds.Contains(l.Bounds));
            if (consoleWord != null) tab = consoleWord.Bounds;
        }
        var text = labels.Where(l => formTitle != null && l.Bounds.Top > formTitle.Bounds.Bottom && l.Text.Trim() == "Текст")
            .OrderBy(l => l.Bounds.Top).FirstOrDefault();
        var executeCandidates = labels.Where(l => IsExecuteCaption(l.Text) && text != null && formTitle != null &&
            l.Bounds.Top >= formTitle.Bounds.Bottom && l.Bounds.Bottom < text.Bounds.Top &&
            l.Bounds.Left >= Math.Max(0, formTitle.Bounds.Left - text.Bounds.Height * 8) &&
            l.Bounds.Right <= text.Bounds.Right + text.Bounds.Height * 12).ToArray();
        // OCR returns both words and lines, and preprocessing repeats the same label.
        // Accept one spatial cluster only; never choose between two different buttons.
        var execute = executeCandidates.FirstOrDefault();
        if (execute != null && executeCandidates.Any(l => !l.Bounds.IntersectsWith(execute.Bounds))) execute = null;
        var heading = labels.FirstOrDefault(l => Regex.IsMatch(l.Text, @"Результат\s*\(", RegexOptions.IgnoreCase));
        int? rows = null;
        if (heading != null && Regex.Match(heading.Text, @"(\d+(?:[ \u00A0\u202F]\d{3})*)\s*строк", RegexOptions.IgnoreCase) is { Success: true } match)
            rows = int.Parse(Regex.Replace(match.Groups[1].Value, @"\s", ""));
        Rectangle? editor = text == null ? null : FindFrame(image, text.Bounds.Left - text.Bounds.Height,
            text.Bounds.Right, text.Bounds.Bottom, heading?.Bounds.Top ?? image.Height, text.Bounds.Height, token,
            allowClippedRight: true);
        Rectangle? table = heading == null ? null : FindFrame(image, heading.Bounds.Left - heading.Bounds.Height * 3,
            heading.Bounds.Left + heading.Bounds.Height, heading.Bounds.Bottom, image.Height, heading.Bounds.Height, token);
        if (table == null && heading != null)
            table = FindClippedResult(image, heading.Bounds, token);
        // Errors are recognized outside query text, where identifiers may contain these words.
        var error = labels.FirstOrDefault(l => (editor == null || !editor.Value.IntersectsWith(l.Bounds)) &&
            (l.Text.Contains("Ошибка выполнения", StringComparison.OrdinalIgnoreCase) ||
             l.Text.Contains("Ошибка при выполнении", StringComparison.OrdinalIgnoreCase) ||
             l.Text.Contains("Ошибка запроса", StringComparison.OrdinalIgnoreCase) ||
             l.Text.Contains("Синтаксическая ошибка", StringComparison.OrdinalIgnoreCase) ||
             Regex.IsMatch(l.Text, @"\b(Таблица|Поле|Параметр|Функция)\s+не\s+найден[ао]?\b", RegexOptions.IgnoreCase) ||
             Regex.IsMatch(l.Text, @"\(?\d+(?:\s*,\s*\d+)?\)?\s*Ожидается", RegexOptions.IgnoreCase) ||
             l.Text.Trim().Equals("Ошибка", StringComparison.OrdinalIgnoreCase)))?.Text;
        var busy = labels.Any(l => (editor == null || !editor.Value.IntersectsWith(l.Bounds)) &&
            (l.Text.Contains("Отменить выполнение", StringComparison.OrdinalIgnoreCase) ||
             l.Text.Contains("Выполняется запрос", StringComparison.OrdinalIgnoreCase) || l.Text.Trim() == "Прервать"));
        var stamp = string.Join("|", labels.Where(l => execute != null && text != null &&
            l.Bounds.Top >= execute.Bounds.Top - execute.Bounds.Height && l.Bounds.Bottom < text.Bounds.Top &&
            l.Bounds.Left > execute.Bounds.Right && IsExecutionTime(l.Text))
            .Select(l => l.Text.Trim()).Distinct());
        return new(tab, text?.Bounds, execute?.Bounds, editor, table, rows, busy, error, stamp);
    }

    public static bool IsExecutionTime(string text) =>
        Regex.IsMatch(text.Trim(), @"^\d+([.,]\d+)?\s*(мс|мсек|сек|с|ms|s)\b");

    public static bool IsExecuteCaption(string value)
    {
        var text = string.Concat(value.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)))
            .Replace('b', 'в').Replace('o', 'о').Replace('t', 'т');
        const string expected = "выполнить";
        if (text == expected) return true;
        if (Math.Abs(text.Length - expected.Length) > 1) return false;
        // At most one insertion/deletion/substitution, restricted to the confirmed toolbar.
        var previous = Enumerable.Range(0, expected.Length + 1).ToArray();
        foreach (var character in text)
        {
            var current = new int[expected.Length + 1];
            current[0] = previous[0] + 1;
            for (var j = 1; j <= expected.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (character == expected[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[expected.Length] <= 1;
    }

    private static Bitmap PrepareToolbar(Bitmap original, int scale, int threshold, CancellationToken token)
    {
        using var strip = new Bitmap(original);
        if (threshold > 0)
            for (var y = 0; y < strip.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < strip.Width; x++)
                {
                    var c = strip.GetPixel(x, y);
                    var luminance = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                    strip.SetPixel(x, y, luminance < threshold ? Color.Black : Color.White);
                }
            }
        var enlarged = new Bitmap(strip.Width * scale + 48, strip.Height * scale + 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(enlarged);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(strip, new Rectangle(24, 24, strip.Width * scale, strip.Height * scale),
            new Rectangle(Point.Empty, strip.Size), GraphicsUnit.Pixel);
        return enlarged;
    }

    private static bool Border(Color c) =>
        (Math.Abs(c.R - c.G) < 8 && Math.Abs(c.G - c.B) < 8 && c.R is >= 110 and <= 225) ||
        (c.R > 225 && c.G is > 130 and < 225 && c.B < 100) ||
        (c.R > 225 && c.G < 100 && c.B < 100);

    private static Rectangle? FindClippedResult(Bitmap image, Rectangle heading, CancellationToken token)
    {
        Rectangle panel;
        try { panel = OneCSectionRecognizer.DetectPanel(image, token); }
        catch (SectionPanelNotFoundException) { return null; }
        // A restored RDP window can clip the table's bottom/right frame. Its
        // visible viewport is bounded by the independently recognized navigation
        // panel, rather than by black margins or the remote taskbar below it.
        var limit = Math.Min(image.Height, panel.Bottom);
        var scale = heading.Height;
        for (var y = heading.Bottom; y < Math.Min(limit, heading.Bottom + scale * 8); y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = Math.Max(panel.Right, heading.Left - scale * 3); x < heading.Left + scale && x < image.Width; x++)
            {
                if (!Border(image.GetPixel(x, y))) continue;
                var right = x;
                while (right + 1 < image.Width && Border(image.GetPixel(right + 1, y))) right++;
                if (right - x < Math.Max(heading.Width * 3, scale * 12)) continue;
                var bottom = y + 1;
                while (bottom < limit && Border(image.GetPixel(x, bottom))) bottom++;
                if (bottom < limit - scale || bottom - y < scale * 5) continue;
                return Rectangle.FromLTRB(x + 3, y + 3, right - 3, Math.Min(bottom, limit) - 3);
            }
        }
        return null;
    }

    private static Rectangle? FindFrame(Bitmap image, int fromX, int toX, int fromY, int toY, int scale,
        CancellationToken token, bool allowClippedRight = false)
    {
        // Search below the anchor for a continuous frame. Both vertical sides must
        // reach the bottom; internal table rows cannot become the region boundary.
        for (var y = fromY; y < Math.Min(toY, fromY + scale * 8); y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = Math.Max(0, fromX); x < Math.Min(image.Width, toX); x++)
            {
                if (!Border(image.GetPixel(x, y))) continue;
                var right = x;
                while (right + 1 < image.Width && Border(image.GetPixel(right + 1, y))) right++;
                if (right - x < scale * 12) continue;
                var clippedRight = allowClippedRight && (right == image.Width - 1 ||
                    image.GetPixel(right + 1, y).A == 0);
                var bottom = y + 1;
                while (bottom < toY && Border(image.GetPixel(x, bottom)) &&
                    (clippedRight || Border(image.GetPixel(right, bottom)))) bottom++;
                if (bottom - y < scale * 5) continue;
                if (clippedRight)
                {
                    // Require a visible bottom border as well as the left side;
                    // the right side may lie outside a restored RDP viewport.
                    var bottomRight = x;
                    while (bottomRight + 1 < image.Width && Border(image.GetPixel(bottomRight + 1, bottom - 1))) bottomRight++;
                    if (bottomRight != right) continue;
                }
                return Rectangle.FromLTRB(x + 3, y + 3, right - 3, bottom - 3);
            }
        }
        return null;
    }

    public static string Fingerprint(string path, Rectangle region, Point? cursor = null)
    {
        using var image = new Bitmap(path);
        using var crop = image.Clone(region, PixelFormat.Format24bppRgb);
        if (cursor is Point point)
        {
            using var graphics = Graphics.FromImage(crop);
            graphics.FillRectangle(Brushes.White, point.X - region.Left - 24, point.Y - region.Top - 24, 48, 48);
        }
        using var stream = new MemoryStream();
        crop.Save(stream, ImageFormat.Bmp);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public static bool ScrollbarAtBottom(string path, Rectangle region)
    {
        using var image = new Bitmap(path);
        // The vertical scrollbar sits against the detected table's right frame.
        // Exclude the horizontal scrollbar and arrow buttons using track width.
        var trackWidth = Math.Max(8, Math.Min(24, region.Width / 80));
        for (var x = Math.Max(region.Left, region.Right - trackWidth); x < Math.Min(image.Width, region.Right + 2); x++)
        {
            var start = -1;
            for (var y = region.Top + trackWidth; y < region.Bottom - trackWidth; y++)
            {
                var c = image.GetPixel(x, y);
                var thumb = c.R is >= 120 and <= 185 && Math.Abs(c.R - c.G) < 6 && Math.Abs(c.G - c.B) < 6;
                if (thumb && start < 0) start = y;
                if (!thumb || y == region.Bottom - trackWidth - 1)
                {
                    if (start >= 0 && y - start >= trackWidth * 2 &&
                        y >= region.Bottom - trackWidth * 2) return true;
                    start = -1;
                }
            }
        }
        return false;
    }
}
