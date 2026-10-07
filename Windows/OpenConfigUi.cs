using System.Diagnostics;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;

namespace ShowroomBot.Windows;

public sealed class OpenConfigUi(RdpController rdp, KeyboardInputSender keyboard,
    WindowScreenshotService screenshots, OneCSectionRecognizer ocr, ScenarioStepDefinition options) : IOpenConfigUi
{
    private IntPtr _handle;
    private string _directory = string.Empty;

    public async Task PrepareAsync(CancellationToken token)
    {
        _handle = await rdp.OpenOrActivateAsync(token);
        await RdpController.EnsureFullScreenAsync(_handle, keyboard, token);
        _directory = ScenarioExecution.Current?.DirectoryPath ?? Path.Combine(AppContext.BaseDirectory,
            "diagnostics", $"open-config-{Guid.NewGuid():N}");
        await Task.Delay(options.AfterActivationDelayMs, token);
    }

    private async Task<ConfiguratorView> CaptureAsync(CancellationToken token)
    {
        RdpController.EnsureSessionForeground(_handle);
        var path = screenshots.CaptureClientArea(_handle, _directory);
        var labels = await ocr.ReadConfiguratorLinesAsync(path, token);
        RdpController.EnsureSessionForeground(_handle);
        using var image = new Bitmap(path);
        var view = OneCConfiguratorRecognizer.Analyze(image, labels);
        ScenarioExecution.Log($"OpenConfig: конфигуратор {(view.IsConfigurator ? "распознан" : "не распознан")}; screenshot: {path}");
        return view;
    }

    public async Task<bool> IsConfiguratorAsync(CancellationToken token) => (await CaptureAsync(token)).IsConfigurator;

    public async Task SelectWindowAsync(int index, CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        await keyboard.SelectRemoteWindowAltTabAsync(index, token);
        await Task.Delay(options.WindowSwitchDelayMs, token);
    }

    public async Task LaunchAsync(ScenarioStepDefinition definition, CancellationToken token)
    {
        using var guard = RdpInputGuard.RequireStrict(_handle);
        keyboard.SendWindowsRun();
        await Task.Delay(options.AfterRunDialogDelayMs, token);
        await WaitAsync(v => OneCConfiguratorRecognizer.HasRunDialog(v.Labels), "диалог «Выполнить»", token);
        await keyboard.SendControlShortcutAsync(0x1E, token);
        if (!OneCConfiguratorRecognizer.HasRunDialog((await CaptureAsync(token)).Labels))
            throw new InvalidOperationException("OpenConfig: диалог «Выполнить» закрыт до ввода команды.");
        // The command contains credentials; never write it to diagnostics.
        await keyboard.SendTextAsync(OpenOneCBaseStep.BuildCommand(definition, "CONFIG"), token);
        keyboard.SendEnter();
    }

    public Task WaitReadyAsync(CancellationToken token) => WaitAsync(v => v.IsConfigurator, "запуск конфигуратора", token);

    private async Task WaitAsync(Func<ConfiguratorView, bool> condition, string what, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(options.ReadyTimeoutSeconds);
        while (timer.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            if (condition(await CaptureAsync(token))) return;
            var remaining = timeout - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(options.PollIntervalMs, remaining.TotalMilliseconds)), token);
        }
        throw new TimeoutException($"OpenConfig: не дождались: {what}.");
    }
}
