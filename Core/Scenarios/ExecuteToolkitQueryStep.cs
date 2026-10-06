using System.Text;
using ShowroomBot.Configuration;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class ExecuteToolkitQueryStep(ScenarioStepDefinition definition, RdpController rdp,
    KeyboardInputSender keyboard, MouseInputSender mouse, WindowScreenshotService screenshots,
    OneCSectionRecognizer sectionRecognizer) : IScenarioStep
{
    public string Name => $"Выполнить запрос Toolkit: {Path.GetFileNameWithoutExtension(definition.QueryFile)}";
    private readonly ToolkitConsoleRecognizer _recognizer = new(sectionRecognizer);
    private string _directory = string.Empty;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Validate the file before activating RDP or sending any native input.
        var query = await ReadQueryAsync(cancellationToken);
        Validate();
        _directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory,
            "diagnostics", $"toolkit-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            ScenarioExecution.Current?.Token ?? CancellationToken.None);
        var token = linked.Token;
        try
        {
            // Open1C is a separate scenario step; use the client it left active in RDP.
            var (_, layout) = await Observe(token);
            if (layout.Editor != null && layout.Execute != null)
                ScenarioExecution.Log("Toolkit: редактор уже открыт; переключение вкладки не требуется.");
            else if (layout.ConsoleTab is Rectangle tab)
                await Click(tab, token);
            else
            {
                // The checked-in console-opening scenario uses this exact command.
                var navigation = new ScenarioStepDefinition
                {
                    Section = "Infostart Toolkit", Command = "Консоль запросов",
                    SectionOpenTimeoutSeconds = definition.SectionOpenTimeoutSeconds,
                    CommandTimeoutSeconds = definition.CommandTimeoutSeconds, PollIntervalMs = definition.PollIntervalMs
                };
                await new OpenOneCCommandStep(navigation, rdp, mouse, screenshots, sectionRecognizer, keyboard).ExecuteAsync(token);
            }
            layout = await WaitForEditor(token);
            await Click(layout.TextTab!.Value, token);
            // Re-read after switching tabs; never use coordinates of a hidden editor.
            layout = await WaitForEditor(token);
            await Click(layout.Editor!.Value, token);
            token.ThrowIfCancellationRequested();
            await Task.Delay(200, token);
            ScenarioExecution.Log($"Toolkit: ввод файла {definition.QueryFile}; режим {definition.QueryInputMode}; редактор {layout.Editor}.");
            if (string.Equals(definition.QueryInputMode, "paste", StringComparison.OrdinalIgnoreCase))
                await new RdpClipboardQueryInput(keyboard).ReplaceAndVerifyAsync(query, definition.QueryInputTimeoutSeconds, token);
            else
                await new RdpKeyboardQueryInput(keyboard).ReplaceAsync(query, definition.QueryInputTimeoutSeconds, token);
            ToolkitConsoleLayout result;
            try
            {
                result = await ExecuteQuery(layout, token);
            }
            catch (ToolkitQueryExecutionException exception) when (
                string.Equals(definition.QueryInputMode, "typing", StringComparison.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                ScenarioExecution.Log($"Toolkit: после typing получена ошибка: {exception.Message}; очищаем редактор и повторяем через paste.");
                layout = await WaitForEditor(token);
                await Click(layout.TextTab!.Value, token);
                layout = await WaitForEditor(token);
                await Click(layout.Editor!.Value, token);
                await Task.Delay(200, token);
                await keyboard.SendControlShortcutAsync(0x1E, token); // Ctrl+A
                await keyboard.SendKeyAsync(0x08, token); // Backspace
                await Task.Delay(150, token);
                await new RdpClipboardQueryInput(keyboard).ReplaceAndVerifyAsync(query,
                    definition.QueryInputTimeoutSeconds, token);
                // One retry only; a query error after paste is propagated normally.
                result = await ExecuteQuery(layout, token);
            }
            if (result.RowCount == 0)
            {
                ScenarioExecution.Log("Toolkit: запрос выполнен успешно, результат пуст.");
                return;
            }
            await ScrollResult(result, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            try { screenshots.CaptureDesktop(_directory); } catch { /* Preserve original failure. */ }
            ScenarioExecution.Log($"Toolkit: {exception.Message}; диагностика: {_directory}");
            throw new InvalidOperationException($"{exception.Message} Диагностика: {_directory}", exception);
        }
    }

    private sealed class ToolkitQueryExecutionException(string message) : InvalidOperationException(message);

    private async Task<ToolkitConsoleLayout> ExecuteQuery(ToolkitConsoleLayout layout, CancellationToken token)
    {
        await Move(Center(layout.TextTab!.Value), token);
        var (beforePath, before) = await Observe(token);
        if (before.Execute == null)
            throw new InvalidOperationException("Toolkit: верхняя кнопка «Выполнить» не распознана после ввода запроса.");
        var baseline = before.Result is Rectangle old ? ToolkitConsoleRecognizer.Fingerprint(beforePath, old) : null;
        await Click(before.Execute.Value, token);
        ScenarioExecution.Log($"Toolkit: запуск {definition.QueryFile}; ожидание нового результата");
        return await WaitForResult(before, baseline, token);
    }

    private async Task<string> ReadQueryAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definition.QueryFile))
            throw new InvalidOperationException("ExecuteToolkitQuery: задайте queryFile.");
        var path = Path.GetFullPath(definition.QueryFile, AppContext.BaseDirectory);
        if (!File.Exists(path)) throw new FileNotFoundException(
            $"Toolkit: файл запроса не найден: {path}. Относительный queryFile разрешается от каталога программы: {AppContext.BaseDirectory}", path);
        var query = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), token);
        if (string.IsNullOrWhiteSpace(query)) throw new InvalidDataException($"Toolkit: файл запроса пуст: {path}");
        return query.TrimEnd('\r', '\n');
    }

    private void Validate()
    {
        if (definition.ScrollSpeed is < 1 or > 10)
            throw new InvalidOperationException("Toolkit: scrollSpeed должен быть целым числом от 1 до 10.");
        if (!string.Equals(definition.QueryInputMode, "typing", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(definition.QueryInputMode, "paste", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Toolkit: queryInputMode должен быть typing (набор) или paste (вставка).");
        if (definition.ConsoleTimeoutSeconds <= 0 || definition.QueryInputTimeoutSeconds <= 0 || definition.QueryTimeoutSeconds <= 0 ||
            definition.ScrollTimeoutSeconds <= 0 || definition.ScrollPauseMs <= 0 || definition.ScrollNotches is < 1 or > 10 ||
            definition.MaxScrollAttempts <= 0 || definition.ScrollUnchangedAttempts < 2 ||
            definition.ResultStablePolls < 2 || definition.PollIntervalMs <= 0)
            throw new InvalidOperationException("Toolkit: таймауты, паузы и лимиты должны быть положительными; scrollNotches — 1..10; stable/unchanged — минимум 2.");
    }

    private async Task<ToolkitConsoleLayout> WaitForEditor(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(definition.ConsoleTimeoutSeconds));
        try
        {
            while (true)
            {
                var (_, layout) = await Observe(timeout.Token);
                // A reused editor may still display an error from the previous text.
                // Replacing it is allowed; current execution errors are checked later.
                if (layout.ConsoleTab != null && layout.TextTab != null)
                {
                    if (layout.Editor != null && layout.Execute != null) return layout;
                    await Click(layout.TextTab.Value, timeout.Token);
                }
                await Task.Delay(definition.PollIntervalMs, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Toolkit: не найдены консоль, вкладка «Текст», редактор или верхняя кнопка «Выполнить»."); }
    }

    private async Task<ToolkitConsoleLayout> WaitForResult(ToolkitConsoleLayout before, string? baseline, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(definition.QueryTimeoutSeconds));
        var progress = new ToolkitExecutionProgress(before, baseline, definition.ResultStablePolls);
        try
        {
            while (true)
            {
                var (path, layout) = await Observe(timeout.Token);
                var fingerprint = layout.Result is Rectangle area ? ToolkitConsoleRecognizer.Fingerprint(path, area) : null;
                if (progress.Observe(layout, fingerprint)) return layout;
                if (progress.HasConfirmedError)
                    throw new ToolkitQueryExecutionException($"Toolkit: ошибка запроса: {layout.Error}");
                await Task.Delay(definition.PollIntervalMs, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Toolkit: не подтверждено завершение нового запроса. Прежний результат без изменения или признака выполнения не принимается; проверьте сообщения 1С."); }
    }

    private async Task ScrollResult(ToolkitConsoleLayout layout, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(definition.ScrollTimeoutSeconds));
        var unchanged = 0;
        var moved = false;
        ScenarioExecution.Log($"Toolkit: скорость прокрутки {definition.ScrollSpeed}; " +
            $"делений {definition.ScrollNotches}, пауза {definition.ScrollPauseMs} мс; " +
            $"лимиты: {definition.MaxScrollAttempts} попыток, {definition.ScrollTimeoutSeconds} с.");
        try
        {
            var initialArea = layout.Result ?? throw new InvalidOperationException("Toolkit: нижняя таблица результата исчезла.");
            await Click(initialArea, timeout.Token);
            // Focus once. Repeated clicks change the selected row after every scroll
            // and make a stationary table look different in screenshot comparisons.
            // Escape closes the modified 1C console and opens a save confirmation.
            // Clicking the result is sufficient to focus it for wheel scrolling.
            for (var attempt = 0; attempt < definition.MaxScrollAttempts; attempt++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var area = layout.Result ?? throw new InvalidOperationException("Toolkit: нижняя таблица результата исчезла.");
                var point = new Point(area.Left + area.Width * 3 / 5, area.Top + area.Height / 2);
                var screenPoint = screenshots.ClientToScreen(Activate(timeout.Token), point);
                if (!NativeMethods.GetCursorPos(out var cursor) || cursor.X != screenPoint.X || cursor.Y != screenPoint.Y)
                {
                    await mouse.MoveToAsync(screenPoint, timeout.Token);
                    // Allow hover effects to settle only when the pointer actually moved.
                    await Task.Delay(Math.Min(200, definition.ScrollPauseMs), timeout.Token);
                }
                var beforePath = Capture(timeout.Token);
                if (ToolkitConsoleRecognizer.ScrollbarAtBottom(beforePath, area))
                {
                    ScenarioExecution.Log("Toolkit: достигнут конец таблицы результата.");
                    return;
                }
                var baseline = ToolkitConsoleRecognizer.Fingerprint(beforePath, area, point);
                // Avoid Ctrl+wheel zoom if a remote modifier release was delayed.
                await keyboard.ReleaseControlAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                mouse.Scroll(-definition.ScrollNotches);
                await Task.Delay(definition.ScrollPauseMs, timeout.Token);
                var (path, next) = await Observe(timeout.Token);
                if (next.Error != null) throw new InvalidOperationException($"Toolkit: {next.Error}");
                if (next.Result == null) throw new InvalidOperationException("Toolkit: область результата потеряна при прокрутке.");
                if (ToolkitConsoleRecognizer.ScrollbarAtBottom(path, next.Result.Value))
                {
                    ScenarioExecution.Log("Toolkit: достигнут конец таблицы результата.");
                    return;
                }
                var same = next.Result == area && baseline == ToolkitConsoleRecognizer.Fingerprint(path, next.Result.Value, point);
                moved |= !same;
                unchanged = same ? unchanged + 1 : 0;
                ScenarioExecution.Log($"Toolkit: прокрутка {attempt + 1}; без изменений {unchanged}; область {next.Result}");
                if (unchanged >= definition.ScrollUnchangedAttempts)
                {
                    // A stationary result with many rows may mean the wheel was not delivered.
                    if (!moved && !ToolkitConsoleRecognizer.ScrollbarAtBottom(path, next.Result.Value) &&
                        next.RowCount > Math.Max(1, area.Height / 8))
                        throw new InvalidOperationException("Toolkit: таблица не реагирует на колесо; конец прокрутки не подтверждён.");
                    return;
                }
                layout = next;
            }
            token.ThrowIfCancellationRequested();
            ScenarioExecution.Log($"Toolkit: достигнут maxScrollAttempts ({definition.MaxScrollAttempts}); конец таблицы не подтверждён. Запрос выполнен успешно, шаг завершён с неполной прокруткой.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        {
            ScenarioExecution.Log($"Toolkit: превышен scrollTimeoutSeconds ({definition.ScrollTimeoutSeconds} с); конец таблицы не подтверждён. Запрос выполнен успешно, шаг завершён с неполной прокруткой.");
        }
    }

    private IntPtr Activate(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ScenarioExecution.CheckCancellation();
        if (!rdp.TryActivateExistingWindow(out var window)) throw new InvalidOperationException("Toolkit: не удалось активировать RDP.");
        return window;
    }
    private string Capture(CancellationToken token) => screenshots.CaptureClientArea(Activate(token), _directory);
    private async Task<(string Path, ToolkitConsoleLayout Layout)> Observe(CancellationToken token)
    {
        var path = Capture(token);
        var layout = await _recognizer.RecognizeAsync(path, token);
        ScenarioExecution.Log($"Toolkit: распознано: вкладка={layout.ConsoleTab}, текст={layout.TextTab}, " +
            $"редактор={layout.Editor}, выполнить={layout.Execute}, результат={layout.Result}, строки={layout.RowCount}, " +
            $"busy={layout.Busy}, ошибка={layout.Error}; screenshot: {path}");
        return (path, layout);
    }
    private static Point Center(Rectangle region) => new(region.Left + region.Width / 2, region.Top + region.Height / 2);
    private async Task Move(Point point, CancellationToken token) => await mouse.MoveToAsync(
        screenshots.ClientToScreen(Activate(token), point),
        token);
    private async Task Click(Rectangle region, CancellationToken token)
    {
        await Move(Center(region), token);
        token.ThrowIfCancellationRequested();
        mouse.ClickLeft();
    }
}
