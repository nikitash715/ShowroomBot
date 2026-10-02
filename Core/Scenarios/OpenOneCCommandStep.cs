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
    private readonly WindowScreenshotService _screenshots;
    private readonly OneCSectionRecognizer _recognizer;
    private readonly MouseSettings _mouseSettings;

    public OpenOneCCommandStep(ScenarioStepDefinition definition, RdpController rdp,
        MouseInputSender mouse, WindowScreenshotService screenshots,
        OneCSectionRecognizer recognizer, MouseSettings mouseSettings)
    {
        _definition = definition;
        _rdp = rdp;
        _mouse = mouse;
        _screenshots = screenshots;
        _recognizer = recognizer;
        _mouseSettings = mouseSettings;
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

        // The existing navigation always clicks the recognized section, including an already open one.
        var sectionStep = new OpenOneCSectionStep(_definition, _rdp, _mouse, _screenshots, _recognizer, _mouseSettings);
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
                        var delay = _mouseSettings.StepDelayMilliseconds > 0 ? _mouseSettings.StepDelayMilliseconds : 10;
                        var duration = _mouseSettings.MovementDurationMilliseconds > 0
                            ? _mouseSettings.MovementDurationMilliseconds : 300;
                        await _mouse.MoveToAsync(_screenshots.ClientToScreen(Activate(searchToken), point),
                            TimeSpan.FromMilliseconds(Math.Max(duration, delay * 2L)),
                            TimeSpan.FromMilliseconds(delay), searchToken);
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
