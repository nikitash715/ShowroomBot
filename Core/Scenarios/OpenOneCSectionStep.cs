using System.Drawing.Imaging;
using ShowroomBot.Configuration;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class OpenOneCSectionStep : IScenarioStep
{
    private readonly ScenarioStepDefinition _definition;
    private readonly RdpController _rdp;
    private readonly MouseInputSender _mouse;
    private readonly WindowScreenshotService _screenshots;
    private readonly OneCSectionRecognizer _recognizer;
    private readonly MouseSettings _mouseSettings;

    public OpenOneCSectionStep(ScenarioStepDefinition definition, RdpController rdp,
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

    public string Name => "Открыть раздел 1С";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_definition.Section))
            throw new InvalidOperationException("В шаге Open1CSection не задан section.");
        Activate();
        await Task.Delay(500, cancellationToken);
        var directory = Path.Combine(AppContext.BaseDirectory, "diagnostics",
            $"open1c-section-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        string Capture() => _screenshots.CaptureClientArea(Activate(), directory);

        // Open1C only waits a fixed delay; allow the application to finish drawing its panel.
        var (path, result) = await WaitForPanelAsync(Capture, directory, cancellationToken);
        if (await TryClickAsync(path, result, cancellationToken)) return;

        foreach (var direction in new[] { -1, 1 }) // Windows wheel: negative = down.
        {
            path = await ScrollToEndAsync(Capture, path, result.Panel, direction, cancellationToken);
            result = await _recognizer.RecognizeAsync(path, _definition.Section, cancellationToken);
            if (await TryClickAsync(path, result, cancellationToken)) return;
        }
        throw new InvalidOperationException(
            $"Раздел «{_definition.Section}» не найден в левой панели 1С после прокрутки вниз и вверх. Диагностика: {directory}");
    }

    private async Task<(string Path, SectionRecognition Result)> WaitForPanelAsync(
        Func<string> capture, string directory, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = capture();
            try
            {
                return (path, await _recognizer.RecognizeAsync(path, _definition.Section, cancellationToken));
            }
            catch (SectionPanelNotFoundException exception)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new InvalidOperationException($"{exception.Message} Диагностика: {directory}", exception);
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private IntPtr Activate()
    {
        if (!_rdp.TryActivateExistingWindow(out var window))
            throw new InvalidOperationException("Не найдено открытое окно mstsc или его не удалось активировать.");
        return window;
    }

    private async Task MoveAsync(Point clientPoint, CancellationToken cancellationToken)
    {
        var screenPoint = _screenshots.ClientToScreen(Activate(), clientPoint);
        var stepDelay = _mouseSettings.StepDelayMilliseconds > 0 ? _mouseSettings.StepDelayMilliseconds : 10;
        var duration = _mouseSettings.MovementDurationMilliseconds > 0
            ? _mouseSettings.MovementDurationMilliseconds : 300;
        await _mouse.MoveToAsync(screenPoint,
            TimeSpan.FromMilliseconds(Math.Max(duration, stepDelay * 2L)),
            TimeSpan.FromMilliseconds(stepDelay), cancellationToken);
    }

    private async Task<bool> TryClickAsync(string path, SectionRecognition result, CancellationToken cancellationToken)
    {
        if (result.TextBounds is not Rectangle text) return false;
        var point = new Point(text.Left + text.Width / 2, text.Top + text.Height / 2);
        if (!result.Panel.Contains(text) || !result.Panel.Contains(point))
            throw new InvalidOperationException("Распознанный раздел выходит за границы панели; клик отменён.");
        using (var bitmap = new Bitmap(path))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var panelPen = new Pen(Color.DodgerBlue, 2))
        using (var textPen = new Pen(Color.LimeGreen, 3))
        using (var clickPen = new Pen(Color.Red, 2))
        {
            graphics.DrawRectangle(panelPen, result.Panel);
            graphics.DrawRectangle(textPen, text);
            graphics.DrawEllipse(clickPen, point.X - 6, point.Y - 6, 12, 12);
            graphics.DrawLine(clickPen, point.X - 10, point.Y, point.X + 10, point.Y);
            graphics.DrawLine(clickPen, point.X, point.Y - 10, point.X, point.Y + 10);
            bitmap.Save(Path.ChangeExtension(path, ".found.png"), ImageFormat.Png);
        }
        await MoveAsync(point, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _mouse.ClickLeft();
        return true;
    }

    private async Task<string> ScrollToEndAsync(Func<string> capture, string path, Rectangle panel,
        int direction, CancellationToken cancellationToken)
    {
        await MoveAsync(new Point(panel.Left + panel.Width / 2, panel.Top + panel.Height / 2), cancellationToken);
        // Require three stable frames, allowing the pointer hover to settle first.
        await Task.Delay(400, cancellationToken);
        path = capture();
        var stableFrames = 0;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Activate();
            _mouse.Scroll(direction * 5);
            await Task.Delay(250, cancellationToken);
            var next = capture();
            stableFrames = PanelChanged(path, next, panel) ? 0 : stableFrames + 1;
            path = next;
            if (stableFrames >= 3) return path;
        }
        throw new InvalidOperationException($"Не удалось достичь конца прокрутки панели 1С. Диагностика: {Path.GetDirectoryName(path)}");
    }

    private static bool PanelChanged(string beforePath, string afterPath, Rectangle panel)
    {
        using var before = new Bitmap(beforePath);
        using var after = new Bitmap(afterPath);
        if (before.Size != after.Size)
            throw new InvalidOperationException("Размер RDP изменился во время прокрутки; повторите шаг.");
        var changed = 0;
        var samples = 0;
        for (var y = panel.Top + 3; y < panel.Bottom - 3; y += 2)
        for (var x = panel.Left + 3; x < panel.Right - 3; x += 2)
        {
            var a = before.GetPixel(x, y);
            var b = after.GetPixel(x, y);
            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 60) changed++;
            samples++;
        }
        return changed > Math.Max(2, samples * .0005);
    }
}
