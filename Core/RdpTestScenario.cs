using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core;

public sealed class RdpTestScenario
{
    private readonly RdpController _rdpController;
    private readonly KeyboardInputSender _keyboardInputSender;
    private readonly WindowScreenshotService _screenshotService;

    public RdpTestScenario(
        RdpController rdpController,
        KeyboardInputSender keyboardInputSender,
        WindowScreenshotService screenshotService)
    {
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
        _screenshotService = screenshotService;
    }

    public async Task<string> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_rdpController.TryActivateExistingWindow(out var windowHandle))
        {
            throw new InvalidOperationException("Не найдено открытое окно mstsc или его не удалось активировать.");
        }

        using var inputGuard = RdpInputGuard.Require(windowHandle);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        _keyboardInputSender.SendWindowsRun();
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        await _keyboardInputSender.SendTextAsync("powershell", cancellationToken);
        _keyboardInputSender.SendEnter();
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await _keyboardInputSender.SendTextAsync("Write-Host \"SHOWROOMBOT TEST OK\"", cancellationToken);
        _keyboardInputSender.SendEnter();
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        var diagnosticsDirectory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        return _screenshotService.CaptureClientArea(windowHandle, diagnosticsDirectory);
    }
}
