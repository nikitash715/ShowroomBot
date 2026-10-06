using System.Diagnostics;
using ShowroomBot.Configuration;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class OpenOneCCommandStep : IScenarioStep
{
    private readonly ScenarioStepDefinition _definition;
    private readonly RdpController _rdp;
    private readonly MouseInputSender _mouse;
    private readonly KeyboardInputSender _keyboard;
    private readonly WindowScreenshotService _screenshots;
    private readonly OneCSectionRecognizer _recognizer;

    public OpenOneCCommandStep(ScenarioStepDefinition definition, RdpController rdp,
        MouseInputSender mouse, WindowScreenshotService screenshots,
        OneCSectionRecognizer recognizer, KeyboardInputSender keyboard)
    {
        _definition = definition;
        _rdp = rdp;
        _mouse = mouse;
        _screenshots = screenshots;
        _recognizer = recognizer;
        _keyboard = keyboard;
    }

    public string Name => $"Открыть команду 1С: {_definition.Command}";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_definition.Section) || string.IsNullOrWhiteSpace(_definition.Command))
            throw new InvalidOperationException("Open1CCommand: необходимо задать section и command.");
        if (_definition.CommandTimeoutSeconds <= 0 || _definition.PollIntervalMs <= 0 ||
            _definition.SectionOpenTimeoutSeconds is <= 0 or > 5)
            throw new InvalidOperationException("Open1CCommand: sectionOpenTimeoutSeconds должен быть 1..5; commandTimeoutSeconds и pollIntervalMs — положительными.");

        try
        {
            await CloseBlockingDialogsAsync(cancellationToken);
            await OpenFromMenuAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            !string.IsNullOrWhiteSpace(_definition.FallbackLink) &&
            exception is InvalidOperationException or TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScenarioExecution.Log($"Открытие через меню не удалось: {exception.Message}; открываем через Shift+F11.");
            await CloseBlockingDialogsAsync(cancellationToken);
            Activate(cancellationToken);
            await _keyboard.SendOpenLinkAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);
            Activate(cancellationToken);
            await _keyboard.SendControlShortcutAsync(0x1E, cancellationToken);
            await _keyboard.SendTextAsync(_definition.FallbackLink, cancellationToken);
            await SubmitLinkAsync(cancellationToken);
        }
    }

    private async Task CloseBlockingDialogsAsync(CancellationToken token)
    {
        var directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Activate(token);
        await _keyboard.SendKeyAsync(0x1B, token);
        ScenarioExecution.Log("Escape перед выполнением команды: закрытие текущей формы или всплывающего окна.");
        await Task.Delay(500, token);
        var clearFrames = 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var path = _screenshots.CaptureClientArea(Activate(token), directory);
            var lines = await _recognizer.ReadLinesAsync(path, token);
            if (FindDismissButton(lines) is Rectangle button)
            {
                clearFrames = 0;
                var point = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
                await _mouse.MoveToAsync(_screenshots.ClientToScreen(Activate(token), point), token);
                token.ThrowIfCancellationRequested();
                _mouse.ClickLeft();
                ScenarioExecution.Log("Закрытие вопроса после Escape: «Не сохранять» / «Нет» / «ОК».");
            }
            else if (HasLinkDialog(lines))
            {
                clearFrames = 0;
                Activate(token);
                await _keyboard.SendKeyAsync(0x1B, token);
            }
            else if (HasSaveQuestion(lines))
                throw new InvalidOperationException("Open1CCommand: вопрос о сохранении найден, но кнопка отказа не распознана однозначно.");
            else if (++clearFrames >= 2) return;
            await Task.Delay(500, token);
        }
        throw new InvalidOperationException("Open1CCommand: не удалось закрыть всплывающее окно перед выполнением команды.");
    }

    private static string DialogText(string text) => text.Trim().Replace("&", "").TrimEnd('.', '?', ':', '!').ToLowerInvariant();

    public static bool HasSaveQuestion(IReadOnlyList<RecognizedText> lines) =>
        lines.Any(line => DialogText(line.Text).Contains("сохранить") &&
            (line.Text.Contains('?') || DialogText(line.Text).Contains("изменени"))) ||
        lines.Any(line => DialogText(line.Text) == "не сохранять");

    public static Rectangle? FindDismissButton(IReadOnlyList<RecognizedText> lines)
    {
        Rectangle? Unique(params string[] labels)
        {
            var matches = lines.Where(line => labels.Contains(DialogText(line.Text)))
                .Select(line => line.Bounds).Distinct().ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        if (lines.Any(line => DialogText(line.Text) == "не сохранять"))
            return Unique("не сохранять");
        // Never accept a save question with OK/Yes: explicitly discard changes.
        if (HasSaveQuestion(lines)) return Unique("нет");
        return Unique("ок", "ok", "оk", "oк");
    }

    public static bool HasLinkDialog(IReadOnlyList<RecognizedText> lines) =>
        lines.Any(line => line.Text.Trim().Equals("Переход по ссылке", StringComparison.OrdinalIgnoreCase));

    private async Task SubmitLinkAsync(CancellationToken token)
    {
        var directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory, "diagnostics");
        var timer = Stopwatch.StartNew();
        var clicked = false;
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            var path = _screenshots.CaptureClientArea(Activate(token), directory);
            var lines = await _recognizer.ReadLinesAsync(path, token);
            var dialog = HasLinkDialog(lines);
            if (clicked && !dialog)
            {
                ScenarioExecution.Log($"Диалог перехода по ссылке закрыт: {_definition.Command}");
                return;
            }
            if (!clicked && FindLinkGoButton(lines) is Rectangle button)
            {
                var point = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
                await _mouse.MoveToAsync(_screenshots.ClientToScreen(Activate(token), point), token);
                token.ThrowIfCancellationRequested();
                _mouse.ClickLeft();
                clicked = true;
                ScenarioExecution.Log("Клик по кнопке «Перейти» в диалоге перехода по ссылке.");
            }
            await Task.Delay(300, token);
        }
        throw new TimeoutException(clicked
            ? "Open1CCommand: после клика «Перейти» диалог перехода по ссылке не закрылся."
            : "Open1CCommand: кнопка «Перейти» в диалоге перехода по ссылке не найдена.");
    }

    public static Rectangle? FindLinkGoButton(IReadOnlyList<RecognizedText> lines)
    {
        var titles = lines.Where(line => line.Text.Trim().Equals("Переход по ссылке", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (titles.Length != 1) return null;
        var title = titles[0].Bounds;
        var buttons = lines.Where(line => line.Text.Trim().Equals("Перейти", StringComparison.OrdinalIgnoreCase) &&
            line.Bounds.Top > title.Bottom && line.Bounds.Top - title.Bottom < 250 &&
            line.Bounds.Left >= title.Left && line.Bounds.Left - title.Left < 250).Select(line => line.Bounds).Distinct().ToArray();
        // ReadLinesAsync includes both complete lines and individual words.
        return buttons.Length == 1 ? buttons[0] : null;
    }

    private async Task OpenFromMenuAsync(CancellationToken cancellationToken)
    {
        // The existing navigation always clicks the recognized section, including an already open one.
        var sectionStep = new OpenOneCSectionStep(_definition, _rdp, _mouse, _screenshots, _recognizer);
        await sectionStep.ExecuteAsync(cancellationToken);

        var directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory, "diagnostics",
            $"open1c-command-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        await WaitForSectionAsync(directory, sectionStep.BeforeClickScreenshotPath, cancellationToken);

        var timer = Stopwatch.StartNew();
        ScenarioExecution.Log($"Начало поиска команды: {_definition.Command}");
        using var searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        searchCancellation.CancelAfter(TimeSpan.FromSeconds(_definition.CommandTimeoutSeconds));
        var searchToken = searchCancellation.Token;
        try
        {
            while (true)
            {
                searchToken.ThrowIfCancellationRequested();
                var path = _screenshots.CaptureClientArea(Activate(searchToken), directory);
                try
                {
                    var result = await _recognizer.RecognizeCommandAsync(path,
                        _definition.Command, searchToken);
                    searchToken.ThrowIfCancellationRequested();
                    // The unique command itself is sufficient; no duplicated section heading is required.
                    if (result.Command is Rectangle text)
                    {
                        if (!result.Workspace.Contains(text))
                            throw new InvalidOperationException("Команда выходит за границы рабочей области; клик отменён.");
                        ScenarioExecution.Log($"Команда найдена: {_definition.Command}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
                        var point = new Point(text.Left + text.Width / 2, text.Top + text.Height / 2);
                        await _mouse.MoveToAsync(_screenshots.ClientToScreen(Activate(searchToken), point), searchToken);
                        searchToken.ThrowIfCancellationRequested();
                        _mouse.ClickLeft();
                        ScenarioExecution.Log($"Клик по команде: {_definition.Command}");
                        return;
                    }
                }
                catch (SectionPanelNotFoundException)
                {
                    // No click is allowed while the workspace boundary is unknown.
                }
                await Task.Delay(_definition.PollIntervalMs, searchToken);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            ScenarioExecution.Log($"Таймаут поиска команды: {_definition.Command}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw new TimeoutException($"Open1CCommand: команда «{_definition.Command}» не найдена за " +
                $"{_definition.CommandTimeoutSeconds} секунд. Диагностика: {directory}", exception);
        }
        catch
        {
            ScenarioExecution.Log($"Поиск команды прерван: {_definition.Command}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw;
        }
    }

    private async Task WaitForSectionAsync(string directory, string? beforeClickPath, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        ScenarioExecution.Log($"Ожидание перерисовки раздела: максимум {_definition.SectionOpenTimeoutSeconds} с");
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waiting.CancelAfter(TimeSpan.FromSeconds(_definition.SectionOpenTimeoutSeconds));
        var token = waiting.Token;
        using var beforeClick = beforeClickPath is null ? null : new Bitmap(beforeClickPath);
        var changed = beforeClick is null;
        Bitmap? previous = null;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var path = _screenshots.CaptureClientArea(Activate(token), directory);
                using var screenshot = new Bitmap(path);
                try
                {
                    var panel = OneCSectionRecognizer.DetectPanel(screenshot, token);
                    var workspace = Rectangle.FromLTRB(panel.Right + 4, 0, screenshot.Width, screenshot.Height);
                    using var current = screenshot.Clone(workspace, screenshot.PixelFormat);
                    if (!changed && beforeClick is not null)
                    {
                        if (beforeClick.Size != screenshot.Size) changed = true;
                        else
                        {
                            using var baseline = beforeClick.Clone(workspace, beforeClick.PixelFormat);
                            changed = !OneCBaseRecognizer.SameWindow(baseline, current);
                        }
                    }
                    // Do not mistake two unchanged frames of the previous section for a completed transition.
                    if (changed && previous is not null && OneCBaseRecognizer.SameWindow(previous, current))
                    {
                        ScenarioExecution.Log($"Рабочая область стабилизировалась; ожидание {timer.Elapsed.TotalSeconds:F3} с");
                        return;
                    }
                    previous?.Dispose();
                    previous = new Bitmap(current);
                }
                catch (SectionPanelNotFoundException)
                {
                    previous?.Dispose();
                    previous = null;
                }
                await Task.Delay(200, token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Five seconds is a hand-off deadline, not a requirement for OCR of the heading.
            ScenarioExecution.Log($"Лимит ожидания раздела достигнут; затрачено {timer.Elapsed.TotalSeconds:F3} с; переходим к поиску команды");
        }
        finally { previous?.Dispose(); }
    }

    private IntPtr Activate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_rdp.TryActivateExistingWindow(out var window))
            throw new InvalidOperationException("Не удалось активировать окно RDP.");
        return window;
    }
}
