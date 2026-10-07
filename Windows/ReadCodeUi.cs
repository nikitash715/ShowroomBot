using System.Diagnostics;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;

namespace ShowroomBot.Windows;

public sealed class ReadCodeUi(RdpController rdp, KeyboardInputSender keyboard, MouseInputSender mouse,
    WindowScreenshotService screenshots, OneCSectionRecognizer ocr, ScenarioStepDefinition options) : IReadCodeUi
{
    private IntPtr _handle;
    private Rectangle? _tree;
    private int _moduleIndent;
    private string? _module;
    private string _directory = string.Empty;
    private string _path = string.Empty;
    private Size _size;
    private readonly Queue<string> _extensionTabs = new();
    private bool _tabsInitialized;
    private string? _lastAnchor;
    private static readonly string[] ModuleAnchors =
        ["ак", "вс", "зак", "зап", "об", "обе", "пер", "пос", "рас", "ск", "ста", "уп", "фо", "ртк"];
    private bool _openedByAppearance;

    public async Task AttachAsync(CancellationToken token)
    {
        _handle = await rdp.OpenOrActivateAsync(token);
        _directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory,
            "diagnostics", $"read-code-{Guid.NewGuid():N}");
        using var guard = RdpInputGuard.RequireStrict(_handle);
        await RequireConfiguratorAsync(token);
    }
    public async Task OpenCommonModulesAsync(CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        var view = await RequireConfiguratorAsync(token);
        var mainTab = !_tabsInitialized ? OneCConfiguratorRecognizer.FindMainConfigurationTab(view.Labels, _size) : null;
        if (!_tabsInitialized)
        {
            foreach (var tab in OneCConfiguratorRecognizer.FindConfigurationTabs(view.Labels, _size)
                         .Where(t => OneCConfiguratorRecognizer.Normalize(t.Text) != "КОНФИГУРАЦИЯ"))
                _extensionTabs.Enqueue(tab.Text);
            _tabsInitialized = true;
        }
        if (mainTab != null)
        {
            await ClickAsync(mainTab.Bounds, token);
            await Task.Delay(options.PollIntervalMs, token);
            view = await RequireConfiguratorAsync(token);
        }
        if (view.Tree == null)
        {
            throw new InvalidOperationException($"ReadCode: не удалось определить границы дерева метаданных для возврата к корню. Снимок: {_path}");
        }
        _tree = view.Tree;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var seconds = Math.Min(30, options.ScrollTimeoutSeconds);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            var ct = budget.Token;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                view = await RequireConfiguratorAsync(ct);
                var modules = TreeLabel(view, "Общие модули");
                LogTreeSearch(view, "Общие модули", modules, attempt + 1);
                if (modules != null)
                {
                    _moduleIndent = modules.Bounds.Left;
                    if (ModuleRows(view, modules).Length == 0)
                    {
                        await ExpandTreeNodeAsync(modules, ct);
                        for (var poll = 0; poll < 3; poll++)
                        {
                            await Task.Delay(options.PollIntervalMs, ct);
                            view = await RequireConfiguratorAsync(ct);
                            modules = TreeLabel(view, "Общие модули");
                            LogTreeSearch(view, "Общие модули после клика по +", modules, attempt + 1);
                            if (modules != null)
                            {
                                _moduleIndent = modules.Bounds.Left;
                                if (ModuleRows(view, modules).Length > 0) break;
                            }
                        }
                        if (modules == null || ModuleRows(view, modules).Length == 0)
                            throw new InvalidOperationException($"ReadCode: после проверки значка +/− список общих модулей не появился. Снимок: {_path}");
                    }
                    ScenarioExecution.Log($"ReadCode: список общих модулей подтверждён; видимых модулей: {ModuleRows(view, modules).Length}.");
                    return;
                }
                var common = TreeLabel(view, "Общие");
                LogTreeSearch(view, "Общие", common, attempt + 1);
                if (common != null)
                {
                    await ExpandTreeNodeAsync(common, ct);
                }
                else if (attempt == 0)
                {
                    ScenarioExecution.Log("ReadCode: категории не видны; возвращаемся к корню через Home, затем Left/Right.");
                    await FocusTreeAndGoHomeAsync(ct);
                    await keyboard.SendKeyAsync(0x25, ct);
                    await keyboard.SendKeyAsync(0x27, ct);
                }
                else break;
                await Task.Delay(options.PollIntervalMs, ct);
            }
            throw new InvalidOperationException($"ReadCode: не найдены «Общие модули» после возврата к корню и раскрытия категорий. Снимок: {_path}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"ReadCode: истёк лимит навигации к общим модулям ({seconds} с). Снимок: {_path}");
        }
    }

    private async Task ExpandTreeNodeAsync(RecognizedText node, CancellationToken token)
    {
        TreeToggle? toggle;
        using (var image = new Bitmap(_path))
            toggle = OneCConfiguratorRecognizer.FindTreeToggle(image, _tree!.Value, node);
        if (toggle == null)
            throw new InvalidOperationException($"ReadCode: не найден однозначный значок +/− слева от «{node.Text}»; строка {node.Bounds}. Снимок: {_path}");
        if (toggle.IsExpanded)
        {
            ScenarioExecution.Log($"ReadCode: у «{node.Text}» найден − в {toggle.Center}; узел уже раскрыт, проверяем дочерние строки.");
            return;
        }
        ScenarioExecution.Log($"ReadCode: у «{node.Text}» найден + в {toggle.Center}; нажимаем значок раскрытия.");
        await ClickAsync(new Rectangle(toggle.Center.X - 1, toggle.Center.Y - 1, 2, 2), token);
    }

    private RecognizedText[] ModuleRows(ConfiguratorView view, RecognizedText? parent)
    {
        return OneCConfiguratorRecognizer.CommonModuleRows(view.Labels, _tree!.Value, parent, _moduleIndent);
    }

    private void LogTreeSearch(ConfiguratorView view, string target, RecognizedText? found, int attempt)
    {
        var rows = OneCConfiguratorRecognizer.TreeRows(view.Labels, _tree!.Value);
        ScenarioExecution.Log($"ReadCode: поиск «{target}», шаг {attempt}/4, дерево {_tree}; найдено: {found?.Text ?? "нет/неоднозначно"}; " +
            $"строки: {string.Join(" | ", rows.Take(18).Select(r => $"{r.Text} @ {r.Bounds.Left},{r.Bounds.Top}"))}; снимок: {_path}");
    }

    public async Task<bool> TryNextConfigurationAsync(CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        while (_extensionTabs.TryDequeue(out var name))
        {
            token.ThrowIfCancellationRequested();
            var view = await RequireConfiguratorAsync(token);
            var tab = OneCConfiguratorRecognizer.FindConfigurationTabs(view.Labels, _size)
                .FirstOrDefault(t => OneCConfiguratorRecognizer.Normalize(t.Text) == OneCConfiguratorRecognizer.Normalize(name));
            if (tab == null) continue;
            await ClickAsync(tab.Bounds, token);
            await Task.Delay(options.PollIntervalMs, token);
            view = await RequireConfiguratorAsync(token);
            if (view.Tree == null) continue;
            ScenarioExecution.Log($"ReadCode: в основной/предыдущей конфигурации мало кода; пробуем открытое расширение «{name}».");
            _lastAnchor = null;
            await OpenCommonModulesAsync(token);
            return true;
        }
        return false;
    }

    public async Task<string?> OpenRandomModuleAsync(ISet<string> visited, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var seconds = Math.Min(30, options.ScrollTimeoutSeconds);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { return await OpenRandomModuleCoreAsync(visited, budget.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"ReadCode: поиск/открытие общего модуля превысило {seconds} с. Снимок: {_path}");
        }
    }

    private async Task<string?> OpenRandomModuleCoreAsync(ISet<string> visited, CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        var view = await RequireConfiguratorAsync(token);
        var down = Random.Shared.Next(100) < 70;
        var changeAnchor = _lastAnchor == null || Random.Shared.Next(100) < 50;
        string? previous = null;
        for (var attempt = 0; attempt < Math.Min(12, options.MaxScrollAttempts); attempt++)
        {
            view = await RequireConfiguratorAsync(token);
            var parent = TreeLabel(view, "Общие модули");
            var fromY = parent?.Bounds.Bottom ?? _tree!.Value.Top;
            var toY = OneCConfiguratorRecognizer.TreeRows(view.Labels, _tree!.Value).Where(l => l.Bounds.Top >= fromY &&
                    l.Bounds.Left <= _moduleIndent + 5).Select(l => l.Bounds.Top).DefaultIfEmpty(_tree!.Value.Bottom).Min();
            var visibleModules = ModuleRows(view, parent);
            if (changeAnchor && visibleModules.Length > 0)
            {
                changeAnchor = false;
                var anchors = ModuleAnchors.Where(anchor => anchor != _lastAnchor).ToArray();
                _lastAnchor = anchors[Random.Shared.Next(anchors.Length)];
                await ClickAsync(visibleModules[0].Bounds, token);
                await keyboard.SendTextWithoutPausesAsync(_lastAnchor, token);
                ScenarioExecution.Log($"ReadCode: случайный буквенный якорь: {_lastAnchor}.");
                await Task.Delay(options.PollIntervalMs, token);
                continue;
            }
            var candidates = ModuleRows(view, parent).Where(l => !visited.Contains(l.Text) &&
                !l.Text.EndsWith('_')).ToArray();
            if (candidates.Length > 0)
            {
                var candidate = candidates[Random.Shared.Next(candidates.Length)];
                ScenarioExecution.Log($"ReadCode: открываем общий модуль «{candidate.Text}»; кандидатов: {candidates.Length}.");
                var beforeOpening = view;
                _openedByAppearance = false;
                ScenarioExecution.Log($"ReadCode: двойной клик по модулю «{candidate.Text}», строка {candidate.Bounds}, интервал 80 мс.");
                await ClickAsync(candidate.Bounds, token, doubleClick: true);
                _module = candidate.Text;
                string? lastConfirmation = null;
                var opened = await WaitAsync(v =>
                {
                    var matches = OneCConfiguratorRecognizer.MatchesModuleName(v.ModuleName, candidate.Text);
                    var confirmation = $"редактор={v.Editor}, заголовок=«{v.ModuleName ?? "не распознан"}», совпадение={matches}";
                    if (confirmation != lastConfirmation)
                    {
                        ScenarioExecution.Log($"ReadCode: подтверждение открытия «{candidate.Text}»: {confirmation}; снимок: {_path}");
                        lastConfirmation = confirmation;
                    }
                    return OneCConfiguratorRecognizer.ConfirmsModuleOpening(beforeOpening, v, candidate.Text);
                },
                    "редактор общего модуля", token);
                _openedByAppearance = !OneCConfiguratorRecognizer.MatchesModuleName(opened.ModuleName, candidate.Text);
                if (_openedByAppearance)
                    ScenarioExecution.Log($"ReadCode: модуль «{candidate.Text}» подтверждён по изменению редактора/процедур; ненадёжный OCR заголовка: {opened.ModuleName ?? "?"}.");
                ScenarioExecution.Log($"ReadCode: подтверждён модуль из строки дерева «{candidate.Text}».");
                // The tree row is the stable identity, never the OCR caption.
                return candidate.Text;
            }
            if (toY < _tree!.Value.Bottom)
            {
                ScenarioExecution.Log("ReadCode: достигнута следующая категория дерева; поиск модулей завершён без прокрутки за её пределы.");
                down = false;
            }
            var fingerprint = string.Join('|', OneCConfiguratorRecognizer.TreeRows(view.Labels, _tree!.Value).Select(l => OneCConfiguratorRecognizer.Normalize(l.Text)));
            if (fingerprint == previous)
            {
                ScenarioExecution.Log("ReadCode: список модулей не изменился после прокрутки; останавливаем поиск.");
                if (attempt > 1) return null;
                down = !down;
            }
            previous = fingerprint;
            ScenarioExecution.Log($"ReadCode: прокрутка списка модулей {attempt + 1}/{Math.Min(12, options.MaxScrollAttempts)}: видимые модули посещены или не распознаны, следующая категория не видна. Снимок: {_path}");
            if (parent != null && !down) down = true;
            await ScrollTreeAsync(down ? -5 : 5, token);
        }
        throw new TimeoutException("ReadCode: исчерпан лимит поиска общего модуля.");
    }

    public async Task<bool> CanReadModuleAsync(CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        var view = await RequireEditorAsync(token);
        if (OneCConfiguratorRecognizer.HasMissingSource(view)) return false;
        using var image = new Bitmap(_path);
        return !OneCConfiguratorRecognizer.IsEmpty(image, view.Editor!.Value);
    }

    public async Task ReadModuleAsync(TimeSpan duration, CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        using var deadline = new CancellationTokenSource(duration);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var ct = budget.Token;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool? forcedDown = null;
        bool? seekingDown = null;
        var readingPasses = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var view = await RequireEditorAsync(ct);
                var editor = view.Editor!.Value;
                // Use visible declarations only; never extract or type source text.
                var routine = OneCConfiguratorRecognizer.VisibleRoutines(view)
                    .Where(l => !visited.Contains(l.Text))
                    .OrderBy(l => seekingDown == false ? -l.Bounds.Top : l.Bounds.Top).FirstOrDefault();
                Point[] pluses;
                using (var image = new Bitmap(_path))
                    pluses = OneCConfiguratorRecognizer.FindFoldPluses(image, editor).ToArray();
                var nearby = routine == null ? pluses : pluses.Where(p =>
                    p.Y >= routine.Bounds.Top - 4 && p.Y <= routine.Bounds.Bottom + 4).ToArray();
                if (routine != null)
                {
                    visited.Add(routine.Text);
                    seekingDown = null;
                    readingPasses = 0;
                    ScenarioExecution.Log($"ReadCode: просмотр {routine.Text}.");
                    await ClickAsync(routine.Bounds, ct);
                }
                if (nearby.Length > 0)
                {
                    var plus = nearby[Random.Shared.Next(nearby.Length)];
                    await ClickAsync(new Rectangle(plus.X - 1, plus.Y - 1, 2, 2), ct);
                    await Task.Delay(options.PollIntervalMs, ct);
                    view = await RequireEditorAsync(ct);
                    editor = view.Editor!.Value;
                }
                for (var movement = 0; seekingDown == null && movement < 3; movement++)
                {
                    await MoveAsync(new Point(editor.Left + editor.Width * Random.Shared.Next(15, 65) / 100,
                        editor.Top + editor.Height * Random.Shared.Next(15, 80) / 100), ct);
                    await Task.Delay(options.ReadLinePauseMs, ct);
                }
                view = await RequireEditorAsync(ct);
                editor = view.Editor!.Value;
                using var beforeScroll = CaptureCodeImage(editor);
                ct.ThrowIfCancellationRequested();
                if (seekingDown == null && ++readingPasses >= 2)
                    seekingDown = forcedDown ?? Random.Shared.Next(100) < 70;
                var down = forcedDown ?? seekingDown ?? Random.Shared.Next(100) < 70;
                forcedDown = null;
                await MoveAsync(new Point(editor.Left + editor.Width / 2, editor.Top + editor.Height / 2), ct);
                var notches = seekingDown != null ? Math.Max(3, editor.Height / 60) : Random.Shared.Next(3, 7);
                mouse.Scroll(down ? -notches : notches);
                await Task.Delay(options.PollIntervalMs, ct);
                view = await RequireEditorAsync(ct);
                using var afterScroll = CaptureCodeImage(view.Editor!.Value);
                if (CodeImageUnchanged(beforeScroll, afterScroll))
                {
                    forcedDown = !down;
                    if (seekingDown != null) seekingDown = !down;
                    ScenarioExecution.Log($"ReadCode: изображение кода не изменилось; достигнута {(down ? "нижняя" : "верхняя")} граница, следующая прокрутка {(down ? "вверх" : "вниз")}.");
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            // The reading deadline completes the step normally.
        }
        token.ThrowIfCancellationRequested();
    }

    public async Task CloseModuleAsync(CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        await RequireEditorAsync(token);
        await keyboard.SendControlShortcutAsync(0x3E, token); // Ctrl+F4
        ScenarioExecution.Log("ReadCode: чтение завершено; вкладка модуля закрыта через Ctrl+F4.");
    }

    private Bitmap CaptureCodeImage(Rectangle editor)
    {
        using var image = new Bitmap(_path);
        // Exclude the folding gutter, editor borders and scrollbars.
        var code = Rectangle.FromLTRB(editor.Left + 40, editor.Top + 8, editor.Right - 20, editor.Bottom - 20);
        code = Rectangle.Intersect(code, new Rectangle(Point.Empty, image.Size));
        if (code.Width <= 0 || code.Height <= 0)
            throw new InvalidOperationException("ReadCode: не удалось определить область изображения кода.");
        return image.Clone(code, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private static bool CodeImageUnchanged(Bitmap before, Bitmap after)
    {
        if (before.Size != after.Size) return false;
        // Ignore a blinking caret and small rendering noise, but compare the code pixels.
        var allowedChanges = Math.Max(8, (long)before.Width * before.Height / 1000);
        long changes = 0;
        for (var y = 0; y < before.Height; y++)
        for (var x = 0; x < before.Width; x++)
        {
            var a = before.GetPixel(x, y);
            var b = after.GetPixel(x, y);
            if ((Math.Abs(a.R - b.R) > 12 || Math.Abs(a.G - b.G) > 12 || Math.Abs(a.B - b.B) > 12) &&
                ++changes > allowedChanges) return false;
        }
        return true;
    }

    private async Task FocusTreeAndGoHomeAsync(CancellationToken token)
    {
        if (_tree is not { } tree) throw new InvalidOperationException("ReadCode: дерево не распознано.");
        var view = await RequireConfiguratorAsync(token);
        var row = OneCConfiguratorRecognizer.FindTreeFocusRow(view.Labels, tree)
            ?? throw new InvalidOperationException("ReadCode: в дереве не распознано ни одной строки для фокусировки.");
        await ClickAsync(row.Bounds, token);
        await keyboard.SendKeyAsync(0x24, token);
        await Task.Delay(options.PollIntervalMs, token);
    }

    private async Task ScrollTreeAsync(int notches, CancellationToken token)
    {
        await RequireConfiguratorAsync(token);
        if (_tree is not { } tree) throw new InvalidOperationException("ReadCode: нет дерева конфигурации.");
        await MoveAsync(new Point(tree.Left + tree.Width / 2, tree.Top + tree.Height / 2), token);
        mouse.Scroll(notches);
        await Task.Delay(options.PollIntervalMs, token);
    }

    private async Task<ConfiguratorView> CaptureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RdpController.EnsureSessionForeground(_handle);
        _path = screenshots.CaptureClientArea(_handle, _directory);
        var labels = await ocr.ReadConfiguratorLinesAsync(_path, token);
        RdpController.EnsureSessionForeground(_handle);
        using var image = new Bitmap(_path);
        if (_size != Size.Empty && image.Size != _size) throw new InvalidOperationException("ReadCode: размер RDP изменился.");
        _size = image.Size;
        var view = OneCConfiguratorRecognizer.Analyze(image, labels);
        if (view.Tree is { } tree)
        {
            var treeLabels = await ocr.ReadTreeLinesAsync(_path, tree, token);
            // Do not merge OCR from different image scales inside the tree:
            // skew correction can move their rows and create false duplicates.
            view = view with { Labels = labels.Where(l => !tree.IntersectsWith(l.Bounds)).Concat(treeLabels).ToArray() };
        }
        return view;
    }

    private async Task<ConfiguratorView> RequireConfiguratorAsync(CancellationToken token)
    {
        var view = await CaptureAsync(token);
        if (!view.IsConfigurator) throw new InvalidOperationException("ReadCode: активный конфигуратор не подтверждён; ввод остановлен.");
        return view;
    }

    private async Task<ConfiguratorView> RequireEditorAsync(CancellationToken token)
    {
        var view = await RequireConfiguratorAsync(token);
        if (view.Editor == null || !EditorIdentityConfirmed(view))
            throw new InvalidOperationException("ReadCode: редактор выбранного общего модуля не подтверждён.");
        return view;
    }

    private bool EditorIdentityConfirmed(ConfiguratorView view) => SameModule(view.ModuleName) ||
        (_openedByAppearance && view.Editor != null);

    private bool SameModule(string? name) => name != null &&
        _module != null && OneCConfiguratorRecognizer.MatchesModuleName(name, _module);

    private async Task<ConfiguratorView> WaitAsync(Func<ConfiguratorView, bool> condition, string what,
        CancellationToken token, bool requireConfigurator = true)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(options.ReadyTimeoutSeconds))
        {
            var view = await CaptureAsync(token);
            if ((!requireConfigurator || view.IsConfigurator) && condition(view)) return view;
            await Task.Delay(options.PollIntervalMs, token);
        }
        throw new TimeoutException($"ReadCode: не дождались: {what}.");
    }

    private RecognizedText? TreeLabel(ConfiguratorView view, string text) =>
        OneCConfiguratorRecognizer.FindTreeNode(view.Labels, _tree!.Value, text);
    private static bool Has(ConfiguratorView view, string text) => view.Labels.Any(l =>
        OneCConfiguratorRecognizer.Normalize(l.Text) == OneCConfiguratorRecognizer.Normalize(text));

    private async Task ClickLabelAsync(ConfiguratorView view, string text, Func<RecognizedText, bool> filter, CancellationToken token)
    {
        var matches = view.Labels.Where(l => filter(l) &&
            OneCConfiguratorRecognizer.Normalize(l.Text) == OneCConfiguratorRecognizer.Normalize(text))
            .OrderByDescending(l => l.Bounds.Width).ToArray();
        if (matches.Length == 0 || matches.Any(l => !l.Bounds.IntersectsWith(matches[0].Bounds)))
            throw new InvalidOperationException($"ReadCode: неоднозначная команда «{text}».");
        await ClickAsync(matches[0].Bounds, token);
    }

    private async Task ClickAsync(Rectangle target, CancellationToken token, bool doubleClick = false)
    {
        await MoveAsync(new Point(target.Left + target.Width / 2, target.Top + target.Height / 2), token);
        token.ThrowIfCancellationRequested();
        mouse.ClickLeft();
        if (doubleClick) { await Task.Delay(80, token); mouse.ClickLeft(); }
    }

    private async Task MoveAsync(Point point, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!new Rectangle(Point.Empty, _size).Contains(point)) throw new InvalidOperationException("ReadCode: точка вне снимка RDP.");
        RdpController.EnsureSessionForeground(_handle);
        await mouse.MoveToAsync(screenshots.ClientToScreen(_handle, point), token);
    }

    private static Rectangle Inset(Rectangle bounds) => new(bounds.Left + 40, bounds.Top + 8, 10, 10);
}
