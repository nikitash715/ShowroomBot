using System.Text.RegularExpressions;

namespace ShowroomBot.Windows;

public sealed record ConfiguratorView(bool IsConfigurator, Rectangle? Tree, Rectangle? Editor,
    string? ModuleName, IReadOnlyList<RecognizedText> Labels);

public sealed record TreeToggle(Point Center, bool IsExpanded);

public static class OneCConfiguratorRecognizer
{
    // Prefer whole rows over the overlapping words/lines returned by multi-scale OCR.
    public static IReadOnlyList<RecognizedText> TreeRows(IReadOnlyList<RecognizedText> labels, Rectangle tree)
    {
        var rows = new List<RecognizedText>();
        foreach (var label in labels.Where(l => l.Bounds.Left >= tree.Left && l.Bounds.Left < tree.Right &&
                     l.Bounds.Top >= tree.Top && l.Bounds.Bottom <= tree.Bottom &&
                     // OCR dock bounds can include the search field above the actual tree.
                     !(l.Bounds.Top < tree.Top + 24 && l.Bounds.Left < tree.Left + 15))
                 .OrderByDescending(l => l.Text.Length))
        {
            var index = rows.FindIndex(r => Math.Abs((r.Bounds.Top + r.Bounds.Bottom) / 2 -
                    (label.Bounds.Top + label.Bounds.Bottom) / 2) <= Math.Max(3, Math.Min(r.Bounds.Height, label.Bounds.Height) / 2));
            if (index >= 0)
            {
                var row = rows[index];
                if (!row.Bounds.IntersectsWith(label.Bounds))
                    rows[index] = new(row.Bounds.Left < label.Bounds.Left ? row.Text + " " + label.Text : label.Text + " " + row.Text,
                        Rectangle.Intersect(Rectangle.Union(row.Bounds, label.Bounds), tree));
                continue;
            }
            rows.Add(label with { Bounds = Rectangle.Intersect(label.Bounds, tree) });
        }
        return rows.OrderBy(r => r.Bounds.Top).ToArray();
    }

    public static RecognizedText? FindTreeFocusRow(IReadOnlyList<RecognizedText> labels, Rectangle tree) =>
        TreeRows(labels, tree)
            // The detected dock can include its search field at the top.
            // Focus a complete, indented metadata row near the middle instead.
            .Where(r => r.Bounds.Top > tree.Top + 30 && r.Bounds.Bottom < tree.Bottom - 10 &&
                r.Bounds.Left >= tree.Left + 20)
            .OrderBy(r => Math.Abs(r.Bounds.Top + r.Bounds.Height / 2 - (tree.Top + tree.Height / 2)))
            .FirstOrDefault();

    public static RecognizedText? FindTreeNode(IReadOnlyList<RecognizedText> labels, Rectangle tree, string name)
    {
        var expected = Normalize(name);
        var matches = TreeRows(labels, tree).Select(row => (Row: row, Distance: EditDistance(Normalize(row.Text), expected)))
            .Where(m => m.Distance <= (expected.Length > 8 ? 2 : 1)).OrderBy(m => m.Distance).ToArray();
        return matches.Length == 0 || (matches.Length > 1 && matches[0].Distance == matches[1].Distance)
            ? null : matches[0].Row;
    }

    public static RecognizedText[] CommonModuleRows(IReadOnlyList<RecognizedText> labels, Rectangle tree,
        RecognizedText? parent, int indent)
    {
        var rows = TreeRows(labels, tree);
        var fromY = parent?.Bounds.Bottom ?? tree.Top;
        var toY = rows.Where(r => r.Bounds.Top >= fromY && r.Bounds.Left <= indent + 5)
            .Select(r => r.Bounds.Top).DefaultIfEmpty(tree.Bottom).Min();
        return rows.Where(r => r.Bounds.Top >= fromY && r.Bounds.Bottom < toY && r.Bounds.Left > indent + 5)
            .Select(r => r with { Text = ModuleRowName(r.Text) })
            .Where(r => Regex.IsMatch(r.Text, @"^[\p{L}_][\p{L}\p{Nd}_]*$")).ToArray();
    }

    private static string ModuleRowName(string text)
    {
        // Metadata identifiers cannot contain spaces. OCR splits camel-case names
        // and sometimes includes the small document icon before a CRM prefix.
        var name = Regex.Replace(text.Trim(), @"^[ТЭ]\s+(?=[\p{L}]{3}_)", "", RegexOptions.IgnoreCase);
        return CanonicalModuleName(Regex.Replace(name, @"\s+", ""));
    }

    public static string CanonicalModuleName(string name) =>
        Regex.Replace(name, @"^(?:сям|сrм|сrm|crm)_", "CRM_", RegexOptions.IgnoreCase);

    public static IReadOnlyList<RecognizedText> VisibleRoutines(ConfiguratorView view) =>
        view.Editor is { } editor ? view.Labels.Where(l => editor.Contains(l.Bounds) &&
            Regex.IsMatch(l.Text.Trim(), @"^(?:Процедура|Функция|Procedure|Function)\s+[\p{L}_][\p{L}\p{Nd}_]*\s*\(", RegexOptions.IgnoreCase)).ToArray() : [];

    public static bool HasRoutineSyntax(ConfiguratorView view) => VisibleRoutines(view).Count > 0;

    public static bool ConfirmsModuleOpening(ConfiguratorView before, ConfiguratorView after, string candidate) =>
        after.Editor != null && (MatchesModuleName(after.ModuleName, candidate) ||
            (before.Editor == null && HasRoutineSyntax(after)) || RoutinesChanged(before, after));

    private static bool RoutinesChanged(ConfiguratorView before, ConfiguratorView after)
    {
        // Compare declarations by name, ignoring OCR whitespace, order, parameters and coordinates.
        static HashSet<string> Names(ConfiguratorView view) => VisibleRoutines(view)
            .Select(r => Regex.Match(r.Text.Trim(), @"^\S+\s+(?<name>[\p{L}_][\p{L}\p{Nd}_]*)\s*\(").Groups["name"].Value)
            .Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var previous = Names(before);
        var current = Names(after);
        // Losing a declaration alone can be a transient OCR failure.
        return before.Editor != null && current.Except(previous).Any();
    }

    private static int EditDistance(string actual, string expected)
    {
        var previous = Enumerable.Range(0, expected.Length + 1).ToArray();
        for (var i = 1; i <= actual.Length; i++)
        {
            var current = new int[expected.Length + 1];
            current[0] = i;
            for (var j = 1; j <= expected.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (actual[i - 1] == expected[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[expected.Length];
    }

    public static bool MatchesModuleName(string? title, string candidate)
    {
        if (title == null) return false;
        static string Comparable(string value) => string.Concat(Normalize(CanonicalModuleName(value)).Select(c => c switch
        {
            'А' => 'A', 'В' => 'B', 'С' => 'C', 'Е' => 'E', 'Н' => 'H', 'К' => 'K',
            'М' => 'M', 'О' => 'O', 'Р' => 'P', 'Т' => 'T', 'Х' => 'X', 'У' => 'Y', _ => c
        }));
        var actual = Comparable(title);
        var expected = Comparable(candidate);
        // Scale the OCR allowance for long names; keep short identifiers strict.
        var allowance = expected.Length >= 16 ? Math.Max(2, (int)Math.Ceiling(expected.Length * 0.12))
            : expected.Length / 8;
        return EditDistance(actual, expected) <= allowance;
    }

    public static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static string? ParseModuleTitle(string text)
    {
        // The small bold caption often loses letters in "Общий" and confuses
        // "л/ь" with "ј/ъ". Keep both module words and the name/colon structure.
        var match = Regex.Match(text,
            @"^.+?\s+(?<kind>\S+)\s+(?<name>[\p{L}\p{Nd}_]+)\s*:\s*(?<suffix>[\p{L}]+)(?:\s|$)",
            RegexOptions.IgnoreCase);
        return match.Success && EditDistance(Normalize(match.Groups["kind"].Value), "МОДУЛЬ") <= 2 &&
            EditDistance(Normalize(match.Groups["suffix"].Value), "МОДУЛЬ") <= 2
            ? CanonicalModuleName(match.Groups["name"].Value) : null;
    }

    public static TreeToggle? FindTreeToggle(Bitmap image, Rectangle tree, RecognizedText node)
    {
        // Classic 1C uses small grey circles/squares with a plus or minus.
        // Search the node's row only, without assuming a fixed indent or screen position.
        var middle = node.Bounds.Top + node.Bounds.Height / 2;
        var matches = new List<TreeToggle>();
        var right = Math.Min(tree.Right - 7, node.Bounds.Left + node.Bounds.Height * 2);
        for (var y = Math.Max(tree.Top + 7, middle - 4); y <= Math.Min(tree.Bottom - 8, middle + 4); y++)
        for (var x = Math.Max(tree.Left + 7, 7); x < right; x++)
        {
            bool Ink(int dx, int dy)
            {
                var c = image.GetPixel(x + dx, y + dy);
                return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 25 &&
                    c.R < 210 && c.G < 210 && c.B < 210;
            }
            if (!Enumerable.Range(-2, 5).All(dx => Ink(dx, 0))) continue;
            for (var radius = 3; radius <= 6; radius++)
            {
                if (!Ink(-radius, 0) || !Ink(radius, 0) || !Ink(0, -radius) || !Ink(0, radius)) continue;
                // Empty diagonal pixels distinguish the glyph from icons/text/solid blocks.
                if (new[] { (-2, -2), (-2, 2), (2, -2), (2, 2) }
                    .Any(p => !White(image.GetPixel(x + p.Item1, y + p.Item2)))) continue;
                var plus = Ink(0, -1) && Ink(0, 1) && Ink(0, -2) && Ink(0, 2);
                var minus = !Ink(0, -1) && !Ink(0, 1);
                if (!plus && !minus) continue;
                if (!matches.Any(m => Math.Abs(m.Center.X - x) < 4 && Math.Abs(m.Center.Y - y) < 4))
                    matches.Add(new(new(x, y), !plus));
                break;
            }
        }
        // A glyph before the text is preferred; the wider search also handles OCR
        // lines whose bounding box includes the metadata icon or the toggle itself.
        var beforeText = matches.Where(m => m.Center.X < node.Bounds.Left - 4).ToArray();
        var candidates = beforeText.Length > 0 ? beforeText : matches.ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static RecognizedText? FindMainConfigurationTab(IReadOnlyList<RecognizedText> labels, Size size) =>
        labels.Where(l => Normalize(l.Text) == "КОНФИГУРАЦИЯ" &&
            // The service-message dock can push the metadata tabs well above
            // the bottom of the screen. Menu/dock captions remain in the top half.
            l.Bounds.Top > size.Height * .5 && l.Bounds.Left < size.Width / 3)
            .OrderByDescending(l => l.Bounds.Top).FirstOrDefault();

    public static IReadOnlyList<RecognizedText> FindConfigurationTabs(IReadOnlyList<RecognizedText> labels, Size size)
    {
        var main = FindMainConfigurationTab(labels, size);
        if (main == null) return [];
        var tabs = new List<RecognizedText> { main };
        foreach (var label in labels.Where(l => l.Bounds.Left < size.Width / 3 &&
                     Math.Abs(l.Bounds.Top - main.Bounds.Top) < main.Bounds.Height && l.Bounds.Height <= main.Bounds.Height * 2)
                 .OrderByDescending(l => l.Text.Length))
        {
            if (!tabs.Any(t => t.Bounds.IntersectsWith(label.Bounds))) tabs.Add(label);
        }
        return tabs.OrderBy(t => Normalize(t.Text) == "КОНФИГУРАЦИЯ" ? 0 : 1).ThenBy(t => t.Bounds.Left).ToArray();
    }

    public static bool HasGoToDialog(IReadOnlyList<RecognizedText> labels) => HasDialog(labels,
        ["Перейти по номеру строки", "Переход к строке", "Перейти к строке"],
        ["Введите номер строки", "Номер строки"], ["Перейти", "ОК", "OK"]);

    public static bool HasRunDialog(IReadOnlyList<RecognizedText> labels) =>
        HasDialog(labels, ["Выполнить"], ["Открыть"], ["ОК", "OK"]);

    private static bool HasDialog(IReadOnlyList<RecognizedText> labels, string[] titles, string[] fields, string[] buttons)
    {
        foreach (var title in labels.Where(l => titles.Any(t => Normalize(t) == Normalize(l.Text))))
        {
            bool Below(RecognizedText l) => l.Bounds.Top > title.Bounds.Bottom &&
                l.Bounds.Top < title.Bounds.Bottom + title.Bounds.Height * 24 &&
                l.Bounds.Left >= title.Bounds.Left - title.Bounds.Height * 2 &&
                l.Bounds.Left < title.Bounds.Left + title.Bounds.Height * 50;
            if (labels.Any(l => Below(l) && fields.Any(f => Normalize(l.Text).StartsWith(Normalize(f)))) &&
                labels.Any(l => Below(l) && buttons.Any(b => Normalize(l.Text) == Normalize(b))) &&
                labels.Any(l => Below(l) && Normalize(l.Text) == "ОТМЕНА")) return true;
        }
        return false;
    }

    public static ConfiguratorView Analyze(Bitmap image, IReadOnlyList<RecognizedText> labels)
    {
        var title = labels.FirstOrDefault(l => l.Bounds.Top < image.Height / 5 &&
            l.Bounds.Left < image.Width / 3 && Normalize(l.Text).Contains("КОНФИГУРАТОР"));
        var menu = labels.FirstOrDefault(l => Normalize(l.Text) == "КОНФИГУРАЦИЯ" && title != null &&
            l.Bounds.Top > title.Bounds.Top && l.Bounds.Top < title.Bounds.Bottom + 60);
        var debug = labels.Any(l => Normalize(l.Text) == "ОТЛАДКА" && menu != null &&
            Math.Abs(l.Bounds.Top - menu.Bounds.Top) < menu.Bounds.Height);
        if (title == null || menu == null || !debug) return new(false, null, null, null, labels);

        Rectangle? tree = null;
        var treeHeading = labels.FirstOrDefault(l => IsConfigurationHeading(l.Text) &&
            l.Bounds.Left < image.Width / 6 && l.Bounds.Top > menu.Bounds.Bottom + 10 &&
            l.Bounds.Top < image.Height / 2);
        // Open extension docks use the extension name as their heading.
        treeHeading ??= labels.FirstOrDefault(l => l.Bounds.Left < image.Width / 6 &&
            l.Bounds.Top > menu.Bounds.Bottom + 10 && l.Bounds.Top < image.Height / 3 &&
            FindConfigurationTabs(labels, image.Size).Any(t => Normalize(t.Text) == Normalize(l.Text)));
        // The grey dock caption can disappear entirely in OCR. Its toolbar is
        // a second anchor, only in a confirmed Configurator with a dock tab.
        if (treeHeading == null && FindConfigurationTabs(labels, image.Size).Count > 0)
        {
            var actions = labels.FirstOrDefault(l => Normalize(l.Text) == "ДЕЙСТВИЯ" &&
                l.Bounds.Left < image.Width / 6 && l.Bounds.Top > menu.Bounds.Bottom + 30 &&
                l.Bounds.Top < image.Height / 3);
            if (actions != null)
                treeHeading = actions with { Bounds = new Rectangle(2, actions.Bounds.Top - 20, actions.Bounds.Width, 14) };
        }
        if (treeHeading != null)
        {
            var dockBottom = FindMainConfigurationTab(labels, image.Size)?.Bounds.Top - 3 ?? image.Height - 65;
            // The main configuration dock has a white tree and a continuous right divider.
            var top = treeHeading.Bounds.Bottom + treeHeading.Bounds.Height * 3;
            for (var x = Math.Max(160, treeHeading.Bounds.Right + 20); x < image.Width / 2; x++)
            {
                var votes = 0;
                var samples = 0;
                var whiteTotal = 0;
                for (var y = top; y < dockBottom - 5; y += 5)
                {
                    samples++;
                    // Leave room for the scrollbar and the metadata status icons.
                    // Their repeated rows must not hide an otherwise continuous dock edge.
                    // Sample a strip rather than two pixels: long module names can
                    // cover both fixed probes on almost every row of a scrolled list.
                    var whiteSamples = 0;
                    for (var offset = 20; offset <= 140; offset += 4)
                        if (White(image.GetPixel(x - offset, y))) whiteSamples++;
                    whiteTotal += whiteSamples;
                    // An open module can start just a few pixels after the dock.
                    // Probe the divider itself, not the editor beyond that gap.
                    if (!White(image.GetPixel(x, y)) &&
                        !White(image.GetPixel(x + 1, y)) && !White(image.GetPixel(x + 3, y))) votes++;
                }
                // Dense metadata names can occupy most of an individual row.
                // Confirm the continuous divider and the white background independently.
                if (samples > 0 && votes > samples * .80 && whiteTotal > samples * 31 * .60)
                {
                    // OCR can omit the first letters of the dock caption and shift
                    // its left bound past root-level tree nodes. This dock is left-aligned.
                    tree = Rectangle.FromLTRB(2, top, x - 2, dockBottom);
                    break;
                }
            }
        }

        var moduleTitle = labels.Where(l => l.Bounds.Top > menu.Bounds.Bottom && l.Bounds.Top < image.Height / 2 &&
            l.Bounds.Left > (tree?.Right ?? image.Width / 5) && ParseModuleTitle(l.Text) != null)
            .OrderByDescending(l => l.Text.Length).FirstOrDefault();
        Rectangle? editor = null;
        string? moduleName = null;
        if (moduleTitle != null)
        {
            moduleName = ParseModuleTitle(moduleTitle.Text);
            var seedX = Math.Min(image.Width - 1, moduleTitle.Bounds.Left + moduleTitle.Bounds.Height * 4);
            for (var y = moduleTitle.Bounds.Bottom + 1; y < Math.Min(image.Height, moduleTitle.Bounds.Bottom + 30); y++)
            {
                if (!White(image.GetPixel(seedX, y))) continue;
                var left = seedX;
                var right = seedX;
                while (left > 0 && White(image.GetPixel(left - 1, y))) left--;
                while (right + 1 < image.Width && White(image.GetPixel(right + 1, y))) right++;
                if (right - left < image.Width / 4 || left <= (tree?.Right ?? 50)) continue;
                var bottom = y;
                while (bottom + 1 < image.Height && White(image.GetPixel(right - 3, bottom + 1))) bottom++;
                if (bottom - y < 100) continue;
                // The caret on the first blank line can split the white run.
                // Recover the left edge from neighbouring rows so the folding
                // gutter is included in the editor rectangle.
                for (var row = y + 1; row < Math.Min(y + 30, bottom); row++)
                {
                    if (!White(image.GetPixel(seedX, row))) continue;
                    var rowLeft = seedX;
                    while (rowLeft > 0 && White(image.GetPixel(rowLeft - 1, row))) rowLeft--;
                    if (rowLeft > (tree?.Right ?? 50)) left = Math.Min(left, rowLeft);
                }
                editor = Rectangle.FromLTRB(left, y, right, bottom);
                break;
            }
        }
        moduleName ??= ParseModuleTitle(title.Text);
        if (editor == null && tree != null)
        {
            var surface = FindModuleSurface(image, new(true, tree, null, moduleName, labels));
            if (surface is { } bounds)
            {
                // Maximized MDI modules put their title in the application title bar.
                // A confirmed module title also covers a single folded procedure or
                // an empty module. Keep the syntax fallback for unreadable titles.
                if (moduleName != null || HasRoutineSyntax(new(true, tree, bounds, moduleName, labels))) editor = bounds;
            }
        }
        return new(true, tree, editor, moduleName, labels);
    }

    // Locate the caption immediately above a large white editor surface. This is
    // only an OCR crop; the complete module title must still confirm the editor.
    public static Rectangle? FindModuleCaptionRegion(Bitmap image, ConfiguratorView view)
    {
        var surface = FindModuleSurface(image, view);
        return surface is { } bounds
            ? Rectangle.FromLTRB(view.Tree!.Value.Right + 4, Math.Max(0, bounds.Top - 24), bounds.Right, bounds.Top)
            : null;
    }

    private static Rectangle? FindModuleSurface(Bitmap image, ConfiguratorView view)
    {
        if (!view.IsConfigurator || view.Tree is not { } tree) return null;
        var seedX = tree.Right + (image.Width - tree.Right) / 2;
        for (var y = Math.Max(30, tree.Top - 60); y < image.Height / 2; y++)
        {
            if (!White(image.GetPixel(seedX, y))) continue;
            var left = seedX;
            var right = seedX;
            while (left > 0 && White(image.GetPixel(left - 1, y))) left--;
            while (right + 1 < image.Width && White(image.GetPixel(right + 1, y))) right++;
            if (left <= tree.Right || right - left < image.Width / 4) continue;
            var bottom = y;
            while (bottom + 1 < image.Height && White(image.GetPixel(right - 3, bottom + 1))) bottom++;
            if (bottom - y < 100) continue;
            // A caret can split the first white scanline; include the folding gutter.
            for (var row = y + 1; row < Math.Min(y + 30, bottom); row++)
            {
                if (!White(image.GetPixel(seedX, row))) continue;
                var rowLeft = seedX;
                while (rowLeft > 0 && White(image.GetPixel(rowLeft - 1, row))) rowLeft--;
                if (rowLeft > tree.Right) left = Math.Min(left, rowLeft);
            }
            return Rectangle.FromLTRB(left, y, right, bottom);
        }
        return null;
    }

    private static bool IsConfigurationHeading(string text)
    {
        const string expected = "КОНФИГУРАЦИЯ";
        var actual = Normalize(text);
        if (actual == expected) return true;
        // Bold dock captions on a grey background lose a few glyphs in Windows OCR.
        // This tolerance is only used inside the dock-heading geometry, after the
        // window title and two separate menu commands have been confirmed.
        if (!actual.StartsWith("КОН") || !actual.EndsWith("ИЯ") ||
            Math.Abs(actual.Length - expected.Length) > 3) return false;
        var previous = Enumerable.Range(0, expected.Length + 1).ToArray();
        for (var i = 1; i <= actual.Length; i++)
        {
            var current = new int[expected.Length + 1];
            current[0] = i;
            for (var j = 1; j <= expected.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (actual[i - 1] == expected[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[expected.Length] <= 3;
    }

    public static IReadOnlyList<Point> FindFoldPluses(Bitmap image, Rectangle editor)
    {
        var result = new List<Point>();
        // Classic 1C folding controls are small outlined squares in the left gutter.
        for (var y = editor.Top + 4; y < editor.Bottom - 5; y++)
        for (var x = Math.Max(5, editor.Left - 32); x < Math.Min(editor.Left + 12, image.Width - 6); x++)
        {
            if (!Dark(image.GetPixel(x, y))) continue;
            for (var radius = 3; radius <= 6; radius++)
            {
                if (x - radius < 0 || x + radius >= image.Width || y - radius < editor.Top || y + radius >= editor.Bottom) continue;
                var cross = Enumerable.Range(-1, 3).All(d => Dark(image.GetPixel(x + d, y)) && Dark(image.GetPixel(x, y + d)));
                var box = Enumerable.Range(-radius, radius * 2 + 1).All(d =>
                    Dark(image.GetPixel(x + d, y - radius)) && Dark(image.GetPixel(x + d, y + radius)) &&
                    Dark(image.GetPixel(x - radius, y + d)) && Dark(image.GetPixel(x + radius, y + d)));
                if (!cross || !box || result.Any(p => Math.Abs(p.X - x) < 8 && Math.Abs(p.Y - y) < 8)) continue;
                result.Add(new Point(x, y));
            }
        }
        return result;
    }

    public static bool HasMissingSource(ConfiguratorView view) =>
        view.IsConfigurator && view.ModuleName != null && view.Editor is { } editor &&
        TreeRows(view.Labels.Where(l => editor.Contains(l.Bounds)).ToArray(), editor)
            .Any(l => Normalize(l.Text) == Normalize("Исходный текст модуля отсутствует"));

    public static bool IsEmpty(Bitmap image, Rectangle editor) =>
        !Enumerable.Range(editor.Top + 3, Math.Max(0, editor.Height - 6)).Any(y =>
            Enumerable.Range(editor.Left + 35, Math.Max(0, editor.Width - 45)).Any(x => !White(image.GetPixel(x, y))));

    private static bool White(Color c) => c.R > 240 && c.G > 240 && c.B > 240;
    private static bool Dark(Color c) => c.R < 180 && c.G < 180 && c.B < 180;
}
