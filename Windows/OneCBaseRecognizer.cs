using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace ShowroomBot.Windows;

public sealed record OneCWindowRecognition(bool IsTargetClient, string Description);

public static class OneCBaseRecognizer
{
    public static async Task<OneCWindowRecognition> RecognizeWindowAsync(
        string screenshotPath, Color expected, int tolerance, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = new Bitmap(screenshotPath);
        var hasColor = HasPanelColor(image, expected, tolerance);
        // Inspect title/menu area, not arbitrary references to the configurator in workspace text.
        using var header = image.Clone(new Rectangle(0, 0, image.Width, Math.Max(1, image.Height / 4)), PixelFormat.Format32bppArgb);
        var headerPath = Path.ChangeExtension(screenshotPath, ".window-header.png");
        header.Save(headerPath, ImageFormat.Png);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(headerPath)).AsTask(token);
        using var stream = await file.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token);
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException("Open1C: размер screenshot превышает предел Windows OCR.");
        var languages = OcrEngine.AvailableRecognizerLanguages
            .Where(language => language.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
                language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (languages.Length == 0)
            throw new InvalidOperationException("Open1C: для исключения конфигуратора нужен русский или английский Windows OCR.");
        var headers = new List<string>();
        foreach (var language in languages)
        {
            token.ThrowIfCancellationRequested();
            var engine = OcrEngine.TryCreateFromLanguage(language)
                ?? throw new InvalidOperationException($"Open1C: недоступен OCR {language.LanguageTag}.");
            var result = await engine.RecognizeAsync(bitmap).AsTask(token);
            headers.Add(result.Text);
        }
        var text = string.Join(Environment.NewLine, headers);
        await File.WriteAllTextAsync(Path.ChangeExtension(screenshotPath, ".window-header.txt"), text, token);
        return ClassifyWindow(hasColor, text);
    }

    public static OneCWindowRecognition ClassifyWindow(bool hasPanelColor, string headerText)
    {
        var text = string.Concat(headerText.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        var configurator = text.Contains("КОНФИГУРАТОР") || text.Contains("CONFIGURATOR") ||
            text.Contains("DESIGNER") || (text.Contains("КОНФИГУРАЦИЯ") && text.Contains("ОТЛАДКА"));
        if (configurator)
            return new(false, $"конфигуратор; цвет панели {(hasPanelColor ? "совпадает, окно исключено" : "не совпадает")}");
        return hasPanelColor
            ? new(true, "пользовательский клиент по цвету панели; признаков конфигуратора не обнаружено")
            : new(false, "другое окно или другая база: цвет панели не совпадает");
    }

    public static bool HasPanelColor(Bitmap image, Color expected, int tolerance)
    {
        var rows = 0;
        for (var y = 0; y < image.Height; y += 4)
        {
            var run = 0;
            var longest = 0;
            for (var x = 0; x < image.Width; x += 4)
            {
                run = Similar(image.GetPixel(x, y), expected, tolerance) ? run + 4 : 0;
                longest = Math.Max(longest, run);
            }
            if (longest >= 80) rows++;
        }
        // Require a substantial panel area, not isolated colour matches.
        return rows >= 8;
    }

    public static bool SameWindow(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size) return false;
        var matches = 0;
        var samples = 0;
        for (var y = 8; y < first.Height; y += Math.Max(8, first.Height / 80))
        for (var x = 8; x < first.Width; x += Math.Max(8, first.Width / 100))
        {
            samples++;
            if (Similar(first.GetPixel(x, y), second.GetPixel(x, y), 12)) matches++;
        }
        // Allow minor clock/caret changes when detecting return to the initial window.
        return samples > 0 && matches >= samples * .995;
    }

    private static bool Similar(Color a, Color b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
}
