using System.Security.Cryptography;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShowroomBot.Windows;

public static class ReadCodeModulePrefixes
{
    public const string RelativePath = "Resources/ReadCode/ReadCodeModulePrefixes.txt";

    public static string[] Parse(IEnumerable<string> lines) => lines.Select(l => l.Trim())
        .Where(l => l.Length == 2 && l.All(char.IsLetter))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string[] Load(string baseDirectory)
    {
        try { return Parse(File.ReadLines(Path.Combine(baseDirectory, RelativePath))); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static bool IsNavigationRow(string name) => name.Length > 0 && !name.EndsWith('_') &&
        !new[] { "CRM", "СЯМ", "САМ", "СРМ", "СRМ" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedModule(string name) => IsNavigationRow(name) &&
        !OneCConfiguratorRecognizer.CanonicalModuleName(name).StartsWith("CRM", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith('_');

    public static RecognizedText[] Candidates(IEnumerable<RecognizedText> rows, ISet<string> visited,
        string? prefix = null) => rows.Where(row => IsAllowedModule(row.Text) && !visited.Contains(row.Text) &&
            (prefix == null || row.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();

    public static RecognizedText? SelectedCandidate(Bitmap image, IReadOnlyList<RecognizedText> rows, string name)
    {
        var selected = ReadCodePrefixNavigation.SelectedRow(image, rows);
        return selected != null && IsAllowedModule(selected.Text) &&
            OneCConfiguratorRecognizer.MatchesModuleName(selected.Text, name) ? selected : null;
    }
}

public sealed record TreeFocusSnapshot(bool IsConfigurator, bool HasEditor, Rectangle Tree,
    Rectangle ClientBounds, string? SelectedModule, string OutsideTreeHash)
{
    public bool IsSafe => IsConfigurator && Tree.Width > 0 && Tree.Height > 0 &&
        SelectedModule != null && ReadCodeModulePrefixes.IsNavigationRow(SelectedModule);
}

public static class ReadCodePrefixNavigation
{
    public static bool SameSurface(TreeFocusSnapshot first, TreeFocusSnapshot next) =>
        first.IsSafe && next.IsSafe && first.Tree == next.Tree && first.ClientBounds == next.ClientBounds &&
        first.HasEditor == next.HasEditor && first.OutsideTreeHash == next.OutsideTreeHash;

    // A click alone is insufficient. Require keyboard-driven selection movement
    // and its return, with no changes outside the metadata tree. An already
    // open editor/form is allowed only while that surface remains unchanged.
    public static async Task<bool> TryJumpAsync(string prefix,
        Func<CancellationToken, Task<TreeFocusSnapshot>> capture,
        Func<ushort, CancellationToken, Task> key,
        Func<string, CancellationToken, Task<bool>> type, CancellationToken token)
    {
        var parsed = ReadCodeModulePrefixes.Parse([prefix]);
        if (parsed.Length != 1) return false;
        prefix = parsed[0];
        var first = await capture(token);
        if (!first.IsSafe) return false;
        await key(0x28, token); // Down: non-mutating focus challenge.
        var down = await capture(token);
        if (!SameSurface(first, down) || down.SelectedModule == first.SelectedModule) return false;
        await key(0x26, token); // Up must restore the original selection.
        var back = await capture(token);
        if (!SameSurface(first, back) || back.SelectedModule != first.SelectedModule) return false;
        token.ThrowIfCancellationRequested();
        return await type(prefix, token);
    }

    public static string OutsideTreeHash(Bitmap image, Rectangle tree)
    {
        using var masked = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(masked))
        {
            graphics.DrawImageUnscaled(image, 0, 0);
            graphics.FillRectangle(Brushes.White, tree);
        }
        var data = masked.LockBits(new Rectangle(Point.Empty, masked.Size), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { masked.UnlockBits(data); }
    }

    public static RecognizedText? SelectedRow(Bitmap image, IReadOnlyList<RecognizedText> rows)
    {
        // Recognize active blue/yellow selection only. Grey inactive highlights
        // and unrecognized themes deliberately fall back to wheel navigation.
        var selected = rows.Where(row =>
        {
            var region = Rectangle.Intersect(row.Bounds, new Rectangle(Point.Empty, image.Size));
            if (region.IsEmpty) return false;
            var colored = 0;
            var total = 0;
            for (var y = region.Top; y < region.Bottom; y += 2)
            for (var x = region.Left; x < region.Right; x += 2)
            {
                var c = image.GetPixel(x, y);
                if ((c.B > c.R + 35 && c.B > c.G + 10) ||
                    (c.R > 180 && c.G > 140 && c.B < Math.Min(c.R, c.G) - 45)) colored++;
                total++;
            }
            return total > 0 && colored > total * .45;
        }).ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }
}
