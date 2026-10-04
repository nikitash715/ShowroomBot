using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Drawing.Drawing2D;

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
            // Full-screen OCR can omit small toolbar labels among dense query/table text.
            // Retry only the toolbar, keeping all returned coordinates in the original image.
            var region = Rectangle.FromLTRB(0, Math.Max(0, text.Top - text.Height * 8),
                Math.Min(image.Width, text.Right + text.Height * 12), text.Top);
            if (region.Height > 0)
            {
                using var crop = new Bitmap(region.Width * 2, region.Height * 2);
                using (var graphics = Graphics.FromImage(crop))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(0, 0, crop.Width, crop.Height), region, GraphicsUnit.Pixel);
                }
                var cropPath = Path.ChangeExtension(path, ".toolkit-toolbar.png");
                crop.Save(cropPath, ImageFormat.Png);
                var toolbar = await ocr.ReadLinesAsync(cropPath, token);
                labels = labels.Concat(toolbar.Select(l => l with { Bounds = Rectangle.FromLTRB(
                    region.Left + l.Bounds.Left / 2, region.Top + l.Bounds.Top / 2,
                    region.Left + (l.Bounds.Right + 1) / 2, region.Top + (l.Bounds.Bottom + 1) / 2) })).ToArray();
                layout = Analyze(image, labels, token);
            }
        }
        if (layout.RowCount == 0 && layout.Result != null)
        {
            var heading = labels.First(l => Regex.IsMatch(l.Text, @"Результат\s*\(", RegexOptions.IgnoreCase));
            var region = Rectangle.Intersect(new Rectangle(heading.Bounds.Left - 4, heading.Bounds.Top - 4,
                heading.Bounds.Width + 8, heading.Bounds.Height + 8), new Rectangle(Point.Empty, image.Size));
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
                .Select(l => new RecognizedText(l.Text, heading.Bounds)).ToArray();
            layout = Analyze(image, corrected.Concat(labels).ToArray(), token);
        }
        return layout;
    }

    public static ToolkitConsoleLayout Analyze(Bitmap image, IReadOnlyList<RecognizedText> labels, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var titles = labels.Where(l => l.Text.Contains("Консоль разработчика", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(l.Text, @"T[oо0]{1,2}[lI1]k[iI1]t", RegexOptions.IgnoreCase)).OrderBy(l => l.Bounds.Top).ToArray();
        var tab = titles.FirstOrDefault()?.Bounds;
        var formTitle = titles.FirstOrDefault(l => tab.HasValue && l.Bounds.Top > tab.Value.Bottom);
        var text = labels.Where(l => formTitle != null && l.Bounds.Top > formTitle.Bounds.Bottom && l.Text.Trim() == "Текст")
            .OrderBy(l => l.Bounds.Top).FirstOrDefault();
        var execute = labels.Where(l => l.Text.Trim() == "Выполнить" && text != null && l.Bounds.Bottom < text.Bounds.Top)
            .OrderByDescending(l => l.Bounds.Top).FirstOrDefault();
        var heading = labels.FirstOrDefault(l => Regex.IsMatch(l.Text, @"Результат\s*\(", RegexOptions.IgnoreCase));
        int? rows = null;
        if (heading != null && Regex.Match(heading.Text, @"(\d+(?:[ \u00A0\u202F]\d{3})*)\s*строк", RegexOptions.IgnoreCase) is { Success: true } match)
            rows = int.Parse(Regex.Replace(match.Groups[1].Value, @"\s", ""));
        Rectangle? editor = text == null ? null : FindFrame(image, text.Bounds.Left - text.Bounds.Height,
            text.Bounds.Right, text.Bounds.Bottom, heading?.Bounds.Top ?? image.Height, text.Bounds.Height, token);
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
            l.Bounds.Left > execute.Bounds.Right && Regex.IsMatch(l.Text.Trim(), @"^\d+([.,]\d+)?\s*(мс|мсек|сек|ms|s)\b"))
            .Select(l => l.Text.Trim()).Distinct());
        return new(tab, text?.Bounds, execute?.Bounds, editor, table, rows, busy, error, stamp);
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

    private static Rectangle? FindFrame(Bitmap image, int fromX, int toX, int fromY, int toY, int scale, CancellationToken token)
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
                var bottom = y + 1;
                while (bottom < toY && Border(image.GetPixel(x, bottom)) && Border(image.GetPixel(right, bottom))) bottom++;
                if (bottom - y < scale * 5) continue;
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
