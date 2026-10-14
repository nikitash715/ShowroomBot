using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ShowroomBot.Windows;

internal static class ToolkitRecognitionChecks
{
    public static async Task RunAsync(string[] images)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }
        foreach (var caption in new[] { "Выполнить", "ВЫПОЛНИТЬ", "Выпoлнить", "Выполнитъ", "Выполнит", "Выполнигь" })
            Check(ToolkitConsoleRecognizer.IsExecuteCaption(caption), "caption accepted: " + caption);
        foreach (var caption in new[] { "Проверить", "Отменить", "Не выполнить", "Выполнить всё", "Выполнение", "" })
            Check(!ToolkitConsoleRecognizer.IsExecuteCaption(caption), "caption rejected: " + caption);

        using (var image = new Bitmap(1000, 800))
        {
            var labels = new[] {
                new RecognizedText("Консоль разработчика (Toolkit)", new(150, 20, 400, 16)),
                new RecognizedText("Консоль разработчика (Toolkit)", new(150, 50, 400, 16)),
                new RecognizedText("Текст", new(305, 125, 40, 16)),
                new RecognizedText("Выполнитъ", new(200, 90, 80, 16)) };
            Check(ToolkitConsoleRecognizer.Analyze(image, labels).Execute == labels[3].Bounds,
                "fuzzy caption in confirmed toolbar maps to original coordinates");
            Check(ToolkitConsoleRecognizer.Analyze(image, labels.Append(labels[3] with { Bounds = new(400, 90, 80, 16) }).ToArray()).Execute == null,
                "two separate matching buttons are rejected");
            Check(ToolkitConsoleRecognizer.Analyze(image, labels.Select((l, i) => i == 3 ? l with { Bounds = new(200, 250, 80, 16) } : l).ToArray()).Execute == null,
                "query text below toolbar cannot be a button");
        }
        var directory = Path.GetFullPath(Path.Combine("artifacts", "toolkit-ocr-check-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(directory);
        foreach (var input in images)
        {
            var screenshot = Path.Combine(directory, Path.GetFileName(input));
            File.Copy(input, screenshot, true);
            var recognizer = new ToolkitConsoleRecognizer(new OneCSectionRecognizer());
            var actual = await recognizer.RecognizeAsync(screenshot, CancellationToken.None);
            Check(actual.Editor != null && actual.Execute is Rectangle button &&
                new Rectangle(380, 98, 150, 42).Contains(button),
                $"incident screenshot: editor={actual.Editor}, execute={actual.Execute}");
            Check(File.Exists(Path.ChangeExtension(screenshot, ".toolkit-ocr.json")), "OCR coordinates persisted");

            var largePath = Path.Combine(directory, "large-" + Path.GetFileName(input));
            using (var source = new Bitmap(screenshot))
            using (var large = new Bitmap(Math.Max(source.Width + 64, (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension + 100), source.Height + 32))
            {
                using var graphics = Graphics.FromImage(large);
                graphics.Clear(Color.White);
                graphics.DrawImageUnscaled(source, 64, 32);
                large.Save(largePath, ImageFormat.Png);
            }
            var largeResult = await recognizer.RecognizeAsync(largePath, CancellationToken.None);
            Check(largeResult.Execute is Rectangle shifted && new Rectangle(444, 130, 150, 42).Contains(shifted),
                $"oversized screenshot tiles preserve shifted button coordinates: {largeResult.Execute}");

            // Remove only the button from the real screenshot: nearby query/result text
            // must not produce a false click, and a complete failure bundle must survive.
            var missingPath = Path.Combine(directory, "missing-" + Path.GetFileName(input));
            using (var missing = new Bitmap(screenshot))
            {
                using var graphics = Graphics.FromImage(missing);
                graphics.FillRectangle(Brushes.White, new Rectangle(380, 98, 150, 42));
                missing.Save(missingPath, ImageFormat.Png);
            }
            var absent = await recognizer.RecognizeAsync(missingPath, CancellationToken.None);
            Check(absent.Execute == null, "removed button is not inferred from a visual guess");
            foreach (var suffix in new[] { ".toolkit-not-found.json", ".toolkit-search.json", ".toolkit-search.png",
                         ".toolkit-search-overlay.png", ".toolkit-toolbar-145.png", ".toolkit-toolbar-180.toolkit-ocr.json" })
                Check(File.Exists(Path.ChangeExtension(missingPath, suffix)), "failure artifact " + suffix);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(missingPath, ".toolkit-search.json")));
            Check(!report.RootElement.GetProperty("Found").GetBoolean(), "failure report explicitly marks button not found");
        }
        Console.WriteLine("Artifacts: " + directory);
    }
}
