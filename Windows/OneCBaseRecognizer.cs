namespace ShowroomBot.Windows;

public static class OneCBaseRecognizer
{
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
