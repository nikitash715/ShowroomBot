using System.Text;
using System.Diagnostics;
using System.Globalization;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class OpenOneCBaseStep : IScenarioStep
{
    private readonly ScenarioStepDefinition _definition;
    private readonly RdpController _rdpController;
    private readonly KeyboardInputSender _keyboardInputSender;
    private readonly WindowScreenshotService _screenshots;

    public OpenOneCBaseStep(
        ScenarioStepDefinition definition,
        RdpController rdpController,
        KeyboardInputSender keyboardInputSender,
        WindowScreenshotService screenshots)
    {
        _definition = definition;
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
        _screenshots = screenshots;
    }

    public string Name => "Открыть базу 1С";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDefinition();

        var windowHandle = await _rdpController.OpenOrActivateAsync(cancellationToken);

        await DelayAsync(_definition.AfterActivationDelayMs, cancellationToken);
        var color = ParsePanelColor();
        var searchTimer = Stopwatch.StartNew();
        ScenarioExecution.Log("Начало поиска открытой базы 1С по цвету панели");
        try
        {
            var found = await FindExistingAsync(windowHandle, color, cancellationToken);
            ScenarioExecution.Log($"Поиск открытой базы: {(found ? "найдена" : "не найдена")}; затрачено {searchTimer.Elapsed.TotalSeconds:F3} с");
            if (found)
            {
                ScenarioExecution.Log("Open1C: reused existing");
                return;
            }
        }
        catch
        {
            ScenarioExecution.Log($"Поиск открытой базы прерван; затрачено {searchTimer.Elapsed.TotalSeconds:F3} с");
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        _keyboardInputSender.SendWindowsRun();
        await DelayAsync(_definition.AfterRunDialogDelayMs, cancellationToken);

        await _keyboardInputSender.SendTextAsync(
            BuildCommand(),
            cancellationToken);
        _keyboardInputSender.SendEnter();
        ScenarioExecution.Log("Open1C: launched new; команда запуска отправлена, ожидаем подтверждение интерфейса");
        var timer = Stopwatch.StartNew();
        ScenarioExecution.Log("Начало ожидания интерфейса запущенной базы 1С");
        var timeout = TimeSpan.FromSeconds(_definition.ReadyTimeoutSeconds);
        while (timer.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = _screenshots.CaptureClientArea(windowHandle,
                ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory, "diagnostics", "open1c-ready"));
            var recognition = await OneCBaseRecognizer.RecognizeWindowAsync(path, color, _definition.ColorTolerance, cancellationToken);
            if (recognition.IsTargetClient)
            {
                ScenarioExecution.Log($"Интерфейс базы найден; затрачено {timer.Elapsed.TotalSeconds:F3} с");
                return;
            }
            var remaining = timeout - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(_definition.PollIntervalMs, remaining.TotalMilliseconds)), cancellationToken);
        }
        ScenarioExecution.Log($"Таймаут ожидания базы; затрачено {timer.Elapsed.TotalSeconds:F3} с");
        throw new TimeoutException($"Open1C: база '{_definition.Database}' с цветом {_definition.PanelColor} не появилась за {_definition.ReadyTimeoutSeconds} секунд.");
    }

    private Bitmap Capture(IntPtr handle)
    {
        var path = _screenshots.CaptureClientArea(handle, Path.Combine(Path.GetTempPath(), "ShowroomBot", "Open1C"));
        try
        {
            using var image = new Bitmap(path);
            return new Bitmap(image);
        }
        finally { File.Delete(path); }
    }

    private async Task<bool> FindExistingAsync(IntPtr handle, Color color, CancellationToken token)
    {
        var directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory,
            "diagnostics", $"open1c-windows-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        for (var index = 0; index < _definition.MaxWindowsToCheck; index++)
        {
            token.ThrowIfCancellationRequested();
            EnsureRdpForeground(handle);
            if (index > 0)
            {
                // Each selection moves the selected window to the MRU front. Incrementing
                // the index visits the next window instead of alternating between two windows.
                _keyboardInputSender.SelectRemoteWindow(index);
                await DelayAsync(_definition.WindowSwitchDelayMs, token);
                EnsureRdpForeground(handle);
            }
            var timer = Stopwatch.StartNew();
            var path = _screenshots.CaptureClientArea(handle, directory);
            var result = await OneCBaseRecognizer.RecognizeWindowAsync(path, color, _definition.ColorTolerance, token);
            token.ThrowIfCancellationRequested();
            EnsureRdpForeground(handle);
            ScenarioExecution.Log($"Open1C: окно {index + 1}/{_definition.MaxWindowsToCheck}: {result.Description}; " +
                $"нужный клиент {(result.IsTargetClient ? "найден" : "не найден")}; " +
                $"распознавание {timer.Elapsed.TotalSeconds:F3} с; screenshot: {path}");
            if (result.IsTargetClient) return true;
            // Similar screenshots are not proof of a completed cycle: two different
            // windows may look identical. Only the configured bound ends this search.
        }
        ScenarioExecution.Log($"Open1C: проверено {_definition.MaxWindowsToCheck} окон; нужный клиент не найден, запускаем новый");
        return false;
    }

    private static void EnsureRdpForeground(IntPtr handle)
    {
        ScenarioExecution.CheckCancellation();
        if (NativeMethods.GetForegroundWindow() != handle)
            throw new InvalidOperationException("Open1C: фокус вышел из RDP. В mstsc включите применение сочетаний клавиш Windows на удалённом компьютере. Перебор прерван, новый экземпляр не запускается.");
    }

    private Color ParsePanelColor()
    {
        var value = _definition.PanelColor;
        if (value is null || value.Length != 7 || value[0] != '#' ||
            !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new InvalidOperationException("Open1C: panelColor должен иметь формат #RRGGBB.");
        return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    private string BuildCommand()
    {
        var command = new StringBuilder();
        command.Append(Quote(_definition.Executable));
        command.Append(" ENTERPRISE /S ");
        command.Append(Quote($"{_definition.Server}\\{_definition.Database}"));

        if (!string.IsNullOrWhiteSpace(_definition.User))
        {
            command.Append(" /N ");
            command.Append(Quote(_definition.User));
        }

        if (!string.IsNullOrEmpty(_definition.Password))
        {
            command.Append(" /P ");
            command.Append(Quote(_definition.Password));
        }

        return command.ToString();
    }

    private void ValidateDefinition()
    {
        if (_definition.ReadyTimeoutSeconds <= 0 || _definition.PollIntervalMs <= 0 ||
            _definition.WindowSwitchDelayMs <= 0 || _definition.MaxWindowsToCheck <= 0 ||
            _definition.ColorTolerance is < 0 or > 32)
            throw new InvalidOperationException("Open1C: таймауты и лимит окон должны быть положительными; colorTolerance — 0..32.");
        _ = ParsePanelColor();
        if (string.IsNullOrWhiteSpace(_definition.Executable))
        {
            throw new InvalidOperationException("В шаге Open1C не задан executable.");
        }

        if (string.IsNullOrWhiteSpace(_definition.Server))
        {
            throw new InvalidOperationException("В шаге Open1C не задан server.");
        }

        if (string.IsNullOrWhiteSpace(_definition.Database))
        {
            throw new InvalidOperationException("В шаге Open1C не задан database.");
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds > 0
            ? Task.Delay(milliseconds, cancellationToken)
            : Task.CompletedTask;
    }
}
